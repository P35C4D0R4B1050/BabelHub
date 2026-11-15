using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.UnitTests.Fuentes;
using BabelHub.Inventario.UnitTests.Soporte;
using static BabelHub.Inventario.Application.Reservas.MotivoDeLiberacionDeReserva;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>T16: <c>LiberarReservas</c> (SEL-07, SEL-17, SEL-32, D15): la matriz motivo × estado, los receptores de la cancelación antes de tocar nada y la devolución al vendible sin tocar el físico.</summary>
public sealed class LiberarReservasTests
{
    private static EntornoDeReservas Entorno() => new(ActorDePrueba.DeIntegracion("expiracion-de-reservas"));

    /// <summary>Una reserva de una línea de 2 unidades en el estado dado, con su traslado vinculado, y el stock coherente (lo reservado solo si la línea está activa).</summary>
    private static (ReservaDePedido Reserva, Guid Pedido, Guid Libro, Guid Traslado) ConLinea(EntornoDeReservas e, string estado, bool recojo = true)
    {
        var libro = e.Impresa();
        var pedido = Guid.NewGuid();
        var reserva = e.Pendiente(pedido, [(libro, e.Base.Cusco, 2)], recojoEnLima: recojo);
        var linea = reserva.Lineas.Single();
        var traslado = Guid.NewGuid();
        linea.VincularTraslado(traslado);
        var ahora = e.Base.Reloj.Ahora;
        switch (estado)
        {
            case "confirmada":
                Assert.True(reserva.Confirmar(ahora));
                break;
            case "entrega":
            case "reparto":
            case "traslado":
                Assert.True(reserva.Confirmar(ahora));
                linea.Cerrar(estado == "entrega" ? CausaDeCierre.Entrega : estado == "reparto" ? CausaDeCierre.Reparto : CausaDeCierre.Traslado, ahora);
                break;
            case "liberada":
                linea.Liberar(ahora);
                break;
            case "expirada":
                linea.Expirar(ahora);
                break;
        }

        var activa = estado is "pendiente" or "confirmada";
        e.Base.Existencias.Poner(e.Base.Cusco.Id, libro, 10, activa ? 2 : 0);
        return (reserva, pedido, libro, traslado);
    }

    private static LiberarReservasEntrada Pide(Guid pedido, MotivoDeLiberacionDeReserva motivo, params Guid[] lineas) => new(pedido, motivo, lineas.Length == 0 ? null : lineas);

    public static TheoryData<MotivoDeLiberacionDeReserva, string, string> Matriz()
    {
        var datos = new TheoryData<MotivoDeLiberacionDeReserva, string, string>();
        foreach (var motivo in new[] { CheckoutCancelado, PagoVencido })
        {
            datos.Add(motivo, "pendiente", "libera");
            datos.Add(motivo, "confirmada", "invalido");
            datos.Add(motivo, "entrega", "invalido");
            datos.Add(motivo, "reparto", "invalido");
            datos.Add(motivo, "traslado", "invalido");
            datos.Add(motivo, "liberada", "sin-cambios");
            datos.Add(motivo, "expirada", "sin-cambios");
        }

        foreach (var motivo in new[] { PedidoAnulado, CanceladoPorElCliente, FallaPosteriorAlPago })
        {
            datos.Add(motivo, "pendiente", "libera");
            datos.Add(motivo, "confirmada", "libera");
            datos.Add(motivo, "entrega", "invalido");
            datos.Add(motivo, "reparto", "invalido");
            datos.Add(motivo, "traslado", "anula");
            datos.Add(motivo, "liberada", "sin-cambios");
            datos.Add(motivo, "expirada", "sin-cambios");
        }

        datos.Add(NoRecogido, "pendiente", "invalido");
        datos.Add(NoRecogido, "confirmada", "libera");
        datos.Add(NoRecogido, "entrega", "invalido");
        datos.Add(NoRecogido, "reparto", "invalido");
        datos.Add(NoRecogido, "traslado", "anula");
        datos.Add(NoRecogido, "liberada", "sin-cambios");
        datos.Add(NoRecogido, "expirada", "sin-cambios");
        return datos;
    }

