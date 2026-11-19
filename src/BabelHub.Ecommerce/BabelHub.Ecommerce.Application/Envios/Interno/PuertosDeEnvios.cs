using BabelHub.Ecommerce.Domain.Envios;

namespace BabelHub.Ecommerce.Application.Envios.Interno;

/// <summary>El almacén (Infraestructura) no pudo atender a tiempo (un <c>lock_timeout</c>, una conexión caída): se responde «servicio no disponible» sin dejar nada a medias.</summary>
internal sealed class EnviosNoDisponiblesException : Exception
{
    public EnviosNoDisponiblesException()
    {
    }

    public EnviosNoDisponiblesException(string message)
        : base(message)
    {
    }

    public EnviosNoDisponiblesException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal enum ResultadoDeAgregadoDeEnvio
{
    Agregado,

    /// <summary>Alguna de las líneas ya pertenece a otro envío: el único <c>(linea_id)</c>, la autoridad final de la idempotencia de <c>Crear</c> (ENV-01).</summary>
    LineaRepetida,
}

internal sealed record ListadoDeEnvios(IReadOnlyList<Envio> Envios, int Total);

/// <summary>
/// El repositorio de los envíos. Lo implementa la Infraestructura bajo <c>EjecutarComo(envios)</c> (T12), con <c>FOR UPDATE</c> parametrizado y <c>lock_timeout</c>. Los métodos de bloqueo exigen
/// una transacción activa; <b>ningún método guarda ni lee nombre, dirección ni teléfono</b> (ENV-15).
/// </summary>
internal interface IRepositorioDeEnvios
{
    Task<ResultadoDeAgregadoDeEnvio> Agregar(Envio envio, CancellationToken cancelacion = default);

    /// <summary>Lee un envío; con <paramref name="paraActualizar"/>, con <c>FOR UPDATE</c> y <c>lock_timeout</c>.</summary>
    Task<Envio?> Obtener(Guid id, bool paraActualizar, CancellationToken cancelacion = default);

    /// <summary>Los envíos de un pedido ordenados por identificador; con <paramref name="paraActualizar"/>, bloqueados en ese orden (el orden de bloqueo pedido → envío).</summary>
    Task<IReadOnlyList<Envio>> ObtenerDelPedido(Guid pedidoId, bool paraActualizar, CancellationToken cancelacion = default);

    /// <summary>El envío que ya contiene alguna de las líneas (cada línea pertenece a un solo envío).</summary>
    Task<IReadOnlyList<Envio>> BuscarPorLineas(IReadOnlyCollection<Guid> lineaIds, CancellationToken cancelacion = default);

    /// <summary>Guarda solo lo que cambia: estado, guía, clave, arrendamientos, marcador y fechas.</summary>
    Task Guardar(Envio envio, CancellationToken cancelacion = default);

    /// <summary>Los envíos en reparto o entregados cuyo progreso todavía no se informó a Pedidos, del más antiguo al más reciente (para que el reparto se informe antes que la entrega).</summary>
    Task<IReadOnlyList<Envio>> ObtenerConProgresoPendiente(int maximo, CancellationToken cancelacion = default);

    /// <summary>Los envíos en guía generada con el marcador de despacho iniciado antes de <paramref name="limite"/> (más de 24 horas sin pasar a en reparto).</summary>
    Task<IReadOnlyList<Envio>> ObtenerConDespachoIniciadoAntesDe(DateTimeOffset limite, int maximo, CancellationToken cancelacion = default);

    /// <summary>Los envíos cancelados que tenían guía y se cancelaron en la ventana dada (una guía sin uso en el operador que logística debe avisar fuera del sistema).</summary>
    Task<IReadOnlyList<Envio>> ObtenerCanceladosConGuiaEntre(DateTimeOffset desde, DateTimeOffset hasta, int maximo, CancellationToken cancelacion = default);

    /// <summary>Lista por fecha de creación descendente, con el estado opcional.</summary>
    Task<ListadoDeEnvios> Listar(EstadoDeEnvio? estado, int pagina, int tamano, CancellationToken cancelacion = default);
}

/// <summary>
/// El destino de las llamadas al operador logístico (ENV-02, ENV-04, ENV-18). El operador debe ser <b>idempotente por clave de solicitud o consultable por referencia</b> (el adaptador lo oculta). Recibe
/// <b>solo nombre, dirección y teléfono</b> y nunca registra ni repite ese contacto.
/// </summary>
internal interface IOperadorLogistico
{
    /// <summary>El nombre corto del operador, de la configuración: es lo que se guarda como <c>operador</c> y no lo que diga su respuesta.</summary>
    string Nombre { get; }

    Task<ResultadoDelOperador> CrearOObtenerGuia(Guid claveDeSolicitud, DestinatarioParaGuia destinatario, CancellationToken cancelacion = default);
}

/// <summary>La respuesta del operador, ya validada y reducida a un código cerrado: nunca lleva su texto de rechazo (ENV-17).</summary>
internal abstract record ResultadoDelOperador
{
    public sealed record GuiaCreada(NumeroDeGuia Numero) : ResultadoDelOperador;

    /// <summary>El operador rechazó la solicitud (por ejemplo, una dirección que no puede atender).</summary>
    public sealed record Rechazada : ResultadoDelOperador;

    /// <summary>Sin respuesta, plazo agotado o cualquier falla del operador.</summary>
    public sealed record NoDisponible : ResultadoDelOperador;

    /// <summary>La respuesta no cumple el formato esperado (SEG-13): se trata como no disponible, sin usarla.</summary>
    public sealed record RespuestaInvalida : ResultadoDelOperador;
}

/// <summary>Cerrar las reservas de un envío con motivo reparto en el Inventario (ENV-11, ENV-12). Lo implementa la Infraestructura sobre <c>IClienteDelInventario</c> (T13).</summary>
internal interface IReservasDelEnvio
{
    /// <summary>Todas las líneas del envío en una sola llamada (todo o nada), con el <c>envioId</c> y <c>Idempotency-Key</c> = <c>envioId</c>.</summary>
    Task<ResultadoDeCierreDeReservas> Cerrar(Guid envioId, IReadOnlyCollection<Guid> lineaIds, CancellationToken cancelacion = default);
}

/// <summary>El resultado del cierre, reducido a la lista cerrada que el caso de uso necesita (D05): un rechazo de negocio solo si no cambió nada; todo lo demás es incierto.</summary>
internal abstract record ResultadoDeCierreDeReservas
{
    /// <summary>Todas las líneas figuran como cerradas o <c>ya-cerrada</c> (un hecho que se acepta, ENV-12).</summary>
    public sealed record Cerrado : ResultadoDeCierreDeReservas;

    /// <summary>Un rechazo de negocio de la lista cerrada (<c>INV-RESERVA-ESTADO-INVALIDO</c>, <c>INV-VALIDACION-INVALIDA</c>, <c>INV-NO-ENCONTRADO</c>): garantiza que no cambió nada.</summary>
    public sealed record RechazoDeNegocio : ResultadoDeCierreDeReservas;

    /// <summary>Sin respuesta, plazo agotado, 5xx, 401, 408, 429, <c>EN-CURSO</c>, conflicto de idempotencia, sin permiso o una respuesta inválida: el primer cierre puede haberse aplicado.</summary>
    public sealed record Incierto : ResultadoDeCierreDeReservas;
}
