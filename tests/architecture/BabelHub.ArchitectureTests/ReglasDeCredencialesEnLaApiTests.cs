using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// El JWT y la renovación nunca llegan al navegador (AGENTS.md §3.9; identidad-seguridad IDE-33, AC-IDE-30): los endpoints JSON de Identidad no dependen de los contratos que emiten o manejan credenciales y renovaciones
/// (ingreso, sesiones, registro, recuperación, la cuenta) ni de sus modelos, y los cuerpos de <c>Shared</c> del E-commerce no tienen ningún campo de credencial, renovación, token o secreto. Estas pruebas son
/// <b>negativas</b>: agregar a la Api de Identidad un endpoint que devuelva una credencial las rompe, y quien lo haga debe discutirlo (el ingreso, la sesión y la cuenta se consumen en proceso por los contratos).
/// </summary>
public class ReglasDeCredencialesEnLaApiTests
{
    private const string EspacioDeLaApiDeIdentidad = "BabelHub.Ecommerce.Api.Identidad";

    private static readonly string[] Prohibidos =
    [
        "BabelHub.Ecommerce.Application.Identidad.IRegistroDeCuentas",
        "BabelHub.Ecommerce.Application.Identidad.IIngreso",
        "BabelHub.Ecommerce.Application.Identidad.ISesiones",
        "BabelHub.Ecommerce.Application.Identidad.IRecuperacionDeAcceso",
        "BabelHub.Ecommerce.Application.Identidad.ICuentaDelCliente",
        "BabelHub.Ecommerce.Application.Identidad.ICambioDeCorreo",
        "BabelHub.Ecommerce.Application.Identidad.IDispositivosDeLaCuenta",
        "BabelHub.Ecommerce.Application.Identidad.IContextoDeLectura",
        "BabelHub.Ecommerce.Application.Web.Interno.ISesionesDeIdentidad",
        "BabelHub.Ecommerce.Application.Web.Interno.IVerificadorDeCredencial",
        "BabelHub.Ecommerce.Application.Identidad.ISolicitudesDeDatos",
        "BabelHub.Ecommerce.Application.Identidad.ICancelacionDeCuenta",
        "BabelHub.Ecommerce.Application.Identidad.IAdministracionDeUsuarios",
        "BabelHub.Ecommerce.Application.Identidad.CredencialDeSesion",
        "BabelHub.Ecommerce.Application.Identidad.ResultadoDelIngreso",
        "BabelHub.Ecommerce.Application.Identidad.DesafioDelIngreso",
        "BabelHub.Ecommerce.Application.Identidad.SecretoDelSegundoFactor",
        "BabelHub.Ecommerce.Application.Identidad.Interno.IEmisorDeCredenciales",
        "BabelHub.Ecommerce.Infrastructure.Identidad.IClavePrivadaDeFirma",
        "BabelHub.Ecommerce.Infrastructure.Identidad.Es256EmisorDeCredenciales",
        "BabelHub.Ecommerce.Application.Plataforma.ValorSecreto",
    ];

