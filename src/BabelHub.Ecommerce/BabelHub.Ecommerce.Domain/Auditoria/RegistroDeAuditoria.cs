using System.Globalization;
using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Domain.Auditoria;

/// <summary>Quién hizo algo (AUD-23): una persona usuaria con su rol, el sistema, una integración o alguien sin sesión.</summary>
public enum TipoDeActor
{
    Usuario,
    Sistema,
    Integracion,
    Anonimo,
}

public static class TipoDeActorExtensiones
{
    /// <summary>El nombre estable del tipo de actor tal como se guarda y se muestra.</summary>
    public static string Nombre(this TipoDeActor tipo) => tipo switch
    {
        TipoDeActor.Usuario => "usuario",
        TipoDeActor.Sistema => "sistema",
        TipoDeActor.Integracion => "integracion",
        TipoDeActor.Anonimo => "anonimo",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
    };
}

/// <summary>
/// El actor de un registro (AUD-23). Se toma de la credencial o del contexto <b>verificados</b>, nunca de la solicitud. «Sistema», «integración» y
/// «anónimo» no tienen identificador ni rol y <b>solo los construye el servidor</b> a partir del alcance de la credencial (el contrato público de la
/// Aplicación, que es quien conoce el contexto, es el único que los pide: un dato de la solicitud no puede elegirlos). La persona usuaria lleva su
/// identificador interno y su rol; la anonimización (AUD-19) los pone en nulo y conserva el tipo.
/// </summary>
public sealed record Actor
{
    private static readonly Regex FormatoDeRol = new(@"^[a-z][a-z0-9-]{1,39}\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private Actor(TipoDeActor tipo, Guid? id, string? rol)
    {
        Tipo = tipo;
        Id = id;
        Rol = rol;
    }

    public TipoDeActor Tipo { get; }

    /// <summary>El identificador interno de la persona usuaria; nulo en los demás tipos y tras la anonimización.</summary>
    public Guid? Id { get; }

    public string? Rol { get; }

    public static Actor Usuario(Guid id, string rol)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("El usuario necesita su identificador.", nameof(id));
        }

        if (string.IsNullOrEmpty(rol) || !FormatoDeRol.IsMatch(rol))
        {
            throw new ArgumentException("El rol es un código (minúsculas, dígitos y guiones).", nameof(rol));
        }

        return new Actor(TipoDeActor.Usuario, id, rol);
    }

    public static Actor Sistema() => new(TipoDeActor.Sistema, null, null);

    public static Actor Integracion() => new(TipoDeActor.Integracion, null, null);

    public static Actor Anonimo() => new(TipoDeActor.Anonimo, null, null);

    /// <summary>El mismo actor sin el identificador (conserva el rol y el tipo).</summary>
    internal Actor SinId() => new(Tipo, null, Rol);

    /// <summary>El mismo actor sin el rol (conserva el identificador y el tipo).</summary>
    internal Actor SinRol() => new(Tipo, Id, null);

    /// <summary>El mismo actor sin identificador ni rol (los datos personales, AUD-19); conserva el tipo.</summary>
    internal Actor SinTodo() => new(Tipo, null, null);
}

public enum ServicioDeOrigen
{
    Ecommerce,
    Inventario,
}

public enum ResultadoDeOperacion
{
    Exitoso,
    Rechazado,
    Fallido,
}

/// <summary>La presentación en hora de Lima (AUD-25): UTC−5, sin horario de verano. Lo que se guarda siempre es UTC.</summary>
public static class HoraDeLima
{
    /// <summary>El desfase de Lima respecto de UTC. El Perú no cambia de hora en verano.</summary>
    public static readonly TimeSpan Desfase = TimeSpan.FromHours(-5);

    public static DateTimeOffset Convertir(DateTimeOffset utc)
    {
        if (utc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("El instante debe estar en UTC.", nameof(utc));
        }

        return utc.ToOffset(Desfase);
    }

    public static string Formatear(DateTimeOffset utc) => Convertir(utc).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}

