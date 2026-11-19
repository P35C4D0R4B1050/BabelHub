using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>Los límites del cobro: 5 por intención, 10 por cuenta y 20 por origen cada hora, con falla cerrada (T12; PAG-04, PAG-17, D18, AM38).</summary>
public sealed class LimitadorDeCobrosTests
{
    private static readonly Guid Pedido = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>Un motor de límites con contadores por política y sujeto, con los máximos de las políticas del Dominio y la clave del llamador idempotente.</summary>
    private sealed class MotorDeCobros : IControlDeLimites
    {
        private readonly Dictionary<(string Politica, string Sujeto), HashSet<string>> _usados = [];

        public bool Caido { get; set; }

        public bool Lanza { get; set; }

        public List<(string Politica, string? Sujeto, string? Clave)> Llamadas { get; } = [];

        public Dictionary<string, int> Maximos { get; } = new()
        {
            ["cobros-por-intencion"] = 5,
            ["cobros-por-cuenta"] = 10,
            ["cobros-por-origen"] = 20,
        };

        public Task<ResultadoDeLimite> Reservar(ClaveDePolitica politica, string? sujeto = null, string? claveDelLlamador = null, HuellaDeSujeto? huellaDelLlamador = null, ClaveDeIdempotencia? idempotencia = null, CancellationToken cancelacion = default)
        {
            Llamadas.Add((politica.Valor, sujeto, claveDelLlamador));
            if (Lanza)
            {
                throw new InvalidOperationException("Redis no responde.");
            }

            if (Caido)
            {
                return Task.FromResult(ResultadoDeLimite.NoDisponible());
            }

            var usados = _usados.TryGetValue((politica.Valor, sujeto ?? "origen"), out var conjunto) ? conjunto : _usados[(politica.Valor, sujeto ?? "origen")] = [];
            if (claveDelLlamador is not null && usados.Contains(claveDelLlamador))
            {
                return Task.FromResult(ResultadoDeLimite.Permitido(Reserva(politica.Valor)));
            }

            if (usados.Count >= Maximos[politica.Valor])
            {
                return Task.FromResult(ResultadoDeLimite.Excedido(politica.Valor, 1_200, DateTimeOffset.UtcNow.AddSeconds(1_200)));
            }

            usados.Add(claveDelLlamador ?? Guid.NewGuid().ToString("N"));
            return Task.FromResult(ResultadoDeLimite.Permitido(Reserva(politica.Valor)));
        }

        public Task Liberar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;

        private static ReservaDeLimite Reserva(string politica) => new(politica, "sujeto", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
    }

