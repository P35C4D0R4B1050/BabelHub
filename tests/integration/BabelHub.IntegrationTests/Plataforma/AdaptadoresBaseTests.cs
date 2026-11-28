using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;

namespace BabelHub.IntegrationTests.Plataforma;

public sealed class AdaptadoresBaseTests
{
    private sealed class ProveedorDeTiempoFijo(DateTimeOffset ahora) : TimeProvider
    {
        public DateTimeOffset Instante { get; set; } = ahora;

        public override DateTimeOffset GetUtcNow() => Instante;
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-04")]
    [InlineData("2026-11-14T04:59:59+00:00", "2026-11-13")] // 23:59:59 en Lima
    [InlineData("2026-11-14T05:00:00+00:00", "2026-11-14")] // medianoche en Lima
    [InlineData("2026-11-13T12:00:00+00:00", "2026-11-13")]
    public void RelojDelSistema_HoyEnLima_EsElDiaDeCalendarioDeLimaYNoElDelServidor(string instante, string esperado)
    {
        var reloj = new RelojDelSistema(new ProveedorDeTiempoFijo(DateTimeOffset.Parse(instante, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal(DateOnly.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture), reloj.HoyEnLima);
        Assert.Equal(DateTimeOffset.Parse(instante, System.Globalization.CultureInfo.InvariantCulture), reloj.Ahora);
    }

    [Fact]
    public void ActorDelFlujo_SinEstablecer_EsNuloYAlEstablecerloLoConserva()
    {
        var actor = new ActorDelFlujo();

        Assert.Null(actor.Actual);
        actor.Establecer(ActorActual.Anonimo());
        Assert.Equal(TipoDeActor.Anonimo, actor.Actual!.Tipo);
        actor.Limpiar();
        Assert.Null(actor.Actual);
        Assert.Throws<ArgumentNullException>(() => actor.Establecer(null!));
    }

    [Fact]
    public async Task ActorDelFlujo_ConSolicitudesSimultaneas_NoMezclaLosActores()
    {
        var actor = new ActorDelFlujo();
        var usuarios = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToArray();

        var resultados = await Task.WhenAll(usuarios.Select(usuario => Task.Run(async () =>
        {
            actor.Establecer(ActorActual.DeUsuario(usuario, "cliente"));
            await Task.Delay(Random.Shared.Next(1, 30));
            await Task.Yield();
            return actor.Actual!.UsuarioId;
        })));

        Assert.Equal(usuarios.Select(u => (Guid?)u), resultados);
        Assert.Null(actor.Actual);
    }

    [Fact]
    public async Task ActorDelFlujo_Establecido_LoVeLoQueElFlujoInicia()
    {
        var actor = new ActorDelFlujo();
        actor.Establecer(ActorActual.DelSistema());

        var visto = await Task.Run(() => actor.Actual);

        Assert.Equal(TipoDeActor.Sistema, visto!.Tipo);
        actor.Limpiar();
    }
}
