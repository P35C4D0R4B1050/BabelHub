using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.Infrastructure.Carrito;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using BabelHub.IntegrationTests.Pedidos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MedioDelContrato = BabelHub.Ecommerce.Application.Pagos.MedioDePago;
using RolesDeUsuario = BabelHub.Shared.Contratos.RolesDeUsuario;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>
/// T31 de <c>ecommerce-pagos</c> con el <b>Carrito real</b> (AC-PAG-04, AC-PAG-13, AC-PAG-17, AC-PAG-20, AC-PAG-22, AC-PAG-25; PAG-28): el anfitrión real del E-commerce sobre PostgreSQL con todos los esquemas migrados
/// (catálogo, identidad, carrito, pagos y auditoría reales; el cobro es el simulador). El checkout se recorre por los contratos públicos de Carrito (<see cref="ICarritoDelCliente"/> y <see cref="ICheckoutDelCliente"/>) hasta
/// pasar al pago, que crea la intención real de Pagos; los avisos de Pagos llegan al <see cref="IProgresoDelCheckout"/> real. Solo lo externo es doble: el reloj (manual), el Inventario (el simulado de Carrito para reservar y el
/// simulado de Pagos para consultar, extender, confirmar y liberar la reserva) y Pedidos (que crea el pedido o falla a voluntad). Cubre lo que los dobles de Carrito no podían: el estado real del checkout y del carrito tras cada
/// aviso, los avisos reintentables y las carreras entre cerrar las intenciones vencidas y volver al carrito.
/// </summary>
[Collection(ColeccionDelEcommerceCompleto.Nombre)]
public sealed class PagosConElCarritoRealTests(PostgresDelEcommerceCompletoFixture bd) : IAsyncLifetime
{
    private static readonly Guid Obra = Guid.Parse("a0000000-0000-0000-0000-0000000000f1");

    private static readonly Guid EdicionImpresa = Guid.Parse("b0000000-0000-0000-0000-0000000000f1");

    private const decimal PrecioDeLaEdicion = 50m;

    private readonly RelojManualDePagos _reloj = new();

    private readonly ConfirmacionDeCompraDePrueba _pedidos = new();

