using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Carrito;

/// <summary>
/// T31: privacidad y alcance de Carrito sobre PostgreSQL real (AC-CAR-26, AC-CAR-28, AC-CAR-29, AC-CAR-30, AC-CAR-31): un cliente frente a otro con un <c>pedidoId</c> ajeno, la cookie ajena o mal formada, el anónimo y el personal; los receptores de Identidad (eliminan todo, sin efecto
/// parcial si fallan); el contacto eliminado en cada disparador y exactamente a los 30 días; y que el nombre, el teléfono, la dirección y la cookie no aparezcan en la auditoría, en lo que se imprime ni en los mensajes de error.
/// </summary>
[Collection(ColeccionPostgresDelCarrito.Nombre)]
public sealed class PrivacidadYAlcanceDelCarritoTests(PostgresDelCarritoFixture bd) : IAsyncLifetime
{
    private const string Nombre = "Ana Pérez Quispe";
    private const string Telefono = "987654321";
    private const string Direccion = "Jr. Lima 123";

    private EntornoDelCarritoConPostgres _e = null!;

    public async Task InitializeAsync() => _e = await EntornoDelCarritoConPostgres.Crear(bd);

    public async Task DisposeAsync() => await _e.DisposeAsync();

    private Guid Impreso()
    {
        var edicion = _e.Catalogo.Poner(FormatoDeEdicion.Impresa, 40m);
        _e.Inventario.FijarStock(edicion, 10);
        _e.Inventario.FijarFuente(edicion, "sede-central", false, null);
        _e.Disponibilidad.Vendible(edicion, 10);
        return edicion;
    }

    /// <summary>Una cuenta con su checkout a domicilio en edición, con contacto.</summary>
    private async Task<Guid> ConContacto()
    {
        var cuenta = EntornoDelCarritoConPostgres.NuevaCuenta();
        await _e.Agregar(cuenta, Impreso(), 3);
        await _e.PrepararCheckout(cuenta, domicilio: true);
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM carrito.checkouts WHERE cuenta_id = '{cuenta}' AND contacto_nombre IS NOT NULL"));
        return cuenta;
    }

    private Task<long> ContactosGuardados(Guid cuenta) => bd.Consultar<long>($"SELECT count(*) FROM carrito.checkouts WHERE cuenta_id = '{cuenta}' AND (contacto_nombre IS NOT NULL OR contacto_telefono IS NOT NULL OR direccion IS NOT NULL)");

    private Task<string?> Estado(Guid cuenta) => bd.Consultar<string>($"SELECT estado FROM carrito.checkouts WHERE cuenta_id = '{cuenta}' ORDER BY creado_en DESC LIMIT 1");

    // ---------- Alcance ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-31")]
    public async Task Un_cliente_con_el_pedidoId_de_otro_recibe_no_encontrado_uniforme_se_registra_el_acceso_y_el_checkout_ajeno_queda_intacto()
    {
        var (a, b) = (await ConContacto(), EntornoDelCarritoConPostgres.NuevaCuenta());
        await _e.Agregar(b, Impreso(), 1);
        await _e.PrepararCheckout(b);
        var pago = await _e.Pagar(a, 132m);
        var pedidoDeA = pago.Valor.PedidoId!.Value;
        var antes = _e.Denegaciones.Registradas;

        var ajenos = new[]
        {
            await _e.Como(b, sp => sp.GetRequiredService<ICheckoutDelCliente>().VolverAlCarrito(new SalirDelCheckoutEntrada(pedidoDeA))),
        };
        var reinicio = await _e.Como(b, sp => sp.GetRequiredService<ICheckoutDelCliente>().Reiniciar(new SalirDelCheckoutEntrada(pedidoDeA)));
        var entrega = await _e.Como(b, sp => sp.GetRequiredService<ICheckoutDelCliente>().ElegirEntrega(new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Recojo, "sede-central", null, pedidoDeA)));
        var aceptaciones = await _e.Como(b, sp => sp.GetRequiredService<ICheckoutDelCliente>().RegistrarAceptaciones(new RegistrarAceptacionesEntrada(EntornoDelCarritoConPostgres.LosDosTextos, PedidoId: pedidoDeA)));
        var otroPago = await _e.Pagar(b, 40m, pedidoId: pedidoDeA);

