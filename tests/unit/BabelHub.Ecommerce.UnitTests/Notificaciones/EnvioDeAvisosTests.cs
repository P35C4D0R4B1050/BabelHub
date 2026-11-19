using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Notificaciones.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Notificaciones;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.Ecommerce.UnitTests.Identidad;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using Entorno = BabelHub.Ecommerce.UnitTests.Notificaciones.SolicitudesDeAvisosTests.Entorno;

namespace BabelHub.Ecommerce.UnitTests.Notificaciones;

/// <summary>
/// El envío de los avisos pendientes, el tope por número, la solicitud del Inventario, el estado de entrega y la consulta administrativa (transversal-notificaciones T08 a T11 y T21; AC-NOT-02, AC-NOT-03, AC-NOT-05,
/// AC-NOT-08, AC-NOT-11, AC-NOT-12, AC-NOT-14, AC-NOT-15, AC-NOT-16): el orden de comprobaciones, los reintentos, el cupo una sola vez, lo que no se envía y lo que no se pierde.
/// </summary>
public sealed class EnvioDeAvisosTests
{
    private const string NumeroDeLaClienta = "+51987654321";

    private sealed class ContactosDeClientes : IContactoDeAvisosDelCliente
    {
        public Dictionary<Guid, ContactoDeAvisosDelCliente> Contactos { get; } = [];

        public bool Falla { get; set; }

        public Task<ContactoDeAvisosDelCliente?> Consultar(Guid usuarioId, CancellationToken cancelacion = default) =>
            Falla ? throw new InvalidOperationException("Identidad no responde.") : Task.FromResult(Contactos.GetValueOrDefault(usuarioId));
    }

    private sealed class SucursalesFalsas : IContactoDeSucursal
    {
        public ContactoDeSucursal Respuesta { get; set; } = new(EstadoDelContactoDeSucursal.Encontrado, "+51912345678");

        public List<string> Consultadas { get; } = [];

        public Task<ContactoDeSucursal> Resolver(string codigoDeSucursal, CancellationToken cancelacion = default)
        {
            Consultadas.Add(codigoDeSucursal);
            return Task.FromResult(Respuesta);
        }
    }

    private sealed class EnvioFalso : IEnvioDeWhatsApp
    {
        public ResultadoDelEnvio Respuesta { get; set; } = ResultadoDelEnvio.Aceptado;

        public bool Lanza { get; set; }

        public List<SolicitudDeEnvio> Enviados { get; } = [];

        public Task<ResultadoDelEnvio> Enviar(SolicitudDeEnvio solicitud, CancellationToken cancelacion = default)
        {
            if (Lanza)
            {
                throw new InvalidOperationException("El servicio de envío no responde.");
            }

            Enviados.Add(solicitud);
            return Task.FromResult(Respuesta);
        }
    }

    private sealed class UnidadSimple : IUnidadDeTrabajo
    {
        public Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default) => trabajo(cancelacion);

