using NetArchTest.Rules;

// Casos de ejemplo que violan (o respetan) las reglas a propósito. Viven en el ensamblado de pruebas, con espacios de nombres
// que imitan los del E-commerce, para que las reglas de arquitectura se prueben a sí mismas mientras los proyectos reales
// aún están vacíos (auditoría R6, H13). No forman parte del código de producción.
namespace BabelHub.Ecommerce.Application.Catalogo
{
    /// <summary>Contrato público del módulo Catálogo (raíz de su espacio de nombres): cualquier módulo puede usarlo.</summary>
    public interface ICatalogoDeEjemplo
    {
        string Titulo(int edicionId);
    }
}

namespace BabelHub.Ecommerce.Application.Catalogo.Interno
{
    /// <summary>Interior privado del módulo Catálogo: ningún otro módulo puede depender de él.</summary>
    public sealed class DetalleOculto
    {
        public string Valor => "interno";
    }
}

namespace BabelHub.Ecommerce.Application.Pagos
{
    /// <summary>Respeta la regla 13: solo usa el contrato público de Catálogo.</summary>
    public sealed class UsaElContratoPublico(Catalogo.ICatalogoDeEjemplo catalogo)
    {
        public string Resumen(int edicionId) => catalogo.Titulo(edicionId);
    }

    /// <summary>Viola la regla 13: usa el interior de Catálogo.</summary>
    public sealed class UsaElInteriorAjeno
    {
        public string Resumen() => new Catalogo.Interno.DetalleOculto().Valor;
    }
}

namespace BabelHub.Ecommerce.Application.Pedidos
{
    /// <summary>Viola la regla de capas: la Aplicación no usa infraestructura (HttpClient).</summary>
    public sealed class UsaInfraestructura
    {
        public HttpClient Cliente { get; } = new();
    }
}

namespace BabelHub.ArchitectureTests
{
    public class PruebasDeSensibilidadTests
    {
        private static readonly System.Reflection.Assembly Pruebas = typeof(PruebasDeSensibilidadTests).Assembly;

        [Fact]
        public void La_regla_de_modulos_detecta_el_uso_del_interior_de_otro_modulo()
        {
            var resultado = ReglasDeModulosTests.ComprobarInterior(Pruebas, "BabelHub.Ecommerce", "Application", "Pagos");

            Assert.False(resultado.IsSuccessful, "La regla 13 no detectó que Pagos usa el interior de Catálogo.");
            var tipos = resultado.FailingTypeNames ?? [];
            Assert.Contains(tipos, nombre => nombre.EndsWith("UsaElInteriorAjeno", StringComparison.Ordinal));
        }

        [Fact]
        public void La_regla_de_modulos_acepta_el_uso_del_contrato_publico()
        {
            var resultado = ReglasDeModulosTests.ComprobarInterior(Pruebas, "BabelHub.Ecommerce", "Application", "Pagos");

            var tipos = resultado.FailingTypeNames ?? [];
            Assert.DoesNotContain(tipos, nombre => nombre.EndsWith("UsaElContratoPublico", StringComparison.Ordinal));
        }

        [Fact]
        public void La_regla_de_capas_detecta_infraestructura_en_la_aplicacion()
        {
            var resultado = Types.InAssembly(Pruebas)
                .That().ResideInNamespace("BabelHub.Ecommerce.Application.Pedidos")
                .ShouldNot().HaveDependencyOnAny("System.Net.Http")
                .GetResult();

            Assert.False(resultado.IsSuccessful, "La regla de capas no detectó el uso de HttpClient en la Aplicación.");
        }
    }
}
