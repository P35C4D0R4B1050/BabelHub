using BabelHub.Inventario.Domain.Plataforma;

namespace BabelHub.Inventario.UnitTests.Plataforma;

public sealed class PoliticasDeSeguridadTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Todas_TienenClavesUnicasYPublicanElExceso()
    {
        Assert.Equal(PoliticasDeSeguridad.Todas.Count, PoliticasDeSeguridad.Todas.Select(p => p.Nombre).Distinct().Count());
        Assert.All(PoliticasDeSeguridad.Todas, p =>
        {
            Assert.True(p.PublicarEvento, p.Nombre);
            Assert.Equal(TipoDeSujetoDeLimite.Usuario, p.Tipo);
        });
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-27")]
    public void AjustesDelPersonal_ConSesentaEnUnaHora_RechazaElSesentaYUnoConLaRespuestaUniforme()
    {
        var simulador = new SimuladorDeLimite(PoliticasDeSeguridad.AjustesDelPersonal);
        var ultimo = simulador.Repetir(60, Inicio, TimeSpan.FromSeconds(50));

        var rechazado = simulador.Intentar(ultimo + TimeSpan.FromSeconds(10));

        Assert.False(rechazado.Permitida);
        Assert.Equal(Inicio + TimeSpan.FromHours(1), rechazado.Respuesta!.DisponibleDesde);
        Assert.Equal(PoliticasDeSeguridad.AjustesDelPersonal.Nombre, rechazado.Respuesta.Motivo);
        Assert.True(PoliticasDeSeguridad.AjustesDelPersonal.PublicarEvento);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-27")]
    public void AjustesDelPersonal_DeOtroEncargado_NoSeVeAfectado()
    {
        var encargado = new SimuladorDeLimite(PoliticasDeSeguridad.AjustesDelPersonal);
        var otro = new SimuladorDeLimite(PoliticasDeSeguridad.AjustesDelPersonal);
        var ultimo = encargado.Repetir(60, Inicio, TimeSpan.FromSeconds(50));

        Assert.False(encargado.Intentar(ultimo + TimeSpan.FromSeconds(1)).Permitida);
        Assert.True(otro.Intentar(ultimo + TimeSpan.FromSeconds(1)).Permitida);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-27")]
    public void AjustesDelPersonal_ConCincuentaYNueveEnUnaHora_AunAceptaElSesenta()
    {
        var simulador = new SimuladorDeLimite(PoliticasDeSeguridad.AjustesDelPersonal);
        var ultimo = simulador.Repetir(59, Inicio, TimeSpan.FromSeconds(50));

        Assert.True(simulador.Intentar(ultimo + TimeSpan.FromSeconds(10)).Permitida);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-27")]
    public void TrasladosDelPersonal_ConTreintaEnUnaHora_RechazaElTreintaYUno()
    {
        var simulador = new SimuladorDeLimite(PoliticasDeSeguridad.TrasladosDelPersonal);
        var ultimo = simulador.Repetir(30, Inicio, TimeSpan.FromMinutes(1));

        var rechazado = simulador.Intentar(ultimo + TimeSpan.FromMinutes(1));

        Assert.False(rechazado.Permitida);
        Assert.Equal(Inicio + TimeSpan.FromHours(1), rechazado.Respuesta!.DisponibleDesde);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-27")]
    public void Presupuestos_DeAjustesYDeTraslados_CuentanPorSeparado()
    {
        var ajustes = new SimuladorDeLimite(PoliticasDeSeguridad.AjustesDelPersonal);
        var traslados = new SimuladorDeLimite(PoliticasDeSeguridad.TrasladosDelPersonal);
        var ultimo = traslados.Repetir(30, Inicio, TimeSpan.FromMinutes(1));

        Assert.False(traslados.Intentar(ultimo + TimeSpan.FromSeconds(1)).Permitida);
        Assert.True(ajustes.Intentar(ultimo + TimeSpan.FromSeconds(1)).Permitida);
    }
}
