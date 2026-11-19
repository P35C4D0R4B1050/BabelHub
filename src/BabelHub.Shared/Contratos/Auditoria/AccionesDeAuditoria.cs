namespace BabelHub.Shared.Contratos.Auditoria;

/// <summary>
/// Los identificadores de las acciones del catálogo de auditoría tal como viajan en el contrato (<c>accion</c> de un registro y el filtro <c>?accion=</c>, AUD-07). Son los mismos en los dos servicios
/// y una prueba de contrato de cada uno (T21) los compara con el catálogo de su módulo <c>Auditoria</c>: una acción nueva sin su constante, o una constante sin acción, no llega a producción. Quien
/// consume el contrato (la interfaz web) arma con esta lista el filtro de acciones; el texto que se muestra por cada una lo pone la interfaz.
/// </summary>
public static class AccionesDeAuditoria
{
    public const string AccesoBloqueadoPorTextosPendientes = "AccesoBloqueadoPorTextosPendientes";

    public const string AccesoDenegado = "AccesoDenegado";

    public const string AccesoDigitalRevocado = "AccesoDigitalRevocado";

    public const string AjusteRegistrado = "AjusteRegistrado";

    public const string AlertaMarcadaRevisada = "AlertaMarcadaRevisada";

    public const string ArchivoDeEdicionReemplazado = "ArchivoDeEdicionReemplazado";

    public const string AvisoDeSeguridadSuprimido = "AvisoDeSeguridadSuprimido";

    public const string AvisosDeNumeroDesactivados = "AvisosDeNumeroDesactivados";

    public const string BajaDeTitularProcesada = "BajaDeTitularProcesada";

    public const string BibliotecaDeClienteConsultada = "BibliotecaDeClienteConsultada";

    public const string BloqueoTemporalAplicado = "BloqueoTemporalAplicado";

    public const string CambioDeCorreoNoReconocidoConfirmado = "CambioDeCorreoNoReconocidoConfirmado";

    public const string CambioDeCorreoNoReconocidoRechazado = "CambioDeCorreoNoReconocidoRechazado";

    public const string CambioDeCorreoNoReconocidoVencido = "CambioDeCorreoNoReconocidoVencido";

    public const string CambioDeRol = "CambioDeRol";

    public const string CancelacionDiferidaDescartada = "CancelacionDiferidaDescartada";

    public const string ClaveDeFirmaDeclaradaComprometida = "ClaveDeFirmaDeclaradaComprometida";

    public const string ClaveDeFirmaRotada = "ClaveDeFirmaRotada";

    public const string CodigoDeRespaldoUsado = "CodigoDeRespaldoUsado";

    public const string CodigoDeUnSoloUsoRechazado = "CodigoDeUnSoloUsoRechazado";

    public const string ContrasenaCambiada = "ContrasenaCambiada";

    public const string ContrasenaRestablecidaPorAdministracion = "ContrasenaRestablecidaPorAdministracion";

    public const string CorreoCambiado = "CorreoCambiado";

    public const string CuentaCancelada = "CuentaCancelada";

    public const string CuentaReemplazada = "CuentaReemplazada";

    public const string DepuracionDiariaEjecutada = "DepuracionDiariaEjecutada";

    public const string DescargasRestituidas = "DescargasRestituidas";

    public const string DevolucionSolicitada = "DevolucionSolicitada";

    public const string EdicionDadaDeBaja = "EdicionDadaDeBaja";

    public const string EdicionDigitalRetirada = "EdicionDigitalRetirada";

    public const string EdicionEditada = "EdicionEditada";

    public const string EdicionRegistrada = "EdicionRegistrada";

    public const string EntradaDeIntegracionRechazada = "EntradaDeIntegracionRechazada";

    public const string EstadoDeDevolucionCambiado = "EstadoDeDevolucionCambiado";

    public const string EstadoDeEnvioCambiado = "EstadoDeEnvioCambiado";

