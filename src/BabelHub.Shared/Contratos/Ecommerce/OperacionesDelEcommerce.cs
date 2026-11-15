namespace BabelHub.Shared.Contratos.Ecommerce;

/// <summary>
/// Operaciones que publica el E-commerce (<c>/api/ecommerce/v1/integraciones</c>) y consume el Inventario. Las dos usan el alcance <c>solicitar-aviso</c>,
/// la única credencial del sentido Inventario → E-commerce (<c>transversal-seguridad</c> T08).
/// </summary>
public static class OperacionesDelEcommerce
{
    private const string Raiz = RutasDeServicio.Ecommerce;

    public static IReadOnlyList<OperacionDeContrato> Todas { get; } =
    [
        new(
            "ecommerce.avisos.solicitar-del-inventario", "POST", Raiz + "/integraciones/inventario/avisos", AlcancesDeCredencial.SolicitarAviso,
            false, false, false, 202, typeof(SolicitudDeAvisoDeInventarioDto), null,
            [CodigosDeErrorDelEcommerce.NoAutenticado, CodigosDeErrorDelEcommerce.ValidacionInvalida, CodigosDeErrorDelEcommerce.LimiteExcedido]),
        new(
            "ecommerce.traslados.informar-estado", "POST", Raiz + "/integraciones/inventario/traslados", AlcancesDeCredencial.SolicitarAviso,
            false, false, false, 204, typeof(EstadoDeTrasladoDePedidoDto), null,
            [
                CodigosDeErrorDelEcommerce.NoAutenticado, CodigosDeErrorDelEcommerce.ValidacionInvalida, CodigosDeErrorDelEcommerce.NoEncontrado,
                CodigosDeErrorDelEcommerce.ServicioNoDisponible,
            ]),
        .. DeAuditoria(),
    ];

    /// <summary>
    /// Auditoría, alertas e historial para la administración (<c>transversal-auditoria</c>, AUD-5): operaciones de usuario (token verificable, sin credencial de servicio entre servicios) que atiende la Api del E-commerce y consume la
    /// interfaz web desde el servidor. Los cuerpos están en <c>BabelHub.Shared.Contratos.Auditoria</c>.
    /// </summary>
    private static OperacionDeContrato[] DeAuditoria() =>
    [
        new(
            "ecommerce.auditoria.consultar", "GET", Raiz + "/auditoria", AlcancesDeCredencial.OperacionDeUsuario,
            true, false, true, 200, null, typeof(Auditoria.PaginaDeRegistrosDto),
            [CodigosDeErrorDelEcommerce.NoAutenticado, CodigosDeErrorDelEcommerce.Prohibido, CodigosDeErrorDelEcommerce.ValidacionInvalida, CodigosDeErrorDelEcommerce.ServicioNoDisponible])
        {
            Parametros = ["accion:texto", "usuarioId:guid", "origen:texto", "desde:instante", "hasta:instante", "texto:texto(3-100)", "cursor:texto"],
        },
        new(
            "ecommerce.alertas.listar", "GET", Raiz + "/alertas", AlcancesDeCredencial.OperacionDeUsuario,
            true, false, true, 200, null, typeof(Auditoria.PaginaDeAlertasDto),
            [CodigosDeErrorDelEcommerce.NoAutenticado, CodigosDeErrorDelEcommerce.Prohibido, CodigosDeErrorDelEcommerce.ValidacionInvalida, CodigosDeErrorDelEcommerce.ServicioNoDisponible])
        {
            Parametros = ["cursor:texto"],
        },
        new(
            "ecommerce.alertas.marcar-revisada", "POST", Raiz + "/alertas/{id}/revisada", AlcancesDeCredencial.OperacionDeUsuario,
            true, false, false, 204, typeof(Auditoria.MarcarAlertaRevisadaDto), null,
            [CodigosDeErrorDelEcommerce.NoAutenticado, CodigosDeErrorDelEcommerce.Prohibido, CodigosDeErrorDelEcommerce.ValidacionInvalida, CodigosDeErrorDelEcommerce.ServicioNoDisponible]),
        new(
            "ecommerce.historial.consultar", "GET", Raiz + "/historial", AlcancesDeCredencial.OperacionDeUsuario,
            true, false, true, 200, null, typeof(Auditoria.PaginaDeRegistrosDto),
            [CodigosDeErrorDelEcommerce.NoAutenticado, CodigosDeErrorDelEcommerce.Prohibido, CodigosDeErrorDelEcommerce.ValidacionInvalida, CodigosDeErrorDelEcommerce.ServicioNoDisponible])
        {
            Parametros = ["sucursal:guid", "cursor:texto"],
        },
    ];
}
