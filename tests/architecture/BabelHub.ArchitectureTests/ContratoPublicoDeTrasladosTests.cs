using System.Reflection;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// T25: el contrato público del módulo <c>Traslados</c> (la raíz de su espacio de nombres en la Aplicación) no expone el Dominio, la Infraestructura, <c>Shared</c> ni su propio <c>Interno</c> (regla 13); <c>Traslados</c> solo usa la
/// raíz de <c>Fuentes</c> y de <c>Reservas</c>; las implementaciones de los puertos de <c>Reservas</c> no dependen entre sí, ni de la entrega de mensajes ni de las piezas de stock y reservas (no cierran el ciclo de inyección) y ningún
/// caso de uso del sistema (los puertos y los trabajos) es alcanzable como caso de uso expuesto.
/// </summary>
public class ContratoPublicoDeTrasladosTests
{
    private const string Raiz = "BabelHub.Inventario.Application.Traslados";

    private const string Interno = Raiz + ".Interno";

    private static readonly string[] Prohibidos =
    [
        "BabelHub.Inventario.Domain",
        "BabelHub.Inventario.Infrastructure",
        "BabelHub.Shared",
        Interno,
    ];

    private static Assembly Aplicacion =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "BabelHub.Inventario.Application") ?? Assembly.Load("BabelHub.Inventario.Application");

    private static IEnumerable<Type> TiposDelContrato() => Aplicacion.GetTypes().Where(t => t.IsPublic && t.Namespace == Raiz);

    private static Type Interna(string nombre) => Aplicacion.GetTypes().Single(t => t.Namespace == Interno && t.Name == nombre);

    private static IEnumerable<Type> ParametrosDeConstructor(Type tipo) => tipo.GetConstructors().SelectMany(c => c.GetParameters().Select(p => p.ParameterType));

    [Fact]
    public void El_contrato_publico_de_Traslados_declara_las_entradas_y_salidas_del_plan()
    {
        var nombres = TiposDelContrato().Select(t => t.Name).ToHashSet();

        foreach (var esperado in new[]
        {
            "SolicitarTrasladoEntrada", "AprobarTrasladoEntrada", "RechazarTrasladoEntrada", "CancelarTrasladoEntrada", "DespacharTrasladoEntrada", "RecibirTrasladoEntrada", "RegistrarGuiaDeTrasladoEntrada",
            "ConsultarOrigenesPosiblesEntrada", "ConsultarTrasladoEntrada", "ListarTrasladosEntrada", "ConsultarTrasladosDeUnPedidoEntrada", "TrasladoRegistrado", "PaginaDeTraslados",
        })
        {
            Assert.Contains(esperado, nombres);
        }
    }

    [Fact]
    public void El_contrato_publico_de_Traslados_no_expone_el_dominio_la_infraestructura_Shared_ni_su_interior()
    {
        var violaciones = ContratoPublicoDeFuentesTests.Violaciones(TiposDelContrato(), Prohibidos);

        Assert.True(violaciones.Count == 0, $"El contrato de Traslados expone tipos que los otros módulos no pueden usar: {string.Join("; ", violaciones)}.");
    }

    [Fact]
    public void El_contrato_publico_de_Traslados_solo_tiene_enumeraciones_y_modelos_inmutables_o_ayudantes_estaticos()
    {
        var mutables = TiposDelContrato()
            .Where(t => !t.IsInterface && !t.IsEnum && !(t.IsAbstract && t.IsSealed))
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.SetMethod is { IsPublic: true } set && !set.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit"))
                .Select(p => $"{t.FullName}.{p.Name}"))
            .ToList();

        Assert.True(mutables.Count == 0, $"Hay modelos con propiedades mutables: {string.Join(", ", mutables)}.");
    }

    [Fact]
    public void El_interior_de_Traslados_no_usa_el_dominio_ni_el_interior_de_Fuentes_o_Reservas()
    {
        var prohibidos = new[] { "BabelHub.Inventario.Domain.Fuentes", "BabelHub.Inventario.Domain.Reservas", "BabelHub.Inventario.Domain.Ajustes", "BabelHub.Inventario.Application.Fuentes.Interno", "BabelHub.Inventario.Application.Reservas.Interno", "BabelHub.Inventario.Application.Ajustes" };
        var tipos = Aplicacion.GetTypes().Where(t => t.Namespace is Raiz or Interno);

        var violaciones = tipos
            .SelectMany(t => t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SelectMany(c => c.GetParameters().Select(p => p.ParameterType))
                .Concat(t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Select(f => f.FieldType))
                .Concat(t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)))
                .Where(tipo => tipo.Namespace is { } espacio && prohibidos.Any(p => espacio == p || espacio.StartsWith(p + ".", StringComparison.Ordinal)))
                .Select(tipo => $"{t.FullName} usa {tipo.FullName}"))
            .Distinct()
            .ToList();

        Assert.True(violaciones.Count == 0, $"Traslados solo usa la raíz de Fuentes y de Reservas: {string.Join("; ", violaciones)}.");
    }

    [Fact]
    public void Las_implementaciones_de_los_puertos_de_Reservas_no_dependen_entre_si_ni_de_las_piezas_de_stock_y_reservas()
    {
        var puertos = new[] { Interna("GenerarTrasladosDePedido"), Interna("CancelarTrasladosDePedido") };
        var prohibidos = new[]
        {
            typeof(BabelHub.Inventario.Application.Fuentes.IMovimientosDeStock), typeof(BabelHub.Inventario.Application.Fuentes.IAplicadorDeMovimientos), typeof(BabelHub.Inventario.Application.Fuentes.IExistencias),
            typeof(BabelHub.Inventario.Application.Reservas.IReservasParaTraslados), Interna("AutorizadorDeTraslados"), Interna("EjecutorDeTransiciones"), Interna("EntregarMensajesSalientes"),
            Interna("GenerarTrasladosDePedido"), Interna("CancelarTrasladosDePedido"),
        };

        var violaciones = puertos.SelectMany(p => ParametrosDeConstructor(p).Where(d => prohibidos.Contains(d)).Select(d => $"{p.Name} depende de {d.Name}")).ToList();

        Assert.True(violaciones.Count == 0, $"Los puertos de Reservas implementados por Traslados no deben cerrar el ciclo de inyección: {string.Join("; ", violaciones)}.");
    }

    [Fact]
    public void Ninguna_fachada_ITraslados_existe_y_ningun_caso_de_uso_del_sistema_se_expone_como_caso_de_uso()
    {
        Assert.DoesNotContain(Aplicacion.GetTypes(), t => t.Name == "ITraslados");

        var delSistema = new[] { "GenerarTrasladosDePedido", "CancelarTrasladosDePedido" }.Select(Interna);
        var expuestos = delSistema.Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition().Name.StartsWith("ICasoDeUso", StringComparison.Ordinal))).Select(t => t.Name).ToList();
        Assert.True(expuestos.Count == 0, $"Estos casos de uso de los puertos de Reservas no deben ser ICasoDeUso: {string.Join(", ", expuestos)}.");

        Assert.Contains(typeof(BabelHub.Inventario.Application.Reservas.IGeneradorDeTrasladosDePedido), Interna("GenerarTrasladosDePedido").GetInterfaces());
        Assert.Contains(typeof(BabelHub.Inventario.Application.Reservas.IAlLiberarPedido), Interna("CancelarTrasladosDePedido").GetInterfaces());
    }

    [Fact]
    public void Todo_el_interior_de_Traslados_es_interno_salvo_las_excepciones_de_infraestructura()
    {
        var publicos = Aplicacion.GetTypes().Where(t => t.Namespace == Interno && t.IsPublic && !t.IsNested).Select(t => t.Name).ToList();

        Assert.True(publicos.Count == 0, $"El interior de Traslados no es parte del contrato: {string.Join(", ", publicos)}.");
    }
}
