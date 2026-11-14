namespace BabelHub.Ecommerce.Application.Auditoria;

/// <summary>
/// Puerto que <b>Auditoría define e Identidad implementa</b> (D09, D17): escribe un registro de una acción «aislada» en <c>identidad.registros</c>, en la
/// misma transacción de la operación (Auditoría no escribe en un esquema ajeno, regla 13). Un fallo se expresa con una excepción, que la auditoría
/// trata como «no disponible» (AUD-04, AUD-12).
/// </summary>
public interface IAlmacenDeRegistrosAislados
{
    /// <summary>La auditoría de una operación que concede o cambia algo: en la transacción de la operación, y un fallo la revierte (AUD-04).</summary>
    Task Insertar(RegistroAuditado registro, CancellationToken cancelacion = default);

    /// <summary>
    /// Un evento de seguridad que no concede nada (un ingreso fallido, un bloqueo): se escribe con un punto de guardado, de modo que si el
    /// <c>INSERT</c> falla la transacción de quien llama sigue válida y el efecto protector se mantiene (AUD-12, AC-AUD-03). El fallo se lanza ya
    /// sin efecto sobre esa transacción.
    /// </summary>
    Task InsertarAislandoElFallo(RegistroAuditado registro, CancellationToken cancelacion = default);
}

/// <summary>
/// El conteo de una acción en una ventana: una acción del catálogo y un rango de <b>como máximo 5 minutos</b> en UTC. Es lo único que Auditoría le
/// pregunta a Identidad para evaluar el umbral de SEG-09 (los ingresos fallidos viven en <c>identidad.registros</c>): ningún filtro libre y solo un número
/// de vuelta.
/// </summary>
public sealed record SolicitudDeConteo
{
    public static readonly TimeSpan VentanaMaxima = TimeSpan.FromMinutes(5);

    public SolicitudDeConteo(AccionAuditable accion, DateTimeOffset desde, DateTimeOffset hasta)
    {
        if (!accion.EsValida)
        {
            throw new ArgumentException("El conteo indica la acción del catálogo.", nameof(accion));
        }

        if (desde.Offset != TimeSpan.Zero || hasta.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("La ventana se expresa en UTC.", nameof(desde));
        }

        if (hasta <= desde || hasta - desde > VentanaMaxima)
        {
            throw new ArgumentException("La ventana es positiva y de 5 minutos como máximo.", nameof(hasta));
        }

        Accion = accion;
        Desde = desde;
        Hasta = hasta;
    }

    public AccionAuditable Accion { get; }

    public DateTimeOffset Desde { get; }

    public DateTimeOffset Hasta { get; }
}

/// <summary>
/// Puerto que Identidad implementa: la lectura de sus registros aislados con los mismos filtros, orden y paginación que los demás (AUD-17), y el conteo
/// de una acción en una ventana deslizante de 5 minutos (con tiempo de espera corto y fallo seguro: si no responde, el límite sigue protegiendo y se
/// publica una alerta técnica).
/// </summary>
public interface IFuenteDeRegistrosAislados
{
    /// <summary>Una página de sus registros; el cursor es su subcursor, que fija la secuencia máxima al abrir (AUD-30).</summary>
    Task<PaginaDeRegistros> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default);

    Task<int> Contar(SolicitudDeConteo solicitud, CancellationToken cancelacion = default);
}

/// <summary>
/// Puerto que Identidad implementa: ejecuta <b>su propia</b> depuración sobre <c>identidad.registros</c> y su <c>identidad.por_anonimizar</c> (que llena
/// con sus cuentas canceladas) y devuelve cuántos registros anonimizó (AUD-19, AUD-36).
/// </summary>
public interface IDepuradorDeRegistrosAislados
{
    Task<int> Depurar(CancellationToken cancelacion = default);
}

/// <summary>
/// Puerto que Identidad implementa: los identificadores opacos de las cuentas canceladas que aún no se anonimizaron, para llenar
/// <c>auditoria.por_anonimizar</c> (AUD-19, AUD-31); después de restaurar un respaldo se vuelve a poblar desde aquí.
/// </summary>
public interface ICuentasCanceladasPendientes
{
    /// <summary>Hasta <paramref name="maximo"/> identificadores, para consumirlos por lotes sin bloqueos largos.</summary>
    Task<IReadOnlyList<Guid>> Listar(int maximo, CancellationToken cancelacion = default);
}

/// <summary>
/// Una cuenta de administración central activa a la que se envía el correo de una alerta (AUD-29). Es el identificador de la cuenta, <b>nunca</b> una
/// dirección: el correo lo resuelve el puerto de correo con la dirección registrada de la cuenta.
/// </summary>
public sealed record DestinatarioDeAlerta
{
    public DestinatarioDeAlerta(Guid cuentaId)
    {
        if (cuentaId == Guid.Empty)
        {
            throw new ArgumentException("El destinatario es una cuenta con identificador.", nameof(cuentaId));
        }

        CuentaId = cuentaId;
    }

    public Guid CuentaId { get; }
}

/// <summary>Puerto que Identidad implementa (solo en el E-commerce): los administradores centrales activos, destinatarios de las alertas.</summary>
public interface IDestinatariosDeAlertas
{
    Task<IReadOnlyList<DestinatarioDeAlerta>> Activos(CancellationToken cancelacion = default);
}
