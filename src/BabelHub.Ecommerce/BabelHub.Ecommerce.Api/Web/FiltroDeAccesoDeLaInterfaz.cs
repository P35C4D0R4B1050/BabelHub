using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>La pantalla o acción exige una sesión válida: sin ella la persona va al ingreso (y con el navegador limpio si su sesión venció o se rechazó su renovación).</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class RequiereSesionAttribute : Attribute;

/// <summary>
/// La ruta forma parte del ingreso o es una de las pocas que quien tiene un paso pendiente del ingreso puede abrir (WEB-34): las rutas del desafío (<c>/ingreso</c>, <c>POST /ingreso/cancelar</c>, el
/// segundo factor y la contraseña temporal), los textos legales, el Libro de Reclamaciones y los destinos de las propias redirecciones (<c>/error</c>, <c>/sin-permiso</c>, <c>/no-disponible</c>).
/// Se declara en métodos o controladores: cualquier otra ruta lleva a la persona al paso que le falta.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class PermitidaConPasoPendienteAttribute : Attribute;

/// <summary>Registra un acceso denegado en Auditoría sin que su falla interrumpa nada (AUD-12): el rechazo se mantiene y no se convierte en un error.</summary>
public static class RegistroDeDenegaciones
{
    public static async Task Registrar(IDenegacionDeAcceso? denegaciones, HttpContext contexto, string motivo, string codigoDeError)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        if (denegaciones is null)
        {
            return;
        }

        try
        {
            // La ruta es la plantilla del endpoint (nunca la dirección con sus valores: puede llevar un token).
            await denegaciones.Registrar(new DenegacionDeAcceso(motivo, PlantillasDeRuta.De(contexto.GetEndpoint())) { CodigoDeError = codigoDeError }, contexto.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception) when (!contexto.RequestAborted.IsCancellationRequested)
        {
            // Auditoría deja su propia alerta técnica.
        }
    }
}

/// <summary>
/// Los filtros globales de acceso a la interfaz (WEB-02, WEB-03, WEB-34; plan §1 y §6), aplicados en <b>cada</b> solicitud y antes de la acción:
/// <list type="number">
/// <item><b>Paso pendiente del ingreso:</b> quien no tiene sesión pero trae un desafío abierto va al ingreso antes de cualquier pantalla que no esté declarada <see cref="PermitidaConPasoPendienteAttribute"/>
/// (también una acción, sin ejecutarla). Falla cerrada y <b>sin bucle</b>: si Identidad no responde se trata como un paso pendiente y las rutas permitidas, incluido el propio ingreso, no se
/// vuelven a redirigir. Un desafío que ya no existe se borra (es de un solo uso).</item>
/// <item><b>Área:</b> el controlador de un área solo lo abre un rol que la ve (<see cref="IAreasDeInterfaz"/>; un visitante ve solo Tienda): sin sesión va al ingreso; con una sesión que no la permite ve
/// la pantalla de sin permiso y queda registrado en Auditoría (<c>fuera-de-rol</c>, AUD-05); una acción directa recibe un <c>403</c> sin redirección.</item>
/// <item><b>Sesión requerida:</b> una ruta <see cref="RequiereSesionAttribute"/> sin sesión va al ingreso.</item>
/// </list>
/// Las reglas de qué roles ven qué áreas son del módulo <c>Web</c>; aquí solo se aplican y se traducen a HTTP. La política de textos pendientes (T37) es posterior.
/// </summary>
public sealed class FiltroDeAccesoDeLaInterfaz(
    IAreasDeInterfaz areas,
    IDesafioDelIngreso desafios,
    OpcionesDeErrores errores,
    IDenegacionDeAcceso? denegaciones = null) : IAsyncAuthorizationFilter
{
    /// <summary>Después de la protección contra la falsificación (−1000): una acción sin token válido no llega a decidir nada más.</summary>
    public const int Orden = -900;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var contexto = context.HttpContext;
        var metadatos = context.ActionDescriptor.EndpointMetadata;
        var actor = contexto.ActorDeLaSolicitud();

        if (actor is null && CookieDelDesafio.Leer(contexto.Request) is { } desafio && await PasoPendiente(desafio, contexto.RequestAborted).ConfigureAwait(false) is var paso)
        {
            if (paso == PasoDelIngresoWeb.Ninguno)
            {
                CookieDelDesafio.Borrar(contexto.Response);
            }
            else if (!metadatos.OfType<PermitidaConPasoPendienteAttribute>().Any())
            {
                context.Result = contexto.RedirigirAlIngreso(limpiarElNavegador: false);
                return;
            }
        }

        var rol = actor?.Rol ?? RolWeb.Visitante;
        if (AreaDe(context.ActionDescriptor) is { } area && !areas.VeLaArea(rol, area))
        {
            context.Result = actor is null ? contexto.RedirigirAlIngreso() : await SinPermiso(contexto).ConfigureAwait(false);
            return;
        }

        if (actor is null && metadatos.OfType<RequiereSesionAttribute>().Any())
        {
            context.Result = contexto.RedirigirAlIngreso();
        }
    }

    private async Task<PasoDelIngresoWeb> PasoPendiente(string desafio, CancellationToken cancelacion)
    {
        try
        {
            return await desafios.PasoPendiente(desafio, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return PasoDelIngresoWeb.Desconocido;
        }
    }

    /// <summary>Un rol que no puede abrir lo que pidió: registro en Auditoría y la pantalla de sin permiso (o un <c>403</c> si era una acción directa).</summary>
    private async Task<IActionResult> SinPermiso(HttpContext contexto)
    {
        await RegistroDeDenegaciones.Registrar(denegaciones, contexto, "fuera-de-rol", "ECO-PROHIBIDO").ConfigureAwait(false);
        contexto.Response.Headers.CacheControl = "no-store";
        if (contexto.Request.EsHtmx())
        {
            return new HxRedirectResult(RespuestasDeSesion.RutaSinPermiso);
        }

        if (HttpMethods.IsGet(contexto.Request.Method))
        {
            return new LocalRedirectResult(RespuestasDeSesion.RutaSinPermiso);
        }

        await ProblemasHttp.Escribir(contexto, ProblemasHttp.Crear(contexto, errores, CategoriaDeError.Prohibido)).ConfigureAwait(false);
        return new EmptyResult();
    }

    private static AreaWeb? AreaDe(ActionDescriptor accion) =>
        accion is ControllerActionDescriptor { RouteValues: var valores } && valores.TryGetValue("area", out var nombre) && !string.IsNullOrEmpty(nombre)
            ? ConvencionDeAreasPorHost.AreaDe(nombre) ?? throw new InvalidOperationException($"El área «{nombre}» no está en la lista cerrada de áreas de la interfaz.")
            : null;
}

public static class FiltroDeAccesoExtensiones
{
    /// <summary>Registra los filtros globales de acceso (área, paso pendiente del ingreso y sesión requerida). Exige <see cref="IAreasDeInterfaz"/> e <see cref="IDesafioDelIngreso"/> (módulo <c>Web</c>).</summary>
    public static IServiceCollection AgregarFiltroDeAccesoDeLaInterfaz(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.Configure<MvcOptions>(opciones => opciones.Filters.Add<FiltroDeAccesoDeLaInterfaz>(FiltroDeAccesoDeLaInterfaz.Orden));
        return servicios;
    }
}
