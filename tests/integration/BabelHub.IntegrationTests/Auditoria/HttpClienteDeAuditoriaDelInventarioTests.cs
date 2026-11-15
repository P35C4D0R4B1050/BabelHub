using System.Net;
using System.Text;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Auditoria;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Auditoria;

/// <summary>
/// El cliente de la auditoría del Inventario por HTTP (T26; AUD-17, AUD-18, AUD-38; AC-AUD-16, AC-AUD-24, AC-AUD-30): con la cadena real de <c>Plataforma</c> (resiliencia y manejador de salida) y un servidor
/// simulado, comprueba el token del usuario y la credencial de cada alcance, que ninguna cabecera de actor sale, que 9 segundos se aceptan y 10 no, y que toda falla es «no disponible» o «rechazada», nunca una excepción.
/// </summary>
public sealed class HttpClienteDeAuditoriaDelInventarioTests
{
    private static readonly string SecretoDeConsulta = "consulta-" + Guid.NewGuid().ToString("N");
    private static readonly string SecretoDeAlertas = "alertas-" + Guid.NewGuid().ToString("N");
    private static readonly string SecretoDeMarca = "marca-" + Guid.NewGuid().ToString("N");
    private static readonly string SecretoDeHistorial = "historial-" + Guid.NewGuid().ToString("N");
    private static readonly string TokenDelUsuario = "jwt." + Guid.NewGuid().ToString("N");

