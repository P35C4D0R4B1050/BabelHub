using System.Reflection;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Plataforma;

/// <summary>
/// El catálogo de políticas de Plataforma cubre todas las que el código pide: una clave que ningún catálogo conoce hace fallar cerrada toda operación que la usa (en el host real, un 503 de «servicio no disponible»).
/// La prueba recorre por reflexión todo campo o propiedad estático de tipo <see cref="ClaveDePolitica"/> de la Aplicación (también los de clases internas de los módulos) y exige que cada uno esté en el catálogo del Dominio,
/// y que cada política del catálogo tenga su clave tipada y su motivo en el catálogo de eventos de seguridad (AUD-28).
/// </summary>
public sealed class CatalogoDePoliticasCubreElCodigoTests
{
    private static IEnumerable<(string Declarante, ClaveDePolitica Clave)> ClavesQueElCodigoPide()
    {
        const BindingFlags Estaticos = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var tipo in typeof(ClaveDePolitica).Assembly.GetTypes())
        {
            foreach (var campo in tipo.GetFields(Estaticos).Where(c => c.FieldType == typeof(ClaveDePolitica)))
            {
                yield return ($"{tipo.FullName}.{campo.Name}", (ClaveDePolitica)campo.GetValue(null)!);
            }

            foreach (var propiedad in tipo.GetProperties(Estaticos).Where(p => p.PropertyType == typeof(ClaveDePolitica) && p.GetIndexParameters().Length == 0))
            {
                yield return ($"{tipo.FullName}.{propiedad.Name}", (ClaveDePolitica)propiedad.GetValue(null)!);
            }
        }
    }

    [Fact]
    public void Toda_clave_de_politica_que_pide_la_Aplicacion_esta_en_el_catalogo_de_Plataforma()
    {
        var catalogo = PoliticasDeSeguridad.Todas.Concat(PoliticasDeSeguridad.Generales).Select(p => p.Nombre).ToHashSet(StringComparer.Ordinal);
        var pedidas = ClavesQueElCodigoPide().ToList();

        // La búsqueda tiene que encontrar algo: si la reflexión dejara de ver las claves, la prueba no probaría nada.
        Assert.True(pedidas.Count >= 36, $"Se esperaban al menos 36 claves y se encontraron {pedidas.Count}.");
        Assert.Contains(pedidas, p => p.Clave.Valor == "consultas-de-devoluciones-por-usuario");
        Assert.Contains(pedidas, p => p.Clave.Valor == "operaciones-de-devoluciones-por-usuario");
        var faltantes = pedidas.Where(p => !catalogo.Contains(p.Clave.Valor)).Select(p => $"{p.Clave.Valor} (en {p.Declarante})").ToList();
        Assert.True(faltantes.Count == 0, "Claves de política sin alta en el catálogo de Plataforma: " + string.Join("; ", faltantes));
    }

    [Fact]
    public void Toda_politica_del_catalogo_tiene_su_clave_tipada_y_su_motivo_de_limite_excedido()
    {
        var tipadas = ClavesQueElCodigoPide().Select(p => p.Clave.Valor).ToHashSet(StringComparer.Ordinal);
        var enElCatalogo = PoliticasDeSeguridad.Todas.Concat(PoliticasDeSeguridad.Generales).Select(p => p.Nombre).ToList();

        Assert.Equal(enElCatalogo.Count, enElCatalogo.Distinct(StringComparer.Ordinal).Count());
        var sinClave = enElCatalogo.Where(nombre => !tipadas.Contains(nombre)).ToList();
        var sinMotivo = enElCatalogo.Where(nombre => !CatalogoDeAccionesDeAuditoria.MotivosDeLimiteExcedido.Contains(nombre)).ToList();
        Assert.True(sinClave.Count == 0, "Políticas del catálogo sin clave tipada: " + string.Join(", ", sinClave));
        Assert.True(sinMotivo.Count == 0, "Políticas del catálogo sin motivo de límite excedido: " + string.Join(", ", sinMotivo));
    }

    [Fact]
    public void Las_politicas_de_devoluciones_tienen_los_valores_del_plan_y_son_generales()
    {
        var consultas = PoliticasDeSeguridad.ConsultasDeDevolucionesPorUsuario;
        var operaciones = PoliticasDeSeguridad.OperacionesDeDevolucionesPorUsuario;

        Assert.Equal(("consultas-de-devoluciones-por-usuario", 60, TimeSpan.FromMinutes(1)), (consultas.Nombre, consultas.Maximo, consultas.Ventana));
        Assert.Equal(("operaciones-de-devoluciones-por-usuario", 30, TimeSpan.FromMinutes(1)), (operaciones.Nombre, operaciones.Maximo, operaciones.Ventana));
        Assert.All(new[] { consultas, operaciones }, p =>
        {
            Assert.Equal(TipoDeSujetoDeLimite.Usuario, p.Tipo);
            Assert.True(p.RespaldoLocal);
            Assert.Contains(p, PoliticasDeSeguridad.Generales);
            Assert.Contains(p.Nombre, CatalogoDeAccionesDeAuditoria.MotivosDeLimiteExcedido);
        });
        Assert.Equal(consultas.Nombre, PoliticasDeLimite.ConsultasDeDevolucionesPorUsuario.Valor);
        Assert.Equal(operaciones.Nombre, PoliticasDeLimite.OperacionesDeDevolucionesPorUsuario.Valor);
    }
}
