using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.Application.Pagos.Interno;

// Los puertos propios de Pagos (AGENTS §4: un puerto solo si hay algo externo o una variación real). Los implementa la Infraestructura (el simulador y Culqi para la pasarela, el cliente del Inventario para las
// reservas, EF Core para los repositorios) y las pruebas los doblan. Viven en el Interno: ningún otro módulo los ve.

// ---------- La pasarela de pago ----------

/// <summary>
/// Lo que se envía a la pasarela para cobrar (PAG-30): solo el importe, la moneda (siempre <c>PEN</c>), la referencia opaca del intento, el token del medio y, si la pasarela lo exige, el correo del titular. No tiene un
/// campo para el nombre, el teléfono ni la dirección de entrega (lo comprueba una prueba de contrato). Su texto no repite el token ni el correo.
/// </summary>
internal sealed record SolicitudDeCobroEnPasarela(Guid ReferenciaOpaca, Importe Importe, Domain.Pagos.MedioDePago Medio, TokenDelMedioDePago Token, [property: System.Text.Json.Serialization.JsonIgnore] string? CorreoDelTitular)
{
    public override string ToString() => $"SolicitudDeCobroEnPasarela {{ ReferenciaOpaca = {ReferenciaOpaca}, Importe = {Importe} }}";
}

internal enum TipoDeResultadoDeReembolsoEnPasarela
{
    Aplicado,
    Rechazado,

    /// <summary>La pasarela no reembolsa con ese medio (por ejemplo, Yape por la API).</summary>
    NoSoportado,

    SinRespuesta,
}

internal sealed record ResultadoDeReembolsoEnPasarela(TipoDeResultadoDeReembolsoEnPasarela Tipo, string? Referencia)
{
    public static ResultadoDeReembolsoEnPasarela Aplicado(string referencia) => new(TipoDeResultadoDeReembolsoEnPasarela.Aplicado, referencia);

    public static ResultadoDeReembolsoEnPasarela Rechazado { get; } = new(TipoDeResultadoDeReembolsoEnPasarela.Rechazado, null);

    public static ResultadoDeReembolsoEnPasarela NoSoportado { get; } = new(TipoDeResultadoDeReembolsoEnPasarela.NoSoportado, null);

    public static ResultadoDeReembolsoEnPasarela SinRespuesta { get; } = new(TipoDeResultadoDeReembolsoEnPasarela.SinRespuesta, null);

    public override string ToString() => $"ResultadoDeReembolsoEnPasarela {{ Tipo = {Tipo} }}";
}

/// <summary>Un reembolso que la pasarela dice haber aplicado a un cargo: su <c>reembolsos.id</c> viaja en los metadatos para reconocerlo (D08).</summary>
internal sealed record ReembolsoEnPasarela(Guid? ReembolsoId, string Referencia, Importe Monto);

/// <summary>
/// La pasarela externa tras un puerto (D05, D13): sus modelos son propios. <c>Cobrar</c> y <c>ConsultarEstado</c> devuelven el mismo tipo; un aprobado se acepta solo tras compararlo con el intento
/// (<see cref="ResultadoDeCobro.Validar"/>). No se supone idempotencia del proveedor: ni <c>Cobrar</c> ni <c>Reembolsar</c> se reintentan a ciegas.
/// </summary>
internal interface IPasarelaDePago
{
    /// <summary>Cobra con el token del medio; sin respuesta o con una respuesta que no se entiende es <see cref="ResultadoDeCobro.SinRespuesta"/>.</summary>
    Task<ResultadoDeCobro> Cobrar(SolicitudDeCobroEnPasarela solicitud, CancellationToken cancelacion = default);

    /// <summary>Consulta el cargo por su referencia opaca (los metadatos), sin cobrar.</summary>
    Task<ResultadoDeCobro> ConsultarEstado(Guid referenciaOpaca, CancellationToken cancelacion = default);

    Task<ResultadoDeReembolsoEnPasarela> Reembolsar(Guid reembolsoId, string referenciaDelCargo, Importe monto, CancellationToken cancelacion = default);

    /// <summary>Los reembolsos que la pasarela tiene de un cargo, para no pedir otro a ciegas; <c>null</c> si no se pudo consultar.</summary>
    Task<IReadOnlyList<ReembolsoEnPasarela>?> ConsultarReembolsos(string referenciaDelCargo, CancellationToken cancelacion = default);
}

// ---------- Las reservas en el Inventario ----------

internal enum ResultadoDeLaLlamadaDeReserva
{
    Correcto,

    /// <summary><c>INV-RESERVA-ESTADO-INVALIDO</c>: la reserva ya no está en el estado que la operación necesita (venció o ya se confirmó).</summary>
    EstadoInvalido,

    NoExiste,

    /// <summary>El Inventario no respondió o respondió algo que no se entiende (SEG-13): falla cerrada.</summary>
    SinRespuesta,
}

internal sealed record RespuestaDeReserva<T>(ResultadoDeLaLlamadaDeReserva Resultado, T? Valor)
{
    public bool EsCorrecta => Resultado == ResultadoDeLaLlamadaDeReserva.Correcto;
}

internal sealed record EstadoDeLaReserva(DateTimeOffset ExpiraEn, bool Vigente);

internal sealed record LineaConfirmadaDeReserva(Guid LineaId, string CodigoDeFuente, bool RequiereTraslado, Guid? TrasladoId, DateOnly? FechaEstimadaDeLlegada);

