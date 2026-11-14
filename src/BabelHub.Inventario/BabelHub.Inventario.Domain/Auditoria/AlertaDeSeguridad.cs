using System.Text.RegularExpressions;

namespace BabelHub.Inventario.Domain.Auditoria;

/// <summary>Una alerta de seguridad (con pantalla para el administrador central) o técnica (solo en el registro técnico y por correo, AUD-29).</summary>
public enum ClaseDeAlerta
{
    Seguridad,
    Tecnica,
}

public enum EstadoDeAlerta
{
    Pendiente,
    Revisada,
}

/// <summary>Un motivo de alerta: un código de un catálogo cerrado (nunca el texto de una excepción) y su clase (D06).</summary>
public sealed record MotivoDeAlerta
{
    private static readonly Regex Formato = new("^[a-z][a-z0-9-]{1,63}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public MotivoDeAlerta(string codigo, ClaseDeAlerta clase)
    {
        if (string.IsNullOrEmpty(codigo) || !Formato.IsMatch(codigo))
        {
            throw new ArgumentException("El motivo es un código del catálogo (minúsculas, dígitos y guiones).", nameof(codigo));
        }

        Codigo = codigo;
        Clase = clase;
    }

    public string Codigo { get; }

    public ClaseDeAlerta Clase { get; }
}

/// <summary>El catálogo cerrado de motivos de alerta. Un motivo que no está aquí no genera alerta (un texto libre filtraría datos, D06).</summary>
public sealed class CatalogoDeMotivosDeAlerta
{
    private readonly Dictionary<string, MotivoDeAlerta> _porCodigo;

    public CatalogoDeMotivosDeAlerta(IReadOnlyCollection<MotivoDeAlerta> motivos)
    {
        ArgumentNullException.ThrowIfNull(motivos);
        _porCodigo = new Dictionary<string, MotivoDeAlerta>(StringComparer.Ordinal);
        foreach (var motivo in motivos)
        {
            if (!_porCodigo.TryAdd(motivo.Codigo, motivo))
            {
                throw new ArgumentException($"El motivo {motivo.Codigo} está repetido.", nameof(motivos));
            }
        }
    }

    public bool Contiene(string codigo) => _porCodigo.ContainsKey(codigo);

    public MotivoDeAlerta Obtener(string codigo) =>
        _porCodigo.TryGetValue(codigo, out var motivo) ? motivo : throw new ArgumentException("El motivo no está en el catálogo de alertas.", nameof(codigo));
}

/// <summary>
/// Una alerta agrupada (AUD-13, AUD-34, AUD-39): un servicio, un motivo, las ocurrencias de una ventana fija de 15 minutos desde la primera, y lo que el
/// administrador central ya revisó. Es inmutable: marcarla como revisada o sumarle una ocurrencia produce otro valor, de modo que la aplicación solo
/// guarda la marca si pudo registrarla en la auditoría (si no, la alerta sigue pendiente).
/// </summary>
public sealed class AlertaDeSeguridad
{
    /// <summary>El contador está acotado: una tormenta de ocurrencias no desborda ni llena el almacén.</summary>
    public const int TopeDeOcurrencias = 100_000;

    internal AlertaDeSeguridad(
        string id,
        ServicioDeOrigen servicio,
        string motivo,
        ClaseDeAlerta clase,
        DateTimeOffset primeraOcurrencia,
        DateTimeOffset ultimaOcurrencia,
        int ocurrencias,
        int revisadasHasta,
        Guid? revisadaPor,
        DateTimeOffset? revisadaEn,
        bool enviadaPorCorreo)
    {
        Id = id;
        Servicio = servicio;
        Motivo = motivo;
        Clase = clase;
        PrimeraOcurrencia = primeraOcurrencia;
        UltimaOcurrencia = ultimaOcurrencia;
        Ocurrencias = ocurrencias;
        RevisadasHasta = revisadasHasta;
        RevisadaPor = revisadaPor;
        RevisadaEn = revisadaEn;
        EnviadaPorCorreo = enviadaPorCorreo;
    }

    /// <summary>El identificador con el prefijo del servicio: <c>eco:</c> o <c>inv:</c>.</summary>
    public string Id { get; }

    public ServicioDeOrigen Servicio { get; }

    public string Motivo { get; }

    public ClaseDeAlerta Clase { get; }

    public DateTimeOffset PrimeraOcurrencia { get; }

    public DateTimeOffset UltimaOcurrencia { get; }

    public int Ocurrencias { get; }

    /// <summary>Hasta qué ocurrencia se revisó; nunca mayor que <see cref="Ocurrencias"/>.</summary>
    public int RevisadasHasta { get; }

    public Guid? RevisadaPor { get; }

    public DateTimeOffset? RevisadaEn { get; }

    public bool EnviadaPorCorreo { get; }

    public int OcurrenciasSinRevisar => Ocurrencias - RevisadasHasta;

    /// <summary>Pendiente mientras haya ocurrencias sin revisar; revisada cuando se revisó hasta la última.</summary>
    public EstadoDeAlerta Estado => OcurrenciasSinRevisar == 0 ? EstadoDeAlerta.Revisada : EstadoDeAlerta.Pendiente;

