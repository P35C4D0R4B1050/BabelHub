using System.Net;
using BabelHub.Ecommerce.Api.Web.Formato;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// Las pantallas públicas del catálogo (T24; AC-CAT-04, AC-CAT-06, AC-CAT-07, AC-CAT-09, AC-CAT-17): la búsqueda con filtros y la ficha de la obra sobre el anfitrión web, con un <see cref="IConsultaDeCatalogo"/> de prueba. Verifican lo que se envía al catálogo, lo que se pinta (texto e icono de
/// disponibilidad, el motivo de una digital que no se ofrece, los avisos de datos desactualizados) y los estados de pantalla, también para htmx.
/// </summary>
public sealed class PantallasDelCatalogoTests
{
    private static readonly Guid Obra1 = Guid.Parse("11111111-2222-3333-4444-555555555501");
    private static readonly Guid Obra2 = Guid.Parse("11111111-2222-3333-4444-555555555502");
    private static readonly string Huella = new('a', 64);

    private sealed class CatalogoDePrueba : IConsultaDeCatalogo
    {
        public Func<BusquedaDeObras, Resultado<PaginaDeObras>> Busqueda { get; set; } = b => Resultado<PaginaDeObras>.Exito(new PaginaDeObras([], b.Pagina, b.Tamano, 0));

