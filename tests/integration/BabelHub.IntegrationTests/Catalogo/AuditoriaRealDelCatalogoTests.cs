using System.Net;
using System.Text.Json;
using BabelHub.IntegrationTests.Plataforma;
using static BabelHub.IntegrationTests.Catalogo.ArchivosDeMuestra;

namespace BabelHub.IntegrationTests.Catalogo;

/// <summary>
/// El catálogo con la <b>auditoría real</b> (persistencia de <c>IAuditoria</c> en la misma base y la misma transacción) y con la auditoría caída (T22; AC-CAT-08, AC-CAT-15, AC-CAT-19, CAT-19): cada operación sensible deja su registro y, si la auditoría no se puede escribir, la
/// operación completa se revierte (alta, edición, baja, precio, licencia, retiro y reemplazo de un archivo) con «servicio no disponible», sin dejar referencias consumidas ni objetos descartados, y se puede repetir cuando la auditoría vuelve.
/// </summary>
[Collection(ColeccionDeLaApiDelCatalogo.Nombre)]
public sealed class AuditoriaRealDelCatalogoTests(PostgresDelCatalogoYAuditoriaFixture bd, AlmacenDeObjetosFixture almacen) : IAsyncLifetime
{
    private static readonly Guid Ana = Guid.Parse("a0000000-0000-0000-0000-0000000000a1");

