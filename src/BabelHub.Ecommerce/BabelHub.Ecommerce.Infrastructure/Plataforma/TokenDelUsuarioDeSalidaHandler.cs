using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Infrastructure.Plataforma;

/// <summary>
/// El manejador de salida que pone el <b>token del usuario de la sesión</b> en toda operación de usuario hacia otro servicio (<c>web-base</c> T14, D13): es el <b>único</b> consumidor de
/// <see cref="ITokenDelUsuarioDeLaSesion"/> en la Infraestructura. Corre antes que <see cref="CredencialesDeSalidaHandler"/> (que lo coloca en <c>Authorization</c>) y <b>reemplaza</b> el token
/// que un llamador hubiera puesto en las opciones de la solicitud: ningún módulo elige en nombre de quién se llama. Sin sesión o sin credencial utilizable la operación no sale. No registra el token.
/// </summary>
/// <remarks>
/// Es un archivo propio (no parte de <c>ClientesHttpDeServicio.cs</c>, que es una copia idéntica en los dos servicios, <c>CopiasDeAdaptadoresTests</c>) y se registra con
/// <c>ConfigureHttpClientDefaults</c>, de modo que va primero en la cadena de todo cliente HTTP; solo actúa sobre las llamadas que declaran el alcance de operación de usuario.
/// </remarks>
public sealed class TokenDelUsuarioDeSalidaHandler(ITokenDelUsuarioDeLaSesion token) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Options.TryGetValue(OpcionesDeLaSolicitudDeServicio.Alcance, out var alcance) && alcance == AlcanceDeServicio.OperacionDeUsuario)
        {
            var valor = await token.Actual(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Una operación de usuario necesita la sesión de la persona en cuyo nombre se llama.");
            request.Options.Set(OpcionesDeLaSolicitudDeServicio.TokenDeUsuario, valor.Revelar());
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
