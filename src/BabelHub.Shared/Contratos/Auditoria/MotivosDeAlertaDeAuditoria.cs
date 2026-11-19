namespace BabelHub.Shared.Contratos.Auditoria;

/// <summary>
/// El catálogo cerrado de motivos de alerta (D06) tal como viaja en <c>motivo</c> de <see cref="AlertaDto"/>: nunca el texto de una excepción. Una prueba de contrato de cada servicio compara esta lista con el
/// catálogo de su módulo <c>Auditoria</c> (T21).
/// </summary>
public static class MotivosDeAlertaDeAuditoria
{
    public const string AuditoriaNoDisponible = "auditoria-no-disponible";

    public const string AccesosDenegados = "accesos-denegados";

    public const string IngresosFallidos = "ingresos-fallidos";

    public const string CredencialDeServicioAnteriorEnUso = "credencial-de-servicio-anterior-en-uso";

    public const string DependenciaNoNecesariaFallida = "dependencia-no-necesaria-fallida";

    public const string TrabajoDeFondoDetenido = "trabajo-de-fondo-detenido";

    public const string MemoriaDeContadoresAlta = "memoria-de-contadores-alta";

    public const string VerificadorDeCredencialesSinClaves = "verificador-de-credenciales-sin-claves";

    public const string RevocacionDeSesionesPendiente = "revocacion-de-sesiones-pendiente";

    public const string AcumuladoDeLimitesNoDisponible = "acumulado-de-limites-no-disponible";

    public const string ConteoDeIngresosNoDisponible = "conteo-de-ingresos-no-disponible";

    public const string MotivoNoCatalogado = "motivo-de-alerta-no-catalogado";

    public const string CorreoDeAlertasNoDisponible = "correo-de-alertas-no-disponible";

    public const string DepuracionDiariaSinExito = "depuracion-diaria-sin-exito";

    public const string BajaDeTitularSinEntregar = "baja-de-titular-sin-entregar";

    public const string TrasladoVinculadoSinAprobar = "traslado-vinculado-sin-aprobar";

    public const string RetomaDePagosFallida = "retoma-de-pagos-fallida";

    public const string ReservasConfirmadasInconsistentes = "reservas-confirmadas-inconsistentes";

    public const string ReservaConfirmadaInexistente = "reserva-confirmada-inexistente";

    public const string ReservaConfirmadaSinLiberar = "reserva-confirmada-sin-liberar";

    public const string ReintentoDeReembolsosFallido = "reintento-de-reembolsos-fallido";

    public const string RegistroDelCobroFallido = "registro-del-cobro-fallido";

    public const string ReembolsoPedidoSinRegistrar = "reembolso-pedido-sin-registrar";

    public const string PurgaDeContactoFallida = "purga-de-contacto-fallida";

    public const string ConsultaDePagoFallida = "consulta-de-pago-fallida";

    public const string ConfirmacionDeCompraFallida = "confirmacion-de-compra-fallida";

    public const string CompraAprobadaSinReferencia = "compra-aprobada-sin-referencia";

    public const string CompraAprobadaSinCompletar = "compra-aprobada-sin-completar";

    public const string CompensacionNoAplicable = "compensacion-no-aplicable";

    public const string CompensacionDeCompraFallida = "compensacion-fallida";

    public const string CobroDudosoParaConciliar = "cobro-dudoso-para-conciliar";

    public const string CierreDeIntencionesFallido = "cierre-de-intenciones-fallido";

    public const string TrabajoDePagosSinCorrer = "trabajo-de-pagos-sin-correr";

    public const string CargoDuplicadoParaConciliar = "cargo-duplicado-para-conciliar";

    public const string AvisoDePagoFallido = "aviso-de-pago-fallido";

    public const string AvisoACarritoFallido = "aviso-a-carrito-fallido";

    public const string CompraAprobadaSinPedidoALos30Dias = "compra-aprobada-sin-pedido-a-los-30-dias";

    public const string AnonimizacionDeHojasSinCorrer = "anonimizacion-de-hojas-sin-correr";

    public const string LecturaDeAlertasDelInventarioNoDisponible = "lectura-de-alertas-del-inventario-no-disponible";

    public const string AlcanceDeDatosSinActor = "alcance-de-datos-sin-actor";

    /// <summary>Más de 200 confirmaciones de reservas en 5 minutos: la residual de la credencial de <c>expiracion-de-reservas</c> (INV-2b).</summary>
    public const string ConfirmacionesDeReservasAnomalas = "confirmaciones-de-reservas-anomalas";

    /// <summary>Más de 200 liberaciones de reservas en 5 minutos (INV-2b).</summary>
    public const string LiberacionesDeReservasAnomalas = "liberaciones-de-reservas-anomalas";

    /// <summary>Más de 20 bajas de titular en un minuto (SEL-34, AM31): solo avisa, la baja es una obligación legal y nunca se rechaza.</summary>
    public const string BajasDeTitularAnomalas = "bajas-de-titular-anomalas";

