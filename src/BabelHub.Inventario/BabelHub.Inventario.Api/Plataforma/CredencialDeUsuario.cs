using System.Security.Claims;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Infrastructure.Plataforma;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

namespace BabelHub.Inventario.Api.Plataforma;

/// <summary>
/// La credencial del usuario en el Inventario (identidad-seguridad T41, COMP-1; IDE-10, IDE-61, D10): si la solicitud trae <c>Authorization: Bearer …</c>, el verificador de credenciales (firma ES256 con la clave publicada, audiencia
/// del Inventario, sin contexto de lectura, copia de claves de menos de 5 minutos) decide quién es; <b>el actor sale de la credencial verificada y de nada más</b> (nunca una cabecera). Una credencial inválida, vencida o sin claves vigentes deja
/// la solicitud sin actor, y el contexto de usuario de la operación la rechaza con el 401 uniforme. Las llamadas de servicio sin token de usuario siguen su camino.
/// </summary>
public sealed class CredencialDeUsuarioMiddleware(RequestDelegate siguiente)
{
    private const string Esquema = "Bearer ";

    public async Task InvokeAsync(HttpContext contexto, IVerificadorDeCredenciales verificador, IEstablecedorDeActor actor, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        var cabeceras = contexto.Request.Headers.Authorization;
        if (cabeceras.Count == 1 && cabeceras[0] is { } valor && valor.StartsWith(Esquema, StringComparison.Ordinal))
        {
            var leida = verificador.Verificar(valor[Esquema.Length..].Trim(), reloj.Ahora);
            if (leida is { EsValida: true, UsuarioId: { } usuario, Rol: { } rol })
            {
                actor.Establecer(ActorActual.DeUsuario(usuario, rol, leida.SucursalId));

                // Los límites generales (SEG-05) cuentan al usuario por su identificador: solo lo que salió de la credencial verificada entra como identidad autenticada.
                contexto.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, usuario.ToString("D"))], "CredencialDeUsuario"));
            }
        }

        // El token del usuario no sigue por el pipeline: ningún módulo lo necesita ni debe poder registrarlo.
        contexto.Request.Headers.Remove(HeaderNames.Authorization);
        await siguiente(contexto).ConfigureAwait(false);
    }
}

/// <summary>Corre <see cref="TrabajoDeRenovacionDeClaves"/> mientras el servicio vive (solo en el modo normal): renueva la clave y las revocaciones cada minuto, mucho antes de los 5 minutos de IDE-61.</summary>
internal sealed class ServicioDeRenovacionDeClaves(TrabajoDeRenovacionDeClaves trabajo) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken cancelacion) => trabajo.Correr(cancelacion);
}

/// <summary><see cref="IPublicadorDeAlertasTecnicas"/> para el trabajo de renovación, que vive más que una solicitud: cada alerta se publica en su propio alcance y nunca interrumpe.</summary>
internal sealed class AlertasTecnicasPorCorrida(IServiceScopeFactory fabrica) : IPublicadorDeAlertasTecnicas
{
    public async Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
    {
        using var alcance = fabrica.CreateScope();
        await alcance.ServiceProvider.GetRequiredService<IPublicadorDeAlertasTecnicas>().Publicar(alerta, cancelacion).ConfigureAwait(false);
    }
}

public static class CredencialDeUsuarioExtensiones
{
    public static IServiceCollection AgregarVerificacionDeCredencialesDeUsuario(this IServiceCollection servicios, OpcionesDeClavesPublicadas direcciones, bool soloEnModoNormal)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(direcciones);
        servicios.AddSingleton(direcciones);

        // La réplica de gestión de la propia VM: sin redirecciones ni proxy (la respuesta debe venir de la dirección configurada).
        servicios.AddHttpClient<IObtenedorDeClavesPublicadas, HttpObtenedorDeClavesPublicadas>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(5) });
        servicios.AddSingleton(sp => new CacheDeClavesDelInventario(sp.GetRequiredService<IObtenedorDeClavesPublicadas>()));
        servicios.AddSingleton<IFuenteDeClavesDeVerificacion>(sp => sp.GetRequiredService<CacheDeClavesDelInventario>());
        servicios.AddSingleton<IVerificadorDeCredenciales>(sp => new VerificadorDeCredencialesDelInventario(sp.GetRequiredService<IFuenteDeClavesDeVerificacion>()));

        if (soloEnModoNormal)
        {
            servicios.AddSingleton(sp => new TrabajoDeRenovacionDeClaves(
                sp.GetRequiredService<CacheDeClavesDelInventario>(),
                new AlertasTecnicasPorCorrida(sp.GetRequiredService<IServiceScopeFactory>()),
                sp.GetRequiredService<IEstablecedorDeActor>(),
                sp.GetRequiredService<IReloj>()));
            servicios.AddHostedService<ServicioDeRenovacionDeClaves>();
        }

        return servicios;
    }

    public static IApplicationBuilder UsarCredencialDeUsuario(this IApplicationBuilder app) => app.UseMiddleware<CredencialDeUsuarioMiddleware>();
}
