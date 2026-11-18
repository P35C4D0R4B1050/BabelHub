using System.Net;
using BabelHub.Shared.Contratos;
using static BabelHub.IntegrationTests.Catalogo.ArchivosDeMuestra;

namespace BabelHub.IntegrationTests.Catalogo;

/// <summary>
/// La matriz de autorización de la administración del catálogo (T19; CAT-14, AC-CAT-11, AC-CAT-19): <b>cada</b> operación (archivos, obras, ediciones, baja, retiro, ingresos, su lista, el reintento y la conciliación) con encargado de sucursal,
/// personal de logística, cliente y visitante se rechaza (403 o 401) <b>antes</b> de crear nada o de llamar al Inventario, y deja <b>un solo</b> evento de acceso denegado con la plantilla de la ruta. El administrador central, en cambio, nunca recibe un 401 ni un 403.
/// El filtro de área de la interfaz (módulo Web) no se compone aquí: es la defensa de la Aplicación la que se prueba.
/// </summary>
[Collection(ColeccionDeLaApiDelCatalogo.Nombre)]
public sealed class MatrizDeAutorizacionDelCatalogoTests(PostgresDelCatalogoYAuditoriaFixture bd, BabelHub.IntegrationTests.Plataforma.AlmacenDeObjetosFixture almacen) : IAsyncLifetime
{
    private CatalogoApiHost _host = null!;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _host = await CatalogoApiHost.Crear(bd, almacen.Cliente);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    /// <summary>Una operación de la administración: su método, su ruta (con <c>{g}</c> por cada identificador), un cuerpo mínimo y la plantilla con la que debe quedar registrada.</summary>
    public sealed record Operacion(string Nombre, HttpMethod Metodo, string Ruta, object? Cuerpo, string Plantilla, bool EsSubida = false)
    {
        public override string ToString() => Nombre;
    }

    private static readonly object Vacio = new { };

    public static readonly IReadOnlyList<Operacion> Operaciones =
    [
        new("subir-archivo", HttpMethod.Post, "/administracion/archivos?finalidad=libro", null, "/administracion/archivos", EsSubida: true),
        new("registrar-obra", HttpMethod.Post, "/administracion/obras", new { titulo = "X", autor = "A", editorial = "E", categoria = "C", ediciones = Array.Empty<object>() }, "/administracion/obras"),
        new("editar-obra", HttpMethod.Put, "/administracion/obras/{g}", new { version = 1, titulo = "X", autor = "A", editorial = "E", categoria = "C" }, "/administracion/obras/{obraId}"),
        new("baja-de-obra", HttpMethod.Post, "/administracion/obras/{g}/baja", new { version = 1 }, "/administracion/obras/{obraId}/baja"),
        new("registrar-edicion", HttpMethod.Post, "/administracion/obras/{g}/ediciones", new { version = 1, edicion = new { formato = "impresa", isbn = "978-0-00-000001-9", precio = 10 } }, "/administracion/obras/{obraId}/ediciones"),
        new("editar-edicion", HttpMethod.Put, "/administracion/obras/{g}/ediciones/{g}", new { version = 1, precio = 10 }, "/administracion/obras/{obraId}/ediciones/{edicionId}"),
        new("baja-de-edicion", HttpMethod.Post, "/administracion/obras/{g}/ediciones/{g}/baja", new { version = 1 }, "/administracion/obras/{obraId}/ediciones/{edicionId}/baja"),
        new("retirar-edicion-digital", HttpMethod.Post, "/administracion/obras/{g}/ediciones/{g}/retirar", new { motivo = "Sin licencia" }, "/administracion/obras/{obraId}/ediciones/{edicionId}/retirar"),
        new("nuevo-ingreso", HttpMethod.Get, "/administracion/ediciones/{g}/ingresos/nuevo", null, "/administracion/ediciones/{edicionId}/ingresos/nuevo"),
        new("registrar-ingreso", HttpMethod.Post, "/administracion/ediciones/{g}/ingresos", new { cantidad = 5, token = "x" }, "/administracion/ediciones/{edicionId}/ingresos"),
        new("listar-ingresos", HttpMethod.Get, "/administracion/ediciones/{g}/ingresos", null, "/administracion/ediciones/{edicionId}/ingresos"),
        new("reintentar-ingreso", HttpMethod.Post, "/administracion/ediciones/{g}/ingresos/{g}/reintentar", null, "/administracion/ediciones/{edicionId}/ingresos/{ingresoId}/reintentar"),
        new("conciliar-ingreso", HttpMethod.Post, "/administracion/ediciones/{g}/ingresos/{g}/conciliar", new { resultado = "registrado" }, "/administracion/ediciones/{edicionId}/ingresos/{ingresoId}/conciliar"),
    ];

    public static TheoryData<string> NombresDeOperaciones => new(Operaciones.Select(o => o.Nombre));

