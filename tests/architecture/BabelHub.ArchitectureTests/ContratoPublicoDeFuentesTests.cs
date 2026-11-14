using System.Reflection;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// El contrato público del módulo <c>Fuentes</c> (la raíz de su espacio de nombres en la Aplicación) lo usan <c>Reservas</c>, <c>Ajustes</c> y <c>Traslados</c>
/// (regla 13): sus modelos son propios, así que no puede exponer tipos del Dominio, de la Infraestructura, de <c>Shared</c> ni de su <c>Interno</c>
/// (inventario-fuentes T07). La regla mira lo que cada tipo público expone: base, interfaces, propiedades, campos, parámetros y retornos.
/// </summary>
public class ContratoPublicoDeFuentesTests
{
    private const string Raiz = "BabelHub.Inventario.Application.Fuentes";

    private static readonly string[] Prohibidos =
    [
        "BabelHub.Inventario.Domain",
        "BabelHub.Inventario.Infrastructure",
        "BabelHub.Shared",
        "BabelHub.Inventario.Application.Fuentes.Interno",
    ];

    private static readonly string[] Esperados =
    [
        "IDirectorioDeFuentes",
        "IFichasDeEdiciones",
        "IExistencias",
        "IMovimientosDeStock",
        "IAplicadorDeMovimientos",
        "IReservasDeStock",
        "IOrigenDeUnidadesVendidas",
        "IReingresosPorDevolucion",
    ];

    private static Assembly Aplicacion =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "BabelHub.Inventario.Application") ?? Assembly.Load("BabelHub.Inventario.Application");

    private static IEnumerable<Type> TiposDelContrato() =>
        Aplicacion.GetTypes().Where(t => t.IsPublic && t.Namespace == Raiz);

    /// <summary>Todos los tipos que aparecen en la superficie pública de un tipo (con sus argumentos genéricos).</summary>
    internal static IEnumerable<Type> TiposExpuestos(Type tipo)
    {
        var encontrados = new List<Type>();
        if (tipo.BaseType is not null)
        {
            encontrados.Add(tipo.BaseType);
        }

        encontrados.AddRange(tipo.GetInterfaces());

        const BindingFlags Publicos = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var propiedad in tipo.GetProperties(Publicos))
        {
            encontrados.Add(propiedad.PropertyType);
        }

        foreach (var campo in tipo.GetFields(Publicos))
        {
            encontrados.Add(campo.FieldType);
        }

        foreach (var metodo in tipo.GetMethods(Publicos).Cast<MethodBase>().Concat(tipo.GetConstructors(Publicos)))
        {
            encontrados.AddRange(metodo.GetParameters().Select(p => p.ParameterType));
            if (metodo is MethodInfo informacion)
            {
                encontrados.Add(informacion.ReturnType);
            }
        }

        return encontrados.SelectMany(Desplegar).Distinct();
    }

    private static IEnumerable<Type> Desplegar(Type tipo)
    {
        if (tipo.HasElementType && tipo.GetElementType() is { } elemento)
        {
            foreach (var interno in Desplegar(elemento))
            {
                yield return interno;
            }
        }

        if (tipo.IsGenericType)
        {
            yield return tipo.GetGenericTypeDefinition();
            foreach (var argumento in tipo.GetGenericArguments().SelectMany(Desplegar))
            {
                yield return argumento;
            }
        }
        else if (!tipo.HasElementType)
        {
            yield return tipo;
        }
    }

    internal static IReadOnlyList<string> Violaciones(IEnumerable<Type> tipos, string[] prohibidos) =>
        tipos
            .SelectMany(t => TiposExpuestos(t).Where(e => prohibidos.Any(p => (e.Namespace ?? string.Empty) == p || (e.Namespace ?? string.Empty).StartsWith(p + ".", StringComparison.Ordinal)))
                .Select(e => $"{t.FullName} expone {e.FullName}"))
            .ToList();

    [Fact]
    [Trait("Criterio", "AC-FUE-12")]
    public void El_contrato_publico_de_Fuentes_declara_los_puertos_y_servicios_del_plan()
    {
        var nombres = TiposDelContrato().Select(t => t.Name).ToHashSet();

        foreach (var esperado in Esperados)
        {
            Assert.Contains(esperado, nombres);
        }
    }

    [Fact]
    public void El_contrato_publico_de_Fuentes_no_expone_tipos_del_dominio_de_la_infraestructura_de_Shared_ni_de_su_interior()
    {
        var violaciones = Violaciones(TiposDelContrato(), Prohibidos);

        Assert.True(violaciones.Count == 0, $"El contrato de Fuentes expone tipos que los otros módulos no pueden usar: {string.Join("; ", violaciones)}.");
    }

    [Fact]
    public void El_contrato_publico_de_Fuentes_solo_tiene_interfaces_y_modelos_inmutables()
    {
        var mutables = TiposDelContrato()
            .Where(t => !t.IsInterface && !t.IsEnum)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.SetMethod is { IsPublic: true } set && !set.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit"))
                .Select(p => $"{t.FullName}.{p.Name}"))
            .ToList();

        Assert.True(mutables.Count == 0, $"Estas propiedades del contrato son mutables: {string.Join(", ", mutables)}.");
    }

    // --- la regla se prueba a sí misma ---

    [Fact]
    public void La_regla_detecta_un_tipo_del_dominio_en_la_firma_de_un_contrato()
    {
        var ejemplo = typeof(ContratoQueExponeElDominio);

        var violaciones = Violaciones([ejemplo], ["BabelHub.Inventario.Domain"]);

        Assert.Contains(violaciones, v => v.Contains("ExistenciaDeStock", StringComparison.Ordinal));
    }

    [Fact]
    public void La_regla_detecta_un_tipo_prohibido_dentro_de_un_generico()
    {
        var ejemplo = typeof(ContratoQueExponeUnaListaDelDominio);

        var violaciones = Violaciones([ejemplo], ["BabelHub.Inventario.Domain"]);

        Assert.Contains(violaciones, v => v.Contains("Fuente", StringComparison.Ordinal));
    }

    public interface ContratoQueExponeElDominio
    {
        BabelHub.Inventario.Domain.Fuentes.ExistenciaDeStock Leer();
    }

    public interface ContratoQueExponeUnaListaDelDominio
    {
        Task<IReadOnlyList<BabelHub.Inventario.Domain.Fuentes.Fuente>> Listar();
    }
}
