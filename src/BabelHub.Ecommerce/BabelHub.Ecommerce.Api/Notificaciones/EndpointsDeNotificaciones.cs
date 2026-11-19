using System.Globalization;
using System.Text.Json;
using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Ecommerce;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Notificaciones;

/// <summary>Desactiva o reactiva los avisos de un número a pedido de su titular (NOT-13). El cuerpo nunca se registra.</summary>
public sealed record DesactivarNumeroDto(string? Numero, string? Motivo);

/// <summary>Reactiva los avisos de un número a pedido de su titular (NOT-13).</summary>
public sealed record ReactivarNumeroDto(string? Numero);

/// <summary>Un aviso como lo ve la administración (NOT-06): estado y fechas, nunca el número ni los campos.</summary>
public sealed record AvisoDto(Guid Id, string EventoId, string Destinatario, string Plantilla, string Estado, int Intentos, DateTimeOffset Creado, DateTimeOffset? FechaFinal, string? MotivoDeCierre);

/// <summary>Una página de avisos.</summary>
public sealed record PaginaDeAvisosDto(IReadOnlyList<AvisoDto> Avisos, int Pagina, int Tamano, int Total);

/// <summary>
/// La entrada firmada de los estados de entrega de n8n (T19; NOT-06, NOT-12, AC-NOT-11): <c>POST /api/ecommerce/v1/integraciones/n8n/estados</c>, <b>firma de proveedor y no credencial de servicio</b>. Presentación pura: lee el cuerpo con
/// un tope de 4 KiB (también si la cabecera de longitud miente o va por trozos), toma la marca y la firma de sus cabeceras y llama a <see cref="IRecepcionDeEstadosDeEntrega"/>, que verifica, deduplica y registra. Sin marca o sin firma,
/// el mismo 401 uniforme que una firma falsa. Solo es alcanzable desde la red interna (el borde público la responde 404). La ráfaga por origen y la exención de SEG-18 son las de la entrada de la pasarela.
/// </summary>
public static class EstadosDeEntregaDeN8nEnLaApi
{
    public const string Ruta = "/api/ecommerce/v1/integraciones/n8n/estados";

    public const string CabeceraDeLaMarca = "X-Marca-De-Tiempo";

    public const string CabeceraDeLaFirma = "X-Firma";

    public const int TopeDelCuerpo = 4 * 1024;

    /// <summary>El actor de la llamada: la integración de n8n.</summary>
    public const string Integracion = "n8n";

    public static IEndpointRouteBuilder MapearEstadosDeEntregaDeN8n(this IEndpointRouteBuilder rutas, bool habilitada)
    {
        ArgumentNullException.ThrowIfNull(rutas);
        if (habilitada)
        {
            rutas.MapPost(Ruta, Recibir).DisableAntiforgery();
        }

        return rutas;
    }

    private static async Task Recibir(HttpContext contexto, IRecepcionDeEstadosDeEntrega recepcion, IEstablecedorDeActor actor, OpcionesDeErrores errores)
    {
        var cancelacion = contexto.RequestAborted;
        if (contexto.Request.ContentLength is > TopeDelCuerpo)
        {
            await ProblemasDelCarrito.Escribir(contexto, errores, Validacion("cuerpo", "demasiado-grande")).ConfigureAwait(false);
            return;
        }

        var marca = contexto.Request.Headers[CabeceraDeLaMarca].ToString();
        var firma = contexto.Request.Headers[CabeceraDeLaFirma].ToString();
        if (string.IsNullOrWhiteSpace(marca) || string.IsNullOrWhiteSpace(firma) || marca.Length > 40 || firma.Length > 512)
        {
            await ProblemasDelCarrito.Escribir(contexto, errores, new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, "no-autenticado")).ConfigureAwait(false);
            return;
        }

        var cuerpo = await LeerElCuerpo(contexto.Request.Body, TopeDelCuerpo, cancelacion).ConfigureAwait(false);
        if (cuerpo is null)
        {
            await ProblemasDelCarrito.Escribir(contexto, errores, Validacion("cuerpo", "demasiado-grande")).ConfigureAwait(false);
            return;
        }

        actor.Establecer(ActorActual.DeIntegracion(Integracion));
        try
        {
            var resultado = await recepcion.Recibir(new EstadoDeEntregaFirmadoEntrada(cuerpo, marca, firma), cancelacion).ConfigureAwait(false);
            if (!resultado.EsExito)
            {
                await ProblemasDelCarrito.Escribir(contexto, errores, resultado.Error!).ConfigureAwait(false);
                return;
            }
        }
        finally
        {
            actor.Limpiar();
        }

        contexto.Response.StatusCode = StatusCodes.Status200OK;
        contexto.Response.Headers.CacheControl = "no-store";
    }

    /// <summary>Lee a lo más <paramref name="tope"/> bytes; <see langword="null"/> si el cuerpo lo supera.</summary>
    internal static async Task<byte[]?> LeerElCuerpo(Stream flujo, int tope, CancellationToken cancelacion)
    {
        using var memoria = new MemoryStream();
        var bufer = new byte[2048];
        int leidos;
        while ((leidos = await flujo.ReadAsync(bufer, cancelacion).ConfigureAwait(false)) > 0)
        {
            if (memoria.Length + leidos > tope)
            {
                return null;
            }

            memoria.Write(bufer, 0, leidos);
        }

        return memoria.ToArray();
    }

    internal static ErrorDeAplicacion Validacion(string campo, string clave) => new(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo(campo, clave)]);
}