    [Theory]
    [MemberData(nameof(Matriz))]
    [Trait("Criterio", "AC-SEL-07")]
    [Trait("Criterio", "AC-SEL-17")]
    public async Task LaMatrizMotivoPorEstado_DeD15(MotivoDeLiberacionDeReserva motivo, string estado, string esperado)
    {
        var e = Entorno();
        var (reserva, pedido, libro, traslado) = ConLinea(e, estado);
        var linea = reserva.Lineas.Single();
        var anulaElPedido = motivo is PedidoAnulado or CanceladoPorElCliente or FallaPosteriorAlPago or NoRecogido;

        var resultado = await e.Liberar().Ejecutar(Pide(pedido, motivo));

        var guardada = e.Reservas.Guardadas.Single().Lineas.Single();
        switch (esperado)
        {
            case "libera":
                Assert.True(resultado.EsExito, resultado.Error?.Clave);
                Assert.Equal(EstadoDeLinea.Liberada, guardada.Estado);
                Assert.Equal((10, 0), e.FilaDe(e.Base.Cusco, libro));
                Assert.Equal("2", Assert.Single(e.Auditoria.Registros).Solicitud.Detalle["cantidad"]);
                break;

            case "anula":
                Assert.True(resultado.EsExito, resultado.Error?.Clave);
                Assert.Equal(EstadoDeLinea.Cerrada, guardada.Estado);
                Assert.NotNull(guardada.AnuladaEn);
                Assert.Equal((10, 0), e.FilaDe(e.Base.Cusco, libro));
                Assert.Equal("venta-anulada", Assert.Single(e.Auditoria.Registros).Solicitud.Detalle["causa"]);
                break;

            case "sin-cambios":
                Assert.True(resultado.EsExito, resultado.Error?.Clave);
                Assert.True(resultado.Valor.EsRepeticion);
                Assert.Equal((10, 0), e.FilaDe(e.Base.Cusco, libro));
                Assert.Empty(e.Auditoria.Registros);
                break;

            default:
                Assert.Equal((CategoriaDeError.Conflicto, "reserva-estado-invalido"), (resultado.Error!.Categoria, resultado.Error.Clave));
                Assert.Equal(linea.Estado, guardada.Estado);
                Assert.Empty(e.Auditoria.Registros);
                break;
        }

        // Los receptores de la cancelación: solo con los motivos que anulan el pedido y nunca ante un rechazo (los traslados no se cancelan si la liberación se rechaza).
        var debeAvisar = anulaElPedido && esperado != "invalido";
        Assert.Equal(debeAvisar ? 1 : 0, e.Cancelaciones.Count);
        if (debeAvisar)
        {
            Assert.Equal((pedido, motivo), (e.Cancelaciones[0].Pedido, e.Cancelaciones[0].Motivo));
            Assert.Equal([traslado], e.Cancelaciones[0].Traslados);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-07")]
    public async Task LiberarNoTocaElFisico_SoloLoReservado()
    {
        var e = Entorno();
        var (_, pedido, libro, _) = ConLinea(e, "confirmada");

        await e.Liberar().Ejecutar(Pide(pedido, PedidoAnulado));

        Assert.Equal((10, 0), e.FilaDe(e.Base.Cusco, libro));
        Assert.All(e.Base.Existencias.Movimientos, m => Assert.Equal(0, m.DeltaFisico));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-07")]
    public async Task RepetirLaLiberacion_NoCambiaElStockOtraVez_PeroSigueAvisandoALosReceptores()
    {
        var e = Entorno();
        var (_, pedido, libro, traslado) = ConLinea(e, "confirmada");
        Assert.True((await e.Liberar().Ejecutar(Pide(pedido, PedidoAnulado))).EsExito);

        var repetida = await e.Liberar().Ejecutar(Pide(pedido, PedidoAnulado));

        Assert.True(repetida.EsExito);
        Assert.True(repetida.Valor.EsRepeticion);
        Assert.Equal((10, 0), e.FilaDe(e.Base.Cusco, libro));
        Assert.Single(e.Auditoria.Registros);
        Assert.Equal(2, e.Cancelaciones.Count);
        Assert.All(e.Cancelaciones, c => Assert.Equal([traslado], c.Traslados));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-17")]
    public async Task LosReceptoresSeInvocanAntesDeTocarLineasYStock()
    {
        var e = Entorno();
        var (_, pedido, _, _) = ConLinea(e, "confirmada");

        await e.Liberar().Ejecutar(Pide(pedido, PedidoAnulado));

        var orden = e.Base.Bitacora.Llamadas;
        Assert.True(orden.IndexOf("traslados.AlLiberar") < orden.IndexOf("existencias.BloquearFilas"));
        Assert.True(orden.IndexOf("reservas.BloquearLineas") < orden.IndexOf("existencias.BloquearFilas"));
        Assert.True(orden.IndexOf("existencias.BloquearFilas") < orden.IndexOf("reservas.LiberarLineasPorId"));
        Assert.True(orden.IndexOf("reservas.LiberarLineasPorId") < orden.IndexOf("existencias.Aplicar(0,-2)"));
        Assert.True(orden.IndexOf("bloqueo.Pedido") < orden.IndexOf("traslados.AlLiberar"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-17")]
    public async Task SoloLosTrasladosDeLasLineasAfectadasLlegaAlReceptor()
    {
        var e = Entorno();
        var a = e.Impresa();
        var b = e.Impresa();
        var pedido = Guid.NewGuid();
        var reserva = e.Pendiente(pedido, [(a, e.Base.Cusco, 1), (b, e.Base.Cusco, 1)]);
        var trasladoA = Guid.NewGuid();
        var trasladoB = Guid.NewGuid();
        reserva.Lineas.Single(l => l.EdicionId == a).VincularTraslado(trasladoA);
        reserva.Lineas.Single(l => l.EdicionId == b).VincularTraslado(trasladoB);
        e.Base.Existencias.Poner(e.Base.Cusco.Id, a, 10, 1);
        e.Base.Existencias.Poner(e.Base.Cusco.Id, b, 10, 1);

        var resultado = await e.Liberar().Ejecutar(Pide(pedido, PedidoAnulado, reserva.Lineas.Single(l => l.EdicionId == a).LineaId));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal([trasladoA], Assert.Single(e.Cancelaciones).Traslados);
        Assert.Equal((10, 0), e.FilaDe(e.Base.Cusco, a));
        Assert.Equal((10, 1), e.FilaDe(e.Base.Cusco, b));
        Assert.Equal(EstadoDeLinea.Pendiente, reserva.Lineas.Single(l => l.EdicionId == b).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-07")]
    public async Task UnaLineaInexistenteEnLineaIds_EsNoEncontrada_YUnPedidoSinReservaTambien()
    {
        var e = Entorno();
        var (_, pedido, _, _) = ConLinea(e, "pendiente");

        var linea = await e.Liberar().Ejecutar(Pide(pedido, CheckoutCancelado, Guid.NewGuid()));
        var sinReserva = await e.Liberar().Ejecutar(Pide(Guid.NewGuid(), CheckoutCancelado));

        Assert.Equal(CategoriaDeError.NoEncontrado, linea.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoEncontrado, sinReserva.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-32")]
    public async Task SiLaAuditoriaFalla_LaLiberacionNoSeAplicaAlStock()
    {
        var e = Entorno();
        var (_, pedido, libro, _) = ConLinea(e, "pendiente");
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => e.Liberar().Ejecutar(Pide(pedido, CheckoutCancelado)));
        // El doble no es transaccional: que la excepción revierta el stock y las líneas lo prueba LiberarReservasPostgresTests con PostgreSQL real (AC-SEL-32).
        Assert.Equal((10, 0), e.FilaDe(e.Base.Cusco, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-17")]
    public async Task SinReceptoresCompuestos_AnularUnPedidoConTrasladosFallaALaVista_YNoSeLiberaNada()
    {
        var e = Entorno();
        e.ConReceptores = false;
        var (reserva, pedido, libro, _) = ConLinea(e, "confirmada");

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Liberar().Ejecutar(Pide(pedido, PedidoAnulado)));

        Assert.Equal(EstadoDeLinea.Confirmada, e.Reservas.Guardadas.Single().Lineas.Single().Estado);
        Assert.Equal((10, 2), e.FilaDe(e.Base.Cusco, libro));
        Assert.NotNull(reserva);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-35")]
    [InlineData("cliente")]
    [InlineData("otra-integracion")]
    [InlineData("sin-actor")]
    public async Task Liberar_SoloLaIntegracionDeExpiracionDeReservas(string quien)
    {
        var actor = quien switch
        {
            "cliente" => ActorDePrueba.DeCliente(),
            "otra-integracion" => ActorDePrueba.DeIntegracion("baja-de-titular"),
            _ => ActorDePrueba.SinContexto(),
        };
        var e = new EntornoDeReservas(actor);
        var (_, pedido, libro, _) = ConLinea(e, "pendiente");

        var resultado = await e.Liberar().Ejecutar(Pide(pedido, CheckoutCancelado));

        Assert.Equal(quien == "sin-actor" ? CategoriaDeError.NoAutenticado : CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal((10, 2), e.FilaDe(e.Base.Cusco, libro));
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-17")]
    [InlineData("pedido-vacio")]
    [InlineData("motivo-inventado")]
    [InlineData("lineas-repetidas")]
    public async Task Liberar_ConUnaEntradaInvalida_EsValidacion(string caso)
    {
        var repetida = Guid.NewGuid();
        var entrada = caso switch
        {
            "pedido-vacio" => Pide(Guid.Empty, CheckoutCancelado),
            "motivo-inventado" => new LiberarReservasEntrada(Guid.NewGuid(), (MotivoDeLiberacionDeReserva)42, null),
            _ => new LiberarReservasEntrada(Guid.NewGuid(), CheckoutCancelado, [repetida, repetida]),
        };

        var resultado = await Entorno().Liberar().Ejecutar(entrada);

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-17")]
    public async Task UnVolumenAnomaloDeLiberaciones_AvisaUnaSolaVezPorVentana_SinRechazarNada()
    {
        var e = Entorno();
        e.UmbralDeVolumen = 2;
        for (var i = 0; i < 5; i++)
        {
            var (_, pedido, _, _) = ConLinea(e, "pendiente");
            Assert.True((await e.Liberar().Ejecutar(Pide(pedido, CheckoutCancelado))).EsExito);
        }

        Assert.Equal(["motivo-de-alerta-no-catalogado"], e.Alertas.Tecnicas);
        Assert.Equal(EntornoDeFuentes.Ahora, e.Base.Reloj.Ahora);
    }
}
