using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Plataforma;

public sealed class EvaluarLimiteTests
{
    private const string Resumen = "k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE";
    private const string OtroResumen = "Zq8Lw2Xv5Nc7Rb1Ty4Hu9Jk3Gf6Ds0Ae2Mp8Oi5UyW";

    private static readonly ClaveDePolitica General = new("solicitudes-generales");
    private static readonly ClaveDePolitica Avisos = new("avisos-por-numero");
    private static readonly ClaveDePolitica Reservas = new("reservas-por-usuario");
    private static readonly ClaveDePolitica Directa = new("direccion-directa-prueba");
    private static readonly DateTimeOffset Inicio = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly RelojFijo _reloj = new(Inicio);
    private readonly PublicadorDeEventosEspia _eventos = new();
    private readonly MotorDeMemoria _motor;
    private readonly MotorDeMemoria _local;
    private readonly OrigenFijo _origen;
    private readonly EvaluarLimite _control;

    public EvaluarLimiteTests()
    {
        _motor = new MotorDeMemoria(_reloj);
        _local = new MotorDeMemoria(_reloj);
        _origen = new OrigenFijo(new OrigenDeSolicitud(HuellaDeOrigen.Crear(Resumen, "v1"), "203.0.113.7"));

        var politicas = PoliticasDeSeguridad.Todas.Concat(
        [
            new PoliticaDeLimite(General.Valor, 3, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.Origen, respaldoLocal: true),
            new PoliticaDeLimite(Avisos.Valor, 2, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Numero, liberable: true, publicarEvento: false),
            new PoliticaDeLimite(Reservas.Valor, 2, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Usuario),
            new PoliticaDeLimite(Directa.Valor, 2, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.OrigenDirecto),
        ]);

        _control = new EvaluarLimite(new CatalogoDePoliticas(politicas), _motor, _local, _origen, _eventos, _reloj);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-08")]
    public async Task Reservar_ConElLimiteExcedido_PublicaUnEventoConLaDireccionCompletaYElOrigenHuellado()
    {
        for (var i = 0; i < 3; i++)
        {
            Assert.True((await _control.Reservar(PoliticasDeLimite.RegistrosPorOrigen)).EsPermitido);
        }

        var rechazo = await _control.Reservar(PoliticasDeLimite.RegistrosPorOrigen);

        Assert.Equal(EstadoDeLimite.Excedido, rechazo.Estado);
        var evento = Assert.Single(_eventos.Eventos);
        Assert.Equal(TipoDeEventoDeSeguridad.LimiteExcedido, evento.Tipo);
        Assert.Equal("registros-por-origen", evento.Limite);
        Assert.Equal("203.0.113.7", evento.DireccionCompleta);
        Assert.Equal(_origen.Actual.Huella, evento.Origen);
        Assert.Equal(Inicio, evento.Instante);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-08")]
    public async Task Reservar_ConCincuentaRechazosDelMismoOrigen_PublicaCincuentaEventosParaQueAuditoriaLosAgrupe()
    {
        for (var i = 0; i < 3; i++)
        {
            await _control.Reservar(PoliticasDeLimite.RegistrosPorOrigen);
        }

        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(EstadoDeLimite.Excedido, (await _control.Reservar(PoliticasDeLimite.RegistrosPorOrigen)).Estado);
        }

        Assert.Equal(50, _eventos.Eventos.Count);
        Assert.All(_eventos.Eventos, e => Assert.Equal(_origen.Actual.Huella, e.Origen));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-08")]
    public async Task Reservar_ConLaMismaRespuestaParaCualquierPolitica_TraeMotivoEsperaYDisponibilidad()
    {
        var a = await Agotar(PoliticasDeLimite.RegistrosPorOrigen, 3);
        var b = await Agotar(PoliticasDeLimite.CodigosPorOrigen, 3);

        Assert.Equal("registros-por-origen", a.Motivo);
        Assert.Equal("codigos-por-origen", b.Motivo);
        Assert.Equal(a.EsperaEnSegundos, b.EsperaEnSegundos);
        Assert.Equal(3600, a.EsperaEnSegundos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-08")]
    public async Task Reservar_ConUnaPoliticaQueNoPublicaElEvento_RechazaSinPublicar()
    {
        var huella = HuellaDeSujeto.Crear(Resumen, "v1");
        await _control.Reservar(Avisos, huellaDelLlamador: huella);
        await _control.Reservar(Avisos, huellaDelLlamador: huella);

        var rechazo = await _control.Reservar(Avisos, huellaDelLlamador: huella);

        Assert.Equal(EstadoDeLimite.Excedido, rechazo.Estado);
        Assert.Empty(_eventos.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-08")]
    public async Task Reservar_ConUnRechazo_CuentaElRechazoDelOrigenParaDetectarAutomatizacion()
    {
        await Agotar(PoliticasDeLimite.RegistrosPorOrigen, 3);
        await _control.Reservar(PoliticasDeLimite.RegistrosPorOrigen);

        Assert.Equal(2, await _motor.ContarRechazosDeOrigen(_origen.Actual.Huella, EvaluarLimite.VentanaDeRechazosDelOrigen, CancellationToken.None));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-19")]
    public async Task Reservar_ConUnaReservaRepetidaConLaMismaClave_NoCuentaParaElLimiteDeReservasPeroSiParaElGeneral()
    {
        var clave = new ClaveDeIdempotencia("reserva-1", "/reservas", "huella-cuerpo");

        var primera = await _control.Reservar(Reservas, sujeto: "u-1", idempotencia: clave);
        await _control.Completar(primera.Reserva!, new ResultadoMinimo("creada", "res-1"));
        var repeticion = await _control.Reservar(Reservas, sujeto: "u-1", idempotencia: clave);
        var otra = await _control.Reservar(Reservas, sujeto: "u-1", idempotencia: new ClaveDeIdempotencia("reserva-2", "/reservas", "otro"));
        var tercera = await _control.Reservar(Reservas, sujeto: "u-1", idempotencia: new ClaveDeIdempotencia("reserva-3", "/reservas", "otro-mas"));

        Assert.Equal(EstadoDeLimite.RepeticionCompletada, repeticion.Estado);
        Assert.Equal("res-1", repeticion.ResultadoGuardado!.IdDelRecurso);
        Assert.True(otra.EsPermitido);
        Assert.Equal(EstadoDeLimite.Excedido, tercera.Estado); // máximo 2: la repetición no gastó un tercer cupo

        // El límite general no recibe la clave de idempotencia: la repetición también cuenta.
        for (var i = 0; i < 3; i++)
        {
            Assert.True((await _control.Reservar(General)).EsPermitido);
        }

        Assert.Equal(EstadoDeLimite.Excedido, (await _control.Reservar(General)).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-19")]
    public async Task Reservar_ConLaClaveEnCursoOEnConflicto_NoCuentaYDevuelveElEstado()
    {
        var clave = new ClaveDeIdempotencia("reserva-1", "/reservas", "cuerpo-a");
        await _control.Reservar(Reservas, sujeto: "u-1", idempotencia: clave);

        var enCurso = await _control.Reservar(Reservas, sujeto: "u-1", idempotencia: clave);
        var conflicto = await _control.Reservar(Reservas, sujeto: "u-1", idempotencia: new ClaveDeIdempotencia("reserva-1", "/reservas", "cuerpo-b"));

        Assert.Equal(EstadoDeLimite.RepeticionEnCurso, enCurso.Estado);
        Assert.Equal(EstadoDeLimite.ClaveEnConflicto, conflicto.Estado);
        Assert.Equal(1, _motor.Cupos("u:s:u-1"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-19")]
    public async Task Fallar_LiberaLaClaveParaQueElClientePuedaReintentar()
    {
        var clave = new ClaveDeIdempotencia("reserva-1", "/reservas", "cuerpo-a");
        var primera = await _control.Reservar(Reservas, sujeto: "u-1", idempotencia: clave);

        await _control.Fallar(primera.Reserva!);
        var reintento = await _control.Reservar(Reservas, sujeto: "u-1", idempotencia: clave);

        Assert.True(reintento.EsPermitido);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-20")]
    public async Task Reservar_DesdeUnOrigenDesconocido_CuentaEnUnUnicoContadorCompartido()
    {
        _origen.Actual = OrigenDeSolicitud.Desconocido;
        for (var i = 0; i < 3; i++)
        {
            Assert.True((await _control.Reservar(PoliticasDeLimite.RegistrosPorOrigen)).EsPermitido);
        }

        var rechazo = await _control.Reservar(PoliticasDeLimite.RegistrosPorOrigen);

        Assert.Equal(EstadoDeLimite.Excedido, rechazo.Estado);
        Assert.Equal(["o:0:desconocido", "o:0:desconocido", "o:0:desconocido", "o:0:desconocido"], _motor.ContadoresUsados);
        var evento = Assert.Single(_eventos.Eventos);
        Assert.True(evento.Origen!.EsDesconocido);
        Assert.Null(evento.DireccionCompleta);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-20")]
    public async Task Reservar_DesdeUnOrigenDesconocido_AplicaAdemasLosLimitesPorUsuario()
    {
        _origen.Actual = OrigenDeSolicitud.Desconocido;
        await _control.Reservar(Reservas, sujeto: "u-1");
        await _control.Reservar(Reservas, sujeto: "u-1");

        Assert.Equal(EstadoDeLimite.Excedido, (await _control.Reservar(Reservas, sujeto: "u-1")).Estado);
        Assert.True((await _control.Reservar(Reservas, sujeto: "u-2")).EsPermitido);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-20")]
    public async Task Reservar_DeOrigenesDistintos_CuentaPorSeparado()
    {
        await Agotar(PoliticasDeLimite.RegistrosPorOrigen, 3);
        _origen.Actual = new OrigenDeSolicitud(HuellaDeOrigen.Crear(OtroResumen, "v1"), "198.51.100.9");

        Assert.True((await _control.Reservar(PoliticasDeLimite.RegistrosPorOrigen)).EsPermitido);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-22")]
    public async Task Reservar_ConLaHuellaDeUnNumero_GuardaSoloLaHuellaEnElContador()
    {
        var huella = HuellaDeSujeto.Crear(Resumen, "v1");

        await _control.Reservar(Avisos, huellaDelLlamador: huella);

        var contador = Assert.Single(_motor.ContadoresUsados);
        Assert.Equal($"n:v1:{Resumen}", contador);
        Assert.DoesNotContain("+51", contador, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reservar_ConLaMismaClaveDelLlamador_DevuelveLaMismaReservaYNoCuentaDeNuevo()
    {
        var huella = HuellaDeSujeto.Crear(Resumen, "v1");

        var primera = await _control.Reservar(Avisos, claveDelLlamador: "aviso-7", huellaDelLlamador: huella);
        var repetida = await _control.Reservar(Avisos, claveDelLlamador: "aviso-7", huellaDelLlamador: huella);
        var segunda = await _control.Reservar(Avisos, claveDelLlamador: "aviso-8", huellaDelLlamador: huella);
        var tercera = await _control.Reservar(Avisos, claveDelLlamador: "aviso-9", huellaDelLlamador: huella);

        Assert.Equal(primera.Reserva, repetida.Reserva);
        Assert.True(segunda.EsPermitido);
        Assert.Equal(EstadoDeLimite.Excedido, tercera.Estado);
    }

    [Fact]
    public async Task Liberar_ConUnaPoliticaLiberable_DevuelveElCupo()
    {
        var huella = HuellaDeSujeto.Crear(Resumen, "v1");
        var uno = await _control.Reservar(Avisos, claveDelLlamador: "aviso-1", huellaDelLlamador: huella);
        await _control.Reservar(Avisos, claveDelLlamador: "aviso-2", huellaDelLlamador: huella);

        await _control.Liberar(uno.Reserva!);

        Assert.True((await _control.Reservar(Avisos, claveDelLlamador: "aviso-3", huellaDelLlamador: huella)).EsPermitido);
    }

    [Fact]
    public async Task Liberar_ConUnCupoCancelado_EnUnaPoliticaNoLiberable_NoDevuelveCupo()
    {
        var uno = await _control.Reservar(PoliticasDeLimite.CodigosPorOrigen);
        await _control.Reservar(PoliticasDeLimite.CodigosPorOrigen);
        await _control.Reservar(PoliticasDeLimite.CodigosPorOrigen);

        await _control.Liberar(uno.Reserva!);

        Assert.Equal(EstadoDeLimite.Excedido, (await _control.Reservar(PoliticasDeLimite.CodigosPorOrigen)).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-06")]
    public async Task Liberar_AlIngresarConExito_DevuelveElCupoDeLosIngresosFallidos()
    {
        var cuenta = HuellaDeSujeto.Crear(Resumen, "v1");
        for (var i = 0; i < 4; i++)
        {
            await _control.Reservar(PoliticasDeLimite.IngresosFallidos, huellaDelLlamador: cuenta);
        }

        var quinto = await _control.Reservar(PoliticasDeLimite.IngresosFallidos, huellaDelLlamador: cuenta);
        await _control.Liberar(quinto.Reserva!);

        // Sin el quinto fallo no hay bloqueo: el titular sigue pudiendo ingresar.
        Assert.True((await _control.Reservar(PoliticasDeLimite.IngresosFallidos, huellaDelLlamador: cuenta)).EsPermitido);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-07")]
    public async Task Reservar_ConUnaCuentaQueExisteYOtraQueNo_DaLaMismaRespuestaDeLimite()
    {
        var existe = HuellaDeSujeto.Crear(Resumen, "v1");
        var noExiste = HuellaDeSujeto.Crear(OtroResumen, "v1");
        ResultadoDeLimite? a = null;
        ResultadoDeLimite? b = null;
        for (var i = 0; i < 6; i++)
        {
            a = await _control.Reservar(PoliticasDeLimite.IngresosFallidos, huellaDelLlamador: existe);
            b = await _control.Reservar(PoliticasDeLimite.IngresosFallidos, huellaDelLlamador: noExiste);
        }

        Assert.Equal(EstadoDeLimite.Excedido, a!.Estado);
        Assert.Equal(a, b);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-24")]
    public async Task Reservar_ConElAlmacenCaidoYUnaPoliticaQueFallaCerrada_ResponderServicioNoDisponible()
    {
        _motor.NoDisponible = true;

        var resultado = await _control.Reservar(PoliticasDeLimite.IngresosFallidos, sujeto: "c-1");

        Assert.Equal(EstadoDeLimite.NoDisponible, resultado.Estado);
        Assert.Empty(_local.ContadoresUsados);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-24")]
    public async Task Reservar_ConElAlmacenCaidoYUnaPoliticaConRespaldoLocal_UsaElMotorLocal()
    {
        _motor.NoDisponible = true;

        var resultado = await _control.Reservar(General);

        Assert.True(resultado.EsPermitido);
        Assert.Single(_local.ContadoresUsados);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-24")]
    public async Task Liberar_ConElAlmacenCaido_NoInterrumpeLaOperacion()
    {
        var huella = HuellaDeSujeto.Crear(Resumen, "v1");
        var reserva = (await _control.Reservar(Avisos, huellaDelLlamador: huella)).Reserva!;
        _motor.NoDisponible = true;

        await _control.Liberar(reserva);
        await _control.Completar(reserva, new ResultadoMinimo("ok"));
        await _control.Fallar(reserva);
    }

    [Fact]
    public async Task Reservar_ConElSujetoMalIndicado_Lanza()
    {
        var huella = HuellaDeSujeto.Crear(Resumen, "v1");

        await Assert.ThrowsAsync<ArgumentException>(() => _control.Reservar(Reservas));
        await Assert.ThrowsAsync<ArgumentException>(() => _control.Reservar(Reservas, sujeto: "u-1", huellaDelLlamador: huella));
        await Assert.ThrowsAsync<ArgumentException>(() => _control.Reservar(PoliticasDeLimite.RegistrosPorOrigen, sujeto: "u-1"));
        await Assert.ThrowsAsync<ArgumentException>(() => _control.Reservar(Avisos, sujeto: "u-1"));
        await Assert.ThrowsAsync<ArgumentException>(() => _control.Reservar(new ClaveDePolitica("no-existe"), sujeto: "u-1"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-29")]
    public async Task Reservar_ConLaDireccionReenviadaIgnorada_NoCuentaLosLimitesPorOrigenYSiElTopeDeLaDireccionDirecta()
    {
        _origen.Actual = _origen.Actual with { AplicaLimitesPorOrigen = false };

        for (var i = 0; i < 5; i++)
        {
            var sinContar = await _control.Reservar(General);
            Assert.True(sinContar.EsPermitido);
            Assert.Null(sinContar.Reserva);
        }

        Assert.Empty(_motor.ContadoresUsados);

        Assert.True((await _control.Reservar(Directa)).EsPermitido);
        Assert.True((await _control.Reservar(Directa)).EsPermitido);
        var rechazo = await _control.Reservar(Directa);

        Assert.Equal(EstadoDeLimite.Excedido, rechazo.Estado);
        Assert.All(_motor.ContadoresUsados, c => Assert.StartsWith("d:", c, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reservar_ConLaPoliticaDeLaDireccionDirectaYSujeto_Lanza()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _control.Reservar(Directa, sujeto: "u-1"));
    }

    private async Task<ResultadoDeLimite> Agotar(ClaveDePolitica politica, int cupos)
    {
        for (var i = 0; i < cupos; i++)
        {
            Assert.True((await _control.Reservar(politica)).EsPermitido);
        }

        return await _control.Reservar(politica);
    }
}
