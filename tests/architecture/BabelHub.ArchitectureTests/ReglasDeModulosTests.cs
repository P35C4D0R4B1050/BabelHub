using System.Reflection;
using NetArchTest.Rules;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// Hace cumplir la regla 13 de AGENTS.md. Cada servicio es un monolito modular: un módulo es una carpeta
/// (y un espacio de nombres) dentro de cada capa. Su contrato público está en la raíz de su espacio de nombres,
/// por ejemplo <c>BabelHub.Ecommerce.Application.Pagos</c>, y su interior privado en <c>.Interno</c>.
/// Un módulo nunca depende del <c>Interno</c>, del Dominio ni de la Infraestructura de otro, y la Api nunca depende del <c>Interno</c> de un módulo.
/// </summary>
public class ReglasDeModulosTests
{
    private static readonly string[] Capas = ["Domain", "Application", "Infrastructure"];

    private static readonly IReadOnlyDictionary<string, string[]> ModulosPorServicio = new Dictionary<string, string[]>
    {
        ["BabelHub.Ecommerce"] =
        [
            "Catalogo", "Carrito", "Pagos", "Pedidos", "Envios", "Devoluciones", "Biblioteca", "Reclamaciones", "Identidad", "Asistente", "Plataforma",
        ],
        ["BabelHub.Inventario"] = ["Fuentes", "Reservas", "Traslados", "Ajustes"],
    };

    public static TheoryData<string> NombresDeServicios => new(ModulosPorServicio.Keys.ToArray());

    [Theory]
    [MemberData(nameof(NombresDeServicios))]
    public void Existe_la_carpeta_de_cada_modulo_en_cada_capa(string servicio)
    {
        foreach (var capa in Capas)
        {
            foreach (var modulo in ModulosPorServicio[servicio])
            {
                var carpeta = Path.Combine(Proyectos.Raiz, "src", servicio, $"{servicio}.{capa}", modulo);
                Assert.True(Directory.Exists(carpeta), $"Falta la carpeta del módulo {modulo} en {servicio}.{capa}.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(NombresDeServicios))]
    public void Un_modulo_no_depende_del_interior_de_otro(string servicio)
    {
        foreach (var capa in Capas)
        {
            var ensamblado = Cargar($"{servicio}.{capa}");
            foreach (var modulo in ModulosPorServicio[servicio])
            {
                var resultado = ComprobarInterior(ensamblado, servicio, capa, modulo);

                Assert.True(
                    resultado.IsSuccessful,
                    $"{modulo} en {servicio}.{capa} depende del interior de otro módulo: {string.Join(", ", resultado.FailingTypeNames ?? [])}.");
            }
        }
    }

    /// <summary>
    /// La regla 13 aplicada a un módulo de una capa: sus tipos no dependen del <c>Interno</c>, del Dominio ni de la Infraestructura de ningún otro módulo del servicio.
    /// La usan la prueba real y las de sensibilidad (<see cref="PruebasDeSensibilidadTests"/>), para que ambas apliquen exactamente la misma regla.
    /// </summary>
    internal static TestResult ComprobarInterior(Assembly ensamblado, string servicio, string capa, string modulo)
    {
        var prohibidos = ModulosPorServicio[servicio]
            .Where(otro => otro != modulo)
            .SelectMany(otro => Capas.Select(c => $"{servicio}.{c}.{otro}.Interno")
                .Concat([$"{servicio}.Domain.{otro}", $"{servicio}.Infrastructure.{otro}"]))
            .ToArray();

        return Types.InAssembly(ensamblado)
            .That().ResideInNamespace($"{servicio}.{capa}.{modulo}")
            .ShouldNot().HaveDependencyOnAny(prohibidos)
            .GetResult();
    }

    /// <summary>
    /// La interfaz web (la Api) llama a los módulos por sus contratos públicos: ninguno de sus tipos depende del <c>Interno</c> de un módulo.
    /// </summary>
    internal static TestResult ComprobarApi(Assembly ensamblado, string servicio)
    {
        var interiores = ModulosPorServicio[servicio]
            .SelectMany(modulo => Capas.Select(c => $"{servicio}.{c}.{modulo}.Interno"))
            .ToArray();

        return Types.InAssembly(ensamblado)
            .That().ResideInNamespace($"{servicio}.Api")
            .ShouldNot().HaveDependencyOnAny(interiores)
            .GetResult();
    }

    [Theory]
    [MemberData(nameof(NombresDeServicios))]
    public void La_api_no_depende_del_interior_de_ningun_modulo(string servicio)
    {
        var resultado = ComprobarApi(Cargar($"{servicio}.Api"), servicio);

        Assert.True(
            resultado.IsSuccessful,
            $"La Api de {servicio} depende del interior de un módulo: {string.Join(", ", resultado.FailingTypeNames ?? [])}.");
    }

    private static Assembly Cargar(string nombre) =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == nombre)
        ?? Assembly.Load(nombre);
}
