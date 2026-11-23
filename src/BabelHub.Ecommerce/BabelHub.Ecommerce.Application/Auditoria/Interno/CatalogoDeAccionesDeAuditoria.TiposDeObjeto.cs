using BabelHub.Ecommerce.Domain.Auditoria;

namespace BabelHub.Ecommerce.Application.Auditoria.Interno;

public static partial class CatalogoDeAccionesDeAuditoria
{
    /// <summary>Los tipos de objeto que designan a una persona: una acción que los admite debe declarar su objeto como personal (AUD-19). Una prueba lo comprueba.</summary>
    public static readonly IReadOnlyList<string> TiposDeObjetoPersonales = ["cuenta", "usuario", "cliente", "titular", "numero"];

    /// <summary>
    /// Los tipos de objeto que admite cada acción (lista cerrada; vacía si no lleva objeto). Así un módulo no puede registrar, por ejemplo, el
    /// identificador de un cliente como objeto de una acción cuyo objeto no es personal y que la anonimización no lo alcance (AUD-19). Una acción nueva
    /// sin entrada aquí no compila el catálogo.
    /// </summary>
    private static readonly Dictionary<string, string[]> ObjetosPorAccion = new(StringComparer.Ordinal)
    {
        // Operaciones (T11).
        [nameof(AccionesAuditables.PrecioDeEdicionCambiado)] = ["edicion"],
        [nameof(AccionesAuditables.PedidoAnulado)] = ["pedido"],
        [nameof(AccionesAuditables.AjusteRegistrado)] = ["ajuste"],
        [nameof(AccionesAuditables.IngresoDeMercaderiaRegistrado)] = ["ingreso"],
        [nameof(AccionesAuditables.IngresoPendienteConciliado)] = ["ingreso"],
        [nameof(AccionesAuditables.IngresosSolicitadosAnonimizados)] = [],
        [nameof(AccionesAuditables.CargaInicialEjecutada)] = [],
        [nameof(AccionesAuditables.IntencionesDePagoDesvinculadas)] = [],
        [nameof(AccionesAuditables.ReembolsoResuelto)] = ["pedido"],
        [nameof(AccionesAuditables.PasoDeCompensacionHecho)] = ["pedido"],
        [nameof(AccionesAuditables.ContactoDeEntregaLiberado)] = [],
        [nameof(AccionesAuditables.FusionDeCarritoAplicada)] = [],
        [nameof(AccionesAuditables.EdicionesRetiradasDeCarritos)] = [],
        [nameof(AccionesAuditables.CheckoutTerminado)] = [],
        [nameof(AccionesAuditables.CarritoEliminadoPorIdentidad)] = [],
        [nameof(AccionesAuditables.PurgaDeCarritosRealizada)] = [],
        [nameof(AccionesAuditables.PasoAlPagoAbortado)] = [],
        [nameof(AccionesAuditables.PasoAlPagoIniciado)] = [],
        [nameof(AccionesAuditables.CobroIniciado)] = ["intento-de-cobro"],
        [nameof(AccionesAuditables.CobroResuelto)] = ["intento-de-cobro"],
        [nameof(AccionesAuditables.AvisoDeLaPasarelaRecibido)] = ["intento-de-cobro"],
        [nameof(AccionesAuditables.ReingresoPorDevolucion)] = ["ajuste", "devolucion"],
        [nameof(AccionesAuditables.ReembolsoRegistrado)] = ["pedido"],
        [nameof(AccionesAuditables.EstadoDePedidoCambiado)] = ["pedido"],
        [nameof(AccionesAuditables.ContactoDePedidoConsultado)] = ["pedido"],
        [nameof(AccionesAuditables.OperacionDePedidoIniciada)] = ["pedido"],
        [nameof(AccionesAuditables.PedidoCanceladoPorElCliente)] = ["pedido"],
        [nameof(AccionesAuditables.ReembolsoDePedidoReintentado)] = ["pedido"],
        [nameof(AccionesAuditables.LineaDePedidoEntregada)] = ["pedido"],
        [nameof(AccionesAuditables.AnulacionDePedidoRechazada)] = ["pedido"],
        [nameof(AccionesAuditables.LineaDePedidoMarcadaNoRecogida)] = ["pedido"],
        [nameof(AccionesAuditables.ContactoDePedidoAnonimizado)] = [],
        [nameof(AccionesAuditables.PedidosDepurados)] = [],
        [nameof(AccionesAuditables.DevolucionesDepuradas)] = [],
        [nameof(AccionesAuditables.DevolucionesDeCuentaRedactadas)] = [],
        [nameof(AccionesAuditables.EstadoDeEnvioCambiado)] = ["envio", "pedido"],
        [nameof(AccionesAuditables.DevolucionSolicitada)] = ["devolucion"],
        [nameof(AccionesAuditables.EstadoDeDevolucionCambiado)] = ["devolucion"],
        [nameof(AccionesAuditables.TrasladoSolicitado)] = ["traslado"],
        [nameof(AccionesAuditables.TrasladoAprobado)] = ["traslado"],
        [nameof(AccionesAuditables.TrasladoRechazado)] = ["traslado"],
        [nameof(AccionesAuditables.TrasladoCancelado)] = ["traslado"],
        [nameof(AccionesAuditables.TrasladoDespachado)] = ["traslado"],
        [nameof(AccionesAuditables.TrasladoRecibido)] = ["traslado"],
        [nameof(AccionesAuditables.GuiaDeTrasladoRegistrada)] = ["traslado"],

        // Movimientos de stock, catálogo, fuentes y rutas (T12).
        [nameof(AccionesAuditables.ReservaConfirmada)] = ["pedido"],
        [nameof(AccionesAuditables.ReservaLiberada)] = ["pedido"],
        [nameof(AccionesAuditables.ReservaExpirada)] = ["pedido"],
        [nameof(AccionesAuditables.StockDescontadoPorEntrega)] = ["pedido"],
        [nameof(AccionesAuditables.StockDescontadoPorReparto)] = ["pedido"],
        [nameof(AccionesAuditables.BajaDeTitularProcesada)] = ["titular"],
        [nameof(AccionesAuditables.AlertaMarcadaRevisada)] = ["alerta"],
        [nameof(AccionesAuditables.DepuracionDiariaEjecutada)] = [],
        [nameof(AccionesAuditables.ObraRegistrada)] = ["obra"],
        [nameof(AccionesAuditables.ObraEditada)] = ["obra"],
        [nameof(AccionesAuditables.ObraDadaDeBaja)] = ["obra"],
        [nameof(AccionesAuditables.EdicionRegistrada)] = ["edicion"],
        [nameof(AccionesAuditables.EdicionEditada)] = ["edicion"],
        [nameof(AccionesAuditables.EdicionDadaDeBaja)] = ["edicion"],
        [nameof(AccionesAuditables.EdicionDigitalRetirada)] = ["edicion"],
        [nameof(AccionesAuditables.LicenciaCambiada)] = ["edicion"],
        [nameof(AccionesAuditables.ArchivoDeEdicionReemplazado)] = ["edicion"],
        [nameof(AccionesAuditables.PortadaDeEdicionReemplazada)] = ["edicion", "obra"],
        [nameof(AccionesAuditables.FuenteRegistrada)] = ["fuente"],
        [nameof(AccionesAuditables.FuenteEditada)] = ["fuente"],
        [nameof(AccionesAuditables.RecepcionDesignada)] = ["fuente"],
        [nameof(AccionesAuditables.RutaRegistrada)] = ["ruta"],
        [nameof(AccionesAuditables.RutaModificada)] = ["ruta"],

        // Identidad, biblioteca, reclamaciones, notificaciones y claves (T13).
        [nameof(AccionesAuditables.UsuarioDadoDeAlta)] = ["usuario", "cuenta"],
        [nameof(AccionesAuditables.UsuarioDesactivado)] = ["usuario", "cuenta"],
        [nameof(AccionesAuditables.UsuarioReactivado)] = ["usuario", "cuenta"],
        [nameof(AccionesAuditables.UsuarioActualizado)] = ["usuario"],
        [nameof(AccionesAuditables.CambioDeRol)] = ["usuario"],
        [nameof(AccionesAuditables.ContrasenaRestablecidaPorAdministracion)] = ["usuario"],
        [nameof(AccionesAuditables.SegundoFactorRestablecidoPorAdministracion)] = ["usuario"],
        [nameof(AccionesAuditables.CuentaCancelada)] = ["cuenta"],
        [nameof(AccionesAuditables.CuentaReemplazada)] = ["cuenta"],
        [nameof(AccionesAuditables.CancelacionDiferidaDescartada)] = ["cuenta"],
        [nameof(AccionesAuditables.SolicitudDeDatosAtendida)] = ["solicitud-de-datos"],
        [nameof(AccionesAuditables.SolicitudDeDatosConsultada)] = ["solicitud-de-datos"],
        [nameof(AccionesAuditables.ClaveDeFirmaRotada)] = ["clave-de-firma"],
        [nameof(AccionesAuditables.ClaveDeFirmaDeclaradaComprometida)] = ["clave-de-firma"],
        [nameof(AccionesAuditables.DescargasRestituidas)] = ["acceso-digital"],
        [nameof(AccionesAuditables.DescargasDeBibliotecaDepuradas)] = [],
        [nameof(AccionesAuditables.AyudasDeBibliotecaDepuradas)] = [],
        [nameof(AccionesAuditables.AccesoDigitalRevocado)] = ["acceso-digital"],
        [nameof(AccionesAuditables.ReporteDeArchivoResuelto)] = ["reporte-de-archivo"],
        [nameof(AccionesAuditables.BibliotecaDeClienteConsultada)] = ["cliente"],
        [nameof(AccionesAuditables.HojaDeReclamacionRespondida)] = ["hoja-de-reclamacion"],
        [nameof(AccionesAuditables.HojaDeReclamacionDescartada)] = ["hoja-de-reclamacion"],
        [nameof(AccionesAuditables.HojaDeReclamacionAnonimizada)] = [],
        [nameof(AccionesAuditables.HojaDeReclamacionVinculada)] = ["usuario"],
        [nameof(AccionesAuditables.HojaDeReclamacionConsultada)] = ["hoja-de-reclamacion"],
        [nameof(AccionesAuditables.AvisosDeNumeroDesactivados)] = ["numero"],

        // Eventos de seguridad (T15, T16): los de la plataforma no llevan objeto; los de Identidad, la cuenta (solo si existe).
        [nameof(AccionesAuditables.AccesoDenegado)] = [],
        [nameof(AccionesAuditables.LimiteExcedido)] = [],
        [nameof(AccionesAuditables.PresupuestoDelPersonalExcedido)] = [],
        [nameof(AccionesAuditables.LlamadaEntreServiciosSinCredencial)] = [],
        [nameof(AccionesAuditables.OrigenIndeterminable)] = [],
        [nameof(AccionesAuditables.VerificacionDePersonaFallida)] = [],
        [nameof(AccionesAuditables.EntradaDeIntegracionRechazada)] = [],
        [nameof(AccionesAuditables.OrigenReenviadoSinCredencial)] = [],
        [nameof(AccionesAuditables.AccesoBloqueadoPorTextosPendientes)] = ["usuario"],
        [nameof(AccionesAuditables.IngresoFallido)] = ["cuenta"],
        [nameof(AccionesAuditables.BloqueoTemporalAplicado)] = ["cuenta"],
        [nameof(AccionesAuditables.RecuperacionDeAccesoSolicitada)] = ["cuenta"],
        [nameof(AccionesAuditables.RecuperacionDeAccesoCompletada)] = ["cuenta"],
        [nameof(AccionesAuditables.SegundoFactorFallido)] = ["cuenta"],
        [nameof(AccionesAuditables.CodigoDeUnSoloUsoRechazado)] = ["cuenta"],
        [nameof(AccionesAuditables.RenovacionReutilizada)] = ["cuenta"],
        [nameof(AccionesAuditables.CodigoDeRespaldoUsado)] = ["cuenta"],
        [nameof(AccionesAuditables.ContrasenaCambiada)] = ["cuenta"],
        [nameof(AccionesAuditables.CorreoCambiado)] = ["cuenta"],
        [nameof(AccionesAuditables.SegundoFactorActivado)] = ["cuenta"],
        [nameof(AccionesAuditables.SegundoFactorDesactivado)] = ["cuenta"],
        [nameof(AccionesAuditables.SegundoFactorRegenerado)] = ["cuenta"],
        [nameof(AccionesAuditables.CambioDeCorreoNoReconocidoConfirmado)] = ["cuenta"],
        [nameof(AccionesAuditables.CambioDeCorreoNoReconocidoRechazado)] = ["cuenta"],
        [nameof(AccionesAuditables.CambioDeCorreoNoReconocidoVencido)] = ["cuenta"],
        [nameof(AccionesAuditables.AvisoDeSeguridadSuprimido)] = ["cuenta"],
    };

    private static IEnumerable<AccionDelCatalogo> ConTiposDeObjeto(IEnumerable<AccionDelCatalogo> acciones) =>
        acciones.Select(a => ObjetosPorAccion.TryGetValue(a.Identificador, out var tipos)
            ? a with { TiposDeObjeto = tipos }
            : throw new InvalidOperationException($"La acción {a.Identificador} no declara sus tipos de objeto."));
}
