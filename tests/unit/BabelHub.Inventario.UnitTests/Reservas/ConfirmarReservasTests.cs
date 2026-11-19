using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.UnitTests.Fuentes;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>T14: <c>ConfirmarReservas</c> (SEL-03, SEL-06, SEL-32): líneas pendientes a confirmadas sin expiración, traslados en la misma transacción, repetición sin cambios y auditoría que, si falla, no confirma.</summary>
public sealed class ConfirmarReservasTests
{
    private static EntornoDeReservas Entorno() => new(ActorDePrueba.DeIntegracion("expiracion-de-reservas"));

    private static ConfirmarReservasEntrada Entrada(Guid pedido, string? clave = null) => new(pedido, clave ?? EntornoDeReservas.ClaveNueva());

    [Fact]
    [Trait("Criterio", "AC-SEL-06")]
    public async Task Confirmar_PasaLasLineasPendientesAConfirmadas_LaReservaYaNoExpiraYSeAudita()
    {
        var e = Entorno();
        var libro = e.Impresa();
        var pedido = Guid.NewGuid();
        var reserva = e.Pendiente(pedido, [(libro, e.Base.Lima, 3)]);

        var resultado = await e.Confirmar().Ejecutar(Entrada(pedido));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((EstadoDeReserva.Confirmada, null, false), (resultado.Valor.Estado, resultado.Valor.ExpiraEn, resultado.Valor.EsRepeticion));
        Assert.Equal(EstadoDeLinea.Confirmada, Assert.Single(reserva.Lineas).Estado);
        Assert.Equal(e.Base.Reloj.Ahora, reserva.Lineas.Single().ConfirmadaEn);
        var (solicitud, actor) = Assert.Single(e.Auditoria.Registros);
        Assert.Equal((AccionesAuditables.ReservaConfirmada, "pedido", pedido.ToString("D"), e.Base.Lima.Id, "3", TipoDeActor.Integracion), (solicitud.Accion, solicitud.ObjetoTipo, solicitud.ObjetoId, solicitud.SucursalOrigen, solicitud.Detalle["cantidad"], actor));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-06")]
    public async Task Repetir_LaConfirmacionConLaMismaOOtraClave_EsExitoSinCambiosNiNuevaAuditoria()
    {
        var e = Entorno();
        var pedido = Guid.NewGuid();
        e.Pendiente(pedido, [(e.Impresa(), e.Base.Lima, 1)]);
        var clave = EntornoDeReservas.ClaveNueva();
        Assert.True((await e.Confirmar().Ejecutar(Entrada(pedido, clave))).EsExito);

        var misma = await e.Confirmar().Ejecutar(Entrada(pedido, clave));
        var otra = await e.Confirmar().Ejecutar(Entrada(pedido));

        Assert.All([misma, otra], r => Assert.True(r.EsExito && r.Valor.EsRepeticion && r.Valor.Estado == EstadoDeReserva.Confirmada));
        Assert.Single(e.Auditoria.Registros);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-06")]
    [InlineData("vencida")]
    [InlineData("expirada")]
    [InlineData("liberada")]
    public async Task Confirmar_UnaReservaVencidaExpiradaOLiberada_SeRechazaYObligaAReiniciarElPago(string estado)
    {
        var e = Entorno();
        var pedido = Guid.NewGuid();
        var reserva = e.Pendiente(pedido, [(e.Impresa(), e.Base.Lima, 1)], haceMinutos: estado == "vencida" ? 20 : 1);
        switch (estado)
        {
            case "expirada":
                reserva.Lineas.Single().Expirar(e.Base.Reloj.Ahora);
                break;
            case "liberada":
                reserva.Lineas.Single().Liberar(e.Base.Reloj.Ahora);
                break;
        }

        var resultado = await e.Confirmar().Ejecutar(Entrada(pedido));

        Assert.Equal((CategoriaDeError.Conflicto, "reserva-estado-invalido"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Empty(e.Auditoria.Registros);
        Assert.Equal(0, e.Traslados.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-06")]
    public async Task Confirmar_UnPedidoSinReserva_EsNoEncontrado()
    {
        var resultado = await Entorno().Confirmar().Ejecutar(Entrada(Guid.NewGuid()));

        Assert.Equal((CategoriaDeError.NoEncontrado, "reserva-no-encontrada"), (resultado.Error!.Categoria, resultado.Error.Clave));
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-35")]
    [InlineData("cliente")]
    [InlineData("otra-integracion")]
    [InlineData("administrador")]
    [InlineData("sin-actor")]
    public async Task Confirmar_SoloLaIntegracionDeExpiracionDeReservas(string quien)
    {
        var actor = quien switch
        {
            "cliente" => ActorDePrueba.DeCliente(),
            "otra-integracion" => ActorDePrueba.DeIntegracion("lectura-de-disponibilidad"),
            "administrador" => ActorDePrueba.DeAdministradorCentral(),
            _ => ActorDePrueba.SinContexto(),
        };
        var e = new EntornoDeReservas(actor);
        var pedido = Guid.NewGuid();
        var reserva = e.Pendiente(pedido, [(e.Impresa(), e.Base.Lima, 1)]);

        var resultado = await e.Confirmar().Ejecutar(Entrada(pedido));

        Assert.Equal(quien == "sin-actor" ? CategoriaDeError.NoAutenticado : CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(EstadoDeLinea.Pendiente, reserva.Lineas.Single().Estado);
        Assert.Single(e.Base.Denegaciones.Eventos);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-06")]
    [InlineData("pedido-vacio")]
    [InlineData("clave-corta")]
    public async Task Confirmar_ConUnaEntradaInvalida_EsValidacion(string caso)
    {
        var entrada = caso == "pedido-vacio" ? Entrada(Guid.Empty) : Entrada(Guid.NewGuid(), "corta");

        var resultado = await Entorno().Confirmar().Ejecutar(entrada);

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-03")]
    public async Task Confirmar_UnPedidoDeRecojoConUnaLineaDeOtraSucursal_PideElTrasladoEnLaMismaTransaccionYLoVincula()
    {
        var e = Entorno();
        var a = e.Impresa();
        var b = e.Impresa();
        var pedido = Guid.NewGuid();
        var reserva = e.Pendiente(pedido, [(a, e.Base.Cusco, 2), (b, e.Base.Lima, 1)]);

        var resultado = await e.Confirmar().Ejecutar(Entrada(pedido));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        var solicitud = Assert.Single(Assert.Single(e.Traslados.Solicitudes));
        Assert.Equal(new SolicitudDeTrasladoDePedido(e.Base.Cusco.Id, a, 2), solicitud);
        var traslado = Assert.Single(resultado.Valor.Traslados);
        Assert.Equal((a, 2, "cusco", "lima"), (traslado.EdicionId, traslado.Cantidad, traslado.CodigoDeOrigen, traslado.CodigoDeDestino));
        Assert.Equal(traslado.TrasladoId, reserva.Lineas.Single(l => l.EdicionId == a).TrasladoId);
        Assert.Null(reserva.Lineas.Single(l => l.EdicionId == b).TrasladoId);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-03")]
    public async Task Confirmar_AlRepetir_DevuelveLosMismosTrasladosYNoCreaOtros()
    {
        var e = Entorno();
        var a = e.Impresa();
        var pedido = Guid.NewGuid();
        e.Pendiente(pedido, [(a, e.Base.Cusco, 2)]);
        var primera = await e.Confirmar().Ejecutar(Entrada(pedido));

        var repetida = await e.Confirmar().Ejecutar(Entrada(pedido));

        Assert.True(repetida.Valor.EsRepeticion);
        Assert.Equal(primera.Valor.Traslados.Single().TrasladoId, repetida.Valor.Traslados.Single().TrasladoId);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-03")]
    public async Task Confirmar_SiTodoSaleDeLaSucursalDeRecojoOEsADomicilio_NoSePideNingunTraslado()
    {
        var e = Entorno();
        var libro = e.Impresa();
        var local = Guid.NewGuid();
        var domicilio = Guid.NewGuid();
        e.Pendiente(local, [(libro, e.Base.Lima, 1)]);
        e.Pendiente(domicilio, [(libro, e.Base.Cusco, 1)], recojoEnLima: false);

        Assert.True((await e.Confirmar().Ejecutar(Entrada(local))).EsExito);
        var aDomicilio = await e.Confirmar().Ejecutar(Entrada(domicilio));

        Assert.True(aDomicilio.EsExito);
        Assert.Empty(aDomicilio.Valor.Traslados);
        Assert.Equal(0, e.Traslados.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-03")]
    public async Task Confirmar_UnPedidoDeRecojoEnUnTercerDestinoConLineasDeCuscoYDeLima_PideUnTrasladoPorFuenteYEdicion()
    {
        var e = Entorno();
        var a = e.Impresa();
        var b = e.Impresa();
        var pedido = Guid.NewGuid();
        var reserva = e.Pendiente(pedido, [(a, e.Base.Cusco, 2), (b, e.Base.Lima, 1)], recojoEn: e.Base.Ayacucho);

        var resultado = await e.Confirmar().Ejecutar(Entrada(pedido));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        var lote = Assert.Single(e.Traslados.Solicitudes);
        Assert.Equal(2, lote.Count);
        Assert.Contains(new SolicitudDeTrasladoDePedido(e.Base.Cusco.Id, a, 2), lote);
        Assert.Contains(new SolicitudDeTrasladoDePedido(e.Base.Lima.Id, b, 1), lote);
        Assert.Equal([("cusco", "ayacucho", a, 2), ("lima", "ayacucho", b, 1)], resultado.Valor.Traslados.OrderBy(t => t.CodigoDeOrigen).Select(t => (t.CodigoDeOrigen, t.CodigoDeDestino, t.EdicionId, t.Cantidad)));
        Assert.All(reserva.Lineas, l => Assert.NotNull(l.TrasladoId));
        Assert.Equal(2, reserva.Lineas.Select(l => l.TrasladoId).Distinct().Count());
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-03")]
    public async Task MientrasLaReservaEstaPendiente_NoSePideNingunTraslado()
    {
        var e = new EntornoDeReservas();
        var libro = e.Impresa();
        e.Base.Existencias.Poner(e.Base.Cusco.Id, libro, 5, 0);

        var resultado = await e.Reservar().Ejecutar(EntornoDeReservas.ARecojo("ayacucho", [EntornoDeReservas.Linea(libro, 2)]));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        var linea = Assert.Single(resultado.Valor.Lineas);
        Assert.Equal(("cusco", true, EstadoDeReserva.Pendiente), (linea.CodigoDeFuente, linea.RequiereTraslado, resultado.Valor.Estado));
        Assert.Equal(0, e.Traslados.Llamadas);
        Assert.Empty(e.Traslados.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-03")]
    public async Task SiLaReservaPendienteExpira_NoSePideNingunTraslado()
    {
        var e = new EntornoDeReservas(ActorDePrueba.DelSistema());
        var libro = e.Impresa();
        e.Base.Existencias.Poner(e.Base.Cusco.Id, libro, 5, 2);
        var pedido = Guid.NewGuid();
        var reserva = e.Pendiente(pedido, [(libro, e.Base.Cusco, 2)], haceMinutos: 20, recojoEn: e.Base.Ayacucho);

        var resultado = await e.ExpirarPedido().Ejecutar(new ExpirarReservasEntrada(pedido));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal(EstadoDeLinea.Expirada, Assert.Single(reserva.Lineas).Estado);
        Assert.Null(reserva.Lineas.Single().TrasladoId);
        Assert.Equal(0, e.Traslados.Llamadas);
        Assert.Empty(e.Traslados.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-32")]
    public async Task SiLaAuditoriaFalla_NoSeConfirmaNiSePideElTraslado()
    {
        var e = Entorno();
        var pedido = Guid.NewGuid();
        e.Pendiente(pedido, [(e.Impresa(), e.Base.Cusco, 1)]);
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => e.Confirmar().Ejecutar(Entrada(pedido)));

        Assert.Equal(0, e.Traslados.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-06")]
    public async Task Confirmar_TomaElBloqueoDelPedidoAntesDeCambiarLineas_YNoElDelCliente()
    {
        var e = Entorno();
        var pedido = Guid.NewGuid();
        e.Pendiente(pedido, [(e.Impresa(), e.Base.Lima, 1)]);

        await e.Confirmar().Ejecutar(Entrada(pedido));

        var orden = e.Base.Bitacora.Llamadas;
        Assert.Equal([pedido], e.Bloqueos.Pedidos);
        Assert.Empty(e.Bloqueos.Clientes);
        Assert.True(orden.IndexOf("bloqueo.Pedido") < orden.IndexOf("reservas.ConfirmarLineas"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-06")]
    public async Task SiNoSeObtieneElBloqueoDelPedido_EsBloqueoOcupado()
    {
        var e = Entorno();
        var pedido = Guid.NewGuid();
        var reserva = e.Pendiente(pedido, [(e.Impresa(), e.Base.Lima, 1)]);
        e.Bloqueos.EsperaAgotada = true;

        var resultado = await e.Confirmar().Ejecutar(Entrada(pedido));

        Assert.Equal((CategoriaDeError.NoDisponible, "bloqueo-ocupado"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal(EstadoDeLinea.Pendiente, reserva.Lineas.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-06")]
    public async Task UnVolumenAnomaloDeConfirmaciones_AvisaUnaSolaVezPorVentana_SinRechazarNada()
    {
        var e = Entorno();
        e.UmbralDeVolumen = 3;
        var libro = e.Impresa();
        for (var i = 0; i < 6; i++)
        {
            var pedido = Guid.NewGuid();
            e.Pendiente(pedido, [(libro, e.Base.Lima, 1)]);
            Assert.True((await e.Confirmar().Ejecutar(Entrada(pedido))).EsExito);
        }

        // El motivo propio aún no consta en el catálogo cerrado: el aviso sale con el de «no catalogado», una sola vez en la ventana.
        Assert.Equal(["motivo-de-alerta-no-catalogado"], e.Alertas.Tecnicas);

        e.Alertas.Catalogados.Add("confirmaciones-de-reservas-anomalas");
        e.Base.Reloj.Ahora = e.Base.Reloj.Ahora.AddMinutes(6);
        for (var i = 0; i < 4; i++)
        {
            var pedido = Guid.NewGuid();
            e.Pendiente(pedido, [(libro, e.Base.Lima, 1)]);
            Assert.True((await e.Confirmar().Ejecutar(Entrada(pedido))).EsExito);
        }

        Assert.Equal(["motivo-de-alerta-no-catalogado", "confirmaciones-de-reservas-anomalas"], e.Alertas.Tecnicas);
    }
}
