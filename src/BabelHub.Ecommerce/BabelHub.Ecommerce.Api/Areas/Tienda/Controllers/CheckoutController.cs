using System.Globalization;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>
/// El checkout (plan §4, CAR-05 a CAR-18, CAR-26 a CAR-35): <c>POST /checkout/iniciar</c>, <c>GET /checkout</c> (sin efectos), <c>POST /checkout/entrega</c>, <c>POST /checkout</c> (pasar al pago, con la verificación de persona de Turnstile), <c>POST /checkout/volver</c> y
/// <c>POST /checkout/reiniciar</c>. Las rutas <c>/checkout</c> y <c>/checkout/pago</c> son de coincidencia <b>exacta</b>. <b>Presentación pura</b>: lee el cuerpo con esquema estricto (un campo desconocido o repetido es validación; <c>carritoId</c>, <c>checkoutId</c> o <c>cuentaId</c> se
/// rechazan y registran un acceso denegado), llama a <see cref="ICheckoutDelCliente"/> y traduce el resultado: una pantalla, una redirección <c>303</c> (o <c>HX-Redirect</c> con htmx) o un <c>application/problem+json</c> con el código <c>ECO-…</c>. El <c>pedidoId</c> oculto, cuando
/// el checkout ya lo tiene, detecta una pestaña obsoleta. <b>Pasar al pago falla cerrado:</b> sin verificación de persona (si se exige) o con el servicio de verificación caído no se llama a la Aplicación. Todo requiere una sesión; el token antifalsificación de cada <c>POST</c> lo valida
/// el filtro global antes de la acción.
/// </summary>
[Area("Tienda")]
[RequestSizeLimit(EntradasHttpDelCarrito.TopeDelCuerpo)]
[RequiereSesion]
public sealed partial class CheckoutController(ICheckoutDelCliente checkout, IExigenciaDeVerificacionDePersona verificacion, ITextosLegales textos, ContextoDePagina pagina, OpcionesDeErrores errores) : Controller
{
    public const string RutaDelPago = "/checkout/pago";

    public const string RutaDelEstadoDelPago = "/checkout/pago/estado";

    private static readonly string[] CamposDeIniciar = ["sucursalPreferida"];

    private static readonly string[] CamposDeEntrega = ["tipoDeEntrega", "sucursal", "direccion", "region", "nombre", "telefono", "pedidoId"];

    private static readonly string[] CamposDeSalir = ["pedidoId"];

    private static readonly string[] CamposDePasarAlPago =
    [
        "terminosVersion",
        "privacidadVersion",
        "reembolsoDigitalVersion",
        "consentimientoDeWhatsApp",
        "huellaDelNumero",
        "totalVisto",
        "pedidoId",
        VerificacionDePersonaWeb.CampoDelToken,
    ];

    // ---------- Iniciar y ver ----------

