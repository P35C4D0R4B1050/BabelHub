using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Errores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.IntegrationTests.Pedidos;

/// <summary>
/// Pedidos con los módulos reales (ECO-6g; T33 a T36; PED-21, PED-22, PED-26, PED-31, PED-40; AC-PED-03, AC-PED-06, AC-PED-17, AC-PED-20, AC-PED-26, AC-PED-30, AC-PED-31, AC-PED-35): el anfitrión real del E-commerce sobre PostgreSQL
/// con <b>todos los esquemas migrados</b>, con la Auditoría persistida, la Biblioteca y los límites de uso reales. Solo lo externo es doble: el Inventario (reservas) y el reembolso de Pagos (el camino con la pasarela lo
/// recorre el E2E de compra y pedido). Cubre: dos <c>Confirmar</c> simultáneos con el mismo pedido, el acceso de la biblioteca revocado al anular aunque ya se haya descargado, el registro de auditoría sin el contacto y el 429 de los
/// límites de consulta y de gestión.
/// </summary>
[Collection(ColeccionDelEcommerceCompleto.Nombre)]
public sealed class PedidosConModulosRealesTests(PostgresDelEcommerceCompletoFixture bd) : IAsyncLifetime
{
    private const string NombreDelContacto = "Ana Pérez";

    private const string TelefonoDelContacto = "987654321";

    private const string DireccionDelContacto = "Jr. Lima 123";

    private static readonly Guid EdicionDigital = Guid.Parse("b0000000-0000-0000-0000-0000000000d1");

    private static readonly Guid Administrador = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly ReservasFalsas _reservas = new();

    private readonly ReembolsosFalsos _reembolsos = new();

