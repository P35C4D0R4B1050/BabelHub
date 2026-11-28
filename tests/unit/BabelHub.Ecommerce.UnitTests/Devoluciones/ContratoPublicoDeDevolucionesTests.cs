using System.Reflection;
using System.Text.Json;
using BabelHub.Ecommerce.Application.Devoluciones;

namespace BabelHub.Ecommerce.UnitTests.Devoluciones;

/// <summary>El contrato público de <c>Devoluciones</c> (T04; DEV-28, DEV-29, DEV-31): modelos propios, sin Dominio ni <c>Interno</c>, sin responsable de un cambio y con el texto libre redactado.</summary>
public sealed class ContratoPublicoDeDevolucionesTests
{
    private static readonly Assembly Aplicacion = typeof(IDevolucionesDelCliente).Assembly;

    private static IEnumerable<Type> TiposPublicos() =>
        Aplicacion.GetTypes().Where(t => t.IsPublic && t.Namespace == "BabelHub.Ecommerce.Application.Devoluciones");

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

    private static IEnumerable<Type> TiposQueExpone(Type tipo)
    {
        var miembros = tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.PropertyType)
            .Concat(tipo.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)))
            .Concat(tipo.GetConstructors().SelectMany(c => c.GetParameters().Select(p => p.ParameterType)));
        return miembros.SelectMany(Desplegar);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-12")]
    public void Ningun_modelo_del_contrato_lleva_el_responsable_de_un_cambio_ni_la_cuenta()
    {
        var campos = TiposPublicos().Where(t => t.IsClass).SelectMany(t => t.GetProperties()).Select(p => p.Name).ToArray();

        Assert.DoesNotContain(campos, c => c.Contains("Responsable", StringComparison.Ordinal) || c.Contains("Cuenta", StringComparison.Ordinal) || c.Contains("Usuario", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-31")]
    public void El_texto_libre_no_se_vuelca_en_ToString_ni_en_la_serializacion_de_ningun_modelo()
    {
        var texto = new TextoLibre("Mi dirección es Jr. Centinela 4321");
        var vista = new DevolucionVista(Guid.NewGuid(), Guid.NewGuid(), TipoDeLaDevolucion.Impreso, OrigenDeLaDevolucion.Solicitud, "lima-central", texto, null, false, DateTimeOffset.UtcNow, []);
        var entrada = new SolicitarDevolucionDeImpresoEntrada(Guid.NewGuid(), [new LineaPedidaParaDevolver(Guid.NewGuid(), 1)], texto, "lima-central");

        foreach (var observado in new[] { texto.ToString(), vista.ToString(), entrada.ToString(), JsonSerializer.Serialize(texto), JsonSerializer.Serialize(vista), JsonSerializer.Serialize(entrada) })
        {
            Assert.DoesNotContain("Centinela", observado, StringComparison.Ordinal);
        }

        Assert.Equal("[redactado]", texto.ToString());
        Assert.Equal("Mi dirección es Jr. Centinela 4321", texto.Valor);
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<TextoLibre>("\"hola\""));
    }
}