        Assert.All(new[] { ajenos[0].Error, reinicio.Error, entrega.Error, aceptaciones.Error, otroPago.Error }, e =>
        {
            Assert.NotNull(e);
            Assert.Equal(CategoriaDeError.NoEncontrado, e.Categoria);
            Assert.Equal("no-encontrado", e.Clave);
        });
        Assert.True(_e.Denegaciones.Registradas - antes >= 5);
        Assert.Equal("en_pago", await bd.Consultar<string>($"SELECT estado FROM carrito.checkouts WHERE pedido_id = '{pedidoDeA}'"));
        Assert.Equal("en_edicion", await Estado(b));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-31")]
    public async Task La_cookie_ajena_o_mal_formada_es_sin_carrito_y_un_anonimo_o_el_personal_no_tocan_el_checkout()
    {
        var (a, impreso) = (EntornoDelCarritoConPostgres.NuevaCuenta(), Impreso());
        var cookieDeA = (await _e.Agregar(null, impreso, 2)).Valor.CookieNueva!;

        var ajena = await _e.Como(null, sp => sp.GetRequiredService<ICarritoDelCliente>().Obtener(new SolicitudSobreElCarrito(new string('B', 43))));
        var malFormada = await _e.Como(null, sp => sp.GetRequiredService<ICarritoDelCliente>().Obtener(new SolicitudSobreElCarrito("no es una cookie")));
        var propia = await _e.Como(null, sp => sp.GetRequiredService<ICarritoDelCliente>().Obtener(new SolicitudSobreElCarrito(cookieDeA)));
        var anonimo = await _e.Como(null, sp => sp.GetRequiredService<ICheckoutDelCliente>().Iniciar(new IniciarCheckoutEntrada()));

        Assert.Empty(ajena.Valor.Carrito.Lineas);
        Assert.Empty(malFormada.Valor.Carrito.Lineas);
        Assert.Equal(2, propia.Valor.Carrito.Lineas[0].Cantidad);
        Assert.False(anonimo.EsExito);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM carrito.checkouts"));
        Assert.NotEqual(a, Guid.Empty);
    }

    // ---------- Receptores de Identidad ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-28")]
    [Trait("Criterio", "AC-CAR-29")]
    public async Task Los_receptores_de_Identidad_eliminan_carrito_avisos_checkout_contacto_y_constancias_y_se_pueden_reaplicar()
    {
        var cuenta = await ConContacto();
        var cookie = (await _e.Agregar(null, Impreso(), 1)).Valor.CookieNueva!;
        await _e.Como(cuenta, sp => sp.GetRequiredService<ICarritoDelCliente>().FusionarCarritoDeVisitante(cookie));

        await _e.Como(cuenta, async sp => { await sp.GetRequiredService<IAplicadorDeCancelacion>().Aplicar(cuenta, Guid.NewGuid()); return 0; });
        await _e.Como(cuenta, async sp => { await sp.GetRequiredService<IAplicadorDeCancelacion>().Aplicar(cuenta, Guid.NewGuid()); return 0; });

        foreach (var tabla in new[] { "checkouts", "carritos", "avisos_de_carrito" })
        {
            Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM carrito.{tabla} WHERE cuenta_id = '{cuenta}'"));
        }

        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM carrito.fusiones_aplicadas"));
        Assert.Contains("eliminado:cancelacion", _e.Auditoria.Eventos);
    }

