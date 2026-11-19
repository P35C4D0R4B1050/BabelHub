namespace BabelHub.Ecommerce.Domain.Plataforma;

/// <summary>
/// Políticas de límite de uso de la identidad (SEG-06, SEG-19, SEG-20, SEG-24 y SEG-29). Los valores son los iniciales de
/// la spec; la Aplicación los sobrescribe con la configuración cuando se ajustan. Ninguna cae al motor local (SEG-26):
/// ingreso, registro y códigos fallan cerrados cuando el almacén de contadores no responde.
/// </summary>
public static class PoliticasDeSeguridad
{
    /// <summary>5 ingresos fallidos de una combinación cuenta-origen en 15 minutos bloquean 15 minutos (SEG-06). Un ingreso correcto devuelve el cupo.</summary>
    public static PoliticaDeLimite IngresosFallidos { get; } = new(
        "ingresos-fallidos", 5, TimeSpan.FromMinutes(15), TipoDeSujetoDeLimite.Combinacion,
        duracionDeBloqueo: TimeSpan.FromMinutes(15), liberable: true);

    /// <summary>3 solicitudes de código por hora y por correo destino, exista o no la cuenta (SEG-19).</summary>
    public static PoliticaDeLimite CodigosPorCorreo { get; } = new(
        "codigos-por-correo", 3, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Usuario);

    /// <summary>3 solicitudes de código por hora y por origen (SEG-19).</summary>
    public static PoliticaDeLimite CodigosPorOrigen { get; } = new(
        "codigos-por-origen", 3, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Origen);

    /// <summary>5 segundos factores incorrectos de una combinación cuenta-origen en 15 minutos bloquean 15 minutos (SEG-20).</summary>
    public static PoliticaDeLimite SegundoFactorPorOrigen { get; } = new(
        "segundo-factor-por-origen", 5, TimeSpan.FromMinutes(15), TipoDeSujetoDeLimite.Combinacion,
        duracionDeBloqueo: TimeSpan.FromMinutes(15), liberable: true);

    /// <summary>10 segundos factores incorrectos por cuenta y hora, desde cualquier origen, bloquean 15 minutos (SEG-29).</summary>
    public static PoliticaDeLimite SegundoFactorPorCuenta { get; } = new(
        "segundo-factor-por-cuenta", 10, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Usuario,
        duracionDeBloqueo: TimeSpan.FromMinutes(15), liberable: true);

    /// <summary>
    /// Tope de avisos de seguridad <b>no críticos</b> por correo, por cuenta y 24 horas (IDE-74): 3 por defecto, configurable. El que lo supera no sale por correo y queda en la cuenta (IDE-79). No cuenta las alertas críticas,
    /// las alertas a la administración ni los correos de reclamos y de pagos.
    /// </summary>
    public static PoliticaDeLimite AvisosNoCriticosPorCuenta { get; } = new(
        "avisos-no-criticos-por-cuenta", 3, TimeSpan.FromHours(24), TipoDeSujetoDeLimite.Usuario, liberable: true);

    /// <summary>La alerta crítica de cuenta es la primera de cada tipo por cuenta en 24 horas (IDE-74): el sujeto es la cuenta con el tipo, así una de otro tipo en la misma ventana sale igual.</summary>
    public static PoliticaDeLimite AlertasCriticasPorTipoYCuenta { get; } = new(
        "alertas-criticas-por-tipo-y-cuenta", 1, TimeSpan.FromHours(24), TipoDeSujetoDeLimite.Usuario, liberable: true);

    /// <summary>Tope diario por origen de las alertas críticas de cuenta que salen por correo (IDE-86): 20 al día por defecto, configurable (valor inicial a confirmar con la persona responsable).</summary>
    public static PoliticaDeLimite AlertasCriticasPorOrigen { get; } = new(
        "alertas-criticas-por-origen", 20, TimeSpan.FromHours(24), TipoDeSujetoDeLimite.Origen, liberable: true);

    /// <summary>Tope de 10 al día por origen del consumo del cupo de códigos de verificación y de recuperación, de modo que un origen no pueda agotarlo (IDE-83).</summary>
    public static PoliticaDeLimite CupoDeCodigosPorOrigen { get; } = new(
        "cupo-de-codigos-por-origen", 10, TimeSpan.FromHours(24), TipoDeSujetoDeLimite.Origen);

    /// <summary>3 cuentas nuevas por hora y por origen, con una ventana deslizante (SEG-24).</summary>
    public static PoliticaDeLimite RegistrosPorOrigen { get; } = new(
        "registros-por-origen", 3, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Origen);

