namespace BabelHub.Ecommerce.Domain.Notificaciones;

/// <summary>A quién va el aviso (NOT-01, NOT-16): un cliente (con su consentimiento y su número, que se resuelven al enviar) o el contacto registrado de una sucursal (exento del tope por número).</summary>
public enum TipoDeDestinatario
{
    Cliente,
    Sucursal,
}

/// <summary>
/// El estado de un aviso (NOT-06): <b>pendiente</b>, <b>enviando</b> (arrendado a un trabajo), y los finales <b>enviado</b>, <b>fallido</b>, <b>cancelado</b>, <b>omitido por falta de consentimiento</b> y <b>omitido por tope</b>.
/// Un estado final no cambia nunca.
/// </summary>
public enum EstadoDelAviso
{
    Pendiente,
    Enviando,
    Enviado,
    Fallido,
    Cancelado,
    OmitidoSinConsentimiento,
    OmitidoPorTope,
}

/// <summary>
/// Un cupo del tope por número ya reservado para un aviso (NOT-14, NOT-15): la política, la clave <b>opaca</b> del contador (una huella, nunca el número), el identificador de la entrada y su instante. Sirve para
/// <b>devolverlo</b> si el aviso termina cancelado u omitido, aunque se desconozca el número; se vacía al llegar a un estado final.
/// </summary>
public sealed record CupoDelTope(string Politica, string Sujeto, string Identificador, DateTimeOffset ReservadoEn)
{
    /// <summary>No imprime la clave del contador.</summary>
    public override string ToString() => $"[cupo {Politica}]";
}

/// <summary>
/// Los reintentos y las esperas de un aviso (NOT-04, AC-NOT-12, AC-NOT-15): el intento inicial y <b>hasta 5 reintentos</b> con esperas de 1, 5, 15, 30 y 60 minutos; los aplazamientos que no consumen un intento (motor de
/// límites o Inventario sin responder) esperan 5 minutos; esperar el estado de entrega devuelve el aviso a pendiente como máximo 3 veces seguidas y la cuarta cuenta como un intento; y todo aviso vive <b>24 horas</b> como máximo
/// desde su creación (un aviso de traslado no debe llegar días tarde).
/// </summary>
public static class PoliticaDeReintento
{
    public const int ReintentosMaximos = 5;

    public const int MaximoDeEsperasSeguidasPorElEstado = 3;

    public static readonly TimeSpan EdadMaxima = TimeSpan.FromHours(24);

    public static readonly TimeSpan EsperaDeUnAplazamiento = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan EsperaDelEstadoDeEntrega = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan[] Esperas = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(60)];

    /// <summary>La espera antes del reintento número <paramref name="reintento"/> (1 a 5).</summary>
    public static TimeSpan EsperaDelReintento(int reintento)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(reintento, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(reintento, ReintentosMaximos);
        return Esperas[reintento - 1];
    }
}

/// <summary>
/// Un aviso por WhatsApp (NOT-01 a NOT-08, NOT-14): <b>uno por evento, destinatario y plantilla</b> (la clave única de la base lo garantiza, NOT-05), con el destinatario como referencia opaca (el id de una cuenta o el código de
/// una sucursal: <b>nunca el número</b>, que se resuelve al enviar) y solo los campos cerrados y tipados de su plantilla, que se vacían al llegar a un estado final. Su máquina de estados no se salta ninguna regla:
/// los finales no cambian, un aviso cancelado no se envía y todo aviso vence a las 24 horas. Los instantes llegan como parámetros.
/// </summary>
public sealed class Aviso
{
    private Aviso(
        Guid id,
        string eventoId,
        string destinatarioRef,
        TipoDeDestinatario tipo,
        string plantilla,
        int versionDeLaPlantilla,
        IReadOnlyDictionary<string, string> campos,
        EstadoDelAviso estado,
        int intentos,
        DateTimeOffset proximaFecha,
        DateTimeOffset? arrendadoHasta,
        IReadOnlyList<CupoDelTope> cupos,
        int repeticionesDeDuplicado,
        string? motivoDeCierre,
        DateTimeOffset creado,
        DateTimeOffset? fechaFinal)
    {
        Id = id;
        EventoId = eventoId;
        DestinatarioRef = destinatarioRef;
        Tipo = tipo;
        Plantilla = plantilla;
        VersionDeLaPlantilla = versionDeLaPlantilla;
        Campos = campos;
        Estado = estado;
        Intentos = intentos;
        ProximaFecha = proximaFecha;
        ArrendadoHasta = arrendadoHasta;
        Cupos = cupos;
        RepeticionesDeDuplicado = repeticionesDeDuplicado;
        MotivoDeCierre = motivoDeCierre;
        Creado = creado;
        FechaFinal = fechaFinal;
    }