    [Fact]
    public async Task Si_Identidad_revierte_su_transaccion_despues_del_receptor_no_se_borra_nada_y_con_la_auditoria_caida_el_receptor_lanza_sin_efecto_parcial()
    {
        var cuenta = await ConContacto();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _e.Como(cuenta, sp => sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar<int>(
            async token =>
            {
                await sp.GetRequiredService<ICarritoDeCuentaReemplazada>().Eliminar(cuenta, token);
                throw new InvalidOperationException("Otro módulo de la transacción de Identidad falló.");
            })));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM carrito.checkouts WHERE cuenta_id = '{cuenta}'"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM carrito.carritos WHERE cuenta_id = '{cuenta}'"));

        _e.Auditoria.Caida = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _e.Como(cuenta, sp => sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token =>
        {
            await sp.GetRequiredService<IAplicadorDeCancelacion>().Aplicar(cuenta, Guid.NewGuid(), token);
            return 0;
        })));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM carrito.checkouts WHERE cuenta_id = '{cuenta}'"));
    }

    // ---------- El contacto se elimina en cada disparador ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-30")]
    public async Task El_contacto_se_elimina_al_volver_al_reiniciar_al_pagar_y_al_compensar()
    {
        var (volver, reiniciar, pagado, compensado) = (await ConContacto(), await ConContacto(), await ConContacto(), await ConContacto());
        await _e.Como(volver, sp => sp.GetRequiredService<ICheckoutDelCliente>().VolverAlCarrito(new SalirDelCheckoutEntrada()));
        await _e.Como(reiniciar, sp => sp.GetRequiredService<ICheckoutDelCliente>().Reiniciar(new SalirDelCheckoutEntrada()));
        var pedidoPagado = (await _e.Pagar(pagado, 132m)).Valor.PedidoId!.Value;
        await _e.Como(pagado, sp => sp.GetRequiredService<IProgresoDelCheckout>().PagoAprobado(pedidoPagado));
        var pedidoCompensado = (await _e.Pagar(compensado, 132m)).Valor.PedidoId!.Value;
        await _e.Como(compensado, sp => sp.GetRequiredService<IProgresoDelCheckout>().PagoResuelto(pedidoCompensado, MotivoDePagoResueltoDeCompra.Compensado));

        Assert.All(new[] { volver, reiniciar, pagado, compensado }, c => Assert.Equal(0L, ContactosGuardados(c).Result));
        Assert.Equal("cancelado", await Estado(volver));
        Assert.Equal("reiniciado", await Estado(reiniciar));
        Assert.Equal("pagado", await Estado(pagado));
        Assert.Equal("cancelado", await Estado(compensado));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-30")]
    public async Task El_contacto_vence_a_los_30_dias_de_la_ultima_modificacion_de_la_persona_y_a_los_29_dias_y_23_horas_no()
    {
        var cuenta = await ConContacto();

        _e.Reloj.Adelantar(TimeSpan.FromDays(29) + TimeSpan.FromHours(23));
        var antes = await _e.ComoSistema(sp => new PurgarContactoYCheckoutsVencidos(sp.GetRequiredService<ServiciosDelCheckout>()).Ejecutar(new TrabajoDelCarritoEntrada()));
        var guardadoAntes = await ContactosGuardados(cuenta);
        _e.Reloj.Adelantar(TimeSpan.FromHours(1));
        var despues = await _e.ComoSistema(sp => new PurgarContactoYCheckoutsVencidos(sp.GetRequiredService<ServiciosDelCheckout>()).Ejecutar(new TrabajoDelCarritoEntrada()));

        Assert.Equal(0, antes.Valor.Tocados);
        Assert.Equal(1L, guardadoAntes);
        Assert.True(despues.Valor.Tocados >= 1);
        Assert.Equal(0L, await ContactosGuardados(cuenta));
        Assert.Equal("en_edicion", await Estado(cuenta));
    }

    [Fact]
    public async Task Los_trabajos_de_fondo_solo_los_acepta_el_sistema()
    {
        var cuenta = await ConContacto();

        var comoCliente = await _e.Como(cuenta, sp => new PurgarContactoYCheckoutsVencidos(sp.GetRequiredService<ServiciosDelCheckout>()).Ejecutar(new TrabajoDelCarritoEntrada()));

        Assert.False(comoCliente.EsExito);
        Assert.Equal(1L, await ContactosGuardados(cuenta));
    }

    // ---------- Nada de datos personales fuera de donde corresponde ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-26")]
    public async Task El_nombre_el_telefono_la_direccion_y_la_cookie_no_aparecen_en_la_auditoria_ni_en_lo_que_se_imprime_ni_en_los_errores()
    {
        var cuenta = await ConContacto();
        var cookie = (await _e.Agregar(null, Impreso(), 1)).Valor.CookieNueva!;
        var pago = await _e.Pagar(cuenta, 132m);
        var invalida = await _e.Como(cuenta, sp => sp.GetRequiredService<ICheckoutDelCliente>().ElegirEntrega(
            new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Domicilio, null, new ContactoDeCompra("Nombre Ajeno Único", "12345", "Dirección Secreta 999", "ayacucho"))));

        var instantanea = _e.Pagos.UltimaInstantanea!;
        var todo = string.Join('\n', _e.Auditoria.Eventos) + instantanea + instantanea.Contacto + invalida.Error + string.Join(',', invalida.Error!.Errores.Select(e => e.Campo + e.Clave));
        foreach (var dato in new[] { Nombre, Telefono, Direccion, "Nombre Ajeno Único", "12345", "Dirección Secreta 999", cookie })
        {
            Assert.DoesNotContain(dato, todo, StringComparison.Ordinal);
        }

        Assert.True(pago.EsExito, pago.Error?.Clave);
        Assert.False(invalida.EsExito);
        Assert.Contains(invalida.Error.Errores, e => e.Campo is "telefono" or "contacto.telefono");
    }
}
