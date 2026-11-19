using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Domain.Identidad;
using BabelHub.Shared.Contratos;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// Reglas de <c>Identidad</c> que no caben en una prueba unitaria (<c>identidad-seguridad</c> T26 y T32): el Dominio y la Aplicación no referencian <c>Shared</c> (regla 6), así que las constantes del contexto de lectura y la
/// vigencia de la credencial se comparan aquí con <c>CredencialDeAcceso</c>, que es lo que leen el E-commerce y el Inventario.
/// </summary>
public class ReglasDeIdentidadTests
{
    [Fact]
    public void Las_constantes_del_contexto_de_lectura_son_las_del_contrato_compartido()
    {
        Assert.Equal(CredencialDeAcceso.AudienciaDePedidos, ContextoDeLecturaDelAsistente.AudienciaDePedidos);
        Assert.Equal(CredencialDeAcceso.TipoDelContextoDeLectura, ContextoDeLecturaDelAsistente.TipoDelContexto);
        Assert.Equal(CredencialDeAcceso.AlcanceDeLeerPedidos, ContextoDeLecturaDelAsistente.AlcanceDeLeerPedidos);
        Assert.Equal(TimeSpan.FromMinutes(CredencialDeAcceso.VigenciaDelContextoDeLecturaEnMinutos), ContextoDeLecturaDelAsistente.Vida);
    }

    [Fact]
    public void La_vigencia_de_la_credencial_y_el_algoritmo_son_los_del_contrato_compartido()
    {
        Assert.Equal(TimeSpan.FromMinutes(CredencialDeAcceso.VigenciaDeLaCredencialEnMinutos), ClaveDeFirma.VigenciaDeLaCredencial);
        Assert.Equal(CredencialDeAcceso.Algoritmo, ClavesDeFirma.AlgoritmoDeFirma);
    }
}