/// <summary>
/// Un registro de auditoría o de seguridad (AUD-01): el servicio de origen, el tipo, el actor, la fecha en UTC, la acción, el objeto afectado, las
/// sucursales (la de origen y la de destino en un traslado), el motivo y el origen (en los eventos de seguridad), la ruta como plantilla, el detalle
/// seguro y el resultado. Solo se crea coherente con su acción del catálogo. Es un valor inmutable: lo único que cambia con el tiempo es su
/// anonimización (AUD-19), que produce otro valor con los datos personales retirados (<c>PoliticaDeAnonimizacion</c>).
/// </summary>
public sealed class RegistroDeAuditoria : IEquatable<RegistroDeAuditoria>
{
    private const int TopeDeTexto = 64;
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(1);
    private static readonly Regex FormatoDeTipo = new(@"^[a-z][a-z0-9-]{0,39}\z", RegexOptions.CultureInvariant, Espera);
    private static readonly Regex FormatoDeIdentificador = new(@"^[A-Za-z0-9:_.-]{1,64}\z", RegexOptions.CultureInvariant, Espera);
    private static readonly Regex FormatoDeSucursal = new(@"^[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant, Espera);
    private static readonly Regex FormatoDeOrigen = new(@"^[A-Za-z0-9:._-]{1,64}\z", RegexOptions.CultureInvariant, Espera);
    private static readonly Regex FormatoDeCorrelacion = new(@"^[A-Za-z0-9_-]{8,64}\z", RegexOptions.CultureInvariant, Espera);
    private readonly AccionDelCatalogo _accion;

    private RegistroDeAuditoria(
        Guid id,
        ServicioDeOrigen servicio,
        AccionDelCatalogo accion,
        Actor actor,
        DateTimeOffset instanteUtc,
        string? objetoTipo,
        string? objetoId,
        string? sucursalOrigen,
        string? sucursalDestino,
        string? motivo,
        string? origen,
        string? ruta,
        DetalleSeguro detalle,
        ResultadoDeOperacion resultado,
        string? correlacion,
        DateTimeOffset? anonimizadoEn)
    {
        Id = id;
        Servicio = servicio;
        _accion = accion;
        Actor = actor;
        InstanteUtc = instanteUtc;
        ObjetoTipo = objetoTipo;
        ObjetoId = objetoId;
        SucursalOrigen = sucursalOrigen;
        SucursalDestino = sucursalDestino;
        Motivo = motivo;
        Origen = origen;
        Ruta = ruta;
        Detalle = detalle;
        Resultado = resultado;
        Correlacion = correlacion;
        AnonimizadoEn = anonimizadoEn;
    }

    public Guid Id { get; }

    public ServicioDeOrigen Servicio { get; }

    public TipoDeRegistro Tipo => _accion.Tipo;

    public bool Aislada => _accion.Aislada;

    public Actor Actor { get; }

    /// <summary>Siempre en UTC (desfase cero).</summary>
    public DateTimeOffset InstanteUtc { get; }

    /// <summary>El mismo instante en hora de Lima, solo para mostrarlo (AUD-25).</summary>
    public DateTimeOffset InstanteEnLima => HoraDeLima.Convertir(InstanteUtc);

    public string Accion => _accion.Identificador;

    public string? ObjetoTipo { get; }

    public string? ObjetoId { get; }

    public string? SucursalOrigen { get; }

    public string? SucursalDestino { get; }

    /// <summary>Un código del catálogo de la acción; nunca el texto de la solicitud.</summary>
    public string? Motivo { get; }

    /// <summary>La huella o la dirección anonimizable de la solicitud (eventos de seguridad).</summary>
    public string? Origen { get; }

    /// <summary>La plantilla de la ruta, o <see cref="PlantillaDeRuta.SinCoincidencia"/>; nunca la ruta cruda.</summary>
    public string? Ruta { get; }

    public DetalleSeguro Detalle { get; }

    public ResultadoDeOperacion Resultado { get; }

    public bool Truncado => Detalle.Truncado;

    /// <summary>La correlación generada por el servidor (acceso denegado); nunca la que envía un cliente.</summary>
    public string? Correlacion { get; }

    /// <summary>Cuándo se anonimizó; nulo mientras conserva sus datos personales.</summary>
    public DateTimeOffset? AnonimizadoEn { get; }

    public AccionDelCatalogo AccionDelCatalogo => _accion;

    public static RegistroDeAuditoria Crear(
        Guid id,
        ServicioDeOrigen servicio,
        AccionDelCatalogo accion,
        Actor actor,
        DateTimeOffset instanteUtc,
        string? objetoTipo,
        string? objetoId,
        string? sucursalOrigen,
        string? sucursalDestino,
        string? motivo,
        string? origen,
        RutaResuelta? ruta,
        DetalleSeguro detalle,
        ResultadoDeOperacion resultado,
        string? correlacion)
    {
        ArgumentNullException.ThrowIfNull(accion);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(detalle);
        if (id == Guid.Empty)
        {
            throw new ArgumentException("El registro necesita su identificador.", nameof(id));
        }

        if (instanteUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("La fecha del registro se guarda en UTC.", nameof(instanteUtc));
        }

        if (!accion.AdmiteMotivo(motivo))
        {
            throw new ArgumentException("El motivo debe ser uno del catálogo de la acción (y es obligatorio en los eventos de seguridad).", nameof(motivo));
        }

        if (accion.Tipo != TipoDeRegistro.Auditoria && string.IsNullOrWhiteSpace(origen))
        {
            throw new ArgumentException("Un evento de seguridad lleva el origen de la solicitud.", nameof(origen));
        }

        if (objetoTipo is null && objetoId is not null)
        {
            throw new ArgumentException("Un objeto sin tipo no puede tener identificador.", nameof(objetoId));
        }

        if (accion.TiposDeObjeto is not null && objetoTipo is not null && !accion.TiposDeObjeto.Contains(objetoTipo, StringComparer.Ordinal))
        {
            throw new ArgumentException("El tipo de objeto no es de los que admite la acción.", nameof(objetoTipo));
        }

        if (!Enum.IsDefined(servicio) || !Enum.IsDefined(resultado))
        {
            throw new ArgumentException("El servicio o el resultado no son válidos.", nameof(servicio));
        }

        // Cada clave del detalle debe llegar con la misma marca de personal que declara la acción: si no, un valor personal quedaría sin marcar y la
        // depuración no lo anonimizaría (AUD-19).
        foreach (var clave in detalle.Valores.Keys)
        {
            var declarada = accion.Detalle.FirstOrDefault(c => c.Nombre == clave) ?? throw new ArgumentException("El detalle trae claves que la acción no declara.", nameof(detalle));
            if (detalle.EsPersonal(clave) != declarada.Personal)
            {
                throw new ArgumentException("La marca de personal del detalle no coincide con la que declara la acción.", nameof(detalle));
            }
        }

        // Los campos de texto no pasan por la redacción del detalle: cada uno tiene su formato (con \z, sin saltos de línea finales).
        (string Nombre, string? Valor, Regex Formato)[] campos =
        [
            ("objetoTipo", objetoTipo, FormatoDeTipo),
            ("objetoId", objetoId, FormatoDeIdentificador),
            ("sucursalOrigen", sucursalOrigen, FormatoDeSucursal),
            ("sucursalDestino", sucursalDestino, FormatoDeSucursal),
            ("origen", origen, FormatoDeOrigen),
            ("correlacion", correlacion, FormatoDeCorrelacion),
        ];
        foreach (var (nombre, valor, formato) in campos)
        {
            if (valor is not null && !formato.IsMatch(valor))
            {
                throw new ArgumentException($"{nombre} no tiene el formato permitido (y no puede pasar de {TopeDeTexto} caracteres).", nombre);
            }
        }

        return new RegistroDeAuditoria(id, servicio, accion, actor, instanteUtc, objetoTipo, objetoId, sucursalOrigen, sucursalDestino, motivo, origen, ruta?.Valor, detalle, resultado, correlacion, anonimizadoEn: null);
    }

    /// <summary>El mismo registro con los datos personales retirados (los decide <c>PoliticaDeAnonimizacion</c>); conserva acción, fecha, tipo y resultado.</summary>
    internal RegistroDeAuditoria ConAnonimizacion(Actor actor, string? objetoId, string? origen, DetalleSeguro detalle, DateTimeOffset cuando) =>
        new(Id, Servicio, _accion, actor, InstanteUtc, ObjetoTipo, objetoId, SucursalOrigen, SucursalDestino, Motivo, origen, Ruta, detalle, Resultado, Correlacion, cuando);

    public bool Equals(RegistroDeAuditoria? otro) => otro is not null && Id == otro.Id;

    public override bool Equals(object? obj) => Equals(obj as RegistroDeAuditoria);

    public override int GetHashCode() => Id.GetHashCode();
}
