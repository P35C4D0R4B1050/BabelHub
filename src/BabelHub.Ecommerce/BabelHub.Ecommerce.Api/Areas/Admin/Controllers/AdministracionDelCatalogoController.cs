using BabelHub.Ecommerce.Api.Catalogo;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace BabelHub.Ecommerce.Api.Areas.Admin.Controllers;

/// <summary>
/// La administración del catálogo (plan §4, CAT-14, AC-CAT-11): las acciones que llaman las pantallas de la administración (<c>ECO-2d</c>) y las que suben los archivos. <b>Sin reglas de negocio</b>: traducen HTTP, leen cuerpos estrictos (un campo desconocido es validación),
/// llaman a la Aplicación por su contrato público y traducen su <see cref="Resultado{T}"/> a <c>application/problem+json</c> con los códigos <c>ECO-…</c>. Quién puede hacer cada cosa lo decide la Aplicación con el actor verificado (solo la administración central, el
/// reintento solo su autor), nunca una cabecera ni el área; el token antifalsificación lo valida el filtro global antes de la acción. No hay una API JSON pública: las pantallas de lectura (lista de obras, ficha) llegan con sus vistas.
/// </summary>
[Area("Admin")]
[RequestSizeLimit(AdministracionDelCatalogoController.TopeDelCuerpoJson)]
public sealed class AdministracionDelCatalogoController(
    IAdministracionDelCatalogo catalogo,
    IArchivosDelCatalogo archivos,
    IIngresosDeMercaderia ingresos,
    OpcionesDeErrores errores) : Controller
{
    /// <summary>El tope del cuerpo de una subida: el de un libro más un margen de 64 KiB; el tope real por finalidad lo cuenta la Aplicación con los bytes leídos (una portada pasa de 2 MiB y se rechaza).</summary>
    public const long TopeDelCuerpoDeUnaSubida = 52_428_800 + 65_536;

    /// <summary>El tope del cuerpo de las acciones JSON (obras, ediciones, ingresos): 64 KiB, mucho más que cualquier alta legítima; el rechazo por rol de la Aplicación nunca espera a parsear megabytes.</summary>
    public const long TopeDelCuerpoJson = 64 * 1024;

    /// <summary>La velocidad mínima del cuerpo de una subida: 240 bytes por segundo tras 30 segundos de espera de datos (un cliente lento no retiene un cupo).</summary>
    private const double BytesPorSegundoMinimos = 240;

    private static readonly TimeSpan EsperaDeDatos = TimeSpan.FromSeconds(30);

    // ---------- Archivos ----------

    [HttpPost("/administracion/archivos")]
    [RequestSizeLimit(TopeDelCuerpoDeUnaSubida)]
    public async Task<IActionResult> SubirArchivo(CancellationToken cancelacion)
    {
        if (FinalidadDe(Request) is not { } finalidad)
        {
            return await Falla(CuerpoDelCatalogo.Falta("finalidad"));
        }

        if (HttpContext.Features.Get<IHttpMinRequestBodyDataRateFeature>() is { } velocidad)
        {
            velocidad.MinDataRate = new MinDataRate(BytesPorSegundoMinimos, EsperaDeDatos);
        }

        // La Aplicación comprueba el rol antes de tocar el cuerpo; el flujo se lee en streaming, nunca completo en memoria ni en disco.
        var resultado = await archivos.SubirArchivo(new SubirArchivoEntrada(finalidad, Request.Body), cancelacion);
        return resultado.EsExito ? Respuesta(StatusCodes.Status201Created, resultado.Valor) : await Falla(resultado.Error!);
    }

    private static FinalidadDeLaSubida? FinalidadDe(HttpRequest solicitud)
    {
        var valores = solicitud.Query["finalidad"];
        return valores.Count == 1
            ? valores[0] switch
            {
                "portada" => FinalidadDeLaSubida.Portada,
                "libro" => FinalidadDeLaSubida.Libro,
                _ => null,
            }
            : null;
    }

    // ---------- Obras ----------

    [HttpPost("/administracion/obras")]
    public async Task<IActionResult> RegistrarObra(CancellationToken cancelacion)
    {
        if (await CuerpoDelCatalogo.Leer<RegistrarObraHttp>(Request, cancelacion) is not { } cuerpo)
        {
            return await Falla(CuerpoDelCatalogo.CuerpoInvalido);
        }

        var ediciones = new List<NuevaEdicionEntrada>();
        foreach (var edicion in cuerpo.Ediciones ?? [])
        {
            if (CuerpoDelCatalogo.AEdicion(edicion, out var error) is not { } nueva)
            {
                return await Falla(error!);
            }

            ediciones.Add(nueva);
        }

        var resultado = await catalogo.RegistrarObra(new RegistrarObraEntrada(cuerpo.Titulo, cuerpo.Autor, cuerpo.Editorial, cuerpo.Categoria, cuerpo.PortadaRef, cuerpo.Ediciones is null ? null : ediciones, cuerpo.Descripcion), cancelacion);
        return resultado.EsExito ? Respuesta(StatusCodes.Status201Created, resultado.Valor) : await Falla(resultado.Error!);
    }

    [HttpPut("/administracion/obras/{obraId:guid}")]
    public async Task<IActionResult> EditarObra(Guid obraId, CancellationToken cancelacion)
    {
        if (await CuerpoDelCatalogo.Leer<EditarObraHttp>(Request, cancelacion) is not { } cuerpo)
        {
            return await Falla(CuerpoDelCatalogo.CuerpoInvalido);
        }

        if (cuerpo.Version is not { } version)
        {
            return await Falla(CuerpoDelCatalogo.Falta("version"));
        }

        var resultado = await catalogo.EditarObra(new EditarObraEntrada(obraId, version, cuerpo.Titulo, cuerpo.Autor, cuerpo.Editorial, cuerpo.Categoria, cuerpo.PortadaRef, cuerpo.Descripcion), cancelacion);
        return resultado.EsExito ? Respuesta(StatusCodes.Status200OK, resultado.Valor) : await Falla(resultado.Error!);
    }

    [HttpPost("/administracion/obras/{obraId:guid}/baja")]
    public async Task<IActionResult> DarDeBajaObra(Guid obraId, CancellationToken cancelacion)
    {
        if (await CuerpoDelCatalogo.Leer<VersionHttp>(Request, cancelacion) is not { } cuerpo)
        {
            return await Falla(CuerpoDelCatalogo.CuerpoInvalido);
        }

        if (cuerpo.Version is not { } version)
        {
            return await Falla(CuerpoDelCatalogo.Falta("version"));
        }

        var resultado = await catalogo.DarDeBajaObra(new DarDeBajaObraEntrada(obraId, version), cancelacion);
        return resultado.EsExito ? NoContent() : await Falla(resultado.Error!);
    }

    // ---------- Ediciones ----------

    [HttpPost("/administracion/obras/{obraId:guid}/ediciones")]
    public async Task<IActionResult> RegistrarEdicion(Guid obraId, CancellationToken cancelacion)
    {
        if (await CuerpoDelCatalogo.Leer<RegistrarEdicionHttp>(Request, cancelacion) is not { } cuerpo)
        {
            return await Falla(CuerpoDelCatalogo.CuerpoInvalido);
        }

        if (cuerpo.Version is not { } version)
        {
            return await Falla(CuerpoDelCatalogo.Falta("version"));
        }

        if (CuerpoDelCatalogo.AEdicion(cuerpo.Edicion, out var error) is not { } edicion)
        {
            return await Falla(error!);
        }

        var resultado = await catalogo.RegistrarEdicion(new RegistrarEdicionEntrada(obraId, version, edicion), cancelacion);
        return resultado.EsExito ? Respuesta(StatusCodes.Status201Created, resultado.Valor) : await Falla(resultado.Error!);
    }

    [HttpPut("/administracion/obras/{obraId:guid}/ediciones/{edicionId:guid}")]
    public async Task<IActionResult> EditarEdicion(Guid obraId, Guid edicionId, CancellationToken cancelacion)
    {
        if (await CuerpoDelCatalogo.Leer<EditarEdicionHttp>(Request, cancelacion) is not { } cuerpo)
        {
            return await Falla(CuerpoDelCatalogo.CuerpoInvalido);
        }

        if (cuerpo.Version is not { } version)
        {
            return await Falla(CuerpoDelCatalogo.Falta("version"));
        }

        var licencia = CuerpoDelCatalogo.ALicencia(cuerpo.Licencia, out var error);
        if (error is not null)
        {
            return await Falla(error);
        }

        var resultado = await catalogo.EditarEdicion(new EditarEdicionEntrada(obraId, edicionId, version, cuerpo.Precio, licencia, cuerpo.ArchivoRef), cancelacion);
        return resultado.EsExito ? Respuesta(StatusCodes.Status200OK, resultado.Valor) : await Falla(resultado.Error!);
    }

    [HttpPost("/administracion/obras/{obraId:guid}/ediciones/{edicionId:guid}/baja")]
    public async Task<IActionResult> DarDeBajaEdicion(Guid obraId, Guid edicionId, CancellationToken cancelacion)
    {
        if (await CuerpoDelCatalogo.Leer<VersionHttp>(Request, cancelacion) is not { } cuerpo)
        {
            return await Falla(CuerpoDelCatalogo.CuerpoInvalido);
        }

        if (cuerpo.Version is not { } version)
        {
            return await Falla(CuerpoDelCatalogo.Falta("version"));
        }

        var resultado = await catalogo.DarDeBajaEdicion(new DarDeBajaEdicionEntrada(obraId, edicionId, version), cancelacion);
        return resultado.EsExito ? NoContent() : await Falla(resultado.Error!);
    }

    [HttpPost("/administracion/obras/{obraId:guid}/ediciones/{edicionId:guid}/retirar")]
    public async Task<IActionResult> RetirarEdicionDigital(Guid obraId, Guid edicionId, CancellationToken cancelacion)
    {
        if (await CuerpoDelCatalogo.Leer<RetirarHttp>(Request, cancelacion) is not { } cuerpo)
        {
            return await Falla(CuerpoDelCatalogo.CuerpoInvalido);
        }

        var resultado = await catalogo.RetirarEdicionDigital(new RetirarEdicionDigitalEntrada(obraId, edicionId, cuerpo.Motivo), cancelacion);
        return resultado.EsExito ? NoContent() : await Falla(resultado.Error!);
    }

    // ---------- Ingresos de mercadería ----------

    /// <summary>Emite el token firmado del formulario de ingreso sin escribir nada (la clave de idempotencia nace al mostrar el formulario).</summary>
    [HttpGet("/administracion/ediciones/{edicionId:guid}/ingresos/nuevo")]
    public async Task<IActionResult> NuevoIngreso(Guid edicionId, CancellationToken cancelacion)
    {
        var resultado = await ingresos.EmitirClave(new EmitirClaveDeIngresoEntrada(edicionId), cancelacion);
        return resultado.EsExito ? Respuesta(StatusCodes.Status200OK, resultado.Valor) : await Falla(resultado.Error!);
    }

    [HttpPost("/administracion/ediciones/{edicionId:guid}/ingresos")]
    public async Task<IActionResult> RegistrarIngreso(Guid edicionId, CancellationToken cancelacion)
    {
        if (await CuerpoDelCatalogo.Leer<RegistrarIngresoHttp>(Request, cancelacion) is not { } cuerpo)
        {
            return await Falla(CuerpoDelCatalogo.CuerpoInvalido);
        }

        if (cuerpo.Cantidad is not { } cantidad)
        {
            return await Falla(CuerpoDelCatalogo.Falta("cantidad"));
        }

        var resultado = await ingresos.Registrar(new RegistrarIngresoEntrada(edicionId, cantidad, cuerpo.Referencia, cuerpo.Nota, cuerpo.Token), cancelacion);
        return resultado.EsExito ? Respuesta(StatusCodes.Status201Created, resultado.Valor) : await Falla(resultado.Error!);
    }

    [HttpGet("/administracion/ediciones/{edicionId:guid}/ingresos")]
    public async Task<IActionResult> ListarIngresos(Guid edicionId, CancellationToken cancelacion)
    {
        var resultado = await ingresos.Listar(new ListarIngresosEntrada(edicionId), cancelacion);
        return resultado.EsExito ? Respuesta(StatusCodes.Status200OK, resultado.Valor) : await Falla(resultado.Error!);
    }

    [HttpPost("/administracion/ediciones/{edicionId:guid}/ingresos/{ingresoId:guid}/reintentar")]
    public async Task<IActionResult> ReintentarIngreso(Guid edicionId, Guid ingresoId, CancellationToken cancelacion)
    {
        var resultado = await ingresos.Reintentar(new ReintentarIngresoEntrada(ingresoId, edicionId), cancelacion);
        return resultado.EsExito ? Respuesta(StatusCodes.Status200OK, resultado.Valor) : await Falla(resultado.Error!);
    }

    [HttpPost("/administracion/ediciones/{edicionId:guid}/ingresos/{ingresoId:guid}/conciliar")]
    public async Task<IActionResult> ConciliarIngreso(Guid edicionId, Guid ingresoId, CancellationToken cancelacion)
    {
        if (await CuerpoDelCatalogo.Leer<ConciliarHttp>(Request, cancelacion) is not { } cuerpo)
        {
            return await Falla(CuerpoDelCatalogo.CuerpoInvalido);
        }

        if (cuerpo.Resultado is not { } resolucion)
        {
            return await Falla(CuerpoDelCatalogo.Falta("resultado"));
        }

        var resultado = await ingresos.Conciliar(new ConciliarIngresoEntrada(ingresoId, resolucion, cuerpo.ConfirmaFallido ?? false, edicionId), cancelacion);
        return resultado.EsExito ? Respuesta(StatusCodes.Status200OK, resultado.Valor) : await Falla(resultado.Error!);
    }

    // ---------- Respuestas ----------

    private JsonResult Respuesta(int estado, object valor)
    {
        Response.Headers.CacheControl = "no-store";
        return new JsonResult(valor, CuerpoDelCatalogo.Opciones) { StatusCode = estado };
    }

    /// <summary>El problema uniforme con su código <c>ECO-…</c>; nunca se cachea.</summary>
    private async Task<IActionResult> Falla(ErrorDeAplicacion error)
    {
        Response.Headers.CacheControl = "no-store";
        await ProblemasDelCatalogo.Escribir(HttpContext, errores, error);
        return new EmptyResult();
    }
}
