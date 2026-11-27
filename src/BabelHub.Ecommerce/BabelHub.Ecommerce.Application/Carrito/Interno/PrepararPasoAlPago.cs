using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;
using TextoLegal = BabelHub.Ecommerce.Domain.Carrito.TextoLegal;
using Tipo = BabelHub.Ecommerce.Domain.Carrito.TipoDeEntrega;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

public enum DesenlaceDePrepararPaso
{
    /// <summary>Se congeló la compra y el checkout pasó a <c>reservando</c> con su <c>pedidoId</c> y su generación: siguen la reserva y la intención.</summary>
    Preparado,

    /// <summary>El checkout ya estaba <c>reservando</c> o <c>en_pago</c> con las mismas huellas (un doble clic, otra pestaña): se continúa con el mismo <c>pedidoId</c>.</summary>
    YaPreparado,

    /// <summary>Había un retiro pendiente: se reabrió la intención, el checkout volvió a <c>en_edicion</c> y se devuelve la revisión con el aviso «tu carrito cambió»; no se paga.</summary>
    RevisionPorRetiro,
}

/// <summary>
/// Lo que el paso 1 de pasar al pago deja para los pasos siguientes (D18): el <c>pedidoId</c> y la generación de reserva con sus dos huellas, lo que se reserva en el Inventario (solo los impresos, con el <c>lineaId</c> estable), la
/// <see cref="InstantaneaDeCompra"/> congelada que recibirá Pagos y la vista del checkout.
/// </summary>
public sealed record PasoAlPagoPreparado(
    DesenlaceDePrepararPaso Desenlace,
    Guid CuentaId,
    Guid? PedidoId,
    int Generacion,
    string? HuellaDeReserva,
    string? HuellaDeInstantanea,
    EstadoDelCheckout EstadoDelCheckout,
    EntregaParaElInventario? Entrega,
    IReadOnlyList<LineaParaElInventario> LineasImpresas,
    InstantaneaDeCompra? Instantanea,
    DateTimeOffset? ReservaExpiraEn,
    VistaDelCheckout Vista);

/// <summary>
/// <c>PrepararPasoAlPago</c>, el paso 1 de pasar al pago (D18; T17; AC-CAR-12, 13, 18, 19, 27; CAR-14, 16, 21, 31, 36, 37): comprobaciones en el orden fijo <b>sesión → estado de la cuenta → aceptaciones y versión de los textos →
/// licencia → posesión → entrega y contacto → total visto</b>. Todo lo que es red (Identidad, textos, Catálogo, posesión, límite) se obtiene <b>antes</b> de abrir la transacción y se revalida al bloquear; dentro solo queda la lectura
/// con bloqueo de la marca de cancelación y la base de datos. Congela las líneas, el envío y el total, calcula las dos huellas (la de instantánea con clave y propósito propio), asigna el <c>pedidoId</c> y la generación de reserva y
/// pasa a <c>reservando</c>. <b>Sin crear reserva ni intención en ninguna falla</b> y, con la misma huella, continúa con el mismo <c>pedidoId</c>. Con un retiro pendiente lo aplica (<c>Reabrir</c> fuera de la transacción) y devuelve la revisión.
/// </summary>
internal sealed class PrepararPasoAlPago(ServiciosDelCheckout servicios) : ICasoDeUso<PasarAlPagoEntrada, PasoAlPagoPreparado>
{
    private const string PropositoDeLaHuella = "instantanea-de-compra";

