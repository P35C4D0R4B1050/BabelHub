using System.Globalization;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Infrastructure.Carrito;

// T26c: los adaptadores reales de lo que el carril de plataforma entregó (límites, auditoría, marca de cancelación, consentimiento de WhatsApp y alertas). Cada uno traduce al puerto delgado de Carrito sin cambiar su contrato; las
// reglas siguen en la Aplicación. Ninguno lleva datos personales: la auditoría y las alertas solo reciben conteos y motivos cerrados.

/// <summary>
/// Los límites de Carrito sobre <see cref="IControlDeLimites"/> y sus políticas de <c>Plataforma</c> (plan §6, SEG-05, SEG-07, SEG-26): crear carritos de visitante, editar líneas (por cuenta o, sin ella, por origen), revisar el checkout, pasar al pago
/// y volver o reiniciar. <b>Pasar al pago falla cerrado</b>: sin respuesta del almacén de contadores (o una excepción) no se reserva stock. En lo demás una excepción no bloquea (la política tiene su respaldo local). La clave de repetición del pedido en curso
/// hace que un doble clic no consuma cupo dos veces.
/// </summary>
public sealed class LimitadorDelCarritoSobreLimites(IControlDeLimites limites) : ILimitadorDelCarrito
{
    public Task<ResultadoDeLimiteDelCarrito> CrearCarritoDeVisitante(CancellationToken cancelacion = default) =>
        Reservar(PoliticasDeLimite.CarritosDeVisitantePorOrigen, null, null, cerrado: false, cancelacion);

    public Task<ResultadoDeLimiteDelCarrito> EditarLineas(Guid? cuentaId, CancellationToken cancelacion = default) =>
        cuentaId is { } cuenta
            ? Reservar(PoliticasDeLimite.EdicionDeLineasPorCuenta, Sujeto(cuenta), null, cerrado: false, cancelacion)
            : Reservar(PoliticasDeLimite.EdicionDeLineasPorOrigen, null, null, cerrado: false, cancelacion);

    public Task<ResultadoDeLimiteDelCarrito> RevisarCheckout(Guid cuentaId, CancellationToken cancelacion = default) =>
        Reservar(PoliticasDeLimite.RevisionDelCheckoutPorCuenta, Sujeto(cuentaId), null, cerrado: false, cancelacion);

    public Task<ResultadoDeLimiteDelCarrito> PasarAlPago(Guid cuentaId, string claveDeRepeticion, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claveDeRepeticion);
        return Reservar(PoliticasDeLimite.PasosAlPagoPorCuenta, Sujeto(cuentaId), claveDeRepeticion, cerrado: true, cancelacion);
    }

    public Task<ResultadoDeLimiteDelCarrito> VolverOReiniciar(Guid cuentaId, CancellationToken cancelacion = default) =>
        Reservar(PoliticasDeLimite.VolverOReiniciarPorCuenta, Sujeto(cuentaId), null, cerrado: false, cancelacion);

    private static string Sujeto(Guid cuentaId) => cuentaId.ToString("N");

    private async Task<ResultadoDeLimiteDelCarrito> Reservar(ClaveDePolitica politica, string? sujeto, string? claveDelLlamador, bool cerrado, CancellationToken cancelacion)
    {
        ResultadoDeLimite resultado;
        try
        {
            resultado = await limites.Reservar(politica, sujeto, claveDelLlamador, cancelacion: cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return cerrado ? ResultadoDeLimiteDelCarrito.NoDisponible : ResultadoDeLimiteDelCarrito.Permitido;
        }

        return resultado.Estado switch
        {
            EstadoDeLimite.Excedido => new ResultadoDeLimiteDelCarrito(DecisionDeLimiteDelCarrito.Excedido, resultado.DisponibleDesde),
            EstadoDeLimite.NoDisponible => ResultadoDeLimiteDelCarrito.NoDisponible,
            // Una repetición ya completada o en curso con la misma clave no vuelve a contar y no es un exceso: el paso sigue su curso (la Aplicación lo vuelve a decidir con el estado del checkout).
            _ => ResultadoDeLimiteDelCarrito.Permitido,
        };
    }
}

