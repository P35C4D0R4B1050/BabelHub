using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Plataforma;

/// <summary>
/// La cuota diaria de correo y sus cupos (identidad-seguridad T11; AC-IDE-55, AC-IDE-60, AC-IDE-62; IDE-66, IDE-73, IDE-77, IDE-78, IDE-80, IDE-82): el reparto es configurable y se valida al arrancar, cada
/// categoría usa solo sus cupos, lo que no tiene cupo se suprime o se encola según su categoría, el cupo de la administración nunca lo toca un aviso provocable por un usuario, el cupo se devuelve si el envío
/// falla y la alerta del 80 % sale una sola vez.
/// </summary>
public sealed class CuotaDeCorreoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 15, 15, 0, 0, TimeSpan.Zero);

    /// <summary>El almacén con la semántica del contrato: reserva condicional por cupo y día, devolución y tope por motivo y servicio.</summary>
    private sealed class AlmacenEnMemoria : IAlmacenDeCuotaDeCorreo
    {
        private readonly Dictionary<(DateOnly, CupoDeCorreo), int> _usados = [];
        private readonly Dictionary<(DateOnly, string, string), int> _alertas = [];

        public int Usados(DateOnly dia, CupoDeCorreo cupo) => _usados.GetValueOrDefault((dia, cupo));

        public Task<ReservaDeCupoDeCorreo?> Reservar(DateOnly dia, IReadOnlyList<CupoCandidato> candidatos, CancellationToken cancelacion = default)
        {
            foreach (var candidato in candidatos)
            {
                var usados = _usados.GetValueOrDefault((dia, candidato.Cupo));
                if (usados < candidato.Tamano)
                {
                    _usados[(dia, candidato.Cupo)] = usados + 1;
                    var total = _usados.Where(u => u.Key.Item1 == dia).Sum(u => u.Value);
                    return Task.FromResult<ReservaDeCupoDeCorreo?>(new ReservaDeCupoDeCorreo(candidato.Cupo, usados + 1, total));
                }
            }

            return Task.FromResult<ReservaDeCupoDeCorreo?>(null);
        }

        public Task Devolver(DateOnly dia, CupoDeCorreo cupo, CancellationToken cancelacion = default)
        {
            _usados[(dia, cupo)] = Math.Max(0, _usados.GetValueOrDefault((dia, cupo)) - 1);
            return Task.CompletedTask;
        }

        public Task DevolverAlertaDeAdministracion(DateOnly dia, string motivo, string servicio, CancellationToken cancelacion = default)
        {
            _alertas[(dia, motivo, servicio)] = Math.Max(0, _alertas.GetValueOrDefault((dia, motivo, servicio)) - 1);
            return Task.CompletedTask;
        }

        public int AlertasContadas(DateOnly dia, string motivo, string servicio) => _alertas.GetValueOrDefault((dia, motivo, servicio));

        public Task<bool> ReservarAlertaDeAdministracion(DateOnly dia, string motivo, string servicio, int tope, CancellationToken cancelacion = default)
        {
            var enviadas = _alertas.GetValueOrDefault((dia, motivo, servicio));
            if (enviadas >= tope)
            {
                return Task.FromResult(false);
            }

            _alertas[(dia, motivo, servicio)] = enviadas + 1;
            return Task.FromResult(true);
        }
    }

    private sealed record Entorno(CuotaDeCorreo Cuota, AlmacenEnMemoria Almacen, BabelHub.Ecommerce.UnitTests.Web.AlertasTecnicasEspia Alertas, RelojFijo Reloj);

    private static Entorno Crear(ConfiguracionDeCuotaDeCorreo? configuracion = null)
    {
        var almacen = new AlmacenEnMemoria();
        var alertas = new BabelHub.Ecommerce.UnitTests.Web.AlertasTecnicasEspia();
        var reloj = new RelojFijo(Ahora);
        return new Entorno(new CuotaDeCorreo(new PoliticaDeCuotaDeCorreo(configuracion ?? ConfiguracionDeCuotaDeCorreo.Predeterminada), almacen, alertas, reloj), almacen, alertas, reloj);
    }

    private static CorreoSaliente Correo(CategoriaDeCorreo categoria, string? motivo = null, string? servicio = null) =>
        new(categoria, "persona@example.com", "aviso", motivo: motivo, servicio: servicio);

    private static async Task Agotar(Entorno e, CategoriaDeCorreo categoria, int veces, string? motivo = null, string? servicio = null)
    {
        for (var i = 0; i < veces; i++)
        {
            var decision = await e.Cuota.Decidir(Correo(categoria, motivo ?? $"m{i}", servicio ?? "ecommerce"));
            Assert.Equal(ResultadoDeEnvio.Enviado, decision.Resultado);
        }
    }

    // ---------- Configuración ----------

    [Fact]
    [Trait("Criterio", "AC-IDE-55")]
    public void LosValoresIniciales_SonLosDeLaDecisionPrevia_ConCuposQueSumanCien()
    {
        var configuracion = ConfiguracionDeCuotaDeCorreo.Predeterminada.Validar();

        Assert.Equal(300, configuracion.TotalDiario);
        Assert.Equal([45, 45, 75, 135], new[] { CupoDeCorreo.Administracion, CupoDeCorreo.CriticasDeCuenta, CupoDeCorreo.Codigos, CupoDeCorreo.Resto }.Select(configuracion.Tamano));
        Assert.Equal(24, configuracion.TopeDeAlertasPorMotivoYServicio);
        Assert.Equal(80, configuracion.PorcentajeDeAlerta);
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-60")]
    [InlineData(0, 15, 15, 25, 45)]
    [InlineData(300, 15, 15, 25, 46)]
    [InlineData(300, 0, 15, 25, 45)]
    [InlineData(300, 15, 15, 25, 101)]
    [InlineData(300, -5, 15, 25, 45)]
    public void UnaConfiguracionQueNoCuadra_FallaAlArrancar(int total, int administracion, int criticas, int codigos, int resto)
    {
        var configuracion = new ConfiguracionDeCuotaDeCorreo
        {
            TotalDiario = total,
            PorcentajeDeAdministracion = administracion,
            PorcentajeDeCriticas = criticas,
            PorcentajeDeCodigos = codigos,
            PorcentajeDeResto = resto,
        };

        Assert.Throws<ArgumentException>(() => new PoliticaDeCuotaDeCorreo(configuracion));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-60")]
    public void CambiarLaCuotaOLosCupos_CambiaElComportamientoSinTocarElCodigo()
    {
        var pequena = new ConfiguracionDeCuotaDeCorreo { TotalDiario = 20, PorcentajeDeAdministracion = 10, PorcentajeDeCriticas = 10, PorcentajeDeCodigos = 30, PorcentajeDeResto = 50 };
        var politica = new PoliticaDeCuotaDeCorreo(pequena);

        Assert.Equal([2, 10], politica.Candidatos(ClaseDeCorreoParaCuota.AlertaALaAdministracion).Select(c => c.Tamano));
        Assert.Equal(6, Assert.Single(politica.Candidatos(ClaseDeCorreoParaCuota.Codigo)).Tamano);
    }

    // ---------- Qué cupos usa cada categoría ----------

    [Fact]
    [Trait("Criterio", "AC-IDE-60")]
    public void CadaCategoriaUsaSoloSusCupos_YNingunAvisoDeUsuarioLlegaAlCupoDeLaAdministracion()
    {
        var politica = new PoliticaDeCuotaDeCorreo(ConfiguracionDeCuotaDeCorreo.Predeterminada);

        Assert.Equal([CupoDeCorreo.Administracion, CupoDeCorreo.Resto], politica.Candidatos(ClaseDeCorreoParaCuota.AlertaALaAdministracion).Select(c => c.Cupo));
        Assert.Equal([CupoDeCorreo.CriticasDeCuenta, CupoDeCorreo.Resto], politica.Candidatos(ClaseDeCorreoParaCuota.AlertaCriticaDeCuenta).Select(c => c.Cupo));
        Assert.Equal([CupoDeCorreo.Codigos], politica.Candidatos(ClaseDeCorreoParaCuota.Codigo).Select(c => c.Cupo));
        foreach (var clase in new[] { ClaseDeCorreoParaCuota.AvisoNoCritico, ClaseDeCorreoParaCuota.Reclamo, ClaseDeCorreoParaCuota.Pago })
        {
            Assert.Equal([CupoDeCorreo.Resto], politica.Candidatos(clase).Select(c => c.Cupo));
        }

        foreach (var clase in Enum.GetValues<ClaseDeCorreoParaCuota>().Where(c => c != ClaseDeCorreoParaCuota.AlertaALaAdministracion))
        {
            Assert.DoesNotContain(CupoDeCorreo.Administracion, politica.Candidatos(clase).Select(c => c.Cupo));
        }

        foreach (var clase in Enum.GetValues<ClaseDeCorreoParaCuota>().Where(c => c != ClaseDeCorreoParaCuota.Codigo))
        {
            Assert.DoesNotContain(CupoDeCorreo.Codigos, politica.Candidatos(clase).Select(c => c.Cupo));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-60")]
    public void SoloLosCodigosYLosAvisosNoCriticosSeSuprimen_LoDemasSeEncolaConPrioridad()
    {
        Assert.Equal(SinCupoDeCorreo.Suprimir, PoliticaDeCuotaDeCorreo.SinCupo(ClaseDeCorreoParaCuota.Codigo));
        Assert.Equal(SinCupoDeCorreo.Suprimir, PoliticaDeCuotaDeCorreo.SinCupo(ClaseDeCorreoParaCuota.AvisoNoCritico));
        foreach (var clase in new[] { ClaseDeCorreoParaCuota.AlertaALaAdministracion, ClaseDeCorreoParaCuota.AlertaCriticaDeCuenta, ClaseDeCorreoParaCuota.Reclamo, ClaseDeCorreoParaCuota.Pago })
        {
            Assert.Equal(SinCupoDeCorreo.Encolar, PoliticaDeCuotaDeCorreo.SinCupo(clase));
        }

        Assert.True(PoliticaDeCuotaDeCorreo.Prioridad(ClaseDeCorreoParaCuota.AlertaALaAdministracion) < PoliticaDeCuotaDeCorreo.Prioridad(ClaseDeCorreoParaCuota.AlertaCriticaDeCuenta));
        Assert.True(PoliticaDeCuotaDeCorreo.Prioridad(ClaseDeCorreoParaCuota.AlertaCriticaDeCuenta) < PoliticaDeCuotaDeCorreo.Prioridad(ClaseDeCorreoParaCuota.Reclamo));
        Assert.Equal(PoliticaDeCuotaDeCorreo.Prioridad(ClaseDeCorreoParaCuota.Reclamo), PoliticaDeCuotaDeCorreo.Prioridad(ClaseDeCorreoParaCuota.Pago));
        Assert.Throws<ArgumentOutOfRangeException>(() => PoliticaDeCuotaDeCorreo.Prioridad(ClaseDeCorreoParaCuota.Codigo));
    }

    // ---------- La cuota aplicada ----------

    [Fact]
    [Trait("Criterio", "AC-IDE-55")]
    public async Task CuandoLosCodigosAgotanSuCupo_LosSiguientesSeSuprimen_YLasAlertasSiguenSaliendoConElSuyo()
    {
        var e = Crear();
        for (var i = 0; i < 75; i++)
        {
            Assert.Equal(ResultadoDeEnvio.Enviado, (await e.Cuota.DecidirCodigo()).Resultado);
        }

        Assert.Equal(ResultadoDeEnvio.Suprimido, (await e.Cuota.DecidirCodigo()).Resultado);

        Assert.Equal(ResultadoDeEnvio.Enviado, (await e.Cuota.Decidir(Correo(CategoriaDeCorreo.AlertaALaAdministracion, "accesos-denegados", "ecommerce"))).Resultado);
        Assert.Equal(ResultadoDeEnvio.Enviado, (await e.Cuota.Decidir(Correo(CategoriaDeCorreo.AlertaCriticaDeCuenta))).Resultado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-60")]
    public async Task ConLaCuotaAgotadaPorAlertasCriticasYAvisos_LaAlertaALaAdministracionSaleConSuCupoExclusivo()
    {
        var e = Crear();
        await Agotar(e, CategoriaDeCorreo.AlertaCriticaDeCuenta, 45 + 135);

        // Las críticas se llevaron su cupo y el resto: el aviso no crítico ya no tiene dónde, pero la administración conserva el suyo.
        Assert.Equal(ResultadoDeEnvio.Suprimido, (await e.Cuota.Decidir(Correo(CategoriaDeCorreo.AvisoDeSeguridad))).Resultado);
        var alerta = await e.Cuota.Decidir(Correo(CategoriaDeCorreo.AlertaALaAdministracion, "accesos-denegados", "ecommerce"));

        Assert.Equal(ResultadoDeEnvio.Enviado, alerta.Resultado);
        Assert.Equal(CupoDeCorreo.Administracion, alerta.Reserva!.Cupo);
        Assert.Equal(0, e.Almacen.Usados(e.Reloj.HoyEnLima, CupoDeCorreo.Codigos));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-60")]
    public async Task LosReclamosYLosPagosSinCupo_QuedanEncoladosSinPerderse_ConPrioridadMenorQueLasAlertas()
    {
        var e = Crear();
        await Agotar(e, CategoriaDeCorreo.Pago, 135);

        var reclamo = await e.Cuota.Decidir(Correo(CategoriaDeCorreo.RespuestaDeReclamo));
        var pago = await e.Cuota.Decidir(Correo(CategoriaDeCorreo.Pago));

        Assert.Equal(ResultadoDeEnvio.Encolado, reclamo.Resultado);
        Assert.Equal(ResultadoDeEnvio.Encolado, pago.Resultado);
        Assert.Null(reclamo.Reserva);
        Assert.True(reclamo.Prioridad > PoliticaDeCuotaDeCorreo.Prioridad(ClaseDeCorreoParaCuota.AlertaCriticaDeCuenta));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-60")]
    public async Task UnaAlertaALaAdministracionSinCupoUtilizable_QuedaPendiente_YUnaCriticaConSuCupoAgotadoNoSeSuprime()
    {
        var e = Crear();
        await Agotar(e, CategoriaDeCorreo.Pago, 135);
        await Agotar(e, CategoriaDeCorreo.AlertaALaAdministracion, 45, motivo: null);

        var administracion = await e.Cuota.Decidir(Correo(CategoriaDeCorreo.AlertaALaAdministracion, "nuevo-motivo", "ecommerce"));
        await Agotar(e, CategoriaDeCorreo.AlertaCriticaDeCuenta, 45);
        var critica = await e.Cuota.Decidir(Correo(CategoriaDeCorreo.AlertaCriticaDeCuenta));

        Assert.Equal(ResultadoDeEnvio.Encolado, administracion.Resultado);
        Assert.Equal(PoliticaDeCuotaDeCorreo.Prioridad(ClaseDeCorreoParaCuota.AlertaALaAdministracion), administracion.Prioridad);
        Assert.Equal(ResultadoDeEnvio.Encolado, critica.Resultado);
        Assert.True(administracion.Prioridad < critica.Prioridad);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-62")]
    public async Task LasAlertasALaAdministracion_NoPasanDe24AlDiaPorMotivoYServicio_YElRestoNoSeCuentaContraElOtroMotivo()
    {
        var e = Crear();
        for (var i = 0; i < 24; i++)
        {
            Assert.Equal(ResultadoDeEnvio.Enviado, (await e.Cuota.Decidir(Correo(CategoriaDeCorreo.AlertaALaAdministracion, "ingresos-fallidos", "ecommerce"))).Resultado);
        }

        Assert.Equal(ResultadoDeEnvio.Suprimido, (await e.Cuota.Decidir(Correo(CategoriaDeCorreo.AlertaALaAdministracion, "ingresos-fallidos", "ecommerce"))).Resultado);
        Assert.Equal(ResultadoDeEnvio.Enviado, (await e.Cuota.Decidir(Correo(CategoriaDeCorreo.AlertaALaAdministracion, "ingresos-fallidos", "inventario"))).Resultado);
        Assert.Equal(ResultadoDeEnvio.Enviado, (await e.Cuota.Decidir(Correo(CategoriaDeCorreo.AlertaALaAdministracion, "accesos-denegados", "ecommerce"))).Resultado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-23")]
    public async Task UnaAlertaQueSeRepiteTrasCadaVentanaDe15Minutos_NoPasaDe24CorreosAlDiaPorMotivoYServicio_YSeRenuevaAlDiaSiguienteDeLima()
    {
        // SEG-27: una alerta por ventana de 15 minutos (la agrupación es de Auditoría, AC-AUD-26) pide un correo; a partir del 24 del día
        // por motivo y servicio la cuota lo suprime y la alerta sigue en la lista sin correo. Aquí se ejerce el tope contra la cuota real.
        var e = Crear();
        var resultados = new List<ResultadoDeEnvio>();
        for (var i = 0; i < 30; i++)
        {
            e.Reloj.Ahora = Ahora.AddMinutes(i * 16);
            resultados.Add((await e.Cuota.Decidir(Correo(CategoriaDeCorreo.AlertaALaAdministracion, "accesos-denegados", "inventario"))).Resultado);
        }

        Assert.Equal(24, resultados.Count(r => r == ResultadoDeEnvio.Enviado));
        Assert.All(resultados.Skip(24), r => Assert.Equal(ResultadoDeEnvio.Suprimido, r));
        Assert.Equal(ResultadoDeEnvio.Enviado, (await e.Cuota.Decidir(Correo(CategoriaDeCorreo.AlertaALaAdministracion, "accesos-denegados", "ecommerce"))).Resultado);

        e.Reloj.Ahora = Ahora.AddDays(1);

        Assert.Equal(ResultadoDeEnvio.Enviado, (await e.Cuota.Decidir(Correo(CategoriaDeCorreo.AlertaALaAdministracion, "accesos-denegados", "inventario"))).Resultado);
    }

    [Fact]
    public async Task LaCuotaSeRenuevaCadaDiaDeLima()
    {
        var e = Crear();
        for (var i = 0; i < 75; i++)
        {
            await e.Cuota.DecidirCodigo();
        }

        Assert.Equal(ResultadoDeEnvio.Suprimido, (await e.Cuota.DecidirCodigo()).Resultado);

        e.Reloj.Ahora = Ahora.AddDays(1);

        Assert.Equal(ResultadoDeEnvio.Enviado, (await e.Cuota.DecidirCodigo()).Resultado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-55")]
    public async Task SiElEnvioFalla_SeDevuelveElCupo_YLaCuotaSoloCuentaLoQueSalio()
    {
        var e = Crear();
        var decision = await e.Cuota.DecidirCodigo();
        Assert.Equal(1, e.Almacen.Usados(e.Reloj.HoyEnLima, CupoDeCorreo.Codigos));

        await e.Cuota.Devolver(decision.Reserva!);

        Assert.Equal(0, e.Almacen.Usados(e.Reloj.HoyEnLima, CupoDeCorreo.Codigos));
    }

    // ---------- Alertas del 80 % ----------

    [Fact]
    [Trait("Criterio", "AC-IDE-62")]
    public async Task AlLlegarElCupoDeCodigosAl80PorCiento_SePublicaUnaAlertaTecnica_UnaSolaVez()
    {
        var e = Crear();
        for (var i = 0; i < 59; i++)
        {
            await e.Cuota.DecidirCodigo();
        }

        Assert.Empty(e.Alertas.Alertas);

        await e.Cuota.DecidirCodigo();
        await e.Cuota.DecidirCodigo();

        Assert.Equal([CuotaDeCorreo.MotivoDeCupoDeCodigosAlto], e.Alertas.Alertas.Select(a => a.Motivo));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-55")]
    public async Task AlLlegarLaCuotaTotalAl80PorCiento_SePublicaUnaAlertaTecnica_UnaSolaVez()
    {
        var e = Crear();
        await Agotar(e, CategoriaDeCorreo.Pago, 135);
        await Agotar(e, CategoriaDeCorreo.AlertaCriticaDeCuenta, 45);
        for (var i = 0; i < 60; i++)
        {
            await e.Cuota.DecidirCodigo();
        }

        // 135 + 45 + 60 = 240 = 80 % de 300: justo en el envío 240 se cruza.
        Assert.Contains(CuotaDeCorreo.MotivoDeCuotaAlta, e.Alertas.Alertas.Select(a => a.Motivo));
        Assert.Equal(1, e.Alertas.Alertas.Count(a => a.Motivo == CuotaDeCorreo.MotivoDeCuotaAlta));

        await e.Cuota.DecidirCodigo();
        Assert.Equal(1, e.Alertas.Alertas.Count(a => a.Motivo == CuotaDeCorreo.MotivoDeCuotaAlta));
    }

    [Fact]
    public async Task UnaAlertaQueNoSePuedePublicar_NoImpideElCorreo()
    {
        var almacen = new AlmacenEnMemoria();
        var cuota = new CuotaDeCorreo(new PoliticaDeCuotaDeCorreo(new ConfiguracionDeCuotaDeCorreo { TotalDiario = 5, PorcentajeDeCodigos = 25, PorcentajeDeAdministracion = 25, PorcentajeDeCriticas = 25, PorcentajeDeResto = 25 }), almacen, new AlertasQueFallan(), new RelojFijo(Ahora));

        var decision = await cuota.DecidirCodigo();

        Assert.Equal(ResultadoDeEnvio.Enviado, decision.Resultado);
    }

    private sealed class AlertasQueFallan : IPublicadorDeAlertasTecnicas
    {
        public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default) => throw new InvalidOperationException("Sin registro técnico.");
    }

    // ---------- El contrato ----------

    [Fact]
    public void UnCorreoSaliente_ExigeUnaSolaDireccion_UnaPlantillaYElMotivoDeLaAlertaALaAdministracion()
    {
        Assert.Throws<ArgumentException>(() => new CorreoSaliente(CategoriaDeCorreo.Pago, "a@example.com, b@example.com", "pago"));
        Assert.Throws<ArgumentException>(() => new CorreoSaliente(CategoriaDeCorreo.Pago, "sin-arroba", "pago"));
        Assert.Throws<ArgumentException>(() => new CorreoSaliente(CategoriaDeCorreo.Pago, "a@example.com", ""));
        Assert.Throws<ArgumentException>(() => new CorreoSaliente(CategoriaDeCorreo.AlertaALaAdministracion, "a@example.com", "alerta"));
        Assert.Throws<ArgumentException>(() => new CorreoSaliente(CategoriaDeCorreo.CodigoDeVerificacionORecuperacion, "a@example.com", "codigo"));
        Assert.DoesNotContain("example.com", new CorreoSaliente(CategoriaDeCorreo.Pago, "a@example.com", "pago").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CadaCategoriaDeCorreoTieneUnaClaseDeCuota()
    {
        foreach (var categoria in Enum.GetValues<CategoriaDeCorreo>())
        {
            _ = CuotaDeCorreo.Clase(categoria);
        }
    }

    // ---------- Correcciones de la revisión de seguridad ----------

    [Fact]
    [Trait("Criterio", "AC-IDE-62")]
    public async Task UnaAlertaQueSeEncolaPorFaltaDeCupo_NoGastaElTopeDelMotivo()
    {
        var e = new Entorno(
            new CuotaDeCorreo(new PoliticaDeCuotaDeCorreo(ConfiguracionDeCuotaDeCorreo.Predeterminada), new AlmacenEnMemoria(), new BabelHub.Ecommerce.UnitTests.Web.AlertasTecnicasEspia(), new RelojFijo(Ahora)),
            new AlmacenEnMemoria(),
            new BabelHub.Ecommerce.UnitTests.Web.AlertasTecnicasEspia(),
            new RelojFijo(Ahora));
        e = Crear();
        await Agotar(e, CategoriaDeCorreo.Pago, 135);
        await Agotar(e, CategoriaDeCorreo.AlertaALaAdministracion, 45, motivo: null);

        var encolada = await e.Cuota.Decidir(Correo(CategoriaDeCorreo.AlertaALaAdministracion, "nuevo-motivo", "ecommerce"));

        Assert.Equal(ResultadoDeEnvio.Encolado, encolada.Resultado);
        Assert.Equal(0, e.Almacen.AlertasContadas(e.Reloj.HoyEnLima, "nuevo-motivo", "ecommerce"));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-62")]
    public async Task SiElEnvioDeUnaAlertaFalla_SeDevuelveElCupoYElTopeDelMotivo()
    {
        var e = Crear();
        var decision = await e.Cuota.Decidir(Correo(CategoriaDeCorreo.AlertaALaAdministracion, "ingresos-fallidos", "ecommerce"));
        Assert.Equal(1, e.Almacen.AlertasContadas(e.Reloj.HoyEnLima, "ingresos-fallidos", "ecommerce"));

        await e.Cuota.Devolver(decision.Reserva!);

        Assert.Equal(0, e.Almacen.AlertasContadas(e.Reloj.HoyEnLima, "ingresos-fallidos", "ecommerce"));
        Assert.Equal(0, e.Almacen.Usados(e.Reloj.HoyEnLima, CupoDeCorreo.Administracion));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-62")]
    public async Task AlLlegarAlTopeDelMotivo_ElCupoReservadoSeDevuelve_YLaAlertaQuedaSinCorreo()
    {
        var e = Crear();
        for (var i = 0; i < 24; i++)
        {
            Assert.Equal(ResultadoDeEnvio.Enviado, (await e.Cuota.Decidir(Correo(CategoriaDeCorreo.AlertaALaAdministracion, "accesos-denegados", "ecommerce"))).Resultado);
        }

        var usadosAntes = e.Almacen.Usados(e.Reloj.HoyEnLima, CupoDeCorreo.Administracion);
        var vigesimoquinta = await e.Cuota.Decidir(Correo(CategoriaDeCorreo.AlertaALaAdministracion, "accesos-denegados", "ecommerce"));

        Assert.Equal(ResultadoDeEnvio.Suprimido, vigesimoquinta.Resultado);
        Assert.Equal(usadosAntes, e.Almacen.Usados(e.Reloj.HoyEnLima, CupoDeCorreo.Administracion));
    }
}
