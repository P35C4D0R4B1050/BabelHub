using System.Reflection;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Ecommerce;
using BabelHub.Shared.Contratos.Auditoria;
using BabelHub.Shared.Contratos.Inventario;

namespace BabelHub.ArchitectureTests.Contratos;

/// <summary>
/// Lo que se congela sobre ajustes, ingresos de mercadería, reingreso por devolución, traslados, historial, las rutas de auditoría y alertas
/// ya fijadas por <c>transversal-auditoria</c>, y los avisos y estados de traslado que el Inventario envía al E-commerce.
/// </summary>
public sealed class ContratosDeAjustesTrasladosYEventosTests
{
    private const string Usuario = AlcancesDeCredencial.OperacionDeUsuario;

    /// <summary>Nombre, método, ruta, alcance, token de usuario, clave de idempotencia, código de éxito, solicitud, respuesta, es de solo lectura.</summary>
    public static TheoryData<string, string, string, string, bool, bool, int, Type?, Type?, bool> Operaciones() => new()
    {
        { "inventario.ajustes.registrar", "POST", "/api/inventario/v1/ajustes", Usuario, true, true, 201, typeof(RegistrarAjusteDto), typeof(AjusteRegistradoDto), false },
        { "inventario.ingresos.registrar", "POST", "/api/inventario/v1/ingresos-de-mercaderia", Usuario, true, true, 201, typeof(RegistrarIngresoDto), typeof(IngresoDeMercaderiaDto), false },
        { "inventario.ingresos.listar", "GET", "/api/inventario/v1/ingresos-de-mercaderia", Usuario, true, false, 200, null, typeof(ListaDeIngresosDto), true },
        { "inventario.devoluciones.reingresar", "POST", "/api/inventario/v1/devoluciones/{devolucionId}/reingreso", Usuario, true, false, 200, typeof(ReingresarDevolucionDto), typeof(ReingresoDeDevolucionDto), false },
        { "inventario.traslados.solicitar", "POST", "/api/inventario/v1/traslados", Usuario, true, false, 201, typeof(SolicitarTrasladoDto), typeof(TrasladoDto), false },
        { "inventario.traslados.listar", "GET", "/api/inventario/v1/traslados", Usuario, true, false, 200, null, typeof(PaginaDto<TrasladoDto>), true },
        { "inventario.traslados.consultar", "GET", "/api/inventario/v1/traslados/{trasladoId}", Usuario, true, false, 200, null, typeof(TrasladoDto), true },
        { "inventario.traslados.aprobar", "POST", "/api/inventario/v1/traslados/{trasladoId}/aprobar", Usuario, true, false, 200, null, typeof(TrasladoDto), false },
        { "inventario.traslados.rechazar", "POST", "/api/inventario/v1/traslados/{trasladoId}/rechazar", Usuario, true, false, 200, typeof(MotivoDeTrasladoDto), typeof(TrasladoDto), false },
        { "inventario.traslados.despachar", "POST", "/api/inventario/v1/traslados/{trasladoId}/despachar", Usuario, true, false, 200, null, typeof(TrasladoDto), false },
        { "inventario.traslados.recibir", "POST", "/api/inventario/v1/traslados/{trasladoId}/recibir", Usuario, true, false, 200, null, typeof(TrasladoDto), false },
        { "inventario.traslados.cancelar", "POST", "/api/inventario/v1/traslados/{trasladoId}/cancelar", Usuario, true, false, 200, typeof(MotivoDeTrasladoDto), typeof(TrasladoDto), false },
        { "inventario.traslados.registrar-guia", "PUT", "/api/inventario/v1/traslados/{trasladoId}/guia", Usuario, true, false, 200, typeof(RegistrarGuiaDto), typeof(TrasladoDto), false },
        { "inventario.historial.consultar", "GET", "/api/inventario/v1/historial", AlcancesDeCredencial.Historial, true, false, 200, null, typeof(HistorialDeSucursalDto), true },
        { "inventario.auditoria.consultar", "GET", "/api/inventario/v1/auditoria", AlcancesDeCredencial.ConsultaDeAuditoria, true, false, 200, null, typeof(PaginaDeRegistrosDto), true },
        { "inventario.alertas.listar", "GET", "/api/inventario/v1/alertas", AlcancesDeCredencial.LecturaDeAlertas, false, false, 200, null, typeof(PaginaDeAlertasDto), true },
        { "inventario.alertas.marcar-revisada", "POST", "/api/inventario/v1/alertas/{id}/revisada", AlcancesDeCredencial.MarcarAlerta, true, false, 204, typeof(MarcarAlertaRevisadaDto), null, false },
        { "ecommerce.auditoria.consultar", "GET", "/api/ecommerce/v1/auditoria", AlcancesDeCredencial.OperacionDeUsuario, true, false, 200, null, typeof(PaginaDeRegistrosDto), true },
        { "ecommerce.alertas.listar", "GET", "/api/ecommerce/v1/alertas", AlcancesDeCredencial.OperacionDeUsuario, true, false, 200, null, typeof(PaginaDeAlertasDto), true },
        { "ecommerce.alertas.marcar-revisada", "POST", "/api/ecommerce/v1/alertas/{id}/revisada", AlcancesDeCredencial.OperacionDeUsuario, true, false, 204, typeof(MarcarAlertaRevisadaDto), null, false },
        { "ecommerce.historial.consultar", "GET", "/api/ecommerce/v1/historial", AlcancesDeCredencial.OperacionDeUsuario, true, false, 200, null, typeof(PaginaDeRegistrosDto), true },
        { "ecommerce.avisos.solicitar-del-inventario", "POST", "/api/ecommerce/v1/integraciones/inventario/avisos", AlcancesDeCredencial.SolicitarAviso, false, false, 202, typeof(SolicitudDeAvisoDeInventarioDto), null, false },
        { "ecommerce.traslados.informar-estado", "POST", "/api/ecommerce/v1/integraciones/inventario/traslados", AlcancesDeCredencial.SolicitarAviso, false, false, 204, typeof(EstadoDeTrasladoDePedidoDto), null, false },
    };

