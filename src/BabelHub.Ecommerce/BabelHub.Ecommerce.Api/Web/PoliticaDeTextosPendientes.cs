using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>
/// Lo que la ruta permite hacer con los textos legales pendientes (WEB-46): <b>solo en métodos</b> (sin herencia) y con una capacidad; <c>GET</c> si la capacidad es de lectura y <c>POST</c> si es de
/// acción. Sin este atributo —o con otro método— la política la niega. El arranque falla si el endpoint no declara sus métodos o declara uno fuera de la clase de su capacidad.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class PermitidaConTextosPendientesAttribute(CapacidadWeb capacidad) : Attribute
{
    public CapacidadWeb Capacidad { get; } = capacidad;
}

/// <summary>
/// La ruta se sirve <b>sin consultar</b> a Identidad: los destinos de las propias redirecciones de la web (<c>/error</c>, <c>/sin-permiso</c>, <c>/no-disponible</c>), que si la consultaran caerían en un
/// bucle con Identidad caída. Es una <b>lista cerrada</b> (<see cref="PoliticaDeTextosPendientes.RutasSinConsultaDeEstado"/>): una prueba y el arranque fallan si otra ruta la declara, y también si
/// coincide con <see cref="PermitidaConTextosPendientesAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class SinConsultaDeEstadoAttribute : Attribute;

/// <summary>La pantalla nunca se guarda como destino de vuelta tras aceptar los textos (por ejemplo, una con un token en su ruta o que reejecutaría algo).</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class SinRetornoAttribute : Attribute;

/// <summary>
/// La ruta lleva un valor de <b>sucursal</b> en el parámetro de ruta indicado: con los textos pendientes la Web lo compara con la sucursal del actor antes de consultar a Identidad (D15 de
/// <c>docs/03/08</c>, R15). Una ruta con un valor de sucursal que no lo declare no recibe esa comprobación hasta que la persona acepta: una prueba lo vigila.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ParametroDeSucursalAttribute(string nombreDelParametro) : Attribute
{
    public string NombreDelParametro { get; } = nombreDelParametro;
}

public static class PoliticaDeTextosPendientes
{
    /// <summary>La lista cerrada de las rutas que no consultan a Identidad, por coincidencia exacta con la plantilla del endpoint (nunca por prefijo).</summary>
    public static readonly IReadOnlyList<string> RutasSinConsultaDeEstado = ["/error", "/sin-permiso", "/no-disponible"];

    /// <summary>La salud para el reinicio automático: se exime por coincidencia exacta con el patrón del endpoint, solo <c>GET</c> y <c>HEAD</c>.</summary>
    public static readonly IReadOnlyList<string> RutasDeSalud = ["/health/live", "/health/ready"];

    public const string ClaveDelRetorno = "bh.sin-retorno";

