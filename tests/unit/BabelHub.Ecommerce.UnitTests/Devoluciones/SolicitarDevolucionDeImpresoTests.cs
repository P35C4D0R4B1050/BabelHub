using System.Text.Json;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Devoluciones;

/// <summary>Solicitar la devolución de un impreso (T06; DEV-01, DEV-02, DEV-03, DEV-15, DEV-18, DEV-19, DEV-26; AC-DEV-01, AC-DEV-02, AC-DEV-03, AC-DEV-14, AC-DEV-17, AC-DEV-28).</summary>
public sealed class SolicitarDevolucionDeImpresoTests
{
    private static readonly DateTimeOffset Ahora = EntornoDeDevoluciones.AhoraInicial;

    private static SolicitarDevolucionDeImpresoEntrada Entrada(Guid pedidoId, Guid lineaId, int cantidad = 1, string sucursal = "lima-central", string motivo = "Llegó con las hojas dobladas") =>
        new(pedidoId, [new LineaPedidaParaDevolver(lineaId, cantidad)], new TextoLibre(motivo), sucursal);

    private static (EntornoDeDevoluciones Entorno, PedidoParaDevoluciones Pedido, LineaDePedidoParaDevoluciones Linea) Preparar(DateTimeOffset? entregadaEn = null, int cantidad = 3, ActorDePrueba? actor = null)
    {
        var entorno = new EntornoDeDevoluciones(actor);
        var linea = EntornoDeDevoluciones.Entregada(entregadaEn ?? Ahora.AddDays(-2), cantidad);
        return (entorno, entorno.SembrarPedido(linea), linea);
    }

