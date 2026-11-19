using System.Reflection;
using BabelHub.Ecommerce.Application.Envios;

namespace BabelHub.Ecommerce.UnitTests.Envios;

/// <summary>El contrato público de <c>Envios</c> (T03; ENV-15): modelos propios, sin Dominio ni <c>Interno</c>, y sin ningún dato de contacto.</summary>
public sealed class ContratoPublicoDeEnviosTests
{
    private static readonly Assembly Aplicacion = typeof(IEnviosDeLogistica).Assembly;

    private static IEnumerable<Type> TiposPublicos() =>
        Aplicacion.GetTypes().Where(t => t.IsPublic && t.Namespace == "BabelHub.Ecommerce.Application.Envios");

    private static IEnumerable<Type> TiposQueExpone(Type tipo)
    {
        var miembros = tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.PropertyType)
            .Concat(tipo.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)))
            .Concat(tipo.GetConstructors().SelectMany(c => c.GetParameters().Select(p => p.ParameterType)));
        return miembros.SelectMany(Desplegar);
    }

    private static IEnumerable<Type> Desplegar(Type tipo)
    {
        yield return tipo;
        foreach (var argumento in tipo.IsGenericType ? tipo.GetGenericArguments() : [])
        {
            foreach (var interno in Desplegar(argumento))
            {
                yield return interno;
            }
        }

        if (tipo.HasElementType)
        {
            foreach (var interno in Desplegar(tipo.GetElementType()!))
            {
                yield return interno;
            }
        }
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-17")]
    public void La_raiz_del_modulo_expone_los_contratos_de_logistica_y_del_cliente()
    {
        var nombres = TiposPublicos().Select(t => t.Name).ToHashSet();

        Assert.Contains(nameof(IEnviosDeLogistica), nombres);
        Assert.Contains(nameof(IEnviosDelCliente), nombres);
        Assert.Contains(nameof(DetalleDeEnvio), nombres);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-17")]
    public void Ningun_tipo_publico_de_la_raiz_expone_el_dominio_ni_el_interior()
    {
        var expuestos = TiposPublicos().SelectMany(TiposQueExpone).Distinct().ToArray();

        Assert.DoesNotContain(expuestos, t => t.Namespace is not null && (t.Namespace.StartsWith("BabelHub.Ecommerce.Domain", StringComparison.Ordinal) || t.Namespace.Contains(".Interno", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-17")]
    public void Ningun_modelo_del_contrato_tiene_un_campo_de_contacto()
    {
        var campos = TiposPublicos().Where(t => t.IsClass).SelectMany(t => t.GetProperties()).Select(p => p.Name).ToArray();

        Assert.DoesNotContain(campos, c => c is "Nombre" or "Direccion" or "Telefono" or "Correo" or "Documento" or "Destinatario" or "Contacto");
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-17")]
    public void Ni_el_dominio_ni_el_interior_de_envios_guardan_campos_de_contacto_salvo_el_destinatario_de_la_guia()
    {
        var prohibidos = new[] { "Nombre", "Direccion", "Telefono", "Correo", "Documento", "Contacto" };
        var tipos = typeof(BabelHub.Ecommerce.Domain.Envios.Envio).Assembly.GetTypes().Where(t => t.Namespace == "BabelHub.Ecommerce.Domain.Envios" && t != typeof(BabelHub.Ecommerce.Domain.Envios.DestinatarioParaGuia) && t != typeof(BabelHub.Ecommerce.Domain.Envios.ResultadoDeValidacionDelDestinatario))
            .Concat(Aplicacion.GetTypes().Where(t => t.Namespace == "BabelHub.Ecommerce.Application.Envios.Interno"));

        var campos = tipos.Where(t => !t.IsEnum && t.Name is not ("IOperadorLogistico" or "ClavesDeEnvios")).SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Select(p => p.Name)
            .Concat(t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Select(f => f.Name))).ToArray();

        Assert.DoesNotContain(campos.Where(c => !c.StartsWith('<')), c => prohibidos.Any(p => c.Contains(p, StringComparison.OrdinalIgnoreCase)));
    }
}
