using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Domain.Carrito;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Carrito;

/// <summary>
/// T30: Carrito de punta a punta sobre PostgreSQL real, el Inventario simulado y la auditoría de prueba (AC-CAR-01, AC-CAR-02, AC-CAR-09, AC-CAR-14, AC-CAR-22, AC-CAR-23, AC-CAR-27): dos dispositivos que agregan a la vez, 20 fusiones simultáneas con la misma cookie y con un pago
/// pendiente, 10 pasos al pago simultáneos, un corte tras el paso 1 y tras el 3a, la edición que rehace la reserva (A→B→A con otra clave), un cambio solo del teléfono o de la versión de un texto que reconstruye la intención, la cancelación de la cuenta y la auditoría caída que revierte.
/// </summary>
[Collection(ColeccionPostgresDelCarrito.Nombre)]
public sealed class CarritoConPostgresTests(PostgresDelCarritoFixture bd) : IAsyncLifetime
{
    private EntornoDelCarritoConPostgres _e = null!;

    public async Task InitializeAsync() => _e = await EntornoDelCarritoConPostgres.Crear(bd);

    public async Task DisposeAsync() => await _e.DisposeAsync();

    private Guid Impreso(int stock = 10, decimal precio = 40m)
    {
        var edicion = _e.Catalogo.Poner(FormatoDeEdicion.Impresa, precio);
        _e.Inventario.FijarStock(edicion, stock);
        _e.Inventario.FijarFuente(edicion, "sede-central", false, null);
        _e.Disponibilidad.Vendible(edicion, stock);
        return edicion;
    }

    private Guid Digital(decimal precio = 20m) => _e.Catalogo.Poner(FormatoDeEdicion.Digital, precio);

    private Task<string?> EstadoDelCheckout(Guid cuenta) => bd.Consultar<string>($"SELECT estado FROM carrito.checkouts WHERE cuenta_id = '{cuenta}' ORDER BY creado_en DESC LIMIT 1");

    private async Task<CarritoDelClienteVista> Carrito(Guid cuenta)
    {
        var r = await _e.Como(cuenta, sp => sp.GetRequiredService<ICarritoDelCliente>().Obtener(new SolicitudSobreElCarrito()));
        return new CarritoDelClienteVista(r.Valor.Carrito);
    }

    private sealed record CarritoDelClienteVista(VistaDelCarrito Vista);

