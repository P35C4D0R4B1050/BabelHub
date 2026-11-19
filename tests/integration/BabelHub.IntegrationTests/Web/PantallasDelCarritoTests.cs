using System.Net;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Formato;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.IntegrationTests.Carrito;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// T33: la pantalla del carrito (visitante y cuenta) sobre el anfitrión web con las vistas reales (AC-CAR-01, AC-CAR-02, AC-CAR-03, AC-CAR-25): líneas con precio en soles, disponibilidad con texto e icono y su frescura, avisos de retiro y de ajuste por la fusión, el resumen, los estados
/// (vacío, error con reintento, servicio no disponible) y los formularios con su token antifalsificación; los fallos de un <c>POST</c> se pintan en pantalla para un navegador.
/// </summary>
public sealed class PantallasDelCarritoTests
{
    private static readonly Guid Impreso = Guid.Parse("e0000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Digital = Guid.Parse("e0000000-0000-0000-0000-0000000000a2");
    private static readonly Guid Agotado = Guid.Parse("e0000000-0000-0000-0000-0000000000a3");

    private sealed class ActorFijo(ActorActual actual) : IActorActual
    {
        public ActorActual? Actual { get; } = actual;
    }

    private sealed class ListaDePrueba : IListaDeSucursales
    {
        public Task<ListaDeSucursales?> Obtener(CancellationToken cancelacion = default) => Task.FromResult<ListaDeSucursales?>(new ListaDeSucursales([new("AYA", "Ayacucho")], true));
    }

    private static DisponibilidadDeEdicion Disp(Guid edicion, EstadoDeDisponibilidad estado, int? vendible, FrescuraDeLaDisponibilidad frescura = FrescuraDeLaDisponibilidad.Reciente) =>
        new(edicion, estado, vendible, [], frescura, DateTimeOffset.UtcNow, null);

    private static LineaDelCarritoVista Linea(Guid edicion, string titulo, FormatoDeEdicion formato, int cantidad, decimal precio, DisponibilidadDeEdicion? disponibilidad, bool retirada = false) =>
        new(edicion, titulo, "9780307474728", formato, cantidad, precio, precio * cantidad, disponibilidad, retirada);

    private static ResultadoDeCarrito Carrito(IEnumerable<LineaDelCarritoVista> lineas, IEnumerable<AvisoDelCarrito>? avisos = null, bool compraEnCurso = false, bool sinPrecios = false)
    {
        var l = lineas.ToList();
        return new ResultadoDeCarrito(new VistaDelCarrito(l, l.Sum(x => x.Importe), [.. avisos ?? []], compraEnCurso, sinPrecios), null, false);
    }

    private static async Task<AnfitrionWeb> Iniciar(CarritoDelClienteFalso falso, bool conSesion = true)
    {
        var actor = new ActorFijo(conSesion ? ActorActual.DeUsuario(Guid.NewGuid(), "cliente", null) : ActorActual.Anonimo());

        return await AnfitrionWeb.Iniciar(servicios =>
        {
            servicios.AddSingleton<ICarritoDelCliente>(falso);
            servicios.AddSingleton<IActorActual>(actor);
            servicios.AddSingleton<IListaDeSucursales>(new ListaDePrueba());
            servicios.AgregarProteccionContraFalsificacion();
            servicios.AddSingleton(new OpcionesDelHostWeb(HostWeb.Tienda, []));
        });
    }

    private static async Task<string> Pedir(AnfitrionWeb anfitrion, string ruta = "/carrito", bool htmx = false)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        if (htmx)
        {
            solicitud.Headers.TryAddWithoutValidation("HX-Request", "true");
        }

        using var respuesta = await anfitrion.Cliente.SendAsync(solicitud);
        return WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
    }

