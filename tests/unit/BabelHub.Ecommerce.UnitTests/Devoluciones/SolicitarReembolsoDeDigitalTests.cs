using System.Text.Json;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;

namespace BabelHub.Ecommerce.UnitTests.Devoluciones;

/// <summary>El reembolso de un libro digital sin defecto (T07; DEV-04, DEV-05, DEV-09, DEV-19, DEV-20, DEV-21; AC-DEV-04, AC-DEV-05, AC-DEV-09, AC-DEV-18, AC-DEV-19, AC-DEV-22).</summary>
public sealed class SolicitarReembolsoDeDigitalTests
{
    private static readonly DateTimeOffset Ahora = EntornoDeDevoluciones.AhoraInicial;

    private static PedirReembolsoDeDigitalEntrada Entrada(Guid pedidoId, Guid lineaId, string motivo = "No me gustó") => new(pedidoId, lineaId, new TextoLibre(motivo));

    private static (EntornoDeDevoluciones Entorno, PedidoParaDevoluciones Pedido, LineaDePedidoParaDevoluciones Linea) Preparar(TimeSpan? desdeElPago = null, bool conAcceso = true, EstadoDelAccesoParaDevoluciones estado = EstadoDelAccesoParaDevoluciones.Vigente, bool enlace = false, EstadoVisibleDeLaLinea estadoDeLinea = EstadoVisibleDeLaLinea.DisponibleEnLaBiblioteca)
    {
        var entorno = new EntornoDeDevoluciones();
        var linea = EntornoDeDevoluciones.Digital() with { Estado = estadoDeLinea };
        var pedido = new PedidoParaDevoluciones(Guid.NewGuid(), "PED-2026-0002", entorno.Actor.Actual!.UsuarioId!.Value, Ahora - (desdeElPago ?? TimeSpan.FromDays(3)), [linea]);
        entorno.Pedidos.Sembrar(pedido);
        if (conAcceso)
        {
            entorno.Accesos.Sembrar(pedido.CuentaId, pedido.PedidoId, linea.EdicionId, estado, enlace);
        }

        return (entorno, pedido, linea);
    }

