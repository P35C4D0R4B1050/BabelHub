using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Domain.Plataforma;
using BabelHub.Inventario.UnitTests.Soporte;
using static BabelHub.Inventario.UnitTests.Reservas.EntornoDeReservas;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>T12: los límites de reservar (SEL-13, SEL-14, SEL-15, SEL-18, SEL-19): una pendiente por cliente, 5 por hora por cliente y 10 por hora por origen, con respuesta uniforme.</summary>
public sealed class ReservarUnidadesLimitesTests
{
    private static (EntornoDeReservas Entorno, Guid Libro) Escenario()
    {
        var entorno = new EntornoDeReservas();
        var libro = entorno.Impresa();
        entorno.Base.Existencias.Poner(entorno.Base.Lima.Id, libro, 100, 0);
        return (entorno, libro);
    }

    private static RechazoPorLimite Rechazo(Resultado<ReservaDelPedido> resultado)
    {
        Assert.Equal((CategoriaDeError.Limite, "limite-excedido"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.True(DetalleDeLimiteDeReservas.TryLeer(resultado.Error.Errores, out var rechazo));
        return rechazo!;
    }

    /// <summary>Una reserva que se confirma al instante: deja de ser pendiente y así se prueba solo el contador por hora.</summary>
    private static async Task ReservaYConfirma(EntornoDeReservas entorno, Guid libro)
    {
        var resultado = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]));
        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.True(entorno.Reservas.Guardadas[^1].Confirmar(entorno.Base.Reloj.Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-13")]
    public async Task UnClienteConUnaReservaPendienteVigente_NoIniciaOtraEnOtroPedido_ConElInstanteEnQueVenceLaSuya()
    {
        var (entorno, libro) = Escenario();
        var primera = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]));
        Assert.True(primera.EsExito);

