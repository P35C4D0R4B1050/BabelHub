using System.Globalization;
using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Infrastructure.Envios;

/// <summary>
/// El cierre de las reservas de un envío sobre <see cref="IClienteDelInventario.CerrarReserva"/> de <c>Plataforma</c> (T13; ENV-11, ENV-12, ENV-16): <b>una sola llamada con todas las líneas del envío</b> (todo o nada: el Inventario
/// descuenta todas o ninguna), con motivo <c>reparto</c>, el <c>envioId</c> y <c>Idempotency-Key</c> = el identificador del envío (repetirla devuelve el resultado original). El cliente de Plataforma pone el plazo de 10 segundos,
/// la credencial del alcance de usuario y el token: <b>este adaptador no lo toca ni lo ve</b>, no abre un <c>HttpClient</c> y nunca guarda ni repite el texto remoto (SEG-10).
/// <para>
/// Traduce a la lista cerrada que el caso de uso necesita (D05): <b>cerrado</b> si el Inventario responde con exactamente las líneas pedidas del mismo pedido, cada una <c>cerrada</c> o <c>ya-cerrada</c> (un hecho que se acepta);
/// <b>rechazo de negocio</b> solo con uno de los tres códigos que garantizan que no cambió nada (<c>INV-RESERVA-ESTADO-INVALIDO</c>, <c>INV-VALIDACION-INVALIDA</c>, <c>INV-NO-ENCONTRADO</c>); y <b>incierto</b> para todo lo demás
/// (sin respuesta, plazo agotado, 5xx, 401, 408, 429, <c>INV-IDEMPOTENCIA-EN-CURSO</c>, conflicto de idempotencia, sin permiso, una respuesta con líneas de más, de menos o de otro pedido, o una excepción), porque el primer cierre
/// puede haberse aplicado (SEG-13: nada fuera de lo pedido se acepta).
/// </para>
/// </summary>
internal sealed class RestReservasDelEnvio(IClienteDelInventario cliente) : IReservasDelEnvio
{
    public const string MotivoReparto = "reparto";

    /// <summary>El tope del contrato de reservas (50 líneas por llamada): una entrada mayor no se envía y no deja el marcador de despacho sin salida.</summary>
    public const int MaximoDeLineas = 50;

    private static readonly string[] RechazosDeNegocio = ["INV-RESERVA-ESTADO-INVALIDO", "INV-VALIDACION-INVALIDA", "INV-NO-ENCONTRADO"];

    private static readonly string[] ResultadosAceptados = ["cerrada", "ya-cerrada"];

    public async Task<ResultadoDeCierreDeReservas> Cerrar(Guid pedidoId, Guid envioId, IReadOnlyCollection<Guid> lineaIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(lineaIds);
        var pedidas = lineaIds.Distinct().ToList();
        if (pedidoId == Guid.Empty || envioId == Guid.Empty || pedidas.Count == 0 || pedidas.Count > MaximoDeLineas || pedidas.Count != lineaIds.Count || pedidas.Contains(Guid.Empty))
        {
            // Una llamada que el contrato no admite ni se envía: no cambió nada y no hay nada que reintentar.
            return new ResultadoDeCierreDeReservas.RechazoDeNegocio();
        }

        ResultadoDelInventario<CierreDeLaReserva> resultado;
        try
        {
            resultado = await cliente.CerrarReserva(pedidoId, MotivoReparto, pedidas, envioId, envioId.ToString("D", CultureInfo.InvariantCulture), cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ResultadoDeCierreDeReservas.Incierto();
        }

        if (resultado.Exitosa)
        {
            return ValidarLaRespuesta(resultado.Valor, pedidoId, pedidas) ? new ResultadoDeCierreDeReservas.Cerrado() : new ResultadoDeCierreDeReservas.Incierto();
        }

        return resultado.Estado == EstadoDeLlamadaAlInventario.Rechazada && resultado.Codigo is { } codigo && RechazosDeNegocio.Contains(codigo, StringComparer.Ordinal)
            ? new ResultadoDeCierreDeReservas.RechazoDeNegocio()
            : new ResultadoDeCierreDeReservas.Incierto();
    }

    /// <summary>SEG-13: el mismo pedido, exactamente las líneas pedidas (ni una de más, de menos ni repetida) y cada resultado de la lista cerrada.</summary>
    private static bool ValidarLaRespuesta(CierreDeLaReserva respuesta, Guid pedidoId, List<Guid> pedidas)
    {
        if (respuesta is null || respuesta.PedidoId != pedidoId || respuesta.Lineas is null || respuesta.Lineas.Count != pedidas.Count)
        {
            return false;
        }

        var devueltas = respuesta.Lineas.Select(l => l?.LineaId).ToList();
        return devueltas.Distinct().Count() == devueltas.Count
            && pedidas.All(p => devueltas.Contains(p))
            && respuesta.Lineas.All(l => l is not null && l.Resultado is { } r && ResultadosAceptados.Contains(r, StringComparer.Ordinal));
    }
}
