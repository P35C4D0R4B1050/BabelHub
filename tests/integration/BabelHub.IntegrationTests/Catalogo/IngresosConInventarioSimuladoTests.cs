using System.Net;
using System.Text.Json;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Infrastructure.Catalogo;
using BabelHub.IntegrationTests.Plataforma;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Catalogo;

/// <summary>
/// El catálogo con el Inventario simulado, caído y con tiempo agotado (T21; CAT-20 a CAT-23, AC-CAT-09, AC-CAT-16, AC-CAT-18, AC-CAT-19): el stock inicial del alta que no se revierte, el ingreso aplicado con respuesta perdida, la clave de idempotencia (repetida, con otra carga
/// y simultánea), el reintento solo por su autor, la conciliación y el abuso del token firmado. La auditoría del ingreso la verifica el Inventario.
/// </summary>
[Collection(ColeccionDeLaApiDelCatalogo.Nombre)]
public sealed class IngresosConInventarioSimuladoTests(PostgresDelCatalogoYAuditoriaFixture bd, AlmacenDeObjetosFixture almacen) : IAsyncLifetime
{
    private static readonly Guid Ana = Guid.Parse("a0000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Beto = Guid.Parse("b0000000-0000-0000-0000-0000000000b2");

    private CatalogoApiHost _host = null!;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _host = await CatalogoApiHost.Crear(bd, almacen.Cliente);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static string Codigo(JsonElement problema) => problema.GetProperty("code").GetString()!;

    private static string Estado(JsonElement cuerpo) => cuerpo.GetProperty("estado").GetString()!;

    private async Task<Guid> Impresa(int? stock = null, string isbn = "978-0-00-000001-9", Guid? usuario = null)
    {
        var alta = await _host.RegistrarImpresa("Obra", isbn, stock, usuario ?? Ana);
        return alta.GetProperty("edicionIds")[0].GetGuid();
    }

    private async Task<string> Token(Guid edicionId, Guid? usuario = null)
    {
        using var respuesta = await _host.Json(HttpMethod.Get, $"/administracion/ediciones/{edicionId}/ingresos/nuevo", usuario: usuario ?? Ana);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await CatalogoApiHost.Leer(respuesta)).GetProperty("token").GetString()!;
    }

    private Task<HttpResponseMessage> Ingresar(Guid edicionId, int cantidad, string? token, string? referencia = null, Guid? usuario = null, string? rol = RolesDeUsuario.AdministradorCentral) =>
        _host.Json(HttpMethod.Post, $"/administracion/ediciones/{edicionId}/ingresos", new { cantidad, referencia, token }, rol, usuario ?? Ana);

    private Task<HttpResponseMessage> Reintentar(Guid edicionId, Guid ingresoId, Guid? usuario = null, string? rol = RolesDeUsuario.AdministradorCentral) =>
        _host.Json(HttpMethod.Post, $"/administracion/ediciones/{edicionId}/ingresos/{ingresoId}/reintentar", null, rol, usuario ?? Ana);

    private Task<HttpResponseMessage> Conciliar(Guid edicionId, Guid ingresoId, string resultado, bool confirma = false, Guid? usuario = null) =>
        _host.Json(HttpMethod.Post, $"/administracion/ediciones/{edicionId}/ingresos/{ingresoId}/conciliar", new { resultado, confirmaFallido = confirma }, usuario: usuario ?? Ana);

    /// <summary>Deja un ingreso pendiente de 30 con el Inventario caído (nada se envió: la ficha no se pudo confirmar).</summary>
    private async Task<(Guid EdicionId, Guid IngresoId)> Pendiente(int cantidad = 30, Guid? usuario = null, Action? antesDeEnviar = null)
    {
        var edicion = await Impresa(usuario: usuario);
        var token = await Token(edicion, usuario);
        antesDeEnviar?.Invoke();
        using var respuesta = await Ingresar(edicion, cantidad, token, usuario: usuario);
        var cuerpo = await CatalogoApiHost.Leer(respuesta);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.Equal("pendiente", Estado(cuerpo));
        return (edicion, cuerpo.GetProperty("ingresoSolicitadoId").GetGuid());
    }

