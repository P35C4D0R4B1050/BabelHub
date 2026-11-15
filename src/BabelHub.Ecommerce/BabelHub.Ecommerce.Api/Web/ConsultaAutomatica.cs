using Microsoft.AspNetCore.Routing;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>
/// Declara que una acción es una <b>consulta automática</b> (un fragmento que htmx pide solo, con <c>hx-trigger="every …"</c>): solo <c>GET</c>, sin efectos, y
/// nunca renueva la inactividad de la sesión ni revive una vencida (WEB-27, D04). Es el servidor quien lo declara: una cabecera del cliente no puede hacerse pasar
/// por una consulta automática ni por una acción de la persona.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ConsultaAutomaticaAttribute : Attribute;

public static class ConsultasAutomaticas
{
    /// <summary>
    /// Falla si un endpoint declarado <c>[ConsultaAutomatica]</c> admite algo más que <c>GET</c> (o no declara sus métodos): una consulta que cambia datos no puede
    /// ser «automática». Se llama una vez, con los endpoints ya mapeados.
    /// </summary>
    public static void Validar(IEnumerable<Endpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        foreach (var endpoint in endpoints)
        {
            if (endpoint.Metadata.GetMetadata<ConsultaAutomaticaAttribute>() is null)
            {
                continue;
            }

            var metodos = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            if (metodos is null || metodos.Count != 1 || !string.Equals(metodos.Single(), HttpMethods.Get, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"El endpoint «{endpoint.DisplayName}» es [ConsultaAutomatica] y debe admitir únicamente GET.");
            }
        }
    }

    public static bool EsConsultaAutomatica(this HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        return contexto.GetEndpoint()?.Metadata.GetMetadata<ConsultaAutomaticaAttribute>() is not null
            && HttpMethods.IsGet(contexto.Request.Method);
    }
}
