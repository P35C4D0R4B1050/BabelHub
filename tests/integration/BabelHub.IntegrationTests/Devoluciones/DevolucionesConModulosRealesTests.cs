using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Devoluciones.Interno;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.IntegrationTests.Pedidos;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.IntegrationTests.Devoluciones;

/// <summary>
/// T30 (AC-DEV-04, AC-DEV-05, AC-DEV-06, AC-DEV-08, AC-DEV-09, AC-DEV-18, AC-DEV-19, AC-DEV-20, AC-DEV-24): Devoluciones con el <b>Pedidos real</b> y la <b>Biblioteca real</b> sobre todos los esquemas migrados del E-commerce (el anfitrión real), con la identidad real
/// (textos legales aceptados), el reloj controlado y solo lo externo simulado (el reembolso de Pagos y el Inventario).
/// </summary>
[Collection(ColeccionDelEcommerceCompleto.Nombre)]
public sealed class DevolucionesConModulosRealesTests(PostgresDelEcommerceCompletoFixture bd) : IAsyncLifetime
{
    private const string Sembrado = "SEMBRADO-motivo-t30";

    private const string SucursalT30 = "t30-lima";

    private static readonly Guid EncargadoT30 = Guid.Parse("55555555-5555-5555-5555-555555555530");

    private static readonly Guid EdicionDigital = Guid.Parse("b0000000-0000-0000-0000-0000000000d1");

    private readonly ReembolsosFalsos _reembolsos = new();

    private readonly ReservasFalsas _reservas = new();

    private readonly InventarioSimulado _inventario = new();

