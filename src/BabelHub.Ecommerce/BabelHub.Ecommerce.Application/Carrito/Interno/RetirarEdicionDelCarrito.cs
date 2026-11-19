using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Domain.Carrito;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

/// <summary>
/// <c>RetirarEdicionDelCarrito</c> (CAT-18, CAR-29, D10, D21; plan §1): lo que <c>Catálogo</c> invoca <b>dentro de su transacción</b> al dar de baja una obra o una edición, con <b>todas</b> las ediciones de la baja en una sola
/// llamada. <b>Solo toca la base de datos</b>, nunca llama a <c>Reabrir</c> ni a Pagos ni espera a otro módulo: bloquea, en un único orden ascendente por <c>id</c>, primero los checkouts vivos y después los carritos de la unión de
/// cuentas afectadas (y los de los visitantes). Con el checkout <c>en_edicion</c>, terminado o sin checkout quita la línea y deja el aviso «fue retirada del catálogo» hasta que la persona lo vea; con <c>reservando</c>, <c>en_pago</c> o
/// <c>pago_pendiente</c> solo anota <c>retiro_pendiente</c> en la línea (que ya cuenta como retirada para la persona): el retiro se aplica, dejando su aviso, en la primera salida de ese estado (T11, T14 a T17, T20, T21).
/// Audita en la misma transacción y, si no se puede auditar, falla (la baja no se aplica). Idempotente.
/// </summary>
/// <remarks>
/// Es la implementación real de <c>IRetiroDeEdicionDeCarritos</c> de Catálogo (T26b): la composición de Carrito la registra reemplazando al retiro nulo de Catálogo, que queda solo como respaldo mientras Catálogo se compone sin Carrito.
/// </remarks>
internal sealed class RetirarEdicionDelCarrito(ServiciosDelCarrito servicios) : IRetiroDeEdicionDeCarritos
{
    private const string TituloGenerico = "Edición retirada";

    public async Task Retirar(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(edicionIds);
        if (edicionIds.Count == 0)
        {
            return;
        }

        var ahora = servicios.Reloj.Ahora;
        var buscadas = edicionIds.Distinct().ToList();

        // 1. Qué carritos tienen alguna de las ediciones (sin bloqueo): de ahí salen las cuentas y los carritos que se bloquean, en orden.
        var afectados = await servicios.Carritos.BuscarConEdiciones(buscadas, cancelacion).ConfigureAwait(false);
        var cuentas = afectados.Where(a => a.CuentaId is not null).Select(a => a.CuentaId!.Value).Distinct().ToList();

        // 2. Los checkouts vivos de esas cuentas, con bloqueo y por id ascendente; después los carritos, por id ascendente (D10).
        var checkouts = cuentas.Count == 0
            ? []
            : await servicios.Checkouts.ObtenerVivosDeCuentasConBloqueo(cuentas, cancelacion).ConfigureAwait(false);
        var estadoPorCuenta = checkouts.ToDictionary(c => c.CuentaId, c => c.Estado);

        var titulos = await ObtenerTitulos(buscadas, cancelacion).ConfigureAwait(false);

        var quitadas = 0;
        var anotadas = 0;
        var tocados = 0;
        foreach (var afectado in afectados.OrderBy(a => a.CarritoId))
        {
            var carrito = await servicios.Carritos.Obtener(afectado.CarritoId, conBloqueo: true, cancelacion).ConfigureAwait(false);
            if (carrito is null)
            {
                continue;
            }

            var diferir = afectado.CuentaId is { } cuenta
                && estadoPorCuenta.TryGetValue(cuenta, out var estado)
                && Checkout.SostieneRetirosPendientes(estado);

            var avisos = new List<AvisoDelCarrito>();
            var cambio = false;
            foreach (var edicion in buscadas)
            {
                switch (carrito.RetirarEdicion(edicion, diferir, ahora))
                {
                    case ResultadoDeRetiro.Quitada:
                        quitadas++;
                        cambio = true;
                        avisos.Add(Aviso(edicion, titulos, ahora));
                        break;
                    case ResultadoDeRetiro.Anotada:
                        anotadas++;
                        cambio = true;
                        break;
                }
            }

            if (!cambio)
            {
                continue;
            }

            tocados++;
            await servicios.Carritos.Guardar(carrito, cancelacion).ConfigureAwait(false);
            if (avisos.Count > 0 && afectado.CuentaId is { } propietaria)
            {
                await servicios.Carritos.AgregarAvisos(propietaria, avisos, cancelacion).ConfigureAwait(false);
            }
        }

        // 3. La auditoría, en la misma transacción: sin ella la baja no se aplica.
        await servicios.Auditoria.EdicionesRetiradas(buscadas.Count, tocados, quitadas, anotadas, cancelacion).ConfigureAwait(false);
    }

    private AvisoDelCarrito Aviso(Guid edicionId, IReadOnlyDictionary<Guid, string> titulos, DateTimeOffset ahora) =>
        new(servicios.Identificadores.NuevoId(), TipoDeAvisoDelCarrito.EdicionRetirada, edicionId, titulos.GetValueOrDefault(edicionId, TituloGenerico), null, null, ahora);

    /// <summary>El título para el aviso: una lectura del Catálogo (que incluye las de baja); si no responde, el retiro igual se aplica con un título genérico (el retiro nunca falla por esto).</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> ObtenerTitulos(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion)
    {
        try
        {
            var consulta = await servicios.Catalogo.ObtenerEdiciones(edicionIds, cancelacion).ConfigureAwait(false);
            return consulta.EsExito ? consulta.Valor.ToDictionary(e => e.EdicionId, e => e.Titulo) : new Dictionary<Guid, string>();
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return new Dictionary<Guid, string>();
        }
    }
}
