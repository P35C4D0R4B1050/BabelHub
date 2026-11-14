using System.Text.RegularExpressions;

namespace BabelHub.Inventario.Domain.Auditoria;

/// <summary>
/// Una ventana fija de 5 minutos alineada al reloj (UTC; Lima tiene un desfase de horas enteras, así que alinea igual) en la que se acumulan los
/// rechazos por límite de uso (AUD-28). El evento se fecha en su <see cref="Cierre"/>, y la ventana se cierra solo después de un margen
/// (<see cref="MargenDeCierre"/>) para no perder los rechazos que llegan justo al terminar.
/// </summary>
public sealed record VentanaDeLimite
{
    public static readonly TimeSpan Duracion = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan MargenDeCierre = TimeSpan.FromSeconds(10);

    private VentanaDeLimite(DateTimeOffset inicio) => Inicio = inicio;

    public DateTimeOffset Inicio { get; }

    public DateTimeOffset Cierre => Inicio + Duracion;

    /// <summary>La ventana que contiene el instante: el múltiplo de 5 minutos del reloj inmediatamente anterior o igual.</summary>
    public static VentanaDeLimite Que(DateTimeOffset instante)
    {
        if (instante.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("El instante debe estar en UTC.", nameof(instante));
        }

        var ticks = instante.UtcTicks - (instante.UtcTicks % Duracion.Ticks);
        return new VentanaDeLimite(new DateTimeOffset(ticks, TimeSpan.Zero));
    }

    public bool Contiene(DateTimeOffset instante) => instante >= Inicio && instante < Cierre;

    /// <summary>Si ya pasó el margen tras el cierre y la ventana se puede cerrar.</summary>
    public bool PuedeCerrarse(DateTimeOffset ahora) => ahora >= Cierre + MargenDeCierre;
}

/// <summary>Los rechazos de una clave (huella, límite) en una ventana, con la dirección completa solo mientras la ventana está abierta.</summary>
public sealed record FilaDeAcumulado(string Huella, string Limite, int Rechazos, string? DireccionCompleta);

/// <summary>El único evento de seguridad de una huella, un límite y una ventana: el total de rechazos, fechado en el cierre, con la dirección completa.</summary>
public sealed record EventoDeLimite(string Huella, string Limite, int Rechazos, string? DireccionCompleta, DateTimeOffset Fecha);

public enum ResultadoDeRegistroDeRechazo
{
    /// <summary>Sumó a su propia fila.</summary>
    Acumulado,

    /// <summary>La ventana estaba llena (1000 filas): sumó a la fila «otros».</summary>
    AgrupadoEnOtros,

    /// <summary>La ventana ya se cerró: no se acumula; quien llama debe escribir el evento aparte (llegó tarde).</summary>
    VentanaCerrada,
}

/// <summary>
/// El acumulado de rechazos por límite de una ventana (AUD-28, AUD-41, D11): <b>un solo evento</b> por huella (usuario u origen), límite y ventana de
/// 5 minutos, con el total de rechazos y la dirección completa, que se borra al escribir el evento. Cada rechazo se persiste sin diferirlo (la
/// infraestructura guarda cada fila al registrarlo, de modo que un reinicio no pierde rechazos: lo persistido se recupera con <see cref="Restaurar"/>
/// y se sigue sumando) y la cardinalidad está acotada: como máximo <see cref="TopeDeFilas"/> filas por ventana; los orígenes nuevos que llegan con la
/// ventana llena se agrupan en <see cref="HuellaDeOtros"/>, sin perder ni duplicar rechazos.
/// </summary>
public sealed class AcumuladoDeLimite
{
    /// <summary>El máximo de filas por ventana, incluida la de «otros».</summary>
    public const int TopeDeFilas = 1000;

    /// <summary>La huella reservada de la fila que agrupa los orígenes que no caben.</summary>
    public const string HuellaDeOtros = "otros";

    public const string LimiteDeOtros = "otros";

    /// <summary>La fila cerrada se purga a las 24 horas del cierre (D11).</summary>
    public static readonly TimeSpan RetencionTrasElCierre = TimeSpan.FromHours(24);

