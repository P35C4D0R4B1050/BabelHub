using System.Reflection;
using NetArchTest.Rules;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// El token del usuario de la sesión (<c>ITokenDelUsuarioDeLaSesion</c>, D13 de <c>web-base</c>) solo lo consume el manejador de salida de
/// <c>Infrastructure.Plataforma</c>: ni la Aplicación de otros módulos, ni su Infraestructura, ni la Presentación lo piden (regla 9: el JWT no sale
/// del servidor y no se reparte entre quienes llaman).
/// </summary>
public sealed class ReglasDelTokenDelUsuarioTests
{
    private const string Puerto = "BabelHub.Ecommerce.Application.Plataforma.ITokenDelUsuarioDeLaSesion";

    private static Assembly Cargar(string nombre) =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == nombre) ?? Assembly.Load(nombre);

    private static IReadOnlyList<string> Dependientes(string ensamblado) =>
        Types.InAssembly(Cargar(ensamblado)).That().HaveDependencyOn(Puerto).GetTypes().Select(t => t.FullName!).Order(StringComparer.Ordinal).ToList();

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public void En_la_Infraestructura_solo_lo_consume_el_manejador_de_salida_de_Plataforma()
    {
        var dependientes = Dependientes("BabelHub.Ecommerce.Infrastructure");

        Assert.Equal(["BabelHub.Ecommerce.Infrastructure.Plataforma.CredencialesDeSalidaHandler"], dependientes);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public void En_la_Aplicacion_no_lo_consume_ningun_tipo_fuera_de_su_propia_declaracion()
    {
        var dependientes = Dependientes("BabelHub.Ecommerce.Application");

        Assert.All(dependientes, t => Assert.Equal(Puerto, t));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public void El_Dominio_no_lo_conoce()
    {
        Assert.Empty(Dependientes("BabelHub.Ecommerce.Domain"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public void En_la_Api_solo_lo_implementa_la_lectura_de_la_sesion_y_su_registro()
    {
        var dependientes = Dependientes("BabelHub.Ecommerce.Api");

        Assert.Equal(
            [
                "BabelHub.Ecommerce.Api.Web.HttpTokenDelUsuarioDeLaSesion",
                "BabelHub.Ecommerce.Api.Web.TokenDelUsuarioDeLaSesionExtensiones",
            ],
            dependientes);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public void El_texto_del_JWT_solo_lo_tocan_el_modulo_y_la_implementacion_del_token()
    {
        const string Credencial = "BabelHub.Ecommerce.Application.Web.CredencialEnMemoria";
        var enLaApi = Types.InAssembly(Cargar("BabelHub.Ecommerce.Api")).That().HaveDependencyOn(Credencial).GetTypes().Select(t => t.FullName!).Order(StringComparer.Ordinal).ToList();
        var enLaInfraestructura = Types.InAssembly(Cargar("BabelHub.Ecommerce.Infrastructure")).That().HaveDependencyOn(Credencial).GetTypes().Select(t => t.FullName!).ToList();

        Assert.Equal(["BabelHub.Ecommerce.Api.Web.HttpTokenDelUsuarioDeLaSesion"], enLaApi);
        Assert.Empty(enLaInfraestructura);
    }

    [Fact]
    public void Todo_controlador_de_una_carpeta_de_area_declara_su_area_para_que_el_mapeo_por_host_no_falle_abierto()
    {
        var controladores = Cargar("BabelHub.Ecommerce.Api").GetTypes()
            .Where(t => t.IsClass && typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t) && t.Namespace is not null && t.Namespace.Contains(".Areas.", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(controladores);
        foreach (var tipo in controladores)
        {
            var carpeta = tipo.Namespace!.Split(".Areas.")[1].Split('.')[0];
            var area = tipo.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.AreaAttribute), inherit: true).Cast<Microsoft.AspNetCore.Mvc.AreaAttribute>().SingleOrDefault();
            Assert.True(area is not null && area.RouteValue == carpeta, $"{tipo.FullName} debe declarar [Area(\"{carpeta}\")].");
        }
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public void La_unica_implementacion_en_produccion_es_la_de_la_sesion_de_la_Api()
    {
        var implementaciones = new[] { "BabelHub.Ecommerce.Api", "BabelHub.Ecommerce.Infrastructure", "BabelHub.Ecommerce.Application", "BabelHub.Ecommerce.Domain" }
            .SelectMany(a => Cargar(a).GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.GetInterfaces().Any(i => i.FullName == Puerto))
            .Select(t => t.FullName)
            .ToList();

        Assert.Equal(["BabelHub.Ecommerce.Api.Web.HttpTokenDelUsuarioDeLaSesion"], implementaciones);
    }
}
