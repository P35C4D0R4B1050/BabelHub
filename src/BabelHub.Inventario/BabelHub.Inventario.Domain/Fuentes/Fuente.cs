using System.Text.RegularExpressions;

namespace BabelHub.Inventario.Domain.Fuentes;

/// <summary>Los nombres de los campos de una fuente tal como se registran en la auditoría de su edición (nunca el valor del contacto).</summary>
public static class CamposDeFuente
{
    public const string Nombre = "nombre";

    public const string Region = "region";

    public const string ContactoDeNotificacion = "contactoDeNotificacion";
}

/// <summary>
/// Una fuente de stock: cada sucursal es una fuente independiente (FUE-01). Su identificador lo genera el servidor y es el <c>sucursal_id</c> de
/// Identidad y el <c>branch_id</c> del token; el código, único, es lo que viaja en las rutas y los avisos. No hay desactivación de fuentes. La
/// fuente es inmutable: cada cambio devuelve otra instancia.
/// </summary>
public sealed partial class Fuente
{
    public const int LargoMinimoDelCodigo = 2;

    public const int LargoMaximoDelCodigo = 20;

    public const int LargoMaximoDelNombre = 100;

    public const int LargoMaximoDeLaRegion = 40;

    public const int LargoMaximoDelContacto = 20;

    private const int MinimoDeDigitosDelContacto = 6;

    private Fuente(Guid id, string codigo, string nombre, string region, string? contactoDeNotificacion, bool esRecepcion)
    {
        Id = id;
        Codigo = codigo;
        Nombre = nombre;
        Region = region;
        ContactoDeNotificacion = contactoDeNotificacion;
        EsRecepcion = esRecepcion;
    }

    public Guid Id { get; }

    /// <summary>Código único, en minúsculas, dígitos y guiones, de 2 a 20 caracteres.</summary>
    public string Codigo { get; }

    public string Nombre { get; }

    /// <summary>Código de región en minúsculas, sin tildes y con guiones: el mismo que usa el E-commerce (ECO-0 D08). Que sea una región conocida lo comprueba la Aplicación.</summary>
    public string Region { get; }

    /// <summary>Número de contacto de la sucursal para avisos (dato personal, nunca a logs, errores ni auditoría); puede faltar.</summary>
    public string? ContactoDeNotificacion { get; }

    /// <summary>Si es la única fuente que recibe la mercadería nueva de la administración central (FUE-19).</summary>
    public bool EsRecepcion { get; }

    public static bool EsCodigoValido(string? codigo) => codigo is not null && FormaDelCodigo().IsMatch(codigo);

    public static bool EsNombreValido(string? nombre) =>
        nombre is not null
        && nombre.Length is >= 2 and <= LargoMaximoDelNombre
        && nombre == nombre.Trim()
        && !nombre.Any(char.IsControl);

    public static bool EsRegionValida(string? region) => region is not null && region.Length <= LargoMaximoDeLaRegion && FormaDeLaRegion().IsMatch(region);

    /// <summary>El contacto es opcional; si viene, tiene dígitos y solo los signos habituales de un número (más, espacios, guiones y paréntesis).</summary>
    public static bool EsContactoValido(string? contacto) =>
        contacto is null
        || (contacto.Length <= LargoMaximoDelContacto
            && contacto == contacto.Trim()
            && FormaDelContacto().IsMatch(contacto)
            && contacto.Count(char.IsAsciiDigit) >= MinimoDeDigitosDelContacto);

