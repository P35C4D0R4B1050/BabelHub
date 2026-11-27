using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;
using static BabelHub.Ecommerce.UnitTests.Pagos.InstantaneasDeCompraDePrueba;
using AppMedioDePago = BabelHub.Ecommerce.Application.Pagos.MedioDePago;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>
/// El cobro cerrado (la pasarela <c>ninguna</c>, DD-110): la tienda se revisa sin ninguna pasarela, así que la Aplicación se niega a emitir la clave de un intento y a cobrar, sin registrar nada, sin consultar ni extender la reserva y sin llamar a la
/// pasarela; el estado del pago lo dice y no deja reintentar.
/// </summary>
public sealed class CobroCerradoTests
{
    private static readonly OpcionesDelCobro Cerrado = new(TimeSpan.FromMilliseconds(300), Cerrado: true);

    private static async Task<(EntornoDePagos Entorno, string Token)> ConUnaClaveYaEmitida()
    {
        var e = await EntornoDePagosExtensiones.ConIntencionLista();
        // La clave se emitió cuando el cobro estaba abierto (o la pestaña es anterior al cambio de pasarela): cerrada la tienda, no sirve.
        var clave = await e.EmitirClave.Ejecutar(new EmitirClaveDeIntentoEntrada(Pedido));
        Assert.True(clave.EsExito);
        return (e, clave.Valor.Token);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-43")]
    [InlineData(AppMedioDePago.Tarjeta)]
    [InlineData(AppMedioDePago.Yape)]
    public async Task Con_el_cobro_cerrado_cobrar_se_rechaza_sin_registrar_el_intento_sin_tocar_la_reserva_y_sin_llamar_a_la_pasarela(AppMedioDePago medio)
    {
        var (e, token) = await ConUnaClaveYaEmitida();
        var cobrar = e.CrearIniciarCobro(Cerrado);
        var guardados = e.Repositorio.GuardadosDeIntenciones;
        var operaciones = e.Repositorio.Operaciones;
        var consultas = e.Reservas.Consultas;

        var resultado = await cobrar.Ejecutar(new CobrarEntrada(Pedido, token, medio, TokenDelMedioDePago.Crear("tok_aprobado")));

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal("pago-no-disponible", resultado.Error.Clave);
        Assert.Empty(e.Repositorio.Intentos);
        Assert.Empty(e.Pasarela.Solicitudes);
        Assert.Equal(guardados, e.Repositorio.GuardadosDeIntenciones);
        Assert.Equal(operaciones, e.Repositorio.Operaciones);
        Assert.Equal(consultas, e.Reservas.Consultas);
        Assert.Empty(e.Reservas.Extensiones);
        Assert.Empty(e.Confirmador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-43")]
    public async Task Con_el_cobro_cerrado_no_se_emite_la_clave_de_un_intento_ni_se_lee_nada()
    {
        var e = await EntornoDePagosExtensiones.ConIntencionLista();
        var emitir = new EmitirClaveDeIntento(e.Autorizador, e.Repositorio, e.Unidad, e.Checkout, e.Firmador, e.Reloj, Cerrado);
        var operaciones = e.Repositorio.Operaciones;

        var resultado = await emitir.Ejecutar(new EmitirClaveDeIntentoEntrada(Pedido));

        Assert.False(resultado.EsExito);
        Assert.Equal("pago-no-disponible", resultado.Error!.Clave);
        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error.Categoria);
        Assert.Equal(operaciones, e.Repositorio.Operaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-43")]
    public async Task Con_el_cobro_cerrado_sin_sesion_la_respuesta_sigue_siendo_no_autenticado()
    {
        var e = await EntornoDePagosExtensiones.ConIntencionLista();
        e.Actor.Establecer(ActorActual.Anonimo());
        var cobrar = e.CrearIniciarCobro(Cerrado);
        var emitir = new EmitirClaveDeIntento(e.Autorizador, e.Repositorio, e.Unidad, e.Checkout, e.Firmador, e.Reloj, Cerrado);

        var cobro = await cobrar.Ejecutar(new CobrarEntrada(Pedido, "clave", AppMedioDePago.Tarjeta, TokenDelMedioDePago.Crear("tok_aprobado")));
        var clave = await emitir.Ejecutar(new EmitirClaveDeIntentoEntrada(Pedido));

        Assert.Equal(CategoriaDeError.NoAutenticado, cobro.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoAutenticado, clave.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-42")]
    public async Task Con_el_cobro_cerrado_el_estado_dice_que_no_hay_cobro_y_no_deja_reintentar()
    {
        var e = await EntornoDePagosExtensiones.ConIntencionLista();

        var cerrado = await new ObtenerEstadoDelPago(e.Autorizador, e.Repositorio, e.Unidad, e.Reloj, Cerrado).Ejecutar(Pedido);
        var abierto = await new ObtenerEstadoDelPago(e.Autorizador, e.Repositorio, e.Unidad, e.Reloj, OpcionesDelCobro.PorDefecto).Ejecutar(Pedido);

        Assert.Equal(EstadoDelPagoDeLaCompra.ListoParaPagar, cerrado.Valor.Estado);
        Assert.False(cerrado.Valor.CobroDisponible);
        Assert.False(cerrado.Valor.PuedeReintentar);
        Assert.True(abierto.Valor.CobroDisponible);
        Assert.True(abierto.Valor.PuedeReintentar);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-42")]
    public async Task Con_el_cobro_cerrado_un_pedido_ajeno_sigue_siendo_no_encontrado()
    {
        var e = await EntornoDePagosExtensiones.ConIntencionLista();

        var resultado = await new ObtenerEstadoDelPago(e.Autorizador, e.Repositorio, e.Unidad, e.Reloj, Cerrado).Ejecutar(Guid.NewGuid());

        Assert.Equal(CategoriaDeError.NoEncontrado, resultado.Error!.Categoria);
        Assert.NotEmpty(e.Denegaciones.Eventos);
    }
}