        var segunda = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]));

        var rechazo = Rechazo(segunda);
        Assert.Equal(MotivosDeLimiteDeReservas.PendientePorCliente, rechazo.Motivo);
        Assert.Equal(primera.Valor.ExpiraEn, rechazo.DisponibleDesde);
        Assert.Equal(15 * 60, rechazo.EsperaEnSegundos);
        Assert.Single(entorno.Reservas.Guardadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-13")]
    public async Task ElRechazoPorUnaPendienteDejaUnEventoDeSeguridadConElClienteYElMotivo_UnaSolaVez()
    {
        var (entorno, libro) = Escenario();
        await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]));

        await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]));

        var evento = Assert.Single(entorno.Eventos.Eventos);
        Assert.Equal((TipoDeEventoDeSeguridad.LimiteExcedido, MotivosDeLimiteDeReservas.PendientePorCliente, entorno.ClienteId.ToString("D")), (evento.Tipo, evento.Limite, evento.Usuario));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-13")]
    public async Task UnaPendienteConfirmadaOVencidaNoImpideIniciarOtra()
    {
        var (entorno, libro) = Escenario();
        await ReservaYConfirma(entorno, libro);

        Assert.True((await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]))).EsExito);

        // Vencida: pasan 16 minutos y la segunda ya no cuenta como pendiente vigente.
        entorno.Base.Reloj.Ahora = entorno.Base.Reloj.Ahora.AddMinutes(16);
        Assert.True((await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]))).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-14")]
    public async Task LaSextaReservaEnUnaHoraDeUnMismoCliente_SeRechazaConElInstanteDeReintento()
    {
        var (entorno, libro) = Escenario();
        var primera = entorno.Base.Reloj.Ahora;
        for (var i = 0; i < 5; i++)
        {
            await ReservaYConfirma(entorno, libro);
            entorno.Base.Reloj.Ahora = entorno.Base.Reloj.Ahora.AddMinutes(1);
        }

        var sexta = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]));

        var rechazo = Rechazo(sexta);
        Assert.Equal(MotivosDeLimiteDeReservas.PorHoraPorCliente, rechazo.Motivo);
        Assert.Equal(primera.AddHours(1), rechazo.DisponibleDesde);
        Assert.True(rechazo.EsperaEnSegundos > 0);
        Assert.Equal(5, entorno.Reservas.Guardadas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-14")]
    public async Task PasadaLaHoraDeLaPrimera_SeVuelveAAceptar()
    {
        var (entorno, libro) = Escenario();
        for (var i = 0; i < 5; i++)
        {
            await ReservaYConfirma(entorno, libro);
        }

        Assert.Equal(CategoriaDeError.Limite, (await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]))).Error!.Categoria);
        entorno.Base.Reloj.Ahora = entorno.Base.Reloj.Ahora.AddMinutes(61);

        Assert.True((await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]))).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-19")]
    public async Task LaDecimaPrimeraReservaDeUnMismoOrigen_SeRechazaAunqueSeanOtrosClientes()
    {
        var (entorno, libro) = Escenario();
        for (var i = 0; i < 10; i++)
        {
            entorno.Base.Actor.Establecer(ActorDePrueba.DeCliente().Actual);
            var resultado = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]));
            Assert.True(resultado.EsExito, resultado.Error?.Clave);
        }

        entorno.Base.Actor.Establecer(ActorDePrueba.DeCliente().Actual);
        var undecima = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]));

        Assert.Equal(MotivosDeLimiteDeReservas.PorHoraPorOrigen, Rechazo(undecima).Motivo);
        Assert.Equal(10, entorno.Reservas.Guardadas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-19")]
    public async Task SinOrigenDelClienteLosLimitesPorOrigenNoCuentan_YNoSeGuardaHuellaDeOrigen()
    {
        var (entorno, libro) = Escenario();
        // Una dirección reenviada ignorada (SEG-32): el origen es el de la dirección directa y no rigen los límites por origen.
        entorno.Origen.Actual = new OrigenDeSolicitud(entorno.Origen.Actual.Huella, entorno.Origen.Actual.Direccion) { AplicaLimitesPorOrigen = false };
        for (var i = 0; i < 11; i++)
        {
            entorno.Base.Actor.Establecer(ActorDePrueba.DeCliente().Actual);
            Assert.True((await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]))).EsExito);
        }

        Assert.All(entorno.Reservas.Solicitudes, s => Assert.Null(s.HuellaDeOrigen));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-19")]
    public async Task LaReservaGuardaLaHuellaDeOrigenSoloSiEsConocida()
    {
        var (entorno, libro) = Escenario();

        await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]));

        Assert.Equal(entorno.Origen.Actual.Huella.ToString(), Assert.Single(entorno.Reservas.Solicitudes).HuellaDeOrigen);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-15")]
    public async Task LaRespuestaDeLimiteEsUniformeParaTodosLosMotivos_SoloCambianElMotivoYLosTiempos()
    {
        var (entorno, libro) = Escenario();
        await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]));
        var porPendiente = await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]));

        var (otro, libroDelOtro) = Escenario();
        for (var i = 0; i < 5; i++)
        {
            await ReservaYConfirma(otro, libroDelOtro);
        }

        var porHora = await otro.Reservar().Ejecutar(ARecojo("lima", [Linea(libroDelOtro)]));

        Assert.Equal((porPendiente.Error!.Categoria, porPendiente.Error.Clave), (porHora.Error!.Categoria, porHora.Error.Clave));
        Assert.Equal(porPendiente.Error.Errores.Select(e => e.Campo), porHora.Error.Errores.Select(e => e.Campo));
        Assert.DoesNotContain(porPendiente.Error.Errores.Concat(porHora.Error.Errores), e => e.Clave.Contains(entorno.ClienteId.ToString("D"), StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-18")]
    public async Task UnaRepeticionIdempotenteNoConsumeElLimiteDeReservasPorHora()
    {
        var (entorno, libro) = Escenario();
        var entrada = ARecojo("lima", [Linea(libro)]);
        Assert.True((await entorno.Reservar().Ejecutar(entrada)).EsExito);
        Assert.True(entorno.Reservas.Guardadas[0].Confirmar(entorno.Base.Reloj.Ahora));

        for (var i = 0; i < 5; i++)
        {
            Assert.True((await entorno.Reservar().Ejecutar(entrada)).Valor.EsRepeticion);
        }

        Assert.True((await entorno.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]))).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-14")]
    public void ElDetalleDeUnRechazoPorLimiteViajaYSeLeeSinPerderDatos()
    {
        var rechazo = new RechazoPorLimite(MotivosDeLimiteDeReservas.PorHoraPorOrigen, 1234, new DateTimeOffset(2026, 11, 14, 16, 30, 5, TimeSpan.Zero));

        Assert.True(DetalleDeLimiteDeReservas.TryLeer(DetalleDeLimiteDeReservas.Codificar(rechazo), out var leido));
        Assert.Equal(rechazo, leido);
        Assert.False(DetalleDeLimiteDeReservas.TryLeer([], out _));
    }
}
