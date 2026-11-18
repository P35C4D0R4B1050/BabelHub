using System.Net;
using System.Text.Json;
using Amazon.S3;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using Microsoft.Extensions.DependencyInjection;
using BabelHub.IntegrationTests.Plataforma;
using BabelHub.Shared.Contratos;
using static BabelHub.IntegrationTests.Catalogo.ArchivosDeMuestra;

namespace BabelHub.IntegrationTests.Catalogo;

[CollectionDefinition(Nombre)]
public sealed class ColeccionDeLaApiDelCatalogo : ICollectionFixture<PostgresDelCatalogoYAuditoriaFixture>, ICollectionFixture<AlmacenDeObjetosFixture>
{
    public const string Nombre = "Api del catálogo";
}

/// <summary>
/// Las acciones MVC de la administración y de las portadas contra PostgreSQL, un almacén S3 real y el Inventario simulado (T18; CAT-14, CAT-16, AC-CAT-11, AC-CAT-12): cuerpos estrictos, códigos <c>ECO-…</c>, subida en streaming con el token
/// antifalsificación en una cabecera y la autorización comprobada antes de abrir el flujo, límite de subidas y servicio de portadas.
/// </summary>
[Collection(ColeccionDeLaApiDelCatalogo.Nombre)]
public sealed class AdministracionDelCatalogoApiTests(PostgresDelCatalogoYAuditoriaFixture bd, AlmacenDeObjetosFixture almacen) : IAsyncLifetime
{
    private CatalogoApiHost _host = null!;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _host = await CatalogoApiHost.Crear(bd, almacen.Cliente);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static string Codigo(JsonElement problema) => problema.GetProperty("code").GetString()!;

    private static string[] Campos(JsonElement problema) => [.. problema.GetProperty("errores").EnumerateArray().Select(e => e.GetProperty("campo").GetString()!)];

    private async Task<(Guid ObraId, long Version, Guid EdicionId)> ObraConImpresa(string titulo = "Estructuras de datos", string isbn = "978-0-00-000001-9", int? stock = null)
    {
        var alta = await _host.RegistrarImpresa(titulo, isbn, stock);
        return (alta.GetProperty("obraId").GetGuid(), alta.GetProperty("version").GetInt64(), alta.GetProperty("edicionIds")[0].GetGuid());
    }