    private readonly RelojDeDevoluciones _reloj = new(new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date.AddSeconds(Math.Floor(DateTimeOffset.UtcNow.TimeOfDay.TotalSeconds)), TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        _inventario.Sucursales.Add(SucursalT30);
        await bd.Ejecutar(
            $"""
            INSERT INTO identidad.versiones_de_textos (id, terminos, privacidad, reembolso) VALUES (1, 'v1', 'v1', 'v1') ON CONFLICT (id) DO UPDATE SET terminos = 'v1', privacidad = 'v1', reembolso = 'v1';
            INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, activo, creada_en, sucursal, acepto_terminos_version, acepto_terminos_en, acepto_privacidad_version, acepto_privacidad_en, acepto_reembolso_version, acepto_reembolso_en)
              VALUES ('{EncargadoT30}', 'Encargado de prueba', 'encargado-t30@example.test', true, 'encargado-de-sucursal', true, now(), '{IdentificadorDeSucursal.DesdeCodigo(SucursalT30)}', 'v1', now(), 'v1', now(), 'v1', now())
              ON CONFLICT DO NOTHING;
            """);
        await bd.Ejecutar(
            """
            BEGIN;
            INSERT INTO catalogo.obras (id, titulo, autor, editorial, categoria, portada_clave, portada_huella, portada_tipo, estado, version, creada_en)
              VALUES ('a0000000-0000-0000-0000-0000000000d1', 'El Aleph (prueba)', 'Autor de prueba', 'Editorial de prueba', 'Narrativa', 'e2e0000000000000000000000000d1d1', decode(repeat('d1', 32), 'hex'), 'jpeg', 'activa', 1, now())
              ON CONFLICT DO NOTHING;
            INSERT INTO catalogo.ediciones (id, obra_id, tipo, isbn13, isbn_ingresado, precio, estado)
              VALUES ('b0000000-0000-0000-0000-0000000000d1', 'a0000000-0000-0000-0000-0000000000d1', 'digital', '9786123450021', '9786123450021', 30.00, 'activa')
              ON CONFLICT DO NOTHING;
            INSERT INTO catalogo.licencias (edicion_id, tipo, titular, territorios, vigente_hasta, formato)
              VALUES ('b0000000-0000-0000-0000-0000000000d1', 'dominio_publico', 'Titular de prueba', ARRAY['PE'], NULL, 'epub')
              ON CONFLICT DO NOTHING;
            INSERT INTO catalogo.archivos_digitales (edicion_id, clave_en_almacen, tipo_detectado, tamano, huella_sha256, subido_en, subido_por)
              VALUES ('b0000000-0000-0000-0000-0000000000d1', 'f2e000000000000000000000000000d1', 'epub', 1024, decode(repeat('d2', 32), 'hex'), now(), '00000000-0000-0000-0000-000000000000')
              ON CONFLICT DO NOTHING;
            INSERT INTO identidad.versiones_de_textos (id, terminos, privacidad, reembolso) VALUES (1, 'v1', 'v1', 'v1') ON CONFLICT (id) DO UPDATE SET terminos = 'v1', privacidad = 'v1', reembolso = 'v1';
            INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, activo, creada_en, acepto_terminos_version, acepto_terminos_en, acepto_privacidad_version, acepto_privacidad_en, acepto_reembolso_version, acepto_reembolso_en)
              VALUES ('44444444-4444-4444-4444-444444444444', 'Admin de prueba', 'admin-t30@example.test', true, 'administrador-central', true, now(), 'v1', now(), 'v1', now(), 'v1', now())
              ON CONFLICT DO NOTHING;
            COMMIT;
            """);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AnfitrionRealDelEcommerce Anfitrion() => bd.Anfitrion(servicios =>
    {
        servicios.RemoveAll<IReservasDelPedido>();
        servicios.AddSingleton<IReservasDelPedido>(_reservas);
        servicios.RemoveAll<IReembolsos>();
        servicios.AddSingleton<IReembolsos>(_reembolsos);
        servicios.RemoveAll<IInventarioDeDevoluciones>();
        servicios.AddSingleton<IInventarioDeDevoluciones>(_inventario);
        servicios.RemoveAll<IReloj>();
        servicios.AddSingleton<IReloj>(_reloj);
    });

    private static ActorActual DelCliente(Guid cuenta) => ActorActual.DeUsuario(cuenta, RolesDeUsuario.Cliente, null);

    private static ActorActual DelAdministrador() => ActorActual.DeUsuario(Guid.Parse("44444444-4444-4444-4444-444444444444"), RolesDeUsuario.AdministradorCentral, null);

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

    /// <summary>Un pedido de recojo con un impreso y un libro digital, confirmado por el Pedidos real (el acceso digital lo concede la Biblioteca real).</summary>
    private async Task<(Guid Pedido, Guid Cuenta, Guid LineaDigital)> ComprarMixto(AnfitrionRealDelEcommerce anfitrion)
    {
        var (pedido, cuenta, digital, _) = await ComprarMixtoConImpreso(anfitrion);
        return (pedido, cuenta, digital);
    }

    private async Task<(Guid Pedido, Guid Cuenta, Guid LineaDigital, Guid LineaImpresa)> ComprarMixtoConImpreso(AnfitrionRealDelEcommerce anfitrion)
    {
        var lineaImpresa = Guid.NewGuid();
        var pedido = Guid.NewGuid();
        var cuenta = await SembrarCuenta();
        var ahora = _reloj.Ahora;
        var lineaDigital = Guid.NewGuid();
        var lineas = new List<LineaDeCompraAprobada>
        {
            new(lineaImpresa, Guid.NewGuid(), false, "Ficciones", null, 1, 50m, 50m, SucursalT30, false, null, null),
            new(lineaDigital, EdicionDigital, true, "El Aleph (digital)", null, 1, 30m, 30m, null, false, null, null),
        };
        var aceptaciones = new List<AceptacionDeCompra> { new(TextoLegalDeCompra.Terminos, "v1", ahora), new(TextoLegalDeCompra.Privacidad, "v1", ahora), new(TextoLegalDeCompra.ReembolsoDigital, "v1", ahora) };
        var compra = new CompraAprobada(pedido, cuenta, TipoDeEntregaDeCompra.Recojo, SucursalT30, null, 0m, 80m, null, aceptaciones, lineas);
        Assert.Equal(ResultadoDeConfirmarCompra.Creado, await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<IConfirmacionDeCompra>().Confirmar(compra)));
        return (pedido, cuenta, lineaDigital, lineaImpresa);
    }

