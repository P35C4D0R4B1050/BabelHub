using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Auditoria;

/// <summary>El servicio de origen de un registro o de una alerta (AUD-01).</summary>
public enum ServicioAuditado
{
    Ecommerce,
    Inventario,
}

/// <summary>El tipo de un registro (AUD-01): de auditoría, de seguridad o ambos a la vez (AUD-20).</summary>
public enum TipoDeRegistroAuditado
{
    Auditoria,
    Seguridad,
    Ambos,
}

/// <summary>
/// Un registro de auditoría o de seguridad tal como lo entregan las consultas y los almacenes (el modelo propio del contrato: el Dominio de
/// <c>Auditoria</c> no sale del módulo, regla 13). Lo usan la lista unificada (E-commerce e Inventario), el almacén aislado de Identidad y la lectura
/// de sus registros. Nunca lleva tarjetas, tokens, enlaces ni contraseñas: el detalle ya salió de <c>DetalleSeguro</c> (AUD-06).
/// </summary>
public sealed record RegistroAuditado
{
    /// <summary>El tope del detalle serializado (AUD-24).</summary>
    public const int MaximoDeCaracteresDelDetalle = 1000;

    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(1);
    private static readonly Regex FormatoDeAccion = new(@"^[A-Z][A-Za-z0-9]{2,63}\z", RegexOptions.CultureInvariant, Espera);
    private static readonly Regex FormatoDeCodigo = new(@"^[a-z][a-z0-9-]{1,63}\z", RegexOptions.CultureInvariant, Espera);
    private static readonly Regex FormatoDeTipo = new(@"^[a-z][a-z0-9-]{0,39}\z", RegexOptions.CultureInvariant, Espera);
    private static readonly Regex FormatoDeIdentificador = new(@"^[A-Za-z0-9:_.-]{1,64}\z", RegexOptions.CultureInvariant, Espera);
    private static readonly Regex FormatoDeSucursal = new(@"^[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant, Espera);
    private static readonly Regex FormatoDeOrigen = new(@"^[A-Za-z0-9:._-]{1,64}\z", RegexOptions.CultureInvariant, Espera);
    private static readonly Regex FormatoDeCorrelacion = new(@"^[A-Za-z0-9_-]{8,64}\z", RegexOptions.CultureInvariant, Espera);
    private static readonly Regex FormatoDeRuta = new(@"^(?:sin-coincidencia|/[A-Za-z0-9._{}/-]{0,198})\z", RegexOptions.CultureInvariant, Espera);

    private readonly IReadOnlyList<string> _clavesPersonales = [];

    /// <summary>
    /// Los campos de texto tienen su formato aunque el registro llegue de otro módulo (Identidad escribe el almacén aislado con este modelo): un valor que
    /// no lo cumple se rechaza en vez de guardarse (AUD-06).
    /// </summary>
    private static string? Validar(string? valor, Regex formato, string campo) =>
        valor is null || formato.IsMatch(valor) ? valor : throw new ArgumentException($"{campo} no tiene el formato permitido.", campo);

    public RegistroAuditado(
        Guid id,
        ServicioAuditado servicio,
        TipoDeRegistroAuditado tipo,
        TipoDeActor actorTipo,
        Guid? actorId,
        string? actorRol,
        DateTimeOffset instanteUtc,
        string accion,
        ResultadoAuditado resultado)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("El registro necesita su identificador.", nameof(id));
        }

        if (string.IsNullOrEmpty(accion) || !FormatoDeAccion.IsMatch(accion))
        {
            throw new ArgumentException("La acción es un identificador del catálogo (empieza con mayúscula, solo letras y dígitos).", nameof(accion));
        }

        if (actorRol is not null && !FormatoDeCodigo.IsMatch(actorRol))
        {
            throw new ArgumentException("El rol es un código (minúsculas, dígitos y guiones).", nameof(actorRol));
        }

        if (instanteUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("El instante del registro se guarda en UTC.", nameof(instanteUtc));
        }