        public Func<Guid, Resultado<ResumenDeObra>> Ficha { get; set; } = id => Resultado<ResumenDeObra>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "obra-no-encontrada"));

        public List<BusquedaDeObras> Busquedas { get; } = [];

        public Task<Resultado<ObraAlAzar?>> ObtenerObraAlAzar(CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<PaginaDeObras>> BuscarObras(BusquedaDeObras busqueda, CancellationToken cancelacion = default)
        {
            Busquedas.Add(busqueda);
            return Task.FromResult(Busqueda(busqueda));
        }

        public Task<Resultado<ResumenDeObra>> ObtenerObra(Guid obraId, CancellationToken cancelacion = default) => Task.FromResult(Ficha(obraId));

        public Task<Resultado<IReadOnlyList<EdicionConsultada>>> ObtenerEdiciones(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private sealed class ListaDePrueba(ListaDeSucursales lista) : IListaDeSucursales
    {
        public Task<ListaDeSucursales?> Obtener(CancellationToken cancelacion = default) => Task.FromResult<ListaDeSucursales?>(lista);
    }

    private static DisponibilidadDeEdicion Disp(Guid edicion, EstadoDeDisponibilidad estado, int? vendible, FrescuraDeLaDisponibilidad frescura = FrescuraDeLaDisponibilidad.Reciente, MotivoDeNoDisponibleDeEdicion? motivo = null, params DisponibilidadPorFuente[] fuentes) =>
        new(edicion, estado, vendible, fuentes, frescura, DateTimeOffset.UtcNow, motivo);

    private static EdicionDeObra Impresa(Guid edicion, decimal precio, DisponibilidadDeEdicion disponibilidad) => new(edicion, FormatoDeEdicion.Impresa, "9780000000019", precio, disponibilidad);

    private static EdicionDeObra Digital(Guid edicion, decimal precio, DisponibilidadDeEdicion disponibilidad) => new(edicion, FormatoDeEdicion.Digital, "9790000000018", precio, disponibilidad);

    private static ResumenDeObra ObraConAmbas(Guid id, string titulo, EdicionDeObra impresa, EdicionDeObra digital) =>
        new(id, titulo, "Ana Quispe", "Editorial Andina", "Técnico", Huella, [impresa, digital]);

    private static PaginaDeObras Pagina(params ResumenDeObra[] obras) => new(obras, 1, 12, obras.Length);

    private static readonly ListaDeSucursales AyacuchoYLima = new([new("AYA", "Ayacucho"), new("LIM", "Lima")], true);

    private static Task<AnfitrionWeb> Iniciar(CatalogoDePrueba catalogo) =>
        AnfitrionWeb.Iniciar(servicios =>
        {
            servicios.AddSingleton<IConsultaDeCatalogo>(catalogo);
            servicios.AddSingleton<IListaDeSucursales>(new ListaDePrueba(AyacuchoYLima));
        });

    private static async Task<string> Texto(HttpResponseMessage respuesta) => WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());

    private static ResumenDeObra ObraDeEjemplo(string titulo = "Estructuras de datos") =>
        ObraConAmbas(
            Obra1,
            titulo,
            Impresa(Guid.NewGuid(), 129.90m, Disp(Guid.NewGuid(), EstadoDeDisponibilidad.Disponible, 7, fuentes: [new("LIM", "Lima", 5), new("AYA", "Ayacucho", 2)])),
            Digital(Guid.NewGuid(), 49m, Disp(Guid.NewGuid(), EstadoDeDisponibilidad.DisponibleAlInstante, null)));

    // ---------- Búsqueda ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-06")]
    public async Task La_busqueda_envia_los_filtros_al_catalogo_y_pinta_las_tarjetas_con_portada_precio_y_disponibilidad_con_texto()
    {
        var catalogo = new CatalogoDePrueba { Busqueda = b => Resultado<PaginaDeObras>.Exito(new PaginaDeObras([ObraDeEjemplo()], b.Pagina, b.Tamano, 25)) };
        await using var anfitrion = await Iniciar(catalogo);

        using var respuesta = await anfitrion.Cliente.GetAsync("/catalogo?q=datos&formato=digital&categoria=T%C3%A9cnico&precioMin=10&precioMax=100,50&disponible=1&pagina=2");
        var html = await Texto(respuesta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var enviada = Assert.Single(catalogo.Busquedas);
        Assert.Equal(new BusquedaDeObras("datos", null, null, "Técnico", FormatoDeEdicion.Digital, 10m, 100.50m, true, 2, 12), enviada);
        Assert.Contains("Estructuras de datos", html, StringComparison.Ordinal);
        Assert.Contains("Ana Quispe", html, StringComparison.Ordinal);
        Assert.Contains($"src=\"/portadas/{Huella}\"", html, StringComparison.Ordinal);
        Assert.Contains("alt=\"Portada de Estructuras de datos\"", html, StringComparison.Ordinal);
        Assert.Contains("Desde " + FormatoPeruano.Soles(49m), html, StringComparison.Ordinal);
        Assert.Contains("Disponible al instante", html, StringComparison.Ordinal);
        Assert.Contains("En 2 sucursales", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/catalogo/obras/" + Obra1.ToString("D") + "\"", html, StringComparison.Ordinal);
        Assert.Contains("Página 2 de 3", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"prev\"", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"next\"", html, StringComparison.Ordinal);
        // El formulario conserva lo que se escribió.
        Assert.Contains("value=\"datos\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"formato\" value=\"digital\"", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-06")]
    public async Task Sin_filtros_se_busca_la_primera_pagina_y_con_htmx_solo_se_devuelven_los_resultados()
    {
        var catalogo = new CatalogoDePrueba { Busqueda = b => Resultado<PaginaDeObras>.Exito(Pagina(ObraDeEjemplo())) };
        await using var anfitrion = await Iniciar(catalogo);

        using var completa = await anfitrion.Cliente.GetAsync("/catalogo");
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, "/catalogo?q=datos");
        solicitud.Headers.Add("HX-Request", "true");
        using var parcial = await anfitrion.Cliente.SendAsync(solicitud);
        var htmlCompleto = await Texto(completa);
        var htmlParcial = await Texto(parcial);

        Assert.Equal(new BusquedaDeObras(null, null, null, null, null, null, null, false, 1, 12), catalogo.Busquedas[0]);
        Assert.Contains("<html", htmlCompleto, StringComparison.Ordinal);
        Assert.Contains("id=\"resultados\"", htmlCompleto, StringComparison.Ordinal);
        Assert.Contains("hx-target=\"#resultados\"", htmlCompleto, StringComparison.Ordinal);
        Assert.DoesNotContain("<html", htmlParcial, StringComparison.Ordinal);
        Assert.DoesNotContain("<form", htmlParcial, StringComparison.Ordinal);
        Assert.Contains("Estructuras de datos", htmlParcial, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-06")]
    public async Task Una_busqueda_sin_resultados_es_el_estado_vacio_con_limpiar_filtros()
    {
        await using var anfitrion = await Iniciar(new CatalogoDePrueba());

        using var respuesta = await anfitrion.Cliente.GetAsync("/catalogo?q=nada");
        var html = await Texto(respuesta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("No encontramos libros", html, StringComparison.Ordinal);
        Assert.Contains("Limpiar filtros", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/catalogo\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-06")]
    [InlineData("/catalogo?formato=pdf", "El formato debe ser impreso o digital.")]
    [InlineData("/catalogo?precioMax=abc", "El precio debe ser un número en soles")]
    [InlineData("/catalogo?precioMin=50&precioMax=10", "El precio mínimo no puede ser mayor que el máximo.")]
    [InlineData("/catalogo?q=a&q=b", "Algún filtro está repetido.")]
    [InlineData("/catalogo?pagina=0", "La página debe ser un número positivo.")]
    public async Task Un_filtro_con_otra_forma_es_un_aviso_sin_llamar_al_catalogo_y_sin_devolver_lo_escrito_como_html(string ruta, string aviso)
    {
        var catalogo = new CatalogoDePrueba();
        await using var anfitrion = await Iniciar(catalogo);

        using var respuesta = await anfitrion.Cliente.GetAsync(ruta);
        var html = await Texto(respuesta);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains(aviso, html, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", html, StringComparison.Ordinal);
        Assert.Empty(catalogo.Busquedas);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-06")]
    public async Task El_texto_escrito_se_codifica_y_no_se_ejecuta()
    {
        await using var anfitrion = await Iniciar(new CatalogoDePrueba());

        using var respuesta = await anfitrion.Cliente.GetAsync("/catalogo?q=%22%3E%3Cscript%3Ealert(1)%3C/script%3E");
        var crudo = await respuesta.Content.ReadAsStringAsync();

        Assert.DoesNotContain("<script>alert(1)", crudo, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)", crudo, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-09")]
    [InlineData(CategoriaDeError.NoDisponible, "x", HttpStatusCode.ServiceUnavailable, "No podemos mostrar el catálogo ahora.")]
    [InlineData(CategoriaDeError.Limite, "x", HttpStatusCode.TooManyRequests, "bh-estado")]
    [InlineData(CategoriaDeError.ReglaDeNegocio, "busqueda-demasiado-amplia", HttpStatusCode.UnprocessableEntity, "Tu búsqueda es muy amplia")]
    [InlineData(CategoriaDeError.Inesperado, "x", HttpStatusCode.InternalServerError, "Algo salió mal")]
    public async Task Los_errores_del_catalogo_son_estados_de_pantalla_sin_trazas(CategoriaDeError categoria, string clave, HttpStatusCode estado, string texto)
    {
        var catalogo = new CatalogoDePrueba { Busqueda = _ => Resultado<PaginaDeObras>.Fallo(new ErrorDeAplicacion(categoria, clave)) };
        await using var anfitrion = await Iniciar(catalogo);

        using var respuesta = await anfitrion.Cliente.GetAsync("/catalogo");
        var html = await Texto(respuesta);

        Assert.Equal(estado, respuesta.StatusCode);
        Assert.Contains(texto, html, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", html, StringComparison.Ordinal);
        Assert.Contains("id=\"resultados\"", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-09")]
    public async Task Con_datos_de_disponibilidad_de_hace_unos_minutos_o_sin_confirmar_el_catalogo_se_muestra_con_el_aviso()
    {
        var desactualizada = ObraConAmbas(
            Obra1,
            "Con dato viejo",
            Impresa(Guid.NewGuid(), 50m, Disp(Guid.NewGuid(), EstadoDeDisponibilidad.Disponible, 4, FrescuraDeLaDisponibilidad.PuedeEstarDesactualizada, fuentes: [new("LIM", "Lima", 4)])),
            Digital(Guid.NewGuid(), 20m, Disp(Guid.NewGuid(), EstadoDeDisponibilidad.DisponibleAlInstante, null)));
        var sinConfirmar = ObraConAmbas(
            Obra2,
            "Sin confirmar",
            Impresa(Guid.NewGuid(), 60m, Disp(Guid.NewGuid(), EstadoDeDisponibilidad.NoSePuedeConfirmar, null, FrescuraDeLaDisponibilidad.NoSePuedeConfirmar)),
            Digital(Guid.NewGuid(), 25m, Disp(Guid.NewGuid(), EstadoDeDisponibilidad.DisponibleAlInstante, null)));
        var catalogo = new CatalogoDePrueba { Busqueda = _ => Resultado<PaginaDeObras>.Exito(Pagina(desactualizada, sinConfirmar)) };
        await using var anfitrion = await Iniciar(catalogo);

        using var respuesta = await anfitrion.Cliente.GetAsync("/catalogo");
        var html = await Texto(respuesta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("la disponibilidad que ves puede estar desactualizada", html, StringComparison.Ordinal);
        Assert.Contains("No se puede confirmar ahora", html, StringComparison.Ordinal);
        Assert.Contains("Con dato viejo", html, StringComparison.Ordinal);
        Assert.Contains("Sin confirmar", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-17")]
    public async Task Un_impreso_agotado_se_ve_agotado_con_texto_e_icono_y_sigue_en_el_catalogo_con_su_digital()
    {
        var agotada = ObraConAmbas(
            Obra1,
            "Agotado en la red",
            Impresa(Guid.NewGuid(), 80m, Disp(Guid.NewGuid(), EstadoDeDisponibilidad.Agotada, 0)),
            Digital(Guid.NewGuid(), 30m, Disp(Guid.NewGuid(), EstadoDeDisponibilidad.DisponibleAlInstante, null)));
        var catalogo = new CatalogoDePrueba { Busqueda = _ => Resultado<PaginaDeObras>.Exito(Pagina(agotada)) };
        await using var anfitrion = await Iniciar(catalogo);

        using var respuesta = await anfitrion.Cliente.GetAsync("/catalogo");
        var html = await Texto(respuesta);

        Assert.Contains("Agotado en la red", html, StringComparison.Ordinal);
        Assert.Contains("<span>Agotado</span>", html, StringComparison.Ordinal);
        Assert.Contains("Disponible al instante", html, StringComparison.Ordinal);
        Assert.Contains("badge-error", html, StringComparison.Ordinal);
    }

    // ---------- Ficha ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-07")]
    public async Task La_ficha_muestra_las_dos_ediciones_con_el_selector_y_la_disponibilidad_por_sucursal_de_la_impresa()
    {
        var catalogo = new CatalogoDePrueba { Ficha = id => Resultado<ResumenDeObra>.Exito(ObraDeEjemplo()) };
        await using var anfitrion = await Iniciar(catalogo);
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, $"/catalogo/obras/{Obra1}");
        solicitud.Headers.Add("Cookie", "bh-sucursal=LIM");

        using var respuesta = await anfitrion.Cliente.SendAsync(solicitud);
        var html = await Texto(respuesta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("<h1", html, StringComparison.Ordinal);
        Assert.Contains("Estructuras de datos", html, StringComparison.Ordinal);
        Assert.Contains(FormatoPeruano.Soles(129.90m), html, StringComparison.Ordinal);
        Assert.Contains("href=\"?edicion=digital\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\"", html, StringComparison.Ordinal);
        Assert.Contains("Disponibilidad por sucursal", html, StringComparison.Ordinal);
        Assert.Contains("Ayacucho", html, StringComparison.Ordinal);
        Assert.Contains("Pocas unidades", html, StringComparison.Ordinal);
        Assert.Contains("Sucursal que atenderá tu pedido", html, StringComparison.Ordinal);
        Assert.DoesNotContain("En tu sucursal", html, StringComparison.Ordinal);
        Assert.Contains("scope=\"col\"", html, StringComparison.Ordinal);
        Assert.Contains($"alt=\"Portada de Estructuras de datos\"", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-07")]
    public async Task La_pestana_digital_dice_disponible_al_instante_sin_igv_y_sin_consultar_stock()
    {
        var catalogo = new CatalogoDePrueba { Ficha = id => Resultado<ResumenDeObra>.Exito(ObraDeEjemplo()) };
        await using var anfitrion = await Iniciar(catalogo);

        using var respuesta = await anfitrion.Cliente.GetAsync($"/catalogo/obras/{Obra1}?edicion=digital");
        var html = await Texto(respuesta);

        Assert.Contains(FormatoPeruano.Soles(49m), html, StringComparison.Ordinal);
        Assert.Contains("Disponible al instante", html, StringComparison.Ordinal);
        Assert.Contains("Sin IGV", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Disponibilidad por sucursal", html, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-04")]
    [InlineData(MotivoDeNoDisponibleDeEdicion.LicenciaVencida, "La licencia de esta edición digital venció y ya no se vende.")]
    [InlineData(MotivoDeNoDisponibleDeEdicion.SinPeru, "La licencia de esta edición digital no incluye Perú.")]
    [InlineData(MotivoDeNoDisponibleDeEdicion.RetiradaDeLaVenta, "Esta edición digital se retiró de la venta.")]
    public async Task Una_digital_que_no_se_ofrece_explica_el_motivo_en_su_ficha(MotivoDeNoDisponibleDeEdicion motivo, string texto)
    {
        var obra = ObraConAmbas(
            Obra2,
            "Sin digital",
            Impresa(Guid.NewGuid(), 50m, Disp(Guid.NewGuid(), EstadoDeDisponibilidad.Disponible, 3, fuentes: [new("LIM", "Lima", 3)])),
            Digital(Guid.NewGuid(), 20m, Disp(Guid.NewGuid(), EstadoDeDisponibilidad.NoSeOfrece, null, motivo: motivo)));
        var catalogo = new CatalogoDePrueba { Ficha = _ => Resultado<ResumenDeObra>.Exito(obra) };
        await using var anfitrion = await Iniciar(catalogo);

        using var respuesta = await anfitrion.Cliente.GetAsync($"/catalogo/obras/{Obra2}?edicion=digital");
        var html = await Texto(respuesta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains(texto, html, StringComparison.Ordinal);
        Assert.Contains("Puedes verlo en impreso.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Disponible al instante en tu biblioteca", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-07")]
    public async Task Una_obra_dada_de_baja_o_inexistente_es_404_con_el_catalogo_como_salida()
    {
        await using var anfitrion = await Iniciar(new CatalogoDePrueba());

        using var respuesta = await anfitrion.Cliente.GetAsync($"/catalogo/obras/{Guid.NewGuid()}");
        using var noEsGuid = await anfitrion.Cliente.GetAsync("/catalogo/obras/no-es-un-guid");
        var html = await Texto(respuesta);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Contains("No encontramos este libro", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/catalogo\"", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, noEsGuid.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-09")]
    public async Task Con_el_inventario_sin_responder_la_ficha_avisa_que_la_disponibilidad_puede_estar_desactualizada()
    {
        var obra = ObraConAmbas(
            Obra1,
            "Dato viejo",
            Impresa(Guid.NewGuid(), 50m, Disp(Guid.NewGuid(), EstadoDeDisponibilidad.Disponible, 3, FrescuraDeLaDisponibilidad.PuedeEstarDesactualizada, fuentes: [new("LIM", "Lima", 3)])),
            Digital(Guid.NewGuid(), 20m, Disp(Guid.NewGuid(), EstadoDeDisponibilidad.DisponibleAlInstante, null)));
        var catalogo = new CatalogoDePrueba { Ficha = _ => Resultado<ResumenDeObra>.Exito(obra) };
        await using var anfitrion = await Iniciar(catalogo);

        using var respuesta = await anfitrion.Cliente.GetAsync($"/catalogo/obras/{Obra1}");
        var html = await Texto(respuesta);

        Assert.Contains("La disponibilidad puede estar desactualizada", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-09")]
    public async Task Un_catalogo_caido_en_la_ficha_es_servicio_no_disponible_con_reintento()
    {
        var catalogo = new CatalogoDePrueba { Ficha = _ => Resultado<ResumenDeObra>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "x")) };
        await using var anfitrion = await Iniciar(catalogo);

        using var respuesta = await anfitrion.Cliente.GetAsync($"/catalogo/obras/{Obra1}");
        var html = await Texto(respuesta);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Contains("No podemos mostrar este libro ahora.", html, StringComparison.Ordinal);
        Assert.Contains("Reintentar", html, StringComparison.Ordinal);
    }
    // ---------- La ficha por sucursal (CAR-42, CAR-43) ----------

    private static async Task<string> Ficha(DisponibilidadDeEdicion disponibilidad, string? cookie = null)
    {
        var catalogo = new CatalogoDePrueba { Ficha = id => Resultado<ResumenDeObra>.Exito(new ResumenDeObra(id, "Estructuras de datos", "Ana Quispe", "Editorial Andina", "Técnico", Huella, [Impresa(disponibilidad.EdicionId, 129.90m, disponibilidad)])) };
        await using var anfitrion = await Iniciar(catalogo);
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, $"/catalogo/obras/{Obra1}");
        if (cookie is not null)
        {
            solicitud.Headers.Add("Cookie", "bh-sucursal=" + cookie);
        }

        using var respuesta = await anfitrion.Cliente.SendAsync(solicitud);
        return await Texto(respuesta);
    }

    private static DisponibilidadDeEdicion ConStockEnAyacuchoYCusco(int ayacucho, int lima) =>
        Disp(Guid.NewGuid(), EstadoDeDisponibilidad.Disponible, ayacucho + lima, fuentes: [new DisponibilidadPorFuente("AYA", "Ayacucho", ayacucho), new DisponibilidadPorFuente("LIM", "Lima", lima)]);

    [Fact]
    [Trait("Criterio", "AC-CAR-36")]
    public async Task La_ficha_muestra_el_stock_de_cada_sucursal_preselecciona_la_preferida_con_stock_y_limita_la_cantidad_a_ella()
    {
        var html = await Ficha(ConStockEnAyacuchoYCusco(25, 4), cookie: "LIM");

        Assert.Matches(@"<option value=""AYA""[^>]*data-bh-maximo=""20""[^>]*>Ayacucho — 25 disponibles</option>", html);
        Assert.Matches(@"<option value=""LIM""\s+selected[^>]*data-bh-maximo=""4""[^>]*>Lima — 4 disponibles</option>", html);
        Assert.Contains("name=\"cantidad\" value=\"1\" min=\"1\" max=\"4\"", html, StringComparison.Ordinal);
        Assert.Contains("data-bh-sucursal-de-ficha", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Tu sucursal", html, StringComparison.Ordinal);
        Assert.DoesNotContain("En tu sucursal", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-36")]
    public async Task Una_sucursal_sin_stock_no_se_puede_elegir_ni_se_preselecciona_y_hay_que_elegir_una()
    {
        var html = await Ficha(ConStockEnAyacuchoYCusco(25, 0), cookie: "LIM");

        Assert.Matches(@"<option value=""LIM""[^>]*disabled[^>]*>Lima — sin stock</option>", html);
        Assert.DoesNotMatch(@"<option value=""(AYA|LIM)""\s+selected", html);
        Assert.Matches(@"<option value=""""\s+selected[^>]*>Elige una sucursal</option>", html);
        Assert.Contains("name=\"sucursal\" class=\"select w-full min-w-64\" required", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-37")]
    public async Task Sin_dato_de_stock_se_elige_entre_las_sucursales_conocidas_sin_cantidades_con_el_aviso()
    {
        var html = await Ficha(Disp(Guid.NewGuid(), EstadoDeDisponibilidad.NoSePuedeConfirmar, null, FrescuraDeLaDisponibilidad.NoSePuedeConfirmar), cookie: "LIM");

        Assert.Contains("No podemos confirmar el stock ahora", html, StringComparison.Ordinal);
        Assert.Matches(@"<option value=""AYA""[^>]*>Ayacucho</option>", html);
        Assert.Matches(@"<option value=""LIM""[^>]*>Lima</option>", html);
        Assert.DoesNotMatch(@"<option value=""(AYA|LIM)""\s+selected", html);
        Assert.DoesNotContain("disponibles</option>", html, StringComparison.Ordinal);
        Assert.Contains("Agregar impreso al carrito", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Disponibilidad por sucursal", html, StringComparison.Ordinal);
    }

}
