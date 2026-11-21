using System.Text.Json;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Contratos.Ecommerce;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Pedidos;

/// <summary>
/// La entrada de los estados de traslado del Inventario a Pedidos (T32; PED-19, TRA-11, AC-PED-03): <c>POST /api/ecommerce/v1/integraciones/inventario/traslados</c>, operación <c>ecommerce.traslados.informar-estado</c>
/// con alcance <c>solicitar-aviso</c> (la verifica el middleware de la superficie de servicio: sin él, <c>ECO-NO-AUTENTICADO</c>). <b>Presentación pura</b>: tope de 4 KiB, cuerpo JSON estricto, traduce el DTO del contrato
/// a <see cref="EstadoDeTrasladoRecibido"/> y llama a <see cref="IReceptorDeEstadosDeTraslado"/>, que cruza, deduplica y aplica. Responde 204 (también si el mismo evento se repite). El actor es la integración del Inventario
/// y nunca sale de una cabecera.
/// </summary>
public static class EndpointsDePedidos
{
    public const string RutaDeLosEstadosDeTraslado = "/api/ecommerce/v1/integraciones/inventario/traslados";

    public const string OperacionDeLosEstadosDeTraslado = "ecommerce.traslados.informar-estado";

    /// <summary>Un estado de traslado (un evento, tres identificadores, una cantidad y dos fechas) cabe holgado en 4 KiB.</summary>
    public const int TopeDelEstadoDeTraslado = 4 * 1024;

    // Esquema estricto: nombres con su mayúscula exacta, números como números y ningún campo que el contrato no declare.
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static IEndpointRouteBuilder MapearPedidos(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);
        rutas.MapPost(RutaDeLosEstadosDeTraslado, EstadoDeTraslado)
            .WithMetadata(new OperacionDeServicioAttribute(OperacionDeLosEstadosDeTraslado), new RequestSizeLimitAttribute(TopeDelEstadoDeTraslado))
            .DisableAntiforgery();
        return rutas;
    }

    /// <summary>Solo <c>application/json</c> exacto (con <c>charset</c> utf-8 o sin él): <c>application/jsonp</c> o <c>application/json-x</c> no son JSON de esta entrada.</summary>
    private static bool EsJsonEstricto(string? tipo) =>
        Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(tipo, out var medio)
        && string.Equals(medio.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase)
        && (medio.Charset.Length == 0 || string.Equals(medio.Charset.Value, "utf-8", StringComparison.OrdinalIgnoreCase));

    private static async Task<IResult> EstadoDeTraslado(HttpContext contexto, OpcionesDeErrores errores, IReceptorDeEstadosDeTraslado receptor, IEstablecedorDeActor actor)
    {
        if (!EsJsonEstricto(contexto.Request.ContentType))
        {
            return await Falla(contexto, errores, Validacion("cuerpo", "invalido")).ConfigureAwait(false);
        }

        var cuerpo = await Leer(contexto).ConfigureAwait(false);
        if (cuerpo is null || cuerpo.EventoId is null || cuerpo.Estado is null || !EstadoConocido(cuerpo.Estado, out var estado))
        {
            return await Falla(contexto, errores, Validacion("cuerpo", "invalido")).ConfigureAwait(false);
        }

        actor.Establecer(ActorActual.DeIntegracion(IntegracionesDeServicio.Inventario));
        try
        {
            var resultado = await receptor.Recibir(
                new EstadoDeTrasladoRecibido(cuerpo.EventoId, cuerpo.TrasladoId, cuerpo.PedidoId, cuerpo.EdicionId, cuerpo.Cantidad, estado, cuerpo.FechaEstimadaDeLlegada),
                contexto.RequestAborted).ConfigureAwait(false);
            if (!resultado.EsExito)
            {
                return await Falla(contexto, errores, resultado.Error!).ConfigureAwait(false);
            }
        }
        finally
        {
            actor.Limpiar();
        }

        contexto.Response.Headers.CacheControl = "no-store";
        return Results.NoContent();
    }

    private static bool EstadoConocido(string texto, out EstadoDelTrasladoInformado estado)
    {
        switch (texto)
        {
            case EstadosDeTrasladoDePedido.EnTransito:
                estado = EstadoDelTrasladoInformado.EnTransito;
                return true;
            case EstadosDeTrasladoDePedido.Recibido:
                estado = EstadoDelTrasladoInformado.Recibido;
                return true;
            case EstadosDeTrasladoDePedido.Cancelado:
                estado = EstadoDelTrasladoInformado.Cancelado;
                return true;
            default:
                estado = default;
                return false;
        }
    }

    private static async Task<EstadoDeTrasladoDePedidoDto?> Leer(HttpContext contexto)
    {
        if (contexto.Request.ContentLength is > TopeDelEstadoDeTraslado)
        {
            return null;
        }

        var bytes = await LeerElCuerpo(contexto.Request.Body, TopeDelEstadoDeTraslado, contexto.RequestAborted).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<EstadoDeTrasladoDePedidoDto>(bytes, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Lee a lo más <paramref name="tope"/> bytes; <see langword="null"/> si el cuerpo lo supera.</summary>
    private static async Task<byte[]?> LeerElCuerpo(Stream flujo, int tope, CancellationToken cancelacion)
    {
        using var memoria = new MemoryStream();
        var bufer = new byte[2048];
        int leidos;
        while ((leidos = await flujo.ReadAsync(bufer, cancelacion).ConfigureAwait(false)) > 0)
        {
            if (memoria.Length + leidos > tope)
            {
                return null;
            }

            memoria.Write(bufer, 0, leidos);
        }

        return memoria.ToArray();
    }

    private static ErrorDeAplicacion Validacion(string campo, string clave) => new(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo(campo, clave)]);

    private static async Task<IResult> Falla(HttpContext contexto, OpcionesDeErrores errores, ErrorDeAplicacion error)
    {
        await ProblemasDePedidos.Escribir(contexto, errores, error).ConfigureAwait(false);
        return Results.Empty;
    }
}