    private readonly ProgresoConFallas _fallasDeCarrito = new();

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
              VALUES ('{Obra}', 'La ciudad y los perros (prueba)', 'Autor de prueba', 'Editorial de prueba', 'Narrativa', 'e2e0000000000000000000000000f1f1', decode(repeat('f1', 32), 'hex'), 'jpeg', 'activa', 1, now())
              ON CONFLICT DO NOTHING;
            INSERT INTO catalogo.ediciones (id, obra_id, tipo, isbn13, isbn_ingresado, precio, estado)
              VALUES ('{EdicionImpresa}', '{Obra}', 'impresa', '9786123450113', '9786123450113', {PrecioDeLaEdicion.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 'activa')
              ON CONFLICT DO NOTHING;
            COMMIT;
            """);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AnfitrionRealDelEcommerce Anfitrion(string? pasarela = null) => bd.Anfitrion(servicios =>
    {
        servicios.RemoveAll<IReloj>();
        servicios.AddSingleton<IReloj>(_reloj);
        servicios.RemoveAll<IInventarioDelCarrito>();
        servicios.AddSingleton<IInventarioDelCarrito>(_inventarioDelCarrito);
        servicios.RemoveAll<IReservasDelPago>();
        servicios.AddSingleton<IReservasDelPago>(new ReservasConRegistroDeLiberaciones(_reservasDelPago, _liberaciones));
        servicios.RemoveAll<IConfirmacionDeCompra>();
        servicios.AddSingleton<IConfirmacionDeCompra>(_pedidos);
        // Sin pasarela se dejan las opciones que compone el anfitrión (el cobro cerrado): sustituirlas lo reabriría.
        if (pasarela is null)
        {
            servicios.RemoveAll<OpcionesDelCobro>();
            servicios.AddSingleton(new OpcionesDelCobro(TimeSpan.FromMilliseconds(300)));
        }

        // El Carrito real recibe los avisos de Pagos a través de un envoltorio que puede fallar a voluntad (los avisos son reintentables).
        var original = servicios.Last(d => d.ServiceType == typeof(IProgresoDelCheckout));
        servicios.Remove(original);
        servicios.AddScoped<IProgresoDelCheckout>(sp =>
        {
            var interior = original.ImplementationFactory is { } fabrica
                ? (IProgresoDelCheckout)fabrica(sp)
                : (IProgresoDelCheckout)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!);
            return new ProgresoConElCarritoReal(interior, _fallasDeCarrito);
        });
    }, pasarela);

    private static ActorActual DelCliente(Guid cuenta) => ActorActual.DeUsuario(cuenta, RolesDeUsuario.Cliente, null);

    /// <summary>Una cuenta de cliente real de Identidad (activa, con el correo verificado y los tres textos vigentes aceptados).</summary>
    private async Task<Guid> SembrarCuenta()
    {
        var id = Guid.NewGuid();
        await bd.Ejecutar(
            $"""
            INSERT INTO identidad.versiones_de_textos (id, terminos, privacidad, reembolso) VALUES (1, 'v1', 'v1', 'v1')
              ON CONFLICT (id) DO UPDATE SET terminos = 'v1', privacidad = 'v1', reembolso = 'v1';
            INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, activo, creada_en, acepto_terminos_version, acepto_terminos_en, acepto_privacidad_version, acepto_privacidad_en, acepto_reembolso_version, acepto_reembolso_en)
              VALUES ('{id}', 'Ana Pérez', 'p{id:N}@example.test', true, 'cliente', true, now(), 'v1', now(), 'v1', now(), 'v1', now());
            """);
        return id;
    }

    /// <summary>Lleva a una cuenta nueva desde el carrito hasta pasar al pago por el Carrito real: la intención de Pagos nace de ahí. Devuelve la cuenta y el pedido.</summary>
    private async Task<(Guid Cuenta, Guid Pedido)> ComprarHastaElPago(AnfitrionRealDelEcommerce anfitrion, int cantidad = 2)
    {
        var cuenta = await SembrarCuenta();
        var textos = await TextosVigentes(anfitrion);
        var agregado = await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICarritoDelCliente>().AgregarLinea(new AgregarLineaEntrada(EdicionImpresa, cantidad)));
        Assert.True(agregado.EsExito, agregado.Error?.Clave);
        Assert.True((await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICheckoutDelCliente>().Iniciar(new IniciarCheckoutEntrada()))).EsExito);
        var entrega = await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICheckoutDelCliente>().ElegirEntrega(new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Recojo, "sede-central", null)));
        Assert.True(entrega.EsExito, entrega.Error?.Clave);
        var aceptado = await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICheckoutDelCliente>().RegistrarAceptaciones(new RegistrarAceptacionesEntrada(textos)));
        // Si las versiones cambiaron entre la lectura y la aceptación, el mensaje dice cuáles eran (un defecto de aislamiento o de la fuente de versiones se vería aquí).
        Assert.True(aceptado.EsExito, aceptado.Error?.Clave + " | vistas: " + string.Join(", ", textos.Select(t => $"{t.Texto}={t.VersionVista}")) + " | vigentes ahora: " + string.Join(", ", (await TextosVigentes(anfitrion)).Select(t => $"{t.Texto}={t.VersionVista}")) + " | en la base: " + await bd.Consultar<string>("SELECT terminos || '/' || privacidad || '/' || reembolso FROM identidad.versiones_de_textos WHERE id = 1"));

        var pasar = await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICheckoutDelCliente>().PasarAlPago(new PasarAlPagoEntrada(PrecioDeLaEdicion * cantidad, textos)));
        Assert.True(pasar.EsExito, pasar.Error?.Clave);
        var pedido = pasar.Valor.PedidoId ?? throw new InvalidOperationException("Pasar al pago no devolvió el pedido.");

        // La intención ya es real (la creó Carrito por Pagos): se siembra en el Inventario simulado de Pagos la reserva de sus líneas.
        var ahora = _reloj.Ahora;
        var lineas = await ConsultarLineasDeLaIntencion(pedido);
        Assert.NotEmpty(lineas);
        _reservasDelPago.Sembrar(pedido, ahora + PlazosDelPago.VigenciaConImpresos, [.. lineas.Select(l => new LineaConfirmadaDeReserva(l, "AYA-01", false, null, null))]);
        return (cuenta, pedido);
    }

    /// <summary>Los términos y la privacidad con la versión vigente que Identidad publica (la que Carrito compara al aceptar), tal como los vería la persona.</summary>
    private static async Task<TextoVistoDeCompra[]> TextosVigentes(AnfitrionRealDelEcommerce anfitrion)
    {
        var terminos = await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<ITextosLegales>().Obtener(TipoDeTextoLegal.Terminos));
        var privacidad = await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<ITextosLegales>().Obtener(TipoDeTextoLegal.Privacidad));
        Assert.True(terminos.EsExito && privacidad.EsExito, "Identidad debe publicar las versiones vigentes de los textos.");
        return [new(TextoLegalDeCompra.Terminos, terminos.Valor.Version), new(TextoLegalDeCompra.Privacidad, privacidad.Valor.Version)];
    }

    private async Task<List<Guid>> ConsultarLineasDeLaIntencion(Guid pedido)
    {
        var lineas = new List<Guid>();
        await using var comando = bd.Administracion.CreateCommand("SELECT linea_id FROM pagos.intencion_lineas WHERE intencion_id = @p");
        comando.Parameters.AddWithValue("p", pedido);
        await using var lector = await comando.ExecuteReaderAsync();
        while (await lector.ReadAsync())
        {
            lineas.Add(lector.GetGuid(0));
        }

        return lineas;
    }

    private async Task<ResultadoDelCobroDelCliente> Cobrar(AnfitrionRealDelEcommerce anfitrion, Guid cuenta, Guid pedido, string token, MedioDelContrato medio = MedioDelContrato.Tarjeta) =>
        await anfitrion.Como(DelCliente(cuenta), async sp =>
        {
            var pagos = sp.GetRequiredService<IPagos>();
            var clave = await pagos.EmitirClaveDeIntento(new EmitirClaveDeIntentoEntrada(pedido));
            Assert.True(clave.EsExito, clave.Error?.Clave);
            var cobro = await pagos.Cobrar(new CobrarEntrada(pedido, clave.Valor.Token, medio, TokenDelMedioDePago.Crear(token)));
            Assert.True(cobro.EsExito, cobro.Error?.Clave);
            return cobro.Valor;
        });

    private Task Reanudar(AnfitrionRealDelEcommerce anfitrion) =>
        anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<ReanudarComprasAprobadas>().Ejecutar(50));

    private Task EnviarAvisosACarrito(AnfitrionRealDelEcommerce anfitrion) =>
        anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<AvisarACarrito>().EnviarPendientes(50));

    private Task<string?> EstadoDelCheckout(Guid cuenta) => bd.Consultar<string>($"SELECT estado FROM carrito.checkouts WHERE cuenta_id = '{cuenta}' ORDER BY creado_en DESC LIMIT 1");

    private Task<string?> EstadoDeLaIntencion(Guid pedido) => bd.Consultar<string>("SELECT estado FROM pagos.intenciones WHERE id = @p", ("p", pedido));

    private Task<long> LineasDelCarrito(Guid cuenta) =>
        bd.Consultar<long>($"SELECT count(*) FROM carrito.lineas_de_carrito l JOIN carrito.carritos c ON c.id = l.carrito_id WHERE c.cuenta_id = '{cuenta}'");

    private Task<long> AvisosSinEnviar(Guid pedido) =>
        bd.Consultar<long>("SELECT count(*) FROM pagos.avisos_a_carrito WHERE intencion_id = @p AND estado IN ('retenido', 'pendiente')", ("p", pedido));

    [Fact]
    [Trait("Criterio", "AC-PAG-42")]
    [Trait("Criterio", "AC-PAG-43")]
    public async Task Sin_pasarela_la_compra_llega_al_paso_de_pago_no_admite_ningun_cobro_y_volver_al_carrito_libera_la_reserva()
    {
        await using var anfitrion = Anfitrion("ninguna");

        var (cuenta, pedido) = await ComprarHastaElPago(anfitrion);

        // El checkout hace lo de siempre: la intención de pago existe y los impresos quedan separados.
        Assert.Equal("en_pago", await EstadoDelCheckout(cuenta));
        Assert.Equal("abierta", await EstadoDeLaIntencion(pedido));
        Assert.Equal(2, _inventarioDelCarrito.Reservado(EdicionImpresa));
        var estado = await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<IPagos>().ObtenerEstado(pedido));
        Assert.Equal(EstadoDelPagoDeLaCompra.ListoParaPagar, estado.Valor.Estado);
        Assert.False(estado.Valor.CobroDisponible);
        Assert.False(estado.Valor.PuedeReintentar);

        var clave = await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<IPagos>().EmitirClaveDeIntento(new EmitirClaveDeIntentoEntrada(pedido)));
        var cobro = await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<IPagos>().Cobrar(
            new CobrarEntrada(pedido, "clave-hecha-a-mano", MedioDelContrato.Tarjeta, TokenDelMedioDePago.Crear(EscenariosDelSimulador.Aprobado))));

        Assert.Equal("pago-no-disponible", clave.Error?.Clave);
        Assert.Equal("pago-no-disponible", cobro.Error?.Clave);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM pagos.intentos WHERE intencion_id = @p", ("p", pedido)));
        Assert.Equal("abierta", await EstadoDeLaIntencion(pedido));
        Assert.DoesNotContain(pedido, _pedidos.PedidosCreados);

        var vuelta = await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICheckoutDelCliente>().VolverAlCarrito(new SalirDelCheckoutEntrada(pedido)));

        Assert.True(vuelta.EsExito, vuelta.Error?.Clave);
        Assert.Equal(0, _inventarioDelCarrito.Reservado(EdicionImpresa));
        Assert.Equal(1L, await LineasDelCarrito(cuenta));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public async Task Pasar_al_pago_por_el_Carrito_real_crea_la_intencion_y_un_cobro_aprobado_completa_la_compra_y_deja_el_checkout_pagado_y_el_carrito_sin_lo_comprado()
    {
        await using var anfitrion = Anfitrion();
        var (cuenta, pedido) = await ComprarHastaElPago(anfitrion);
        Assert.Equal("en_pago", await EstadoDelCheckout(cuenta));
        Assert.Equal("abierta", await EstadoDeLaIntencion(pedido));

        var cobro = await Cobrar(anfitrion, cuenta, pedido, EscenariosDelSimulador.Aprobado);
        await Reanudar(anfitrion);
        await EnviarAvisosACarrito(anfitrion);

        Assert.Equal(EstadoDelCobro.Aprobado, cobro.Estado);
        Assert.Contains(pedido, _pedidos.PedidosCreados);
        Assert.Equal("completada", await EstadoDeLaIntencion(pedido));
        Assert.Equal("pagado", await EstadoDelCheckout(cuenta));
        Assert.Equal(0L, await LineasDelCarrito(cuenta));
        Assert.Equal(0L, await AvisosSinEnviar(pedido));
        Assert.DoesNotContain(_liberaciones, l => l.Pedido == pedido);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Una_falla_de_Pedidos_tras_el_cobro_deja_la_compra_aprobada_y_el_Carrito_en_pago_y_la_retoma_la_completa_sin_duplicar_el_pedido()
    {
        await using var anfitrion = Anfitrion();
        var (cuenta, pedido) = await ComprarHastaElPago(anfitrion);
        _pedidos.Falla = new InvalidOperationException("Pedidos no responde.");

        await Cobrar(anfitrion, cuenta, pedido, EscenariosDelSimulador.Aprobado);

        Assert.Equal("aprobada", await EstadoDeLaIntencion(pedido));
        Assert.DoesNotContain(pedido, _pedidos.PedidosCreados);
        Assert.NotEqual("pagado", await EstadoDelCheckout(cuenta));

        _pedidos.Falla = null;
        await Reanudar(anfitrion);
        await EnviarAvisosACarrito(anfitrion);

        Assert.Single(_pedidos.PedidosCreados, p => p == pedido);
        Assert.Equal("completada", await EstadoDeLaIntencion(pedido));
        Assert.Equal("pagado", await EstadoDelCheckout(cuenta));
        Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM pagos.intentos WHERE intencion_id = @p AND referencia_pasarela IS NOT NULL", ("p", pedido)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Un_aviso_que_Carrito_no_acepta_queda_anotado_se_reintenta_y_Carrito_termina_en_pagado_sin_cambiar_el_estado_del_pago()
    {
        await using var anfitrion = Anfitrion();
        var (cuenta, pedido) = await ComprarHastaElPago(anfitrion);
        _fallasDeCarrito.Fallar = true;

        await Cobrar(anfitrion, cuenta, pedido, EscenariosDelSimulador.Aprobado);
        await Reanudar(anfitrion);
        await EnviarAvisosACarrito(anfitrion);

        // El pago está resuelto aunque Carrito no respondió: el aviso sigue pendiente y el checkout aún no lo sabe.
        Assert.Equal("completada", await EstadoDeLaIntencion(pedido));
        Assert.True(await AvisosSinEnviar(pedido) > 0, "el aviso a Carrito debe seguir anotado");
        Assert.NotEqual("pagado", await EstadoDelCheckout(cuenta));

        _fallasDeCarrito.Fallar = false;
        _reloj.Avanzar(TimeSpan.FromMinutes(10));
        await EnviarAvisosACarrito(anfitrion);

        Assert.Equal(0L, await AvisosSinEnviar(pedido));
        Assert.Equal("pagado", await EstadoDelCheckout(cuenta));
        Assert.Equal(0L, await LineasDelCarrito(cuenta));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public async Task Un_pago_pendiente_que_se_aprueba_despues_de_vencer_se_reembolsa_sin_pedido_y_Carrito_no_queda_pagado_ni_en_pago_pendiente()
    {
        await using var anfitrion = Anfitrion();
        var (cuenta, pedido) = await ComprarHastaElPago(anfitrion);

        var lento = await Cobrar(anfitrion, cuenta, pedido, EscenariosDelSimulador.Lento);
        await EnviarAvisosACarrito(anfitrion);
        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, lento.Estado);
        Assert.Equal("pago_pendiente", await EstadoDelCheckout(cuenta));

        // La consulta llega recién a los 31 minutos: el cargo existe y la intención ya venció (pago tardío).
        _reloj.Avanzar(TimeSpan.FromMinutes(31));
        await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<ConsultarPagosPendientes>().Ejecutar(50));
        await Reanudar(anfitrion);
        await EnviarAvisosACarrito(anfitrion);

        Assert.DoesNotContain(pedido, _pedidos.PedidosCreados);
        Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM pagos.reembolsos WHERE intencion_id = @p AND estado = 'reembolsado'", ("p", pedido)));
        Assert.NotEqual("pagado", await EstadoDelCheckout(cuenta));
        Assert.NotEqual("pago_pendiente", await EstadoDelCheckout(cuenta));
        Assert.Equal(1L, await LineasDelCarrito(cuenta));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Si_Pedidos_sigue_fallando_a_los_treinta_minutos_se_compensa_con_un_solo_reembolso_y_Carrito_recibe_el_aviso_de_compensado()
    {
        await using var anfitrion = Anfitrion();
        var (cuenta, pedido) = await ComprarHastaElPago(anfitrion);
        _pedidos.Falla = new InvalidOperationException("Pedidos no responde.");
        await Cobrar(anfitrion, cuenta, pedido, EscenariosDelSimulador.Aprobado);

        _reloj.Avanzar(TimeSpan.FromMinutes(31));
        await Reanudar(anfitrion);
        await Reanudar(anfitrion);
        await EnviarAvisosACarrito(anfitrion);

        Assert.Equal("compensada", await EstadoDeLaIntencion(pedido));
        Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM pagos.reembolsos WHERE intencion_id = @p AND estado = 'reembolsado' AND origen = 'compensacion'", ("p", pedido)));
        Assert.DoesNotContain(pedido, _pedidos.PedidosCreados);
        Assert.All(_liberaciones.Where(l => l.Pedido == pedido), l => Assert.Equal(MotivoDeLiberacion.FallaPosteriorAlPago, l.Motivo));
        Assert.NotEqual("pagado", await EstadoDelCheckout(cuenta));
        Assert.Equal(0L, await AvisosSinEnviar(pedido));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-20")]
    public async Task Dos_ejecutores_simultaneos_sobre_la_compra_aprobada_crean_un_solo_pedido_y_Carrito_la_marca_pagada_una_vez()
    {
        await using var anfitrion = Anfitrion();
        var (cuenta, pedido) = await ComprarHastaElPago(anfitrion);
        _pedidos.Falla = new InvalidOperationException("Pedidos no responde.");
        await Cobrar(anfitrion, cuenta, pedido, EscenariosDelSimulador.Aprobado);
        _pedidos.Falla = null;
        _pedidos.Retraso = TimeSpan.FromMilliseconds(400);
        var llamadasAntes = _pedidos.Llamadas;

        await Task.WhenAll(Task.Run(() => Reanudar(anfitrion)), Task.Run(() => Reanudar(anfitrion)));
        await EnviarAvisosACarrito(anfitrion);

        Assert.Equal(1, _pedidos.Llamadas - llamadasAntes);
        Assert.Single(_pedidos.PedidosCreados, p => p == pedido);
        Assert.Equal("completada", await EstadoDeLaIntencion(pedido));
        Assert.Equal("pagado", await EstadoDelCheckout(cuenta));
        Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM pagos.avisos_a_carrito WHERE intencion_id = @p AND transicion = 'aprobado'", ("p", pedido)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-22")]
    public async Task Cincuenta_carreras_entre_cerrar_las_intenciones_vencidas_y_volver_al_carrito_terminan_sin_interbloqueo_con_la_intencion_cerrada_una_sola_vez()
    {
        await using var anfitrion = Anfitrion();
        for (var i = 0; i < 50; i++)
        {
            var (cuenta, pedido) = await ComprarHastaElPago(anfitrion, cantidad: 1);
            _reloj.Avanzar(PlazosDelPago.VigenciaConImpresos + TimeSpan.FromSeconds(1));

            // Dos caminos a la vez sobre la misma intención: el trabajo que cierra las vencidas y la persona que vuelve al carrito.
            var cierre = Task.Run(() => anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<CerrarIntencionesVencidas>().Ejecutar(50)));
            var vuelta = Task.Run(() => anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICheckoutDelCliente>().VolverAlCarrito(new SalirDelCheckoutEntrada(pedido))));
            await Task.WhenAll(cierre, vuelta).WaitAsync(TimeSpan.FromSeconds(30));
            await EnviarAvisosACarrito(anfitrion);

            // Una intención abierta y vencida queda cerrada sin cobro, una sola vez, y el checkout ya no está esperando el pago.
            Assert.True(await EstadoDeLaIntencion(pedido) is "cerrada_sin_cobro", $"iteración {i}: la intención debía quedar cerrada sin cobro");
            Assert.NotEqual("en_pago", await EstadoDelCheckout(cuenta));
            Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM pagos.intenciones WHERE id = @p", ("p", pedido)));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-25")]
    public async Task Tras_una_compra_con_el_Carrito_real_no_queda_tarjeta_token_correo_ni_contacto_en_registros_auditoria_ni_avisos()
    {
        await using var anfitrion = Anfitrion();
        var (cuenta, pedido) = await ComprarHastaElPago(anfitrion);
        await Cobrar(anfitrion, cuenta, pedido, EscenariosDelSimulador.Aprobado);
        await Reanudar(anfitrion);
        await EnviarAvisosACarrito(anfitrion);

        // Lo sensible de la siembra: el token del medio, el correo del titular y el nombre de la cuenta.
        var correo = $"p{cuenta:N}@example.test";
        foreach (var tabla in new[] { "auditoria.registros", "pagos.intentos", "pagos.avisos_de_pago", "pagos.avisos_a_carrito", "pagos.compensaciones", "pagos.reembolsos" })
        {
            var filas = await bd.Consultar<long>($"SELECT count(*) FROM {tabla} t WHERE t::text ILIKE ANY (ARRAY['%tok_aprobado%', '%{correo}%', '%Ana P%', '%4111111111111111%'])");
            Assert.True(filas == 0, $"{tabla} no debe guardar el token del medio, el correo ni el nombre del titular");
        }
    }
}

/// <summary>Los avisos de Pagos al Carrito real, con un interruptor para que Carrito «no responda» (lanza) y se pruebe el reintento.</summary>
internal sealed class ProgresoConFallas
{
    public volatile bool Fallar;
}

internal sealed class ProgresoConElCarritoReal(IProgresoDelCheckout interior, ProgresoConFallas fallas) : IProgresoDelCheckout
{
    private void Verificar()
    {
        if (fallas.Fallar)
        {
            throw new InvalidOperationException("Carrito no responde.");
        }
    }

    public Task<ResultadoDelAvisoDePago> PagoPendiente(Guid pedidoId, DateTimeOffset limiteEn, DateTimeOffset reservaExpiraEn, CancellationToken cancelacion = default)
    {
        Verificar();
        return interior.PagoPendiente(pedidoId, limiteEn, reservaExpiraEn, cancelacion);
    }

    public Task<ResultadoDelAvisoDePago> PagoAprobado(Guid pedidoId, CancellationToken cancelacion = default)
    {
        Verificar();
        return interior.PagoAprobado(pedidoId, cancelacion);
    }

    public Task<ResultadoDelAvisoDePago> PagoResuelto(Guid pedidoId, MotivoDePagoResueltoDeCompra motivo, CancellationToken cancelacion = default)
    {
        Verificar();
        return interior.PagoResuelto(pedidoId, motivo, cancelacion);
    }
}