    /// <summary>3 hojas de reclamación por hora y por origen, con una ventana deslizante (REC-11). No falla cerrada: con el almacén de contadores caído cae al límite local por instancia (SEG-26).</summary>
    public static PoliticaDeLimite HojasPorOrigen { get; } = new(
        "hojas-por-origen", 3, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Origen, respaldoLocal: true);

    /// <summary>Un consumidor con sesión consultando sus hojas de reclamación: 60 por minuto (plan de reclamaciones, REC-10), valor inicial configurable.</summary>
    public static PoliticaDeLimite ConsultasDeHojasPorUsuario { get; } = new(
        "consultas-de-hojas-por-usuario", 60, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.Usuario, respaldoLocal: true);

    /// <summary>5 intentos de cobro por intención y hora (PAG-17, D18). Falla cerrada: una operación de dinero no cae a un contador local (SEG-26).</summary>
    public static PoliticaDeLimite CobrosPorIntencion { get; } = new("cobros-por-intencion", 5, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Usuario);

    /// <summary>10 intentos de cobro por cuenta y hora (D18, AM38). Falla cerrada.</summary>
    public static PoliticaDeLimite CobrosPorCuenta { get; } = new("cobros-por-cuenta", 10, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Usuario);

    /// <summary>20 intentos de cobro por origen y hora (D18, AM38). Falla cerrada.</summary>
    public static PoliticaDeLimite CobrosPorOrigen { get; } = new("cobros-por-origen", 20, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Origen);

    /// <summary>Crear carritos de visitante: 10 por origen y hora (plan de carrito-checkout §6, SEG-05). No falla cerrada: con el almacén caído cae al límite local (SEG-26).</summary>
    public static PoliticaDeLimite CarritosDeVisitantePorOrigen { get; } = new(
        "carritos-de-visitante-por-origen", 10, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Origen, respaldoLocal: true);

    /// <summary>Agregar, cambiar o quitar líneas del carrito: 60 por cuenta y minuto (la cuenta si la hay).</summary>
    public static PoliticaDeLimite EdicionDeLineasPorCuenta { get; } = new(
        "edicion-de-lineas-por-cuenta", 60, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.Usuario, respaldoLocal: true);

    /// <summary>Agregar, cambiar o quitar líneas del carrito de un visitante: 60 por origen y minuto.</summary>
    public static PoliticaDeLimite EdicionDeLineasPorOrigen { get; } = new(
        "edicion-de-lineas-por-origen", 60, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.Origen, respaldoLocal: true);

    /// <summary>Revisar el checkout y elegir la entrega consultan el Inventario sin memoria: 30 por cuenta y minuto.</summary>
    public static PoliticaDeLimite RevisionDelCheckoutPorCuenta { get; } = new(
        "revision-del-checkout-por-cuenta", 30, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.Usuario, respaldoLocal: true);

    /// <summary>Pasar al pago reserva stock: 10 por cuenta y hora. <b>Falla cerrada</b> (SEG-26): una operación que reserva stock no cae a un contador local; repetir el mismo pedido no cuenta otra vez (DD-12).</summary>
    public static PoliticaDeLimite PasosAlPagoPorCuenta { get; } = new("pasos-al-pago-por-cuenta", 10, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Usuario);

    /// <summary>Volver al carrito y reiniciar el checkout: 20 por cuenta y hora.</summary>
    public static PoliticaDeLimite VolverOReiniciarPorCuenta { get; } = new(
        "volver-o-reiniciar-por-cuenta", 20, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Usuario, respaldoLocal: true);

    /// <summary>Las consultas de envíos (el cliente por su pedido, logística por la lista): 60 por usuario y minuto (plan de envíos §6, SEG-05).</summary>
    public static PoliticaDeLimite ConsultasDeEnviosPorUsuario { get; } = new(
        "consultas-de-envios-por-usuario", 60, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.Usuario, respaldoLocal: true);

    /// <summary>Las operaciones de envíos (pedir la guía, pasar a en reparto, marcar entregado): 30 por usuario y minuto (SEG-07).</summary>
    public static PoliticaDeLimite OperacionesDeEnviosPorUsuario { get; } = new(
        "operaciones-de-envios-por-usuario", 30, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.Usuario, respaldoLocal: true);

    /// <summary>Las solicitudes de guía al operador logístico: 30 por minuto en total (el sujeto es una constante interna, no una persona); protege al operador de una ráfaga.</summary>
    public static PoliticaDeLimite GuiasAlOperador { get; } = new(
        "guias-al-operador", 30, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.Usuario, respaldoLocal: true);

