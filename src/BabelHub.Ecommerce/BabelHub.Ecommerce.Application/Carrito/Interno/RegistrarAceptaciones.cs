using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;
using TextoLegal = BabelHub.Ecommerce.Domain.Carrito.TextoLegal;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

/// <summary>El checkout vivo de quien pide, sin datos: <c>GET /checkout</c> no recibe identificadores (el actor es la cuenta).</summary>
public sealed record ObtenerCheckoutEntrada;

/// <summary>
/// <c>ObtenerCheckout</c> (CAR-18; <c>GET /checkout</c>): el checkout vivo de quien pide, <b>sin efectos</b>, con la oferta del consentimiento de WhatsApp (solo con número, con su huella enmascarada; si Identidad no responde, no se
/// ofrece y el checkout igual se muestra). No consulta el Inventario: la revisión con la disponibilidad real es <c>IniciarCheckout</c>.
/// </summary>
internal sealed class ObtenerCheckout(ServiciosDelCheckout servicios) : ICasoDeUso<ObtenerCheckoutEntrada, VistaDelCheckout>
{
    public async Task<Resultado<VistaDelCheckout>> Ejecutar(ObtenerCheckoutEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var entro = await servicios.EntrarAlCheckout(null, cancelacion).ConfigureAwait(false);
        if (!entro.EsExito)
        {
            return Resultado<VistaDelCheckout>.Fallo(entro.Error!);
        }

        var (cuenta, vivo, delCarrito) = entro.Valor;
        var oferta = await servicios.Consentimiento.Consultar(cuenta, cancelacion).ConfigureAwait(false);
        return Resultado<VistaDelCheckout>.Exito(servicios.Vista(vivo, delCarrito, null) with { ConsentimientoDeWhatsApp = ServiciosDelCheckout.DeOferta(oferta) });
    }
}

/// <summary>
/// <c>RegistrarAceptaciones</c> (CAR-12, CAR-13, CAR-18, CAR-22, CAR-28; AC-CAR-11, AC-CAR-15, AC-CAR-16, AC-CAR-20): compara las versiones vistas con las <b>vigentes</b> (<c>ITextosLegales</c>, de Identidad), exige términos y privacidad
/// siempre y el reembolso digital si hay digitales, guarda la versión y la fecha de <b>cada</b> texto (repetir la misma versión conserva la primera fecha) e invalida las aceptaciones de una versión que cambió. Una versión vista que ya no
/// es la vigente responde <c>checkout-texto-cambio</c> (409) y falta de un texto requerido, <c>checkout-aceptacion-requerida</c> (422); lo que sí cambió se guarda igual. Con el checkout <c>en_pago</c> un cambio reabre la intención (edición
/// común de T11) y uno sin variación no. El <b>consentimiento de WhatsApp</b> se pide a Identidad <b>fuera de la transacción</b>, solo si todo lo demás salió bien, solo si se ofrece (la cuenta ya tiene número), con la huella enmascarada
/// que se mostró y solo si la cuenta puede comprar: una falla no bloquea el pago ni dice «guardado».
/// </summary>
internal sealed class RegistrarAceptaciones(ServiciosDelCheckout servicios) : ICasoDeUso<RegistrarAceptacionesEntrada, VistaDelCheckout>
{
    public async Task<Resultado<VistaDelCheckout>> Ejecutar(RegistrarAceptacionesEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var carrito = servicios.Carrito;

        // 1. Alcance, límite y forma de la entrada, antes de cualquier red.
        var entro = await servicios.EntrarAlCheckout(entrada.PedidoId, cancelacion).ConfigureAwait(false);
        if (!entro.EsExito)
        {
            return Resultado<VistaDelCheckout>.Fallo(entro.Error!);
        }

        var (cuenta, vivo, delCarrito) = entro.Valor;
        if (ValidarForma(entrada) is { } invalida)
        {
            return Resultado<VistaDelCheckout>.Fallo(invalida);
        }

        if (ServiciosDelCheckout.ErrorDeEstado(vivo.Estado) is { } estado)
        {
            return Resultado<VistaDelCheckout>.Fallo(estado);
        }

        // 2. Las versiones vigentes (fuera de la transacción): sin ellas no se puede comparar y se falla cerrado (CAR-28).
        var hayDigitales = vivo.Lineas.Any(l => l.EsDigital);
        var requeridos = AceptacionesDelCheckout.Requeridas(hayDigitales);
        var vigentes = new Dictionary<TextoLegal, string>();
        foreach (var texto in requeridos)
        {
            var obtenido = await servicios.Textos.Obtener(ServiciosDelCheckout.TipoDeTextoWeb(texto), cancelacion).ConfigureAwait(false);
            if (!obtenido.EsExito)
            {
                return Resultado<VistaDelCheckout>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDelCarrito.ServicioNoDisponible));
            }

