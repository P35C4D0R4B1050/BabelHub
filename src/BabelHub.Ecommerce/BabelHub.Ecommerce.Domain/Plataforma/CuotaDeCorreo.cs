namespace BabelHub.Ecommerce.Domain.Plataforma;

/// <summary>Los cupos de la cuota diaria de correo (IDE-66): un reparto de la cuota total de la cuenta de envío.</summary>
public enum CupoDeCorreo
{
    /// <summary>Exclusivo de las alertas a la administración: ningún aviso provocable por un usuario lo consume.</summary>
    Administracion,

    /// <summary>De las alertas críticas de cuenta (dispositivo no reconocido, segundo factor restablecido, contraseña cambiada).</summary>
    CriticasDeCuenta,

    /// <summary>De los códigos de verificación y de recuperación.</summary>
    Codigos,

    /// <summary>Todo lo demás: avisos no críticos, correos de reclamos y de pagos.</summary>
    Resto,
}

/// <summary>Qué clase de correo es, para elegir los cupos que puede usar y qué pasa si no hay (IDE-66, IDE-77, IDE-78, IDE-80).</summary>
public enum ClaseDeCorreoParaCuota
{
    AlertaALaAdministracion,
    AlertaCriticaDeCuenta,
    Codigo,
    AvisoNoCritico,
    Reclamo,
    Pago,
}

/// <summary>Qué hacer con un correo cuando no queda cupo utilizable.</summary>
public enum SinCupoDeCorreo
{
    /// <summary>No sale y no se guarda: un código vence a los 10 minutos y un aviso no crítico se muestra en la cuenta (IDE-65, IDE-79).</summary>
    Suprimir,

    /// <summary>Queda pendiente y se reintenta solo al renovarse la cuota diaria, con prioridad (IDE-78, IDE-80).</summary>
    Encolar,
}

/// <summary>
/// La cuota diaria de correo y su reparto en cupos, configurables (IDE-66; valores iniciales de <c>progress/decisiones-previas-al-plan.md</c>: 300 al día, 15 % para la administración,
/// 15 % para las alertas críticas, 25 % para los códigos y 45 % para todo lo demás). <see cref="Validar"/> se llama al arrancar y falla si los porcentajes suman más de 100 %.
/// </summary>
public sealed record ConfiguracionDeCuotaDeCorreo
{
    public int TotalDiario { get; init; } = 300;

    public int PorcentajeDeAdministracion { get; init; } = 15;

    public int PorcentajeDeCriticas { get; init; } = 15;

    public int PorcentajeDeCodigos { get; init; } = 25;

    public int PorcentajeDeResto { get; init; } = 45;

    /// <summary>Tope diario de correos de alertas a la administración por motivo y servicio (IDE-82).</summary>
    public int TopeDeAlertasPorMotivoYServicio { get; init; } = 24;

    /// <summary>Porcentaje de la cuota (o del cupo de códigos) que dispara la alerta técnica (IDE-73).</summary>
    public int PorcentajeDeAlerta { get; init; } = 80;

    public static ConfiguracionDeCuotaDeCorreo Predeterminada { get; } = new();

    /// <summary>Falla si la configuración no es utilizable: sin cuota, un porcentaje negativo o que pasa de 100, o cupos que suman más de 100 %.</summary>
    public ConfiguracionDeCuotaDeCorreo Validar()
    {
        if (TotalDiario <= 0)
        {
            throw new ArgumentException("La cuota diaria de correo debe ser mayor que cero.", nameof(TotalDiario));
        }

        int[] porcentajes = [PorcentajeDeAdministracion, PorcentajeDeCriticas, PorcentajeDeCodigos, PorcentajeDeResto];
        if (porcentajes.Any(p => p is <= 0 or > 100))
        {
            throw new ArgumentException("Cada cupo es un porcentaje entre 1 y 100.", nameof(PorcentajeDeAdministracion));
        }

        if (porcentajes.Sum() > 100)
        {
            throw new ArgumentException($"Los cupos suman {porcentajes.Sum()} % y no pueden pasar de 100 %.", nameof(PorcentajeDeAdministracion));
        }

        if (TopeDeAlertasPorMotivoYServicio <= 0)
        {
            throw new ArgumentException("El tope de alertas por motivo y servicio debe ser mayor que cero.", nameof(TopeDeAlertasPorMotivoYServicio));
        }

        if (PorcentajeDeAlerta is <= 0 or > 100)
        {
            throw new ArgumentException("El porcentaje de la alerta es entre 1 y 100.", nameof(PorcentajeDeAlerta));
        }

        return this;
    }