        public Task EjecutarComo(EsquemaDeModulo esquema, Func<CancellationToken, Task> bloque, CancellationToken cancelacion = default) => bloque(cancelacion);
    }

    private sealed class Mundo
    {
        public Mundo()
        {
            Tope = new TopeDeAvisosPorNumero(Base.Limites, Base.Huellas);
            Enviador = new EnviarAvisosPendientes(Base.Avisos, new UnidadSimple(), Clientes, Sucursales, Base.Huellas, Base.Desactivados, Envio, Tope, Eventos, Base.Alertas, Base.Reloj);
            Estado = new RegistrarEstadoDeEntrega(Base.Avisos, new UnidadSimple(), Base.Reloj);
        }

        public Entorno Base { get; } = new();

        public ContactosDeClientes Clientes { get; } = new();

        public SucursalesFalsas Sucursales { get; } = new();

        public EnvioFalso Envio { get; } = new();

        public PublicadorDeEventosEspia Eventos { get; } = new();

        public TopeDeAvisosPorNumero Tope { get; }

        public EnviarAvisosPendientes Enviador { get; }

        public RegistrarEstadoDeEntrega Estado { get; }

        public DateTimeOffset Ahora => Base.Reloj.Ahora;

        /// <summary>Una clienta con consentimiento y su número, con un aviso de «listo para recojo» ya pendiente.</summary>
        public (Guid Cuenta, Aviso Aviso) ClienteConAviso(string numero = NumeroDeLaClienta, bool consiente = true, string evento = "pedido:1:listo-para-recojo")
        {
            var cuenta = Guid.NewGuid();
            Clientes.Contactos[cuenta] = new ContactoDeAvisosDelCliente(false, consiente, numero);
            return (cuenta, Pendiente(cuenta, evento));
        }

        public Aviso Pendiente(Guid cuenta, string evento)
        {
            var aviso = Aviso.Crear(Guid.NewGuid(), evento, cuenta.ToString("D"), TipoDeDestinatario.Cliente, "pedido-listo-para-recojo", 1, new Dictionary<string, string> { ["numeroDePedido"] = "BH-000001" }, Ahora);
            Base.Avisos.Guardados.Add(aviso);
            return aviso;
        }

        public Aviso AvisoDeSucursal(string codigo = "AYACUCHO")
        {
            var aviso = Aviso.Crear(Guid.NewGuid(), "traslado:7:aprobado", codigo, TipoDeDestinatario.Sucursal, "traslado-estado", 1, new Dictionary<string, string> { ["trasladoId"] = "tr-7", ["estado"] = "aprobado" }, Ahora);
            Base.Avisos.Guardados.Add(aviso);
            return aviso;
        }

        public Task<int> Enviar() => Enviador.Ejecutar();
    }

    // ---------------------------------------------------------------- enviar (T08)

    [Fact]
    [Trait("Criterio", "AC-NOT-03")]
    public async Task UnAvisoPendiente_SeEntregaAlServicioConSuIdentificador_SuNumeroYSusCampos_YEsperaElEstadoDeEntrega()
    {
        var m = new Mundo();
        var (_, aviso) = m.ClienteConAviso();

        var tomados = await m.Enviar();

        Assert.Equal(1, tomados);
        var enviado = Assert.Single(m.Envio.Enviados);
        Assert.Equal(aviso.Id, enviado.AvisoId);
        Assert.Equal(NumeroDeLaClienta, enviado.Numero.E164);
        Assert.Equal("pedido-listo-para-recojo", enviado.Plantilla);
        Assert.Equal("BH-000001", enviado.Campos["numeroDePedido"]);

        // El 200 es «aceptado», no entregado: el aviso sigue en envío hasta el estado de entrega firmado.
        Assert.Equal(EstadoDelAviso.Enviando, aviso.Estado);
        Assert.Equal(m.Ahora.AddMinutes(10), aviso.ArrendadoHasta);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-05")]
    public async Task SoloElEstadoDeEntregaFirmado_MarcaEnviado_YUnEstadoTardioNoRevierteUnFinal()
    {
        var m = new Mundo();
        var (_, aviso) = m.ClienteConAviso();
        await m.Enviar();

        var entregado = await m.Estado.Registrar(aviso.Id, EstadoDeEntrega.Entregado, m.Ahora);
        var fallidoTardio = await m.Estado.Registrar(aviso.Id, EstadoDeEntrega.Fallido, m.Ahora);

        Assert.True(entregado.EsExito);
        Assert.True(fallidoTardio.EsExito);
        Assert.Equal(EstadoDelAviso.Enviado, aviso.Estado);
        Assert.Equal(m.Ahora, aviso.FechaFinal);
        Assert.Empty(aviso.Campos);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-11")]
    public async Task UnEstadoFallido_CuentaComoUnIntentoConReintento_YNoEsElEstadoFinalDelAviso()
    {
        var m = new Mundo();
        var (_, aviso) = m.ClienteConAviso();
        await m.Enviar();

        var resultado = await m.Estado.Registrar(aviso.Id, EstadoDeEntrega.Fallido, m.Ahora);

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDelAviso.Pendiente, aviso.Estado);
        Assert.Equal(1, aviso.Intentos);
        Assert.Equal(m.Ahora.AddMinutes(1), aviso.ProximaFecha);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-11")]
    public async Task ElEstadoDeEntrega_DeUnAvisoQueNoExiste_OConUnEstadoDesconocido_SeRechaza()
    {
        var m = new Mundo();

        var desconocido = await m.Estado.Registrar(Guid.NewGuid(), EstadoDeEntrega.Entregado, m.Ahora);
        var invalido = await m.Estado.Registrar(Guid.NewGuid(), (EstadoDeEntrega)99, m.Ahora);
        var vacio = await m.Estado.Registrar(Guid.Empty, EstadoDeEntrega.Entregado, m.Ahora);

        Assert.Equal(CategoriaDeError.NoEncontrado, desconocido.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, invalido.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, vacio.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-02")]
    public async Task SiElClienteYaNoConsiente_AlEnviar_QuedaOmitidoSinConsentimiento_YNoSeEnvia()
    {
        var m = new Mundo();
        var (cuenta, aviso) = m.ClienteConAviso();
        m.Clientes.Contactos[cuenta] = new ContactoDeAvisosDelCliente(false, false, NumeroDeLaClienta);

        await m.Enviar();

        Assert.Empty(m.Envio.Enviados);
        Assert.Equal(EstadoDelAviso.OmitidoSinConsentimiento, aviso.Estado);
        Assert.Equal(SolicitarAviso.MotivoDeOmisionSinConsentimiento, aviso.MotivoDeCierre);
        Assert.Empty(m.Base.Motor.ContadoresUsados);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-02")]
    public async Task SinNumeroRegistrado_QuedaOmitidoConSuMotivo_YUnaCuentaCancelada_QuedaCancelada()
    {
        var m = new Mundo();
        var (cuentaSinNumero, sinNumero) = m.ClienteConAviso();
        m.Clientes.Contactos[cuentaSinNumero] = new ContactoDeAvisosDelCliente(false, true, null);
        var cancelada = Guid.NewGuid();
        m.Clientes.Contactos[cancelada] = new ContactoDeAvisosDelCliente(true, true, NumeroDeLaClienta);
        var avisoCancelado = m.Pendiente(cancelada, "pedido:2:entregado");
        var inexistente = m.Pendiente(Guid.NewGuid(), "pedido:3:entregado");

        await m.Enviar();

        Assert.Equal(EstadoDelAviso.OmitidoSinConsentimiento, sinNumero.Estado);
        Assert.Equal(EnviarAvisosPendientes.MotivoDeSinNumero, sinNumero.MotivoDeCierre);
        Assert.Equal(EstadoDelAviso.Cancelado, avisoCancelado.Estado);
        Assert.Equal(EnviarAvisosPendientes.MotivoDeCuentaCancelada, avisoCancelado.MotivoDeCierre);
        Assert.Equal(EstadoDelAviso.Cancelado, inexistente.Estado);
        Assert.Empty(m.Envio.Enviados);
    }

    [Theory]
    [InlineData("987 654 321")]
    [InlineData("0051987654321")]
    [InlineData("+51 987-654-321")]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task UnNumeroDesactivado_EnCualquierFormato_NoRecibeAvisos_QuedaCanceladoYSinCupo(string formato)
    {
        var m = new Mundo();
        var (_, aviso) = m.ClienteConAviso(NumeroDeLaClienta);
        Assert.True(NumeroDeDestino.TryNormalizar(formato, out var desactivado));
        await m.Base.Desactivados.Agregar(new NumeroDesactivado(m.Base.Huellas.Actual(desactivado), m.Ahora, "a pedido del titular"));

        await m.Enviar();

        Assert.Empty(m.Envio.Enviados);
        Assert.Equal(EstadoDelAviso.Cancelado, aviso.Estado);
        Assert.Equal(EnviarAvisosPendientes.MotivoDeNumeroDesactivado, aviso.MotivoDeCierre);
        Assert.Empty(m.Base.Motor.ContadoresUsados);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task UnNumeroDesactivado_ConUnaVersionAnteriorDeLaClave_SigueExcluido()
    {
        var m = new Mundo();
        m.Base.Huellas.VersionActual = 1;
        var (_, aviso) = m.ClienteConAviso();
        Assert.True(NumeroDeDestino.TryNormalizar(NumeroDeLaClienta, out var numero));
        await m.Base.Desactivados.Agregar(new NumeroDesactivado(m.Base.Huellas.Actual(numero), m.Ahora, "a pedido del titular"));
        m.Base.Huellas.VersionActual = 2;

        await m.Enviar();

        Assert.Equal(EstadoDelAviso.Cancelado, aviso.Estado);
        Assert.Empty(m.Envio.Enviados);
    }

    // ---------------------------------------------------------------- reintentos (NOT-04, AC-NOT-12)

    [Fact]
    [Trait("Criterio", "AC-NOT-12")]
    public async Task UnEnvioFallido_ConsumeUnIntento_ReintentaConEsperaCreciente_YTrasElUltimoQuedaFallido()
    {
        var m = new Mundo();
        var (_, aviso) = m.ClienteConAviso();
        m.Envio.Respuesta = ResultadoDelEnvio.Fallido;
        TimeSpan[] esperas = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(60)];

        foreach (var espera in esperas)
        {
            await m.Enviar();
            Assert.Equal(EstadoDelAviso.Pendiente, aviso.Estado);
            Assert.Equal(m.Ahora + espera, aviso.ProximaFecha);
            m.Base.Reloj.Ahora = aviso.ProximaFecha;
        }

        // El intento inicial y los 5 reintentos: el sexto fallo termina el aviso, visible para la administración.
        await m.Enviar();

        Assert.Equal(6, m.Envio.Enviados.Count);
        Assert.Equal(EstadoDelAviso.Fallido, aviso.Estado);
        Assert.Equal("reintentos-agotados", aviso.MotivoDeCierre);
        Assert.Empty(aviso.Campos);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-12")]
    public async Task UnAdaptadorQueLanza_SeTrataComoUnEnvioFallido_ElLoteSigueConLosDemas()
    {
        var m = new Mundo();
        var (_, primero) = m.ClienteConAviso();
        m.Envio.Lanza = true;

        var tomados = await m.Enviar();

        Assert.Equal(1, tomados);
        Assert.Equal(EstadoDelAviso.Pendiente, primero.Estado);
        Assert.Equal(1, primero.Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-12")]
    public async Task ElAvisoNoSeEnviaAntesDeSuFecha_NiDosVecesPorElMismoTrabajo()
    {
        var m = new Mundo();
        var (_, aviso) = m.ClienteConAviso();
        m.Envio.Respuesta = ResultadoDelEnvio.Fallido;
        await m.Enviar();

        var antes = await m.Enviar();
        m.Base.Reloj.Ahora = aviso.ProximaFecha;
        var despues = await m.Enviar();

        Assert.Equal(0, antes);
        Assert.Equal(1, despues);
        Assert.Equal(2, m.Envio.Enviados.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-12")]
    public async Task UnEnvioDuplicado_EsperaElEstado_SinConsumirIntento_HastaTresVeces_YLaCuartaCuenta()
    {
        var m = new Mundo();
        var (_, aviso) = m.ClienteConAviso();
        m.Envio.Respuesta = ResultadoDelEnvio.Duplicado;

        for (var repeticion = 1; repeticion <= 3; repeticion++)
        {
            await m.Enviar();
            Assert.Equal(EstadoDelAviso.Pendiente, aviso.Estado);
            Assert.Equal(0, aviso.Intentos);
            Assert.Equal(m.Ahora.AddMinutes(10), aviso.ProximaFecha);
            m.Base.Reloj.Ahora = aviso.ProximaFecha;
        }

        await m.Enviar();

        Assert.Equal(1, aviso.Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-12")]
    public async Task SiElEstadoNoLlego_ElArrendamientoVencidoPermiteRecogerElAviso_YReenviarloConElMismoIdentificador()
    {
        var m = new Mundo();
        var (_, aviso) = m.ClienteConAviso();
        await m.Enviar();
        m.Base.Reloj.Ahora = aviso.ArrendadoHasta!.Value;
        m.Envio.Respuesta = ResultadoDelEnvio.Duplicado;

        await m.Enviar();

        Assert.Equal(2, m.Envio.Enviados.Count);
        Assert.All(m.Envio.Enviados, e => Assert.Equal(aviso.Id, e.AvisoId));
        Assert.Equal(EstadoDelAviso.Pendiente, aviso.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-15")]
    public async Task UnAvisoDe24Horas_VenceComoFallido_SinEnviarse_YSinCampos()
    {
        var m = new Mundo();
        var (_, aviso) = m.ClienteConAviso();
        m.Base.Reloj.Ahora = aviso.Creado.AddHours(24);

        await m.Enviar();

        Assert.Empty(m.Envio.Enviados);
        Assert.Equal(EstadoDelAviso.Fallido, aviso.Estado);
        Assert.Equal("vencido", aviso.MotivoDeCierre);
        Assert.Empty(aviso.Campos);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-03")]
    public async Task SiIdentidadNoResponde_ElAvisoSeAplaza5Minutos_SinConsumirIntento_YSeAlerta()
    {
        var m = new Mundo();
        var (_, aviso) = m.ClienteConAviso();
        m.Clientes.Falla = true;

        await m.Enviar();

        Assert.Empty(m.Envio.Enviados);
        Assert.Equal(EstadoDelAviso.Pendiente, aviso.Estado);
        Assert.Equal(0, aviso.Intentos);
        Assert.Equal(m.Ahora.AddMinutes(5), aviso.ProximaFecha);
        Assert.Contains(SolicitarAviso.MotivoDeFallaPropia, m.Base.Alertas.Tecnicas);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-03")]
    public async Task SiElAvisoSeCancelaMientrasSeAtiende_NoSeEnvia_YElCupoRecienReservadoSeDevuelve()
    {
        var m = new Mundo();
        var (_, aviso) = m.ClienteConAviso();
        m.Base.Avisos.TerminadosPorOtro.Add(aviso.Id);

        await m.Enviar();

        Assert.Empty(m.Envio.Enviados);
        Assert.Equal(0, CuposAbiertos(m));
    }

    // ---------------------------------------------------------------- el tope por número (T09)

    private static int CuposAbiertos(Mundo m) => m.Base.Motor.ContadoresUsados.Distinct().Sum(c => m.Base.Motor.Cupos(c));

    [Fact]
    [Trait("Criterio", "AC-NOT-14")]
    public async Task ElSextoAvisoDeLaHora_AUnMismoNumero_QuedaOmitidoPorTope_ConEventoDeSeguridadSinElNumero()
    {
        var m = new Mundo();
        var cuenta = Guid.NewGuid();
        m.Clientes.Contactos[cuenta] = new ContactoDeAvisosDelCliente(false, true, NumeroDeLaClienta);
        var avisos = Enumerable.Range(1, 6).Select(i => m.Pendiente(cuenta, $"pedido:{i}:listo-para-recojo")).ToList();

        await m.Enviar();

        Assert.Equal(5, m.Envio.Enviados.Count);
        var omitido = avisos.Single(a => a.Estado == EstadoDelAviso.OmitidoPorTope);
        Assert.Equal("tope-por-numero", omitido.MotivoDeCierre);
        Assert.Equal(m.Ahora, omitido.FechaFinal);
        Assert.Empty(omitido.Campos);
        var evento = Assert.Single(m.Eventos.Eventos);
        Assert.Equal(TipoDeEventoDeSeguridad.LimiteExcedido, evento.Tipo);
        Assert.Equal(PoliticasDeSeguridad.AvisosPorNumeroPorHora.Nombre, evento.Limite);
        Assert.Equal(cuenta.ToString("D"), evento.Usuario);
        Assert.DoesNotContain(NumeroDeLaClienta, evento.ToString() + evento.Usuario + evento.Limite);
        Assert.Empty(m.Base.EventosAutomaticos.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-14")]
    public async Task ElOmitidoPorTope_NoSeReenvia_NiPasadaLaVentana()
    {
        var m = new Mundo();
        var cuenta = Guid.NewGuid();
        m.Clientes.Contactos[cuenta] = new ContactoDeAvisosDelCliente(false, true, NumeroDeLaClienta);
        var avisos = Enumerable.Range(1, 6).Select(i => m.Pendiente(cuenta, $"pedido:{i}:listo-para-recojo")).ToList();
        await m.Enviar();
        var omitido = avisos.Single(a => a.Estado == EstadoDelAviso.OmitidoPorTope);

        m.Base.Reloj.Ahora = m.Ahora.AddHours(2);
        await m.Enviar();

        Assert.Equal(EstadoDelAviso.OmitidoPorTope, omitido.Estado);
        Assert.DoesNotContain(m.Envio.Enviados, e => e.AvisoId == omitido.Id);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-14")]
    public async Task ElTopeDe20En24Horas_Rige_AunqueNoSeAlcancenLos5PorHora()
    {
        var m = new Mundo();
        var cuenta = Guid.NewGuid();
        m.Clientes.Contactos[cuenta] = new ContactoDeAvisosDelCliente(false, true, NumeroDeLaClienta);
        var total = 0;
        for (var hora = 0; hora < 5; hora++)
        {
            for (var i = 0; i < 5; i++)
            {
                m.Pendiente(cuenta, $"pedido:{++total}:listo-para-recojo");
            }

            await m.Enviar();
            foreach (var enEnvio in m.Base.Avisos.Guardados.Where(a => a.Estado == EstadoDelAviso.Enviando).ToList())
            {
                await m.Estado.Registrar(enEnvio.Id, EstadoDeEntrega.Entregado, m.Ahora);
            }

            m.Base.Reloj.Ahora = m.Ahora.AddMinutes(61);
        }

        // 25 solicitados en 5 horas: salen 20 y 5 quedan omitidos por la política de 24 horas.
        Assert.Equal(20, m.Envio.Enviados.Count);
        Assert.Equal(5, m.Base.Avisos.Guardados.Count(a => a.Estado == EstadoDelAviso.OmitidoPorTope));
        Assert.Contains(m.Eventos.Eventos, e => e.Limite == PoliticasDeSeguridad.AvisosPorNumeroEn24Horas.Nombre);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-14")]
    public async Task OtroNumero_NoSeVeAfectadoPorElTopeDeUnNumeroAjeno()
    {
        var m = new Mundo();
        var cuenta = Guid.NewGuid();
        m.Clientes.Contactos[cuenta] = new ContactoDeAvisosDelCliente(false, true, NumeroDeLaClienta);
        foreach (var i in Enumerable.Range(1, 6))
        {
            m.Pendiente(cuenta, $"pedido:{i}:listo-para-recojo");
        }

        var (_, otro) = m.ClienteConAviso("+51911111111", evento: "pedido:99:listo-para-recojo");
        await m.Enviar();

        Assert.Contains(m.Envio.Enviados, e => e.AvisoId == otro.Id);
        Assert.Equal(EstadoDelAviso.Enviando, otro.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-15")]
    public async Task ElCupoSeReservaUnaVezPorAviso_AunqueSeReintente()
    {
        var m = new Mundo();
        var (_, aviso) = m.ClienteConAviso();
        m.Envio.Respuesta = ResultadoDelEnvio.Fallido;
        await m.Enviar();
        var reservasTrasElPrimero = m.Base.Motor.ContadoresUsados.Count;
        Assert.Equal(2, reservasTrasElPrimero);
        Assert.True(aviso.CupoReservado);

        m.Base.Reloj.Ahora = aviso.ProximaFecha;
        await m.Enviar();

        Assert.Equal(reservasTrasElPrimero, m.Base.Motor.ContadoresUsados.Count);
        Assert.Equal(2, aviso.Cupos.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-15")]
    public async Task UnaReservaForzadaDosVeces_ConElMismoIdentificadorDelAviso_NuncaCuentaDosVeces()
    {
        var m = new Mundo();
        var (_, aviso) = m.ClienteConAviso();
        Assert.True(NumeroDeDestino.TryNormalizar(NumeroDeLaClienta, out var numero));

        var primera = await m.Tope.Reservar(aviso, numero);
        var segunda = await m.Tope.Reservar(aviso, numero);

        Assert.Equal(EstadoDelCupo.Reservado, primera.Estado);
        Assert.Equal(EstadoDelCupo.Reservado, segunda.Estado);
        Assert.Equal(primera.Cupos.Select(c => c.Identificador), segunda.Cupos.Select(c => c.Identificador));
        Assert.Equal(2, CuposAbiertos(m));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-15")]
    public async Task SiElMotorDeLimitesNoResponde_ElAvisoSigueConIntentosIntactos_ConEsperaDe5Minutos_YNoUsaElMotorLocal()
    {
        var m = new Mundo();
        var (_, aviso) = m.ClienteConAviso();
        m.Base.Motor.NoDisponible = true;

        await m.Enviar();

        Assert.Empty(m.Envio.Enviados);
        Assert.Equal(EstadoDelAviso.Pendiente, aviso.Estado);
        Assert.Equal(0, aviso.Intentos);
        Assert.Equal(m.Ahora.AddMinutes(5), aviso.ProximaFecha);
        Assert.False(aviso.CupoReservado);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-15")]
    public async Task ConElMotorCaido_ElAvisoSigueAplazandoseHastaSuEdadMaxima_YVenceSinEnviarse()
    {
        var m = new Mundo();
        var (_, aviso) = m.ClienteConAviso();
        m.Base.Motor.NoDisponible = true;
        while (m.Ahora < aviso.Creado.AddHours(24))
        {
            await m.Enviar();
            m.Base.Reloj.Ahora = aviso.ProximaFecha > m.Ahora ? aviso.ProximaFecha : m.Ahora.AddMinutes(5);
        }

        await m.Enviar();

        Assert.Empty(m.Envio.Enviados);
        Assert.Equal(EstadoDelAviso.Fallido, aviso.Estado);
        Assert.Equal("vencido", aviso.MotivoDeCierre);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-16")]
    public async Task ElContactoDeUnaSucursal_EstaExentoDelTope_YSeResuelveAlEnviar()
    {
        var m = new Mundo();
        var avisos = Enumerable.Range(1, 8).Select(i =>
        {
            var aviso = Aviso.Crear(Guid.NewGuid(), $"traslado:{i}:aprobado", "AYACUCHO", TipoDeDestinatario.Sucursal, "traslado-estado", 1, new Dictionary<string, string> { ["trasladoId"] = $"tr-{i}", ["estado"] = "aprobado" }, m.Ahora);
            m.Base.Avisos.Guardados.Add(aviso);
            return aviso;
        }).ToList();

        await m.Enviar();

        Assert.Equal(8, m.Envio.Enviados.Count);
        Assert.All(m.Envio.Enviados, e => Assert.Equal("+51912345678", e.Numero.E164));
        Assert.Equal(8, m.Sucursales.Consultadas.Count);
        Assert.Empty(m.Base.Motor.ContadoresUsados);
        Assert.All(avisos, a => Assert.False(a.CupoReservado));
        Assert.Empty(m.Eventos.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-08")]
    public async Task SiElInventarioNoResponde_ElAvisoDeSucursalQuedaPendienteSinConsumirIntento()
    {
        var m = new Mundo();
        var aviso = m.AvisoDeSucursal();
        m.Sucursales.Respuesta = new ContactoDeSucursal(EstadoDelContactoDeSucursal.NoDisponible);

        await m.Enviar();

        Assert.Empty(m.Envio.Enviados);
        Assert.Equal(EstadoDelAviso.Pendiente, aviso.Estado);
        Assert.Equal(0, aviso.Intentos);
        Assert.Equal(m.Ahora.AddMinutes(5), aviso.ProximaFecha);
    }

    [Theory]
    [InlineData(EstadoDelContactoDeSucursal.NoEncontrada)]
    [InlineData(EstadoDelContactoDeSucursal.NoAutenticado)]
    [InlineData(EstadoDelContactoDeSucursal.SinContacto)]
    [Trait("Criterio", "AC-NOT-08")]
    public async Task LosErroresPermanentesDelContactoDeSucursal_CuentanComoUnIntento(EstadoDelContactoDeSucursal estado)
    {
        var m = new Mundo();
        var aviso = m.AvisoDeSucursal();
        m.Sucursales.Respuesta = new ContactoDeSucursal(estado);

        await m.Enviar();

        Assert.Empty(m.Envio.Enviados);
        Assert.Equal(EstadoDelAviso.Pendiente, aviso.Estado);
        Assert.Equal(1, aviso.Intentos);
        Assert.Equal(m.Ahora.AddMinutes(1), aviso.ProximaFecha);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task UnNumeroDesactivado_TambienExcluyeAlContactoDeUnaSucursal()
    {
        var m = new Mundo();
        var aviso = m.AvisoDeSucursal();
        Assert.True(NumeroDeDestino.TryNormalizar("+51912345678", out var numero));
        await m.Base.Desactivados.Agregar(new NumeroDesactivado(m.Base.Huellas.Actual(numero), m.Ahora, "a pedido del titular"));

        await m.Enviar();

        Assert.Empty(m.Envio.Enviados);
        Assert.Equal(EstadoDelAviso.Cancelado, aviso.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-16")]
    public async Task LaFaltaDeConsentimiento_TienePrecedenciaSobreElTope_YNoGastaCupo()
    {
        var m = new Mundo();
        var cuenta = Guid.NewGuid();
        m.Clientes.Contactos[cuenta] = new ContactoDeAvisosDelCliente(false, false, NumeroDeLaClienta);
        var avisos = Enumerable.Range(1, 7).Select(i => m.Pendiente(cuenta, $"pedido:{i}:listo-para-recojo")).ToList();

        await m.Enviar();

        Assert.All(avisos, a => Assert.Equal(EstadoDelAviso.OmitidoSinConsentimiento, a.Estado));
        Assert.Empty(m.Base.Motor.ContadoresUsados);
        Assert.Empty(m.Eventos.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-15")]
    public async Task AlCancelarLosAvisosDeUnCliente_SeDevuelveElCupoYaReservado()
    {
        var m = new Mundo();
        var (cuenta, aviso) = m.ClienteConAviso();
        m.Envio.Respuesta = ResultadoDelEnvio.Fallido;
        await m.Enviar();
        Assert.Equal(2, CuposAbiertos(m));

        await m.Base.Cancelar.CancelarLosAvisosDe(cuenta);

        Assert.Equal(EstadoDelAviso.Cancelado, aviso.Estado);
        Assert.Equal(0, CuposAbiertos(m));
    }

    // ---------------------------------------------------------------- la solicitud del Inventario (T10)

    [Fact]
    [Trait("Criterio", "AC-NOT-08")]
    public async Task LaSolicitudDelInventario_CreaElAvisoDeSucursal_UnaSolaVezPorEvento_ConElCodigoNoUnNumero()
    {
        var m = new Mundo();
        var recepcion = new RecibirSolicitudDelInventario(m.Base.Solicitar);
        var solicitud = new SolicitudDeAvisoDelInventario("traslado:9:despachado", "traslado-estado", "AYACUCHO", new Dictionary<string, string> { ["trasladoId"] = "tr-9", ["estado"] = "despachado" });

        var primera = await recepcion.Recibir(solicitud);
        var repetida = await recepcion.Recibir(solicitud);

        Assert.Equal(ResultadoDeLaSolicitudDeAviso.Creado, primera);
        Assert.Equal(ResultadoDeLaSolicitudDeAviso.YaExistia, repetida);
        var aviso = Assert.Single(m.Base.Avisos.Guardados);
        Assert.Equal(TipoDeDestinatario.Sucursal, aviso.Tipo);
        Assert.Equal("AYACUCHO", aviso.DestinatarioRef);
    }

    [Theory]
    [InlineData("pedido:9:despachado", "traslado-estado", "AYACUCHO")]
    [InlineData("traslado:9:despachado", "pedido-listo-para-recojo", "AYACUCHO")]
    [InlineData("traslado:9:despachado", "traslado-estado", "+51987654321")]
    [InlineData("traslado-9-despachado", "traslado-estado", "AYACUCHO")]
    [InlineData("traslado:9:despachado", "no-existe", "AYACUCHO")]
    [Trait("Criterio", "AC-NOT-08")]
    public async Task LaSolicitudDelInventario_ConEventoPlantillaOSucursalInvalidos_SeRechaza_SinGuardarNada(string evento, string plantilla, string sucursal)
    {
        var m = new Mundo();
        var recepcion = new RecibirSolicitudDelInventario(m.Base.Solicitar);

        var resultado = await recepcion.Recibir(new SolicitudDeAvisoDelInventario(evento, plantilla, sucursal, new Dictionary<string, string> { ["trasladoId"] = "tr-9", ["estado"] = "despachado" }));

        Assert.Equal(ResultadoDeLaSolicitudDeAviso.Rechazado, resultado);
        Assert.Empty(m.Base.Avisos.Guardados);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-08")]
    public async Task LaSolicitudDelInventario_ConCamposFueraDelCatalogo_SeRechaza()
    {
        var m = new Mundo();
        var recepcion = new RecibirSolicitudDelInventario(m.Base.Solicitar);

        var resultado = await recepcion.Recibir(new SolicitudDeAvisoDelInventario("traslado:9:despachado", "traslado-estado", "AYACUCHO", new Dictionary<string, string> { ["trasladoId"] = "tr-9", ["estado"] = "despachado", ["telefono"] = "987654321" }));

        Assert.Equal(ResultadoDeLaSolicitudDeAviso.Rechazado, resultado);
        Assert.Empty(m.Base.Avisos.Guardados);
    }

    // ---------------------------------------------------------------- la consulta y la depuración (T12, T21)

    [Fact]
    [Trait("Criterio", "AC-NOT-05")]
    public async Task LaConsultaAdministrativa_MuestraEstadosYFechas_IncluidosLosFallidos_SinNumeroNiCampos()
    {
        var m = new Mundo();
        var (_, aviso) = m.ClienteConAviso();
        m.Envio.Respuesta = ResultadoDelEnvio.Fallido;
        for (var i = 0; i < 6; i++)
        {
            await m.Enviar();
            m.Base.Reloj.Ahora = aviso.ProximaFecha;
        }

        m.Base.Actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), AdministracionCentral());
        var consulta = new ConsultarAvisos(m.Base.Actor, m.Base.Denegaciones, m.Base.Avisos);

        var pagina = await consulta.Listar("fallido", 1, 20);

        Assert.True(pagina.EsExito);
        var visto = Assert.Single(pagina.Valor!.Avisos);
        Assert.Equal("fallido", visto.Estado);
        Assert.Equal(6, visto.Intentos);
        Assert.Equal("reintentos-agotados", visto.MotivoDeCierre);
        Assert.NotNull(visto.FechaFinal);
        Assert.Equal(1, pagina.Valor.Total);
        Assert.DoesNotContain(NumeroDeLaClienta, visto.ToString());
    }

    [Theory]
    [InlineData("desconocido", 1, 20)]
    [InlineData("enviado", 0, 20)]
    [InlineData("enviado", 1, 0)]
    [InlineData("enviado", 1, 101)]
    [Trait("Criterio", "AC-NOT-05")]
    public async Task LaConsultaAdministrativa_ValidaElFiltroYLaPagina(string estado, int pagina, int tamano)
    {
        var m = new Mundo();
        m.Base.Actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), AdministracionCentral());

        var resultado = await new ConsultarAvisos(m.Base.Actor, m.Base.Denegaciones, m.Base.Avisos).Listar(estado, pagina, tamano);

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-05")]
    public async Task LaConsultaAdministrativa_SoloLaVeLaAdministracionCentral_ConLaDenegacionRegistrada()
    {
        var m = new Mundo();
        m.Base.Actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), "cliente");

        var resultado = await new ConsultarAvisos(m.Base.Actor, m.Base.Denegaciones, m.Base.Avisos).Listar(null, 1, 20);

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Single(m.Base.Denegaciones.Registradas);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-04")]
    public async Task LaDepuracion_EliminaLosAvisosTerminadosHaceMasDe90Dias_YConservaLosDemas()
    {
        var m = new Mundo();
        var (_, viejo) = m.ClienteConAviso(evento: "pedido:1:listo-para-recojo");
        var (_, reciente) = m.ClienteConAviso(evento: "pedido:2:listo-para-recojo");
        var (_, pendiente) = m.ClienteConAviso(evento: "pedido:3:listo-para-recojo");
        viejo.Cancelar("x", m.Ahora.AddDays(-91));
        reciente.Cancelar("x", m.Ahora.AddDays(-89));

        var eliminados = await new PurgarAvisosFinalizados(m.Base.Avisos, m.Base.Reloj).Ejecutar();

        Assert.Equal(1, eliminados);
        Assert.DoesNotContain(viejo, m.Base.Avisos.Guardados);
        Assert.Contains(reciente, m.Base.Avisos.Guardados);
        Assert.Contains(pendiente, m.Base.Avisos.Guardados);
    }

    private static string AdministracionCentral() => DesactivarAvisosDeUnNumero.RolDeAdministracionCentral;
}
