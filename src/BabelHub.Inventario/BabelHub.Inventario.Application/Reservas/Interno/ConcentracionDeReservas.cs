using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Reservas.Interno;

/// <summary>
/// Cuenta las unidades que apartan las reservas por sucursal nuevas, por pareja (edición, sucursal) y en una ventana <b>fija</b> de 15 minutos desde la primera unidad contada (SEL-47, D18). En memoria, uno por
/// proceso (un solo servicio, una instancia): el conteo se pierde al reiniciar. No resta lo liberado ni lo vencido y no avisa de un acaparamiento que no supere el máximo en ninguna ventana. Seguro entre hilos.
/// </summary>
internal sealed class ContadorDeConcentracion
{
    /// <summary>El máximo de unidades por ventana si la configuración no dice otro (decisión de la persona, 2026-11-27).</summary>
    public const int MaximoPorDefecto = 100;

    public static readonly TimeSpan Ventana = TimeSpan.FromMinutes(15);

    private readonly object _candado = new();
    private readonly Dictionary<(Guid EdicionId, string Sucursal), (DateTimeOffset Inicio, long Unidades, bool Avisada)> _ventanas = [];
    private readonly IReloj _reloj;
    private readonly int _maximo;

    public ContadorDeConcentracion(IReloj reloj, int maximo = MaximoPorDefecto)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximo);
        _reloj = reloj;
        _maximo = maximo;
    }

    /// <summary>Suma las unidades y devuelve el total de la ventana <b>solo</b> la primera vez que supera el máximo; en cualquier otro caso, <see langword="null"/>.</summary>
    public long? Contar(Guid edicionId, string codigoDeSucursal, int unidades)
    {
        var pareja = (edicionId, codigoDeSucursal);
        lock (_candado)
        {
            var ahora = _reloj.Ahora;

            // Las ventanas vencidas se descartan: el contador no crece sin fin y la pareja vuelve a empezar desde cero.
            foreach (var vencida in _ventanas.Where(v => ahora - v.Value.Inicio >= Ventana).Select(v => v.Key).ToList())
            {
                _ventanas.Remove(vencida);
            }

            var (inicio, cuenta, avisada) = _ventanas.TryGetValue(pareja, out var actual) ? actual : (ahora, 0L, false);
            cuenta += unidades;
            var debeAvisar = !avisada && cuenta > _maximo;
            _ventanas[pareja] = (inicio, cuenta, avisada || debeAvisar);
            return debeAvisar ? cuenta : null;
        }
    }
}

/// <summary>
/// Decorador de reservar, <b>exterior</b> a la transacción y al reintento por interbloqueo (SEL-47): cuenta lo que apartó una reserva por sucursal <b>nueva</b> y confirmada en la base (ni un rechazo, ni un reintento
/// deshecho, ni una repetición con la misma clave) y, la primera vez que una pareja supera el máximo en la ventana, publica la alerta técnica y la anota. Ninguna falla del conteo, de la publicación o del registro
/// cambia la respuesta, y el aviso no usa la cancelación de la solicitud: la reserva ya está hecha.
/// </summary>
internal sealed class ConcentracionDeReservas(
    ICasoDeUso<ReservarEntrada, ReservaDelPedido> interior,
    ContadorDeConcentracion contador,
    IAlertas alertas,
    IRegistroDeConcentracion registro) : ICasoDeUso<ReservarEntrada, ReservaDelPedido>
{
    /// <summary>El motivo de la alerta técnica; su texto y su lugar en el catálogo cerrado son de <c>Shared</c> y de la auditoría.</summary>
    public const string MotivoDeLaAlerta = "concentracion-de-reservas";

    public async Task<Resultado<ReservaDelPedido>> Ejecutar(ReservarEntrada entrada, CancellationToken cancelacion = default)
    {
        var resultado = await interior.Ejecutar(entrada, cancelacion).ConfigureAwait(false);

        // Un éxito garantiza que las líneas son válidas e impresas y que, si alguna declara sucursal, la declaran todas (SEL-42).
        if (resultado.EsExito && !resultado.Valor.EsRepeticion && FormaDeLaSolicitud.AlgunaDeclaraSucursal(entrada.Lineas))
        {
            await Vigilar(entrada.Lineas).ConfigureAwait(false);
        }

        return resultado;
    }

    private async Task Vigilar(IReadOnlyList<LineaSolicitada> lineas)
    {
        foreach (var pareja in lineas.GroupBy(l => (l.EdicionId, Sucursal: l.CodigoDeSucursal!)))
        {
            long? total;
            try
            {
                total = contador.Contar(pareja.Key.EdicionId, pareja.Key.Sucursal, pareja.Sum(l => l.Cantidad));
            }
            catch (Exception)
            {
                // La reserva ya está hecha: un fallo del conteo no la convierte en un error (SEL-47).
                continue;
            }

            if (total is not { } unidades)
            {
                continue;
            }

            try
            {
                registro.Registrar(pareja.Key.EdicionId, pareja.Key.Sucursal, (int)Math.Min(int.MaxValue, unidades));
            }
            catch (Exception)
            {
                // Sin registro, la alerta se publica igual.
            }

            await AlertasDeVolumenDeReservas.Publicar(alertas, MotivoDeLaAlerta, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