    /// <summary>Cuántos correos tiene un cupo al día (redondeado hacia abajo, y al menos uno).</summary>
    public int Tamano(CupoDeCorreo cupo)
    {
        var porcentaje = cupo switch
        {
            CupoDeCorreo.Administracion => PorcentajeDeAdministracion,
            CupoDeCorreo.CriticasDeCuenta => PorcentajeDeCriticas,
            CupoDeCorreo.Codigos => PorcentajeDeCodigos,
            CupoDeCorreo.Resto => PorcentajeDeResto,
            _ => throw new ArgumentOutOfRangeException(nameof(cupo), cupo, "Cupo desconocido."),
        };
        return Math.Max(1, (int)((long)TotalDiario * porcentaje / 100));
    }
}

/// <summary>Un cupo candidato para un correo y su tamaño: el almacén reserva en el primero que tenga lugar.</summary>
public sealed record CupoCandidato(CupoDeCorreo Cupo, int Tamano);

/// <summary>Lo que cada clase de correo puede consumir y qué pasa si no hay (IDE-66, IDE-77, IDE-78, IDE-80), como regla pura.</summary>
public sealed class PoliticaDeCuotaDeCorreo(ConfiguracionDeCuotaDeCorreo configuracion)
{
    public ConfiguracionDeCuotaDeCorreo Configuracion { get; } = configuracion.Validar();

    /// <summary>
    /// Los cupos que puede usar la clase, en el orden en que los consume (primero el propio). Las alertas a la administración usan su cupo exclusivo y después el resto; las críticas, el suyo y el resto,
    /// <b>nunca</b> el de la administración ni el de códigos; los códigos, solo el suyo; los avisos no críticos y los correos de reclamos y de pagos, el resto. Ningún aviso provocable por un usuario
    /// llega al cupo de la administración.
    /// </summary>
    public IReadOnlyList<CupoCandidato> Candidatos(ClaseDeCorreoParaCuota clase)
    {
        CupoDeCorreo[] cupos = clase switch
        {
            ClaseDeCorreoParaCuota.AlertaALaAdministracion => [CupoDeCorreo.Administracion, CupoDeCorreo.Resto],
            ClaseDeCorreoParaCuota.AlertaCriticaDeCuenta => [CupoDeCorreo.CriticasDeCuenta, CupoDeCorreo.Resto],
            ClaseDeCorreoParaCuota.Codigo => [CupoDeCorreo.Codigos],
            ClaseDeCorreoParaCuota.AvisoNoCritico or ClaseDeCorreoParaCuota.Reclamo or ClaseDeCorreoParaCuota.Pago => [CupoDeCorreo.Resto],
            _ => throw new ArgumentOutOfRangeException(nameof(clase), clase, "Clase de correo desconocida."),
        };
        return [.. cupos.Select(c => new CupoCandidato(c, Configuracion.Tamano(c)))];
    }

    /// <summary>Si la clase se suprime o se encola cuando no hay cupo: solo los códigos y los avisos no críticos se suprimen (IDE-65, IDE-77, IDE-78, IDE-79, IDE-80).</summary>
    public static SinCupoDeCorreo SinCupo(ClaseDeCorreoParaCuota clase) => clase switch
    {
        ClaseDeCorreoParaCuota.Codigo or ClaseDeCorreoParaCuota.AvisoNoCritico => SinCupoDeCorreo.Suprimir,
        ClaseDeCorreoParaCuota.AlertaALaAdministracion or ClaseDeCorreoParaCuota.AlertaCriticaDeCuenta or ClaseDeCorreoParaCuota.Reclamo or ClaseDeCorreoParaCuota.Pago => SinCupoDeCorreo.Encolar,
        _ => throw new ArgumentOutOfRangeException(nameof(clase), clase, "Clase de correo desconocida."),
    };

    /// <summary>
    /// La prioridad al reintentar lo pendiente cuando se renueva la cuota: las alertas a la administración primero (0), después las críticas (1) y por último los reclamos y los pagos (2). Solo lo que
    /// se encola tiene prioridad.
    /// </summary>
    public static int Prioridad(ClaseDeCorreoParaCuota clase) => clase switch
    {
        ClaseDeCorreoParaCuota.AlertaALaAdministracion => 0,
        ClaseDeCorreoParaCuota.AlertaCriticaDeCuenta => 1,
        ClaseDeCorreoParaCuota.Reclamo or ClaseDeCorreoParaCuota.Pago => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(clase), clase, "Esta clase no se encola."),
    };

    /// <summary>¿Cruzó este envío el umbral de alerta (80 %) del total o del cupo? Verdadero solo en el envío que lo cruza (<paramref name="antes"/> por debajo, <paramref name="despues"/> en o sobre él).</summary>
    public bool CruzaElUmbral(int antes, int despues, int tamano) =>
        (long)antes * 100 < (long)tamano * Configuracion.PorcentajeDeAlerta && (long)despues * 100 >= (long)tamano * Configuracion.PorcentajeDeAlerta;

    /// <summary>¿Cabe una alerta más a la administración de ese motivo y servicio hoy? (IDE-82)</summary>
    public bool CabeUnaAlertaMas(int enviadasHoy) => enviadasHoy < Configuracion.TopeDeAlertasPorMotivoYServicio;
}
