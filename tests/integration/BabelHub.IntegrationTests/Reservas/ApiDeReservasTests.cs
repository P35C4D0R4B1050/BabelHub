using System.Net;
using System.Text.Json;
using BabelHub.Inventario.Api.Reservas;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Inventario;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>
/// T26 y T27 con PostgreSQL real, el rol real de la aplicación, la cadena real de middleware de servicio y los endpoints de <c>Reservas</c>: cada operación con la credencial de su alcance (y rechazada con la de otro,
/// con la de un cliente o sin ella), el esquema estricto (un <c>clienteId</c> en el cuerpo se rechaza), la <c>Idempotency-Key</c>, los códigos <c>INV-…</c> y la respuesta única del límite con <c>Retry-After</c>.
/// </summary>
public sealed class ApiDeReservasTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private ApiDeReservasHost _api = null!;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _api = await ApiDeReservasHost.Crear(bd);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) => JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();

    private static string Clave() => Guid.NewGuid().ToString("N");

    private static string Reservar(Guid pedido, Guid linea, Guid edicion, int cantidad = 1, string sucursal = "lima", string? extra = null) =>
        $$"""{"pedidoId":"{{pedido}}","tipoDeEntrega":"recojo","codigoDeSucursalDeRecojo":"{{sucursal}}","regionDeEntrega":null,"lineas":[{"lineaId":"{{linea}}","edicionId":"{{edicion}}","cantidad":{{cantidad}}}]{{extra}}}""";

    private Task<HttpResponseMessage> Crear(Guid usuario, string json, string? clave = null) =>
        _api.Enviar(HttpMethod.Post, "/reservas", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, usuario: usuario, json: json, clave: clave ?? Clave());

    private Task<HttpResponseMessage> Servicio(string ruta, string? json = null, string? clave = null) =>
        _api.Enviar(HttpMethod.Post, ruta, ApiDeReservasHost.CredencialDeExpiracion, json: json, clave: clave);

    private async Task<(Guid Lima, Guid Libro)> Escenario(int fisico = 50)
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, fisico, 0);
        return (lima, libro);
    }

    private async Task<Guid> ReservaConfirmada(Guid usuario, Guid libro, int cantidad = 1)
    {
        var pedido = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await Crear(usuario, Reservar(pedido, Guid.NewGuid(), libro, cantidad))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Servicio($"/reservas/{pedido}/confirmar", clave: Clave())).StatusCode);
        return pedido;
    }

    // ---------- crear ----------

    [Fact]
    [Trait("Criterio", "AC-SEL-01")]
    public async Task Crear_ConLaCredencialYElToken_Devuelve201_ReservaPendiente_YElStockSube()
    {
        var (lima, libro) = await Escenario();
        var pedido = Guid.NewGuid();
        var linea = Guid.NewGuid();

        var respuesta = await Crear(Guid.NewGuid(), Reservar(pedido, linea, libro, 2));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var cuerpo = await Cuerpo(respuesta);
        Assert.Equal(pedido, cuerpo.GetProperty("pedidoId").GetGuid());
        Assert.Equal("pendiente", cuerpo.GetProperty("estado").GetString());
        Assert.NotNull(cuerpo.GetProperty("expiraEn").GetString());
        var l = cuerpo.GetProperty("lineas")[0];
        Assert.Equal((linea, 2, "lima", false), (l.GetProperty("lineaId").GetGuid(), l.GetProperty("cantidad").GetInt32(), l.GetProperty("codigoDeFuente").GetString(), l.GetProperty("requiereTraslado").GetBoolean()));
        Assert.Equal((50, 2), await bd.StockDe(lima, libro));
        Assert.False(respuesta.Headers.Contains("Retry-After"));
        Assert.True(respuesta.Headers.Contains("X-Trace-Id"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-09")]
    public async Task Crear_RepetidoConLaMismaClave_DevuelveLaMismaRespuesta_SinReservarOtraVez_YConOtraCarga_Es409()
    {
        var (lima, libro) = await Escenario();
        var usuario = Guid.NewGuid();
        var pedido = Guid.NewGuid();
        var linea = Guid.NewGuid();
        var clave = Clave();
        var json = Reservar(pedido, linea, libro, 3);

        var primera = await Crear(usuario, json, clave);
        var repetida = await Crear(usuario, json, clave);
        var otraCarga = await Crear(usuario, Reservar(pedido, linea, libro, 4), clave);

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(HttpStatusCode.Created, repetida.StatusCode);
        Assert.Equal((await Cuerpo(primera)).GetRawText(), (await Cuerpo(repetida)).GetRawText());
        Assert.Equal(HttpStatusCode.Conflict, otraCarga.StatusCode);
        Assert.Equal(CodigosDeErrorDelInventario.IdempotenciaEnConflicto, (await Cuerpo(otraCarga)).GetProperty("code").GetString());
        Assert.Equal((50, 3), await bd.StockDe(lima, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-01")]
    public async Task Crear_ConUnClienteIdEnElCuerpo_SeRechaza_ElClienteSaleDelToken()
    {
        var (_, libro) = await Escenario();
        var usuario = Guid.NewGuid();

        var respuesta = await Crear(usuario, Reservar(Guid.NewGuid(), Guid.NewGuid(), libro, extra: $$""","clienteId":"{{Guid.NewGuid()}}" """));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(CodigosDeErrorDelInventario.ValidacionInvalida, (await Cuerpo(respuesta)).GetProperty("code").GetString());
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas"));
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-01")]
    [InlineData(null)]
    [InlineData("corta")]
    [InlineData("con espacios y simbolos !!!!!!")]
    public async Task Crear_SinClaveDeIdempotenciaOConUnaMalFormada_Es400(string? clave)
    {
        var (_, libro) = await Escenario();

        var respuesta = await _api.Enviar(
            HttpMethod.Post, "/reservas", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, usuario: Guid.NewGuid(), json: Reservar(Guid.NewGuid(), Guid.NewGuid(), libro), clave: clave);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains(
            (await Cuerpo(respuesta)).GetProperty("errores").EnumerateArray(),
            e => e.GetProperty("campo").GetString() == "idempotencyKey");
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-40")]
    public async Task Crear_SinElAlcanceQueLaAutoriza_SeRechaza_AunConUnCuerpoMalFormado_ElAlcancePrimero()
    {
        var (lima, libro) = await Escenario();
        var json = Reservar(Guid.NewGuid(), Guid.NewGuid(), libro);

        var conOtraCredencial = await _api.Enviar(HttpMethod.Post, "/reservas", ApiDeReservasHost.CredencialDeExpiracion, RolesDeUsuario.Cliente, usuario: Guid.NewGuid(), json: json, clave: Clave());
        var sinToken = await _api.Enviar(HttpMethod.Post, "/reservas", ApiDeReservasHost.CredencialDeUsuario, json: json, clave: Clave());
        var sinCredencial = await _api.Enviar(HttpMethod.Post, "/reservas", null, RolesDeUsuario.Cliente, usuario: Guid.NewGuid(), json: json, clave: Clave());
        var personal = await _api.Enviar(HttpMethod.Post, "/reservas", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.EncargadoDeSucursal, lima, json: "{ esto no es json", clave: Clave());

        Assert.Equal(HttpStatusCode.Unauthorized, conOtraCredencial.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sinToken.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sinCredencial.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, personal.StatusCode);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas"));
        Assert.Contains(_api.Denegaciones.Registros, r => r.Motivo == "fuera-de-rol");
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-05")]
    public async Task Crear_SinStock_Es409_ConElDetalleDeLasLineasSinStock_YNoReservaNinguna()
    {
        var (lima, libro) = await Escenario(fisico: 2);
        var otro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, otro, 9, 0);
        var linea = Guid.NewGuid();
        var json = $$"""{"pedidoId":"{{Guid.NewGuid()}}","tipoDeEntrega":"recojo","codigoDeSucursalDeRecojo":"lima","regionDeEntrega":null,"lineas":[{"lineaId":"{{Guid.NewGuid()}}","edicionId":"{{otro}}","cantidad":1},{"lineaId":"{{linea}}","edicionId":"{{libro}}","cantidad":5}]}""";

        var respuesta = await Crear(Guid.NewGuid(), json);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        var cuerpo = await Cuerpo(respuesta);
        Assert.Equal(CodigosDeErrorDelInventario.StockInsuficiente, cuerpo.GetProperty("code").GetString());
        var sin = Assert.Single(cuerpo.GetProperty("lineas").EnumerateArray());
        Assert.Equal((linea, 5, 2), (sin.GetProperty("lineaId").GetGuid(), sin.GetProperty("solicitada").GetInt32(), sin.GetProperty("disponibleEnLaRed").GetInt32()));
        Assert.Equal((2, 0), await bd.StockDe(lima, libro));
        Assert.Equal((9, 0), await bd.StockDe(lima, otro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-12")]
    public async Task Crear_ConUnaEdicionDigital_Es422_ConElCodigoDeEdicionNoImpresa()
    {
        await Escenario();
        var digital = await bd.AgregarEdicion("digital");

        var respuesta = await Crear(Guid.NewGuid(), Reservar(Guid.NewGuid(), Guid.NewGuid(), digital));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        Assert.Equal(CodigosDeErrorDelInventario.EdicionNoImpresa, (await Cuerpo(respuesta)).GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-13")]
    public async Task ElLimite_RespondeUnSolo429ConRetryAfter_ParaLaPendienteYParaLaHora_SinDatosDeCuentas()
    {
        var (_, libro) = await Escenario(fisico: 500);
        var usuario = Guid.NewGuid();
        var primera = ReservaConfirmada(usuario, libro);
        var pedido = await primera;

        // Una pendiente por cliente (SEL-13): la segunda se rechaza hasta que la primera se confirme.
        Assert.Equal(HttpStatusCode.Created, (await Crear(usuario, Reservar(Guid.NewGuid(), Guid.NewGuid(), libro))).StatusCode);
        var porPendiente = await Crear(usuario, Reservar(Guid.NewGuid(), Guid.NewGuid(), libro));

        // Cinco por hora (SEL-14): se confirman las pendientes y la sexta se rechaza.
        for (var i = 0; i < 3; i++)
        {
            await bd.Ejecutar("UPDATE reservas.reserva_lineas SET estado = 'confirmada', confirmada_en = now() WHERE estado = 'pendiente'; UPDATE reservas.reservas SET fecha_expiracion = NULL WHERE fecha_expiracion IS NOT NULL");
            Assert.Equal(HttpStatusCode.Created, (await Crear(usuario, Reservar(Guid.NewGuid(), Guid.NewGuid(), libro))).StatusCode);
        }

        await bd.Ejecutar("UPDATE reservas.reserva_lineas SET estado = 'confirmada', confirmada_en = now() WHERE estado = 'pendiente'; UPDATE reservas.reservas SET fecha_expiracion = NULL WHERE fecha_expiracion IS NOT NULL");
        var porHora = await Crear(usuario, Reservar(Guid.NewGuid(), Guid.NewGuid(), libro));

        Assert.NotEqual(Guid.Empty, pedido);
        foreach (var (respuesta, motivo) in new[] { (porPendiente, "reservas-pendiente-por-cliente"), (porHora, "reservas-por-hora-por-cliente") })
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, respuesta.StatusCode);
            Assert.True(respuesta.Headers.TryGetValues("Retry-After", out var retry) && int.Parse(retry.Single()) >= 1);
            var cuerpo = await Cuerpo(respuesta);
            Assert.Equal(CodigosDeErrorDelInventario.LimiteExcedido, cuerpo.GetProperty("code").GetString());
            Assert.Equal(motivo, cuerpo.GetProperty("motivo").GetString());
            Assert.True(cuerpo.GetProperty("retryAfterSeconds").GetInt32() >= 1);
            Assert.NotNull(cuerpo.GetProperty("disponibleDesde").GetString());
        }

        // La forma es la misma para los dos motivos: solo cambian el motivo, la espera y el instante (SEL-15).
        var formas = new[] { porPendiente, porHora }.Select(async r => string.Join(",", (await Cuerpo(r)).EnumerateObject().Select(p => p.Name).Order())).ToList();
        Assert.Equal(await formas[0], await formas[1]);
        Assert.DoesNotContain(usuario.ToString(), await porPendiente.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    // ---------- consulta previa ----------

    [Fact]
    [Trait("Criterio", "AC-SEL-20")]
    public async Task ConsultaPrevia_SinToken_ConLaCredencialDeLectura_NoReservaNada_YConOtraSeRechaza()
    {
        var (lima, libro) = await Escenario();
        var json = $$"""{"tipoDeEntrega":"recojo","codigoDeSucursalDeRecojo":"lima","regionDeEntrega":null,"lineas":[{"lineaId":"{{Guid.NewGuid()}}","edicionId":"{{libro}}","cantidad":3}]}""";

        var respuesta = await _api.Enviar(HttpMethod.Post, "/reservas/consulta-previa", ApiDeReservasHost.CredencialDeDisponibilidad, json: json);
        var conOtra = await _api.Enviar(HttpMethod.Post, "/reservas/consulta-previa", ApiDeReservasHost.CredencialDeExpiracion, json: json);
        var malFormada = await _api.Enviar(HttpMethod.Post, "/reservas/consulta-previa", ApiDeReservasHost.CredencialDeDisponibilidad, json: """{"tipoDeEntrega":"mensajeria","lineas":[]}""");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var linea = (await Cuerpo(respuesta)).GetProperty("lineas")[0];
        Assert.Equal(("disponible", "lima"), (linea.GetProperty("resultado").GetString(), linea.GetProperty("codigoDeFuente").GetString()));
        Assert.Equal(HttpStatusCode.Unauthorized, conOtra.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malFormada.StatusCode);
        Assert.Equal((50, 0), await bd.StockDe(lima, libro));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas"));
    }

    // ---------- consultar ----------

    [Fact]
    [Trait("Criterio", "AC-SEL-20")]
    public async Task Consultar_LaDuenaLaVe_OtroClienteRecibe404Uniforme_ElPersonalRecibe403_YSinTokenSeRechaza()
    {
        var (lima, libro) = await Escenario();
        var duena = Guid.NewGuid();
        var pedido = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await Crear(duena, Reservar(pedido, Guid.NewGuid(), libro))).StatusCode);

        var propia = await _api.Enviar(HttpMethod.Get, $"/reservas/{pedido}", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, usuario: duena);
        var ajena = await _api.Enviar(HttpMethod.Get, $"/reservas/{pedido}", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, usuario: Guid.NewGuid());
        var inexistente = await _api.Enviar(HttpMethod.Get, $"/reservas/{Guid.NewGuid()}", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, usuario: Guid.NewGuid());
        var personal = await _api.Enviar(HttpMethod.Get, $"/reservas/{pedido}", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.EncargadoDeSucursal, lima);
        var sinToken = await _api.Enviar(HttpMethod.Get, $"/reservas/{pedido}", ApiDeReservasHost.CredencialDeUsuario);

        Assert.Equal(HttpStatusCode.OK, propia.StatusCode);
        Assert.Equal(pedido, (await Cuerpo(propia)).GetProperty("pedidoId").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, ajena.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal((await Cuerpo(ajena)).GetProperty("code").GetString(), (await Cuerpo(inexistente)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, personal.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sinToken.StatusCode);
    }

    // ---------- confirmar, extender y liberar (expiracion-de-reservas) ----------

    [Fact]
    [Trait("Criterio", "AC-SEL-06")]
    public async Task Confirmar_ConLaCredencialDeExpiracion_Devuelve200_RepetidaConLaMismaClaveNoCambiaNada_YConOtraCredencialSeRechaza()
    {
        var (lima, libro) = await Escenario();
        var usuario = Guid.NewGuid();
        var pedido = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await Crear(usuario, Reservar(pedido, Guid.NewGuid(), libro, 2))).StatusCode);
        var clave = Clave();

        var confirmada = await Servicio($"/reservas/{pedido}/confirmar", clave: clave);
        var repetida = await Servicio($"/reservas/{pedido}/confirmar", clave: clave);
        var conUsuario = await _api.Enviar(HttpMethod.Post, $"/reservas/{pedido}/confirmar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, usuario: usuario, clave: Clave());
        var sinClave = await Servicio($"/reservas/{pedido}/confirmar");

        Assert.Equal(HttpStatusCode.OK, confirmada.StatusCode);
        var cuerpo = await Cuerpo(confirmada);
        Assert.Equal("confirmada", cuerpo.GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.Null, cuerpo.GetProperty("expiraEn").ValueKind);
        Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
        Assert.Equal((50, 2), await bd.StockDe(lima, libro));
        Assert.Equal(HttpStatusCode.Unauthorized, conUsuario.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sinClave.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-22")]
    public async Task Extender_UnaVez_ConLaCredencialDeExpiracion_LaSegundaNoExtiende_YConOtraCredencialSeRechaza()
    {
        var (_, libro) = await Escenario();
        var usuario = Guid.NewGuid();
        var pedido = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await Crear(usuario, Reservar(pedido, Guid.NewGuid(), libro))).StatusCode);

        var primera = await Servicio($"/reservas/{pedido}/extender");
        var segunda = await Servicio($"/reservas/{pedido}/extender");
        var conUsuario = await _api.Enviar(HttpMethod.Post, $"/reservas/{pedido}/extender", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, usuario: usuario);

        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);
        Assert.True((await Cuerpo(primera)).GetProperty("extendida").GetBoolean());
        Assert.False((await Cuerpo(segunda)).GetProperty("extendida").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, conUsuario.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-07")]
    public async Task Liberar_PorMotivo_DevuelveElStockAlVendible_RepetirNoCambiaNada_YUnMotivoDesconocidoEs400()
    {
        var (lima, libro) = await Escenario();
        var pedido = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await Crear(Guid.NewGuid(), Reservar(pedido, Guid.NewGuid(), libro, 4))).StatusCode);

        var desconocido = await Servicio($"/reservas/{pedido}/liberar", """{"motivo":"porque-si","lineaIds":null}""");
        var liberada = await Servicio($"/reservas/{pedido}/liberar", """{"motivo":"checkout-cancelado","lineaIds":null}""");
        var repetida = await Servicio($"/reservas/{pedido}/liberar", """{"motivo":"checkout-cancelado","lineaIds":null}""");
        var conUsuario = await _api.Enviar(HttpMethod.Post, $"/reservas/{pedido}/liberar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, json: """{"motivo":"checkout-cancelado","lineaIds":null}""");

        Assert.Equal(HttpStatusCode.BadRequest, desconocido.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liberada.StatusCode);
        Assert.Equal("liberada", (await Cuerpo(liberada)).GetProperty("estado").GetString());
        Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
        Assert.Equal((50, 0), await bd.StockDe(lima, libro));
        Assert.Equal(HttpStatusCode.Unauthorized, conUsuario.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-29")]
    public async Task ConfirmarUnaReservaLiberada_Es409_ConElCodigoDeEstadoInvalido()
    {
        var (_, libro) = await Escenario();
        var pedido = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await Crear(Guid.NewGuid(), Reservar(pedido, Guid.NewGuid(), libro))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Servicio($"/reservas/{pedido}/liberar", """{"motivo":"checkout-cancelado","lineaIds":null}""")).StatusCode);

        var respuesta = await Servicio($"/reservas/{pedido}/confirmar", clave: Clave());

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal(CodigosDeErrorDelInventario.ReservaEstadoInvalido, (await Cuerpo(respuesta)).GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-32")]
    public async Task SiLaAuditoriaNoResponde_ConfirmarFalla503_YNoCambiaNada()
    {
        var (_, libro) = await Escenario();
        var pedido = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await Crear(Guid.NewGuid(), Reservar(pedido, Guid.NewGuid(), libro))).StatusCode);
        _api.Auditoria.Falla = true;

        var respuesta = await Servicio($"/reservas/{pedido}/confirmar", clave: Clave());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Equal(CodigosDeErrorDelInventario.ServicioNoDisponible, (await Cuerpo(respuesta)).GetProperty("code").GetString());
        Assert.Equal("pendiente", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}'"));
    }

    // ---------- cerrar (T27) ----------

    [Fact]
    [Trait("Criterio", "AC-SEL-21")]
    public async Task Cerrar_LaEntregaLaHaceElEncargadoDeLaSucursalDeRecojo_RepetirDevuelveElResultado_YConOtraClaveEsYaCerrada()
    {
        var (lima, libro) = await Escenario();
        var usuario = Guid.NewGuid();
        var pedido = await ReservaConfirmada(usuario, libro, 3);
        var linea = await bd.Consultar<Guid>($"SELECT linea_id FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}'");
        var json = $$"""{"motivo":"entrega","lineaIds":["{{linea}}"],"envioId":null}""";
        var clave = Clave();
        Task<HttpResponseMessage> Cerrar(string? k, Guid? sucursal = null, string rol = RolesDeUsuario.EncargadoDeSucursal, string? credencial = ApiDeReservasHost.CredencialDeUsuario) =>
            _api.Enviar(HttpMethod.Post, $"/reservas/{pedido}/cerrar", credencial, rol, sucursal ?? lima, json: json, clave: k);

        var primera = await Cerrar(clave);
        var repetida = await Cerrar(clave);
        var otraClave = await Cerrar(Clave());

        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);
        var resultado = (await Cuerpo(primera)).GetProperty("lineas")[0];
        Assert.Equal(("cerrada", linea), (resultado.GetProperty("resultado").GetString(), resultado.GetProperty("lineaId").GetGuid()));
        Assert.Equal((await Cuerpo(primera)).GetRawText(), (await Cuerpo(repetida)).GetRawText());
        Assert.Equal("ya-cerrada", (await Cuerpo(otraClave)).GetProperty("lineas")[0].GetProperty("resultado").GetString());
        Assert.Equal((47, 0), await bd.StockDe(lima, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-40")]
    public async Task Cerrar_ElAlcanceVaPrimero_OtraSucursalUnClienteOOtraCredencialSeRechazan_SinDescontarNada()
    {
        var (lima, libro) = await Escenario();
        var cusco = await bd.AgregarFuente("cusco");
        var usuario = Guid.NewGuid();
        var pedido = await ReservaConfirmada(usuario, libro, 2);
        var linea = await bd.Consultar<Guid>($"SELECT linea_id FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}'");
        var json = $$"""{"motivo":"entrega","lineaIds":["{{linea}}"],"envioId":null}""";

        var deOtraSucursal = await _api.Enviar(HttpMethod.Post, $"/reservas/{pedido}/cerrar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.EncargadoDeSucursal, cusco, json: json, clave: Clave());
        var cliente = await _api.Enviar(HttpMethod.Post, $"/reservas/{pedido}/cerrar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, usuario: usuario, json: json, clave: Clave());
        var conOtraCredencial = await _api.Enviar(HttpMethod.Post, $"/reservas/{pedido}/cerrar", ApiDeReservasHost.CredencialDeExpiracion, json: json, clave: Clave());
        // El rol se comprueba antes de mirar el cuerpo y, en la entrega, el alcance de sucursal antes de la forma (SEL-39): un cuerpo mal formado de otra sucursal recibe el mismo 403 con su evento.
        var malFormadoDeUnCliente = await _api.Enviar(HttpMethod.Post, $"/reservas/{pedido}/cerrar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, usuario: usuario, json: """{"motivo":"x"}""", clave: Clave());
        var malFormadoDeOtraSucursal = await _api.Enviar(HttpMethod.Post, $"/reservas/{pedido}/cerrar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.EncargadoDeSucursal, cusco, json: """{"motivo":"x"}""", clave: Clave());
        var sinClave = await _api.Enviar(HttpMethod.Post, $"/reservas/{pedido}/cerrar", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.EncargadoDeSucursal, lima, json: json);

        Assert.Equal(HttpStatusCode.Forbidden, deOtraSucursal.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cliente.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, conOtraCredencial.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, malFormadoDeUnCliente.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, malFormadoDeOtraSucursal.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sinClave.StatusCode);
        Assert.Equal((50, 2), await bd.StockDe(lima, libro));
        Assert.Contains(_api.Denegaciones.Registros, r => r.Motivo == "fuera-de-sucursal" || r.Motivo == "fuera-de-rol");
    }

    // ---------- pendientes y baja del titular (T27) ----------

    [Fact]
    [Trait("Criterio", "AC-SEL-35")]
    public async Task Pendientes_ConLaCredencialDeSuAlcance_CuentaLosPedidosVivos_YConOtraCredencialOUnClienteSeRechaza()
    {
        var (_, libro) = await Escenario();
        var conReserva = Guid.NewGuid();
        await ReservaConfirmada(conReserva, libro);
        var cuerpo = (string titular) => $$"""{"titularId":"{{titular}}"}""";

        var con = await _api.Enviar(HttpMethod.Post, "/titulares/consultas-de-pendientes", ApiDeReservasHost.CredencialDePendientes, json: cuerpo(conReserva.ToString()));
        var sin = await _api.Enviar(HttpMethod.Post, "/titulares/consultas-de-pendientes", ApiDeReservasHost.CredencialDePendientes, json: cuerpo(Guid.NewGuid().ToString()));
        var conOtra = await _api.Enviar(HttpMethod.Post, "/titulares/consultas-de-pendientes", ApiDeReservasHost.CredencialDeBaja, json: cuerpo(conReserva.ToString()));
        var cliente = await _api.Enviar(HttpMethod.Post, "/titulares/consultas-de-pendientes", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, usuario: conReserva, json: cuerpo(conReserva.ToString()));
        var sinCredencial = await _api.Enviar(HttpMethod.Post, "/titulares/consultas-de-pendientes", null, json: cuerpo(conReserva.ToString()));
        var conIdEnLaRuta = await _api.Enviar(HttpMethod.Get, $"/titulares/{conReserva}/pendientes", ApiDeReservasHost.CredencialDePendientes);
        var conCampoDeMas = await _api.Enviar(HttpMethod.Post, "/titulares/consultas-de-pendientes", ApiDeReservasHost.CredencialDePendientes, json: $$"""{"titularId":"{{conReserva}}","edicionId":"{{libro}}"}""");

        Assert.Equal(HttpStatusCode.OK, con.StatusCode);
        var respuesta = await Cuerpo(con);
        Assert.Equal(1, respuesta.GetProperty("reservasActivas").GetInt32());
        Assert.Equal(["reservasActivas"], respuesta.EnumerateObject().Select(p => p.Name));
        Assert.Equal(0, (await Cuerpo(sin)).GetProperty("reservasActivas").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, conOtra.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, cliente.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sinCredencial.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, conIdEnLaRuta.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, conCampoDeMas.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task Baja_ConLaCredencialDeSuAlcance_Responde204_TambienAlRepetirla_AnonimizaYElTitularYaNoReserva()
    {
        var (_, libro) = await Escenario();
        var titular = Guid.NewGuid();
        await ReservaConfirmada(titular, libro);
        var json = $$"""{"titularId":"{{titular}}"}""";

        var primera = await _api.Enviar(HttpMethod.Post, "/bajas-de-titular", ApiDeReservasHost.CredencialDeBaja, json: json);
        var repetida = await _api.Enviar(HttpMethod.Post, "/bajas-de-titular", ApiDeReservasHost.CredencialDeBaja, json: json);
        var reserva = await Crear(titular, Reservar(Guid.NewGuid(), Guid.NewGuid(), libro));

        Assert.Equal(HttpStatusCode.NoContent, primera.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repetida.StatusCode);
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE cliente_id = '{titular}'"));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reservas.bajas_recibidas"));
        Assert.Equal(1, _api.Auditoria.Registros.Count(r => r.Accion == "BajaDeTitularProcesada"));
        Assert.Equal(HttpStatusCode.Forbidden, reserva.StatusCode);
        Assert.Equal(CodigosDeErrorDelInventario.Prohibido, (await Cuerpo(reserva)).GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task Baja_SinLaCredencialDeSuAlcance_SeRechaza_SinEscribirNada_YUnCuerpoConCampoDeMasEs400()
    {
        var (_, libro) = await Escenario();
        var titular = Guid.NewGuid();
        await ReservaConfirmada(titular, libro);
        var json = $$"""{"titularId":"{{titular}}"}""";

        var conOtra = await _api.Enviar(HttpMethod.Post, "/bajas-de-titular", ApiDeReservasHost.CredencialDePendientes, json: json);
        var conExpiracion = await _api.Enviar(HttpMethod.Post, "/bajas-de-titular", ApiDeReservasHost.CredencialDeExpiracion, json: json);
        var cliente = await _api.Enviar(HttpMethod.Post, "/bajas-de-titular", ApiDeReservasHost.CredencialDeUsuario, RolesDeUsuario.Cliente, usuario: titular, json: json);
        var sinCredencial = await _api.Enviar(HttpMethod.Post, "/bajas-de-titular", null, json: json);
        var campoDeMas = await _api.Enviar(HttpMethod.Post, "/bajas-de-titular", ApiDeReservasHost.CredencialDeBaja, json: $$"""{"titularId":"{{titular}}","motivo":"x"}""");
        var vacio = await _api.Enviar(HttpMethod.Post, "/bajas-de-titular", ApiDeReservasHost.CredencialDeBaja, json: $$"""{"titularId":"{{Guid.Empty}}"}""");

        Assert.Equal(HttpStatusCode.Unauthorized, conOtra.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, conExpiracion.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, cliente.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sinCredencial.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, campoDeMas.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, vacio.StatusCode);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.bajas_recibidas"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reservas WHERE cliente_id = '{titular}'"));
    }

    // ---------- la matriz ----------

    [Fact]
    [Trait("Criterio", "AC-SEL-40")]
    public void CadaOperacionDeReservasEstaDeclaradaEnLaMatrizConSuMetodoSuRutaYSuAlcance()
    {
        var endpoints = _api.Endpoints.ToList();

        foreach (var nombre in MatrizDeReservas.Operaciones)
        {
            var contrato = OperacionesDelInventario.Todas.Single(o => o.Nombre == nombre);
            var plantilla = contrato.Ruta.Replace("{pedidoId}", "{pedidoId:guid}", StringComparison.Ordinal);
            var endpoint = Assert.Single(endpoints, e => string.Equals(e.RoutePattern.RawText, plantilla, StringComparison.Ordinal) && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(contrato.Metodo));
            Assert.Equal(nombre, endpoint.Metadata.GetMetadata<BabelHub.Inventario.Api.Plataforma.OperacionDeServicioAttribute>()!.Operacion.Valor);
        }

        var matriz = MatrizDeReservas.Declaraciones().ToDictionary(d => d.Operacion.Valor, d => d.Alcance);
        Assert.Equal(AlcanceDeServicio.ExpiracionDeReservas, matriz["inventario.reservas.confirmar"]);
        Assert.Equal(AlcanceDeServicio.ExpiracionDeReservas, matriz["inventario.reservas.extender"]);
        Assert.Equal(AlcanceDeServicio.ExpiracionDeReservas, matriz["inventario.reservas.liberar"]);
        Assert.Equal(AlcanceDeServicio.OperacionDeUsuario, matriz["inventario.reservas.cerrar"]);
        Assert.Equal(AlcanceDeServicio.LecturaDeDisponibilidad, matriz["inventario.reservas.consulta-previa"]);
        Assert.Equal(AlcanceDeServicio.ConsultaDePendientes, matriz["inventario.titulares.consultar-pendientes"]);
        Assert.Equal(AlcanceDeServicio.BajaDeTitular, matriz["inventario.titulares.baja"]);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-40")]
    public void NingunEndpointDeReservasQuedaSinOperacionDeclarada()
    {
        var rutasDeReservas = _api.Endpoints.Where(e => (e.RoutePattern.RawText ?? string.Empty).Contains("/reservas", StringComparison.Ordinal)
                                                       || (e.RoutePattern.RawText ?? string.Empty).Contains("/titulares", StringComparison.Ordinal)
                                                       || (e.RoutePattern.RawText ?? string.Empty).Contains("/bajas-de-titular", StringComparison.Ordinal)).ToList();

        Assert.Equal(MatrizDeReservas.Operaciones.Count, rutasDeReservas.Count);
        Assert.All(rutasDeReservas, e => Assert.NotNull(e.Metadata.GetMetadata<BabelHub.Inventario.Api.Plataforma.OperacionDeServicioAttribute>()));
    }
}
