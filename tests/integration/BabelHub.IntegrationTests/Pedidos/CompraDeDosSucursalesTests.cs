using System.Net;
using System.Reflection;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Envios;
using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.Infrastructure.Envios;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.IntegrationTests.Reservas;
using BabelHub.Shared.Contratos;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MedioDelContrato = BabelHub.Ecommerce.Application.Pagos.MedioDePago;
using RolesDeUsuario = BabelHub.Shared.Contratos.RolesDeUsuario;

namespace BabelHub.IntegrationTests.Pedidos;

/// <summary>
/// T49 (AC-PED-20, AC-PED-36, AC-PED-38, AC-PED-40, AC-PED-41, AC-PED-42, AC-PAG-46): compras de varias sucursales de punta a punta entre los dos servicios, con <b>Pagos, Pedidos, Envíos y Devoluciones reales</b> sobre PostgreSQL y el <b>Inventario real</b> por su Api (solo el operador logístico es el simulado): se paga con el simulador, cada encargado prepara y entrega solo lo suyo, la anulación devuelve todos los envíos
/// una vez —o, si alguna sucursal ya envió, solo el de las que no— y la devolución de dos líneas de un libro reingresa su suma una sola vez. El Carrito no participa: la reserva se crea en el Inventario con la sucursal de cada línea y la intención de pago, con la instantánea que Carrito le entregaría.
/// </summary>
[Collection(ColeccionDelEcommerceCompleto.Nombre)]
public sealed class CompraDeDosSucursalesTests(PostgresDelEcommerceCompletoFixture bd, PostgresDeReservasFixture inventario) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private const string Ayacucho = "ayacucho";
    private const string Cusco = "cusco";
    private const string Huanta = "huanta";
    private const decimal Precio = 50m;

    private static readonly Guid Administrador = Guid.Parse("49494949-4949-4949-4949-494949494949");

    private static readonly Guid Logistica = Guid.Parse("49494949-4949-4949-4949-494949494950");

    private ApiDeReservasHost _api = null!;
    private AnfitrionRealDelEcommerce _anfitrion = null!;
    private Guid _libro;
    private CheckoutEnPago _checkout = null!;

    public async Task InitializeAsync()
    {
        await inventario.Limpiar();
        _libro = await inventario.AgregarEdicion();
        await inventario.PonerStock(await inventario.AgregarFuente(Ayacucho), _libro, 20, 0);
        await inventario.PonerStock(await inventario.AgregarFuente(Cusco), _libro, 30, 0);
        await inventario.PonerStock(await inventario.AgregarFuente(Huanta), _libro, 10, 0);
        _api = await ApiDeReservasHost.Crear(inventario);
        _api.Reloj.Ahora = DateTimeOffset.UtcNow;
        _anfitrion = bd.Anfitrion(ConectarConElInventario);
        await bd.Ejecutar(
            """
            INSERT INTO identidad.versiones_de_textos (id, terminos, privacidad, reembolso) VALUES (1, 'v1', 'v1', 'v1')
              ON CONFLICT (id) DO UPDATE SET terminos = 'v1', privacidad = 'v1', reembolso = 'v1';
            INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, activo, creada_en, acepto_terminos_version, acepto_terminos_en, acepto_privacidad_version, acepto_privacidad_en, acepto_reembolso_version, acepto_reembolso_en)
              VALUES ('49494949-4949-4949-4949-494949494949', 'Administración de prueba', 'admin-t49@example.test', true, 'administrador-central', true, now(), 'v1', now(), 'v1', now(), 'v1', now()),
                     ('49494949-4949-4949-4949-494949494950', 'Logística de prueba', 'logistica-t49@example.test', true, 'personal-de-logistica', true, now(), 'v1', now(), 'v1', now(), 'v1', now())
              ON CONFLICT DO NOTHING;
            """);
    }

    public async Task DisposeAsync()
    {
        await _anfitrion.DisposeAsync();
        await _api.DisposeAsync();
    }

    /// <summary>El cliente real del Inventario del E-commerce contra la Api del Inventario en el mismo proceso: sus credenciales de salida y, en lugar del token que verificaría Identidad, las cabeceras de actor que solo esa Api de prueba lee.</summary>
    private void ConectarConElInventario(IServiceCollection servicios)
    {
        servicios.RemoveAll<OpcionesDeCredencialesDeSalida>();
        servicios.AddSingleton(new OpcionesDeCredencialesDeSalida(
        [
            Secreto(AlcanceDeServicio.LecturaDeDisponibilidad, ApiDeReservasHost.CredencialDeDisponibilidad),
            Secreto(AlcanceDeServicio.OperacionDeUsuario, ApiDeReservasHost.CredencialDeUsuario),
            Secreto(AlcanceDeServicio.ExpiracionDeReservas, ApiDeReservasHost.CredencialDeExpiracion),
            Secreto(AlcanceDeServicio.ConsultaDePendientes, ApiDeReservasHost.CredencialDePendientes),
            Secreto(AlcanceDeServicio.BajaDeTitular, ApiDeReservasHost.CredencialDeBaja),
        ]));
        // El Carrito no participa: Pagos le pregunta si el checkout sigue vivo y aquí lo está para la compra en curso.
        servicios.RemoveAll<ICheckoutDelCliente>();
        var checkout = DispatchProxy.Create<ICheckoutDelCliente, CheckoutEnPago>();
        _checkout = (CheckoutEnPago)(object)checkout;
        servicios.AddSingleton(checkout);
        servicios.RemoveAll<ITokenDelUsuarioDeLaSesion>();
        servicios.AddSingleton<ITokenDelUsuarioDeLaSesion>(new TokenDePrueba());
        servicios.AddHttpClient(HttpClienteDelInventario.NombreDelCliente)
            .ConfigurePrimaryHttpMessageHandler(() => ((TestServer)_api.Servicios.GetRequiredService<IServer>()).CreateHandler())
            .AddHttpMessageHandler(sp => new ActorEnCabeceras(sp.GetRequiredService<IActorActual>()));
        servicios.RemoveAll<IOperadorLogistico>();
        servicios.AddSingleton<IOperadorLogistico>(new SimuladorOperadorLogistico());
        servicios.RemoveAll<BabelHub.Ecommerce.Application.Pagos.Interno.OpcionesDelCobro>();
        servicios.AddSingleton(new BabelHub.Ecommerce.Application.Pagos.Interno.OpcionesDelCobro(TimeSpan.FromMilliseconds(300)));
    }

    private static KeyValuePair<AlcanceDeServicio, ValorSecreto> Secreto(AlcanceDeServicio alcance, string valor) => new(alcance, new ValorSecreto(valor));

    /// <summary>El checkout de Carrito que Pagos consulta antes de cobrar: solo responde que la compra en curso está en el pago.</summary>
    public class CheckoutEnPago : DispatchProxy
    {
        public Guid Pedido { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(ICheckoutDelCliente.ObtenerParaPagar)
                ? Task.FromResult(Resultado<CheckoutVivoParaPagar>.Exito(new CheckoutVivoParaPagar(Pedido, EstadoVisibleDelCheckout.EnPago)))
                : throw new NotSupportedException();
    }

    private sealed class TokenDePrueba : ITokenDelUsuarioDeLaSesion
    {
        public Task<ValorSecreto?> Actual(CancellationToken cancelacion = default) => Task.FromResult<ValorSecreto?>(new ValorSecreto("token-de-prueba-del-usuario"));
    }

    private sealed class ActorEnCabeceras(IActorActual actores) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Solo las llamadas en nombre de una persona llevan su token (y aquí sus cabeceras); las de sistema se autorizan por su credencial.
            if (request.Headers.Authorization is not null && actores.Actual is { Tipo: TipoDeActor.Usuario, UsuarioId: { } usuario, Rol: { } rol } actor)
            {
                request.Headers.Add(ApiDeReservasHost.CabeceraDeRol, rol);
                request.Headers.Add(ApiDeReservasHost.CabeceraDeUsuario, usuario.ToString());
                if (actor.SucursalId is { } sucursal)
                {
                    request.Headers.Add(ApiDeReservasHost.CabeceraDeSucursal, sucursal.ToString());
                }
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    private static ActorActual DelCliente(Guid cuenta) => ActorActual.DeUsuario(cuenta, RolesDeUsuario.Cliente, null);

    private static ActorActual DelAdministrador() => ActorActual.DeUsuario(Administrador, RolesDeUsuario.AdministradorCentral, null);

    private static ActorActual DelEncargadoDe(string sucursal) => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, IdentificadorDeSucursal.DesdeCodigo(sucursal));

    private Task<T> Como<T>(ActorActual actor, Func<IServiceProvider, Task<T>> bloque) => _anfitrion.Como(actor, bloque);

    private async Task<(int Fisico, int Reservado)> Stock(string sucursal) => (await inventario.StockDe(PostgresDeFuentes(sucursal), _libro))!.Value;

    private static Guid PostgresDeFuentes(string codigo) => IdentificadorDeSucursal.DesdeCodigo(codigo)!.Value;

    private sealed record Compra(Guid Cuenta, Guid Pedido, IReadOnlyList<Guid> Lineas);

    private async Task<Guid> SembrarCuenta()
    {
        var id = Guid.NewGuid();
        await bd.Ejecutar(
            $"""
            INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, activo, creada_en, acepto_terminos_version, acepto_terminos_en, acepto_privacidad_version, acepto_privacidad_en, acepto_reembolso_version, acepto_reembolso_en)
              VALUES ('{id}', 'Ana Pérez', 's{id:N}@example.test', true, 'cliente', true, now(), 'v1', now(), 'v1', now(), 'v1', now());
            """);
        return id;
    }

    private Task<Compra> Comprar(TipoDeEntregaDeCompra entrega, params (string Sucursal, int Cantidad)[] lotes) => Comprar(entrega, _ => 30m, lotes);

    /// <summary>Una compra de lotes de un libro, cada uno de su sucursal (a domicilio, con el envío que cobra cada una): la reserva en el Inventario, la intención de pago, el cobro aprobado del simulador y el pedido creado por Pagos con la reserva confirmada.</summary>
    private async Task<Compra> Comprar(TipoDeEntregaDeCompra entrega, Func<string, decimal> tarifa, params (string Sucursal, int Cantidad)[] lotes)
    {
        var cuenta = await SembrarCuenta();
        var pedido = Guid.NewGuid();
        var lineas = lotes.Select(l => (Id: Guid.NewGuid(), l.Sucursal, l.Cantidad)).ToList();
        var aDomicilio = entrega == TipoDeEntregaDeCompra.Domicilio;

        var reservadas = string.Join(',', lineas.Select(l => $$"""{"lineaId":"{{l.Id}}","edicionId":"{{_libro}}","cantidad":{{l.Cantidad}},"codigoDeSucursal":"{{l.Sucursal}}"}"""));
        var region = aDomicilio ? $"\"regionDeEntrega\":\"{Ayacucho}\"," : string.Empty;
        var reserva = await _api.Enviar(
            HttpMethod.Post, "/reservas", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, usuario: cuenta, clave: Guid.NewGuid().ToString("N"),
            json: $$"""{"pedidoId":"{{pedido}}","tipoDeEntrega":"{{(aDomicilio ? "domicilio" : "recojo")}}",{{region}}"lineas":[{{reservadas}}]}""");
        Assert.True(reserva.StatusCode == HttpStatusCode.Created, await reserva.Content.ReadAsStringAsync());

        var ahora = DateTimeOffset.UtcNow;
        var subtotal = lineas.Sum(l => l.Cantidad) * Precio;
        var sucursales = lineas.Select(l => l.Sucursal).Distinct().Order(StringComparer.Ordinal).ToList();
        var envio = aDomicilio ? sucursales.Sum(tarifa) : 0m;
        var instantanea = new InstantaneaDeCompra(
            pedido, cuenta, entrega, null, aDomicilio ? Ayacucho : null, subtotal, envio, subtotal + envio,
            aDomicilio ? new ContactoDeCompra("Rosa Quispe Ficticia", "987654321", "Jr. Ficticio 123, Ayacucho", Ayacucho) : null,
            [new(TextoLegalDeCompra.Terminos, "v1", ahora), new(TextoLegalDeCompra.Privacidad, "v1", ahora)],
            [.. lineas.Select(l => new LineaDeInstantanea(l.Id, _libro, false, "Ficciones", null, l.Cantidad, Precio, l.Cantidad * Precio, l.Sucursal, false, null))],
            "huella-" + pedido.ToString("N"),
            aDomicilio ? [.. sucursales.Select(s => new EnvioDeSucursalDeCompra(s, s, tarifa(s)))] : null);
        var intencion = await Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICreadorDeIntencionesDePago>().Crear(cuenta, instantanea, instantanea.HuellaDeInstantanea, ahora + PlazosDelPago.VigenciaConImpresos));
        Assert.Equal(ResultadoDeCrearIntencion.Creada, intencion);

        _checkout.Pedido = pedido;
        await Como(DelCliente(cuenta), async sp =>
        {
            var pagos = sp.GetRequiredService<IPagos>();
            var clave = await pagos.EmitirClaveDeIntento(new EmitirClaveDeIntentoEntrada(pedido));
            Assert.True(clave.EsExito, clave.Error?.Clave);
            var cobro = await pagos.Cobrar(new CobrarEntrada(pedido, clave.Valor.Token, MedioDelContrato.Tarjeta, TokenDelMedioDePago.Crear(EscenariosDelSimulador.Aprobado)));
            Assert.True(cobro.EsExito, cobro.Error?.Clave);
            return true;
        });
        await Como(ActorActual.DelSistema(), async sp =>
        {
            await sp.GetRequiredService<ReanudarComprasAprobadas>().Ejecutar(50);
            return true;
        });

        Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM pedidos.pedidos WHERE id = @p", ("p", pedido)));
        return new Compra(cuenta, pedido, [.. lineas.Select(l => l.Id)]);
    }

    /// <summary>El encargado prepara, marca listas y entrega sus líneas.</summary>
    private async Task Entregar(string sucursal, Guid pedido, params Guid[] lineas)
    {
        var encargado = DelEncargadoDe(sucursal);
        var entrada = new OperarLineasEntrada(pedido, lineas);
        var preparadas = await Como(encargado, sp => sp.GetRequiredService<IGestionDeRecojo>().Preparar(sucursal, entrada));
        Assert.True(preparadas.EsExito, preparadas.Error?.Clave);
        var listas = await Como(encargado, sp => sp.GetRequiredService<IGestionDeRecojo>().MarcarListas(sucursal, entrada));
        Assert.True(listas.EsExito, listas.Error?.Clave);
        var entregadas = await Como(encargado, sp => sp.GetRequiredService<IGestionDeRecojo>().Entregar(sucursal, entrada));
        Assert.True(entregadas.EsExito, entregadas.Error?.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-36")]
    [Trait("Criterio", "AC-PED-38")]
    public async Task En_un_recojo_de_dos_sucursales_cada_encargado_prepara_y_entrega_solo_lo_suyo_contra_el_stock_de_su_sucursal()
    {
        var compra = await Comprar(TipoDeEntregaDeCompra.Recojo, (Cusco, 18), (Ayacucho, 6), (Ayacucho, 2));
        var (deCusco, deAyacucho, otroDeAyacucho) = (compra.Lineas[0], compra.Lineas[1], compra.Lineas[2]);
        Assert.Equal((20, 8), await Stock(Ayacucho));
        Assert.Equal((30, 18), await Stock(Cusco));
        Assert.Equal(3, await bd.Consultar<int>("SELECT count(*) FROM pedidos.lineas_de_pedido WHERE pedido_id = @p AND codigo_de_fuente IS NOT NULL", ("p", compra.Pedido)));

        var encargado = DelEncargadoDe(Ayacucho);
        var suyo = await Como(encargado, sp => sp.GetRequiredService<IGestionDeRecojo>().Obtener(Ayacucho, compra.Pedido));
        var ajeno = await Como(encargado, sp => sp.GetRequiredService<IGestionDeRecojo>().Preparar(Ayacucho, new OperarLineasEntrada(compra.Pedido, [deAyacucho, deCusco])));

        Assert.True(suyo.EsExito, suyo.Error?.Clave);
        Assert.Equal(new[] { deAyacucho, otroDeAyacucho }.Order(), suyo.Valor.Lineas.Select(l => l.LineaId).Order());
        Assert.Equal((400m, 2), (suyo.Valor.Resumen.Total, suyo.Valor.Resumen.CantidadDeLineas));
        Assert.Equal("no-encontrado", ajeno.Error!.Clave);

        await Entregar(Ayacucho, compra.Pedido, deAyacucho, otroDeAyacucho);

        Assert.Equal((12, 0), await Stock(Ayacucho));
        Assert.Equal((30, 18), await Stock(Cusco));
        Assert.Equal("confirmado", await bd.Consultar<string>("SELECT estado FROM pedidos.pedidos WHERE id = @p", ("p", compra.Pedido)));

        await Entregar(Cusco, compra.Pedido, deCusco);

        Assert.Equal((12, 0), await Stock(Cusco));
        Assert.Equal("entregado", await bd.Consultar<string>("SELECT estado FROM pedidos.pedidos WHERE id = @p", ("p", compra.Pedido)));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-40")]
    public async Task Anular_un_pedido_a_domicilio_de_dos_sucursales_sin_nada_salido_libera_ambas_y_reembolsa_el_total_con_los_dos_envios_una_vez()
    {
        var compra = await Comprar(TipoDeEntregaDeCompra.Domicilio, (Ayacucho, 2), (Cusco, 3));
        Assert.Equal((20, 2), await Stock(Ayacucho));
        Assert.Equal((30, 3), await Stock(Cusco));

        var anulado = await Como(DelAdministrador(), sp => sp.GetRequiredService<IAdministracionDePedidos>().Anular(new AnularPedidoEntrada(compra.Pedido, "otro")));
        var repetido = await Como(DelAdministrador(), sp => sp.GetRequiredService<IAdministracionDePedidos>().Anular(new AnularPedidoEntrada(compra.Pedido, "otro")));

        Assert.True(anulado.EsExito, anulado.Error?.Clave);
        Assert.Equal((310m, false), (anulado.Valor.Reembolso, anulado.Valor.ReembolsoPendiente));
        Assert.False(repetido.EsExito);
        Assert.Equal((20, 0), await Stock(Ayacucho));
        Assert.Equal((30, 0), await Stock(Cusco));
        Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM pagos.reembolsos WHERE intencion_id = @p AND estado = 'reembolsado'", ("p", compra.Pedido)));
        Assert.Equal((310m, 60m), (
            await bd.Consultar<decimal>("SELECT monto FROM pagos.reembolsos WHERE intencion_id = @p", ("p", compra.Pedido)),
            await bd.Consultar<decimal>("SELECT envio FROM pagos.reembolsos WHERE intencion_id = @p", ("p", compra.Pedido))));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-41")]
    public async Task Devolver_unidades_de_dos_lineas_de_un_libro_reingresa_su_suma_una_vez_en_la_receptora_y_reembolsa_cada_linea()
    {
        var compra = await Comprar(TipoDeEntregaDeCompra.Recojo, (Cusco, 6), (Ayacucho, 4));
        var (deCusco, deAyacucho) = (compra.Lineas[0], compra.Lineas[1]);
        await Entregar(Cusco, compra.Pedido, deCusco);
        await Entregar(Ayacucho, compra.Pedido, deAyacucho);
        Assert.Equal((16, 0), await Stock(Ayacucho));

        var solicitada = await Como(DelCliente(compra.Cuenta), sp => sp.GetRequiredService<IDevolucionesDelCliente>().SolicitarImpreso(new SolicitarDevolucionDeImpresoEntrada(
            compra.Pedido, [new LineaPedidaParaDevolver(deCusco, 5), new LineaPedidaParaDevolver(deAyacucho, 2)], new TextoLibre("Llegaron dañados"), Ayacucho)));
        Assert.True(solicitada.EsExito, solicitada.Error?.Clave);
        var devolucion = solicitada.Valor.DevolucionId;
        var aprobada = await Como(DelAdministrador(), sp => sp.GetRequiredService<IGestionDeDevoluciones>().Aprobar(devolucion));
        Assert.True(aprobada.EsExito, aprobada.Error?.Clave);

        var recibida = await Como(DelAdministrador(), sp => sp.GetRequiredService<IGestionDeDevoluciones>().ConfirmarRecepcion(devolucion));
        await Como(DelAdministrador(), sp => sp.GetRequiredService<IGestionDeDevoluciones>().ConfirmarRecepcion(devolucion));

        Assert.True(recibida.EsExito, recibida.Error?.Clave);
        Assert.Equal((23, 0), await Stock(Ayacucho));
        Assert.Equal((24, 0), await Stock(Cusco));
        Assert.Equal(2, await bd.Consultar<int>("SELECT count(*) FROM pagos.reembolsos WHERE intencion_id = @p AND estado = 'reembolsado' AND origen = 'devolucion'", ("p", compra.Pedido)));
        Assert.Equal(350m, await bd.Consultar<decimal>("SELECT sum(monto) FROM pagos.reembolsos WHERE intencion_id = @p", ("p", compra.Pedido)));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-20")]
    [Trait("Criterio", "AC-PED-42")]
    [Trait("Criterio", "AC-PAG-46")]
    public async Task Anular_con_una_sucursal_en_reparto_devuelve_las_demas_lineas_y_el_envio_solo_de_las_que_no_enviaron()
    {
        var compra = await Comprar(TipoDeEntregaDeCompra.Domicilio, s => s == Ayacucho ? 30m : s == Cusco ? 18.50m : 25m, (Ayacucho, 1), (Cusco, 2), (Cusco, 3), (Huanta, 1));
        var (deAyacucho, enReparto) = (compra.Lineas[0], compra.Lineas[1]);
        var logistica = ActorActual.DeUsuario(Logistica, RolesDeUsuario.PersonalDeLogistica, null);
        var aPreparar = new OperarLineasEntrada(compra.Pedido, [deAyacucho, enReparto]);
        var preparadas = await Como(logistica, sp => sp.GetRequiredService<IGestionDeDespacho>().Preparar(aPreparar));
        Assert.True(preparadas.EsExito, preparadas.Error?.Clave);
        var listas = await Como(logistica, sp => sp.GetRequiredService<IGestionDeDespacho>().MarcarListas(aPreparar));
        Assert.True(listas.EsExito, listas.Error?.Clave);
        var envioDeCusco = await bd.Consultar<string>("SELECT envio_id::text FROM pedidos.lineas_de_pedido WHERE id = @p", ("p", enReparto));
        var deCusco = Guid.Parse(envioDeCusco!);
        var guia = await Como(logistica, sp => sp.GetRequiredService<IEnviosDeLogistica>().SolicitarGuia(deCusco));
        Assert.True(guia.EsExito, guia.Error?.Clave);
        var salida = await Como(logistica, sp => sp.GetRequiredService<IEnviosDeLogistica>().PasarAEnReparto(deCusco));
        Assert.True(salida.EsExito, salida.Error?.Clave);
        Assert.Equal((28, 3), await Stock(Cusco));

        var anulado = await Como(DelAdministrador(), sp => sp.GetRequiredService<IAdministracionDePedidos>().Anular(new AnularPedidoEntrada(compra.Pedido, "otro")));

        // Líneas anuladas: Ayacucho 50, Cusco 150 y Huanta 50; envío de Ayacucho (30) y Huanta (25), sin los 18,50 de Cusco.
        Assert.True(anulado.EsExito, anulado.Error?.Clave);
        Assert.Equal((305m, false), (anulado.Valor.Reembolso, anulado.Valor.ReembolsoPendiente));
        Assert.Equal(55m, await bd.Consultar<decimal>("SELECT envio FROM pagos.reembolsos WHERE intencion_id = @p", ("p", compra.Pedido)));
        Assert.Equal(55m, await bd.Consultar<decimal>("SELECT envio FROM pedidos.anulaciones WHERE pedido_id = @p", ("p", compra.Pedido)));
        Assert.Equal((20, 0), await Stock(Ayacucho));
        Assert.Equal((28, 0), await Stock(Cusco));
        Assert.Equal((10, 0), await Stock(Huanta));
        Assert.Equal("despachado", await bd.Consultar<string>("SELECT estado FROM pedidos.pedidos WHERE id = @p", ("p", compra.Pedido)));
        Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM envios.envios WHERE pedido_id = @p AND estado = 'cancelado'", ("p", compra.Pedido)));

        // Pagos no admite un envío acumulado mayor que el cobrado (73,50): quedan 18,50.
        var deMas = await Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<IReembolsos>().Registrar(
            new RegistrarReembolsoEntrada(compra.Pedido, OrigenDelReembolso.Devolucion, "dev:" + Guid.NewGuid().ToString("N"), [new LineaAReembolsar(enReparto, 100m)], 18.51m)));

        Assert.False(deMas.EsExito);
        Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM pagos.reembolsos WHERE intencion_id = @p", ("p", compra.Pedido)));
    }
}
