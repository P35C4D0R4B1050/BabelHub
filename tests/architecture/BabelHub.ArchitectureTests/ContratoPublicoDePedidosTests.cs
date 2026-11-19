using System.Reflection;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Pedidos;
using NetArchTest.Rules;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// El contrato público del módulo <c>Pedidos</c> (la raíz de su espacio de nombres en la Aplicación; <c>ecommerce-pedidos</c> T07) lo usan la web, el asistente, Envíos y el Inventario (regla 13): modelos propios, ningún tipo del Dominio, de la Infraestructura, de <c>Shared</c> ni
/// de su <c>Interno</c>, solo interfaces, enumeraciones y modelos inmutables. Los DTO son distintos por quien los ve (PED-40): el encargado de sucursal, los listados y el asistente reciben tipos <b>sin contacto</b>.
/// </summary>
public class ContratoPublicoDePedidosTests
{
    private const string Raiz = "BabelHub.Ecommerce.Application.Pedidos";

    private static readonly string[] Prohibidos =
    [
        "BabelHub.Ecommerce.Domain",
        "BabelHub.Ecommerce.Infrastructure",
        "BabelHub.Shared",
        "BabelHub.Ecommerce.Application.Pedidos.Interno",
    ];

    private static readonly string[] Esperados =
    [
        "IPedidosDelCliente",
        "IGestionDeRecojo",
        "IGestionDeDespacho",
        "IAdministracionDePedidos",
        "IPedidosParaElAsistente",
        "IReceptorDeEstadosDeTraslado",
        "IDespachoDeEnvios",
        "IProgresoDeEnvios",
        "IContactoDeEntregaDelPedido",
        "IPedidosParaEnvios",
        "IPedidosParaDevoluciones",
        "DetalleDelPedidoDelCliente",
        "DetalleDelPedidoDePersonal",
        "DetalleDelPedidoDeSucursal",
    ];

    private static Assembly Aplicacion => typeof(IPedidosDelCliente).Assembly;

    private static IEnumerable<Type> TiposDelContrato() => Aplicacion.GetTypes().Where(t => t.IsPublic && t.Namespace == Raiz);

    [Fact]
    public void El_contrato_publico_de_Pedidos_declara_las_interfaces_y_modelos_del_plan()
    {
        var nombres = TiposDelContrato().Select(t => t.Name).ToHashSet();

        foreach (var esperado in Esperados)
        {
            Assert.Contains(esperado, nombres);
        }

        Assert.Contains(typeof(IAccesosDeBiblioteca).Name, Aplicacion.GetTypes().Where(t => t.IsPublic && t.Namespace == "BabelHub.Ecommerce.Application.Biblioteca").Select(t => t.Name));
    }

    [Fact]
    public void El_contrato_publico_de_Pedidos_no_expone_tipos_del_dominio_de_la_infraestructura_de_Shared_ni_de_su_interior()
    {
        var violaciones = ContratoPublicoDeFuentesTests.Violaciones(TiposDelContrato(), Prohibidos);

        Assert.True(violaciones.Count == 0, $"El contrato de Pedidos expone tipos que los otros módulos no pueden usar: {string.Join("; ", violaciones)}.");
    }

    [Fact]
    public void El_contrato_publico_de_Pedidos_solo_tiene_interfaces_enumeraciones_y_modelos_inmutables()
    {
        var mutables = TiposDelContrato()
            .Where(t => !t.IsInterface && !t.IsEnum)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.SetMethod is { IsPublic: true } set && !set.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit"))
                .Select(p => $"{t.FullName}.{p.Name}"))
            .ToList();

        Assert.True(mutables.Count == 0, $"Estas propiedades del contrato son mutables: {string.Join(", ", mutables)}.");
    }

    [Fact]
    public void Los_puertos_con_tipos_del_dominio_viven_en_el_interior_del_modulo_y_no_en_la_raiz()
    {
        var interno = Aplicacion.GetTypes().Where(t => t.IsPublic && t.Namespace == $"{Raiz}.Interno").Select(t => t.Name).ToHashSet();

        foreach (var puerto in new[] { "IRepositorioDePedidos", "IReservasDelPedido", "IResolucionDeSucursal", "IGeneradorDeNumerosDePedido" })
        {
            Assert.Contains(puerto, interno);
            Assert.DoesNotContain(puerto, TiposDelContrato().Select(t => t.Name));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PED-34")]
    public void El_encargado_los_listados_y_el_asistente_reciben_tipos_sin_contacto()
    {
        var sinContacto = new[] { typeof(DetalleDelPedidoDeSucursal), typeof(PedidoParaElAsistente), typeof(ResumenDePedido), typeof(PaginaDePedidos), typeof(LineaVisibleDelPedido), typeof(LineaParaElAsistente), typeof(LineasOperadas) };

        foreach (var tipo in sinContacto)
        {
            var conContacto = tipo.GetProperties().Where(p => TocaElContacto(p.PropertyType)).Select(p => $"{tipo.Name}.{p.Name}").ToList();
            Assert.True(conContacto.Count == 0, $"{tipo.Name} no puede llevar el contacto de entrega: {string.Join(", ", conContacto)}.");
            var camposPersonales = tipo.GetProperties().Where(p => p.Name is "Telefono" or "Direccion" or "Nombre" or "Contacto").Select(p => p.Name).ToList();
            Assert.True(camposPersonales.Count == 0, $"{tipo.Name} tiene campos de contacto por nombre: {string.Join(", ", camposPersonales)}.");
        }

        Assert.Contains(typeof(DetalleDelPedidoDelCliente).GetProperties(), p => p.PropertyType == typeof(ContactoDeEntrega));
        Assert.Contains(typeof(DetalleDelPedidoDePersonal).GetProperties(), p => p.PropertyType == typeof(ContactoDeEntrega));
    }

    private static bool TocaElContacto(Type tipo) =>
        tipo == typeof(ContactoDeEntrega) || (tipo.IsGenericType && tipo.GetGenericArguments().Any(TocaElContacto)) || (tipo.IsArray && tipo.GetElementType() is { } e && TocaElContacto(e));
}