    /// <summary>El acceso a pantallas bloqueado por textos legales pendientes superó el umbral en una ventana de 15 minutos (WEB-47): no cuenta para SEG-09.</summary>
    public const string BloqueosPorTextosPendientes = "bloqueos-por-textos-pendientes";

    /// <summary>La cuota diaria de correo llegó al 80 % (IDE-73).</summary>
    public const string CuotaDeCorreoAlta = "cuota-de-correo-alta";

    /// <summary>El cupo de códigos de verificación y de recuperación llegó al 80 % (IDE-73).</summary>
    public const string CupoDeCodigosAlto = "cupo-de-codigos-alto";

    /// <summary>Se reutilizó una renovación ya rotada y se revocó su familia (D02, IDE-14).</summary>
    public const string RenovacionReutilizada = "renovacion-reutilizada";

    /// <summary>El Notificador rechazó la plantilla o los campos del aviso de un envío (ENV-06).</summary>
    public const string EnvioAvisoRechazado = "envio-aviso-rechazado";

    /// <summary>El operador logístico devolvió un número de guía que ya pertenece a otro envío (ENV-04).</summary>
    public const string EnvioGuiaRepetida = "envio-guia-repetida";

    /// <summary>Pedidos no recibió el avance de un envío (guía generada, en reparto o entregado) en más de una hora.</summary>
    public const string EnvioProgresoSinInformar = "envio-progreso-sin-informar";

    /// <summary>Una guía de un envío cancelado quedó sin uso en el operador logístico y hay que avisarle (R05).</summary>
    public const string EnvioGuiaSinUso = "envio-guia-sin-uso";

    /// <summary>Un trabajo de fondo de Carrito no corrió en el plazo esperado.</summary>
    public const string CarritoTrabajoSinCorrer = "carrito-trabajo-sin-correr";

    /// <summary>Hay checkouts de Carrito con el pago pendiente más tiempo del permitido.</summary>
    public const string CarritoCheckoutsConElPagoPendienteDemasiado = "carrito-checkouts-con-el-pago-pendiente-demasiado";

    /// <summary>La clave de idempotencia de una reserva de Carrito chocó con otra reserva distinta.</summary>
    public const string CarritoClaveDeReservaEnConflicto = "carrito-clave-de-reserva-en-conflicto";

    /// <summary>Carrito intentó liberar la reserva de un pedido que ya estaba confirmado (T26c).</summary>
    public const string CarritoLiberacionDeUnPedidoConfirmado = "carrito-liberacion-de-un-pedido-confirmado";

    public static readonly IReadOnlyList<string> Todos =
    [
        AccesosDenegados,
        AuditoriaNoDisponible,
        IngresosFallidos,
        CredencialDeServicioAnteriorEnUso,
        DependenciaNoNecesariaFallida,
        TrabajoDeFondoDetenido,
        MemoriaDeContadoresAlta,
        VerificadorDeCredencialesSinClaves,
        RevocacionDeSesionesPendiente,
        AcumuladoDeLimitesNoDisponible,
        ConteoDeIngresosNoDisponible,
        MotivoNoCatalogado,
        CorreoDeAlertasNoDisponible,
        DepuracionDiariaSinExito,
        BajaDeTitularSinEntregar,
        TrasladoVinculadoSinAprobar,
        RetomaDePagosFallida,
        ReservasConfirmadasInconsistentes,
        ReservaConfirmadaInexistente,
        ReservaConfirmadaSinLiberar,
        ReintentoDeReembolsosFallido,
        RegistroDelCobroFallido,
        ReembolsoPedidoSinRegistrar,
        PurgaDeContactoFallida,
        ConsultaDePagoFallida,
        ConfirmacionDeCompraFallida,
        CompraAprobadaSinReferencia,
        CompraAprobadaSinCompletar,
        CompensacionNoAplicable,
        CompensacionDeCompraFallida,
        CobroDudosoParaConciliar,
        CierreDeIntencionesFallido,
        TrabajoDePagosSinCorrer,
        CargoDuplicadoParaConciliar,
        AvisoDePagoFallido,
        AvisoACarritoFallido,
        CompraAprobadaSinPedidoALos30Dias,
        AnonimizacionDeHojasSinCorrer,
        LecturaDeAlertasDelInventarioNoDisponible,
        AlcanceDeDatosSinActor,
        ConfirmacionesDeReservasAnomalas,
        LiberacionesDeReservasAnomalas,
        BajasDeTitularAnomalas,
        BloqueosPorTextosPendientes,
        CuotaDeCorreoAlta,
        CupoDeCodigosAlto,
        RenovacionReutilizada,
        EnvioAvisoRechazado,
        EnvioGuiaRepetida,
        EnvioProgresoSinInformar,
        EnvioGuiaSinUso,
        CarritoTrabajoSinCorrer,
        CarritoCheckoutsConElPagoPendienteDemasiado,
        CarritoClaveDeReservaEnConflicto,
        CarritoLiberacionDeUnPedidoConfirmado,
    ];
}
