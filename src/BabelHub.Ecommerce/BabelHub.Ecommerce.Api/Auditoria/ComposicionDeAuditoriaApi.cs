using BabelHub.Ecommerce.Infrastructure.Auditoria;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;

namespace BabelHub.Ecommerce.Api.Auditoria;

public static class ComposicionDeAuditoriaApi
{
    /// <summary>
    /// Registra el cliente HTTP con nombre propio hacia la auditoría del Inventario (T28; AUD-17, AUD-18, AUD-38): <b>sin reintentos</b> (la consulta acepta 9 segundos y corta a los 10; el cortacircuitos es solo suyo y no lo comparte con las
    /// demás llamadas al Inventario), <b>sin redirecciones</b> (los manejadores de salida ponen la credencial de servicio y el token del usuario: no deben viajar a otro destino), <b>sin los registros de petición del cliente</b>, que
    /// escribirían la consulta (lleva el usuario, el origen y el texto buscado), y con el manejador del token de la sesión (<see cref="TokenDelUsuarioDeSalidaHandler"/>, el único consumidor de <c>ITokenDelUsuarioDeLaSesion</c>) primero en
    /// su cadena. El anfitrión además registra <c>ITokenDelUsuarioDeLaSesion</c> (<c>AgregarTokenDelUsuarioDeLaSesion</c>) y las credenciales de salida.
    /// </summary>
    public static IServiceCollection AgregarClienteDeAuditoriaDelInventario(this IServiceCollection servicios, Uri direccion, OpcionesDelClienteDeServicio? opciones = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(direccion);
        var cliente = servicios.AgregarClienteDeServicio(
            HttpClienteDeAuditoriaDelInventario.NombreDelCliente,
            direccion,
            opciones ?? new OpcionesDelClienteDeServicio { Espera = HttpClienteDeAuditoriaDelInventario.Espera, ReintentosMaximos = 0 });
        cliente.RemoveAllLoggers();
        cliente.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        servicios.TryAddTransient<TokenDelUsuarioDeSalidaHandler>();
        // El manejador del token va siempre primero de la cadena (antes de la resiliencia y de las credenciales de servicio).
        cliente.ConfigureAdditionalHttpMessageHandlers((manejadores, proveedor) => manejadores.Insert(0, proveedor.GetRequiredService<TokenDelUsuarioDeSalidaHandler>()));
        return servicios;
    }
}
