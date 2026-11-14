namespace BabelHub.Ecommerce.Api.Web.Estados;

/// <summary>Los estados estándar de una pantalla de datos (WEB-06, docs/06/03 §1): se pintan siempre con el mismo componente.</summary>
public enum EstadoDePantalla
{
    Cargando,
    Vacio,
    Error,
    SinPermiso,
    ServicioNoDisponible,
    Exito,
}

/// <summary>Una salida ofrecida a quien no tiene permiso: un texto y una ruta local a algo que sí puede usar (WEB-03).</summary>
public sealed record EnlaceDeSalida(string Texto, string Ruta);

/// <summary>
/// Lo que pinta el componente <c>bh-estado</c>. Los textos son de los catálogos de <c>docs/06-experiencia-de-usuario/08-contenido-y-mensajes.md</c>
/// y nunca llevan detalles técnicos: el código estable y el identificador de seguimiento solo van en el detalle plegable «Soporte».
/// </summary>
public sealed record EstadoVista
{
    public EstadoDePantalla Estado { get; init; }

    public string Titulo { get; init; } = string.Empty;

    public string? Mensaje { get; init; }

    /// <summary>Ruta a la que apunta el botón «Reintentar» (un <c>GET</c> a la misma pantalla); sin ella el estado no ofrece reintento.</summary>
    public string? RutaDeReintento { get; init; }

    /// <summary>Si la pantalla se pidió con htmx, el elemento que el reintento reemplaza (selector); sin él el reintento es una navegación.</summary>
    public string? DestinoHtmx { get; init; }

    public string? TextoDeAccion { get; init; }

    public string? RutaDeAccion { get; init; }

    public IReadOnlyList<EnlaceDeSalida> Salidas { get; init; } = [];

    /// <summary>La espera sugerida de un límite excedido (WEB-24).</summary>
    public int? EsperaSugeridaSegundos { get; init; }

    /// <summary>La hora de Lima desde la que se puede volver a intentar, ya formateada (por ejemplo «15:42»).</summary>
    public string? DisponibleDesde { get; init; }

    public string? CodigoDeSoporte { get; init; }

    public string? IdentificadorDeSeguimiento { get; init; }

    public static EstadoVista Cargando() => new() { Estado = EstadoDePantalla.Cargando, Titulo = TextosDeInterfaz.Cargando };

    public static EstadoVista Vacio(string titulo, string mensaje, string? textoDeAccion = null, string? rutaDeAccion = null) =>
        new() { Estado = EstadoDePantalla.Vacio, Titulo = titulo, Mensaje = mensaje, TextoDeAccion = textoDeAccion, RutaDeAccion = rutaDeAccion };

    public static EstadoVista ErrorGenerico(string? rutaDeReintento) => new()
    {
        Estado = EstadoDePantalla.Error,
        Titulo = TextosDeInterfaz.ErrorTitulo,
        Mensaje = TextosDeInterfaz.ErrorMensaje,
        RutaDeReintento = rutaDeReintento,
    };

    public static EstadoVista SinPermiso(IReadOnlyList<EnlaceDeSalida> salidas) => new()
    {
        Estado = EstadoDePantalla.SinPermiso,
        Titulo = TextosDeInterfaz.SinPermisoTitulo,
        Mensaje = TextosDeInterfaz.SinPermisoMensaje,
        Salidas = salidas,
    };

    /// <summary>El servicio o el módulo no responde: <paramref name="queParteFalla"/> nombra lo afectado («No podemos validar el stock ahora.»).</summary>
    public static EstadoVista ServicioNoDisponible(string queParteFalla, string? rutaDeReintento) => new()
    {
        Estado = EstadoDePantalla.ServicioNoDisponible,
        Titulo = TextosDeInterfaz.NoDisponibleTitulo,
        Mensaje = queParteFalla,
        RutaDeReintento = rutaDeReintento,
    };

    public static EstadoVista Exito(string titulo, string? mensaje = null) =>
        new() { Estado = EstadoDePantalla.Exito, Titulo = titulo, Mensaje = mensaje };
}

/// <summary>Los textos fijos de los estados estándar (docs/06/08 §4); en un solo lugar para poder revisarlos juntos.</summary>
public static class TextosDeInterfaz
{
    public const string Cargando = "Cargando…";
    public const string ErrorTitulo = "Algo salió mal";
    public const string ErrorMensaje = "No pudimos completar la acción. Intenta de nuevo.";
    public const string NoDisponibleTitulo = "No podemos mostrar esto ahora";
    public const string NoDisponibleMensajeGeneral = "Una parte del sistema no responde en este momento. Intenta de nuevo en unos minutos.";
    public const string SinPermisoTitulo = "No tienes acceso";
    public const string SinPermisoMensaje = "Tu cuenta no tiene acceso a esta sección.";
    public const string Reintentar = "Reintentar";
    public const string Soporte = "Soporte";
}
