namespace BabelHub.Shared.Contratos.Inventario;

/// <summary>
/// Operaciones que publica el Inventario (<c>/api/inventario/v1</c>) y consume el E-commerce. Las de reserva que dispara un actor del sistema
/// (confirmar, extender y liberar; una pasarela, un trabajo de fondo o el propio servidor) usan el alcance <c>expiracion-de-reservas</c>, sin token
/// de usuario; las que hace una persona (reservar, consultar, cerrar) usan <c>operacion-de-usuario</c> con su token.
/// </summary>
public static class OperacionesDelInventario
{
    private const string Usuario = AlcancesDeCredencial.OperacionDeUsuario;
    private const string Lectura = AlcancesDeCredencial.LecturaDeDisponibilidad;
    private const string Sistema = AlcancesDeCredencial.ExpiracionDeReservas;
    private const string Get = "GET";
    private const string Post = "POST";
    private const string Put = "PUT";
    private const string Raiz = RutasDeServicio.Inventario;

    /// <summary>
    /// Parámetros de consulta de cada operación (nombre:tipo y tope). Los de paginación siguen <c>docs/03-diseno-de-software/04-modulos-y-contratos.md</c> §2;
    /// <c>edicionId</c> repetido filtra las existencias por las ediciones que el Catálogo resolvió (el Inventario no guarda títulos, autores ni ISBN: FUE-09 se
    /// compone en la web con el Catálogo y esta consulta).
    /// </summary>
    private static readonly Dictionary<string, string[]> ParametrosDeConsulta = new(StringComparer.Ordinal)
    {
        ["inventario.fuentes.existencias"] = ["edicionId:guid[1-50]", "estado:texto", "pagina:int(>=1)", "tamano:int(1-100)"],
        ["inventario.traslados.listar"] = ["estado:texto", "codigoDeFuente:texto", "pagina:int(>=1)", "tamano:int(1-100)"],
        ["inventario.ingresos.listar"] = ["edicionId:guid", "limite:int(1-50)"],
        ["inventario.unidades-vendidas.consultar"] = ["codigoDeFuente:texto"],
        ["inventario.historial.consultar"] = ["pagina:int(>=1)", "tamano:int(1-100)"],
        ["inventario.alertas.listar"] = ["desde:instante"],
        ["inventario.auditoria.consultar"] = ["filtros y cursor: los define transversal-auditoria (AUD-2)"],
    };

    public static IReadOnlyList<OperacionDeContrato> Todas { get; } =
    [
        .. Declaradas().Select(o => ParametrosDeConsulta.TryGetValue(o.Nombre, out var parametros) ? o with { Parametros = parametros } : o),
    ];

    private static OperacionDeContrato[] Declaradas() =>
    [
        .. DeDisponibilidad(),
        .. DeReservas(),
        .. DeFuentes(),
        .. DeTitulares(),
        .. DeAjustes(),
        .. DeTraslados(),
        .. DeAdministracion(),
    ];

    private static OperacionDeContrato Op(
        string nombre, string metodo, string ruta, string alcance, bool token, bool clave, bool lectura, int exito, Type? solicitud, Type? respuesta, params string[] errores) =>
        new(nombre, metodo, Raiz + ruta, alcance, token, clave, lectura, exito, solicitud, respuesta, errores);