    /// <summary>
    /// Falla si un endpoint declara <see cref="PermitidaConTextosPendientesAttribute"/> sin <c>HttpMethodMetadata</c> o con un método fuera de la clase de su capacidad (lectura: solo <c>GET</c>; acción: solo
    /// <c>POST</c>), si <see cref="SinConsultaDeEstadoAttribute"/> coincide con ella o está fuera de la lista cerrada, o si <see cref="ParametroDeSucursalAttribute"/> nombra un parámetro que la ruta no tiene.
    /// </summary>
    public static void Validar(IEnumerable<Endpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        foreach (var endpoint in endpoints)
        {
            var permitida = endpoint.Metadata.GetMetadata<PermitidaConTextosPendientesAttribute>();
            var sinConsulta = endpoint.Metadata.GetMetadata<SinConsultaDeEstadoAttribute>();
            var plantilla = PlantillasDeRuta.De(endpoint);

            // Un endpoint con ShortCircuit se ejecuta justo tras el enrutado y se saltaría la sesión y esta política: no se admite.
            if (endpoint.Metadata.Any(m => m.GetType().Name == "ShortCircuitMetadata"))
            {
                throw new InvalidOperationException($"«{endpoint.DisplayName}» usa ShortCircuit: se saltaría la sesión y la política de textos pendientes.");
            }

            if (permitida is not null)
            {
                var metodos = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
                if (metodos is null || metodos.Count == 0)
                {
                    throw new InvalidOperationException($"«{endpoint.DisplayName}» declara [PermitidaConTextosPendientes] sin declarar sus métodos HTTP: un endpoint sin HttpMethodMetadata admite todos.");
                }

                var fuera = metodos.Where(m => !permitida.Capacidad.AdmiteElMetodo(m)).ToList();
                if (fuera.Count > 0)
                {
                    throw new InvalidOperationException($"«{endpoint.DisplayName}» declara {permitida.Capacidad} y admite {string.Join(", ", fuera)}, que no es de la clase de su capacidad.");
                }
            }

            if (sinConsulta is not null)
            {
                if (permitida is not null)
                {
                    throw new InvalidOperationException($"«{endpoint.DisplayName}» no puede ser [SinConsultaDeEstado] y [PermitidaConTextosPendientes] a la vez.");
                }

                if (plantilla is null || !RutasSinConsultaDeEstado.Contains(plantilla, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException($"«{endpoint.DisplayName}» declara [SinConsultaDeEstado] en «{plantilla}», que no está en la lista cerrada.");
                }

                // Y solo GET: un destino de redirección nunca es una acción.
                var metodosSinConsulta = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
                if (metodosSinConsulta is null || metodosSinConsulta.Count != 1 || !string.Equals(metodosSinConsulta.Single(), HttpMethods.Get, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"«{endpoint.DisplayName}» es [SinConsultaDeEstado] y debe admitir únicamente GET.");
                }
            }

            if (endpoint.Metadata.GetMetadata<ParametroDeSucursalAttribute>() is { } parametro
                && (endpoint as RouteEndpoint)?.RoutePattern.GetParameter(parametro.NombreDelParametro) is null)
            {
                throw new InvalidOperationException($"«{endpoint.DisplayName}» declara [ParametroDeSucursal(\"{parametro.NombreDelParametro}\")] y su ruta no tiene ese parámetro.");
            }
        }
    }
}

/// <summary>
/// La política de textos pendientes entre el enrutado y los endpoints (T37, WEB-41 a WEB-49): lee el endpoint ya seleccionado, arma la solicitud para <see cref="IPoliticaDeAccesoPorTextos"/> y traduce su
/// decisión a HTTP; no decide nada. Va <b>después de la sesión</b> y antes de la autorización y de la acción. Sin endpoint, sin atributo o con otro método se niega (también 404, 405, HEAD, OPTIONS y TRACE);
/// los estáticos no llegan aquí y la salud se exime por coincidencia exacta con su patrón.
/// </summary>
public sealed class PoliticaDeTextosPendientesMiddleware(RequestDelegate siguiente, OpcionesDeErrores errores)
{
    private static readonly string[] MetodosDeSalud = ["GET", "HEAD"];

    public async Task InvokeAsync(HttpContext contexto, IPoliticaDeAccesoPorTextos politica, IDenegacionDeAcceso? denegaciones = null)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(politica);

        var endpoint = contexto.GetEndpoint();
        if (EsSalud(endpoint, contexto.Request.Method))
        {
            await siguiente(contexto).ConfigureAwait(false);
            return;
        }

        var decision = await politica.Resolver(ArmarSolicitud(contexto, endpoint), contexto.RequestAborted).ConfigureAwait(false);
        switch (decision.Tipo)
        {
            case TipoDeDecisionDeAcceso.Permitida:
                if (decision.TextosPendientes)
                {
                    contexto.Items[ContextoDePagina.ClaveTextosPendientes] = true;
                }

                await siguiente(contexto).ConfigureAwait(false);
                return;
            case TipoDeDecisionDeAcceso.Redirige:
                Ir(contexto, TraductorDeErrores.RutaDeAceptacion);
                return;
            case TipoDeDecisionDeAcceso.Bloqueada:
                await Rechazar(contexto, StatusCodes.Status403Forbidden, TraductorDeErrores.CodigoDeTextosPendientes, "Para continuar, acepta los textos legales vigentes.").ConfigureAwait(false);
                return;
            case TipoDeDecisionDeAcceso.NoDisponible:
                if (HttpMethods.IsGet(contexto.Request.Method) || contexto.Request.EsHtmx())
                {
                    Ir(contexto, "/no-disponible");
                }
                else
                {
                    await ProblemasHttp.Escribir(contexto, ProblemasHttp.Crear(contexto, errores, CategoriaDeError.NoDisponible)).ConfigureAwait(false);
                }

                return;
            case TipoDeDecisionDeAcceso.SinSesion:
                contexto.IrAlIngreso();
                return;
            default:
                await RegistroDeDenegaciones.Registrar(denegaciones, contexto, decision.AlcanceFueraDeLaSucursal ? "fuera-de-sucursal" : "fuera-de-rol", "ECO-PROHIBIDO").ConfigureAwait(false);
                if (HttpMethods.IsGet(contexto.Request.Method) || contexto.Request.EsHtmx())
                {
                    Ir(contexto, RespuestasDeSesion.RutaSinPermiso);
                }
                else
                {
                    await ProblemasHttp.Escribir(contexto, ProblemasHttp.Crear(contexto, errores, CategoriaDeError.Prohibido)).ConfigureAwait(false);
                }

                return;
        }
    }

    private static bool EsSalud(Endpoint? endpoint, string metodo) =>
        PlantillasDeRuta.De(endpoint) is { } ruta && PoliticaDeTextosPendientes.RutasDeSalud.Contains(ruta, StringComparer.Ordinal) && MetodosDeSalud.Contains(metodo, StringComparer.Ordinal);

