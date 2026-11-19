using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Devoluciones.Interno;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Devoluciones;

/// <summary>Confirmar la recepción de un impreso: el marcador, el reingreso, la recepción y el reembolso (T10; DEV-07, DEV-08, DEV-16, DEV-17, DEV-21; AC-DEV-07, AC-DEV-08, AC-DEV-15, AC-DEV-16, AC-DEV-20).</summary>
public sealed class ConfirmarRecepcionTests
{
    private static readonly DateTimeOffset Ahora = EntornoDeDevoluciones.AhoraInicial;

    /// <summary>Una devolución de dos líneas, ya aprobada, vista por la administración central sobre el mismo almacén.</summary>
    private static async Task<(EntornoDeDevoluciones Entorno, Guid DevolucionId)> ConAprobada(ActorDePrueba? actor = null)
    {
        var cliente = new EntornoDeDevoluciones();
        var a = EntornoDeDevoluciones.Entregada(Ahora.AddDays(-2), 3, 50m);
        var b = EntornoDeDevoluciones.Entregada(Ahora.AddDays(-2), 2, 20m);
        var pedido = cliente.SembrarPedido(a, b);
        var solicitada = await cliente.SolicitarImpreso.Ejecutar(new SolicitarDevolucionDeImpresoEntrada(pedido.PedidoId, [new LineaPedidaParaDevolver(a.LineaId, 2), new LineaPedidaParaDevolver(b.LineaId, 1)], new TextoLibre("Defecto"), "lima-central"));
        Assert.True(solicitada.EsExito);
        var entorno = new EntornoDeDevoluciones(actor ?? ActorDePrueba.DeAdministradorCentral()).ConLosDatosDe(cliente);
        if (entorno.Actor.Actual!.Rol == "encargado-de-sucursal")
        {
            entorno.Sucursales.Codigos[entorno.Actor.Actual.SucursalId!.Value] = "lima-central";
        }

        Assert.True((await entorno.Gestion.Aprobar(solicitada.Valor.DevolucionId)).EsExito);
        return (entorno, solicitada.Valor.DevolucionId);
    }

    private static Devolucion Guardada(EntornoDeDevoluciones entorno, Guid id) => entorno.Repositorio.Obtener(id, false).GetAwaiter().GetResult()!;