    private const int TopeDeDireccion = 64;
    private static readonly Regex FormatoDeHuella = new("^[A-Za-z0-9_-]{8,128}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex FormatoDeLimite = new("^[a-z][a-z0-9-]{1,63}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex FormatoDeDireccion = new(@"^\S{1,64}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private readonly Dictionary<(string Huella, string Limite), FilaDeAcumulado> _filas = new();

    public AcumuladoDeLimite(VentanaDeLimite ventana)
    {
        ArgumentNullException.ThrowIfNull(ventana);
        Ventana = ventana;
    }

    public VentanaDeLimite Ventana { get; }

    public bool Cerrada { get; private set; }

    /// <summary>Una copia de las filas, en un orden estable; no se altera desde fuera.</summary>
    public IReadOnlyList<FilaDeAcumulado> Filas =>
        [.. _filas.Values.OrderBy(f => f.Huella, StringComparer.Ordinal).ThenBy(f => f.Limite, StringComparer.Ordinal)];

    public int TotalDeRechazos => _filas.Values.Sum(f => f.Rechazos);

    /// <summary>Recupera lo persistido (tras un reinicio) para seguir sumando a la misma ventana.</summary>
    public static AcumuladoDeLimite Restaurar(VentanaDeLimite ventana, IReadOnlyCollection<FilaDeAcumulado> filas, bool cerrada)
    {
        ArgumentNullException.ThrowIfNull(filas);
        var acumulado = new AcumuladoDeLimite(ventana);
        foreach (var fila in filas)
        {
            if (fila.Rechazos < 1)
            {
                throw new ArgumentException("Una fila persistida tiene al menos un rechazo.", nameof(filas));
            }

            if (cerrada && fila.DireccionCompleta is not null)
            {
                throw new ArgumentException("Una ventana cerrada ya no conserva la dirección completa.", nameof(filas));
            }

            if (!acumulado._filas.TryAdd((fila.Huella, fila.Limite), fila))
            {
                throw new ArgumentException("Una fila está repetida.", nameof(filas));
            }
        }

        if (acumulado._filas.Count > TopeDeFilas)
        {
            throw new ArgumentException("La ventana supera el tope de filas.", nameof(filas));
        }

        acumulado.Cerrada = cerrada;
        return acumulado;
    }

    public ResultadoDeRegistroDeRechazo Registrar(string huella, string limite, DateTimeOffset instante, string? direccionCompleta)
    {
        if (string.IsNullOrEmpty(huella) || !FormatoDeHuella.IsMatch(huella) || string.Equals(huella, HuellaDeOtros, StringComparison.Ordinal))
        {
            throw new ArgumentException("La huella no es válida (o es la reservada de «otros»).", nameof(huella));
        }

        if (string.IsNullOrEmpty(limite) || !FormatoDeLimite.IsMatch(limite) || string.Equals(limite, LimiteDeOtros, StringComparison.Ordinal))
        {
            throw new ArgumentException("El límite es un código del catálogo (minúsculas, dígitos y guiones).", nameof(limite));
        }

        if (direccionCompleta is not null && (direccionCompleta.Length > TopeDeDireccion || !FormatoDeDireccion.IsMatch(direccionCompleta)))
        {
            throw new ArgumentException("La dirección no es válida.", nameof(direccionCompleta));
        }

        if (instante.Offset != TimeSpan.Zero || !Ventana.Contiene(instante))
        {
            throw new ArgumentException("El rechazo no corresponde a esta ventana.", nameof(instante));
        }

        if (Cerrada)
        {
            return ResultadoDeRegistroDeRechazo.VentanaCerrada;
        }

        var clave = (huella, limite);
        if (_filas.TryGetValue(clave, out var existente))
        {
            // La dirección ya guardada no se cambia (es la del origen que abrió la fila); solo se completa si faltaba.
            _filas[clave] = existente with { Rechazos = existente.Rechazos + 1, DireccionCompleta = existente.DireccionCompleta ?? direccionCompleta };
            return ResultadoDeRegistroDeRechazo.Acumulado;
        }

        if (_filas.Count < TopeDeFilas - 1)
        {
            _filas[clave] = new FilaDeAcumulado(huella, limite, 1, direccionCompleta);
            return ResultadoDeRegistroDeRechazo.Acumulado;
        }

        var otros = (HuellaDeOtros, LimiteDeOtros);
        _filas[otros] = _filas.TryGetValue(otros, out var fila)
            ? fila with { Rechazos = fila.Rechazos + 1 }
            : new FilaDeAcumulado(HuellaDeOtros, LimiteDeOtros, 1, null);
        return ResultadoDeRegistroDeRechazo.AgrupadoEnOtros;
    }

    public bool PuedeCerrarse(DateTimeOffset ahora) => Ventana.PuedeCerrarse(ahora);

    /// <summary>
    /// Cierra la ventana: devuelve el evento de cada fila (fechado en el cierre, con el total y la dirección completa) y borra las direcciones. Cerrar de
    /// nuevo no devuelve nada: ningún evento se duplica. Solo se cierra pasado el margen.
    /// </summary>
    public IReadOnlyList<EventoDeLimite> Cerrar(DateTimeOffset ahora)
    {
        if (Cerrada)
        {
            return [];
        }

        if (!PuedeCerrarse(ahora))
        {
            throw new InvalidOperationException("La ventana todavía no puede cerrarse: falta el margen posterior a su cierre.");
        }

        var eventos = Filas.Select(f => new EventoDeLimite(f.Huella, f.Limite, f.Rechazos, f.DireccionCompleta, Ventana.Cierre)).ToList();
        BorrarDirecciones();
        Cerrada = true;
        return eventos;
    }

    /// <summary>
    /// Borra las direcciones completas conservando solo la huella y los totales (cuando la auditoría sigue caída tras los reintentos, o al cerrar,
    /// AUD-41: la dirección solo vive hasta escribir el evento).
    /// </summary>
    public void BorrarDirecciones()
    {
        foreach (var clave in _filas.Keys.ToList())
        {
            _filas[clave] = _filas[clave] with { DireccionCompleta = null };
        }
    }

    /// <summary>Si la fila cerrada ya pasó las 24 horas de retención desde el cierre de su ventana.</summary>
    public bool EsPurgable(DateTimeOffset ahora) => Cerrada && ahora >= Ventana.Cierre + RetencionTrasElCierre;
}
