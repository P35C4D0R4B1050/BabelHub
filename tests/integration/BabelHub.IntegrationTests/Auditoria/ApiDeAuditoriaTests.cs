using System.Net;
using System.Text;
using System.Text.Json;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;
using InventarioAuditoria = BabelHub.Inventario.Application.Auditoria;

namespace BabelHub.IntegrationTests.Auditoria;

/// <summary>
/// La Api de la auditoría de los dos servicios y su recorrido de punta a punta (T28 y T30; AC-AUD-03, AC-AUD-04, AC-AUD-07, AC-AUD-16, AC-AUD-21, AC-AUD-24, AC-AUD-27, AC-AUD-30; AUD-05, AUD-08, AUD-18, AUD-23, AUD-27, AUD-38):
/// el E-commerce y el Inventario reales en proceso (TestServer) sobre PostgreSQL con los roles reales, el cliente HTTP real entre ellos con la cadena de <c>Plataforma</c> (credencial de servicio por alcance y token del usuario), y
/// la composición del módulo (con validación de ciclos de vida). El usuario sale de cabeceras o de un token de prueba solo aquí (D10); una cabecera de actor nunca vale.
/// </summary>
public sealed class ApiDeAuditoriaTests(PostgresDeAuditoriaFixture bdEco, AuditoriaInventario.PostgresDeAuditoriaFixture bdInv) : IClassFixture<PostgresDeAuditoriaFixture>, IClassFixture<AuditoriaInventario.PostgresDeAuditoriaFixture>, IAsyncLifetime
{
    private const string RutaEco = RutasDeServicio.Ecommerce;
    private const string RutaInv = RutasDeServicio.Inventario;
    private const string Admin = "administrador-central";
    private const string Encargado = "encargado-de-sucursal";

    private InventarioDeAuditoriaHost _inv = null!;
    private EcommerceDeAuditoriaHost _eco = null!;

    public async Task InitializeAsync()
    {
        _inv = await InventarioDeAuditoriaHost.Crear(bdInv);
        _eco = await EcommerceDeAuditoriaHost.Crear(bdEco, _inv);
    }

    public async Task DisposeAsync()
    {
        await _eco.DisposeAsync();
        await _inv.DisposeAsync();
    }

    private static string TokenDeAdmin() => TokensDePrueba.Crear(Admin, Guid.NewGuid());

