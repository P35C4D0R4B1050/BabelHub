using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Controllers;

public enum PantallaDelIngreso
{
    Credenciales,
    SegundoFactor,
    ConfigurarSegundoFactor,
    ContrasenaTemporal,
    CodigosDeRespaldo,
}

/// <summary>Lo que dibuja la pantalla de ingreso: el paso, el mensaje de lo que falló (siempre el mismo para un dato incorrecto) y, solo al configurar el segundo factor o al mostrar los códigos de respaldo, un secreto que se muestra <b>una vez</b> y no se guarda.</summary>
public sealed class IngresoVista : ModeloBaseDePagina
{
    public PantallaDelIngreso Pantalla { get; set; }

    public string? Mensaje { get; set; }

    public string? SecretoBase32 { get; set; }

    public string? UriDelSecreto { get; set; }

    /// <summary>El código QR de esa misma dirección (web-acceso ACC-13); nulo si no se está configurando o si no se pudo generar.</summary>
    public CodigoQr? Qr { get; set; }

    public IReadOnlyList<string> CodigosDeRespaldo { get; set; } = [];

    public string Destino { get; set; } = "/";

    public bool EsGestion { get; set; }

    /// <summary>La persona estaba comprando: los formularios llevan la marca para volver al carrito al terminar (<see cref="ContinuacionDelIngreso"/>).</summary>
    public bool ContinuaLaCompra { get; set; }
}