/// <summary>
/// Los endpoints de Notificaciones del E-commerce (T20, T21; NOT-01, NOT-06, NOT-09, NOT-13; AC-NOT-01, AC-NOT-05, AC-NOT-08, AC-NOT-13), bajo <c>/api/ecommerce/v1</c>. <b>Presentación pura</b>: traducen HTTP, llaman a la Aplicación por su
/// contrato público (<see cref="IRecepcionDeAvisos"/>, <see cref="IDesactivacionDeNumeros"/>, <see cref="IConsultaDeAvisos"/>) y devuelven <c>application/problem+json</c> con <c>ECO-…</c>. La solicitud del Inventario lleva la credencial de
/// alcance <c>solicitar-aviso</c> (la verifica el middleware de la superficie de servicio: sin ella, <c>ECO-NO-AUTENTICADO</c> y un evento de seguridad); el mismo evento repetido responde 202 sin crear otro aviso. La desactivación y la consulta
/// son <b>solo de la administración central</b> (la Aplicación lo decide con el actor verificado, nunca una cabecera); el cuerpo de la desactivación no se registra y la consulta nunca devuelve el número.
/// </summary>
public static class EndpointsDeNotificaciones
{
    public const string RutaDeLasSolicitudesDelInventario = "/api/ecommerce/v1/integraciones/inventario/avisos";

    public const string OperacionDeLasSolicitudesDelInventario = "ecommerce.avisos.solicitar-del-inventario";

    public const string RutaDeLosNumerosDesactivados = "/api/ecommerce/v1/avisos/numeros-desactivados";

    public const string RutaDeLaReactivacion = "/api/ecommerce/v1/avisos/numeros-desactivados/reactivar";

    public const string RutaDeLaConsulta = "/api/ecommerce/v1/avisos";

    /// <summary>La solicitud de un aviso cabe holgada en 8 KiB (un evento, una plantilla, un código y unos campos cortos).</summary>
    public const int TopeDeLaSolicitudDelInventario = 8 * 1024;