    // ---------- Camino feliz ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-01")]
    public async Task Una_solicitud_valida_registra_la_devolucion_con_sus_lineas_solicitadas_el_responsable_cliente_y_la_auditoria_sin_el_motivo()
    {
        var (entorno, pedido, linea) = Preparar();

        var resultado = await entorno.SolicitarImpreso.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId, 2));

        Assert.True(resultado.EsExito);
        var vista = resultado.Valor;
        Assert.Equal(TipoDeLaDevolucion.Impreso, vista.Tipo);
        Assert.Equal("lima-central", vista.CodigoDeSucursalReceptora);
        var l = Assert.Single(vista.Lineas);
        Assert.Equal(EstadoDeLaLineaDeDevolucion.Solicitada, l.Estado);
        Assert.Equal(100m, l.Monto);
        Assert.False(l.ReembolsoPendiente);
        var guardada = Assert.Single(entorno.Repositorio.Todas);
        Assert.Equal(ResponsableDelCambio.Cliente(entorno.Actor.Actual!.UsuarioId!.Value), guardada.Lineas[0].Cambios[0].Responsable);
        var auditoria = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.DevolucionSolicitada, auditoria.Accion);
        Assert.Equal(["cantidadDeLineas", "moneda", "monto", "pedidoId"], auditoria.Detalle.Keys.Order());
        Assert.DoesNotContain("hojas", JsonSerializer.Serialize(auditoria.Detalle), StringComparison.Ordinal);
        Assert.Equal(2, entorno.Repositorio.Cupos[linea.LineaId].CantidadComprometida);
    }

    // ---------- Orden de comprobación ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-28")]
    public async Task Un_pedido_ajeno_o_inexistente_responde_lo_mismo_con_un_evento_y_sin_llamar_al_Inventario()
    {
        var entorno = new EntornoDeDevoluciones();
        var ajeno = new PedidoParaDevoluciones(Guid.NewGuid(), "PED-2026-0009", Guid.NewGuid(), Ahora.AddDays(-9), [EntornoDeDevoluciones.Entregada(Ahora.AddDays(-2))]);
        entorno.Pedidos.Sembrar(ajeno);

        var deOtro = await entorno.SolicitarImpreso.Ejecutar(Entrada(ajeno.PedidoId, ajeno.Lineas[0].LineaId));
        var inexistente = await entorno.SolicitarImpreso.Ejecutar(Entrada(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(CategoriaDeError.NoEncontrado, deOtro.Error!.Categoria);
        Assert.Equal(deOtro.Error, inexistente.Error);
        Assert.Equal(2, entorno.Denegaciones.Eventos.Count);
        Assert.Equal(0, entorno.Inventario.ConsultasDeSucursales);
        Assert.Empty(entorno.Repositorio.Todas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-28")]
    public async Task Un_rol_sin_permiso_se_rechaza_antes_de_leer_nada_ni_consumir_un_limite()
    {
        var (entorno, pedido, linea) = Preparar(actor: ActorDePrueba.DeEncargadoDe(Guid.NewGuid()));

        var resultado = await entorno.SolicitarImpreso.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId));

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(0, entorno.Pedidos.Lecturas);
        Assert.Empty(entorno.Limites.Reservas);
        Assert.Single(entorno.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-28")]
    public async Task Con_textos_pendientes_o_el_limite_excedido_no_se_lee_ni_se_escribe_nada()
    {
        var (pendientes, pedidoP, lineaP) = Preparar();
        pendientes.Cuentas.TextosPendientes = true;
        var (limitado, pedidoL, lineaL) = Preparar();
        limitado.Limites.Maximos["operaciones-de-devoluciones-por-usuario"] = 0;

        var textos = await pendientes.SolicitarImpreso.Ejecutar(Entrada(pedidoP.PedidoId, lineaP.LineaId));
        var limite = await limitado.SolicitarImpreso.Ejecutar(Entrada(pedidoL.PedidoId, lineaL.LineaId));

        Assert.Equal("textos-pendientes", textos.Error!.Clave);
        Assert.Equal(CategoriaDeError.Limite, limite.Error!.Categoria);
        Assert.Equal(0, pendientes.Pedidos.Lecturas + limitado.Pedidos.Lecturas);
        Assert.Empty(pendientes.Repositorio.Todas.Concat(limitado.Repositorio.Todas));
    }

    // ---------- Validación ----------

    [Theory]
    [Trait("Criterio", "AC-DEV-01")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("con\nsalto")]
    public async Task Un_motivo_invalido_es_validacion_y_no_repite_lo_escrito(string motivo)
    {
        var (entorno, pedido, linea) = Preparar();

        var resultado = await entorno.SolicitarImpreso.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId, motivo: motivo));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal("motivo", Assert.Single(resultado.Error.Errores).Campo);
        Assert.Equal(0, entorno.Pedidos.Lecturas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-01")]
    public async Task Una_sucursal_que_no_esta_entre_las_activas_es_validacion_y_con_el_Inventario_caido_no_se_escribe_nada()
    {
        var (desconocida, pedidoD, lineaD) = Preparar();
        var (caido, pedidoC, lineaC) = Preparar();
        caido.Inventario.NoResponde = true;
        var (lanza, pedidoL, lineaL) = Preparar();
        lanza.Inventario.Lanza = true;

        var porCodigo = await desconocida.SolicitarImpreso.Ejecutar(Entrada(pedidoD.PedidoId, lineaD.LineaId, sucursal: "no-existe"));
        var sinRespuesta = await caido.SolicitarImpreso.Ejecutar(Entrada(pedidoC.PedidoId, lineaC.LineaId));
        var excepcion = await lanza.SolicitarImpreso.Ejecutar(Entrada(pedidoL.PedidoId, lineaL.LineaId));

        Assert.Equal("sucursalReceptora", Assert.Single(porCodigo.Error!.Errores).Campo);
        Assert.Equal(CategoriaDeError.NoDisponible, sinRespuesta.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, excepcion.Error!.Categoria);
        Assert.Empty(desconocida.Repositorio.Todas.Concat(caido.Repositorio.Todas).Concat(lanza.Repositorio.Todas));
        Assert.Empty(caido.Auditoria.Solicitudes);
    }

    // ---------- Política de las líneas ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-02")]
    public async Task Una_linea_no_entregada_o_anulada_se_rechaza_sin_crear_nada()
    {
        var entorno = new EntornoDeDevoluciones();
        var enReparto = EntornoDeDevoluciones.Entregada(Ahora, estado: EstadoVisibleDeLaLinea.Despachado);
        var anulada = EntornoDeDevoluciones.Entregada(Ahora, estado: EstadoVisibleDeLaLinea.Anulado);
        var pedido = entorno.SembrarPedido(enReparto, anulada);

        var noEntregada = await entorno.SolicitarImpreso.Ejecutar(Entrada(pedido.PedidoId, enReparto.LineaId));
        var anuladaR = await entorno.SolicitarImpreso.Ejecutar(Entrada(pedido.PedidoId, anulada.LineaId));

        Assert.Equal("linea-no-entregada", noEntregada.Error!.Clave);
        Assert.Equal("linea-no-devolvible", anuladaR.Error!.Clave);
        Assert.Equal(CategoriaDeError.ReglaDeNegocio, noEntregada.Error.Categoria);
        Assert.Empty(entorno.Repositorio.Todas);
        Assert.Empty(entorno.Repositorio.Cupos);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-14")]
    public async Task Un_impreso_entregado_hace_15_dias_exactos_se_acepta_y_a_15_dias_y_una_hora_se_rechaza_con_el_plazo()
    {
        var (enPlazo, pedidoA, lineaA) = Preparar(Ahora.AddDays(-15));
        var (vencido, pedidoB, lineaB) = Preparar(Ahora.AddDays(-15).AddHours(-1));

        var aceptada = await enPlazo.SolicitarImpreso.Ejecutar(Entrada(pedidoA.PedidoId, lineaA.LineaId));
        var rechazada = await vencido.SolicitarImpreso.Ejecutar(Entrada(pedidoB.PedidoId, lineaB.LineaId));

        Assert.True(aceptada.EsExito);
        Assert.Equal("plazo-vencido", rechazada.Error!.Clave);
        Assert.Equal(("plazo-en-dias", "15"), (rechazada.Error.Errores[0].Campo, rechazada.Error.Errores[0].Clave));
        Assert.Empty(vencido.Repositorio.Todas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-01")]
    public async Task Una_linea_digital_en_la_solicitud_de_un_impreso_es_tipos_mezclados()
    {
        var entorno = new EntornoDeDevoluciones();
        var impresa = EntornoDeDevoluciones.Entregada(Ahora.AddDays(-1));
        var digital = EntornoDeDevoluciones.Digital();
        var pedido = entorno.SembrarPedido(impresa, digital);
        var mezcla = new SolicitarDevolucionDeImpresoEntrada(pedido.PedidoId, [new LineaPedidaParaDevolver(impresa.LineaId, 1), new LineaPedidaParaDevolver(digital.LineaId, 1)], new TextoLibre("x"), "lima-central");

        var resultado = await entorno.SolicitarImpreso.Ejecutar(mezcla);

        Assert.Equal("tipos-mezclados", resultado.Error!.Clave);
        Assert.Empty(entorno.Repositorio.Todas);
    }

    // ---------- Cupo (DEV-03, DEV-18) ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-03")]
    public async Task Pedir_mas_unidades_de_las_compradas_se_rechaza_y_tampoco_se_puede_devolver_lo_ya_comprometido()
    {
        var (entorno, pedido, linea) = Preparar(cantidad: 3);

        var demasiadas = await entorno.SolicitarImpreso.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId, 4));
        var primera = await entorno.SolicitarImpreso.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId, 2));
        var segunda = await entorno.SolicitarImpreso.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId, 2));

        Assert.Equal("cantidad-excedida", demasiadas.Error!.Clave);
        Assert.True(primera.EsExito);
        Assert.Equal("cantidad-excedida", segunda.Error!.Clave);
        Assert.Single(entorno.Repositorio.Todas);
        Assert.Equal(2, entorno.Repositorio.Cupos[linea.LineaId].CantidadComprometida);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-03")]
    public async Task Si_la_segunda_linea_no_alcanza_el_cupo_de_la_primera_se_revierte_y_no_queda_ningun_registro()
    {
        var entorno = new EntornoDeDevoluciones();
        var a = EntornoDeDevoluciones.Entregada(Ahora.AddDays(-1), 1);
        var b = EntornoDeDevoluciones.Entregada(Ahora.AddDays(-1), 1);
        var pedido = entorno.SembrarPedido(a, b);
        var primera = await entorno.SolicitarImpreso.Ejecutar(Entrada(pedido.PedidoId, b.LineaId));
        Assert.True(primera.EsExito);
        var pareja = new SolicitarDevolucionDeImpresoEntrada(pedido.PedidoId, [new LineaPedidaParaDevolver(a.LineaId, 1), new LineaPedidaParaDevolver(b.LineaId, 1)], new TextoLibre("x"), "lima-central");

        var resultado = await entorno.SolicitarImpreso.Ejecutar(pareja);

        Assert.Equal("cantidad-excedida", resultado.Error!.Clave);
        Assert.Single(entorno.Repositorio.Todas);
        Assert.Equal(0, entorno.Repositorio.Cupos.GetValueOrDefault(a.LineaId)?.CantidadComprometida ?? 0);
        Assert.Single(entorno.Auditoria.Solicitudes);
    }

    // ---------- Cancelación de la cuenta (DEV-26) ----------

    [Theory]
    [Trait("Criterio", "AC-DEV-28")]
    [InlineData(EstadoDeLaMarca.EnEjecucion)]
    [InlineData(EstadoDeLaMarca.Ejecutada)]
    public async Task Con_la_cancelacion_autorizada_a_ejecutar_se_rechaza_sin_escribir_ningun_registro(EstadoDeLaMarca marca)
    {
        var (entorno, pedido, linea) = Preparar();
        entorno.Marca.Estado = marca;

        var resultado = await entorno.SolicitarImpreso.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId));

        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
        Assert.Equal("cuenta-en-cancelacion", resultado.Error.Clave);
        Assert.Empty(entorno.Repositorio.Todas);
        Assert.Empty(entorno.Repositorio.Cupos);
        Assert.Empty(entorno.Auditoria.Solicitudes);
        Assert.Equal(1, entorno.Marca.Lecturas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-28")]
    public async Task Una_cancelacion_diferida_sigue_aceptando_la_solicitud_y_si_Identidad_no_responde_no_se_escribe_nada()
    {
        var (diferida, pedidoD, lineaD) = Preparar();
        diferida.Marca.Estado = EstadoDeLaMarca.Diferida;
        var (caida, pedidoC, lineaC) = Preparar();
        caida.Marca.NoResponde = true;

        var aceptada = await diferida.SolicitarImpreso.Ejecutar(Entrada(pedidoD.PedidoId, lineaD.LineaId));
        var sinIdentidad = await caida.SolicitarImpreso.Ejecutar(Entrada(pedidoC.PedidoId, lineaC.LineaId));

        Assert.True(aceptada.EsExito);
        Assert.Equal(CategoriaDeError.NoDisponible, sinIdentidad.Error!.Categoria);
        Assert.Empty(caida.Repositorio.Todas);
        Assert.Empty(caida.Auditoria.Solicitudes);
    }

    // ---------- Fallas ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-01")]
    public async Task Con_Pedidos_caido_o_el_almacen_caido_no_se_crea_nada_y_con_la_auditoria_caida_la_solicitud_falla()
    {
        var (pedidosCaido, pedidoP, lineaP) = Preparar();
        pedidosCaido.Pedidos.NoResponde = true;
        var (almacen, pedidoA, lineaA) = Preparar();
        almacen.Repositorio.NoDisponible = true;
        var (sinAuditoria, pedidoS, lineaS) = Preparar();
        sinAuditoria.Auditoria.Caida = true;

        var a = await pedidosCaido.SolicitarImpreso.Ejecutar(Entrada(pedidoP.PedidoId, lineaP.LineaId));
        var b = await almacen.SolicitarImpreso.Ejecutar(Entrada(pedidoA.PedidoId, lineaA.LineaId));
        var c = await Record.ExceptionAsync(() => sinAuditoria.SolicitarImpreso.Ejecutar(Entrada(pedidoS.PedidoId, lineaS.LineaId)));

        Assert.Equal(CategoriaDeError.NoDisponible, a.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, b.Error!.Categoria);
        Assert.NotNull(c);
        Assert.Empty(sinAuditoria.Repositorio.Todas);
        Assert.Empty(sinAuditoria.Repositorio.Cupos);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-01")]
    public async Task El_resultado_no_lleva_el_responsable_ni_la_cuenta_ni_el_motivo_en_su_texto()
    {
        var (entorno, pedido, linea) = Preparar();

        var resultado = await entorno.SolicitarImpreso.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId, motivo: "Mi DNI es 12345678"));

        Assert.DoesNotContain("12345678", resultado.Valor.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("12345678", JsonSerializer.Serialize(resultado.Valor), StringComparison.Ordinal);
        Assert.DoesNotContain(entorno.Actor.Actual!.UsuarioId!.Value.ToString(), JsonSerializer.Serialize(resultado.Valor), StringComparison.OrdinalIgnoreCase);
    }
}