    /// <summary>Solo las de seguridad se muestran al administrador central; las técnicas no tienen pantalla.</summary>
    public bool TienePantalla => Clase == ClaseDeAlerta.Seguridad;

    public static string NuevoId(ServicioDeOrigen servicio, Guid identificador)
    {
        if (identificador == Guid.Empty)
        {
            throw new ArgumentException("La alerta necesita su identificador.", nameof(identificador));
        }

        return (servicio == ServicioDeOrigen.Ecommerce ? "eco:" : "inv:") + identificador.ToString("D");
    }

    /// <summary>
    /// Marca las ocurrencias hasta el contador que el administrador veía (AUD-34). Nunca más allá del contador actual ni hacia atrás: una marca menor que
    /// una anterior no retrocede lo revisado.
    /// </summary>
    public AlertaDeSeguridad MarcarRevisada(int contadorVisto, Guid por, DateTimeOffset cuando)
    {
        if (contadorVisto < 1 || contadorVisto > Ocurrencias)
        {
            throw new ArgumentOutOfRangeException(nameof(contadorVisto), "El contador visto debe estar entre 1 y el número de ocurrencias de la alerta.");
        }

        if (por == Guid.Empty)
        {
            throw new ArgumentException("Quien revisa necesita su identificador.", nameof(por));
        }

        return new AlertaDeSeguridad(Id, Servicio, Motivo, Clase, PrimeraOcurrencia, UltimaOcurrencia, Ocurrencias, Math.Max(RevisadasHasta, contadorVisto), por, cuando, EnviadaPorCorreo);
    }

    public AlertaDeSeguridad MarcarComoEnviada() =>
        EnviadaPorCorreo ? this : new AlertaDeSeguridad(Id, Servicio, Motivo, Clase, PrimeraOcurrencia, UltimaOcurrencia, Ocurrencias, RevisadasHasta, RevisadaPor, RevisadaEn, enviadaPorCorreo: true);

    internal AlertaDeSeguridad ConOcurrencia(DateTimeOffset instante) =>
        new(
            Id,
            Servicio,
            Motivo,
            Clase,
            PrimeraOcurrencia,
            instante > UltimaOcurrencia ? instante : UltimaOcurrencia,
            Math.Min(Ocurrencias + 1, TopeDeOcurrencias),
            RevisadasHasta,
            RevisadaPor,
            RevisadaEn,
            EnviadaPorCorreo);
}

/// <summary>Lo que resulta de registrar una ocurrencia: la alerta, si se abrió una nueva y si corresponde enviarla por correo.</summary>
public sealed record ResultadoDeAgrupacion(AlertaDeSeguridad Alerta, bool Abrio, bool EnviarCorreo);

/// <summary>
/// La ventana de agrupación de alertas (AUD-13, AUD-39): <b>fija</b>, de 15 minutos desde la primera ocurrencia. Dentro de ella las ocurrencias del
/// mismo servicio y motivo suman a la misma alerta (y la devuelven a pendiente si ya estaba revisada, sin otro correo); después se abre una alerta
/// nueva con su correo (sujeto al tope diario de AUD-42, que aplica la Aplicación).
/// </summary>
public static class VentanaDeAgrupacion
{
    public static readonly TimeSpan Duracion = TimeSpan.FromMinutes(15);

    /// <summary>Si el instante cae en la ventana que abre <paramref name="primera"/>: desde ella, incluida, hasta antes de los 15 minutos.</summary>
    public static bool Contiene(DateTimeOffset primera, DateTimeOffset instante) => instante >= primera && instante < primera + Duracion;

    public static ResultadoDeAgrupacion Registrar(
        AlertaDeSeguridad? abierta,
        CatalogoDeMotivosDeAlerta catalogo,
        ServicioDeOrigen servicio,
        string motivo,
        DateTimeOffset instante,
        Guid identificadorSiSeAbre)
    {
        ArgumentNullException.ThrowIfNull(catalogo);
        if (instante.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("La fecha de la ocurrencia se guarda en UTC.", nameof(instante));
        }

        var definicion = catalogo.Obtener(motivo);
        if (abierta is not null && abierta.Servicio == servicio && abierta.Motivo == motivo)
        {
            if (instante < abierta.PrimeraOcurrencia)
            {
                throw new ArgumentException("La ocurrencia es anterior a la primera de la alerta (reloj atrasado).", nameof(instante));
            }

            if (Contiene(abierta.PrimeraOcurrencia, instante))
            {
                return new ResultadoDeAgrupacion(abierta.ConOcurrencia(instante), Abrio: false, EnviarCorreo: false);
            }
        }

        var nueva = new AlertaDeSeguridad(
            AlertaDeSeguridad.NuevoId(servicio, identificadorSiSeAbre),
            servicio,
            motivo,
            definicion.Clase,
            instante,
            instante,
            ocurrencias: 1,
            revisadasHasta: 0,
            revisadaPor: null,
            revisadaEn: null,
            enviadaPorCorreo: false);
        return new ResultadoDeAgrupacion(nueva, Abrio: true, EnviarCorreo: true);
    }
}