    public const string EstadoDePedidoCambiado = "EstadoDePedidoCambiado";

    public const string ContactoDePedidoAnonimizado = "ContactoDePedidoAnonimizado";

    public const string ContactoDePedidoConsultado = "ContactoDePedidoConsultado";

    public const string LineaDePedidoEntregada = "LineaDePedidoEntregada";

    public const string OperacionDePedidoIniciada = "OperacionDePedidoIniciada";

    public const string PedidoCanceladoPorElCliente = "PedidoCanceladoPorElCliente";

    public const string PedidosDepurados = "PedidosDepurados";

    public const string AnulacionDePedidoRechazada = "AnulacionDePedidoRechazada";

    public const string LineaDePedidoMarcadaNoRecogida = "LineaDePedidoMarcadaNoRecogida";

    public const string ReembolsoDePedidoReintentado = "ReembolsoDePedidoReintentado";

    public const string FuenteEditada = "FuenteEditada";

    public const string FuenteRegistrada = "FuenteRegistrada";

    public const string GuiaDeTrasladoRegistrada = "GuiaDeTrasladoRegistrada";

    public const string HojaDeReclamacionAnonimizada = "HojaDeReclamacionAnonimizada";

    public const string HojaDeReclamacionConsultada = "HojaDeReclamacionConsultada";

    public const string HojaDeReclamacionDescartada = "HojaDeReclamacionDescartada";

    public const string HojaDeReclamacionRespondida = "HojaDeReclamacionRespondida";

    public const string CobroIniciado = "CobroIniciado";

    public const string CobroResuelto = "CobroResuelto";

    /// <summary>El aviso firmado de la pasarela adelantó la consulta de un intento sin resolver (PAG-19): sin el cuerpo del aviso ni datos de pago.</summary>
    public const string AvisoDeLaPasarelaRecibido = "AvisoDeLaPasarelaRecibido";

    public const string IngresoDeMercaderiaRegistrado = "IngresoDeMercaderiaRegistrado";

    public const string IngresoPendienteConciliado = "IngresoPendienteConciliado";

    public const string IngresosSolicitadosAnonimizados = "IngresosSolicitadosAnonimizados";

    /// <summary>El trabajo de fondo desvinculó las intenciones de pago cerradas hace más de 2 años (cantidad, sin datos personales, PAG-26).</summary>
    public const string IntencionesDePagoDesvinculadas = "IntencionesDePagoDesvinculadas";

    /// <summary>Un reembolso de Pagos se concluyó o quedó pendiente (con su motivo de lista cerrada); el actor es el administrador central o el sistema. Sin datos de pago.</summary>
    public const string ReembolsoResuelto = "ReembolsoResuelto";

    /// <summary>Un paso de la compensación de un cobro se hizo (abrir, liberar reservas, registrar el reembolso o cerrar el checkout), con su motivo de lista cerrada.</summary>
    public const string PasoDeCompensacionHecho = "PasoDeCompensacionHecho";

    /// <summary>Pagos eliminó el contacto de entrega de una o varias intenciones (cantidad, sin datos personales, PAG-27).</summary>
    public const string ContactoDeEntregaLiberado = "ContactoDeEntregaLiberado";

    public const string FusionDeCarritoAplicada = "FusionDeCarritoAplicada";

    public const string EdicionesRetiradasDeCarritos = "EdicionesRetiradasDeCarritos";

    public const string CheckoutTerminado = "CheckoutTerminado";

    public const string CarritoEliminadoPorIdentidad = "CarritoEliminadoPorIdentidad";

    public const string PurgaDeCarritosRealizada = "PurgaDeCarritosRealizada";

    public const string PasoAlPagoAbortado = "PasoAlPagoAbortado";

    public const string PasoAlPagoIniciado = "PasoAlPagoIniciado";

    public const string IngresoFallido = "IngresoFallido";

