using BabelHub.Ecommerce.Application.Devoluciones.Interno;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Infrastructure.Devoluciones;

/// <summary>Las sucursales activas que el Inventario informó, guardadas unos minutos (plan §1: «con la lista en caché unos minutos»). Un <b>singleton</b> compartido por las solicitudes; es seguro entre hilos y nunca guarda una respuesta fallida.</summary>
internal sealed class CacheDeSucursalesActivas(IReloj reloj)
{
    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(5);

    private readonly object _candado = new();
    private IReadOnlyCollection<string>? _codigos;
    private DateTimeOffset _expira;

    public IReadOnlyCollection<string>? Vigentes()
    {
        lock (_candado)
        {
            return _codigos is not null && reloj.Ahora < _expira ? _codigos : null;
        }
    }

    public void Guardar(IReadOnlyCollection<string> codigos)
    {
        lock (_candado)
        {
            _codigos = codigos;
            _expira = reloj.Ahora + Vigencia;
        }
    }
}

/// <summary>
/// El Inventario visto por Devoluciones sobre <see cref="IClienteDelInventario"/> de <c>Plataforma</c> (T20; DEV-01, DEV-07, DEV-17; AC-DEV-07, AC-DEV-16): <b>este adaptador no abre un <c>HttpClient</c>, no toca el token ni la credencial</b> (el cliente de Plataforma pone el plazo de 10 segundos, la credencial del
/// alcance de usuario y el token de quien confirma la recepción) y nunca guarda ni repite el texto remoto (SEG-10). Un libro digital nunca llega aquí (regla 12).
/// <para>
/// <b>El reingreso</b> es idempotente por devolución (sin clave): <c>yaAplicado</c> es un hecho que se acepta (<see cref="ResultadoDelReingreso.Reingresado"/>). Se traduce a la lista cerrada que el orquestador necesita (D07): <b>rechazo de negocio</b> solo con <c>INV-NO-ENCONTRADO</c>, <c>INV-EDICION-NO-IMPRESA</c> o
/// <c>INV-VALIDACION-INVALIDA</c> (garantizan que no se reingresó nada); <c>INV-IDEMPOTENCIA-EN-CONFLICTO</c> es un reingreso previo con otra carga; y <b>todo lo demás</b> —sin respuesta, plazo agotado, 5xx, 401, 408, 429, <c>INV-PROHIBIDO</c>, <c>INV-IDEMPOTENCIA-EN-CURSO</c>, una respuesta que no es la de la devolución o una
/// excepción— es <b>incierto</b>, porque el reingreso pudo aplicarse. Una carga que el contrato no admite no se envía.
/// </para>
/// <para>
/// <b>Las sucursales activas</b> salen de <c>inventario.sucursales.listar</c> con la credencial de servicio (sin token), con caché de cinco minutos; una respuesta fallida o con un código vacío o demasiado largo es «no disponible» y no se guarda (la solicitud responde <c>ECO-SERVICIO-NO-DISPONIBLE</c> sin escribir nada).
/// </para>
/// </summary>
internal sealed class RestInventarioDeDevoluciones(IClienteDelInventario cliente, CacheDeSucursalesActivas cache) : IInventarioDeDevoluciones
{
    private const int MaximoDeLineas = 20;

    private const int LongitudMaximaDelCodigo = 40;

    private static readonly string[] RechazosDeNegocio = ["INV-NO-ENCONTRADO", "INV-EDICION-NO-IMPRESA", "INV-VALIDACION-INVALIDA"];

    private const string ConflictoDeIdempotencia = "INV-IDEMPOTENCIA-EN-CONFLICTO";

    public async Task<ResultadoDelReingreso> Reingresar(ReingresoPorDevolucion reingreso, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(reingreso);
        if (!EsAdmitida(reingreso))
        {
            // Una carga que el contrato no admite no se envía: no cambió nada y no hay nada que reintentar.
            return new ResultadoDelReingreso.RechazoDeNegocio();
        }

        ResultadoDelInventario<ReingresoDeDevolucion> resultado;
        try
        {
            resultado = await cliente.ReingresarDevolucion(
                reingreso.DevolucionId,
                reingreso.PedidoId,
                reingreso.CodigoDeFuente,
                [.. reingreso.Lineas.Select(l => new LineaAReingresar(l.EdicionId, l.Cantidad))],
                cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ResultadoDelReingreso.Incierto();
        }

        if (resultado.Exitosa)
        {
            return resultado.Valor.DevolucionId == reingreso.DevolucionId ? new ResultadoDelReingreso.Reingresado() : new ResultadoDelReingreso.Incierto();
        }

        if (resultado.Estado == EstadoDeLlamadaAlInventario.Rechazada && resultado.Codigo is { } codigo)
        {
            if (RechazosDeNegocio.Contains(codigo, StringComparer.Ordinal))
            {
                return new ResultadoDelReingreso.RechazoDeNegocio();
            }

            if (string.Equals(codigo, ConflictoDeIdempotencia, StringComparison.Ordinal))
            {
                return new ResultadoDelReingreso.ConflictoDeIdempotencia();
            }
        }

        return new ResultadoDelReingreso.Incierto();
    }

    public async Task<ResultadoDeSucursalesActivas> ListarSucursalesActivas(CancellationToken cancelacion = default)
    {
        if (cache.Vigentes() is { } guardadas)
        {
            return new ResultadoDeSucursalesActivas.Lista(guardadas);
        }

        ResultadoDelInventario<IReadOnlyList<SucursalPublica>> resultado;
        try
        {
            resultado = await cliente.ListarSucursales(cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ResultadoDeSucursalesActivas.NoDisponible();
        }

        if (!resultado.Exitosa)
        {
            return new ResultadoDeSucursalesActivas.NoDisponible();
        }

        // SEG-13: solo se acepta una lista bien formada; una sola entrada inválida invalida toda la respuesta.
        var codigos = new List<string>();
        foreach (var sucursal in resultado.Valor)
        {
            if (sucursal is null || string.IsNullOrWhiteSpace(sucursal.Codigo) || sucursal.Codigo.Length > LongitudMaximaDelCodigo)
            {
                return new ResultadoDeSucursalesActivas.NoDisponible();
            }

            codigos.Add(sucursal.Codigo);
        }

        IReadOnlyCollection<string> distintos = [.. codigos.Distinct(StringComparer.Ordinal)];
        cache.Guardar(distintos);
        return new ResultadoDeSucursalesActivas.Lista(distintos);
    }

    private static bool EsAdmitida(ReingresoPorDevolucion r) =>
        r.DevolucionId != Guid.Empty
        && r.PedidoId != Guid.Empty
        && !string.IsNullOrWhiteSpace(r.CodigoDeFuente)
        && r.Lineas is { Count: >= 1 and <= MaximoDeLineas }
        && r.Lineas.All(l => l.EdicionId != Guid.Empty && l.Cantidad >= 1)
        && r.Lineas.Select(l => l.EdicionId).Distinct().Count() == r.Lineas.Count;
}