    [HttpPost("/checkout/iniciar")]
    public async Task<IActionResult> Iniciar(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDeIniciar, cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        var sucursal = campos.GetValueOrDefault("sucursalPreferida");
        if (sucursal is not null && (sucursal.Length is 0 or > 40 || sucursal.Any(char.IsControl)))
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("sucursalPreferida"), cancelacion);
        }

        return await Terminar(await checkout.Iniciar(new IniciarCheckoutEntrada(sucursal), cancelacion), "/checkout", cancelacion);
    }

    [HttpGet("/checkout")]
    [VerificacionDePersona(AccionProtegida.Reserva)]
    public async Task<IActionResult> Index(CancellationToken cancelacion)
    {
        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext) is { } consulta)
        {
            return await Falla(consulta, cancelacion);
        }

        var resultado = await checkout.Obtener(cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, cancelacion);
        }

        // Con un retiro pendiente la Aplicación no entrega el pedidoId: se muestra la revisión con su aviso, sin redirigir al pago (CAR-26).
        var vista = await Armar(resultado.Valor, cancelacion);
        Response.Headers.CacheControl = "no-store";
        return View("Index", vista);
    }

    // ---------- Entrega ----------

    [HttpPost("/checkout/entrega")]
    public async Task<IActionResult> ElegirEntrega(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDeEntrega, cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        if (!campos.TryGetValue("tipoDeEntrega", out var tipoTexto))
        {
            return await Falla(EntradasHttpDelCarrito.Falta("tipoDeEntrega"), cancelacion);
        }

        TipoDeEntregaDeCompra? tipo = tipoTexto switch
        {
            "recojo" => TipoDeEntregaDeCompra.Recojo,
            "domicilio" => TipoDeEntregaDeCompra.Domicilio,
            "digital" => TipoDeEntregaDeCompra.Digital,
            _ => null,
        };
        if (tipo is null)
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("tipoDeEntrega"), cancelacion);
        }

        if (!PedidoOculto(campos, out var pedidoId, out var errorDelPedido))
        {
            return await Falla(errorDelPedido!, cancelacion);
        }

        // Solo el domicilio lleva contacto; con otro tipo, un campo de contacto es validación (nunca se ignora en silencio).
        var contactoEnviado = new[] { "direccion", "region", "nombre", "telefono" }.Where(campos.ContainsKey).ToList();
        if (tipo != TipoDeEntregaDeCompra.Domicilio && contactoEnviado.Count > 0)
        {
            return await Falla(EntradasHttpDelCarrito.Invalido(contactoEnviado[0]), cancelacion);
        }

        if (tipo != TipoDeEntregaDeCompra.Recojo && campos.ContainsKey("sucursal"))
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("sucursal"), cancelacion);
        }

        var contacto = tipo == TipoDeEntregaDeCompra.Domicilio
            ? new ContactoDeCompra(campos.GetValueOrDefault("nombre"), campos.GetValueOrDefault("telefono"), campos.GetValueOrDefault("direccion"), campos.GetValueOrDefault("region"))
            : null;
        var elegida = await checkout.ElegirEntrega(new ElegirEntregaEntrada(tipo.Value, campos.GetValueOrDefault("sucursal"), contacto, pedidoId), cancelacion);
        return elegida.EsExito ? Redirigir("/checkout") : await FallaEnLaPagina(elegida.Error!, campos, cancelacion);
    }

    // ---------- Pasar al pago ----------

    /// <summary>
    /// Pasa al pago: la verificación de persona (Turnstile) primero y <b>falla cerrada</b> (reserva stock), luego las aceptaciones con la versión de cada texto que la persona vio y el total visto, y por último el paso al pago. Con el pago en curso o ya iniciado redirige
    /// a la pantalla de Pagos; con una revisión (el carrito cambió o hay un intento activo) vuelve a <c>/checkout</c> sin pagar.
    /// </summary>
    [HttpPost("/checkout")]
    [VerificacionDePersona(AccionProtegida.Reserva)]
    public async Task<IActionResult> PasarAlPago(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDePasarAlPago, cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        var token = campos.GetValueOrDefault(VerificacionDePersonaWeb.CampoDelToken) ?? Request.Headers["X-Verificacion-De-Persona"].FirstOrDefault();
        switch (await verificacion.Exigir(AccionProtegida.Reserva, token, cancelacion))
        {
            case ResultadoDeExigencia.Requerida:
                return await FallaEnLaPagina(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "verificacion-requerida"), campos, cancelacion);
            case ResultadoDeExigencia.Fallida:
                return await FallaEnLaPagina(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "verificacion-fallida"), campos, cancelacion);
            case ResultadoDeExigencia.NoDisponible:
                return await Falla(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible"), cancelacion);
        }

        if (!PedidoOculto(campos, out var pedidoId, out var errorDelPedido))
        {
            return await Falla(errorDelPedido!, cancelacion);
        }

        if (!TryTotal(campos, out var total))
        {
            return await Falla(campos.ContainsKey("totalVisto") ? EntradasHttpDelCarrito.Invalido("totalVisto") : EntradasHttpDelCarrito.Falta("totalVisto"), cancelacion);
        }

        var vistos = new List<TextoVistoDeCompra>();
        foreach (var (campo, texto) in new[] { ("terminosVersion", TextoLegalDeCompra.Terminos), ("privacidadVersion", TextoLegalDeCompra.Privacidad), ("reembolsoDigitalVersion", TextoLegalDeCompra.ReembolsoDigital) })
        {
            if (!campos.TryGetValue(campo, out var version))
            {
                continue;
            }

            if (version.Length is 0 or > 40 || version.Any(char.IsControl))
            {
                return await Falla(EntradasHttpDelCarrito.Invalido(campo), cancelacion);
            }

            vistos.Add(new TextoVistoDeCompra(texto, version));
        }

        bool? whatsapp = null;
        if (campos.TryGetValue("consentimientoDeWhatsApp", out var consentimiento))
        {
            whatsapp = consentimiento switch
            {
                "true" => true,
                "false" => false,
                _ => null,
            };
            if (whatsapp is null)
            {
                return await Falla(EntradasHttpDelCarrito.Invalido("consentimientoDeWhatsApp"), cancelacion);
            }
        }

        var huella = campos.GetValueOrDefault("huellaDelNumero");
        if (huella is not null && (huella.Length is 0 or > 40 || huella.Any(char.IsControl)))
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("huellaDelNumero"), cancelacion);
        }

        var aceptaciones = await checkout.RegistrarAceptaciones(new RegistrarAceptacionesEntrada(vistos, whatsapp, huella, pedidoId), cancelacion);
        if (!aceptaciones.EsExito)
        {
            return await FallaEnLaPagina(aceptaciones.Error!, campos, cancelacion);
        }

        var resultado = await checkout.PasarAlPago(new PasarAlPagoEntrada(total, vistos, pedidoId), cancelacion);
        if (!resultado.EsExito)
        {
            return await FallaEnLaPagina(resultado.Error!, campos, cancelacion);
        }

        var pago = resultado.Valor;
        Response.Headers.CacheControl = "no-store";
        if (pago.Revision is not null || pago.PedidoId is null)
        {
            return Redirigir("/checkout");
        }

        return Redirigir(pago.PagoEnCurso ? RutaDelEstadoDelPago : RutaDelPago);
    }

    // ---------- Volver y reiniciar ----------

    [HttpPost("/checkout/volver")]
    public async Task<IActionResult> Volver(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDeSalir, cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        if (!PedidoOculto(campos, out var pedidoId, out var errorDelPedido))
        {
            return await Falla(errorDelPedido!, cancelacion);
        }

        var resultado = await checkout.VolverAlCarrito(new SalirDelCheckoutEntrada(pedidoId), cancelacion);
        return resultado.EsExito ? Redirigir("/carrito") : await Falla(resultado.Error!, cancelacion);
    }

    [HttpPost("/checkout/reiniciar")]
    public async Task<IActionResult> Reiniciar(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDeSalir, cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        if (!PedidoOculto(campos, out var pedidoId, out var errorDelPedido))
        {
            return await Falla(errorDelPedido!, cancelacion);
        }

        var resultado = await checkout.Reiniciar(new SalirDelCheckoutEntrada(pedidoId), cancelacion);
        return resultado.EsExito ? Redirigir("/carrito") : await Falla(resultado.Error!, cancelacion);
    }

    // ---------- Piezas ----------

    /// <summary>El <c>pedidoId</c> oculto: ausente es válido (se usa el checkout vivo); presente debe ser un GUID.</summary>
    private static bool PedidoOculto(IReadOnlyDictionary<string, string> campos, out Guid? pedidoId, out ErrorDeAplicacion? error)
    {
        pedidoId = null;
        error = null;
        if (!campos.ContainsKey("pedidoId"))
        {
            return true;
        }

        if (!EntradasHttpDelCarrito.TryGuid(campos, "pedidoId", out var id))
        {
            error = EntradasHttpDelCarrito.Invalido("pedidoId");
            return false;
        }

        pedidoId = id;
        return true;
    }

    /// <summary>El total visto en soles con punto decimal y a lo más dos decimales (sin separadores, signo ni exponente); solo se compara con el del servidor.</summary>
    private static bool TryTotal(IReadOnlyDictionary<string, string> campos, out decimal total)
    {
        total = 0m;
        return campos.TryGetValue("totalVisto", out var texto)
            && FormaDelTotal().IsMatch(texto)
            && decimal.TryParse(texto, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out total);
    }

    [GeneratedRegex(@"^\d{1,10}(\.\d{1,2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex FormaDelTotal();

    private async Task<IActionResult> Terminar(Resultado<VistaDelCheckout> resultado, string destino, CancellationToken cancelacion) =>
        resultado.EsExito ? Redirigir(destino) : await Falla(resultado.Error!, cancelacion);

    private IActionResult Redirigir(string destino)
    {
        Response.Headers.CacheControl = "no-store";
        if (Request.EsHtmx())
        {
            return new HxRedirectResult(destino);
        }

        // POST/Redirect/GET: 303 See Other, para que el navegador pida la pantalla con GET.
        Response.StatusCode = StatusCodes.Status303SeeOther;
        Response.Headers.Location = destino;
        return new EmptyResult();
    }

    private async Task<CheckoutVista> Armar(VistaDelCheckout vista, CancellationToken cancelacion)
    {
        var modelo = await pagina.Completar(new CheckoutVista(), "Tu compra", cancelacion: cancelacion);
        ConstructorDeLaVistaDelCheckout.Armar(modelo, vista);
        modelo.SucursalesDeRecojo = modelo.Sucursales;
        modelo.Textos = await TextosParaAceptar(vista, modelo.HayDigitales, cancelacion);
        return modelo;
    }

    /// <summary>
    /// Los textos que la persona debe aceptar con la <b>versión vigente</b> que informa Identidad (la que se manda para compararla, CAR-28): términos y privacidad siempre y el reembolso si hay digitales. Si Identidad no informa una versión no se inventa: la lista queda vacía y no se ofrece pagar.
    /// </summary>
    private async Task<IReadOnlyList<TextoParaAceptar>> TextosParaAceptar(VistaDelCheckout vista, bool hayDigitales, CancellationToken cancelacion)
    {
        var pedidos = new List<(TipoDeTextoLegal Tipo, TextoLegalDeCompra Texto, string Campo, string Titulo, string Ruta)>
        {
            (TipoDeTextoLegal.Terminos, TextoLegalDeCompra.Terminos, "terminosVersion", "los términos y condiciones", "/legal/terminos"),
            (TipoDeTextoLegal.Privacidad, TextoLegalDeCompra.Privacidad, "privacidadVersion", "la política de privacidad y el tratamiento de mis datos personales", "/legal/privacidad"),
        };
        if (hayDigitales)
        {
            pedidos.Add((TipoDeTextoLegal.Reembolso, TextoLegalDeCompra.ReembolsoDigital, "reembolsoDigitalVersion", "la política de reembolso de libros digitales", "/legal/reembolso"));
        }

        var lista = new List<TextoParaAceptar>();
        foreach (var (tipo, texto, campo, titulo, ruta) in pedidos)
        {
            var obtenido = await textos.Obtener(tipo, cancelacion);
            if (!obtenido.EsExito)
            {
                return [];
            }

            var version = obtenido.Valor.Version;
            lista.Add(new TextoParaAceptar(campo, titulo, ruta, version, vista.Aceptaciones.Any(a => a.Texto == texto && a.Version == version)));
        }

        return lista;
    }

    /// <summary>
    /// El fallo de un formulario del checkout: un navegador vuelve a ver la pantalla con el aviso del bloqueo, <b>los errores junto a cada campo</b> y lo que escribió (solo en esta respuesta, nunca en un borrador); quien no pide HTML recibe el problema en JSON.
    /// </summary>
    private async Task<IActionResult> FallaEnLaPagina(ErrorDeAplicacion error, IReadOnlyDictionary<string, string> campos, CancellationToken cancelacion)
    {
        if (!PantallasDelCarrito.PrefiereHtml(Request))
        {
            return await Falla(error, cancelacion);
        }

        var actual = await checkout.Obtener(cancelacion);
        if (!actual.EsExito)
        {
            return await Falla(error, cancelacion);
        }

        var problema = ProblemasDelCarrito.Crear(HttpContext, errores, error);
        await DenegacionesHttp.Registrar(HttpContext, error.Categoria, error.Clave, problema.Code);
        var modelo = await Armar(actual.Valor, cancelacion);
        PantallasDelCarrito.AplicarElFallo(modelo, problema, error, campos);
        Response.StatusCode = Request.EsHtmx() ? StatusCodes.Status200OK : problema.Status;
        Response.Headers.CacheControl = "no-store";
        return View("Index", modelo);
    }

    private async Task<IActionResult> Falla(ErrorDeAplicacion error, CancellationToken cancelacion)
    {
        if (PantallasDelCarrito.PrefiereHtml(Request))
        {
            return await PantallasDelCarrito.Escribir(this, pagina, errores, error, "/checkout", cancelacion);
        }

        await ProblemasDelCarrito.Escribir(HttpContext, errores, error);
        return new EmptyResult();
    }
}
