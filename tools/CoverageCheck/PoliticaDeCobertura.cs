namespace BabelHub.Tools.CoverageCheck;

/// <summary>Los mínimos de cobertura de líneas de un módulo.</summary>
public sealed record Umbrales(int DominioYAplicacion, int Total);

/// <summary>La criticidad de un módulo. Uno sin clasificar se exige como crítico.</summary>
public enum Criticidad
{
    SinClasificar = 0,
    Critico = 1,
    NoCritico = 2,
}

/// <summary>
/// La política de cobertura (RNF28, PLA-14) y el único sitio donde se declara la criticidad de cada módulo. Su explicación está en
/// <c>docs/07-proceso-de-desarrollo/03-estrategia-de-pruebas.md</c> §3. Pasar un módulo a «no crítico» baja su exigencia: es una
/// decisión de la persona responsable. Un módulo que no figure en ninguna de las dos listas se exige como crítico.
/// </summary>
public sealed class PoliticaDeCobertura
{
    private readonly HashSet<string> _criticos;
    private readonly HashSet<string> _noCriticos;

    /// <param name="criticos">Módulos críticos, como <c>Servicio/Modulo</c>.</param>
    /// <param name="noCriticos">Módulos no críticos, como <c>Servicio/Modulo</c>.</param>
    public PoliticaDeCobertura(IEnumerable<string> criticos, IEnumerable<string> noCriticos)
    {
        _criticos = new HashSet<string>(criticos, StringComparer.Ordinal);
        _noCriticos = new HashSet<string>(noCriticos, StringComparer.Ordinal);
        if (_criticos.Overlaps(_noCriticos))
        {
            throw new ArgumentException("Un módulo no puede ser crítico y no crítico a la vez.", nameof(noCriticos));
        }
    }

    public static PoliticaDeCobertura Vigente { get; } = new(
        criticos:
        [
            // Stock.
            "Inventario/Ajustes",
            "Inventario/Fuentes",
            "Inventario/Reservas",
            "Inventario/Traslados",

            // Dinero y pedidos.
            "Ecommerce/Pagos",
            "Ecommerce/Pedidos",

            // Identidad y acceso por rol y sucursal (Web: sesión del servidor, área por rol y la Presentación de las áreas).
            "Ecommerce/Identidad",
            "Ecommerce/Web",

            // Biblioteca digital.
            "Ecommerce/Biblioteca",

            // Auditoría y datos personales (el Libro de Reclamaciones guarda documento y domicilio del consumidor).
            "Ecommerce/Auditoria",
            "Inventario/Auditoria",
            "Ecommerce/Reclamaciones",

            // Seguridad transversal: límites, secretos, registro de supresiones, composición y migración.
            "Ecommerce/Plataforma",
            "Inventario/Plataforma",
        ],
        noCriticos:
        [
            "Ecommerce/Asistente",
            "Ecommerce/Carrito",
            "Ecommerce/Catalogo",
            "Ecommerce/Devoluciones",
            "Ecommerce/Envios",
            "Ecommerce/Notificaciones",
        ]);

    public Umbrales UmbralesCriticos { get; } = new(DominioYAplicacion: 85, Total: 70);

    public Umbrales UmbralesNoCriticos { get; } = new(DominioYAplicacion: 60, Total: 40);

    /// <summary>Los módulos críticos declarados, como <c>Servicio/Modulo</c>: todos deben tener datos de cobertura.</summary>
    public IReadOnlyCollection<string> ModulosCriticos => _criticos;

    /// <summary>Los módulos no críticos declarados, como <c>Servicio/Modulo</c>.</summary>
    public IReadOnlyCollection<string> ModulosNoCriticos => _noCriticos;

    public Criticidad CriticidadDe(string servicio, string modulo)
    {
        var clave = $"{servicio}/{modulo}";
        return _noCriticos.Contains(clave) ? Criticidad.NoCritico
            : _criticos.Contains(clave) ? Criticidad.Critico
            : Criticidad.SinClasificar;
    }

    public Umbrales UmbralesDe(Criticidad criticidad) => criticidad == Criticidad.NoCritico ? UmbralesNoCriticos : UmbralesCriticos;

    /// <summary>
    /// El módulo de una carpeta de primer nivel de una capa. Las carpetas que no son un módulo se asignan al que les corresponde:
    /// la Presentación de las áreas es la interfaz web, y la raíz del proyecto, la composición y la migración son de Plataforma.
    /// </summary>
    public static string ModuloDeLaCarpeta(string carpeta) => carpeta switch
    {
        "" or "Composicion" or "Migracion" => "Plataforma",
        "Areas" or "Controllers" or "Views" => "Web",
        _ => carpeta,
    };
}