    // ---------- Carrito ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-01")]
    public async Task Dos_dispositivos_de_la_misma_cuenta_que_agregan_a_la_vez_dejan_un_solo_carrito_con_las_dos_lineas()
    {
        var (cuenta, uno, otro) = (EntornoDelCarritoConPostgres.NuevaCuenta(), Impreso(), Impreso());

        var resultados = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => Task.Run(() => _e.Agregar(cuenta, i == 0 ? uno : otro, 2))));

        Assert.All(resultados, r => Assert.True(r.EsExito, r.Error?.Clave));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM carrito.carritos WHERE cuenta_id = '{cuenta}'"));
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM carrito.lineas_de_carrito"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-02")]
    public async Task Un_visitante_conserva_su_carrito_con_la_cookie_y_una_digital_queda_en_una_unidad()
    {
        var (impreso, digital) = (Impreso(), Digital());
        var primera = await _e.Agregar(null, impreso, 2);
        var cookie = primera.Valor.CookieNueva;
        var segunda = await _e.Agregar(null, digital, 5, cookie);

        var carrito = await _e.Como(null, sp => sp.GetRequiredService<ICarritoDelCliente>().Obtener(new SolicitudSobreElCarrito(cookie)));

        Assert.NotNull(cookie);
        Assert.Null(segunda.Valor.CookieNueva);
        Assert.Equal([2, 1], carrito.Valor.Carrito.Lineas.Select(l => l.Cantidad));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM carrito.carritos WHERE visitante_hash IS NOT NULL"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-22")]
    public async Task Veinte_fusiones_simultaneas_con_la_misma_cookie_son_idempotentes_y_dejan_el_carrito_de_la_cuenta_con_las_lineas_una_sola_vez()
    {
        var (cuenta, impreso, otro) = (EntornoDelCarritoConPostgres.NuevaCuenta(), Impreso(), Impreso());
        await _e.Agregar(cuenta, impreso, 1);
        var cookie = (await _e.Agregar(null, otro, 2)).Valor.CookieNueva!;

        var fusiones = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => _e.Como(cuenta, sp => sp.GetRequiredService<ICarritoDelCliente>().FusionarCarritoDeVisitante(cookie)))));

        Assert.All(fusiones, r => Assert.True(r.EsExito, r.Error?.Clave));
        var carrito = await Carrito(cuenta);
        Assert.Equal(2, carrito.Vista.Lineas.Count);
        Assert.Equal(3, carrito.Vista.Lineas.Sum(l => l.Cantidad));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM carrito.fusiones_aplicadas"));
        Assert.Equal(1, _e.Auditoria.Eventos.Count(e => e == "fusion"));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM carrito.carritos WHERE visitante_hash IS NOT NULL AND estado = 'abierto'"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-22")]
    public async Task Con_un_pago_pendiente_la_fusion_se_aplaza_y_la_cookie_sigue_hasta_que_se_resuelve()
    {
        var (cuenta, impreso, otro) = (EntornoDelCarritoConPostgres.NuevaCuenta(), Impreso(), Impreso());
        await _e.Agregar(cuenta, impreso, 3);
        await _e.PrepararCheckout(cuenta);
        var pago = await _e.Pagar(cuenta, 120m);
        var pedido = pago.Valor.PedidoId!.Value;
        await _e.Como(cuenta, sp => sp.GetRequiredService<IProgresoDelCheckout>().PagoPendiente(pedido, _e.Reloj.Ahora.AddMinutes(30), _e.Reloj.Ahora.AddMinutes(15)));
        var cookie = (await _e.Agregar(null, otro, 1)).Valor.CookieNueva!;

        var aplazada = await _e.Como(cuenta, sp => sp.GetRequiredService<ICarritoDelCliente>().FusionarCarritoDeVisitante(cookie));

        Assert.True(aplazada.EsExito, aplazada.Error?.Clave);
        Assert.True(aplazada.Valor.Aplazada);
        Assert.False(aplazada.Valor.BorrarLaCookie);
        Assert.Single((await Carrito(cuenta)).Vista.Lineas);
        Assert.Equal("pago_pendiente", await EstadoDelCheckout(cuenta));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-19")]
    public async Task Con_el_correo_sin_verificar_pasar_al_pago_no_crea_reserva_ni_intencion_y_el_carrito_sigue_intacto()
    {
        var (cuenta, impreso) = (EntornoDelCarritoConPostgres.NuevaCuenta(), Impreso());
        await _e.Agregar(cuenta, impreso, 3);
        await _e.PrepararCheckout(cuenta);
        _e.Cuentas.CorreoSinVerificar = true;

        var pago = await _e.Pagar(cuenta, 120m);

        Assert.False(pago.EsExito);
        Assert.Equal(0, _e.Inventario.Reservado(impreso));
        Assert.Equal(0, _e.Pagos.Creaciones);
        Assert.NotEqual("en_pago", await EstadoDelCheckout(cuenta));
        Assert.Equal(3, (await Carrito(cuenta)).Vista.Lineas[0].Cantidad);
    }

    // ---------- Pasar al pago ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-09")]
    public async Task Pasar_al_pago_reserva_una_vez_crea_la_intencion_y_deja_el_checkout_en_pago()
    {
        var (cuenta, impreso) = (EntornoDelCarritoConPostgres.NuevaCuenta(), Impreso());
        await _e.Agregar(cuenta, impreso, 3);
        await _e.PrepararCheckout(cuenta);

        var pago = await _e.Pagar(cuenta, 120m);

        Assert.True(pago.EsExito, pago.Error?.Clave);
        Assert.Equal(EstadoVisibleDelCheckout.EnPago, pago.Valor.Estado);
        Assert.Equal("en_pago", await EstadoDelCheckout(cuenta));
        Assert.Equal(3, _e.Inventario.Reservado(impreso));
        Assert.Equal(1, _e.Pagos.Creaciones);
        Assert.Contains("paso-al-pago", _e.Auditoria.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-14")]
    public async Task Diez_pasos_al_pago_simultaneos_reservan_una_sola_vez_y_dejan_un_solo_pedido()
    {
        var (cuenta, impreso) = (EntornoDelCarritoConPostgres.NuevaCuenta(), Impreso());
        await _e.Agregar(cuenta, impreso, 3);
        await _e.PrepararCheckout(cuenta);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => _e.Pagar(cuenta, 120m))));

        // Cada solicitud termina en un estado consistente: redirigida al pago (también la que lo encontró ya en curso) o con «en curso» de la primera; ninguna reserva de más.
        Assert.Contains(resultados, r => r.EsExito);
        Assert.All(resultados.Where(r => !r.EsExito), r => Assert.Equal("checkout-en-curso", r.Error!.Clave));
        Assert.Equal("en_pago", await EstadoDelCheckout(cuenta));
        Assert.Equal(3, _e.Inventario.Reservado(impreso));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(DISTINCT pedido_id) FROM carrito.checkouts WHERE cuenta_id = '{cuenta}' AND pedido_id IS NOT NULL"));
    }

    // ---------- Cortes ----------

    [Fact]
    public async Task Un_corte_tras_el_paso_1_deja_reservando_y_el_trabajo_de_recuperacion_lo_devuelve_a_edicion_y_se_puede_volver_a_pagar()
    {
        var (cuenta, impreso) = (EntornoDelCarritoConPostgres.NuevaCuenta(), Impreso());
        await _e.Agregar(cuenta, impreso, 3);
        await _e.PrepararCheckout(cuenta);
        await _e.Como(cuenta, sp => new PrepararPasoAlPago(sp.GetRequiredService<ServiciosDelCheckout>()).Ejecutar(new PasarAlPagoEntrada(120m, EntornoDelCarritoConPostgres.LosDosTextos)));
        Assert.Equal("reservando", await EstadoDelCheckout(cuenta));
        _e.Reloj.Adelantar(TimeSpan.FromMinutes(3));

        var recuperados = await _e.ComoSistema(sp => new RecuperarCheckoutsReservando(sp.GetRequiredService<ServiciosDelCheckout>()).Ejecutar(new TrabajoDelCarritoEntrada()));

        Assert.Equal(1, recuperados.Valor.Tocados);
        Assert.Equal("en_edicion", await EstadoDelCheckout(cuenta));
        Assert.Contains("aborto:recuperacion", _e.Auditoria.Eventos);
        var otraVez = await _e.Pagar(cuenta, 120m);
        Assert.True(otraVez.EsExito, otraVez.Error?.Clave);
        Assert.Equal(3, _e.Inventario.Reservado(impreso));
    }

    [Fact]
    public async Task Un_corte_tras_el_paso_3a_con_la_intencion_creada_y_la_reserva_hecha_se_recupera_cancelando_la_intencion_y_liberando_la_reserva()
    {
        var (cuenta, impreso) = (EntornoDelCarritoConPostgres.NuevaCuenta(), Impreso());
        await _e.Agregar(cuenta, impreso, 3);
        await _e.PrepararCheckout(cuenta);
        var preparado = (await _e.Como(cuenta, sp => new PrepararPasoAlPago(sp.GetRequiredService<ServiciosDelCheckout>()).Ejecutar(new PasarAlPagoEntrada(120m, EntornoDelCarritoConPostgres.LosDosTextos)))).Valor;
        var paso = await _e.Como(cuenta, async sp => await new ReservarImpresos(sp.GetRequiredService<ServiciosDelCheckout>()).Ejecutar(preparado));
        await _e.Como(cuenta, async sp => await new SolicitarIntencionDePago(sp.GetRequiredService<ServiciosDelCheckout>()).Ejecutar(preparado, paso.ExpiraEn, CancellationToken.None));
        Assert.Equal(3, _e.Inventario.Reservado(impreso));
        Assert.Equal(1, _e.Pagos.Creaciones);
        _e.Reloj.Adelantar(TimeSpan.FromMinutes(3));

        await _e.ComoSistema(sp => new RecuperarCheckoutsReservando(sp.GetRequiredService<ServiciosDelCheckout>()).Ejecutar(new TrabajoDelCarritoEntrada()));

        Assert.Equal("en_edicion", await EstadoDelCheckout(cuenta));
        Assert.True(_e.Pagos.Cancelaciones >= 1);
        Assert.Equal(0, _e.Inventario.Reservado(impreso));
    }

    // ---------- Ediciones con el checkout en pago ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-23")]
    public async Task Editar_la_cantidad_en_pago_y_volverla_al_valor_original_rehace_la_reserva_con_otra_clave_sin_reservar_de_mas()
    {
        var (cuenta, impreso) = (EntornoDelCarritoConPostgres.NuevaCuenta(), Impreso());
        await _e.Agregar(cuenta, impreso, 3);
        await _e.PrepararCheckout(cuenta);
        var primera = await _e.Pagar(cuenta, 120m);
        Assert.True(primera.EsExito, primera.Error?.Clave);
        var generacionA = await bd.Consultar<int>($"SELECT generacion_de_reserva FROM carrito.checkouts WHERE cuenta_id = '{cuenta}'");

        var aB = await _e.Como(cuenta, sp => sp.GetRequiredService<ICarritoDelCliente>().ModificarLinea(new ModificarLineaEntrada(impreso, 2)));
        Assert.True(aB.EsExito, aB.Error?.Clave);
        await _e.Como(cuenta, sp => sp.GetRequiredService<ICheckoutDelCliente>().RegistrarAceptaciones(new RegistrarAceptacionesEntrada(EntornoDelCarritoConPostgres.LosDosTextos)));
        var pagoB = await _e.Pagar(cuenta, 80m);
        Assert.True(pagoB.EsExito, pagoB.Error?.Clave);
        var aA = await _e.Como(cuenta, sp => sp.GetRequiredService<ICarritoDelCliente>().ModificarLinea(new ModificarLineaEntrada(impreso, 3)));
        Assert.True(aA.EsExito, aA.Error?.Clave);
        await _e.Como(cuenta, sp => sp.GetRequiredService<ICheckoutDelCliente>().RegistrarAceptaciones(new RegistrarAceptacionesEntrada(EntornoDelCarritoConPostgres.LosDosTextos)));
        var pagoA = await _e.Pagar(cuenta, 120m);
        Assert.True(pagoA.EsExito, pagoA.Error?.Clave);

        var generacionFinal = await bd.Consultar<int>($"SELECT generacion_de_reserva FROM carrito.checkouts WHERE cuenta_id = '{cuenta}'");
        Assert.True(generacionFinal > generacionA, "volver a la cantidad original no reutiliza la clave de la primera reserva");
        Assert.Equal(3, _e.Inventario.Reservado(impreso));
        Assert.Equal("en_pago", await EstadoDelCheckout(cuenta));
    }

    [Fact]
    public async Task Un_cambio_solo_del_telefono_con_el_checkout_en_pago_vuelve_a_edicion_y_reabre_la_intencion()
    {
        var (cuenta, impreso) = (EntornoDelCarritoConPostgres.NuevaCuenta(), Impreso());
        await _e.Agregar(cuenta, impreso, 3);
        await _e.PrepararCheckout(cuenta, domicilio: true);
        var pago = await _e.Pagar(cuenta, 132m);
        Assert.True(pago.EsExito, pago.Error?.Clave);

        var cambio = await _e.Como(cuenta, sp => sp.GetRequiredService<ICheckoutDelCliente>().ElegirEntrega(
            new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Domicilio, null, new ContactoDeCompra("Ana Pérez Quispe", "912345678", "Jr. Lima 123", "ayacucho"))));

        Assert.True(cambio.EsExito, cambio.Error?.Clave);
        Assert.Equal("en_edicion", await EstadoDelCheckout(cuenta));
        Assert.Equal(1, _e.Pagos.Reaperturas);
    }

    [Fact]
    public async Task Un_cambio_de_la_version_vigente_de_un_texto_invalida_la_aceptacion_y_exige_aceptar_la_nueva()
    {
        var (cuenta, impreso) = (EntornoDelCarritoConPostgres.NuevaCuenta(), Impreso());
        await _e.Agregar(cuenta, impreso, 3);
        await _e.PrepararCheckout(cuenta);
        _e.Textos.Vigente(TipoDeTextoLegal.Terminos, "t-2");

        var conLaVersionVieja = await _e.Como(cuenta, sp => sp.GetRequiredService<ICheckoutDelCliente>().RegistrarAceptaciones(new RegistrarAceptacionesEntrada(EntornoDelCarritoConPostgres.LosDosTextos)));
        var pago = await _e.Pagar(cuenta, 120m);

        Assert.False(conLaVersionVieja.EsExito);
        Assert.Equal("checkout-texto-cambio", conLaVersionVieja.Error!.Clave);
        Assert.False(pago.EsExito);
        Assert.Equal(0, _e.Inventario.Reservado(impreso));
    }

    // ---------- Cancelación de la cuenta y auditoría ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-27")]
    public async Task Una_cuenta_con_la_cancelacion_en_marcha_no_pasa_al_pago_ni_deja_reserva()
    {
        var (cuenta, impreso) = (EntornoDelCarritoConPostgres.NuevaCuenta(), Impreso());
        await _e.Agregar(cuenta, impreso, 3);
        await _e.PrepararCheckout(cuenta);
        _e.Marca.Estado = EstadoDeLaMarcaDeCancelacion.EnEjecucion;

        var pago = await _e.Pagar(cuenta, 120m);

        Assert.False(pago.EsExito);
        Assert.NotEqual("en_pago", await EstadoDelCheckout(cuenta));
        Assert.Equal(0, _e.Inventario.Reservado(impreso));
        Assert.Equal(0, _e.Pagos.Creaciones);
    }

    [Fact]
    public async Task Con_la_auditoria_caida_pasar_al_pago_falla_sin_dejar_en_pago_y_el_trabajo_de_recuperacion_libera_la_reserva()
    {
        var (cuenta, impreso) = (EntornoDelCarritoConPostgres.NuevaCuenta(), Impreso());
        await _e.Agregar(cuenta, impreso, 3);
        await _e.PrepararCheckout(cuenta);
        _e.Auditoria.Caida = true;

        var pago = await _e.Pagar(cuenta, 120m);

        Assert.False(pago.EsExito);
        Assert.Equal(CategoriaDeError.NoDisponible, pago.Error!.Categoria);
        Assert.Equal("reservando", await EstadoDelCheckout(cuenta));

        // Con la auditoría de vuelta, el trabajo de recuperación devuelve el checkout a edición y libera la reserva.
        _e.Auditoria.Caida = false;
        _e.Reloj.Adelantar(TimeSpan.FromMinutes(3));
        await _e.ComoSistema(sp => new RecuperarCheckoutsReservando(sp.GetRequiredService<ServiciosDelCheckout>()).Ejecutar(new TrabajoDelCarritoEntrada()));
        Assert.Equal("en_edicion", await EstadoDelCheckout(cuenta));
        Assert.Equal(0, _e.Inventario.Reservado(impreso));
    }

    [Fact]
    public async Task Con_la_auditoria_caida_agregar_una_linea_no_deja_nada_a_medias()
    {
        var (cuenta, impreso) = (EntornoDelCarritoConPostgres.NuevaCuenta(), Impreso());
        await _e.Agregar(cuenta, impreso, 1);
        _e.Auditoria.Caida = true;

        var otra = await _e.Agregar(cuenta, Impreso(), 1);

        // Agregar no audita (solo las operaciones sensibles): el carrito sigue funcionando con la auditoría caída.
        Assert.True(otra.EsExito, otra.Error?.Clave);
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM carrito.lineas_de_carrito"));
    }
}
