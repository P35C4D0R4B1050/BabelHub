using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;
using BabelHub.Inventario.Domain.Plataforma;

namespace BabelHub.Inventario.UnitTests.Plataforma;

public sealed class EvaluarLimiteTests
{
    private const string Resumen = "k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE";

    private static readonly ClaveDePolitica General = new("solicitudes-generales");
    private static readonly ClaveDePolitica Reservas = new("reservas-por-cliente");
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
            new PoliticaDeLimite(Reservas.Valor, 2, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Usuario, liberable: true),
            new PoliticaDeLimite(Directa.Valor, 2, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.OrigenDirecto),
        ]);

        _control = new EvaluarLimite(new CatalogoDePoliticas(politicas), _motor, _local, _origen, _eventos, _reloj);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-27")]
    public async Task Reservar_ConElPresupuestoDelPersonalExcedido_PublicaElEventoConElUsuarioYNoAfectaAOtro()
    {
        for (var i = 0; i < 30; i++)
        {
            Assert.True((await _control.Reservar(PoliticasDeLimite.TrasladosDelPersonal, sujeto: "u-1")).EsPermitido);
        }

        var rechazo = await _control.Reservar(PoliticasDeLimite.TrasladosDelPersonal, sujeto: "u-1");
        var otro = await _control.Reservar(PoliticasDeLimite.TrasladosDelPersonal, sujeto: "u-2");

        Assert.Equal(EstadoDeLimite.Excedido, rechazo.Estado);
        Assert.True(otro.EsPermitido);
        var evento = Assert.Single(_eventos.Eventos);
        Assert.Equal("traslados-del-personal", evento.Limite);
        Assert.Equal("u-1", evento.Usuario);
        Assert.Equal("203.0.113.7", evento.DireccionCompleta);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-08")]
    public async Task Reservar_ConCincuentaRechazosDelMismoOrigen_PublicaCincuentaEventosParaQueAuditoriaLosAgrupe()
    {
        await _control.Reservar(General);
        await _control.Reservar(General);
        await _control.Reservar(General);

        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(EstadoDeLimite.Excedido, (await _control.Reservar(General)).Estado);
        }

        Assert.Equal(50, _eventos.Eventos.Count);
        Assert.Equal(50, await _motor.ContarRechazosDeOrigen(_origen.Actual.Huella, EvaluarLimite.VentanaDeRechazosDelOrigen, CancellationToken.None));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-19")]
    public async Task Reservar_ConUnaReservaRepetidaConLaMismaClave_NoCuentaParaElLimiteDeReservasPeroSiParaElGeneral()
    {
        var clave = new ClaveDeIdempotencia("reserva-1", "/reservas", "huella-cuerpo");

        var primera = await _control.Reservar(Reservas, sujeto: "c-1", idempotencia: clave);
        await _control.Completar(primera.Reserva!, new ResultadoMinimo("creada", "res-1"));
        var repeticion = await _control.Reservar(Reservas, sujeto: "c-1", idempotencia: clave);
        var otra = await _control.Reservar(Reservas, sujeto: "c-1", idempotencia: new ClaveDeIdempotencia("reserva-2", "/reservas", "otro"));
        var tercera = await _control.Reservar(Reservas, sujeto: "c-1", idempotencia: new ClaveDeIdempotencia("reserva-3", "/reservas", "otro-mas"));

        Assert.Equal(EstadoDeLimite.RepeticionCompletada, repeticion.Estado);
        Assert.True(otra.EsPermitido);
        Assert.Equal(EstadoDeLimite.Excedido, tercera.Estado);

        for (var i = 0; i < 3; i++)
        {
            Assert.True((await _control.Reservar(General)).EsPermitido);
        }

        Assert.Equal(EstadoDeLimite.Excedido, (await _control.Reservar(General)).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-19")]
    public async Task Fallar_LiberaLaClaveParaQueElClientePuedaReintentar()
    {
        var clave = new ClaveDeIdempotencia("reserva-1", "/reservas", "cuerpo-a");
        var primera = await _control.Reservar(Reservas, sujeto: "c-1", idempotencia: clave);

        await _control.Fallar(primera.Reserva!);

        Assert.True((await _control.Reservar(Reservas, sujeto: "c-1", idempotencia: clave)).EsPermitido);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-20")]
    public async Task Reservar_DesdeUnOrigenDesconocido_CuentaEnUnUnicoContadorCompartido()
    {
        _origen.Actual = OrigenDeSolicitud.Desconocido;
        await _control.Reservar(General);
        await _control.Reservar(General);
        await _control.Reservar(General);

        var rechazo = await _control.Reservar(General);

        Assert.Equal(EstadoDeLimite.Excedido, rechazo.Estado);
        Assert.All(_motor.ContadoresUsados, c => Assert.Equal("o:0:desconocido", c));
        Assert.Null(Assert.Single(_eventos.Eventos).DireccionCompleta);
    }

    [Fact]
    public async Task Liberar_ConUnaPoliticaLiberable_DevuelveElCupoYUnaNoLiberableNo()
    {
        var uno = await _control.Reservar(Reservas, sujeto: "c-1");
        await _control.Reservar(Reservas, sujeto: "c-1");
        await _control.Liberar(uno.Reserva!);

        Assert.True((await _control.Reservar(Reservas, sujeto: "c-1")).EsPermitido);

        var ajuste = await _control.Reservar(PoliticasDeLimite.AjustesDelPersonal, sujeto: "u-1");
        await _control.Liberar(ajuste.Reserva!);
        Assert.Equal(1, _motor.Cupos("u:s:u-1"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-24")]
    public async Task Reservar_ConElAlmacenCaido_FallaCerradoOCaeAlMotorLocalSegunLaPolitica()
    {
        _motor.NoDisponible = true;

        var cerrada = await _control.Reservar(PoliticasDeLimite.AjustesDelPersonal, sujeto: "u-1");
        var conRespaldo = await _control.Reservar(General);

        Assert.Equal(EstadoDeLimite.NoDisponible, cerrada.Estado);
        Assert.True(conRespaldo.EsPermitido);
        Assert.Single(_local.ContadoresUsados);
    }

    [Fact]
    public async Task Reservar_ConElSujetoMalIndicado_Lanza()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _control.Reservar(Reservas));
        await Assert.ThrowsAsync<ArgumentException>(() => _control.Reservar(General, sujeto: "u-1"));
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
}
