using System.Net;
using System.Text.Json;
using BabelHub.Inventario.Api.Plataforma;
using BabelHub.Inventario.Api.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;
using BabelHub.IntegrationTests.Reservas;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Inventario;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Traslados;

/// <summary>
/// T24: la superficie HTTP de <c>Traslados</c> con PostgreSQL real, el rol real de la aplicación y la cadena real de credenciales. Cada operación con el token de su rol: el recorrido completo de un traslado, la tabla rol × operación con
/// 403 y un único evento (también en lectura y en orígenes posibles), las reglas de negocio con su código (409 y 422 sin evento de seguridad), el esquema estricto, el presupuesto de 30 por hora con la respuesta uniforme, la credencial de otro
/// alcance, los valores centinela ausentes de las respuestas, de la auditoría y de las denegaciones, y que ningún endpoint alcance los puertos de <c>Reservas</c> ni los trabajos de fondo.
/// </summary>
public sealed class ApiDeTrasladosTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private const string Centinela = "CENTINELA-NO-DEBE-SALIR-5521";

    private ApiDeReservasHost _api = null!;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _api = await ApiDeReservasHost.Crear(bd, conTraslados: true);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<(Guid Lima, Guid Cusco, Guid Ayacucho, Guid Libro)> Escenario(int fisicoEnCusco = 10)
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var cusco = await bd.AgregarFuente("cusco");
        var ayacucho = await bd.AgregarFuente("ayacucho");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(cusco, libro, fisicoEnCusco, 0);
        await bd.PonerStock(ayacucho, libro, 0, 0);
        await bd.PonerStock(lima, libro, 0, 0);
        await bd.DefinirRuta(cusco, ayacucho, 3);
        return (lima, cusco, ayacucho, libro);
    }

    private Task<HttpResponseMessage> Enviar(HttpMethod metodo, string ruta, string rol, Guid? sucursal = null, string? json = null, string? credencial = null) =>
        _api.Enviar(metodo, ruta, credencial ?? ApiDeReservasHost.CredencialDeUsuario, rol, sucursal, Guid.NewGuid(), json);

    private static string Solicitud(Guid edicion, int cantidad = 3, string origen = "cusco", string destino = "ayacucho", string? motivo = "reposición") =>
        $$"""{"edicionId":"{{edicion}}","cantidad":{{cantidad}},"codigoDeOrigen":"{{origen}}","codigoDeDestino":"{{destino}}","motivo":{{(motivo is null ? "null" : $"\"{motivo}\"")}}}""";

    private async Task<Guid> Solicitar(Guid edicion, Guid ayacucho, int cantidad = 3, string? motivo = "reposición")
    {
        var r = await Enviar(HttpMethod.Post, "/traslados", RolesDeUsuario.EncargadoDeSucursal, ayacucho, Solicitud(edicion, cantidad, motivo: motivo));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await Cuerpo(r)).GetProperty("trasladoId").GetGuid();
    }

    // ---------- Recorrido completo ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-01")]
    public async Task ElRecorridoCompletoDeUnTraslado_PorHttp_ConLosDtoDeEco0()
    {
        var (lima, cusco, ayacucho, libro) = await Escenario();

        var origenes = await Enviar(HttpMethod.Get, $"/ediciones/{libro}/origenes-posibles?codigoDeDestino=ayacucho", RolesDeUsuario.EncargadoDeSucursal, ayacucho);
        var solicitada = await Enviar(HttpMethod.Post, "/traslados", RolesDeUsuario.EncargadoDeSucursal, ayacucho, Solicitud(libro));
        var id = (await Cuerpo(solicitada)).GetProperty("trasladoId").GetGuid();
        var aprobada = await Enviar(HttpMethod.Post, $"/traslados/{id}/aprobar", RolesDeUsuario.EncargadoDeSucursal, cusco);
        var despachada = await Enviar(HttpMethod.Post, $"/traslados/{id}/despachar", RolesDeUsuario.EncargadoDeSucursal, cusco);
        var guia = await Enviar(HttpMethod.Put, $"/traslados/{id}/guia", RolesDeUsuario.PersonalDeLogistica, json: "{\"guia\":\"CA-771204\"}");
        var recibida = await Enviar(HttpMethod.Post, $"/traslados/{id}/recibir", RolesDeUsuario.EncargadoDeSucursal, ayacucho);
        var detalle = await Enviar(HttpMethod.Get, $"/traslados/{id}", RolesDeUsuario.PersonalDeLogistica);
        var lista = await Enviar(HttpMethod.Get, "/traslados?estado=recibido&codigoDeFuente=ayacucho", RolesDeUsuario.EncargadoDeSucursal, ayacucho);
        var delPedido = await Enviar(HttpMethod.Get, $"/pedidos/{Guid.NewGuid()}/traslados", RolesDeUsuario.AdministradorCentral);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK], [origenes.StatusCode, solicitada.StatusCode, aprobada.StatusCode, despachada.StatusCode, guia.StatusCode, recibida.StatusCode, detalle.StatusCode, lista.StatusCode, delPedido.StatusCode]);
        var disponibles = (await Cuerpo(origenes)).GetProperty("origenes").EnumerateArray().Select(o => o.GetProperty("codigo").GetString()).ToList();
        Assert.Equal(["cusco"], disponibles);
        Assert.All((await Cuerpo(origenes)).GetProperty("origenes").EnumerateArray(), o => Assert.Equal(2, o.EnumerateObject().Count()));
        var dto = JsonSerializer.Deserialize<TrasladoDto>(await detalle.Content.ReadAsStringAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })!;
        Assert.Equal((id, libro, 3, "cusco", "ayacucho", "recibido", "CA-771204", (Guid?)null), (dto.TrasladoId, dto.EdicionId, dto.Cantidad, dto.CodigoDeOrigen, dto.CodigoDeDestino, dto.Estado, dto.Guia, dto.PedidoId));
        Assert.Equal(1, (await Cuerpo(lista)).GetProperty("total").GetInt32());
        Assert.Empty((await Cuerpo(delPedido)).GetProperty("traslados").EnumerateArray());
        Assert.Equal((7, 3), ((await bd.StockDe(cusco, libro))!.Value.Fisico, (await bd.StockDe(ayacucho, libro))!.Value.Fisico));
        _ = lima;
    }

    // ---------- La tabla rol × operación ----------

    private sealed record Operacion(string Nombre, HttpMethod Metodo, Func<Guid, Guid, string> Ruta, Func<Guid, string?> Cuerpo, bool LogisticaPuede = false, bool EncargadoAjenoPuede = false);

    private static readonly Operacion[] Operaciones =
    [
        new("solicitar", HttpMethod.Post, (t, e) => "/traslados", e => Solicitud(e)),
        new("origenes-posibles", HttpMethod.Get, (t, e) => $"/ediciones/{e}/origenes-posibles?codigoDeDestino=ayacucho", _ => null),
        new("aprobar", HttpMethod.Post, (t, e) => $"/traslados/{t}/aprobar", _ => null),
        new("rechazar", HttpMethod.Post, (t, e) => $"/traslados/{t}/rechazar", _ => "{\"motivo\":\"sin stock\"}"),
        new("cancelar", HttpMethod.Post, (t, e) => $"/traslados/{t}/cancelar", _ => "{\"motivo\":\"error\"}"),
        new("despachar", HttpMethod.Post, (t, e) => $"/traslados/{t}/despachar", _ => null),
        new("recibir", HttpMethod.Post, (t, e) => $"/traslados/{t}/recibir", _ => null),
        new("guia", HttpMethod.Put, (t, e) => $"/traslados/{t}/guia", _ => "{\"guia\":\"CA-771204\"}", LogisticaPuede: true),
        new("detalle", HttpMethod.Get, (t, e) => $"/traslados/{t}", _ => null, LogisticaPuede: true),
        new("lista", HttpMethod.Get, (t, e) => "/traslados?codigoDeFuente=cusco", _ => null, LogisticaPuede: true),
        new("de-un-pedido", HttpMethod.Get, (t, e) => $"/pedidos/{Guid.NewGuid()}/traslados", _ => null, LogisticaPuede: true, EncargadoAjenoPuede: true),
    ];

    [Theory]
    [Trait("Criterio", "AC-TRA-03")]
    [InlineData("logistica")]
    [InlineData("cliente")]
    [InlineData("encargado-ajeno")]
    public async Task CadaOperacionNegadaDevuelveUn403ConUnUnicoEvento_ElTrasladoNoCambia(string quien)
    {
        var (lima, cusco, ayacucho, libro) = await Escenario();
        var id = await Solicitar(libro, ayacucho);
        var versionAntes = await bd.Consultar<long>($"SELECT version FROM traslados.traslados WHERE id = '{id}'");

        foreach (var operacion in Operaciones)
        {
            if ((quien == "logistica" && operacion.LogisticaPuede) || (quien == "encargado-ajeno" && operacion.EncargadoAjenoPuede))
            {
                continue;
            }

            var eventosAntes = _api.Denegaciones.Registros.Count;
            var respuesta = quien switch
            {
                "logistica" => await Enviar(operacion.Metodo, operacion.Ruta(id, libro), RolesDeUsuario.PersonalDeLogistica, json: operacion.Cuerpo(libro)),
                "cliente" => await Enviar(operacion.Metodo, operacion.Ruta(id, libro), RolesDeUsuario.Cliente, json: operacion.Cuerpo(libro)),
                _ => await Enviar(operacion.Metodo, operacion.Ruta(id, libro), RolesDeUsuario.EncargadoDeSucursal, lima, operacion.Cuerpo(libro)),
            };

            Assert.True(respuesta.StatusCode == HttpStatusCode.Forbidden, $"{operacion.Nombre} con {quien}: {(int)respuesta.StatusCode}");
            Assert.Equal("INV-PROHIBIDO", (await Cuerpo(respuesta)).GetProperty("code").GetString());
            Assert.Equal(eventosAntes + 1, _api.Denegaciones.Registros.Count);
        }

        Assert.Equal(versionAntes, await bd.Consultar<long>($"SELECT version FROM traslados.traslados WHERE id = '{id}'"));
        _ = cusco;
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task SinTokenDeUsuarioOConUnaCredencialDeOtroAlcanceNoSeToca_ConUnEventoDeSeguridad()
    {
        var (_, _, ayacucho, libro) = await Escenario();

        var sinUsuario = await _api.Enviar(HttpMethod.Get, "/traslados", ApiDeReservasHost.CredencialDeUsuario);
        var otroAlcance = await _api.Enviar(HttpMethod.Post, "/traslados", ApiDeReservasHost.CredencialDeDisponibilidad, RolesDeUsuario.EncargadoDeSucursal, ayacucho, Guid.NewGuid(), Solicitud(libro));
        var sinCredencial = await _api.Enviar(HttpMethod.Get, "/traslados", null, RolesDeUsuario.AdministradorCentral);

        Assert.All(new[] { sinUsuario, otroAlcance, sinCredencial }, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM traslados.traslados"));
        Assert.NotEmpty(_api.Eventos.Eventos);
    }

    // ---------- Reglas de negocio con su código ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-14")]
    public async Task RechazarOCancelarUnVinculadoEsUn422ConSuCodigo_SinEventoDeSeguridad()
    {
        var (_, cusco, ayacucho, libro) = await Escenario();
        var pedido = Guid.NewGuid();
        var vinculado = Guid.NewGuid();
        await bd.Ejecutar($"INSERT INTO traslados.traslados (id, edicion_id, origen_id, destino_id, cantidad, estado, pedido_id, fecha_estimada, solicitado_en) VALUES ('{vinculado}', '{libro}', '{cusco}', '{ayacucho}', 1, 'solicitado', '{pedido}', '2026-11-20', now())");

        var rechazo = await Enviar(HttpMethod.Post, $"/traslados/{vinculado}/rechazar", RolesDeUsuario.EncargadoDeSucursal, cusco, "{\"motivo\":\"sin stock\"}");
        var cancelacion = await Enviar(HttpMethod.Post, $"/traslados/{vinculado}/cancelar", RolesDeUsuario.AdministradorCentral, json: "{\"motivo\":\"error\"}");
        var aprobacion = await Enviar(HttpMethod.Post, $"/traslados/{vinculado}/aprobar", RolesDeUsuario.EncargadoDeSucursal, cusco);

        Assert.All(new[] { rechazo, cancelacion }, r => Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode));
        Assert.Equal("INV-TRASLADO-VINCULADO-NO-SE-RECHAZA", (await Cuerpo(rechazo)).GetProperty("code").GetString());
        Assert.Contains("anule el pedido", (await Cuerpo(rechazo)).GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, aprobacion.StatusCode);
        Assert.Empty(_api.Denegaciones.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-02")]
    public async Task UnaTransicionInvalidaEsUn409ConSuCodigo_SinEventoDeSeguridad()
    {
        var (_, cusco, ayacucho, libro) = await Escenario();
        var id = await Solicitar(libro, ayacucho);

        var despachar = await Enviar(HttpMethod.Post, $"/traslados/{id}/despachar", RolesDeUsuario.EncargadoDeSucursal, cusco);
        var recibir = await Enviar(HttpMethod.Post, $"/traslados/{id}/recibir", RolesDeUsuario.EncargadoDeSucursal, ayacucho);

        Assert.All(new[] { despachar, recibir }, r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal("INV-TRASLADO-ESTADO-INVALIDO", (await Cuerpo(despachar)).GetProperty("code").GetString());
        Assert.Empty(_api.Denegaciones.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-05")]
    public async Task ElDespachoSinStockVendibleEsUn409ConSuCodigo_ElTrasladoSigueAprobado()
    {
        var (_, cusco, ayacucho, libro) = await Escenario(fisicoEnCusco: 3);
        var id = await Solicitar(libro, ayacucho, 3);
        await Enviar(HttpMethod.Post, $"/traslados/{id}/aprobar", RolesDeUsuario.EncargadoDeSucursal, cusco);
        await bd.PonerStock(cusco, libro, 3, 3);

        var despacho = await Enviar(HttpMethod.Post, $"/traslados/{id}/despachar", RolesDeUsuario.EncargadoDeSucursal, cusco);

        Assert.Equal(HttpStatusCode.Conflict, despacho.StatusCode);
        var cuerpo = await Cuerpo(despacho);
        Assert.Equal("INV-STOCK-INSUFICIENTE", cuerpo.GetProperty("code").GetString());
        Assert.DoesNotContain("fisico", cuerpo.ToString(), StringComparison.Ordinal);
        Assert.Equal("aprobado", await bd.Consultar<string>($"SELECT estado FROM traslados.traslados WHERE id = '{id}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-19")]
    public async Task LasCincoSolicitudesInvalidasSeRechazanConSuCodigo()
    {
        var (_, _, ayacucho, libro) = await Escenario();
        var digital = await bd.AgregarEdicion("digital");

        async Task<HttpResponseMessage> Pedir(string json) => await Enviar(HttpMethod.Post, "/traslados", RolesDeUsuario.EncargadoDeSucursal, ayacucho, json);
        var mismo = await Pedir(Solicitud(libro, 1, origen: "ayacucho"));
        var cero = await Pedir(Solicitud(libro, 0));
        var digitalR = await Pedir(Solicitud(digital, 1));
        var mayor = await Pedir(Solicitud(libro, 99));
        var haciaOtra = await Pedir(Solicitud(libro, 1, origen: "lima", destino: "cusco"));

        Assert.Equal(
            [(HttpStatusCode.BadRequest, "INV-VALIDACION-INVALIDA"), (HttpStatusCode.BadRequest, "INV-VALIDACION-INVALIDA"), (HttpStatusCode.UnprocessableEntity, "INV-EDICION-NO-IMPRESA"), (HttpStatusCode.Conflict, "INV-STOCK-INSUFICIENTE"), (HttpStatusCode.Forbidden, "INV-PROHIBIDO")],
            [.. new[] { mismo, cero, digitalR, mayor, haciaOtra }.Select(r => (r.StatusCode, Cuerpo(r).GetAwaiter().GetResult().GetProperty("code").GetString()!))]);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM traslados.traslados"));
        Assert.Single(_api.Denegaciones.Registros);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-19")]
    [InlineData("{\"edicionId\":\"00000000-0000-0000-0000-000000000001\",\"cantidad\":1,\"codigoDeOrigen\":\"cusco\",\"codigoDeDestino\":\"ayacucho\",\"motivo\":\"x\",\"actor\":\"yo\"}")]
    [InlineData("{\"cantidad\":1}")]
    [InlineData("no es json")]
    [InlineData("")]
    public async Task ElEsquemaEstrictoRechazaCamposDeMasFaltantesOCuerposInvalidos(string json)
    {
        var (_, _, ayacucho, _) = await Escenario();

        var respuesta = await Enviar(HttpMethod.Post, "/traslados", RolesDeUsuario.EncargadoDeSucursal, ayacucho, json);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("INV-VALIDACION-INVALIDA", (await Cuerpo(respuesta)).GetProperty("code").GetString());
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM traslados.traslados"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-19")]
    public async Task LaListaValidaSusParametros_ConElAlcancePrimero()
    {
        var (lima, _, ayacucho, _) = await Escenario();

        var estado = await Enviar(HttpMethod.Get, "/traslados?estado=perdido", RolesDeUsuario.AdministradorCentral);
        var tamano = await Enviar(HttpMethod.Get, "/traslados?tamano=500", RolesDeUsuario.AdministradorCentral);
        var vinculado = await Enviar(HttpMethod.Get, "/traslados?vinculado=quizas", RolesDeUsuario.AdministradorCentral);
        var demora = await Enviar(HttpMethod.Get, "/traslados?vinculado=true&demoraMinimaHoras=48", RolesDeUsuario.AdministradorCentral);
        var clienteConParametroInvalido = await Enviar(HttpMethod.Get, "/traslados?estado=perdido", RolesDeUsuario.Cliente);

        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.OK, HttpStatusCode.Forbidden], [estado.StatusCode, tamano.StatusCode, vinculado.StatusCode, demora.StatusCode, clienteConParametroInvalido.StatusCode]);
        _ = (lima, ayacucho);
    }

    // ---------- Presupuesto y textos libres ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task ElIntento31DeTrasladosDelPersonalEsUn429UniformeConRetryAfter_OtroEncargadoNoSeAfecta()
    {
        var (_, _, ayacucho, libro) = await Escenario();
        var usuario = Guid.NewGuid();

        HttpResponseMessage Intento() => _api.Enviar(HttpMethod.Post, "/traslados", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.EncargadoDeSucursal, ayacucho, usuario, Solicitud(libro, 1)).GetAwaiter().GetResult();
        for (var i = 0; i < 30; i++)
        {
            Assert.Equal(HttpStatusCode.Created, Intento().StatusCode);
        }

        var excedido = Intento();
        var otro = await Enviar(HttpMethod.Post, "/traslados", RolesDeUsuario.EncargadoDeSucursal, ayacucho, Solicitud(libro, 1));

        Assert.Equal(HttpStatusCode.TooManyRequests, excedido.StatusCode);
        Assert.True(excedido.Headers.RetryAfter is not null);
        var cuerpo = await Cuerpo(excedido);
        Assert.Equal(("INV-LIMITE-EXCEDIDO", "traslados-del-personal"), (cuerpo.GetProperty("code").GetString(), cuerpo.GetProperty("motivo").GetString()));
        Assert.Equal(HttpStatusCode.Created, otro.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task LosTextosLibresNoSalenEnLasRespuestasDeError_NiEnLaAuditoriaNiEnLasDenegaciones_LaGuiaSoloEnLaAuditoria()
    {
        var (lima, cusco, ayacucho, libro) = await Escenario();
        var id = await Solicitar(libro, ayacucho, 2, Centinela);
        await Enviar(HttpMethod.Post, $"/traslados/{id}/aprobar", RolesDeUsuario.EncargadoDeSucursal, cusco);

        var errores = new[]
        {
            await Enviar(HttpMethod.Post, $"/traslados/{id}/rechazar", RolesDeUsuario.EncargadoDeSucursal, cusco, $"{{\"motivo\":\"{Centinela}\"}}"),
            await Enviar(HttpMethod.Post, $"/traslados/{id}/cancelar", RolesDeUsuario.EncargadoDeSucursal, lima, $"{{\"motivo\":\"{Centinela}\"}}"),
            await Enviar(HttpMethod.Put, $"/traslados/{id}/guia", RolesDeUsuario.EncargadoDeSucursal, cusco, $"{{\"guia\":\"{Centinela}\"}}"),
            await Enviar(HttpMethod.Put, $"/traslados/{id}/guia", RolesDeUsuario.PersonalDeLogistica, json: $"{{\"guia\":\"{Centinela}{Centinela}\"}}"),
        };

        foreach (var r in errores)
        {
            Assert.DoesNotContain("CENTINELA", await r.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        Assert.DoesNotContain(_api.Auditoria.Registros, r => (r.Detalle.Texto ?? string.Empty).Contains("CENTINELA", StringComparison.Ordinal) || (r.ToString() ?? string.Empty).Contains("CENTINELA", StringComparison.Ordinal));
        Assert.DoesNotContain(_api.Denegaciones.Registros, r => (r.Detalle.Texto ?? string.Empty).Contains("CENTINELA", StringComparison.Ordinal) || (r.ToString() ?? string.Empty).Contains("CENTINELA", StringComparison.Ordinal));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes WHERE carga::text LIKE '%CENTINELA%'"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public async Task ConLaAuditoriaCaida_LaOperacionResponde503YElCambioNoSeAplica()
    {
        var (_, cusco, ayacucho, libro) = await Escenario();
        var id = await Solicitar(libro, ayacucho);
        _api.Auditoria.Falla = true;

        var respuesta = await Enviar(HttpMethod.Post, $"/traslados/{id}/aprobar", RolesDeUsuario.EncargadoDeSucursal, cusco);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Equal("solicitado", await bd.Consultar<string>($"SELECT estado FROM traslados.traslados WHERE id = '{id}'"));
    }

    // ---------- Superficie ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-15")]
    public void NingunEndpointAlcanzaLosPuertosDeReservasNiLosTrabajosDeFondo()
    {
        var rutas = _api.Endpoints.Where(e => e.Metadata.GetMetadata<OperacionDeServicioAttribute>()?.Operacion.Valor.StartsWith("inventario.traslados.", StringComparison.Ordinal) == true).ToList();

        Assert.Equal(MatrizDeTraslados.Operaciones.Order().ToArray(), rutas.Select(e => e.Metadata.GetMetadata<OperacionDeServicioAttribute>()!.Operacion.Valor).Order().ToArray());
        var textos = _api.Endpoints.Select(e => (e.RoutePattern.RawText ?? string.Empty).ToLowerInvariant()).ToList();
        Assert.DoesNotContain(textos, t => t.Contains("mensajes", StringComparison.Ordinal) || t.Contains("generar", StringComparison.Ordinal) || t.Contains("depurar", StringComparison.Ordinal) || t.Contains("entrega", StringComparison.Ordinal));
        Assert.DoesNotContain(_api.Servicios.GetServices<Microsoft.Extensions.Hosting.IHostedService>(), s => s.GetType().FullName!.Contains("Traslados", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-15")]
    public void CadaOperacionDeTrasladosDeEco0TieneSuEndpoint_ConElAlcanceOperacionDeUsuario()
    {
        var deEco0 = OperacionesDelInventario.Todas.Where(o => o.Nombre.StartsWith("inventario.traslados.", StringComparison.Ordinal)).Select(o => o.Nombre).ToList();

        Assert.All(deEco0, nombre => Assert.Contains(nombre, MatrizDeTraslados.Operaciones));
        Assert.All(MatrizDeTraslados.Declaraciones(), d => Assert.Equal(BabelHub.Inventario.Application.Plataforma.AlcanceDeServicio.OperacionDeUsuario, d.Alcance));
        Assert.Equal(2, MatrizDeTraslados.Operaciones.Except(deEco0).Count());
    }
}