    private static async Task<JsonDocument> Json(HttpResponseMessage respuesta) => JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());

    private Task<long> Denegaciones(PostgresDeAuditoriaFixture bd) => bd.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE accion = 'AccesoDenegado'");

    private Task<long> Denegaciones(AuditoriaInventario.PostgresDeAuditoriaFixture bd) => bd.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE accion = 'AccesoDenegado'");

    private async Task<Guid> AjusteEnElInventario()
    {
        var respuesta = await _inv.Enviar(HttpMethod.Post, "/servicio/prueba/ajuste", InventarioDeAuditoriaHost.CredencialDeConsulta);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var cuerpo = await Json(respuesta);
        return cuerpo.RootElement.GetProperty("ajuste").GetGuid();
    }

    private async Task<Guid> AjusteEnElEcommerce()
    {
        var respuesta = await _eco.Enviar(HttpMethod.Post, "/prueba/ajuste", Admin);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var cuerpo = await Json(respuesta);
        return cuerpo.RootElement.GetProperty("ajuste").GetGuid();
    }

    // ---------- Inventario: auditoría, directo ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-24")]
    public async Task Inventario_Auditoria_ConCredencialDeConsultaYAdministrador_ResponderConLosRegistrosDelContrato()
    {
        var ajuste = await AjusteEnElInventario();

        var respuesta = await _inv.Enviar(HttpMethod.Get, $"{RutaInv}/auditoria?accion=AjusteRegistrado", InventarioDeAuditoriaHost.CredencialDeConsulta, TokenDeAdmin());

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var cuerpo = await Json(respuesta);
        var registros = cuerpo.RootElement.GetProperty("registros").EnumerateArray().ToList();
        Assert.Contains(registros, r => r.GetProperty("objetoId").GetString() == ajuste.ToString("D") && r.GetProperty("servicio").GetString() == "Inventario" && r.GetProperty("accion").GetString() == "AjusteRegistrado");
        Assert.True(cuerpo.RootElement.TryGetProperty("cursorDeLaPagina", out _));
        Assert.False(cuerpo.RootElement.GetProperty("faltanDelInventario").GetBoolean());
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-21")]
    public async Task Inventario_SinCredencialDeServicio_Responde401_YDejaUnEvento()
    {
        var antes = await Denegaciones(bdInv);

        var respuesta = await _inv.Enviar(HttpMethod.Get, $"{RutaInv}/auditoria", credencial: null, TokenDeAdmin());

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        using var cuerpo = await Json(respuesta);
        Assert.Equal("INV-NO-AUTENTICADO", cuerpo.RootElement.GetProperty("code").GetString());
        Assert.Equal(antes + 1, await Denegaciones(bdInv));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-24")]
    public async Task Inventario_ConLaCredencialDeOtroAlcance_Responde401_AunqueElTokenSeaDeAdministrador()
    {
        var antes = await Denegaciones(bdInv);

        var respuesta = await _inv.Enviar(HttpMethod.Get, $"{RutaInv}/auditoria", InventarioDeAuditoriaHost.CredencialDeHistorial, TokenDeAdmin());

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal(antes + 1, await Denegaciones(bdInv));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-23")]
    public async Task Inventario_ConSoloLaCredencialDeServicio_NoSuplantaAUnAdministradorConUnaCabeceraDeActor()
    {
        var antes = await Denegaciones(bdInv);
        var admin = Guid.NewGuid();

        var respuesta = await _inv.Enviar(
            HttpMethod.Get,
            $"{RutaInv}/auditoria",
            InventarioDeAuditoriaHost.CredencialDeConsulta,
            token: null,
            cuerpo: null,
            ("X-Actor-Id", admin.ToString("D")),
            ("X-Actor-Rol", Admin),
            ("X-Prueba-Rol", Admin));

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal(antes + 1, await Denegaciones(bdInv));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-04")]
    public async Task Inventario_UnUsuarioQueNoEsAdministradorCentral_Recibe403_ConUnEventoPorIntento()
    {
        var usuario = Guid.NewGuid();
        var token = TokensDePrueba.Crear(Encargado, usuario, Guid.NewGuid());
        var antes = await Denegaciones(bdInv);

        var primera = await _inv.Enviar(HttpMethod.Get, $"{RutaInv}/auditoria", InventarioDeAuditoriaHost.CredencialDeConsulta, token);
        var segunda = await _inv.Enviar(HttpMethod.Get, $"{RutaInv}/auditoria", InventarioDeAuditoriaHost.CredencialDeConsulta, token);

        Assert.Equal(HttpStatusCode.Forbidden, primera.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, segunda.StatusCode);
        using var cuerpo = await Json(primera);
        Assert.Equal("INV-PROHIBIDO", cuerpo.RootElement.GetProperty("code").GetString());
        Assert.Equal(antes + 2, await Denegaciones(bdInv));
        var delUsuario = await bdInv.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'AccesoDenegado' AND actor_id = '{usuario}' AND motivo = 'fuera-de-rol' AND ruta = '/api/inventario/v1/auditoria'");
        Assert.Equal(2L, delUsuario);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-21")]
    public async Task Inventario_CienSolicitudesConLaMismaCorrelacionDelCliente_SonCienEventos()
    {
        var antes = await Denegaciones(bdInv);

        for (var i = 0; i < 100; i++)
        {
            var respuesta = await _inv.Enviar(HttpMethod.Get, $"{RutaInv}/auditoria", credencial: null, token: null, cuerpo: null, ("X-Correlacion-Id", "la-misma-correlacion"));
            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        }

        Assert.Equal(antes + 100, await Denegaciones(bdInv));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-27")]
    public async Task Inventario_UnCursorAlteradoOTruncado_Responde400Uniforme()
    {
        var token = TokenDeAdmin();
        foreach (var cursor in new[] { "v1.abc.firma", "no-es-un-cursor", new string('a', 5000), "v1.", ".." })
        {
            var respuesta = await _inv.Enviar(HttpMethod.Get, $"{RutaInv}/auditoria?cursor={Uri.EscapeDataString(cursor)}", InventarioDeAuditoriaHost.CredencialDeConsulta, token);

            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            using var cuerpo = await Json(respuesta);
            Assert.Equal("INV-VALIDACION-INVALIDA", cuerpo.RootElement.GetProperty("code").GetString());
        }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-27")]
    public async Task Inventario_UnCursorDeOtroUsuario_Responde400Uniforme()
    {
        for (var i = 0; i < 22; i++)
        {
            await AjusteEnElInventario();
        }

        var uno = TokenDeAdmin();
        var primera = await _inv.Enviar(HttpMethod.Get, $"{RutaInv}/auditoria", InventarioDeAuditoriaHost.CredencialDeConsulta, uno);
        using var pagina = await Json(primera);
        var cursor = pagina.RootElement.GetProperty("siguienteCursor").GetString();
        Assert.NotNull(cursor);

        var propio = await _inv.Enviar(HttpMethod.Get, $"{RutaInv}/auditoria?cursor={Uri.EscapeDataString(cursor!)}", InventarioDeAuditoriaHost.CredencialDeConsulta, uno);
        var ajeno = await _inv.Enviar(HttpMethod.Get, $"{RutaInv}/auditoria?cursor={Uri.EscapeDataString(cursor!)}", InventarioDeAuditoriaHost.CredencialDeConsulta, TokenDeAdmin());
        var otroFiltro = await _inv.Enviar(HttpMethod.Get, $"{RutaInv}/auditoria?accion=AjusteRegistrado&cursor={Uri.EscapeDataString(cursor!)}", InventarioDeAuditoriaHost.CredencialDeConsulta, uno);

        Assert.Equal(HttpStatusCode.OK, propio.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, ajeno.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, otroFiltro.StatusCode);
    }

    [Theory]
    [Trait("Criterio", "AC-AUD-07")]
    [InlineData("usuarioId=no-es-un-guid")]
    [InlineData("desde=ayer")]
    [InlineData("accion=A&accion=B")]
    [InlineData("texto=ab")]
    [InlineData("accion=UnaAccionQueNoExiste")]
    public async Task Inventario_UnFiltroInvalido_Responde400(string consulta)
    {
        var respuesta = await _inv.Enviar(HttpMethod.Get, $"{RutaInv}/auditoria?{consulta}", InventarioDeAuditoriaHost.CredencialDeConsulta, TokenDeAdmin());

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-07")]
    public async Task Inventario_ElTextoBuscadoConComodines_SeTrataComoTextoLiteral()
    {
        await AjusteEnElInventario();

        var respuesta = await _inv.Enviar(HttpMethod.Get, $"{RutaInv}/auditoria?texto={Uri.EscapeDataString("%%%")}", InventarioDeAuditoriaHost.CredencialDeConsulta, TokenDeAdmin());

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var cuerpo = await Json(respuesta);
        Assert.Empty(cuerpo.RootElement.GetProperty("registros").EnumerateArray());
    }

    // ---------- Inventario: historial, alertas y marca ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-07")]
    public async Task Inventario_ElHistorialEsElDeLaSucursalDelToken_YPedirOtraSeRechazaYSeRegistra()
    {
        var sucursal = Guid.NewGuid();
        var otra = Guid.NewGuid();
        var usuario = Guid.NewGuid();
        var token = TokensDePrueba.Crear(Encargado, usuario, sucursal);
        var antes = await Denegaciones(bdInv);

        var propio = await _inv.Enviar(HttpMethod.Get, $"{RutaInv}/historial", InventarioDeAuditoriaHost.CredencialDeHistorial, token);
        var propioExplicito = await _inv.Enviar(HttpMethod.Get, $"{RutaInv}/historial?sucursal={sucursal:D}", InventarioDeAuditoriaHost.CredencialDeHistorial, token);
        var ajeno = await _inv.Enviar(HttpMethod.Get, $"{RutaInv}/historial?sucursal={otra:D}", InventarioDeAuditoriaHost.CredencialDeHistorial, token);

        Assert.Equal(HttpStatusCode.OK, propio.StatusCode);
        Assert.Equal(HttpStatusCode.OK, propioExplicito.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.Equal(antes + 1, await Denegaciones(bdInv));
        Assert.Equal(1L, await bdInv.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'AccesoDenegado' AND actor_id = '{usuario}' AND ruta = '/api/inventario/v1/historial'"));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-30")]
    public async Task Inventario_LasAlertasNuevas_SoloSeLeenConLaCredencialDeLecturaDeAlertas()
    {
        using (var alcance = _inv.Servicios.CreateScope())
        {
            await alcance.ServiceProvider.GetRequiredService<InventarioAuditoria.IAlertas>().PublicarDeSeguridad(new InventarioAuditoria.CodigoDeAlerta(InventarioAuditoria.Interno.MotivosDeAlerta.AccesosDenegados));
        }

        var leidas = await _inv.Enviar(HttpMethod.Get, $"{RutaInv}/alertas?desde={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"))}", InventarioDeAuditoriaHost.CredencialDeAlertas);
        var conOtraCredencial = await _inv.Enviar(HttpMethod.Get, $"{RutaInv}/alertas", InventarioDeAuditoriaHost.CredencialDeConsulta, TokenDeAdmin());
        var sinCredencial = await _inv.Enviar(HttpMethod.Get, $"{RutaInv}/alertas", credencial: null, TokenDeAdmin());

        Assert.Equal(HttpStatusCode.OK, leidas.StatusCode);
        using var cuerpo = await Json(leidas);
        Assert.Contains(cuerpo.RootElement.EnumerateArray(), a => a.GetProperty("motivo").GetString() == "accesos-denegados" && a.GetProperty("id").GetString()!.StartsWith("inv:", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Unauthorized, conOtraCredencial.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sinCredencial.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public async Task Inventario_MarcarRevisada_ExigeAdministradorCentral_YLaMarcaQuedaAuditada()
    {
        using (var alcance = _inv.Servicios.CreateScope())
        {
            await alcance.ServiceProvider.GetRequiredService<InventarioAuditoria.IAlertas>().PublicarDeSeguridad(new InventarioAuditoria.CodigoDeAlerta(InventarioAuditoria.Interno.MotivosDeAlerta.IngresosFallidos));
        }

        var lectura = await _inv.Enviar(HttpMethod.Get, $"{RutaInv}/alertas?desde={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"))}", InventarioDeAuditoriaHost.CredencialDeAlertas);
        using var alertas = await Json(lectura);
        var alerta = alertas.RootElement.EnumerateArray().First(a => a.GetProperty("motivo").GetString() == "ingresos-fallidos");
        var id = alerta.GetProperty("id").GetString()!;
        var ocurrencias = alerta.GetProperty("ocurrencias").GetInt32();
        var cuerpoDeLaMarca = () => new StringContent($"{{\"contadorVisto\":{ocurrencias}}}", Encoding.UTF8, "application/json");

        var comoEncargado = await _inv.Enviar(HttpMethod.Post, $"{RutaInv}/alertas/{id}/revisada", InventarioDeAuditoriaHost.CredencialDeMarca, TokensDePrueba.Crear(Encargado, Guid.NewGuid(), Guid.NewGuid()), cuerpoDeLaMarca());
        var comoAdmin = await _inv.Enviar(HttpMethod.Post, $"{RutaInv}/alertas/{id}/revisada", InventarioDeAuditoriaHost.CredencialDeMarca, TokenDeAdmin(), cuerpoDeLaMarca());
        var conCampoExtra = await _inv.Enviar(HttpMethod.Post, $"{RutaInv}/alertas/{id}/revisada", InventarioDeAuditoriaHost.CredencialDeMarca, TokenDeAdmin(), new StringContent("{\"contadorVisto\":1,\"revisadaPor\":\"x\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Forbidden, comoEncargado.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, comoAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, conCampoExtra.StatusCode);
        Assert.Equal(1L, await bdInv.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'AlertaMarcadaRevisada' AND objeto_id = '{id}'"));
    }

    // ---------- E-commerce: la lista unificada ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-16")]
    public async Task Ecommerce_LaListaUnificadaTraeLoDeLosDosServicios_ConLasPaginasDeVeinte()
    {
        var propio = await AjusteEnElEcommerce();
        var remoto = await AjusteEnElInventario();

        var respuesta = await _eco.Enviar(HttpMethod.Get, $"{RutaEco}/auditoria?accion=AjusteRegistrado", Admin);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var cuerpo = await Json(respuesta);
        var registros = cuerpo.RootElement.GetProperty("registros").EnumerateArray().ToList();
        Assert.Contains(registros, r => r.GetProperty("objetoId").GetString() == propio.ToString("D") && r.GetProperty("servicio").GetString() == "Ecommerce");
        Assert.Contains(registros, r => r.GetProperty("objetoId").GetString() == remoto.ToString("D") && r.GetProperty("servicio").GetString() == "Inventario");
        Assert.True(registros.Count <= 20);
        Assert.False(cuerpo.RootElement.GetProperty("faltanDelInventario").GetBoolean());
        Assert.DoesNotContain("cursorDeLaPagina", cuerpo.RootElement.EnumerateObject().Where(p => p.Value.ValueKind != JsonValueKind.Null).Select(p => p.Name));
        var instantes = registros.Select(r => r.GetProperty("instanteUtc").GetDateTimeOffset()).ToList();
        Assert.Equal(instantes.OrderByDescending(i => i), instantes);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-16")]
    public async Task Ecommerce_ConElInventarioCaido_Responde200ConElAvisoDeQueFaltanSusRegistros()
    {
        await AjusteEnElEcommerce();
        _eco.InventarioCaido = true;
        try
        {
            var respuesta = await _eco.Enviar(HttpMethod.Get, $"{RutaEco}/auditoria", Admin);

            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            using var cuerpo = await Json(respuesta);
            Assert.True(cuerpo.RootElement.GetProperty("faltanDelInventario").GetBoolean());
            Assert.NotEmpty(cuerpo.RootElement.GetProperty("registros").EnumerateArray());
        }
        finally
        {
            _eco.InventarioCaido = false;
        }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-04")]
    public async Task Ecommerce_SinSesionOSinRol_Rechaza_YRegistraUnEventoPorIntento()
    {
        var antes = await Denegaciones(bdEco);
        var usuario = Guid.NewGuid();

        var anonimo = await _eco.Enviar(HttpMethod.Get, $"{RutaEco}/auditoria");
        var cliente = await _eco.Enviar(HttpMethod.Get, $"{RutaEco}/auditoria", "cliente", usuario);
        var clienteDeNuevo = await _eco.Enviar(HttpMethod.Get, $"{RutaEco}/alertas", "cliente", usuario);

        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cliente.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, clienteDeNuevo.StatusCode);
        using var cuerpo = await Json(cliente);
        Assert.Equal("ECO-PROHIBIDO", cuerpo.RootElement.GetProperty("code").GetString());
        Assert.Equal(antes + 3, await Denegaciones(bdEco));
        Assert.Equal(2L, await bdEco.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'AccesoDenegado' AND actor_id = '{usuario}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-27")]
    public async Task Ecommerce_ElCursorYLosFiltrosInvalidos_SonElMismo400Uniforme()
    {
        for (var i = 0; i < 22; i++)
        {
            await AjusteEnElEcommerce();
        }

        var primera = await _eco.Enviar(HttpMethod.Get, $"{RutaEco}/auditoria?accion=AjusteRegistrado", Admin, Guid.NewGuid());
        using var pagina = await Json(primera);
        var cursor = pagina.RootElement.GetProperty("siguienteCursor").GetString();
        Assert.NotNull(cursor);

        var alterado = await _eco.Enviar(HttpMethod.Get, $"{RutaEco}/auditoria?accion=AjusteRegistrado&cursor={Uri.EscapeDataString(cursor![..^2] + "xx")}", Admin);
        var deOtroUsuario = await _eco.Enviar(HttpMethod.Get, $"{RutaEco}/auditoria?accion=AjusteRegistrado&cursor={Uri.EscapeDataString(cursor)}", Admin, Guid.NewGuid());
        var textoCorto = await _eco.Enviar(HttpMethod.Get, $"{RutaEco}/auditoria?texto=ab", Admin);

        foreach (var respuesta in new[] { alterado, deOtroUsuario, textoCorto })
        {
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            using var cuerpo = await Json(respuesta);
            Assert.Equal("ECO-VALIDACION-INVALIDA", cuerpo.RootElement.GetProperty("code").GetString());
        }
    }

    // ---------- E-commerce: alertas, historial y auditoría caída ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public async Task Ecommerce_LasAlertasDeSeguridadDeLosDosServicios_SeListan_YLaMarcaDeLaDelInventarioLlegaAlInventario()
    {
        using (var alcance = _inv.Servicios.CreateScope())
        {
            await alcance.ServiceProvider.GetRequiredService<InventarioAuditoria.IAlertas>().PublicarDeSeguridad(new InventarioAuditoria.CodigoDeAlerta(InventarioAuditoria.Interno.MotivosDeAlerta.AccesosDenegados));
        }

        using (var alcance = _eco.Servicios.CreateScope())
        {
            await alcance.ServiceProvider.GetRequiredService<IAlertas>().PublicarDeSeguridad(new CodigoDeAlerta(MotivosDeAlerta.IngresosFallidos));
        }

        var lista = await _eco.Enviar(HttpMethod.Get, $"{RutaEco}/alertas", Admin);

        Assert.Equal(HttpStatusCode.OK, lista.StatusCode);
        using var cuerpo = await Json(lista);
        var alertas = cuerpo.RootElement.GetProperty("alertas").EnumerateArray().ToList();
        var remota = alertas.First(a => a.GetProperty("id").GetString()!.StartsWith("inv:", StringComparison.Ordinal));
        Assert.Contains(alertas, a => a.GetProperty("id").GetString()!.StartsWith("eco:", StringComparison.Ordinal));
        Assert.False(cuerpo.RootElement.GetProperty("faltanDelInventario").GetBoolean());
        Assert.All(alertas, a => Assert.Equal("Seguridad", a.GetProperty("clase").GetString()));

        var id = remota.GetProperty("id").GetString()!;
        var marca = await _eco.Enviar(HttpMethod.Post, $"{RutaEco}/alertas/{id}/revisada", Admin, Guid.NewGuid(), cuerpo: new StringContent($"{{\"contadorVisto\":{remota.GetProperty("ocurrencias").GetInt32()}}}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NoContent, marca.StatusCode);
        Assert.Equal(1L, await bdInv.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'AlertaMarcadaRevisada' AND objeto_id = '{id}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-07")]
    public async Task Ecommerce_ElHistorialEsElDeLaSucursalDelToken_PedidoAlInventario_YOtraSucursalSeRechaza()
    {
        var sucursal = Guid.NewGuid();
        var usuario = Guid.NewGuid();
        var antes = await Denegaciones(bdEco);

        var propio = await _eco.Enviar(HttpMethod.Get, $"{RutaEco}/historial", Encargado, usuario, sucursal);
        var ajeno = await _eco.Enviar(HttpMethod.Get, $"{RutaEco}/historial?sucursal={Guid.NewGuid():D}", Encargado, usuario, sucursal);

        Assert.Equal(HttpStatusCode.OK, propio.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.Equal(antes + 1, await Denegaciones(bdEco));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-03")]
    public async Task Ecommerce_SinAuditoria_ElAjusteNoSeAplica_YResponde503()
    {
        var antes = await bdEco.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE accion = 'AjusteRegistrado'");
        _eco.AuditoriaCaida = true;
        try
        {
            var respuesta = await _eco.Enviar(HttpMethod.Post, "/prueba/ajuste", Admin);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
            using var cuerpo = await Json(respuesta);
            Assert.Equal("ECO-SERVICIO-NO-DISPONIBLE", cuerpo.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain("InvalidOperation", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        finally
        {
            _eco.AuditoriaCaida = false;
        }

        Assert.Equal(antes, await bdEco.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE accion = 'AjusteRegistrado'"));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-03")]
    public async Task Inventario_SinAuditoria_ElAjusteNoSeAplica_YResponde503()
    {
        var antes = await bdInv.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE accion = 'AjusteRegistrado'");
        _inv.AuditoriaCaida = true;
        try
        {
            var respuesta = await _inv.Enviar(HttpMethod.Post, "/servicio/prueba/ajuste", InventarioDeAuditoriaHost.CredencialDeConsulta);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
            using var cuerpo = await Json(respuesta);
            Assert.Equal("INV-SERVICIO-NO-DISPONIBLE", cuerpo.RootElement.GetProperty("code").GetString());
        }
        finally
        {
            _inv.AuditoriaCaida = false;
        }

        Assert.Equal(antes, await bdInv.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE accion = 'AjusteRegistrado'"));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-03")]
    public async Task Ecommerce_SiNoSePuedeRegistrarElRechazo_ElRechazoSeMantiene_YQuedaUnaAlertaTecnica()
    {
        _eco.DenegacionesCaidas = true;
        try
        {
            var respuesta = await _eco.Enviar(HttpMethod.Get, $"{RutaEco}/auditoria", "cliente", Guid.NewGuid());

            Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        }
        finally
        {
            _eco.DenegacionesCaidas = false;
        }

        Assert.True(await bdEco.Consultar<long>($"SELECT count(*) FROM auditoria.alertas WHERE motivo = '{MotivosDeAlerta.AuditoriaNoDisponible}' AND clase = 'tecnica'") >= 1);
    }

    // ---------- Composición ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-21")]
    public void LaComposicionDeLosDosServicios_ValidaLosCiclosDeVida_YDaUnContextoDeDenegacionPorSolicitud()
    {
        // Los dos anfitriones se construyeron con ValidateScopes y ValidateOnBuild: un servicio de vida por solicitud capturado por uno de vida larga haría fallar el arranque.
        using var uno = _eco.Servicios.CreateScope();
        using var otro = _eco.Servicios.CreateScope();

        var primero = uno.ServiceProvider.GetRequiredService<IContextoDeDenegacion>();
        Assert.Same(primero, uno.ServiceProvider.GetRequiredService<IContextoDeDenegacion>());
        Assert.NotSame(primero, otro.ServiceProvider.GetRequiredService<IContextoDeDenegacion>());
        Assert.NotEqual(primero.Correlacion, otro.ServiceProvider.GetRequiredService<IContextoDeDenegacion>().Correlacion);
        using var delInventario = _inv.Servicios.CreateScope();
        var inv1 = delInventario.ServiceProvider.GetRequiredService<InventarioAuditoria.Interno.IContextoDeDenegacion>();
        Assert.Same(inv1, delInventario.ServiceProvider.GetRequiredService<InventarioAuditoria.Interno.IContextoDeDenegacion>());
    }
}