            vigentes[texto] = obtenido.Valor.Version;
        }

        // 3. Qué se acepta y qué cambió: solo cuentan los textos requeridos; la versión vista debe ser la vigente.
        var vistos = entrada.Aceptados.ToDictionary(a => ServiciosDelCheckout.DeTextoDeCompra(a.Texto), a => a.VersionVista);
        var cambiaron = requeridos.Where(t => vistos.TryGetValue(t, out var v) && v != vigentes[t]).ToList();
        var porAceptar = requeridos.Where(t => vistos.TryGetValue(t, out var v) && v == vigentes[t]).ToList();

        var yaAceptadas = vivo.Aceptaciones.Aceptadas.ToDictionary(a => a.Texto, a => a.Version);
        var invalidadas = yaAceptadas.Where(p => vigentes.TryGetValue(p.Key, out var vigente) && vigente != p.Value).Select(p => p.Key).ToList();
        var hayCambioDeEstado = invalidadas.Count > 0 || porAceptar.Any(t => !yaAceptadas.TryGetValue(t, out var v) || v != vistos[t]);

        CarritoDeDominio? delCarritoFinal = delCarrito;
        var checkout = vivo;
        if (hayCambioDeEstado)
        {
            // 4. La edición con el checkout vivo (T11): Reabrir fuera de la transacción y antes de bloquear.
            var decidida = await carrito.Edicion.PrepararAntesDeBloquear(cuenta, cancelacion).ConfigureAwait(false);
            if (!decidida.EsExito)
            {
                return Resultado<VistaDelCheckout>.Fallo(decidida.Error!);
            }

            // 5. La transacción: el checkout primero y el carrito después (D10), sin red.
            var transaccion = await carrito.Unidad.Ejecutar(token => EnLaTransaccion(cuenta, vigentes, porAceptar, vistos, decidida.Valor, token), cancelacion).ConfigureAwait(false);
            if (!transaccion.Resultado.EsExito)
            {
                return Resultado<VistaDelCheckout>.Fallo(transaccion.Resultado.Error!);
            }

            checkout = transaccion.Resultado.Valor;
            delCarritoFinal = transaccion.Carrito;
        }

        // 6. El resultado: un cambio de versión primero, después lo que falta.
        if (cambiaron.Count > 0)
        {
            return Resultado<VistaDelCheckout>.Fallo(new ErrorDeAplicacion(
                CategoriaDeError.Conflicto,
                ClavesDelCarrito.TextoCambio,
                [.. cambiaron.Select(t => new ErrorDeCampo(ServiciosDelCheckout.NombreDelTexto(t), vigentes[t]))]));
        }

        var faltan = checkout.TextosPorAceptar();
        if (faltan.Count > 0)
        {
            return Resultado<VistaDelCheckout>.Fallo(new ErrorDeAplicacion(
                CategoriaDeError.Validacion,
                ClavesDelCarrito.AceptacionRequerida,
                [.. faltan.Select(t => new ErrorDeCampo(ServiciosDelCheckout.NombreDelTexto(t), ConstructorDeValidacion.Obligatorio))]));
        }

        // 7. El consentimiento de WhatsApp: fuera de la transacción, sin exigirlo y siempre después de las comprobaciones de la cuenta.
        var consentimiento = await ResolverConsentimiento(cuenta, entrada, cancelacion).ConfigureAwait(false);
        return Resultado<VistaDelCheckout>.Exito(servicios.Vista(checkout, delCarritoFinal, null) with { ConsentimientoDeWhatsApp = consentimiento });
    }

    private async Task<(Resultado<Checkout> Resultado, CarritoDeDominio? Carrito)> EnLaTransaccion(
        Guid cuenta,
        IReadOnlyDictionary<TextoLegal, string> vigentes,
        IReadOnlyList<TextoLegal> porAceptar,
        IReadOnlyDictionary<TextoLegal, string> vistos,
        PuertaDeEdicion puerta,
        CancellationToken cancelacion)
    {
        var carrito = servicios.Carrito;
        var ahora = carrito.Reloj.Ahora;
        var checkout = await carrito.Checkouts.ObtenerVivoDeCuenta(cuenta, conBloqueo: true, cancelacion).ConfigureAwait(false);
        var delCarrito = await carrito.Carritos.ObtenerDeCuenta(cuenta, conBloqueo: true, cancelacion).ConfigureAwait(false);
        if (checkout is null || delCarrito is null)
        {
            return (Resultado<Checkout>.Fallo(carrito.Autorizador.NoEncontrado()), delCarrito);
        }

        var (cerrada, retiradas) = carrito.Edicion.AplicarDentroDeLaTransaccion(checkout, delCarrito, puerta, ahora);
        if (cerrada is not null)
        {
            return (Resultado<Checkout>.Fallo(cerrada), delCarrito);
        }

        // Las aceptaciones de una versión que cambió se invalidan (CAR-28); después se guardan las vistas con la versión vigente, con su fecha.
        checkout.RevalidarTextos(vigentes, ahora);
        foreach (var texto in porAceptar)
        {
            checkout.Aceptar(texto, vistos[texto], ahora);
        }

        IReadOnlyList<EdicionRetirada> quitadas = [.. retiradas, .. delCarrito.QuitarRetirosHuerfanos(checkout.Estado, ahora)];
        await carrito.Checkouts.Guardar(checkout, cancelacion).ConfigureAwait(false);
        if (quitadas.Count > 0)
        {
            await carrito.Carritos.Guardar(delCarrito, cancelacion).ConfigureAwait(false);
            await carrito.Carritos.AgregarAvisos(cuenta, carrito.AvisosDeRetiro(quitadas, new Dictionary<Guid, EdicionConsultada>(), ahora), cancelacion).ConfigureAwait(false);
        }

        return (Resultado<Checkout>.Exito(checkout), delCarrito);
    }

    private async Task<ConsentimientoDeWhatsAppDeCompra> ResolverConsentimiento(Guid cuenta, RegistrarAceptacionesEntrada entrada, CancellationToken cancelacion)
    {
        var oferta = await servicios.Consentimiento.Consultar(cuenta, cancelacion).ConfigureAwait(false);
        if (oferta.Estado != EstadoDeLaOfertaDeConsentimiento.SeOfrece || entrada.ConsentimientoDeWhatsApp != true)
        {
            return ServiciosDelCheckout.DeOferta(oferta);
        }

        // Marcado: solo con la huella que se mostró y solo si la cuenta puede comprar (IDE-58, CAR-31); una falla no bloquea el pago ni dice «guardado».
        if (string.IsNullOrWhiteSpace(entrada.HuellaEnmascaradaDelNumero) || !await servicios.Carrito.PuedeComprar(cuenta, cancelacion).ConfigureAwait(false))
        {
            return new ConsentimientoDeWhatsAppDeCompra(EstadoDelConsentimientoDeCompra.NoGuardado, oferta.HuellaEnmascarada);
        }

        var guardado = await servicios.Consentimiento.Guardar(cuenta, entrada.HuellaEnmascaradaDelNumero, cancelacion).ConfigureAwait(false);
        return new ConsentimientoDeWhatsAppDeCompra(guardado ? EstadoDelConsentimientoDeCompra.Guardado : EstadoDelConsentimientoDeCompra.NoGuardado, oferta.HuellaEnmascarada);
    }

    private static ErrorDeAplicacion? ValidarForma(RegistrarAceptacionesEntrada entrada)
    {
        var errores = new List<ErrorDeCampo>();
        if (entrada.Aceptados is null || entrada.Aceptados.Count == 0 || entrada.Aceptados.Count > 3)
        {
            errores.Add(new ErrorDeCampo("aceptados", ConstructorDeValidacion.FueraDeRango));
        }
        else
        {
            if (entrada.Aceptados.Select(a => a.Texto).Distinct().Count() != entrada.Aceptados.Count)
            {
                errores.Add(new ErrorDeCampo("aceptados", ConstructorDeValidacion.FormatoInvalido));
            }

            if (entrada.Aceptados.Any(a => string.IsNullOrWhiteSpace(a.VersionVista) || a.VersionVista.Length > AceptacionesDelCheckout.LongitudMaximaDeLaVersion))
            {
                errores.Add(new ErrorDeCampo("aceptados", ConstructorDeValidacion.FormatoInvalido));
            }
        }

        if (entrada.HuellaEnmascaradaDelNumero is { Length: > 40 })
        {
            errores.Add(new ErrorDeCampo("huellaEnmascaradaDelNumero", ConstructorDeValidacion.DemasiadoLargo));
        }

        return errores.Count == 0
            ? null
            : new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDelCarrito.ValidacionFallida, [.. errores.DistinctBy(e => (e.Campo, e.Clave))]);
    }
}
