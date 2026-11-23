using System.Net;
using System.Text.Json;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.IntegrationTests.Plataforma;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;
using static BabelHub.IntegrationTests.Catalogo.ArchivosDeMuestra;

namespace BabelHub.IntegrationTests.Catalogo;

/// <summary>
/// La descripción opcional de la obra contra PostgreSQL real y las acciones de la administración (T31 y T33; CAT-01, CAT-14, CAT-19, AC-CAT-20): se guarda y se lee (nula si falta), 600 se acepta y 601 se rechaza con el
/// campo, la edición la reemplaza o la quita bajo el rol del esquema (el permiso de columna), un encargado de sucursal es rechazado con su acceso denegado, la auditoría solo nombra el campo y la migración es idempotente.
/// </summary>
[Collection(ColeccionDeLaApiDelCatalogo.Nombre)]
public sealed class DescripcionDeLaObraConPostgresTests(PostgresDelCatalogoYAuditoriaFixture bd, AlmacenDeObjetosFixture almacen) : IAsyncLifetime
{
    private const string TextoQueNoDebeAuditarse = "sinopsis-que-no-debe-aparecer-en-la-auditoria";

    private CatalogoApiHost _host = null!;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _host = await CatalogoApiHost.Crear(bd, almacen.Cliente);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<HttpResponseMessage> Registrar(string titulo, string isbn, string? descripcion, bool conDescripcion = true)
    {
        var portada = await _host.SubirYReferenciar("portada", Png(80, 60, 900));
        var ediciones = new[] { new { formato = "impresa", isbn, precio = 40m } };
        object cuerpo = conDescripcion
            ? new { titulo, autor = "Ana", editorial = "Andina", categoria = "Técnico", portadaRef = portada, descripcion, ediciones }
            : new { titulo, autor = "Ana", editorial = "Andina", categoria = "Técnico", portadaRef = portada, ediciones };
        return await _host.Json(HttpMethod.Post, "/administracion/obras", cuerpo);
    }

    private static Guid ObraId(JsonElement alta) => alta.GetProperty("obraId").GetGuid();

