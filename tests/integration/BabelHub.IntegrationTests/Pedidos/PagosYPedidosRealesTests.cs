using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.Infrastructure.Carrito;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using BabelHub.IntegrationTests.Pagos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MedioDelContrato = BabelHub.Ecommerce.Application.Pagos.MedioDePago;
using RolesDeUsuario = BabelHub.Shared.Contratos.RolesDeUsuario;

namespace BabelHub.IntegrationTests.Pedidos;

/// <summary>
/// Pedidos con el <b>Pagos real</b> y el Carrito real (ECO-6g, ronda 3; T35; PED-35; AC-PED-27): una compra a domicilio recorre el carrito, el checkout con su contacto, el cobro aprobado del simulador y la creación del pedido real; después
/// el pedido guarda la copia del contacto y Pagos lo libera de la intención. Solo lo externo es doble: el Inventario (stock del Carrito, reservas del pago y reservas del pedido).
/// </summary>
[Collection(ColeccionDelEcommerceCompleto.Nombre)]
public sealed class PagosYPedidosRealesTests(PostgresDelEcommerceCompletoFixture bd) : IAsyncLifetime
{
    private static readonly Guid Obra = Guid.Parse("a0000000-0000-0000-0000-0000000000f2");

    private static readonly Guid EdicionImpresa = Guid.Parse("b0000000-0000-0000-0000-0000000000f2");

    private const decimal PrecioDeLaEdicion = 50m;

    private const string NombreDelContacto = "Rosa Quispe Ficticia";

    private readonly RelojManualDePagos _reloj = new();

    private InventarioSimuladoDelCarrito _inventarioDelCarrito = null!;

    private ReservasSimuladasDelPago _reservasDelPago = null!;

    private readonly List<(Guid Pedido, MotivoDeLiberacion Motivo)> _liberaciones = [];