/// <summary>
/// La auditoría de Carrito sobre <see cref="IAuditoria"/> (plan §6): cada operación se registra <b>en la misma transacción</b> del caso de uso y, si no se puede, <see cref="AuditoriaNoDisponibleException"/> sube y la operación no se aplica. Solo
/// conteos y motivos cerrados: ningún dato personal, la cookie del visitante ni identificadores de cuenta o pedido.
/// </summary>
public sealed class AuditoriaDelCarritoSobreAuditoria(IAuditoria auditoria) : IAuditoriaDelCarrito
{
    public Task FusionAplicada(Guid cuentaId, int lineasDelVisitante, IReadOnlyList<Application.Carrito.MotivoDeAjusteDeAviso> ajustes, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(ajustes);
        return Registrar(AccionesAuditables.FusionDeCarritoAplicada, cancelacion,
            ("lineasDelVisitante", Numero(lineasDelVisitante)),
            ("ajustes", string.Join(',', ajustes.Select(a => a.ToString()))));
    }

    public Task EdicionesRetiradas(int ediciones, int carritosAfectados, int lineasQuitadas, int lineasAnotadas, CancellationToken cancelacion = default) =>
        Registrar(AccionesAuditables.EdicionesRetiradasDeCarritos, cancelacion,
            ("ediciones", Numero(ediciones)),
            ("carritosAfectados", Numero(carritosAfectados)),
            ("lineasQuitadas", Numero(lineasQuitadas)),
            ("lineasAnotadas", Numero(lineasAnotadas)));

    public Task CheckoutTerminado(string motivo, CancellationToken cancelacion = default) =>
        Registrar(AccionesAuditables.CheckoutTerminado, cancelacion, ("motivo", motivo));

    public Task CarritoEliminadoPorIdentidad(string motivo, CancellationToken cancelacion = default) =>
        Registrar(AccionesAuditables.CarritoEliminadoPorIdentidad, cancelacion, ("motivo", motivo));

    public Task PurgaRealizada(string trabajo, int cantidad, CancellationToken cancelacion = default) =>
        Registrar(AccionesAuditables.PurgaDeCarritosRealizada, cancelacion, ("trabajo", trabajo), ("cantidad", Numero(cantidad)));

    public Task PasoAlPagoAbortado(string motivo, CancellationToken cancelacion = default) =>
        Registrar(AccionesAuditables.PasoAlPagoAbortado, cancelacion, ("motivo", motivo));

    public Task PasoAlPagoIniciado(bool conImpresos, bool aDomicilio, CancellationToken cancelacion = default) =>
        Registrar(AccionesAuditables.PasoAlPagoIniciado, cancelacion, ("conImpresos", Booleano(conImpresos)), ("aDomicilio", Booleano(aDomicilio)));

    public Task TarifaDeEnvioCambiada(string codigoDeSucursal, decimal anterior, decimal nuevo, CancellationToken cancelacion = default) =>
        auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.TarifaDeEnvioCambiada)
            {
                ObjetoTipo = "sucursal",
                ObjetoId = codigoDeSucursal,
                Detalle = new Dictionary<string, string?>(StringComparer.Ordinal) { ["importeAnterior"] = Soles(anterior), ["importeNuevo"] = Soles(nuevo), ["moneda"] = "PEN" },
            },
            cancelacion);

    private static string Soles(decimal valor) => valor.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Numero(int valor) => valor.ToString(CultureInfo.InvariantCulture);

    private static string Booleano(bool valor) => valor ? "true" : "false";

    private Task Registrar(AccionAuditable accion, CancellationToken cancelacion, params (string Clave, string Valor)[] detalle) =>
        auditoria.Registrar(new SolicitudDeRegistro(accion) { Detalle = detalle.ToDictionary(d => d.Clave, d => (string?)d.Valor, StringComparer.Ordinal) }, cancelacion);
}

/// <summary>
/// La marca de cancelación de la cuenta sobre <see cref="IMarcaDeCancelacionDeCuenta"/> de Identidad (IDE-71, CAR-31): se lee <b>con bloqueo</b>, dentro de la transacción del paso al pago, de modo que la cancelación y el pago de la misma cuenta se
/// serializan. Si no se puede leer, la excepción sube y el paso al pago no se hace (nunca se asume libre).
/// </summary>
public sealed class MarcaDeCancelacionSobreIdentidad(IMarcaDeCancelacionDeCuenta marca) : IMarcaDeCancelacion
{
    public async Task<EstadoDeLaMarcaDeCancelacion> LeerConBloqueo(Guid cuentaId, CancellationToken cancelacion = default) =>
        await marca.LeerConBloqueo(cuentaId, cancelacion).ConfigureAwait(false) switch
        {
            EstadoDeLaMarca.Libre => EstadoDeLaMarcaDeCancelacion.Libre,
            EstadoDeLaMarca.Diferida => EstadoDeLaMarcaDeCancelacion.Diferida,
            EstadoDeLaMarca.EnEjecucion => EstadoDeLaMarcaDeCancelacion.EnEjecucion,
            _ => EstadoDeLaMarcaDeCancelacion.Ejecutada,
        };
}

