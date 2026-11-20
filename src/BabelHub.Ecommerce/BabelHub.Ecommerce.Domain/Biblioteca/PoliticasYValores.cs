using System.Security.Cryptography;
using System.Text;

namespace BabelHub.Ecommerce.Domain.Biblioteca;

/// <summary>Las reglas numéricas de la descarga (spec §5): 5 descargas por acceso, enlace de 300 segundos como máximo y 10 enlaces por cliente y hora (el límite de esta última lo aplica <c>Plataforma</c>; aquí se nombra una sola vez).</summary>
public static class PoliticaDeDescargas
{
    public const int MaximoDeDescargas = 5;

    public const int VigenciaDelEnlaceEnSegundos = 300;

    public const int EnlacesPorClienteYHora = 10;

    public static readonly TimeSpan VigenciaDelEnlace = TimeSpan.FromSeconds(VigenciaDelEnlaceEnSegundos);

    /// <summary>Las claves de descarga se purgan una hora después de su vencimiento.</summary>
    public static readonly TimeSpan RetencionDeLaClaveVencida = TimeSpan.FromHours(1);
}

/// <summary>
/// La clave de un solo uso de la descarga como resumen SHA-256 de 32 bytes (SEG-22, D02): la base nunca guarda la clave en claro. Solo acepta la forma que genera la interfaz web (32 hexadecimales en minúscula) o, en general,
/// de 8 a 128 caracteres imprimibles sin espacios.
/// </summary>
public sealed class ClaveDeDescarga : IEquatable<ClaveDeDescarga>
{
    private ClaveDeDescarga(byte[] resumen) => Resumen = resumen;

    /// <summary>Los 32 bytes del resumen.</summary>
    public byte[] Resumen { get; }

    public static ClaveDeDescarga? Crear(string? clave)
    {
        if (clave is null || clave.Length is < 8 or > 128 || clave.Any(c => c <= ' ' || c > '~'))
        {
            return null;
        }

        return new ClaveDeDescarga(SHA256.HashData(Encoding.UTF8.GetBytes(clave)));
    }

    public static ClaveDeDescarga DeResumen(byte[] resumen)
    {
        ArgumentNullException.ThrowIfNull(resumen);
        return resumen.Length == 32 ? new ClaveDeDescarga(resumen) : throw new ArgumentException("El resumen tiene 32 bytes.", nameof(resumen));
    }

    public string Hexadecimal => Convert.ToHexStringLower(Resumen);

    public bool Equals(ClaveDeDescarga? otra) => otra is not null && Resumen.AsSpan().SequenceEqual(otra.Resumen);

    public override bool Equals(object? obj) => Equals(obj as ClaveDeDescarga);

    public override int GetHashCode() => BitConverter.ToInt32(Resumen, 0);

    public override string ToString() => "ClaveDeDescarga(***)";
}

/// <summary>Motivo de lista cerrada de una restitución de descargas (BIB-08, D06): sin texto libre, sin datos personales.</summary>
public enum MotivoDeRestitucion
{
    DescargaFallida,
    ArchivoDefectuoso,
    ErrorDelSistema,
    Otro,
}

/// <summary>Motivo de lista cerrada de una solicitud de ayuda (BIB-16, D06).</summary>
public enum MotivoDeAyuda
{
    ArchivoNoAbre,
    ArchivoNoDisponible,
    DescargasAgotadas,
    Otro,
}

public enum EstadoDeAyuda
{
    Abierta,
    Cerrada,
}

public static class CodigosDeMotivos
{
    public static string Codigo(this MotivoDeRestitucion motivo) => motivo switch
    {
        MotivoDeRestitucion.DescargaFallida => "descarga-fallida",
        MotivoDeRestitucion.ArchivoDefectuoso => "archivo-defectuoso",
        MotivoDeRestitucion.ErrorDelSistema => "error-del-sistema",
        MotivoDeRestitucion.Otro => "otro",
        _ => throw new ArgumentOutOfRangeException(nameof(motivo)),
    };

    public static MotivoDeRestitucion? RestitucionDesdeCodigo(string? codigo) => codigo switch
    {
        "descarga-fallida" => MotivoDeRestitucion.DescargaFallida,
        "archivo-defectuoso" => MotivoDeRestitucion.ArchivoDefectuoso,
        "error-del-sistema" => MotivoDeRestitucion.ErrorDelSistema,
        "otro" => MotivoDeRestitucion.Otro,
        _ => null,
    };

    public static string Codigo(this MotivoDeAyuda motivo) => motivo switch
    {
        MotivoDeAyuda.ArchivoNoAbre => "archivo-no-abre",
        MotivoDeAyuda.ArchivoNoDisponible => "archivo-no-disponible",
        MotivoDeAyuda.DescargasAgotadas => "descargas-agotadas",
        MotivoDeAyuda.Otro => "otro",
        _ => throw new ArgumentOutOfRangeException(nameof(motivo)),
    };

