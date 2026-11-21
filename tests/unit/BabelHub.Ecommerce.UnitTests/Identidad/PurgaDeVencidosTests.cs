using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.UnitTests.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// La purga de lo vencido (identidad-seguridad T37 y T38; AC-IDE-19; IDE-19, IDE-46, IDE-54): los desafíos una hora después de vencer, los códigos y las renovaciones a los 30 días, la fila de la solicitud de código a los
/// 10 minutos y el cambio de correo a las 24 horas; un paso que falla no detiene los demás.
/// </summary>
public sealed class PurgaDeVencidosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    private sealed class PurgaEspia : IPurgaDeVencidos
    {
        public Dictionary<string, DateTimeOffset> Limites { get; } = [];

        public HashSet<string> Falla { get; } = [];

        private Task<int> Paso(string nombre, DateTimeOffset limite, int quitadas)
        {
            Limites[nombre] = limite;
            return Falla.Contains(nombre) ? throw new InvalidOperationException("La base no responde.") : Task.FromResult(quitadas);
        }

        public Task<int> DesafiosVencidos(DateTimeOffset limite, CancellationToken cancelacion = default) => Paso("desafios", limite, 1);

        public Task<int> CodigosDeUnSoloUsoVencidos(DateTimeOffset limite, CancellationToken cancelacion = default) => Paso("codigos", limite, 2);

        public Task<int> RenovacionesVencidas(DateTimeOffset limite, CancellationToken cancelacion = default) => Paso("renovaciones", limite, 3);

        public Task<int> SolicitudesDeCodigo(DateTimeOffset limite, CancellationToken cancelacion = default) => Paso("solicitudes", limite, 4);

        public Task<int> CambiosDeCorreoVencidos(DateTimeOffset ahora, CancellationToken cancelacion = default) => Paso("cambios", ahora, 5);
    }

    private sealed class PurgaConCambiosNoReconocidos(params Guid[] cuentas) : IPurgaDeVencidos
    {
        public bool Borro { get; private set; }

        public Task<int> DesafiosVencidos(DateTimeOffset limite, CancellationToken cancelacion = default) => Task.FromResult(0);

        public Task<int> CodigosDeUnSoloUsoVencidos(DateTimeOffset limite, CancellationToken cancelacion = default) => Task.FromResult(0);

        public Task<int> RenovacionesVencidas(DateTimeOffset limite, CancellationToken cancelacion = default) => Task.FromResult(0);

        public Task<int> SolicitudesDeCodigo(DateTimeOffset limite, CancellationToken cancelacion = default) => Task.FromResult(0);

        public Task<int> CambiosDeCorreoVencidos(DateTimeOffset ahora, CancellationToken cancelacion = default)
        {
            Borro = true;
            return Task.FromResult(cuentas.Length);
        }

        public Task<IReadOnlyList<Guid>> CuentasConCambioNoReconocidoVencido(DateTimeOffset ahora, CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<Guid>>(cuentas);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-08")]
    public async Task UnCambioDeCorreoNoReconocidoQueVence_QuedaComoEventoDeSeguridadDeSuCuenta_AntesDeBorrarLaFila()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = Guid.NewGuid();
        var purga = new PurgaConCambiosNoReconocidos(cuenta);

        var resultado = await new PurgarVencidosDeIdentidad(purga, new RelojFijo(Ahora), e.Eventos).Ejecutar();

        var evento = Assert.Single(e.Seguridad.Eventos);
        Assert.Equal(AccionesAuditables.CambioDeCorreoNoReconocidoVencido, evento.Accion);
        Assert.Equal("vencido-sin-confirmar", evento.Motivo);
        Assert.Equal(cuenta.ToString("D"), evento.ObjetoId);
        Assert.True(purga.Borro);
        Assert.Equal(1, resultado.CambiosDeCorreo);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-08")]
    public async Task SiElEventoNoSePuedeLeer_LaFilaSeConserva_YLaPasadaFalla_ParaNoPerderElEvento()
    {
        var e = new EntornoDeIdentidad();
        var purga = new PurgaQueNoLee();

        await Assert.ThrowsAsync<AggregateException>(() => new PurgarVencidosDeIdentidad(purga, new RelojFijo(Ahora), e.Eventos).Ejecutar());

        Assert.False(purga.Borro);
    }

    private sealed class PurgaQueNoLee : IPurgaDeVencidos
    {
        public bool Borro { get; private set; }

        public Task<int> DesafiosVencidos(DateTimeOffset limite, CancellationToken cancelacion = default) => Task.FromResult(0);

        public Task<int> CodigosDeUnSoloUsoVencidos(DateTimeOffset limite, CancellationToken cancelacion = default) => Task.FromResult(0);

        public Task<int> RenovacionesVencidas(DateTimeOffset limite, CancellationToken cancelacion = default) => Task.FromResult(0);

        public Task<int> SolicitudesDeCodigo(DateTimeOffset limite, CancellationToken cancelacion = default) => Task.FromResult(0);

        public Task<int> CambiosDeCorreoVencidos(DateTimeOffset ahora, CancellationToken cancelacion = default)
        {
            Borro = true;
            return Task.FromResult(1);
        }

        public Task<IReadOnlyList<Guid>> CuentasConCambioNoReconocidoVencido(DateTimeOffset ahora, CancellationToken cancelacion = default) => throw new InvalidOperationException("La base no responde.");
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-19")]
    public async Task CadaPasoUsaSuPropiaRetencion_YElResultadoSumaLoQuitado()
    {
        var purga = new PurgaEspia();

        var resultado = await new PurgarVencidosDeIdentidad(purga, new RelojFijo(Ahora)).Ejecutar();

        Assert.Equal(new ResultadoDeLaPurga(1, 2, 3, 4, 5), resultado);
        Assert.Equal(15, resultado.Total);
        Assert.Equal(Ahora.AddHours(-1), purga.Limites["desafios"]);
        Assert.Equal(Ahora.AddDays(-30), purga.Limites["codigos"]);
        Assert.Equal(Ahora.AddDays(-30), purga.Limites["renovaciones"]);
        Assert.Equal(Ahora.AddMinutes(-10), purga.Limites["solicitudes"]);
        Assert.Equal(Ahora, purga.Limites["cambios"]);
    }

    [Theory]
    [InlineData("desafios")]
    [InlineData("codigos")]
    [InlineData("renovaciones")]
    [InlineData("solicitudes")]
    [InlineData("cambios")]
    [Trait("Criterio", "AC-IDE-19")]
    public async Task UnPasoQueFalla_NoDetieneLosDemas_YLaPasadaFallaAlFinal(string falla)
    {
        var purga = new PurgaEspia();
        purga.Falla.Add(falla);

        var error = await Assert.ThrowsAsync<AggregateException>(() => new PurgarVencidosDeIdentidad(purga, new RelojFijo(Ahora)).Ejecutar());

        Assert.Single(error.InnerExceptions);
        Assert.Equal(5, purga.Limites.Count);
    }

    [Fact]
    public async Task LaCancelacionDeQuienLlama_SePropaga_SinConvertirseEnFallo()
    {
        using var origen = new CancellationTokenSource();
        var purga = new CancelaAlRecibir(origen);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PurgarVencidosDeIdentidad(purga, new RelojFijo(Ahora)).Ejecutar(origen.Token));
    }

    private sealed class CancelaAlRecibir(CancellationTokenSource origen) : IPurgaDeVencidos
    {
        public Task<int> DesafiosVencidos(DateTimeOffset limite, CancellationToken cancelacion = default)
        {
            origen.Cancel();
            cancelacion.ThrowIfCancellationRequested();
            return Task.FromResult(0);
        }

        public Task<int> CodigosDeUnSoloUsoVencidos(DateTimeOffset limite, CancellationToken cancelacion = default) => Task.FromResult(0);

        public Task<int> RenovacionesVencidas(DateTimeOffset limite, CancellationToken cancelacion = default) => Task.FromResult(0);

        public Task<int> SolicitudesDeCodigo(DateTimeOffset limite, CancellationToken cancelacion = default) => Task.FromResult(0);

        public Task<int> CambiosDeCorreoVencidos(DateTimeOffset ahora, CancellationToken cancelacion = default) => Task.FromResult(0);
    }
}
