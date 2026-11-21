#if DEBUG
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using Microsoft.AspNetCore.Routing;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>Lo que pide el recorrido: el rol (<c>cliente</c>, <c>encargado</c>, <c>logistica</c> o <c>administrador</c>) y, para un encargado, el código de su sucursal.</summary>
public sealed record SolicitudDeSiembraDto(string? Rol, string? Sucursal, string? Contrasena = null)
{
    /// <summary>Nunca imprime la contraseña elegida (AGENTS.md sección 5).</summary>
    public override string ToString() => $"SolicitudDeSiembraDto {{ Rol = {Rol}, Sucursal = {Sucursal} }}";
}

/// <summary>
/// El sembrador de sesiones de <b>solo desarrollo</b> en la Api: <c>POST /desarrollo/sesion</c> devuelve el <c>storageState</c> de Playwright con la cookie de sesión de una cuenta recién sembrada (ver
/// <see cref="SembradorDeSesionesDeDesarrollo"/>). <b>Cerrojos independientes</b>: (1) todo el archivo existe solo en una compilación <c>Debug</c> y la imagen de producción se publica en <c>Release</c> (y el Dockerfile falla si el
/// binario publicado lo contiene), así que el endpoint ni siquiera está en el binario; (2) solo se registra y se mapea si el entorno es <c>Development</c>; (3) solo atiende una conexión de la propia máquina (origen y destino
/// loopback) dirigida al nombre <c>localhost</c>, <c>127.0.0.1</c> o <c>[::1]</c> —un túnel o un proxy local reenvía con otro nombre de host, y un ataque de reasignación de DNS también—; (4) exige la cabecera
/// <c>X-Sembrador-De-Desarrollo</c> con una <b>clave aleatoria</b> que solo existe en el archivo de entorno local (<c>BABELHUB__Sembrador__Clave</c>); sin la clave el endpoint ni se mapea. Cualquier fallo de los cerrojos responde 404
/// sin leer el cuerpo. Ningún valor de configuración lo enciende en producción: sin el código no hay nada que encender.
/// </summary>
public static class SembradorDeSesionesEnLaApi
{
    public const string Ruta = "/desarrollo/sesion";

    public const string CabeceraDeAutorizacion = "X-Sembrador-De-Desarrollo";

    public const string ClaveDeConfiguracion = "Sembrador:Clave";

    /// <summary>La clave tiene al menos esta longitud: no se acepta una clave corta o de ejemplo.</summary>
    public const int LargoMinimoDeLaClave = 32;

    private static readonly string[] HostsLocales = ["localhost", "127.0.0.1", "[::1]", "::1"];

    public static IServiceCollection AgregarSembradorDeSesiones(this IServiceCollection servicios, bool esDesarrollo)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        if (esDesarrollo)
        {
            servicios.AddScoped<SembradorDeSesionesDeDesarrollo>();
        }

        return servicios;
    }

    public static IEndpointRouteBuilder MapearSembradorDeSesiones(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (app.Environment.IsDevelopment() && app.Configuration[ClaveDeConfiguracion] is { Length: >= LargoMinimoDeLaClave } clave)
        {
            var bytesDeLaClave = Encoding.UTF8.GetBytes(clave);
            Func<HttpContext, Task<IResult>> manejador = contexto => Sembrar(contexto, bytesDeLaClave);
            app.MapPost(Ruta, manejador).DisableAntiforgery();
        }

        return app;
    }

    private static async Task<IResult> Sembrar(HttpContext contexto, byte[] clave)
    {
        if (!EsUnaLlamadaLocalAutorizada(contexto.Request, contexto.Connection, clave))
        {
            return Results.NotFound();
        }

        SolicitudDeSiembraDto? cuerpo;
        try
        {
            cuerpo = await JsonSerializer.DeserializeAsync<SolicitudDeSiembraDto>(contexto.Request.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web), contexto.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "El cuerpo debe ser un JSON con rol y, para un encargado, sucursal." });
        }

        if (cuerpo?.Rol is null || !TryRol(cuerpo.Rol, out var rol))
        {
            return Results.BadRequest(new { error = "El rol es cliente, encargado, logistica o administrador." });
        }

        var host = contexto.RequestServices.GetRequiredService<OpcionesDelHostWeb>().Host;
        var sembrador = contexto.RequestServices.GetRequiredService<SembradorDeSesionesDeDesarrollo>();
        try
        {
            var sesion = await sembrador.Sembrar(rol, cuerpo.Sucursal, host, cuerpo.Contrasena, contexto.RequestAborted).ConfigureAwait(false);
            contexto.Response.Headers.CacheControl = "no-store";
            contexto.Response.Headers["X-Usuario-Id"] = sesion.UsuarioId.ToString("D");
            contexto.Response.Headers["X-Correo-Sembrado"] = sesion.Correo;
            return Results.Json(new
            {
                cookies = new[]
                {
                    new
                    {
                        name = CookieDeSesionWeb.Nombre(host),
                        value = sesion.ValorDeLaCookie,
                        domain = contexto.Request.Host.Host,
                        path = "/",
                        expires = -1,
                        httpOnly = true,
                        secure = true,
                        sameSite = "Lax",
                    },
                },
                origins = Array.Empty<object>(),
            });
        }
        catch (ArgumentException ex)
        {
            // Son textos fijos de la validación del rol, del host y de la sucursal (sin datos).
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // El detalle va al registro local, nunca a la respuesta: la excepción puede venir de la base o de la inyección de dependencias.
            contexto.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(SembradorDeSesionesEnLaApi)).LogWarning("El sembrador de sesiones falló: {Mensaje}", ex.Message);
            return Results.Problem("El sembrado falló; el detalle está en el registro del host.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>Origen y destino loopback, un nombre de host local, sin cabeceras de reenvío y la clave aleatoria (comparada en tiempo constante).</summary>
    private static bool EsUnaLlamadaLocalAutorizada(HttpRequest peticion, ConnectionInfo conexion, byte[] clave)
    {
        if (conexion.RemoteIpAddress is not { } origen || !IPAddress.IsLoopback(origen) || conexion.LocalIpAddress is not { } destino || !IPAddress.IsLoopback(destino))
        {
            return false;
        }

        if (Array.IndexOf(HostsLocales, peticion.Host.Host) < 0)
        {
            return false;
        }

        // Mejor esfuerzo: el borde de la Api ya quita estas cabeceras de una llamada que no viene de un proxy conocido; si llegaran, es un proxy.
        if (peticion.Headers.ContainsKey("Forwarded") || peticion.Headers.ContainsKey("Via") || peticion.Headers.Keys.Any(k => k.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var enviada = Encoding.UTF8.GetBytes(peticion.Headers[CabeceraDeAutorizacion].ToString());
        return enviada.Length == clave.Length && CryptographicOperations.FixedTimeEquals(enviada, clave);
    }

    private static bool TryRol(string texto, out RolDeCuenta rol)
    {
        (var ok, rol) = texto.Trim().ToLowerInvariant() switch
        {
            "cliente" => (true, RolDeCuenta.Cliente),
            "encargado" => (true, RolDeCuenta.EncargadoDeSucursal),
            "logistica" => (true, RolDeCuenta.Logistica),
            "administrador" => (true, RolDeCuenta.AdministradorCentral),
            _ => (false, default),
        };
        return ok;
    }
}
#endif