    private static string Concretar(string ruta)
    {
        while (ruta.Contains("{g}", StringComparison.Ordinal))
        {
            var indice = ruta.IndexOf("{g}", StringComparison.Ordinal);
            ruta = ruta.Remove(indice, 3).Insert(indice, Guid.NewGuid().ToString("D"));
        }

        return ruta;
    }

    private async Task<HttpResponseMessage> Ejecutar(Operacion operacion, string? rol, Guid usuario)
    {
        var ruta = Concretar(operacion.Ruta);
        if (operacion.EsSubida)
        {
            return await _host.SubirConRuta(ruta, new MemoryStream(Epub()), rol, usuario);
        }

        return await _host.Json(operacion.Metodo, ruta, operacion.Cuerpo, rol, usuario);
    }

    private async Task<long> Denegaciones(string plantilla, string condicion) =>
        await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'AccesoDenegado' AND ruta = '{plantilla}' AND {condicion}");

    private async Task<long> FilasDelCatalogo() =>
        await bd.Consultar<long>("SELECT (SELECT count(*) FROM catalogo.obras) + (SELECT count(*) FROM catalogo.ediciones) + (SELECT count(*) FROM catalogo.archivos_pendientes) + (SELECT count(*) FROM catalogo.ingresos_solicitados)");

    [Theory]
    [MemberData(nameof(NombresDeOperaciones))]
    [Trait("Criterio", "AC-CAT-11")]
    public async Task Un_encargado_de_sucursal_personal_de_logistica_o_cliente_recibe_403_sin_efectos_y_un_solo_evento_con_la_ruta(string nombre)
    {
        var operacion = Operaciones.Single(o => o.Nombre == nombre);
        foreach (var rol in new[] { RolesDeUsuario.EncargadoDeSucursal, RolesDeUsuario.PersonalDeLogistica, RolesDeUsuario.Cliente })
        {
            var usuario = Guid.NewGuid();

            using var respuesta = await Ejecutar(operacion, rol, usuario);
            var problema = await CatalogoApiHost.Leer(respuesta);

            Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
            Assert.Equal("ECO-PROHIBIDO", problema.GetProperty("code").GetString());
            Assert.Equal(1, await Denegaciones(operacion.Plantilla, $"actor_id = '{usuario}' AND motivo = 'fuera-de-rol' AND resultado = 'rechazado'"));
        }

        Assert.Equal(0, await FilasDelCatalogo());
        Assert.Equal(0, _host.Inventario.LlamadasDeRegistrarEdicion + _host.Inventario.LlamadasDeRegistrarIngreso + _host.Inventario.LlamadasDeListar);
    }

    [Theory]
    [MemberData(nameof(NombresDeOperaciones))]
    [Trait("Criterio", "AC-CAT-11")]
    public async Task Un_visitante_recibe_401_sin_efectos_y_un_solo_evento_sin_sesion_con_la_ruta(string nombre)
    {
        var operacion = Operaciones.Single(o => o.Nombre == nombre);
        var antes = await Denegaciones(operacion.Plantilla, "actor_tipo = 'anonimo' AND motivo = 'sin-sesion'");

        using var respuesta = await Ejecutar(operacion, rol: null, Guid.NewGuid());
        var problema = await CatalogoApiHost.Leer(respuesta);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal("ECO-NO-AUTENTICADO", problema.GetProperty("code").GetString());
        Assert.Equal(antes + 1, await Denegaciones(operacion.Plantilla, "actor_tipo = 'anonimo' AND motivo = 'sin-sesion'"));
        Assert.Equal(0, await FilasDelCatalogo());
        Assert.Equal(0, _host.Inventario.LlamadasDeRegistrarEdicion + _host.Inventario.LlamadasDeRegistrarIngreso + _host.Inventario.LlamadasDeListar);
    }

    [Theory]
    [MemberData(nameof(NombresDeOperaciones))]
    [Trait("Criterio", "AC-CAT-19")]
    public async Task La_administracion_central_nunca_recibe_401_ni_403_y_no_deja_accesos_denegados(string nombre)
    {
        var operacion = Operaciones.Single(o => o.Nombre == nombre);
        var usuario = Guid.NewGuid();

        using var respuesta = await Ejecutar(operacion, RolesDeUsuario.AdministradorCentral, usuario);

        Assert.NotEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal(0, await Denegaciones(operacion.Plantilla, $"actor_id = '{usuario}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-11")]
    public async Task Un_rol_inventado_o_con_otra_forma_no_concede_nada()
    {
        foreach (var rol in new[] { "administrador", "ADMINISTRADOR-CENTRAL", "administrador-central ", "sistema", "integracion" })
        {
            using var respuesta = await _host.Json(HttpMethod.Post, "/administracion/obras", new { titulo = "X" }, rol, Guid.NewGuid());

            Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        }

        Assert.Equal(0, await FilasDelCatalogo());
    }
}