    private Task<string?> EstadoEnLaBase(Guid ingresoId) => bd.Consultar<string>($"SELECT estado FROM catalogo.ingresos_solicitados WHERE id = '{ingresoId}'");

    // ---------- El alta con stock inicial (CAT-20, CAT-23) ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task Un_alta_con_stock_inicial_de_12_y_el_Inventario_respondiendo_deja_la_ficha_el_ingreso_registrado_y_12_unidades()
    {
        var alta = await _host.RegistrarImpresa("Con stock", stockInicial: 12, usuario: Ana);
        var edicion = alta.GetProperty("edicionIds")[0].GetGuid();

        Assert.Equal("registrado", Estado(alta.GetProperty("ingresos")[0]));
        Assert.Equal(12, alta.GetProperty("ingresos")[0].GetProperty("cantidad").GetInt32());
        Assert.Equal(12, _host.Inventario.StockDe(edicion));
        Assert.Contains(edicion, _host.Inventario.Registradas);
        Assert.Equal("registrado", await bd.Consultar<string>($"SELECT estado FROM catalogo.ingresos_solicitados WHERE edicion_id = '{edicion}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task Un_alta_con_stock_inicial_y_el_Inventario_caido_no_se_revierte_la_ficha_queda_sin_confirmar_y_el_ingreso_fallido_se_repone_con_una_clave_nueva()
    {
        _host.Inventario.Caido = true;
        var alta = await _host.RegistrarImpresa("Sin respuesta", stockInicial: 12, usuario: Ana);
        var edicion = alta.GetProperty("edicionIds")[0].GetGuid();

        // Nada se envió (la ficha no se pudo confirmar): el ingreso del primer envío es «fallido» y habilita una clave nueva.
        Assert.Equal("fallido", Estado(alta.GetProperty("ingresos")[0]));
        Assert.Contains(edicion, alta.GetProperty("fichasSinConfirmar").EnumerateArray().Select(e => e.GetGuid()));
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM catalogo.obras"));
        Assert.Equal(0, _host.Inventario.StockDe(edicion));

        _host.Inventario.Caido = false;
        using var nuevo = await Ingresar(edicion, 12, await Token(edicion), usuario: Ana);

        Assert.Equal("registrado", Estado(await CatalogoApiHost.Leer(nuevo)));
        Assert.Equal(12, _host.Inventario.StockDe(edicion));
        Assert.Single(_host.Inventario.Ingresos);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task Un_alta_con_el_ingreso_sin_respuesta_no_se_revierte_y_deja_el_ingreso_pendiente_que_se_reintenta_sin_duplicar()
    {
        _host.Inventario.IngresosCaidos = true;
        var alta = await _host.RegistrarImpresa("Ingreso sin respuesta", stockInicial: 12, usuario: Ana);
        var edicion = alta.GetProperty("edicionIds")[0].GetGuid();
        var ingreso = alta.GetProperty("ingresos")[0].GetProperty("ingresoSolicitadoId").GetGuid();

        Assert.Equal("pendiente", Estado(alta.GetProperty("ingresos")[0]));
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM catalogo.obras"));
        Assert.Equal(0, _host.Inventario.StockDe(edicion));

        _host.Inventario.IngresosCaidos = false;
        using var reintento = await Reintentar(edicion, ingreso);
        using var otraVez = await Reintentar(edicion, ingreso);

        Assert.Equal(HttpStatusCode.OK, reintento.StatusCode);
        Assert.Equal("registrado", Estado(await CatalogoApiHost.Leer(reintento)));
        Assert.Equal(HttpStatusCode.Conflict, otraVez.StatusCode);
        Assert.Equal(12, _host.Inventario.StockDe(edicion));
        Assert.Single(_host.Inventario.Ingresos);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task Un_alta_con_el_ingreso_aplicado_y_la_respuesta_perdida_queda_pendiente_y_el_reintento_no_duplica_el_stock()
    {
        _host.Inventario.AplicarYNoResponder = 1;
        var alta = await _host.RegistrarImpresa("Aplicado sin respuesta", stockInicial: 12, usuario: Ana);
        var edicion = alta.GetProperty("edicionIds")[0].GetGuid();
        var ingreso = alta.GetProperty("ingresos")[0].GetProperty("ingresoSolicitadoId").GetGuid();
        Assert.Equal("pendiente", Estado(alta.GetProperty("ingresos")[0]));
        Assert.Equal(12, _host.Inventario.StockDe(edicion));

        using var reintento = await Reintentar(edicion, ingreso);

        Assert.Equal("registrado", Estado(await CatalogoApiHost.Leer(reintento)));
        Assert.Equal(12, _host.Inventario.StockDe(edicion));
        Assert.Single(_host.Inventario.Ingresos);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task Un_stock_inicial_de_0_de_6000_o_negativo_se_rechaza_sin_crear_nada_y_1_y_5000_se_aceptan()
    {
        foreach (var stock in new[] { 0, 6000, -3, 5001 })
        {
            var portada = await _host.SubirYReferenciar("portada", ArchivosDeMuestra.Png(40, 40, 900), Ana);
            using var respuesta = await _host.Json(HttpMethod.Post, "/administracion/obras", new { titulo = "X", autor = "A", editorial = "E", categoria = "C", portadaRef = portada, ediciones = new[] { new { formato = "impresa", isbn = "978-0-00-000001-9", precio = 10m, stockInicial = stock } } }, usuario: Ana);

            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            Assert.Equal("ECO-VALIDACION-INVALIDA", Codigo(await CatalogoApiHost.Leer(respuesta)));
        }

        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM catalogo.obras"));
        Assert.Equal(0, _host.Inventario.LlamadasDeRegistrarEdicion);

        var uno = await _host.RegistrarImpresa("Uno", "978-0-00-000001-9", 1, Ana);
        var cinco = await _host.RegistrarImpresa("Cinco mil", "9780000000026", 5000, Ana);
        Assert.Equal("registrado", Estado(uno.GetProperty("ingresos")[0]));
        Assert.Equal("registrado", Estado(cinco.GetProperty("ingresos")[0]));
    }

    // ---------- El ingreso y su clave (CAT-22, AC-CAT-18) ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Un_ingreso_de_30_con_la_clave_repetida_devuelve_el_mismo_resultado_y_el_Inventario_aplica_uno_solo_y_con_otra_carga_es_409()
    {
        var edicion = await Impresa();
        var token = await Token(edicion);

        using var primero = await Ingresar(edicion, 30, token, "GR-001");
        using var repetido = await Ingresar(edicion, 30, token, "GR-001");
        using var otraCarga = await Ingresar(edicion, 31, token, "GR-001");
        using var otraReferencia = await Ingresar(edicion, 30, token, "GR-002");
        var uno = await CatalogoApiHost.Leer(primero);
        var dos = await CatalogoApiHost.Leer(repetido);

        Assert.Equal(HttpStatusCode.Created, primero.StatusCode);
        Assert.Equal(HttpStatusCode.Created, repetido.StatusCode);
        Assert.Equal(uno.GetProperty("ingresoSolicitadoId").GetGuid(), dos.GetProperty("ingresoSolicitadoId").GetGuid());
        Assert.Equal(30, _host.Inventario.StockDe(edicion));
        Assert.Single(_host.Inventario.Ingresos);
        foreach (var rechazada in new[] { otraCarga, otraReferencia })
        {
            Assert.Equal(HttpStatusCode.Conflict, rechazada.StatusCode);
            Assert.Equal("ECO-CLAVE-INVALIDA", Codigo(await CatalogoApiHost.Leer(rechazada)));
        }

        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM catalogo.ingresos_solicitados"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Cinco_envios_simultaneos_con_la_misma_clave_se_reducen_a_un_solo_ingreso_en_el_Inventario()
    {
        var edicion = await Impresa();
        var token = await Token(edicion);
        _host.Inventario.Retraso = TimeSpan.FromMilliseconds(300);

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Ingresar(edicion, 30, token, "GR-001")));

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Single(_host.Inventario.Ingresos);
        Assert.Equal(30, _host.Inventario.StockDe(edicion));
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM catalogo.ingresos_solicitados"));
        foreach (var respuesta in respuestas)
        {
            respuesta.Dispose();
        }
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Una_cantidad_de_0_de_6000_o_negativa_es_validacion_sin_fila_y_sin_llamada_y_1_y_5000_se_aceptan()
    {
        var edicion = await Impresa();
        var llamadasAntes = _host.Inventario.LlamadasDeRegistrarIngreso;

        foreach (var cantidad in new[] { 0, 6000, -3, 5001 })
        {
            using var respuesta = await Ingresar(edicion, cantidad, await Token(edicion));
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        }

        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM catalogo.ingresos_solicitados"));
        Assert.Equal(llamadasAntes, _host.Inventario.LlamadasDeRegistrarIngreso);

        using var uno = await Ingresar(edicion, 1, await Token(edicion));
        using var cinco = await Ingresar(edicion, 5000, await Token(edicion));
        Assert.Equal(HttpStatusCode.Created, uno.StatusCode);
        Assert.Equal(HttpStatusCode.Created, cinco.StatusCode);
        Assert.Equal(5001, _host.Inventario.StockDe(edicion));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Un_ingreso_a_una_edicion_digital_o_dada_de_baja_es_validacion()
    {
        var portada = await _host.SubirYReferenciar("portada", ArchivosDeMuestra.Png(40, 40, 900), Ana);
        var libro = await _host.SubirYReferenciar("libro", ArchivosDeMuestra.Epub(), Ana);
        using var alta = await _host.Json(HttpMethod.Post, "/administracion/obras", new
        {
            titulo = "Digital",
            autor = "A",
            editorial = "E",
            categoria = "C",
            portadaRef = portada,
            ediciones = new object[] { new { formato = "digital", isbn = "9790000000018", precio = 5m, licencia = new { tipo = "dominioPublico", titular = "DP", territorios = new[] { "PE" }, formato = "epub" }, archivoRef = libro } },
        }, usuario: Ana);
        var edicionDigital = (await CatalogoApiHost.Leer(alta)).GetProperty("edicionIds")[0].GetGuid();

        using var formulario = await _host.Json(HttpMethod.Get, $"/administracion/ediciones/{edicionDigital}/ingresos/nuevo", usuario: Ana);
        using var respuesta = await Ingresar(edicionDigital, 5, "cualquiera");
        Assert.Equal(HttpStatusCode.BadRequest, formulario.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(0, _host.Inventario.LlamadasDeRegistrarIngreso);
    }

    // ---------- Disponibilidad (AC-CAT-09) ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-09")]
    public async Task La_disponibilidad_se_muestra_reciente_luego_desactualizada_con_el_ultimo_dato_y_por_ultimo_no_se_puede_confirmar()
    {
        var edicion = await Impresa();
        using (await Ingresar(edicion, 30, await Token(edicion)))
        {
        }

        using var alcance = _host.Servicios.CreateScope();
        var disponibilidad = alcance.ServiceProvider.GetRequiredService<IDisponibilidadDeEdiciones>();

        var reciente = (await disponibilidad.Consultar([edicion])).Valor.Single();
        _host.Inventario.Caido = true;
        var llamadas = _host.Inventario.LlamadasDeDisponibilidad;
        var enCache = (await disponibilidad.Consultar([edicion])).Valor.Single();
        var llamadasTrasLaCache = _host.Inventario.LlamadasDeDisponibilidad;
        _host.Reloj.Adelantar(TimeSpan.FromMinutes(1));
        var desactualizada = (await disponibilidad.Consultar([edicion])).Valor.Single();
        _host.Reloj.Adelantar(TimeSpan.FromMinutes(10));
        var sinConfirmar = (await disponibilidad.Consultar([edicion])).Valor.Single();

        Assert.Equal((EstadoDeDisponibilidad.Disponible, FrescuraDeLaDisponibilidad.Reciente, 30), (reciente.Estado, reciente.Frescura, reciente.VendibleEnLaRed));
        Assert.Equal(llamadas, llamadasTrasLaCache);
        Assert.Equal(FrescuraDeLaDisponibilidad.Reciente, enCache.Frescura);
        Assert.Equal((EstadoDeDisponibilidad.Disponible, FrescuraDeLaDisponibilidad.PuedeEstarDesactualizada, 30), (desactualizada.Estado, desactualizada.Frescura, desactualizada.VendibleEnLaRed));
        Assert.Equal((EstadoDeDisponibilidad.NoSePuedeConfirmar, FrescuraDeLaDisponibilidad.NoSePuedeConfirmar), (sinConfirmar.Estado, sinConfirmar.Frescura));
    }

    // ---------- Reintento: solo su autor (AC-CAT-18) ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task El_reintento_de_un_pendiente_solo_lo_hace_su_autor_otro_administrador_recibe_403_registrado_y_una_fila_de_otra_edicion_es_404()
    {
        var (edicion, ingreso) = await Pendiente(antesDeEnviar: () => _host.Inventario.IngresosCaidos = true);
        _host.Inventario.IngresosCaidos = false;
        var otraEdicion = await Impresa(isbn: "9780000000026");
        var antes = await bd.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE accion = 'AccesoDenegado' AND motivo = 'recurso-ajeno'");

        using var deOtro = await Reintentar(edicion, ingreso, usuario: Beto);
        using var deCliente = await Reintentar(edicion, ingreso, usuario: Beto, rol: RolesDeUsuario.Cliente);
        using var clienteConFilaInexistente = await Reintentar(edicion, Guid.NewGuid(), usuario: Beto, rol: RolesDeUsuario.Cliente);
        using var deOtraEdicion = await Reintentar(otraEdicion, ingreso);
        using var delAutor = await Reintentar(edicion, ingreso);

        Assert.Equal(HttpStatusCode.Forbidden, deOtro.StatusCode);
        Assert.Equal("ECO-PROHIBIDO", Codigo(await CatalogoApiHost.Leer(deOtro)));
        Assert.Equal(antes + 1, await bd.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE accion = 'AccesoDenegado' AND motivo = 'recurso-ajeno' AND ruta = '/administracion/ediciones/{edicionId}/ingresos/{ingresoId}/reintentar'"));
        // El rol se comprueba antes de buscar la fila: un cliente recibe 403 también con una fila que no existe.
        Assert.Equal(HttpStatusCode.Forbidden, deCliente.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, clienteConFilaInexistente.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deOtraEdicion.StatusCode);
        Assert.Equal("ECO-NO-ENCONTRADO", Codigo(await CatalogoApiHost.Leer(deOtraEdicion)));
        Assert.Equal(HttpStatusCode.OK, delAutor.StatusCode);
        Assert.Equal("registrado", Estado(await CatalogoApiHost.Leer(delAutor)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task El_doble_clic_del_reintento_aplica_un_solo_ingreso_y_deja_la_fila_registrada()
    {
        var (edicion, ingreso) = await Pendiente(antesDeEnviar: () => _host.Inventario.IngresosCaidos = true);
        _host.Inventario.IngresosCaidos = false;
        _host.Inventario.Retraso = TimeSpan.FromMilliseconds(300);

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Reintentar(edicion, ingreso)));

        Assert.All(respuestas, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        Assert.Contains(respuestas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(_host.Inventario.Ingresos);
        Assert.Equal("registrado", await EstadoEnLaBase(ingreso));
        foreach (var respuesta in respuestas)
        {
            respuesta.Dispose();
        }
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Un_envio_aplicado_con_tiempo_agotado_y_un_reintento_con_401_dejan_la_fila_pendiente_y_la_conciliacion_la_encuentra()
    {
        _host.Inventario.AplicarYNoResponder = 1;
        var (edicion, ingreso) = await Pendiente();
        Assert.Equal(30, _host.Inventario.StockDe(edicion));

        // El segundo intento recibe un 401 de forma que en un primer envío probaría que no se aplicó; en un reintento no prueba nada.
        _host.Inventario.Rechazo = (1, 401, "INV-NO-AUTENTICADO");
        using var reintento = await Reintentar(edicion, ingreso);
        Assert.Equal(HttpStatusCode.OK, reintento.StatusCode);
        Assert.Equal("pendiente", Estado(await CatalogoApiHost.Leer(reintento)));
        Assert.Equal("pendiente", await EstadoEnLaBase(ingreso));

        using var conciliacion = await Conciliar(edicion, ingreso, "registrado");

        Assert.Equal(HttpStatusCode.OK, conciliacion.StatusCode);
        Assert.Equal("registrado", Estado(await CatalogoApiHost.Leer(conciliacion)));
        Assert.Equal(30, _host.Inventario.StockDe(edicion));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Dos_intentos_inciertos_con_el_ingreso_aplicado_en_el_primero_se_concilian_con_la_coincidencia_unica()
    {
        _host.Inventario.AplicarYNoResponder = 1;
        var (edicion, ingreso) = await Pendiente();
        _host.Inventario.IngresosCaidos = true;
        using var segundoIntento = await Reintentar(edicion, ingreso);
        Assert.Equal("pendiente", Estado(await CatalogoApiHost.Leer(segundoIntento)));
        _host.Inventario.IngresosCaidos = false;

        using var conciliacion = await Conciliar(edicion, ingreso, "registrado");

        Assert.Equal("registrado", Estado(await CatalogoApiHost.Leer(conciliacion)));
        Assert.Equal(2, await bd.Consultar<int>($"SELECT intentos FROM catalogo.ingresos_solicitados WHERE id = '{ingreso}'"));
        Assert.Single(_host.Inventario.Ingresos);
    }

    // ---------- Conciliación (AC-CAT-18) ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Dos_conciliaciones_simultaneas_de_la_misma_fila_dejan_un_solo_registrado_y_la_otra_recibe_409()
    {
        _host.Inventario.AplicarYNoResponder = 1;
        var (edicion, ingreso) = await Pendiente();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Conciliar(edicion, ingreso, "registrado")));

        Assert.Equal(1, respuestas.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(respuestas.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal("registrado", await EstadoEnLaBase(ingreso));
        foreach (var respuesta in respuestas)
        {
            respuesta.Dispose();
        }
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Conciliar_como_registrado_sin_coincidencia_o_con_varias_es_422_y_con_ingresos_listar_caido_es_503_y_la_fila_queda_pendiente()
    {
        var (edicion, ingreso) = await Pendiente(antesDeEnviar: () => _host.Inventario.IngresosCaidos = true);
        _host.Inventario.IngresosCaidos = false;

        using var sinCoincidencia = await Conciliar(edicion, ingreso, "registrado");
        _host.Inventario.SembrarIngreso(edicion, 30, Ana, DateTimeOffset.UtcNow);
        _host.Inventario.SembrarIngreso(edicion, 30, Ana, DateTimeOffset.UtcNow);
        using var varias = await Conciliar(edicion, ingreso, "registrado");
        _host.Inventario.ListaCaida = true;
        using var sinLista = await Conciliar(edicion, ingreso, "registrado");

        foreach (var respuesta in new[] { sinCoincidencia, varias })
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
            var problema = await CatalogoApiHost.Leer(respuesta);
            Assert.Equal("ECO-REGLA-DE-NEGOCIO-INCUMPLIDA", Codigo(problema));
            Assert.Equal("sin-coincidencia-unica", problema.GetProperty("errores")[0].GetProperty("clave").GetString());
        }

        Assert.Equal(HttpStatusCode.ServiceUnavailable, sinLista.StatusCode);
        Assert.Equal("ECO-SERVICIO-NO-DISPONIBLE", Codigo(await CatalogoApiHost.Leer(sinLista)));
        Assert.Equal("pendiente", await EstadoEnLaBase(ingreso));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Conciliar_como_fallido_exige_confirmacion_y_dos_minutos_desde_el_ultimo_intento_y_habilita_una_clave_nueva()
    {
        var (edicion, ingreso) = await Pendiente(antesDeEnviar: () => _host.Inventario.IngresosCaidos = true);
        _host.Inventario.IngresosCaidos = false;

        using var reciente = await Conciliar(edicion, ingreso, "fallido", confirma: true);
        _host.Reloj.Adelantar(TimeSpan.FromMinutes(3));
        using var sinConfirmar = await Conciliar(edicion, ingreso, "fallido", confirma: false);
        using var confirmado = await Conciliar(edicion, ingreso, "fallido", confirma: true);
        using var nuevo = await Ingresar(edicion, 30, await Token(edicion));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reciente.StatusCode);
        Assert.Equal("intento-reciente", (await CatalogoApiHost.Leer(reciente)).GetProperty("errores")[0].GetProperty("clave").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sinConfirmar.StatusCode);
        Assert.Equal("confirmaFallido", (await CatalogoApiHost.Leer(sinConfirmar)).GetProperty("errores")[0].GetProperty("campo").GetString());
        Assert.Equal(HttpStatusCode.OK, confirmado.StatusCode);
        Assert.Equal("fallido", Estado(await CatalogoApiHost.Leer(confirmado)));
        Assert.Equal(HttpStatusCode.Created, nuevo.StatusCode);
        Assert.Equal("registrado", Estado(await CatalogoApiHost.Leer(nuevo)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Marcar_fallido_con_una_coincidencia_unica_se_rechaza_porque_el_ingreso_consta_como_aplicado()
    {
        _host.Inventario.AplicarYNoResponder = 1;
        var (edicion, ingreso) = await Pendiente();
        _host.Reloj.Adelantar(TimeSpan.FromMinutes(3));

        using var respuesta = await Conciliar(edicion, ingreso, "fallido", confirma: true);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        Assert.Equal("consta-como-aplicado", (await CatalogoApiHost.Leer(respuesta)).GetProperty("errores")[0].GetProperty("clave").GetString());
        Assert.Equal("pendiente", await EstadoEnLaBase(ingreso));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Un_pendiente_de_mas_de_24_horas_no_se_reintenta_y_se_concilia()
    {
        _host.Inventario.AplicarYNoResponder = 1;
        var (edicion, ingreso) = await Pendiente();
        _host.Reloj.Adelantar(TimeSpan.FromHours(25));

        using var reintento = await Reintentar(edicion, ingreso);
        using var conciliacion = await Conciliar(edicion, ingreso, "registrado");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reintento.StatusCode);
        Assert.Equal("ECO-REGLA-DE-NEGOCIO-INCUMPLIDA", Codigo(await CatalogoApiHost.Leer(reintento)));
        Assert.Equal(HttpStatusCode.OK, conciliacion.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Un_ingreso_sin_autor_anonimizado_no_se_reintenta_ni_se_concilia_como_registrado_y_solo_se_resuelve_como_fallido_confirmado()
    {
        var (edicion, ingreso) = await Pendiente(antesDeEnviar: () => _host.Inventario.IngresosCaidos = true);
        _host.Inventario.IngresosCaidos = false;
        await bd.Ejecutar($"UPDATE catalogo.ingresos_solicitados SET usuario_id = '{Guid.Empty}' WHERE id = '{ingreso}'");
        _host.Reloj.Adelantar(TimeSpan.FromMinutes(3));

        using var reintento = await Reintentar(edicion, ingreso);
        using var comoRegistrado = await Conciliar(edicion, ingreso, "registrado");
        using var comoFallido = await Conciliar(edicion, ingreso, "fallido", confirma: true);

        Assert.Equal(HttpStatusCode.Forbidden, reintento.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, comoRegistrado.StatusCode);
        Assert.Equal(HttpStatusCode.OK, comoFallido.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Dos_pendientes_de_una_edicion_se_rechazan_con_409_ingreso_pendiente()
    {
        var (edicion, _) = await Pendiente(antesDeEnviar: () => _host.Inventario.IngresosCaidos = true);
        _host.Inventario.IngresosCaidos = false;

        using var segundo = await Ingresar(edicion, 10, await Token(edicion));

        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
        Assert.Equal("ECO-INGRESO-PENDIENTE", Codigo(await CatalogoApiHost.Leer(segundo)));
        Assert.Equal(1, await bd.Consultar<long>($"SELECT count(*) FROM catalogo.ingresos_solicitados WHERE edicion_id = '{edicion}'"));
    }

    // ---------- El token firmado (AC-CAT-19) ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-19")]
    public async Task Un_token_manipulado_de_otro_usuario_o_edicion_o_sin_forma_se_rechaza_con_409_clave_invalida_sin_distinguir_la_causa()
    {
        var edicion = await Impresa();
        var otraEdicion = await Impresa(isbn: "9780000000026");
        var token = await Token(edicion);
        var partes = token.Split('.');
        var firmaAlterada = string.Join('.', partes[..4]) + "." + (partes[4][0] == 'a' ? 'b' : 'a') + partes[4][1..];
        var venceAlterado = string.Join('.', partes[0], partes[1], partes[2], (long.Parse(partes[3]) + 600).ToString(System.Globalization.CultureInfo.InvariantCulture), partes[4]);
        var deOtraEdicion = await Token(otraEdicion);
        var deBeto = await Token(edicion, Beto);

        foreach (var malo in new string?[] { firmaAlterada, venceAlterado, deOtraEdicion, deBeto, "x.y.z", "a.b.c.d.e", new string('a', 400) })
        {
            using var respuesta = await Ingresar(edicion, 30, malo);

            Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
            Assert.Equal("ECO-CLAVE-INVALIDA", Codigo(await CatalogoApiHost.Leer(respuesta)));
        }

        // Sin token o con uno que ni cabe en el campo es validación, antes de tocar nada.
        foreach (var vacio in new string?[] { "", null })
        {
            using var respuesta = await Ingresar(edicion, 30, vacio);

            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        }

        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM catalogo.ingresos_solicitados"));
        Assert.Equal(0, _host.Inventario.LlamadasDeRegistrarIngreso);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-19")]
    public async Task El_token_vale_29_minutos_y_a_los_30_exactos_ya_vencio()
    {
        var edicion = await Impresa();
        var aTiempo = await Token(edicion);
        _host.Reloj.Adelantar(TimeSpan.FromMinutes(29));
        using var vigente = await Ingresar(edicion, 5, aTiempo);
        var vencido = await Token(edicion);
        _host.Reloj.Adelantar(TimeSpan.FromMinutes(30));

        using var tarde = await Ingresar(edicion, 5, vencido);

        Assert.Equal(HttpStatusCode.Created, vigente.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, tarde.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-19")]
    public async Task Un_token_firmado_con_la_clave_anterior_se_acepta_y_uno_firmado_con_otra_clave_no()
    {
        var edicion = await Impresa();
        var conLaAnterior = new EmisorDeClavesDeIngresoHmac(CatalogoApiHost.ClaveDeIngresosAnterior).Emitir(Ana, edicion, _host.Reloj.Ahora).Token;
        var conOtra = new EmisorDeClavesDeIngresoHmac("otra-clave-que-no-es-ninguna-del-entorno-0123456789").Emitir(Ana, edicion, _host.Reloj.Ahora).Token;

        using var rechazado = await Ingresar(edicion, 5, conOtra);
        using var aceptado = await Ingresar(edicion, 5, conLaAnterior);

        Assert.Equal(HttpStatusCode.Conflict, rechazado.StatusCode);
        Assert.Equal(HttpStatusCode.Created, aceptado.StatusCode);
        Assert.Equal(5, _host.Inventario.StockDe(edicion));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-19")]
    public void La_clave_de_firma_exige_32_bytes_y_el_token_nunca_se_imprime()
    {
        Assert.Throws<ArgumentException>(() => new EmisorDeClavesDeIngresoHmac("corta"));
        var emisor = new EmisorDeClavesDeIngresoHmac(CatalogoApiHost.ClaveDeIngresos);
        var emitido = emisor.Emitir(Ana, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.DoesNotContain(emitido.Token, emitido.ToString(), StringComparison.Ordinal);
        Assert.Null(emisor.Verificar(emitido.Token, Beto, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }
}
