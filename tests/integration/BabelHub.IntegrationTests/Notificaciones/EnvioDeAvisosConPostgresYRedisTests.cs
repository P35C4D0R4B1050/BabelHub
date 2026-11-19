using System.Collections.Concurrent;
using System.Text;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Notificaciones.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Notificaciones;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Notificaciones;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.IntegrationTests.Limites;

namespace BabelHub.IntegrationTests.Notificaciones;

/// <summary>
/// El envío de avisos con PostgreSQL y el motor de límites reales de Redis (transversal-notificaciones T22, T23; AC-NOT-03, AC-NOT-12, AC-NOT-14, AC-NOT-15, AC-NOT-16): dos trabajadores simultáneos envían cada aviso una sola vez,
/// seis avisos simultáneos a un mismo número dejan cinco enviados y uno omitido por tope, otro número no se ve afectado, el contacto de sucursal queda exento, la cancelación devuelve el cupo y una reserva doble forzada con el
/// identificador del aviso nunca cuenta dos veces.
/// </summary>
[Collection(ColeccionPostgresDeNotificaciones.Nombre)]
public sealed class EnvioDeAvisosConPostgresYRedisTests(PostgresDeNotificacionesFixture bd, RedisDeContadoresFixture redis) : IClassFixture<RedisDeContadoresFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    private sealed class RelojDePrueba : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = Inicio;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class OrigenDeTrabajo : IOrigenDeSolicitud
    {
        public OrigenDeSolicitud Actual => OrigenDeSolicitud.Desconocido;
    }

    private sealed class Clientes : IContactoDeAvisosDelCliente
    {
        public ConcurrentDictionary<Guid, ContactoDeAvisosDelCliente> Contactos { get; } = new();

        public Task<ContactoDeAvisosDelCliente?> Consultar(Guid usuarioId, CancellationToken cancelacion = default) =>
            Task.FromResult(Contactos.TryGetValue(usuarioId, out var contacto) ? contacto : null);
    }

    private sealed class Sucursales : IContactoDeSucursal
    {
        public Task<ContactoDeSucursal> Resolver(string codigoDeSucursal, CancellationToken cancelacion = default) =>
            Task.FromResult(new ContactoDeSucursal(EstadoDelContactoDeSucursal.Encontrado, "+51912345678"));
    }

    private sealed class Envio : IEnvioDeWhatsApp
    {
        public ConcurrentBag<Guid> Enviados { get; } = [];

        public ConcurrentBag<string> Numeros { get; } = [];

        public ResultadoDelEnvio Respuesta { get; set; } = ResultadoDelEnvio.Aceptado;

        public Task<ResultadoDelEnvio> Enviar(SolicitudDeEnvio solicitud, CancellationToken cancelacion = default)
        {
            Enviados.Add(solicitud.AvisoId);
            Numeros.Add(solicitud.Numero.E164);
            return Task.FromResult(Respuesta);
        }
    }

    private sealed class Eventos : IPublicadorDeEventosDeSeguridad
    {
        public ConcurrentBag<EventoDeSeguridad> Publicados { get; } = [];

        public Task Publicar(EventoDeSeguridad evento, CancellationToken cancelacion = default)
        {
            Publicados.Add(evento);
            return Task.CompletedTask;
        }
    }

    private sealed class Alertas : IAlertas
    {
        public ConcurrentBag<string> Tecnicas { get; } = [];

        public Task PublicarTecnica(CodigoDeAlerta motivo, CancellationToken cancelacion = default)
        {
            Tecnicas.Add(motivo.Valor);
            return Task.CompletedTask;
        }

        public Task PublicarDeSeguridad(CodigoDeAlerta motivo, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    private readonly RelojDePrueba _reloj = new();
    private readonly Clientes _clientes = new();
    private readonly Envio _envio = new();
    private readonly Eventos _eventos = new();
    private readonly Alertas _alertas = new();
    private TopeDeAvisosPorNumero _tope = null!;
    private IControlDeLimites _limites = null!;
    private CalculadorDeHuellaDeNumero _huellas = null!;
    private static int _secuencia;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        var plataforma = new CalculadorDeHuellaHmac(new OpcionesDeHuella("v1:" + Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)(i * 3)).ToArray())));
        _huellas = new CalculadorDeHuellaDeNumero(new OpcionesDeHuellaDeNumero(1, Encoding.UTF8.GetBytes("clave-de-la-huella-de-numero-0123456789")), plataforma);
        _limites = new EvaluarLimite(new CatalogoDePoliticas(PoliticasDeSeguridad.Todas), redis.Motor(), null, new OrigenDeTrabajo(), new Eventos(), _reloj);
        _tope = new TopeDeAvisosPorNumero(_limites, _huellas);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private EnviarAvisosPendientes Trabajador() =>
        new(new RepositorioDeAvisosPostgres(bd.Esquema), bd.Unidad, _clientes, new Sucursales(), _huellas, new RepositorioDeNumerosDesactivadosPostgres(bd.Esquema), _envio, _tope, _eventos, _alertas, _reloj);

    private static string NumeroAlAzar() => $"+5198{Random.Shared.Next(1_000_000, 9_999_999)}";

    private async Task<(Guid Cuenta, Aviso Aviso)> ClienteConAviso(string numero, string? cuenta = null, bool consiente = true)
    {
        var id = cuenta is null ? Guid.NewGuid() : Guid.Parse(cuenta);
        _clientes.Contactos[id] = new ContactoDeAvisosDelCliente(false, consiente, numero);
        var aviso = Aviso.Crear(Guid.NewGuid(), $"pedido:{Interlocked.Increment(ref _secuencia)}:listo-para-recojo", id.ToString("D"), TipoDeDestinatario.Cliente, "pedido-listo-para-recojo", 1, new Dictionary<string, string> { ["numeroDePedido"] = "BH-000001" }, Inicio);
        Assert.True(await bd.Unidad.Ejecutar(ct => bd.Avisos.InsertarSiNoExiste(aviso, ct)));
        return (id, aviso);
    }

    private async Task<EstadoDelAviso> EstadoDe(Aviso aviso) => (await bd.Unidad.Ejecutar(ct => bd.Avisos.PorId(aviso.Id, false, ct)))!.Estado;

    private async Task<List<EstadoDelAviso>> EstadosDe(IEnumerable<Aviso> avisos) => [.. await Task.WhenAll(avisos.Select(EstadoDe))];

    // ---------------------------------------------------------------- dos trabajadores (T22)

    [Fact]
    [Trait("Criterio", "AC-NOT-12")]
    public async Task DosTrabajadoresSimultaneos_EnvianCadaAvisoUnaSolaVez()
    {
        var avisos = new List<Aviso>();
        for (var i = 0; i < 30; i++)
        {
            avisos.Add((await ClienteConAviso(NumeroAlAzar())).Aviso);
        }

        var tomados = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Trabajador().Ejecutar(10))));

        Assert.Equal(30, tomados.Sum());
        Assert.Equal(30, _envio.Enviados.Count);
        Assert.Equal(30, _envio.Enviados.Distinct().Count());
        Assert.All(avisos, a => Assert.Contains(a.Id, _envio.Enviados));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-03")]
    public async Task ElCanalCaido_DejaElAvisoPendienteConSuEsperaCreciente_HastaFallidoVisibleParaLaAdministracion()
    {
        var (_, aviso) = await ClienteConAviso(NumeroAlAzar());
        _envio.Respuesta = ResultadoDelEnvio.Fallido;

        for (var intento = 1; intento <= 6; intento++)
        {
            await Trabajador().Ejecutar();
            var leido = (await bd.Unidad.Ejecutar(ct => bd.Avisos.PorId(aviso.Id, false, ct)))!;
            Assert.Equal(intento, leido.Intentos);
            if (intento < 6)
            {
                Assert.Equal(EstadoDelAviso.Pendiente, leido.Estado);
                _reloj.Ahora = leido.ProximaFecha;
            }
        }

        var final = (await bd.Unidad.Ejecutar(ct => bd.Avisos.PorId(aviso.Id, false, ct)))!;
        Assert.Equal(EstadoDelAviso.Fallido, final.Estado);
        Assert.Equal("reintentos-agotados", final.MotivoDeCierre);
        Assert.Empty(final.Campos);
        Assert.Equal(6, _envio.Enviados.Count);
    }

    // ---------------------------------------------------------------- el tope con el motor real (T23)

    [Fact]
    [Trait("Criterio", "AC-NOT-14")]
    public async Task SeisAvisosSimultaneosAUnMismoNumero_DejanCincoEnviadosYUnoOmitidoPorTope_ConUnEventoSinElNumero()
    {
        var numero = NumeroAlAzar();
        var cuenta = Guid.NewGuid().ToString("D");
        var avisos = new List<Aviso>();
        for (var i = 0; i < 6; i++)
        {
            avisos.Add((await ClienteConAviso(numero, cuenta)).Aviso);
        }

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => Trabajador().Ejecutar(2))));

        Assert.Equal(5, _envio.Enviados.Count);
        var estados = await Task.WhenAll(avisos.Select(EstadoDe));
        Assert.Equal(5, estados.Count(e => e == EstadoDelAviso.Enviando));
        Assert.Equal(1, estados.Count(e => e == EstadoDelAviso.OmitidoPorTope));
        var evento = Assert.Single(_eventos.Publicados);
        Assert.Equal(PoliticasDeSeguridad.AvisosPorNumeroPorHora.Nombre, evento.Limite);
        Assert.DoesNotContain(numero, evento.Usuario + evento.Limite + evento.Motivo, StringComparison.Ordinal);
        Assert.DoesNotContain(numero, await bd.Consultar<string>("SELECT coalesce(string_agg(cupos::text || campos::text, ','), '') FROM notificaciones.avisos") ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-14")]
    public async Task ElOmitidoPorTope_NoSeReenvia_ElOtroNumeroNoSeAfecta_YLaSucursalEstaExenta()
    {
        var numero = NumeroAlAzar();
        var cuenta = Guid.NewGuid().ToString("D");
        var avisos = new List<Aviso>();
        for (var i = 0; i < 6; i++)
        {
            avisos.Add((await ClienteConAviso(numero, cuenta)).Aviso);
        }

        var (_, otroNumero) = await ClienteConAviso(NumeroAlAzar());
        var sucursales = new List<Aviso>();
        for (var i = 0; i < 8; i++)
        {
            var deSucursal = Aviso.Crear(Guid.NewGuid(), $"traslado:{Interlocked.Increment(ref _secuencia)}:aprobado", "AYACUCHO", TipoDeDestinatario.Sucursal, "traslado-cambio-de-estado", 1, new Dictionary<string, string> { ["numeroDeTraslado"] = "7", ["rolDeLaSucursal"] = "destino", ["cantidad"] = "3", ["estado"] = "aprobado" }, Inicio);
            await bd.Unidad.Ejecutar(ct => bd.Avisos.InsertarSiNoExiste(deSucursal, ct));
            sucursales.Add(deSucursal);
        }

        await Trabajador().Ejecutar(25);

        var estadosDeLosSeis = await EstadosDe(avisos);
        var omitido = avisos[estadosDeLosSeis.IndexOf(EstadoDelAviso.OmitidoPorTope)];
        Assert.Contains(otroNumero.Id, _envio.Enviados);
        Assert.All(sucursales, a => Assert.Contains(a.Id, _envio.Enviados));
        _reloj.Ahora = Inicio.AddMinutes(30);
        await Trabajador().Ejecutar(25);

        Assert.DoesNotContain(omitido.Id, _envio.Enviados);
        Assert.Equal(EstadoDelAviso.OmitidoPorTope, await EstadoDe(omitido));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-15")]
    public async Task AlCancelarLosAvisosDeUnCliente_SeDevuelveElCupo_YOtroAvisoPuedeSalir()
    {
        var numero = NumeroAlAzar();
        var cuenta = Guid.NewGuid().ToString("D");
        var avisos = new List<Aviso>();
        for (var i = 0; i < 5; i++)
        {
            avisos.Add((await ClienteConAviso(numero, cuenta)).Aviso);
        }

        _envio.Respuesta = ResultadoDelEnvio.Fallido;
        await Trabajador().Ejecutar(25);
        Assert.Equal(5, _envio.Enviados.Count);

        // Los cinco cupos están tomados: un sexto aviso de otra cuenta al mismo número se omitiría.
        var (cuentaB, sexto) = await ClienteConAviso(numero);
        Assert.NotEqual(Guid.Parse(cuenta), cuentaB);
        await Trabajador().Ejecutar(25);
        Assert.Equal(EstadoDelAviso.OmitidoPorTope, await EstadoDe(sexto));

        // Identidad cancela los avisos pendientes de la primera cuenta: sus cupos vuelven.
        var cancelacion = new CancelarAvisosPendientes(new RepositorioDeAvisosPostgres(bd.Esquema), _reloj, _tope);
        await bd.Unidad.Ejecutar(async ct => { await cancelacion.CancelarLosAvisosDe(Guid.Parse(cuenta), ct); return 0; });
        Assert.All(await EstadosDe(avisos), e => Assert.Equal(EstadoDelAviso.Cancelado, e));

        var (_, septimo) = await ClienteConAviso(numero);
        _envio.Respuesta = ResultadoDelEnvio.Aceptado;
        await Trabajador().Ejecutar(25);

        Assert.Contains(septimo.Id, _envio.Enviados);
        Assert.NotEqual(EstadoDelAviso.OmitidoPorTope, await EstadoDe(septimo));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-15")]
    public async Task UnaReservaDobleForzada_ConElMismoIdentificadorDelAviso_NuncaCuentaDosVeces_EnElMotorReal()
    {
        var (_, aviso) = await ClienteConAviso(NumeroAlAzar());
        Assert.True(NumeroDeDestino.TryNormalizar(NumeroAlAzar(), out var numero));

        var primera = await _tope.Reservar(aviso, numero);
        var segunda = await _tope.Reservar(aviso, numero);

        Assert.Equal(EstadoDelCupo.Reservado, primera.Estado);
        Assert.Equal(EstadoDelCupo.Reservado, segunda.Estado);
        Assert.Equal(primera.Cupos.Select(c => c.Identificador), segunda.Cupos.Select(c => c.Identificador));

        // Cinco avisos distintos más caben (el primero ya ocupa un cupo por política); el sexto no.
        var otros = new List<ResultadoDelCupo>();
        for (var i = 0; i < 5; i++)
        {
            var (_, otro) = await ClienteConAviso(NumeroAlAzar());
            otros.Add(await _tope.Reservar(otro, numero));
        }

        Assert.Equal(4, otros.Count(r => r.Estado == EstadoDelCupo.Reservado));
        Assert.Equal(1, otros.Count(r => r.Estado == EstadoDelCupo.Excedido));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-16")]
    public async Task SinConsentimiento_ElAvisoQuedaOmitido_ANTESDelTope_YNoGastaCupo()
    {
        var numero = NumeroAlAzar();
        var sin = new List<Aviso>();
        for (var i = 0; i < 7; i++)
        {
            sin.Add((await ClienteConAviso(numero, consiente: false)).Aviso);
        }

        await Trabajador().Ejecutar(25);

        Assert.All(await EstadosDe(sin), e => Assert.Equal(EstadoDelAviso.OmitidoSinConsentimiento, e));
        Assert.Empty(_envio.Enviados);
        Assert.Empty(_eventos.Publicados);
        var (_, conConsentimiento) = await ClienteConAviso(numero);
        await Trabajador().Ejecutar(25);
        Assert.Contains(conConsentimiento.Id, _envio.Enviados);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task UnNumeroDesactivadoEnOtroFormato_TienePrecedenciaSobreElTope_YElAvisoQuedaCancelado()
    {
        var (_, aviso) = await ClienteConAviso("+51987654321");
        Assert.True(NumeroDeDestino.TryNormalizar("987 654 321", out var escrito));
        await bd.Unidad.Ejecutar(ct => new RepositorioDeNumerosDesactivadosPostgres(bd.Esquema).Agregar(new NumeroDesactivado(_huellas.Actual(escrito), Inicio, "a pedido del titular"), ct));

        await Trabajador().Ejecutar(25);

        Assert.Empty(_envio.Enviados);
        var leido = (await bd.Unidad.Ejecutar(ct => bd.Avisos.PorId(aviso.Id, false, ct)))!;
        Assert.Equal(EstadoDelAviso.Cancelado, leido.Estado);
        Assert.Equal("numero-desactivado", leido.MotivoDeCierre);
        Assert.Empty(leido.Cupos);
    }
}