    public async Task InitializeAsync() =>
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
            COMMIT;
            """);

    public Task DisposeAsync() => Task.CompletedTask;

    private AnfitrionRealDelEcommerce Anfitrion() => bd.Anfitrion(servicios =>
    {
        servicios.RemoveAll<IReservasDelPedido>();
        servicios.AddSingleton<IReservasDelPedido>(_reservas);
        servicios.RemoveAll<IReembolsos>();
        servicios.AddSingleton<IReembolsos>(_reembolsos);
    });

    private static ActorActual DelAdministrador() => ActorActual.DeUsuario(Administrador, RolesDeUsuario.AdministradorCentral, null);

    private static ActorActual DelTitular(Guid cuenta) => ActorActual.DeUsuario(cuenta, RolesDeUsuario.Cliente, null);

    private static CompraAprobada Compra(Guid pedido, Guid cuenta, bool conDigital)
    {
        var ahora = DateTimeOffset.UtcNow;
        var lineas = new List<LineaDeCompraAprobada> { new(Guid.NewGuid(), Guid.NewGuid(), false, "Ficciones", null, 1, 50m, 50m, "lima", false, null, null) };
        if (conDigital)
        {
            lineas.Add(new LineaDeCompraAprobada(Guid.NewGuid(), EdicionDigital, true, "El Aleph (digital)", null, 1, 30m, 30m, null, false, null, null));
        }

        var aceptaciones = new List<AceptacionDeCompra> { new(TextoLegalDeCompra.Terminos, "v1", ahora), new(TextoLegalDeCompra.Privacidad, "v1", ahora) };
        if (conDigital)
        {
            aceptaciones.Add(new AceptacionDeCompra(TextoLegalDeCompra.ReembolsoDigital, "v1", ahora));
        }

        var total = lineas.Sum(l => l.Importe) + 12m;
        return new CompraAprobada(pedido, cuenta, TipoDeEntregaDeCompra.Domicilio, null, "ayacucho", 12m, total, new ContactoDeLaCompra(NombreDelContacto, TelefonoDelContacto, DireccionDelContacto, "ayacucho"), aceptaciones, lineas);
    }

    /// <summary>Una cuenta de cliente real de Identidad (activa, con el correo verificado y los tres textos vigentes aceptados): Pedidos la consulta por la marca de cancelación y por el alcance, y una cuenta inexistente es «ejecutada».</summary>
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

    private async Task<T?> Consultar<T>(string sql, Guid pedido) => await bd.Consultar<T>(sql, ("p", pedido));

    [Fact]
    [Trait("Criterio", "AC-PED-30")]
    [Trait("Criterio", "AC-PED-31")]
    public async Task Dos_Confirmar_simultaneos_con_el_mismo_pedido_crean_uno_solo_y_su_creacion_se_audita_una_vez_sin_el_contacto()
    {
        await using var anfitrion = Anfitrion();
        var pedido = Guid.NewGuid();
        var cuenta = await SembrarCuenta();
        var compra = Compra(pedido, cuenta, conDigital: false);
        var salida = new TaskCompletionSource();

        var tareas = Enumerable.Range(0, 12).Select(_ => Task.Run(async () =>
        {
            await salida.Task;
            return await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<IConfirmacionDeCompra>().Confirmar(compra));
        })).ToList();
        salida.SetResult();
        var resultados = await Task.WhenAll(tareas);

        Assert.All(resultados, r => Assert.Equal(ResultadoDeConfirmarCompra.Creado, r));
        Assert.Equal(1, await Consultar<int>("SELECT count(*) FROM pedidos.pedidos WHERE id = @p", pedido));
        Assert.Equal(1, await Consultar<int>("SELECT count(*) FROM pedidos.lineas_de_pedido WHERE pedido_id = @p", pedido));
        // La creación queda auditada en la misma transacción, una sola vez, y el registro persistido no lleva el contacto.
        Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM auditoria.registros WHERE objeto_id = @o AND accion = 'EstadoDePedidoCambiado'", ("o", pedido.ToString("D"))));
        Assert.Equal(0, await bd.Consultar<int>("SELECT count(*) FROM auditoria.registros WHERE detalle::text ILIKE ANY (ARRAY['%Ana P%', '%987654321%', '%Jr. Lima%'])"));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-26")]
    public async Task ContarPorTipo_informa_los_dos_numeros_de_la_cuenta_con_PostgreSQL_real_y_Contar_devuelve_su_suma_sin_contar_los_de_otra()
    {
        await using var anfitrion = Anfitrion();
        var cuenta = await SembrarCuenta();
        var ajena = await SembrarCuenta();
        Assert.Equal(ResultadoDeConfirmarCompra.Creado, await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<IConfirmacionDeCompra>().Confirmar(Compra(Guid.NewGuid(), cuenta, conDigital: false))));
        Assert.Equal(ResultadoDeConfirmarCompra.Creado, await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<IConfirmacionDeCompra>().Confirmar(Compra(Guid.NewGuid(), cuenta, conDigital: true))));
        Assert.Equal(ResultadoDeConfirmarCompra.Creado, await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<IConfirmacionDeCompra>().Confirmar(Compra(Guid.NewGuid(), ajena, conDigital: false))));
        _reembolsos.PendientesPorCuenta[(cuenta, OrigenDelReembolso.Anulacion)] = 1;
        _reembolsos.PendientesPorCuenta[(cuenta, OrigenDelReembolso.CancelacionDelCliente)] = 2;
        _reembolsos.PendientesPorCuenta[(ajena, OrigenDelReembolso.Anulacion)] = 7;

        var porTipo = await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<IPendientesDeCuentaPorTipo>().ContarPorTipo(cuenta));
        var suma = await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetServices<IPendientesDeCuenta>().OfType<IPendientesDeCuentaPorTipo>().Single().Contar(cuenta));

        Assert.Equal(new PendientesDeCuentaPorTipo(PedidosSinCerrar: 2, ReembolsosPendientes: 3), porTipo);
        Assert.Equal(5, suma);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-11")]
    public async Task Anular_un_pedido_con_un_libro_digital_ya_descargado_revoca_el_acceso_real_y_audita_la_operacion_en_PostgreSQL()
    {
        await using var anfitrion = Anfitrion();
        var pedido = Guid.NewGuid();
        var cuenta = await SembrarCuenta();
        var compra = Compra(pedido, cuenta, conDigital: true);
        Assert.Equal(ResultadoDeConfirmarCompra.Creado, await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<IConfirmacionDeCompra>().Confirmar(compra)));
        Assert.Equal("vigente", await Consultar<string>("SELECT estado FROM biblioteca.accesos WHERE pedido_id = @p", pedido));
        // El cliente ya descargó el libro: la revocación no depende de eso.
        await bd.Ejecutar($"UPDATE biblioteca.accesos SET descargas_usadas = 1, enlaces_entregados = 1 WHERE pedido_id = '{pedido:D}'");

        var resultado = await anfitrion.Como(DelAdministrador(), sp => sp.GetRequiredService<IAdministracionDePedidos>().Anular(new AnularPedidoEntrada(pedido, "otro")));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal(EstadoVisibleDelPedido.Anulado, resultado.Valor.Estado);
        Assert.Equal("revocado", await Consultar<string>("SELECT estado FROM biblioteca.accesos WHERE pedido_id = @p", pedido));
        Assert.Equal("anulado", await Consultar<string>("SELECT estado FROM pedidos.pedidos WHERE id = @p", pedido));
        Assert.Single(_reembolsos.Registrados);
        Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM auditoria.registros WHERE objeto_id = @o AND accion = 'PedidoAnulado' AND actor_tipo = 'usuario'", ("o", pedido.ToString("D"))));
        // La anulación auditada tampoco lleva el contacto del cliente.
        Assert.Equal(0, await bd.Consultar<int>("SELECT count(*) FROM auditoria.registros WHERE objeto_id = @o AND detalle::text ILIKE ANY (ARRAY['%Ana P%', '%987654321%', '%Jr. Lima%'])", ("o", pedido.ToString("D"))));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-11")]
    public async Task Anular_un_pedido_cuya_digital_Devoluciones_ya_reembolso_no_vuelve_a_reembolsarla_y_la_anulacion_termina()
    {
        await using var anfitrion = Anfitrion();
        var pedido = Guid.NewGuid();
        var cuenta = await SembrarCuenta();
        Assert.Equal(ResultadoDeConfirmarCompra.Creado, await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<IConfirmacionDeCompra>().Confirmar(Compra(pedido, cuenta, conDigital: true))));
        // Devoluciones reembolsó el libro digital y Biblioteca revocó su acceso por reembolso (BIB-24).
        await bd.Ejecutar($"UPDATE biblioteca.accesos SET estado = 'revocado', revocado_en = now(), motivo_de_revocacion = 'reembolso' WHERE pedido_id = '{pedido:D}'");

        var resultado = await anfitrion.Como(DelAdministrador(), sp => sp.GetRequiredService<IAdministracionDePedidos>().Anular(new AnularPedidoEntrada(pedido, "otro")));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal(EstadoVisibleDelPedido.Anulado, resultado.Valor.Estado);
        // El reembolso lleva solo el impreso y el envío: pedir también el digital superaría el tope de Pagos y dejaría la anulación fallida.
        var reembolso = Assert.Single(_reembolsos.Registrados);
        Assert.Single(reembolso.Lineas);
        Assert.Equal("anulado", await Consultar<string>("SELECT estado FROM pedidos.pedidos WHERE id = @p", pedido));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-35")]
    public async Task El_limite_de_consultas_de_pedidos_responde_429_con_la_espera_en_el_pedido_numero_61_de_la_misma_persona_y_no_afecta_a_otra()
    {
        await using var anfitrion = Anfitrion();
        var titular = await SembrarCuenta();
        var otra = await SembrarCuenta();

        var respuestas = new List<Resultado<PaginaDePedidos>>();
        for (var i = 0; i < 61; i++)
        {
            respuestas.Add(await anfitrion.Como(DelTitular(titular), sp => sp.GetRequiredService<IPedidosDelCliente>().Listar(new FiltroDePedidos())));
        }

        Assert.All(respuestas.Take(60), r => Assert.True(r.EsExito, r.Error?.Clave));
        var excedida = respuestas[60];
        Assert.False(excedida.EsExito);
        Assert.Equal(CategoriaDeError.Limite, excedida.Error!.Categoria);
        Assert.Contains(excedida.Error.Errores, e => e.Campo == "disponibleDesde");
        Assert.True((await anfitrion.Como(DelTitular(otra), sp => sp.GetRequiredService<IPedidosDelCliente>().Listar(new FiltroDePedidos()))).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-35")]
    public async Task El_limite_de_gestion_responde_429_en_la_operacion_numero_31_y_un_pedido_ajeno_no_se_cancela()
    {
        await using var anfitrion = Anfitrion();
        var titular = await SembrarCuenta();

        var respuestas = new List<Resultado<ResultadoDeCancelacionDePedido>>();
        for (var i = 0; i < 31; i++)
        {
            respuestas.Add(await anfitrion.Como(DelTitular(titular), sp => sp.GetRequiredService<IPedidosDelCliente>().Cancelar(Guid.NewGuid())));
        }

        Assert.All(respuestas.Take(30), r => Assert.Equal(CategoriaDeError.NoEncontrado, r.Error!.Categoria));
        Assert.Equal(CategoriaDeError.Limite, respuestas[30].Error!.Categoria);
        Assert.Empty(_reservas.Liberaciones);
    }
}