    public const string LicenciaCambiada = "LicenciaCambiada";

    public const string LimiteExcedido = "LimiteExcedido";

    public const string LlamadaEntreServiciosSinCredencial = "LlamadaEntreServiciosSinCredencial";

    public const string ObraDadaDeBaja = "ObraDadaDeBaja";

    public const string ObraEditada = "ObraEditada";

    public const string ObraRegistrada = "ObraRegistrada";

    public const string OrigenIndeterminable = "OrigenIndeterminable";

    public const string OrigenReenviadoSinCredencial = "OrigenReenviadoSinCredencial";

    public const string PedidoAnulado = "PedidoAnulado";

    public const string PortadaDeEdicionReemplazada = "PortadaDeEdicionReemplazada";

    public const string PrecioDeEdicionCambiado = "PrecioDeEdicionCambiado";

    public const string PresupuestoDelPersonalExcedido = "PresupuestoDelPersonalExcedido";

    public const string RecepcionDesignada = "RecepcionDesignada";

    public const string RecuperacionDeAccesoCompletada = "RecuperacionDeAccesoCompletada";

    public const string RecuperacionDeAccesoSolicitada = "RecuperacionDeAccesoSolicitada";

    public const string ReembolsoRegistrado = "ReembolsoRegistrado";

    public const string ReingresoPorDevolucion = "ReingresoPorDevolucion";

    public const string RenovacionReutilizada = "RenovacionReutilizada";

    public const string ReporteDeArchivoResuelto = "ReporteDeArchivoResuelto";

    public const string ReservaConfirmada = "ReservaConfirmada";

    public const string ReservaExpirada = "ReservaExpirada";

    public const string ReservaLiberada = "ReservaLiberada";

    public const string RutaModificada = "RutaModificada";

    public const string RutaRegistrada = "RutaRegistrada";

    public const string SegundoFactorActivado = "SegundoFactorActivado";

    public const string SegundoFactorDesactivado = "SegundoFactorDesactivado";

    public const string SegundoFactorFallido = "SegundoFactorFallido";

    public const string SegundoFactorRegenerado = "SegundoFactorRegenerado";

    public const string SegundoFactorRestablecidoPorAdministracion = "SegundoFactorRestablecidoPorAdministracion";

    public const string SolicitudDeDatosAtendida = "SolicitudDeDatosAtendida";

    public const string SolicitudDeDatosConsultada = "SolicitudDeDatosConsultada";

    public const string StockDescontadoPorEntrega = "StockDescontadoPorEntrega";

    public const string StockDescontadoPorReparto = "StockDescontadoPorReparto";

    public const string TrasladoAprobado = "TrasladoAprobado";

    public const string TrasladoCancelado = "TrasladoCancelado";

    public const string TrasladoDespachado = "TrasladoDespachado";

    public const string TrasladoRechazado = "TrasladoRechazado";

    public const string TrasladoRecibido = "TrasladoRecibido";

    public const string TrasladoSolicitado = "TrasladoSolicitado";

    public const string UsuarioDadoDeAlta = "UsuarioDadoDeAlta";

    public const string UsuarioDesactivado = "UsuarioDesactivado";

    public const string UsuarioReactivado = "UsuarioReactivado";

    public const string VerificacionDePersonaFallida = "VerificacionDePersonaFallida";