    private static Task<Resultado<DevolucionVista>> PedirReembolso(AnfitrionRealDelEcommerce anfitrion, Guid cuenta, Guid pedido, Guid linea) =>
        anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<IDevolucionesDelCliente>().PedirReembolsoDeDigital(new PedirReembolsoDeDigitalEntrada(pedido, linea, new TextoLibre(Sembrado))));

    [Fact]
    [Trait("Criterio", "AC-DEV-04")]
    [Trait("Criterio", "AC-DEV-22")]
    public async Task El_reembolso_de_un_digital_a_los_catorce_dias_exactos_se_aprueba_revoca_el_acceso_real_y_registra_un_solo_reembolso_a_los_quince_no()
    {
        await using var anfitrion = Anfitrion();
        var (pedido, cuenta, linea) = await ComprarMixto(anfitrion);
        Assert.Equal("vigente", await bd.Consultar<string>("SELECT estado FROM biblioteca.accesos WHERE pedido_id = @p", ("p", pedido)));

        _reloj.Ahora = _reloj.Ahora.AddDays(14).AddSeconds(1);
        var tarde = await PedirReembolso(anfitrion, cuenta, pedido, linea);
        _reloj.Ahora = _reloj.Ahora.AddSeconds(-1);
        var enPlazo = await PedirReembolso(anfitrion, cuenta, pedido, linea);

        Assert.False(tarde.EsExito);
        Assert.Equal("plazo-vencido", tarde.Error!.Clave);
        Assert.True(enPlazo.EsExito, enPlazo.Error?.Clave);
        Assert.Equal("revocado", await bd.Consultar<string>("SELECT estado FROM biblioteca.accesos WHERE pedido_id = @p", ("p", pedido)));
        Assert.Equal("reembolso", await bd.Consultar<string>("SELECT motivo_de_revocacion FROM biblioteca.accesos WHERE pedido_id = @p", ("p", pedido)));
        var reembolso = Assert.Single(_reembolsos.Registrados);
        Assert.Equal(30m, reembolso.Lineas.Sum(l => l.Monto));
        Assert.Equal(0m, reembolso.Envio);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-05")]
    [Trait("Criterio", "AC-DEV-09")]
    public async Task Con_un_enlace_ya_entregado_el_reembolso_se_rechaza_por_politica_sin_revocar_ni_registrar_nada()
    {
        await using var anfitrion = Anfitrion();
        var (pedido, cuenta, linea) = await ComprarMixto(anfitrion);
        await bd.Ejecutar($"UPDATE biblioteca.accesos SET descargas_usadas = 1, enlaces_entregados = 1 WHERE pedido_id = '{pedido:D}'");

        var resultado = await PedirReembolso(anfitrion, cuenta, pedido, linea);

        Assert.False(resultado.EsExito);
        Assert.Equal("politica-digital", resultado.Error!.Clave);
        Assert.Equal("vigente", await bd.Consultar<string>("SELECT estado FROM biblioteca.accesos WHERE pedido_id = @p", ("p", pedido)));
        Assert.Empty(_reembolsos.Registrados);
        Assert.Equal(0, await bd.Consultar<int>("SELECT count(*) FROM devoluciones.devoluciones WHERE pedido_id = @p", ("p", pedido)));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-24")]
    public async Task Un_reporte_suspende_las_descargas_el_rechazo_las_reanuda_y_el_aprobado_revoca_y_reembolsa()
    {
        await using var anfitrion = Anfitrion();
        var (pedido, cuenta, linea) = await ComprarMixto(anfitrion);
        var reportado = await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<IDevolucionesDelCliente>().ReportarArchivoDefectuoso(new ReportarArchivoDefectuosoEntrada(pedido, linea, new TextoLibre(Sembrado), new TextoLibre("No abre el capítulo 2"))));
        Assert.True(reportado.EsExito, reportado.Error?.Clave);
        Assert.Equal("suspendido", await bd.Consultar<string>("SELECT estado FROM biblioteca.accesos WHERE pedido_id = @p", ("p", pedido)));

        var rechazado = await anfitrion.Como(DelAdministrador(), sp => sp.GetRequiredService<IGestionDeDevoluciones>().RechazarReporte(reportado.Valor.DevolucionId));

        Assert.True(rechazado.EsExito, rechazado.Error?.Clave);
        Assert.Equal("vigente", await bd.Consultar<string>("SELECT estado FROM biblioteca.accesos WHERE pedido_id = @p", ("p", pedido)));
        Assert.Empty(_reembolsos.Registrados);

        var segundo = await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<IDevolucionesDelCliente>().ReportarArchivoDefectuoso(new ReportarArchivoDefectuosoEntrada(pedido, linea, new TextoLibre(Sembrado), new TextoLibre("Sigue sin abrir"))));
        Assert.True(segundo.EsExito, segundo.Error?.Clave);
        var aprobado = await anfitrion.Como(DelAdministrador(), sp => sp.GetRequiredService<IGestionDeDevoluciones>().AprobarReporte(segundo.Valor.DevolucionId));

        Assert.True(aprobado.EsExito, aprobado.Error?.Clave);
        Assert.Equal("revocado", await bd.Consultar<string>("SELECT estado FROM biblioteca.accesos WHERE pedido_id = @p", ("p", pedido)));
        Assert.Equal("reporte-aprobado", await bd.Consultar<string>("SELECT motivo_de_revocacion FROM biblioteca.accesos WHERE pedido_id = @p", ("p", pedido)));
        Assert.Single(_reembolsos.Registrados);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-18")]
    public async Task La_anulacion_del_pedido_mixto_tras_el_reembolso_digital_excluye_la_linea_ya_reembolsada()
    {
        await using var anfitrion = Anfitrion();
        var (pedido, cuenta, linea) = await ComprarMixto(anfitrion);
        Assert.True((await PedirReembolso(anfitrion, cuenta, pedido, linea)).EsExito);
        var registradosAntes = _reembolsos.Registrados.Count;
        Assert.Equal(1, registradosAntes);

        var anulacion = await anfitrion.Como(DelAdministrador(), sp => sp.GetRequiredService<IAdministracionDePedidos>().Anular(new AnularPedidoEntrada(pedido, "otro")));

        Assert.True(anulacion.EsExito, anulacion.Error?.Clave);
        var deLaAnulacion = _reembolsos.Registrados.Single(r => r.Origen == OrigenDelReembolso.Anulacion);
        Assert.Single(deLaAnulacion.Lineas);
        Assert.Equal(50m, deLaAnulacion.Lineas.Sum(l => l.Monto));
    }

    private sealed class AplicadorQueFalla : BabelHub.Ecommerce.Application.Identidad.IAplicadorDeCancelacion
    {
        public string Modulo => "falla-al-final";

        public Task Aplicar(Guid usuarioId, Guid identificadorOpaco, CancellationToken cancelacion = default) => throw new InvalidOperationException("falla simulada");
    }

    private static Task AplicarLosReceptores(IServiceProvider sp, Guid cuenta, params BabelHub.Ecommerce.Application.Identidad.IAplicadorDeCancelacion[] extra) =>
        sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar<bool>(
            async token =>
            {
                foreach (var receptor in BabelHub.Ecommerce.Application.Identidad.Interno.EjecutorDeLaCancelacion.EnElOrdenDeLosReceptores(sp.GetServices<BabelHub.Ecommerce.Application.Identidad.IAplicadorDeCancelacion>().Concat(extra)))
                {
                    await receptor.Aplicar(cuenta, cuenta, token);
                }

                return true;
            });

    [Fact]
    [Trait("Criterio", "AC-DEV-26")]
    [Trait("Criterio", "AC-DEV-27")]
    public async Task La_cancelacion_de_la_cuenta_aplica_Pedidos_Devoluciones_y_Pagos_en_ese_orden_y_redacta_el_motivo_sin_tocar_lo_demas()
    {
        await using var anfitrion = Anfitrion();
        var (pedido, cuenta, linea) = await ComprarMixto(anfitrion);
        var devolucion = await PedirReembolso(anfitrion, cuenta, pedido, linea);
        Assert.True(devolucion.EsExito, devolucion.Error?.Clave);

        var orden = await anfitrion.Como(ActorActual.DelSistema(), sp => Task.FromResult(BabelHub.Ecommerce.Application.Identidad.Interno.EjecutorDeLaCancelacion.EnElOrdenDeLosReceptores(sp.GetServices<BabelHub.Ecommerce.Application.Identidad.IAplicadorDeCancelacion>()).Select(a => a.Modulo).ToList()));
        await anfitrion.Como(ActorActual.DelSistema(), sp => AplicarLosReceptores(sp, cuenta).ContinueWith(_ => true, TaskScheduler.Default));

        Assert.True(orden.IndexOf("pedidos") < orden.IndexOf("devoluciones"), string.Join(",", orden));
        Assert.True(orden.IndexOf("devoluciones") < orden.IndexOf("pagos"), string.Join(",", orden));
        Assert.Equal(TextoLibre.Redactado, await bd.Consultar<string>("SELECT motivo FROM devoluciones.devoluciones WHERE pedido_id = @p", ("p", pedido)));
        Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM devoluciones.lineas_de_devolucion l JOIN devoluciones.devoluciones d ON d.id = l.devolucion_id WHERE d.pedido_id = @p", ("p", pedido)));
        Assert.Equal(30m, await bd.Consultar<decimal>("SELECT sum(monto) FROM devoluciones.lineas_de_devolucion l JOIN devoluciones.devoluciones d ON d.id = l.devolucion_id WHERE d.pedido_id = @p", ("p", pedido)));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-26")]
    public async Task Si_un_receptor_posterior_falla_la_redaccion_de_Devoluciones_se_revierte_con_el_resto()
    {
        await using var anfitrion = Anfitrion();
        var (pedido, cuenta, linea) = await ComprarMixto(anfitrion);
        Assert.True((await PedirReembolso(anfitrion, cuenta, pedido, linea)).EsExito);

        await Assert.ThrowsAsync<InvalidOperationException>(() => anfitrion.Como(ActorActual.DelSistema(), sp => AplicarLosReceptores(sp, cuenta, new AplicadorQueFalla()).ContinueWith(t => t.IsFaulted ? throw t.Exception!.InnerException! : true, TaskScheduler.Default)));

        Assert.Equal(Sembrado, await bd.Consultar<string>("SELECT motivo FROM devoluciones.devoluciones WHERE pedido_id = @p", ("p", pedido)));
    }

    private static ActorActual DelEncargadoT30() => ActorActual.DeUsuario(EncargadoT30, RolesDeUsuario.EncargadoDeSucursal, IdentificadorDeSucursal.DesdeCodigo(SucursalT30));

    /// <summary>Deja el impreso como entregado hace tres días (la entrega por Pedidos y el Inventario la cubren otras pruebas; aquí interesa Devoluciones sobre el pedido y la biblioteca reales).</summary>
    private async Task EntregarElImpreso(Guid pedido, Guid lineaImpresa) =>
        await bd.Ejecutar($"UPDATE pedidos.lineas_de_pedido SET estado = 'entregado', entregada_en = '{_reloj.Ahora.AddDays(-3):O}' WHERE id = '{lineaImpresa:D}' AND pedido_id = '{pedido:D}'");

    private static Task<Resultado<DevolucionVista>> SolicitarImpreso(AnfitrionRealDelEcommerce anfitrion, Guid cuenta, Guid pedido, Guid linea) =>
        anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<IDevolucionesDelCliente>().SolicitarImpreso(new SolicitarDevolucionDeImpresoEntrada(pedido, [new LineaPedidaParaDevolver(linea, 1)], new TextoLibre(Sembrado), SucursalT30)));

    [Fact]
    [Trait("Criterio", "AC-DEV-08")]
    public async Task El_impreso_se_reembolsa_solo_al_confirmar_la_recepcion_por_su_precio_sin_el_envio_y_sin_superar_lo_cobrado()
    {
        await using var anfitrion = Anfitrion();
        var (pedido, cuenta, _, impresa) = await ComprarMixtoConImpreso(anfitrion);
        await EntregarElImpreso(pedido, impresa);

        var solicitada = await SolicitarImpreso(anfitrion, cuenta, pedido, impresa);
        Assert.True(solicitada.EsExito, solicitada.Error?.Clave);
        var aprobada = await anfitrion.Como(DelEncargadoT30(), sp => sp.GetRequiredService<IGestionDeDevoluciones>().Aprobar(solicitada.Valor.DevolucionId));
        Assert.True(aprobada.EsExito, aprobada.Error?.Clave);
        Assert.Empty(_reembolsos.Registrados);

        var recibida = await anfitrion.Como(DelEncargadoT30(), sp => sp.GetRequiredService<IGestionDeDevoluciones>().ConfirmarRecepcion(solicitada.Valor.DevolucionId));

        Assert.True(recibida.EsExito, recibida.Error?.Clave);
        var reembolso = Assert.Single(_reembolsos.Registrados);
        Assert.Equal(50m, reembolso.Lineas.Sum(l => l.Monto));
        Assert.Equal(0m, reembolso.Envio);
        Assert.True(reembolso.Lineas.Sum(l => l.Monto) + reembolso.Envio <= 80m);
        Assert.Equal(1, _inventario.Aplicaciones(solicitada.Valor.DevolucionId));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-20")]
    public async Task Con_la_pasarela_caida_el_impreso_queda_con_el_reembolso_pendiente_y_la_conciliacion_lo_completa_una_sola_vez()
    {
        await using var anfitrion = Anfitrion();
        var (pedido, cuenta, _, impresa) = await ComprarMixtoConImpreso(anfitrion);
        await EntregarElImpreso(pedido, impresa);
        var solicitada = await SolicitarImpreso(anfitrion, cuenta, pedido, impresa);
        Assert.True(solicitada.EsExito, solicitada.Error?.Clave);
        Assert.True((await anfitrion.Como(DelEncargadoT30(), sp => sp.GetRequiredService<IGestionDeDevoluciones>().Aprobar(solicitada.Valor.DevolucionId))).EsExito);

        _reembolsos.EstadoAlProcesar = null;
        var recibida = await anfitrion.Como(DelEncargadoT30(), sp => sp.GetRequiredService<IGestionDeDevoluciones>().ConfirmarRecepcion(solicitada.Valor.DevolucionId));

        Assert.True(recibida.EsExito, recibida.Error?.Clave);
        var pendiente = await anfitrion.Como(DelEncargadoT30(), sp => sp.GetRequiredService<IGestionDeDevoluciones>().Obtener(solicitada.Valor.DevolucionId));
        Assert.True(pendiente.Valor.Lineas.Single().ReembolsoPendiente);

        _reembolsos.EstadoAlProcesar = EstadoDelReembolso.Reembolsado;
        var primera = await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<ConciliarReembolsosDeDevoluciones>().Ejecutar(100));
        var segunda = await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<ConciliarReembolsosDeDevoluciones>().Ejecutar(100));

        Assert.True(primera.EsExito && segunda.EsExito);
        Assert.Equal("reembolsada", await bd.Consultar<string>("SELECT l.estado FROM devoluciones.lineas_de_devolucion l JOIN devoluciones.devoluciones d ON d.id = l.devolucion_id WHERE d.pedido_id = @p", ("p", pedido)));
        Assert.Single(_reembolsos.Registrados.Select(r => r.ClaveNatural).Distinct());
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-19")]
    public async Task Cincuenta_pares_de_descarga_por_reembolso_automatico_los_deciden_a_favor_de_uno_solo()
    {
        await using var anfitrion = Anfitrion();
        var reembolsos = 0;
        var descargas = 0;
        for (var i = 0; i < 50; i++)
        {
            var (pedido, cuenta, linea) = await ComprarMixto(anfitrion);
            var acceso = await bd.Consultar<Guid>("SELECT id FROM biblioteca.accesos WHERE pedido_id = @p", ("p", pedido));

            var reembolso = anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<IDevolucionesDelCliente>().PedirReembolsoDeDigital(new PedirReembolsoDeDigitalEntrada(pedido, linea, new TextoLibre(Sembrado))));
            var descarga = anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<IDescargasDeLaBiblioteca>().SolicitarEnlace(acceso, "clave-" + Guid.NewGuid().ToString("N")));
            await Task.WhenAll(reembolso, descarga);

            var gananElReembolso = (await reembolso).EsExito;
            var ganaLaDescarga = (await descarga).EsExito;
            Assert.True(gananElReembolso ^ ganaLaDescarga, $"par {i}: reembolso={gananElReembolso}, descarga={ganaLaDescarga}");
            if (gananElReembolso)
            {
                reembolsos++;
                Assert.Equal("revocado", await bd.Consultar<string>("SELECT estado FROM biblioteca.accesos WHERE id = @a", ("a", acceso)));
            }
            else
            {
                descargas++;
                Assert.Equal("politica-digital", (await reembolso).Error!.Clave);
                Assert.Equal("vigente", await bd.Consultar<string>("SELECT estado FROM biblioteca.accesos WHERE id = @a", ("a", acceso)));
            }
        }

        Assert.Equal(50, reembolsos + descargas);
        Assert.Equal(reembolsos, _reembolsos.Registrados.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-24")]
    public async Task Aprobar_un_reporte_cuya_linea_una_anulacion_ya_revoco_responde_linea_no_devolvible_y_rechazarlo_lo_cierra()
    {
        await using var anfitrion = Anfitrion();
        var (pedido, cuenta, linea) = await ComprarMixto(anfitrion);
        var reportado = await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<IDevolucionesDelCliente>().ReportarArchivoDefectuoso(new ReportarArchivoDefectuosoEntrada(pedido, linea, new TextoLibre(Sembrado), new TextoLibre("No abre"))));
        Assert.True(reportado.EsExito, reportado.Error?.Clave);
        Assert.True((await anfitrion.Como(DelAdministrador(), sp => sp.GetRequiredService<IAdministracionDePedidos>().Anular(new AnularPedidoEntrada(pedido, "otro")))).EsExito);

        var aprobado = await anfitrion.Como(DelAdministrador(), sp => sp.GetRequiredService<IGestionDeDevoluciones>().AprobarReporte(reportado.Valor.DevolucionId));
        var rechazado = await anfitrion.Como(DelAdministrador(), sp => sp.GetRequiredService<IGestionDeDevoluciones>().RechazarReporte(reportado.Valor.DevolucionId));

        Assert.False(aprobado.EsExito);
        Assert.Equal("linea-no-devolvible", aprobado.Error!.Clave);
        Assert.True(rechazado.EsExito, rechazado.Error?.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-23")]
    public async Task Un_encargado_no_ve_ni_decide_los_reportes_y_un_cliente_ajeno_recibe_lo_mismo_que_por_una_devolucion_inexistente()
    {
        await using var anfitrion = Anfitrion();
        var (pedido, cuenta, linea) = await ComprarMixto(anfitrion);
        var reportado = await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<IDevolucionesDelCliente>().ReportarArchivoDefectuoso(new ReportarArchivoDefectuosoEntrada(pedido, linea, new TextoLibre(Sembrado), new TextoLibre("No abre"))));
        Assert.True(reportado.EsExito, reportado.Error?.Clave);
        var id = reportado.Valor.DevolucionId;

        var listaDelEncargado = await anfitrion.Como(DelEncargadoT30(), sp => sp.GetRequiredService<IGestionDeDevoluciones>().ListarDeLaAdministracion(new ListarDevolucionesDeLaAdministracionEntrada(SoloReportes: true)));
        var aprobaElEncargado = await anfitrion.Como(DelEncargadoT30(), sp => sp.GetRequiredService<IGestionDeDevoluciones>().AprobarReporte(id));
        var rechazaElEncargado = await anfitrion.Como(DelEncargadoT30(), sp => sp.GetRequiredService<IGestionDeDevoluciones>().RechazarReporte(id));
        var ajeno = ActorActual.DeUsuario(await SembrarCuenta(), RolesDeUsuario.Cliente);
        var ajenoSobreLaSuya = await anfitrion.Como(ajeno, sp => sp.GetRequiredService<IDevolucionesDelCliente>().ObtenerMia(id));
        var ajenoSobreUnaInexistente = await anfitrion.Como(ajeno, sp => sp.GetRequiredService<IDevolucionesDelCliente>().ObtenerMia(Guid.NewGuid()));

        Assert.False(listaDelEncargado.EsExito);
        Assert.Equal("prohibido", listaDelEncargado.Error!.Clave);
        Assert.Equal("prohibido", aprobaElEncargado.Error!.Clave);
        Assert.Equal("prohibido", rechazaElEncargado.Error!.Clave);
        Assert.Equal(ajenoSobreUnaInexistente.Error!.Clave, ajenoSobreLaSuya.Error!.Clave);
        Assert.Equal(ajenoSobreUnaInexistente.Error.Categoria, ajenoSobreLaSuya.Error.Categoria);
        Assert.Equal("suspendido", await bd.Consultar<string>("SELECT estado FROM biblioteca.accesos WHERE pedido_id = @p", ("p", pedido)));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-21")]
    public async Task Con_los_limites_reales_la_operacion_numero_treinta_y_uno_del_minuto_responde_limite_excedido_y_otro_usuario_no_se_afecta()
    {
        await using var anfitrion = Anfitrion();
        var cuenta = await SembrarCuenta();
        var otra = await SembrarCuenta();

        var resultados = new List<Resultado<DevolucionVista>>();
        for (var i = 0; i < 31; i++)
        {
            resultados.Add(await PedirReembolso(anfitrion, cuenta, Guid.NewGuid(), Guid.NewGuid()));
        }

        Assert.All(resultados.Take(30), r => Assert.NotEqual(CategoriaDeError.Limite, r.Error!.Categoria));
        var excedida = resultados[30];
        Assert.Equal(CategoriaDeError.Limite, excedida.Error!.Categoria);
        Assert.Equal("limite-excedido", excedida.Error.Clave);
        Assert.Contains(excedida.Error.Errores, c => c.Campo == "espera-en-segundos");
        var otraCuenta = await PedirReembolso(anfitrion, otra, Guid.NewGuid(), Guid.NewGuid());
        Assert.NotEqual(CategoriaDeError.Limite, otraCuenta.Error!.Categoria);
    }
}
