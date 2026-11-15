using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.UnitTests.Soporte;
using BabelHub.Shared.Contratos;
using static BabelHub.Inventario.UnitTests.Reservas.EntornoDeReservas;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>Las correcciones de la revisión de INV-2b: el envío de <c>ya-cerrada</c>, la auditoría encadenada, los traslados sin cabos sueltos, la liberación parcial con dos filas, el origen en los eventos propios y la repetición vencida.</summary>
public sealed class CorreccionesDeRevisionDeReservasTests
{
    private static ActorActual Logistica() => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.PersonalDeLogistica);

    private static (EntornoDeReservas E, Guid Pedido, Guid Libro, Guid Linea) ADomicilioConfirmada()
    {
        var e = new EntornoDeReservas(ActorDePrueba.DeAdministradorCentral());
        var libro = e.Impresa();
        var pedido = Guid.NewGuid();
        var reserva = e.Pendiente(pedido, [(libro, e.Base.Cusco, 1)], recojoEnLima: false);
        Assert.True(reserva.Confirmar(e.Base.Reloj.Ahora));
        e.Base.Existencias.Poner(e.Base.Cusco.Id, libro, 9, 1);
        return (e, pedido, libro, reserva.Lineas.Single().LineaId);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-21")]
    public async Task YaCerrada_ExigeElMismoEnvio_UnEnvioDistinto_EsEstadoInvalido()
    {
        var (e, pedido, libro, linea) = ADomicilioConfirmada();
        e.Base.Actor.Establecer(Logistica());
        var envio = Guid.NewGuid();
        Assert.True((await e.Cerrar().Ejecutar(new CerrarReservasEntrada(pedido, MotivoDeCierreDeReserva.Reparto, [linea], envio, ClaveNueva()))).EsExito);

        var mismo = await e.Cerrar().Ejecutar(new CerrarReservasEntrada(pedido, MotivoDeCierreDeReserva.Reparto, [linea], envio, ClaveNueva()));
        var otro = await e.Cerrar().Ejecutar(new CerrarReservasEntrada(pedido, MotivoDeCierreDeReserva.Reparto, [linea], Guid.NewGuid(), ClaveNueva()));

        Assert.Equal([true], mismo.Valor.Lineas.Select(l => l.YaCerrada));
        Assert.Equal((CategoriaDeError.Conflicto, "reserva-estado-invalido"), (otro.Error!.Categoria, otro.Error.Clave));
        Assert.Equal((8, 0), e.FilaDe(e.Base.Cusco, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-32")]
    public async Task LaAuditoriaDelCierreEncadenaElFisicoLineaALinea_CuandoDosLineasCompartenFila()
    {
        var e = new EntornoDeReservas(ActorDePrueba.DeAdministradorCentral());
        var libro = e.Impresa();
        var pedido = Guid.NewGuid();
        var reserva = e.Pendiente(pedido, [(libro, e.Base.Lima, 2), (libro, e.Base.Lima, 3)]);
        Assert.True(reserva.Confirmar(e.Base.Reloj.Ahora));
        e.Base.Existencias.Poner(e.Base.Lima.Id, libro, 10, 5);

        var resultado = await e.Cerrar().Ejecutar(new CerrarReservasEntrada(pedido, MotivoDeCierreDeReserva.Entrega, [.. reserva.Lineas.Select(l => l.LineaId)], null, ClaveNueva()));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((5, 0), e.FilaDe(e.Base.Lima, libro));
        var pares = e.Auditoria.Registros.Select(r => (int.Parse(r.Solicitud.Detalle["cantidad"]!), int.Parse(r.Solicitud.Detalle["cantidadAnterior"]!), int.Parse(r.Solicitud.Detalle["cantidadPosterior"]!))).OrderBy(p => p.Item1).ToList();
        Assert.Equal([(2, 10, 8), (3, 8, 5)], pares);
    }

    private sealed class GeneradorIncompleto : IGeneradorDeTrasladosDePedido
    {
        public Task<IReadOnlyList<TrasladoVinculadoAlPedido>> Generar(Guid pedidoId, Guid sucursalDeRecojoId, IReadOnlyCollection<SolicitudDeTrasladoDePedido> solicitudes, CancellationToken cancelacion = default) =>
            Task.FromResult<IReadOnlyList<TrasladoVinculadoAlPedido>>([]);
    }

    private sealed class GeneradorDeOrigenDesconocido : IGeneradorDeTrasladosDePedido
    {
        public Task<IReadOnlyList<TrasladoVinculadoAlPedido>> Generar(Guid pedidoId, Guid sucursalDeRecojoId, IReadOnlyCollection<SolicitudDeTrasladoDePedido> solicitudes, CancellationToken cancelacion = default) =>
            Task.FromResult<IReadOnlyList<TrasladoVinculadoAlPedido>>([.. solicitudes.Select(s => new TrasladoVinculadoAlPedido(Guid.NewGuid(), s.EdicionId, s.Cantidad, "origen-que-no-existe", "lima", "solicitado", null))]);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-03")]
    [InlineData("incompleto")]
    [InlineData("origen-desconocido")]
    public async Task Confirmar_SiTrasladosNoCubreTodasLasLineas_FallaALaVista(string caso)
    {
        var e = new EntornoDeReservas(ActorDePrueba.DeIntegracion("expiracion-de-reservas"));
        var pedido = Guid.NewGuid();
        e.Pendiente(pedido, [(e.Impresa(), e.Base.Cusco, 1)]);
        IGeneradorDeTrasladosDePedido generador = caso == "incompleto" ? new GeneradorIncompleto() : new GeneradorDeOrigenDesconocido();
        var confirmar = new ConfirmarReservas(e.Autorizador, new ValidadorDeConfirmar(), e.Bloqueos, e.Reservas, e.Directorio, generador, e.Auditoria, e.Volumen, new ConstructorDeLaReserva(e.Directorio), e.Base.Reloj);

        await Assert.ThrowsAsync<InvalidOperationException>(() => confirmar.Ejecutar(new ConfirmarReservasEntrada(pedido, ClaveNueva())));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-07")]
    public async Task LiberarUnaLineaDelPedidoQueTieneDosFilas_NoEsNoEncontrada()
    {
        var e = new EntornoDeReservas(ActorDePrueba.DeIntegracion("expiracion-de-reservas"));
        var libro = e.Impresa();
        var pedido = Guid.NewGuid();
        var lineaDelPedido = Guid.NewGuid();
        var ahora = e.Base.Reloj.Ahora;
        var traslado = Guid.NewGuid();

        // Tras recibir el traslado: la fila del origen, cerrada por traslado, y la del destino, confirmada y creada por él (mismo identificador de línea del pedido).
        var origen = LineaDeReserva.Rehidratar(Guid.NewGuid(), lineaDelPedido, libro, e.Base.Cusco.Id, 1, EstadoDeLinea.Cerrada, CausaDeCierre.Traslado, traslado, false, true, ahora, null, ahora);
        var destino = LineaDeReserva.Rehidratar(Guid.NewGuid(), lineaDelPedido, libro, e.Base.Lima.Id, 1, EstadoDeLinea.Confirmada, null, traslado, true, false, ahora, null, ahora);
        e.Reservas.Sembrar(
            ReservaDePedido.Rehidratar(Guid.NewGuid(), pedido, Guid.NewGuid(), TipoDeEntrega.Recojo, e.Base.Lima.Id, null, ahora.AddMinutes(-5), null, false, 0, [origen, destino]),
            new DatosDeLaSolicitud(ClaveNueva(), new string('a', 64), null));
        e.Base.Existencias.Poner(e.Base.Lima.Id, libro, 5, 1);

        var resultado = await e.Liberar().Ejecutar(new LiberarReservasEntrada(pedido, MotivoDeLiberacionDeReserva.PedidoAnulado, [lineaDelPedido]));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((5, 0), e.FilaDe(e.Base.Lima, libro));
        var guardadas = e.Reservas.Guardadas.Single().Lineas;
        Assert.Equal(EstadoDeLinea.Liberada, guardadas.Single(l => l.FuenteId == e.Base.Lima.Id).Estado);
        Assert.NotNull(guardadas.Single(l => l.FuenteId == e.Base.Cusco.Id).AnuladaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-13")]
    public async Task ElEventoPropioDeUnLimiteLlevaElOrigenConocido_YNadaSiNoRigeElOrigen()
    {
        var e = new EntornoDeReservas();
        var libro = e.Impresa();
        e.Base.Existencias.Poner(e.Base.Lima.Id, libro, 10, 0);
        Assert.True((await e.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]))).EsExito);

        await e.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]));
        var conOrigen = Assert.Single(e.Eventos.Eventos);
        Assert.Equal(e.Origen.Actual.Huella, conOrigen.Origen);
        Assert.Equal(e.Origen.Actual.Direccion, conOrigen.DireccionCompleta);

        e.Origen.Actual = new OrigenDeSolicitud(e.Origen.Actual.Huella, e.Origen.Actual.Direccion) { AplicaLimitesPorOrigen = false };
        await e.Reservar().Ejecutar(ARecojo("lima", [Linea(libro)]));
        var sinOrigen = e.Eventos.Eventos[^1];
        Assert.Null(sinOrigen.Origen);
        Assert.Null(sinOrigen.DireccionCompleta);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public async Task LaRepeticionDeUnaReservaVencidaSinBarrer_SeMuestraExpirada()
    {
        var e = new EntornoDeReservas();
        var libro = e.Impresa();
        e.Base.Existencias.Poner(e.Base.Lima.Id, libro, 10, 0);
        var entrada = ARecojo("lima", [Linea(libro)]);
        Assert.True((await e.Reservar().Ejecutar(entrada)).EsExito);
        e.Base.Reloj.Ahora = e.Base.Reloj.Ahora.AddMinutes(20);

        var repetida = await e.Reservar().Ejecutar(entrada);

        Assert.True(repetida.Valor.EsRepeticion);
        Assert.Equal(EstadoDeReserva.Expirada, repetida.Valor.Estado);
    }
}
