using System.Globalization;
using System.Text.Json;
using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Api.Web.Auditoria;

/// <summary>Un dato del detalle de un registro, ya separado de su JSON: la vista lo pinta como texto (Razor lo codifica, nunca se marca como seguro).</summary>
public sealed record DatoDeDetalleVista(string Clave, string Valor);

/// <summary>Lo que pinta una fila de la pantalla de auditoría (WEB-30; AUD-25): la hora ya está en Lima y los valores son texto sin marcar.</summary>
public sealed record FilaDeAuditoriaVista(
    Guid Id,
    string HoraDeLima,
    string Servicio,
    string Accion,
    string Usuario,
    string? Rol,
    string Resultado,
    string? Objeto,
    string? Origen,
    string? Motivo,
    IReadOnlyList<DatoDeDetalleVista> Detalle,
    bool DetalleTruncado,
    bool Anonimizado);

/// <summary>Una página de la auditoría: sus filas, el cursor de la siguiente, el aviso del servicio que no respondió y el estado estándar de la pantalla (WEB-06).</summary>
public sealed record PaginaDeAuditoriaVista(IReadOnlyList<FilaDeAuditoriaVista> Filas, string? SiguienteCursor, string? Aviso, EstadoVista Estado);

/// <summary>Una alerta de seguridad para la pantalla de alertas (WEB-33; AUD-13): el motivo es un texto fijo del catálogo cerrado, nunca el de una excepción.</summary>
public sealed record AlertaVista(
    string Id,
    string Servicio,
    string CodigoDeMotivo,
    string Motivo,
    string PrimeraOcurrencia,
    string UltimaOcurrencia,
    int Ocurrencias,
    int SinRevisar,
    bool Pendiente,
    string? RevisadaEn,
    int ContadorVisto);

public sealed record PaginaDeAlertasVista(IReadOnlyList<AlertaVista> Alertas, string? SiguienteCursor, string? Aviso, EstadoVista Estado);

/// <summary>
/// El contrato que consumen las pantallas de auditoría y alertas (T29; AC-AUD-16, AC-AUD-22; AUD-18, AUD-25): convierte lo que entrega la Aplicación (<see cref="IConsultasDeAuditoria"/> e <see cref="IConsultaDeAlertas"/>, por su
/// contrato público, regla 9: la interfaz no llama al Inventario) en modelos de vista con <b>la hora de Lima</b> (UTC−5, sin horario de verano), <b>valores en texto sin codificar</b> (la vista Razor los codifica al pintarlos; un
/// valor con marcas HTML llega como texto y nunca se concatena a un fragmento), <b>el aviso de «faltan los registros del Inventario»</b> y los estados estándar (vacío, sin permiso, servicio no disponible, error).
/// Los textos son provisionales hasta que <c>docs/06/08</c> tenga su entrada.
/// </summary>
public static class PresentadorDeAuditoria
{
    public const string AvisoFaltanLosDelInventario = "No pudimos leer los registros del Inventario. Se muestran los del E-commerce; intenta de nuevo en unos minutos.";

    public const string AvisoFaltanLasAlertasDelInventario = "No pudimos leer las alertas del Inventario. Se muestran las del E-commerce; intenta de nuevo en unos minutos.";

    public const string TituloDeAuditoriaVacia = "Sin registros";

    public const string MensajeDeAuditoriaVacia = "No hay registros que coincidan con los filtros.";

    public const string TituloDeAlertasVacias = "Sin alertas";

    public const string MensajeDeAlertasVacias = "No hay alertas de seguridad.";

    private static readonly CultureInfo Cultura = CultureInfo.InvariantCulture;