    public Guid Id { get; }

    /// <summary>El hecho de negocio, determinista (<c>pedido:123:listo-para-recojo</c>): dos solicitudes del mismo evento no generan dos avisos.</summary>
    public string EventoId { get; }

    /// <summary>La referencia opaca del destinatario: el id de la cuenta (cliente) o el código de la sucursal. Nunca un número de teléfono.</summary>
    public string DestinatarioRef { get; }

    public TipoDeDestinatario Tipo { get; }

    public string Plantilla { get; }

    public int VersionDeLaPlantilla { get; }

    /// <summary>Los campos cerrados y tipados de la plantilla; vacío al alcanzar un estado final.</summary>
    public IReadOnlyDictionary<string, string> Campos { get; private set; }

    public EstadoDelAviso Estado { get; private set; }

    /// <summary>Los intentos de envío consumidos (el inicial y los reintentos).</summary>
    public int Intentos { get; private set; }

    public DateTimeOffset ProximaFecha { get; private set; }

    public DateTimeOffset? ArrendadoHasta { get; private set; }

    /// <summary>Los cupos del tope por número ya reservados (solo clientes): se reservan <b>una vez</b> por aviso aunque se reintente; vacío al terminar.</summary>
    public IReadOnlyList<CupoDelTope> Cupos { get; private set; }

    /// <summary>Si ya se reservó el cupo del tope por número.</summary>
    public bool CupoReservado => Cupos.Count > 0;

    /// <summary>Cuántas veces seguidas el servicio de envío respondió «duplicado» sin dar el estado de entrega.</summary>
    public int RepeticionesDeDuplicado { get; private set; }

    /// <summary>Por qué terminó sin enviarse (sin número, vencido, número desactivado, cuenta cancelada…): un código, nunca un dato personal.</summary>
    public string? MotivoDeCierre { get; private set; }

    public DateTimeOffset Creado { get; }

    /// <summary>Cuándo llegó a su estado final; nulo mientras no.</summary>
    public DateTimeOffset? FechaFinal { get; private set; }

    public bool EsFinal => Estado is not (EstadoDelAviso.Pendiente or EstadoDelAviso.Enviando);

    /// <summary>Cuándo vence por edad (24 horas desde su creación).</summary>
    public DateTimeOffset VenceEn => Creado + PoliticaDeReintento.EdadMaxima;

    public static Aviso Crear(Guid id, string eventoId, string destinatarioRef, TipoDeDestinatario tipo, string plantilla, int versionDeLaPlantilla, IReadOnlyDictionary<string, string> campos, DateTimeOffset ahora)
    {
        Validar(id, eventoId, destinatarioRef, plantilla, campos);
        return new Aviso(id, eventoId, destinatarioRef, tipo, plantilla, versionDeLaPlantilla, new Dictionary<string, string>(campos), EstadoDelAviso.Pendiente, 0, ahora, null, [], 0, null, ahora, null);
    }

    /// <summary>Un aviso que nace omitido (NOT-03, NOT-06): el cliente no consiente o no hay número; queda registrado con su estado y su fecha, sin campos y sin enviarse.</summary>
    public static Aviso OmitidoSinConsentimiento(Guid id, string eventoId, string destinatarioRef, TipoDeDestinatario tipo, string plantilla, int versionDeLaPlantilla, string motivo, DateTimeOffset ahora)
    {
        Validar(id, eventoId, destinatarioRef, plantilla, new Dictionary<string, string>());
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        return new Aviso(id, eventoId, destinatarioRef, tipo, plantilla, versionDeLaPlantilla, new Dictionary<string, string>(), EstadoDelAviso.OmitidoSinConsentimiento, 0, ahora, null, [], 0, motivo, ahora, ahora);
    }