    private static async Task<DecisionDelLimiteDeCobro[]> Cobrar(LimitadorDeCobros limitador, int veces, Guid? pedido = null, Guid? cuenta = null)
    {
        var decisiones = new List<DecisionDelLimiteDeCobro>();
        for (var i = 0; i < veces; i++)
        {
            decisiones.Add(await limitador.Reservar(pedido ?? Pedido, cuenta ?? Cuenta, Guid.NewGuid(), CancellationToken.None));
        }

        return [.. decisiones];
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Cada_intento_reserva_un_cupo_de_la_intencion_de_la_cuenta_y_del_origen_con_el_intento_como_clave()
    {
        var motor = new MotorDeCobros();
        var limitador = new LimitadorDeCobros(motor);
        var intento = Guid.NewGuid();

        var decision = await limitador.Reservar(Pedido, Cuenta, intento, CancellationToken.None);

        Assert.Equal(ResultadoDelLimiteDeCobro.Permitido, decision.Resultado);
        Assert.Equal(
            [("cobros-por-intencion", (string?)Pedido.ToString("N")), ("cobros-por-cuenta", Cuenta.ToString("N")), ("cobros-por-origen", null)],
            motor.Llamadas.Select(l => (l.Politica, l.Sujeto)).ToArray());
        Assert.All(motor.Llamadas, l => Assert.Equal(intento.ToString("N"), l.Clave));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task El_sexto_intento_de_una_intencion_en_la_hora_se_rechaza_con_su_espera()
    {
        var limitador = new LimitadorDeCobros(new MotorDeCobros());

        var decisiones = await Cobrar(limitador, 6);

        Assert.All(decisiones.Take(5), d => Assert.Equal(ResultadoDelLimiteDeCobro.Permitido, d.Resultado));
        Assert.Equal(ResultadoDelLimiteDeCobro.Excedido, decisiones[5].Resultado);
        Assert.Equal(1_200, decisiones[5].EsperaEnSegundos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task El_undecimo_intento_de_una_cuenta_en_la_hora_se_rechaza_aunque_cambie_de_intencion()
    {
        var limitador = new LimitadorDeCobros(new MotorDeCobros());
        var decisiones = new List<DecisionDelLimiteDeCobro>();
        for (var i = 0; i < 11; i++)
        {
            decisiones.Add(await limitador.Reservar(Guid.NewGuid(), Cuenta, Guid.NewGuid(), CancellationToken.None));
        }

        Assert.All(decisiones.Take(10), d => Assert.Equal(ResultadoDelLimiteDeCobro.Permitido, d.Resultado));
        Assert.Equal(ResultadoDelLimiteDeCobro.Excedido, decisiones[10].Resultado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task El_intento_veintiuno_de_un_origen_en_la_hora_se_rechaza_aunque_cambie_de_cuenta_e_intencion()
    {
        var limitador = new LimitadorDeCobros(new MotorDeCobros());
        var decisiones = new List<DecisionDelLimiteDeCobro>();
        for (var i = 0; i < 21; i++)
        {
            decisiones.Add(await limitador.Reservar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
        }

        Assert.All(decisiones.Take(20), d => Assert.Equal(ResultadoDelLimiteDeCobro.Permitido, d.Resultado));
        Assert.Equal(ResultadoDelLimiteDeCobro.Excedido, decisiones[20].Resultado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-03")]
    public async Task Repetir_el_mismo_intento_no_consume_otro_cupo()
    {
        var limitador = new LimitadorDeCobros(new MotorDeCobros());
        var intento = Guid.NewGuid();

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(ResultadoDelLimiteDeCobro.Permitido, (await limitador.Reservar(Pedido, Cuenta, intento, CancellationToken.None)).Resultado);
        }

        var decisiones = await Cobrar(limitador, 4);
        Assert.All(decisiones, d => Assert.Equal(ResultadoDelLimiteDeCobro.Permitido, d.Resultado));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Con_el_almacen_de_contadores_caido_o_que_lanza_el_cobro_falla_cerrado()
    {
        var caido = new MotorDeCobros { Caido = true };
        var lanza = new MotorDeCobros { Lanza = true };

        var a = await new LimitadorDeCobros(caido).Reservar(Pedido, Cuenta, Guid.NewGuid(), CancellationToken.None);
        var b = await new LimitadorDeCobros(lanza).Reservar(Pedido, Cuenta, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ResultadoDelLimiteDeCobro.NoDisponible, a.Resultado);
        Assert.Equal(ResultadoDelLimiteDeCobro.NoDisponible, b.Resultado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task La_decision_se_traduce_a_un_429_con_la_espera_o_a_servicio_no_disponible()
    {
        var excedida = new DecisionDelLimiteDeCobro(ResultadoDelLimiteDeCobro.Excedido, 600).ComoFallo<int>();
        var sinEspera = new DecisionDelLimiteDeCobro(ResultadoDelLimiteDeCobro.Excedido).ComoFallo<int>();
        var caida = DecisionDelLimiteDeCobro.NoDisponible.ComoFallo<int>();

        Assert.Equal(CategoriaDeError.Limite, excedida.Error!.Categoria);
        Assert.Equal("limite-excedido", excedida.Error.Clave);
        Assert.Equal("espera-en-segundos:600", $"{excedida.Error.Errores.Single().Campo}:{excedida.Error.Errores.Single().Clave}");
        Assert.Equal("1", sinEspera.Error!.Errores.Single().Clave);
        Assert.Equal(CategoriaDeError.NoDisponible, caida.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public void Las_tres_politicas_del_cobro_son_las_de_la_plataforma_y_no_caen_al_motor_local()
    {
        var politicas = BabelHub.Ecommerce.Domain.Plataforma.PoliticasDeSeguridad.Todas.ToDictionary(p => p.Nombre);

        Assert.Equal(5, politicas["cobros-por-intencion"].Maximo);
        Assert.Equal(10, politicas["cobros-por-cuenta"].Maximo);
        Assert.Equal(20, politicas["cobros-por-origen"].Maximo);
        Assert.All(new[] { "cobros-por-intencion", "cobros-por-cuenta", "cobros-por-origen" }, n => Assert.False(politicas[n].RespaldoLocal));
    }
}
