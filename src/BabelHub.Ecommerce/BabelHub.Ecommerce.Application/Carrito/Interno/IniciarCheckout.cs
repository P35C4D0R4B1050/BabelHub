using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

/// <summary>
/// <c>IniciarCheckout</c> (CAR-05, CAR-06, CAR-07; AC-CAR-04, AC-CAR-05, AC-CAR-06): <b>solo con sesión y cuenta activa</b> (el estado de la cuenta se consulta antes de la transacción y falla cerrado). Un orquestador sin red en
/// su transacción: antes de abrirla obtiene del Catálogo los precios y del Inventario la <b>disponibilidad real</b> por sucursal, por lotes y sin memoria, y la <b>consulta previa</b> declarando la sucursal de cada
/// línea (las <c>noRegistradas</c> son agotadas y las líneas de un libro en una sucursal se suman, CAR-05); un carrito solo digital no consulta (regla 12); sin respuesta del Inventario informa que no puede validar el stock y no avanza y un impreso sin sucursal tampoco (CAR-45). En
/// la transacción bloquea el checkout y después el carrito, revalida el stock y la composición del carrito y crea el checkout en la revisión; con uno vivo <b>devuelve ese</b> (si está en edición, vuelve a armar sus líneas
/// desde el carrito conservando el <c>lineaId</c> de las que siguen).
/// </summary>
internal sealed class IniciarCheckout(ServiciosDelCheckout servicios) : ICasoDeUso<IniciarCheckoutEntrada, VistaDelCheckout>
{
    public async Task<Resultado<VistaDelCheckout>> Ejecutar(IniciarCheckoutEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var carrito = servicios.Carrito;

        // 1. Alcance y entrada: solo un cliente con sesión; la cuenta sale del actor.
        var cuenta = await carrito.Autorizador.ExigirCuenta(cancelacion).ConfigureAwait(false);
        if (!cuenta.EsExito)
        {
            return Resultado<VistaDelCheckout>.Fallo(cuenta.Error!);
        }

        // 2. Todo lo que es red, antes de la transacción (D19): la cuenta, el límite, el Catálogo y el Inventario.
        if (await carrito.ExigirCuentaActiva(cuenta.Valor, cancelacion).ConfigureAwait(false) is { } cuentaRechazada)
        {
            return Resultado<VistaDelCheckout>.Fallo(cuentaRechazada);
        }

        if (ServiciosDelCarrito.ErrorDeLimite(await carrito.Limites.RevisarCheckout(cuenta.Valor, cancelacion).ConfigureAwait(false)) is { } limite)
        {
            return Resultado<VistaDelCheckout>.Fallo(limite);
        }

        var vivo = await carrito.Checkouts.ObtenerVivoDeCuenta(cuenta.Valor, conBloqueo: false, cancelacion).ConfigureAwait(false);
        var delCarrito = await carrito.Carritos.ObtenerDeCuenta(cuenta.Valor, conBloqueo: false, cancelacion).ConfigureAwait(false);

        // Una compra anterior a la venta por sucursal reservando o en pago se aborta y su reserva se libera (CAR-45); con un cobro vivo no cambia nada.
        if (vivo is { Estado: EstadoDelCheckout.Reservando or EstadoDelCheckout.EnPago, TieneImpresosSinSucursal: true }
            && await servicios.ResolverCompraAnterior(cuenta.Valor, vivo, delCarrito, cancelacion).ConfigureAwait(false) is { Clave: not ClavesDelCarrito.PagoPendiente } anterior)
        {
            return Resultado<VistaDelCheckout>.Fallo(anterior);
        }

        // Con un checkout que ya no está en edición no hay nada que armar ni que consultar: se devuelve tal cual (reservando, en pago o pago pendiente).
        if (vivo is { Estado: not EstadoDelCheckout.EnEdicion })
        {
            return Resultado<VistaDelCheckout>.Exito(await servicios.VistaActual(vivo, delCarrito, null, cancelacion).ConfigureAwait(false));
        }

        if (delCarrito is null || delCarrito.Lineas.Count == 0)
        {
            return Resultado<VistaDelCheckout>.Fallo(ServiciosDelCheckout.CheckoutVacio());
        }

        // Un impreso sin sucursal no inicia el checkout: se quita y se vuelve a agregar eligiendo sucursal (CAR-45).
        if (ServiciosDelCheckout.TieneImpresosSinSucursal(delCarrito))
        {
            return Resultado<VistaDelCheckout>.Fallo(ServiciosDelCheckout.LineaSinSucursal());
        }

        var consulta = await carrito.Catalogo.ObtenerEdiciones([.. delCarrito.Lineas.Select(l => l.EdicionId).Distinct()], cancelacion).ConfigureAwait(false);
        if (!consulta.EsExito)
        {
            return Resultado<VistaDelCheckout>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDelCarrito.ServicioNoDisponible));
        }

        var ediciones = consulta.Valor.ToDictionary(e => e.EdicionId);
        var lineas = ServiciosDelCheckout.LineasParaElCheckout(delCarrito, ediciones);
        if (lineas.Count == 0)
        {
            return Resultado<VistaDelCheckout>.Fallo(ServiciosDelCheckout.CheckoutVacio());
        }

        var impresas = lineas.Where(l => !l.EsDigital).ToList();
        IReadOnlyDictionary<Guid, StockDeEdicion> stock = new Dictionary<Guid, StockDeEdicion>();
        IReadOnlyDictionary<Guid, InformacionDeLinea> porLineaDelCarrito = new Dictionary<Guid, InformacionDeLinea>();
        if (impresas.Count > 0)
        {
            var disponibilidad = await servicios.DisponibilidadReal([.. impresas.Select(l => l.EdicionId)], cancelacion).ConfigureAwait(false);
            if (disponibilidad.Error is { } sinRespuesta)
            {
                return Resultado<VistaDelCheckout>.Fallo(sinRespuesta);
            }

            stock = disponibilidad.Stock;
            if (ServiciosDelCheckout.PrimerExcesoDeStock(lineas, stock) is { } exceso)
            {
                return Resultado<VistaDelCheckout>.Fallo(exceso);
            }

            // La consulta previa declara la sucursal de cada línea (CAR-05): aún sin entrega elegida, como recojo en cada sucursal.
            var aConsultar = impresas.Select(l => new LineaParaElInventario(l.LineaDeCarritoId!.Value, l.EdicionId, l.Cantidad, l.CodigoDeSucursal)).ToList();
            var previa = await servicios.ConsultaPrevia(new EntregaParaElInventario(TipoDeEntrega.Recojo, null), aConsultar, cancelacion).ConfigureAwait(false);
            if (previa.Error is { } previaFallida)
            {
                return Resultado<VistaDelCheckout>.Fallo(previaFallida);
            }

            if (ServiciosDelCheckout.PrimeraSinStock(aConsultar, previa.Selecciones) is { } sinStock)
            {
                return Resultado<VistaDelCheckout>.Fallo(sinStock);
            }

            porLineaDelCarrito = previa.Informacion;
        }

        // 3. La transacción: el checkout primero y el carrito después (D10), sin red.
        (Resultado<Checkout> Resultado, CarritoDeDominio? Carrito) transaccion;
        try
        {
            transaccion = await carrito.Unidad.Ejecutar(token => EnLaTransaccion(cuenta.Valor, ediciones, stock, token), cancelacion).ConfigureAwait(false);
        }
        catch (CheckoutYaExisteException)
        {
            // Otra solicitud creó el checkout a la vez (índice único): se rehace y ahora lo encuentra.
            transaccion = await carrito.Unidad.Ejecutar(token => EnLaTransaccion(cuenta.Valor, ediciones, stock, token), cancelacion).ConfigureAwait(false);
        }

        if (!transaccion.Resultado.EsExito)
        {
            return Resultado<VistaDelCheckout>.Fallo(transaccion.Resultado.Error!);
        }

        // 4. La vista, fuera de la transacción: lo informado por línea del carrito pasa a la línea del checkout que sale de ella.
        var checkout = transaccion.Resultado.Valor;
        var informacion = new Dictionary<Guid, InformacionDeLinea>();
        foreach (var linea in checkout.Lineas)
        {
            if (linea.LineaDeCarritoId is { } deCarrito && porLineaDelCarrito.TryGetValue(deCarrito, out var informado))
            {
                informacion[linea.LineaId] = informado;
            }
        }

        return Resultado<VistaDelCheckout>.Exito(await servicios.VistaActual(checkout, transaccion.Carrito, informacion, cancelacion).ConfigureAwait(false));
    }

    private async Task<(Resultado<Checkout> Resultado, CarritoDeDominio? Carrito)> EnLaTransaccion(Guid cuenta, IReadOnlyDictionary<Guid, EdicionConsultada> ediciones, IReadOnlyDictionary<Guid, StockDeEdicion> stock, CancellationToken cancelacion)
    {
        var carrito = servicios.Carrito;
        var ahora = carrito.Reloj.Ahora;

        var vivo = await carrito.Checkouts.ObtenerVivoDeCuenta(cuenta, conBloqueo: true, cancelacion).ConfigureAwait(false);
        var delCarrito = await carrito.Carritos.ObtenerDeCuenta(cuenta, conBloqueo: true, cancelacion).ConfigureAwait(false);
        if (vivo is { Estado: not EstadoDelCheckout.EnEdicion })
        {
            return (Resultado<Checkout>.Exito(vivo), delCarrito);
        }

        if (delCarrito is null)
        {
            return (Resultado<Checkout>.Fallo(ServiciosDelCheckout.CheckoutVacio()), null);
        }

        // Se revalida lo que se obtuvo antes de abrir la transacción: otro dispositivo pudo cambiar el carrito mientras tanto.
        var lineas = ServiciosDelCheckout.LineasParaElCheckout(delCarrito, ediciones);
        if (lineas.Count == 0)
        {
            return (Resultado<Checkout>.Fallo(ServiciosDelCheckout.CheckoutVacio()), delCarrito);
        }

        if (lineas.Any(l => !l.EsDigital && l.CodigoDeSucursal is null))
        {
            return (Resultado<Checkout>.Fallo(ServiciosDelCheckout.LineaSinSucursal()), delCarrito);
        }

        if (lineas.Any(l => !l.EsDigital && !stock.ContainsKey(l.EdicionId)))
        {
            // Una edición impresa que no estaba cuando se consultó el Inventario: la solicitud se rehace desde el principio.
            return (Resultado<Checkout>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.EnCurso)), delCarrito);
        }

        if (ServiciosDelCheckout.PrimerExcesoDeStock(lineas, stock) is { } exceso)
        {
            return (Resultado<Checkout>.Fallo(exceso), delCarrito);
        }

        if (vivo is null)
        {
            var nuevo = Checkout.Iniciar(carrito.Identificadores.NuevoId(), cuenta, delCarrito.Id, lineas, carrito.Identificadores.NuevoId, ahora);
            await carrito.Checkouts.Agregar(nuevo, cancelacion).ConfigureAwait(false);
            return (Resultado<Checkout>.Exito(nuevo), delCarrito);
        }

        // En edición: vuelve a armar las líneas desde el carrito, conservando el lineaId de las que siguen.
        if (vivo.SincronizarLineas(lineas, carrito.Identificadores.NuevoId, ahora).Efecto == EfectoDeTransicion.Aplicada)
        {
            await carrito.Checkouts.Guardar(vivo, cancelacion).ConfigureAwait(false);
        }

        return (Resultado<Checkout>.Exito(vivo), delCarrito);
    }
}