    // ---------- Camino feliz ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-07")]
    [Trait("Criterio", "AC-DEV-08")]
    public async Task Confirmar_reingresa_una_vez_con_la_carga_inmutable_pasa_a_recibida_registra_el_reembolso_sin_el_envio_y_lo_deja_reembolsado()
    {
        var (entorno, id) = await ConAprobada();

        var resultado = await entorno.Recepcion.Ejecutar(id);

        Assert.True(resultado.EsExito);
        Assert.All(resultado.Valor.Lineas, l => Assert.Equal(EstadoDeLaLineaDeDevolucion.Reembolsada, l.Estado));
        var reingreso = Assert.Single(entorno.Inventario.Reingresos);
        Assert.Equal(id, reingreso.DevolucionId);
        Assert.Equal("lima-central", reingreso.CodigoDeFuente);
        Assert.Equal(2, reingreso.Lineas.Count);
        Assert.Equal([2, 1], reingreso.Lineas.Select(l => l.Cantidad).Order().Reverse());
        Assert.Equal([100m, 20m], entorno.Reembolsos.Registrados.Select(r => r.Lineas.Single().Monto).OrderDescending());
        Assert.All(entorno.Reembolsos.Registrados, r => Assert.Equal(0m, r.Envio));
        Assert.All(entorno.Reembolsos.Registrados, r => Assert.StartsWith($"dev:{id:D}:", r.ClaveNatural, StringComparison.Ordinal));
        var devolucion = Guardada(entorno, id);
        Assert.NotNull(devolucion.ReingresoIniciadoEn);
        Assert.NotNull(devolucion.ReingresoConfirmadoEn);
        Assert.Null(devolucion.ReingresoIncertoEn);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-07")]
    public async Task El_Inventario_y_la_pasarela_se_llaman_fuera_de_toda_transaccion_y_la_recepcion_iniciada_y_el_reingreso_se_auditan()
    {
        var (entorno, id) = await ConAprobada();

        await entorno.Recepcion.Ejecutar(id);

        Assert.Equal(0, entorno.Inventario.LlamadasDentroDeLaTransaccion);
        Assert.Equal(0, entorno.Reembolsos.ProcesosDentroDeLaTransaccion);
        var acciones = entorno.Auditoria.Solicitudes.Select(s => s.Accion.ToString()).ToList();
        Assert.Contains("ReingresoPorDevolucion", acciones);
        Assert.Contains(entorno.Auditoria.Solicitudes, s => s.Detalle.GetValueOrDefault("estadoNuevo") == "recepcion-iniciada");
        Assert.Equal(2, entorno.Reembolsos.Procesados);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-20")]
    public async Task Con_la_pasarela_caida_las_lineas_quedan_recibidas_con_el_reembolso_pendiente()
    {
        var (entorno, id) = await ConAprobada();
        entorno.Reembolsos.ProcesarResponde = EstadoDelReembolso.Pendiente;

        var resultado = await entorno.Recepcion.Ejecutar(id);

        Assert.True(resultado.EsExito);
        Assert.All(resultado.Valor.Lineas, l =>
        {
            Assert.Equal(EstadoDeLaLineaDeDevolucion.Recibida, l.Estado);
            Assert.True(l.ReembolsoPendiente);
        });
        Assert.Equal(2, entorno.Reembolsos.Registrados.Count);
    }

    // ---------- El Inventario falla (DEV-07, DEV-17) ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-16")]
    public async Task Con_el_Inventario_caido_no_pasa_a_recibida_conserva_el_marcador_fija_el_incierto_y_el_reintento_reingresa_una_sola_vez()
    {
        var (entorno, id) = await ConAprobada();
        entorno.Inventario.Resultado = new ResultadoDelReingreso.Incierto();

        var primero = await entorno.Recepcion.Ejecutar(id);
        entorno.Inventario.Resultado = new ResultadoDelReingreso.Reingresado();
        var reintento = await entorno.Recepcion.Ejecutar(id);

        Assert.Equal(CategoriaDeError.NoDisponible, primero.Error!.Categoria);
        Assert.All(Guardada(entorno, id).Lineas, l => Assert.NotEqual(EstadoDeLineaDeDevolucion.Aprobada, l.Estado));
        Assert.True(reintento.EsExito);
        Assert.Equal(2, entorno.Inventario.Reingresos.Count);
        Assert.Single(entorno.Inventario.Reingresos.Select(r => r.DevolucionId).Distinct());
        Assert.NotNull(Guardada(entorno, id).ReingresoIncertoEn);
        Assert.Equal(2, entorno.Reembolsos.Registrados.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-16")]
    public async Task Una_excepcion_del_adaptador_es_incierta_y_no_propaga_su_texto()
    {
        var (entorno, id) = await ConAprobada();
        entorno.Inventario.Resultado = new ResultadoDelReingreso.Incierto();
        entorno.Inventario.LanzaAlReingresar = true;

        var resultado = await entorno.Recepcion.Ejecutar(id);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.NotNull(Guardada(entorno, id).ReingresoIncertoEn);
        Assert.DoesNotContain("TEXTO-INTERNO", resultado.Error.Clave, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-15")]
    public async Task Un_rechazo_de_negocio_sin_intento_incierto_limpia_el_marcador_alerta_y_la_revision_de_plazos_vuelve_a_aplicar()
    {
        var (entorno, id) = await ConAprobada();
        entorno.Inventario.Resultado = new ResultadoDelReingreso.RechazoDeNegocio();

        var resultado = await entorno.Recepcion.Ejecutar(id);

        Assert.Equal("reingreso-rechazado", resultado.Error!.Clave);
        Assert.Equal(CategoriaDeError.ReglaDeNegocio, resultado.Error.Categoria);
        var devolucion = Guardada(entorno, id);
        Assert.Null(devolucion.ReingresoIniciadoEn);
        Assert.Single(entorno.Alertas.Alertas);
        Assert.Equal(ResultadoDeTransicionDeDevolucion.Aplicada, devolucion.RechazarPorPlazoDeRecepcion(Ahora.AddDays(11)));
        Assert.Single(entorno.Inventario.Reingresos);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-15")]
    public async Task Una_falla_transitoria_seguida_de_un_rechazo_conserva_el_marcador_y_el_plazo_no_rechaza_la_devolucion()
    {
        var (entorno, id) = await ConAprobada();
        entorno.Inventario.Resultado = new ResultadoDelReingreso.Incierto();
        await entorno.Recepcion.Ejecutar(id);
        entorno.Inventario.Resultado = new ResultadoDelReingreso.RechazoDeNegocio();

        var resultado = await entorno.Recepcion.Ejecutar(id);

        Assert.Equal("reingreso-rechazado", resultado.Error!.Clave);
        var devolucion = Guardada(entorno, id);
        Assert.NotNull(devolucion.ReingresoIniciadoEn);
        Assert.NotNull(devolucion.ReingresoIncertoEn);
        Assert.Equal(ResultadoDeTransicionDeDevolucion.ReingresoIniciado, devolucion.RechazarPorPlazoDeRecepcion(Ahora.AddDays(30)));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-15")]
    public async Task Un_conflicto_de_idempotencia_no_limpia_el_marcador_alerta_y_responde_conflicto_sin_que_el_plazo_la_rechace()
    {
        var (entorno, id) = await ConAprobada();
        entorno.Inventario.Resultado = new ResultadoDelReingreso.ConflictoDeIdempotencia();

        var resultado = await entorno.Recepcion.Ejecutar(id);

        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
        Assert.Equal("conflicto", resultado.Error.Clave);
        Assert.Single(entorno.Alertas.Alertas);
        var devolucion = Guardada(entorno, id);
        Assert.NotNull(devolucion.ReingresoIniciadoEn);
        Assert.Equal(ResultadoDeTransicionDeDevolucion.ReingresoIniciado, devolucion.RechazarPorPlazoDeRecepcion(Ahora.AddDays(30)));
        Assert.All(devolucion.Lineas, l => Assert.Equal(EstadoDeLineaDeDevolucion.Aprobada, l.Estado));
    }

    // ---------- Repeticiones y fallas intermedias (DEV-17) ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-16")]
    public async Task Repetir_una_recepcion_completada_es_estado_invalido_sin_reingresar_ni_reembolsar_otra_vez()
    {
        var (entorno, id) = await ConAprobada();
        await entorno.Recepcion.Ejecutar(id);

        var repetida = await entorno.Recepcion.Ejecutar(id);

        Assert.Equal("estado-invalido", repetida.Error!.Clave);
        Assert.Single(entorno.Inventario.Reingresos);
        Assert.Equal(2, entorno.Reembolsos.Registrados.Count);
        Assert.Equal(2, entorno.Reembolsos.Procesados);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-16")]
    public async Task Si_Pagos_no_registra_el_reembolso_la_transaccion_de_recibida_se_revierte_y_el_reintento_salta_el_reingreso_ya_confirmado()
    {
        var (entorno, id) = await ConAprobada();
        entorno.Reembolsos.NoDisponible = true;

        var primero = await entorno.Recepcion.Ejecutar(id);
        entorno.Reembolsos.NoDisponible = false;
        var reintento = await entorno.Recepcion.Ejecutar(id);

        Assert.Equal(CategoriaDeError.NoDisponible, primero.Error!.Categoria);
        Assert.True(reintento.EsExito);
        Assert.Single(entorno.Inventario.Reingresos);
        Assert.Equal(2, entorno.Reembolsos.Registrados.Count);
        Assert.All(reintento.Valor.Lineas, l => Assert.Equal(EstadoDeLaLineaDeDevolucion.Reembolsada, l.Estado));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-07")]
    public async Task Si_no_se_puede_auditar_la_recepcion_iniciada_no_hay_marcador_ni_llamada_al_Inventario()
    {
        var (entorno, id) = await ConAprobada();
        entorno.Auditoria.Caida = true;

        var excepcion = await Record.ExceptionAsync(() => entorno.Recepcion.Ejecutar(id));

        Assert.NotNull(excepcion);
        Assert.Null(Guardada(entorno, id).ReingresoIniciadoEn);
        Assert.Empty(entorno.Inventario.Reingresos);
    }

    // ---------- Alcance y estado ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-12")]
    public async Task Un_encargado_de_otra_sucursal_un_cliente_o_una_devolucion_no_aprobada_no_llegan_al_Inventario()
    {
        var (ajeno, idAjeno) = await ConAprobada(ActorDePrueba.DeEncargadoDe(Guid.NewGuid()));
        ajeno.Sucursales.Codigos[ajeno.Actor.Actual!.SucursalId!.Value] = "arequipa";
        var (cliente, idCliente) = await ConAprobada();
        var soloCliente = new EntornoDeDevoluciones(ActorDePrueba.DeCliente()).ConLosDatosDe(cliente);

        var deOtraSucursal = await ajeno.Recepcion.Ejecutar(idAjeno);
        var comoCliente = await soloCliente.Recepcion.Ejecutar(idCliente);

        Assert.Equal(CategoriaDeError.NoEncontrado, deOtraSucursal.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, comoCliente.Error!.Categoria);
        Assert.Empty(ajeno.Inventario.Reingresos);
        Assert.Empty(soloCliente.Inventario.Reingresos);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-07")]
    public async Task Una_devolucion_solo_solicitada_o_inexistente_no_se_confirma()
    {
        var cliente = new EntornoDeDevoluciones();
        var linea = EntornoDeDevoluciones.Entregada(Ahora.AddDays(-2));
        var pedido = cliente.SembrarPedido(linea);
        var solicitada = await cliente.SolicitarImpreso.Ejecutar(new SolicitarDevolucionDeImpresoEntrada(pedido.PedidoId, [new LineaPedidaParaDevolver(linea.LineaId, 1)], new TextoLibre("x"), "lima-central"));
        var admin = new EntornoDeDevoluciones(ActorDePrueba.DeAdministradorCentral()).ConLosDatosDe(cliente);

        var sinAprobar = await admin.Recepcion.Ejecutar(solicitada.Valor.DevolucionId);
        var inexistente = await admin.Recepcion.Ejecutar(Guid.NewGuid());

        Assert.Equal("estado-invalido", sinAprobar.Error!.Clave);
        Assert.Equal(CategoriaDeError.NoEncontrado, inexistente.Error!.Categoria);
        Assert.Empty(admin.Inventario.Reingresos);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-15")]
    public async Task Un_conflicto_seguido_de_un_rechazo_de_negocio_no_limpia_el_marcador_y_el_plazo_no_rechaza_la_devolucion()
    {
        var (entorno, id) = await ConAprobada();
        entorno.Inventario.Resultado = new ResultadoDelReingreso.ConflictoDeIdempotencia();
        await entorno.Recepcion.Ejecutar(id);
        entorno.Inventario.Resultado = new ResultadoDelReingreso.RechazoDeNegocio();

        await entorno.Recepcion.Ejecutar(id);

        var devolucion = Guardada(entorno, id);
        Assert.NotNull(devolucion.ReingresoIniciadoEn);
        Assert.NotNull(devolucion.ReingresoIncertoEn);
        Assert.Equal(ResultadoDeTransicionDeDevolucion.ReingresoIniciado, devolucion.RechazarPorPlazoDeRecepcion(Ahora.AddDays(30)));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-07")]
    public async Task Dos_confirmaciones_con_el_reingreso_ya_confirmado_no_auditan_dos_veces_el_reingreso()
    {
        var (entorno, id) = await ConAprobada();
        entorno.Reembolsos.ProcesarResponde = EstadoDelReembolso.Pendiente;
        entorno.Reembolsos.NoDisponible = true;
        await entorno.Recepcion.Ejecutar(id);
        entorno.Reembolsos.NoDisponible = false;

        await entorno.Recepcion.Ejecutar(id);

        Assert.Single(entorno.Auditoria.Solicitudes, s => s.Accion.ToString() == "ReingresoPorDevolucion");
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-07")]
    public async Task Un_digital_o_un_reporte_no_se_confirma_ni_llega_al_Inventario_aunque_lo_pida_la_administracion()
    {
        var cliente = new EntornoDeDevoluciones();
        var digital = EntornoDeDevoluciones.Digital();
        var pedido = new PedidoParaDevolucionesParaPruebas(cliente, digital).Pedido;
        cliente.Accesos.Sembrar(pedido.CuentaId, pedido.PedidoId, digital.EdicionId, enlaceEntregado: true);
        await cliente.Reportar.Ejecutar(new ReportarArchivoDefectuosoEntrada(pedido.PedidoId, digital.LineaId, new TextoLibre("x"), new TextoLibre("y")));
        var admin = new EntornoDeDevoluciones(ActorDePrueba.DeAdministradorCentral()).ConLosDatosDe(cliente);

        var resultado = await admin.Recepcion.Ejecutar(cliente.Repositorio.Todas.Single().Id);

        Assert.Equal("estado-invalido", resultado.Error!.Clave);
        Assert.Empty(admin.Inventario.Reingresos);
    }
}