    public static MotivoDeAyuda? AyudaDesdeCodigo(string? codigo) => codigo switch
    {
        "archivo-no-abre" => MotivoDeAyuda.ArchivoNoAbre,
        "archivo-no-disponible" => MotivoDeAyuda.ArchivoNoDisponible,
        "descargas-agotadas" => MotivoDeAyuda.DescargasAgotadas,
        "otro" => MotivoDeAyuda.Otro,
        _ => null,
    };

    public static string Codigo(this EstadoDeAyuda estado) => estado == EstadoDeAyuda.Abierta ? "abierta" : "cerrada";

    public static EstadoDeAyuda? EstadoDeAyudaDesdeCodigo(string? codigo) => codigo switch
    {
        "abierta" => EstadoDeAyuda.Abierta,
        "cerrada" => EstadoDeAyuda.Cerrada,
        _ => null,
    };
}

/// <summary>La solicitud de ayuda de un titular sobre su acceso (BIB-16): una abierta por acceso; sin texto libre.</summary>
public sealed class SolicitudDeAyuda
{
    private SolicitudDeAyuda(Guid id, Guid accesoId, MotivoDeAyuda motivo, EstadoDeAyuda estado, DateTimeOffset creadaEn, DateTimeOffset? cerradaEn)
    {
        Id = id;
        AccesoId = accesoId;
        Motivo = motivo;
        Estado = estado;
        CreadaEn = creadaEn;
        CerradaEn = cerradaEn;
    }

    public Guid Id { get; }

    public Guid AccesoId { get; }

    public MotivoDeAyuda Motivo { get; }

    public EstadoDeAyuda Estado { get; private set; }

    public DateTimeOffset CreadaEn { get; }

    public DateTimeOffset? CerradaEn { get; private set; }

    public static SolicitudDeAyuda Abrir(Guid accesoId, MotivoDeAyuda motivo, DateTimeOffset ahora, Guid? id = null)
    {
        if (accesoId == Guid.Empty)
        {
            throw new ArgumentException("El acceso no puede ser vacío.", nameof(accesoId));
        }

        return new SolicitudDeAyuda(id ?? Guid.NewGuid(), accesoId, motivo, EstadoDeAyuda.Abierta, ahora, null);
    }

    public static SolicitudDeAyuda Reconstruir(Guid id, Guid accesoId, MotivoDeAyuda motivo, EstadoDeAyuda estado, DateTimeOffset creadaEn, DateTimeOffset? cerradaEn) =>
        new(id, accesoId, motivo, estado, creadaEn, cerradaEn);

    /// <summary>Cierra la solicitud; devuelve falso si ya estaba cerrada.</summary>
    public bool Cerrar(DateTimeOffset ahora)
    {
        if (Estado == EstadoDeAyuda.Cerrada)
        {
            return false;
        }

        Estado = EstadoDeAyuda.Cerrada;
        CerradaEn = ahora;
        return true;
    }
}

/// <summary>El registro de una descarga entregada (BIB-11): acceso, usuario, edición y fecha; tras la depuración solo quedan la edición y el día (BIB-26, D09).</summary>
public sealed record RegistroDeDescarga(Guid Id, Guid? AccesoId, Guid? UsuarioId, Guid EdicionId, DateTimeOffset? EntregadaEn, DateOnly Dia)
{
    public bool EstaDepurado => AccesoId is null && UsuarioId is null && EntregadaEn is null;

    /// <summary>El mismo registro sin referencia al acceso ni al usuario ni la hora exacta: queda la edición y el día.</summary>
    public RegistroDeDescarga Depurar() => this with { AccesoId = null, UsuarioId = null, EntregadaEn = null };
}

/// <summary>La clave de descarga registrada (D02): el resumen, el instante de firma y el de vencimiento, con los que se reconstruye el mismo enlace sin guardarlo.</summary>
public sealed record ClaveRegistrada(Guid AccesoId, ClaveDeDescarga Clave, DateTimeOffset FirmadoEn, DateTimeOffset VenceEn, Guid DescargaId)
{
    public bool EstaVigente(DateTimeOffset ahora) => VenceEn > ahora;
}

/// <summary>La retención de dos años por día de Lima (BIB-26, BIB-29, D09): «hoy ≥ día + 2 años» está vencido; 2 años y un día sí, 2 años menos un día no.</summary>
public static class PoliticaDeRetencion
{
    public const int Anios = 2;

    public static bool EstaVencido(DateOnly dia, DateOnly hoyEnLima) => hoyEnLima >= dia.AddYears(Anios);
}
