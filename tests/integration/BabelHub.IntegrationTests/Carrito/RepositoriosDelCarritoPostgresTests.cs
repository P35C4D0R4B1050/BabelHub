using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Domain.Carrito;
using BabelHub.Ecommerce.Infrastructure.Carrito;
using System.Security.Cryptography;
using System.Text;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.IntegrationTests.Carrito;

/// <summary>
/// T24: los repositorios de Carrito y de Checkout sobre PostgreSQL real con el rol <c>rol_carrito</c> (AC-CAR-01, AC-CAR-25, AC-CAR-28, AC-CAR-29, AC-CAR-30): ida y vuelta de los agregados, la unicidad con su traducción, el bloqueo <c>FOR UPDATE</c>,
/// la eliminación de una cuenta (incluida la constancia de fusión por cascada), los límites inclusivos de las purgas, la liberación pendiente por <c>pedido_a_liberar</c> y que un <c>CHECK</c> fallido no deje el contacto en el error.
/// </summary>
[Collection(ColeccionPostgresDelCarrito.Nombre)]
public sealed class RepositoriosDelCarritoPostgresTests(PostgresDelCarritoFixture bd) : IAsyncLifetime
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static byte[] Huella(string semilla) => SHA256.HashData(Encoding.UTF8.GetBytes(semilla));

    private static string Hex(string texto) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto)));

    private static CarritoDeDominio CarritoDeCuenta(Guid cuenta, params (Guid Edicion, bool Digital, int Cantidad)[] lineas)
    {
        var carrito = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeCuenta(cuenta), Inicio);
        foreach (var (edicion, digital, cantidad) in lineas)
        {
            Assert.True(carrito.Agregar(edicion, digital, cantidad, Inicio).EsExito);
        }

        return carrito;
    }

    private static Checkout ElCheckout(CarritoDeDominio carrito, bool aDomicilio = false)
    {
        var cuenta = carrito.Propietario.CuentaId!.Value;
        var checkout = Checkout.Iniciar(
            Guid.NewGuid(),
            cuenta,
            carrito.Id,
            carrito.Lineas.Select(l => new LineaParaCheckout(l.EdicionId, l.EsDigital, "Cien años de soledad", "9780307474728", l.Cantidad.Valor, 40m, l.LineaId, l.CodigoDeSucursal)),
            Guid.NewGuid,
            Inicio);
        Assert.Equal(EfectoDeTransicion.Aplicada, checkout.ConfirmarRevision(Inicio).Efecto);
        if (aDomicilio)
        {
            var contacto = ContactoDeEntrega.Crear(NombreDeContacto.Crear("Ana Pérez Quispe"), TelefonoDeContacto.Crear("987654321"), DireccionDeEntrega.Crear("Jr. Lima 123, Ayacucho", "ayacucho"));
            Assert.Equal(EfectoDeTransicion.Aplicada, checkout.ElegirDomicilio(contacto, Inicio).Efecto);
        }
        else
        {
            Assert.Equal(EfectoDeTransicion.Aplicada, checkout.ElegirRecojo(Inicio).Efecto);
        }

        checkout.Aceptar(TextoLegal.Terminos, "t-1", Inicio);
        checkout.Aceptar(TextoLegal.Privacidad, "p-1", Inicio.AddMinutes(1));
        checkout.Aceptar(TextoLegal.ReembolsoDigital, "r-1", Inicio.AddMinutes(1));
        return checkout;
    }

    private async Task<(CarritoDeDominio Carrito, Checkout Checkout)> Sembrar(Guid cuenta, bool aDomicilio = false)
    {
        var carrito = CarritoDeCuenta(cuenta, (Guid.NewGuid(), false, 3), (Guid.NewGuid(), true, 1));
        var checkout = ElCheckout(carrito, aDomicilio);
        await bd.Unidad.Ejecutar(
            async token =>
            {
                await bd.Carritos.Agregar(carrito, token);
                await bd.Checkouts.Agregar(checkout, token);
                return 0;
            });
        return (carrito, checkout);
    }

    // ---------- Carritos ----------

    [Fact]
    public async Task Un_carrito_de_cuenta_se_guarda_y_se_lee_de_nuevo_con_sus_lineas_en_orden_y_el_retiro_pendiente()
    {
        var cuenta = Guid.NewGuid();
        var (primera, segunda) = (Guid.NewGuid(), Guid.NewGuid());
        var carrito = CarritoDeCuenta(cuenta, (primera, false, 3), (segunda, true, 1));
        await bd.Unidad.Ejecutar(async token => { await bd.Carritos.Agregar(carrito, token); return 0; });

        var leido = await bd.Unidad.Ejecutar(token => bd.Carritos.ObtenerDeCuenta(cuenta, conBloqueo: false, token));

        Assert.NotNull(leido);
        Assert.Equal(carrito.Id, leido.Id);
        Assert.Equal([primera, segunda], leido.Lineas.Select(l => l.EdicionId));
        Assert.Equal([3, 1], leido.Lineas.Select(l => l.Cantidad.Valor));
        Assert.Equal([false, true], leido.Lineas.Select(l => l.EsDigital));
        Assert.Null(leido.VenceEn);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-01")]
    public async Task Dos_dispositivos_que_crean_el_carrito_de_la_misma_cuenta_a_la_vez_dejan_uno_y_el_otro_recibe_CarritoYaExiste()
    {
        var cuenta = Guid.NewGuid();
        var puerta = new TaskCompletionSource();

        async Task<Exception?> Crear()
        {
            try
            {
                await puerta.Task;
                await bd.Unidad.Ejecutar(async token => { await bd.Carritos.Agregar(CarritoDeCuenta(cuenta, (Guid.NewGuid(), false, 1)), token); return 0; });
                return null;
            }
            catch (Exception excepcion)
            {
                return excepcion;
            }
        }

        var tareas = new[] { Crear(), Crear() };
        puerta.SetResult();
        var errores = (await Task.WhenAll(tareas)).Where(e => e is not null).ToList();

        Assert.IsType<CarritoYaExisteException>(Assert.Single(errores));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM carrito.carritos WHERE cuenta_id = '{cuenta}'"));
    }

    [Fact]
    public async Task Dos_cookies_iguales_de_visitante_no_crean_dos_carritos()
    {
        var huella = Huella("cookie");
        var uno = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeVisitante(huella), Inicio);
        var otro = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeVisitante(huella), Inicio);
        await bd.Unidad.Ejecutar(async token => { await bd.Carritos.Agregar(uno, token); return 0; });

        await Assert.ThrowsAsync<CarritoYaExisteException>(() => bd.Unidad.Ejecutar(async token => { await bd.Carritos.Agregar(otro, token); return 0; }));

        var leido = await bd.Unidad.Ejecutar(token => bd.Carritos.ObtenerDeVisitante(huella, conBloqueo: false, token));
        Assert.Equal(uno.Id, leido!.Id);
        Assert.Equal(Inicio.AddDays(30), leido.VenceEn);
    }

    [Fact]
    public async Task Guardar_cambia_las_lineas_y_una_version_mas_nueva_en_la_base_hace_fallar_a_la_mas_vieja()
    {
        var cuenta = Guid.NewGuid();
        var carrito = CarritoDeCuenta(cuenta, (Guid.NewGuid(), false, 1));
        await bd.Unidad.Ejecutar(async token => { await bd.Carritos.Agregar(carrito, token); return 0; });
        var nueva = await bd.Unidad.Ejecutar(token => bd.Carritos.ObtenerDeCuenta(cuenta, conBloqueo: true, token));
        var vieja = await bd.Unidad.Ejecutar(token => bd.Carritos.ObtenerDeCuenta(cuenta, conBloqueo: false, token));
        Assert.True(nueva!.Agregar(Guid.NewGuid(), false, 2, Inicio.AddMinutes(1)).EsExito);
        Assert.True(nueva!.Agregar(Guid.NewGuid(), false, 1, Inicio.AddMinutes(2)).EsExito);
        await bd.Unidad.Ejecutar(async token => { await bd.Carritos.Guardar(nueva!, token); return 0; });
        Assert.True(vieja!.Agregar(Guid.NewGuid(), false, 1, Inicio.AddMinutes(3)).EsExito);

        await Assert.ThrowsAsync<InvalidOperationException>(() => bd.Unidad.Ejecutar(async token => { await bd.Carritos.Guardar(vieja!, token); return 0; }));

        Assert.Equal(3L, await bd.Consultar<long>($"SELECT count(*) FROM carrito.lineas_de_carrito WHERE carrito_id = '{carrito.Id}'"));
    }

    [Fact]
    public async Task Una_lectura_con_bloqueo_espera_a_la_transaccion_que_tiene_la_fila_y_sigue_cuando_esta_termina()
    {
        var cuenta = Guid.NewGuid();
        await bd.Unidad.Ejecutar(async token => { await bd.Carritos.Agregar(CarritoDeCuenta(cuenta, (Guid.NewGuid(), false, 1)), token); return 0; });
        var primeraTieneLaFila = new TaskCompletionSource();
        var soltar = new TaskCompletionSource();
        var primera = Task.Run(() => bd.Unidad.Ejecutar(
            async token =>
            {
                await bd.Carritos.ObtenerDeCuenta(cuenta, conBloqueo: true, token);
                primeraTieneLaFila.SetResult();
                await soltar.Task;
                return 0;
            }));
        await primeraTieneLaFila.Task;

        var segunda = Task.Run(() => bd.Unidad.Ejecutar(token => bd.Carritos.ObtenerDeCuenta(cuenta, conBloqueo: true, token)));
        var termino = await Task.WhenAny(segunda, Task.Delay(700));

        Assert.NotSame(segunda, termino);
        soltar.SetResult();
        await primera;
        Assert.NotNull(await segunda);
    }

    [Fact]
    public async Task Un_carrito_fusionado_no_admite_lineas_y_la_constancia_de_fusion_es_idempotente()
    {
        var huella = Huella("visitante");
        var cuenta = CarritoDeCuenta(Guid.NewGuid(), (Guid.NewGuid(), false, 1));
        var visitante = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeVisitante(huella), Inicio);
        await bd.Unidad.Ejecutar(
            async token =>
            {
                await bd.Carritos.Agregar(cuenta, token);
                await bd.Carritos.Agregar(visitante, token);
                await bd.Carritos.RegistrarFusion(huella, cuenta.Id, Inicio, token);
                await bd.Carritos.RegistrarFusion(huella, cuenta.Id, Inicio.AddMinutes(5), token);
                return 0;
            });
        await bd.Ejecutar($"UPDATE carrito.carritos SET estado = 'fusionado' WHERE id = '{visitante.Id}'");

        var falla = await Assert.ThrowsAnyAsync<Exception>(() => bd.Ejecutar($"INSERT INTO carrito.lineas_de_carrito (carrito_id, edicion_id, posicion, cantidad, es_digital, agregada_en) VALUES ('{visitante.Id}', gen_random_uuid(), 0, 1, false, now())"));

        Assert.Contains("fusionado", falla.Message, StringComparison.Ordinal);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM carrito.fusiones_aplicadas"));
    }

    // ---------- Avisos ----------

    [Fact]
    public async Task Los_avisos_se_listan_del_mas_reciente_se_marcan_como_vistos_y_se_purgan_a_los_30_dias_de_vistos_o_a_los_90()
    {
        var cuenta = Guid.NewGuid();
        var avisoViejo = new AvisoDelCarrito(Guid.NewGuid(), TipoDeAvisoDelCarrito.EdicionRetirada, Guid.NewGuid(), "A", null, null, Inicio.AddDays(-91));
        var avisoVisto = new AvisoDelCarrito(Guid.NewGuid(), TipoDeAvisoDelCarrito.FusionAjustada, Guid.NewGuid(), "B", MotivoDeAjusteDeAviso.RecortadaAlMaximo, 20, Inicio.AddDays(-40));
        var avisoNuevo = new AvisoDelCarrito(Guid.NewGuid(), TipoDeAvisoDelCarrito.EdicionRetirada, Guid.NewGuid(), "C", null, null, Inicio);
        await bd.Unidad.Ejecutar(
            async token =>
            {
                await bd.Carritos.AgregarAvisos(cuenta, [avisoViejo, avisoVisto, avisoNuevo], token);
                await bd.Carritos.MarcarAvisosComoVistos(cuenta, [avisoVisto.Id], Inicio.AddDays(-30), token);
                return 0;
            });

        var sinVer = await bd.Unidad.Ejecutar(token => bd.Carritos.AvisosSinVer(cuenta, token));
        var eliminados = await bd.Unidad.Ejecutar(token => bd.Carritos.EliminarAvisosVencidos(Inicio.AddDays(-30), Inicio.AddDays(-90), 100, token));

        Assert.Equal([avisoNuevo.Id, avisoViejo.Id], sinVer.Select(a => a.Id));
        Assert.Equal(2, eliminados);
        Assert.Equal(avisoNuevo.Id, Assert.Single(await bd.Unidad.Ejecutar(token => bd.Carritos.AvisosSinVer(cuenta, token))).Id);
        var recuperado = Assert.Single(sinVer, a => a.Id == avisoNuevo.Id);
        Assert.Equal((TipoDeAvisoDelCarrito.EdicionRetirada, "C"), (recuperado.Tipo, recuperado.Titulo));
    }

    // ---------- Purgas con límites inclusivos ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-25")]
    public async Task El_carrito_de_visitante_vence_exactamente_a_los_30_dias_y_el_de_una_cuenta_no_caduca()
    {
        var visitante = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeVisitante(Huella("v")), Inicio);
        var cuenta = CarritoDeCuenta(Guid.NewGuid(), (Guid.NewGuid(), false, 1));
        await bd.Unidad.Ejecutar(async token => { await bd.Carritos.Agregar(visitante, token); await bd.Carritos.Agregar(cuenta, token); return 0; });

        var a = await bd.Unidad.Ejecutar(token => bd.Carritos.EliminarDeVisitantesVencidos(Inicio.AddDays(30).AddSeconds(-1), 100, token));
        var b = await bd.Unidad.Ejecutar(token => bd.Carritos.EliminarDeVisitantesVencidos(Inicio.AddDays(30), 100, token));

        Assert.Equal((0, 1), (a, b));
        Assert.Equal(cuenta.Propietario.CuentaId, Assert.Single(await bd.Unidad.Ejecutar(token => bd.Carritos.CuentasSinMovimientoDesde(Inicio.AddDays(365), 10, token))));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM carrito.carritos"));
    }

    [Fact]
    public async Task Las_purgas_respetan_el_tope_por_corrida()
    {
        await bd.Unidad.Ejecutar(
            async token =>
            {
                for (var i = 0; i < 5; i++)
                {
                    await bd.Carritos.Agregar(CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeVisitante(Huella("t" + i)), Inicio), token);
                }

                return 0;
            });

        var primera = await bd.Unidad.Ejecutar(token => bd.Carritos.EliminarDeVisitantesVencidos(Inicio.AddDays(31), 3, token));
        var segunda = await bd.Unidad.Ejecutar(token => bd.Carritos.EliminarDeVisitantesVencidos(Inicio.AddDays(31), 3, token));

        Assert.Equal((3, 2), (primera, segunda));
    }

    // ---------- Checkouts ----------

    [Fact]
    public async Task Un_checkout_a_domicilio_se_guarda_y_se_lee_de_nuevo_con_todo_su_estado_y_el_contacto()
    {
        var cuenta = Guid.NewGuid();
        var (carrito, checkout) = await Sembrar(cuenta, aDomicilio: true);

        var leido = await bd.Unidad.Ejecutar(token => bd.Checkouts.ObtenerVivoDeCuenta(cuenta, conBloqueo: true, token));

        Assert.NotNull(leido);
        Assert.Equal((checkout.Id, carrito.Id, EstadoDelCheckout.EnEdicion), (leido.Id, leido.CarritoId, leido.Estado));
        Assert.Equal(checkout.PasoDeEdicion, leido.PasoDeEdicion);
        Assert.Equal(Tipo(), leido.TipoDeEntrega);
        Assert.Equal("ayacucho", leido.RegionDeEntrega);
        Assert.Equal("Ana Pérez Quispe", leido.Contacto!.Nombre.Valor);
        Assert.Equal("987654321", leido.Contacto.Telefono.Valor);
        Assert.Equal("Jr. Lima 123, Ayacucho", leido.Contacto.Direccion.Texto);
        Assert.Equal(checkout.Lineas.Select(l => (l.LineaId, l.EdicionId, l.Cantidad.Valor)), leido.Lineas.Select(l => (l.LineaId, l.EdicionId, l.Cantidad.Valor)));
        Assert.Equal(checkout.Aceptaciones.Aceptadas, leido.Aceptaciones.Aceptadas);
        Assert.DoesNotContain("Ana", leido.Contacto.ToString(), StringComparison.Ordinal);

        static TipoDeEntrega Tipo() => TipoDeEntrega.Domicilio;
    }

    [Fact]
    public async Task Un_segundo_checkout_vivo_de_la_misma_cuenta_recibe_CheckoutYaExiste_y_uno_terminado_no_estorba()
    {
        var cuenta = Guid.NewGuid();
        var (carrito, _) = await Sembrar(cuenta);
        var otro = ElCheckout(carrito);

        await Assert.ThrowsAsync<CheckoutYaExisteException>(() => bd.Unidad.Ejecutar(async token => { await bd.Checkouts.Agregar(otro, token); return 0; }));

        await bd.Ejecutar($"UPDATE carrito.checkouts SET estado = 'cancelado', direccion = NULL, contacto_nombre = NULL, contacto_telefono = NULL WHERE cuenta_id = '{cuenta}'");
        await bd.Unidad.Ejecutar(async token => { await bd.Checkouts.Agregar(otro, token); return 0; });
        Assert.Equal(otro.Id, (await bd.Unidad.Ejecutar(token => bd.Checkouts.ObtenerVivoDeCuenta(cuenta, conBloqueo: false, token)))!.Id);
    }

    [Fact]
    public async Task Guardar_un_checkout_en_pago_conserva_los_ids_de_las_lineas_y_las_huellas_y_se_relee_igual()
    {
        var cuenta = Guid.NewGuid();
        var (carrito, checkout) = await Sembrar(cuenta);
        var pedido = Guid.NewGuid();
        Assert.Equal(EfectoDeTransicion.Aplicada, checkout.PrepararPasoAlPago(pedido, CostoDeEnvio.Crear(12m), s => Hex(s), s => "v1:" + Hex(s), carrito, Inicio.AddMinutes(2)).Efecto);
        Assert.Equal(EfectoDeTransicion.Aplicada, checkout.ConfirmarEnPago(pedido, checkout.GeneracionDeReserva, checkout.HuellaDeInstantanea!, Inicio.AddMinutes(17), carrito).Efecto);
        await bd.Unidad.Ejecutar(async token => { await bd.Checkouts.Guardar(checkout, token); return 0; });

        var leido = await bd.Unidad.Ejecutar(token => bd.Checkouts.ObtenerPorPedido(pedido, conBloqueo: false, token));

        Assert.Equal((EstadoDelCheckout.EnPago, pedido, checkout.GeneracionDeReserva), (leido!.Estado, leido.PedidoId, leido.GeneracionDeReserva));
        Assert.Equal((checkout.HuellaDeReserva, checkout.HuellaDeInstantanea, checkout.ReservaExpiraEn), (leido.HuellaDeReserva, leido.HuellaDeInstantanea, leido.ReservaExpiraEn));
        Assert.Equal((checkout.EnvioCongelado, checkout.TotalCongelado), (leido.EnvioCongelado, leido.TotalCongelado));
        Assert.Equal(checkout.Lineas.Select(l => l.LineaId), leido.Lineas.Select(l => l.LineaId));
        Assert.Equal(EstadoDelCheckout.EnPago, await bd.Unidad.Ejecutar(async token => (await bd.Checkouts.LeerEstadoSinBloqueo(cuenta, token))!.Value));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-21")]
    public async Task El_envio_de_cada_sucursal_se_guarda_con_su_importe_se_relee_igual_y_se_borra_al_volver_a_edicion()
    {
        var cuenta = Guid.NewGuid();
        var carrito = CarritoDeCuenta(cuenta, (Guid.NewGuid(), false, 1));
        Assert.True(carrito.Agregar(Guid.NewGuid(), Guid.NewGuid(), false, "cusco", 1, Inicio).EsExito);
        var checkout = ElCheckout(carrito, aDomicilio: true);
        await bd.Unidad.Ejecutar(async token => { await bd.Carritos.Agregar(carrito, token); await bd.Checkouts.Agregar(checkout, token); return 0; });
        var pedido = Guid.NewGuid();
        checkout.PrepararPasoAlPago(
            pedido,
            [new EnvioDeSucursal("cusco", CostoDeEnvio.Crear(0m)), new EnvioDeSucursal(AtajosDeCarrito.Sucursal, CostoDeEnvio.Crear(18.50m))],
            s => Hex(s),
            s => "v1:" + Hex(s),
            carrito,
            Inicio.AddMinutes(2));
        await bd.Unidad.Ejecutar(async token => { await bd.Checkouts.Guardar(checkout, token); return 0; });

        var leido = await bd.Unidad.Ejecutar(token => bd.Checkouts.ObtenerPorPedido(pedido, conBloqueo: false, token));
        var guardado = await bd.Consultar<string>($"SELECT envios_congelados::text FROM carrito.checkouts WHERE cuenta_id = '{cuenta}'");

        Assert.Equal([("cusco", 0m), (AtajosDeCarrito.Sucursal, 18.50m)], leido!.EnviosCongelados!.Select(e => (e.CodigoDeSucursal, e.Costo.Valor)));
        Assert.Equal(18.50m, leido.EnvioCongelado);
        // Solo el código y el importe: el nombre de la sucursal no se guarda.
        Assert.Equal("""[{"importe": 0.00, "sucursal": "cusco"}, {"importe": 18.50, "sucursal": "sede-central"}]""", guardado);

        leido.AbortarPasoAlPago(EstadoDelCheckout.Reservando, pedido, leido.GeneracionDeReserva, carrito, Inicio.AddMinutes(3));
        await bd.Unidad.Ejecutar(async token => { await bd.Checkouts.Guardar(leido, token); return 0; });

        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM carrito.checkouts WHERE cuenta_id = '{cuenta}' AND envios_congelados IS NULL AND envio_congelado IS NULL AND total_congelado IS NULL"));
    }

    [Fact]
    public async Task La_liberacion_pendiente_se_busca_y_se_direcciona_por_pedido_a_liberar_aunque_el_checkout_tenga_ya_otro_pedido_vivo()
    {
        var cuenta = Guid.NewGuid();
        var (carrito, checkout) = await Sembrar(cuenta);
        var (p1, p2) = (Guid.NewGuid(), Guid.NewGuid());
        checkout.PrepararPasoAlPago(p1, CostoDeEnvio.Crear(12m), s => Hex(s), s => "v1:" + Hex(s), carrito, Inicio.AddMinutes(2));
        checkout.AbortarPasoAlPago(EstadoDelCheckout.Reservando, p1, checkout.GeneracionDeReserva, carrito, Inicio.AddMinutes(3));
        checkout.PrepararPasoAlPago(p2, CostoDeEnvio.Crear(12m), s => Hex(s), s => "v1:" + Hex(s), carrito, Inicio.AddMinutes(4));
        await bd.Unidad.Ejecutar(async token => { await bd.Checkouts.Guardar(checkout, token); return 0; });

        var pendientes = await bd.Unidad.Ejecutar(token => bd.Checkouts.ConLiberacionPendiente(10, token));
        var porPedidoALiberar = await bd.Unidad.Ejecutar(token => bd.Checkouts.ObtenerPorPedido(p1, conBloqueo: true, token));

        Assert.Equal(p1, Assert.Single(pendientes).PedidoId);
        Assert.Equal((checkout.Id, p2), (porPedidoALiberar!.Id, porPedidoALiberar.PedidoId));
        porPedidoALiberar.LiberacionConfirmada();
        await bd.Unidad.Ejecutar(async token => { await bd.Checkouts.Guardar(porPedidoALiberar, token); return 0; });
        Assert.Empty(await bd.Unidad.Ejecutar(token => bd.Checkouts.ConLiberacionPendiente(10, token)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-30")]
    public async Task Los_checkouts_vivos_sin_movimiento_se_buscan_a_los_30_dias_inclusive_y_a_29_dias_y_23_horas_no()
    {
        var cuenta = Guid.NewGuid();
        await Sembrar(cuenta, aDomicilio: true);
        var modificado = Inicio.AddMinutes(1);

        var antes = await bd.Unidad.Ejecutar(token => bd.Checkouts.CuentasConCheckoutVivoSinMovimientoDesde(modificado.AddHours(-1), 10, token));
        var justo = await bd.Unidad.Ejecutar(token => bd.Checkouts.CuentasConCheckoutVivoSinMovimientoDesde(modificado, 10, token));

        Assert.Empty(antes);
        Assert.Equal(cuenta, Assert.Single(justo));
    }

    [Fact]
    public async Task Los_terminados_se_eliminan_a_los_30_dias_pero_no_los_que_aun_tienen_una_reserva_por_liberar()
    {
        var (c1, k1) = (Guid.NewGuid(), Guid.NewGuid());
        var (carrito1, checkout1) = await Sembrar(c1);
        var (carrito2, checkout2) = await Sembrar(Guid.NewGuid());
        var pedido1 = Guid.NewGuid();
        checkout1.PrepararPasoAlPago(pedido1, CostoDeEnvio.Crear(12m), s => Hex(s), s => "v1:" + Hex(s), carrito1, Inicio.AddMinutes(2));
        checkout1.ConfirmarEnPago(pedido1, checkout1.GeneracionDeReserva, checkout1.HuellaDeInstantanea!, Inicio.AddMinutes(17), carrito1);
        checkout1.Volver(carrito1, Inicio.AddMinutes(3));
        checkout2.Volver(carrito2, Inicio.AddMinutes(3));
        _ = k1;
        await bd.Unidad.Ejecutar(async token => { await bd.Checkouts.Guardar(checkout1, token); await bd.Checkouts.Guardar(checkout2, token); return 0; });

        var borrados = await bd.Unidad.Ejecutar(token => bd.Checkouts.EliminarTerminadosAnterioresA(Inicio.AddDays(31), 10, token));

        Assert.Equal(1, borrados);
        Assert.NotNull(await bd.Unidad.Ejecutar(token => bd.Checkouts.ObtenerPorPedido(checkout1.PedidoALiberar!.Value, conBloqueo: false, token)));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM carrito.checkouts"));
    }

    [Fact]
    public async Task Los_checkouts_reservando_atascados_y_los_pagos_pendientes_viejos_se_encuentran_con_sus_limites()
    {
        var (carrito, checkout) = await Sembrar(Guid.NewGuid());
        var pedido = Guid.NewGuid();
        checkout.PrepararPasoAlPago(pedido, CostoDeEnvio.Crear(12m), s => Hex(s), s => "v1:" + Hex(s), carrito, Inicio.AddMinutes(2));
        await bd.Unidad.Ejecutar(async token => { await bd.Checkouts.Guardar(checkout, token); return 0; });

        var temprano = await bd.Unidad.Ejecutar(token => bd.Checkouts.ReservandoDesdeAntesDe(Inicio.AddMinutes(1), 10, token));
        var atascado = await bd.Unidad.Ejecutar(token => bd.Checkouts.ReservandoDesdeAntesDe(Inicio.AddMinutes(2), 10, token));

        Assert.Empty(temprano);
        Assert.Equal((checkout.CuentaId, pedido, checkout.GeneracionDeReserva), (atascado.Single().CuentaId, atascado.Single().PedidoId, atascado.Single().Generacion));
        Assert.Equal(0, await bd.Unidad.Ejecutar(token => bd.Checkouts.ContarConElPagoPendienteDesdeAntesDe(Inicio.AddDays(1), token)));
    }

    [Fact]
    public async Task ObtenerVivosDeCuentasConBloqueo_devuelve_solo_los_vivos_de_esas_cuentas_por_id_ascendente()
    {
        var (c1, c2, c3) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await Sembrar(c1);
        await Sembrar(c2);
        await Sembrar(c3);

        var vivos = await bd.Unidad.Ejecutar(token => bd.Checkouts.ObtenerVivosDeCuentasConBloqueo([c1, c2], token));

        Assert.Equal(2, vivos.Count);
        Assert.Equal(vivos.Select(v => v.Id).Order(), vivos.Select(v => v.Id));
        Assert.DoesNotContain(vivos, v => v.CuentaId == c3);
    }

    [Fact]
    public async Task BuscarConEdiciones_devuelve_los_carritos_de_cuenta_y_de_visitante_que_las_tienen_ordenados_por_id()
    {
        var edicion = Guid.NewGuid();
        var deCuenta = CarritoDeCuenta(Guid.NewGuid(), (edicion, false, 1));
        var deVisitante = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeVisitante(Huella("b")), Inicio);
        deVisitante.Agregar(edicion, false, 2, Inicio);
        var sinLaEdicion = CarritoDeCuenta(Guid.NewGuid(), (Guid.NewGuid(), false, 1));
        await bd.Unidad.Ejecutar(async token => { await bd.Carritos.Agregar(deCuenta, token); await bd.Carritos.Agregar(deVisitante, token); await bd.Carritos.Agregar(sinLaEdicion, token); return 0; });

        var encontrados = await bd.Unidad.Ejecutar(token => bd.Carritos.BuscarConEdiciones([edicion], token));

        Assert.Equal(new[] { deCuenta.Id, deVisitante.Id }.Order(), encontrados.Select(e => e.CarritoId));
        Assert.Contains(encontrados, e => e.CuentaId is null);
    }

    // ---------- Eliminación de una cuenta ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-28")]
    [Trait("Criterio", "AC-CAR-29")]
    public async Task Eliminar_una_cuenta_borra_su_checkout_con_el_contacto_su_carrito_sus_avisos_y_las_constancias_de_fusion_sin_tocar_a_otras()
    {
        var (cuenta, otra) = (Guid.NewGuid(), Guid.NewGuid());
        var (carrito, _) = await Sembrar(cuenta, aDomicilio: true);
        await Sembrar(otra, aDomicilio: true);
        await bd.Unidad.Ejecutar(
            async token =>
            {
                await bd.Carritos.RegistrarFusion(Huella("cookie-de-la-cuenta"), carrito.Id, Inicio, token);
                await bd.Carritos.AgregarAvisos(cuenta, [new AvisoDelCarrito(Guid.NewGuid(), TipoDeAvisoDelCarrito.EdicionRetirada, Guid.NewGuid(), "T", null, null, Inicio)], token);
                return 0;
            });

        await bd.Unidad.Ejecutar(
            async token =>
            {
                await bd.Checkouts.EliminarDeCuenta(cuenta, token);
                await bd.Carritos.EliminarDeCuenta(cuenta, token);
                return 0;
            });
        await bd.Unidad.Ejecutar(
            async token =>
            {
                await bd.Checkouts.EliminarDeCuenta(cuenta, token);
                await bd.Carritos.EliminarDeCuenta(cuenta, token);
                return 0;
            });

        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM carrito.checkouts WHERE cuenta_id = '{cuenta}'"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM carrito.carritos WHERE cuenta_id = '{cuenta}'"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM carrito.avisos_de_carrito WHERE cuenta_id = '{cuenta}'"));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM carrito.fusiones_aplicadas"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM carrito.checkouts WHERE cuenta_id = '{otra}'"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM carrito.carritos WHERE cuenta_id = '{otra}'"));
    }

    // ---------- Privacidad del CHECK ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-30")]
    public async Task Un_CHECK_fallido_con_contacto_sembrado_no_deja_nombre_telefono_ni_direccion_en_el_error()
    {
        var cuenta = Guid.NewGuid();
        await Sembrar(cuenta, aDomicilio: true);

        var falla = await Assert.ThrowsAsync<FallaDeLaBaseDelCarritoException>(
            () => bd.AccesoAlEsquema.Ejecutar(
                async (conexion, transaccion, token) =>
                {
                    await using var comando = new Npgsql.NpgsqlCommand("UPDATE carrito.checkouts SET estado = 'cancelado' WHERE cuenta_id = @cuenta", conexion, transaccion);
                    comando.Parameters.AddWithValue("cuenta", cuenta);
                    return await comando.ExecuteNonQueryAsync(token);
                },
                CancellationToken.None));

        Assert.Equal("23514", falla.CodigoDeEstado);
        Assert.Equal("checkouts_contacto_solo_vivo_a_domicilio", falla.Restriccion);
        var todo = falla + " " + falla.Message + " " + falla.Data.Count + " " + falla.InnerException;
        Assert.DoesNotContain("Ana", todo, StringComparison.Ordinal);
        Assert.DoesNotContain("987654321", todo, StringComparison.Ordinal);
        Assert.DoesNotContain("Jr. Lima", todo, StringComparison.Ordinal);
        Assert.Null(falla.InnerException);
    }
}