    // ---------- Aprobación automática ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-04")]
    public async Task Sin_enlace_entregado_y_dentro_de_14_dias_la_linea_se_aprueba_por_el_sistema_con_el_acceso_revocado_y_se_reembolsa_al_confirmar_la_pasarela()
    {
        var (entorno, pedido, linea) = Preparar();

        var resultado = await entorno.SolicitarDigital.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId));

        Assert.True(resultado.EsExito);
        var vista = Assert.Single(resultado.Valor.Lineas);
        Assert.Equal(EstadoDeLaLineaDeDevolucion.Reembolsada, vista.Estado);
        Assert.False(vista.ReembolsoPendiente);
        var devolucion = Assert.Single(entorno.Repositorio.Todas);
        var cambios = devolucion.Lineas[0].Cambios;
        Assert.Equal([EstadoDeLineaDeDevolucion.Solicitada, EstadoDeLineaDeDevolucion.Aprobada, EstadoDeLineaDeDevolucion.Reembolsada], cambios.Select(c => c.Estado));
        Assert.Equal(ResponsableDelCambio.Sistema, cambios[1].Responsable);
        Assert.Equal(EstadoDelAccesoParaDevoluciones.Revocado, entorno.Accesos.Estado(pedido.CuentaId, pedido.PedidoId, linea.EdicionId)!.Estado);
        var registrado = Assert.Single(entorno.Reembolsos.Registrados);
        Assert.Equal($"dev:{devolucion.Id:D}:{linea.LineaId:D}", registrado.ClaveNatural);
        Assert.Equal(OrigenDelReembolso.Devolucion, registrado.Origen);
        Assert.Equal(0m, registrado.Envio);
        Assert.Equal(30m, registrado.Lineas[0].Monto);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-09")]
    public async Task El_orden_es_registrar_el_reembolso_antes_de_revocar_el_acceso_la_pasarela_va_fuera_de_transaccion_y_el_Inventario_no_se_toca()
    {
        var (entorno, pedido, linea) = Preparar();

        await entorno.SolicitarDigital.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId));

        Assert.Equal(["consultar", "revocar"], entorno.Accesos.Llamadas);
        Assert.Equal(1, entorno.Reembolsos.Procesados);
        Assert.Equal(0, entorno.Reembolsos.ProcesosDentroDeLaTransaccion);
        Assert.Equal(0, entorno.Inventario.ConsultasDeSucursales);
        Assert.Empty(entorno.Inventario.Reingresos);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-04")]
    public async Task La_auditoria_lleva_la_solicitud_la_aprobacion_y_el_reembolso_sin_el_motivo()
    {
        var (entorno, pedido, linea) = Preparar();

        await entorno.SolicitarDigital.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId, "Mi DNI es 12345678"));

        Assert.Equal(["DevolucionSolicitada", "EstadoDeDevolucionCambiado", "EstadoDeDevolucionCambiado"], entorno.Auditoria.Solicitudes.Select(s => s.Accion.ToString()));
        Assert.DoesNotContain("12345678", JsonSerializer.Serialize(entorno.Auditoria.Solicitudes.Select(s => s.Detalle)), StringComparison.Ordinal);
    }

    // ---------- Pasarela caída (DEV-21) ----------

    [Theory]
    [Trait("Criterio", "AC-DEV-09")]
    [InlineData("pendiente")]
    [InlineData("solicitado")]
    [InlineData("lanza")]
    public async Task Con_la_pasarela_caida_la_linea_sigue_aprobada_con_el_reembolso_pendiente_el_acceso_revocado_y_el_Inventario_sin_tocar(string falla)
    {
        var (entorno, pedido, linea) = Preparar();
        entorno.Reembolsos.ProcesarResponde = falla == "solicitado" ? EstadoDelReembolso.Solicitado : EstadoDelReembolso.Pendiente;
        entorno.Reembolsos.ProcesarLanza = falla == "lanza";

        var resultado = await entorno.SolicitarDigital.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId));

        Assert.True(resultado.EsExito);
        var vista = Assert.Single(resultado.Valor.Lineas);
        Assert.Equal(EstadoDeLaLineaDeDevolucion.Aprobada, vista.Estado);
        Assert.True(vista.ReembolsoPendiente);
        Assert.Equal(EstadoDelAccesoParaDevoluciones.Revocado, entorno.Accesos.Estado(pedido.CuentaId, pedido.PedidoId, linea.EdicionId)!.Estado);
        Assert.Single(entorno.Reembolsos.Registrados);
        Assert.Empty(entorno.Inventario.Reingresos);
    }

    // ---------- Política (DEV-05) ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-05")]
    public async Task Con_un_enlace_ya_entregado_se_rechaza_por_la_politica_sin_crear_nada()
    {
        var (entorno, pedido, linea) = Preparar(enlace: true);

        var resultado = await entorno.SolicitarDigital.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId));

        Assert.Equal("politica-digital", resultado.Error!.Clave);
        Assert.Equal(CategoriaDeError.ReglaDeNegocio, resultado.Error.Categoria);
        Assert.Empty(entorno.Repositorio.Todas);
        Assert.Empty(entorno.Reembolsos.Registrados);
        Assert.DoesNotContain("revocar", entorno.Accesos.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-22")]
    public async Task A_14_dias_exactos_del_pago_se_aprueba_y_a_14_dias_y_una_hora_se_rechaza_con_el_plazo()
    {
        var (exacto, pedidoE, lineaE) = Preparar(TimeSpan.FromDays(14));
        var (vencido, pedidoV, lineaV) = Preparar(TimeSpan.FromDays(14) + TimeSpan.FromHours(1));

        var aprobada = await exacto.SolicitarDigital.Ejecutar(Entrada(pedidoE.PedidoId, lineaE.LineaId));
        var rechazada = await vencido.SolicitarDigital.Ejecutar(Entrada(pedidoV.PedidoId, lineaV.LineaId));

        Assert.True(aprobada.EsExito);
        Assert.Equal("plazo-vencido", rechazada.Error!.Clave);
        Assert.Equal("14", rechazada.Error.Errores[0].Clave);
        Assert.Empty(vencido.Repositorio.Todas);
    }

    // ---------- Línea no devolvible (DEV-19) ----------

    [Theory]
    [Trait("Criterio", "AC-DEV-18")]
    [InlineData("anulada")]
    [InlineData("revocado")]
    [InlineData("suspendido")]
    [InlineData("sin-acceso")]
    public async Task Una_linea_anulada_o_con_el_acceso_revocado_suspendido_o_inexistente_se_rechaza_sin_crear_un_segundo_reembolso(string caso)
    {
        var (entorno, pedido, linea) = caso switch
        {
            "anulada" => Preparar(estadoDeLinea: EstadoVisibleDeLaLinea.Anulado),
            "revocado" => Preparar(estado: EstadoDelAccesoParaDevoluciones.Revocado),
            "suspendido" => Preparar(estado: EstadoDelAccesoParaDevoluciones.Suspendido),
            _ => Preparar(conAcceso: false),
        };

        var resultado = await entorno.SolicitarDigital.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId));

        Assert.Equal("linea-no-devolvible", resultado.Error!.Clave);
        Assert.Empty(entorno.Repositorio.Todas);
        Assert.Empty(entorno.Reembolsos.Registrados);
        Assert.Equal(0, entorno.Reembolsos.Procesados);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-18")]
    public async Task El_tope_de_Pagos_por_una_linea_ya_reembolsada_por_una_anulacion_revierte_toda_la_transaccion()
    {
        var (entorno, pedido, linea) = Preparar();
        entorno.Reembolsos.RechazaPorElTope = true;

        var resultado = await entorno.SolicitarDigital.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId));

        Assert.Equal("linea-no-devolvible", resultado.Error!.Clave);
        Assert.Empty(entorno.Repositorio.Todas);
        Assert.Empty(entorno.Repositorio.Cupos);
        Assert.Empty(entorno.Auditoria.Solicitudes);
        Assert.DoesNotContain("revocar", entorno.Accesos.Llamadas);
    }

    // ---------- La carrera con la entrega del enlace (DEV-20) ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-19")]
    public async Task Si_el_enlace_llega_antes_de_revocar_gana_el_enlace_y_se_revierte_todo_incluido_el_reembolso_registrado()
    {
        var (entorno, pedido, linea) = Preparar();
        entorno.Accesos.EnlaceLlegaAntesDeRevocar = true;

        var resultado = await entorno.SolicitarDigital.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId));

        Assert.Equal("politica-digital", resultado.Error!.Clave);
        Assert.Empty(entorno.Repositorio.Todas);
        Assert.Empty(entorno.Repositorio.Cupos);
        Assert.Empty(entorno.Reembolsos.Registrados);
        Assert.Empty(entorno.Auditoria.Solicitudes);
        Assert.Equal(0, entorno.Reembolsos.Procesados);
        Assert.Equal(EstadoDelAccesoParaDevoluciones.Vigente, entorno.Accesos.Estado(pedido.CuentaId, pedido.PedidoId, linea.EdicionId)!.Estado);
    }

    // ---------- Alcance, cupo y cancelación ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-04")]
    public async Task Un_pedido_ajeno_responde_no_encontrado_con_un_evento_y_sin_consultar_a_Biblioteca()
    {
        var entorno = new EntornoDeDevoluciones();
        var linea = EntornoDeDevoluciones.Digital();
        var ajeno = new PedidoParaDevoluciones(Guid.NewGuid(), "PED-2026-0009", Guid.NewGuid(), Ahora.AddDays(-1), [linea]);
        entorno.Pedidos.Sembrar(ajeno);

        var resultado = await entorno.SolicitarDigital.Ejecutar(Entrada(ajeno.PedidoId, linea.LineaId));

        Assert.Equal(CategoriaDeError.NoEncontrado, resultado.Error!.Categoria);
        Assert.Single(entorno.Denegaciones.Eventos);
        Assert.Empty(entorno.Accesos.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-04")]
    public async Task Una_segunda_solicitud_de_la_misma_linea_no_crea_otro_reembolso()
    {
        var (entorno, pedido, linea) = Preparar();
        var primera = await entorno.SolicitarDigital.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId));

        var segunda = await entorno.SolicitarDigital.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId));

        Assert.True(primera.EsExito);
        Assert.Equal("linea-no-devolvible", segunda.Error!.Clave);
        Assert.Single(entorno.Reembolsos.Registrados);
        Assert.Single(entorno.Repositorio.Todas);
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-28")]
    [InlineData(EstadoDeLaMarca.EnEjecucion)]
    [InlineData(EstadoDeLaMarca.Ejecutada)]
    public async Task Con_la_cancelacion_autorizada_a_ejecutar_no_se_escribe_nada_ni_se_revoca(EstadoDeLaMarca marca)
    {
        var (entorno, pedido, linea) = Preparar();
        entorno.Marca.Estado = marca;

        var resultado = await entorno.SolicitarDigital.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId));

        Assert.Equal("cuenta-en-cancelacion", resultado.Error!.Clave);
        Assert.Empty(entorno.Repositorio.Todas);
        Assert.Empty(entorno.Reembolsos.Registrados);
        Assert.DoesNotContain("revocar", entorno.Accesos.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-04")]
    public async Task Biblioteca_o_Pagos_caidos_no_dejan_nada_y_una_linea_impresa_o_ajena_al_pedido_es_validacion()
    {
        var (bibliotecaCaida, pedidoB, lineaB) = Preparar();
        bibliotecaCaida.Accesos.NoResponde = true;
        var (pagosCaido, pedidoP, lineaP) = Preparar();
        pagosCaido.Reembolsos.NoDisponible = true;
        var impresa = new EntornoDeDevoluciones();
        var linea = EntornoDeDevoluciones.Entregada(Ahora.AddDays(-1));
        var pedidoI = impresa.SembrarPedido(linea);

        var a = await bibliotecaCaida.SolicitarDigital.Ejecutar(Entrada(pedidoB.PedidoId, lineaB.LineaId));
        var b = await pagosCaido.SolicitarDigital.Ejecutar(Entrada(pedidoP.PedidoId, lineaP.LineaId));
        var c = await impresa.SolicitarDigital.Ejecutar(Entrada(pedidoI.PedidoId, linea.LineaId));
        var d = await impresa.SolicitarDigital.Ejecutar(Entrada(pedidoI.PedidoId, Guid.NewGuid()));

        Assert.Equal(CategoriaDeError.NoDisponible, a.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, b.Error!.Categoria);
        Assert.Equal("no-es-digital", c.Error!.Errores[0].Clave);
        Assert.Equal("no-es-del-pedido", d.Error!.Errores[0].Clave);
        Assert.Empty(bibliotecaCaida.Repositorio.Todas);
        Assert.Empty(pagosCaido.Repositorio.Todas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-04")]
    public async Task Un_rol_sin_permiso_o_un_motivo_invalido_se_rechazan_antes_de_leer_nada()
    {
        var (entorno, pedido, linea) = Preparar();
        var sinMotivo = await entorno.SolicitarDigital.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId, "   "));
        var encargado = new EntornoDeDevoluciones(BabelHub.Ecommerce.UnitTests.Soporte.ActorDePrueba.DeEncargadoDe(Guid.NewGuid()));
        var prohibido = await encargado.SolicitarDigital.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId));

        Assert.Equal(CategoriaDeError.Validacion, sinMotivo.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, prohibido.Error!.Categoria);
        Assert.Equal(0, entorno.Pedidos.Lecturas);
        Assert.Equal(0, encargado.Pedidos.Lecturas);
    }
}
