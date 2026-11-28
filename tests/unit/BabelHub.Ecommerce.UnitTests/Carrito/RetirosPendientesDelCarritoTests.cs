using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>T04 (D21): el retiro de una edición del catálogo sobre el carrito: se quita de inmediato o solo se anota según el estado del checkout, y una anotación huérfana la quita la primera lectura con bloqueo.</summary>
public sealed class RetirosPendientesDelCarritoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Libro = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Digital = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static CarritoDeDominio ConDosLineas(PropietarioDelCarrito? propietario = null)
    {
        var carrito = CarritoDeDominio.Crear(Guid.NewGuid(), propietario ?? PropietarioDelCarrito.DeCuenta(Guid.NewGuid()), Ahora);
        carrito.Agregar(Libro, esDigital: false, cantidad: 2, Ahora);
        carrito.Agregar(Digital, esDigital: true, cantidad: 1, Ahora);
        return carrito;
    }

    [Fact]
    public void Con_un_checkout_vivo_que_no_admite_editar_el_retiro_solo_anota_la_linea_y_repetirlo_no_cambia_nada()
    {
        var carrito = ConDosLineas();

        var primera = carrito.RetirarEdicion(Libro, diferir: true, Ahora.AddMinutes(1));
        var version = carrito.Version;
        var segunda = carrito.RetirarEdicion(Libro, diferir: true, Ahora.AddMinutes(2));

        Assert.Equal((ResultadoDeRetiro.Anotada, ResultadoDeRetiro.Anotada), (primera, segunda));
        Assert.Equal(version, carrito.Version);
        Assert.True(carrito.Lineas.Single(l => l.EdicionId == Libro).RetiroPendiente);
        Assert.False(carrito.Lineas.Single(l => l.EdicionId == Digital).RetiroPendiente);
        Assert.Equal(2, carrito.Lineas.Single(l => l.EdicionId == Libro).Cantidad.Valor);
    }

    [Fact]
    public void El_retiro_no_es_una_accion_de_la_persona_y_no_mueve_la_caducidad_del_visitante()
    {
        var carrito = ConDosLineas(PropietarioDelCarrito.DeVisitante(new byte[32]));
        var vence = carrito.VenceEn;

        carrito.RetirarEdicion(Libro, diferir: false, Ahora.AddDays(10));
        carrito.RetirarEdicion(Digital, diferir: true, Ahora.AddDays(11));
        carrito.AplicarRetirosPendientes(Ahora.AddDays(12));

        Assert.Equal(vence, carrito.VenceEn);
    }

    [Fact]
    public void Aplicar_los_retiros_pendientes_quita_solo_las_marcadas_y_dice_cuales_eran()
    {
        var carrito = ConDosLineas();
        carrito.RetirarEdicion(Digital, diferir: true, Ahora);

        var retiradas = carrito.AplicarRetirosPendientes(Ahora.AddMinutes(1));

        Assert.Equal([new EdicionRetirada(Digital, true)], retiradas);
        Assert.Equal(Libro, Assert.Single(carrito.Lineas).EdicionId);
        Assert.Empty(carrito.AplicarRetirosPendientes(Ahora.AddMinutes(2)));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(EstadoDelCheckout.EnEdicion, true)]
    [InlineData(EstadoDelCheckout.Cancelado, true)]
    [InlineData(EstadoDelCheckout.Reservando, false)]
    [InlineData(EstadoDelCheckout.PagoPendiente, false)]
    public void Una_anotacion_huerfana_la_quita_la_primera_lectura_solo_si_el_checkout_no_la_sostiene(EstadoDelCheckout? estado, bool seQuita)
    {
        var carrito = ConDosLineas();
        carrito.RetirarEdicion(Libro, diferir: true, Ahora);

        var retiradas = carrito.QuitarRetirosHuerfanos(estado, Ahora.AddMinutes(1));

        Assert.Equal(seQuita, retiradas.Count == 1);
        Assert.Equal(!seQuita, carrito.TieneRetirosPendientes);
    }

    [Fact]
    public void Una_linea_con_retiro_pendiente_no_se_puede_agregar_ni_modificar_pero_si_quitar()
    {
        var carrito = ConDosLineas();
        carrito.RetirarEdicion(Libro, diferir: true, Ahora);
        var version = carrito.Version;

        Assert.Equal(RechazoDeCarrito.EdicionRetirada, carrito.Modificar(Libro, cantidad: 5, Ahora.AddMinutes(1)).Rechazo);
        Assert.Equal(RechazoDeCarrito.EdicionRetirada, carrito.Agregar(Libro, esDigital: false, cantidad: 1, Ahora.AddMinutes(1)).Rechazo);
        Assert.Equal(version, carrito.Version);
        Assert.Equal(2, carrito.Lineas.Single(l => l.EdicionId == Libro).Cantidad.Valor);

        Assert.True(carrito.Eliminar(Libro, Ahora.AddMinutes(2)).EsExito);
        Assert.False(carrito.TieneRetirosPendientes);
    }
}
