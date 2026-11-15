using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Domain.Fuentes;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.UnitTests.Fuentes;
using BabelHub.Inventario.UnitTests.Soporte;
using static BabelHub.Inventario.UnitTests.Reservas.EntornoDeReservas;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>T18: la consulta previa de la selección (solo lectura, sin límites de reservas) y la consulta de la reserva (dueño o administración) (SEL-20).</summary>
public sealed class ConsultasDeReservasTests
{
    private static EntornoDeReservas ComoIntegracion() => new(ActorDePrueba.DeIntegracion("lectura-de-disponibilidad"));

    [Fact]
    [Trait("Criterio", "AC-SEL-20")]
    public async Task ConsultarSeleccion_DiceLaFuenteQueSeSeleccionaria_SinReservarNadaNiMoverElStock()
    {
        var e = ComoIntegracion();
        var libro = e.Impresa();
        e.Base.Existencias.Poner(e.Base.Lima.Id, libro, 0, 0);
        e.Base.Existencias.Poner(e.Base.Cusco.Id, libro, 5, 0);
        e.Base.Rutas.Rutas.Add(RutaEntreFuentes.Definir(e.Base.Cusco.Id, e.Base.Lima.Id, 3));
        var linea = Linea(libro, 2);

        var resultado = await e.ConsultarSeleccion().Ejecutar(new ConsultarSeleccionEntrada(TipoDeEntregaDeReserva.Recojo, "lima", null, [linea]));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        var seleccion = Assert.Single(resultado.Valor.Lineas);
        Assert.Equal((linea.LineaId, libro, true, "cusco", true, 3), (seleccion.LineaId, seleccion.EdicionId, seleccion.HayStock, seleccion.CodigoDeFuente, seleccion.RequiereTraslado, seleccion.PlazoEstimadoEnDias));
        Assert.Equal(EntornoDeFuentes.Ahora, resultado.Valor.ConsultadaEn);
        Assert.Empty(e.Base.Existencias.Movimientos);
        Assert.Empty(e.Reservas.Guardadas);
        Assert.Equal((5, 0), e.FilaDe(e.Base.Cusco, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-20")]
    public async Task ConsultarSeleccion_SinStockEnLaRed_DiceQueNoHay_SinRevelarCuantoHayEnLaRed()
    {
        var e = ComoIntegracion();
        var libro = e.Impresa();
        e.Base.Existencias.Poner(e.Base.Lima.Id, libro, 1, 0);
        e.Base.Existencias.Poner(e.Base.Cusco.Id, libro, 1, 0);

        var resultado = await e.ConsultarSeleccion().Ejecutar(new ConsultarSeleccionEntrada(TipoDeEntregaDeReserva.Recojo, "lima", null, [Linea(libro, 5)]));

        var seleccion = Assert.Single(resultado.Valor.Lineas);
        Assert.False(seleccion.HayStock);
        Assert.Null(seleccion.CodigoDeFuente);
        Assert.Equal(0, seleccion.DisponibleEnLaRed);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-20")]
    public async Task ConsultarSeleccion_IgnoraLasLineasDigitales_YUnaEdicionDesconocidaEsNoEncontrada()
    {
        var e = ComoIntegracion();
        var libro = e.Impresa();
        var digital = e.Digital();
        e.Base.Existencias.Poner(e.Base.Lima.Id, libro, 3, 0);

        var resultado = await e.ConsultarSeleccion().Ejecutar(new ConsultarSeleccionEntrada(TipoDeEntregaDeReserva.Recojo, "lima", null, [Linea(libro), Linea(digital)]));
        var desconocida = await e.ConsultarSeleccion().Ejecutar(new ConsultarSeleccionEntrada(TipoDeEntregaDeReserva.Recojo, "lima", null, [Linea(Guid.NewGuid())]));

        Assert.Equal(libro, Assert.Single(resultado.Valor.Lineas).EdicionId);
        Assert.Equal(CategoriaDeError.NoEncontrado, desconocida.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-20")]
    public async Task ConsultarSeleccion_NoCuentaParaLosLimitesDeReservasNiTomaBloqueos()
    {
        var e = ComoIntegracion();
        var libro = e.Impresa();
        e.Base.Existencias.Poner(e.Base.Lima.Id, libro, 100, 0);

        for (var i = 0; i < 20; i++)
        {
            Assert.True((await e.ConsultarSeleccion().Ejecutar(new ConsultarSeleccionEntrada(TipoDeEntregaDeReserva.Recojo, "lima", null, [Linea(libro)]))).EsExito);
        }

        Assert.Empty(e.Eventos.Eventos);
        Assert.Empty(e.Bloqueos.Clientes);
        Assert.Empty(e.Bloqueos.Pedidos);
        Assert.DoesNotContain("existencias.BloquearFilas", e.Base.Bitacora.Llamadas);
        Assert.DoesNotContain("reservas.ExpirarLineasVencidas", e.Base.Bitacora.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-20")]
    public async Task ConsultarSeleccion_ContaLoVencidoSinBarrerComoVendible_SinEscribir()
    {
        var e = ComoIntegracion();
        var libro = e.Impresa();
        e.Base.Existencias.Poner(e.Base.Lima.Id, libro, 2, 2);
        e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Lima, 2)], haceMinutos: 30);

        var resultado = await e.ConsultarSeleccion().Ejecutar(new ConsultarSeleccionEntrada(TipoDeEntregaDeReserva.Recojo, "lima", null, [Linea(libro, 2)]));

        Assert.True(Assert.Single(resultado.Valor.Lineas).HayStock);
        Assert.Equal((2, 2), e.FilaDe(e.Base.Lima, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-20")]
    public async Task ConsultarSeleccion_SinActorEsNoAutenticado_YConEntradaInvalidaEsValidacion()
    {
        var sinActor = new EntornoDeReservas(ActorDePrueba.SinContexto());
        var e = ComoIntegracion();

        var rechazo = await sinActor.ConsultarSeleccion().Ejecutar(new ConsultarSeleccionEntrada(TipoDeEntregaDeReserva.Recojo, "lima", null, [Linea(Guid.NewGuid())]));
        var invalida = await e.ConsultarSeleccion().Ejecutar(new ConsultarSeleccionEntrada(TipoDeEntregaDeReserva.Recojo, null, null, []));

        Assert.Equal(CategoriaDeError.NoAutenticado, rechazo.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, invalida.Error!.Categoria);
    }

    // --- consultar la reserva ---

    [Fact]
    [Trait("Criterio", "AC-SEL-20")]
    public async Task ElClienteDuenoVeSuReserva()
    {
        var e = new EntornoDeReservas();
        var libro = e.Impresa();
        var pedido = Guid.NewGuid();
        e.Pendiente(pedido, [(libro, e.Base.Lima, 2)], clienteId: e.ClienteId);

        var resultado = await e.ConsultarReserva().Ejecutar(new ConsultarReservaEntrada(pedido));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((pedido, EstadoDeReserva.Pendiente), (resultado.Valor.PedidoId, resultado.Valor.Estado));
        Assert.Equal("lima", Assert.Single(resultado.Valor.Lineas).CodigoDeFuente);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-20")]
    public async Task LaAdministracionVeCualquierReserva()
    {
        var e = new EntornoDeReservas(ActorDePrueba.DeAdministradorCentral());
        var pedido = Guid.NewGuid();
        e.Pendiente(pedido, [(e.Impresa(), e.Base.Lima, 1)]);

        Assert.True((await e.ConsultarReserva().Ejecutar(new ConsultarReservaEntrada(pedido))).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-20")]
    public async Task ElPedidoDeOtroCliente_EsNoEncontradoConElEvento_ComoUnoInexistente()
    {
        var e = new EntornoDeReservas();
        var pedido = Guid.NewGuid();
        e.Pendiente(pedido, [(e.Impresa(), e.Base.Lima, 1)]);

        var ajeno = await e.ConsultarReserva().Ejecutar(new ConsultarReservaEntrada(pedido));
        var inexistente = await e.ConsultarReserva().Ejecutar(new ConsultarReservaEntrada(Guid.NewGuid()));

        Assert.Equal((CategoriaDeError.NoEncontrado, "reserva-no-encontrada"), (ajeno.Error!.Categoria, ajeno.Error.Clave));
        Assert.Equal((CategoriaDeError.NoEncontrado, "reserva-no-encontrada"), (inexistente.Error!.Categoria, inexistente.Error.Clave));
        Assert.Equal(["recurso-ajeno"], e.Base.MotivosDeDenegacion);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-20")]
    [InlineData("encargado")]
    [InlineData("logistica")]
    [InlineData("integracion")]
    public async Task ElPersonalDeSucursalOLogistica_RecibeProhibidoSinLeerNada(string quien)
    {
        var e = new EntornoDeReservas(quien switch
        {
            "encargado" => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
            "logistica" => ActorDePrueba.DePersonalDeLogistica(),
            _ => ActorDePrueba.DeIntegracion("lectura-de-disponibilidad"),
        });
        var pedido = Guid.NewGuid();
        e.Pendiente(pedido, [(e.Impresa(), e.Base.Lima, 1)]);

        var resultado = await e.ConsultarReserva().Ejecutar(new ConsultarReservaEntrada(pedido));

        Assert.Equal((CategoriaDeError.Prohibido, "prohibido"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.DoesNotContain("reservas.UltimaDelPedido", e.Base.Bitacora.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public async Task UnaPendienteVencidaQueNadieBarrio_SeMuestraComoExpirada()
    {
        var e = new EntornoDeReservas();
        var pedido = Guid.NewGuid();
        e.Pendiente(pedido, [(e.Impresa(), e.Base.Lima, 1)], haceMinutos: 30, clienteId: e.ClienteId);

        var resultado = await e.ConsultarReserva().Ejecutar(new ConsultarReservaEntrada(pedido));

        Assert.Equal(EstadoDeReserva.Expirada, resultado.Valor.Estado);
        Assert.Equal(EstadoDeReserva.Expirada, Assert.Single(resultado.Valor.Lineas).Estado);
    }
}
