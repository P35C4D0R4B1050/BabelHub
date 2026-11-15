using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>Los nombres que usa la protección contra la falsificación de solicitudes (WEB-05): la cookie propia <c>__Host-</c>, la cabecera que htmx envía y el campo de los formularios.</summary>
public static class ProteccionContraFalsificacion
{
    public const string NombreDeLaCookie = "__Host-bh-antiforgery";
    public const string NombreDeLaCabecera = "RequestVerificationToken";
    public const string NombreDelCampo = "__RequestVerificationToken";

    /// <summary>El motivo de <c>acceso_denegado</c> con el que se registra un rechazo (R12 pide motivos propios: hasta que existan, el más cercano del catálogo).</summary>
    public const string MotivoDelRegistro = "sin-sesion";
}

/// <summary>
/// Marca un campo de un formulario como <b>conservable</b> si la sesión vence mientras la persona lo escribe (WEB-25, D06): es una lista de campos autorizados uno a uno, no una
/// exclusión; un campo nuevo no se conserva hasta marcarlo. Solo tiene efecto en rutas cuya plantilla está en la lista cerrada del módulo (<c>/reclamaciones/nueva</c>,
/// <c>/cuenta/datos</c>, <c>/checkout/datos</c>…) y nunca para contraseñas ni códigos (el Dominio los rechaza por nombre).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false, Inherited = true)]
public sealed class ConservarAttribute : Attribute;

public enum ResultadoDeOrigen
{
    Valido,

    /// <summary>La cabecera <c>Origin</c> no es del sitio (o es <c>null</c> o ilegible).</summary>
    OrigenNoPermitido,

    /// <summary>El navegador declara que la solicitud viene de otro sitio (<c>Sec-Fetch-Site</c> distinto de <c>same-origin</c>).</summary>
    DeOtroSitio,
}

/// <summary>
/// Comprueba <c>Origin</c> y <c>Sec-Fetch-Site</c> de una solicitud que cambia algo (WEB-05, D07): <c>Origin</c>, si viene, debe ser exactamente el esquema y uno de los hosts
/// configurados (<c>BABELHUB__Web__HostsPermitidos</c>; sin lista, el host de la propia solicitud); <c>Sec-Fetch-Site</c>, si viene, debe ser <c>same-origin</c>. Un cliente sin
/// ninguna de las dos cabeceras no se rechaza por esto: sigue necesitando el token antifalsificación.
/// </summary>
public static class ComprobadorDeOrigen
{
    public static ResultadoDeOrigen Comprobar(HttpRequest solicitud, IReadOnlyList<string> hostsPermitidos)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        ArgumentNullException.ThrowIfNull(hostsPermitidos);

        if (solicitud.Headers.TryGetValue("Origin", out var origenes) && origenes.Count > 0)
        {
            if (origenes.Count != 1 || !Uri.TryCreate(origenes[0], UriKind.Absolute, out var origen) || !EsDelSitio(origen, solicitud, hostsPermitidos))
            {
                return ResultadoDeOrigen.OrigenNoPermitido;
            }
        }

        if (solicitud.Headers.TryGetValue("Sec-Fetch-Site", out var sitios) && sitios.Count > 0
            && (sitios.Count != 1 || !string.Equals(sitios[0], "same-origin", StringComparison.Ordinal)))
        {
            return ResultadoDeOrigen.DeOtroSitio;
        }

        return ResultadoDeOrigen.Valido;
    }

    private static bool EsDelSitio(Uri origen, HttpRequest solicitud, IReadOnlyList<string> hostsPermitidos)
    {
        // El origen es solo esquema, host y puerto: cualquier ruta, consulta o credencial lo vuelve sospechoso.
        if (!string.Equals(origen.Scheme, solicitud.Scheme, StringComparison.OrdinalIgnoreCase)
            || origen.AbsolutePath != "/" || origen.Query.Length > 0 || origen.UserInfo.Length > 0)
        {
            return false;
        }

        var autoridad = origen.IsDefaultPort ? origen.Host : $"{origen.Host}:{origen.Port}";
        return hostsPermitidos.Count == 0
            ? string.Equals(autoridad, solicitud.Host.ToUriComponent(), StringComparison.OrdinalIgnoreCase)
            : hostsPermitidos.Any(h => string.Equals(h, autoridad, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// El token antifalsificación va ligado a la <b>sesión</b>, no solo a la persona: lleva como dato adicional el hash del identificador de la cookie, de modo que un token emitido
/// para una sesión no sirve con otra (tras ingresar de nuevo) ni para una visita anónima. Sin cookie de sesión el dato es vacío.
/// </summary>
public sealed class DatosAdicionalesDelAntiforgery(OpcionesDelHostWeb host) : IAntiforgeryAdditionalDataProvider
{
    public string GetAdditionalData(HttpContext context) => Calcular(context);

    public bool ValidateAdditionalData(HttpContext context, string additionalData)
    {
        ArgumentNullException.ThrowIfNull(additionalData);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Calcular(context)), Encoding.UTF8.GetBytes(additionalData));
    }

    private string Calcular(HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        return CookieDeSesionWeb.Leer(contexto.Request, host.Host) is { } valor
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor)))
            : string.Empty;
    }
}