    private static OperacionDeContrato[] DeDisponibilidad() =>
    [
        Op(
            "inventario.ediciones.registrar", Put, "/ediciones/{edicionId}", Usuario, true, false, false, 204, typeof(RegistrarEdicionDto), null,
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.ValidacionInvalida,
            CodigosDeErrorDelInventario.FormatoEnConflicto, CodigosDeErrorDelInventario.LimiteExcedido, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.disponibilidad.consultar", Get, "/disponibilidad/{edicionId}", Lectura, false, false, true, 200, null, typeof(DisponibilidadDeEdicionDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.NoEncontrado, CodigosDeErrorDelInventario.EdicionNoImpresa,
            CodigosDeErrorDelInventario.LimiteExcedido, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.disponibilidad.consultar-varias", Post, "/disponibilidad/consultas", Lectura, false, false, true, 200,
            typeof(ConsultaDeDisponibilidadDto), typeof(DisponibilidadDeEdicionesDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.LimiteExcedido,
            CodigosDeErrorDelInventario.ServicioNoDisponible),
    ];

    private static OperacionDeContrato[] DeReservas() =>
    [
        Op(
            "inventario.reservas.consulta-previa", Post, "/reservas/consulta-previa", Lectura, false, false, true, 200, typeof(ConsultaPreviaDto), typeof(ConsultaPreviaRespuestaDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.NoEncontrado, CodigosDeErrorDelInventario.ValidacionInvalida,
            CodigosDeErrorDelInventario.LimiteExcedido, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.reservas.crear", Post, "/reservas", Usuario, true, true, false, 201, typeof(CrearReservaDto), typeof(ReservaDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.NoEncontrado, CodigosDeErrorDelInventario.ValidacionInvalida,
            CodigosDeErrorDelInventario.StockInsuficiente, CodigosDeErrorDelInventario.EdicionNoImpresa, CodigosDeErrorDelInventario.LimiteExcedido,
            CodigosDeErrorDelInventario.IdempotenciaEnConflicto, CodigosDeErrorDelInventario.IdempotenciaEnCurso, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.reservas.consultar", Get, "/reservas/{pedidoId}", Usuario, true, false, true, 200, null, typeof(ReservaDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.reservas.confirmar", Post, "/reservas/{pedidoId}/confirmar", Sistema, false, true, false, 200, null, typeof(ReservaDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.NoEncontrado, CodigosDeErrorDelInventario.ValidacionInvalida,
            CodigosDeErrorDelInventario.ReservaEstadoInvalido, CodigosDeErrorDelInventario.IdempotenciaEnConflicto, CodigosDeErrorDelInventario.IdempotenciaEnCurso,
            CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.reservas.extender", Post, "/reservas/{pedidoId}/extender", Sistema, false, false, false, 200, null, typeof(ExtensionDeReservaDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.NoEncontrado, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.reservas.liberar", Post, "/reservas/{pedidoId}/liberar", Sistema, false, false, false, 200, typeof(LiberarReservaDto), typeof(ReservaDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.NoEncontrado, CodigosDeErrorDelInventario.ValidacionInvalida,
            CodigosDeErrorDelInventario.ReservaEstadoInvalido, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.reservas.cerrar", Post, "/reservas/{pedidoId}/cerrar", Usuario, true, true, false, 200, typeof(CerrarReservaDto), typeof(CierreDeReservaDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.ReservaEstadoInvalido, CodigosDeErrorDelInventario.IdempotenciaEnConflicto,
            CodigosDeErrorDelInventario.IdempotenciaEnCurso, CodigosDeErrorDelInventario.ServicioNoDisponible),
    ];

    private static OperacionDeContrato[] DeFuentes() =>
    [
        Op(
            "inventario.sucursales.listar", Get, "/sucursales", Lectura, false, false, true, 200, null, typeof(SucursalesDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.LimiteExcedido, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.fuentes.listar", Get, "/fuentes", Usuario, true, false, true, 200, null, typeof(ListaDeFuentesDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.fuentes.consultar", Get, "/fuentes/{codigo}", Usuario, true, false, true, 200, null, typeof(FuenteDetalleDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.fuentes.registrar", Post, "/fuentes", Usuario, true, false, false, 201, typeof(RegistrarFuenteDto), typeof(FuenteDetalleDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.ValidacionInvalida,
            CodigosDeErrorDelInventario.FuenteDuplicada, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.fuentes.editar", Put, "/fuentes/{codigo}", Usuario, true, false, false, 200, typeof(EditarFuenteDto), typeof(FuenteDetalleDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.fuentes.designar-recepcion", Put, "/fuentes-de-recepcion", Usuario, true, false, false, 204, typeof(DesignarRecepcionDto), null,
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.fuentes.existencias", Get, "/fuentes/{codigo}/existencias", Usuario, true, false, true, 200, null, typeof(PaginaDto<ExistenciaDto>),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.rutas.listar", Get, "/rutas", Usuario, true, false, true, 200, null, typeof(ListaDeRutasDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.rutas.consultar", Get, "/rutas/{origen}/{destino}", Usuario, true, false, true, 200, null, typeof(RutaDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.rutas.definir", Put, "/rutas/{origen}/{destino}", Usuario, true, false, false, 200, typeof(DefinirRutaDto), typeof(RutaDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.RutaInvalida, CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.unidades-vendidas.consultar", Get, "/unidades-vendidas", Usuario, true, false, true, 200, null, typeof(UnidadesVendidasDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.sucursales.contacto", Get, "/sucursales/{codigo}/contacto-de-notificacion", AlcancesDeCredencial.LecturaDeContacto, false, false, true, 200,
            null, typeof(ContactoDeSucursalDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.NoEncontrado, CodigosDeErrorDelInventario.ServicioNoDisponible),
    ];

    private static OperacionDeContrato[] DeTitulares() =>
    [
        Op(
            "inventario.titulares.consultar-pendientes", Post, "/titulares/consultas-de-pendientes", AlcancesDeCredencial.ConsultaDePendientes, false, false, true, 200,
            typeof(ConsultaDePendientesDto), typeof(PendientesDeTitularDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.titulares.baja", Post, "/bajas-de-titular", AlcancesDeCredencial.BajaDeTitular, false, false, false, 204, typeof(BajaDeTitularDto), null,
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.ServicioNoDisponible),
    ];

    private static OperacionDeContrato[] DeAjustes() =>
    [
        Op(
            "inventario.ajustes.registrar", Post, "/ajustes", Usuario, true, true, false, 201, typeof(RegistrarAjusteDto), typeof(AjusteRegistradoDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.EdicionNoImpresa, CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.StockInsuficiente,
            CodigosDeErrorDelInventario.ConteoMenorQueReservas, CodigosDeErrorDelInventario.SinCambios, CodigosDeErrorDelInventario.LimiteExcedido,
            CodigosDeErrorDelInventario.IdempotenciaEnConflicto, CodigosDeErrorDelInventario.IdempotenciaEnCurso, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.ingresos.registrar", Post, "/ingresos-de-mercaderia", Usuario, true, true, false, 201, typeof(RegistrarIngresoDto), typeof(IngresoDeMercaderiaDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.EdicionNoImpresa, CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.FuenteNoEsDeRecepcion,
            CodigosDeErrorDelInventario.IdempotenciaEnConflicto, CodigosDeErrorDelInventario.IdempotenciaEnCurso, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.ingresos.listar", Get, "/ingresos-de-mercaderia", Usuario, true, false, true, 200, null, typeof(ListaDeIngresosDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.devoluciones.reingresar", Post, "/devoluciones/{devolucionId}/reingreso", Usuario, true, false, false, 200,
            typeof(ReingresarDevolucionDto), typeof(ReingresoDeDevolucionDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.EdicionNoImpresa, CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.ServicioNoDisponible),
    ];

    private static OperacionDeContrato[] DeTraslados() =>
    [
        Op(
            "inventario.traslados.solicitar", Post, "/traslados", Usuario, true, false, false, 201, typeof(SolicitarTrasladoDto), typeof(TrasladoDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.EdicionNoImpresa, CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.StockInsuficiente,
            CodigosDeErrorDelInventario.LimiteExcedido, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.traslados.listar", Get, "/traslados", Usuario, true, false, true, 200, null, typeof(PaginaDto<TrasladoDto>),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.ValidacionInvalida,
            CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.traslados.consultar", Get, "/traslados/{trasladoId}", Usuario, true, false, true, 200, null, typeof(TrasladoDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.traslados.aprobar", Post, "/traslados/{trasladoId}/aprobar", Usuario, true, false, false, 200, null, typeof(TrasladoDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.TrasladoEstadoInvalido, CodigosDeErrorDelInventario.LimiteExcedido, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.traslados.rechazar", Post, "/traslados/{trasladoId}/rechazar", Usuario, true, false, false, 200, typeof(MotivoDeTrasladoDto), typeof(TrasladoDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.TrasladoEstadoInvalido,
            CodigosDeErrorDelInventario.TrasladoVinculadoNoSeRechaza, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.traslados.despachar", Post, "/traslados/{trasladoId}/despachar", Usuario, true, false, false, 200, null, typeof(TrasladoDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.StockInsuficiente, CodigosDeErrorDelInventario.TrasladoEstadoInvalido, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.traslados.recibir", Post, "/traslados/{trasladoId}/recibir", Usuario, true, false, false, 200, null, typeof(TrasladoDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.TrasladoEstadoInvalido, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.traslados.cancelar", Post, "/traslados/{trasladoId}/cancelar", Usuario, true, false, false, 200, typeof(MotivoDeTrasladoDto), typeof(TrasladoDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.TrasladoEstadoInvalido, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.traslados.registrar-guia", Put, "/traslados/{trasladoId}/guia", Usuario, true, false, false, 200, typeof(RegistrarGuiaDto), typeof(TrasladoDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.TrasladoEstadoInvalido, CodigosDeErrorDelInventario.ServicioNoDisponible),
    ];

    /// <summary>
    /// Historial de sucursal, auditoría y alertas: las rutas, los alcances y los errores están fijados por el plan de <c>transversal-auditoria</c>
    /// (sección 4). Aquí solo se congela el historial; los cuerpos de la auditoría y de las alertas los define ese plan (carril A, AUD-2).
    /// </summary>
    private static OperacionDeContrato[] DeAdministracion() =>
    [
        Op(
            "inventario.historial.consultar", Get, "/historial", AlcancesDeCredencial.Historial, true, false, true, 200, null, typeof(HistorialDeSucursalDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.auditoria.consultar", Get, "/auditoria", AlcancesDeCredencial.ConsultaDeAuditoria, true, false, true, 200, null, null,
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.ValidacionInvalida),
        Op(
            "inventario.alertas.listar", Get, "/alertas", AlcancesDeCredencial.LecturaDeAlertas, false, false, true, 200, null, null,
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.Prohibido),
        Op(
            "inventario.alertas.marcar-revisada", Post, "/alertas/{id}/revisada", AlcancesDeCredencial.MarcarAlerta, true, false, false, 204, null, null,
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.ServicioNoDisponible),
    ];
}
