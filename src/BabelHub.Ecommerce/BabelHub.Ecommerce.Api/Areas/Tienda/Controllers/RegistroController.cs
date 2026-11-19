using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Controllers;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

public enum PantallaDelRegistro
{
    Formulario,
    Verificar,
    Recuperar,
    ConfirmarRecuperacion,
    Listo,
}

/// <summary>Lo que dibujan el registro, la verificación del correo y la recuperación: el paso, el mensaje de lo que falló y, en el registro, las versiones vigentes de los textos que la persona acepta. El correo que escribió solo vuelve en el campo de la pantalla siguiente, nunca en la URL.</summary>
public sealed class RegistroVista : ModeloBaseDePagina
{
    public PantallaDelRegistro Pantalla { get; set; }

    public string? Mensaje { get; set; }

    public string? Correo { get; set; }

    public VersionParaAceptar? Textos { get; set; }
}

/// <summary>
/// El registro de una cuenta de cliente, la verificación de su correo y la recuperación del acceso (T28; WEB-20, SEG-31): <c>/registro</c> (con la verificación de persona y la aceptación de la versión vigente de los términos y la privacidad), <c>/verificacion</c> (el código que llegó al correo, que además
/// abre la sesión) y <c>/recuperacion</c> (pedir el código y fijar una contraseña nueva). <b>Presentación pura</b>: toda regla es de Identidad (<see cref="IRegistroDeCuentas"/>, <see cref="IRecuperacionDeAcceso"/>). Cada respuesta es la misma exista o no la cuenta (IDE-56, IDE-65). Solo en la tienda.
/// </summary>
[Area("Tienda")]
[SinRetorno]
public sealed class RegistroController(
    IRegistroDeCuentas registro,
    IRecuperacionDeAcceso recuperacion,
    IAceptacionDeTextos textos,
    IExigenciaDeVerificacionDePersona verificacion,
    CierreDelIngresoWeb cierre,
    ContextoDePagina pagina) : Controller
{
    private static readonly string[] CamposDelRegistro = ["nombre", "correo", "contrasena", "versionDeTerminos", "versionDePrivacidad", "acepto", "mayorDeEdad", VerificacionDePersonaWeb.CampoDelToken];
    private static readonly string[] CamposDeLaVerificacion = ["correo", "codigo"];
    private static readonly string[] CamposDelCorreo = ["correo"];
    private static readonly string[] CamposDeLaRecuperacion = ["correo", "codigo", "contrasenaNueva", "confirmacion"];

    private const string TextoNoDisponible = "No podemos atenderte ahora. Inténtalo en unos minutos.";
    private const string TextoLimite = "Hiciste demasiados intentos. Espera unos minutos y vuelve a probar.";
    private const string TextoCodigoInvalido = "El código no es válido o venció. Pide uno nuevo.";

    // ---------- Registro ----------

    [HttpGet("/registro")]
    [VerificacionDePersona(AccionProtegida.Registro)]
    public async Task<IActionResult> Registro(CancellationToken cancelacion)
    {
        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext) is { } consulta)
        {
            return await Falla(consulta, cancelacion);
        }

        return await Formulario(null, StatusCodes.Status200OK, cancelacion);
    }

    [HttpPost("/registro")]
    [VerificacionDePersona(AccionProtegida.Registro)]
    public async Task<IActionResult> Registrar(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDelRegistro, cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        var token = campos.GetValueOrDefault(VerificacionDePersonaWeb.CampoDelToken) ?? Request.Headers["X-Verificacion-De-Persona"].FirstOrDefault();
        switch (await verificacion.Exigir(AccionProtegida.Registro, token, cancelacion))
        {
            case ResultadoDeExigencia.Requerida:
            case ResultadoDeExigencia.Fallida:
                return await Formulario("No pudimos comprobar que eres una persona. Inténtalo de nuevo.", StatusCodes.Status403Forbidden, cancelacion);
            case ResultadoDeExigencia.NoDisponible:
                return await Formulario(TextoNoDisponible, StatusCodes.Status503ServiceUnavailable, cancelacion);
        }

        if (!campos.TryGetValue("nombre", out var nombre) || !campos.TryGetValue("correo", out var correo) || !campos.TryGetValue("contrasena", out var contrasena)
            || nombre.Length is 0 or > 120 || correo.Length is 0 or > 254 || contrasena.Length is 0 or > 1024)
        {
            return await Formulario("Escribe tu nombre, tu correo y una contraseña.", StatusCodes.Status400BadRequest, cancelacion);
        }

        // La persona acepta la versión que vio (los términos y la privacidad) y declara su mayoría de edad: sin esas dos marcas no se envía nada a Identidad.
        if (!campos.ContainsKey("acepto") || !campos.ContainsKey("mayorDeEdad"))
        {
            return await Formulario("Para crear tu cuenta debes aceptar los términos y la política de privacidad y declarar que eres mayor de edad.", StatusCodes.Status400BadRequest, cancelacion);
        }

        var resultado = await Llamar(() => registro.Registrar(new SolicitudDeAltaDeCuenta(nombre, correo, new ValorSecreto(contrasena), campos.GetValueOrDefault("versionDeTerminos"), campos.GetValueOrDefault("versionDePrivacidad"), DeclaraSerMayorDeEdad: true), cancelacion), cancelacion);
        if (!resultado.EsExito)
        {
            return await Formulario(TextoDelError(resultado.Error!), CodigoHttp(resultado.Error!), cancelacion);
        }

        // La misma respuesta exista o no la cuenta (IDE-56): se pide el código del correo.
        return await Pintar(PantallaDelRegistro.Verificar, correo, null, StatusCodes.Status200OK, "Verifica tu correo", cancelacion);
    }

    // ---------- Verificación ----------

    [HttpGet("/verificacion")]
    public async Task<IActionResult> Verificacion(CancellationToken cancelacion)
    {
        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext) is { } consulta)
        {
            return await Falla(consulta, cancelacion, PantallaDelRegistro.Verificar);
        }

        return await Pintar(PantallaDelRegistro.Verificar, null, null, StatusCodes.Status200OK, "Verifica tu correo", cancelacion);
    }

    [HttpPost("/verificacion")]
    public async Task<IActionResult> Verificar(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDeLaVerificacion, cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion, PantallaDelRegistro.Verificar);
        }

        if (!campos.TryGetValue("correo", out var correo) || !campos.TryGetValue("codigo", out var codigo) || correo.Length is 0 or > 254 || codigo.Length is 0 or > 64)
        {
            return await Pintar(PantallaDelRegistro.Verificar, campos.GetValueOrDefault("correo"), "Escribe tu correo y el código que te enviamos.", StatusCodes.Status400BadRequest, "Verifica tu correo", cancelacion);
        }

        var resultado = await Llamar(() => registro.VerificarCorreo(correo, new ValorSecreto(codigo), cancelacion), cancelacion);
        if (!resultado.EsExito)
        {
            return await Pintar(PantallaDelRegistro.Verificar, correo, resultado.Error!.Categoria is CategoriaDeError.Limite or CategoriaDeError.NoDisponible ? TextoDelError(resultado.Error) : TextoCodigoInvalido, CodigoHttp(resultado.Error), "Verifica tu correo", cancelacion);
        }

        // Verificar el correo abre la sesión (la credencial solo viaja al servidor); si Identidad pide otro paso, sigue el ingreso.
        if (resultado.Valor.Credencial is { } credencial)
        {
            var cerrado = await cierre.Completar(HttpContext, credencial, cancelacion);
            if (cerrado.Iniciada)
            {
                return Redirigir(cerrado.Destino);
            }

            return await Pintar(PantallaDelRegistro.Listo, null, null, StatusCodes.Status200OK, "Tu correo está verificado", cancelacion);
        }

        if (resultado.Valor.Desafio is { } desafio)
        {
            CookieDelDesafio.Escribir(Response, desafio.Identificador.Revelar());
            return Redirigir(IngresoController.Ruta);
        }

        return await Pintar(PantallaDelRegistro.Listo, null, null, StatusCodes.Status200OK, "Tu correo está verificado", cancelacion);
    }

    [HttpPost("/verificacion/reenviar")]
    public async Task<IActionResult> Reenviar(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDelCorreo, cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion, PantallaDelRegistro.Verificar);
        }

        if (!campos.TryGetValue("correo", out var correo) || correo.Length is 0 or > 254)
        {
            return await Pintar(PantallaDelRegistro.Verificar, null, "Escribe tu correo.", StatusCodes.Status400BadRequest, "Verifica tu correo", cancelacion);
        }

        var resultado = await Llamar(() => registro.ReenviarCodigo(correo, cancelacion), cancelacion);
        return await Pintar(
            PantallaDelRegistro.Verificar,
            correo,
            resultado.EsExito ? "Si el correo corresponde a una cuenta sin verificar, te enviamos un código nuevo." : TextoDelError(resultado.Error!),
            resultado.EsExito ? StatusCodes.Status200OK : CodigoHttp(resultado.Error!),
            "Verifica tu correo",
            cancelacion);
    }

    // ---------- Recuperación ----------

    [HttpGet("/recuperacion")]
    public async Task<IActionResult> Recuperacion(CancellationToken cancelacion)
    {
        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext) is { } consulta)
        {
            return await Falla(consulta, cancelacion, PantallaDelRegistro.Recuperar);
        }

        return await Pintar(PantallaDelRegistro.Recuperar, null, null, StatusCodes.Status200OK, "Recupera tu acceso", cancelacion);
    }

    [HttpPost("/recuperacion")]
    public async Task<IActionResult> Solicitar(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDelCorreo, cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion, PantallaDelRegistro.Recuperar);
        }

        if (!campos.TryGetValue("correo", out var correo) || correo.Length is 0 or > 254)
        {
            return await Pintar(PantallaDelRegistro.Recuperar, null, "Escribe tu correo.", StatusCodes.Status400BadRequest, "Recupera tu acceso", cancelacion);
        }

        var resultado = await Llamar(() => recuperacion.Solicitar(correo, cancelacion), cancelacion);
        if (!resultado.EsExito && resultado.Error!.Categoria is CategoriaDeError.Limite or CategoriaDeError.NoDisponible)
        {
            return await Pintar(PantallaDelRegistro.Recuperar, correo, TextoDelError(resultado.Error), CodigoHttp(resultado.Error), "Recupera tu acceso", cancelacion);
        }

        // La misma pantalla exista o no la cuenta (IDE-56).
        return await Pintar(PantallaDelRegistro.ConfirmarRecuperacion, correo, "Si el correo corresponde a una cuenta, te enviamos un código. Escríbelo con tu contraseña nueva.", StatusCodes.Status200OK, "Recupera tu acceso", cancelacion);
    }

    [HttpPost("/recuperacion/confirmar")]
    public async Task<IActionResult> Confirmar(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDeLaRecuperacion, cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion, PantallaDelRegistro.ConfirmarRecuperacion);
        }

        campos.TryGetValue("correo", out var correo);
        if (correo is null || !campos.TryGetValue("codigo", out var codigo) || !campos.TryGetValue("contrasenaNueva", out var nueva) || !campos.TryGetValue("confirmacion", out var confirmacion)
            || correo.Length is 0 or > 254 || codigo.Length is 0 or > 64 || nueva.Length is 0 or > 1024)
        {
            return await Pintar(PantallaDelRegistro.ConfirmarRecuperacion, correo, "Escribe tu correo, el código y tu contraseña nueva dos veces.", StatusCodes.Status400BadRequest, "Recupera tu acceso", cancelacion);
        }

        if (!string.Equals(nueva, confirmacion, StringComparison.Ordinal))
        {
            return await Pintar(PantallaDelRegistro.ConfirmarRecuperacion, correo, "Las dos contraseñas no coinciden.", StatusCodes.Status400BadRequest, "Recupera tu acceso", cancelacion);
        }

        var resultado = await Llamar(() => recuperacion.Confirmar(correo, new ValorSecreto(codigo), new ValorSecreto(nueva), cancelacion), cancelacion);
        if (!resultado.EsExito)
        {
            var mensaje = resultado.Error!.Categoria is CategoriaDeError.Limite or CategoriaDeError.NoDisponible ? TextoDelError(resultado.Error) : resultado.Error.Categoria == CategoriaDeError.Validacion && resultado.Error.Errores.Count > 0 && resultado.Error.Errores[0].Campo.Contains("contrasena", StringComparison.OrdinalIgnoreCase)
                ? "La contraseña no cumple las reglas: usa al menos 12 caracteres y evita las muy comunes."
                : TextoCodigoInvalido;
            return await Pintar(PantallaDelRegistro.ConfirmarRecuperacion, correo, mensaje, CodigoHttp(resultado.Error), "Recupera tu acceso", cancelacion);
        }

        return await Pintar(PantallaDelRegistro.Listo, null, "Tu contraseña se cambió. Ya puedes ingresar con ella.", StatusCodes.Status200OK, "Contraseña cambiada", cancelacion);
    }

    // ---------- Piezas ----------

    private static async Task<Resultado<T>> Llamar<T>(Func<Task<Resultado<T>>> llamada, CancellationToken cancelacion)
    {
        try
        {
            return await llamada();
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return Resultado<T>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible"));
        }
    }

    private static string TextoDelError(ErrorDeAplicacion error) => error.Categoria switch
    {
        CategoriaDeError.Limite => TextoLimite,
        CategoriaDeError.NoDisponible => TextoNoDisponible,
        CategoriaDeError.Conflicto => "No pudimos crear la cuenta con esos datos. Si ya tienes una, ingresa o recupera tu acceso.",
        CategoriaDeError.Validacion => error.Errores.FirstOrDefault()?.Clave switch
        {
            "demasiado-corta" => "La contraseña es demasiado corta: usa al menos 12 caracteres.",
            "demasiado-comun" => "Esa contraseña es muy común. Elige otra.",
            "correo-invalido" => "El correo no es válido.",
            "version-de-textos-cambio" => "Los textos legales cambiaron mientras leías. Revisa y acepta la versión nueva.",
            _ => "Revisa los datos: alguno falta o no es válido.",
        },
        _ => "No pudimos completar la operación. Inténtalo de nuevo.",
    };

    private static int CodigoHttp(ErrorDeAplicacion error) => error.Categoria switch
    {
        CategoriaDeError.Limite => StatusCodes.Status429TooManyRequests,
        CategoriaDeError.NoDisponible => StatusCodes.Status503ServiceUnavailable,
        CategoriaDeError.Conflicto => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    private async Task<IActionResult> Formulario(string? mensaje, int estado, CancellationToken cancelacion)
    {
        var vigente = await Llamar(() => textos.VersionVigente(cancelacion), cancelacion);
        return await Pintar(PantallaDelRegistro.Formulario, null, mensaje, estado, "Crear mi cuenta", cancelacion, vigente.EsExito ? vigente.Valor : null);
    }

    private async Task<IActionResult> Pintar(PantallaDelRegistro pantalla, string? correo, string? mensaje, int estado, string titulo, CancellationToken cancelacion, VersionParaAceptar? vigente = null)
    {
        var vista = await pagina.Completar(new RegistroVista { Pantalla = pantalla, Correo = correo, Mensaje = mensaje, Textos = vigente }, titulo, cancelacion: cancelacion);
        Response.Headers.CacheControl = "no-store";
        Response.StatusCode = Request.EsHtmx() ? StatusCodes.Status200OK : estado;
        return View("Registro", vista);
    }

    private IActionResult Redirigir(string destino)
    {
        Response.Headers.CacheControl = "no-store";
        if (Request.EsHtmx())
        {
            return new HxRedirectResult(destino);
        }

        Response.StatusCode = StatusCodes.Status303SeeOther;
        Response.Headers.Location = destino;
        return new EmptyResult();
    }

    private async Task<IActionResult> Falla(ErrorDeAplicacion error, CancellationToken cancelacion, PantallaDelRegistro pantalla = PantallaDelRegistro.Formulario) =>
        await Pintar(
            pantalla,
            null,
            error.Categoria == CategoriaDeError.NoDisponible ? TextoNoDisponible : "No pudimos leer lo que enviaste. Revisa los datos e inténtalo de nuevo.",
            error.Categoria == CategoriaDeError.NoDisponible ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status400BadRequest,
            pantalla == PantallaDelRegistro.Formulario ? "Crear mi cuenta" : "Tu cuenta",
            cancelacion);
}