    public async Task<Resultado<PasoAlPagoPreparado>> Ejecutar(PasarAlPagoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var carrito = servicios.Carrito;

        // 1. Sesión (CAR-16), cuenta activa, límite de revisión, pedido oculto y el checkout vivo.
        var entro = await servicios.EntrarAlCheckout(entrada.PedidoId, cancelacion).ConfigureAwait(false);
        if (!entro.EsExito)
        {
            return Resultado<PasoAlPagoPreparado>.Fallo(entro.Error!);
        }

        var (cuenta, vivo, delCarrito) = entro.Valor;
        if (ValidarForma(entrada) is { } invalida)
        {
            return Resultado<PasoAlPagoPreparado>.Fallo(invalida);
        }

        if (vivo.Estado == EstadoDelCheckout.PagoPendiente)
        {
            return Resultado<PasoAlPagoPreparado>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.PagoPendiente));
        }

        if (delCarrito is null)
        {
            return Resultado<PasoAlPagoPreparado>.Fallo(ServiciosDelCheckout.CheckoutVacio());
        }

        // Una compra anterior a la venta por sucursal no se paga; reservando o en pago, además se aborta y libera su reserva (CAR-45).
        if (await servicios.ResolverCompraAnterior(cuenta, vivo, delCarrito, cancelacion).ConfigureAwait(false) is { } anterior)
        {
            return Resultado<PasoAlPagoPreparado>.Fallo(anterior);
        }

        // 2. Lo que es red, antes de la transacción (D19), en el orden de las comprobaciones.
        if (ServiciosDelCarrito.ErrorDeLimite(await carrito.Limites.PasarAlPago(cuenta, $"{vivo.Id}:{vivo.PedidoId}", cancelacion).ConfigureAwait(false)) is { } limite)
        {
            return Resultado<PasoAlPagoPreparado>.Fallo(limite);
        }

        if (await ComprobarLaCuenta(cuenta, cancelacion).ConfigureAwait(false) is { } cuentaRechazada)
        {
            return Resultado<PasoAlPagoPreparado>.Fallo(cuentaRechazada);
        }

        var digitales = DigitalesVigentes(vivo, delCarrito);
        var textos = await ComprobarLosTextos(vivo, digitales.Count > 0, entrada, cancelacion).ConfigureAwait(false);
        if (textos.Error is { } textoRechazado)
        {
            return Resultado<PasoAlPagoPreparado>.Fallo(textoRechazado);
        }

        var consulta = await carrito.Catalogo.ObtenerEdiciones([.. delCarrito.Lineas.Select(l => l.EdicionId).Distinct()], cancelacion).ConfigureAwait(false);
        if (!consulta.EsExito)
        {
            return Resultado<PasoAlPagoPreparado>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDelCarrito.ServicioNoDisponible));
        }

        var ediciones = consulta.Valor.ToDictionary(e => e.EdicionId);
        if (ComprobarLaLicencia(digitales, ediciones) is { } licencia)
        {
            return Resultado<PasoAlPagoPreparado>.Fallo(licencia);
        }

        if (await ComprobarLaPosesion(cuenta, digitales, cancelacion).ConfigureAwait(false) is { } posesion)
        {
            return Resultado<PasoAlPagoPreparado>.Fallo(posesion);
        }

        if (ComprobarLaEntrega(vivo) is { } entrega)
        {
            return Resultado<PasoAlPagoPreparado>.Fallo(entrega);
        }

        // Con un retiro pendiente con el checkout en pago, el resultado es la revisión (aunque no quede ninguna línea): no se exige una línea vigente.
        var conRetiro = delCarrito.TieneRetirosPendientes && vivo.Estado == EstadoDelCheckout.EnPago;
        var lineasVigentes = ServiciosDelCheckout.LineasParaElCheckout(delCarrito, ediciones);
        if (lineasVigentes.Count == 0 && !conRetiro)
        {
            return Resultado<PasoAlPagoPreparado>.Fallo(ServiciosDelCheckout.CheckoutVacio());
        }

        // Lo ya congelado se compara antes de tocar a Pagos; en edición el total se calcula y se compara dentro de la transacción que lo congela (CAR-49).
        if (vivo.Estado != EstadoDelCheckout.EnEdicion && ComprobarElTotal(vivo.TotalCongelado ?? 0m, entrada.TotalVisto) is { } total)
        {
            return Resultado<PasoAlPagoPreparado>.Fallo(total);
        }

        // 3. Con un retiro pendiente: Reabrir fuera de la transacción y antes de bloquear; el resultado es la revisión, no el pago.
        var puerta = PuertaDeEdicion.Libre;
        if (conRetiro)
        {
            var decidida = await carrito.Edicion.PrepararAntesDeBloquear(cuenta, cancelacion).ConfigureAwait(false);
            if (!decidida.EsExito)
            {
                return Resultado<PasoAlPagoPreparado>.Fallo(decidida.Error!);
            }

            puerta = decidida.Valor;
        }

        // 4. La transacción corta: la marca de cancelación con bloqueo primero (D10), después el checkout y el carrito, sin red.
        var vigentes = textos.Vigentes;
        var nombres = await carrito.NombresDeSucursales(cancelacion).ConfigureAwait(false);
        PasoAlPagoPreparado preparado;
        try
        {
            preparado = await carrito.Unidad.Ejecutar(token => EnLaTransaccion(cuenta, vigentes, ediciones, lineasVigentes.Select(l => l.LineaDeCarritoId!.Value).ToHashSet(), nombres, puerta, conRetiro, entrada, token), cancelacion).ConfigureAwait(false);
        }
        catch (RechazoDePasarAlPagoException rechazo)
        {
            return Resultado<PasoAlPagoPreparado>.Fallo(rechazo.Error);
        }

        return Resultado<PasoAlPagoPreparado>.Exito(preparado);
    }

    private async Task<PasoAlPagoPreparado> EnLaTransaccion(
        Guid cuenta,
        IReadOnlyDictionary<TextoLegal, string> vigentes,
        IReadOnlyDictionary<Guid, EdicionConsultada> ediciones,
        IReadOnlySet<Guid> lineasDelCarritoVigentes,
        IReadOnlyDictionary<string, string> nombres,
        PuertaDeEdicion puerta,
        bool conRetiro,
        PasarAlPagoEntrada entrada,
        CancellationToken cancelacion)
    {
        var carrito = servicios.Carrito;
        var ahora = carrito.Reloj.Ahora;

        // La marca de cancelación de la cuenta, leída con bloqueo: lo primero que se bloquea (D10). Una cancelación en marcha impide iniciar el pago (AC-CAR-27); si no se puede leer, lanza y la transacción se revierte.
        var marca = await servicios.Marca.LeerConBloqueo(cuenta, cancelacion).ConfigureAwait(false);
        if (marca != EstadoDeLaMarcaDeCancelacion.Libre)
        {
            throw new RechazoDePasarAlPagoException(new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDelCarrito.CuentaNoPuedeComprar, [new ErrorDeCampo("motivo", "cancelacion-en-curso")]));
        }

        var checkout = await carrito.Checkouts.ObtenerVivoDeCuenta(cuenta, conBloqueo: true, cancelacion).ConfigureAwait(false);
        var delCarrito = await carrito.Carritos.ObtenerDeCuenta(cuenta, conBloqueo: true, cancelacion).ConfigureAwait(false);
        if (checkout is null || delCarrito is null)
        {
            throw new RechazoDePasarAlPagoException(carrito.Autorizador.NoEncontrado());
        }

        // Solo con un retiro pendiente hay una intención reabierta que aplicar: el pago normal no pasa por la edición común (con el checkout reservando o en pago es el atajo de la misma huella).
        IReadOnlyList<EdicionRetirada> retiradas = [];
        if (puerta.Decision != DecisionDeLaPuerta.Libre)
        {
            var (cerrada, alEditar) = carrito.Edicion.AplicarDentroDeLaTransaccion(checkout, delCarrito, puerta, ahora);
            if (cerrada is not null)
            {
                throw new RechazoDePasarAlPagoException(cerrada);
            }

            retiradas = alEditar;
        }

        IReadOnlyList<EdicionRetirada> quitadas = [.. retiradas, .. delCarrito.QuitarRetirosHuerfanos(checkout.Estado, ahora)];
        if (conRetiro)
        {
            // Reabierta la intención y aplicado el retiro, se devuelve la revisión («tu carrito cambió»): no se sigue al pago.
            await GuardarTodo(cuenta, checkout, delCarrito, quitadas, cancelacion).ConfigureAwait(false);
            return new PasoAlPagoPreparado(DesenlaceDePrepararPaso.RevisionPorRetiro, cuenta, checkout.PedidoId, checkout.GeneracionDeReserva, null, null, checkout.Estado, null, [], null, null, servicios.Vista(checkout, delCarrito, null, new DatosDeEnvio(nombres, null)) with { CarritoCambio = true });
        }

        // Se revalida lo precargado: el carrito no cambió de composición mientras tanto.
        var lineas = ServiciosDelCheckout.LineasParaElCheckout(delCarrito, ediciones);
        if (lineas.Count == 0 || !lineas.Select(l => l.LineaDeCarritoId!.Value).ToHashSet().SetEquals(lineasDelCarritoVigentes))
        {
            throw new RechazoDePasarAlPagoException(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.EnCurso));
        }

        if (checkout.Estado == EstadoDelCheckout.EnEdicion)
        {
            checkout.SincronizarLineas(lineas, carrito.Identificadores.NuevoId, ahora);
        }

        checkout.RevalidarTextos(vigentes, ahora);
        foreach (var visto in entrada.TextosVistos)
        {
            var texto = ServiciosDelCheckout.DeTextoDeCompra(visto.Texto);
            if (vigentes.TryGetValue(texto, out var vigente) && vigente == visto.VersionVista)
            {
                checkout.Aceptar(texto, visto.VersionVista, ahora);
            }
        }

        // El envío que se congela: en edición se leen las tarifas una sola vez, aquí, se calcula de esa lectura lo de cada sucursal y el total, y se compara con el que la persona vio (CAR-46, CAR-49); reservando o en pago rige lo congelado.
        IReadOnlyList<EnvioDeSucursal> envios = checkout.EnviosCongelados ?? [];
        if (checkout.Estado == EstadoDelCheckout.EnEdicion)
        {
            envios = checkout.TipoDeEntrega == Tipo.Domicilio ? await servicios.DesgloseDelEnvio(checkout, cancelacion).ConfigureAwait(false) : [];
            if (ComprobarElTotal(checkout.CalcularTotal(envios).Total, entrada.TotalVisto) is { } cambio)
            {
                throw new RechazoDePasarAlPagoException(cambio);
            }
        }

        var efecto = checkout.PrepararPasoAlPago(carrito.Identificadores.NuevoId(), envios, HuellaDeLaReserva, servicios.HuellaDeLaInstantanea, delCarrito, ahora);
        if (!efecto.EsExito)
        {
            throw new RechazoDePasarAlPagoException(DeRechazo(efecto.Rechazo));
        }

        if (efecto.Efecto == EfectoDeTransicion.Aplicada)
        {
            await GuardarTodo(cuenta, checkout, delCarrito, quitadas, cancelacion).ConfigureAwait(false);
        }

        var desenlace = efecto.Efecto == EfectoDeTransicion.Aplicada ? DesenlaceDePrepararPaso.Preparado : DesenlaceDePrepararPaso.YaPreparado;
        return ConstruirPreparado(desenlace, cuenta, checkout, delCarrito, new DatosDeEnvio(nombres, null));
    }

    private async Task GuardarTodo(Guid cuenta, Checkout checkout, CarritoDeDominio delCarrito, IReadOnlyList<EdicionRetirada> quitadas, CancellationToken cancelacion)
    {
        var carrito = servicios.Carrito;
        await carrito.Checkouts.Guardar(checkout, cancelacion).ConfigureAwait(false);
        if (quitadas.Count > 0)
        {
            await carrito.Carritos.Guardar(delCarrito, cancelacion).ConfigureAwait(false);
            await carrito.Carritos.AgregarAvisos(cuenta, carrito.AvisosDeRetiro(quitadas, new Dictionary<Guid, EdicionConsultada>(), carrito.Reloj.Ahora), cancelacion).ConfigureAwait(false);
        }
    }

    // ---------- Las huellas ----------

    /// <summary>
    /// La huella de reserva: lo que el Inventario ve (líneas, cantidades y entrega) y nunca contacto ni precios, así que un SHA-256 simple basta; solo cambia si cambian las líneas o la entrega, y entonces sube la generación y
    /// cambia la clave de idempotencia (editar A→B→A genera otra, SEL-16).
    /// </summary>
    private static string HuellaDeLaReserva(string textoCanonico) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(textoCanonico))).ToLowerInvariant();

    // ---------- Comprobaciones, en orden ----------

    /// <summary>
    /// Las digitales que se pagarían: con el checkout en edición, las del carrito (las líneas del checkout pueden estar por sincronizar tras una edición o un retiro); con <c>reservando</c> o <c>en_pago</c>, las ya congeladas.
    /// Una con retiro pendiente no cuenta.
    /// </summary>
    private static IReadOnlyList<Guid> DigitalesVigentes(Checkout vivo, CarritoDeDominio delCarrito) =>
        vivo.Estado == EstadoDelCheckout.EnEdicion
            ? [.. delCarrito.Lineas.Where(l => l.EsDigital && !l.RetiroPendiente).Select(l => l.EdicionId)]
            : [.. vivo.Lineas.Where(l => l.EsDigital).Select(l => l.EdicionId)];

    /// <summary>
    /// El estado de la cuenta por <c>IEstadoDeCuenta</c> (sin caché y fallando cerrado, CAR-37): cancelada o desactivada, con cancelación diferida o en ejecución (CAR-31), con el correo sin verificar (CAR-21) o con los textos
    /// pendientes (CAR-36). Si Identidad no responde, no se inicia.
    /// </summary>
    private async Task<ErrorDeAplicacion?> ComprobarLaCuenta(Guid cuenta, CancellationToken cancelacion)
    {
        EstadoDeCuenta? estado;
        try
        {
            estado = await servicios.Carrito.EstadoDeLaCuenta.Consultar(cuenta, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDelCarrito.ServicioNoDisponible);
        }

        if (estado is null || estado.CuentaCanceladaODesactivada)
        {
            return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDelCarrito.CuentaNoPuedeComprar, [new ErrorDeCampo("motivo", "cuenta-no-activa")]);
        }

        if (estado.PuedeComprar)
        {
            return null;
        }

        if (!estado.CorreoVerificado)
        {
            return new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDelCarrito.CorreoSinVerificar);
        }

        return estado.Textos == EstadoDeAceptacionDeLosTextos.Pendiente
            ? new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDelCarrito.TextosPendientes)
            : new ErrorDeAplicacion(CategoriaDeError.Prohibido, ClavesDelCarrito.CuentaNoPuedeComprar, [new ErrorDeCampo("motivo", "cancelacion-diferida")]);
    }

    /// <summary>
    /// Las aceptaciones y la versión de los textos (CAR-12, CAR-13, CAR-28): una versión vista que ya no es la vigente es <c>texto-cambio</c> (409); lo que falta de lo requerido (ni aceptado con la versión vigente ni visto ahora)
    /// es <c>aceptacion-requerida</c> (422). Sin las versiones vigentes no se puede comparar y se falla cerrado.
    /// </summary>
    private async Task<(ErrorDeAplicacion? Error, IReadOnlyDictionary<TextoLegal, string> Vigentes)> ComprobarLosTextos(Checkout vivo, bool hayDigitales, PasarAlPagoEntrada entrada, CancellationToken cancelacion)
    {
        var requeridos = AceptacionesDelCheckout.Requeridas(hayDigitales);
        var vigentes = new Dictionary<TextoLegal, string>();
        foreach (var texto in requeridos)
        {
            var obtenido = await servicios.Textos.Obtener(ServiciosDelCheckout.TipoDeTextoWeb(texto), cancelacion).ConfigureAwait(false);
            if (!obtenido.EsExito)
            {
                return (new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDelCarrito.ServicioNoDisponible), vigentes);
            }

            vigentes[texto] = obtenido.Valor.Version;
        }

        var vistos = entrada.TextosVistos.ToDictionary(v => ServiciosDelCheckout.DeTextoDeCompra(v.Texto), v => v.VersionVista);
        var cambiaron = requeridos.Where(t => vistos.TryGetValue(t, out var v) && v != vigentes[t]).ToList();
        if (cambiaron.Count > 0)
        {
            return (new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.TextoCambio, [.. cambiaron.Select(t => new ErrorDeCampo(ServiciosDelCheckout.NombreDelTexto(t), vigentes[t]))]), vigentes);
        }

        var aceptadas = vivo.Aceptaciones.Aceptadas.ToDictionary(a => a.Texto, a => a.Version);
        var faltan = requeridos.Where(t => !(aceptadas.TryGetValue(t, out var a) && a == vigentes[t]) && !(vistos.TryGetValue(t, out var v) && v == vigentes[t])).ToList();
        return faltan.Count > 0
            ? (new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDelCarrito.AceptacionRequerida, [.. faltan.Select(t => new ErrorDeCampo(ServiciosDelCheckout.NombreDelTexto(t), ConstructorDeValidacion.Obligatorio))]), vigentes)
            : (null, vigentes);
    }

    /// <summary>La licencia de cada digital (CAR-14, AC-CAR-12): una edición que el Catálogo ya no ofrece (licencia vencida, sin Perú, retirada o dada de baja) bloquea el pago y se invita a quitarla.</summary>
    private static ErrorDeAplicacion? ComprobarLaLicencia(IReadOnlyList<Guid> digitales, IReadOnlyDictionary<Guid, EdicionConsultada> ediciones)
    {
        var sinLicencia = digitales
            .Where(id => !(ediciones.TryGetValue(id, out var e) && e.Estado == EstadoDeEdicionConsultado.Activa && e.MotivoDeNoDisponible is null))
            .Select(id => new ErrorDeCampo("edicionId", id.ToString("D")))
            .ToList();
        return sinLicencia.Count > 0 ? new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.LicenciaNoVigente, sinLicencia) : null;
    }

    /// <summary>La posesión de las digitales (CAR-20, AC-CAR-18): ya poseída es <c>digital-ya-poseida</c> (409); si no se puede confirmar, falla cerrado (D07).</summary>
    private async Task<ErrorDeAplicacion?> ComprobarLaPosesion(Guid cuenta, IReadOnlyList<Guid> digitales, CancellationToken cancelacion)
    {
        if (digitales.Count == 0)
        {
            return null;
        }

        ResultadoDePosesion posesion;
        try
        {
            posesion = await servicios.Carrito.Posesion.Posee(cuenta, digitales, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDelCarrito.ServicioNoDisponible);
        }

        return posesion.Estado switch
        {
            EstadoDePosesion.Posee => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.DigitalYaPoseida, [.. posesion.Poseidas.Select(id => new ErrorDeCampo("edicionId", id.ToString("D")))]),
            EstadoDePosesion.NoSePuedeConfirmar => new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDelCarrito.ServicioNoDisponible),
            _ => null,
        };
    }

    /// <summary>La entrega y el contacto completos (CAR-08, CAR-30, AC-CAR-26): un carrito con impresos necesita su entrega y, a domicilio, el contacto.</summary>
    private static ErrorDeAplicacion? ComprobarLaEntrega(Checkout vivo) =>
        (vivo.RequiereEntrega && vivo.TipoDeEntrega is null) || (vivo.TipoDeEntrega == Tipo.Domicilio && vivo.Contacto is null)
            ? new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDelCarrito.DatosDeEntregaIncompletos)
            : null;

    /// <summary>El total que la persona vio solo se compara (D17) con el calculado (precio vigente y envío de las tarifas vigentes) o, ya congelado, con el congelado. Si difiere, no se crea nada y se devuelve el nuevo (CAR-49).</summary>
    private static ErrorDeAplicacion? ComprobarElTotal(decimal total, decimal totalVisto) =>
        Soles.Redondear(total) == Soles.Redondear(totalVisto)
            ? null
            : new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.TotalCambio, [new ErrorDeCampo("total", total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))]);

    private static ErrorDeAplicacion? ValidarForma(PasarAlPagoEntrada entrada)
    {
        var errores = new List<ErrorDeCampo>();
        if (entrada.TotalVisto < 0 || entrada.TotalVisto > 99_999_999.99m)
        {
            errores.Add(new ErrorDeCampo("totalVisto", ConstructorDeValidacion.FueraDeRango));
        }

        if (entrada.TextosVistos is null || entrada.TextosVistos.Count > 3
            || entrada.TextosVistos.Select(t => t.Texto).Distinct().Count() != entrada.TextosVistos.Count
            || entrada.TextosVistos.Any(t => string.IsNullOrWhiteSpace(t.VersionVista) || t.VersionVista.Length > AceptacionesDelCheckout.LongitudMaximaDeLaVersion))
        {
            errores.Add(new ErrorDeCampo("textosVistos", ConstructorDeValidacion.FormatoInvalido));
        }

        return errores.Count == 0 ? null : new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDelCarrito.ValidacionFallida, errores);
    }

    private static ErrorDeAplicacion DeRechazo(RechazoDeCheckout? rechazo) => rechazo switch
    {
        RechazoDeCheckout.EntregaNoElegida or RechazoDeCheckout.FaltaElContacto => new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDelCarrito.DatosDeEntregaIncompletos),
        RechazoDeCheckout.AceptacionRequerida => new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDelCarrito.AceptacionRequerida),
        RechazoDeCheckout.SinLineas => ServiciosDelCheckout.CheckoutVacio(),
        RechazoDeCheckout.PagoPendiente => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.PagoPendiente),
        RechazoDeCheckout.LineaSinSucursal => ServiciosDelCheckout.LineaSinSucursal(),
        _ => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.EnCurso),
    };

    // ---------- Lo que queda para los pasos siguientes ----------

    private PasoAlPagoPreparado ConstruirPreparado(DesenlaceDePrepararPaso desenlace, Guid cuenta, Checkout checkout, CarritoDeDominio delCarrito, DatosDeEnvio datos)
    {
        var impresas = checkout.Lineas.Where(l => !l.EsDigital).Select(l => new LineaParaElInventario(l.LineaId, l.EdicionId, l.Cantidad.Valor, l.CodigoDeSucursal)).ToList();
        EntregaParaElInventario? entrega = checkout.TipoDeEntrega switch
        {
            Tipo.Recojo => new EntregaParaElInventario(Tipo.Recojo, null),
            Tipo.Domicilio => new EntregaParaElInventario(Tipo.Domicilio, checkout.RegionDeEntrega),
            _ => null,
        };

        return new PasoAlPagoPreparado(
            desenlace,
            cuenta,
            checkout.PedidoId,
            checkout.GeneracionDeReserva,
            checkout.HuellaDeReserva,
            checkout.HuellaDeInstantanea,
            checkout.Estado,
            entrega,
            impresas,
            ConstruirLaInstantanea(cuenta, checkout, datos.Nombres),
            checkout.ReservaExpiraEn,
            servicios.Vista(checkout, delCarrito, null, datos));
    }

    private InstantaneaDeCompra ConstruirLaInstantanea(Guid cuenta, Checkout checkout, IReadOnlyDictionary<string, string> nombres)
    {
        var subtotal = Soles.Redondear(checkout.Lineas.Sum(l => l.Importe));
        var envio = checkout.EnvioCongelado ?? 0m;
        var contacto = checkout.TipoDeEntrega == Tipo.Domicilio && checkout.Contacto is { } c
            ? new ContactoDeCompra(c.Nombre.Valor, c.Telefono.Valor, c.Direccion.Texto, c.Direccion.Region)
            : null;
        return new InstantaneaDeCompra(
            checkout.PedidoId!.Value,
            cuenta,
            checkout.TipoDeEntrega is { } tipo ? ServiciosDelCheckout.DeTipo(tipo) : TipoDeEntregaDeCompra.Digital,
            checkout.CodigoDeSucursalDeRecojo,
            checkout.RegionDeEntrega,
            subtotal,
            envio,
            checkout.TotalCongelado ?? subtotal + envio,
            contacto,
            [.. checkout.Aceptaciones.Aceptadas.Select(a => new AceptacionDeCompra(ServiciosDelCheckout.DeTexto(a.Texto), a.Version, a.AceptadoEn))],
            [.. checkout.Lineas.Select(l => new LineaDeInstantanea(l.LineaId, l.EdicionId, l.EsDigital, l.Titulo, l.Isbn, l.Cantidad.Valor, l.PrecioUnitario, l.Importe, l.CodigoDeFuenteInformado, l.RequiereTrasladoInformado, l.PlazoInformadoDias))],
            checkout.HuellaDeInstantanea!,
            checkout.EnviosCongelados is { } congelados
                ? [.. congelados.Select(e => new EnvioDeSucursalDeCompra(e.CodigoDeSucursal, nombres.GetValueOrDefault(e.CodigoDeSucursal), e.Costo.Valor))]
                : null);
    }
}

/// <summary>Un rechazo esperado dentro de la transacción de pasar al pago: revierte la transacción (nada se guarda) y sube como un fallo del caso de uso.</summary>
internal sealed class RechazoDePasarAlPagoException(ErrorDeAplicacion error) : Exception("Pasar al pago se rechazó dentro de la transacción.")
{
    public ErrorDeAplicacion Error { get; } = error;
}
