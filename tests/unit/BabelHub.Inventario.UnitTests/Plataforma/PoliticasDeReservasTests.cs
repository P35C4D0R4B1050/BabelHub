using BabelHub.Inventario.Domain.Plataforma;

namespace BabelHub.Inventario.UnitTests.Plataforma;

/// <summary>T01 de <c>inventario-seleccion-fuente</c>: las políticas de reservas por hora (SEL-14, SEL-19) y sus propiedades.</summary>
public sealed class PoliticasDeReservasTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Criterio", "AC-SEL-14")]
    public void PorCliente_SonCincoPorHoraPorUsuario_SinRespaldoLocalNiLiberacion_YEstaEnTodas()
    {
        var politica = PoliticasDeSeguridad.ReservasPorHoraPorCliente;

        Assert.Equal(("reservas-por-hora-por-cliente", 5, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Usuario), (politica.Nombre, politica.Maximo, politica.Ventana, politica.Tipo));
        Assert.False(politica.RespaldoLocal);
        Assert.False(politica.Liberable);
        Assert.True(politica.PublicarEvento);
        Assert.Null(politica.DuracionDeBloqueo);
        Assert.Contains(politica, PoliticasDeSeguridad.Todas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-19")]
    public void PorOrigen_SonDiezPorHoraPorOrigen_SinRespaldoLocalNiLiberacion_YEstaEnTodas()
    {
        var politica = PoliticasDeSeguridad.ReservasPorHoraPorOrigen;

        Assert.Equal(("reservas-por-hora-por-origen", 10, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Origen), (politica.Nombre, politica.Maximo, politica.Ventana, politica.Tipo));
        Assert.False(politica.RespaldoLocal);
        Assert.False(politica.Liberable);
        Assert.True(politica.PublicarEvento);
        Assert.Contains(politica, PoliticasDeSeguridad.Todas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-14")]
    public void LaSextaReservaEnLaHora_SeRechazaConElInstanteDeReintento_YLaDeOtroClienteNoSeAfecta()
    {
        var cliente = new SimuladorDeLimite(PoliticasDeSeguridad.ReservasPorHoraPorCliente);
        var otro = new SimuladorDeLimite(PoliticasDeSeguridad.ReservasPorHoraPorCliente);
        var ultimo = cliente.Repetir(5, Inicio, TimeSpan.FromMinutes(5));

        var sexta = cliente.Intentar(ultimo + TimeSpan.FromMinutes(1));

        Assert.False(sexta.Permitida);
        Assert.Equal(Inicio + TimeSpan.FromHours(1), sexta.Respuesta!.DisponibleDesde);
        Assert.Equal("reservas-por-hora-por-cliente", sexta.Respuesta.Motivo);
        Assert.True(otro.Intentar(Inicio).Permitida);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-19")]
    public void LaOnceavaReservaDelOrigen_SeRechaza_YUnaViejaDeMasDeUnaHoraYaNoCuenta()
    {
        var origen = new SimuladorDeLimite(PoliticasDeSeguridad.ReservasPorHoraPorOrigen);
        var ultimo = origen.Repetir(10, Inicio, TimeSpan.FromMinutes(3));

        Assert.False(origen.Intentar(ultimo + TimeSpan.FromMinutes(1)).Permitida);
        Assert.True(origen.Intentar(Inicio + TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1)).Permitida);
    }
}