    public const int TopeDeLaAdministracion = 2 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public static IEndpointRouteBuilder MapearNotificaciones(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);
        rutas.MapPost(RutaDeLasSolicitudesDelInventario, SolicitudDelInventario)
            .WithMetadata(new OperacionDeServicioAttribute(OperacionDeLasSolicitudesDelInventario), new RequestSizeLimitAttribute(TopeDeLaSolicitudDelInventario))
            .DisableAntiforgery();
        rutas.MapPost(RutaDeLosNumerosDesactivados, Desactivar).WithMetadata(new RequestSizeLimitAttribute(TopeDeLaAdministracion)).DisableAntiforgery();
        rutas.MapPost(RutaDeLaReactivacion, Reactivar).WithMetadata(new RequestSizeLimitAttribute(TopeDeLaAdministracion)).DisableAntiforgery();
        rutas.MapGet(RutaDeLaConsulta, Consultar);
        return rutas;
    }

    private static async Task<IResult> SolicitudDelInventario(HttpContext contexto, OpcionesDeErrores errores, IRecepcionDeAvisos recepcion, IEstablecedorDeActor actor)
    {
        var cuerpo = await Leer<SolicitudDeAvisoDeInventarioDto>(contexto, TopeDeLaSolicitudDelInventario).ConfigureAwait(false);
        if (cuerpo is null || cuerpo.EventoId is null || cuerpo.Plantilla is null || cuerpo.CodigoDeSucursal is null || cuerpo.Campos is null)
        {
            return await Falla(contexto, errores, EstadosDeEntregaDeN8nEnLaApi.Validacion("cuerpo", "invalido")).ConfigureAwait(false);
        }

        // El actor es la integración del Inventario (la credencial ya se verificó): ningún actor sale de una cabecera.
        actor.Establecer(ActorActual.DeIntegracion("inventario"));
        try
        {
            var resultado = await recepcion.Recibir(new SolicitudDeAvisoDelInventario(cuerpo.EventoId, cuerpo.Plantilla, cuerpo.CodigoDeSucursal, cuerpo.Campos), contexto.RequestAborted).ConfigureAwait(false);
            return resultado switch
            {
                ResultadoDeLaSolicitudDeAviso.Creado or ResultadoDeLaSolicitudDeAviso.OmitidoSinConsentimiento or ResultadoDeLaSolicitudDeAviso.YaExistia => Aceptado(contexto),
                ResultadoDeLaSolicitudDeAviso.Rechazado => await Falla(contexto, errores, EstadosDeEntregaDeN8nEnLaApi.Validacion("solicitud", "invalida")).ConfigureAwait(false),
                _ => await Falla(contexto, errores, new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible")).ConfigureAwait(false),
            };
        }
        finally
        {
            actor.Limpiar();
        }
    }

    private static async Task<IResult> Desactivar(HttpContext contexto, OpcionesDeErrores errores, IDesactivacionDeNumeros numeros, IActorActual actor)
    {
        if (SinUsuario(actor) is { } sinUsuario)
        {
            return await Falla(contexto, errores, sinUsuario).ConfigureAwait(false);
        }

        var cuerpo = await Leer<DesactivarNumeroDto>(contexto, TopeDeLaAdministracion).ConfigureAwait(false);
        if (cuerpo?.Numero is null || cuerpo.Motivo is null)
        {
            return await Falla(contexto, errores, EstadosDeEntregaDeN8nEnLaApi.Validacion("cuerpo", "invalido")).ConfigureAwait(false);
        }

        var resultado = await numeros.Desactivar(cuerpo.Numero, cuerpo.Motivo, contexto.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito ? SinContenido(contexto) : await Falla(contexto, errores, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> Reactivar(HttpContext contexto, OpcionesDeErrores errores, IDesactivacionDeNumeros numeros, IActorActual actor)
    {
        if (SinUsuario(actor) is { } sinUsuario)
        {
            return await Falla(contexto, errores, sinUsuario).ConfigureAwait(false);
        }

        var cuerpo = await Leer<ReactivarNumeroDto>(contexto, TopeDeLaAdministracion).ConfigureAwait(false);
        if (cuerpo?.Numero is null)
        {
            return await Falla(contexto, errores, EstadosDeEntregaDeN8nEnLaApi.Validacion("cuerpo", "invalido")).ConfigureAwait(false);
        }

        var resultado = await numeros.Reactivar(cuerpo.Numero, contexto.RequestAborted).ConfigureAwait(false);
        return resultado.EsExito ? SinContenido(contexto) : await Falla(contexto, errores, resultado.Error!).ConfigureAwait(false);
    }

    private static async Task<IResult> Consultar(HttpContext contexto, OpcionesDeErrores errores, IConsultaDeAvisos consulta, IActorActual actor)
    {
        if (SinUsuario(actor) is { } sinUsuario)
        {
            return await Falla(contexto, errores, sinUsuario).ConfigureAwait(false);
        }

        var consultaHttp = contexto.Request.Query;
        if (!Entero(consultaHttp["pagina"].ToString(), 1, out var pagina) || !Entero(consultaHttp["tamano"].ToString(), 20, out var tamano))
        {
            return await Falla(contexto, errores, EstadosDeEntregaDeN8nEnLaApi.Validacion("pagina", "invalida")).ConfigureAwait(false);
        }

        var resultado = await consulta.Listar(consultaHttp["estado"].ToString() is { Length: > 0 } estado ? estado : null, pagina, tamano, contexto.RequestAborted).ConfigureAwait(false);
        if (!resultado.EsExito)
        {
            return await Falla(contexto, errores, resultado.Error!).ConfigureAwait(false);
        }

        contexto.Response.Headers.CacheControl = "no-store";
        var pagina_ = resultado.Valor;
        return Results.Json(new PaginaDeAvisosDto(
            [.. pagina_.Avisos.Select(a => new AvisoDto(a.Id, a.EventoId, a.Destinatario == DestinatarioDelAviso.Cliente ? "cliente" : "sucursal", a.Plantilla, a.Estado, a.Intentos, a.Creado, a.FechaFinal, a.MotivoDeCierre))],
            pagina_.Pagina,
            pagina_.Tamano,
            pagina_.Total));
    }

    /// <summary>Sin un usuario autenticado no hay a quién preguntarle: 401 uniforme. El rol lo decide la Aplicación.</summary>
    private static ErrorDeAplicacion? SinUsuario(IActorActual actor) =>
        actor.Actual is { Tipo: TipoDeActor.Usuario, UsuarioId: not null } ? null : new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, "no-autenticado");

    private static bool Entero(string texto, int porDefecto, out int valor)
    {
        if (string.IsNullOrEmpty(texto))
        {
            valor = porDefecto;
            return true;
        }

        return int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out valor);
    }

    private static async Task<T?> Leer<T>(HttpContext contexto, int tope)
        where T : class
    {
        if (contexto.Request.ContentLength is > 0 and var largo && largo > tope)
        {
            return null;
        }

        var bytes = await EstadosDeEntregaDeN8nEnLaApi.LeerElCuerpo(contexto.Request.Body, tope, contexto.RequestAborted).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(bytes, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IResult Aceptado(HttpContext contexto)
    {
        contexto.Response.Headers.CacheControl = "no-store";
        return Results.StatusCode(StatusCodes.Status202Accepted);
    }

    private static IResult SinContenido(HttpContext contexto)
    {
        contexto.Response.Headers.CacheControl = "no-store";
        return Results.NoContent();
    }

    private static async Task<IResult> Falla(HttpContext contexto, OpcionesDeErrores errores, ErrorDeAplicacion error)
    {
        await ProblemasDelCarrito.Escribir(contexto, errores, error).ConfigureAwait(false);
        return Results.Empty;
    }
}