    public static Aviso Restaurar(
        Guid id,
        string eventoId,
        string destinatarioRef,
        TipoDeDestinatario tipo,
        string plantilla,
        int versionDeLaPlantilla,
        IReadOnlyDictionary<string, string> campos,
        EstadoDelAviso estado,
        int intentos,
        DateTimeOffset proximaFecha,
        DateTimeOffset? arrendadoHasta,
        IReadOnlyList<CupoDelTope> cupos,
        int repeticionesDeDuplicado,
        string? motivoDeCierre,
        DateTimeOffset creado,
        DateTimeOffset? fechaFinal) =>
        new(id, eventoId, destinatarioRef, tipo, plantilla, versionDeLaPlantilla, campos, estado, intentos, proximaFecha, arrendadoHasta, cupos, repeticionesDeDuplicado, motivoDeCierre, creado, fechaFinal);

    /// <summary>
    /// Toma el aviso para enviarlo, con un arrendamiento corto: uno pendiente cuya fecha llegó o uno en envío <b>cuyo arrendamiento venció</b> (el trabajo que lo tenía cayó o el estado de entrega no llegó) y que no venció por
    /// edad (si venció, se vence).
    /// </summary>
    public bool IniciarEnvio(DateTimeOffset ahora, DateTimeOffset arrendadoHasta)
    {
        var tomable = (Estado == EstadoDelAviso.Pendiente && ahora >= ProximaFecha)
            || (Estado == EstadoDelAviso.Enviando && ArrendadoHasta is { } vence && ahora >= vence);
        if (!tomable)
        {
            return false;
        }

        if (VencerSiCorresponde(ahora))
        {
            return false;
        }

        Estado = EstadoDelAviso.Enviando;
        ArrendadoHasta = arrendadoHasta;
        return true;
    }

    /// <summary>
    /// El servicio de envío <b>aceptó</b> el mensaje (200 «aceptado», no entregado): el aviso sigue en envío esperando el estado de entrega firmado; si no llega en 10 minutos, el arrendamiento vence y el siguiente
    /// intento lo recoge (NOT-06, NOT-12).
    /// </summary>
    public bool EsperarLaEntrega(DateTimeOffset ahora)
    {
        if (Estado != EstadoDelAviso.Enviando)
        {
            return false;
        }

        ArrendadoHasta = ahora + PoliticaDeReintento.EsperaDelEstadoDeEntrega;
        return true;
    }

    /// <summary>El servicio de envío entregó el mensaje (estado de entrega firmado): final; vacía los campos.</summary>
    public bool MarcarEnviado(DateTimeOffset ahora)
    {
        if (EsFinal)
        {
            return false;
        }

        Terminar(EstadoDelAviso.Enviado, null, ahora);
        return true;
    }

    /// <summary>
    /// Un intento de envío falló (un error permanente, el servicio caído, un estado de entrega «fallido»): consume un intento. Con intentos disponibles queda pendiente con la espera creciente de la política; tras el
    /// intento inicial y los 5 reintentos queda <b>fallido</b> (NOT-04, AC-NOT-12).
    /// </summary>
    public bool FalloElIntento(DateTimeOffset ahora)
    {
        if (EsFinal)
        {
            return false;
        }

        Intentos++;
        RepeticionesDeDuplicado = 0;
        ArrendadoHasta = null;
        if (Intentos > PoliticaDeReintento.ReintentosMaximos)
        {
            Terminar(EstadoDelAviso.Fallido, "reintentos-agotados", ahora);
            return true;
        }

        Estado = EstadoDelAviso.Pendiente;
        ProximaFecha = ahora + PoliticaDeReintento.EsperaDelReintento(Intentos);
        return true;
    }

    /// <summary>
    /// El aviso no pudo intentarse por algo que no es suyo (el motor de límites o el Inventario no responden, AC-NOT-15): vuelve a pendiente <b>sin consumir un intento</b> y espera 5 minutos. Sigue sujeto a su edad
    /// máxima.
    /// </summary>
    public bool AplazarSinConsumir(DateTimeOffset ahora)
    {
        if (EsFinal)
        {
            return false;
        }

        Estado = EstadoDelAviso.Pendiente;
        ArrendadoHasta = null;
        ProximaFecha = ahora + PoliticaDeReintento.EsperaDeUnAplazamiento;
        return true;
    }

