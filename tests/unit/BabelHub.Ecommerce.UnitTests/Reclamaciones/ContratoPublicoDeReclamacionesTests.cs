using System.Reflection;
using BabelHub.Ecommerce.Application.Reclamaciones;

namespace BabelHub.Ecommerce.UnitTests.Reclamaciones;

/// <summary>El contrato público de <c>Reclamaciones</c> (T03; REC-02): modelos propios, sin Dominio ni <c>Interno</c>, y con los datos personales redactados en su texto.</summary>
public sealed class ContratoPublicoDeReclamacionesTests
{
    private static readonly Assembly Aplicacion = typeof(IReclamaciones).Assembly;

    private static IEnumerable<Type> TiposPublicos() =>
        Aplicacion.GetTypes().Where(t => t.IsPublic && t.Namespace == "BabelHub.Ecommerce.Application.Reclamaciones");

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
    [Trait("Criterio", "AC-REC-02")]
    public void Ningun_tipo_publico_de_la_raiz_expone_el_dominio_ni_el_interior()
    {
        var expuestos = TiposPublicos().SelectMany(TiposQueExpone).Distinct().ToArray();

        Assert.DoesNotContain(expuestos, t => t.Namespace is not null && (t.Namespace.StartsWith("BabelHub.Ecommerce.Domain", StringComparison.Ordinal) || t.Namespace.Contains(".Interno", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-02")]
    public void Los_modelos_con_datos_personales_no_los_muestran_en_su_texto()
    {
        var entrada = new RegistrarHojaEntrada("Ana", "12345678", "Calle Larga 1", "ana@correo.com", "Libro", 10m, "reclamo", "Detalle secreto", "PED-1", "clave", "token");
        var constancia = new ConstanciaDeHoja("2026-000001", DateTimeOffset.UnixEpoch, "reclamo", "Libro", 10m, "Detalle secreto", null, "Ana", "12345678", "Calle Larga 1", "ana@correo.com", 15, new DateOnly(2026, 12, 3));
        var respuesta = new ResponderHojaEntrada(Guid.NewGuid(), "Respuesta para Ana");

        var texto = entrada + " " + constancia + " " + respuesta;

        foreach (var secreto in new[] { "Ana", "12345678", "Calle Larga", "ana@correo.com", "Detalle secreto", "token", "clave" })
        {
            Assert.DoesNotContain(secreto, texto);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-REC-10")]
    public void El_resumen_del_listado_no_tiene_ningun_campo_personal()
    {
        var campos = typeof(ResumenDeHoja).GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain(campos, c => c is "Nombre" or "Documento" or "Domicilio" or "Correo" or "Detalle" or "Respuesta");
    }
}
