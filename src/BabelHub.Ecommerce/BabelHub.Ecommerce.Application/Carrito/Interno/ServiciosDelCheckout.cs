using System.Globalization;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;
using TextoLegal = BabelHub.Ecommerce.Domain.Carrito.TextoLegal;
using Tipo = BabelHub.Ecommerce.Domain.Carrito.TipoDeEntrega;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

/// <summary>Lo que se informó de una línea impresa en esta solicitud: dónde está y, si viene por traslado, en cuántos días (<see langword="null"/> es «a confirmar», FUE-13).</summary>
internal sealed record InformacionDeLinea(DisponibilidadDeLineaDeCheckout Disponibilidad, int? PlazoDias, bool PlazoAConfirmar, string? CodigoDeFuente, bool? RequiereTraslado);

/// <summary>
/// Lo que comparten los casos de uso del checkout: los del carrito, el Inventario (disponibilidad real, consulta previa y, más adelante, reservar y liberar) y el costo de envío configurado, más las funciones que arman las
/// líneas del checkout desde el carrito y el Catálogo y la <see cref="VistaDelCheckout"/>. El Inventario se consulta siempre <b>antes</b> de abrir una transacción y sin memoria (CAR-05); sin respuesta no se avanza (CAR-06).
/// </summary>
internal sealed partial class ServiciosDelCheckout(
    ServiciosDelCarrito carrito,
    IInventarioDelCarrito inventario,
    ICostoDeEnvioConfigurado costoDeEnvio,
    ITextosLegales textosLegales,
    IConsentimientoDeAvisos consentimiento,
    IMarcaDeCancelacion marcaDeCancelacion,
    ICalculadorDeHuella calculadorDeHuella,
    IAlertasDelCarrito alertas)
{
    public IAlertasDelCarrito Alertas => alertas;

    public IMarcaDeCancelacion Marca => marcaDeCancelacion;

    /// <summary>La huella de instantánea (D19): un digest <b>con clave</b> y propósito propio, de modo que el teléfono que lleva su texto canónico no se pueda recuperar por fuerza bruta. Se guarda con la versión de la clave.</summary>
    public string HuellaDeLaInstantanea(string textoCanonico) => calculadorDeHuella.DeSujeto(textoCanonico, "instantanea-de-compra").ToString();

    public ICalculadorDeHuella Huellas => calculadorDeHuella;

    public ITextosLegales Textos => textosLegales;

    public IConsentimientoDeAvisos Consentimiento => consentimiento;

    public ServiciosDelCarrito Carrito => carrito;

    public IInventarioDelCarrito Inventario => inventario;

    public ICostoDeEnvioConfigurado CostoDeEnvio => costoDeEnvio;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex FormaDelCodigoDeSucursal();

    public static bool EsCodigoDeSucursalValido(string? codigo) => codigo is not null && FormaDelCodigoDeSucursal().IsMatch(codigo);

    public static ErrorDeAplicacion StockNoValidable() => new(CategoriaDeError.NoDisponible, ClavesDelCarrito.StockNoValidable);

    public static ErrorDeAplicacion CheckoutVacio() => new(CategoriaDeError.ReglaDeNegocio, ClavesDelCarrito.CheckoutVacio);

    public static ErrorDeAplicacion SinStock(int maximo) =>
        new(CategoriaDeError.Conflicto, ClavesDelCarrito.SinStock, [new ErrorDeCampo("maximo", maximo.ToString(CultureInfo.InvariantCulture))]);

    // ---------- Las líneas del checkout ----------

    /// <summary>
    /// Las líneas que entran al checkout: las del carrito que no tienen retiro pendiente y cuya edición el Catálogo ofrece (activa y sin motivo de no disponible), con el precio vigente. Una edición que no se conoce
    /// no entra (se ve retirada en el carrito).
    /// </summary>
    public static IReadOnlyList<LineaParaCheckout> LineasParaElCheckout(CarritoDeDominio carrito, IReadOnlyDictionary<Guid, EdicionConsultada> ediciones)
    {
        var lineas = new List<LineaParaCheckout>();
        foreach (var linea in carrito.Lineas)
        {
            if (linea.RetiroPendiente || !ediciones.TryGetValue(linea.EdicionId, out var edicion) || edicion.Estado != EstadoDeEdicionConsultado.Activa || edicion.MotivoDeNoDisponible is not null)
            {
                continue;
            }

            lineas.Add(new LineaParaCheckout(linea.EdicionId, linea.EsDigital, edicion.Titulo, edicion.Isbn, linea.Cantidad.Valor, edicion.Precio));
        }

        return lineas;
    }

    // ---------- El Inventario (siempre fuera de una transacción) ----------

    /// <summary>
    /// La disponibilidad real de los impresos (sin memoria, por lotes de a lo más 50): devuelve lo vendible por edición, con las <c>noRegistradas</c> en cero. Sin respuesta, con un límite o con algo inválido, el error es
    /// «no se puede validar el stock» y no se avanza (CAR-06).
    /// </summary>
    public async Task<(ErrorDeAplicacion? Error, IReadOnlyDictionary<Guid, int> Vendible)> DisponibilidadReal(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion)
    {
        var vendible = new Dictionary<Guid, int>();
        foreach (var lote in edicionIds.Distinct().Chunk(50))
        {
            var respuesta = await inventario.ConsultarDisponibilidad(lote, cancelacion).ConfigureAwait(false);
            if (!respuesta.Exitosa)
            {
                return (StockNoValidable(), vendible);
            }

            foreach (var e in respuesta.Valor.Ediciones)
            {
                vendible[e.EdicionId] = Math.Max(0, e.VendibleEnLaRed);
            }

            foreach (var noRegistrada in respuesta.Valor.NoRegistradas)
            {
                vendible[noRegistrada] = 0;
            }
        }

        return (null, vendible);
    }

    /// <summary>La primera línea impresa que pide más de lo vendible (la que no está en la respuesta cuenta como cero), con el máximo.</summary>
    public static ErrorDeAplicacion? PrimerExcesoDeStock(IEnumerable<LineaParaCheckout> lineas, IReadOnlyDictionary<Guid, int> vendible)
    {
        foreach (var linea in lineas.Where(l => !l.EsDigital))
        {
            var hay = vendible.GetValueOrDefault(linea.EdicionId);
            if (linea.Cantidad > hay)
            {
                return SinStock(hay);
            }
        }

        return null;
    }

    /// <summary>La consulta previa de los impresos para una entrega (fuente, traslado y plazo por línea); sin respuesta no se avanza (CAR-06).</summary>
    public async Task<(ErrorDeAplicacion? Error, IReadOnlyDictionary<Guid, InformacionDeLinea> Informacion, IReadOnlyList<SeleccionDeLinea> Selecciones)> ConsultaPrevia(EntregaParaElInventario entrega, IReadOnlyList<LineaParaElInventario> lineas, CancellationToken cancelacion)
    {
        var informacion = new Dictionary<Guid, InformacionDeLinea>();
        if (lineas.Count == 0)
        {
            return (null, informacion, []);
        }

        var respuesta = await inventario.ConsultarPrevia(entrega, lineas, cancelacion).ConfigureAwait(false);
        if (!respuesta.Exitosa)
        {
            return (StockNoValidable(), informacion, []);
        }

        foreach (var s in respuesta.Valor.Lineas)
        {
            informacion[s.EdicionId] = DeSeleccion(s);
        }

        return (null, informacion, respuesta.Valor.Lineas);
    }

    /// <summary>
    /// El estado del checkout frente a una edición de la entrega, el contacto o las aceptaciones: con el pago pendiente se rechaza (CAR-26) y con <c>reservando</c> responde «en curso»; con <c>en_pago</c> la decide la edición
    /// común (<see cref="EdicionConCheckoutVivo"/>, que reabre la intención). Sin error con <c>en_edicion</c> y <c>en_pago</c>.
    /// </summary>
    public static ErrorDeAplicacion? ErrorDeEstado(EstadoDelCheckout estado) => estado switch
    {
        EstadoDelCheckout.PagoPendiente => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.PagoPendiente),
        EstadoDelCheckout.Reservando => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.EnCurso),
        _ => null,
    };

    /// <summary>
    /// Lo primero de todo caso de uso del checkout: solo un cliente con sesión y cuenta activa (falla cerrado si Identidad no responde), el límite de revisión, el <c>pedidoId</c> oculto si lo trae (uno ajeno o inexistente es «no
    /// encontrado» uniforme con su evento; el propio ya terminado o descartado, un error amable sin evento) y el checkout vivo de la cuenta leído sin bloqueo (sin checkout, «no encontrado»).
    /// </summary>
    public async Task<Resultado<CheckoutDeQuienPide>> EntrarAlCheckout(Guid? pedidoOculto, CancellationToken cancelacion)
    {
        var entro = await EntrarSinExigirCheckout(pedidoOculto, cancelacion).ConfigureAwait(false);
        if (!entro.EsExito)
        {
            return Resultado<CheckoutDeQuienPide>.Fallo(entro.Error!);
        }

        return entro.Valor.Checkout is { } vivo
            ? Resultado<CheckoutDeQuienPide>.Exito(new CheckoutDeQuienPide(entro.Valor.Cuenta, vivo, entro.Valor.Carrito))
            : Resultado<CheckoutDeQuienPide>.Fallo(carrito.Autorizador.NoEncontrado());
    }

    /// <summary>Igual que <see cref="EntrarAlCheckout"/> pero sin checkout vivo no es un error (volver y reiniciar son idempotentes).</summary>
    public async Task<Resultado<CheckoutOpcionalDeQuienPide>> EntrarSinExigirCheckout(Guid? pedidoOculto, CancellationToken cancelacion)
    {
        var cuenta = await carrito.Autorizador.ExigirCuenta(cancelacion).ConfigureAwait(false);
        if (!cuenta.EsExito)
        {
            return Resultado<CheckoutOpcionalDeQuienPide>.Fallo(cuenta.Error!);
        }

        if (await carrito.ExigirCuentaActiva(cuenta.Valor, cancelacion).ConfigureAwait(false) is { } cuentaRechazada)
        {
            return Resultado<CheckoutOpcionalDeQuienPide>.Fallo(cuentaRechazada);
        }

        if (ServiciosDelCarrito.ErrorDeLimite(await carrito.Limites.RevisarCheckout(cuenta.Valor, cancelacion).ConfigureAwait(false)) is { } limite)
        {
            return Resultado<CheckoutOpcionalDeQuienPide>.Fallo(limite);
        }

        if (pedidoOculto is { } pedido)
        {
            var dueno = await carrito.Checkouts.ObtenerPorPedido(pedido, conBloqueo: false, cancelacion).ConfigureAwait(false);
            var visto = dueno is null ? null : new PedidoVisto(dueno.CuentaId, dueno.EsVivo && dueno.PedidoId == pedido);
            if (await carrito.Autorizador.ComprobarPedidoOculto(pedido, cuenta.Valor, visto, cancelacion).ConfigureAwait(false) is { } rechazo)
            {
                return Resultado<CheckoutOpcionalDeQuienPide>.Fallo(rechazo);
            }
        }

        var vivo = await carrito.Checkouts.ObtenerVivoDeCuenta(cuenta.Valor, conBloqueo: false, cancelacion).ConfigureAwait(false);
        var delCarrito = await carrito.Carritos.ObtenerDeCuenta(cuenta.Valor, conBloqueo: false, cancelacion).ConfigureAwait(false);
        return Resultado<CheckoutOpcionalDeQuienPide>.Exito(new CheckoutOpcionalDeQuienPide(cuenta.Valor, vivo, delCarrito));
    }

    public static InformacionDeLinea DeSeleccion(SeleccionDeLinea s) =>
        !s.HayStock
            ? new InformacionDeLinea(DisponibilidadDeLineaDeCheckout.Agotada, null, false, null, null)
            : new InformacionDeLinea(
                s.RequiereTraslado ? DisponibilidadDeLineaDeCheckout.PorTraslado : DisponibilidadDeLineaDeCheckout.EnSuSucursal,
                s.PlazoEstimadoDias,
                s.RequiereTraslado && s.PlazoEstimadoDias is null,
                s.CodigoDeFuente,
                s.RequiereTraslado);

    // ---------- La vista ----------

    public VistaDelCheckout Vista(Checkout checkout, CarritoDeDominio? delCarrito, IReadOnlyDictionary<Guid, InformacionDeLinea>? informacion)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        var retiradas = delCarrito?.Lineas.Where(l => l.RetiroPendiente).Select(l => l.EdicionId).ToHashSet() ?? [];
        var lineas = checkout.Lineas.Select(l => new LineaDeRevision(
            l.LineaId,
            l.EdicionId,
            l.Titulo,
            l.EsDigital ? FormatoDeEdicion.Digital : FormatoDeEdicion.Impresa,
            l.Cantidad.Valor,
            l.PrecioUnitario,
            l.Importe,
            DisponibilidadDe(l, informacion),
            PlazoDe(l, informacion),
            PlazoAConfirmarDe(l, informacion),
            retiradas.Contains(l.EdicionId))).ToList();

        var subtotal = Soles.Redondear(lineas.Where(l => !l.Retirada).Sum(l => l.Importe));
        var total = checkout.CalcularTotal(costoDeEnvio.Valor);
        var envio = checkout.TipoDeEntrega == Tipo.Domicilio ? checkout.EnvioCongelado ?? total.Envio : (decimal?)null;
        var totalAPagar = checkout.TotalCongelado ?? total.Total;

        var estado = DeEstado(checkout.Estado);
        var conRetiro = delCarrito is { TieneRetirosPendientes: true };
        Guid? pedido = checkout.Estado == EstadoDelCheckout.PagoPendiente || (checkout.Estado == EstadoDelCheckout.EnPago && !conRetiro) ? checkout.PedidoId : null;

        return new VistaDelCheckout(
            estado,
            checkout.NumeroDePaso,
            checkout.TotalDePasos,
            lineas,
            checkout.TipoDeEntrega is { } tipo ? DeTipo(tipo) : null,
            checkout.CodigoDeSucursalDeRecojo,
            checkout.RegionDeEntrega,
            subtotal,
            envio,
            totalAPagar,
            checkout.Contacto is { } c ? new ContactoDeCompra(c.Nombre.Valor, c.Telefono.Valor, c.Direccion.Texto, c.Direccion.Region) : null,
            [.. checkout.Aceptaciones.Aceptadas.Select(a => new AceptacionDeCompra(DeTexto(a.Texto), a.Version, a.AceptadoEn))],
            [.. checkout.TextosPorAceptar().Select(DeTexto)],
            checkout.ReservaExpiraEn,
            pedido,
            conRetiro);
    }

    private static DisponibilidadDeLineaDeCheckout DisponibilidadDe(LineaDelCheckout l, IReadOnlyDictionary<Guid, InformacionDeLinea>? informacion)
    {
        if (l.EsDigital)
        {
            return DisponibilidadDeLineaDeCheckout.DisponibleAlInstante;
        }

        if (informacion is not null && informacion.TryGetValue(l.EdicionId, out var i))
        {
            return i.Disponibilidad;
        }

        return l.RequiereTrasladoInformado is { } traslado
            ? (traslado ? DisponibilidadDeLineaDeCheckout.PorTraslado : DisponibilidadDeLineaDeCheckout.EnSuSucursal)
            : DisponibilidadDeLineaDeCheckout.Disponible;
    }

    private static int? PlazoDe(LineaDelCheckout l, IReadOnlyDictionary<Guid, InformacionDeLinea>? informacion) =>
        informacion is not null && informacion.TryGetValue(l.EdicionId, out var i) ? i.PlazoDias : l.PlazoInformadoDias;

    private static bool PlazoAConfirmarDe(LineaDelCheckout l, IReadOnlyDictionary<Guid, InformacionDeLinea>? informacion) =>
        informacion is not null && informacion.TryGetValue(l.EdicionId, out var i) ? i.PlazoAConfirmar : l.RequiereTrasladoInformado == true && l.PlazoInformadoDias is null;

    public static EstadoVisibleDelCheckout DeEstado(EstadoDelCheckout estado) => estado switch
    {
        EstadoDelCheckout.EnEdicion => EstadoVisibleDelCheckout.EnEdicion,
        EstadoDelCheckout.Reservando => EstadoVisibleDelCheckout.Reservando,
        EstadoDelCheckout.EnPago => EstadoVisibleDelCheckout.EnPago,
        EstadoDelCheckout.PagoPendiente => EstadoVisibleDelCheckout.PagoPendiente,
        _ => throw new ArgumentOutOfRangeException(nameof(estado), "Un checkout terminado no tiene vista."),
    };

    public static TipoDeEntregaDeCompra DeTipo(Tipo tipo) => tipo switch
    {
        Tipo.Recojo => TipoDeEntregaDeCompra.Recojo,
        Tipo.Domicilio => TipoDeEntregaDeCompra.Domicilio,
        _ => TipoDeEntregaDeCompra.Digital,
    };

    public static string NombreDelTexto(TextoLegal texto) => texto switch
    {
        TextoLegal.Terminos => "terminos",
        TextoLegal.Privacidad => "privacidad",
        _ => "reembolsoDigital",
    };

    public static TipoDeTextoLegal TipoDeTextoWeb(TextoLegal texto) => texto switch
    {
        TextoLegal.Terminos => TipoDeTextoLegal.Terminos,
        TextoLegal.Privacidad => TipoDeTextoLegal.Privacidad,
        _ => TipoDeTextoLegal.Reembolso,
    };

    public static TextoLegal DeTextoDeCompra(TextoLegalDeCompra texto) => texto switch
    {
        TextoLegalDeCompra.Terminos => TextoLegal.Terminos,
        TextoLegalDeCompra.Privacidad => TextoLegal.Privacidad,
        _ => TextoLegal.ReembolsoDigital,
    };

    public static ConsentimientoDeWhatsAppDeCompra DeOferta(OfertaDeConsentimiento oferta) => oferta.Estado switch
    {
        EstadoDeLaOfertaDeConsentimiento.SeOfrece => new ConsentimientoDeWhatsAppDeCompra(EstadoDelConsentimientoDeCompra.SeOfrece, oferta.HuellaEnmascarada),
        EstadoDeLaOfertaDeConsentimiento.YaConsiente => new ConsentimientoDeWhatsAppDeCompra(EstadoDelConsentimientoDeCompra.YaConsiente),
        _ => new ConsentimientoDeWhatsAppDeCompra(EstadoDelConsentimientoDeCompra.NoSeOfrece),
    };

    public static TextoLegalDeCompra DeTexto(TextoLegal texto) => texto switch
    {
        TextoLegal.Terminos => TextoLegalDeCompra.Terminos,
        TextoLegal.Privacidad => TextoLegalDeCompra.Privacidad,
        _ => TextoLegalDeCompra.ReembolsoDigital,
    };
}
