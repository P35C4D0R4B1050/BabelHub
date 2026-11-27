using System.Globalization;
using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Admin.Controllers;

/// <summary>La pantalla del costo de envío por sucursal: los costos vigentes y, tras guardar, la sucursal que se cambió.</summary>
public sealed class CostosDeEnvioVista : ModeloBaseDePagina
{
    public CostosDeEnvio Costos { get; set; } = null!;

    /// <summary>La sucursal recién guardada, solo si está en la lista.</summary>
    public CostoDeEnvioDeSucursal? Guardada { get; set; }

    /// <summary>El importe como lo admite un campo numérico: con punto y dos decimales.</summary>
    public static string ParaElCampo(decimal importe) => importe.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>
/// El costo de envío a domicilio de cada sucursal para la administración central (T48; CAR-46, CAR-47, CAR-48): <c>GET /administracion/costos-de-envio</c> los lista y <c>POST</c> a la misma ruta fija uno. Presentación pura:
/// <see cref="IAdministracionDeEnvios"/> decide quién puede (otro rol recibe un 403 y queda el acceso denegado), qué sucursal existe y qué importe se admite.
/// </summary>
[Area("Admin")]
public sealed class CostosDeEnvioController(IAdministracionDeEnvios envios, ContextoDePagina pantalla, OpcionesDeErrores errores) : Controller
{
    public const string Ruta = "/administracion/costos-de-envio";

    private const string Guardada = "guardada";

    [HttpGet(Ruta)]
    public async Task<IActionResult> Index([FromQuery(Name = Guardada)] string? guardada, CancellationToken cancelacion = default)
    {
        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext, [Guardada]) is { } consulta)
        {
            return await Falla(consulta, cancelacion);
        }

        var resultado = await envios.Listar(cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, cancelacion);
        }

        var vista = await pantalla.Completar(
            new CostosDeEnvioVista { Costos = resultado.Valor, Guardada = resultado.Valor.Sucursales.FirstOrDefault(s => string.Equals(s.CodigoDeSucursal, guardada, StringComparison.Ordinal)) },
            "Costo de envío por sucursal",
            AreaDeLaPagina.Admin,
            cancelacion);
        Response.Headers.CacheControl = "no-store";
        return View("Index", vista);
    }

    [HttpPost(Ruta)]
    public async Task<IActionResult> Guardar(CancellationToken cancelacion)
    {
        var (campos, error) = await EntradasHttpDelCarrito.Leer(HttpContext, ["sucursal", "importe"], cancelacion);
        if (campos is null)
        {
            return await Falla(error!, cancelacion);
        }

        if (!campos.TryGetValue("sucursal", out var sucursal))
        {
            return await Falla(EntradasHttpDelCarrito.Falta("sucursal"), cancelacion);
        }

        if (!campos.TryGetValue("importe", out var texto) || texto.Length == 0)
        {
            return await Falla(EntradasHttpDelCarrito.Falta("importe"), cancelacion);
        }

        // Solo dígitos, un punto decimal y el signo (un negativo se dice «fuera de rango»); el rango y los decimales los decide el módulo.
        if (texto.Length > 12 || !decimal.TryParse(texto, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var importe))
        {
            return await Falla(EntradasHttpDelCarrito.Invalido("importe"), cancelacion);
        }

        var resultado = await envios.Cambiar(new CambiarCostoDeEnvioEntrada(sucursal, importe), cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, cancelacion);
        }

        var destino = $"{Ruta}?{Guardada}={Uri.EscapeDataString(resultado.Valor.CodigoDeSucursal)}";
        Response.Headers.CacheControl = "no-store";
        if (Request.EsHtmx())
        {
            return new HxRedirectResult(destino);
        }

        Response.StatusCode = StatusCodes.Status303SeeOther;
        Response.Headers.Location = destino;
        return new EmptyResult();
    }

    private async Task<IActionResult> Falla(ErrorDeAplicacion error, CancellationToken cancelacion)
    {
        if (PantallasDelCarrito.PrefiereHtml(Request))
        {
            return await PantallasDelCarrito.Escribir(this, pantalla, errores, error, Ruta, cancelacion);
        }

        await ProblemasDelCarrito.Escribir(HttpContext, errores, error);
        return new EmptyResult();
    }
}