    private static SolicitudDeAcceso ArmarSolicitud(HttpContext contexto, Endpoint? endpoint)
    {
        var metadatos = endpoint?.Metadata;
        var actor = contexto.ActorDeLaSolicitud();
        var plantilla = PlantillasDeRuta.De(endpoint);
        var area = metadatos?.GetMetadata<ControllerActionDescriptor>()?.RouteValues is { } valores && valores.TryGetValue("area", out var nombre) && !string.IsNullOrEmpty(nombre)
            ? ConvencionDeAreasPorHost.AreaDe(nombre) ?? throw new InvalidOperationException($"El área «{nombre}» no está en la lista cerrada de áreas de la interfaz.")
            : (AreaWeb?)null;

        Guid? sucursal = null;
        if (metadatos?.GetMetadata<ParametroDeSucursalAttribute>() is { } parametro)
        {
            // Un valor presente que no es una sucursal válida no coincide con ninguna: se trata como una sucursal ajena.
            // La ruta lleva el código de la sucursal (`ayacucho`); su identificador es el derivado canónico (D20), el mismo que lleva el token del encargado. Un identificador ya en forma de Guid también se admite.
            var texto = contexto.Request.RouteValues.TryGetValue(parametro.NombreDelParametro, out var valor) ? valor?.ToString() : null;
            sucursal = Guid.TryParse(texto, out var id) ? id : BabelHub.Ecommerce.Application.Plataforma.IdentificadorDeSucursal.DesdeCodigo(texto) ?? Guid.Empty;
        }

        var esGet = HttpMethods.IsGet(contexto.Request.Method);
        // Solo una navegación real a una pantalla que existe: ni un fragmento, ni un sondeo, ni una ruta sin endpoint, ni un recurso que el navegador pide por su cuenta (imagen, favicon, prefetch).
        var pantallaCompleta = esGet && endpoint is not null && !contexto.Request.EsHtmx() && !contexto.EsConsultaAutomatica() && EsNavegacion(contexto.Request);
        return new SolicitudDeAcceso(
            actor,
            CookieDeSesionWeb.Leer(contexto.Request, contexto.RequestServices.GetRequiredService<OpcionesDelHostWeb>().Host),
            area,
            metadatos?.GetMetadata<PermitidaConTextosPendientesAttribute>()?.Capacidad,
            contexto.Request.Method,
            sucursal,
            metadatos?.GetMetadata<SinConsultaDeEstadoAttribute>() is not null,
            plantilla,
            esGet ? contexto.Request.Path.Value : null,
            pantallaCompleta,
            metadatos?.GetMetadata<SinRetornoAttribute>() is not null);
    }

    private static bool EsNavegacion(HttpRequest solicitud) =>
        (!solicitud.Headers.TryGetValue("Sec-Fetch-Mode", out var modo) || string.Equals(modo.ToString(), "navigate", StringComparison.Ordinal))
        && (!solicitud.Headers.TryGetValue("Sec-Fetch-Dest", out var destino) || string.Equals(destino.ToString(), "document", StringComparison.Ordinal));

    private static void Ir(HttpContext contexto, string destino)
    {
        contexto.Response.Headers.CacheControl = "no-store";
        if (contexto.Request.EsHtmx())
        {
            contexto.Response.StatusCode = StatusCodes.Status200OK;
            contexto.Response.Headers["HX-Redirect"] = destino;
        }
        else
        {
            contexto.Response.StatusCode = StatusCodes.Status302Found;
            contexto.Response.Headers.Location = destino;
        }
    }

    private static Task Rechazar(HttpContext contexto, int estado, string codigo, string detalle) =>
        ProblemasHttp.Escribir(contexto, new ProblemaDto { Type = "about:blank", Title = "Acción no permitida", Status = estado, Detail = detalle, Code = codigo, TraceId = contexto.TraceIdentifier, Errores = [] });
}

public static class PoliticaDeTextosPendientesExtensiones
{
    /// <summary>Va <b>después de <c>UseRouting</c></b> y de la sesión web, y antes de la autorización y de los endpoints. Exige <see cref="IPoliticaDeAccesoPorTextos"/>.</summary>
    public static IApplicationBuilder UsarPoliticaDeTextosPendientes(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Montarla sin su política fallaría en cada solicitud: mejor que el arranque lo diga.
        if (!(app.ApplicationServices.GetService<IServiceProviderIsService>()?.IsService(typeof(IPoliticaDeAccesoPorTextos)) ?? false))
        {
            throw new InvalidOperationException("La política de textos pendientes está montada pero IPoliticaDeAccesoPorTextos no está registrada (AgregarPoliticaDeTextosPendientes).");
        }

        return app.UseMiddleware<PoliticaDeTextosPendientesMiddleware>();
    }
}