    /// <summary>Los textos de los motivos del catálogo cerrado de alertas (D06); un motivo que el catálogo no conoce se muestra como «Motivo no catalogado».</summary>
    private static readonly IReadOnlyDictionary<string, string> TextosDeMotivos = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["accesos-denegados"] = "Más de 10 accesos denegados en 5 minutos",
        ["ingresos-fallidos"] = "Más de 20 ingresos fallidos en 5 minutos",
        ["auditoria-no-disponible"] = "La auditoría no está disponible",
        ["credencial-de-servicio-anterior-en-uso"] = "Se usó la credencial de servicio anterior",
        ["dependencia-no-necesaria-fallida"] = "Falló una dependencia no necesaria",
        ["trabajo-de-fondo-detenido"] = "Un trabajo de fondo se detuvo",
        ["trabajo-de-pagos-sin-correr"] = "Un trabajo de fondo de Pagos falló varias veces seguidas o lleva un día sin correr bien",
        ["memoria-de-contadores-alta"] = "La memoria de los contadores de límites es alta",
        ["verificador-de-credenciales-sin-claves"] = "El verificador de credenciales no tiene claves",
        ["revocacion-de-sesiones-pendiente"] = "Hay sesiones por revocar",
        ["acumulado-de-limites-no-disponible"] = "El acumulado de límites no está disponible",
        ["conteo-de-ingresos-no-disponible"] = "El conteo de ingresos fallidos no está disponible",
        ["correo-de-alertas-no-disponible"] = "El correo de alertas no está disponible",
        ["depuracion-diaria-sin-exito"] = "La depuración diaria no se completó en 24 horas",
        ["baja-de-titular-sin-entregar"] = "Una baja de titular no se entregó en 24 horas",
        ["traslado-vinculado-sin-aprobar"] = "Un traslado vinculado sigue sin aprobar a las 48 horas",
        ["aviso-de-traslado-no-entregado"] = "Un aviso de traslado no se entregó en 24 horas",
        ["estado-de-traslado-sin-entregar"] = "El estado de un traslado de pedido lleva 24 horas sin entregarse",
        ["entrega-de-traslado-con-error-permanente"] = "El E-commerce rechazó un mensaje de traslado sin posibilidad de reintento",
        ["credencial-del-ecommerce-rechazada"] = "El E-commerce rechazó la credencial de servicio del Inventario",
        ["lectura-de-alertas-del-inventario-no-disponible"] = "No se pudieron leer las alertas del Inventario",
        ["alcance-de-datos-sin-actor"] = "Una lectura de datos llegó sin actor",
        ["confirmaciones-de-reservas-anomalas"] = "Más de 200 confirmaciones de reservas en 5 minutos",
        ["liberaciones-de-reservas-anomalas"] = "Más de 200 liberaciones de reservas en 5 minutos",
        ["bajas-de-titular-anomalas"] = "Más de 20 bajas de titular en un minuto",
        ["bloqueos-por-textos-pendientes"] = "Muchas acciones bloqueadas por textos legales pendientes",
        ["cuota-de-correo-alta"] = "La cuota diaria de correo llegó al 80 %",
        ["cupo-de-codigos-alto"] = "El cupo de códigos por correo llegó al 80 %",
        ["renovacion-reutilizada"] = "Se reutilizó una renovación de sesión y se cerró su familia",
        ["envio-aviso-rechazado"] = "El Notificador rechazó el aviso de un envío",
        ["envio-guia-repetida"] = "El operador devolvió un número de guía que ya usa otro envío",
        ["envio-progreso-sin-informar"] = "Pedidos no recibió el avance de un envío en más de una hora",
        ["envio-guia-sin-uso"] = "Una guía de un envío cancelado quedó sin uso en el operador",
        ["biblioteca-archivo-faltante"] = "El archivo de un libro digital falta en el almacén y un cliente no pudo descargarlo",
        ["pedido-operacion-fallida"] = "Una anulación o una entrega de un pedido quedó fallida y hay que revisarla",
        ["pedido-liberacion-no-recogido-ya-cerrada"] = "Al marcar un pedido no recogido, la reserva de una línea ya estaba cerrada",
        ["pedido-liberacion-de-anulacion-ya-cerrada"] = "Al anular un pedido, la reserva de una línea ya estaba cerrada",
        ["pedido-estado-de-traslado-de-un-pedido-o-linea-inexistente"] = "El Inventario informó el estado de un traslado de un pedido o una línea que no existen",
        ["pedido-estado-de-traslado-de-una-linea-anulada"] = "El Inventario informó el estado de un traslado de una línea ya anulada",
        ["carrito-trabajo-sin-correr"] = "Un trabajo de fondo de Carrito no corrió en el plazo esperado",
        ["carrito-checkouts-con-el-pago-pendiente-demasiado"] = "Hay checkouts con el pago pendiente por demasiado tiempo",
        ["carrito-clave-de-reserva-en-conflicto"] = "La clave de reserva de un checkout chocó con otra reserva distinta",
        ["carrito-liberacion-de-un-pedido-confirmado"] = "Carrito intentó liberar la reserva de un pedido ya confirmado",
        ["anonimizacion-de-hojas-sin-correr"] = "La anonimización de hojas de reclamación no corrió en 24 horas",
        ["compra-aprobada-sin-pedido-a-los-30-dias"] = "Una compra aprobada sigue sin pedido a los 30 días",
        ["aviso-a-carrito-fallido"] = "No se pudo avisar a Carrito el estado de un pago",
        ["aviso-de-pago-fallido"] = "No se pudo enviar un aviso del estado de un pago",
        ["cargo-duplicado-para-conciliar"] = "La pasarela tiene dos cargos para un mismo intento de pago: concilia a mano",
        ["cierre-de-intenciones-fallido"] = "No se pudieron cerrar intenciones de pago vencidas",
        ["cobro-dudoso-para-conciliar"] = "La pasarela aprobó un cobro que no coincide con el intento: concilia a mano",
        ["compensacion-fallida"] = "Falló un paso de la compensación de un cobro",
        ["compensacion-no-aplicable"] = "Se pidió compensar un cobro que no corresponde",
        ["compra-aprobada-sin-completar"] = "Una compra aprobada no se completó en 30 minutos y se compensó",
        ["compra-aprobada-sin-referencia"] = "Una compra aprobada no tiene la referencia del cargo para retomarla",
        ["confirmacion-de-compra-fallida"] = "Falló la confirmación de una compra aprobada",
        ["consulta-de-pago-fallida"] = "Falló la consulta del estado de un pago pendiente",
        ["purga-de-contacto-fallida"] = "No se pudo eliminar el contacto de entrega de una intención de pago",
        ["reembolso-pedido-sin-registrar"] = "Se pidió un reembolso a la pasarela y no se pudo registrar su resultado",
        ["registro-del-cobro-fallido"] = "No se pudo registrar el resultado de un cobro",
        ["reintento-de-reembolsos-fallido"] = "Falló el reintento de reembolsos pendientes",
        ["reserva-confirmada-sin-liberar"] = "Al compensar un pago no se pudo liberar una reserva ya confirmada",
        ["reserva-confirmada-inexistente"] = "El Inventario no conoce la reserva de una compra aprobada",
        ["reservas-confirmadas-inconsistentes"] = "El Inventario devolvió reservas que no coinciden con una compra aprobada",
        ["retoma-de-pagos-fallida"] = "Falló la retoma de compras o reembolsos a medias",
    };

    public static PaginaDeAuditoriaVista Presentar(PaginaDeRegistros pagina)
    {
        ArgumentNullException.ThrowIfNull(pagina);
        var filas = pagina.Registros.Select(ARegistro).ToList();
        var estado = filas.Count == 0
            ? EstadoVista.Vacio(TituloDeAuditoriaVacia, MensajeDeAuditoriaVacia)
            : EstadoVista.Exito("Auditoría");
        return new PaginaDeAuditoriaVista(filas, pagina.SiguienteCursor, pagina.FaltanDelInventario ? AvisoFaltanLosDelInventario : null, estado);
    }

    public static PaginaDeAlertasVista Presentar(PaginaDeAlertas pagina)
    {
        ArgumentNullException.ThrowIfNull(pagina);
        var alertas = pagina.Alertas.Select(AAlerta).ToList();
        var estado = alertas.Count == 0
            ? EstadoVista.Vacio(TituloDeAlertasVacias, MensajeDeAlertasVacias)
            : EstadoVista.Exito("Alertas de seguridad");
        return new PaginaDeAlertasVista(alertas, pagina.SiguienteCursor, pagina.FaltanDelInventario ? AvisoFaltanLasAlertasDelInventario : null, estado);
    }

    /// <summary>
    /// El estado estándar de un fallo de la Aplicación: prohibido o no autenticado es «sin permiso» (un encargado que abre la pantalla de la administración central ve esa, AC-WEB-25), servicio no disponible es «no disponible»
    /// con su reintento y todo lo demás (un filtro o un cursor inválido) es el error genérico, que nunca lleva el motivo técnico.
    /// </summary>
    public static EstadoVista EstadoDelFallo(ErrorDeAplicacion error, string rutaDeReintento)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Categoria switch
        {
            CategoriaDeError.Prohibido or CategoriaDeError.NoAutenticado => EstadoVista.SinPermiso([]),
            CategoriaDeError.NoDisponible => EstadoVista.ServicioNoDisponible("No podemos mostrar la auditoría ahora.", rutaDeReintento),
            _ => EstadoVista.ErrorGenerico(rutaDeReintento),
        };
    }

    public static FilaDeAuditoriaVista ARegistro(RegistroAuditado registro)
    {
        ArgumentNullException.ThrowIfNull(registro);
        return new FilaDeAuditoriaVista(
            registro.Id,
            registro.InstanteEnLima.ToString("dd/MM/yyyy HH:mm:ss", Cultura),
            registro.Servicio == ServicioAuditado.Ecommerce ? "E-commerce" : "Inventario",
            registro.Accion,
            TextoDelUsuario(registro),
            registro.ActorRol,
            registro.Resultado switch
            {
                ResultadoAuditado.Exitoso => "Correcto",
                ResultadoAuditado.Rechazado => "Rechazado",
                _ => "Fallido",
            },
            registro.ObjetoTipo is null ? null : registro.ObjetoId is null ? registro.ObjetoTipo : $"{registro.ObjetoTipo} {registro.ObjetoId}",
            registro.Origen,
            registro.Motivo,
            DatosDelDetalle(registro.Detalle),
            registro.Truncado,
            registro.AnonimizadoEn is not null);
    }

    public static AlertaVista AAlerta(AlertaConsultada alerta)
    {
        ArgumentNullException.ThrowIfNull(alerta);
        return new AlertaVista(
            alerta.Id,
            alerta.Servicio == ServicioAuditado.Ecommerce ? "E-commerce" : "Inventario",
            alerta.Motivo.Valor,
            TextosDeMotivos.GetValueOrDefault(alerta.Motivo.Valor, "Motivo no catalogado"),
            Lima(alerta.PrimeraOcurrencia),
            Lima(alerta.UltimaOcurrencia),
            alerta.Ocurrencias,
            alerta.Ocurrencias - alerta.RevisadasHasta,
            alerta.Estado == EstadoDeAlertaConsultada.Pendiente,
            alerta.RevisadaEn is { } revisada ? Lima(revisada) : null,
            alerta.Ocurrencias);
    }

    private static string Lima(DateTimeOffset instante) => instante.ToOffset(TimeSpan.FromHours(-5)).ToString("dd/MM/yyyy HH:mm:ss", Cultura);

    private static string TextoDelUsuario(RegistroAuditado registro) => registro.ActorTipo switch
    {
        TipoDeActor.Sistema => "Sistema",
        TipoDeActor.Integracion => "Integración",
        TipoDeActor.Anonimo => "Anónimo",
        _ => registro.ActorId is { } id ? "Usuario " + id.ToString("N")[..8] : "Usuario anonimizado",
    };

    /// <summary>
    /// El detalle (un objeto JSON de texto a texto, de hasta 1000 caracteres) como pares clave-valor. Un detalle que no se puede leer no se muestra ni se corta a la mitad: la fila sale sin datos de detalle. Los valores se devuelven tal como
    /// están guardados: la codificación es de la vista.
    /// </summary>
    private static IReadOnlyList<DatoDeDetalleVista> DatosDelDetalle(string? detalle)
    {
        if (string.IsNullOrWhiteSpace(detalle))
        {
            return [];
        }

        try
        {
            using var documento = JsonDocument.Parse(detalle);
            if (documento.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            return [.. documento.RootElement.EnumerateObject()
                .Where(p => p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                .Select(p => new DatoDeDetalleVista(p.Name, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? string.Empty : p.Value.GetRawText()))];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
