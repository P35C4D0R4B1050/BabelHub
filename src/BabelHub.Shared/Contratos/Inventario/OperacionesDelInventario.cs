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

    public static IReadOnlyList<OperacionDeContrato> Todas { get; } =
    [
        .. DeDisponibilidad(),
        .. DeReservas(),
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
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.NoEncontrado, CodigosDeErrorDelInventario.EdicionNoImpresa,
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
            "inventario.reservas.consulta-previa", Post, "/reservas/consulta-previa", Usuario, true, false, true, 200, typeof(ConsultaPreviaDto), typeof(ConsultaPreviaRespuestaDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.NoEncontrado, CodigosDeErrorDelInventario.ValidacionInvalida,
            CodigosDeErrorDelInventario.LimiteExcedido, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.reservas.crear", Post, "/reservas", Usuario, true, true, false, 201, typeof(CrearReservaDto), typeof(ReservaDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.NoEncontrado, CodigosDeErrorDelInventario.ValidacionInvalida,
            CodigosDeErrorDelInventario.StockInsuficiente, CodigosDeErrorDelInventario.EdicionNoImpresa, CodigosDeErrorDelInventario.LimiteExcedido,
            CodigosDeErrorDelInventario.IdempotenciaEnConflicto, CodigosDeErrorDelInventario.IdempotenciaEnCurso, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.reservas.consultar", Get, "/reservas/{pedidoId}", Usuario, true, false, true, 200, null, typeof(ReservaDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.reservas.confirmar", Post, "/reservas/{pedidoId}/confirmar", Sistema, false, true, false, 200, null, typeof(ReservaDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.NoEncontrado, CodigosDeErrorDelInventario.ValidacionInvalida,
            CodigosDeErrorDelInventario.ReservaEstadoInvalido, CodigosDeErrorDelInventario.IdempotenciaEnConflicto, CodigosDeErrorDelInventario.IdempotenciaEnCurso,
            CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.reservas.extender", Post, "/reservas/{pedidoId}/extender", Sistema, false, false, false, 200, null, typeof(ExtensionDeReservaDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.NoEncontrado, CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.reservas.liberar", Post, "/reservas/{pedidoId}/liberar", Sistema, false, false, false, 200, typeof(LiberarReservaDto), typeof(ReservaDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.NoEncontrado, CodigosDeErrorDelInventario.ValidacionInvalida,
            CodigosDeErrorDelInventario.ServicioNoDisponible),
        Op(
            "inventario.reservas.cerrar", Post, "/reservas/{pedidoId}/cerrar", Usuario, true, true, false, 200, typeof(CerrarReservaDto), typeof(CierreDeReservaDto),
            CodigosDeErrorDelInventario.NoAutenticado, CodigosDeErrorDelInventario.Prohibido, CodigosDeErrorDelInventario.NoEncontrado,
            CodigosDeErrorDelInventario.ValidacionInvalida, CodigosDeErrorDelInventario.ReservaEstadoInvalido, CodigosDeErrorDelInventario.IdempotenciaEnConflicto,
            CodigosDeErrorDelInventario.IdempotenciaEnCurso, CodigosDeErrorDelInventario.ServicioNoDisponible),
    ];
}
