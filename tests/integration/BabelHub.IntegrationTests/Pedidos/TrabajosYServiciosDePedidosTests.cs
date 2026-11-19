using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Ecommerce.Infrastructure.Pedidos;

namespace BabelHub.IntegrationTests.Pedidos;

/// <summary>T30 (PED-34, PED-39; AC-PED-13, 29, 33): los generadores, la sucursal por su código y el ejecutor de los trabajos de fondo de Pedidos (sin base de datos).</summary>
public sealed class TrabajosYServiciosDePedidosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 20, 8, 30, 0, TimeSpan.Zero);

    private sealed class Reloj(DateTimeOffset ahora) : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = ahora;

        public DateOnly HoyEnLima => PoliticaDeRetencion.FechaEnLima(Ahora);
    }

    private sealed class RegistroEspia : IRegistroDeTrabajosDeFondo
    {
        public List<(string Trabajo, ResultadoDeTrabajo Resultado)> Corridas { get; } = [];

        public Task Registrar(string trabajo, ResultadoDeTrabajo resultado, DateTimeOffset instante, CancellationToken cancelacion = default)
        {
            Corridas.Add((trabajo, resultado));
            return Task.CompletedTask;
        }
    }

    private sealed class AlertasEspia : IPublicadorDeAlertasTecnicas
    {
        public List<string> Motivos { get; } = [];

        public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
        {
            Motivos.Add(alerta.Motivo);
            return Task.CompletedTask;
        }
    }

    private static readonly AlcanceDeSistemaDePedidos SinActor = (corrida, cancelacion) => corrida(cancelacion);

    [Fact]
    public async Task La_sucursal_se_resuelve_por_su_codigo_siempre_al_mismo_identificador_sin_distinguir_mayusculas_y_un_codigo_invalido_no_se_resuelve()
    {
        var resolucion = new CodigoResolucionDeSucursal();

        var lima = await resolucion.IdentificadorDe("LIM");

        Assert.NotNull(lima);
        Assert.Equal(lima, await resolucion.IdentificadorDe("lim"));
        Assert.Equal(lima, await resolucion.IdentificadorDe(" Lim "));
        Assert.NotEqual(lima, await resolucion.IdentificadorDe("AYA"));
        Assert.All(new[] { "", "   ", "LIM; DROP", new string('a', 41), "lim/central" }, codigo => Assert.Null(CodigoResolucionDeSucursal.Derivar(codigo)));
        Assert.Null(CodigoResolucionDeSucursal.Derivar(null));
    }

    [Fact]
    public void Los_numeros_de_pedido_son_validos_y_no_se_repiten_y_los_identificadores_de_operacion_son_distintos()
    {
        var generador = new AleatorioGeneradorDeNumerosDePedido();

        var numeros = Enumerable.Range(0, 200).Select(_ => generador.Siguiente().Valor).ToList();

        Assert.All(numeros, n => Assert.NotNull(NumeroDePedido.Crear(n)));
        Assert.Equal(numeros.Count, numeros.Distinct().Count());
        var ids = new GeneradorDeIdentificadoresDePedidos();
        Assert.NotEqual(ids.Nuevo(), ids.Nuevo());
    }

    [Fact]
    [Trait("Criterio", "AC-PED-33")]
    public void Un_trabajo_diario_corre_solo_a_su_hora_de_Lima_y_una_vez_por_dia()
    {
        // 08:30 UTC = 03:30 en Lima.
        var reloj = new Reloj(Ahora);
        var ayer = Ahora.AddDays(-1);

        Assert.True(TrabajosDePedidos.TocaHoy(reloj, null, TrabajosDePedidos.HoraDeLimaDeLaDepuracion));
        Assert.True(TrabajosDePedidos.TocaHoy(reloj, ayer, TrabajosDePedidos.HoraDeLimaDeLaDepuracion));
        Assert.False(TrabajosDePedidos.TocaHoy(reloj, Ahora.AddMinutes(-20), TrabajosDePedidos.HoraDeLimaDeLaDepuracion));
        Assert.False(TrabajosDePedidos.TocaHoy(reloj, ayer, TrabajosDePedidos.HoraDeLimaDeLaRevision));
        reloj.Ahora = Ahora.AddHours(-2);
        Assert.False(TrabajosDePedidos.TocaHoy(reloj, ayer, TrabajosDePedidos.HoraDeLimaDeLaDepuracion));

        // Un corte a las 03:xx se recupera el mismo día: a las 10:00 de Lima sigue tocando si no corrió hoy.
        reloj.Ahora = Ahora.AddHours(7);
        Assert.True(TrabajosDePedidos.TocaHoy(reloj, ayer, TrabajosDePedidos.HoraDeLimaDeLaDepuracion));
        Assert.False(TrabajosDePedidos.TocaHoy(reloj, Ahora.AddHours(1), TrabajosDePedidos.HoraDeLimaDeLaDepuracion));
    }

    [Fact]
    public async Task Cada_corrida_se_registra_y_tras_tres_fallos_seguidos_se_publica_la_alerta_sin_detener_el_trabajo()
    {
        var registro = new RegistroEspia();
        var alertas = new AlertasEspia();
        var fallar = true;
        var trabajo = new TrabajoDeFondoDePedidos(
            "pedidos-prueba",
            _ => fallar ? throw new InvalidOperationException("sin base") : Task.FromResult(true),
            registro,
            SinActor,
            alertas,
            new Reloj(Ahora),
            new OpcionesDelTrabajoDePedidos(TimeSpan.FromMinutes(1)));

        await trabajo.EjecutarUnaVez();
        await trabajo.EjecutarUnaVez();
        Assert.Empty(alertas.Motivos);
        await trabajo.EjecutarUnaVez();
        fallar = false;
        await trabajo.EjecutarUnaVez();

        Assert.Equal([ResultadoDeTrabajo.Fallo, ResultadoDeTrabajo.Fallo, ResultadoDeTrabajo.Fallo, ResultadoDeTrabajo.Exito], registro.Corridas.Select(c => c.Resultado));
        Assert.Equal([TrabajoDeFondoDePedidos.MotivoDelTrabajoDetenido], alertas.Motivos);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-13")]
    public async Task Un_tick_sin_trabajo_debido_no_se_registra_como_exito_ni_reinicia_los_fallos_y_los_cinco_trabajos_tienen_nombre_estable()
    {
        var registro = new RegistroEspia();
        var alertas = new AlertasEspia();
        var modo = 0;
        var trabajo = new TrabajoDeFondoDePedidos(
            "pedidos-diario",
            _ => modo == 0 ? throw new InvalidOperationException("sin base") : Task.FromResult(false),
            registro,
            SinActor,
            alertas,
            new Reloj(Ahora),
            new OpcionesDelTrabajoDePedidos(TimeSpan.FromMinutes(1)));

        await trabajo.EjecutarUnaVez();
        await trabajo.EjecutarUnaVez();
        modo = 1;
        await trabajo.EjecutarUnaVez();
        modo = 0;
        await trabajo.EjecutarUnaVez();

        // El tick intermedio que no tocaba no registró nada ni reinició la cuenta: el tercer fallo alerta.
        Assert.Equal([ResultadoDeTrabajo.Fallo, ResultadoDeTrabajo.Fallo, ResultadoDeTrabajo.Fallo], registro.Corridas.Select(c => c.Resultado));
        Assert.Single(alertas.Motivos);
        Assert.Equal(
            ["pedidos-revision-de-plazos", "pedidos-depuracion-diaria", "pedidos-liberacion-del-contacto", "pedidos-reanudacion-de-operaciones", "pedidos-revision-sin-estado-final"],
            new[] { TrabajosDePedidos.RevisionDePlazos, TrabajosDePedidos.DepuracionDiaria, TrabajosDePedidos.LiberacionDelContacto, TrabajosDePedidos.ReanudacionDeOperaciones, TrabajosDePedidos.RevisionSinEstadoFinal });
    }

    [Fact]
    public async Task Una_corrida_que_se_cuelga_se_cancela_por_su_plazo_y_cuenta_como_fallo_y_solo_el_apagado_se_propaga()
    {
        var registro = new RegistroEspia();
        var trabajo = new TrabajoDeFondoDePedidos(
            "pedidos-colgado",
            async ct =>
            {
                await Task.Delay(TimeSpan.FromMinutes(10), ct);
                return true;
            },
            registro,
            SinActor,
            new AlertasEspia(),
            new Reloj(Ahora),
            new OpcionesDelTrabajoDePedidos(TimeSpan.FromMinutes(1), PlazoMaximoDeLaCorrida: TimeSpan.FromMilliseconds(50)));

        await trabajo.EjecutarUnaVez();

        Assert.Equal([ResultadoDeTrabajo.Fallo], registro.Corridas.Select(c => c.Resultado));

        using var apagado = new CancellationTokenSource();
        var corriendo = trabajo.Correr(apagado.Token);
        await apagado.CancelAsync();
        await corriendo;
    }
}