    // ---------- Altas y errores ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-11")]
    public async Task Registrar_una_obra_impresa_responde_201_sin_cache_y_la_ficha_queda_registrada_en_el_Inventario()
    {
        var portada = await _host.SubirYReferenciar("portada", Png(800, 600, 6000));

        using var respuesta = await _host.Json(HttpMethod.Post, "/administracion/obras", new { titulo = "Algoritmos", autor = "Ana", editorial = "Andina", categoria = "Técnico", portadaRef = portada, ediciones = new[] { new { formato = "impresa", isbn = "978-0-00-000001-9", precio = 99.5m } } });
        var cuerpo = await CatalogoApiHost.Leer(respuesta);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.Equal("no-store", respuesta.Headers.CacheControl?.ToString());
        Assert.Equal(1, cuerpo.GetProperty("edicionIds").GetArrayLength());
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM catalogo.obras"));
        Assert.Single(_host.Inventario.Registradas);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-11")]
    public async Task Un_campo_desconocido_un_cuerpo_vacio_o_un_enumerado_numerico_son_validacion_uniforme_y_no_crean_nada()
    {
        var portada = await _host.SubirYReferenciar("portada", Png(800, 600, 6000));
        var conCampoExtra = $$"""{"titulo":"X","autor":"A","editorial":"E","categoria":"C","portadaRef":"{{portada}}","ediciones":[{"formato":"impresa","isbn":"978-0-00-000001-9","precio":10}],"esAdministrador":true}""";
        var enumNumerico = $$"""{"titulo":"X","autor":"A","editorial":"E","categoria":"C","portadaRef":"{{portada}}","ediciones":[{"formato":0,"isbn":"978-0-00-000001-9","precio":10}]}""";

        foreach (var cuerpo in new[] { conCampoExtra, enumNumerico, "{}", "no es json", "[]", "null" })
        {
            using var respuesta = await _host.Json(HttpMethod.Post, "/administracion/obras", cuerpo);
            var problema = await CatalogoApiHost.Leer(respuesta);

            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            Assert.Equal("ECO-VALIDACION-INVALIDA", Codigo(problema));
            Assert.DoesNotContain("esAdministrador", problema.ToString(), StringComparison.Ordinal);
        }

        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM catalogo.obras"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-11")]
    public async Task Sin_el_tipo_json_o_sin_cuerpo_es_validacion_y_un_campo_obligatorio_ausente_se_nombra()
    {
        var obra = await ObraConImpresa();
        using var sinTipo = await _host.Solicitud(HttpMethod.Put, $"/administracion/obras/{obra.ObraId}");
        sinTipo.Content = new StringContent("""{"version":1,"titulo":"Nuevo","autor":"A","editorial":"E","categoria":"C"}""", System.Text.Encoding.UTF8, "text/plain");
        using var respuestaSinTipo = await _host.Cliente.SendAsync(sinTipo);
        using var sinVersion = await _host.Json(HttpMethod.Put, $"/administracion/obras/{obra.ObraId}", new { titulo = "Nuevo" });
        var problema = await CatalogoApiHost.Leer(sinVersion);

        Assert.Equal(HttpStatusCode.BadRequest, respuestaSinTipo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sinVersion.StatusCode);
        Assert.Contains("version", Campos(problema));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-14")]
    public async Task Un_isbn_repetido_es_409_con_el_titulo_que_ya_lo_usa_solo_para_la_administracion()
    {
        await ObraConImpresa("Primero", "978-0-00-000001-9");
        var portada = await _host.SubirYReferenciar("portada", Png(800, 600, 6000));

        using var respuesta = await _host.Json(HttpMethod.Post, "/administracion/obras", new { titulo = "Segundo", autor = "A", editorial = "E", categoria = "C", portadaRef = portada, ediciones = new[] { new { formato = "impresa", isbn = "0-00-000001-9", precio = 10m } } });
        var problema = await CatalogoApiHost.Leer(respuesta);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("ECO-ISBN-DUPLICADO", Codigo(problema));
        Assert.Equal("Primero", problema.GetProperty("errores").EnumerateArray().Single(e => e.GetProperty("campo").GetString() == "tituloExistente").GetProperty("clave").GetString());
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-11")]
    public async Task Editar_con_una_version_vieja_es_409_conflicto_y_baja_de_obra_es_204()
    {
        var obra = await ObraConImpresa();
        using var editada = await _host.Json(HttpMethod.Put, $"/administracion/obras/{obra.ObraId}", new { version = obra.Version, titulo = "Otro título", autor = "Ana Quispe", editorial = "Editorial Andina", categoria = "Técnico" });
        var nueva = (await CatalogoApiHost.Leer(editada)).GetProperty("version").GetInt64();

        using var vieja = await _host.Json(HttpMethod.Put, $"/administracion/obras/{obra.ObraId}", new { version = obra.Version, titulo = "Tercero", autor = "Ana Quispe", editorial = "Editorial Andina", categoria = "Técnico" });
        using var baja = await _host.Json(HttpMethod.Post, $"/administracion/obras/{obra.ObraId}/baja", new { version = nueva });

        Assert.Equal(HttpStatusCode.OK, editada.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, vieja.StatusCode);
        Assert.Equal("ECO-CONFLICTO", Codigo(await CatalogoApiHost.Leer(vieja)));
        Assert.Equal(HttpStatusCode.NoContent, baja.StatusCode);
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM catalogo.obras WHERE estado = 'baja'"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-11")]
    public async Task Una_obra_o_una_edicion_inexistente_es_404_con_su_codigo()
    {
        using var obra = await _host.Json(HttpMethod.Put, $"/administracion/obras/{Guid.NewGuid()}", new { version = 1, titulo = "X", autor = "A", editorial = "E", categoria = "C" });
        using var edicion = await _host.Json(HttpMethod.Post, $"/administracion/obras/{Guid.NewGuid()}/ediciones/{Guid.NewGuid()}/retirar", new { motivo = "x" });
        using var rutaMala = await _host.Json(HttpMethod.Put, "/administracion/obras/no-es-un-guid", new { version = 1 });

        Assert.Equal(HttpStatusCode.NotFound, obra.StatusCode);
        Assert.Equal("ECO-NO-ENCONTRADO", Codigo(await CatalogoApiHost.Leer(obra)));
        Assert.Equal(HttpStatusCode.NotFound, edicion.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, rutaMala.StatusCode);
    }

    // ---------- Subida de archivos ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Una_subida_sin_el_token_antifalsificacion_se_rechaza_y_no_toca_la_base_ni_el_almacen()
    {
        using var respuesta = await _host.Subir("libro", Epub(), conToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("ECO-VALIDACION-INVALIDA", Codigo(await CatalogoApiHost.Leer(respuesta)));
        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM catalogo.archivos_pendientes"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Un_cliente_o_un_visitante_recibe_403_o_401_antes_de_crear_nada_y_queda_el_acceso_denegado_registrado()
    {
        var antes = await bd.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE accion = 'AccesoDenegado'");

        using var comoCliente = await _host.Subir("libro", Epub(), rol: RolesDeUsuario.Cliente);
        using var comoVisitante = await _host.Subir("libro", Epub(), rol: null);

        Assert.Equal(HttpStatusCode.Forbidden, comoCliente.StatusCode);
        Assert.Equal("ECO-PROHIBIDO", Codigo(await CatalogoApiHost.Leer(comoCliente)));
        Assert.Equal(HttpStatusCode.Unauthorized, comoVisitante.StatusCode);
        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM catalogo.archivos_pendientes"));
        Assert.Equal(antes + 2, await bd.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE accion = 'AccesoDenegado'"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task La_finalidad_debe_venir_una_sola_vez_y_ser_portada_o_libro()
    {
        foreach (var ruta in new[] { "/administracion/archivos", "/administracion/archivos?finalidad=otra", "/administracion/archivos?finalidad=libro&finalidad=portada", "/administracion/archivos?finalidad=LIBRO" })
        {
            using var respuesta = await _host.SubirConRuta(ruta, new MemoryStream(Pdf()));

            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Un_libro_valido_responde_201_con_la_huella_el_tipo_y_el_tamano_y_queda_en_el_almacen_privado()
    {
        var epub = Epub(rellenoEnBytes: 4000);

        using var respuesta = await _host.Subir("libro", epub);
        var cuerpo = await CatalogoApiHost.Leer(respuesta);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.Equal("epub", cuerpo.GetProperty("tipo").GetString());
        Assert.Equal(epub.Length, cuerpo.GetProperty("tamanoEnBytes").GetInt64());
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(epub)), cuerpo.GetProperty("huellaSha256").GetString());
        Assert.DoesNotContain("clave", cuerpo.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM catalogo.archivos_pendientes WHERE estado = 'lista' AND finalidad = 'libro'"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Un_ejecutable_renombrado_es_422_archivo_invalido_y_un_libro_enorme_es_413_demasiado_grande()
    {
        var ejecutable = new byte[] { 0x4D, 0x5A, 0x90, 0x00 }.Concat(new byte[3000]).ToArray();

        using var invalido = await _host.Subir("libro", ejecutable);
        using var grande = await _host.Subir("portada", Png(800, 600, 3 * 1024 * 1024));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalido.StatusCode);
        var problemaInvalido = await CatalogoApiHost.Leer(invalido);
        Assert.Equal("ECO-ARCHIVO-INVALIDO", Codigo(problemaInvalido));
        Assert.Equal("tipo-no-valido", problemaInvalido.GetProperty("errores")[0].GetProperty("clave").GetString());
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, grande.StatusCode);
        Assert.Equal("ECO-ARCHIVO-DEMASIADO-GRANDE", Codigo(await CatalogoApiHost.Leer(grande)));
        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM catalogo.archivos_pendientes WHERE estado = 'lista'"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Un_almacen_que_no_responde_es_503_servicio_no_disponible_y_no_deja_la_subida_lista()
    {
        await using var sinAlmacen = await CatalogoApiHost.Crear(bd, new AmazonS3Client(
            new Amazon.Runtime.BasicAWSCredentials("acceso-de-pruebas-0123456789", "secreta-de-pruebas-0123456789-abcdefghijkl"),
            new AmazonS3Config { ServiceURL = "http://127.0.0.1:1", ForcePathStyle = true, MaxErrorRetry = 0, Timeout = TimeSpan.FromSeconds(5), AuthenticationRegion = "us-east-1" }),
            crearContenedores: false);

        using var respuesta = await sinAlmacen.Subir("libro", Epub());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Equal("ECO-SERVICIO-NO-DISPONIBLE", Codigo(await CatalogoApiHost.Leer(respuesta)));
        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM catalogo.archivos_pendientes WHERE estado = 'lista'"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Mas_de_dos_subidas_simultaneas_del_mismo_usuario_son_429_con_retry_after_y_el_cupo_se_libera_al_terminar()
    {
        var uno = new FlujoQueEspera(Epub());
        var dos = new FlujoQueEspera(Epub());
        var primera = _host.Subir("libro", uno);
        var segunda = _host.Subir("libro", dos);
        await Task.WhenAll(uno.Empezo, dos.Empezo).WaitAsync(TimeSpan.FromSeconds(30));
        var limitador = _host.Servicios.GetRequiredService<LimitadorDeSubidas>();
        for (var intento = 0; intento < 1500 && limitador.EnCurso < 2; intento++)
        {
            await Task.Delay(20);
        }

        Assert.Equal(2, limitador.EnCurso);

        using var tercera = await _host.Subir("libro", Epub());
        var problema = await CatalogoApiHost.Leer(tercera);
        using var deOtroUsuario = await _host.Subir("libro", Epub(), usuario: Guid.NewGuid());

        uno.Liberar();
        dos.Liberar();
        using var r1 = await primera;
        using var r2 = await segunda;
        using var despues = await _host.Subir("libro", Epub());

        Assert.Equal(HttpStatusCode.TooManyRequests, tercera.StatusCode);
        Assert.Equal("ECO-LIMITE-EXCEDIDO", Codigo(problema));
        Assert.Equal("30", tercera.Headers.GetValues("Retry-After").Single());
        Assert.Equal(HttpStatusCode.Created, deOtroUsuario.StatusCode);
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
        Assert.Equal(HttpStatusCode.Created, despues.StatusCode);
    }

    // ---------- Portadas ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task La_portada_se_sirve_con_el_tipo_detectado_nosniff_politica_de_contenido_y_cache_inmutable_e_ignora_la_consulta()
    {
        var png = Png(800, 600, 6000);
        await _host.RegistrarImpresa();
        var huella = await bd.Consultar<byte[]>("SELECT portada_huella FROM catalogo.obras LIMIT 1");
        var hex = Convert.ToHexStringLower(huella!);

        using var limpia = await _host.Cliente.GetAsync($"/portadas/{hex}");
        using var conConsulta = await _host.Cliente.GetAsync($"/portadas/{hex}?w=1&evil=%3Cscript%3E");

        foreach (var respuesta in new[] { limpia, conConsulta })
        {
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.Equal("image/png", respuesta.Content.Headers.ContentType?.MediaType);
            Assert.Equal("nosniff", respuesta.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Contains("default-src 'none'", string.Join(';', respuesta.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
            Assert.Equal("public, max-age=31536000, immutable", respuesta.Headers.CacheControl?.ToString());
            Assert.NotEmpty(await respuesta.Content.ReadAsByteArrayAsync());
        }

        Assert.Equal(png.Length, (await limpia.Content.ReadAsByteArrayAsync()).Length);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Una_huella_ajena_mal_formada_o_de_un_libro_es_404_sin_cache_inmutable()
    {
        var epub = Epub(rellenoEnBytes: 2000);
        using var subida = await _host.Subir("libro", epub);
        var huellaDelLibro = (await CatalogoApiHost.Leer(subida)).GetProperty("huellaSha256").GetString()!;
        // Un libro consumido por una edición digital: su huella nunca se sirve en /portadas.
        var portada = await _host.SubirYReferenciar("portada", Png(10, 10, 800));
        var libroRef = await _host.SubirYReferenciar("libro", epub);
        using var alta = await _host.Json(HttpMethod.Post, "/administracion/obras", new
        {
            titulo = "Con libro",
            autor = "A",
            editorial = "E",
            categoria = "C",
            portadaRef = portada,
            ediciones = new[] { new { formato = "digital", isbn = "9790000000018", precio = 20m, licencia = new { tipo = "dominioPublico", titular = "Dominio público", territorios = new[] { "PE" }, formato = "epub" }, archivoRef = libroRef } },
        });
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        foreach (var huella in new[] { huellaDelLibro, new string('a', 64), huellaDelLibro.ToUpperInvariant(), new string('a', 63), "no-es-una-huella", "../../etc/passwd" })
        {
            using var respuesta = await _host.Cliente.GetAsync($"/portadas/{Uri.EscapeDataString(huella)}");

            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
            Assert.DoesNotContain("immutable", respuesta.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
            Assert.Equal("ECO-NO-ENCONTRADO", Codigo(await CatalogoApiHost.Leer(respuesta)));
        }
    }

    // ---------- Ingresos ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task El_formulario_de_ingreso_emite_el_token_sin_escribir_y_el_ingreso_con_ese_token_responde_201_y_se_lista()
    {
        var obra = await ObraConImpresa();
        var llamadasAntes = _host.Inventario.LlamadasDeRegistrarIngreso;

        using var formulario = await _host.Json(HttpMethod.Get, $"/administracion/ediciones/{obra.EdicionId}/ingresos/nuevo");
        var token = (await CatalogoApiHost.Leer(formulario)).GetProperty("token").GetString();
        Assert.Equal(llamadasAntes, _host.Inventario.LlamadasDeRegistrarIngreso);
        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM catalogo.ingresos_solicitados"));

        using var ingreso = await _host.Json(HttpMethod.Post, $"/administracion/ediciones/{obra.EdicionId}/ingresos", new { cantidad = 30, referencia = "GR-001", token });
        var resultado = await CatalogoApiHost.Leer(ingreso);
        using var lista = await _host.Json(HttpMethod.Get, $"/administracion/ediciones/{obra.EdicionId}/ingresos");

        Assert.Equal(HttpStatusCode.Created, ingreso.StatusCode);
        Assert.Equal("registrado", resultado.GetProperty("estado").GetString());
        Assert.Equal(30, _host.Inventario.StockDe(obra.EdicionId));
        Assert.Equal(HttpStatusCode.OK, lista.StatusCode);
        Assert.Equal(30, (await CatalogoApiHost.Leer(lista)).GetProperty("ingresos")[0].GetProperty("cantidad").GetInt32());
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task Reintentar_o_conciliar_un_ingreso_inexistente_es_404_y_un_cuerpo_de_conciliacion_incompleto_es_validacion()
    {
        var obra = await ObraConImpresa();
        var inexistente = Guid.NewGuid();

        using var reintento = await _host.Json(HttpMethod.Post, $"/administracion/ediciones/{obra.EdicionId}/ingresos/{inexistente}/reintentar");
        using var conciliacion = await _host.Json(HttpMethod.Post, $"/administracion/ediciones/{obra.EdicionId}/ingresos/{inexistente}/conciliar", new { resultado = "registrado" });
        using var sinResultado = await _host.Json(HttpMethod.Post, $"/administracion/ediciones/{obra.EdicionId}/ingresos/{inexistente}/conciliar", new { confirmaFallido = true });

        Assert.Equal(HttpStatusCode.NotFound, reintento.StatusCode);
        Assert.Equal("ECO-NO-ENCONTRADO", Codigo(await CatalogoApiHost.Leer(reintento)));
        Assert.Equal(HttpStatusCode.NotFound, conciliacion.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sinResultado.StatusCode);
    }

    // ---------- Flujos de lectura ----------

    /// <summary>Un cuerpo que entrega su primer trozo, avisa que empezó y espera a que lo liberen para seguir (mantiene un cupo de subida ocupado).</summary>
    private sealed class FlujoQueEspera(byte[] datos) : Stream
    {
        private readonly TaskCompletionSource _empezo = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _liberado = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _posicion;

        public Task Empezo => _empezo.Task;

        public void Liberar() => _liberado.TrySetResult();

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_posicion == 0)
            {
                // El primer trozo (los primeros bytes) y después espera: la Aplicación ya tomó su cupo.
                var primero = Math.Min(buffer.Length, 16);
                datos.AsMemory(0, primero).CopyTo(buffer);
                _posicion = primero;
                _empezo.TrySetResult();
                return primero;
            }

            await _liberado.Task.WaitAsync(cancellationToken);
            var restante = Math.Min(buffer.Length, datos.Length - _posicion);
            datos.AsMemory(_posicion, restante).CopyTo(buffer);
            _posicion += restante;
            return restante;
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
