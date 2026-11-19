using System.Text.Json;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Devoluciones;

/// <summary>Reportar un archivo defectuoso y su decisión por la administración central (T08; DEV-06, DEV-09, DEV-18, DEV-22, DEV-26, DEV-28; AC-DEV-06, AC-DEV-21, AC-DEV-23, AC-DEV-24).</summary>
public sealed class ReporteDeArchivoDefectuosoTests
{
    private static readonly DateTimeOffset Ahora = EntornoDeDevoluciones.AhoraInicial;

    private static ReportarArchivoDefectuosoEntrada Entrada(Guid pedidoId, Guid lineaId, string motivo = "El archivo no abre", string texto = "Da error en la página 3") => new(pedidoId, lineaId, new TextoLibre(motivo), new TextoLibre(texto));

    private static (EntornoDeDevoluciones Entorno, PedidoParaDevoluciones Pedido, LineaDePedidoParaDevoluciones Linea) Preparar(TimeSpan? desdeElPago = null, EstadoDelAccesoParaDevoluciones estado = EstadoDelAccesoParaDevoluciones.Vigente, bool conAcceso = true, bool enlace = true)
    {
        var entorno = new EntornoDeDevoluciones();
        var linea = EntornoDeDevoluciones.Digital();
        var pedido = new PedidoParaDevoluciones(Guid.NewGuid(), "PED-2026-0003", entorno.Actor.Actual!.UsuarioId!.Value, Ahora - (desdeElPago ?? TimeSpan.FromDays(20)), [linea]);
        entorno.Pedidos.Sembrar(pedido);
        if (conAcceso)
        {
            entorno.Accesos.Sembrar(pedido.CuentaId, pedido.PedidoId, linea.EdicionId, estado, enlace);
        }

        return (entorno, pedido, linea);
    }

