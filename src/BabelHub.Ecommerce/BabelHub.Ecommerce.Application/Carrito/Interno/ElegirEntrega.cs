using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;
using Tipo = BabelHub.Ecommerce.Domain.Carrito.TipoDeEntrega;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

/// <summary>Lo que se sabe del checkout vivo de quien pide después de comprobar el alcance: su cuenta y lo leído sin bloqueo (el checkout y el carrito).</summary>
internal sealed record CheckoutDeQuienPide(Guid Cuenta, Checkout Checkout, CarritoDeDominio? Carrito);

/// <summary>Lo mismo pero con el checkout opcional: <c>volver</c> y <c>reiniciar</c> repetidos (un doble envío) sobre un checkout ya terminado son un éxito idempotente.</summary>
internal sealed record CheckoutOpcionalDeQuienPide(Guid Cuenta, Checkout? Checkout, CarritoDeDominio? Carrito);

/// <summary>
/// <c>ElegirEntrega</c> (CAR-08, CAR-09, CAR-23, CAR-30; AC-CAR-07, AC-CAR-08, AC-CAR-21, AC-CAR-26): recojo gratuito, cada línea en su sucursal, o domicilio con su contacto. Valida la forma antes de cualquier red (errores por campo que no repiten
/// el valor), comprueba la región contra la lista del Inventario, hace la <b>consulta previa fuera de la transacción</b> declarando la sucursal de cada línea (CAR-09) y guarda lo informado por
/// línea; muestra el envío de cada sucursal y la suma con las tarifas vigentes <b>sin congelarlos</b>; vacía el contacto al pasar a recojo. Con el checkout <c>en_pago</c> el cambio reabre la intención (edición común de T11); uno <b>sin variación de valores</b> es un no-op que no la reabre.
/// </summary>
internal sealed class ElegirEntrega(ServiciosDelCheckout servicios) : ICasoDeUso<ElegirEntregaEntrada, VistaDelCheckout>
{
    public async Task<Resultado<VistaDelCheckout>> Ejecutar(ElegirEntregaEntrada entrada, CancellationToken cancelacion = default)
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
        var forma = ValidarForma(entrada, vivo);
        if (forma.Error is { } invalida)
        {
            return Resultado<VistaDelCheckout>.Fallo(invalida);
        }

        // Una compra anterior a la venta por sucursal no avanza; reservando o en pago, además se aborta y libera su reserva (CAR-45).
        if (await servicios.ResolverCompraAnterior(cuenta, vivo, delCarrito, cancelacion).ConfigureAwait(false) is { } anterior)
        {
            return Resultado<VistaDelCheckout>.Fallo(anterior);
        }

        if (ServiciosDelCheckout.ErrorDeEstado(vivo.Estado) is { } estado)
        {
            return Resultado<VistaDelCheckout>.Fallo(estado);
        }

