using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Carrito;
using Microsoft.Extensions.Configuration;

namespace BabelHub.IntegrationTests.Carrito;

/// <summary>
/// T26a: el costo de envío validado al arrancar (CAR-23; AC-CAR-21), el generador de identificadores y la cookie del visitante, y el ejecutor de los trabajos de fondo de Carrito: como «sistema», con registro, alerta tras fallos seguidos y a las
/// 24 horas sin una corrida exitosa, y falla cerrada.
/// </summary>
public sealed class AdaptadoresBaseDelCarritoTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

    // ---------- Costo de envío ----------

    [Theory]
    [InlineData("0", 0.00)]
    [InlineData("12", 12.00)]
    [InlineData("12.50", 12.50)]
    [InlineData(" 15.00 ", 15.00)]
    [Trait("Criterio", "AC-CAR-21")]
    public void El_costo_de_envio_valido_se_lee_en_soles_con_punto_decimal(string texto, double esperado)
    {
        var opciones = AppSettingsOpcionesDeEnvio.Desde(texto);

        Assert.Equal((decimal)esperado, opciones.Valor.Valor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-1")]
    [InlineData("12,50")]
    [InlineData("12.505")]
    [InlineData("doce")]
    [InlineData("1e3")]
    [InlineData("99999999999.00")]
    public void Un_costo_de_envio_ausente_o_invalido_impide_arrancar_y_nombra_la_clave(string? texto)
    {
        var excepcion = Assert.Throws<InvalidOperationException>(() => AppSettingsOpcionesDeEnvio.Desde(texto));

        Assert.Contains("Carrito:CostoDeEnvio", excepcion.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void El_costo_se_carga_de_la_configuracion_por_su_clave()
    {
        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Carrito:CostoDeEnvio"] = "9.90" }).Build();

        Assert.Equal(9.90m, AppSettingsOpcionesDeEnvio.Cargar(configuracion).Valor.Valor);
        Assert.Throws<InvalidOperationException>(() => AppSettingsOpcionesDeEnvio.Cargar(new ConfigurationBuilder().Build()));
    }

    // ---------- Identificadores ----------

    [Fact]
    public void Los_ids_son_GUID_v4_distintos_y_la_cookie_son_43_caracteres_base64url_cuya_huella_la_acepta()
    {
        var generador = new CriptoGeneradorDeIdentificadores();

        var ids = Enumerable.Range(0, 200).Select(_ => generador.NuevoId()).ToList();
        var cookies = Enumerable.Range(0, 200).Select(_ => generador.NuevaCookieDeVisitante()).ToList();

        Assert.Equal(200, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Equal('4', id.ToString("D")[14]));
        Assert.All(ids, id => Assert.Contains(id.ToString("D")[19], "89ab"));
        Assert.Equal(200, cookies.Distinct().Count());
        Assert.All(cookies, cookie =>
        {
            Assert.Equal(43, cookie.Length);
            Assert.Matches("^[A-Za-z0-9_-]{43}$", cookie);
            Assert.True(HuellaDeLaCookie.TryDe(cookie, out var huella));
            Assert.Equal(32, huella!.Length);
        });
    }

    // ---------- Ejecutor de trabajos de fondo ----------

    private sealed class RelojMovil : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = Inicio;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class ActorEspia : IEstablecedorDeActor
    {
        public List<TipoDeActor> Fijados { get; } = [];

        public int Limpiezas { get; private set; }

        public void Establecer(ActorActual actor) => Fijados.Add(actor.Tipo);

        public void Limpiar() => Limpiezas++;
    }

    private sealed class RegistroEspia : IRegistroDeTrabajosDeFondo
    {
        public List<(string Trabajo, ResultadoDeTrabajo Resultado)> Corridas { get; } = [];

        public bool Falla { get; set; }

        public Task Registrar(string trabajo, ResultadoDeTrabajo resultado, DateTimeOffset instante, CancellationToken cancelacion = default)
        {
            Corridas.Add((trabajo, resultado));
            return Falla ? throw new InvalidOperationException("sin registro") : Task.CompletedTask;
        }
    }

    private sealed class AlertasEspia : IAlertasDelCarrito
    {
        public List<(TipoDeAlertaDelCarrito Tipo, int Cantidad)> Publicadas { get; } = [];

        public bool Falla { get; set; }

        public Task Publicar(TipoDeAlertaDelCarrito tipo, int cantidad, CancellationToken cancelacion = default)
        {
            Publicadas.Add((tipo, cantidad));
            return Falla ? throw new InvalidOperationException("sin alertas") : Task.CompletedTask;
        }
    }

    private static (TrabajoDeFondoDeCarrito Trabajo, ActorEspia Actor, RegistroEspia Registro, AlertasEspia Alertas, RelojMovil Reloj) Crear(Func<CancellationToken, Task> corrida)
    {
        var (actor, registro, alertas, reloj) = (new ActorEspia(), new RegistroEspia(), new AlertasEspia(), new RelojMovil());
        return (new TrabajoDeFondoDeCarrito("carrito-prueba", corrida, registro, actor, alertas, reloj, new OpcionesDelTrabajoDelCarrito(TimeSpan.FromMinutes(1))), actor, registro, alertas, reloj);
    }

    [Fact]
    public async Task La_corrida_se_ejecuta_como_sistema_se_registra_como_exito_y_el_actor_se_limpia()
    {
        var (trabajo, actor, registro, alertas, _) = Crear(_ => Task.CompletedTask);

        await trabajo.EjecutarUnaVez();

        Assert.Equal([TipoDeActor.Sistema], actor.Fijados);
        Assert.Equal(1, actor.Limpiezas);
        Assert.Equal([("carrito-prueba", ResultadoDeTrabajo.Exito)], registro.Corridas);
        Assert.Empty(alertas.Publicadas);
    }

    [Fact]
    public async Task Una_falla_no_cae_el_servicio_se_registra_y_tras_tres_fallos_seguidos_se_alerta_cada_tres()
    {
        var (trabajo, _, registro, alertas, _) = Crear(_ => throw new InvalidOperationException("falla"));

        for (var i = 0; i < 6; i++)
        {
            await trabajo.EjecutarUnaVez();
        }

        Assert.Equal(6, registro.Corridas.Count(c => c.Resultado == ResultadoDeTrabajo.Fallo));
        Assert.Equal([(TipoDeAlertaDelCarrito.TrabajoSinCorrer, 1), (TipoDeAlertaDelCarrito.TrabajoSinCorrer, 1)], alertas.Publicadas);
    }

    [Fact]
    public async Task Un_exito_reinicia_la_cuenta_de_fallos()
    {
        var fallar = true;
        var (trabajo, _, _, alertas, _) = Crear(_ => fallar ? throw new InvalidOperationException("falla") : Task.CompletedTask);
        await trabajo.EjecutarUnaVez();
        await trabajo.EjecutarUnaVez();
        fallar = false;
        await trabajo.EjecutarUnaVez();
        fallar = true;

        await trabajo.EjecutarUnaVez();
        await trabajo.EjecutarUnaVez();

        Assert.Empty(alertas.Publicadas);
    }

    [Fact]
    public async Task Sin_una_corrida_exitosa_en_24_horas_se_alerta_una_vez_por_dia_aunque_no_lleguen_tres_fallos_seguidos()
    {
        var (trabajo, _, _, alertas, reloj) = Crear(_ => throw new InvalidOperationException("falla"));
        await trabajo.EjecutarUnaVez();
        reloj.Ahora = Inicio.AddHours(23);
        await trabajo.EjecutarUnaVez();
        Assert.Empty(alertas.Publicadas);

        reloj.Ahora = Inicio.AddHours(24);
        await trabajo.EjecutarUnaVez();
        reloj.Ahora = Inicio.AddHours(30);
        await trabajo.EjecutarUnaVez();
        reloj.Ahora = Inicio.AddHours(48);
        await trabajo.EjecutarUnaVez();

        Assert.Equal(2, alertas.Publicadas.Count(a => a.Tipo == TipoDeAlertaDelCarrito.TrabajoSinCorrer));
    }

    [Fact]
    public async Task Si_el_registro_o_la_alerta_fallan_el_trabajo_sigue_y_solo_se_propaga_la_cancelacion()
    {
        var (trabajo, _, registro, alertas, _) = Crear(_ => throw new InvalidOperationException("falla"));
        registro.Falla = true;
        alertas.Falla = true;
        for (var i = 0; i < 3; i++)
        {
            await trabajo.EjecutarUnaVez();
        }

        using var cancelada = new CancellationTokenSource();
        var (cancelable, _, _, _, _) = Crear(async ct => { await Task.Delay(Timeout.Infinite, ct); });
        var tarea = cancelable.Correr(cancelada.Token);
        await cancelada.CancelAsync();
        await tarea;

        Assert.Equal(3, registro.Corridas.Count);
        Assert.Single(alertas.Publicadas);
    }
}