        Id = id;
        Servicio = servicio;
        Tipo = tipo;
        ActorTipo = actorTipo;
        ActorId = actorId;
        ActorRol = actorRol;
        InstanteUtc = instanteUtc;
        Accion = accion;
        Resultado = resultado;
    }

    /// <summary>Identificador global (GUID): es el desempate del orden de la lista unificada (D05).</summary>
    public Guid Id { get; }

    public ServicioAuditado Servicio { get; }

    public TipoDeRegistroAuditado Tipo { get; }

    public TipoDeActor ActorTipo { get; }

    /// <summary>El identificador interno de la persona; nulo en «sistema», «integración» y «anónimo» y tras la anonimización.</summary>
    public Guid? ActorId { get; }

    public string? ActorRol { get; }

    /// <summary>Siempre en UTC.</summary>
    public DateTimeOffset InstanteUtc { get; }

    /// <summary>El mismo instante en hora de Lima (UTC−5, sin horario de verano), solo para mostrarlo (AUD-25, AC-AUD-22).</summary>
    public DateTimeOffset InstanteEnLima => InstanteUtc.ToOffset(TimeSpan.FromHours(-5));

    public string Accion { get; }

    public ResultadoAuditado Resultado { get; }

    public string? ObjetoTipo { get; init => field = Validar(value, FormatoDeTipo, nameof(ObjetoTipo)); }

    public string? ObjetoId { get; init => field = Validar(value, FormatoDeIdentificador, nameof(ObjetoId)); }

    public string? SucursalOrigen { get; init => field = Validar(value, FormatoDeSucursal, nameof(SucursalOrigen)); }

    public string? SucursalDestino { get; init => field = Validar(value, FormatoDeSucursal, nameof(SucursalDestino)); }

    /// <summary>Un código del catálogo de la acción; nunca el texto de la solicitud.</summary>
    public string? Motivo { get; init => field = Validar(value, FormatoDeCodigo, nameof(Motivo)); }

    public string? Origen { get; init => field = Validar(value, FormatoDeOrigen, nameof(Origen)); }

    /// <summary>La plantilla de la ruta (o «sin-coincidencia»); nunca la ruta cruda.</summary>
    public string? Ruta { get; init => field = Validar(value, FormatoDeRuta, nameof(Ruta)); }

    /// <summary>El detalle serializado (un objeto JSON de texto a texto), de como máximo <see cref="MaximoDeCaracteresDelDetalle"/> caracteres. Se codifica al mostrarlo.</summary>
    public string? Detalle
    {
        get;
        init
        {
            if (value is { Length: > MaximoDeCaracteresDelDetalle })
            {
                throw new ArgumentException("El detalle supera el tope de caracteres (AUD-24).", nameof(value));
            }

            field = value;
        }
    }

    /// <summary>Las claves del detalle que identifican a una persona (la depuración las anonimiza, AUD-19).</summary>
    public IReadOnlyList<string> ClavesPersonales
    {
        get => _clavesPersonales;
        init => _clavesPersonales = Array.AsReadOnly(value.ToArray());
    }

    public bool Truncado { get; init; }

    public string? Correlacion { get; init => field = Validar(value, FormatoDeCorrelacion, nameof(Correlacion)); }

    public DateTimeOffset? AnonimizadoEn { get; init; }
}

/// <summary>Una página de la lista de registros (AUD-09): de 20 en 20, con el cursor firmado de la siguiente y el aviso de que faltan los del Inventario (AUD-18).</summary>
public sealed record PaginaDeRegistros
{
    public const int TamanoDePagina = 20;

    public const int MaximoDeCaracteresDelCursor = 4096;

    public PaginaDeRegistros(IReadOnlyList<RegistroAuditado> registros, string? siguienteCursor, bool faltanDelInventario = false)
    {
        ArgumentNullException.ThrowIfNull(registros);
        if (registros.Count > TamanoDePagina)
        {
            throw new ArgumentException($"Una página tiene como máximo {TamanoDePagina} registros.", nameof(registros));
        }

        if (siguienteCursor is { Length: > MaximoDeCaracteresDelCursor })
        {
            throw new ArgumentException("El cursor es demasiado largo.", nameof(siguienteCursor));
        }

        Registros = Array.AsReadOnly(registros.ToArray());
        SiguienteCursor = siguienteCursor;
        FaltanDelInventario = faltanDelInventario;
    }

    public IReadOnlyList<RegistroAuditado> Registros { get; }

    /// <summary>El cursor opaco y firmado de la página siguiente; nulo en la última.</summary>
    public string? SiguienteCursor { get; }

    /// <summary>Verdadero si el Inventario no respondió y la página solo trae lo del E-commerce (AUD-18).</summary>
    public bool FaltanDelInventario { get; }
}
