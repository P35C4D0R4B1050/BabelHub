using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.Infrastructure.Composicion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Fuentes;

/// <summary>T06 de <c>inventario-seleccion-fuente</c>: la estrategia de selección se elige por nombre en la configuración; un nombre desconocido hace fallar el arranque y otra estrategia sustituye a la predeterminada sin cambiar el contrato.</summary>
public sealed class EstrategiaDeSeleccionConfiguradaTests
{
    private sealed class SiempreLaDeMayorVendible : IEstrategiaDeSeleccion
    {
        public string Nombre => "mayor-vendible";

        public Seleccion Seleccionar(SolicitudDeSeleccion solicitud)
        {
            var elegida = solicitud.Candidatas.OrderByDescending(c => c.Vendible).First();
            return new Seleccion(elegida.FuenteId, elegida.Codigo, false, null, 0);
        }
    }

    private static IConfiguration Configuracion(string? estrategia) =>
        new ConfigurationBuilder().AddInMemoryCollection(estrategia is null ? [] : new Dictionary<string, string?> { ["Inventario:EstrategiaDeSeleccion"] = estrategia }).Build();

    private static ServiceProvider Construir(string? estrategia) =>
        new ServiceCollection().AgregarEstrategiasDeSeleccion(Configuracion(estrategia), [new SiempreLaDeMayorVendible()]).BuildServiceProvider();

    private static readonly SolicitudDeSeleccion Solicitud = new(
        TipoDeEntrega.Recojo,
        1,
        [new CandidataDeFuente(Guid.NewGuid(), "ayacucho", 5, true, false, 0), new CandidataDeFuente(Guid.NewGuid(), "lima", 50, false, false, 4)]);

    [Fact]
    [Trait("Criterio", "AC-SEL-10")]
    public void Sin_configuracion_rige_la_estrategia_de_cercania()
    {
        using var proveedor = Construir(null);

        var activa = ComposicionDeReservas.ExigirEstrategiaDeSeleccion(proveedor);

        Assert.Equal("cercania", activa.Nombre);
        Assert.Equal("ayacucho", activa.Seleccionar(Solicitud).Codigo);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-10")]
    [InlineData("mayor-vendible")]
    [InlineData(" Mayor-Vendible ")]
    public void Otra_estrategia_configurada_sustituye_a_la_predeterminada_sin_cambiar_el_contrato(string nombre)
    {
        using var proveedor = Construir(nombre);

        var activa = ComposicionDeReservas.ExigirEstrategiaDeSeleccion(proveedor);

        Assert.Equal("mayor-vendible", activa.Nombre);
        Assert.Equal("lima", activa.Seleccionar(Solicitud).Codigo);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-10")]
    [InlineData("no-existe")]
    [InlineData("cercania2")]
    public void Un_nombre_desconocido_hace_fallar_el_arranque_con_los_nombres_disponibles(string nombre)
    {
        using var proveedor = Construir(nombre);

        var error = Assert.Throws<EstrategiaDeSeleccionDesconocidaException>(() => ComposicionDeReservas.ExigirEstrategiaDeSeleccion(proveedor));

        Assert.Contains("cercania", error.Message, StringComparison.Ordinal);
        Assert.Contains("mayor-vendible", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-10")]
    public void Dos_estrategias_con_el_mismo_nombre_se_rechazan()
    {
        Assert.Throws<ArgumentException>(() => new RegistroDeEstrategiasDeSeleccion([new SeleccionPorCercania(), new SeleccionPorCercania()]));
    }
}