    /// <summary>
    /// El servicio de envío respondió «duplicado» (ya lo recibió) pero aún no hay estado de entrega: espera el estado hasta 10 minutos y vuelve a pendiente <b>sin consumir un intento</b>, como máximo 3 veces seguidas;
    /// la cuarta cuenta como un intento fallido (NOT-04).
    /// </summary>
    public bool EsperarElEstadoDeEntrega(DateTimeOffset ahora)
    {
        if (EsFinal)
        {
            return false;
        }

        RepeticionesDeDuplicado++;
        if (RepeticionesDeDuplicado > PoliticaDeReintento.MaximoDeEsperasSeguidasPorElEstado)
        {
            return FalloElIntento(ahora);
        }

        Estado = EstadoDelAviso.Pendiente;
        ArrendadoHasta = null;
        ProximaFecha = ahora + PoliticaDeReintento.EsperaDelEstadoDeEntrega;
        return true;
    }

    /// <summary>Se cancela (NOT-08, NOT-13): el cliente revocó el consentimiento, cambió de número, canceló su cuenta o el número se desactivó; un aviso ya final no cambia.</summary>
    public bool Cancelar(string motivo, DateTimeOffset ahora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        if (EsFinal)
        {
            return false;
        }

        Terminar(EstadoDelAviso.Cancelado, motivo, ahora);
        return true;
    }

    /// <summary>Al enviar se comprobó que el cliente no consiente o no tiene número (NOT-03): final, con su motivo.</summary>
    public bool OmitirSinConsentimiento(string motivo, DateTimeOffset ahora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        if (EsFinal)
        {
            return false;
        }

        Terminar(EstadoDelAviso.OmitidoSinConsentimiento, motivo, ahora);
        return true;
    }

    /// <summary>El número destino llegó a su tope (NOT-14): final y no se reenvía.</summary>
    public bool OmitirPorTope(DateTimeOffset ahora)
    {
        if (EsFinal)
        {
            return false;
        }

        Terminar(EstadoDelAviso.OmitidoPorTope, "tope-por-numero", ahora);
        return true;
    }

    /// <summary>Pasadas 24 horas desde su creación, un aviso que no terminó pasa a <b>fallido</b> por vencido y se vacían sus campos (AC-NOT-15).</summary>
    public bool VencerSiCorresponde(DateTimeOffset ahora)
    {
        if (EsFinal || ahora < VenceEn)
        {
            return false;
        }

        Terminar(EstadoDelAviso.Fallido, "vencido", ahora);
        return true;
    }

    /// <summary>Los cupos del tope por número ya se reservaron para este aviso (una sola vez, NOT-15); no se reservan dos veces.</summary>
    public void ReservarLosCupos(IReadOnlyList<CupoDelTope> cupos)
    {
        ArgumentNullException.ThrowIfNull(cupos);
        if (EsFinal || CupoReservado)
        {
            return;
        }

        Cupos = [.. cupos];
    }

    private void Terminar(EstadoDelAviso estado, string? motivo, DateTimeOffset ahora)
    {
        Estado = estado;
        MotivoDeCierre = motivo;
        FechaFinal = ahora;
        ArrendadoHasta = null;
        Campos = new Dictionary<string, string>();
        Cupos = [];
    }

    private static void Validar(Guid id, string eventoId, string destinatarioRef, string plantilla, IReadOnlyDictionary<string, string> campos)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("El aviso necesita su identificador.", nameof(id));
        }

        if (!PlantillaDeAviso.EventoValido(eventoId))
        {
            throw new ArgumentException("El identificador del evento no tiene el formato <tipo>:<id>:<estado>.", nameof(eventoId));
        }

        if (!PlantillaDeAviso.ReferenciaValida(destinatarioRef))
        {
            throw new ArgumentException("El destinatario es una referencia opaca: el id de una cuenta o el código de una sucursal.", nameof(destinatarioRef));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(plantilla);
        ArgumentNullException.ThrowIfNull(campos);
    }
}