    /// <summary>Las consultas de pedidos (el cliente por su cuenta, el personal por la lista y el detalle): 60 por usuario y minuto (plan de pedidos, SEG-05).</summary>
    public static PoliticaDeLimite ConsultasDePedidosPorUsuario { get; } = new(
        "consultas-de-pedidos-por-usuario", 60, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.Usuario, respaldoLocal: true);

    /// <summary>La gestión de pedidos (anular, entregar, reintentar el reembolso, cancelar): 30 por usuario y minuto (SEG-07).</summary>
    public static PoliticaDeLimite GestionDePedidosPorUsuario { get; } = new(
        "gestion-de-pedidos-por-usuario", 30, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.Usuario, respaldoLocal: true);

    /// <summary>Las lecturas del detalle de un pedido con el contacto del cliente: 200 por persona y hora. <b>Crítica y falla cerrada</b> (SEG-26): un dato personal no se sirve sin contador; sin el almacén, la lectura con contacto no se sirve.</summary>
    public static PoliticaDeLimite LecturasDelContactoDePedidosPorPersona { get; } = new(
        "lecturas-del-contacto-de-pedidos-por-persona", 200, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Usuario);

    /// <summary>La ráfaga de avisos de la pasarela de pagos (PAG-05, SEG-05): 120 por origen y minuto, antes de verificar la firma, para que una entrada pública no pueda agotar el cómputo. Con el almacén caído cae al límite local (SEG-26); el aviso legítimo se reintenta.</summary>
    public static PoliticaDeLimite AvisosDeLaPasarelaPorOrigen { get; } = new(
        "avisos-de-la-pasarela-por-origen", 120, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.Origen, respaldoLocal: true);

    /// <summary>Tope previo barato por huella de origen, antes de autenticar: 600 por minuto (el bucket del origen desconocido tiene el suyo).</summary>
    public static PoliticaDeLimite SolicitudesPreviasPorOrigen { get; } = new(
        "solicitudes-previas-por-origen", 600, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.Origen, respaldoLocal: true);

    /// <summary>Tope previo del «origen desconocido» (SEG-23): más alto, porque en él caen todos los que no se pueden identificar.</summary>
    public static PoliticaDeLimite SolicitudesDelOrigenDesconocido { get; } = new(
        "solicitudes-del-origen-desconocido", 3000, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.Origen, respaldoLocal: true);

    /// <summary>Un mismo origen sin sesión: 60 solicitudes por minuto (SEG-18).</summary>
    public static PoliticaDeLimite SolicitudesSinSesionPorOrigen { get; } = new(
        "solicitudes-sin-sesion-por-origen", 60, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.Origen, respaldoLocal: true);

    /// <summary>Un usuario autenticado: 100 solicitudes por minuto (SEG-05).</summary>
    public static PoliticaDeLimite SolicitudesPorUsuario { get; } = new(
        "solicitudes-por-usuario", 100, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.Usuario, respaldoLocal: true);

    /// <summary>Tope general de la dirección directa de una llamada entre servicios cuya dirección reenviada se ignoró (SEG-32): 120 por minuto, configurable.</summary>
    public static PoliticaDeLimite SolicitudesPorHuellaDirecta { get; } = new(
        "solicitudes-por-huella-directa", 120, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.OrigenDirecto, respaldoLocal: true);

    /// <summary>Límites generales de uso: con el almacén caído caen al límite local por instancia (SEG-26), a diferencia de los críticos de <see cref="Todas"/>.</summary>
    public static IReadOnlyList<PoliticaDeLimite> Generales { get; } =
        [SolicitudesPreviasPorOrigen, SolicitudesDelOrigenDesconocido, SolicitudesSinSesionPorOrigen, SolicitudesPorUsuario, SolicitudesPorHuellaDirecta, HojasPorOrigen, ConsultasDeHojasPorUsuario,
         CarritosDeVisitantePorOrigen, EdicionDeLineasPorCuenta, EdicionDeLineasPorOrigen, RevisionDelCheckoutPorCuenta, VolverOReiniciarPorCuenta,
         ConsultasDeEnviosPorUsuario, OperacionesDeEnviosPorUsuario, GuiasAlOperador, AvisosDeLaPasarelaPorOrigen, ConsultasDePedidosPorUsuario, GestionDePedidosPorUsuario];

    public static IReadOnlyList<PoliticaDeLimite> Todas { get; } =
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
        LecturasDelContactoDePedidosPorPersona,
    ];
}
