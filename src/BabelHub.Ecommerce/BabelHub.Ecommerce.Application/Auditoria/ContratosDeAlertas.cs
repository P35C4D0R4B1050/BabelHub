using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Auditoria;

/// <summary>
/// El motivo de una alerta: un código de un catálogo cerrado (D06), nunca el texto de una excepción ni un dato de la solicitud. El catálogo concreto lo
/// cierra el módulo <c>Auditoria</c>; aquí solo se garantiza la forma de un código (minúsculas, dígitos y guiones).
/// </summary>
public readonly partial record struct CodigoDeAlerta
{
    public CodigoDeAlerta(string valor)
    {
        if (string.IsNullOrEmpty(valor) || !Forma().IsMatch(valor))
        {
            throw new ArgumentException("El motivo de una alerta es un código del catálogo (minúsculas, dígitos y guiones).", nameof(valor));
        }

        Valor = valor;
    }

    public string Valor { get; }

    public override string ToString() => Valor ?? string.Empty;

    [GeneratedRegex(@"^[a-z][a-z0-9-]{1,63}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Forma();
}

/// <summary>
/// Publica una alerta con un motivo del catálogo cerrado: la guarda en el registro técnico del servicio, la agrupa en ventanas fijas de 15 minutos y,
/// en el E-commerce, la envía por correo a los administradores centrales activos (AUD-13, AUD-29). <b>Nunca interrumpe</b> a quien la publica
/// (AUD-40): solo lanza <see cref="ArgumentException"/> por un motivo que el catálogo no tiene (un defecto de quien llama).
/// </summary>
public interface IAlertas
{
    /// <summary>Una alerta técnica (un trabajo de fondo que falla, una dependencia caída): sin pantalla, solo registro técnico y correo.</summary>
    Task PublicarTecnica(CodigoDeAlerta motivo, CancellationToken cancelacion = default);

    /// <summary>Una alerta de seguridad (un umbral superado): con pantalla para el administrador central.</summary>
    Task PublicarDeSeguridad(CodigoDeAlerta motivo, CancellationToken cancelacion = default);
}

public enum ClaseDeAlertaConsultada
{
    Seguridad,
    Tecnica,
}

public enum EstadoDeAlertaConsultada
{
    Pendiente,
    Revisada,
}

/// <summary>Una alerta agrupada tal como la entregan las consultas (AUD-13): su servicio, su motivo, sus ocurrencias y lo revisado hasta ahora.</summary>
public sealed partial record AlertaConsultada
{
    public AlertaConsultada(
        string id,
        ServicioAuditado servicio,
        CodigoDeAlerta motivo,
        ClaseDeAlertaConsultada clase,
        DateTimeOffset primeraOcurrencia,
        DateTimeOffset ultimaOcurrencia,
        int ocurrencias,
        int revisadasHasta)
    {
        var prefijo = servicio == ServicioAuditado.Ecommerce ? "eco:" : "inv:";
        if (string.IsNullOrEmpty(id) || !id.StartsWith(prefijo, StringComparison.Ordinal) || !FormaDelId().IsMatch(id))
        {
            throw new ArgumentException("El identificador de la alerta lleva el prefijo de su servicio (eco: o inv:).", nameof(id));
        }

        if (primeraOcurrencia.Offset != TimeSpan.Zero || ultimaOcurrencia.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Las ocurrencias se guardan en UTC.", nameof(primeraOcurrencia));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(ocurrencias, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(revisadasHasta);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(revisadasHasta, ocurrencias);

        Id = id;
        Servicio = servicio;
        Motivo = motivo;
        Clase = clase;
        PrimeraOcurrencia = primeraOcurrencia;
        UltimaOcurrencia = ultimaOcurrencia;
        Ocurrencias = ocurrencias;
        RevisadasHasta = revisadasHasta;
    }

    /// <summary>El identificador con el prefijo de su servicio (<c>eco:</c> o <c>inv:</c>): el E-commerce sabe a quién reenviar la marca.</summary>
    public string Id { get; }

    public ServicioAuditado Servicio { get; }

    public CodigoDeAlerta Motivo { get; }

    public ClaseDeAlertaConsultada Clase { get; }

    public DateTimeOffset PrimeraOcurrencia { get; }

    public DateTimeOffset UltimaOcurrencia { get; }

    /// <summary>Las ocurrencias agrupadas en la ventana de 15 minutos desde la primera.</summary>
    public int Ocurrencias { get; }

    /// <summary>El contador que el administrador veía al marcarla como revisada; nunca mayor que <see cref="Ocurrencias"/>.</summary>
    public int RevisadasHasta { get; }

    public Guid? RevisadaPor { get; init; }

    public DateTimeOffset? RevisadaEn { get; init; }

    /// <summary>Pendiente mientras haya ocurrencias sin revisar; revisada cuando se revisó hasta la última (AUD-34, AUD-39).</summary>
    public EstadoDeAlertaConsultada Estado => RevisadasHasta >= Ocurrencias ? EstadoDeAlertaConsultada.Revisada : EstadoDeAlertaConsultada.Pendiente;

    [GeneratedRegex(@"^(eco|inv):[A-Za-z0-9-]{1,64}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FormaDelId();
}

/// <summary>Una página de alertas de seguridad (AUD-13), de la más reciente a la más antigua, con el aviso de que faltan las del Inventario (AUD-18).</summary>
public sealed record PaginaDeAlertas
{
    public PaginaDeAlertas(IReadOnlyList<AlertaConsultada> alertas, string? siguienteCursor, bool faltanDelInventario = false)
    {
        ArgumentNullException.ThrowIfNull(alertas);
        if (alertas.Count > PaginaDeRegistros.TamanoDePagina)
        {
            throw new ArgumentException($"Una página tiene como máximo {PaginaDeRegistros.TamanoDePagina} alertas.", nameof(alertas));
        }

        if (siguienteCursor is { Length: > PaginaDeRegistros.MaximoDeCaracteresDelCursor })
        {
            throw new ArgumentException("El cursor es demasiado largo.", nameof(siguienteCursor));
        }

        Alertas = Array.AsReadOnly(alertas.ToArray());
        SiguienteCursor = siguienteCursor;
        FaltanDelInventario = faltanDelInventario;
    }

    public IReadOnlyList<AlertaConsultada> Alertas { get; }

    public string? SiguienteCursor { get; }

    /// <summary>Verdadero si el Inventario no respondió y la página solo trae las del E-commerce (AUD-18).</summary>
    public bool FaltanDelInventario { get; }
}

/// <summary>
/// Consulta y marca de las alertas (solo administrador central, AUD-27). Los fallos esperados son <see cref="ErrorDeAplicacion"/> (prohibido, validación,
/// servicio no disponible), nunca una excepción.
/// </summary>
public interface IConsultaDeAlertas
{
    /// <summary>La lista de alertas de seguridad de la más reciente a la más antigua (AUD-13); las técnicas no tienen pantalla.</summary>
    Task<Resultado<PaginaDeAlertas>> Listar(string? cursor, CancellationToken cancelacion = default);

    /// <summary>
    /// Las alertas, de seguridad y técnicas, con ocurrencia posterior a <paramref name="desde"/>: lo que el E-commerce lee del Inventario con la
    /// credencial de servicio de alcance <c>lectura-alertas</c> para enviar su correo (AUD-38).
    /// </summary>
    Task<Resultado<IReadOnlyList<AlertaConsultada>>> LeerNuevas(DateTimeOffset desde, CancellationToken cancelacion = default);

    /// <summary>
    /// Marca como revisadas las ocurrencias hasta <paramref name="contadorVisto"/> y registra la marca en la auditoría; si no se puede registrar, la
    /// alerta sigue pendiente (AUD-34). Devuelve la alerta ya actualizada.
    /// </summary>
    Task<Resultado<AlertaConsultada>> MarcarRevisada(string alertaId, int contadorVisto, CancellationToken cancelacion = default);
}
