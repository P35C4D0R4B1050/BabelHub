namespace BabelHub.Ecommerce.Domain.Web;

/// <summary>Cada servicio web existe en dos hosts (AD15, D03): la tienda y la gestión del personal. Una sesión solo vale en el host que la creó.</summary>
public enum HostDeSesion
{
    Tienda,
    Gestion,
}

/// <summary>El estado de una sesión web: abierta, vencida (por inactividad, tope o renovación rechazada) o cerrada (por la persona o por revocación).</summary>
public enum EstadoDeSesion
{
    Abierta,
    Vencida,
    Cerrada,
}

/// <summary>Por qué terminó una sesión. Decide si lo que la persona tenía en pantalla se conserva (WEB-25).</summary>
public enum MotivoDeCierre
{
    Voluntario,
    VencidaPorInactividad,
    VencidaPorTopeAbsoluto,
    RenovacionRechazada,
    CerradaPorRevocacion,
}

public static class MotivosDeCierre
{
    /// <summary>
    /// Solo un vencimiento o un rechazo de la renovación conservan el borrador (WEB-25, D06): nunca un cierre voluntario ni «cerrar todas», que son
    /// decisiones de la persona y no deben dejar lo escrito para quien use el equipo después.
    /// </summary>
    public static bool ConservaElBorrador(this MotivoDeCierre motivo) =>
        motivo is MotivoDeCierre.VencidaPorInactividad or MotivoDeCierre.VencidaPorTopeAbsoluto or MotivoDeCierre.RenovacionRechazada;

    /// <summary>Un vencimiento deja la sesión «vencida»; el cierre por la persona o por revocación, «cerrada».</summary>
    public static EstadoDeSesion EstadoResultante(this MotivoDeCierre motivo) =>
        motivo is MotivoDeCierre.Voluntario or MotivoDeCierre.CerradaPorRevocacion ? EstadoDeSesion.Cerrada : EstadoDeSesion.Vencida;
}

/// <summary>Lo que dice la política de una sesión en un instante dado (sin modificarla).</summary>
public enum EvaluacionDeSesion
{
    Vigente,
    VencidaPorInactividad,
    VencidaPorTopeAbsoluto,

    /// <summary>Ya estaba cerrada o vencida: un identificador cerrado nunca vuelve a usarse (WEB-28).</summary>
    Cerrada,
}

/// <summary>
/// El identificador de una sesión tal como se guarda: el SHA-256 en hexadecimal minúsculo (64 caracteres) del valor de la cookie (D01). El valor de la
/// cookie nunca se guarda ni existe en el Dominio; así un volcado de la base no sirve para suplantar una sesión.
/// </summary>
public readonly record struct HashDeSesion
{
    private HashDeSesion(string valor) => Valor = valor;

    public string Valor { get; }

    public static HashDeSesion De(string valor)
    {
        if (valor is null || valor.Length != 64 || !valor.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new ArgumentException("El identificador de la sesión es un SHA-256 de 64 caracteres hexadecimales en minúscula.", nameof(valor));
        }

        return new HashDeSesion(valor);
    }

    public override string ToString() => "HashDeSesion";
}

/// <summary>Los plazos de la sesión web (WEB-27, WEB-31, D04). El instante llega siempre como parámetro: el Dominio no tiene reloj.</summary>
public static class PoliticaDeSesion
{
    /// <summary>Sin acciones del usuario durante este tiempo la sesión se cierra (WEB-27).</summary>
    public static readonly TimeSpan InactividadMaxima = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan TopeDelCliente = TimeSpan.FromDays(7);

    public static readonly TimeSpan TopeDelPersonal = TimeSpan.FromHours(8);

    /// <summary>La actividad se escribe como máximo una vez por minuto (tolerancia que cubre el criterio de 31 minutos).</summary>
    public static readonly TimeSpan IntervaloMinimoDeActividad = TimeSpan.FromMinutes(1);

    public static TimeSpan TopeAbsoluto(bool esPersonal) => esPersonal ? TopeDelPersonal : TopeDelCliente;
}

