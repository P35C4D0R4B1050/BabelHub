using System.Text.Json;
using BabelHub.Ecommerce.Api.Notificaciones;
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

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public static IEndpointRouteBuilder MapearPedidos(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);
        rutas.MapPost(RutaDeLosEstadosDeTraslado, EstadoDeTraslado)
            .WithMetadata(new OperacionDeServicioAttribute(OperacionDeLosEstadosDeTraslado), new RequestSizeLimitAttribute(TopeDelEstadoDeTraslado))
            .DisableAntiforgery();
        return rutas;
    }

    private static async Task<IResult> EstadoDeTraslado(HttpContext contexto, OpcionesDeErrores errores, IReceptorDeEstadosDeTraslado receptor, IEstablecedorDeActor actor)
    {
        var cuerpo = await Leer(contexto).ConfigureAwait(false);
        if (cuerpo is null || cuerpo.EventoId is null || cuerpo.Estado is null || !EstadoConocido(cuerpo.Estado, out var estado))
        {
            return await Falla(contexto, errores, EstadosDeEntregaDeN8nEnLaApi.Validacion("cuerpo", "invalido")).ConfigureAwait(false);
        }

        actor.Establecer(ActorActual.DeIntegracion("inventario"));
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

        var bytes = await EstadosDeEntregaDeN8nEnLaApi.LeerElCuerpo(contexto.Request.Body, TopeDelEstadoDeTraslado, contexto.RequestAborted).ConfigureAwait(false);
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

    private static async Task<IResult> Falla(HttpContext contexto, OpcionesDeErrores errores, ErrorDeAplicacion error)
    {
        await ProblemasDePedidos.Escribir(contexto, errores, error).ConfigureAwait(false);
        return Results.Empty;
    }
}