    [Theory]
    [MemberData(nameof(Operaciones))]
    [Trait("Criterio", "AC-TRA-11")]
    public void Cada_operacion_esta_congelada_con_su_ruta_alcance_y_dtos(
        string nombre, string metodo, string ruta, string alcance, bool token, bool clave, int exito, Type? solicitud, Type? respuesta, bool lectura)
    {
        var operacion = CatalogoDeOperaciones.V1.SingleOrDefault(o => o.Nombre == nombre);

        Assert.NotNull(operacion);
        Assert.Equal(metodo, operacion.Metodo);
        Assert.Equal(ruta, operacion.Ruta);
        Assert.Equal(alcance, operacion.Alcance);
        Assert.Equal(token, operacion.RequiereTokenDeUsuario);
        Assert.Equal(clave, operacion.RequiereClaveDeIdempotencia);
        Assert.Equal(exito, operacion.CodigoDeExito);
        Assert.Equal(solicitud, operacion.Solicitud);
        Assert.Equal(respuesta, operacion.Respuesta);
        Assert.Equal(lectura, operacion.EsLectura);
    }

    [Fact]
    public void Los_ajustes_y_los_ingresos_exigen_clave_de_idempotencia_y_el_reingreso_se_identifica_por_la_devolucion()
    {
        var porNombre = CatalogoDeOperaciones.V1.ToDictionary(o => o.Nombre);

        Assert.True(porNombre["inventario.ajustes.registrar"].RequiereClaveDeIdempotencia);
        Assert.True(porNombre["inventario.ingresos.registrar"].RequiereClaveDeIdempotencia);
        Assert.False(porNombre["inventario.devoluciones.reingresar"].RequiereClaveDeIdempotencia);
        Assert.Contains("{devolucionId}", porNombre["inventario.devoluciones.reingresar"].Ruta, StringComparison.Ordinal);
    }

    [Fact]
    public void Los_valores_cerrados_de_ajustes_y_traslados_son_los_de_las_specs()
    {
        Assert.Equal(["merma", "danio", "correccion-de-conteo", "ingreso-de-mercaderia", "devolucion"], MotivosDeAjuste.Todos);
        Assert.Equal(["merma", "danio", "correccion-de-conteo"], MotivosDeAjuste.Manuales);
        Assert.Equal(["solicitado", "aprobado", "en-transito", "recibido", "rechazado", "cancelado"], EstadosDeTraslado.Todos);
        Assert.Equal(["en-transito", "recibido", "cancelado"], EstadosDeTrasladoDePedido.Todos);
    }

    [Fact]
    public void Un_conteo_menor_que_lo_reservado_indica_los_pedidos_que_reservan()
    {
        Assert.Equal(["pedidosQueReservan"], NombresJson(typeof(ProblemaDeConteoDto)).Where(n => n == "pedidosQueReservan").ToArray());
        Assert.Contains("code", NombresJson(typeof(ProblemaDeConteoDto)));
    }

    [Fact]
    public void Cada_codigo_de_error_del_inventario_lo_devuelve_alguna_operacion()
    {
        var declarados = typeof(CodigosDeErrorDelInventario).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetRawConstantValue()!).ToHashSet();
        var usados = CatalogoDeOperaciones.V1.SelectMany(o => o.Errores).ToHashSet();

        Assert.Empty(declarados.Except(usados));
        Assert.Empty(usados.Where(c => c.StartsWith("INV-", StringComparison.Ordinal)).Except(declarados));
    }

    [Fact]
    public void Cada_codigo_de_error_del_ecommerce_lo_devuelve_alguna_operacion()
    {
        var declarados = typeof(CodigosDeErrorDelEcommerce).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetRawConstantValue()!).ToHashSet();
        var usados = CatalogoDeOperaciones.V1.SelectMany(o => o.Errores).ToHashSet();

        Assert.Empty(declarados.Except(usados));
        Assert.Empty(usados.Where(c => c.StartsWith("ECO-", StringComparison.Ordinal)).Except(declarados));
    }

    [Fact]
    public void Los_avisos_del_inventario_llevan_el_evento_determinista_y_solo_campos_de_texto()
    {
        Assert.Equal(["eventoId", "plantilla", "codigoDeSucursal", "campos"], NombresJson(typeof(SolicitudDeAvisoDeInventarioDto)));
        Assert.Equal(
            ["eventoId", "trasladoId", "pedidoId", "edicionId", "cantidad", "estado", "fechaEstimadaDeLlegada", "ocurridoEn"],
            NombresJson(typeof(EstadoDeTrasladoDePedidoDto)));
    }

    private static string[] NombresJson(Type dto) =>
        dto.GetProperties()
            .Where(p => p.Name != "EqualityContract")
            .Select(p => ((System.Text.Json.Serialization.JsonPropertyNameAttribute)Attribute.GetCustomAttribute(p, typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute))!).Name)
            .ToArray();
}