/// <summary>
/// El ingreso (T28; WEB-19, WEB-20, WEB-34, SEG-31): <c>GET</c> y <c>POST /ingreso</c> con el correo y la contraseña (pegar y autocompletar permitidos, la verificación de persona antes de llamar a Identidad), los pasos que Identidad pide después —el código del segundo factor, su configuración y la
/// contraseña temporal— <b>sobre el desafío de la cookie <c>__Host-bh-desafio</c></b> (nunca en la URL ni en un campo) y el cierre que abre la sesión del servidor sin que la credencial ni la renovación lleguen al navegador (<see cref="CierreDelIngresoWeb"/>). <b>Presentación pura</b>: toda regla es de Identidad.
/// Un dato incorrecto, una cuenta inexistente o un bloqueo responden con el mismo mensaje (IDE-03). Existe en los dos hosts (la pareja de rol y host la decide <see cref="ISesionesWeb"/>).
/// </summary>
[PermitidaConPasoPendiente]
[SinRetorno]
public sealed class IngresoController(
    IIngreso ingreso,
    IDesafioDelIngreso desafios,
    IExigenciaDeVerificacionDePersona verificacion,
    CierreDelIngresoWeb cierre,
    ContextoDePagina pagina,
    IGeneradorDeCodigoQr codigos,
    OpcionesDelHostWeb host) : Controller
{
    public const string Ruta = "/ingreso";

    private static readonly string[] CamposDeLasCredenciales = ["correo", "contrasena", VerificacionDePersonaWeb.CampoDelToken, ContinuacionDelIngreso.Campo];
    private static readonly string[] CamposDelCodigo = ["codigo", ContinuacionDelIngreso.Campo];
    private static readonly string[] CamposDeLaContrasena = ["contrasenaNueva", "confirmacion"];
    private static readonly string[] ConsultaAdmitida = [ContinuacionDelIngreso.Campo];

    private bool _continuaLaCompra;

    // ---------- Credenciales ----------

    [HttpGet(Ruta)]
    [VerificacionDePersona(AccionProtegida.Ingreso)]
    public async Task<IActionResult> Mostrar(CancellationToken cancelacion)
    {
        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext, ConsultaAdmitida) is { } consulta)
        {
            return await Falla(consulta, cancelacion);
        }

        // La única consulta admitida es una marca de lista cerrada, nunca un destino: cualquier otro valor se ignora.
        _continuaLaCompra = Request.Query.TryGetValue(ContinuacionDelIngreso.Campo, out var continuar) && continuar.Count == 1 && ContinuacionDelIngreso.EsLaCompra(continuar[0]);
        if (HttpContext.ActorDeLaSolicitud() is { } actor)
        {
            return Redirigir(Destino(actor.Rol, CierreDelIngresoWeb.DestinoPorRol(actor.Rol)));
        }

        if (CookieDelDesafio.Leer(Request) is not { } desafio)
        {
            return await Pintar(PantallaDelIngreso.Credenciales, null, StatusCodes.Status200OK, cancelacion);
        }

        switch (await PasoActual(desafio, cancelacion))
        {
            case PasoDelIngresoWeb.PresentarSegundoFactor:
                return await Pintar(PantallaDelIngreso.SegundoFactor, null, StatusCodes.Status200OK, cancelacion);
            case PasoDelIngresoWeb.ContrasenaTemporal:
                return await Pintar(PantallaDelIngreso.ContrasenaTemporal, null, StatusCodes.Status200OK, cancelacion);
            case PasoDelIngresoWeb.ConfigurarSegundoFactor:
                return await MostrarLaConfiguracion(desafio, null, cancelacion);
            case PasoDelIngresoWeb.Desconocido:
                // Identidad no responde: el ingreso se abre igual (sin bucle) y avisa; la cookie del desafío se conserva por si Identidad vuelve antes de que venza.
                return await Pintar(PantallaDelIngreso.Credenciales, TextoNoDisponible, StatusCodes.Status200OK, cancelacion);
            default:
                // El desafío venció o ya se usó: la cookie no sirve y se empieza de nuevo.
                CookieDelDesafio.Borrar(Response);
                return await Pintar(PantallaDelIngreso.Credenciales, "Tu ingreso venció. Escribe tus datos otra vez.", StatusCodes.Status200OK, cancelacion);
        }
    }

    [HttpPost(Ruta)]
    [VerificacionDePersona(AccionProtegida.Ingreso)]
    public async Task<IActionResult> Ingresar(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDeLasCredenciales, cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        Recordar(campos);
        var token = campos.GetValueOrDefault(VerificacionDePersonaWeb.CampoDelToken) ?? Request.Headers["X-Verificacion-De-Persona"].FirstOrDefault();
        switch (await verificacion.Exigir(AccionProtegida.Ingreso, token, cancelacion))
        {
            case ResultadoDeExigencia.Requerida:
            case ResultadoDeExigencia.Fallida:
                return await Pintar(PantallaDelIngreso.Credenciales, "No pudimos comprobar que eres una persona. Inténtalo de nuevo.", StatusCodes.Status403Forbidden, cancelacion);
            case ResultadoDeExigencia.NoDisponible:
                return await Pintar(PantallaDelIngreso.Credenciales, TextoNoDisponible, StatusCodes.Status503ServiceUnavailable, cancelacion);
        }

        if (!campos.TryGetValue("correo", out var correo) || !campos.TryGetValue("contrasena", out var contrasena) || correo.Length is 0 or > 254 || contrasena.Length is 0 or > 1024)
        {
            return await Pintar(PantallaDelIngreso.Credenciales, TextoCredencialesInvalidas, StatusCodes.Status400BadRequest, cancelacion);
        }

        var dispositivo = DatosDelBorde.Leer(Request);
        var resultado = await Llamar(() => ingreso.Ingresar(new SolicitudDeIngreso(correo, new ValorSecreto(contrasena), dispositivo.Marca is null ? null : new ValorSecreto(dispositivo.Marca), dispositivo.Region), cancelacion), cancelacion);
        return await Continuar(resultado, PantallaDelIngreso.Credenciales, cancelacion);
    }

    // ---------- Segundo factor ----------

    [HttpPost("/ingreso/segundo-factor")]
    public async Task<IActionResult> PresentarSegundoFactor(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDelCodigo, cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        Recordar(campos);
        if (CookieDelDesafio.Leer(Request) is not { } desafio)
        {
            return Redirigir(RutaDelPaso);
        }

        if (!campos.TryGetValue("codigo", out var codigo) || codigo.Length is 0 or > 64)
        {
            return await Pintar(PantallaDelIngreso.SegundoFactor, TextoCodigoInvalido, StatusCodes.Status400BadRequest, cancelacion);
        }

        var resultado = await Llamar(() => ingreso.PresentarSegundoFactor(new ValorSecreto(desafio), new ValorSecreto(codigo), cancelacion), cancelacion);
        return await Continuar(resultado, PantallaDelIngreso.SegundoFactor, cancelacion);
    }

    [HttpPost("/ingreso/configuracion")]
    public async Task<IActionResult> ConfirmarElSegundoFactor(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDelCodigo, cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        Recordar(campos);
        if (CookieDelDesafio.Leer(Request) is not { } desafio)
        {
            return Redirigir(RutaDelPaso);
        }

        if (!campos.TryGetValue("codigo", out var codigo) || codigo.Length is 0 or > 64)
        {
            return await MostrarLaConfiguracion(desafio, TextoCodigoInvalido, cancelacion, StatusCodes.Status400BadRequest);
        }

        var resultado = await Llamar(() => ingreso.ConfirmarElSegundoFactor(new ValorSecreto(desafio), new ValorSecreto(codigo), cancelacion), cancelacion);
        return await Continuar(resultado, PantallaDelIngreso.ConfigurarSegundoFactor, cancelacion);
    }

    // ---------- Contraseña temporal ----------

    [HttpPost("/ingreso/contrasena")]
    public async Task<IActionResult> CambiarLaContrasenaTemporal(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, CamposDeLaContrasena, cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        if (CookieDelDesafio.Leer(Request) is not { } desafio)
        {
            return Redirigir(Ruta);
        }

        if (!campos.TryGetValue("contrasenaNueva", out var nueva) || !campos.TryGetValue("confirmacion", out var confirmacion) || nueva.Length is 0 or > 1024)
        {
            return await Pintar(PantallaDelIngreso.ContrasenaTemporal, "Escribe tu contraseña nueva dos veces.", StatusCodes.Status400BadRequest, cancelacion);
        }

        if (!string.Equals(nueva, confirmacion, StringComparison.Ordinal))
        {
            return await Pintar(PantallaDelIngreso.ContrasenaTemporal, "Las dos contraseñas no coinciden.", StatusCodes.Status400BadRequest, cancelacion);
        }

        var resultado = await Llamar(() => ingreso.CambiarLaContrasenaTemporal(new ValorSecreto(desafio), new ValorSecreto(nueva), cancelacion), cancelacion);
        return await Continuar(resultado, PantallaDelIngreso.ContrasenaTemporal, cancelacion);
    }

    // ---------- Cancelar ----------

    /// <summary>
    /// <c>POST /ingreso/cancelar</c>: descarta el desafío abierto (Identidad) y borra su cookie. La cookie se borra aunque Identidad no responda: quien cancela siempre puede salir, y el desafío vence solo a los 5 minutos.
    /// </summary>
    [HttpPost("/ingreso/cancelar")]
    public async Task<IActionResult> Cancelar(CancellationToken cancelacion)
    {
        if (CookieDelDesafio.Leer(Request) is { } desafio)
        {
            try
            {
                await desafios.Cancelar(desafio, cancelacion);
            }
            catch (Exception) when (!cancelacion.IsCancellationRequested)
            {
                // El desafío caduca solo; no se retiene a la persona.
            }
        }

        CookieDelDesafio.Borrar(Response);
        Response.Headers.CacheControl = "no-store";
        return Request.EsHtmx() ? new HxRedirectResult(RespuestasDeSesion.RutaDelIngreso) : new LocalRedirectResult(RespuestasDeSesion.RutaDelIngreso);
    }

    // ---------- Piezas ----------

    private const string TextoCredencialesInvalidas = "El correo o la contraseña no son correctos, o la cuenta no está disponible.";
    private const string TextoCodigoInvalido = "El código no es válido o ya se usó.";
    private const string TextoNoDisponible = "No podemos atenderte ahora. Inténtalo en unos minutos.";
    private const string TextoLimite = "Hiciste demasiados intentos. Espera unos minutos y vuelve a probar.";

    /// <summary>El ingreso en el que sigue la persona: conserva la marca de la compra entre un paso y el siguiente.</summary>
    private string RutaDelPaso => _continuaLaCompra ? ContinuacionDelIngreso.RutaDelIngresoDeLaCompra : Ruta;

    private void Recordar(IReadOnlyDictionary<string, string> campos) =>
        _continuaLaCompra = campos.TryGetValue(ContinuacionDelIngreso.Campo, out var valor) && ContinuacionDelIngreso.EsLaCompra(valor);

    /// <summary>Quien compraba y es cliente vuelve a su carrito; cualquier otro caso va a donde ya iba.</summary>
    private string Destino(RolWeb rol, string destinoDelRol) => _continuaLaCompra && rol == RolWeb.Cliente ? ContinuacionDelIngreso.DestinoDeLaCompra : destinoDelRol;

    private async Task<PasoDelIngresoWeb> PasoActual(string desafio, CancellationToken cancelacion)
    {
        try
        {
            return await desafios.PasoPendiente(desafio, cancelacion);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return PasoDelIngresoWeb.Desconocido;
        }
    }

    /// <summary>Llama a Identidad: una excepción nunca se propaga como pantalla técnica, es «servicio no disponible».</summary>
    private static async Task<Resultado<ResultadoDelIngreso>> Llamar(Func<Task<Resultado<ResultadoDelIngreso>>> llamada, CancellationToken cancelacion)
    {
        try
        {
            return await llamada();
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return Resultado<ResultadoDelIngreso>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible"));
        }
    }

    private async Task<IActionResult> Continuar(Resultado<ResultadoDelIngreso> resultado, PantallaDelIngreso pantallaActual, CancellationToken cancelacion)
    {
        if (!resultado.EsExito)
        {
            return await Rechazo(resultado.Error!, pantallaActual, cancelacion);
        }

        var valor = resultado.Valor;
        if (valor.Credencial is { } credencial)
        {
            var cerrado = await cierre.Completar(HttpContext, credencial, cancelacion);
            if (!cerrado.Iniciada)
            {
                // La pareja de rol y host no corresponde (un cliente en gestión, personal en la tienda) o el servicio no responde: el mismo mensaje de un dato incorrecto.
                if (CookieDelDesafio.Leer(Request) is not null)
                {
                    CookieDelDesafio.Borrar(Response);
                }

                return cerrado.ServicioNoDisponible
                    ? await Pintar(PantallaDelIngreso.Credenciales, TextoNoDisponible, StatusCodes.Status503ServiceUnavailable, cancelacion)
                    : await Pintar(PantallaDelIngreso.Credenciales, TextoCredencialesInvalidas, StatusCodes.Status401Unauthorized, cancelacion);
            }

            if (valor.CodigosDeRespaldo.Count > 0)
            {
                // Los códigos de respaldo se muestran esta única vez, en la respuesta que abre la sesión, y no se guardan.
                var vista = await pagina.Completar(
                    new IngresoVista { Pantalla = PantallaDelIngreso.CodigosDeRespaldo, CodigosDeRespaldo = [.. valor.CodigosDeRespaldo.Select(c => c.Revelar())], Destino = cerrado.Destino, EsGestion = host.Host == HostWeb.Gestion },
                    "Guarda tus códigos de respaldo",
                    cancelacion: cancelacion);
                Response.Headers.CacheControl = "no-store";
                return View("Ingreso", vista);
            }

            return Redirigir(Destino(CierreDelIngresoWeb.Rol(credencial.Rol), cerrado.Destino));
        }

        if (valor.Desafio is { } desafio)
        {
            CookieDelDesafio.Escribir(Response, desafio.Identificador.Revelar());
            return Redirigir(RutaDelPaso);
        }

        return await Rechazo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible"), pantallaActual, cancelacion);
    }

    private async Task<IActionResult> Rechazo(ErrorDeAplicacion error, PantallaDelIngreso pantalla, CancellationToken cancelacion)
    {
        var (texto, estado) = error.Categoria switch
        {
            CategoriaDeError.Limite => (TextoLimite, StatusCodes.Status429TooManyRequests),
            CategoriaDeError.NoDisponible => (TextoNoDisponible, StatusCodes.Status503ServiceUnavailable),
            CategoriaDeError.Validacion when error.Clave is "codigo-invalido" => (TextoCodigoInvalido, StatusCodes.Status400BadRequest),
            CategoriaDeError.Validacion when pantalla == PantallaDelIngreso.Credenciales => (TextoCredencialesInvalidas, StatusCodes.Status401Unauthorized),
            CategoriaDeError.Validacion => (TextoDeLaContrasena(error), StatusCodes.Status400BadRequest),
            _ when pantalla is PantallaDelIngreso.Credenciales => (TextoCredencialesInvalidas, StatusCodes.Status401Unauthorized),
            // Un desafío inválido (venció, ya se usó o es de otro paso): no sirve y se empieza otra vez.
            _ => ("Tu ingreso venció o ya no es válido. Empieza otra vez.", StatusCodes.Status401Unauthorized),
        };
        if (error.Categoria == CategoriaDeError.NoAutenticado && pantalla != PantallaDelIngreso.Credenciales)
        {
            CookieDelDesafio.Borrar(Response);
            return await Pintar(PantallaDelIngreso.Credenciales, texto, estado, cancelacion);
        }

        if (pantalla == PantallaDelIngreso.ConfigurarSegundoFactor && CookieDelDesafio.Leer(Request) is { } desafio)
        {
            return await MostrarLaConfiguracion(desafio, texto, cancelacion, estado);
        }

        return await Pintar(pantalla, texto, estado, cancelacion);
    }

    /// <summary>Las razones de una contraseña nueva rechazada, en palabras de la persona y sin repetir lo que escribió.</summary>
    private static string TextoDeLaContrasena(ErrorDeAplicacion error) => error.Errores.FirstOrDefault()?.Clave switch
    {
        "demasiado-corta" => $"La contraseña es demasiado corta: usa al menos {ReglasDeContrasena.LongitudMinima} caracteres.",
        "demasiado-larga" => $"La contraseña es demasiado larga: usa como máximo {ReglasDeContrasena.LongitudMaxima} caracteres.",
        "demasiado-comun" => "Esa contraseña es muy común. Elige otra.",
        "igual-a-la-actual" => "La contraseña nueva debe ser distinta de la anterior.",
        _ => "La contraseña no cumple las reglas. Elige otra más larga y poco común.",
    };

    private async Task<IActionResult> MostrarLaConfiguracion(string desafio, string? mensaje, CancellationToken cancelacion, int estado = StatusCodes.Status200OK)
    {
        var secreto = await Llamar2(() => ingreso.IniciarConfiguracionDelSegundoFactor(new ValorSecreto(desafio), cancelacion), cancelacion);
        if (!secreto.EsExito)
        {
            if (secreto.Error!.Categoria == CategoriaDeError.NoAutenticado)
            {
                CookieDelDesafio.Borrar(Response);
                return await Pintar(PantallaDelIngreso.Credenciales, "Tu ingreso venció. Escribe tus datos otra vez.", StatusCodes.Status401Unauthorized, cancelacion);
            }

            return await Pintar(PantallaDelIngreso.ConfigurarSegundoFactor, TextoNoDisponible, StatusCodes.Status503ServiceUnavailable, cancelacion);
        }

        // El código QR dice lo mismo que el enlace y viaja solo en esta respuesta, que no se guarda en caché (web-acceso ACC-13, ACC-14).
        var direccion = ConfiguracionDelSegundoFactor.Direccion(secreto.Valor);
        var vista = await pagina.Completar(
            new IngresoVista
            {
                Pantalla = PantallaDelIngreso.ConfigurarSegundoFactor,
                Mensaje = mensaje,
                SecretoBase32 = secreto.Valor.SecretoBase32.Revelar(),
                UriDelSecreto = direccion,
                Qr = codigos.Generar(new ValorSecreto(direccion)),
                EsGestion = host.Host == HostWeb.Gestion,
            },
            "Configura tu segundo factor",
            cancelacion: cancelacion);
        Response.Headers.CacheControl = "no-store";
        Response.StatusCode = Request.EsHtmx() ? StatusCodes.Status200OK : estado;
        return View("Ingreso", vista);
    }

    private static async Task<Resultado<SecretoDelSegundoFactor>> Llamar2(Func<Task<Resultado<SecretoDelSegundoFactor>>> llamada, CancellationToken cancelacion)
    {
        try
        {
            return await llamada();
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return Resultado<SecretoDelSegundoFactor>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible"));
        }
    }

    private async Task<IActionResult> Pintar(PantallaDelIngreso pantalla, string? mensaje, int estado, CancellationToken cancelacion)
    {
        var titulo = pantalla switch
        {
            PantallaDelIngreso.SegundoFactor => "Tu código de verificación",
            PantallaDelIngreso.ContrasenaTemporal => "Cambia tu contraseña temporal",
            _ => "Ingresar",
        };
        var vista = await pagina.Completar(new IngresoVista { Pantalla = pantalla, Mensaje = mensaje, EsGestion = host.Host == HostWeb.Gestion, ContinuaLaCompra = _continuaLaCompra }, titulo, cancelacion: cancelacion);
        Response.Headers.CacheControl = "no-store";
        Response.StatusCode = Request.EsHtmx() ? StatusCodes.Status200OK : estado;
        return View("Ingreso", vista);
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

    private async Task<IActionResult> Falla(ErrorDeAplicacion error, CancellationToken cancelacion) =>
        error.Categoria == CategoriaDeError.NoDisponible
            ? await Pintar(PantallaDelIngreso.Credenciales, TextoNoDisponible, StatusCodes.Status503ServiceUnavailable, cancelacion)
            : await Pintar(PantallaDelIngreso.Credenciales, "No pudimos leer lo que enviaste. Revisa los datos e inténtalo de nuevo.", StatusCodes.Status400BadRequest, cancelacion);
}
