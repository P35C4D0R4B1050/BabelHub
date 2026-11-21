using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Web.Formato;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// Las pantallas de administración del catálogo (T25, T26; AC-CAT-01, AC-CAT-03, AC-CAT-05, AC-CAT-12, AC-CAT-13, AC-CAT-16, AC-CAT-17, AC-CAT-18): lista de obras, obra nueva, página de una obra y pantalla de ingresos, con los contratos de la administración de prueba. Verifican lo que se pinta (chips de
/// «Agotado en la red» y «Registro pendiente», licencia, advertencias del ingreso y de la conciliación), los cinco estados y que la página no lleve scripts en línea ni controles sin etiqueta. Las escrituras las prueban las acciones JSON (T18 a T22).
/// </summary>
public sealed partial class PantallasDeAdministracionDelCatalogoTests
{
    private static readonly Guid ObraId = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid ImpresaId = Guid.Parse("22222222-0000-0000-0000-0000000000a1");
    private static readonly Guid DigitalId = Guid.Parse("22222222-0000-0000-0000-0000000000d1");
    private static readonly string Huella = new('b', 64);

    [GeneratedRegex("<(?:input|select|textarea)\\b[^>]*\\bid=\"(?<id>[^\"]+)\"[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex Controles();

    private sealed class AdministracionDePrueba : IAdministracionDelCatalogo
    {
        public Func<ListarObrasDeAdministracionEntrada, Resultado<PaginaDeObrasDeAdministracion>> Lista { get; set; } = e => Resultado<PaginaDeObrasDeAdministracion>.Exito(new PaginaDeObrasDeAdministracion([], e.Pagina, e.Tamano, 0));

        public Func<Guid, Resultado<ObraDeAdministracion>> Obra { get; set; } = id => Resultado<ObraDeAdministracion>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "obra-no-encontrada"));

        public List<ListarObrasDeAdministracionEntrada> Listadas { get; } = [];

        public Task<Resultado<PaginaDeObrasDeAdministracion>> ListarObras(ListarObrasDeAdministracionEntrada entrada, CancellationToken cancelacion = default)
        {
            Listadas.Add(entrada);
            return Task.FromResult(Lista(entrada));
        }

        public Task<Resultado<ObraDeAdministracion>> ObtenerObra(Guid obraId, CancellationToken cancelacion = default) => Task.FromResult(Obra(obraId));

        public Task<Resultado<ResultadoDeAlta>> RegistrarObra(RegistrarObraEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ResultadoDeEscritura>> EditarObra(EditarObraEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ResultadoDeEscritura>> DarDeBajaObra(DarDeBajaObraEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ResultadoDeAlta>> RegistrarEdicion(RegistrarEdicionEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ResultadoDeEscritura>> EditarEdicion(EditarEdicionEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ResultadoDeEscritura>> DarDeBajaEdicion(DarDeBajaEdicionEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ResultadoDeEscritura>> RetirarEdicionDigital(RetirarEdicionDigitalEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private sealed class IngresosDePrueba : IIngresosDeMercaderia
    {
        public Func<Resultado<ClaveDeIngresoEmitida>> Clave { get; set; } = () => Resultado<ClaveDeIngresoEmitida>.Exito(new ClaveDeIngresoEmitida("token-firmado-de-prueba", DateTimeOffset.UtcNow.AddMinutes(30)));

        public Func<Resultado<HistorialDeIngresos>> Historial { get; set; } = () => Resultado<HistorialDeIngresos>.Exito(new HistorialDeIngresos(ImpresaId, [], false));

        public int Emisiones { get; private set; }

        public Task<Resultado<ClaveDeIngresoEmitida>> EmitirClave(EmitirClaveDeIngresoEntrada entrada, CancellationToken cancelacion = default)
        {
            Emisiones++;
            return Task.FromResult(Clave());
        }

        public Task<Resultado<HistorialDeIngresos>> Listar(ListarIngresosEntrada entrada, CancellationToken cancelacion = default) => Task.FromResult(Historial());

        public Task<Resultado<ResultadoDeIngreso>> Registrar(RegistrarIngresoEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ResultadoDeIngreso>> Reintentar(ReintentarIngresoEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ResultadoDeIngreso>> Conciliar(ConciliarIngresoEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private sealed class CatalogoDePrueba(EdicionConsultada? edicion) : IConsultaDeCatalogo
    {
        public Task<Resultado<ObraAlAzar?>> ObtenerObraAlAzar(CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<PaginaDeObras>> BuscarObras(BusquedaDeObras busqueda, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ResumenDeObra>> ObtenerObra(Guid obraId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<IReadOnlyList<EdicionConsultada>>> ObtenerEdiciones(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) =>
            Task.FromResult(Resultado<IReadOnlyList<EdicionConsultada>>.Exito(edicion is null ? [] : [edicion]));
    }

    private sealed class DisponibilidadDePrueba(EstadoDeDisponibilidad estado) : IDisponibilidadDeEdiciones
    {
        public Task<Resultado<IReadOnlyList<DisponibilidadDeEdicion>>> Consultar(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) =>
            Task.FromResult(Resultado<IReadOnlyList<DisponibilidadDeEdicion>>.Exito([.. edicionIds.Select(id => new DisponibilidadDeEdicion(id, estado, estado == EstadoDeDisponibilidad.Agotada ? 0 : 5, [], FrescuraDeLaDisponibilidad.Reciente, DateTimeOffset.UtcNow, null))]));
    }

    private static EdicionDeAdministracion Impresa(bool registroPendiente = false, EstadoDeEdicionConsultado estado = EstadoDeEdicionConsultado.Activa) =>
        new(ImpresaId, FormatoDeEdicion.Impresa, "9780000000019", "0-00-000001-9", 129.90m, estado, null, null, null, null, registroPendiente);

    private static EdicionDeAdministracion Digital(MotivoDeNoDisponibleDeEdicion? motivo = null, EstadoDeEdicionConsultado estado = EstadoDeEdicionConsultado.Activa, string? retiro = null) =>
        new(
            DigitalId,
            FormatoDeEdicion.Digital,
            "9790000000018",
            "979-0-00-000001-8",
            49m,
            estado,
            retiro,
            motivo,
            new LicenciaDeEdicionDeAdministracion(TipoDeLicenciaDeEdicion.Comercial, "Editorial Andina", ["PE", "CO"], new DateOnly(2027, 6, 30), FormatoDeArchivoDigital.Epub),
            new ArchivoDeEdicionDeAdministracion("epub", 2_500_000, "abcdef0123456789", DateTimeOffset.UtcNow));

    private static ObraDeAdministracion Obra(bool baja = false, params EdicionDeAdministracion[] ediciones) =>
        new(ObraId, "Estructuras de datos", "Ana Quispe", "Editorial Andina", "Técnico", Huella, 3, baja, ediciones);

    private static Task<AnfitrionWeb> Iniciar(AdministracionDePrueba? administracion = null, IngresosDePrueba? ingresos = null, EdicionConsultada? edicion = null, EstadoDeDisponibilidad disponibilidad = EstadoDeDisponibilidad.Disponible) =>
        AnfitrionWeb.Iniciar(servicios =>
        {
            servicios.AddSingleton<IAdministracionDelCatalogo>(administracion ?? new AdministracionDePrueba());
            servicios.AddSingleton<IIngresosDeMercaderia>(ingresos ?? new IngresosDePrueba());
            servicios.AddSingleton<IConsultaDeCatalogo>(new CatalogoDePrueba(edicion));
            servicios.AddSingleton<IDisponibilidadDeEdiciones>(new DisponibilidadDePrueba(disponibilidad));
        });

    private static async Task<string> Texto(HttpResponseMessage respuesta) => System.Net.WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());

    private static void AssertSinScriptsEnLineaNiControlesSinEtiqueta(string html)
    {
        Assert.DoesNotMatch(new Regex("<script(?![^>]*\\bsrc=)", RegexOptions.CultureInvariant), html);
        Assert.DoesNotMatch(new Regex("\\son(?:click|change|submit|load|error)=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase), html);
        foreach (Match control in Controles().Matches(html))
        {
            var id = control.Groups["id"].Value;
            var tipo = Regex.Match(control.Value, "type=\"(?<t>[^\"]+)\"").Groups["t"].Value;
            if (tipo is "hidden" or "submit")
            {
                continue;
            }

            Assert.True(html.Contains($"for=\"{id}\"", StringComparison.Ordinal) || control.Value.Contains("aria-label=", StringComparison.Ordinal), $"El control «{id}» no tiene etiqueta.");
        }
    }

    // ---------- Lista ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public async Task La_lista_pinta_las_obras_con_sus_ediciones_licencia_y_chips_y_envia_la_busqueda()
    {
        var administracion = new AdministracionDePrueba
        {
            Lista = e => Resultado<PaginaDeObrasDeAdministracion>.Exito(new PaginaDeObrasDeAdministracion([Obra(false, Impresa(registroPendiente: true), Digital(MotivoDeNoDisponibleDeEdicion.LicenciaVencida))], e.Pagina, e.Tamano, 45)),
        };
        await using var anfitrion = await Iniciar(administracion, disponibilidad: EstadoDeDisponibilidad.Agotada);

        using var respuesta = await anfitrion.Cliente.GetAsync("/administracion/obras?q=datos&bajas=1&pagina=2");
        var html = await Texto(respuesta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(new ListarObrasDeAdministracionEntrada("datos", true, 2, 20), Assert.Single(administracion.Listadas));
        Assert.Contains("Estructuras de datos", html, StringComparison.Ordinal);
        Assert.Contains(FormatoPeruano.Soles(129.90m), html, StringComparison.Ordinal);
        Assert.Contains("9780000000019", html, StringComparison.Ordinal);
        Assert.Contains("Agotado en la red", html, StringComparison.Ordinal);
        Assert.Contains("Registro pendiente en el Inventario", html, StringComparison.Ordinal);
        Assert.Contains("Licencia vencida", html, StringComparison.Ordinal);
        Assert.Contains("Licencia comercial · Editorial Andina · hasta 2027-06-30", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/administracion/ediciones/{ImpresaId}/ingreso\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/administracion/obras/{ObraId}\"", html, StringComparison.Ordinal);
        Assert.Contains("Página 2 de 3", html, StringComparison.Ordinal);
        Assert.Contains("<caption class=\"sr-only\">", html, StringComparison.Ordinal);
        Assert.Contains("scope=\"col\"", html, StringComparison.Ordinal);
        AssertSinScriptsEnLineaNiControlesSinEtiqueta(html);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-17")]
    public async Task La_lista_no_afirma_que_un_titulo_esta_agotado_si_no_hay_dato_y_marca_la_obra_dada_de_baja()
    {
        var administracion = new AdministracionDePrueba
        {
            Lista = e => Resultado<PaginaDeObrasDeAdministracion>.Exito(new PaginaDeObrasDeAdministracion([Obra(true, Impresa(estado: EstadoDeEdicionConsultado.DadaDeBaja))], 1, 20, 1)),
        };
        await using var anfitrion = await Iniciar(administracion, disponibilidad: EstadoDeDisponibilidad.NoSePuedeConfirmar);

        using var respuesta = await anfitrion.Cliente.GetAsync("/administracion/obras?bajas=1");
        var html = await Texto(respuesta);

        Assert.DoesNotContain("Agotado en la red", html, StringComparison.Ordinal);
        Assert.Contains("Dada de baja", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Registrar ingreso", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public async Task Sin_obras_la_lista_es_el_estado_vacio_con_nueva_obra_y_con_un_filtro_sin_resultados_ofrece_limpiar()
    {
        await using var anfitrion = await Iniciar();

        using var vacia = await anfitrion.Cliente.GetAsync("/administracion/obras");
        using var sinResultados = await anfitrion.Cliente.GetAsync("/administracion/obras?q=zzz");
        var htmlVacia = await Texto(vacia);
        var htmlFiltro = await Texto(sinResultados);

        Assert.Contains("Aún no hay obras", htmlVacia, StringComparison.Ordinal);
        Assert.Contains("href=\"/administracion/obras/nueva\"", htmlVacia, StringComparison.Ordinal);
        Assert.Contains("Sin resultados", htmlFiltro, StringComparison.Ordinal);
        Assert.Contains("Limpiar filtros", htmlFiltro, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-01")]
    [InlineData(CategoriaDeError.Prohibido, HttpStatusCode.Forbidden, "No tienes acceso")]
    [InlineData(CategoriaDeError.NoAutenticado, HttpStatusCode.Unauthorized, "No tienes acceso")]
    [InlineData(CategoriaDeError.NoDisponible, HttpStatusCode.ServiceUnavailable, "No podemos mostrar el catálogo ahora.")]
    public async Task Una_falla_de_la_administracion_es_un_estado_de_pantalla_sin_permiso_o_servicio_no_disponible(CategoriaDeError categoria, HttpStatusCode estado, string texto)
    {
        var administracion = new AdministracionDePrueba { Lista = _ => Resultado<PaginaDeObrasDeAdministracion>.Fallo(new ErrorDeAplicacion(categoria, "x")) };
        await using var anfitrion = await Iniciar(administracion);

        using var respuesta = await anfitrion.Cliente.GetAsync("/administracion/obras");
        var html = await Texto(respuesta);

        Assert.Equal(estado, respuesta.StatusCode);
        Assert.Contains(texto, html, StringComparison.Ordinal);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public async Task Un_filtro_repetido_o_una_pagina_invalida_es_un_aviso_sin_llamar_a_la_administracion()
    {
        var administracion = new AdministracionDePrueba();
        await using var anfitrion = await Iniciar(administracion);

        using var repetido = await anfitrion.Cliente.GetAsync("/administracion/obras?q=a&q=b");
        using var pagina = await anfitrion.Cliente.GetAsync("/administracion/obras?pagina=cero");

        Assert.Equal(HttpStatusCode.BadRequest, repetido.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, pagina.StatusCode);
        Assert.Contains("Algún filtro está repetido.", await Texto(repetido), StringComparison.Ordinal);
        Assert.Empty(administracion.Listadas);
    }

    // ---------- Obra nueva ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task La_obra_nueva_tiene_etiquetas_archivos_con_sus_topes_y_la_ayuda_del_stock_inicial_y_carga_su_script_externo()
    {
        await using var anfitrion = await Iniciar();

        using var respuesta = await anfitrion.Cliente.GetAsync("/administracion/obras/nueva");
        var html = await Texto(respuesta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("data-bh-formulario=\"alta-obra\"", html, StringComparison.Ordinal);
        Assert.Contains("hasta 2 MB", html, StringComparison.Ordinal);
        Assert.Contains("hasta 50 MB", html, StringComparison.Ordinal);
        Assert.Contains("accept=\"image/jpeg,image/png,image/webp\"", html, StringComparison.Ordinal);
        Assert.Contains("Stock inicial (opcional, de 1 a 5 000)", html, StringComparison.Ordinal);
        Assert.Contains("el stock no se edita aquí", html, StringComparison.Ordinal);
        Assert.Contains("La edición solo se vende si la licencia incluye Perú", html, StringComparison.Ordinal);
        Assert.Contains("<script src=\"/js/admin-catalogo.js\" defer></script>", html, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", html, StringComparison.Ordinal);
        Assert.Contains("<fieldset", html, StringComparison.Ordinal);
        Assert.Contains("<legend", html, StringComparison.Ordinal);
        AssertSinScriptsEnLineaNiControlesSinEtiqueta(html);
    }

    // ---------- Una obra ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-03")]
    public async Task La_pagina_de_una_obra_muestra_sus_ediciones_licencia_archivo_y_las_acciones_con_la_version_leida()
    {
        var administracion = new AdministracionDePrueba { Obra = _ => Resultado<ObraDeAdministracion>.Exito(Obra(false, Impresa(registroPendiente: true), Digital())) };
        await using var anfitrion = await Iniciar(administracion);

        using var respuesta = await anfitrion.Cliente.GetAsync($"/administracion/obras/{ObraId}");
        var html = await Texto(respuesta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("data-bh-formulario=\"editar-obra\"", html, StringComparison.Ordinal);
        Assert.Contains("data-version=\"3\"", html, StringComparison.Ordinal);
        Assert.Contains($"data-url=\"/administracion/obras/{ObraId}\"", html, StringComparison.Ordinal);
        Assert.Contains("(escrito como 0-00-000001-9)", html, StringComparison.Ordinal);
        Assert.Contains("El Inventario aún no confirmó el registro de la edición impresa.", html, StringComparison.Ordinal);
        Assert.Contains("Licencia comercial · Editorial Andina", html, StringComparison.Ordinal);
        Assert.Contains("PE, CO", html, StringComparison.Ordinal);
        Assert.Contains("vigente hasta 2027-06-30", html, StringComparison.Ordinal);
        Assert.Contains("EPUB · 2,4 MB · huella abcdef0123456789".Replace("2,4", "2.4"), html, StringComparison.Ordinal);
        Assert.Contains("Retirar de la venta", html, StringComparison.Ordinal);
        Assert.Contains("Quienes ya la compraron conservan su acceso".ToLowerInvariant(), html.ToLowerInvariant(), StringComparison.Ordinal);
        Assert.Contains($"/administracion/obras/{ObraId}/ediciones/{DigitalId}/retirar", html, StringComparison.Ordinal);
        Assert.Contains($"/administracion/obras/{ObraId}/baja", html, StringComparison.Ordinal);
        Assert.Contains("data-confirmar=", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/administracion/ediciones/{ImpresaId}/ingreso\"", html, StringComparison.Ordinal);
        AssertSinScriptsEnLineaNiControlesSinEtiqueta(html);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-05")]
    public async Task Una_digital_retirada_muestra_el_motivo_sin_acciones_de_edicion_y_una_obra_sin_digital_ofrece_agregarla()
    {
        var retirada = new AdministracionDePrueba { Obra = _ => Resultado<ObraDeAdministracion>.Exito(Obra(false, Digital(estado: EstadoDeEdicionConsultado.RetiradaDeVenta, retiro: "Se venció el contrato"))) };
        var sinDigital = new AdministracionDePrueba { Obra = _ => Resultado<ObraDeAdministracion>.Exito(Obra(false, Impresa())) };
        await using var a1 = await Iniciar(retirada);
        await using var a2 = await Iniciar(sinDigital);

        var htmlRetirada = await Texto(await a1.Cliente.GetAsync($"/administracion/obras/{ObraId}"));
        var htmlSinDigital = await Texto(await a2.Cliente.GetAsync($"/administracion/obras/{ObraId}"));

        Assert.Contains("Retirada de la venta", htmlRetirada, StringComparison.Ordinal);
        Assert.Contains("Motivo del retiro: Se venció el contrato", htmlRetirada, StringComparison.Ordinal);
        Assert.DoesNotContain("Retirar de la venta", htmlRetirada, StringComparison.Ordinal);
        Assert.Contains("Agregar edición digital", htmlSinDigital, StringComparison.Ordinal);
        Assert.Contains("data-formato=\"digital\"", htmlSinDigital, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-03")]
    public async Task Una_obra_dada_de_baja_no_ofrece_formularios_y_una_inexistente_es_404()
    {
        var baja = new AdministracionDePrueba { Obra = _ => Resultado<ObraDeAdministracion>.Exito(Obra(true, Impresa(estado: EstadoDeEdicionConsultado.DadaDeBaja))) };
        await using var anfitrion = await Iniciar(baja);
        await using var otro = await Iniciar();

        var htmlBaja = await Texto(await anfitrion.Cliente.GetAsync($"/administracion/obras/{ObraId}"));
        using var inexistente = await otro.Cliente.GetAsync($"/administracion/obras/{Guid.NewGuid()}");

        Assert.Contains("Esta obra está dada de baja", htmlBaja, StringComparison.Ordinal);
        Assert.DoesNotContain("data-bh-formulario", htmlBaja, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }

    // ---------- Ingresos ----------

    private static EdicionConsultada EdicionImpresa(EstadoDeEdicionConsultado estado = EstadoDeEdicionConsultado.Activa, FormatoDeEdicion formato = FormatoDeEdicion.Impresa) =>
        new(ImpresaId, ObraId, "Estructuras de datos", formato, "9780000000019", 129.90m, estado, null);

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task La_pantalla_de_ingresos_lleva_el_formulario_con_el_token_firmado_la_vigencia_y_el_chip_de_agotado()
    {
        var ingresos = new IngresosDePrueba();
        await using var anfitrion = await Iniciar(ingresos: ingresos, edicion: EdicionImpresa(), disponibilidad: EstadoDeDisponibilidad.Agotada);

        using var respuesta = await anfitrion.Cliente.GetAsync($"/administracion/ediciones/{ImpresaId}/ingreso");
        var html = await Texto(respuesta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(1, ingresos.Emisiones);
        Assert.Contains("data-bh-formulario=\"registrar-ingreso\"", html, StringComparison.Ordinal);
        Assert.Contains("data-token=\"token-firmado-de-prueba\"", html, StringComparison.Ordinal);
        Assert.Contains($"data-url=\"/administracion/ediciones/{ImpresaId}/ingresos\"", html, StringComparison.Ordinal);
        Assert.Contains("Agotado en la red", html, StringComparison.Ordinal);
        Assert.Contains("Cantidad recibida (de 1 a 5 000)", html, StringComparison.Ordinal);
        Assert.Contains("No escribas datos personales", html, StringComparison.Ordinal);
        Assert.Contains("sirve para un solo ingreso", html, StringComparison.Ordinal);
        Assert.Contains("Aún no hay ingresos de esta edición.", html, StringComparison.Ordinal);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        AssertSinScriptsEnLineaNiControlesSinEtiqueta(html);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task El_historial_muestra_fecha_cantidad_referencia_y_usuario_y_el_pendiente_oculta_el_formulario_y_advierte_del_riesgo_de_duplicar()
    {
        var usuario = Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789");
        var viejo = DateTimeOffset.UtcNow.AddHours(-30);
        var ingresos = new IngresosDePrueba
        {
            Historial = () => Resultado<HistorialDeIngresos>.Exito(new HistorialDeIngresos(
                ImpresaId,
                [
                    new IngresoDeLaLista(Guid.NewGuid(), null, 30, "GR-001", usuario, DateTimeOffset.UtcNow.AddDays(-5), null),
                    new IngresoDeLaLista(null, Guid.Parse("33333333-0000-0000-0000-000000000001"), 12, null, usuario, viejo, EstadoDeIngresoSolicitado.Pendiente),
                ],
                false)),
        };
        await using var anfitrion = await Iniciar(ingresos: ingresos, edicion: EdicionImpresa());

        using var respuesta = await anfitrion.Cliente.GetAsync($"/administracion/ediciones/{ImpresaId}/ingreso");
        var html = await Texto(respuesta);

        Assert.Contains("GR-001", html, StringComparison.Ordinal);
        Assert.Contains("Usuario abcdef01", html, StringComparison.Ordinal);
        Assert.Contains(FormatoPeruano.FechaYHora(viejo), html, StringComparison.Ordinal);
        Assert.Contains("Hay un ingreso pendiente de esta edición.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-bh-formulario=\"registrar-ingreso\"", html, StringComparison.Ordinal);
        Assert.Contains("Reintentar ingreso pendiente", html, StringComparison.Ordinal);
        Assert.Contains("Conciliar ingreso pendiente", html, StringComparison.Ordinal);
        Assert.Contains("habilita una clave nueva y", html, StringComparison.Ordinal);
        Assert.Contains("puede duplicar el stock", html, StringComparison.Ordinal);
        Assert.Contains("más de 24 horas pendiente", html, StringComparison.Ordinal);
        Assert.Contains("Entiendo el riesgo", html, StringComparison.Ordinal);
        Assert.Contains("/ingresos/33333333-0000-0000-0000-000000000001/conciliar", html, StringComparison.Ordinal);
        AssertSinScriptsEnLineaNiControlesSinEtiqueta(html);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Con_el_inventario_sin_responder_el_historial_lo_avisa()
    {
        var ingresos = new IngresosDePrueba { Historial = () => Resultado<HistorialDeIngresos>.Exito(new HistorialDeIngresos(ImpresaId, [], true)) };
        await using var anfitrion = await Iniciar(ingresos: ingresos, edicion: EdicionImpresa());

        var html = await Texto(await anfitrion.Cliente.GetAsync($"/administracion/ediciones/{ImpresaId}/ingreso"));

        Assert.Contains("El Inventario no respondió", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Una_edicion_digital_o_dada_de_baja_no_recibe_ingresos_y_un_actor_sin_permiso_ve_el_estado_sin_permiso_sin_token()
    {
        var sinPermiso = new IngresosDePrueba { Clave = () => Resultado<ClaveDeIngresoEmitida>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "prohibido")) };
        await using var digital = await Iniciar(edicion: EdicionImpresa(formato: FormatoDeEdicion.Digital));
        await using var baja = await Iniciar(edicion: EdicionImpresa(estado: EstadoDeEdicionConsultado.DadaDeBaja));
        await using var prohibido = await Iniciar(ingresos: sinPermiso, edicion: EdicionImpresa());
        await using var inexistente = await Iniciar();

        using var r1 = await digital.Cliente.GetAsync($"/administracion/ediciones/{ImpresaId}/ingreso");
        using var r2 = await baja.Cliente.GetAsync($"/administracion/ediciones/{ImpresaId}/ingreso");
        using var r3 = await prohibido.Cliente.GetAsync($"/administracion/ediciones/{ImpresaId}/ingreso");
        using var r4 = await inexistente.Cliente.GetAsync($"/administracion/ediciones/{ImpresaId}/ingreso");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);
        Assert.Contains("Esta edición no recibe ingresos", await Texto(r1), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r2.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, r3.StatusCode);
        var htmlProhibido = await Texto(r3);
        Assert.Contains("No tienes acceso", htmlProhibido, StringComparison.Ordinal);
        Assert.DoesNotContain("data-token", htmlProhibido, StringComparison.Ordinal);
        Assert.DoesNotContain("token-firmado-de-prueba", htmlProhibido, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, r4.StatusCode);
    }

    // ---------- El script ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void El_script_de_la_administracion_no_usa_eval_ni_html_dinamico_y_envia_el_token_antifalsificacion_en_su_cabecera()
    {
        var js = File.ReadAllText(Path.Combine(RaizDelRepositorio.Api, "wwwroot", "js", "admin-catalogo.js"));

        Assert.DoesNotContain("eval(", js, StringComparison.Ordinal);
        Assert.DoesNotContain("new Function", js, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("document.write", js, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", js, StringComparison.Ordinal);
        Assert.Contains("'RequestVerificationToken': TOKEN", js, StringComparison.Ordinal);
        Assert.Contains("credentials: 'same-origin'", js, StringComparison.Ordinal);
        Assert.Contains("textContent", js, StringComparison.Ordinal);
        Assert.Contains("/administracion/archivos?finalidad=", js, StringComparison.Ordinal);
    }

    // ---------- El autor de cada ingreso (CAT-22) ----------

    /// <summary>Identidad vista por el Catálogo: lista a los usuarios del personal con su nombre, o falla.</summary>
    private sealed class UsuariosDePrueba(IReadOnlyList<UsuarioAdministrado> lista, bool falla = false) : IAdministracionDeUsuarios
    {
        public Task<Resultado<PaginaDeUsuarios>> Listar(Guid administradorId, FiltroDeUsuarios filtro, CancellationToken cancelacion = default) =>
            falla ? throw new InvalidOperationException("Identidad no responde.") : Task.FromResult(Resultado<PaginaDeUsuarios>.Exito(new PaginaDeUsuarios(lista, null)));

        public Task<Resultado<ContrasenaTemporalEntregada>> Crear(Guid administradorId, string nombre, string correo, RolDeCuenta rol, string? codigoDeSucursal, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<UsuarioAdministrado>> CambiarElRol(Guid administradorId, Guid usuarioId, RolDeCuenta rol, string? codigoDeSucursal, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<UsuarioAdministrado>> Desactivar(Guid administradorId, Guid usuarioId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<UsuarioAdministrado>> Reactivar(Guid administradorId, Guid usuarioId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ContrasenaTemporalEntregada>> RestablecerLaContrasena(Guid administradorId, Guid usuarioId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<RespuestaUniforme>> RestablecerElSegundoFactor(Guid administradorId, Guid usuarioId, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private static Task<AnfitrionWeb> IniciarConUsuarios(IngresosDePrueba ingresos, IAdministracionDeUsuarios usuarios) =>
        AnfitrionWeb.Iniciar(servicios =>
        {
            servicios.AddSingleton<IAdministracionDelCatalogo>(new AdministracionDePrueba());
            servicios.AddSingleton<IIngresosDeMercaderia>(ingresos);
            servicios.AddSingleton<IConsultaDeCatalogo>(new CatalogoDePrueba(EdicionImpresa()));
            servicios.AddSingleton<IDisponibilidadDeEdiciones>(new DisponibilidadDePrueba(EstadoDeDisponibilidad.Disponible));
            servicios.AddSingleton(usuarios);
        },
        antesDeLasRutas: app => app.Use((Microsoft.AspNetCore.Http.HttpContext contexto, Func<Task> siguiente) =>
        {
            // La administración con sesión: el actor verificado de la solicitud, como lo deja el manejador de la sesión.
            contexto.EstablecerCredencial(new ResultadoDeCredencial(EstadoDeCredencial.Asegurada, new ActorVerificado(Guid.Parse("55555555-5555-5555-5555-555555555555"), RolWeb.AdministradorCentral, null), null));
            return siguiente();
        }));

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task El_historial_muestra_el_nombre_de_quien_registro_el_ingreso_resuelto_por_Identidad_y_sin_Identidad_el_identificador_abreviado()
    {
        var autor = Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789");
        var otro = Guid.Parse("12345678-0000-0000-0000-000000000099");
        var ingresos = new IngresosDePrueba
        {
            Historial = () => Resultado<HistorialDeIngresos>.Exito(new HistorialDeIngresos(
                ImpresaId,
                [new IngresoDeLaLista(Guid.NewGuid(), null, 30, "GR-001", autor, DateTimeOffset.UtcNow.AddDays(-5), null), new IngresoDeLaLista(Guid.NewGuid(), null, 5, "GR-002", otro, DateTimeOffset.UtcNow.AddDays(-4), null)],
                false)),
        };
        var lista = new[] { new UsuarioAdministrado(autor, "Rosa Quispe Ficticia", "rosa@example.test", RolDeCuenta.AdministradorCentral, null, true, true) };

        await using (var conIdentidad = await IniciarConUsuarios(ingresos, new UsuariosDePrueba(lista)))
        {
            using var respuesta = await conIdentidad.Cliente.GetAsync($"/administracion/ediciones/{ImpresaId}/ingreso");
            var html = await Texto(respuesta);

            Assert.Contains("Rosa Quispe Ficticia", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Usuario abcdef01", html, StringComparison.Ordinal);
            // Quien Identidad no devuelve conserva el identificador abreviado, y el correo del autor nunca se muestra.
            Assert.Contains("Usuario 12345678", html, StringComparison.Ordinal);
            Assert.DoesNotContain("rosa@example.test", html, StringComparison.Ordinal);
        }

        await using var sinIdentidad = await IniciarConUsuarios(ingresos, new UsuariosDePrueba(lista, falla: true));
        using var caida = await sinIdentidad.Cliente.GetAsync($"/administracion/ediciones/{ImpresaId}/ingreso");
        var htmlCaida = await Texto(caida);
        Assert.Equal(HttpStatusCode.OK, caida.StatusCode);
        Assert.Contains("GR-001", htmlCaida, StringComparison.Ordinal);
        Assert.Contains("Usuario abcdef01", htmlCaida, StringComparison.Ordinal);
    }
}
