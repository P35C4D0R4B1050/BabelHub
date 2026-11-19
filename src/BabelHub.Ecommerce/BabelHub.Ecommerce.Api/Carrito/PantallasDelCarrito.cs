using System.Globalization;
using BabelHub.Ecommerce.Api.Controllers;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace BabelHub.Ecommerce.Api.Carrito;

/// <summary>
/// Los fallos de Carrito y del checkout en <b>pantalla</b> (T33 a T35; WEB-07, docs/06 §1 y §8): cuando quien pide es un navegador (<c>Accept: text/html</c>) el error se pinta con el componente <c>bh-estado</c> y los textos de la persona, con el código estable y el
/// seguimiento solo en el soporte plegable; cuando no lo es (htmx con otro tipo, una prueba, una integración) sigue saliendo el <c>application/problem+json</c>. Nunca muestra trazas ni datos de lo que la persona escribió. La regla ya la decidió el módulo: aquí solo se elige el texto.
/// </summary>
public static class PantallasDelCarrito
{
    private sealed record Texto(string Titulo, string Mensaje);

    private static readonly Dictionary<string, Texto> PorCodigo = new(StringComparer.Ordinal)
    {
        ["ECO-CARRITO-SIN-STOCK"] = new("No hay stock suficiente", "Ajusta la cantidad: de este libro quedan menos unidades de las que pediste. No se hizo ningún cobro."),
        ["ECO-CARRITO-LIMITE-DE-LINEAS"] = new("Tu carrito está lleno", "Un carrito admite hasta 50 libros distintos. Quita alguno para agregar otro."),
        ["ECO-CARRITO-LIMITE-DE-UNIDADES"] = new("Llegaste al máximo de unidades", "Puedes comprar hasta 99 unidades de un mismo libro."),
        ["ECO-DIGITAL-YA-POSEIDA"] = new("Ya tienes este libro", "Está en tu biblioteca: no hace falta comprarlo otra vez."),
        ["ECO-EDICION-NO-DISPONIBLE"] = new("Este libro ya no está disponible", "Lo retiraron de la venta. Puedes elegir otra edición en el catálogo."),
        ["ECO-CHECKOUT-PAGO-PENDIENTE"] = new("Tu pago está en proceso", "Espera a que se confirme antes de cambiar tu compra."),
        ["ECO-CHECKOUT-EN-CURSO"] = new("Estamos preparando tu compra", "Tu solicitud anterior sigue en curso. Espera unos segundos e inténtalo de nuevo."),
        ["ECO-CHECKOUT-STOCK-NO-VALIDABLE"] = new("No podemos validar el stock ahora", "No se hizo ningún cobro y tu carrito sigue igual. Inténtalo de nuevo en unos minutos."),
        ["ECO-CHECKOUT-VACIO"] = new("Tu carrito está vacío", "Agrega libros desde el catálogo para continuar."),
        ["ECO-CHECKOUT-RESERVA-RECHAZADA"] = new("No pudimos reservar tus libros", "Cambió la disponibilidad o el plazo. Revisa tu compra: no se hizo ningún cobro y tu carrito se conservó."),
        ["ECO-CHECKOUT-TEXTO-CAMBIO"] = new("Actualizamos nuestros textos", "Léelos y acéptalos de nuevo para continuar con tu compra."),
        ["ECO-CHECKOUT-TOTAL-CAMBIO"] = new("El total cambió", "Revisa tu compra: el total que viste ya no es el vigente. No se hizo ningún cobro."),
        ["ECO-CHECKOUT-LICENCIA-NO-VIGENTE"] = new("Un libro digital ya no se puede vender", "Su licencia venció. Quítalo de tu carrito para continuar."),
        ["ECO-CHECKOUT-CUENTA-NO-PUEDE-COMPRAR"] = new("Tu cuenta no puede comprar ahora", "Revisa el estado de tu cuenta e inténtalo de nuevo."),
        ["ECO-CHECKOUT-CORREO-SIN-VERIFICAR"] = new("Verifica tu correo para pagar", "Confirma tu correo con el enlace que te enviamos. Tu carrito sigue intacto."),
        ["ECO-CHECKOUT-TEXTOS-PENDIENTES"] = new("Acepta los textos legales vigentes", "Necesitas aceptar los términos y la privacidad actualizados para pagar."),
        ["ECO-CHECKOUT-ACEPTACION-REQUERIDA"] = new("Falta aceptar los textos", "Marca los términos y la privacidad (y la política de reembolso si llevas libros digitales) para pagar."),
        ["ECO-CHECKOUT-DATOS-DE-ENTREGA-INCOMPLETOS"] = new("Completa los datos de entrega", "Faltan datos para despachar tu pedido a domicilio."),
        ["ECO-CHECKOUT-PEDIDO-OBSOLETO"] = new("Esta pantalla quedó desactualizada", "Tu compra cambió en otra pestaña. Vuelve a revisarla."),
        ["ECO-VERIFICACION-REQUERIDA"] = new("Confirma que eres una persona", "Completa la verificación e inténtalo de nuevo."),
        ["ECO-VERIFICACION-FALLIDA"] = new("No pudimos verificarte", "Completa la verificación de nuevo e inténtalo otra vez."),
    };

