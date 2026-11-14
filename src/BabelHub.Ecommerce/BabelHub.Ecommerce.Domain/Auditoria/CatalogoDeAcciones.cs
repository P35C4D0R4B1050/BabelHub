using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Domain.Auditoria;

/// <summary>
/// Si la acción concede o cambia algo (se registra con <c>IAuditoria</c>: si no se puede registrar, la operación no se aplica, AUD-04) o no
/// concede nada (un rechazo, un bloqueo, un intento: se registra con <c>IRegistroDeSeguridad</c>, que nunca interrumpe, AUD-12).
/// </summary>
public enum ClaseDeAccion
{
    ConcedeOCambia,
    NoConcede,
}

/// <summary>El tipo del registro (AUD-01): de auditoría, de seguridad, o ambos a la vez (AUD-20, como un cambio de rol).</summary>
public enum TipoDeRegistro
{
    Auditoria,
    Seguridad,
    Ambos,
}

/// <summary>
/// Una acción del catálogo (AUD-01, AUD-02): su identificador estable, su clase, el tipo de su registro, si va al almacén aislado de Identidad, las
/// claves permitidas de su detalle (cada una marcada personal o no), si su objeto y su rol son datos personales (<b>sin valor por defecto</b>: quien
/// declara la acción decide) y sus motivos, de catálogo cerrado. Los textos libres de una solicitud nunca son un motivo.
/// </summary>
public sealed record AccionDelCatalogo
{
    private static readonly Regex FormatoDeAccion = new("^[A-Z][A-Za-z0-9]{2,63}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex FormatoDeMotivo = new("^[a-z][a-z0-9-]{1,63}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public AccionDelCatalogo(
        string identificador,
        ClaseDeAccion clase,
        TipoDeRegistro tipo,
        bool aislada,
        IReadOnlyCollection<ClaveDeDetalle> detalle,
        bool objetoPersonal,
        bool rolPersonal,
        IReadOnlyCollection<string> motivos,
        bool exigeMotivo)
    {
        ArgumentNullException.ThrowIfNull(detalle);
        ArgumentNullException.ThrowIfNull(motivos);
        if (string.IsNullOrEmpty(identificador) || !FormatoDeAccion.IsMatch(identificador))
        {
            throw new ArgumentException("El identificador de una acción empieza con mayúscula y solo lleva letras y dígitos (3 a 64).", nameof(identificador));
        }

        var coherente = clase == ClaseDeAccion.ConcedeOCambia ? tipo is TipoDeRegistro.Auditoria or TipoDeRegistro.Ambos : tipo == TipoDeRegistro.Seguridad;
        if (!coherente)
        {
            throw new ArgumentException("Una acción que concede o cambia algo es de auditoría o de ambos tipos; una que no concede nada es de seguridad.", nameof(tipo));
        }

        if (detalle.Select(c => c.Nombre).Distinct(StringComparer.Ordinal).Count() != detalle.Count)
        {
            throw new ArgumentException("Una clave del detalle se declaró más de una vez.", nameof(detalle));
        }

        if (motivos.Any(m => string.IsNullOrEmpty(m) || !FormatoDeMotivo.IsMatch(m)) || motivos.Distinct(StringComparer.Ordinal).Count() != motivos.Count)
        {
            throw new ArgumentException("Los motivos son códigos del catálogo (minúsculas, dígitos y guiones) y no se repiten.", nameof(motivos));
        }

        if (exigeMotivo && motivos.Count == 0)
        {
            throw new ArgumentException("Una acción que exige motivo necesita al menos uno en el catálogo.", nameof(exigeMotivo));
        }

        if (tipo != TipoDeRegistro.Auditoria && !exigeMotivo)
        {
            throw new ArgumentException("Un evento de seguridad lleva siempre su motivo (AUD-01): la acción debe exigirlo.", nameof(exigeMotivo));
        }

        Identificador = identificador;
        Clase = clase;
        Tipo = tipo;
        Aislada = aislada;
        Detalle = [.. detalle];
        ObjetoPersonal = objetoPersonal;
        RolPersonal = rolPersonal;
        Motivos = [.. motivos];
        ExigeMotivo = exigeMotivo;
    }

    public string Identificador { get; }

    public ClaseDeAccion Clase { get; }

    public TipoDeRegistro Tipo { get; }

    /// <summary>Si el registro va al almacén aislado de Identidad (<c>identidad.registros</c>) y no al del módulo de Auditoría (D17).</summary>
    public bool Aislada { get; }

    /// <summary>Las claves permitidas del detalle, en orden, con su marca de personal.</summary>
    public IReadOnlyList<ClaveDeDetalle> Detalle { get; }

    /// <summary>Si el objeto afectado (su identificador) identifica a una persona: la depuración lo anonimiza (AUD-19).</summary>
    public bool ObjetoPersonal { get; }

    /// <summary>Si el rol del actor identifica a una persona en esta acción (por ejemplo, el encargado único de una sucursal).</summary>
    public bool RolPersonal { get; }

    public IReadOnlyList<string> Motivos { get; }

    public bool ExigeMotivo { get; }

    /// <summary>Si el motivo es válido para esta acción: del catálogo cerrado, o ausente cuando la acción no lo exige.</summary>
    public bool AdmiteMotivo(string? motivo) =>
        motivo is null ? !ExigeMotivo : Motivos.Contains(motivo, StringComparer.Ordinal);
}

/// <summary>El catálogo cerrado de acciones auditables de un servicio (AUD-01, AUD-02, D15). Cada feature aporta las suyas; aquí se valida la estructura.</summary>
public sealed class CatalogoDeAcciones
{
    private readonly Dictionary<string, AccionDelCatalogo> _porIdentificador;