/// <summary>
/// Una sesión web (WEB-27, WEB-28, WEB-31): la fila del servidor que la cookie opaca señala. Guarda quién es, en qué host vale y sus plazos; <b>no guarda el
/// rol ni la sucursal</b>: el actor sale de la credencial verificada en cada solicitud (D04). Solo una acción de la persona renueva la inactividad.
/// </summary>
public sealed class SesionWeb
{
    private SesionWeb(
        HashDeSesion id,
        Guid usuarioId,
        HostDeSesion host,
        DateTimeOffset creada,
        DateTimeOffset ultimaActividad,
        DateTimeOffset expiraAbsoluta,
        EstadoDeSesion estado,
        MotivoDeCierre? motivoDeCierre,
        DateTimeOffset? cerradaEn)
    {
        Id = id;
        UsuarioId = usuarioId;
        Host = host;
        Creada = creada;
        UltimaActividad = ultimaActividad;
        ExpiraAbsoluta = expiraAbsoluta;
        Estado = estado;
        MotivoDeCierre = motivoDeCierre;
        CerradaEn = cerradaEn;
    }

    public HashDeSesion Id { get; }

    public Guid UsuarioId { get; }

    public HostDeSesion Host { get; }

    public DateTimeOffset Creada { get; }

    /// <summary>El instante de la última acción de la persona (no de una consulta automática); se escribe como máximo una vez por minuto.</summary>
    public DateTimeOffset UltimaActividad { get; private set; }

    public DateTimeOffset ExpiraAbsoluta { get; }

    public EstadoDeSesion Estado { get; private set; }

    public MotivoDeCierre? MotivoDeCierre { get; private set; }

    public DateTimeOffset? CerradaEn { get; private set; }

    public static SesionWeb Abrir(HashDeSesion id, Guid usuarioId, HostDeSesion host, bool esPersonal, DateTimeOffset ahora)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La sesión necesita un usuario.", nameof(usuarioId));
        }

        return new SesionWeb(id, usuarioId, host, ahora, ahora, ahora + PoliticaDeSesion.TopeAbsoluto(esPersonal), EstadoDeSesion.Abierta, null, null);
    }

    /// <summary>Reconstruye una sesión guardada, sin validar de nuevo lo que ya validó quien la abrió.</summary>
    public static SesionWeb Reconstruir(
        HashDeSesion id,
        Guid usuarioId,
        HostDeSesion host,
        DateTimeOffset creada,
        DateTimeOffset ultimaActividad,
        DateTimeOffset expiraAbsoluta,
        EstadoDeSesion estado,
        MotivoDeCierre? motivoDeCierre,
        DateTimeOffset? cerradaEn) =>
        new(id, usuarioId, host, creada, ultimaActividad, expiraAbsoluta, estado, motivoDeCierre, cerradaEn);

    /// <summary>Qué dice la política en <paramref name="ahora"/>. El tope absoluto manda sobre la inactividad. No modifica la sesión.</summary>
    public EvaluacionDeSesion Evaluar(DateTimeOffset ahora)
    {
        if (Estado != EstadoDeSesion.Abierta)
        {
            return EvaluacionDeSesion.Cerrada;
        }

        if (ahora >= ExpiraAbsoluta)
        {
            return EvaluacionDeSesion.VencidaPorTopeAbsoluto;
        }

        return ahora - UltimaActividad >= PoliticaDeSesion.InactividadMaxima ? EvaluacionDeSesion.VencidaPorInactividad : EvaluacionDeSesion.Vigente;
    }

    /// <summary>
    /// Registra una acción de la persona (nunca una consulta automática). Devuelve <see langword="true"/> si hay que escribir la nueva actividad (pasó al menos
    /// un minuto desde la anterior) y <see langword="false"/> si no cambió nada: o por la escritura limitada, o porque la sesión ya no es vigente, que una acción
    /// nunca revive.
    /// </summary>
    public bool RegistrarAccionDelUsuario(DateTimeOffset ahora)
    {
        if (Evaluar(ahora) != EvaluacionDeSesion.Vigente || ahora - UltimaActividad < PoliticaDeSesion.IntervaloMinimoDeActividad)
        {
            return false;
        }

        UltimaActividad = ahora;
        return true;
    }

    /// <summary>Cierra o vence la sesión con su motivo. Cerrar una sesión ya terminada conserva el primer cierre.</summary>
    public void Cerrar(MotivoDeCierre motivo, DateTimeOffset ahora)
    {
        if (Estado != EstadoDeSesion.Abierta)
        {
            return;
        }

        Estado = motivo.EstadoResultante();
        MotivoDeCierre = motivo;
        CerradaEn = ahora;
    }
}
