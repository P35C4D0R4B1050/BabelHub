using System.Reflection;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Domain.Biblioteca;
using BabelHub.Ecommerce.UnitTests.Catalogo;

namespace BabelHub.Ecommerce.UnitTests.Biblioteca;

/// <summary>El contrato público de <c>Biblioteca</c> (T03; BIB-14): modelos propios, sin Dominio ni <c>Interno</c>, y sin URL ni clave del almacén en ningún modelo.</summary>
public sealed class ContratoPublicoDeBibliotecaTests
{
    private static readonly Assembly Aplicacion = typeof(IBibliotecaDelCliente).Assembly;

    private static IEnumerable<Type> TiposPublicos() =>
        Aplicacion.GetTypes().Where(t => t.IsPublic && t.Namespace == "BabelHub.Ecommerce.Application.Biblioteca");

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

    private static IEnumerable<Type> TiposQueExpone(Type tipo) =>
        tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.PropertyType)
            .Concat(tipo.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)))
            .Concat(tipo.GetConstructors().SelectMany(c => c.GetParameters().Select(p => p.ParameterType)))
            .SelectMany(Desplegar);

    [Fact]
    [Trait("Criterio", "AC-BIB-12")]
    public void Ningun_tipo_publico_de_la_raiz_expone_el_dominio_ni_el_interior()
    {
        var expuestos = TiposPublicos().SelectMany(TiposQueExpone).Distinct().ToArray();

        Assert.DoesNotContain(expuestos, t => t.Namespace is not null && (t.Namespace.StartsWith("BabelHub.Ecommerce.Domain", StringComparison.Ordinal) || t.Namespace.Contains(".Interno", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-12")]
    public void Ningun_modelo_del_contrato_guarda_la_clave_del_almacen_ni_una_url()
    {
        var campos = TiposPublicos().Where(t => t.IsClass && t != typeof(EnlaceDeDescarga)).SelectMany(t => t.GetProperties()).ToArray();

        Assert.DoesNotContain(campos, p => p.PropertyType == typeof(Uri) || p.Name.Contains("Clave", StringComparison.Ordinal) || p.Name.Contains("Url", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Correo", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-12")]
    public void El_enlace_de_descarga_no_imprime_la_url()
    {
        var enlace = new EnlaceDeDescarga(new Uri("https://almacen.prueba/libro?X-Amz-Signature=secreto"), DateTimeOffset.UnixEpoch);

        Assert.DoesNotContain("secreto", enlace.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("almacen.prueba", enlace.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-14")]
    public void Los_motivos_que_ofrece_la_interfaz_son_las_listas_cerradas_del_dominio()
    {
        Assert.Equal(Enum.GetValues<MotivoDeAyuda>().Select(m => m.Codigo()).Order(), MotivosDeBiblioteca.Ayuda.Order());
        Assert.Equal(Enum.GetValues<MotivoDeRestitucion>().Select(m => m.Codigo()).Order(), MotivosDeBiblioteca.Restitucion.Order());
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-21")]
    public void Cada_motivo_de_revocacion_del_contrato_tiene_su_causa_de_dominio()
    {
        foreach (var motivo in Enum.GetValues<MotivoDeRevocacionDeAcceso>())
        {
            var causa = BabelHub.Ecommerce.Application.Biblioteca.Interno.MapeoDeBiblioteca.Causa(motivo);
            Assert.True(Enum.IsDefined(causa));
        }

        Assert.Equal(CausaDeRevocacion.Reembolso, BabelHub.Ecommerce.Application.Biblioteca.Interno.MapeoDeBiblioteca.Causa(MotivoDeRevocacionDeAcceso.Reembolso));
    }
}