/// <summary>
/// Las reservas de la compra en el Inventario (<c>reservas.consultar</c>, <c>.extender</c>, <c>.confirmar</c> y <c>.liberar</c> de los contratos v1), por <c>IClienteDelInventario</c> de Plataforma. <b>Ninguna se llama dentro de una
/// transacción</b> (D03): son orquestadores sin transacción con pasos cortos.
/// </summary>
internal interface IReservasDelPago
{
    /// <summary>Con alcance de usuario: ¿la reserva sigue vigente?</summary>
    Task<RespuestaDeReserva<EstadoDeLaReserva>> Consultar(Guid pedidoId, Guid cuentaId, CancellationToken cancelacion = default);

    /// <summary>Extiende una sola vez la reserva de los impresos hasta el instante dado (PAG-13).</summary>
    Task<RespuestaDeReserva<EstadoDeLaReserva>> Extender(Guid pedidoId, DateTimeOffset hasta, CancellationToken cancelacion = default);

    /// <summary>Confirma las reservas con la clave <c>conf-&lt;intentoId&gt;</c> y devuelve la fuente y los traslados de cada línea.</summary>
    Task<RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>> Confirmar(Guid pedidoId, string claveDeIdempotencia, CancellationToken cancelacion = default);

    /// <summary>Con alcance de sistema: libera las reservas con el motivo (D07). Liberar una reserva ya expirada o liberada es un éxito sin cambios.</summary>
    Task<RespuestaDeReserva<bool>> Liberar(Guid pedidoId, MotivoDeLiberacion motivo, CancellationToken cancelacion = default);
}

// ---------- Los repositorios ----------

internal enum ResultadoDeAgregarIntento
{
    Agregado,

    /// <summary>Ya existe un intento con esa clave (el <c>intentoId</c>): es el resultado original (PAG-04).</summary>
    ClaveRepetida,

    /// <summary>La intención ya tiene un intento activo (el índice único parcial, PAG-17).</summary>
    IntentoActivoExistente,
}

/// <summary>Las intenciones y los intentos de cobro (esquema <c>pagos</c>). Las lecturas «para actualizar» bloquean la fila con <c>FOR UPDATE</c> y <c>lock_timeout</c>, siempre después de leer la marca de cancelación de la cuenta (D24).</summary>
internal interface IRepositorioDePagos
{
    Task<IntencionDePago?> ObtenerIntencion(Guid pedidoId, bool paraActualizar, CancellationToken cancelacion = default);

    Task AgregarIntencion(IntencionDePago intencion, CancellationToken cancelacion = default);

    Task GuardarIntencion(IntencionDePago intencion, CancellationToken cancelacion = default);

    Task<IntentoDeCobro?> ObtenerIntento(Guid intentoId, CancellationToken cancelacion = default);

    Task<IntentoDeCobro?> ObtenerIntentoActivo(Guid intencionId, CancellationToken cancelacion = default);

    Task<ResultadoDeAgregarIntento> AgregarIntento(IntentoDeCobro intento, CancellationToken cancelacion = default);

    Task GuardarIntento(IntentoDeCobro intento, CancellationToken cancelacion = default);
}

/// <summary>Los reembolsos de toda la plataforma (D08): clave natural única por origen.</summary>
internal interface IRepositorioDeReembolsos
{
    Task<Reembolso?> Obtener(Guid reembolsoId, bool paraActualizar, CancellationToken cancelacion = default);

    Task<Reembolso?> BuscarPorOrigen(OrigenDeReembolso origen, string claveNatural, CancellationToken cancelacion = default);

    /// <summary>Todos los reembolsos de una intención, para validar el tope por cargo y por línea.</summary>
    Task<IReadOnlyList<Reembolso>> ListarDeLaIntencion(Guid intencionId, CancellationToken cancelacion = default);

    Task Agregar(Reembolso reembolso, CancellationToken cancelacion = default);

    Task Guardar(Reembolso reembolso, CancellationToken cancelacion = default);

    /// <summary>Los reembolsos pendientes o solicitados de un origen en las intenciones de una cuenta (PAG-24).</summary>
    Task<int> ContarPendientesDeLaCuenta(Guid cuentaId, OrigenDeReembolso origen, CancellationToken cancelacion = default);

    Task<(IReadOnlyList<Reembolso> Reembolsos, int Total)> ListarPendientes(int pagina, int tamano, CancellationToken cancelacion = default);
}

// ---------- La cuenta del pagador (Identidad) ----------

internal enum MotivoDeCuentaQueNoPuedePagar
{
    CorreoSinVerificar,
    CancelacionDiferida,
    TextosPendientes,
    CuentaInactiva,
}

/// <summary>Si la cuenta puede iniciar un cobro nuevo (PAG-21, PAG-25, PAG-31): a partir de lo que informa Identidad por su contrato.</summary>
internal sealed record EstadoDeLaCuentaParaPagar(bool PuedePagar, MotivoDeCuentaQueNoPuedePagar? Motivo);

/// <summary>
/// Lo que Pagos pide a Identidad (<c>IEstadoDeCuenta</c> y el contacto de la cuenta): puede pagar o el motivo, la marca de cancelación en ejecución (leída con bloqueo antes de bloquear la intención, D24) y el correo del
/// titular, que no se guarda (D12). Falla cerrada: <c>null</c> o una excepción significa que no se pudo saber y no se cobra.
/// </summary>
internal interface ICuentaDelPagador
{
    Task<EstadoDeLaCuentaParaPagar?> Consultar(Guid cuentaId, CancellationToken cancelacion = default);

    /// <summary>La marca de cancelación de la cuenta: <c>true</c> si su cancelación está en ejecución. Con <paramref name="bloquear"/>, la lectura toma el bloqueo (orden global: cuenta → pedido → …).</summary>
    Task<bool> CancelacionEnEjecucion(Guid cuentaId, bool bloquear, CancellationToken cancelacion = default);

    Task<string?> CorreoDelTitular(Guid cuentaId, CancellationToken cancelacion = default);
}