    private static Assembly Cargar(string nombre) =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == nombre) ?? Assembly.Load(nombre);

    [Fact]
    public void Ningun_tipo_de_la_Api_depende_del_firmador_ni_del_verificador_del_contexto_de_lectura()
    {
        // El contexto del asistente (aud=pedidos) solo lo verifica Pedidos por el contrato de Identidad; ningún endpoint de la Api (escritura, biblioteca, cuenta, descarga) puede autenticar con él.
        var resultado = Types.InAssembly(Cargar("BabelHub.Ecommerce.Api"))
            .ShouldNot().HaveDependencyOnAny(
                "BabelHub.Ecommerce.Application.Identidad.IContextoDeLectura",
                "BabelHub.Ecommerce.Application.Identidad.Interno.IFirmaDeContextosDeLectura",
                "BabelHub.Ecommerce.Infrastructure.Identidad.Es256FirmaDeContextosDeLectura")
            .GetResult();

        Assert.True(resultado.IsSuccessful, $"Un tipo de la Api usa el contexto de lectura: {string.Join(", ", resultado.FailingTypeNames ?? [])}.");
    }



    [Fact]
    public void Solo_Identidad_y_Reclamaciones_usan_el_contrato_de_la_cuenta_por_correo_verificado()
    {
        // IClientePorCorreoVerificadoDeSistema resuelve un correo a una cuenta sin actor (IDE-99, AC-IDE-73): sería un oráculo de enumeración en cualquier otro módulo. Solo Reclamaciones (la vinculación inicial de las
        // hojas) lo usa; Identidad lo define, lo implementa y lo registra en su composición.
        const string Contrato = "BabelHub.Ecommerce.Application.Identidad.IClientePorCorreoVerificadoDeSistema";
        var enLaAplicacion = Types.InAssembly(Cargar("BabelHub.Ecommerce.Application"))
            .That().HaveDependencyOn(Contrato)
            .And().DoNotResideInNamespaceStartingWith("BabelHub.Ecommerce.Application.Identidad")
            .And().DoNotResideInNamespaceStartingWith("BabelHub.Ecommerce.Application.Reclamaciones")
            .GetTypes();
        var enLaInfraestructura = Types.InAssembly(Cargar("BabelHub.Ecommerce.Infrastructure"))
            .That().HaveDependencyOn(Contrato)
            .And().DoNotResideInNamespaceStartingWith("BabelHub.Ecommerce.Infrastructure.Reclamaciones")
            .And().DoNotResideInNamespaceStartingWith("BabelHub.Ecommerce.Infrastructure.Composicion")
            .GetTypes();

        Assert.Empty(enLaAplicacion);
        Assert.Empty(enLaInfraestructura);
    }
    [Fact]
    public void Ningun_tipo_de_la_Api_lee_el_correo_ni_el_contacto_de_una_cuenta_por_los_contratos_de_sistema()
    {
        // Los contratos de sistema de Identidad entregan un dato personal (el correo verificado, el número de WhatsApp) a partir de un identificador, sin comprobar titularidad: solo los consumen los módulos en proceso
        // (Reclamaciones y Notificaciones). Un controlador o un endpoint con un identificador de la ruta que los inyecte sería una fuga de datos personales (Ley 29733).
        var resultado = Types.InAssembly(Cargar("BabelHub.Ecommerce.Api"))
            .ShouldNot().HaveDependencyOnAny(
                "BabelHub.Ecommerce.Application.Identidad.ICorreoVerificadoDeSistema",
                "BabelHub.Ecommerce.Application.Identidad.IClientePorCorreoVerificadoDeSistema",
                "BabelHub.Ecommerce.Application.Identidad.IContactoDeAvisosDelCliente")
            .GetResult();

        Assert.True(resultado.IsSuccessful, $"Un tipo de la Api usa un contrato de sistema de Identidad: {string.Join(", ", resultado.FailingTypeNames ?? [])}.");
    }
    [Fact]
    public void Los_endpoints_de_Identidad_en_la_Api_no_dependen_de_ningun_contrato_que_emita_o_maneje_credenciales()
    {
        var resultado = Types.InAssembly(Cargar("BabelHub.Ecommerce.Api"))
            .That().ResideInNamespaceStartingWith(EspacioDeLaApiDeIdentidad)
            .ShouldNot().HaveDependencyOnAny(Prohibidos)
            .GetResult();

        Assert.True(resultado.IsSuccessful, $"Un endpoint de Identidad toca credenciales o renovaciones: {string.Join(", ", resultado.FailingTypeNames ?? [])}.");
    }

    [Fact]
    public void Los_endpoints_JSON_de_Identidad_son_una_lista_cerrada_la_de_las_claves()
    {
        var endpoints = Cargar("BabelHub.Ecommerce.Api").GetTypes()
            .Where(t => t.Namespace?.StartsWith(EspacioDeLaApiDeIdentidad, StringComparison.Ordinal) == true && t.Name.StartsWith("Endpoints", StringComparison.Ordinal))
            .Select(t => t.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        // Agregar un endpoint JSON de Identidad obliga a revisar esta prueba: el JWT y la renovación no llegan al navegador.
        Assert.Equal(["EndpointsDeClaves"], endpoints);
    }

    [Fact]
    public void Los_cuerpos_JSON_de_Identidad_y_los_de_Shared_del_Ecommerce_no_tienen_campos_de_credencial_renovacion_token_o_secreto()
    {
        var prohibidos = new Regex("credencial|renovacion|renovaci[oó]n|jwt|token|secret|contrasena|contraseña|privad|desafio|desaf[ií]o", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var dtosDeIdentidad = Cargar("BabelHub.Ecommerce.Api").GetTypes()
            .Where(t => t.Namespace == EspacioDeLaApiDeIdentidad && t.Name.EndsWith("Dto", StringComparison.Ordinal))
            .ToList();
        var dtosCompartidos = Cargar("BabelHub.Shared").GetTypes()
            .Where(t => t.Namespace == "BabelHub.Shared.Contratos.Ecommerce" && t.Name.EndsWith("Dto", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(dtosDeIdentidad);
        Assert.NotEmpty(dtosCompartidos);
        var campos = dtosDeIdentidad.Concat(dtosCompartidos).SelectMany(t => t.GetProperties().Select(p => $"{t.Name}.{p.Name}")).Where(n => prohibidos.IsMatch(n.Split('.')[1])).ToList();
        Assert.Empty(campos);
    }
}