/// <summary>
/// El consentimiento de avisos por WhatsApp sobre <see cref="ICuentaDelCliente"/> de Identidad (CAR-18, IDE-31): la oferta lleva solo la huella enmascarada (el número nunca llega a Carrito) y se guarda si la huella es la del número vigente. <b>Una falla
/// nunca bloquea el pago</b>: no se ofrece, y guardar devuelve <see langword="false"/> para que se informe que no se guardó (nunca se dice «guardado»).
/// </summary>
public sealed class ConsentimientoDeAvisosSobreIdentidad(ICuentaDelCliente cuentas) : IConsentimientoDeAvisos
{
    public async Task<OfertaDeConsentimiento> Consultar(Guid cuentaId, CancellationToken cancelacion = default)
    {
        Resultado<OfertaDeConsentimientoDeAvisos> resultado;
        try
        {
            resultado = await cuentas.ConsultarLaOfertaDeConsentimiento(cuentaId, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return OfertaDeConsentimiento.NoSeOfrece;
        }

        if (!resultado.EsExito)
        {
            return OfertaDeConsentimiento.NoSeOfrece;
        }

        var oferta = resultado.Valor;
        if (oferta.YaConsiente)
        {
            return new OfertaDeConsentimiento(EstadoDeLaOfertaDeConsentimiento.YaConsiente, null);
        }

        return oferta.SeOfrece && !string.IsNullOrWhiteSpace(oferta.HuellaEnmascaradaDelNumero)
            ? new OfertaDeConsentimiento(EstadoDeLaOfertaDeConsentimiento.SeOfrece, oferta.HuellaEnmascaradaDelNumero)
            : OfertaDeConsentimiento.NoSeOfrece;
    }

    public async Task<bool> Guardar(Guid cuentaId, string huellaEnmascaradaDelNumero, CancellationToken cancelacion = default)
    {
        try
        {
            return (await cuentas.GuardarElConsentimientoPorHuella(cuentaId, huellaEnmascaradaDelNumero, cancelacion).ConfigureAwait(false)).EsExito;
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return false;
        }
    }
}

/// <summary>
/// Las alertas técnicas de Carrito sobre <see cref="IPublicadorDeAlertasTecnicas"/> (AUD-31): un motivo cerrado por tipo, sin datos personales ni identificadores (la cantidad no se publica: la alerta agrupa por motivo). Una falla al publicar nunca interrumpe al
/// llamador. Los motivos de Carrito aún no están en el catálogo de alertas de Auditoría: hasta que se agreguen se guardan con el código genérico «motivo no catalogado» (hallazgo para el carril de auditoría).
/// </summary>
public sealed class AlertasDelCarritoSobrePublicador(IPublicadorDeAlertasTecnicas alertas, IReloj reloj) : IAlertasDelCarrito
{
    public async Task Publicar(TipoDeAlertaDelCarrito tipo, int cantidad, CancellationToken cancelacion = default)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(Motivo(tipo), reloj.Ahora), cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            // Publicar una alerta no puede romper el caso de uso que la origina.
        }
    }

    public static string Motivo(TipoDeAlertaDelCarrito tipo) => tipo switch
    {
        TipoDeAlertaDelCarrito.LiberacionDeUnPedidoConfirmado => "carrito-liberacion-de-un-pedido-confirmado",
        TipoDeAlertaDelCarrito.ClaveDeReservaEnConflicto => "carrito-clave-de-reserva-en-conflicto",
        TipoDeAlertaDelCarrito.CheckoutsConElPagoPendienteDemasiado => "carrito-checkouts-con-el-pago-pendiente-demasiado",
        TipoDeAlertaDelCarrito.TrabajoSinCorrer => "carrito-trabajo-sin-correr",
        _ => "carrito-alerta",
    };
}
