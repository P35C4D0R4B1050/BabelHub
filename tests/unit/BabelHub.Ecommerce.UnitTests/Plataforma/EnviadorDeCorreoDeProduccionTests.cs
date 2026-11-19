using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.Ecommerce.UnitTests.Web;

namespace BabelHub.Ecommerce.UnitTests.Plataforma;

/// <summary>
/// El enviador de correo de producción y el reintento de lo pendiente (identidad-seguridad T34; AC-IDE-54, AC-IDE-55, AC-IDE-60, AC-SEG-23; IDE-65, IDE-66, IDE-73, IDE-78, IDE-79, IDE-80, IDE-82): todo correo cuenta contra la
/// cuota, el cupo se devuelve si el transporte falla, lo que se reintenta se encola con su prioridad, los códigos no tienen cola ni reintento, el tope de 24 al día por motivo y servicio suprime el resto y nada
/// de esto lanza a quien llama.
/// </summary>
public sealed class EnviadorDeCorreoDeProduccionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 15, 15, 0, 0, TimeSpan.Zero);

    private sealed class AlmacenEnMemoria : IAlmacenDeCuotaDeCorreo
    {
        private readonly Dictionary<(DateOnly, CupoDeCorreo), int> _usados = [];
        private readonly Dictionary<(DateOnly, string, string), int> _alertas = [];

        public bool Cae { get; set; }

        public int Usados(DateOnly dia, CupoDeCorreo cupo) => _usados.GetValueOrDefault((dia, cupo));

        public int Total(DateOnly dia) => _usados.Where(u => u.Key.Item1 == dia).Sum(u => u.Value);

        public int AlertasContadas(DateOnly dia, string motivo, string servicio) => _alertas.GetValueOrDefault((dia, motivo, servicio));

        public Task<ReservaDeCupoDeCorreo?> Reservar(DateOnly dia, IReadOnlyList<CupoCandidato> candidatos, CancellationToken cancelacion = default)
        {
            if (Cae)
            {
                throw new InvalidOperationException("El almacén de la cuota no responde.");
            }

            foreach (var candidato in candidatos)
            {
                var usados = _usados.GetValueOrDefault((dia, candidato.Cupo));
                if (usados < candidato.Tamano)
                {
                    _usados[(dia, candidato.Cupo)] = usados + 1;
                    return Task.FromResult<ReservaDeCupoDeCorreo?>(new ReservaDeCupoDeCorreo(candidato.Cupo, usados + 1, Total(dia)));
                }
            }

            return Task.FromResult<ReservaDeCupoDeCorreo?>(null);
        }

        public Task Devolver(DateOnly dia, CupoDeCorreo cupo, CancellationToken cancelacion = default)
        {
            _usados[(dia, cupo)] = Math.Max(0, _usados.GetValueOrDefault((dia, cupo)) - 1);
            return Task.CompletedTask;
        }

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

        public Task DevolverAlertaDeAdministracion(DateOnly dia, string motivo, string servicio, CancellationToken cancelacion = default)
        {
            _alertas[(dia, motivo, servicio)] = Math.Max(0, _alertas.GetValueOrDefault((dia, motivo, servicio)) - 1);
            return Task.CompletedTask;
        }
    }

    private sealed class TransporteDePrueba : ITransporteDeCorreo
    {
        public List<MensajeDeCorreo> Enviados { get; } = [];

        public bool Falla { get; set; }

        /// <summary>Falla solo los mensajes cuyo asunto es este.</summary>
        public string? FallaElAsunto { get; set; }

        public Task Enviar(MensajeDeCorreo mensaje, CancellationToken cancelacion = default)
        {
            if (Falla || (FallaElAsunto is not null && mensaje.Asunto == FallaElAsunto))
            {
                throw new InvalidOperationException("SMTP caído.");
            }

            Enviados.Add(mensaje);
            return Task.CompletedTask;
        }
    }

    private sealed class RenderizadorDePrueba : IRenderizadorDeCorreo
    {
        public MensajeDeCorreo De(CorreoSaliente correo) => new([correo.Destinatario], $"{correo.Categoria}:{correo.Plantilla}:{correo.Motivo}", "cuerpo");

        public MensajeDeCorreo DeCodigo(CorreoDeCodigo correo) => new([correo.Destinatario], "codigo", "cuerpo");

        public MensajeDeCorreo DeAlerta(string servicio, string motivo, DateTimeOffset primeraOcurrencia, int ocurrencias, IReadOnlyList<string> direcciones) => new(direcciones, $"alerta:{motivo}", "cuerpo");
    }

    private sealed class ColaEnMemoria : IColaDeCorreosPendientes
    {
        public List<CorreoPendiente> Items { get; } = [];

        public bool Falla { get; set; }

        public Dictionary<Guid, DateTimeOffset> Proximos { get; } = [];

        public Task Agregar(CorreoSaliente correo, int prioridad, DateTimeOffset proximoIntento, CancellationToken cancelacion = default)
        {
            if (Falla)
            {
                throw new InvalidOperationException("La cola no responde.");
            }

            var item = new CorreoPendiente(Guid.NewGuid(), correo, prioridad, 0, proximoIntento);
            Items.Add(item);
            Proximos[item.Id] = proximoIntento;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CorreoPendiente>> Vencidos(DateTimeOffset ahora, int limite, CancellationToken cancelacion = default) =>
            Task.FromResult<IReadOnlyList<CorreoPendiente>>([.. Items.Where(i => Proximos[i.Id] <= ahora).OrderBy(i => i.Prioridad).ThenBy(i => i.Creado).Take(limite)]);

        public Task Eliminar(Guid id, CancellationToken cancelacion = default)
        {
            Items.RemoveAll(i => i.Id == id);
            return Task.CompletedTask;
        }

        public Task Reprogramar(Guid id, int intentos, DateTimeOffset proximoIntento, CancellationToken cancelacion = default)
        {
            var indice = Items.FindIndex(i => i.Id == id);
            Items[indice] = Items[indice] with { Intentos = intentos };
            Proximos[id] = proximoIntento;
            return Task.CompletedTask;
        }
    }

    private sealed record Entorno(
        EnviadorDeCorreoDeProduccion Enviador,
        ReintentoDeCorreosPendientes Reintento,
        AlmacenEnMemoria Almacen,
        TransporteDePrueba Transporte,
        ColaEnMemoria Cola,
        AlertasTecnicasEspia Alertas,
        RelojFijo Reloj);

    private static Entorno Crear(ConfiguracionDeCuotaDeCorreo? configuracion = null)
    {
        var almacen = new AlmacenEnMemoria();
        var transporte = new TransporteDePrueba();
        var cola = new ColaEnMemoria();
        var alertas = new AlertasTecnicasEspia();
        var reloj = new RelojFijo(Ahora);
        var cuota = new CuotaDeCorreo(new PoliticaDeCuotaDeCorreo(configuracion ?? ConfiguracionDeCuotaDeCorreo.Predeterminada), almacen, alertas, reloj);
        var plantillas = new RenderizadorDePrueba();
        return new Entorno(
            new EnviadorDeCorreoDeProduccion(cuota, transporte, plantillas, cola, alertas, reloj),
            new ReintentoDeCorreosPendientes(cuota, transporte, plantillas, cola, reloj),
            almacen,
            transporte,
            cola,
            alertas,
            reloj);
    }

    private static CorreoSaliente Correo(CategoriaDeCorreo categoria, string? motivo = null, string? servicio = null) =>
        new(categoria, "persona@example.com", "plantilla", motivo: motivo, servicio: servicio);

    private static CorreoSaliente Alerta(string motivo = "motivo-a", string servicio = "Ecommerce") => Correo(CategoriaDeCorreo.AlertaALaAdministracion, motivo, servicio);

    private static CorreoDeCodigo Codigo() => CorreoDeCodigo.ParaElTitular("persona@example.com", PropositoDeCodigo.VerificacionDeCuenta, new ValorSecreto("123456"));

    private static DateOnly Hoy(Entorno e) => e.Reloj.HoyEnLima;

    // ---------------------------------------------------------------- salir

    [Fact]
    [Trait("Criterio", "AC-IDE-60")]
    public async Task UnCorreoConCupo_SaleEntregadoPorElTransporte_YCuentaContraLaCuota()
    {
        var e = Crear();

        var resultado = await e.Enviador.Enviar(Correo(CategoriaDeCorreo.Pago));

        Assert.Equal(ResultadoDeEnvio.Enviado, resultado);
        Assert.Single(e.Transporte.Enviados);
        Assert.Equal(1, e.Almacen.Total(Hoy(e)));
        Assert.Empty(e.Cola.Items);
    }

    [Theory]
    [InlineData(CategoriaDeCorreo.Pago, 2)]
    [InlineData(CategoriaDeCorreo.RespuestaDeReclamo, 2)]
    [InlineData(CategoriaDeCorreo.AlertaCriticaDeCuenta, 1)]
    [InlineData(CategoriaDeCorreo.AlertaALaAdministracion, 0)]
    [Trait("Criterio", "AC-IDE-60")]
    public async Task SiElTransporteFalla_ElCupoSeDevuelve_ElCorreoQuedaEncoladoConSuPrioridad_YNadaLanza(CategoriaDeCorreo categoria, int prioridad)
    {
        var e = Crear();
        e.Transporte.Falla = true;

        var resultado = await e.Enviador.Enviar(Correo(categoria, motivo: "motivo-a", servicio: "Ecommerce"));

        Assert.Equal(ResultadoDeEnvio.Encolado, resultado);
        Assert.Equal(0, e.Almacen.Total(Hoy(e)));
        Assert.Equal(prioridad, Assert.Single(e.Cola.Items).Prioridad);
        Assert.Equal(EnviadorDeCorreoDeProduccion.MotivoDeFallaDelCorreo, Assert.Single(e.Alertas.Alertas.Select(a => a.Motivo)));
        Assert.Equal(0, e.Almacen.AlertasContadas(Hoy(e), "motivo-a", "Ecommerce"));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-54")]
    public async Task UnAvisoNoCriticoQueFalla_SeSuprime_SinColaYDevolviendoElCupo()
    {
        var e = Crear();
        e.Transporte.Falla = true;

        var resultado = await e.Enviador.Enviar(Correo(CategoriaDeCorreo.AvisoDeSeguridad));

        Assert.Equal(ResultadoDeEnvio.Suprimido, resultado);
        Assert.Empty(e.Cola.Items);
        Assert.Equal(0, e.Almacen.Total(Hoy(e)));
    }

    // ---------------------------------------------------------------- sin cupo

    [Fact]
    [Trait("Criterio", "AC-IDE-60")]
    public async Task SinCupo_UnPagoQuedaEnLaCola_SinLlamarAlTransporte_YUnAvisoNoCriticoSeSuprime()
    {
        var e = Crear(ConfiguracionDeCuotaDeCorreo.Predeterminada with { TotalDiario = 1 });
        Assert.Equal(ResultadoDeEnvio.Enviado, await e.Enviador.Enviar(Correo(CategoriaDeCorreo.Pago)));
        e.Transporte.Enviados.Clear();

        var pago = await e.Enviador.Enviar(Correo(CategoriaDeCorreo.Pago));
        var reclamo = await e.Enviador.Enviar(Correo(CategoriaDeCorreo.RespuestaDeReclamo));
        var aviso = await e.Enviador.Enviar(Correo(CategoriaDeCorreo.AvisoDeSeguridad));

        Assert.Equal(ResultadoDeEnvio.Encolado, pago);
        Assert.Equal(ResultadoDeEnvio.Encolado, reclamo);
        Assert.Equal(ResultadoDeEnvio.Suprimido, aviso);
        Assert.Empty(e.Transporte.Enviados);
        Assert.Equal(2, e.Cola.Items.Count);
    }

    // ---------------------------------------------------------------- el tope de 24 al día (IDE-82, AC-SEG-23)

    [Fact]
    [Trait("Criterio", "AC-SEG-23")]
    public async Task LasAlertasDeUnMismoMotivoYServicio_SalenHastaElTopeDe24_ElRestoSeSuprime_YOtroMotivoSigueSaliendo()
    {
        var e = Crear();

        var resultados = new List<ResultadoDeEnvio>();
        for (var i = 0; i < 30; i++)
        {
            resultados.Add(await e.Enviador.Enviar(Alerta("motivo-a")));
        }

        Assert.Equal(24, resultados.Count(r => r == ResultadoDeEnvio.Enviado));
        Assert.Equal(6, resultados.Count(r => r == ResultadoDeEnvio.Suprimido));
        Assert.Equal(24, e.Transporte.Enviados.Count);
        Assert.Empty(e.Cola.Items);
        Assert.Equal(ResultadoDeEnvio.Enviado, await e.Enviador.Enviar(Alerta("motivo-b")));
        Assert.Equal(ResultadoDeEnvio.Enviado, await e.Enviador.Enviar(Alerta("motivo-a", servicio: "Inventario")));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-23")]
    public async Task UnaAlertaQueFallaEnElTransporte_NoGastaElTope()
    {
        var e = Crear();
        e.Transporte.Falla = true;
        for (var i = 0; i < 5; i++)
        {
            await e.Enviador.Enviar(Alerta());
        }

        Assert.Equal(0, e.Almacen.AlertasContadas(Hoy(e), "motivo-a", "Ecommerce"));
    }

    // ---------------------------------------------------------------- códigos (IDE-65)

    [Fact]
    [Trait("Criterio", "AC-IDE-54")]
    public async Task UnCodigo_SaleSinColaNiReintento_YSiElTransporteFalla_ElCupoSeDevuelve_YQuedaLaAlertaTecnica()
    {
        var e = Crear();

        var salio = await e.Enviador.EnviarCodigo(Codigo());
        e.Transporte.Falla = true;
        var fallo = await e.Enviador.EnviarCodigo(Codigo());

        Assert.Equal(ResultadoDeEnvio.Enviado, salio);
        Assert.Equal(ResultadoDeEnvio.Suprimido, fallo);
        Assert.Equal(1, e.Almacen.Total(Hoy(e)));
        Assert.Empty(e.Cola.Items);
        Assert.Equal(EnviadorDeCorreoDeProduccion.MotivoDeFallaDelCorreo, Assert.Single(e.Alertas.Alertas.Select(a => a.Motivo)));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-55")]
    public async Task ElCupoDeCodigos_SeAgota_ElCodigoSeSuprime_ElPagoSigueSaliendo_YLaAlertaDelOchentaPorCientoSaleUnaSolaVez()
    {
        var e = Crear();
        var tamano = ConfiguracionDeCuotaDeCorreo.Predeterminada.Tamano(CupoDeCorreo.Codigos);

        var resultados = new List<ResultadoDeEnvio>();
        for (var i = 0; i < tamano + 5; i++)
        {
            resultados.Add(await e.Enviador.EnviarCodigo(Codigo()));
        }

        Assert.True(resultados.Count(r => r == ResultadoDeEnvio.Enviado) >= tamano);
        Assert.Equal(ResultadoDeEnvio.Suprimido, resultados[^1]);
        Assert.Equal(ResultadoDeEnvio.Enviado, await e.Enviador.Enviar(Correo(CategoriaDeCorreo.Pago)));
        Assert.Single(e.Alertas.Alertas.Select(a => a.Motivo), m => m == CuotaDeCorreo.MotivoDeCupoDeCodigosAlto);
    }

    // ---------------------------------------------------------------- nada lanza

    [Fact]
    public async Task SiElAlmacenDeLaCuotaCae_NadaLanza_UnPagoQuedaEncolado_UnCodigoYUnAvisoSeSuprimen()
    {
        var e = Crear();
        e.Almacen.Cae = true;

        var pago = await e.Enviador.Enviar(Correo(CategoriaDeCorreo.Pago));
        var aviso = await e.Enviador.Enviar(Correo(CategoriaDeCorreo.AvisoDeSeguridad));
        var codigo = await e.Enviador.EnviarCodigo(Codigo());

        Assert.Equal(ResultadoDeEnvio.Encolado, pago);
        Assert.Equal(ResultadoDeEnvio.Suprimido, aviso);
        Assert.Equal(ResultadoDeEnvio.Suprimido, codigo);
        Assert.Empty(e.Transporte.Enviados);
        Assert.Single(e.Cola.Items);
    }

    [Fact]
    public async Task SiLaColaTampocoResponde_NoSePrometeLoQueNoSePuedeCumplir_YNadaLanza()
    {
        var e = Crear();
        e.Transporte.Falla = true;
        e.Cola.Falla = true;

        var resultado = await e.Enviador.Enviar(Correo(CategoriaDeCorreo.Pago));

        Assert.Equal(ResultadoDeEnvio.Suprimido, resultado);
    }

    [Fact]
    public async Task ElMensajeNoImprimeNiLasDireccionesNiElContenido()
    {
        var mensaje = new MensajeDeCorreo(["persona@example.com"], "asunto", "tu código es 123456");

        Assert.DoesNotContain("persona", mensaje.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("123456", mensaje.ToString(), StringComparison.Ordinal);
        await Task.CompletedTask;
    }

    // ---------------------------------------------------------------- reintento (IDE-78, IDE-80)

    [Fact]
    [Trait("Criterio", "AC-IDE-60")]
    public async Task AlRenovarseLaCuota_ElReintentoSaleConPrioridad_LaAdministracionPrimero_LuegoLasCriticas_YPorUltimoReclamosYPagos()
    {
        // Cada cupo vale uno: solo cabe un correo de «todo lo demás» (reclamos y pagos), así se ve cuál sale primero.
        var e = Crear(ConfiguracionDeCuotaDeCorreo.Predeterminada with { TotalDiario = 1 });
        e.Transporte.Falla = true;
        await e.Enviador.Enviar(Correo(CategoriaDeCorreo.Pago, motivo: "pago-0"));
        await e.Enviador.Enviar(Correo(CategoriaDeCorreo.Pago, motivo: "pago-1"));
        await e.Enviador.Enviar(Correo(CategoriaDeCorreo.AlertaCriticaDeCuenta, motivo: "critica-1"));
        await e.Enviador.Enviar(Alerta("alerta-1"));
        await e.Enviador.Enviar(Correo(CategoriaDeCorreo.RespuestaDeReclamo, motivo: "reclamo-1"));
        Assert.Equal(5, e.Cola.Items.Count);
        e.Transporte.Falla = false;
        e.Transporte.Enviados.Clear();

        // El día siguiente en Lima la cuota se renovó.
        e.Reloj.Ahora = Ahora.AddDays(1);
        var resultado = await e.Reintento.Procesar();

        Assert.Equal(3, resultado.Enviados);
        Assert.Equal(2, resultado.SinCupo);
        Assert.StartsWith("AlertaALaAdministracion", e.Transporte.Enviados[0].Asunto, StringComparison.Ordinal);
        Assert.StartsWith("AlertaCriticaDeCuenta", e.Transporte.Enviados[1].Asunto, StringComparison.Ordinal);
        Assert.StartsWith("Pago", e.Transporte.Enviados[2].Asunto, StringComparison.Ordinal);
        Assert.Equal(2, e.Cola.Items.Count);

        // Al otro día salen los que quedaron, de uno en uno.
        e.Reloj.Ahora = Ahora.AddDays(2);
        Assert.Equal(1, (await e.Reintento.Procesar()).Enviados);
        e.Reloj.Ahora = Ahora.AddDays(3);
        Assert.Equal(1, (await e.Reintento.Procesar()).Enviados);
        Assert.Empty(e.Cola.Items);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-60")]
    public async Task ElReintento_EnviaTodoLoPendienteSiHayCuota_YPurgaLoEnviado()
    {
        var e = Crear();
        e.Transporte.Falla = true;
        await e.Enviador.Enviar(Correo(CategoriaDeCorreo.Pago));
        await e.Enviador.Enviar(Alerta("alerta-1"));
        e.Transporte.Falla = false;
        e.Transporte.Enviados.Clear();
        e.Reloj.Ahora = Ahora.AddMinutes(1);

        var resultado = await e.Reintento.Procesar();

        Assert.Equal(2, resultado.Enviados);
        Assert.Empty(e.Cola.Items);
        Assert.StartsWith("AlertaALaAdministracion", e.Transporte.Enviados[0].Asunto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ElReintento_SiUnoFallaEnElTransporte_LoReprogramaConEsperaCreciente_DevuelveElCupo_YNoDetieneLosDemas()
    {
        var e = Crear();
        e.Transporte.Falla = true;
        await e.Enviador.Enviar(Correo(CategoriaDeCorreo.Pago, motivo: "malo"));
        await e.Enviador.Enviar(Correo(CategoriaDeCorreo.Pago, motivo: "bueno"));
        e.Transporte.Falla = false;
        e.Transporte.FallaElAsunto = "Pago:plantilla:malo";
        e.Reloj.Ahora = Ahora.AddMinutes(1);

        var primera = await e.Reintento.Procesar();

        Assert.Equal(1, primera.Enviados);
        Assert.Equal(1, primera.Fallidos);
        var pendiente = Assert.Single(e.Cola.Items);
        Assert.Equal(1, pendiente.Intentos);
        Assert.Equal(e.Reloj.Ahora.AddMinutes(1), e.Cola.Proximos[pendiente.Id]);
        Assert.Equal(1, e.Almacen.Total(Hoy(e)));

        // Antes de que llegue su próximo intento no se vuelve a tocar; después, con el doble de espera si vuelve a fallar.
        Assert.Equal(0, (await e.Reintento.Procesar()).Vencidos);
        e.Reloj.Ahora = e.Reloj.Ahora.AddMinutes(1);
        await e.Reintento.Procesar();
        Assert.Equal(e.Reloj.Ahora.AddMinutes(2), e.Cola.Proximos[Assert.Single(e.Cola.Items).Id]);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-23")]
    public async Task ElReintento_UnaAlertaQuePasoElTopeDelDia_SePurgaSinEnviarse()
    {
        var e = Crear();
        for (var i = 0; i < 24; i++)
        {
            await e.Enviador.Enviar(Alerta("motivo-a"));
        }

        e.Cola.Items.Add(new CorreoPendiente(Guid.NewGuid(), Alerta("motivo-a"), 0, 0, Ahora));
        e.Cola.Proximos[e.Cola.Items[0].Id] = Ahora;
        e.Transporte.Enviados.Clear();

        var resultado = await e.Reintento.Procesar();

        Assert.Equal(0, resultado.Enviados);
        Assert.Empty(e.Cola.Items);
        Assert.Empty(e.Transporte.Enviados);
    }

    [Fact]
    public async Task ElReintento_SiLaCuotaFallaParaUno_ElCorreoSeQuedaEnLaCola_YSigueConLosDemas()
    {
        var e = Crear();
        e.Transporte.Falla = true;
        await e.Enviador.Enviar(Correo(CategoriaDeCorreo.Pago));
        e.Transporte.Falla = false;
        e.Almacen.Cae = true;
        e.Reloj.Ahora = Ahora.AddMinutes(1);

        var resultado = await e.Reintento.Procesar();

        Assert.Equal(1, resultado.Fallidos);
        Assert.Single(e.Cola.Items);
    }

    [Fact]
    public async Task SiQuienLlamaCancelaYElTransporteFalla_ElCupoYElTopeSeDevuelvenIgual_YNadaLanza()
    {
        var e = Crear();
        e.Transporte.Falla = true;
        using var cancelacion = new CancellationTokenSource();
        await cancelacion.CancelAsync();

        var resultado = await e.Enviador.Enviar(Alerta(), cancelacion.Token);

        Assert.True(resultado is ResultadoDeEnvio.Encolado or ResultadoDeEnvio.Suprimido);
        Assert.Equal(0, e.Almacen.Total(Hoy(e)));
        Assert.Equal(0, e.Almacen.AlertasContadas(Hoy(e), "motivo-a", "Ecommerce"));
    }
}