        // 2. A domicilio, la región se valida contra la lista del Inventario (R05); sin lista no se continúa.
        if (forma.Contacto is { } contacto)
        {
            var lista = await servicios.Inventario.ListarSucursales(cancelacion).ConfigureAwait(false);
            if (lista is null || lista.Sucursales.Count == 0)
            {
                return Resultado<VistaDelCheckout>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDelCarrito.EntregaNoValidable));
            }

            if (lista.Sucursales.All(s => s.Region != contacto.Direccion.Region))
            {
                return Resultado<VistaDelCheckout>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDelCarrito.ValidacionFallida, [new ErrorDeCampo("region", ConstructorDeValidacion.FormatoInvalido)]));
            }
        }

        // 3. La consulta previa de los impresos para esa entrega, declarando la sucursal de cada línea, fuera de la transacción.
        var impresas = vivo.Lineas.Where(l => !l.EsDigital).ToList();
        var informacion = new Dictionary<Guid, InformacionDeLinea>();
        if (impresas.Count > 0)
        {
            var aConsultar = impresas.Select(l => new LineaParaElInventario(l.LineaId, l.EdicionId, l.Cantidad.Valor, l.CodigoDeSucursal)).ToList();
            var previa = await servicios.ConsultaPrevia(
                forma.Recojo ? new EntregaParaElInventario(Tipo.Recojo, null) : new EntregaParaElInventario(Tipo.Domicilio, forma.Contacto!.Direccion.Region),
                aConsultar,
                cancelacion).ConfigureAwait(false);
            if (previa.Error is { } sinRespuesta)
            {
                return Resultado<VistaDelCheckout>.Fallo(sinRespuesta);
            }

            foreach (var par in previa.Informacion)
            {
                informacion[par.Key] = par.Value;
            }

            if (ServiciosDelCheckout.PrimeraSinStock(aConsultar, previa.Selecciones) is { } sinStock)
            {
                return Resultado<VistaDelCheckout>.Fallo(sinStock);
            }
        }

        // 4. Un cambio sin variación de valores es un no-op: no reabre la intención ni abre la transacción.
        if (EsLoMismo(vivo, forma))
        {
            return Resultado<VistaDelCheckout>.Exito(await servicios.VistaActual(vivo, delCarrito, informacion, cancelacion).ConfigureAwait(false));
        }

        // 5. La edición con el checkout vivo (T11): Reabrir fuera de la transacción y antes de bloquear.
        var decidida = await carrito.Edicion.PrepararAntesDeBloquear(cuenta, cancelacion).ConfigureAwait(false);
        if (!decidida.EsExito)
        {
            return Resultado<VistaDelCheckout>.Fallo(decidida.Error!);
        }

        // 6. La transacción: el checkout primero y el carrito después (D10), sin red.
        var transaccion = await carrito.Unidad.Ejecutar(token => EnLaTransaccion(cuenta, forma, informacion, decidida.Valor, token), cancelacion).ConfigureAwait(false);
        return transaccion.Resultado.EsExito
            ? Resultado<VistaDelCheckout>.Exito(await servicios.VistaActual(transaccion.Resultado.Valor, transaccion.Carrito, informacion, cancelacion).ConfigureAwait(false))
            : Resultado<VistaDelCheckout>.Fallo(transaccion.Resultado.Error!);
    }

    private async Task<(Resultado<Checkout> Resultado, CarritoDeDominio? Carrito)> EnLaTransaccion(Guid cuenta, FormaDeLaEntrega forma, IReadOnlyDictionary<Guid, InformacionDeLinea> informacion, PuertaDeEdicion puerta, CancellationToken cancelacion)
    {
        var carrito = servicios.Carrito;
        var ahora = carrito.Reloj.Ahora;
        var checkout = await carrito.Checkouts.ObtenerVivoDeCuenta(cuenta, conBloqueo: true, cancelacion).ConfigureAwait(false);
        var delCarrito = await carrito.Carritos.ObtenerDeCuenta(cuenta, conBloqueo: true, cancelacion).ConfigureAwait(false);
        if (checkout is null || delCarrito is null)
        {
            return (Resultado<Checkout>.Fallo(carrito.Autorizador.NoEncontrado()), delCarrito);
        }

        // La compuerta del checkout vivo con los dos bloqueados: aplica lo decidido antes o rechaza si el checkout cambió entre tanto.
        var (cerrada, retiradas) = carrito.Edicion.AplicarDentroDeLaTransaccion(checkout, delCarrito, puerta, ahora);
        if (cerrada is not null)
        {
            return (Resultado<Checkout>.Fallo(cerrada), delCarrito);
        }

        var efecto = forma.Recojo ? checkout.ElegirRecojo(ahora) : checkout.ElegirDomicilio(forma.Contacto!, ahora);
        if (!efecto.EsExito)
        {
            return (Resultado<Checkout>.Fallo(DeRechazo(efecto.Rechazo)), delCarrito);
        }

        foreach (var linea in checkout.Lineas.Where(l => !l.EsDigital).ToList())
        {
            if (informacion.TryGetValue(linea.LineaId, out var i))
            {
                checkout.RegistrarLoInformado(linea.LineaId, i.CodigoDeFuente, i.RequiereTraslado, i.PlazoDias, ahora);
            }
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

    // ---------- Forma de la entrada ----------

    private sealed record FormaDeLaEntrega(bool Recojo, ContactoDeEntrega? Contacto, ErrorDeAplicacion? Error);

    private static FormaDeLaEntrega ValidarForma(ElegirEntregaEntrada entrada, Checkout vivo)
    {
        if (entrada.Tipo == TipoDeEntregaDeCompra.Digital || vivo.EsSoloDigital)
        {
            // Un carrito solo digital no tiene entrega (CAR-07) y «digital» no se elige: lo decide el contenido.
            return Invalida("tipoDeEntrega", vivo.EsSoloDigital ? "solo-digital" : "no-aplica");
        }

        if (entrada.Tipo == TipoDeEntregaDeCompra.Recojo)
        {
            return new FormaDeLaEntrega(true, null, null);
        }

        if (entrada.Contacto is null)
        {
            return Invalida("contacto", ConstructorDeValidacion.Obligatorio);
        }

        var c = entrada.Contacto;
        var errores = new List<ErrorDeCampo>();
        var nombreValido = NombreDeContacto.TryCrear(c.Nombre, out var nombre);
        var telefonoValido = TelefonoDeContacto.TryCrear(c.Telefono, out var telefono);
        var direccionValida = DireccionDeEntrega.TryCrear(c.Direccion, "region", out _);
        var regionValida = DireccionDeEntrega.TryCrear("direccion", c.Region, out _);
        if (!nombreValido)
        {
            errores.Add(new ErrorDeCampo("nombre", Falta(c.Nombre)));
        }

        if (!telefonoValido)
        {
            errores.Add(new ErrorDeCampo("telefono", Falta(c.Telefono)));
        }

        if (!direccionValida)
        {
            errores.Add(new ErrorDeCampo("direccion", Falta(c.Direccion)));
        }

        if (!regionValida)
        {
            errores.Add(new ErrorDeCampo("region", Falta(c.Region)));
        }

        if (errores.Count > 0)
        {
            return new FormaDeLaEntrega(false, null, new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDelCarrito.ValidacionFallida, errores));
        }

        return new FormaDeLaEntrega(false, ContactoDeEntrega.Crear(nombre!, telefono!, DireccionDeEntrega.Crear(c.Direccion, c.Region)), null);
    }

    private static string Falta(string valor) => string.IsNullOrWhiteSpace(valor) ? ConstructorDeValidacion.Obligatorio : ConstructorDeValidacion.FormatoInvalido;

    private static FormaDeLaEntrega Invalida(string campo, string clave) =>
        new(false, null, new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDelCarrito.ValidacionFallida, [new ErrorDeCampo(campo, clave)]));

    private static bool EsLoMismo(Checkout vivo, FormaDeLaEntrega forma) =>
        forma.Recojo
            ? vivo.TipoDeEntrega == Tipo.Recojo && vivo.CodigoDeSucursalDeRecojo is null
            : vivo.TipoDeEntrega == Tipo.Domicilio && forma.Contacto!.Equals(vivo.Contacto);

    private static ErrorDeAplicacion DeRechazo(RechazoDeCheckout? rechazo) => rechazo switch
    {
        RechazoDeCheckout.PagoPendiente => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.PagoPendiente),
        RechazoDeCheckout.EnCurso or RechazoDeCheckout.EstadoNoPermite => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.EnCurso),
        RechazoDeCheckout.LineaSinSucursal => ServiciosDelCheckout.LineaSinSucursal(),
        RechazoDeCheckout.EsSoloDigital => new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDelCarrito.ValidacionFallida, [new ErrorDeCampo("tipoDeEntrega", "solo-digital")]),
        _ => new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, ClavesDelCarrito.CheckoutNoEncontrado),
    };
}
