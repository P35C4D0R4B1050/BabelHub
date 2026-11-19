using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>
/// Clave estable y tipada de una política de límite. Los demás módulos nombran las políticas con ella: los valores
/// (máximo, ventana, bloqueo) son datos de la plataforma (D04) y no salen de su Dominio.
/// </summary>
public readonly partial record struct ClaveDePolitica
{
    public ClaveDePolitica(string valor)
    {
        if (string.IsNullOrEmpty(valor) || !Forma().IsMatch(valor))
        {
            throw new ArgumentException("La clave de política es un identificador estable en minúsculas, con guiones.", nameof(valor));
        }

        Valor = valor;
    }

    public string Valor { get; }

    public override string ToString() => Valor;

    [GeneratedRegex("^[a-z][a-z0-9-]{1,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex Forma();
}

/// <summary>Claves de las políticas que usan los módulos del E-commerce (los valores están en el Dominio de Plataforma).</summary>
public static class PoliticasDeLimite
{
    public static ClaveDePolitica IngresosFallidos { get; } = new("ingresos-fallidos");

    public static ClaveDePolitica CodigosPorCorreo { get; } = new("codigos-por-correo");

    public static ClaveDePolitica CodigosPorOrigen { get; } = new("codigos-por-origen");

    public static ClaveDePolitica SegundoFactorPorOrigen { get; } = new("segundo-factor-por-origen");

    public static ClaveDePolitica SegundoFactorPorCuenta { get; } = new("segundo-factor-por-cuenta");

    public static ClaveDePolitica RegistrosPorOrigen { get; } = new("registros-por-origen");

    /// <summary>3 avisos de seguridad no críticos por correo por cuenta y 24 horas (IDE-74).</summary>
    public static ClaveDePolitica AvisosNoCriticosPorCuenta { get; } = new("avisos-no-criticos-por-cuenta");

    /// <summary>La primera alerta crítica de cada tipo por cuenta en 24 horas (IDE-74).</summary>
    public static ClaveDePolitica AlertasCriticasPorTipoYCuenta { get; } = new("alertas-criticas-por-tipo-y-cuenta");

    /// <summary>Tope diario por origen de las alertas críticas de cuenta por correo (IDE-86).</summary>
    public static ClaveDePolitica AlertasCriticasPorOrigen { get; } = new("alertas-criticas-por-origen");

    /// <summary>10 al día por origen del consumo del cupo de códigos (IDE-83).</summary>
    public static ClaveDePolitica CupoDeCodigosPorOrigen { get; } = new("cupo-de-codigos-por-origen");

    /// <summary>3 hojas de reclamación por hora y por origen (REC-11).</summary>
    public static ClaveDePolitica HojasPorOrigen { get; } = new("hojas-por-origen");

    /// <summary>60 consultas de hojas por usuario y minuto.</summary>
    public static ClaveDePolitica ConsultasDeHojasPorUsuario { get; } = new("consultas-de-hojas-por-usuario");

    /// <summary>5 intentos de cobro por intención y hora.</summary>
    public static ClaveDePolitica CobrosPorIntencion { get; } = new("cobros-por-intencion");

    /// <summary>10 intentos de cobro por cuenta y hora.</summary>
    public static ClaveDePolitica CobrosPorCuenta { get; } = new("cobros-por-cuenta");

    /// <summary>20 intentos de cobro por origen y hora.</summary>
    public static ClaveDePolitica CobrosPorOrigen { get; } = new("cobros-por-origen");

    /// <summary>Los límites de uso de Carrito (plan de carrito-checkout §6): crear carritos de visitante, editar líneas, revisar el checkout, pasar al pago (falla cerrada) y volver o reiniciar.</summary>
    public static ClaveDePolitica CarritosDeVisitantePorOrigen { get; } = new("carritos-de-visitante-por-origen");

    public static ClaveDePolitica EdicionDeLineasPorCuenta { get; } = new("edicion-de-lineas-por-cuenta");

    public static ClaveDePolitica EdicionDeLineasPorOrigen { get; } = new("edicion-de-lineas-por-origen");

    public static ClaveDePolitica RevisionDelCheckoutPorCuenta { get; } = new("revision-del-checkout-por-cuenta");

    public static ClaveDePolitica PasosAlPagoPorCuenta { get; } = new("pasos-al-pago-por-cuenta");

    public static ClaveDePolitica VolverOReiniciarPorCuenta { get; } = new("volver-o-reiniciar-por-cuenta");

    /// <summary>Los límites de uso de Envíos (plan de envíos §6): consultas y operaciones por usuario y las solicitudes de guía al operador.</summary>
    public static ClaveDePolitica ConsultasDeEnviosPorUsuario { get; } = new("consultas-de-envios-por-usuario");

    public static ClaveDePolitica OperacionesDeEnviosPorUsuario { get; } = new("operaciones-de-envios-por-usuario");

    public static ClaveDePolitica GuiasAlOperador { get; } = new("guias-al-operador");

    /// <summary>La ráfaga de avisos de la pasarela de pagos: 120 por origen y minuto, antes de verificar la firma (PAG-05).</summary>
    public static ClaveDePolitica AvisosDeLaPasarelaPorOrigen { get; } = new("avisos-de-la-pasarela-por-origen");

    public static ClaveDePolitica SolicitudesPreviasPorOrigen { get; } = new("solicitudes-previas-por-origen");

    public static ClaveDePolitica SolicitudesDelOrigenDesconocido { get; } = new("solicitudes-del-origen-desconocido");

    public static ClaveDePolitica SolicitudesSinSesionPorOrigen { get; } = new("solicitudes-sin-sesion-por-origen");

    public static ClaveDePolitica SolicitudesPorUsuario { get; } = new("solicitudes-por-usuario");

    public static ClaveDePolitica SolicitudesPorHuellaDirecta { get; } = new("solicitudes-por-huella-directa");

    /// <summary>Los límites generales de uso (con respaldo local), aparte de las políticas críticas de <see cref="Todas"/>.</summary>
    public static IReadOnlyList<ClaveDePolitica> Generales { get; } =
        [SolicitudesPreviasPorOrigen, SolicitudesDelOrigenDesconocido, SolicitudesSinSesionPorOrigen, SolicitudesPorUsuario, SolicitudesPorHuellaDirecta, HojasPorOrigen, ConsultasDeHojasPorUsuario,
         CarritosDeVisitantePorOrigen, EdicionDeLineasPorCuenta, EdicionDeLineasPorOrigen, RevisionDelCheckoutPorCuenta, VolverOReiniciarPorCuenta,
         ConsultasDeEnviosPorUsuario, OperacionesDeEnviosPorUsuario, GuiasAlOperador, AvisosDeLaPasarelaPorOrigen];

    public static IReadOnlyList<ClaveDePolitica> Todas { get; } =
    [
        IngresosFallidos,
        CodigosPorCorreo,
        CodigosPorOrigen,
        SegundoFactorPorOrigen,
        SegundoFactorPorCuenta,
        RegistrosPorOrigen,
        AvisosNoCriticosPorCuenta,
        AlertasCriticasPorTipoYCuenta,
        AlertasCriticasPorOrigen,
        CupoDeCodigosPorOrigen,
        CobrosPorIntencion,
        CobrosPorCuenta,
        CobrosPorOrigen,
        PasosAlPagoPorCuenta,
    ];
}
