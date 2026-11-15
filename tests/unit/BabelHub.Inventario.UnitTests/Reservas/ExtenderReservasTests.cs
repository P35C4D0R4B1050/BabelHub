using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.UnitTests.Fuentes;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>T15: <c>ExtenderReservas</c> (SEL-22, SEL-30): una sola extensión por pedido a 30 minutos desde el inicio del pago; el aviso que ya no sirve es un éxito sin cambios.</summary>
public sealed class ExtenderReservasTests
{
    private static EntornoDeReservas Entorno() => new(ActorDePrueba.DeIntegracion("expiracion-de-reservas"));

    [Fact]
    [Trait("Criterio", "AC-SEL-24")]
    public async Task Extender_LlevaLaExpiracionATreintaMinutosDesdeElInicioDelPago()
    {
        var e = Entorno();
        var pedido = Guid.NewGuid();
        e.Pendiente(pedido, [(e.Impresa(), e.Base.Lima, 1)], haceMinutos: 5);

        var resultado = await e.Extender().Ejecutar(new ExtenderReservasEntrada(pedido));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.True(resultado.Valor.Extendida);
        Assert.Equal(e.Base.Reloj.Ahora.AddMinutes(-5).AddMinutes(30), resultado.Valor.Reserva.ExpiraEn);
        Assert.True(e.Reservas.Guardadas.Single().Extendida);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-24")]
    public async Task UnSegundoAviso_EsExitoSinCambios_LaExtensionEsUnaPorPedido()
    {
        var e = Entorno();
        var pedido = Guid.NewGuid();
        e.Pendiente(pedido, [(e.Impresa(), e.Base.Lima, 1)], haceMinutos: 5);
        var primero = await e.Extender().Ejecutar(new ExtenderReservasEntrada(pedido));

        var segundo = await e.Extender().Ejecutar(new ExtenderReservasEntrada(pedido));

        Assert.True(segundo.EsExito);
        Assert.False(segundo.Valor.Extendida);
        Assert.Equal(primero.Valor.Reserva.ExpiraEn, segundo.Valor.Reserva.ExpiraEn);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-30")]
    [InlineData("vencida")]
    [InlineData("confirmada")]
    [InlineData("liberada")]
    public async Task UnAvisoQueYaNoSirve_EsExitoSinCambios(string estado)
    {
        var e = Entorno();
        var pedido = Guid.NewGuid();
        var reserva = e.Pendiente(pedido, [(e.Impresa(), e.Base.Lima, 1)], haceMinutos: estado == "vencida" ? 20 : 5);
        switch (estado)
        {
            case "confirmada":
                Assert.True(reserva.Confirmar(e.Base.Reloj.Ahora));
                break;
            case "liberada":
                reserva.Lineas.Single().Liberar(e.Base.Reloj.Ahora);
                break;
        }

        var resultado = await e.Extender().Ejecutar(new ExtenderReservasEntrada(pedido));

        Assert.True(resultado.EsExito);
        Assert.False(resultado.Valor.Extendida);
        Assert.False(e.Reservas.Guardadas.Single().Extendida);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-30")]
    public async Task Extender_UnPedidoSinReserva_EsNoEncontrado()
    {
        var resultado = await Entorno().Extender().Ejecutar(new ExtenderReservasEntrada(Guid.NewGuid()));

        Assert.Equal((CategoriaDeError.NoEncontrado, "reserva-no-encontrada"), (resultado.Error!.Categoria, resultado.Error.Clave));
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-35")]
    [InlineData("cliente")]
    [InlineData("otra-integracion")]
    [InlineData("sin-actor")]
    public async Task Extender_SoloLaIntegracionDeExpiracionDeReservas(string quien)
    {
        var actor = quien switch
        {
            "cliente" => ActorDePrueba.DeCliente(),
            "otra-integracion" => ActorDePrueba.DeIntegracion("consulta-de-pendientes"),
            _ => ActorDePrueba.SinContexto(),
        };
        var e = new EntornoDeReservas(actor);
        var pedido = Guid.NewGuid();
        e.Pendiente(pedido, [(e.Impresa(), e.Base.Lima, 1)], haceMinutos: 5);

        var resultado = await e.Extender().Ejecutar(new ExtenderReservasEntrada(pedido));

        Assert.Equal(quien == "sin-actor" ? CategoriaDeError.NoAutenticado : CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.False(e.Reservas.Guardadas.Single().Extendida);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-24")]
    public async Task Extender_TomaElBloqueoDelPedido()
    {
        var e = Entorno();
        var pedido = Guid.NewGuid();
        e.Pendiente(pedido, [(e.Impresa(), e.Base.Lima, 1)], haceMinutos: 5);

        await e.Extender().Ejecutar(new ExtenderReservasEntrada(pedido));

        Assert.Equal([pedido], e.Bloqueos.Pedidos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-24")]
    public async Task Extender_ConUnPedidoVacio_EsValidacion()
    {
        var resultado = await Entorno().Extender().Ejecutar(new ExtenderReservasEntrada(Guid.Empty));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal(EntornoDeFuentes.Ahora, new EntornoDeReservas().Base.Reloj.Ahora);
    }
}