/// <summary>
/// La protección contra la falsificación de solicitudes en el pipeline de MVC (WEB-05, WEB-25; plan §6): <b>global</b> para todo <c>POST</c>, <c>PUT</c>, <c>PATCH</c> y <c>DELETE</c> salvo
/// lo que declare <c>[IgnoreAntiforgeryToken]</c>. En orden: (1) <c>Origin</c> y <c>Sec-Fetch-Site</c>; (2) si la sesión <b>venció</b> o Identidad rechazó la renovación, guarda lo que
/// la persona escribía —solo campos <see cref="ConservarAttribute">[Conservar]</see> de una ruta de la lista cerrada y <b>solo con un token válido para esa sesión</b>— y la lleva al
/// ingreso sin ejecutar la acción; (3) valida el token. Todo rechazo queda registrado en Auditoría (<c>IDenegacionDeAcceso</c>, que nunca interrumpe) y responde sin detalles.
/// </summary>
public sealed class ProteccionContraFalsificacionFilter(
    IAntiforgery antiforgery,
    OpcionesDelHostWeb host,
    OpcionesDeErrores errores,
    IDenegacionDeAcceso? denegaciones = null,
    IBorradoresDePantalla? borradores = null) : IAsyncAuthorizationFilter
{
    /// <summary>Corre antes de los filtros de autorización por omisión, entre ellos el <c>AutoValidateAntiforgeryToken</c> de MVC y los <c>[ValidateAntiForgeryToken]</c> explícitos.</summary>
    public const int Orden = -1000;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var contexto = context.HttpContext;
        if (EsSeguro(contexto.Request.Method) || SeIgnora(context))
        {
            return;
        }

        var origen = ComprobadorDeOrigen.Comprobar(contexto.Request, host.HostsPermitidos);
        if (origen != ResultadoDeOrigen.Valido)
        {
            await Rechazar(context, CategoriaDeError.Prohibido).ConfigureAwait(false);
            return;
        }

        if (contexto.EstadoDeLaSesion() is EstadoDeLaSesionEnLaSolicitud.Vencida or EstadoDeLaSesionEnLaSolicitud.RenovacionRechazada)
        {
            await ConservarLoEscrito(context).ConfigureAwait(false);
            context.Result = contexto.RedirigirAlIngreso();
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(contexto).ConfigureAwait(false);
        }
        catch (AntiforgeryValidationException)
        {
            await Rechazar(context, CategoriaDeError.Validacion).ConfigureAwait(false);
        }
    }

    /// <summary>La plantilla de la ruta del endpoint (nunca la dirección con sus valores), siempre con «/» inicial: es lo que el módulo compara con su lista cerrada.</summary>
    internal static string? PlantillaDeLaRuta(HttpContext contexto) =>
        PlantillasDeRuta.De(contexto.GetEndpoint());

    /// <summary>Una ruta declarada <c>[IgnoreAntiforgeryToken]</c> (por ejemplo, el destino de las propias redirecciones de error) no pide token.</summary>
    private static bool SeIgnora(AuthorizationFilterContext context) =>
        context.Filters.OfType<IgnoreAntiforgeryTokenAttribute>().Any()
        || context.ActionDescriptor.EndpointMetadata.OfType<IAntiforgeryMetadata>().LastOrDefault() is { RequiresValidation: false };

    private static bool EsSeguro(string metodo) =>
        HttpMethods.IsGet(metodo) || HttpMethods.IsHead(metodo) || HttpMethods.IsOptions(metodo) || HttpMethods.IsTrace(metodo);

    private async Task Rechazar(AuthorizationFilterContext context, CategoriaDeError categoria)
    {
        var contexto = context.HttpContext;
        await Registrar(contexto).ConfigureAwait(false);
        await ProblemasHttp.Escribir(contexto, ProblemasHttp.Crear(contexto, errores, categoria)).ConfigureAwait(false);
        context.Result = new EmptyResult();
    }

    /// <summary>El registro de un rechazo no interrumpe nada: si Auditoría falla, la solicitud sigue rechazada y no se convierte en un error.</summary>
    private Task Registrar(HttpContext contexto) => RegistroDeDenegaciones.Registrar(denegaciones, contexto, ProteccionContraFalsificacion.MotivoDelRegistro, "ECO-PROHIBIDO");

    private async Task ConservarLoEscrito(AuthorizationFilterContext context)
    {
        var contexto = context.HttpContext;
        if (borradores is null
            || CookieDeSesionWeb.Leer(contexto.Request, host.Host) is not { } cookie
            || context.ActionDescriptor is not ControllerActionDescriptor accion
            || PlantillaDeLaRuta(contexto) is not { } plantilla
            || !contexto.Request.HasFormContentType)
        {
            return;
        }

        var conservables = CamposConservables(accion);
        if (conservables.Count == 0)
        {
            return;
        }

        var formulario = await contexto.Request.ReadFormAsync(contexto.RequestAborted).ConfigureAwait(false);
        var campos = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var nombre in conservables)
        {
            if (formulario.TryGetValue(nombre, out var valores) && valores.Count == 1 && valores[0] is { } valor)
            {
                campos[nombre] = valor;
            }
        }

        if (campos.Count == 0)
        {
            return;
        }

        try
        {
            await borradores.Conservar(new ConservarBorrador(cookie, plantilla, campos, (usuario, ct) => TokenValidoParaLaSesionRetenida(contexto, usuario)), contexto.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception) when (!contexto.RequestAborted.IsCancellationRequested)
        {
            // Conservar lo escrito es un favor, no una condición: un fallo no impide llevar a la persona al ingreso.
        }
    }

    /// <summary>
    /// Valida el token como si la persona aún tuviera su sesión: con un principal construido con el <c>usuario_id</c> de la <b>fila retenida</b> (la solicitud ya es anónima) y el
    /// hash del identificador de su cookie como dato adicional (lo calcula <see cref="DatosAdicionalesDelAntiforgery"/> con la cookie que la solicitud trae). Un token emitido para
    /// otra persona o para otra sesión no pasa.
    /// </summary>
    private async Task<bool> TokenValidoParaLaSesionRetenida(HttpContext contexto, Guid usuarioId)
    {
        var original = contexto.User;
        try
        {
            contexto.User = SesionWebHandler.PrincipalDe(usuarioId, rol: null, sucursalId: null);
            return await antiforgery.IsRequestValidAsync(contexto).ConfigureAwait(false);
        }
        finally
        {
            contexto.User = original;
        }
    }

    /// <summary>Los campos conservables de la acción: las propiedades de sus modelos y los parámetros marcados con <see cref="ConservarAttribute"/>.</summary>
    internal static IReadOnlySet<string> CamposConservables(ControllerActionDescriptor accion)
    {
        var nombres = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parametro in accion.Parameters)
        {
            if ((parametro as ControllerParameterDescriptor)?.ParameterInfo.GetCustomAttribute<ConservarAttribute>() is not null)
            {
                nombres.Add(parametro.Name);
            }

            foreach (var propiedad in parametro.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (propiedad.GetCustomAttribute<ConservarAttribute>() is not null)
                {
                    nombres.Add(propiedad.Name);
                }
            }
        }

        return nombres;
    }
}

