using System.Globalization;
using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Application.Plataforma.Interno;

/// <summary>Opciones de las entradas de integración: lista blanca de proveedores, tolerancia de la marca de tiempo y tope del cuerpo.</summary>
public sealed record OpcionesDeEntradasDeIntegracion
{
    public OpcionesDeEntradasDeIntegracion(
        IEnumerable<string> proveedoresPermitidos,
        TimeSpan? tolerancia = null,
        int topeDelCuerpoEnBytes = 262_144)
    {
        ArgumentNullException.ThrowIfNull(proveedoresPermitidos);
        ProveedoresPermitidos = proveedoresPermitidos.ToHashSet(StringComparer.Ordinal);
        Tolerancia = tolerancia ?? TimeSpan.FromMinutes(5);
        TopeDelCuerpoEnBytes = topeDelCuerpoEnBytes > 0
            ? topeDelCuerpoEnBytes
            : throw new ArgumentOutOfRangeException(nameof(topeDelCuerpoEnBytes), "El tope del cuerpo es positivo.");
    }

    public IReadOnlySet<string> ProveedoresPermitidos { get; }

    public TimeSpan Tolerancia { get; }

    public int TopeDelCuerpoEnBytes { get; }
}

/// <summary>
/// Implementa <see cref="IProcesadorDeEntradasDeIntegracion"/>: solo acepta proveedores de la lista blanca, con una marca
/// de tiempo reciente, un cuerpo dentro del tope y una firma válida sobre «{marca}.{cuerpo}»; rechaza todo lo demás con el
/// mismo resultado y deja el motivo en un evento de seguridad (AUD-37). Luego reclama el identificador de la entrada: una
/// repetición no se vuelve a procesar y recibe el mismo resultado.
/// </summary>
public sealed partial class ProcesarEntradaDeIntegracion(
    OpcionesDeEntradasDeIntegracion opciones,
    IVerificadorDeFirma firmas,
    IAlmacenDeEntradasProcesadas almacen,
    IOrigenDeSolicitud origenes,
    IPublicadorDeEventosDeSeguridad eventos,
    IReloj reloj) : IProcesadorDeEntradasDeIntegracion
{
    public const string MotivoProveedorDesconocido = "proveedor-desconocido";
    public const string MotivoIdInvalido = "id-invalido";
    public const string MotivoCuerpoDemasiadoGrande = "cuerpo-demasiado-grande";
    public const string MotivoMarcaInvalida = "marca-invalida";
    public const string MotivoMarcaFueraDeTolerancia = "marca-fuera-de-tolerancia";
    public const string MotivoSinFirma = "sin-firma";
    public const string MotivoFirmaInvalida = "firma-invalida";

    public async Task<ResultadoDeEntradaDeIntegracion> Recibir(EntradaDeIntegracion entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        var motivo = Verificar(entrada);
        if (motivo is not null)
        {
            await Rechazar(entrada, motivo, cancelacion).ConfigureAwait(false);
            return new ResultadoDeEntradaDeIntegracion(EstadoDeEntradaDeIntegracion.Rechazada);
        }

        var reclamo = await almacen.Reclamar(entrada.Proveedor, entrada.IdDeEntrada, cancelacion).ConfigureAwait(false);
        return reclamo.Estado switch
        {
            EstadoDeEntrada.Nueva => new ResultadoDeEntradaDeIntegracion(EstadoDeEntradaDeIntegracion.Aceptada),
            EstadoDeEntrada.EnCurso => new ResultadoDeEntradaDeIntegracion(EstadoDeEntradaDeIntegracion.EnCurso),
            _ => new ResultadoDeEntradaDeIntegracion(EstadoDeEntradaDeIntegracion.Repetida, reclamo.Resultado),
        };
    }

    public Task Completar(string proveedor, string idDeEntrada, ResultadoMinimo resultado, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        return almacen.Completar(proveedor, idDeEntrada, resultado, cancelacion);
    }

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormaDelId();

    private string? Verificar(EntradaDeIntegracion entrada)
    {
        if (!opciones.ProveedoresPermitidos.Contains(entrada.Proveedor))
        {
            return MotivoProveedorDesconocido;
        }

        if (!FormaDelId().IsMatch(entrada.IdDeEntrada))
        {
            return MotivoIdInvalido;
        }

        if (entrada.Cuerpo.Length > opciones.TopeDelCuerpoEnBytes)
        {
            return MotivoCuerpoDemasiadoGrande;
        }

        if (string.IsNullOrEmpty(entrada.Firma))
        {
            return MotivoSinFirma;
        }

        if (!long.TryParse(entrada.MarcaDeTiempo, NumberStyles.None, CultureInfo.InvariantCulture, out var segundos)
            || segundos > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return MotivoMarcaInvalida;
        }

        var marca = DateTimeOffset.FromUnixTimeSeconds(segundos);
        if ((reloj.Ahora - marca).Duration() > opciones.Tolerancia)
        {
            return MotivoMarcaFueraDeTolerancia;
        }

        return firmas.Verificar(entrada.Proveedor, entrada.MarcaDeTiempo, entrada.Cuerpo, entrada.Firma) ? null : MotivoFirmaInvalida;
    }

    private Task Rechazar(EntradaDeIntegracion entrada, string motivo, CancellationToken cancelacion)
    {
        var origen = origenes.Actual;

        // El nombre del proveedor lo envía el cliente: solo se registra si está en la lista blanca.
        var proveedor = opciones.ProveedoresPermitidos.Contains(entrada.Proveedor) ? entrada.Proveedor : null;

        return eventos.Publicar(
            new EventoDeSeguridad(
                TipoDeEventoDeSeguridad.EntradaDeIntegracionRechazada,
                reloj.Ahora,
                origen: origen.Huella,
                direccionCompleta: origen.Direccion,
                proveedor: proveedor,
                motivo: motivo),
            cancelacion);
    }
}
