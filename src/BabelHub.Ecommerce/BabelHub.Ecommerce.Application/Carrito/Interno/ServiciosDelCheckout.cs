using System.Globalization;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;
using Tipo = BabelHub.Ecommerce.Domain.Carrito.TipoDeEntrega;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

/// <summary>Lo que se informó de una línea impresa en esta solicitud: dónde está y, si viene por traslado, en cuántos días (<see langword="null"/> es «a confirmar», FUE-13).</summary>
internal sealed record InformacionDeLinea(DisponibilidadDeLineaDeCheckout Disponibilidad, int? PlazoDias, bool PlazoAConfirmar, string? CodigoDeFuente, bool? RequiereTraslado);

/// <summary>
/// Lo que comparten los casos de uso del checkout: los del carrito, el Inventario (disponibilidad real, consulta previa y, más adelante, reservar y liberar) y el costo de envío configurado, más las funciones que arman las
/// líneas del checkout desde el carrito y el Catálogo y la <see cref="VistaDelCheckout"/>. El Inventario se consulta siempre <b>antes</b> de abrir una transacción y sin memoria (CAR-05); sin respuesta no se avanza (CAR-06).
/// </summary>
internal sealed partial class ServiciosDelCheckout(ServiciosDelCarrito carrito, IInventarioDelCarrito inventario, ICostoDeEnvioConfigurado costoDeEnvio)
{
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

    /// <summary>La consulta previa de los impresos para un recojo (fuente, traslado y plazo por línea); sin respuesta no se avanza.</summary>
    public async Task<(ErrorDeAplicacion? Error, IReadOnlyDictionary<Guid, InformacionDeLinea> Informacion)> ConsultaPreviaDeRecojo(string codigoDeSucursal, IReadOnlyList<LineaParaElInventario> lineas, CancellationToken cancelacion)
    {
        var informacion = new Dictionary<Guid, InformacionDeLinea>();
        if (lineas.Count == 0)
        {
            return (null, informacion);
        }

        var respuesta = await inventario.ConsultarPrevia(new EntregaParaElInventario(Tipo.Recojo, codigoDeSucursal, null), lineas, cancelacion).ConfigureAwait(false);
        if (!respuesta.Exitosa)
        {
            return (StockNoValidable(), informacion);
        }

        foreach (var s in respuesta.Valor.Lineas)
        {
            informacion[s.EdicionId] = DeSeleccion(s);
        }

        return (null, informacion);
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

    public static TextoLegalDeCompra DeTexto(TextoLegal texto) => texto switch
    {
        TextoLegal.Terminos => TextoLegalDeCompra.Terminos,
        TextoLegal.Privacidad => TextoLegalDeCompra.Privacidad,
        _ => TextoLegalDeCompra.ReembolsoDigital,
    };
}