public static class ProteccionContraFalsificacionExtensiones
{
    /// <summary>
    /// Registra el antiforgery de la web: cookie propia <c>__Host-</c> (<c>Secure</c>, <c>HttpOnly</c>, <c>SameSite=Lax</c>), la cabecera de htmx, el token ligado a la sesión y el filtro
    /// global. Quien la use registra <see cref="OpcionesDelHostWeb"/> y <see cref="OpcionesDeErrores"/>; las claves de Data Protection las aporta quien compone el servicio (T12).
    /// </summary>
    public static IServiceCollection AgregarProteccionContraFalsificacion(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.AddAntiforgery(opciones =>
        {
            opciones.Cookie.Name = ProteccionContraFalsificacion.NombreDeLaCookie;
            opciones.Cookie.Path = "/";
            opciones.Cookie.HttpOnly = true;
            opciones.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            opciones.Cookie.SameSite = SameSiteMode.Lax;
            opciones.HeaderName = ProteccionContraFalsificacion.NombreDeLaCabecera;
            opciones.FormFieldName = ProteccionContraFalsificacion.NombreDelCampo;
            // El marco no se ofrece a otros sitios: lo dice frame-ancestors de la CSP (T17); no hace falta una segunda cabecera.
            opciones.SuppressXFrameOptionsHeader = true;
        });
        servicios.AddSingleton<IAntiforgeryAdditionalDataProvider, DatosAdicionalesDelAntiforgery>();
        servicios.Configure<MvcOptions>(opciones => opciones.Filters.Add<ProteccionContraFalsificacionFilter>(ProteccionContraFalsificacionFilter.Orden));
        return servicios;
    }
}