    /// <summary>Un navegador pide una página: <c>Accept</c> incluye <c>text/html</c>. Una prueba o una integración (sin ese tipo) recibe el problema en JSON.</summary>
    public static bool PrefiereHtml(HttpRequest solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return solicitud.GetTypedHeaders().Accept.Any(a => a.MediaType.HasValue && string.Equals(a.MediaType.Value, "text/html", StringComparison.OrdinalIgnoreCase));
    }

    public static RespuestaDePantalla Traducir(ProblemaDto problema, string rutaDeReintento)
    {
        ArgumentNullException.ThrowIfNull(problema);
        if (PorCodigo.TryGetValue(problema.Code, out var texto))
        {
            return new RespuestaDePantalla(
                new EstadoVista
                {
                    Estado = EstadoDePantalla.Error,
                    Titulo = texto.Titulo,
                    Mensaje = texto.Mensaje,
                    RutaDeReintento = rutaDeReintento,
                    CodigoDeSoporte = problema.Code,
                    IdentificadorDeSeguimiento = problema.TraceId,
                },
                problema.Status,
                null,
                true,
                problema.Errores);
        }

        return TraductorDeErrores.Traducir(problema, rutaDeReintento: rutaDeReintento, parteAfectada: "No podemos continuar con tu compra ahora. No se hizo ningún cobro.");
    }

    /// <summary>
    /// Escribe el fallo como pantalla completa (el estado dentro del layout) con el código HTTP del módulo (<c>200</c> con htmx, que no reemplaza un fragmento con un 4xx), la cabecera <c>Retry-After</c> en un límite y <c>no-store</c>; una solicitud no autenticada va al ingreso.
    /// </summary>
    public static async Task<IActionResult> Escribir(Controller controlador, ContextoDePagina pagina, OpcionesDeErrores errores, ErrorDeAplicacion error, string rutaDeReintento, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(controlador);
        var contexto = controlador.HttpContext;
        var problema = ProblemasDelCarrito.Crear(contexto, errores, error);
        await DenegacionesHttp.Registrar(contexto, error.Categoria, error.Clave, problema.Code).ConfigureAwait(false);
        var respuesta = Traducir(problema, rutaDeReintento);
        if (respuesta.RedirigirA is { } destino)
        {
            return controlador.Request.EsHtmx() ? new HxRedirectResult(destino) : new LocalRedirectResult(destino);
        }

        var modelo = await pagina.Completar(new PantallaDeEstadoVista { Estado = respuesta.Vista }, respuesta.Vista.Titulo, cancelacion: cancelacion).ConfigureAwait(false);
        if (error.Categoria == CategoriaDeError.Limite && ProblemasDelCarrito.ReintentarEn(error) is { } hasta)
        {
            controlador.Response.Headers["Retry-After"] = Math.Max(1, (int)Math.Ceiling((hasta - DateTimeOffset.UtcNow).TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        controlador.Response.StatusCode = controlador.Request.EsHtmx() ? StatusCodes.Status200OK : respuesta.EstadoHttp;
        controlador.Response.Headers.CacheControl = "no-store";
        return controlador.View("~/Views/Estados/Pantalla.cshtml", modelo);
    }
}
