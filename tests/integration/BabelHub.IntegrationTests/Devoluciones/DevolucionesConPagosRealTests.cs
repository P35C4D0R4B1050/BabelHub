using BabelHub.Ecommerce.Infrastructure.Carrito;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Devoluciones.Interno;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using BabelHub.IntegrationTests.Pagos;
using BabelHub.IntegrationTests.Pedidos;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MedioDelContrato = BabelHub.Ecommerce.Application.Pagos.MedioDePago;

namespace BabelHub.IntegrationTests.Devoluciones;

/// <summary>
/// Devoluciones con el <b>Pagos real</b> (DEV-08, DEV-17, DEV-21): una compra a domicilio recorre el carrito, el checkout, el cobro del simulador de la pasarela y el pedido real; el impreso se devuelve por la Aplicación real de Devoluciones y el reembolso lo registra,
/// topa y procesa el <c>IReembolsos</c> real de Pagos (el tope por línea, el envío que no se reembolsa y la pasarela que aplica sin responder). Solo lo externo es doble: el Inventario (carrito, reservas y reingreso).
/// </summary>
[Collection(ColeccionDelEcommerceCompleto.Nombre)]
public sealed class DevolucionesConPagosRealTests(PostgresDelEcommerceCompletoFixture bd) : IAsyncLifetime
{
    private static readonly Guid Obra = Guid.Parse("a0000000-0000-0000-0000-0000000000f2");

    private static readonly Guid EdicionImpresa = Guid.Parse("b0000000-0000-0000-0000-0000000000f2");

    private const string SucursalReceptora = "t30-lima";

    private static readonly Guid Encargado = Guid.Parse("55555555-5555-5555-5555-555555555530");

    private readonly RelojManualDePagos _reloj = new();

    private InventarioSimuladoDelCarrito _inventarioDelCarrito = null!;

    private ReservasSimuladasDelPago _reservasDelPago = null!;

    private readonly InventarioSimulado _inventario = new();