    private sealed class ServidorSimulado(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Solicitudes { get; } = [];

        public List<Dictionary<string, string>> Cabeceras { get; } = [];

        public List<string?> Cuerpos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Solicitudes.Add(request);
            Cabeceras.Add(request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase));
            Cuerpos.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return await responder(request, cancellationToken);
        }
    }

    private sealed class OrigenDePrueba : IOrigenDeSolicitud
    {
        public OrigenDeSolicitud Actual => OrigenDeSolicitud.Desconocido;
    }

    private sealed class TokenDePrueba(string? token) : ITokenDelUsuarioDeLaSesion
    {
        public int Pedidos { get; private set; }

        public Task<ValorSecreto?> Actual(CancellationToken cancelacion = default)
        {
            Pedidos++;
            return Task.FromResult(token is null ? null : new ValorSecreto(token));
        }
    }

    private static OpcionesDeCredencialesDeSalida Credenciales() => new(
    [
        new(AlcanceDeServicio.ConsultaDeAuditoria, new ValorSecreto(SecretoDeConsulta)),
        new(AlcanceDeServicio.LecturaDeAlertas, new ValorSecreto(SecretoDeAlertas)),
        new(AlcanceDeServicio.MarcarAlerta, new ValorSecreto(SecretoDeMarca)),
        new(AlcanceDeServicio.Historial, new ValorSecreto(SecretoDeHistorial)),
    ]);

    private static (HttpClienteDeAuditoriaDelInventario Cliente, TokenDePrueba Token) Crear(ServidorSimulado servidor, string? token = "usar-el-del-usuario", OpcionesDelClienteDeServicio? opciones = null)
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddSingleton(Credenciales());
        servicios.AddSingleton<IOrigenDeSolicitud>(new OrigenDePrueba());
        servicios.AgregarClienteDeServicio(HttpClienteDeAuditoriaDelInventario.NombreDelCliente, new Uri("https://inventario.interno/"), opciones ?? new OpcionesDelClienteDeServicio { ReintentosMaximos = 0, DuracionDelCorte = TimeSpan.FromSeconds(30) })
            .ConfigurePrimaryHttpMessageHandler(() => servidor);
        var proveedor = servicios.BuildServiceProvider();
        var tokenDePrueba = new TokenDePrueba(token == "usar-el-del-usuario" ? TokenDelUsuario : token);
        return (new HttpClienteDeAuditoriaDelInventario(proveedor.GetRequiredService<IHttpClientFactory>(), tokenDePrueba), tokenDePrueba);
    }

    private static HttpResponseMessage Json(string cuerpo, HttpStatusCode estado = HttpStatusCode.OK) => new(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private static string Pagina(int registros, string? siguiente = null, string? deLaPagina = "v1.pagina.firma") =>
        "{\"registros\":[" + string.Join(',', Enumerable.Range(0, registros).Select(i => Registro(Guid.NewGuid(), i))) + "],\"siguienteCursor\":" + (siguiente is null ? "null" : $"\"{siguiente}\"") + ",\"cursorDeLaPagina\":" + (deLaPagina is null ? "null" : $"\"{deLaPagina}\"") + "}";

    private static string Registro(Guid id, int segundos) =>
        $"{{\"id\":\"{id}\",\"servicio\":\"Inventario\",\"tipo\":\"Auditoria\",\"actorTipo\":\"Usuario\",\"actorId\":\"{Guid.NewGuid()}\",\"rol\":\"administrador-central\",\"instanteUtc\":\"{DateTimeOffset.UtcNow.AddSeconds(-segundos):O}\",\"accion\":\"AjusteRegistrado\",\"resultado\":\"Exitoso\",\"objetoTipo\":\"ajuste\",\"objetoId\":\"{Guid.NewGuid():D}\",\"sucursalOrigen\":\"{Guid.NewGuid():D}\",\"detalle\":\"{{\\\"cantidadAnterior\\\":\\\"5\\\"}}\",\"clavesPersonales\":[],\"truncado\":false}}";

    private static string Alerta(string id, string clase = "Seguridad") =>
        $"{{\"id\":\"{id}\",\"servicio\":\"Inventario\",\"motivo\":\"accesos-denegados\",\"clase\":\"{clase}\",\"primeraOcurrencia\":\"{DateTimeOffset.UtcNow.AddMinutes(-5):O}\",\"ultimaOcurrencia\":\"{DateTimeOffset.UtcNow.AddMinutes(-1):O}\",\"ocurrencias\":3,\"revisadasHasta\":1,\"revisadaPor\":null,\"revisadaEn\":null}}";

    // ---------- Credenciales y token ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-24")]
    public async Task LaAuditoriaSaleConLaCredencialDeSuAlcanceYElTokenDelUsuario_SinCabecerasDeActor()
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json(Pagina(2, "sig"))));
        var (cliente, _) = Crear(servidor);
        var filtro = new FiltroDeAuditoria { Accion = "PedidoAnulado", UsuarioId = Guid.NewGuid(), Origen = "v1:abc", Desde = DateTimeOffset.UtcNow.AddDays(-2), Hasta = DateTimeOffset.UtcNow, Texto = "100% & más=1; x" };

        var respuesta = await cliente.Consultar(new ConsultaDeAuditoria(filtro, "cursor/con+raros=="));

        Assert.True(respuesta.Disponible);
        Assert.Equal(2, respuesta.Valor.Registros.Count);
        Assert.Equal("sig", respuesta.Valor.SiguienteCursor);
        Assert.Equal("v1.pagina.firma", respuesta.Valor.CursorDeLaPagina);
        var solicitud = Assert.Single(servidor.Solicitudes);
        Assert.Equal(HttpMethod.Get, solicitud.Method);
        var cabeceras = Assert.Single(servidor.Cabeceras);
        Assert.Equal(SecretoDeConsulta, cabeceras[CredencialesDeSalidaHandler.CabeceraDeCredencial]);
        Assert.Equal("Bearer " + TokenDelUsuario, cabeceras["Authorization"]);
        Assert.DoesNotContain(cabeceras.Keys, k => k.StartsWith("X-Actor", StringComparison.OrdinalIgnoreCase) || k.StartsWith("X-Usuario", StringComparison.OrdinalIgnoreCase) || k.StartsWith("X-Rol", StringComparison.OrdinalIgnoreCase));
        var consulta = solicitud.RequestUri!.Query;
        Assert.Contains("accion=PedidoAnulado", consulta, StringComparison.Ordinal);
        Assert.Contains("texto=100%25%20%26%20m%C3%A1s%3D1%3B%20x", consulta, StringComparison.Ordinal);
        Assert.Contains("cursor=cursor%2Fcon%2Braros%3D%3D", consulta, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-30")]
    public async Task LaLecturaDeAlertasUsaLaCredencialDeLecturaDeAlertasSinTokenDeUsuario()
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json("[" + Alerta($"inv:{Guid.NewGuid():D}") + "]")));
        var (cliente, token) = Crear(servidor);

        var respuesta = await cliente.LeerAlertasNuevas(DateTimeOffset.UtcNow.AddMinutes(-20));

        Assert.True(respuesta.Disponible);
        var alerta = Assert.Single(respuesta.Valor);
        Assert.Equal(ServicioAuditado.Inventario, alerta.Servicio);
        Assert.Equal(0, token.Pedidos);
        var cabeceras = Assert.Single(servidor.Cabeceras);
        Assert.Equal(SecretoDeAlertas, cabeceras[CredencialesDeSalidaHandler.CabeceraDeCredencial]);
        Assert.DoesNotContain("Authorization", cabeceras.Keys);
        Assert.StartsWith("/api/inventario/v1/alertas?desde=", servidor.Solicitudes[0].RequestUri!.PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public async Task MarcarRevisadaReenviaElContadorConElTokenYLaCredencialDeMarca_YDevuelveLaAlertaYaActualizada()
    {
        var id = $"inv:{Guid.NewGuid():D}";
        var servidor = new ServidorSimulado((solicitud, _) => Task.FromResult(solicitud.Method == HttpMethod.Post ? new HttpResponseMessage(HttpStatusCode.NoContent) : Json("[" + Alerta(id) + "]")));
        var (cliente, _) = Crear(servidor);

        var respuesta = await cliente.MarcarAlertaRevisada(id, 3);

        Assert.True(respuesta.Disponible);
        Assert.Equal(id, respuesta.Valor.Id);
        Assert.Equal(2, servidor.Solicitudes.Count);
        Assert.Equal(SecretoDeMarca, servidor.Cabeceras[0][CredencialesDeSalidaHandler.CabeceraDeCredencial]);
        Assert.Equal("Bearer " + TokenDelUsuario, servidor.Cabeceras[0]["Authorization"]);
        Assert.Equal($"/api/inventario/v1/alertas/{Uri.EscapeDataString(id)}/revisada", servidor.Solicitudes[0].RequestUri!.AbsolutePath);
        Assert.Equal("{\"contadorVisto\":3}", servidor.Cuerpos[0]);
        Assert.Equal(SecretoDeAlertas, servidor.Cabeceras[1][CredencialesDeSalidaHandler.CabeceraDeCredencial]);
    }


    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public async Task UnIdentificadorDeAlertaQueNoEsDelInventarioNoSale()
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        var (cliente, _) = Crear(servidor);

        await Assert.ThrowsAsync<ArgumentException>(() => cliente.MarcarAlertaRevisada("inv:../../x", 1));
        await Assert.ThrowsAsync<ArgumentException>(() => cliente.MarcarAlertaRevisada($"eco:{Guid.NewGuid():D}", 1));

        Assert.Empty(servidor.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-07")]
    public async Task ElHistorialSaleConSuAlcanceYElTokenYLaSucursalComoParametro()
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json(Pagina(1, null, "v1.p.f"))));
        var (cliente, _) = Crear(servidor);
        var sucursal = Guid.NewGuid();

        var respuesta = await cliente.ConsultarHistorial(sucursal, "cur");

        Assert.True(respuesta.Disponible);
        Assert.Equal(SecretoDeHistorial, Assert.Single(servidor.Cabeceras)[CredencialesDeSalidaHandler.CabeceraDeCredencial]);
        Assert.Equal($"?sucursal={sucursal:D}&cursor=cur", servidor.Solicitudes[0].RequestUri!.Query);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-24")]
    public async Task SinTokenDelUsuarioNoSeLlamaAlInventarioYSeInformaRechazada()
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json(Pagina(0))));
        var (cliente, token) = Crear(servidor, token: null);

        Assert.Equal(EstadoDeRespuestaDelInventario.Rechazada, (await cliente.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()))).Estado);
        Assert.Equal(EstadoDeRespuestaDelInventario.Rechazada, (await cliente.ConsultarHistorial(Guid.NewGuid(), null)).Estado);
        Assert.Equal(EstadoDeRespuestaDelInventario.Rechazada, (await cliente.MarcarAlertaRevisada($"inv:{Guid.NewGuid():D}", 1)).Estado);
        Assert.Empty(servidor.Solicitudes);
        Assert.Equal(3, token.Pedidos);
    }

    // ---------- Plazos y fallas (AUD-18, AC-AUD-16) ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-16")]
    public async Task ElInventarioQueRespondeEnNueveSegundosSeAcepta()
    {
        var servidor = new ServidorSimulado(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(9), ct);
            return Json(Pagina(1));
        });
        var (cliente, _) = Crear(servidor, opciones: new OpcionesDelClienteDeServicio { Espera = TimeSpan.FromSeconds(10), ReintentosMaximos = 0 });

        var respuesta = await cliente.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()));

        Assert.True(respuesta.Disponible);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-16")]
    public async Task ElInventarioQueNoRespondeEnElPlazoEsNoDisponible_SinExcepcion()
    {
        var servidor = new ServidorSimulado(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json(Pagina(1));
        });
        var (cliente, _) = Crear(servidor, opciones: new OpcionesDelClienteDeServicio { Espera = TimeSpan.FromMilliseconds(300), ReintentosMaximos = 0 });

        var respuesta = await cliente.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()));

        Assert.Equal(EstadoDeRespuestaDelInventario.NoDisponible, respuesta.Estado);
    }

    [Theory]
    [Trait("Criterio", "AC-AUD-16")]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task UnErrorDelServidorEsNoDisponible(HttpStatusCode estado)
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(new HttpResponseMessage(estado) { Content = new StringContent("Host=db;Password=Sup3r") }));
        var (cliente, _) = Crear(servidor);

        Assert.Equal(EstadoDeRespuestaDelInventario.NoDisponible, (await cliente.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()))).Estado);
        Assert.Equal(EstadoDeRespuestaDelInventario.NoDisponible, (await cliente.LeerAlertasNuevas(DateTimeOffset.UtcNow)).Estado);
    }

    [Theory]
    [Trait("Criterio", "AC-AUD-24")]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task UnRechazoDelInventarioEsRechazadaYNoSeDisfrazaDeFalla(HttpStatusCode estado)
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(new HttpResponseMessage(estado)));
        var (cliente, _) = Crear(servidor);

        Assert.Equal(EstadoDeRespuestaDelInventario.Rechazada, (await cliente.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()))).Estado);
        Assert.Equal(EstadoDeRespuestaDelInventario.Rechazada, (await cliente.ConsultarHistorial(Guid.NewGuid(), null)).Estado);
    }

    [Theory]
    [Trait("Criterio", "AC-AUD-16")]
    [InlineData("esto no es json")]
    [InlineData("{\"registros\":null}")]
    [InlineData("[]")]
    [InlineData("{\"registros\":[{\"id\":\"no-es-un-guid\"}]}")]
    [InlineData("{\"registros\":[{\"id\":\"11111111-1111-4111-8111-111111111111\",\"servicio\":\"Marte\",\"tipo\":\"Auditoria\",\"actorTipo\":\"Usuario\",\"instanteUtc\":\"2026-11-14T10:00:00Z\",\"accion\":\"AjusteRegistrado\",\"resultado\":\"Exitoso\"}]}")]
    [InlineData("{\"registros\":[{\"id\":\"11111111-1111-4111-8111-111111111111\",\"servicio\":\"Inventario\",\"tipo\":\"Auditoria\",\"actorTipo\":\"Usuario\",\"instanteUtc\":\"2026-11-14T10:00:00Z\",\"accion\":\"accion en minusculas\",\"resultado\":\"Exitoso\"}]}")]
    [InlineData("{\"registros\":[{\"id\":\"11111111-1111-4111-8111-111111111111\",\"servicio\":\"Inventario\",\"tipo\":\"Auditoria\",\"actorTipo\":\"Usuario\",\"instanteUtc\":\"2026-11-14T10:00:00-05:00\",\"accion\":\"AjusteRegistrado\",\"resultado\":\"Exitoso\"}]}")]
    public async Task UnaRespuestaInvalidaEsNoDisponible_SinExcepcion(string cuerpo)
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json(cuerpo)));
        var (cliente, _) = Crear(servidor);

        var respuesta = await cliente.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()));

        Assert.Equal(EstadoDeRespuestaDelInventario.NoDisponible, respuesta.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-16")]
    public async Task UnaRespuestaMasGrandeQueElTopeEsNoDisponible()
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json("{\"registros\":[],\"relleno\":\"" + new string('x', HttpClienteDeAuditoriaDelInventario.MaximoDeBytesDeLaRespuesta) + "\"}")));
        var (cliente, _) = Crear(servidor);

        Assert.Equal(EstadoDeRespuestaDelInventario.NoDisponible, (await cliente.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()))).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-16")]
    public async Task UnaAlertaInvalidaEsNoDisponible_YUnaListaVaciaEsValida()
    {
        var invalida = new ServidorSimulado((_, _) => Task.FromResult(Json("[{\"id\":\"eco:11111111-1111-4111-8111-111111111111\",\"servicio\":\"Inventario\",\"motivo\":\"accesos-denegados\",\"clase\":\"Seguridad\",\"primeraOcurrencia\":\"2026-11-14T10:00:00Z\",\"ultimaOcurrencia\":\"2026-11-14T10:01:00Z\",\"ocurrencias\":1,\"revisadasHasta\":0}]")));
        Assert.Equal(EstadoDeRespuestaDelInventario.NoDisponible, (await Crear(invalida).Cliente.LeerAlertasNuevas(DateTimeOffset.UtcNow)).Estado);

        var vacia = new ServidorSimulado((_, _) => Task.FromResult(Json("[]")));
        var respuesta = await Crear(vacia).Cliente.LeerAlertasNuevas(DateTimeOffset.UtcNow);
        Assert.True(respuesta.Disponible);
        Assert.Empty(respuesta.Valor);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-16")]
    public async Task CancelarLaSolicitudPropagaLaCancelacion_NoLaDisfrazaDeNoDisponible()
    {
        var servidor = new ServidorSimulado(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json(Pagina(0));
        });
        var (cliente, _) = Crear(servidor);
        using var cancelada = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cliente.Consultar(new ConsultaDeAuditoria(new FiltroDeAuditoria()), cancelada.Token));
    }

    // ---------- Las llaves de opciones coinciden con las de Plataforma ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-24")]
    public void LasLlavesDeLasOpcionesDeLaSolicitudSonLasDeLaPlataforma()
    {
        var solicitud = new HttpRequestMessage(HttpMethod.Get, "x");
        solicitud.Options.Set(new HttpRequestOptionsKey<AlcanceDeServicio>("babelhub.alcance"), AlcanceDeServicio.Historial);
        solicitud.Options.Set(new HttpRequestOptionsKey<string>("babelhub.token-de-usuario"), "t");

        Assert.True(solicitud.Options.TryGetValue(OpcionesDeLaSolicitudDeServicio.Alcance, out var alcance));
        Assert.Equal(AlcanceDeServicio.Historial, alcance);
        Assert.True(solicitud.Options.TryGetValue(OpcionesDeLaSolicitudDeServicio.TokenDeUsuario, out var token));
        Assert.Equal("t", token);
    }
}
