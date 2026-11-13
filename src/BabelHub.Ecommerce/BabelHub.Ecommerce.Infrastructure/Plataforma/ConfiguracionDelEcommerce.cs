using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.Extensions.Configuration;

namespace BabelHub.Ecommerce.Infrastructure.Plataforma;

/// <summary>Secretos propios del E-commerce, además de los comunes: Turnstile y la contraseña del Redis de contadores.</summary>
public sealed record ConfiguracionDelEcommerce(
    ConfiguracionDeSeguridad Seguridad,
    OpcionesDeTurnstile Turnstile,
    ValorSecreto ClaveDeRedisDeLimites)
{
    /// <summary>
    /// Además de lo común: <c>BABELHUB__Seguridad__Turnstile__Secreto</c>, <c>BABELHUB__Seguridad__Turnstile__Hostnames</c> (separados por
    /// comas) y <c>BABELHUB__Seguridad__ClaveDeRedisDeLimites</c>, todas obligatorias, con las mismas reglas y sin repetirse entre sí.
    /// </summary>
    public static ConfiguracionDelEcommerce Cargar(
        IConfiguration configuracion,
        IReadOnlyCollection<AlcanceDeServicio> alcancesRequeridos,
        IReadOnlyCollection<string> proveedoresRequeridos)
    {
        var lector = new LectorDeSecretos(configuracion);
        var comunes = ConfiguracionDeSeguridad.Leer(lector, alcancesRequeridos, proveedoresRequeridos);

        const string rutaTurnstile = "Seguridad:Turnstile:Secreto";
        const string rutaHostnames = "Seguridad:Turnstile:Hostnames";
        const string rutaRedis = "Seguridad:ClaveDeRedisDeLimites";

        var secretoDeTurnstile = lector.Obligatorio(rutaTurnstile);
        if (secretoDeTurnstile is not null)
        {
            lector.Comprobar(rutaTurnstile, secretoDeTurnstile);
        }

        var hostnames = lector.Obligatorio(rutaHostnames);
        var claveDeRedis = lector.Obligatorio(rutaRedis);
        if (claveDeRedis is not null)
        {
            lector.Comprobar(rutaRedis, claveDeRedis);
        }

        lector.ExigirValida();

        var turnstile = new OpcionesDeTurnstile(
            new ValorSecreto(secretoDeTurnstile!),
            hostnames!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return new ConfiguracionDelEcommerce(comunes!, turnstile, new ValorSecreto(claveDeRedis!));
    }
}
