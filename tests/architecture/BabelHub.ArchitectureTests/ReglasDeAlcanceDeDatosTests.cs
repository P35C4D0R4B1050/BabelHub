using System.Reflection;
using NetArchTest.Rules;

// Tipos de ejemplo que usan (o no) la elevación del alcance de datos desde donde no deben, para que la regla se pruebe a sí misma
// (como las de módulos). Imitan los espacios de nombres del Inventario; no forman parte del código de producción.
namespace BabelHub.Inventario.Application.Plataforma
{
    /// <summary>Hace de <c>IElevacionDeAlcance</c> en las pruebas de sensibilidad (la real es interna y no se ve desde aquí).</summary>
    public interface IElevacionDeEjemplo
    {
        void ConAlcanceDeRed();
    }
}

namespace BabelHub.Inventario.Infrastructure.Fuentes
{
    /// <summary>Permitido: el adaptador de Fuentes puede elevar.</summary>
    public sealed class AdaptadorQueElevaPermitido(BabelHub.Inventario.Application.Plataforma.IElevacionDeEjemplo elevacion)
    {
        public void Usar() => elevacion.ConAlcanceDeRed();
    }
}

namespace BabelHub.Inventario.Infrastructure.Reservas
{
    /// <summary>Permitido: el adaptador de Reservas puede elevar.</summary>
    public sealed class AdaptadorDeReservasQueElevaPermitido(BabelHub.Inventario.Application.Plataforma.IElevacionDeEjemplo elevacion)
    {
        public void Usar() => elevacion.ConAlcanceDeRed();
    }
}

namespace BabelHub.Inventario.Infrastructure.Traslados
{
    /// <summary>Viola la regla: Traslados ya no eleva (D09 de inventario-fuentes).</summary>
    public sealed class AdaptadorQueEleva(BabelHub.Inventario.Application.Plataforma.IElevacionDeEjemplo elevacion)
    {
        public void Usar() => elevacion.ConAlcanceDeRed();
    }
}

namespace BabelHub.Inventario.Infrastructure.Plataforma
{
    /// <summary>Permitido: es quien implementa la elevación.</summary>
    public sealed class UnidadDeTrabajoPostgres : BabelHub.Inventario.Application.Plataforma.IElevacionDeEjemplo
    {
        public void ConAlcanceDeRed()
        {
        }
    }

    /// <summary>Viola la regla: otro adaptador de Plataforma (por ejemplo el motor de límites) no debe elevar.</summary>
    public sealed class OtroAdaptadorDePlataforma(BabelHub.Inventario.Application.Plataforma.IElevacionDeEjemplo elevacion)
    {
        public void Usar() => elevacion.ConAlcanceDeRed();
    }
}

namespace BabelHub.Inventario.Application.Ajustes
{
    /// <summary>Viola la regla: la Aplicación nunca eleva.</summary>
    public sealed class CasoDeUsoQueEleva(BabelHub.Inventario.Application.Plataforma.IElevacionDeEjemplo elevacion)
    {
        public void Usar() => elevacion.ConAlcanceDeRed();
    }
}

namespace BabelHub.Inventario.Api
{
    /// <summary>Viola la regla: la Api nunca eleva.</summary>
    public sealed class ControladorQueEleva(BabelHub.Inventario.Application.Plataforma.IElevacionDeEjemplo elevacion)
    {
        public void Usar() => elevacion.ConAlcanceDeRed();
    }
}

namespace BabelHub.ArchitectureTests
{
    /// <summary>
    /// La elevación del alcance de datos a «toda la red» (<c>ConAlcanceDeRed</c>) es de visibilidad restringida (inventario-fuentes D09 y T04):
    /// solo la usan los adaptadores de Infraestructura de <c>Fuentes</c>, <c>Ajustes</c> y <c>Reservas</c> (y <c>UnidadDeTrabajoPostgres</c>, que la
    /// implementa); nunca la Api, el Dominio ni la Aplicación. <c>Traslados</c> ya no la usa y por eso no está en la lista. Esta prueba hace
    /// cumplir lo que el compilador no puede (la Infraestructura ve el interior de la Aplicación).
    /// </summary>
    public class ReglasDeAlcanceDeDatosTests
    {
        private const string Servicio = "BabelHub.Inventario";
        private const string InterfazReal = "BabelHub.Inventario.Application.Plataforma.IElevacionDeAlcance";
        private const string InterfazDeEjemplo = "BabelHub.Inventario.Application.Plataforma.IElevacionDeEjemplo";

        /// <summary>Los módulos cuyos adaptadores de Infraestructura pueden elevar el alcance.</summary>
        private static readonly string[] ModulosPermitidos = ["Fuentes", "Ajustes", "Reservas"];

        private static readonly Assembly Pruebas = typeof(ReglasDeAlcanceDeDatosTests).Assembly;

        /// <summary>Los tipos de Infraestructura que dependen de la elevación y no están entre los permitidos.</summary>
        internal static IReadOnlyList<string> ViolacionesEnInfraestructura(Assembly ensamblado, string interfaz)
        {
            var permitidos = ModulosPermitidos.Select(m => $"{Servicio}.Infrastructure.{m}").ToArray();

            return Types.InAssembly(ensamblado)
                .That().ResideInNamespaceStartingWith($"{Servicio}.Infrastructure")
                .And().HaveDependencyOn(interfaz)
                .GetTypes()
                .Where(t => !EsPermitido(t, permitidos))
                .Select(t => t.FullName ?? t.Name)
                .ToList();
        }