    // ---------- Reportar ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-06")]
    public async Task Un_reporte_de_un_acceso_vigente_dentro_de_30_dias_registra_la_solicitud_con_su_texto_y_suspende_las_descargas()
    {
        var (entorno, pedido, linea) = Preparar();

        var resultado = await entorno.Reportar.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId));

        Assert.True(resultado.EsExito);
        Assert.Equal(OrigenDeLaDevolucion.Reporte, resultado.Valor.Origen);
        Assert.Equal(EstadoDeLaLineaDeDevolucion.Solicitada, Assert.Single(resultado.Valor.Lineas).Estado);
        var guardada = Assert.Single(entorno.Repositorio.Todas);
        Assert.NotNull(guardada.Texto);
        Assert.NotNull(guardada.Lineas[0].AccesoId);
        Assert.Contains("suspender", entorno.Accesos.Llamadas);
        Assert.Empty(entorno.Reembolsos.Registrados);
        Assert.Empty(entorno.Inventario.Reingresos);
        Assert.Equal("DevolucionSolicitada", Assert.Single(entorno.Auditoria.Solicitudes).Accion.ToString());
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-24")]
    public async Task A_30_dias_exactos_se_admite_y_a_30_dias_y_una_hora_se_rechaza_con_el_plazo()
    {
        var (exacto, pedidoE, lineaE) = Preparar(TimeSpan.FromDays(30));
        var (vencido, pedidoV, lineaV) = Preparar(TimeSpan.FromDays(30) + TimeSpan.FromHours(1));

        var admitido = await exacto.Reportar.Ejecutar(Entrada(pedidoE.PedidoId, lineaE.LineaId));
        var rechazado = await vencido.Reportar.Ejecutar(Entrada(pedidoV.PedidoId, lineaV.LineaId));

        Assert.True(admitido.EsExito);
        Assert.Equal("plazo-vencido", rechazado.Error!.Clave);
        Assert.Equal("30", rechazado.Error.Errores[0].Clave);
        Assert.Empty(vencido.Repositorio.Todas);
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-21")]
    [InlineData("revocado")]
    [InlineData("suspendido")]
    [InlineData("sin-acceso")]
    public async Task Un_acceso_revocado_suspendido_o_ajeno_no_admite_un_reporte_y_no_crea_ningun_registro(string caso)
    {
        var (entorno, pedido, linea) = caso switch
        {
            "revocado" => Preparar(estado: EstadoDelAccesoParaDevoluciones.Revocado),
            "suspendido" => Preparar(estado: EstadoDelAccesoParaDevoluciones.Suspendido),
            _ => Preparar(conAcceso: false),
        };

        var resultado = await entorno.Reportar.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId));

        Assert.Equal("reporte-no-admitido", resultado.Error!.Clave);
        Assert.Empty(entorno.Repositorio.Todas);
        Assert.DoesNotContain("suspender", entorno.Accesos.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-21")]
    public async Task Un_segundo_reporte_de_la_misma_linea_se_rechaza_por_el_cupo_y_por_el_acceso_ya_suspendido()
    {
        var (entorno, pedido, linea) = Preparar();
        var primero = await entorno.Reportar.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId));
        // Biblioteca aún muestra el acceso vigente (la carrera): el cupo de la línea lo rechaza igual.
        entorno.Accesos.Sembrar(pedido.CuentaId, pedido.PedidoId, linea.EdicionId);

        var segundo = await entorno.Reportar.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId));

        Assert.True(primero.EsExito);
        Assert.Equal("reporte-no-admitido", segundo.Error!.Clave);
        Assert.Single(entorno.Repositorio.Todas);
        Assert.Equal(1, entorno.Repositorio.Cupos[linea.LineaId].CantidadComprometida);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-23")]
    public async Task Un_pedido_ajeno_es_no_encontrado_con_un_evento_y_con_Biblioteca_caida_no_se_escribe_nada()
    {
        var entorno = new EntornoDeDevoluciones();
        var linea = EntornoDeDevoluciones.Digital();
        var ajeno = new PedidoParaDevoluciones(Guid.NewGuid(), "PED-2026-0009", Guid.NewGuid(), Ahora.AddDays(-5), [linea]);
        entorno.Pedidos.Sembrar(ajeno);
        var (caida, pedido, lineaC) = Preparar();
        caida.Accesos.NoResponde = true;

        var deOtro = await entorno.Reportar.Ejecutar(Entrada(ajeno.PedidoId, linea.LineaId));
        var sinBiblioteca = await caida.Reportar.Ejecutar(Entrada(pedido.PedidoId, lineaC.LineaId));

        Assert.Equal(CategoriaDeError.NoEncontrado, deOtro.Error!.Categoria);
        Assert.Single(entorno.Denegaciones.Eventos);
        Assert.Equal(CategoriaDeError.NoDisponible, sinBiblioteca.Error!.Categoria);
        Assert.Empty(caida.Repositorio.Todas);
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-23")]
    [InlineData(EstadoDeLaMarca.EnEjecucion)]
    [InlineData(EstadoDeLaMarca.Ejecutada)]
    public async Task Con_la_cancelacion_autorizada_a_ejecutar_el_reporte_se_rechaza_sin_escribir_ni_suspender(EstadoDeLaMarca marca)
    {
        var (entorno, pedido, linea) = Preparar();
        entorno.Marca.Estado = marca;

        var resultado = await entorno.Reportar.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId));

        Assert.Equal("cuenta-en-cancelacion", resultado.Error!.Clave);
        Assert.Empty(entorno.Repositorio.Todas);
        Assert.DoesNotContain("suspender", entorno.Accesos.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-06")]
    public async Task El_motivo_y_el_texto_no_llegan_a_la_auditoria_ni_al_resultado_serializado_y_uno_invalido_es_validacion()
    {
        var (entorno, pedido, linea) = Preparar();

        var resultado = await entorno.Reportar.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId, "Mi DNI 12345678", "Mi dirección Jr. Centinela 4321"));
        var invalido = await entorno.Reportar.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId, "ok", "   "));

        Assert.True(resultado.EsExito);
        var observado = JsonSerializer.Serialize(entorno.Auditoria.Solicitudes.Select(s => s.Detalle)) + JsonSerializer.Serialize(resultado.Valor) + resultado.Valor;
        Assert.DoesNotContain("12345678", observado, StringComparison.Ordinal);
        Assert.DoesNotContain("Centinela", observado, StringComparison.Ordinal);
        Assert.Equal("texto", invalido.Error!.Errores[0].Campo);
    }

    // ---------- Decidir (solo la administración central) ----------

    private static async Task<(EntornoDeDevoluciones Entorno, PedidoParaDevoluciones Pedido, LineaDePedidoParaDevoluciones Linea, Guid DevolucionId)> ConReporteAbierto()
    {
        var cliente = new EntornoDeDevoluciones();
        var linea = EntornoDeDevoluciones.Digital();
        var pedido = new PedidoParaDevoluciones(Guid.NewGuid(), "PED-2026-0003", cliente.Actor.Actual!.UsuarioId!.Value, Ahora.AddDays(-5), [linea]);
        cliente.Pedidos.Sembrar(pedido);
        cliente.Accesos.Sembrar(pedido.CuentaId, pedido.PedidoId, linea.EdicionId, enlaceEntregado: true);
        await cliente.Reportar.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId));
        var devolucionId = cliente.Repositorio.Todas.Single().Id;

        // El mismo almacén y los mismos dobles, vistos por la administración central.
        var admin = new EntornoDeDevoluciones(ActorDePrueba.DeAdministradorCentral());
        return (admin.ConLosDatosDe(cliente), pedido, linea, devolucionId);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-23")]
    public async Task Solo_la_administracion_central_decide_un_reporte_un_encargado_o_un_cliente_son_prohibido()
    {
        var (_, pedido, _, devolucionId) = await ConReporteAbierto();
        var encargado = new EntornoDeDevoluciones(ActorDePrueba.DeEncargadoDe(Guid.NewGuid()));
        var cliente = new EntornoDeDevoluciones();

        var a = await encargado.Decidir.Aprobar(devolucionId);
        var b = await cliente.Decidir.Rechazar(devolucionId);

        Assert.Equal(CategoriaDeError.Prohibido, a.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, b.Error!.Categoria);
        Assert.Equal(0, encargado.Pedidos.Lecturas + cliente.Pedidos.Lecturas);
        Assert.NotEqual(Guid.Empty, pedido.PedidoId);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-06")]
    public async Task Aprobar_un_reporte_pasa_la_linea_a_aprobada_registra_el_reembolso_revoca_el_acceso_y_la_deja_reembolsada_al_confirmar_la_pasarela()
    {
        var (admin, pedido, linea, devolucionId) = await ConReporteAbierto();

        var resultado = await admin.Decidir.Aprobar(devolucionId);

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDeLaLineaDeDevolucion.Reembolsada, Assert.Single(resultado.Valor.Lineas).Estado);
        var guardada = Assert.Single(admin.Repositorio.Todas);
        Assert.Equal(TipoDeResponsable.Personal, guardada.Lineas[0].Cambios[1].Responsable.Tipo);
        Assert.Equal(ResponsableDelCambio.Sistema, guardada.Lineas[0].Cambios[2].Responsable);
        Assert.Equal((pedido.PedidoId, linea.EdicionId, MotivoDeRevocacionDeAcceso.ReporteAprobado), Assert.Single(admin.Biblioteca.Revocados));
        Assert.Equal($"dev:{devolucionId:D}:{linea.LineaId:D}", Assert.Single(admin.Reembolsos.Registrados).ClaveNatural);
        Assert.Equal(0, admin.Reembolsos.ProcesosDentroDeLaTransaccion);
        Assert.Empty(admin.Inventario.Reingresos);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-06")]
    public async Task Con_la_pasarela_caida_el_reporte_aprobado_queda_aprobado_con_el_reembolso_pendiente_y_el_acceso_revocado()
    {
        var (admin, _, _, devolucionId) = await ConReporteAbierto();
        admin.Reembolsos.ProcesarResponde = EstadoDelReembolso.Pendiente;

        var resultado = await admin.Decidir.Aprobar(devolucionId);

        var vista = Assert.Single(resultado.Valor.Lineas);
        Assert.Equal(EstadoDeLaLineaDeDevolucion.Aprobada, vista.Estado);
        Assert.True(vista.ReembolsoPendiente);
        Assert.Single(admin.Biblioteca.Revocados);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-24")]
    public async Task Aprobar_sobre_una_linea_ya_reembolsada_o_con_el_acceso_revocado_revierte_y_responde_linea_no_devolvible()
    {
        var (porTope, _, _, idTope) = await ConReporteAbierto();
        porTope.Reembolsos.RechazaPorElTope = true;
        var (revocado, _, _, idRevocado) = await ConReporteAbierto();
        revocado.Biblioteca.YaRevocado = true;

        var a = await porTope.Decidir.Aprobar(idTope);
        var b = await revocado.Decidir.Aprobar(idRevocado);

        Assert.Equal("linea-no-devolvible", a.Error!.Clave);
        Assert.Equal("linea-no-devolvible", b.Error!.Clave);
        Assert.All(new[] { porTope, revocado }, e => Assert.Equal(EstadoDeLineaDeDevolucion.Solicitada, e.Repositorio.Todas.Single().Lineas[0].Estado));
        Assert.Empty(revocado.Reembolsos.Registrados);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-24")]
    public async Task Rechazar_devuelve_el_cupo_reanuda_las_descargas_y_deja_pedir_otro_reporte_dentro_de_los_30_dias()
    {
        var (admin, pedido, linea, devolucionId) = await ConReporteAbierto();

        var resultado = await admin.Decidir.Rechazar(devolucionId);

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDeLaLineaDeDevolucion.Rechazada, Assert.Single(resultado.Valor.Lineas).Estado);
        Assert.Equal(0, admin.Repositorio.Cupos[linea.LineaId].CantidadComprometida);
        Assert.Contains("reanudar", admin.Accesos.Llamadas);
        Assert.Empty(admin.Reembolsos.Registrados);
        Assert.Equal(CategoriaDeError.Conflicto, (await admin.Decidir.Rechazar(devolucionId)).Error!.Categoria);
        Assert.NotEqual(Guid.Empty, pedido.PedidoId);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-24")]
    public async Task Decidir_un_reporte_ya_decidido_es_estado_invalido_y_una_devolucion_que_no_es_un_reporte_es_no_encontrada()
    {
        var (admin, _, _, devolucionId) = await ConReporteAbierto();
        await admin.Decidir.Aprobar(devolucionId);

        var repetida = await admin.Decidir.Aprobar(devolucionId);
        var inexistente = await admin.Decidir.Aprobar(Guid.NewGuid());

        Assert.Equal("estado-invalido", repetida.Error!.Clave);
        Assert.Equal(CategoriaDeError.NoEncontrado, inexistente.Error!.Categoria);
        Assert.Single(admin.Reembolsos.Registrados);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-21")]
    public async Task Con_la_auditoria_caida_el_reporte_se_revierte_y_el_acceso_no_queda_suspendido()
    {
        var (entorno, pedido, linea) = Preparar();
        entorno.Auditoria.Caida = true;

        var excepcion = await Record.ExceptionAsync(() => entorno.Reportar.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId)));

        Assert.NotNull(excepcion);
        Assert.Empty(entorno.Repositorio.Todas);
        Assert.Equal(EstadoDelAccesoParaDevoluciones.Vigente, entorno.Accesos.Estado(pedido.CuentaId, pedido.PedidoId, linea.EdicionId)!.Estado);
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-21")]
    [InlineData("yaSuspendido")]
    [InlineData("noDisponible")]
    public async Task Si_Biblioteca_no_puede_suspender_el_reporte_se_rechaza_sin_crear_nada(string caso)
    {
        var (entorno, pedido, linea) = Preparar();
        entorno.Accesos.SuspenderResponde = caso == "yaSuspendido" ? ResultadoDeSuspenderAcceso.YaSuspendido : ResultadoDeSuspenderAcceso.NoDisponible;

        var resultado = await entorno.Reportar.Ejecutar(Entrada(pedido.PedidoId, linea.LineaId));

        Assert.Equal("reporte-no-admitido", resultado.Error!.Clave);
        Assert.Empty(entorno.Repositorio.Todas);
        Assert.Empty(entorno.Repositorio.Cupos);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-24")]
    public async Task Rechazar_tolera_que_Reanudar_responda_no_disponible_pero_una_falla_tecnica_de_Biblioteca_revierte_y_el_reporte_sigue_abierto()
    {
        var (tolerado, _, _, idTolerado) = await ConReporteAbierto();
        tolerado.Accesos.ReanudarResponde = ResultadoDeReanudarAcceso.NoDisponible;
        var (falla, _, linea, idFalla) = await ConReporteAbierto();
        falla.Accesos.ReanudarFalla = true;

        var cerrado = await tolerado.Decidir.Rechazar(idTolerado);
        var revertido = await falla.Decidir.Rechazar(idFalla);

        Assert.True(cerrado.EsExito);
        Assert.Equal(CategoriaDeError.NoDisponible, revertido.Error!.Categoria);
        Assert.Equal(EstadoDeLineaDeDevolucion.Solicitada, falla.Repositorio.Todas.Single().Lineas[0].Estado);
        Assert.Equal(1, falla.Repositorio.Cupos[linea.LineaId].CantidadComprometida);
    }
}
