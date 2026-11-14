using System.Reflection;
using BabelHub.Shared.Contratos;

namespace BabelHub.ArchitectureTests.Contratos;

/// <summary>
/// Los contratos congelados en <c>BabelHub.Shared</c> coinciden con lo que ya implementan los dos servicios (alcances de la credencial, cabeceras
/// y errores) y el usuario actual de prueba no entra en ningún proyecto de producción.
/// </summary>
public sealed class ConformidadConLosServiciosTests
{
    private static readonly string[] Servicios = ["Ecommerce", "Inventario"];

    public static TheoryData<string> NombresDeServicios => new(Servicios);

    private static Assembly Ensamblado(string servicio, string capa) => Assembly.Load($"BabelHub.{servicio}.{capa}");

    [Theory]
    [MemberData(nameof(NombresDeServicios))]
    [Trait("Criterio", "AC-SEG-14")]
    public void Los_alcances_de_la_Aplicacion_son_los_de_Shared(string servicio)
    {
        var tipoDeAlcance = Ensamblado(servicio, "Application").GetType($"BabelHub.{servicio}.Application.Plataforma.AlcanceDeServicio", throwOnError: true)!;
        var extensiones = Ensamblado(servicio, "Application").GetType($"BabelHub.{servicio}.Application.Plataforma.AlcancesDeServicio", throwOnError: true)!;
        var nombreEstable = extensiones.GetMethod("NombreEstable", BindingFlags.Public | BindingFlags.Static)!;

        var nombres = Enum.GetValues(tipoDeAlcance).Cast<object>().Select(a => (string)nombreEstable.Invoke(null, [a])!).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(AlcancesDeCredencial.Todos.Order(StringComparer.Ordinal).ToList(), nombres);
    }

    [Theory]
    [MemberData(nameof(NombresDeServicios))]
    public void Las_cabeceras_de_credencial_de_la_Infraestructura_y_de_la_Api_son_las_de_Shared(string servicio)
    {
        var salida = Ensamblado(servicio, "Infrastructure").GetType($"BabelHub.{servicio}.Infrastructure.Plataforma.CredencialesDeSalidaHandler", throwOnError: true)!;
        var entrada = Ensamblado(servicio, "Api").GetType($"BabelHub.{servicio}.Api.Plataforma.CredencialDeServicioMiddleware", throwOnError: true)!;

        foreach (var tipo in new[] { salida, entrada })
        {
            Assert.Equal(CabecerasDeServicio.CredencialDeServicio, Constante(tipo, "CabeceraDeCredencial"));
            Assert.Equal(CabecerasDeServicio.CredencialDeOrigen, Constante(tipo, "CabeceraDeCredencialDeOrigen"));
            Assert.Equal(CabecerasDeServicio.OrigenReenviado, Constante(tipo, "CabeceraDeOrigenReenviado"));
        }
    }

    [Theory]
    [MemberData(nameof(NombresDeServicios))]
    public void La_cabecera_de_seguimiento_de_los_errores_es_la_de_Shared(string servicio)
    {
        var errores = Ensamblado(servicio, "Api").GetTypes().First(t => t.GetField("CabeceraDeSeguimiento", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy) is not null);

        Assert.Equal(CabecerasDeServicio.Seguimiento, Constante(errores, "CabeceraDeSeguimiento"));
    }

    [Fact]
    public void Los_roles_de_los_contratos_son_los_cuatro_de_identidad_y_son_estables()
    {
        Assert.Equal(["cliente", "encargado-de-sucursal", "personal-de-logistica", "administrador-central"], RolesDeUsuario.Todos);
        Assert.Equal(4, RolesDeUsuario.Todos.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(NombresDeServicios))]
    public void El_usuario_actual_de_prueba_no_existe_en_ningun_proyecto_de_produccion(string servicio)
    {
        foreach (var capa in new[] { "Domain", "Application", "Infrastructure", "Api" })
        {
            var sospechosos = Ensamblado(servicio, capa).GetTypes()
                .Where(t => t.Name.Contains("DePrueba", StringComparison.Ordinal) || t.Name.Contains("ActorFalso", StringComparison.Ordinal))
                .Select(t => t.FullName)
                .ToList();

            Assert.True(sospechosos.Count == 0, $"{servicio}.{capa} tiene tipos de prueba en producción: {string.Join(", ", sospechosos)}.");
        }
    }

    [Theory]
    [MemberData(nameof(NombresDeServicios))]
    public void La_unica_implementacion_de_IActorActual_en_produccion_es_el_actor_del_flujo(string servicio)
    {
        var puerto = Ensamblado(servicio, "Application").GetType($"BabelHub.{servicio}.Application.Plataforma.IActorActual", throwOnError: true)!;

        var implementaciones = new[] { "Domain", "Application", "Infrastructure", "Api" }
            .SelectMany(capa => Ensamblado(servicio, capa).GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && puerto.IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        Assert.Equal(["ActorDelFlujo"], implementaciones);
    }

    private static string Constante(Type tipo, string nombre) =>
        (string)(tipo.GetField(nombre, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            ?? throw new InvalidOperationException($"{tipo.FullName} no tiene la constante pública {nombre}.")).GetRawConstantValue()!;
}
