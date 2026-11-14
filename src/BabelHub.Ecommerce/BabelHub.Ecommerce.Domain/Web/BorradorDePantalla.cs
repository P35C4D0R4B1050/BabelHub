using System.Text;
using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Domain.Web;

/// <summary>Las plazas y los topes del borrador de pantalla (WEB-25, D06).</summary>
public static class PoliticaDeBorradores
{
    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(30);

    public const int MaximoPorUsuario = 5;

    public const int TamanoMaximoEnBytes = 32 * 1024;

    /// <summary>Un usuario tiene hasta 5 borradores; reemplazar el de la misma plantilla no ocupa otra plaza.</summary>
    public static bool PuedeGuardar(int existentesDelUsuario, bool reemplazaElDeLaMismaPlantilla) =>
        reemplazaElDeLaMismaPlantilla || existentesDelUsuario < MaximoPorUsuario;
}

/// <summary>
/// La lista cerrada de plantillas de ruta cuyo contenido puede conservarse al vencer la sesión (D06): una plantilla, nunca la dirección completa ni
/// una consulta. Una pantalla nueva solo entra aquí por decisión explícita; ingreso, recuperación, verificación y administración no.
/// </summary>
public static class PlantillasDeBorrador
{
    private static readonly HashSet<string> Conservables = new(StringComparer.Ordinal)
    {
        "/reclamaciones/nueva",
        "/cuenta/datos",
        "/checkout/datos",
    };

    public static bool EsConservable(string? plantilla) => plantilla is not null && Conservables.Contains(plantilla);
}

public enum ResultadoDeEntrega
{
    Entregado,
    NoEsDelUsuario,
    Vencido,
    YaEntregado,
}

/// <summary>Lo que devuelve la entrega de un borrador: el resultado y, solo si se entregó, una copia de los campos.</summary>
public sealed record EntregaDeBorrador(ResultadoDeEntrega Resultado, IReadOnlyDictionary<string, string>? Campos);

/// <summary>
/// Lo que la persona tenía en pantalla cuando venció su sesión (WEB-25): solo campos de una lista cerrada de plantillas, hasta 32 KB, 30 minutos, ligado
/// al usuario y de entrega única. Nunca contiene contraseñas, códigos ni tokens: aunque un campo se marque por error para conservarlo, el Dominio lo rechaza.
/// </summary>
public sealed partial class BorradorDePantalla
{
    private static readonly string[] FragmentosProhibidos = ["contrasena", "contraseña", "password", "clave", "codigo", "código", "token", "secreto", "captcha", "turnstile"];

    private readonly Dictionary<string, string> _campos;

    private BorradorDePantalla(Guid id, Guid usuarioId, string plantilla, Dictionary<string, string> campos, DateTimeOffset creadoEn, bool entregado)
    {
        Id = id;
        UsuarioId = usuarioId;
        Plantilla = plantilla;
        _campos = campos;
        CreadoEn = creadoEn;
        VenceEn = creadoEn + PoliticaDeBorradores.Vigencia;
        Entregado = entregado;
    }

    public Guid Id { get; }

    public Guid UsuarioId { get; }

    public string Plantilla { get; }

    public DateTimeOffset CreadoEn { get; }

    public DateTimeOffset VenceEn { get; }

    public bool Entregado { get; private set; }

    public IReadOnlyDictionary<string, string> CamposGuardados => _campos;

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex NombreDeCampo();

    public static BorradorDePantalla Crear(Guid id, Guid usuarioId, string plantilla, IReadOnlyDictionary<string, string> campos, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(campos);
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("El borrador necesita un usuario.", nameof(usuarioId));
        }

        if (!PlantillasDeBorrador.EsConservable(plantilla))
        {
            throw new ArgumentException("La plantilla no está en la lista cerrada de pantallas que conservan lo escrito.", nameof(plantilla));
        }

        if (campos.Count == 0)
        {
            throw new ArgumentException("El borrador necesita al menos un campo.", nameof(campos));
        }

        long bytes = 0;
        foreach (var (nombre, valor) in campos)
        {
            if (!NombreDeCampo().IsMatch(nombre))
            {
                throw new ArgumentException("El nombre de un campo no tiene la forma permitida.", nameof(campos));
            }

            if (FragmentosProhibidos.Any(f => nombre.Contains(f, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Un campo de contraseña, código o token no se conserva nunca.", nameof(campos));
            }

            bytes += Encoding.UTF8.GetByteCount(nombre) + Encoding.UTF8.GetByteCount(valor ?? string.Empty);
        }

        if (bytes > PoliticaDeBorradores.TamanoMaximoEnBytes)
        {
            throw new ArgumentException("El borrador supera el tamaño máximo.", nameof(campos));
        }

        return new BorradorDePantalla(id, usuarioId, plantilla, new Dictionary<string, string>(campos, StringComparer.Ordinal), ahora, false);
    }

    /// <summary>Reconstruye un borrador guardado (los campos ya fueron validados al crearlo).</summary>
    public static BorradorDePantalla Reconstruir(Guid id, Guid usuarioId, string plantilla, IReadOnlyDictionary<string, string> campos, DateTimeOffset creadoEn, bool entregado) =>
        new(id, usuarioId, plantilla, new Dictionary<string, string>(campos, StringComparer.Ordinal), creadoEn, entregado);

    /// <summary>
    /// Entrega los campos una sola vez y solo al mismo usuario. Una petición de otra persona no gasta la entrega; un borrador vencido o ya entregado no
    /// devuelve nada.
    /// </summary>
    public EntregaDeBorrador Entregar(Guid usuarioQuePide, DateTimeOffset ahora)
    {
        if (usuarioQuePide != UsuarioId)
        {
            return new EntregaDeBorrador(ResultadoDeEntrega.NoEsDelUsuario, null);
        }

        if (Entregado)
        {
            return new EntregaDeBorrador(ResultadoDeEntrega.YaEntregado, null);
        }

        if (ahora >= VenceEn)
        {
            return new EntregaDeBorrador(ResultadoDeEntrega.Vencido, null);
        }

        Entregado = true;
        return new EntregaDeBorrador(ResultadoDeEntrega.Entregado, new Dictionary<string, string>(_campos, StringComparer.Ordinal));
    }
}
