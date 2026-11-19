using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace BabelHub.Ecommerce.Api.Pagos;

/// <summary>
/// La entrada de avisos de la pasarela (T29; PAG-19, AC-PAG-16): <c>POST /api/ecommerce/v1/integraciones/pasarela/avisos</c>, <b>firma de proveedor y no credencial de servicio</b>. Presentación pura: lee el cuerpo con un <b>tope de tamaño</b> (se rechaza antes de leerlo si
/// declara más, y se corta si lo supera), toma la marca de tiempo y la firma de sus cabeceras y llama a <see cref="IAvisosDeLaPasarela"/>, que verifica, deduplica y solo adelanta la consulta. <b>Está deshabilitada</b> (no se mapea y responde 404) mientras no haya un
/// esquema de firma verificable (<c>habilitada</c>): sin él un aviso no se puede autenticar. Un duplicado responde 200; una firma o marca inválida, un 401 uniforme sin decir por qué (la Aplicación deja el evento de seguridad); un fallo al aplicar, un 503 para que la
/// pasarela reintente. El actor es la integración <c>culqi</c>. La ráfaga la acota el límite general de origen sin sesión del pipeline. Nunca repite el cuerpo, la firma ni la marca en una respuesta.
/// </summary>
public static class AvisosDeLaPasarelaEnLaApi
{
    public const string Ruta = "/api/ecommerce/v1/integraciones/pasarela/avisos";

    public const string CabeceraDeLaMarca = "X-Marca-De-Tiempo";

    public const string CabeceraDeLaFirma = "X-Firma";

    /// <summary>16 KiB: un aviso de cargo ocupa unos cientos de bytes.</summary>
    public const int TopeDelCuerpo = 16 * 1024;

    public static IEndpointRouteBuilder MapearAvisosDeLaPasarela(this IEndpointRouteBuilder rutas, bool habilitada)
    {
        ArgumentNullException.ThrowIfNull(rutas);
        if (!habilitada)
        {
            return rutas;
        }

        rutas.MapPost(Ruta, Recibir).DisableAntiforgery();
        return rutas;
    }

    private static async Task Recibir(HttpContext contexto, IAvisosDeLaPasarela avisos, IEstablecedorDeActor actor, OpcionesDeErrores errores)
    {
        var cancelacion = contexto.RequestAborted;
        if (contexto.Request.ContentLength is > TopeDelCuerpo)
        {
            await Fallar(contexto, errores, new ErrorDeAplicacion(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo("cuerpo", "demasiado-grande")])).ConfigureAwait(false);
            return;
        }

        var marca = contexto.Request.Headers[CabeceraDeLaMarca].ToString();
        var firma = contexto.Request.Headers[CabeceraDeLaFirma].ToString();
        if (string.IsNullOrWhiteSpace(marca) || string.IsNullOrWhiteSpace(firma) || marca.Length > 40 || firma.Length > 512)
        {
            // Sin marca o sin firma no se puede autenticar: el mismo 401 uniforme que una firma falsa.
            await Fallar(contexto, errores, new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, "no-autenticado")).ConfigureAwait(false);
            return;
        }

        var cuerpo = await LeerElCuerpo(contexto.Request.Body, cancelacion).ConfigureAwait(false);
        if (cuerpo is null)
        {
            await Fallar(contexto, errores, new ErrorDeAplicacion(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo("cuerpo", "demasiado-grande")])).ConfigureAwait(false);
            return;
        }

        actor.Establecer(ActorActual.DeIntegracion("culqi"));
        try
        {
            var resultado = await avisos.Recibir(new AvisoDeLaPasarelaEntrada(cuerpo, marca, firma), cancelacion).ConfigureAwait(false);
            if (!resultado.EsExito)
            {
                await Fallar(contexto, errores, resultado.Error!).ConfigureAwait(false);
                return;
            }
        }
        finally
        {
            actor.Limpiar();
        }

        contexto.Response.StatusCode = StatusCodes.Status200OK;
        contexto.Response.Headers.CacheControl = "no-store";
    }

    /// <summary>Lee a lo más <see cref="TopeDelCuerpo"/> bytes; <see langword="null"/> si el cuerpo lo supera (una cabecera de longitud falsa o un cuerpo por trozos).</summary>
    private static async Task<byte[]?> LeerElCuerpo(Stream flujo, CancellationToken cancelacion)
    {
        using var memoria = new MemoryStream();
        var bufer = new byte[4096];
        int leidos;
        while ((leidos = await flujo.ReadAsync(bufer, cancelacion).ConfigureAwait(false)) > 0)
        {
            if (memoria.Length + leidos > TopeDelCuerpo)
            {
                return null;
            }

            memoria.Write(bufer, 0, leidos);
        }

        return memoria.ToArray();
    }

    private static Task Fallar(HttpContext contexto, OpcionesDeErrores errores, ErrorDeAplicacion error) => ProblemasDelCarrito.Escribir(contexto, errores, error);
}