    /// <summary>Registra una fuente nueva. Los datos inválidos lanzan: la Aplicación valida la entrada antes y devuelve los campos erróneos.</summary>
    public static Fuente Registrar(Guid id, string codigo, string nombre, string region, string? contactoDeNotificacion, bool esRecepcion)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("La fuente necesita su identificador.", nameof(id));
        }

        if (!EsCodigoValido(codigo))
        {
            throw new ArgumentException("El código es de 2 a 20 caracteres: minúsculas, dígitos y guiones.", nameof(codigo));
        }

        ComprobarDatos(nombre, region, contactoDeNotificacion);
        return new Fuente(id, codigo, nombre, region, contactoDeNotificacion, esRecepcion);
    }

    /// <summary>Edita nombre, región y contacto (el código no cambia, FUE-16) y devuelve la fuente nueva y los nombres de los campos que cambiaron.</summary>
    public (Fuente Fuente, IReadOnlyList<string> CamposCambiados) Editar(string nombre, string region, string? contactoDeNotificacion)
    {
        ComprobarDatos(nombre, region, contactoDeNotificacion);

        var cambiados = new List<string>(3);
        if (!string.Equals(Nombre, nombre, StringComparison.Ordinal))
        {
            cambiados.Add(CamposDeFuente.Nombre);
        }

        if (!string.Equals(Region, region, StringComparison.Ordinal))
        {
            cambiados.Add(CamposDeFuente.Region);
        }

        if (!string.Equals(ContactoDeNotificacion, contactoDeNotificacion, StringComparison.Ordinal))
        {
            cambiados.Add(CamposDeFuente.ContactoDeNotificacion);
        }

        return (new Fuente(Id, Codigo, nombre, region, contactoDeNotificacion, EsRecepcion), cambiados);
    }

    /// <summary>La misma fuente con la marca de recepción puesta o quitada.</summary>
    public Fuente ConRecepcion(bool esRecepcion) =>
        esRecepcion == EsRecepcion ? this : new Fuente(Id, Codigo, Nombre, Region, ContactoDeNotificacion, esRecepcion);

    private static void ComprobarDatos(string nombre, string region, string? contacto)
    {
        if (!EsNombreValido(nombre))
        {
            throw new ArgumentException("El nombre es de 2 a 100 caracteres, sin espacios en los extremos.", nameof(nombre));
        }

        if (!EsRegionValida(region))
        {
            throw new ArgumentException("La región es un código en minúsculas, sin tildes y con guiones.", nameof(region));
        }

        if (!EsContactoValido(contacto))
        {
            throw new ArgumentException("El contacto de notificación no es un número válido.", nameof(contacto));
        }
    }

    // \z y no $: «$» acepta un salto de línea final y dejaría pasar «lima\n».
    [GeneratedRegex(@"^[a-z0-9-]{2,20}\z", RegexOptions.CultureInvariant)]
    private static partial Regex FormaDelCodigo();

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex FormaDeLaRegion();

    [GeneratedRegex(@"^\+?[0-9 ()-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex FormaDelContacto();
}

/// <summary>Por qué se rechaza una ruta entre fuentes (FUE-17).</summary>
public enum MotivoDeRutaInvalida
{
    /// <summary>El plazo no es un entero mayor que cero.</summary>
    PlazoNoPositivo,

    /// <summary>El origen y el destino son la misma fuente.</summary>
    OrigenIgualADestino,

    /// <summary>Falta el origen o el destino.</summary>
    FuenteVacia,
}

/// <summary>
/// El plazo estimado, en días, del traslado desde una fuente hacia otra (FUE-12). Es <b>direccional</b> (Cusco→Ayacucho no implica
/// Ayacucho→Cusco), el plazo es un entero mayor que cero y el origen es distinto del destino (FUE-17). Es un dato que la administración
/// mantiene; agregar una sucursal no exige código.
/// </summary>
public sealed record RutaEntreFuentes
{
    private RutaEntreFuentes(Guid origenId, Guid destinoId, int plazoEnDias)
    {
        OrigenId = origenId;
        DestinoId = destinoId;
        PlazoEnDias = plazoEnDias;
    }

    public Guid OrigenId { get; }

    public Guid DestinoId { get; }

    public int PlazoEnDias { get; }

    /// <summary>El motivo por el que la ruta no sería válida, o <see langword="null"/> si lo es.</summary>
    public static MotivoDeRutaInvalida? Validar(Guid origenId, Guid destinoId, int plazoEnDias)
    {
        if (origenId == Guid.Empty || destinoId == Guid.Empty)
        {
            return MotivoDeRutaInvalida.FuenteVacia;
        }

        if (origenId == destinoId)
        {
            return MotivoDeRutaInvalida.OrigenIgualADestino;
        }

        return plazoEnDias <= 0 ? MotivoDeRutaInvalida.PlazoNoPositivo : null;
    }

    /// <summary>Define una ruta; una ruta inválida lanza (la Aplicación usa <see cref="Validar"/> y la rechaza con su código).</summary>
    public static RutaEntreFuentes Definir(Guid origenId, Guid destinoId, int plazoEnDias) =>
        Validar(origenId, destinoId, plazoEnDias) is { } motivo
            ? throw new ArgumentException($"La ruta no es válida: {motivo}.")
            : new RutaEntreFuentes(origenId, destinoId, plazoEnDias);

    /// <summary>La misma ruta con otro plazo; el nuevo plazo se valida igual.</summary>
    public RutaEntreFuentes ConPlazo(int plazoEnDias) => Definir(OrigenId, DestinoId, plazoEnDias);
}

/// <summary>El formato de una edición: solo las impresas tienen stock (regla 12 de AGENTS.md).</summary>
public enum FormatoDeEdicion
{
    Impreso,
    Digital,
}

/// <summary>Resultado de registrar o actualizar la ficha de una edición.</summary>
public enum ResultadoDeFicha
{
    /// <summary>La edición no se conocía: ficha nueva.</summary>
    Registrada,

    /// <summary>La edición ya tenía ese formato: nada cambia (idempotente).</summary>
    SinCambios,

    /// <summary>La edición cambió de formato y no tenía stock.</summary>
    FormatoActualizado,

    /// <summary>La edición tiene stock y no puede cambiar de formato (<c>INV-FORMATO-EN-CONFLICTO</c>).</summary>
    FormatoEnConflicto,
}

/// <summary>
/// La ficha que el Inventario guarda de una edición: solo su formato (D03; el identificador es el <c>edicionId</c> del Catálogo y no se guardan título
/// ni ISBN). Sin ficha, la edición es inexistente (FUE-15); con formato digital no tiene stock ni fuente (FUE-07).
/// </summary>
public sealed record FichaDeEdicion
{
    private FichaDeEdicion(Guid edicionId, FormatoDeEdicion formato)
    {
        EdicionId = edicionId;
        Formato = formato;
    }

    public Guid EdicionId { get; }

    public FormatoDeEdicion Formato { get; }

    /// <summary>Solo una edición impresa puede tener stock.</summary>
    public bool PuedeTenerStock => Formato == FormatoDeEdicion.Impreso;

    public static FichaDeEdicion Crear(Guid edicionId, FormatoDeEdicion formato)
    {
        if (edicionId == Guid.Empty)
        {
            throw new ArgumentException("La edición necesita su identificador.", nameof(edicionId));
        }

        if (!Enum.IsDefined(formato))
        {
            throw new ArgumentOutOfRangeException(nameof(formato), "El formato es impreso o digital.");
        }

        return new FichaDeEdicion(edicionId, formato);
    }

    /// <summary>
    /// Alta o actualización idempotente de la ficha por su <c>edicionId</c> (FUE-07, D03): una ficha nueva se registra, el mismo formato no
    /// cambia nada, y otro formato solo se acepta si la edición no tiene stock; con stock (físico o reservado en alguna fuente) es conflicto.
    /// </summary>
    public static (ResultadoDeFicha Resultado, FichaDeEdicion Ficha) Actualizar(FichaDeEdicion? actual, Guid edicionId, FormatoDeEdicion formato, bool tieneStock)
    {
        var nueva = Crear(edicionId, formato);
        if (actual is null)
        {
            return (ResultadoDeFicha.Registrada, nueva);
        }

        if (actual.EdicionId != edicionId)
        {
            throw new ArgumentException("La ficha actual es de otra edición.", nameof(actual));
        }

        if (actual.Formato == formato)
        {
            return (ResultadoDeFicha.SinCambios, actual);
        }

        return tieneStock ? (ResultadoDeFicha.FormatoEnConflicto, actual) : (ResultadoDeFicha.FormatoActualizado, nueva);
    }
}

/// <summary>Resultado de designar la fuente de recepción.</summary>
public enum ResultadoDeDesignacion
{
    /// <summary>La recepción pasó de otra fuente a la elegida.</summary>
    Cambiada,

    /// <summary>La elegida ya era la de recepción: nada cambia (idempotente).</summary>
    SinCambios,

    /// <summary>La fuente elegida no existe entre las fuentes.</summary>
    FuenteInexistente,
}

/// <summary>El resultado de <see cref="ReglaDeRecepcion.Designar"/>: lo que pasó, la fuente que dejó de serlo y la que lo es ahora.</summary>
public sealed record DesignacionDeRecepcion(ResultadoDeDesignacion Resultado, Fuente? Anterior, Fuente? Nueva, IReadOnlyList<Fuente> Fuentes);

/// <summary>
/// La regla «hay exactamente una fuente de recepción» (FUE-19, FUE-20, D07): la primera fuente registrada lo es; las demás reciben stock por
/// traslado o devolución, y la administración central puede designar otra, con lo que la marca anterior se quita. Si no hay fuentes, no hay recepción.
/// </summary>
public static class ReglaDeRecepcion
{
    /// <summary>La primera fuente registrada queda como la de recepción; las siguientes, no.</summary>
    public static bool LaNuevaEsRecepcion(int fuentesExistentes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fuentesExistentes);
        return fuentesExistentes == 0;
    }

    /// <summary>El invariante: con al menos una fuente, exactamente una es de recepción; sin fuentes, ninguna.</summary>
    public static bool CumpleElInvariante(IReadOnlyCollection<Fuente> fuentes)
    {
        ArgumentNullException.ThrowIfNull(fuentes);
        return fuentes.Count(f => f.EsRecepcion) == (fuentes.Count == 0 ? 0 : 1);
    }

    /// <summary>
    /// Designa la fuente de recepción: quita la marca de la anterior y pone la de la elegida, de modo que el conjunto resultante cumple el
    /// invariante. Designar la que ya lo es no cambia nada.
    /// </summary>
    public static DesignacionDeRecepcion Designar(IReadOnlyCollection<Fuente> fuentes, Guid fuenteElegidaId)
    {
        ArgumentNullException.ThrowIfNull(fuentes);

        var elegida = fuentes.FirstOrDefault(f => f.Id == fuenteElegidaId);
        if (elegida is null)
        {
            return new DesignacionDeRecepcion(ResultadoDeDesignacion.FuenteInexistente, null, null, [.. fuentes]);
        }

        if (elegida.EsRecepcion && fuentes.Count(f => f.EsRecepcion) == 1)
        {
            return new DesignacionDeRecepcion(ResultadoDeDesignacion.SinCambios, null, elegida, [.. fuentes]);
        }

        var anterior = fuentes.FirstOrDefault(f => f.EsRecepcion && f.Id != fuenteElegidaId);
        var nueva = elegida.ConRecepcion(true);
        var resultado = fuentes
            .Select(f => f.Id == fuenteElegidaId ? nueva : f.ConRecepcion(false))
            .ToList();

        return new DesignacionDeRecepcion(ResultadoDeDesignacion.Cambiada, anterior?.ConRecepcion(false), nueva, resultado);
    }
}
