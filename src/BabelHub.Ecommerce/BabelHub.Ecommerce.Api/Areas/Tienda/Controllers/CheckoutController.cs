using System.Globalization;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>La pantalla del checkout (revisión, entrega, datos y aceptaciones): lo que la persona ve de su compra en curso. Los precios, el envío y el total salen del servidor; la pantalla solo los pinta y devuelve el total visto para compararlo.</summary>
public sealed class CheckoutVista : ModeloBaseDePagina
{
    public VistaDelCheckout Checkout { get; set; } = null!;
}

/// <summary>
/// El checkout (plan §4, CAR-05 a CAR-18, CAR-26 a CAR-35): <c>POST /checkout/iniciar</c>, <c>GET /checkout</c> (sin efectos), <c>POST /checkout/entrega</c>, <c>POST /checkout</c> (pasar al pago, con la verificación de persona de Turnstile), <c>POST /checkout/volver</c> y
/// <c>POST /checkout/reiniciar</c>. Las rutas <c>/checkout</c> y <c>/checkout/pago</c> son de coincidencia <b>exacta</b>. <b>Presentación pura</b>: lee el cuerpo con esquema estricto (un campo desconocido o repetido es validación; <c>carritoId</c>, <c>checkoutId</c> o <c>cuentaId</c> se
/// rechazan y registran un acceso denegado), llama a <see cref="ICheckoutDelCliente"/> y traduce el resultado: una pantalla, una redirección <c>303</c> (o <c>HX-Redirect</c> con htmx) o un <c>application/problem+json</c> con el código <c>ECO-…</c>. El <c>pedidoId</c> oculto, cuando
/// el checkout ya lo tiene, detecta una pestaña obsoleta. <b>Pasar al pago falla cerrado:</b> sin verificación de persona (si se exige) o con el servicio de verificación caído no se llama a la Aplicación. Todo requiere una sesión; el token antifalsificación de cada <c>POST</c> lo valida
/// el filtro global antes de la acción.
/// </summary>
[Area("Tienda")]
[RequiereSesion]
public sealed partial class CheckoutController(ICheckoutDelCliente checkout, IExigenciaDeVerificacionDePersona verificacion, ContextoDePagina pagina, OpcionesDeErrores errores) : Controller
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
            return await Falla(error!);
        }

        var sucursal = campos.GetValueOrDefault("sucursalPreferida");
        if (sucursal is not null && (sucursal.Length is 0 or > 40 || sucursal.Any(char.IsControl)))
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("sucursalPreferida"));
        }

        return await Terminar(await checkout.Iniciar(new IniciarCheckoutEntrada(sucursal), cancelacion), "/checkout");
    }

    [HttpGet("/checkout")]
    public async Task<IActionResult> Index(CancellationToken cancelacion)
    {
        if (Request.Query.Count > 0)
        {
            return await Falla(EntradasHttpDelCarrito.CuerpoInvalido);
        }

        var resultado = await checkout.Obtener(cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!);
        }

        // Con un retiro pendiente la Aplicación no entrega el pedidoId: se muestra la revisión con su aviso, sin redirigir al pago (CAR-26).
        var vista = await pagina.Completar(new CheckoutVista { Checkout = resultado.Valor }, "Tu compra", cancelacion: cancelacion);
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
            return await Falla(error!);
        }

        if (!campos.TryGetValue("tipoDeEntrega", out var tipoTexto))
        {
            return await Falla(EntradasHttpDelCarrito.Falta("tipoDeEntrega"));
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
            return await Falla(EntradasHttpDelCarrito.Invalido("tipoDeEntrega"));
        }

        if (!PedidoOculto(campos, out var pedidoId, out var errorDelPedido))
        {
            return await Falla(errorDelPedido!);
        }

        // Solo el domicilio lleva contacto; con otro tipo, un campo de contacto es validación (nunca se ignora en silencio).
        var contactoEnviado = new[] { "direccion", "region", "nombre", "telefono" }.Where(campos.ContainsKey).ToList();
        if (tipo != TipoDeEntregaDeCompra.Domicilio && contactoEnviado.Count > 0)
        {
            return await Falla(EntradasHttpDelCarrito.Invalido(contactoEnviado[0]));
        }

        if (tipo != TipoDeEntregaDeCompra.Recojo && campos.ContainsKey("sucursal"))
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("sucursal"));
        }

        var contacto = tipo == TipoDeEntregaDeCompra.Domicilio
            ? new ContactoDeCompra(campos.GetValueOrDefault("nombre"), campos.GetValueOrDefault("telefono"), campos.GetValueOrDefault("direccion"), campos.GetValueOrDefault("region"))
            : null;
        return await Terminar(await checkout.ElegirEntrega(new ElegirEntregaEntrada(tipo.Value, campos.GetValueOrDefault("sucursal"), contacto, pedidoId), cancelacion), "/checkout");
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
            return await Falla(error!);
        }

        var token = campos.GetValueOrDefault(VerificacionDePersonaWeb.CampoDelToken) ?? Request.Headers["X-Verificacion-De-Persona"].FirstOrDefault();
        switch (await verificacion.Exigir(AccionProtegida.Reserva, token, cancelacion))
        {
            case ResultadoDeExigencia.Requerida:
                return await FallaDeVerificacion("VERIFICACION-REQUERIDA", CategoriaDeError.Prohibido);
            case ResultadoDeExigencia.Fallida:
                return await FallaDeVerificacion("VERIFICACION-FALLIDA", CategoriaDeError.Prohibido);
            case ResultadoDeExigencia.NoDisponible:
                return await Falla(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible"));
        }

        if (!PedidoOculto(campos, out var pedidoId, out var errorDelPedido))
        {
            return await Falla(errorDelPedido!);
        }

        if (!TryTotal(campos, out var total))
        {
            return await Falla(campos.ContainsKey("totalVisto") ? EntradasHttpDelCarrito.Invalido("totalVisto") : EntradasHttpDelCarrito.Falta("totalVisto"));
        }

        var textos = new List<TextoVistoDeCompra>();
        foreach (var (campo, texto) in new[] { ("terminosVersion", TextoLegalDeCompra.Terminos), ("privacidadVersion", TextoLegalDeCompra.Privacidad), ("reembolsoDigitalVersion", TextoLegalDeCompra.ReembolsoDigital) })
        {
            if (!campos.TryGetValue(campo, out var version))
            {
                continue;
            }

            if (version.Length is 0 or > 40 || version.Any(char.IsControl))
            {
                return await Falla(EntradasHttpDelCarrito.Invalido(campo));
            }

            textos.Add(new TextoVistoDeCompra(texto, version));
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
                return await Falla(EntradasHttpDelCarrito.Invalido("consentimientoDeWhatsApp"));
            }
        }

        var huella = campos.GetValueOrDefault("huellaDelNumero");
        if (huella is not null && (huella.Length is 0 or > 40 || huella.Any(char.IsControl)))
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("huellaDelNumero"));
        }

        var aceptaciones = await checkout.RegistrarAceptaciones(new RegistrarAceptacionesEntrada(textos, whatsapp, huella, pedidoId), cancelacion);
        if (!aceptaciones.EsExito)
        {
            return await Falla(aceptaciones.Error!);
        }

        var resultado = await checkout.PasarAlPago(new PasarAlPagoEntrada(total, textos, pedidoId), cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!);
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
            return await Falla(error!);
        }

        if (!PedidoOculto(campos, out var pedidoId, out var errorDelPedido))
        {
            return await Falla(errorDelPedido!);
        }

        var resultado = await checkout.VolverAlCarrito(new SalirDelCheckoutEntrada(pedidoId), cancelacion);
        return resultado.EsExito ? Redirigir("/carrito") : await Falla(resultado.Error!);
    }

    [HttpPost("/checkout/reiniciar")]
    public async Task<IActionResult> Reiniciar(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDeSalir, cancelacion);
        if (campos is null)
        {
            return await Falla(error!);
        }

        if (!PedidoOculto(campos, out var pedidoId, out var errorDelPedido))
        {
            return await Falla(errorDelPedido!);
        }

        var resultado = await checkout.Reiniciar(new SalirDelCheckoutEntrada(pedidoId), cancelacion);
        return resultado.EsExito ? Redirigir("/carrito") : await Falla(resultado.Error!);
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

    private async Task<IActionResult> Terminar(Resultado<VistaDelCheckout> resultado, string destino) =>
        resultado.EsExito ? Redirigir(destino) : await Falla(resultado.Error!);

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

    private async Task<IActionResult> FallaDeVerificacion(string sufijo, CategoriaDeError categoria)
    {
        var problema = ProblemasHttp.Crear(HttpContext, errores, categoria, sufijo.ToLowerInvariant(), []) with { Code = CodigosDeError.De(errores.Servicio, sufijo) };
        Response.OnStarting(() =>
        {
            Response.Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });
        await DenegacionesHttp.Registrar(HttpContext, categoria, sufijo.ToLowerInvariant(), problema.Code);
        await ProblemasHttp.Escribir(HttpContext, problema);
        return new EmptyResult();
    }

    private async Task<IActionResult> Falla(ErrorDeAplicacion error)
    {
        await ProblemasDelCarrito.Escribir(HttpContext, errores, error);
        return new EmptyResult();
    }
}
