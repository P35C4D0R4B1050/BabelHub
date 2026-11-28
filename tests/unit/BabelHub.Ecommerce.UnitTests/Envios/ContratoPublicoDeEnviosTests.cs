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
    public void Ningun_modelo_del_contrato_tiene_un_campo_de_contacto()
    {
        var campos = TiposPublicos().Where(t => t.IsClass).SelectMany(t => t.GetProperties()).Select(p => p.Name).ToArray();

        Assert.DoesNotContain(campos, c => c is "Nombre" or "Direccion" or "Telefono" or "Correo" or "Documento" or "Destinatario" or "Contacto");
    }
}
