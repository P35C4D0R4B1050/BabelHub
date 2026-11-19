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

namespace BabelHub.Ecommerce.Api.Envios;

/// <summary>
/// Los fallos de Envíos en <b>pantalla</b> (T18; ENV-04, ENV-10, ENV-12, WEB-07): cuando quien pide es un navegador (<c>Accept: text/html</c>) el error se pinta con el componente <c>bh-estado</c> con el texto de su código
/// y, en lo reintentable, el enlace para volver a la lista; cuando no lo es sigue saliendo el <c>application/problem+json</c>. <b>Nunca muestra el contacto del destinatario</b>: el contacto incompleto nombra solo el campo
/// que falta o que el contacto está anonimizado. Ni el texto del operador ni el del Inventario llegan a la pantalla.
/// </summary>
public static class PantallasDeEnvios
{
    private sealed record Texto(string Titulo, string Mensaje);

    private static readonly Dictionary<string, Texto> PorCodigo = new(StringComparer.Ordinal)
    {
        ["ECO-ENV-ESTADO-INVALIDO"] = new("El envío ya cambió de estado", "Otra persona lo actualizó mientras mirabas la lista. Vuelve a la lista para ver su estado actual."),
        ["ECO-ENV-GUIA-EN-CURSO"] = new("La guía ya se está pidiendo", "Otra solicitud está pidiendo la guía de este envío. Espera unos segundos y revisa la lista."),
        ["ECO-ENV-OPERADOR-NO-DISPONIBLE"] = new("El operador logístico no respondió", "No se generó la guía y el envío sigue sin guía. Puedes volver a intentarlo en unos minutos."),
        ["ECO-ENV-INVENTARIO-RECHAZO"] = new("El Inventario rechazó el descuento", "El envío sigue con su guía y no se descontó ningún libro. Revisa el stock de la sucursal de origen e inténtalo de nuevo."),
        ["ECO-NO-ENCONTRADO"] = new("No encontramos ese envío", "Puede que ya no exista. Vuelve a la lista de envíos."),
    };

    /// <summary>El texto del contacto incompleto: el campo que falta o el aviso de que el contacto se anonimizó. Nunca un valor (ENV-17).</summary>
    private static Texto DelContactoIncompleto(ProblemaDto problema)
    {
        var campo = problema.Errores.FirstOrDefault()?.Campo;
        return campo switch
        {
            "anonimizado" => new Texto("El contacto del pedido se anonimizó", "La cuenta del cliente se canceló y ya no hay datos de entrega. Este envío no puede recibir guía."),
            "nombre" => new Texto("Falta el nombre del destinatario", "El pedido no tiene el nombre de quien recibe. No se pidió la guía al operador. Corrige el pedido y vuelve a Envíos desde el menú."),
            "direccion" => new Texto("Falta la dirección de entrega", "El pedido no tiene una dirección de entrega válida. No se pidió la guía al operador. Corrige el pedido y vuelve a Envíos desde el menú."),
            "telefono" => new Texto("Falta el teléfono del destinatario", "El pedido no tiene un teléfono de contacto válido. No se pidió la guía al operador. Corrige el pedido y vuelve a Envíos desde el menú."),
            _ => new Texto("Faltan datos de entrega", "El pedido no tiene todos los datos de entrega. No se pidió la guía al operador. Corrige el pedido y vuelve a Envíos desde el menú."),
        };
    }

    /// <summary>Un navegador pide una página: <c>Accept</c> incluye <c>text/html</c>. Una prueba o una integración (sin ese tipo) recibe el problema en JSON.</summary>
    public static bool PrefiereHtml(HttpRequest solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return solicitud.GetTypedHeaders().Accept.Any(a => a.MediaType.HasValue && string.Equals(a.MediaType.Value, "text/html", StringComparison.OrdinalIgnoreCase));
    }

    public static RespuestaDePantalla Traducir(ProblemaDto problema, ErrorDeAplicacion error, string rutaDeReintento)
    {
        ArgumentNullException.ThrowIfNull(problema);
        ArgumentNullException.ThrowIfNull(error);
        var texto = problema.Code == "ECO-ENV-CONTACTO-INCOMPLETO" ? DelContactoIncompleto(problema) : PorCodigo.GetValueOrDefault(problema.Code);
        if (texto is not null)
        {
            // El contacto incompleto no se arregla reintentando: se corrige en el pedido. Lo demás vuelve a la lista.
            var reintentable = problema.Code is not "ECO-ENV-CONTACTO-INCOMPLETO";
            return new RespuestaDePantalla(
                new EstadoVista
                {
                    Estado = EstadoDePantalla.Error,
                    Titulo = texto.Titulo,
                    Mensaje = texto.Mensaje,
                    RutaDeReintento = reintentable ? rutaDeReintento : null,
                    CodigoDeSoporte = problema.Code,
                    IdentificadorDeSeguimiento = problema.TraceId,
                },
                problema.Status,
                null,
                true,
                []);
        }

        var respuesta = TraductorDeErrores.Traducir(problema, rutaDeReintento: rutaDeReintento, parteAfectada: "No podemos operar los envíos ahora. No se cambió nada.");
        return error.Categoria == CategoriaDeError.Limite
            ? respuesta with { Vista = respuesta.Vista with { EsperaSugeridaSegundos = ProblemasDeEnvios.EsperaEnSegundos(error) } }
            : respuesta;
    }

    /// <summary>
    /// Escribe el fallo como pantalla completa (el estado dentro del layout) con el código HTTP del módulo (<c>200</c> con htmx, que no reemplaza un fragmento con un 4xx), <c>Retry-After</c> en un límite y <c>no-store</c>.
    /// </summary>
    public static async Task<IActionResult> Escribir(Controller controlador, ContextoDePagina pagina, OpcionesDeErrores errores, ErrorDeAplicacion error, string rutaDeReintento, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(controlador);
        var contexto = controlador.HttpContext;
        var problema = ProblemasDeEnvios.Crear(contexto, errores, error);
        await DenegacionesHttp.Registrar(contexto, error.Categoria, error.Clave, problema.Code).ConfigureAwait(false);
        var respuesta = Traducir(problema, error, rutaDeReintento);
        if (respuesta.RedirigirA is { } destino)
        {
            return controlador.Request.EsHtmx() ? new HxRedirectResult(destino) : new LocalRedirectResult(destino);
        }

        var modelo = await pagina.Completar(new PantallaDeEstadoVista { Estado = respuesta.Vista }, respuesta.Vista.Titulo, AreaDeLaPagina.Sucursal, cancelacion).ConfigureAwait(false);
        if (error.Categoria == CategoriaDeError.Limite)
        {
            controlador.Response.Headers["Retry-After"] = ProblemasDeEnvios.EsperaEnSegundos(error).ToString(CultureInfo.InvariantCulture);
        }

        controlador.Response.StatusCode = controlador.Request.EsHtmx() ? StatusCodes.Status200OK : respuesta.EstadoHttp;
        controlador.Response.Headers.CacheControl = "no-store";
        return controlador.View("~/Views/Estados/Pantalla.cshtml", modelo);
    }
}