    public async Task InitializeAsync()
    {
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
        servicios.AddSingleton<IReservasDelPago>(new ReservasConRegistroDeLiberaciones(_reservasDelPago, _liberaciones));
        servicios.RemoveAll<IReservasDelPedido>();
        servicios.AddSingleton<IReservasDelPedido>(new ReservasFalsas());
        servicios.RemoveAll<OpcionesDelCobro>();
        servicios.AddSingleton(new OpcionesDelCobro(TimeSpan.FromMilliseconds(300)));
    });

    private static ActorActual DelCliente(Guid cuenta) => ActorActual.DeUsuario(cuenta, RolesDeUsuario.Cliente, null);

    private async Task<Guid> SembrarCuenta()
    {
        var id = Guid.NewGuid();
        await bd.Ejecutar(
            $"""
            INSERT INTO identidad.versiones_de_textos (id, terminos, privacidad, reembolso) VALUES (1, 'v1', 'v1', 'v1')
              ON CONFLICT (id) DO UPDATE SET terminos = 'v1', privacidad = 'v1', reembolso = 'v1';
            INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, activo, creada_en, acepto_terminos_version, acepto_terminos_en, acepto_privacidad_version, acepto_privacidad_en, acepto_reembolso_version, acepto_reembolso_en)
              VALUES ('{id}', 'Ana Pérez', 'r{id:N}@example.test', true, 'cliente', true, now(), 'v1', now(), 'v1', now(), 'v1', now());
            """);
        return id;
    }

    private static async Task<TextoVistoDeCompra[]> TextosVigentes(AnfitrionRealDelEcommerce anfitrion)
    {
        var terminos = await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<ITextosLegales>().Obtener(TipoDeTextoLegal.Terminos));
        var privacidad = await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<ITextosLegales>().Obtener(TipoDeTextoLegal.Privacidad));
        Assert.True(terminos.EsExito && privacidad.EsExito, "Identidad debe publicar las versiones vigentes de los textos.");
        return [new(TextoLegalDeCompra.Terminos, terminos.Valor.Version), new(TextoLegalDeCompra.Privacidad, privacidad.Valor.Version)];
    }

    private async Task<(Guid Cuenta, Guid Pedido)> ComprarADomicilioHastaElPago(AnfitrionRealDelEcommerce anfitrion)
    {
        var cuenta = await SembrarCuenta();
        var textos = await TextosVigentes(anfitrion);
        Assert.True((await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICarritoDelCliente>().AgregarLinea(new AgregarLineaEntrada(EdicionImpresa, 1)))).EsExito);
        Assert.True((await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICheckoutDelCliente>().Iniciar(new IniciarCheckoutEntrada()))).EsExito);
        var entrega = await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICheckoutDelCliente>().ElegirEntrega(new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Domicilio, null, new ContactoDeCompra(NombreDelContacto, "987654321", "Jr. Ficticio 123, Ayacucho", "ayacucho"))));
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

        Assert.NotEmpty(lineas);
        _reservasDelPago.Sembrar(pedido, _reloj.Ahora + PlazosDelPago.VigenciaConImpresos, [.. lineas.Select(l => new LineaConfirmadaDeReserva(l, "AYA-01", false, null, null))]);
        return (cuenta, pedido);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-27")]
    public async Task Una_compra_a_domicilio_con_el_Pagos_real_crea_el_pedido_con_su_contacto_y_Pagos_libera_el_contacto_de_la_intencion()
    {
        await using var anfitrion = Anfitrion();
        var (cuenta, pedido) = await ComprarADomicilioHastaElPago(anfitrion);
        Assert.Equal(NombreDelContacto, await bd.Consultar<string>("SELECT contacto_nombre FROM pagos.intenciones WHERE id = @p", ("p", pedido)));

        var cobro = await anfitrion.Como(DelCliente(cuenta), async sp =>
        {
            var pagos = sp.GetRequiredService<IPagos>();
            var clave = await pagos.EmitirClaveDeIntento(new EmitirClaveDeIntentoEntrada(pedido));
            Assert.True(clave.EsExito, clave.Error?.Clave);
            var resultado = await pagos.Cobrar(new CobrarEntrada(pedido, clave.Valor.Token, MedioDelContrato.Tarjeta, TokenDelMedioDePago.Crear(EscenariosDelSimulador.Aprobado)));
            Assert.True(resultado.EsExito, resultado.Error?.Clave);
            return resultado.Valor;
        });
        await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<ReanudarComprasAprobadas>().Ejecutar(50));

        Assert.Equal(EstadoDelCobro.Aprobado, cobro.Estado);
        // El pedido real existe, a domicilio, con la copia del contacto (la lee la administración, con su auditoría)...
        var detalle = await anfitrion.Como(
            ActorActual.DeUsuario(Guid.Parse("66666666-6666-6666-6666-666666666666"), RolesDeUsuario.AdministradorCentral, null),
            sp => sp.GetRequiredService<IAdministracionDePedidos>().Obtener(pedido));
        Assert.True(detalle.EsExito, detalle.Error?.Clave);
        Assert.Equal(NombreDelContacto, detalle.Valor.Contacto?.Nombre);
        // La liberación se anotó en la misma transacción que el pedido y la pasada del trabajo pide a Pagos eliminar el contacto de la intención (PED-35, PAG-27): liberado y con su fecha; repetirla no cambia nada.
        Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM pedidos.liberaciones_de_contacto WHERE pedido_id = @p", ("p", pedido)));
        Assert.Equal(NombreDelContacto, await bd.Consultar<string>("SELECT contacto_nombre FROM pagos.intenciones WHERE id = @p", ("p", pedido)));
        for (var pasada = 0; pasada < 2; pasada++)
        {
            var liberadas = await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<LiberarContactoDeIntenciones>().Liberar());
            Assert.True(liberadas.EsExito, liberadas.Error?.Clave);
        }

        Assert.Null(await bd.Consultar<string>("SELECT contacto_nombre FROM pagos.intenciones WHERE id = @p", ("p", pedido)));
        Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM pagos.intenciones WHERE id = @p AND contacto_liberado_en IS NOT NULL", ("p", pedido)));
    }

    private Task<Guid> CobrarAprobado(AnfitrionRealDelEcommerce anfitrion, Guid cuenta, Guid pedido) => CobrarConEscenario(anfitrion, cuenta, pedido, EscenariosDelSimulador.Aprobado);

    private Task<Guid> CobrarConEscenario(AnfitrionRealDelEcommerce anfitrion, Guid cuenta, Guid pedido, string escenario) =>
        anfitrion.Como(DelCliente(cuenta), async sp =>
        {
            var pagos = sp.GetRequiredService<IPagos>();
            var clave = await pagos.EmitirClaveDeIntento(new EmitirClaveDeIntentoEntrada(pedido));
            Assert.True(clave.EsExito, clave.Error?.Clave);
            var resultado = await pagos.Cobrar(new CobrarEntrada(pedido, clave.Valor.Token, MedioDelContrato.Tarjeta, TokenDelMedioDePago.Crear(escenario)));
            Assert.True(resultado.EsExito, resultado.Error?.Clave);
            return pedido;
        });

    private Task Reanudar(AnfitrionRealDelEcommerce anfitrion) =>
        anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<ReanudarComprasAprobadas>().Ejecutar(50));

    [Fact]
    [Trait("Criterio", "AC-PED-30")]
    public async Task Una_compra_a_domicilio_sin_el_contacto_en_la_intencion_no_crea_el_pedido_ni_pide_liberar_y_Pagos_compensa_con_un_solo_reembolso()
    {
        await using var anfitrion = Anfitrion();
        var (cuenta, pedido) = await ComprarADomicilioHastaElPago(anfitrion);
        // La intención pierde su contacto (no se puede guardar la copia): Pedidos no crea el pedido de un domicilio sin contacto (PED-36).
        await bd.Ejecutar($"UPDATE pagos.intenciones SET contacto_nombre = NULL, contacto_telefono = NULL, contacto_direccion = NULL WHERE id = '{pedido:D}'");
        await CobrarAprobado(anfitrion, cuenta, pedido);

        await Reanudar(anfitrion);
        _reloj.Avanzar(TimeSpan.FromMinutes(31));
        await Reanudar(anfitrion);
        await Reanudar(anfitrion);

        Assert.Equal(0, await bd.Consultar<int>("SELECT count(*) FROM pedidos.pedidos WHERE id = @p", ("p", pedido)));
        Assert.Equal(0, await bd.Consultar<int>("SELECT count(*) FROM pedidos.liberaciones_de_contacto WHERE pedido_id = @p", ("p", pedido)));
        Assert.Equal("compensada", await bd.Consultar<string>("SELECT estado FROM pagos.intenciones WHERE id = @p", ("p", pedido)));
        Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM pagos.reembolsos WHERE intencion_id = @p AND estado = 'reembolsado' AND origen = 'compensacion'", ("p", pedido)));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-31")]
    public async Task Una_cuenta_cuya_cancelacion_se_ejecuta_antes_de_que_Pagos_cree_el_pedido_no_deja_pedido_y_el_cobro_se_compensa()
    {
        await using var anfitrion = Anfitrion();
        var (cuenta, pedido) = await ComprarADomicilioHastaElPago(anfitrion);
        await CobrarConEscenario(anfitrion, cuenta, pedido, EscenariosDelSimulador.Lento);
        // El pago queda pendiente de confirmación y, mientras tanto, la cuenta se cancela (su marca de cancelación queda ejecutada): cuando la pasarela confirma, Pedidos no crea el pedido (PED-38) y la compra se compensa.
        await bd.Ejecutar($"UPDATE identidad.usuarios SET cancelada_en = now(), activo = false WHERE id = '{cuenta}'");
        _reloj.Avanzar(TimeSpan.FromMinutes(2));
        await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<ConsultarPagosPendientes>().Ejecutar(50));
        await Reanudar(anfitrion);
        _reloj.Avanzar(TimeSpan.FromMinutes(31));
        await Reanudar(anfitrion);
        await Reanudar(anfitrion);

        Assert.Equal(0, await bd.Consultar<int>("SELECT count(*) FROM pedidos.pedidos WHERE id = @p", ("p", pedido)));
        Assert.Equal("compensada", await bd.Consultar<string>("SELECT estado FROM pagos.intenciones WHERE id = @p", ("p", pedido)));
        Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM pagos.reembolsos WHERE intencion_id = @p AND estado = 'reembolsado' AND origen = 'compensacion'", ("p", pedido)));
    }
}