        /// <summary>Los tipos de otra capa (Dominio, Aplicación o Api) que dependen de la elevación: ninguno puede.</summary>
        internal static IReadOnlyList<string> ViolacionesEnOtrasCapas(Assembly ensamblado, string capa, string interfaz) =>
            Types.InAssembly(ensamblado)
                .That().ResideInNamespaceStartingWith($"{Servicio}.{capa}")
                .And().HaveDependencyOn(interfaz)
                .GetTypes()
                .Select(t => t.FullName ?? t.Name)
                .ToList();

        private static bool EsPermitido(Type tipo, string[] espaciosPermitidos)
        {
            var espacio = tipo.Namespace ?? string.Empty;

            // Quien implementa la elevación (el tipo, no el resto de Plataforma).
            if (espacio == $"{Servicio}.Infrastructure.Plataforma" && Raiz(tipo).Name == "UnidadDeTrabajoPostgres")
            {
                return true;
            }

            return espaciosPermitidos.Any(p => espacio == p || espacio.StartsWith(p + ".", StringComparison.Ordinal));
        }

        private static Type Raiz(Type tipo)
        {
            while (tipo.DeclaringType is not null)
            {
                tipo = tipo.DeclaringType;
            }

            return tipo;
        }

        private static Assembly Cargar(string nombre) =>
            AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == nombre) ?? Assembly.Load(nombre);

        // --- el código real ---

        [Fact]
        [Trait("Criterio", "AC-FUE-06")]
        public void La_elevacion_existe_y_es_interna_a_la_aplicacion()
        {
            var interfaz = Cargar($"{Servicio}.Application").GetType(InterfazReal);

            Assert.NotNull(interfaz); // si se renombra, la regla de abajo dejaría de vigilar algo y no avisaría
            Assert.False(interfaz.IsPublic, "La elevación del alcance debe ser interna a la Aplicación.");
        }

        [Fact]
        [Trait("Criterio", "AC-FUE-06")]
        public void Ningun_tipo_publico_expone_un_metodo_ConAlcanceDeRed_fuera_de_la_interfaz_interna()
        {
            var publicos = new[] { "Domain", "Application", "Infrastructure", "Api" }
                .SelectMany(capa => Cargar($"{Servicio}.{capa}").GetTypes())
                .Where(t => t.IsVisible)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                .Where(m => m.Name == "ConAlcanceDeRed")
                .Select(m => $"{m.DeclaringType?.FullName}.{m.Name}")
                .ToList();

            Assert.True(publicos.Count == 0, $"La elevación solo se alcanza por IElevacionDeAlcance, pero hay métodos públicos: {string.Join(", ", publicos)}.");
        }

        [Fact]
        [Trait("Criterio", "AC-FUE-06")]
        public void La_infraestructura_solo_eleva_en_los_adaptadores_de_Fuentes_Ajustes_y_Reservas()
        {
            var violaciones = ViolacionesEnInfraestructura(Cargar($"{Servicio}.Infrastructure"), InterfazReal);

            Assert.True(violaciones.Count == 0, $"Estos tipos de Infraestructura usan la elevación del alcance y no deben: {string.Join(", ", violaciones)}.");
        }

        [Theory]
        [Trait("Criterio", "AC-FUE-06")]
        [InlineData("Domain")]
        [InlineData("Application")]
        [InlineData("Api")]
        public void El_dominio_la_aplicacion_y_la_api_no_usan_la_elevacion(string capa)
        {
            var violaciones = ViolacionesEnOtrasCapas(Cargar($"{Servicio}.{capa}"), capa, InterfazReal);

            Assert.True(violaciones.Count == 0, $"Estos tipos de {capa} usan la elevación del alcance y no deben: {string.Join(", ", violaciones)}.");
        }

        // --- la regla se prueba a sí misma ---

        [Fact]
        [Trait("Criterio", "AC-FUE-06")]
        public void La_regla_detecta_a_Traslados_y_a_otro_adaptador_de_Plataforma_y_admite_los_permitidos()
        {
            var violaciones = ViolacionesEnInfraestructura(Pruebas, InterfazDeEjemplo);

            Assert.Contains($"{Servicio}.Infrastructure.Traslados.AdaptadorQueEleva", violaciones);
            Assert.Contains($"{Servicio}.Infrastructure.Plataforma.OtroAdaptadorDePlataforma", violaciones);
            Assert.DoesNotContain($"{Servicio}.Infrastructure.Fuentes.AdaptadorQueElevaPermitido", violaciones);
            Assert.DoesNotContain($"{Servicio}.Infrastructure.Reservas.AdaptadorDeReservasQueElevaPermitido", violaciones);
            Assert.DoesNotContain($"{Servicio}.Infrastructure.Plataforma.UnidadDeTrabajoPostgres", violaciones);
            Assert.Equal(2, violaciones.Count);
        }

        [Fact]
        [Trait("Criterio", "AC-FUE-06")]
        public void La_regla_detecta_a_la_aplicacion_y_a_la_api()
        {
            Assert.Contains($"{Servicio}.Application.Ajustes.CasoDeUsoQueEleva", ViolacionesEnOtrasCapas(Pruebas, "Application", InterfazDeEjemplo));
            Assert.Contains($"{Servicio}.Api.ControladorQueEleva", ViolacionesEnOtrasCapas(Pruebas, "Api", InterfazDeEjemplo));
        }
    }
}
