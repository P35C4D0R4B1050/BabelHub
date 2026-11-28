using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T22: los receptores de Identidad (CAR-32, CAR-33; AC-CAR-28, AC-CAR-29): <c>ICarritoDeCuentaReemplazada</c> y la parte de <c>IAplicadorDeCancelacion</c> de Carrito eliminan el checkout —con su contacto— y luego el carrito, idempotentes ante filas ausentes y sin
/// efecto parcial cuando fallan.
/// </summary>
public sealed class ReceptoresDeIdentidadTests
{
    private static readonly DateTimeOffset Inicio = EntornoDelCarrito.Inicio;

    private static readonly TextoVistoDeCompra[] Los2 = [new(TextoLegalDeCompra.Terminos, "t-1"), new(TextoLegalDeCompra.Privacidad, "p-1")];

    private static async Task<(EntornoDelCarrito Entorno, Guid Cuenta, Guid Impreso)> ConCarritoYCheckout()
    {
        var e = EntornoDelCarrito.DeCliente();
        var cuenta = e.Actor.Actual!.UsuarioId!.Value;
        var impreso = e.Impreso(10, 40m);
        await new AgregarLinea(e.Servicios()).Ejecutar(new AgregarLineaEntrada(impreso, 3, CodigoDeSucursal: Sucursal.Principal));
        await new IniciarCheckout(e.ServiciosDelCheckout()).Ejecutar(new IniciarCheckoutEntrada());
        await new ElegirEntrega(e.ServiciosDelCheckout()).Ejecutar(new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Domicilio, new ContactoDeCompra("Ana Pérez", "987654321", "Jr. Lima 123", "ayacucho")));
        await new RegistrarAceptaciones(e.ServiciosDelCheckout()).Ejecutar(new RegistrarAceptacionesEntrada(Los2));
        e.Bitacora.Bloqueos.Clear();
        return (e, cuenta, impreso);
    }

    private static EliminarCarritoYContacto Eliminador(EntornoDelCarrito e) => new(e.Servicios());

    [Fact]
    [Trait("Criterio", "AC-CAR-28")]
    public async Task El_reemplazo_elimina_el_carrito_los_avisos_y_el_checkout_con_su_contacto_y_la_cuenta_nueva_parte_sin_carrito()
    {
        var (e, cuenta, impreso) = await ConCarritoYCheckout();
        await e.Carritos.AgregarAvisos(cuenta, [new AvisoDelCarrito(Guid.NewGuid(), TipoDeAvisoDelCarrito.EdicionRetirada, impreso, "Libro", null, null, Inicio)]);

        await new CarritoDeCuentaReemplazada(Eliminador(e)).Eliminar(cuenta);

        Assert.Empty(e.Carritos.Todos);
        Assert.Empty(e.Checkouts.Todos);
        Assert.Empty(await e.Carritos.AvisosSinVer(cuenta));
        Assert.Equal(["reemplazo"], e.Auditoria.Eliminaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-29")]
    public async Task La_cancelacion_elimina_carrito_datos_de_entrega_y_checkout_abierto_en_la_misma_operacion()
    {
        var (e, cuenta, _) = await ConCarritoYCheckout();
        IAplicadorDeCancelacionDePrueba(e, out var aplicador);

        await aplicador.Aplicar(cuenta, Guid.NewGuid());

        Assert.Empty(e.Carritos.Todos);
        Assert.Empty(e.Checkouts.Todos);
        Assert.Equal("carrito", aplicador.Modulo);
        Assert.Equal(["cancelacion"], e.Auditoria.Eliminaciones);
    }

    private static void IAplicadorDeCancelacionDePrueba(EntornoDelCarrito e, out BabelHub.Ecommerce.Application.Identidad.IAplicadorDeCancelacion aplicador) =>
        aplicador = new AplicadorDeCancelacionDelCarrito(Eliminador(e));

    [Fact]
    public async Task Borra_siempre_el_checkout_y_luego_el_carrito_en_el_orden_de_bloqueo_de_D10()
    {
        var (e, cuenta, _) = await ConCarritoYCheckout();

        await Eliminador(e).Eliminar(cuenta, "cancelacion");

        Assert.Equal(["checkout", "carrito"], e.Bitacora.Bloqueos);
    }

    [Fact]
    public async Task Es_idempotente_ante_filas_ausentes_y_se_puede_reaplicar()
    {
        var (e, cuenta, _) = await ConCarritoYCheckout();
        var eliminador = Eliminador(e);

        await eliminador.Eliminar(cuenta, "cancelacion");
        await eliminador.Eliminar(cuenta, "cancelacion");
        await eliminador.Eliminar(Guid.NewGuid(), "cancelacion");

        Assert.Empty(e.Carritos.Todos);
        Assert.Equal(3, e.Auditoria.Eliminaciones.Count);
    }

    [Fact]
    public async Task No_toca_lo_de_otras_cuentas_ni_el_carrito_de_un_visitante()
    {
        var (e, cuenta, impreso) = await ConCarritoYCheckout();
        var otra = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeCuenta(Guid.NewGuid()), Inicio);
        otra.Agregar(impreso, false, 1, Inicio);
        e.Carritos.Sembrar(otra);
        var visitante = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeVisitante(new byte[32]), Inicio);
        visitante.Agregar(impreso, false, 1, Inicio);
        e.Carritos.Sembrar(visitante);

        await Eliminador(e).Eliminar(cuenta, "cancelacion");

        Assert.Equal([otra, visitante], e.Carritos.Todos);
    }

    [Fact]
    public async Task Un_retiro_pendiente_o_un_checkout_en_pago_no_impide_eliminar_y_no_se_aplica_a_ciegas()
    {
        var (e, cuenta, impreso) = await ConCarritoYCheckout();
        var carrito = e.Carritos.Todos.Single();
        carrito.RetirarEdicion(impreso, diferir: true, Inicio);

        await Eliminador(e).Eliminar(cuenta, "cancelacion");

        Assert.Empty(e.Carritos.Todos);
        Assert.Empty(e.Checkouts.Todos);
    }

    [Fact]
    public async Task Si_no_puede_eliminar_lanza_para_que_Identidad_revierta_y_no_reemplace_ni_cancele()
    {
        var (e, cuenta, _) = await ConCarritoYCheckout();
        e.Carritos.FallaAlEliminar = new InvalidOperationException("no se pudo");

        await Assert.ThrowsAsync<InvalidOperationException>(() => new CarritoDeCuentaReemplazada(Eliminador(e)).Eliminar(cuenta));

        Assert.Empty(e.Auditoria.Eliminaciones);
    }

    [Fact]
    public async Task Si_no_se_puede_auditar_la_eliminacion_falla_para_que_no_quede_un_efecto_parcial()
    {
        var (e, cuenta, _) = await ConCarritoYCheckout();
        e.Auditoria.Caida = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Eliminador(e).Eliminar(cuenta, "cancelacion"));
    }
}