    private CatalogoApiHost _host = null!;
    private int _isbn;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _host = await CatalogoApiHost.Crear(bd, almacen.Cliente);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private string NuevoIsbn()
    {
        var cuerpo = "979" + (++_isbn).ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
        var suma = cuerpo.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return cuerpo + ((10 - (suma % 10)) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static object Licencia(string titular = "Dominio público") => new { tipo = "dominioPublico", titular, territorios = new[] { "PE" }, formato = "epub" };

    private sealed record Obra(Guid ObraId, long Version, Guid ImpresaId, Guid DigitalId);

    /// <summary>Una obra con una impresa y una digital (con su libro) creada con la auditoría funcionando.</summary>
    private async Task<Obra> NuevaObra()
    {
        var portada = await _host.SubirYReferenciar("portada", Png(40, 40, 900), Ana);
        var libro = await _host.SubirYReferenciar("libro", Epub(rellenoEnBytes: 300), Ana);
        using var alta = await _host.Json(HttpMethod.Post, "/administracion/obras", new
        {
            titulo = "Auditada",
            autor = "Ana",
            editorial = "Andina",
            categoria = "Técnico",
            portadaRef = portada,
            ediciones = new object[]
            {
                new { formato = "impresa", isbn = NuevoIsbn(), precio = 40m },
                new { formato = "digital", isbn = NuevoIsbn(), precio = 20m, licencia = Licencia(), archivoRef = libro },
            },
        }, usuario: Ana);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var datos = await CatalogoApiHost.Leer(alta);
        var ids = datos.GetProperty("edicionIds").EnumerateArray().Select(e => e.GetGuid()).ToList();
        return new Obra(datos.GetProperty("obraId").GetGuid(), datos.GetProperty("version").GetInt64(), ids[0], ids[1]);
    }

    private Task<long> Registros(string accion, Guid? objeto = null) =>
        bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = '{accion}'" + (objeto is { } id ? $" AND objeto_id = '{id:D}'" : string.Empty));


    private async Task AssertErrorDeAuditoria(HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Equal("ECO-SERVICIO-NO-DISPONIBLE", (await CatalogoApiHost.Leer(respuesta)).GetProperty("code").GetString());
    }

    // ---------- Con la auditoría funcionando ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-19")]
    public async Task Cada_operacion_sensible_deja_su_registro_en_la_misma_base_sin_claves_del_almacen_ni_huellas_completas()
    {
        var obra = await NuevaObra();
        var libroNuevo = await _host.SubirYReferenciar("libro", Epub(rellenoEnBytes: 400), Ana);
        var portadaNueva = await _host.SubirYReferenciar("portada", Png(41, 41, 1000), Ana);

        using var editar = await _host.Json(HttpMethod.Put, $"/administracion/obras/{obra.ObraId}", new { version = obra.Version, titulo = "Auditada 2", autor = "Ana", editorial = "Andina", categoria = "Técnico", portadaRef = portadaNueva }, usuario: Ana);
        var version = (await CatalogoApiHost.Leer(editar)).GetProperty("version").GetInt64();
        using var precio = await _host.Json(HttpMethod.Put, $"/administracion/obras/{obra.ObraId}/ediciones/{obra.ImpresaId}", new { version, precio = 55m }, usuario: Ana);
        version = (await CatalogoApiHost.Leer(precio)).GetProperty("version").GetInt64();
        using var licencia = await _host.Json(HttpMethod.Put, $"/administracion/obras/{obra.ObraId}/ediciones/{obra.DigitalId}", new { version, licencia = Licencia("Otro titular") }, usuario: Ana);
        version = (await CatalogoApiHost.Leer(licencia)).GetProperty("version").GetInt64();
        using var archivo = await _host.Json(HttpMethod.Put, $"/administracion/obras/{obra.ObraId}/ediciones/{obra.DigitalId}", new { version, archivoRef = libroNuevo }, usuario: Ana);
        using var retiro = await _host.Json(HttpMethod.Post, $"/administracion/obras/{obra.ObraId}/ediciones/{obra.DigitalId}/retirar", new { motivo = "Se venció el contrato" }, usuario: Ana);
        version = (await CatalogoApiHost.Leer(archivo)).GetProperty("version").GetInt64();
        using var bajaEdicion = await _host.Json(HttpMethod.Post, $"/administracion/obras/{obra.ObraId}/ediciones/{obra.ImpresaId}/baja", new { version }, usuario: Ana);

        foreach (var respuesta in new[] { editar, precio, licencia, archivo, retiro })
        {
            Assert.True(respuesta.IsSuccessStatusCode, respuesta.StatusCode.ToString());
        }

        Assert.Equal(1, await Registros("ObraRegistrada", obra.ObraId));
        Assert.Equal(1, await Registros("ObraEditada", obra.ObraId));
        Assert.Equal(1, await Registros("PortadaDeEdicionReemplazada", obra.ObraId));
        Assert.Equal(1, await Registros("PrecioDeEdicionCambiado", obra.ImpresaId));
        Assert.Equal(1, await Registros("LicenciaCambiada", obra.DigitalId));
        Assert.Equal(1, await Registros("ArchivoDeEdicionReemplazado", obra.DigitalId));
        Assert.Equal(1, await Registros("EdicionDigitalRetirada", obra.DigitalId));
        Assert.Equal(1, await Registros("EdicionRegistrada", obra.ImpresaId));
        Assert.Equal(1, await Registros("EdicionRegistrada", obra.DigitalId));
        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE detalle::text ~* '(clave_en_almacen|claveEnAlmacen|[0-9a-f]{32,})' AND accion IN ('ObraRegistrada','ObraEditada','ArchivoDeEdicionReemplazado','PortadaDeEdicionReemplazada')"));
        Assert.Equal(Ana, await bd.Consultar<Guid>($"SELECT actor_id FROM auditoria.registros WHERE accion = 'PrecioDeEdicionCambiado' AND objeto_id = '{obra.ImpresaId:D}'"));
        Assert.NotNull(bajaEdicion);
    }

    // ---------- Con la auditoría caída: todo se revierte ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-19")]
    public async Task Un_alta_con_la_auditoria_caida_se_revierte_no_crea_la_obra_ni_consume_las_referencias_y_se_repite_al_volver()
    {
        var portada = await _host.SubirYReferenciar("portada", Png(40, 40, 900), Ana);
        var libro = await _host.SubirYReferenciar("libro", Epub(), Ana);
        var cuerpo = new
        {
            titulo = "No debe existir",
            autor = "Ana",
            editorial = "Andina",
            categoria = "Técnico",
            portadaRef = portada,
            ediciones = new object[] { new { formato = "impresa", isbn = NuevoIsbn(), precio = 40m }, new { formato = "digital", isbn = NuevoIsbn(), precio = 20m, licencia = Licencia(), archivoRef = libro } },
        };

        _host.AuditoriaCaida = true;
        using var caida = await _host.Json(HttpMethod.Post, "/administracion/obras", cuerpo, usuario: Ana);

        await AssertErrorDeAuditoria(caida);
        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM catalogo.obras"));
        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM catalogo.ediciones"));
        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM catalogo.archivos_pendientes WHERE estado <> 'lista'"));
        Assert.Equal(0, _host.Inventario.LlamadasDeRegistrarEdicion);

        _host.AuditoriaCaida = false;
        using var repetida = await _host.Json(HttpMethod.Post, "/administracion/obras", cuerpo, usuario: Ana);
        Assert.Equal(HttpStatusCode.Created, repetida.StatusCode);
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM catalogo.obras"));
    }

    [Theory]
    [InlineData("editar-obra")]
    [InlineData("reemplazar-portada")]
    [InlineData("baja-de-obra")]
    [InlineData("cambiar-precio")]
    [InlineData("cambiar-licencia")]
    [InlineData("reemplazar-libro")]
    [InlineData("baja-de-edicion")]
    [InlineData("retirar-digital")]
    [InlineData("agregar-edicion")]
    [Trait("Criterio", "AC-CAT-19")]
    public async Task Una_operacion_con_la_auditoria_caida_se_revierte_por_completo_y_se_puede_repetir_cuando_vuelve(string escenario)
    {
        // Una obra solo con una impresa para poder agregarle una digital; las demás tienen impresa y digital.
        var obra = escenario == "agregar-edicion" ? await NuevaSoloImpresa() : await NuevaObra();
        var portadaNueva = escenario == "reemplazar-portada" ? await _host.SubirYReferenciar("portada", Png(41, 41, 1000), Ana) : (Guid?)null;
        var libroNuevo = escenario is "reemplazar-libro" or "agregar-edicion" ? await _host.SubirYReferenciar("libro", Epub(rellenoEnBytes: 500), Ana) : (Guid?)null;
        var antes = await Instantanea();
        var llamada = Llamada(escenario, obra, portadaNueva, libroNuevo);

        _host.AuditoriaCaida = true;
        using var caida = await llamada();

        await AssertErrorDeAuditoria(caida);
        Assert.Equal(antes, await Instantanea());

        _host.AuditoriaCaida = false;
        using var repetida = await llamada();
        Assert.True(repetida.IsSuccessStatusCode, $"{escenario}: {(int)repetida.StatusCode}");
    }

    private async Task<Obra> NuevaSoloImpresa()
    {
        var portada = await _host.SubirYReferenciar("portada", Png(40, 40, 900), Ana);
        using var alta = await _host.Json(HttpMethod.Post, "/administracion/obras", new { titulo = "Solo impresa", autor = "Ana", editorial = "Andina", categoria = "Técnico", portadaRef = portada, ediciones = new[] { new { formato = "impresa", isbn = NuevoIsbn(), precio = 40m } } }, usuario: Ana);
        var datos = await CatalogoApiHost.Leer(alta);
        return new Obra(datos.GetProperty("obraId").GetGuid(), datos.GetProperty("version").GetInt64(), datos.GetProperty("edicionIds")[0].GetGuid(), Guid.Empty);
    }

    private Func<Task<HttpResponseMessage>> Llamada(string escenario, Obra obra, Guid? portadaNueva, Guid? libroNuevo) =>
        escenario switch
        {
            "editar-obra" => () => _host.Json(HttpMethod.Put, $"/administracion/obras/{obra.ObraId}", new { version = obra.Version, titulo = "Otro título", autor = "Ana", editorial = "Andina", categoria = "Técnico" }, usuario: Ana),
            "reemplazar-portada" => () => _host.Json(HttpMethod.Put, $"/administracion/obras/{obra.ObraId}", new { version = obra.Version, titulo = "Auditada", autor = "Ana", editorial = "Andina", categoria = "Técnico", portadaRef = portadaNueva }, usuario: Ana),
            "baja-de-obra" => () => _host.Json(HttpMethod.Post, $"/administracion/obras/{obra.ObraId}/baja", new { version = obra.Version }, usuario: Ana),
            "cambiar-precio" => () => _host.Json(HttpMethod.Put, $"/administracion/obras/{obra.ObraId}/ediciones/{obra.ImpresaId}", new { version = obra.Version, precio = 99m }, usuario: Ana),
            "cambiar-licencia" => () => _host.Json(HttpMethod.Put, $"/administracion/obras/{obra.ObraId}/ediciones/{obra.DigitalId}", new { version = obra.Version, licencia = Licencia("Otro titular") }, usuario: Ana),
            "reemplazar-libro" => () => _host.Json(HttpMethod.Put, $"/administracion/obras/{obra.ObraId}/ediciones/{obra.DigitalId}", new { version = obra.Version, archivoRef = libroNuevo }, usuario: Ana),
            "baja-de-edicion" => () => _host.Json(HttpMethod.Post, $"/administracion/obras/{obra.ObraId}/ediciones/{obra.DigitalId}/baja", new { version = obra.Version }, usuario: Ana),
            "retirar-digital" => () => _host.Json(HttpMethod.Post, $"/administracion/obras/{obra.ObraId}/ediciones/{obra.DigitalId}/retirar", new { motivo = "Se venció el contrato" }, usuario: Ana),
            "agregar-edicion" => () => _host.Json(HttpMethod.Post, $"/administracion/obras/{obra.ObraId}/ediciones", new { version = obra.Version, edicion = new { formato = "digital", isbn = NuevoIsbn(), precio = 10m, licencia = Licencia(), archivoRef = libroNuevo } }, usuario: Ana),
            _ => throw new ArgumentOutOfRangeException(nameof(escenario)),
        };

    /// <summary>Todo lo que una operación puede cambiar en el catálogo, para compararlo antes y después de una operación revertida.</summary>
    private async Task<string> Instantanea() =>
        await bd.Consultar<string>(
            """
            SELECT concat_ws('|',
              (SELECT string_agg(concat(id, ':', titulo, ':', version, ':', estado, ':', encode(portada_huella, 'hex'), ':', portada_clave), ',' ORDER BY id) FROM catalogo.obras),
              (SELECT string_agg(concat(id, ':', precio, ':', estado, ':', coalesce(motivo_de_retiro, '')), ',' ORDER BY id) FROM catalogo.ediciones),
              (SELECT string_agg(concat(edicion_id, ':', titular, ':', tipo), ',' ORDER BY edicion_id) FROM catalogo.licencias),
              (SELECT string_agg(concat(edicion_id, ':', clave_en_almacen), ',' ORDER BY edicion_id) FROM catalogo.archivos_digitales),
              (SELECT string_agg(concat(id, ':', estado), ',' ORDER BY id) FROM catalogo.archivos_pendientes))
            """) ?? string.Empty;
}