    public CatalogoDeAcciones(IReadOnlyCollection<AccionDelCatalogo> acciones)
    {
        ArgumentNullException.ThrowIfNull(acciones);
        _porIdentificador = new Dictionary<string, AccionDelCatalogo>(StringComparer.Ordinal);
        foreach (var accion in acciones)
        {
            if (!_porIdentificador.TryAdd(accion.Identificador, accion))
            {
                throw new ArgumentException($"La acción {accion.Identificador} está repetida en el catálogo.", nameof(acciones));
            }
        }

        Acciones = [.. acciones];
    }

    public IReadOnlyList<AccionDelCatalogo> Acciones { get; }

    public bool TryObtener(string identificador, out AccionDelCatalogo? accion) =>
        _porIdentificador.TryGetValue(identificador, out accion);

    public AccionDelCatalogo Obtener(string identificador) =>
        _porIdentificador.TryGetValue(identificador, out var accion) ? accion : throw new KeyNotFoundException($"La acción {identificador} no está en el catálogo.");
}

/// <summary>
/// La ruta de un recurso tal como se guarda en un registro (AUD-05, AUD-06): <b>la plantilla</b> de la ruta, nunca la ruta cruda. Sin parámetros de
/// consulta, de ruta (<c>;</c>) ni fragmento, con un tope de longitud, y la constante <see cref="SinCoincidencia"/> cuando ninguna plantilla coincide.
/// </summary>
public static class PlantillaDeRuta
{
    public const string SinCoincidencia = "sin-coincidencia";

    public const int MaximoDeCaracteres = 200;

    private static readonly Regex Formato = new(@"^/(?:[A-Za-z0-9._-]+|\{[A-Za-z][A-Za-z0-9]*\})(?:/(?:[A-Za-z0-9._-]+|\{[A-Za-z][A-Za-z0-9]*\}))*$|^/$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Si es una plantilla que se puede guardar: empieza con «/», no lleva «?», «;», «#», «%» ni espacios, y los parámetros son segmentos enteros como <c>{id}</c>.</summary>
    public static bool EsPlantillaValida(string? plantilla) =>
        !string.IsNullOrEmpty(plantilla) && plantilla.Length <= MaximoDeCaracteres && Formato.IsMatch(plantilla);

    /// <summary>La plantilla que coincide con la ruta cruda (la más específica), o <see cref="SinCoincidencia"/>.</summary>
    public static string Resolver(string? rutaCruda, IReadOnlyCollection<string> plantillas)
    {
        ArgumentNullException.ThrowIfNull(plantillas);
        foreach (var plantilla in plantillas)
        {
            if (!EsPlantillaValida(plantilla))
            {
                throw new ArgumentException("Una plantilla de ruta no es válida (sin consulta, «;» ni fragmento).", nameof(plantillas));
            }
        }

        if (string.IsNullOrWhiteSpace(rutaCruda) || rutaCruda.Length > MaximoDeCaracteres || rutaCruda[0] != '/')
        {
            return SinCoincidencia;
        }

        var sinConsulta = rutaCruda;
        var corte = sinConsulta.IndexOfAny(['?', '#']);
        if (corte >= 0)
        {
            sinConsulta = sinConsulta[..corte];
        }

        var segmentos = sinConsulta
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Contains(';', StringComparison.Ordinal) ? s[..s.IndexOf(';', StringComparison.Ordinal)] : s)
            .ToArray();
        if (segmentos.Any(string.IsNullOrEmpty))
        {
            return SinCoincidencia;
        }

        string? mejor = null;
        var mejorLiterales = -1;
        foreach (var plantilla in plantillas.Order(StringComparer.Ordinal))
        {
            var partes = plantilla.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length != segmentos.Length)
            {
                continue;
            }

            var literales = 0;
            var coincide = true;
            for (var i = 0; i < partes.Length && coincide; i++)
            {
                if (partes[i].StartsWith('{'))
                {
                    continue;
                }

                coincide = string.Equals(partes[i], segmentos[i], StringComparison.OrdinalIgnoreCase);
                literales++;
            }

            if (coincide && literales > mejorLiterales)
            {
                mejor = plantilla;
                mejorLiterales = literales;
            }
        }

        return mejor ?? SinCoincidencia;
    }
}