    private static CarritoDelClienteFalso ConLineas() => new()
    {
        Respuesta = Resultado<ResultadoDeCarrito>.Exito(Carrito(
        [
            Linea(Impreso, "Estructuras de datos", FormatoDeEdicion.Impresa, 3, 129.90m, Disp(Impreso, EstadoDeDisponibilidad.Disponible, 7)),
            Linea(Digital, "Cien años de soledad", FormatoDeEdicion.Digital, 1, 49m, Disp(Digital, EstadoDeDisponibilidad.DisponibleAlInstante, null)),
        ])),
    };

    // ---------- Con líneas ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-01")]
    [Trait("Criterio", "AC-CAR-03")]
    public async Task Las_lineas_se_pintan_con_titulo_formato_precio_en_soles_e_importe_y_el_resumen_suma_el_subtotal()
    {
        await using var anfitrion = await Iniciar(ConLineas());

        var html = await Pedir(anfitrion);

        Assert.Contains("Estructuras de datos", html, StringComparison.Ordinal);
        Assert.Contains("Libro impreso", html, StringComparison.Ordinal);
        Assert.Contains("Libro digital", html, StringComparison.Ordinal);
        Assert.Contains("3 × " + FormatoPeruano.Soles(129.90m), html, StringComparison.Ordinal);
        Assert.Contains(FormatoPeruano.Soles(389.70m), html, StringComparison.Ordinal);
        Assert.Contains(FormatoPeruano.Soles(438.70m), html, StringComparison.Ordinal);
        Assert.Contains("Se elige en el siguiente paso", html, StringComparison.Ordinal);
        Assert.Contains("15 minutos", html, StringComparison.Ordinal);
        Assert.Contains("biblioteca", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-03")]
    public async Task La_disponibilidad_se_dice_con_texto_e_icono_y_un_dato_viejo_lo_avisa()
    {
        var agotada = Linea(Agotado, "Libro agotado", FormatoDeEdicion.Impresa, 1, 20m, Disp(Agotado, EstadoDeDisponibilidad.Agotada, 0));
        var dudosa = Linea(Impreso, "Libro dudoso", FormatoDeEdicion.Impresa, 1, 30m, Disp(Impreso, EstadoDeDisponibilidad.NoSePuedeConfirmar, null, FrescuraDeLaDisponibilidad.NoSePuedeConfirmar));
        var viejo = Linea(Digital, "Libro con dato viejo", FormatoDeEdicion.Impresa, 1, 25m, Disp(Digital, EstadoDeDisponibilidad.Disponible, 4, FrescuraDeLaDisponibilidad.PuedeEstarDesactualizada));
        var falso = new CarritoDelClienteFalso { Respuesta = Resultado<ResultadoDeCarrito>.Exito(Carrito([agotada, dudosa, viejo])) };
        await using var anfitrion = await Iniciar(falso);

        var html = await Pedir(anfitrion);

        Assert.Contains("Agotado", html, StringComparison.Ordinal);
        Assert.Contains("No se puede confirmar ahora", html, StringComparison.Ordinal);
        Assert.Contains("Hay stock en la red", html, StringComparison.Ordinal);
        Assert.Contains("Este dato puede estar desactualizado.", html, StringComparison.Ordinal);
        Assert.Contains("Ya no hay unidades: quita este libro", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-02")]
    public async Task Cada_formulario_lleva_su_token_la_cantidad_de_un_impreso_tiene_limites_y_una_digital_no_se_cambia()
    {
        await using var anfitrion = await Iniciar(ConLineas());

        var html = await Pedir(anfitrion);

        Assert.Contains($"action=\"/carrito/lineas/{Impreso:D}\"", html, StringComparison.Ordinal);
        Assert.Contains($"action=\"/carrito/lineas/{Impreso:D}/quitar\"", html, StringComparison.Ordinal);
        Assert.Contains($"action=\"/carrito/lineas/{Digital:D}/quitar\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"action=\"/carrito/lineas/{Digital:D}\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"cantidad\" value=\"3\" min=\"1\" max=\"7\"", html, StringComparison.Ordinal);
        Assert.Contains("Una licencia por cuenta.", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Quitar «Estructuras de datos» del carrito\"", html, StringComparison.Ordinal);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(html, "name=\"__RequestVerificationToken\"").Count >= 4);
    }

    [Fact]
    public async Task Con_sesion_continuar_inicia_el_checkout_con_un_POST_y_sin_sesion_invita_a_ingresar()
    {
        await using var conSesion = await Iniciar(ConLineas());
        await using var visitante = await Iniciar(ConLineas(), conSesion: false);

        var logueado = await Pedir(conSesion);
        var anonimo = await Pedir(visitante);

        Assert.Contains("action=\"/checkout/iniciar\"", logueado, StringComparison.Ordinal);
        Assert.Contains("Continuar con la compra", logueado, StringComparison.Ordinal);
        Assert.DoesNotContain("action=\"/checkout/iniciar\"", anonimo, StringComparison.Ordinal);
        Assert.Contains("href=\"/ingreso\"", anonimo, StringComparison.Ordinal);
        Assert.Contains("Tu carrito se conserva al ingresar", anonimo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Con_una_compra_en_curso_se_ofrece_volver_a_ella()
    {
        var falso = new CarritoDelClienteFalso { Respuesta = Resultado<ResultadoDeCarrito>.Exito(Carrito([Linea(Impreso, "Libro", FormatoDeEdicion.Impresa, 1, 10m, Disp(Impreso, EstadoDeDisponibilidad.Disponible, 5))], compraEnCurso: true)) };
        await using var anfitrion = await Iniciar(falso);

        var html = await Pedir(anfitrion);

        Assert.Contains("href=\"/checkout\"", html, StringComparison.Ordinal);
        Assert.Contains("Volver a mi compra", html, StringComparison.Ordinal);
        Assert.DoesNotContain("action=\"/checkout/iniciar\"", html, StringComparison.Ordinal);
    }

    // ---------- Problemas que impiden continuar ----------

    [Fact]
    public async Task Una_linea_agotada_o_retirada_deja_inactivo_continuar_y_lo_explica_sin_depender_solo_del_color()
    {
        var falso = new CarritoDelClienteFalso
        {
            Respuesta = Resultado<ResultadoDeCarrito>.Exito(Carrito(
            [
                Linea(Impreso, "Libro retirado", FormatoDeEdicion.Digital, 1, 49m, null, retirada: true),
            ])),
        };
        await using var anfitrion = await Iniciar(falso);

        var html = await Pedir(anfitrion);

        Assert.Contains("Retirada del catálogo", html, StringComparison.Ordinal);
        Assert.Contains("aria-disabled=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("Resuelve los avisos de arriba para continuar con la compra.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("action=\"/checkout/iniciar\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_precios_confirmados_se_dice_y_el_subtotal_no_se_inventa()
    {
        var falso = new CarritoDelClienteFalso { Respuesta = Resultado<ResultadoDeCarrito>.Exito(Carrito([Linea(Impreso, "Libro", FormatoDeEdicion.Impresa, 1, 0m, null)], sinPrecios: true)) };
        await using var anfitrion = await Iniciar(falso);

        var html = await Pedir(anfitrion);

        Assert.Contains("No pudimos confirmar los precios ahora", html, StringComparison.Ordinal);
        Assert.Contains("No se puede confirmar", html, StringComparison.Ordinal);
        Assert.DoesNotContain(FormatoPeruano.Soles(0m), html, StringComparison.Ordinal);
        Assert.Contains("Reintentar", html, StringComparison.Ordinal);
    }

    // ---------- Avisos ----------

    [Fact]
    public async Task Los_avisos_de_retiro_y_de_ajuste_por_la_fusion_se_leen_en_una_region_viva_con_su_boton_Entendido()
    {
        var aviso1 = new AvisoDelCarrito(Guid.Parse("a0000000-0000-0000-0000-000000000001"), TipoDeAvisoDelCarrito.EdicionRetirada, Impreso, "Libro retirado", null, null, DateTimeOffset.UtcNow);
        var aviso2 = new AvisoDelCarrito(Guid.Parse("a0000000-0000-0000-0000-000000000002"), TipoDeAvisoDelCarrito.FusionAjustada, Digital, "Libro ajustado", MotivoDeAjusteDeAviso.RecortadaAlMaximo, 4, DateTimeOffset.UtcNow);
        var falso = new CarritoDelClienteFalso { Respuesta = Resultado<ResultadoDeCarrito>.Exito(Carrito([Linea(Impreso, "Libro", FormatoDeEdicion.Impresa, 1, 10m, Disp(Impreso, EstadoDeDisponibilidad.Disponible, 5))], [aviso1, aviso2])) };
        await using var anfitrion = await Iniciar(falso);

        var html = await Pedir(anfitrion);

        Assert.Contains("aria-live=\"polite\"", html, StringComparison.Ordinal);
        Assert.Contains("«Libro retirado» fue retirada del catálogo y se quitó de tu carrito.", html, StringComparison.Ordinal);
        Assert.Contains("Ajustamos «Libro ajustado» a 4 unidades, el máximo disponible.", html, StringComparison.Ordinal);
        Assert.Contains("action=\"/carrito/avisos/a0000000-0000-0000-0000-000000000001/visto\"", html, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "Entendido").Count);
    }

    // ---------- Estados ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-25")]
    public async Task Un_carrito_vacio_se_dice_con_el_estado_vacio_y_lleva_al_catalogo()
    {
        await using var anfitrion = await Iniciar(new CarritoDelClienteFalso());

        var html = await Pedir(anfitrion);

        Assert.Contains("Tu carrito está vacío", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/catalogo\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Resumen", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_servicio_que_no_responde_se_pinta_como_no_disponible_con_reintento_y_sin_trazas()
    {
        var falso = new CarritoDelClienteFalso { Respuesta = Resultado<ResultadoDeCarrito>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible")) };
        await using var anfitrion = await Iniciar(falso);

        using var solicitud = new HttpRequestMessage(HttpMethod.Get, "/carrito");
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        using var respuesta = await anfitrion.Cliente.SendAsync(solicitud);
        var html = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Contains("No podemos mostrar esto ahora", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/carrito\"", html, StringComparison.Ordinal);
        Assert.Contains("Reintentar", html, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    // ---------- Seguridad de la salida ----------

    [Fact]
    public async Task El_titulo_de_un_libro_con_marcado_se_codifica_y_nunca_se_ejecuta()
    {
        var falso = new CarritoDelClienteFalso { Respuesta = Resultado<ResultadoDeCarrito>.Exito(Carrito([Linea(Impreso, "<script>alert(1)</script>", FormatoDeEdicion.Impresa, 1, 10m, Disp(Impreso, EstadoDeDisponibilidad.Disponible, 5))])) };
        await using var anfitrion = await Iniciar(falso);

        using var solicitud = new HttpRequestMessage(HttpMethod.Get, "/carrito");
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        using var respuesta = await anfitrion.Cliente.SendAsync(solicitud);
        var crudo = await respuesta.Content.ReadAsStringAsync();

        Assert.DoesNotContain("<script>alert(1)</script>", crudo, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", crudo, StringComparison.Ordinal);
    }

    // ---------- Fallos de una acción ----------

    [Fact]
    public async Task El_fallo_de_una_accion_se_pinta_en_pantalla_para_un_navegador_con_el_texto_del_codigo_y_el_estado_del_modulo()
    {
        var falso = new CarritoDelClienteFalso { Respuesta = Resultado<ResultadoDeCarrito>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, "carrito-sin-stock")) };
        await using var anfitrion = await Iniciar(falso);
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, "/carrito/lineas") { Content = new FormUrlEncodedContent([new("edicionId", Impreso.ToString("D")), new("cantidad", "9")]) };
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        solicitud.Headers.TryAddWithoutValidation("Origin", "https://localhost");
        solicitud.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");

        using var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        // Sin el token antifalsificación el filtro global rechaza antes de la acción: el navegador nunca llega a la Aplicación.
        Assert.False(respuesta.IsSuccessStatusCode);
        Assert.Empty(falso.Llamadas);
    }
}
