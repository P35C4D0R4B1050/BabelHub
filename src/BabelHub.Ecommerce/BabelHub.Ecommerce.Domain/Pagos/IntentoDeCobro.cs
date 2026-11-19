namespace BabelHub.Ecommerce.Domain.Pagos;

/// <summary>
/// El estado de un intento de cobro (PAG-07, PAG-17, D23). Activos: <c>Iniciado</c>, <c>PendienteDeConfirmacion</c> y <c>Aprobado</c> (a lo más uno por intención). <c>AprobadoSobrante</c>
/// es un cobro que la pasarela aprobó cuando la intención ya no podía aceptarlo: queda fuera del índice del intento activo y solo se reembolsa.
/// </summary>
public enum EstadoDeIntento
{
    Iniciado,
    PendienteDeConfirmacion,
    Aprobado,
    Rechazado,
    Vencido,
    AprobadoSobrante,
}

/// <summary>Qué pasó al aplicar una aprobación de la pasarela a un intento.</summary>
public enum ResultadoDeAprobacion
{
    /// <summary>El intento activo pasó a <c>Aprobado</c>: se completa la compra.</summary>
    Aprobado,

    /// <summary>El intento ya estaba aprobado (o era sobrante) con el mismo cargo: repetir no cambia nada.</summary>
    YaAprobado,

    /// <summary>El intento ya tenía un cargo aprobado con otra referencia de la pasarela: dos cargos por el mismo intento. No se reembolsa nada solo: se alerta para conciliar (D13).</summary>
    CargoDuplicado,

    /// <summary>Era de un intento rechazado o vencido, o la intención ya no lo acepta: es un cobro sobrante que solo se reembolsa.</summary>
    Sobrante,
}

/// <summary>
/// Un intento de cobro con un medio (PAG-16): su <c>Id</c> es la clave de idempotencia y la referencia opaca que viaja a la pasarela (PAG-30). No guarda token del medio, tarjeta,
/// correo, contacto ni la respuesta cruda de la pasarela. Cada transición es condicional: un resultado tardío no deshace un aprobado ni un rechazado.
/// </summary>
public sealed class IntentoDeCobro
{
    private IntentoDeCobro(Guid id, Guid intencionId, MedioDePago medio, Importe importe, DateTimeOffset ahora)
    {
        Id = id;
        IntencionId = intencionId;
        Medio = medio;
        Importe = importe;
        IniciadoEn = ahora;
        Estado = EstadoDeIntento.Iniciado;
    }

    public Guid Id { get; }

    /// <summary>La referencia opaca que viaja a la pasarela en los metadatos: la misma clave del intento, sin nada del cliente.</summary>
    public Guid ReferenciaOpaca => Id;

    public Guid IntencionId { get; }

    public MedioDePago Medio { get; }

    public EstadoDeIntento Estado { get; private set; }

    /// <summary>El identificador del cargo en la pasarela, cuando se conoce.</summary>
    public string? ReferenciaDePasarela { get; private set; }

    /// <summary>La marca el aviso entrante de la pasarela: la consulta se adelanta (PAG-19).</summary>
    public bool ConsultaInmediata { get; private set; }

    public MotivoDeRechazo? MotivoDeRechazo { get; private set; }

    public Importe Importe { get; }

    public DateTimeOffset IniciadoEn { get; }

    public DateTimeOffset? EnviadoEn { get; private set; }

    public DateTimeOffset? ResueltoEn { get; private set; }

    public int Consultas { get; private set; }

    public DateTimeOffset? UltimaConsultaEn { get; private set; }

    /// <summary><c>Iniciado</c>, <c>PendienteDeConfirmacion</c> o <c>Aprobado</c>: ocupa el índice único del intento activo de la intención.</summary>
    public bool EsActivo => Estado is EstadoDeIntento.Iniciado or EstadoDeIntento.PendienteDeConfirmacion or EstadoDeIntento.Aprobado;

    internal bool EstaSinResolver => Estado is EstadoDeIntento.Iniciado or EstadoDeIntento.PendienteDeConfirmacion;

    public static IntentoDeCobro Iniciar(Guid id, Guid intencionId, MedioDePago medio, Importe importe, DateTimeOffset ahora)
    {
        if (id == Guid.Empty || intencionId == Guid.Empty)
        {
            throw new ArgumentException("El intento necesita su clave y su intención.");
        }

        if (importe.Valor <= 0m)
        {
            throw new ArgumentException("Un intento cobra un importe mayor que cero.", nameof(importe));
        }

        return new IntentoDeCobro(id, intencionId, medio, importe, ahora);
    }