    public static readonly IReadOnlyList<string> Todas =
    [
        AccesoDenegado,
        AccesoDigitalRevocado,
        AjusteRegistrado,
        AlertaMarcadaRevisada,
        ArchivoDeEdicionReemplazado,
        AvisoDeSeguridadSuprimido,
        AvisosDeNumeroDesactivados,
        BajaDeTitularProcesada,
        BibliotecaDeClienteConsultada,
        BloqueoTemporalAplicado,
        CambioDeCorreoNoReconocidoConfirmado,
        CambioDeCorreoNoReconocidoRechazado,
        CambioDeCorreoNoReconocidoVencido,
        CambioDeRol,
        CancelacionDiferidaDescartada,
        ClaveDeFirmaDeclaradaComprometida,
        ClaveDeFirmaRotada,
        CodigoDeRespaldoUsado,
        ContrasenaCambiada,
        ContrasenaRestablecidaPorAdministracion,
        CorreoCambiado,
        CobroIniciado,
        CobroResuelto,
        AvisoDeLaPasarelaRecibido,
        CuentaCancelada,
        CuentaReemplazada,
        DepuracionDiariaEjecutada,
        DescargasRestituidas,
        DevolucionSolicitada,
        EdicionDadaDeBaja,
        EdicionDigitalRetirada,
        EdicionEditada,
        EdicionRegistrada,
        EntradaDeIntegracionRechazada,
        EstadoDeDevolucionCambiado,
        EstadoDeEnvioCambiado,
        EstadoDePedidoCambiado,
        ContactoDePedidoAnonimizado,
        ContactoDePedidoConsultado,
        LineaDePedidoEntregada,
        OperacionDePedidoIniciada,
        PedidoCanceladoPorElCliente,
        PedidosDepurados,
        AnulacionDePedidoRechazada,
        LineaDePedidoMarcadaNoRecogida,
        ReembolsoDePedidoReintentado,
        FuenteEditada,
        FuenteRegistrada,
        GuiaDeTrasladoRegistrada,
        HojaDeReclamacionAnonimizada,
        HojaDeReclamacionConsultada,
        HojaDeReclamacionDescartada,
        HojaDeReclamacionRespondida,
        IngresoDeMercaderiaRegistrado,
        IngresoFallido,
        IngresoPendienteConciliado,
        IngresosSolicitadosAnonimizados,
        IntencionesDePagoDesvinculadas,
        ReembolsoResuelto,
        PasoDeCompensacionHecho,
        ContactoDeEntregaLiberado,
        FusionDeCarritoAplicada,
        EdicionesRetiradasDeCarritos,
        CheckoutTerminado,
        CarritoEliminadoPorIdentidad,
        PurgaDeCarritosRealizada,
        PasoAlPagoAbortado,
        PasoAlPagoIniciado,
        LicenciaCambiada,
        LimiteExcedido,
        LlamadaEntreServiciosSinCredencial,
        ObraDadaDeBaja,
        ObraEditada,
        ObraRegistrada,
        OrigenIndeterminable,
        OrigenReenviadoSinCredencial,
        AccesoBloqueadoPorTextosPendientes,
        CodigoDeUnSoloUsoRechazado,
        RenovacionReutilizada,
        PedidoAnulado,
        PortadaDeEdicionReemplazada,
        PrecioDeEdicionCambiado,
        PresupuestoDelPersonalExcedido,
        RecepcionDesignada,
        RecuperacionDeAccesoCompletada,
        RecuperacionDeAccesoSolicitada,
        ReembolsoRegistrado,
        ReingresoPorDevolucion,
        ReporteDeArchivoResuelto,
        ReservaConfirmada,
        ReservaExpirada,
        ReservaLiberada,
        RutaModificada,
        RutaRegistrada,
        SegundoFactorActivado,
        SegundoFactorDesactivado,
        SegundoFactorFallido,
        SegundoFactorRegenerado,
        SegundoFactorRestablecidoPorAdministracion,
        SolicitudDeDatosAtendida,
        SolicitudDeDatosConsultada,
        StockDescontadoPorEntrega,
        StockDescontadoPorReparto,
        TrasladoAprobado,
        TrasladoCancelado,
        TrasladoDespachado,
        TrasladoRechazado,
        TrasladoRecibido,
        TrasladoSolicitado,
        UsuarioDadoDeAlta,
        UsuarioDesactivado,
        UsuarioReactivado,
        VerificacionDePersonaFallida,
    ];
}
