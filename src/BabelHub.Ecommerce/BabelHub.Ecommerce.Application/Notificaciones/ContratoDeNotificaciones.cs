using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Notificaciones;

// El contrato público de Notificaciones (T03; NOT-01, NOT-05, NOT-08, NOT-13): modelos propios, sin tipos del Dominio ni de Shared. Los demás módulos dependen solo de esta raíz.

/// <summary>A quién se avisa (NOT-01): el cliente o el contacto registrado de una sucursal.</summary>
public enum DestinatarioDelAviso
{
    Cliente,
    Sucursal,
}

/// <summary>
/// Lo que un módulo pide: el <b>evento</b> de negocio (determinista, <c>pedido:123:listo-para-recojo</c>), el destinatario como <b>referencia opaca</b> (el id de la cuenta o el código de la sucursal: nunca un número de teléfono), la
/// plantilla del catálogo cerrado y sus <b>campos cerrados y tipados</b> (sin enlaces, datos de pago, códigos ni texto libre del usuario).
/// </summary>
public sealed record SolicitudDeAviso(string EventoId, DestinatarioDelAviso Destinatario, string DestinatarioRef, string Plantilla, IReadOnlyDictionary<string, string> Campos);

/// <summary>Qué pasó con una solicitud: nunca una excepción (NOT-04).</summary>
public enum ResultadoDeLaSolicitudDeAviso
{
    /// <summary>Quedó pendiente de envío.</summary>
    Creado,

    /// <summary>El cliente no consiente: quedó registrado como omitido por falta de consentimiento y no se enviará (NOT-03).</summary>
    OmitidoSinConsentimiento,

    /// <summary>Ya había un aviso para ese evento, destinatario y plantilla: no se crea otro (NOT-05).</summary>
    YaExistia,

    /// <summary>La plantilla, un campo, el evento o el destinatario no son válidos: no se guardó nada.</summary>
    Rechazado,

    /// <summary>No se pudo guardar por un fallo propio (ya se publicó la alerta técnica): quien llama no debe interrumpir su operación.</summary>
    NoSeGuardo,
}

/// <summary>
/// El puerto con el que cualquier módulo pide un aviso (NOT-01): <b>solo guarda</b> el aviso en la transacción del llamador y <b>nunca lanza ni usa la red</b>; un trabajo de fondo lo envía después. La validación
/// (plantilla, campos y valores) ocurre en memoria antes de tocar la base.
/// </summary>
public interface INotificador
{
    Task<ResultadoDeLaSolicitudDeAviso> Solicitar(SolicitudDeAviso solicitud, CancellationToken cancelacion = default);
}

/// <summary>Una solicitud de aviso que llega del Inventario (un cambio de estado de un traslado, NOT-09): el código de la sucursal destino, nunca un número; el contacto se resuelve al enviar.</summary>
public sealed record SolicitudDeAvisoDelInventario(string EventoId, string Plantilla, string CodigoDeSucursal, IReadOnlyDictionary<string, string> Campos);

/// <summary>La solicitud del Inventario (T10): lo mismo que <see cref="INotificador"/> pero para la sucursal por su código y solo con las plantillas de traslado.</summary>
public interface IRecepcionDeAvisos
{
    Task<ResultadoDeLaSolicitudDeAviso> Recibir(SolicitudDeAvisoDelInventario solicitud, CancellationToken cancelacion = default);
}

/// <summary>El estado de entrega que firma el servicio de envío (NOT-12): de un conjunto cerrado.</summary>
public enum EstadoDeEntrega
{
    Entregado,
    Fallido,
}

/// <summary>Registra el estado de entrega firmado del servicio de envío (T11, NOT-06, NOT-12): solo un estado firmado de un conjunto cerrado marca el aviso como enviado.</summary>
public interface IRegistroDeEstadoDeEntrega
{
    Task<Resultado<ConfirmacionDeNotificaciones>> Registrar(Guid avisoId, EstadoDeEntrega estado, DateTimeOffset marcaDeTiempo, CancellationToken cancelacion = default);
}

/// <summary>Un aviso como lo ve la administración (NOT-06): su estado y sus fechas, <b>sin número</b> y sin campos.</summary>
public sealed record AvisoConsultado(Guid Id, string EventoId, DestinatarioDelAviso Destinatario, string Plantilla, string Estado, int Intentos, DateTimeOffset Creado, DateTimeOffset? FechaFinal, string? MotivoDeCierre);

/// <summary>Una página de avisos consultados.</summary>
public sealed record PaginaDeAvisos(IReadOnlyList<AvisoConsultado> Avisos, int Pagina, int Tamano, int Total);

/// <summary>La consulta administrativa de avisos (AC-NOT-05): solo la administración central.</summary>
public interface IConsultaDeAvisos
{
    Task<Resultado<PaginaDeAvisos>> Listar(string? estado, int pagina, int tamano, CancellationToken cancelacion = default);
}

/// <summary>
/// Desactiva y reactiva los avisos de un número a pedido de su titular (NOT-13): solo la administración central, auditado en la misma transacción con una huella parcial; el número no se guarda, solo su huella, y la
/// exclusión vale para el número escrito de cualquier forma.
/// </summary>
public interface IDesactivacionDeNumeros
{
    Task<Resultado<ConfirmacionDeNotificaciones>> Desactivar(string numero, string motivo, CancellationToken cancelacion = default);

    Task<Resultado<ConfirmacionDeNotificaciones>> Reactivar(string numero, CancellationToken cancelacion = default);
}

/// <summary>El resultado de una operación de Notificaciones que no devuelve datos: sin información que filtrar.</summary>
public sealed record ConfirmacionDeNotificaciones;