    /// <summary>La reconstruye desde el almacén (la infraestructura): no valida ni cambia nada.</summary>
    public static IntentoDeCobro Rehidratar(
        Guid id,
        Guid intencionId,
        MedioDePago medio,
        Importe importe,
        DateTimeOffset iniciadoEn,
        EstadoDeIntento estado,
        string? referenciaDePasarela,
        bool consultaInmediata,
        MotivoDeRechazo? motivoDeRechazo,
        DateTimeOffset? enviadoEn,
        DateTimeOffset? resueltoEn,
        int consultas,
        DateTimeOffset? ultimaConsultaEn) =>
        new(id, intencionId, medio, importe, iniciadoEn)
        {
            Estado = estado,
            ReferenciaDePasarela = referenciaDePasarela,
            ConsultaInmediata = consultaInmediata,
            MotivoDeRechazo = motivoDeRechazo,
            EnviadoEn = enviadoEn,
            ResueltoEn = resueltoEn,
            Consultas = consultas,
            UltimaConsultaEn = ultimaConsultaEn,
        };

    internal IntentoDeCobro Copiar() => (IntentoDeCobro)MemberwiseClone();

    /// <summary>Anota el instante en que se llamó a la pasarela (una sola vez).</summary>
    public void MarcarEnviado(DateTimeOffset ahora) => EnviadoEn ??= ahora;

    /// <summary>Un intento <c>iniciado</c> sin resultado tras 15 segundos lo resuelve la reconciliación: pasa a pendiente, nunca a rechazado por la ausencia del cargo.</summary>
    public bool DebeResolverseSinRespuesta(DateTimeOffset ahora) =>
        Estado == EstadoDeIntento.Iniciado && ahora - IniciadoEn >= PlazosDelPago.EsperaDeReconciliacion;

    /// <summary><c>iniciado → pendiente_de_confirmacion</c>: la pasarela no respondió en 10 segundos o la respuesta fue dudosa. Condicional: no toca un intento ya resuelto.</summary>
    public bool MarcarPendiente(DateTimeOffset ahora)
    {
        if (Estado != EstadoDeIntento.Iniciado)
        {
            return false;
        }

        Estado = EstadoDeIntento.PendienteDeConfirmacion;
        return true;
    }

    /// <summary>La pasarela rechazó el cobro (con un motivo de la lista cerrada). Condicional: no deshace un aprobado ni un vencido.</summary>
    public bool MarcarRechazado(MotivoDeRechazo motivo, DateTimeOffset ahora)
    {
        if (!EstaSinResolver)
        {
            return false;
        }

        Estado = EstadoDeIntento.Rechazado;
        MotivoDeRechazo = motivo;
        ResueltoEn = ahora;
        return true;
    }

    /// <summary>Pasados los 30 minutos desde el inicio de la intención, un pendiente vence. Condicional: no toca uno aprobado o rechazado.</summary>
    public bool Vencer(DateTimeOffset ahora)
    {
        if (!EstaSinResolver)
        {
            return false;
        }

        Estado = EstadoDeIntento.Vencido;
        ResueltoEn = ahora;
        return true;
    }

    /// <summary>Si ya llegó el instante en que un pendiente (o un iniciado que nunca respondió) vence: el límite de 30 minutos de la intención.</summary>
    public bool DebeVencer(DateTimeOffset limitePendienteEn, DateTimeOffset ahora) => EstaSinResolver && ahora >= limitePendienteEn;

