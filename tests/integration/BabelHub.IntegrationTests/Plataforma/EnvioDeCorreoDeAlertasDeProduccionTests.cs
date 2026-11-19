using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Composicion;

namespace BabelHub.IntegrationTests.Plataforma;

/// <summary>
/// El correo de las alertas a la administración con la cuota real (identidad-seguridad T34; AUD-29, IDE-82; AC-SEG-23): un solo correo con todos los destinatarios cuenta una vez, el tope de 24 al día por motivo y servicio
/// suprime el resto, un transporte caído devuelve el cupo y el tope y se lanza para que Auditoría reintente, y las direcciones salen de las cuentas registradas.
/// </summary>
public sealed class EnvioDeCorreoDeAlertasDeProduccionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 15, 15, 0, 0, TimeSpan.Zero);

    private sealed class Reloj : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = EnvioDeCorreoDeAlertasDeProduccionTests.Ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class Almacen : IAlmacenDeCuotaDeCorreo
    {
        private readonly Dictionary<CupoDeCorreo, int> _usados = [];
        private readonly Dictionary<(string, string), int> _alertas = [];

        public int Usados => _usados.Values.Sum();

        public int Alertas(string motivo, string servicio) => _alertas.GetValueOrDefault((motivo, servicio));

        public Task<ReservaDeCupoDeCorreo?> Reservar(DateOnly dia, IReadOnlyList<CupoCandidato> candidatos, CancellationToken cancelacion = default)
        {
            foreach (var candidato in candidatos)
            {
                var usados = _usados.GetValueOrDefault(candidato.Cupo);
                if (usados < candidato.Tamano)
                {
                    _usados[candidato.Cupo] = usados + 1;
                    return Task.FromResult<ReservaDeCupoDeCorreo?>(new ReservaDeCupoDeCorreo(candidato.Cupo, usados + 1, Usados));
                }
            }

            return Task.FromResult<ReservaDeCupoDeCorreo?>(null);
        }

        public Task Devolver(DateOnly dia, CupoDeCorreo cupo, CancellationToken cancelacion = default)
        {
            _usados[cupo] = Math.Max(0, _usados.GetValueOrDefault(cupo) - 1);
            return Task.CompletedTask;
        }

        public Task<bool> ReservarAlertaDeAdministracion(DateOnly dia, string motivo, string servicio, int tope, CancellationToken cancelacion = default)
        {
            var n = _alertas.GetValueOrDefault((motivo, servicio));
            if (n >= tope)
            {
                return Task.FromResult(false);
            }

            _alertas[(motivo, servicio)] = n + 1;
            return Task.FromResult(true);
        }

        public Task DevolverAlertaDeAdministracion(DateOnly dia, string motivo, string servicio, CancellationToken cancelacion = default)
        {
            _alertas[(motivo, servicio)] = Math.Max(0, _alertas.GetValueOrDefault((motivo, servicio)) - 1);
            return Task.CompletedTask;
        }
    }

    private sealed class Transporte : ITransporteDeCorreo
    {
        public List<MensajeDeCorreo> Enviados { get; } = [];

        public bool Falla { get; set; }

        public Task Enviar(MensajeDeCorreo mensaje, CancellationToken cancelacion = default)
        {
            if (Falla)
            {
                throw new InvalidOperationException("SMTP caído.");
            }

            Enviados.Add(mensaje);
            return Task.CompletedTask;
        }
    }

    private sealed class Plantillas : IRenderizadorDeCorreo
    {
        public MensajeDeCorreo De(CorreoSaliente correo) => new([correo.Destinatario], "x", "x");

        public MensajeDeCorreo DeCodigo(CorreoDeCodigo correo) => new([correo.Destinatario], "x", "x");

        public MensajeDeCorreo DeAlerta(string servicio, string motivo, DateTimeOffset primeraOcurrencia, int ocurrencias, IReadOnlyList<string> direcciones) =>
            new(direcciones, $"alerta {motivo} en {servicio}", $"{ocurrencias} ocurrencias");
    }

    private sealed class Direcciones(Dictionary<Guid, string> cuentas) : IDireccionesDeCuentas
    {
        public Task<string?> DeLaCuenta(Guid cuentaId, CancellationToken cancelacion = default) => Task.FromResult(cuentas.GetValueOrDefault(cuentaId));
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

    private static (EnvioDeCorreoDeAlertasDeProduccion Envio, Almacen Almacen, Transporte Transporte, DestinatarioDeAlerta[] Destinatarios) Crear(ConfiguracionDeCuotaDeCorreo? configuracion = null)
    {
        var almacen = new Almacen();
        var transporte = new Transporte();
        var destinatarios = new[] { new DestinatarioDeAlerta(Guid.NewGuid()), new DestinatarioDeAlerta(Guid.NewGuid()), new DestinatarioDeAlerta(Guid.NewGuid()) };
        var direcciones = new Direcciones(new Dictionary<Guid, string>
        {
            [destinatarios[0].CuentaId] = "admin1@example.com",
            [destinatarios[1].CuentaId] = "admin2@example.com",
        });
        var cuota = new CuotaDeCorreo(new PoliticaDeCuotaDeCorreo(configuracion ?? ConfiguracionDeCuotaDeCorreo.Predeterminada), almacen, new AlertasEspia(), new Reloj());
        return (new EnvioDeCorreoDeAlertasDeProduccion(cuota, transporte, new Plantillas(), direcciones), almacen, transporte, destinatarios);
    }

    private static CorreoDeAlerta Alerta(string motivo = "traslado-vinculado-sin-aprobar", ServicioAuditado servicio = ServicioAuditado.Inventario) =>
        new(servicio, new CodigoDeAlerta(motivo), ClaseDeAlertaConsultada.Tecnica, Ahora, 3);

    [Fact]
    [Trait("Criterio", "AC-SEG-23")]
    public async Task UnCorreoConVariosDestinatarios_CuentaUnaVezContraElCupoYElTope_YSoloLlevaLasDireccionesRegistradas()
    {
        var (envio, almacen, transporte, destinatarios) = Crear();

        var resultado = await envio.Enviar(Alerta(), destinatarios);

        Assert.Equal(ResultadoDeEnvioDeAlerta.Enviado, resultado);
        var mensaje = Assert.Single(transporte.Enviados);
        // Dos de las tres cuentas tienen dirección (la tercera no está activa): se envía a las dos, sin duplicar el correo por destinatario.
        Assert.Equal(["admin1@example.com", "admin2@example.com"], mensaje.Destinatarios);
        Assert.Equal(1, almacen.Usados);
        Assert.Equal(1, almacen.Alertas("traslado-vinculado-sin-aprobar", "Inventario"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-23")]
    public async Task ElTopeDe24AlDiaPorMotivoYServicio_SuprimeElResto_SinSalirPorCorreo()
    {
        var (envio, _, transporte, destinatarios) = Crear();

        var resultados = new List<ResultadoDeEnvioDeAlerta>();
        for (var i = 0; i < 30; i++)
        {
            resultados.Add(await envio.Enviar(Alerta(), destinatarios));
        }

        Assert.Equal(24, resultados.Count(r => r == ResultadoDeEnvioDeAlerta.Enviado));
        Assert.Equal(6, resultados.Count(r => r == ResultadoDeEnvioDeAlerta.Suprimido));
        Assert.Equal(24, transporte.Enviados.Count);

        // Otro motivo y otro servicio tienen su propio tope.
        Assert.Equal(ResultadoDeEnvioDeAlerta.Enviado, await envio.Enviar(Alerta("otro-motivo"), destinatarios));
        Assert.Equal(ResultadoDeEnvioDeAlerta.Enviado, await envio.Enviar(Alerta(servicio: ServicioAuditado.Ecommerce), destinatarios));
    }

    [Fact]
    public async Task SinCupoParaLaAdministracion_LaAlertaQuedaEncolada_ParaQueAuditoriaLaReintente_SinSalirPorCorreo()
    {
        var (envio, _, transporte, destinatarios) = Crear(ConfiguracionDeCuotaDeCorreo.Predeterminada with { TotalDiario = 1 });

        var resultados = new List<ResultadoDeEnvioDeAlerta>();
        for (var i = 0; i < 10; i++)
        {
            resultados.Add(await envio.Enviar(Alerta($"motivo-{i}"), destinatarios));
        }

        Assert.Contains(ResultadoDeEnvioDeAlerta.Encolado, resultados);
        Assert.Equal(resultados.Count(r => r == ResultadoDeEnvioDeAlerta.Enviado), transporte.Enviados.Count);
        Assert.True(transporte.Enviados.Count < 10);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-23")]
    public async Task SiElTransporteFalla_SeDevuelvenElCupoYElTope_YSeLanzaParaQueAuditoriaReintente()
    {
        var (envio, almacen, transporte, destinatarios) = Crear();
        transporte.Falla = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => envio.Enviar(Alerta(), destinatarios));

        Assert.Equal(0, almacen.Usados);
        Assert.Equal(0, almacen.Alertas("traslado-vinculado-sin-aprobar", "Inventario"));
    }

    [Fact]
    public async Task SiNingunDestinatarioTieneDireccion_NoSeEnvia_SeDevuelveLaReserva_YSeLanza()
    {
        var (envio, almacen, _, _) = Crear();
        var sinDireccion = new[] { new DestinatarioDeAlerta(Guid.NewGuid()) };

        await Assert.ThrowsAsync<InvalidOperationException>(() => envio.Enviar(Alerta(), sinDireccion));

        Assert.Equal(0, almacen.Usados);
        Assert.Equal(0, almacen.Alertas("traslado-vinculado-sin-aprobar", "Inventario"));
    }
}