    public async Task InitializeAsync()
    {
        _inventario.Sucursales.Add(SucursalReceptora);
        _inventarioDelCarrito = new InventarioSimuladoDelCarrito(_reloj);
        _inventarioDelCarrito.FijarStock(EdicionImpresa, 1000);
        _reservasDelPago = new ReservasSimuladasDelPago(_reloj);
        await bd.Ejecutar(
            $"""
            BEGIN;
            INSERT INTO catalogo.obras (id, titulo, autor, editorial, categoria, portada_clave, portada_huella, portada_tipo, estado, version, creada_en)
              VALUES ('{Obra}', 'Conversación en La Catedral (prueba)', 'Autor de prueba', 'Editorial de prueba', 'Narrativa', 'e2e0000000000000000000000000f2f2', decode(repeat('f2', 32), 'hex'), 'jpeg', 'activa', 1, now())
              ON CONFLICT DO NOTHING;
            INSERT INTO catalogo.ediciones (id, obra_id, tipo, isbn13, isbn_ingresado, precio, estado)
              VALUES ('{EdicionImpresa}', '{Obra}', 'impresa', '9786123450120', '9786123450120', 50.00, 'activa')
              ON CONFLICT DO NOTHING;
            INSERT INTO identidad.versiones_de_textos (id, terminos, privacidad, reembolso) VALUES (1, 'v1', 'v1', 'v1')
              ON CONFLICT (id) DO UPDATE SET terminos = 'v1', privacidad = 'v1', reembolso = 'v1';
            INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, activo, creada_en, sucursal, acepto_terminos_version, acepto_terminos_en, acepto_privacidad_version, acepto_privacidad_en, acepto_reembolso_version, acepto_reembolso_en)
              VALUES ('{Encargado}', 'Encargado de prueba', 'encargado-t30@example.test', true, 'encargado-de-sucursal', true, now(), '{IdentificadorDeSucursal.DesdeCodigo(SucursalReceptora)}', 'v1', now(), 'v1', now(), 'v1', now())
              ON CONFLICT DO NOTHING;
            COMMIT;
            """);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AnfitrionRealDelEcommerce Anfitrion() => bd.Anfitrion(servicios =>
    {
        servicios.RemoveAll<IReloj>();
        servicios.AddSingleton<IReloj>(_reloj);
        servicios.RemoveAll<IInventarioDelCarrito>();
        servicios.AddSingleton<IInventarioDelCarrito>(_inventarioDelCarrito);
        servicios.RemoveAll<IReservasDelPago>();
        servicios.AddSingleton<IReservasDelPago>(_reservasDelPago);
        servicios.RemoveAll<IReservasDelPedido>();
        servicios.AddSingleton<IReservasDelPedido>(new ReservasFalsas());
        servicios.RemoveAll<IInventarioDeDevoluciones>();
        servicios.AddSingleton<IInventarioDeDevoluciones>(_inventario);
        servicios.RemoveAll<OpcionesDelCobro>();
        servicios.AddSingleton(new OpcionesDelCobro(TimeSpan.FromMilliseconds(300)));
    });

    private static ActorActual DelCliente(Guid cuenta) => ActorActual.DeUsuario(cuenta, RolesDeUsuario.Cliente, null);

    private static ActorActual DelEncargado() => ActorActual.DeUsuario(Encargado, RolesDeUsuario.EncargadoDeSucursal, IdentificadorDeSucursal.DesdeCodigo(SucursalReceptora));

    private async Task<Guid> SembrarCuenta()
    {
        var id = Guid.NewGuid();
        await bd.Ejecutar(
            $"""
            INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, activo, creada_en, acepto_terminos_version, acepto_terminos_en, acepto_privacidad_version, acepto_privacidad_en, acepto_reembolso_version, acepto_reembolso_en)
              VALUES ('{id}', 'Ana Pérez', 'd{id:N}@example.test', true, 'cliente', true, now(), 'v1', now(), 'v1', now(), 'v1', now());
            """);
        return id;
    }

    /// <summary>La compra a domicilio de un impreso (50 más el envío), cobrada con el escenario del simulador y con el pedido real creado; el impreso queda entregado hace tres días. Devuelve el cobrado total y el envío.</summary>
    private async Task<(Guid Cuenta, Guid Pedido, Guid Linea, decimal Total, decimal Envio)> ComprarYEntregar(AnfitrionRealDelEcommerce anfitrion, string escenario)
    {
        var cuenta = await SembrarCuenta();
        var terminos = await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<ITextosLegales>().Obtener(TipoDeTextoLegal.Terminos));
        var privacidad = await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<ITextosLegales>().Obtener(TipoDeTextoLegal.Privacidad));
        Assert.True(terminos.EsExito && privacidad.EsExito);
        TextoVistoDeCompra[] textos = [new(TextoLegalDeCompra.Terminos, terminos.Valor.Version), new(TextoLegalDeCompra.Privacidad, privacidad.Valor.Version)];
        Assert.True((await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICarritoDelCliente>().AgregarLinea(new AgregarLineaEntrada(EdicionImpresa, 1)))).EsExito);
        Assert.True((await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICheckoutDelCliente>().Iniciar(new IniciarCheckoutEntrada()))).EsExito);
        var entrega = await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICheckoutDelCliente>().ElegirEntrega(new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Domicilio, null, new ContactoDeCompra("Rosa Quispe Ficticia", "987654321", "Jr. Ficticio 123, Ayacucho", "ayacucho"))));
        Assert.True(entrega.EsExito, entrega.Error?.Clave);
        Assert.True((await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICheckoutDelCliente>().RegistrarAceptaciones(new RegistrarAceptacionesEntrada(textos)))).EsExito);
        var total = entrega.Valor.Total;
        var pasar = await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICheckoutDelCliente>().PasarAlPago(new PasarAlPagoEntrada(total, textos)));
        Assert.True(pasar.EsExito, pasar.Error?.Clave);
        var pedido = pasar.Valor.PedidoId ?? throw new InvalidOperationException("Pasar al pago no devolvió el pedido.");

        var lineas = new List<Guid>();
        await using (var comando = bd.Administracion.CreateCommand("SELECT linea_id FROM pagos.intencion_lineas WHERE intencion_id = @p"))
        {
            comando.Parameters.AddWithValue("p", pedido);
            await using var lector = await comando.ExecuteReaderAsync();
            while (await lector.ReadAsync())
            {
                lineas.Add(lector.GetGuid(0));
            }
        }

        _reservasDelPago.Sembrar(pedido, _reloj.Ahora + PlazosDelPago.VigenciaConImpresos, [.. lineas.Select(l => new LineaConfirmadaDeReserva(l, "AYA-01", false, null, null))]);
        await anfitrion.Como(DelCliente(cuenta), async sp =>
        {
            var pagos = sp.GetRequiredService<IPagos>();
            var clave = await pagos.EmitirClaveDeIntento(new EmitirClaveDeIntentoEntrada(pedido));
            Assert.True(clave.EsExito, clave.Error?.Clave);
            var cobro = await pagos.Cobrar(new CobrarEntrada(pedido, clave.Valor.Token, MedioDelContrato.Tarjeta, TokenDelMedioDePago.Crear(escenario)));
            Assert.True(cobro.EsExito, cobro.Error?.Clave);
            return cobro.Valor;
        });
        await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<ReanudarComprasAprobadas>().Ejecutar(50));
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM pedidos.pedidos WHERE id = @p", ("p", pedido)));

        var linea = await bd.Consultar<Guid>("SELECT id FROM pedidos.lineas_de_pedido WHERE pedido_id = @p", ("p", pedido));
        await bd.Ejecutar($"UPDATE pedidos.lineas_de_pedido SET estado = 'entregado', entregada_en = '{_reloj.Ahora.AddDays(-3):O}' WHERE id = '{linea:D}'");
        var envio = total - 50m;
        return (cuenta, pedido, linea, total, envio);
    }

    private Task<Resultado<DevolucionVista>> Solicitar(AnfitrionRealDelEcommerce anfitrion, Guid cuenta, Guid pedido, Guid linea) =>
        anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<IDevolucionesDelCliente>().SolicitarImpreso(new SolicitarDevolucionDeImpresoEntrada(pedido, [new LineaPedidaParaDevolver(linea, 1)], new TextoLibre("No era el libro que buscaba"), SucursalReceptora)));

    private async Task<Guid> SolicitarYAprobar(AnfitrionRealDelEcommerce anfitrion, Guid cuenta, Guid pedido, Guid linea)
    {
        var solicitada = await Solicitar(anfitrion, cuenta, pedido, linea);
        Assert.True(solicitada.EsExito, solicitada.Error?.Clave);
        var aprobada = await anfitrion.Como(DelEncargado(), sp => sp.GetRequiredService<IGestionDeDevoluciones>().Aprobar(solicitada.Valor.DevolucionId));
        Assert.True(aprobada.EsExito, aprobada.Error?.Clave);
        return solicitada.Valor.DevolucionId;
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-08")]
    public async Task El_reembolso_del_impreso_lo_registra_y_aplica_el_Pagos_real_por_el_precio_sin_el_envio_y_nunca_supera_lo_cobrado()
    {
        await using var anfitrion = Anfitrion();
        var (cuenta, pedido, linea, total, envio) = await ComprarYEntregar(anfitrion, EscenariosDelSimulador.Aprobado);
        Assert.True(envio > 0m, "La compra a domicilio debe llevar un costo de envío.");
        var devolucionId = await SolicitarYAprobar(anfitrion, cuenta, pedido, linea);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM pagos.reembolsos WHERE intencion_id = @p AND origen = 'devolucion'", ("p", pedido)));

        var recibida = await anfitrion.Como(DelEncargado(), sp => sp.GetRequiredService<IGestionDeDevoluciones>().ConfirmarRecepcion(devolucionId));

        Assert.True(recibida.EsExito, recibida.Error?.Clave);
        Assert.Equal(50m, await bd.Consultar<decimal>("SELECT monto FROM pagos.reembolsos WHERE intencion_id = @p AND origen = 'devolucion'", ("p", pedido)));
        Assert.Equal(0m, await bd.Consultar<decimal>("SELECT envio FROM pagos.reembolsos WHERE intencion_id = @p AND origen = 'devolucion'", ("p", pedido)));
        Assert.Equal("reembolsado", await bd.Consultar<string>("SELECT estado FROM pagos.reembolsos WHERE intencion_id = @p AND origen = 'devolucion'", ("p", pedido)));
        Assert.Equal("reembolsada", await bd.Consultar<string>("SELECT l.estado FROM devoluciones.lineas_de_devolucion l JOIN devoluciones.devoluciones d ON d.id = l.devolucion_id WHERE d.pedido_id = @p", ("p", pedido)));

        // El tope de Pagos: la misma línea ya reembolsada no admite otro reembolso, ni siquiera de un solo sol, y lo reembolsado jamás pasa de lo cobrado.
        var segundo = await anfitrion.Como(
            ActorActual.DelSistema(),
            sp => sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token => await sp.GetRequiredService<IReembolsos>().Registrar(new RegistrarReembolsoEntrada(pedido, OrigenDelReembolso.Devolucion, "otra-clave-de-prueba", [new LineaAReembolsar(linea, 1m)], 0m), token)));
        Assert.False(segundo.EsExito);
        Assert.True(await bd.Consultar<decimal>("SELECT coalesce(sum(monto + envio), 0) FROM pagos.reembolsos WHERE intencion_id = @p", ("p", pedido)) <= total);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-20")]
    public async Task Con_la_pasarela_que_aplica_sin_responder_el_reembolso_queda_pendiente_y_la_conciliacion_lo_resuelve_con_un_solo_reembolso_en_la_pasarela()
    {
        await using var anfitrion = Anfitrion();
        var (cuenta, pedido, linea, _, _) = await ComprarYEntregar(anfitrion, EscenariosDelSimulador.ReembolsoSinRespuesta);
        var devolucionId = await SolicitarYAprobar(anfitrion, cuenta, pedido, linea);

        var recibida = await anfitrion.Como(DelEncargado(), sp => sp.GetRequiredService<IGestionDeDevoluciones>().ConfirmarRecepcion(devolucionId));

        Assert.True(recibida.EsExito, recibida.Error?.Clave);
        Assert.Equal("recibida", await bd.Consultar<string>("SELECT l.estado FROM devoluciones.lineas_de_devolucion l JOIN devoluciones.devoluciones d ON d.id = l.devolucion_id WHERE d.pedido_id = @p", ("p", pedido)));
        Assert.Equal("pendiente", await bd.Consultar<string>("SELECT estado FROM pagos.reembolsos WHERE intencion_id = @p AND origen = 'devolucion'", ("p", pedido)));
        var visible = await anfitrion.Como(DelEncargado(), sp => sp.GetRequiredService<IGestionDeDevoluciones>().Obtener(devolucionId));
        Assert.True(visible.Valor.Lineas.Single().ReembolsoPendiente);

        for (var pasada = 0; pasada < 3; pasada++)
        {
            // Pagos respeta una espera tras un «sin respuesta» antes de volver a procesar.
            _reloj.Avanzar(TimeSpan.FromHours(1));
            var conciliada = await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<ConciliarReembolsosDeDevoluciones>().Ejecutar(100));
            Assert.True(conciliada.EsExito, conciliada.Error?.Clave);
        }

        // La pasarela aplicó el reembolso una sola vez: antes de repetirlo, Pagos lo consulta.
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM pagos.reembolsos WHERE intencion_id = @p AND origen = 'devolucion'", ("p", pedido)));
        var referencia = await bd.Consultar<string>("SELECT referencia_pasarela FROM pagos.intentos WHERE intencion_id = @p AND estado = 'aprobado'", ("p", pedido));
        var enLaPasarela = await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<IPasarelaDePago>().ConsultarReembolsos(referencia!));
        Assert.Single(enLaPasarela!);
        Assert.Equal("reembolsado", await bd.Consultar<string>("SELECT estado FROM pagos.reembolsos WHERE intencion_id = @p AND origen = 'devolucion'", ("p", pedido)));
        Assert.Equal("reembolsada", await bd.Consultar<string>("SELECT l.estado FROM devoluciones.lineas_de_devolucion l JOIN devoluciones.devoluciones d ON d.id = l.devolucion_id WHERE d.pedido_id = @p", ("p", pedido)));
    }
}