    private Task<string?> DescripcionGuardada(Guid obraId) => bd.Consultar<string>($"SELECT descripcion FROM catalogo.obras WHERE id = '{obraId:D}'");

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public async Task Una_descripcion_de_600_se_guarda_una_de_601_se_rechaza_con_el_campo_y_sin_o_con_solo_espacios_queda_nula()
    {
        using var con600 = await Registrar("Seiscientos", "978-0-00-000001-9", new string('a', 600));
        using var con601 = await Registrar("Seiscientos uno", "9790000000018", new string('b', 601));
        using var espacios = await Registrar("Espacios", "9790000000025", "    ");
        using var ausente = await Registrar("Sin campo", "9790000000032", null, conDescripcion: false);

        Assert.Equal(HttpStatusCode.Created, con600.StatusCode);
        Assert.Equal(600, (await DescripcionGuardada(ObraId(await CatalogoApiHost.Leer(con600))))!.Length);
        Assert.Equal(HttpStatusCode.BadRequest, con601.StatusCode);
        var problema = await CatalogoApiHost.Leer(con601);
        Assert.Equal("ECO-VALIDACION-INVALIDA", problema.GetProperty("code").GetString());
        Assert.Contains("descripcion", problema.GetProperty("errores").EnumerateArray().Select(e => e.GetProperty("campo").GetString()));
        Assert.Equal(HttpStatusCode.Created, espacios.StatusCode);
        Assert.Null(await DescripcionGuardada(ObraId(await CatalogoApiHost.Leer(espacios))));
        Assert.Equal(HttpStatusCode.Created, ausente.StatusCode);
        Assert.Null(await DescripcionGuardada(ObraId(await CatalogoApiHost.Leer(ausente))));
        Assert.Equal(3, await bd.Consultar<long>("SELECT count(*) FROM catalogo.obras"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public async Task Un_caracter_de_control_se_rechaza_y_los_saltos_de_linea_y_el_marcado_se_guardan_tal_como_se_escribieron()
    {
        using var conTabulador = await Registrar("Con tab", "978-0-00-000001-9", "uno\tdos");
        const string Texto = "<script>alert(1)</script>\nsegunda línea &lt;b&gt;";
        using var valida = await Registrar("Con marcado", "9790000000018", Texto);

        Assert.Equal(HttpStatusCode.BadRequest, conTabulador.StatusCode);
        Assert.Equal(HttpStatusCode.Created, valida.StatusCode);
        Assert.Equal(Texto, await DescripcionGuardada(ObraId(await CatalogoApiHost.Leer(valida))));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public async Task La_edicion_reemplaza_o_quita_la_descripcion_con_el_permiso_de_columna_y_la_auditoria_solo_nombra_el_campo()
    {
        using var alta = await Registrar("Base", "978-0-00-000001-9", "Original");
        var altaJson = await CatalogoApiHost.Leer(alta);
        var obraId = ObraId(altaJson);
        var version = altaJson.GetProperty("version").GetInt64();

        using var reemplazo = await _host.Json(HttpMethod.Put, $"/administracion/obras/{obraId}", new { version, titulo = "Base", autor = "Ana", editorial = "Andina", categoria = "Técnico", descripcion = TextoQueNoDebeAuditarse });
        Assert.Equal(HttpStatusCode.OK, reemplazo.StatusCode);
        Assert.Equal(TextoQueNoDebeAuditarse, await DescripcionGuardada(obraId));
        var version2 = (await CatalogoApiHost.Leer(reemplazo)).GetProperty("version").GetInt64();

        using var demasiado = await _host.Json(HttpMethod.Put, $"/administracion/obras/{obraId}", new { version = version2, titulo = "Base", autor = "Ana", editorial = "Andina", categoria = "Técnico", descripcion = new string('c', 601) });
        Assert.Equal(HttpStatusCode.BadRequest, demasiado.StatusCode);
        Assert.Equal(TextoQueNoDebeAuditarse, await DescripcionGuardada(obraId));

        using var quitada = await _host.Json(HttpMethod.Put, $"/administracion/obras/{obraId}", new { version = version2, titulo = "Base", autor = "Ana", editorial = "Andina", categoria = "Técnico", descripcion = "" });
        Assert.Equal(HttpStatusCode.OK, quitada.StatusCode);
        Assert.Null(await DescripcionGuardada(obraId));

        Assert.Equal(2, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'ObraEditada' AND objeto_id = '{obraId:D}' AND detalle->>'camposCambiados' = 'descripcion'"));
        Assert.Equal(0, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE objeto_id = '{obraId:D}' AND detalle::text LIKE '%{TextoQueNoDebeAuditarse}%'"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public async Task Un_encargado_de_sucursal_que_intenta_editar_la_descripcion_recibe_403_y_queda_el_acceso_denegado()
    {
        using var alta = await Registrar("Base", "978-0-00-000001-9", "Original");
        var altaJson = await CatalogoApiHost.Leer(alta);
        var obraId = ObraId(altaJson);
        var antes = await bd.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE accion = 'AccesoDenegado'");

        using var respuesta = await _host.Json(
            HttpMethod.Put,
            $"/administracion/obras/{obraId}",
            new { version = altaJson.GetProperty("version").GetInt64(), titulo = "Base", autor = "Ana", editorial = "Andina", categoria = "Técnico", descripcion = "Cambiada" },
            rol: RolesDeUsuario.EncargadoDeSucursal,
            usuario: Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Equal("Original", await DescripcionGuardada(obraId));
        Assert.Equal(antes + 1, await bd.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE accion = 'AccesoDenegado'"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public async Task La_consulta_publica_y_la_de_administracion_devuelven_la_descripcion_y_nulo_si_falta()
    {
        using var con = await Registrar("Con", "978-0-00-000001-9", "Una sinopsis.");
        using var sin = await Registrar("Sin", "9790000000018", null, conDescripcion: false);
        var conId = ObraId(await CatalogoApiHost.Leer(con));
        var sinId = ObraId(await CatalogoApiHost.Leer(sin));

        await using var alcance = _host.Servicios.CreateAsyncScope();
        var consulta = alcance.ServiceProvider.GetRequiredService<IConsultaDeCatalogo>();

        Assert.Equal("Una sinopsis.", (await consulta.ObtenerObra(conId)).Valor.Descripcion);
        Assert.Null((await consulta.ObtenerObra(sinId)).Valor.Descripcion);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public async Task La_migracion_es_idempotente_la_columna_nunca_guarda_una_cadena_vacia_ni_mas_de_600()
    {
        // Aplicar otra vez el esquema completo (como hace cada despliegue) no falla ni cambia la columna.
        await bd.Ejecutar("ALTER TABLE catalogo.obras ADD COLUMN IF NOT EXISTS descripcion text CHECK (char_length(descripcion) BETWEEN 1 AND 600)");
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM information_schema.columns WHERE table_schema = 'catalogo' AND table_name = 'obras' AND column_name = 'descripcion'"));

        using var alta = await Registrar("Base", "978-0-00-000001-9", "x");
        var obraId = ObraId(await CatalogoApiHost.Leer(alta));
        await Assert.ThrowsAnyAsync<Exception>(() => bd.Ejecutar($"UPDATE catalogo.obras SET descripcion = '' WHERE id = '{obraId:D}'"));
        await Assert.ThrowsAnyAsync<Exception>(() => bd.Ejecutar($"UPDATE catalogo.obras SET descripcion = repeat('a', 601) WHERE id = '{obraId:D}'"));
    }
}