    /// <summary>
    /// Aplica una aprobación confirmada de la pasarela. Si el intento está sin resolver y la intención lo acepta, pasa a <c>Aprobado</c>; si está rechazado o vencido, o la intención ya
    /// no lo acepta (cerrada), es un cobro <c>AprobadoSobrante</c>: nunca reabre ni completa la intención (D23). Repetir la aprobación no cambia nada.
    /// </summary>
    internal ResultadoDeAprobacion AplicarAprobacion(string referenciaDePasarela, DateTimeOffset ahora, bool laIntencionLaAcepta)
    {
        if (string.IsNullOrWhiteSpace(referenciaDePasarela))
        {
            throw new ArgumentException("Un cobro aprobado trae la referencia del cargo en la pasarela.", nameof(referenciaDePasarela));
        }

        if (Estado is EstadoDeIntento.Aprobado or EstadoDeIntento.AprobadoSobrante)
        {
            return string.Equals(ReferenciaDePasarela, referenciaDePasarela.Trim(), StringComparison.Ordinal) ? ResultadoDeAprobacion.YaAprobado : ResultadoDeAprobacion.CargoDuplicado;
        }

        ReferenciaDePasarela = referenciaDePasarela.Trim();
        ConsultaInmediata = false;
        if (EstaSinResolver && laIntencionLaAcepta)
        {
            Estado = EstadoDeIntento.Aprobado;
            ResueltoEn = ahora;
            return ResultadoDeAprobacion.Aprobado;
        }

        Estado = EstadoDeIntento.AprobadoSobrante;
        ResueltoEn = ahora;
        return ResultadoDeAprobacion.Sobrante;
    }

    /// <summary>Cuánto tiempo después de resolverse un intento rechazado o vencido todavía puede llegar un cobro aprobado que haya que conciliar (D23, R02).</summary>
    public static readonly TimeSpan VentanaDeConciliacion = TimeSpan.FromHours(24);

    /// <summary>Tras vencer, el trabajo sigue consultando cada minuto este tiempo: cubre el cobro que se aprueba entre la última consulta y el límite.</summary>
    public static readonly TimeSpan VentanaDeConsultaTrasVencer = TimeSpan.FromMinutes(15);

    /// <summary>Un intento <c>vencido</c> o <c>rechazado</c> resuelto hace menos de 24 horas: si la pasarela lo aprueba tarde, es un cobro sobrante que se reembolsa (nunca completa la intención).</summary>
    public bool EsConciliable(DateTimeOffset ahora) =>
        Estado is EstadoDeIntento.Vencido or EstadoDeIntento.Rechazado && ResueltoEn is { } resuelto && ahora - resuelto <= VentanaDeConciliacion;

    /// <summary>El aviso de la pasarela adelanta la consulta de un intento sin resolver; en uno resuelto no hace nada.</summary>
    public bool MarcarConsultaInmediata()
    {
        if (!EstaSinResolver)
        {
            return false;
        }

        ConsultaInmediata = true;
        return true;
    }

    /// <summary>El aviso de la pasarela también adelanta la consulta de un intento resuelto que aún es conciliable (un aprobado tardío).</summary>
    public bool MarcarConsultaInmediata(DateTimeOffset ahora)
    {
        if (!EstaSinResolver && !EsConciliable(ahora))
        {
            return false;
        }

        ConsultaInmediata = true;
        return true;
    }

    /// <summary>Se consulta cada minuto un pendiente (o ya, si el aviso la adelantó); un intento <c>iniciado</c> lo resuelve la reconciliación por la misma vía. Uno <c>vencido</c> se sigue consultando 15 minutos y uno resuelto solo cuando el aviso lo adelanta.</summary>
    public bool DebeConsultar(DateTimeOffset ahora)
    {
        if (EsConciliable(ahora))
        {
            if (ConsultaInmediata)
            {
                return true;
            }

            return Estado == EstadoDeIntento.Vencido && ahora - ResueltoEn <= VentanaDeConsultaTrasVencer && (UltimaConsultaEn is null || ahora - UltimaConsultaEn >= PlazosDelPago.IntervaloDeConsulta);
        }

        if (!EstaSinResolver)
        {
            return false;
        }

        // Un intento que aún está en vuelo no se consulta antes de los 15 segundos, salvo que el aviso de la pasarela lo adelante.
        if (Estado == EstadoDeIntento.Iniciado && !ConsultaInmediata && !DebeResolverseSinRespuesta(ahora))
        {
            return false;
        }

        return ConsultaInmediata || UltimaConsultaEn is null || ahora - UltimaConsultaEn >= PlazosDelPago.IntervaloDeConsulta;
    }

    public void RegistrarConsulta(DateTimeOffset ahora)
    {
        Consultas++;
        UltimaConsultaEn = ahora;
        ConsultaInmediata = false;
    }

    /// <summary>Sin el motivo, el medio ni la referencia: solo identificador y estado.</summary>
    public override string ToString() => $"IntentoDeCobro {{ Id = {Id}, Estado = {Estado} }}";
}
