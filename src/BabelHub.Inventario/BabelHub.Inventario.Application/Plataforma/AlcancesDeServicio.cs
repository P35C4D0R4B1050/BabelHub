namespace BabelHub.Inventario.Application.Plataforma;

/// <summary>Nombres estables de los alcances de la credencial de servicio; la Api los mapea a las constantes de <c>Shared</c>.</summary>
public static class AlcancesDeServicio
{
    private static readonly IReadOnlyDictionary<AlcanceDeServicio, string> Nombres = new Dictionary<AlcanceDeServicio, string>
    {
        [AlcanceDeServicio.OperacionDeUsuario] = "operacion-de-usuario",
        [AlcanceDeServicio.LecturaDeDisponibilidad] = "lectura-de-disponibilidad",
        [AlcanceDeServicio.ExpiracionDeReservas] = "expiracion-de-reservas",
        [AlcanceDeServicio.ConsultaDePendientes] = "consulta-de-pendientes",
        [AlcanceDeServicio.BajaDeTitular] = "baja-de-titular",
        [AlcanceDeServicio.LecturaDeAlertas] = "lectura-alertas",
        [AlcanceDeServicio.ConsultaDeAuditoria] = "consulta-de-auditoria",
        [AlcanceDeServicio.MarcarAlerta] = "marcar-alerta",
        [AlcanceDeServicio.Historial] = "historial",
        [AlcanceDeServicio.SolicitarAviso] = "solicitar-aviso",
        [AlcanceDeServicio.LecturaDeContacto] = "lectura-de-contacto",
        [AlcanceDeServicio.OrigenReenviado] = "origen-reenviado",
    };

    public static string NombreEstable(this AlcanceDeServicio alcance) =>
        Nombres.TryGetValue(alcance, out var nombre) ? nombre : throw new ArgumentOutOfRangeException(nameof(alcance));

    /// <summary>Convierte el nombre estable en alcance; un nombre desconocido no es ningún alcance (rechazo por defecto).</summary>
    public static bool TryParse(string? nombre, out AlcanceDeServicio alcance)
    {
        foreach (var (clave, valor) in Nombres)
        {
            if (string.Equals(valor, nombre, StringComparison.Ordinal))
            {
                alcance = clave;
                return true;
            }
        }

        alcance = default;
        return false;
    }
}

/// <summary>Operación de una superficie de servicio (un endpoint entre servicios), con un nombre estable como «inventario.reservas.reservar».</summary>
public readonly record struct OperacionDeServicio
{
    public OperacionDeServicio(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor) || valor.Length > 100 || valor.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-')))
        {
            throw new ArgumentException("La operación es un nombre estable de letras, dígitos, puntos y guiones.", nameof(valor));
        }

        Valor = valor;
    }

    public string Valor { get; }

    public override string ToString() => Valor;
}

/// <summary>Una llamada entre servicios tal como la ve la Aplicación: la operación y el alcance de la credencial verificada (ninguno si no trae).</summary>
/// <param name="ContextoDeUsuarioDiferido">Verdadero antes de autenticar: la credencial y la matriz ya se pueden comprobar, y el contexto del usuario de una operación de usuario se comprueba después de autenticar.</param>
public sealed record LlamadaDeServicio(OperacionDeServicio Operacion, AlcanceDeServicio? AlcanceDeLaCredencial, bool ContextoDeUsuarioDiferido = false);

/// <summary>Qué operaciones declaran su alcance y cuál exige cada una; la consulta pública de la matriz (la Api verifica con ella que ningún endpoint de servicio quede sin declarar).</summary>
public interface IMatrizDeAlcancesDeServicio
{
    IReadOnlyCollection<OperacionDeServicio> Operaciones { get; }

    AlcanceDeServicio? Exigido(OperacionDeServicio operacion);
}

public enum MotivoDeRechazoDeLlamada
{
    SinCredencial,

    /// <summary>La operación no declaró su alcance: el rechazo es por defecto.</summary>
    OperacionNoDeclarada,

    OtroAlcance,

    /// <summary>La operación actúa por un usuario y la llamada no lleva el contexto de uno.</summary>
    SinContextoDeUsuario,
}

public sealed record ResultadoDeAutorizacion
{
    private ResultadoDeAutorizacion(MotivoDeRechazoDeLlamada? motivo) => Motivo = motivo;

    public bool Autorizada => Motivo is null;

    public MotivoDeRechazoDeLlamada? Motivo { get; }

    public static ResultadoDeAutorizacion Permitida { get; } = new((MotivoDeRechazoDeLlamada?)null);

    public static ResultadoDeAutorizacion Rechazada(MotivoDeRechazoDeLlamada motivo) => new(motivo);
}

/// <summary>Decide si una llamada entre servicios puede ejecutarse; una rechazada deja un evento de seguridad (SEG-15, AUD-37).</summary>
public interface IAutorizadorDeLlamadasDeServicio
{
    Task<ResultadoDeAutorizacion> Autorizar(LlamadaDeServicio llamada, CancellationToken cancelacion = default);
}

/// <summary>Una llamada entre módulos llegó sin el contexto del actor que la origina (SEG-15).</summary>
public sealed class ContextoDeActorAusenteException : InvalidOperationException
{
    public ContextoDeActorAusenteException()
        : base("La llamada no conserva el contexto del actor que la origina.")
    {
    }

    public ContextoDeActorAusenteException(string message)
        : base(message)
    {
    }

    public ContextoDeActorAusenteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public static class ContextoDelActor
{
    /// <summary>El actor de la llamada; si nadie estableció el contexto, la llamada se rechaza.</summary>
    public static ActorActual Exigir(this IActorActual actores)
    {
        ArgumentNullException.ThrowIfNull(actores);
        return actores.Actual ?? throw new ContextoDeActorAusenteException();
    }
}

/// <summary>Origen ya resuelto de una llamada entre servicios.</summary>
public sealed record OrigenResuelto(OrigenDeSolicitud Origen, bool AplicaLimitesPorOrigen, bool ReenvioIgnorado);

/// <summary>
/// Decide qué dirección vale como origen de una llamada entre servicios (SEG-17, SEG-32): la reenviada solo con la credencial
/// de alcance «origen reenviado»; sin ella se ignora, se registra y solo rigen los límites por usuario y el tope general de la
/// dirección directa.
/// </summary>
public interface IResolutorDeOrigenDeServicio
{
    Task<OrigenResuelto> Resolver(string? direccionDirecta, string? direccionReenviada, bool credencialDeOrigenValida, CancellationToken cancelacion = default);
}
