using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Auditoria;
using BabelHub.Inventario.UnitTests.Plataforma;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Auditoria;

/// <summary>
/// El cierre de las ventanas de límites (T25 y T27; AUD-28, AUD-41; AC-AUD-25): un solo evento por huella, límite y ventana con el total y la dirección completa, fechado en el cierre,
/// escrito junto con el cierre y el borrado de la dirección; idempotente, con margen de 10 segundos, reintentos y purga a las 24 horas.
/// </summary>
public sealed class CerrarVentanasDeLimiteTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);
    private const string Huella = "k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE";

    /// <summary>Lo persistido: filas por ventana y su cierre; <c>Cargar</c> devuelve una copia nueva, como una lectura de la base.</summary>
    private sealed class AcumuladoPersistente : IAcumuladoParaCerrar
    {
        public Dictionary<DateTimeOffset, List<FilaDeAcumulado>> Filas { get; } = [];

        public Dictionary<DateTimeOffset, DateTimeOffset> Cerradas { get; } = [];

        public Dictionary<DateTimeOffset, int> Intentos { get; } = [];

        public void Rechazar(string huella, string limite, DateTimeOffset instante, string? direccion)
        {
            var ventana = VentanaDeLimite.Que(instante);
            var copia = AcumuladoDeLimite.Restaurar(ventana, Filas.GetValueOrDefault(ventana.Inicio) ?? [], cerrada: false);
            copia.Registrar(huella, limite, instante, direccion);
            Filas[ventana.Inicio] = [.. copia.Filas];
        }

        public Task<IReadOnlyList<DateTimeOffset>> VentanasCerrables(DateTimeOffset ahora, int maximo, CancellationToken cancelacion = default) =>
            Task.FromResult<IReadOnlyList<DateTimeOffset>>([.. Filas.Keys.Where(i => !Cerradas.ContainsKey(i) && VentanaDeLimite.Que(i).PuedeCerrarse(ahora)).Order().Take(maximo)]);

        public Task<AcumuladoDeLimite?> Cargar(DateTimeOffset inicioDeLaVentana, CancellationToken cancelacion = default) =>
            Task.FromResult<AcumuladoDeLimite?>(Filas.TryGetValue(inicioDeLaVentana, out var filas) ? AcumuladoDeLimite.Restaurar(VentanaDeLimite.Que(inicioDeLaVentana), filas, Cerradas.ContainsKey(inicioDeLaVentana)) : null);

        public Task GuardarCierre(DateTimeOffset inicioDeLaVentana, DateTimeOffset ahora, CancellationToken cancelacion = default)
        {
            Cerradas[inicioDeLaVentana] = ahora;
            Filas[inicioDeLaVentana] = [.. Filas[inicioDeLaVentana].Select(f => f with { DireccionCompleta = null })];
            return Task.CompletedTask;
        }

        public Task<int> RegistrarIntentoFallido(DateTimeOffset inicioDeLaVentana, CancellationToken cancelacion = default)
        {
            Intentos[inicioDeLaVentana] = Intentos.GetValueOrDefault(inicioDeLaVentana) + 1;
            return Task.FromResult(Intentos[inicioDeLaVentana]);
        }

        public Task BorrarDirecciones(DateTimeOffset inicioDeLaVentana, CancellationToken cancelacion = default)
        {
            Filas[inicioDeLaVentana] = [.. Filas[inicioDeLaVentana].Select(f => f with { DireccionCompleta = null })];
            return Task.CompletedTask;
        }

        public Task<int> Purgar(DateTimeOffset ahora, CancellationToken cancelacion = default)
        {
            var vencidas = Cerradas.Where(c => ahora >= c.Key + VentanaDeLimite.Duracion + AcumuladoDeLimite.RetencionTrasElCierre).Select(c => c.Key).ToList();
            var filas = vencidas.Sum(v => Filas[v].Count);
            foreach (var v in vencidas)
            {
                Filas.Remove(v);
                Cerradas.Remove(v);
            }

            return Task.FromResult(filas);
        }
    }

    private sealed class Entorno
    {
        public Entorno(ActorDePrueba? actor = null)
        {
            Reloj = new RelojFijo(Inicio.AddMinutes(5).AddSeconds(11));
            Cierre = new CerrarVentanasDeLimite(Acumulado, Registros, CatalogoDeAccionesDeAuditoria.Crear(), ServicioDeOrigen.Inventario, new UnidadDeTrabajoDeAuditoria(), Alertas, actor ?? ActorDePrueba.DelSistema(), Reloj);
        }

        public RelojFijo Reloj { get; }

        public AcumuladoPersistente Acumulado { get; } = new();

        public AlmacenDeRegistrosEnMemoria Registros { get; } = new();

        public AlertasEspia Alertas { get; } = new();

        public CerrarVentanasDeLimite Cierre { get; }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public async Task CincuentaRechazosYVeinteTrasUnReinicio_SonUnSoloEventoDeSetentaConLaDireccionCompletaFechadoEnElCierre()
    {
        var e = new Entorno();
        for (var i = 0; i < 50; i++)
        {
            e.Acumulado.Rechazar(Huella, "registros-por-origen", Inicio.AddSeconds(i), "203.0.113.7");
        }

        // «Reinicio»: lo persistido sigue y se suman los 20 siguientes a la misma ventana.
        for (var i = 0; i < 20; i++)
        {
            e.Acumulado.Rechazar(Huella, "registros-por-origen", Inicio.AddSeconds(100 + i), "203.0.113.7");
        }

        var resultado = await e.Cierre.Ejecutar();

        Assert.Equal(1, resultado.VentanasCerradas);
        Assert.Equal(1, resultado.EventosEscritos);
        var registro = Assert.Single(e.Registros.Registros);
        Assert.Equal("LimiteExcedido", registro.Accion);
        Assert.Equal("registros-por-origen", registro.Motivo);
        Assert.Equal("203.0.113.7", registro.Origen);
        Assert.Equal(Inicio.AddMinutes(5), registro.InstanteUtc);
        Assert.Equal(Domain.Auditoria.TipoDeActor.Sistema, registro.Actor.Tipo);
        Assert.Equal(ResultadoDeOperacion.Rechazado, registro.Resultado);
        Assert.Contains("70", registro.Detalle.Texto, StringComparison.Ordinal);
        Assert.All(e.Acumulado.Filas[Inicio], f => Assert.Null(f.DireccionCompleta));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public async Task UnaVentanaCerradaNoVuelveAEmitir_NiSeAcumulanRechazosTardiosEnElla()
    {
        var e = new Entorno();
        e.Acumulado.Rechazar(Huella, "registros-por-origen", Inicio, "203.0.113.7");
        await e.Cierre.Ejecutar();

        var otra = await e.Cierre.Ejecutar();

        Assert.Equal(0, otra.VentanasCerradas);
        Assert.Single(e.Registros.Registros);
        var cerrada = AcumuladoDeLimite.Restaurar(VentanaDeLimite.Que(Inicio), e.Acumulado.Filas[Inicio], cerrada: true);
        Assert.Equal(ResultadoDeRegistroDeRechazo.VentanaCerrada, cerrada.Registrar(Huella, "registros-por-origen", Inicio.AddSeconds(30), "203.0.113.7"));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public async Task AntesDelMargenDe10SegundosNoSeCierraNada()
    {
        var e = new Entorno();
        e.Acumulado.Rechazar(Huella, "registros-por-origen", Inicio, "203.0.113.7");
        e.Reloj.Ahora = Inicio.AddMinutes(5).AddSeconds(9);

        var resultado = await e.Cierre.Ejecutar();

        Assert.Equal(0, resultado.VentanasCerradas);
        Assert.Empty(e.Registros.Registros);
        Assert.Equal("203.0.113.7", e.Acumulado.Filas[Inicio].Single().DireccionCompleta);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public async Task UnEventoPorCadaHuellaYLimiteDeLaVentana_ConUsuarioSinDireccionYLaFilaOtros()
    {
        var e = new Entorno();
        e.Acumulado.Rechazar(Huella, "registros-por-origen", Inicio, "203.0.113.7");
        e.Acumulado.Rechazar(Huella, "ingresos-fallidos", Inicio.AddSeconds(1), "203.0.113.7");
        e.Acumulado.Rechazar("u-3f1c2a40-aaaa-bbbb-cccc-111122223333", "solicitudes-por-usuario", Inicio.AddSeconds(2), null);
        e.Acumulado.Filas[Inicio].Add(new FilaDeAcumulado(AcumuladoDeLimite.HuellaDeOtros, AcumuladoDeLimite.LimiteDeOtros, 40, null));

        await e.Cierre.Ejecutar();

        Assert.Equal(4, e.Registros.Registros.Count);
        Assert.Contains(e.Registros.Registros, r => r.Motivo == "ingresos-fallidos" && r.Origen == "203.0.113.7");
        Assert.Contains(e.Registros.Registros, r => r.Motivo == "solicitudes-por-usuario" && r.Origen == "u-3f1c2a40-aaaa-bbbb-cccc-111122223333");
        Assert.Contains(e.Registros.Registros, r => r.Motivo == CerrarVentanasDeLimite.MotivoDeLaFilaOtros && r.Origen == AcumuladoDeLimite.HuellaDeOtros && r.Detalle.Texto.Contains("40", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-41")]
    public async Task SiLaAuditoriaFalla_LaVentanaSigueAbiertaConSuDireccion_YLaProximaCorridaLaCierraSinDuplicar()
    {
        var e = new Entorno();
        e.Acumulado.Rechazar(Huella, "registros-por-origen", Inicio, "203.0.113.7");
        e.Registros.Falla = new InvalidOperationException("la auditoría no responde");

        var fallida = await e.Cierre.Ejecutar();

        Assert.Equal(1, fallida.Fallidas);
        Assert.Equal(0, fallida.VentanasCerradas);
        Assert.Equal("203.0.113.7", e.Acumulado.Filas[Inicio].Single().DireccionCompleta);
        Assert.Empty(e.Acumulado.Cerradas);
        Assert.Equal(1, e.Acumulado.Intentos[Inicio]);

        e.Registros.Falla = null;
        var recuperada = await e.Cierre.Ejecutar();

        Assert.Equal(1, recuperada.VentanasCerradas);
        Assert.Single(e.Registros.Registros);
        Assert.Equal("203.0.113.7", e.Registros.Registros[0].Origen);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-41")]
    public async Task TrasDiezReintentosSinAuditoria_SeBorraLaDireccionConservandoLaHuella_SeAlertaYAlRecuperarseElEventoLlevaLaHuella()
    {
        var e = new Entorno();
        e.Acumulado.Rechazar(Huella, "registros-por-origen", Inicio, "203.0.113.7");
        e.Registros.Falla = new InvalidOperationException("la auditoría no responde");

        for (var i = 0; i < CerrarVentanasDeLimite.IntentosAntesDeBorrarLaDireccion; i++)
        {
            await e.Cierre.Ejecutar();
        }

        Assert.Equal([MotivosDeAlerta.AuditoriaNoDisponible], e.Alertas.Tecnicas.Distinct());
        var fila = e.Acumulado.Filas[Inicio].Single();
        Assert.Null(fila.DireccionCompleta);
        Assert.Equal(Huella, fila.Huella);
        Assert.Equal(1, fila.Rechazos);

        e.Registros.Falla = null;
        await e.Cierre.Ejecutar();

        Assert.Equal(Huella, Assert.Single(e.Registros.Registros).Origen);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-41")]
    public async Task LasVentanasCerradasSePurganA24HorasDelCierre()
    {
        var e = new Entorno();
        e.Acumulado.Rechazar(Huella, "registros-por-origen", Inicio, "203.0.113.7");
        await e.Cierre.Ejecutar();

        e.Reloj.Ahora = Inicio.AddMinutes(5).AddHours(24).AddSeconds(-1);
        Assert.Equal(0, (await e.Cierre.Ejecutar()).FilasPurgadas);

        e.Reloj.Ahora = Inicio.AddMinutes(5).AddHours(24);
        Assert.Equal(1, (await e.Cierre.Ejecutar()).FilasPurgadas);
        Assert.Empty(e.Acumulado.Filas);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public async Task ElCierreSeNiegaACorrerSinElActorSistema()
    {
        foreach (var actor in new[] { ActorDePrueba.SinContexto(), ActorDePrueba.Anonimo(), ActorDePrueba.DeAdministradorCentral(), ActorDePrueba.DeIntegracion("x") })
        {
            var e = new Entorno(actor);
            e.Acumulado.Rechazar(Huella, "registros-por-origen", Inicio, "203.0.113.7");

            await Assert.ThrowsAsync<InvalidOperationException>(() => e.Cierre.Ejecutar());
            Assert.Empty(e.Registros.Registros);
        }
    }
}
