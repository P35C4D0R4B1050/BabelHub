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
        ["ECO-CARRITO-LIMITE-DE-LINEAS"] = new("Tu carrito está lleno", "Un carrito admite hasta 30 líneas. Quita alguna para agregar otra."),
        ["ECO-CARRITO-LIMITE-DE-UNIDADES"] = new("Llegaste al máximo de unidades", "Cada línea admite hasta 20 unidades. Para más, agrega el libro otra vez desde su ficha: se abrirá otra línea."),
        ["ECO-CARRITO-LINEA-SIN-SUCURSAL"] = new("Vuelve a elegir la sucursal", "Tienes libros agregados antes de la venta por sucursal. Quítalos del carrito y agrégalos otra vez eligiendo la sucursal. No se hizo ningún cobro."),
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
        ["ECO-CHECKOUT-ACEPTACION-REQUERIDA"] = new("Falta aceptar los textos", "Marca los términos y la privacidad (y la política de cambios y devoluciones si llevas libros digitales) para pagar."),
        ["ECO-CHECKOUT-DATOS-DE-ENTREGA-INCOMPLETOS"] = new("Completa los datos de entrega", "Faltan datos para despachar tu pedido a domicilio."),
        ["ECO-CHECKOUT-PEDIDO-OBSOLETO"] = new("Esta pantalla quedó desactualizada", "Tu compra cambió en otra pestaña. Vuelve a revisarla."),
        ["ECO-VERIFICACION-REQUERIDA"] = new("Confirma que eres una persona", "Completa la verificación e inténtalo de nuevo."),
        ["ECO-VERIFICACION-FALLIDA"] = new("No pudimos verificarte", "Completa la verificación de nuevo e inténtalo otra vez."),
        ["ECO-REC-ESTADO-INVALIDO"] = new("Esta hoja ya no admite esa acción", "Otra persona la respondió o la descartó, o ya pasó su plazo de conservación. Vuelve al listado para ver su estado."),
        ["ECO-CONFLICTO"] = new("No pudimos completar la solicitud", "Otra solicitud igual sigue en curso o la información cambió. Espera unos segundos y revisa el estado antes de repetirla."),
        // Sin ninguna pasarela (DD-110): una solicitud de cobro hecha a mano o desde una pestaña vieja.
        ["ECO-PAGO-NO-DISPONIBLE"] = new(Areas.Tienda.Controllers.TextosDelPago.TituloSinPasarela, "Todavía no recibimos pagos en la tienda. No se te cobró nada y tu carrito se conservó."),
    };

    /// <summary>
    /// CAR-04 y CAR-27: el rechazo por falta de stock dice en pantalla la sucursal (y, en el checkout, el libro) y la cantidad máxima disponible en ella; sin ese dato queda el texto general.
    /// </summary>
    private static string ConElMaximo(ProblemaDto problema, string mensaje, DetalleDelStock? detalle)
    {
        var enElCarrito = problema.Code.EndsWith("-CARRITO-SIN-STOCK", StringComparison.Ordinal);
        if ((!enElCarrito && !problema.Code.EndsWith("-CHECKOUT-RESERVA-RECHAZADA", StringComparison.Ordinal))
            || problema.Errores.FirstOrDefault(e => e.Campo == "maximo") is not { } campo
            || !int.TryParse(campo.Clave, NumberStyles.None, CultureInfo.InvariantCulture, out var maximo))
        {
            return mensaje;
        }

        var donde = detalle?.Sucursal is { } nombre ? $"en {nombre}" : "en la sucursal elegida";
        var libro = detalle?.Libro is { } titulo ? $" de «{titulo}»" : string.Empty;
        var hay = maximo == 0
            ? $"Ya no quedan unidades{libro} {donde}."
            : $"El máximo disponible{libro} {donde} es de {maximo} {(maximo == 1 ? "unidad" : "unidades")}, contando las que ya estén en tu carrito.";
        return enElCarrito
            ? $"{hay} {(maximo == 0 ? "Elige otra sucursal" : "Elige una cantidad menor u otra sucursal")}. No se hizo ningún cobro."
            : $"{hay} Tu carrito se conservó y no se hizo ningún cobro.";
    }

    private static readonly Dictionary<string, string> CampoDelFormulario = new(StringComparer.Ordinal)
    {
        ["terminos"] = "terminosVersion",
        ["privacidad"] = "privacidadVersion",
        ["reembolsoDigital"] = "reembolsoDigitalVersion",
    };

    private static string TextoDelCampo(string campo, string clave) => (campo, clave) switch
    {
        ("telefono", _) => "Escribe un celular de 9 dígitos que empiece con 9.",
        ("nombre", "obligatorio") => "Escribe tu nombre completo.",
        ("direccion", "obligatorio") => "Escribe tu dirección de entrega.",
        ("region", "obligatorio") => "Escribe la región de entrega.",
        ("region", _) => "Esa región no está en nuestra lista de despacho.",
        ("sucursal", _) => "Elige una sucursal de la lista para agregar el libro impreso.",
        (_, "obligatorio") => "Completa este dato.",
        (_, "demasiado-largo") => "Es demasiado largo.",
        (_, "fuera-de-rango") => "Está fuera del rango permitido.",
        _ => "Revisa este dato.",
    };

    /// <summary>
    /// Aplica un fallo a la pantalla del checkout: el aviso del bloqueo con el texto de su código (o «Revisa los datos» en una validación), los errores junto a cada campo con el nombre del formulario y lo que la persona escribió (sin el token ni la verificación de persona).
    /// </summary>
    public static void AplicarElFallo(Areas.Tienda.Controllers.CheckoutVista modelo, ProblemaDto problema, ErrorDeAplicacion error, IReadOnlyDictionary<string, string> campos, DetalleDelStock? detalle = null)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        ArgumentNullException.ThrowIfNull(problema);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(campos);
        if (PorCodigo.TryGetValue(problema.Code, out var texto))
        {
            modelo.AlertaTitulo = texto.Titulo;
            modelo.AlertaMensaje = ConElMaximo(problema, texto.Mensaje, detalle);
        }
        else
        {
            modelo.AlertaTitulo = error.Categoria == CategoriaDeError.Validacion ? "Revisa los datos" : "No pudimos continuar con tu compra";
            modelo.AlertaMensaje = error.Categoria == CategoriaDeError.Validacion ? "Corrige los campos marcados e inténtalo de nuevo." : "No se hizo ningún cobro y tu carrito se conservó.";
        }

        if (error.Categoria == CategoriaDeError.Limite)
        {
            modelo.AlertaTitulo = "Demasiados intentos";
            modelo.AlertaMensaje = ProblemasDelCarrito.ReintentarEn(error) is { } hasta
                ? $"Podrás intentarlo de nuevo a las {Web.Formato.FormatoPeruano.Hora(hasta)} (hora de Lima). Tu carrito se conservó y no se hizo ningún cobro."
                : "Espera unos minutos e inténtalo de nuevo. Tu carrito se conservó y no se hizo ningún cobro.";
        }

        var porCampo = new Dictionary<string, string>(StringComparer.Ordinal);
        if (error.Categoria == CategoriaDeError.Validacion)
        {
            foreach (var e in error.Errores)
            {
                var nombre = CampoDelFormulario.GetValueOrDefault(e.Campo, e.Campo);
                porCampo.TryAdd(nombre, TextoDelCampo(nombre, e.Clave));
            }
        }

        modelo.Errores = porCampo;
        // Lista blanca de lo que la pantalla repinta: nada más de lo recibido vuelve a salir.
        modelo.Valores = campos
            .Where(c => c.Key is "nombre" or "telefono" or "direccion" or "region" or "consentimientoDeWhatsApp" or "terminosVersion" or "privacidadVersion" or "reembolsoDigitalVersion")
            .ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
    }

    /// <summary>Un navegador pide una página: <c>Accept</c> incluye <c>text/html</c>. Una prueba o una integración (sin ese tipo) recibe el problema en JSON.</summary>
    public static bool PrefiereHtml(HttpRequest solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return solicitud.GetTypedHeaders().Accept.Any(a => a.MediaType.HasValue && string.Equals(a.MediaType.Value, "text/html", StringComparison.OrdinalIgnoreCase));
    }

    public static RespuestaDePantalla Traducir(ProblemaDto problema, string rutaDeReintento, DetalleDelStock? detalle = null)
    {
        ArgumentNullException.ThrowIfNull(problema);
        if (problema.Errores.Any(e => e.Campo == ProblemasDelCarrito.CampoRegistrarSinSesion))
        {
            // REC-33: la hoja no se guardó con la sesión. La guía lleva a registrarla sin sesión; no dice por qué ni repite ningún dato.
            return new RespuestaDePantalla(
                new EstadoVista
                {
                    Estado = EstadoDePantalla.ServicioNoDisponible,
                    Titulo = "No pudimos registrar tu hoja con tu sesión",
                    Mensaje = "Tu hoja no se guardó. Puedes registrarla sin iniciar sesión: cierra tu sesión o abre una ventana privada, vuelve al Libro de Reclamaciones y envía el formulario de nuevo. Te daremos tu número igual.",
                    RutaDeReintento = rutaDeReintento,
                    CodigoDeSoporte = problema.Code,
                    IdentificadorDeSeguimiento = problema.TraceId,
                },
                problema.Status,
                null,
                true,
                []);
        }

        if (PorCodigo.TryGetValue(problema.Code, out var texto))
        {
            return new RespuestaDePantalla(
                new EstadoVista
                {
                    Estado = EstadoDePantalla.Error,
                    Titulo = texto.Titulo,
                    Mensaje = ConElMaximo(problema, texto.Mensaje, detalle),
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
    public static async Task<IActionResult> Escribir(Controller controlador, ContextoDePagina pagina, OpcionesDeErrores errores, ErrorDeAplicacion error, string rutaDeReintento, CancellationToken cancelacion, DetalleDelStock? detalle = null)
    {
        ArgumentNullException.ThrowIfNull(controlador);
        var contexto = controlador.HttpContext;
        var problema = ProblemasDelCarrito.Crear(contexto, errores, error);
        await DenegacionesHttp.Registrar(contexto, error.Categoria, error.Clave, problema.Code).ConfigureAwait(false);
        // Un rechazo por falta de stock desde la ficha nombra la sucursal elegida aunque todavía no haya una línea suya en el carrito (CAR-04).
        if (detalle is null && error.Errores.FirstOrDefault(e => e.Campo == "sucursal") is { } campo)
        {
            var conocidas = (await pagina.Completar(new ModeloBaseDePagina(), string.Empty, cancelacion: cancelacion).ConfigureAwait(false)).Sucursales;
            detalle = conocidas.FirstOrDefault(s => s.Codigo == campo.Clave) is { } sucursal ? new DetalleDelStock(sucursal.Nombre, null) : null;
        }

        var respuesta = Traducir(problema, rutaDeReintento, detalle);
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

/// <summary>Lo que acompaña a un rechazo por falta de stock en pantalla: el nombre de la sucursal y, si se sabe, el título del libro.</summary>
public sealed record DetalleDelStock(string? Sucursal, string? Libro);
