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
        ["memoria-de-contadores-alta"] = "La memoria de los contadores de límites es alta",
        ["verificador-de-credenciales-sin-claves"] = "El verificador de credenciales no tiene claves",
        ["revocacion-de-sesiones-pendiente"] = "Hay sesiones por revocar",
        ["acumulado-de-limites-no-disponible"] = "El acumulado de límites no está disponible",
        ["conteo-de-ingresos-no-disponible"] = "El conteo de ingresos fallidos no está disponible",
        ["correo-de-alertas-no-disponible"] = "El correo de alertas no está disponible",
        ["depuracion-diaria-sin-exito"] = "La depuración diaria no se completó en 24 horas",
        ["baja-de-titular-sin-entregar"] = "Una baja de titular no se entregó en 24 horas",
        ["traslado-vinculado-sin-aprobar"] = "Un traslado vinculado sigue sin aprobar a las 48 horas",
        ["lectura-de-alertas-del-inventario-no-disponible"] = "No se pudieron leer las alertas del Inventario",
        ["alcance-de-datos-sin-actor"] = "Una lectura de datos llegó sin actor",
        ["confirmaciones-de-reservas-anomalas"] = "Más de 200 confirmaciones de reservas en 5 minutos",
        ["liberaciones-de-reservas-anomalas"] = "Más de 200 liberaciones de reservas en 5 minutos",
        ["bajas-de-titular-anomalas"] = "Más de 20 bajas de titular en un minuto",
        ["bloqueos-por-textos-pendientes"] = "Muchas acciones bloqueadas por textos legales pendientes",
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
