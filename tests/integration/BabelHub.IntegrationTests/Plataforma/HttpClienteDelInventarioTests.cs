using System.Net;
using System.Text;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Plataforma;

/// <summary>
/// El cliente REST del Inventario de <c>Plataforma</c> (<see cref="IClienteDelInventario"/>) con la cadena real (resiliencia y manejadores de salida) y un servidor simulado: registrar edición, disponibilidad
/// por lotes, ingresos y sucursales (hallazgo de <c>ecommerce-catalogo</c> D01 y <c>web-base</c> R12). Comprueba la credencial de cada alcance, el token del usuario solo en las operaciones de usuario, que ninguna
/// cabecera de actor sale, la clave de idempotencia y el reintento solo de lo idempotente, la validación de las respuestas y que toda falla es un estado, nunca una excepción.
/// </summary>
public sealed class HttpClienteDelInventarioTests
{
    private static readonly string SecretoDeUsuario = "usuario-" + Guid.NewGuid().ToString("N");
    private static readonly string SecretoDeLectura = "lectura-" + Guid.NewGuid().ToString("N");
    private static readonly string SecretoDeOrigen = "origen-" + Guid.NewGuid().ToString("N");
    private static readonly string SecretoDePendientes = "pendientes-" + Guid.NewGuid().ToString("N");
    private static readonly string SecretoDeBaja = "baja-" + Guid.NewGuid().ToString("N");
    private static readonly string SecretoDeExpiracion = "expiracion-" + Guid.NewGuid().ToString("N");
    private static readonly string TokenDelUsuario = "jwt." + Guid.NewGuid().ToString("N");
    private static readonly string Clave = "ingreso-" + Guid.NewGuid().ToString("N");

    private sealed class ServidorSimulado(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public List<Dictionary<string, string>> Cabeceras { get; } = [];

        public List<string> Rutas { get; } = [];

        public List<HttpMethod> Metodos { get; } = [];

        public List<string?> Cuerpos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Cabeceras.Add(request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase));
            Rutas.Add(request.RequestUri!.PathAndQuery);
            Metodos.Add(request.Method);
            Cuerpos.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return await responder(request, Rutas.Count);
        }
    }

    private sealed class OrigenDePrueba(OrigenDeSolicitud origen) : IOrigenDeSolicitud
    {
        public OrigenDeSolicitud Actual => origen;
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

    private static (IClienteDelInventario Cliente, TokenDePrueba Token) Crear(ServidorSimulado servidor, string? token = "usar-el-del-usuario", OpcionesDelClienteDeServicio? opciones = null, OrigenDeSolicitud? origen = null)
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddSingleton(new OpcionesDeCredencialesDeSalida(
        [
            new(AlcanceDeServicio.OperacionDeUsuario, new ValorSecreto(SecretoDeUsuario)),
            new(AlcanceDeServicio.LecturaDeDisponibilidad, new ValorSecreto(SecretoDeLectura)),
            new(AlcanceDeServicio.OrigenReenviado, new ValorSecreto(SecretoDeOrigen)),
            new(AlcanceDeServicio.ConsultaDePendientes, new ValorSecreto(SecretoDePendientes)),
            new(AlcanceDeServicio.BajaDeTitular, new ValorSecreto(SecretoDeBaja)),
            new(AlcanceDeServicio.ExpiracionDeReservas, new ValorSecreto(SecretoDeExpiracion)),
        ]));
        servicios.AddSingleton<IOrigenDeSolicitud>(new OrigenDePrueba(origen ?? OrigenDeSolicitud.Desconocido));
        var tokenDePrueba = new TokenDePrueba(token == "usar-el-del-usuario" ? TokenDelUsuario : token);
        servicios.AddSingleton<ITokenDelUsuarioDeLaSesion>(tokenDePrueba);
        servicios.TryAddTransientDelToken();
        servicios.AgregarClienteDelInventario(new Uri("https://inventario.interno/"), opciones ?? new OpcionesDelClienteDeServicio { ReintentosMaximos = 0, RetrasoDelReintento = TimeSpan.FromMilliseconds(1), DuracionDelCorte = TimeSpan.FromSeconds(30) });
        servicios.AddHttpClient(HttpClienteDelInventario.NombreDelCliente).ConfigurePrimaryHttpMessageHandler(() => servidor);
        var proveedor = servicios.BuildServiceProvider();
        return (proveedor.GetRequiredService<IClienteDelInventario>(), tokenDePrueba);
    }

    private static HttpResponseMessage Json(string cuerpo, HttpStatusCode estado = HttpStatusCode.OK) => new(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Problema(HttpStatusCode estado, string codigo) =>
        new(estado) { Content = new StringContent($"{{\"type\":\"about:blank\",\"title\":\"texto remoto que no debe copiarse\",\"status\":{(int)estado},\"detail\":\"detalle remoto\",\"code\":\"{codigo}\",\"traceId\":\"t\"}}", Encoding.UTF8, "application/problem+json") };

    private static string Ingreso(Guid edicion, int cantidad = 5, Guid? id = null) =>
        $"{{\"ingresoId\":\"{id ?? Guid.NewGuid()}\",\"edicionId\":\"{edicion}\",\"codigoDeFuente\":\"recepcion\",\"cantidad\":{cantidad},\"referencia\":\"OC-1\",\"nota\":null,\"usuarioId\":\"{Guid.NewGuid()}\",\"instante\":\"{DateTimeOffset.UtcNow:O}\"}}";

    private static string Disponibilidad(Guid edicion, int vendible = 4) =>
        $"{{\"edicionId\":\"{edicion}\",\"vendibleEnLaRed\":{vendible},\"fuentes\":[{{\"codigoDeFuente\":\"central\",\"nombre\":\"Central\",\"vendible\":{vendible},\"estadoDeStock\":\"normal\"}}],\"consultadaEn\":\"{DateTimeOffset.UtcNow:O}\"}}";

    private static void SinCabecerasDeActor(Dictionary<string, string> cabeceras) =>
        Assert.DoesNotContain(cabeceras.Keys, k => k.StartsWith("X-Actor", StringComparison.OrdinalIgnoreCase) || k.StartsWith("X-Usuario", StringComparison.OrdinalIgnoreCase) || k.StartsWith("X-Rol", StringComparison.OrdinalIgnoreCase));

    // ---------- Registrar edición ----------

    [Fact]
    public async Task RegistrarEdicion_SaleConLaCredencialDeUsuarioYElToken_SinCabecerasDeActor()
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        var (cliente, _) = Crear(servidor);
        var id = Guid.NewGuid();

        var resultado = await cliente.RegistrarEdicion(new EdicionARegistrar(id, "impreso"));

        Assert.True(resultado.Exitosa);
        Assert.Equal(HttpMethod.Put, Assert.Single(servidor.Metodos));
        Assert.Equal($"/api/inventario/v1/ediciones/{id:D}", Assert.Single(servidor.Rutas));
        Assert.Contains("\"formato\":\"impreso\"", Assert.Single(servidor.Cuerpos), StringComparison.Ordinal);
        var cabeceras = Assert.Single(servidor.Cabeceras);
        Assert.Equal(SecretoDeUsuario, cabeceras[CredencialesDeSalidaHandler.CabeceraDeCredencial]);
        Assert.Equal("Bearer " + TokenDelUsuario, cabeceras["Authorization"]);
        SinCabecerasDeActor(cabeceras);
    }

    [Fact]
    public async Task RegistrarEdicion_ConFormatoEnConflicto_DevuelveElCodigoEstableSinElTextoRemoto()
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Problema(HttpStatusCode.Conflict, "INV-FORMATO-EN-CONFLICTO")));
        var (cliente, _) = Crear(servidor);

        var resultado = await cliente.RegistrarEdicion(new EdicionARegistrar(Guid.NewGuid(), "digital"));

        Assert.Equal(EstadoDeLlamadaAlInventario.Rechazada, resultado.Estado);
        Assert.Equal("INV-FORMATO-EN-CONFLICTO", resultado.Codigo);
        Assert.Equal(409, resultado.EstadoHttp);
    }

    [Fact]
    public async Task RegistrarEdicion_ConUnFormatoQueNoEsDelContrato_Rechaza()
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        var (cliente, _) = Crear(servidor);

        await Assert.ThrowsAsync<ArgumentException>(() => cliente.RegistrarEdicion(new EdicionARegistrar(Guid.NewGuid(), "otro")));
        Assert.Empty(servidor.Rutas);
    }

    [Fact]
    public async Task UnaOperacionDeUsuario_SinSesion_NoSaleYEsNoDisponible()
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        var (cliente, token) = Crear(servidor, token: null);

        var resultado = await cliente.RegistrarEdicion(new EdicionARegistrar(Guid.NewGuid(), "impreso"));

        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, resultado.Estado);
        Assert.Empty(servidor.Rutas);
        Assert.Equal(1, token.Pedidos);
    }

    // ---------- Disponibilidad por lotes ----------

    [Fact]
    public async Task ConsultarDisponibilidad_UsaLaCredencialDeLecturaSinTokenYReenviaElOrigen()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json($"{{\"ediciones\":[{Disponibilidad(a)}],\"noRegistradas\":[\"{b}\"]}}")));
        var origen = new OrigenDeSolicitud(HuellaDeOrigen.Crear("k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE", "v1"), "203.0.113.7");
        var (cliente, token) = Crear(servidor, origen: origen);

        var resultado = await cliente.ConsultarDisponibilidad([a, b]);

        Assert.True(resultado.Exitosa);
        Assert.Equal(a, Assert.Single(resultado.Valor.Ediciones).EdicionId);
        Assert.Equal(4, resultado.Valor.Ediciones[0].VendibleEnLaRed);
        Assert.Equal(b, Assert.Single(resultado.Valor.NoRegistradas));
        Assert.Equal(HttpMethod.Post, Assert.Single(servidor.Metodos));
        Assert.Equal("/api/inventario/v1/disponibilidad/consultas", Assert.Single(servidor.Rutas));
        var cabeceras = Assert.Single(servidor.Cabeceras);
        Assert.Equal(SecretoDeLectura, cabeceras[CredencialesDeSalidaHandler.CabeceraDeCredencial]);
        Assert.False(cabeceras.ContainsKey("Authorization"));
        Assert.Equal("203.0.113.7", cabeceras[CredencialesDeSalidaHandler.CabeceraDeOrigenReenviado]);
        Assert.Equal(SecretoDeOrigen, cabeceras[CredencialesDeSalidaHandler.CabeceraDeCredencialDeOrigen]);
        Assert.Equal(0, token.Pedidos);
    }

    [Fact]
    public async Task ConsultarDisponibilidad_ConUnaEdicionQueNoSePidio_EsNoDisponible()
    {
        var pedida = Guid.NewGuid();
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json($"{{\"ediciones\":[{Disponibilidad(Guid.NewGuid())}],\"noRegistradas\":[]}}")));
        var (cliente, _) = Crear(servidor);

        var resultado = await cliente.ConsultarDisponibilidad([pedida]);

        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, resultado.Estado);
    }

    [Fact]
    public async Task ConsultarDisponibilidad_ConVendibleNegativoOEstadoDeStockInventado_EsNoDisponible()
    {
        var id = Guid.NewGuid();
        var respuestas = new Queue<string>(
        [
            $"{{\"ediciones\":[{Disponibilidad(id, vendible: -1)}],\"noRegistradas\":[]}}",
            $"{{\"ediciones\":[{Disponibilidad(id).Replace("\"normal\"", "\"abundante\"", StringComparison.Ordinal)}],\"noRegistradas\":[]}}",
        ]);
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json(respuestas.Dequeue())));
        var (cliente, _) = Crear(servidor);

        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, (await cliente.ConsultarDisponibilidad([id])).Estado);
        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, (await cliente.ConsultarDisponibilidad([id])).Estado);
    }

    [Fact]
    public async Task ConsultarDisponibilidad_ConMasDeCincuentaEdiciones_NoSale()
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json("{}")));
        var (cliente, _) = Crear(servidor);

        await Assert.ThrowsAsync<ArgumentException>(() => cliente.ConsultarDisponibilidad([.. Enumerable.Range(0, 51).Select(_ => Guid.NewGuid())]));
        await Assert.ThrowsAsync<ArgumentException>(() => cliente.ConsultarDisponibilidad([]));
        Assert.Empty(servidor.Rutas);
    }

    [Fact]
    public async Task ConsultarDisponibilidad_Con429_EsLimiteExcedidoYNoUnaExcepcion()
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Problema(HttpStatusCode.TooManyRequests, "INV-LIMITE-EXCEDIDO")));
        var (cliente, _) = Crear(servidor);

        var resultado = await cliente.ConsultarDisponibilidad([Guid.NewGuid()]);

        Assert.Equal(EstadoDeLlamadaAlInventario.LimiteExcedido, resultado.Estado);
        Assert.Equal("INV-LIMITE-EXCEDIDO", resultado.Codigo);
    }

    [Fact]
    public async Task ConsultarDisponibilidad_UnaLecturaSeReintentaAnteUnaFallaTransitoria()
    {
        var id = Guid.NewGuid();
        var servidor = new ServidorSimulado((_, n) => Task.FromResult(n == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Json($"{{\"ediciones\":[{Disponibilidad(id)}],\"noRegistradas\":[]}}")));
        var (cliente, _) = Crear(servidor, opciones: new OpcionesDelClienteDeServicio { ReintentosMaximos = 2, RetrasoDelReintento = TimeSpan.FromMilliseconds(1) });

        var resultado = await cliente.ConsultarDisponibilidad([id]);

        Assert.True(resultado.Exitosa);
        Assert.Equal(2, servidor.Rutas.Count);
    }

    // ---------- Ingresos ----------

    [Fact]
    public async Task RegistrarIngreso_LlevaLaClaveDeIdempotenciaElTokenYLaCredencialDeUsuario()
    {
        var edicion = Guid.NewGuid();
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json(Ingreso(edicion, 7), HttpStatusCode.Created)));
        var (cliente, _) = Crear(servidor);

        var resultado = await cliente.RegistrarIngreso(new IngresoARegistrar(edicion, null, 7, "OC-1", null), Clave);

        Assert.True(resultado.Exitosa);
        Assert.Equal(7, resultado.Valor.Cantidad);
        Assert.Equal(edicion, resultado.Valor.EdicionId);
        Assert.Equal("/api/inventario/v1/ingresos-de-mercaderia", Assert.Single(servidor.Rutas));
        var cabeceras = Assert.Single(servidor.Cabeceras);
        Assert.Equal(Clave, cabeceras["Idempotency-Key"]);
        Assert.Equal(SecretoDeUsuario, cabeceras[CredencialesDeSalidaHandler.CabeceraDeCredencial]);
        Assert.Equal("Bearer " + TokenDelUsuario, cabeceras["Authorization"]);
        SinCabecerasDeActor(cabeceras);
    }

    [Fact]
    public async Task RegistrarIngreso_ConClaveDeIdempotencia_SeReintentaAunqueSeaUnPost_YElIngresoSeAplicaUnaVezPorLaClave()
    {
        var edicion = Guid.NewGuid();
        var servidor = new ServidorSimulado((_, n) => Task.FromResult(n == 1 ? new HttpResponseMessage(HttpStatusCode.BadGateway) : Json(Ingreso(edicion), HttpStatusCode.Created)));
        var (cliente, _) = Crear(servidor, opciones: new OpcionesDelClienteDeServicio { ReintentosMaximos = 2, RetrasoDelReintento = TimeSpan.FromMilliseconds(1) });

        var resultado = await cliente.RegistrarIngreso(new IngresoARegistrar(edicion, null, 5, null, null), Clave);

        Assert.True(resultado.Exitosa);
        Assert.Equal(2, servidor.Rutas.Count);
        Assert.All(servidor.Cabeceras, c => Assert.Equal(Clave, c["Idempotency-Key"]));
    }

    [Fact]
    public async Task RegistrarIngreso_ConUnaClaveInvalidaOUnaCantidadFueraDeRango_NoSale()
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json("{}", HttpStatusCode.Created)));
        var (cliente, _) = Crear(servidor);
        var edicion = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() => cliente.RegistrarIngreso(new IngresoARegistrar(edicion, null, 5, null, null), "corta"));
        await Assert.ThrowsAsync<ArgumentException>(() => cliente.RegistrarIngreso(new IngresoARegistrar(edicion, null, 5, null, null), new string('a', 81)));
        await Assert.ThrowsAsync<ArgumentException>(() => cliente.RegistrarIngreso(new IngresoARegistrar(edicion, null, 5, null, null), "con espacio y raros !!"));
        await Assert.ThrowsAsync<ArgumentException>(() => cliente.RegistrarIngreso(new IngresoARegistrar(edicion, null, 0, null, null), Clave));
        await Assert.ThrowsAsync<ArgumentException>(() => cliente.RegistrarIngreso(new IngresoARegistrar(edicion, null, 5001, null, null), Clave));
        Assert.Empty(servidor.Rutas);
    }

    [Fact]
    public async Task RegistrarIngreso_ConIngresoDeOtraEdicionOCantidadDistinta_EsNoDisponible()
    {
        var edicion = Guid.NewGuid();
        var respuestas = new Queue<string>([Ingreso(Guid.NewGuid(), 5), Ingreso(edicion, 9)]);
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json(respuestas.Dequeue(), HttpStatusCode.Created)));
        var (cliente, _) = Crear(servidor);

        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, (await cliente.RegistrarIngreso(new IngresoARegistrar(edicion, null, 5, null, null), Clave)).Estado);
        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, (await cliente.RegistrarIngreso(new IngresoARegistrar(edicion, null, 5, null, null), Clave)).Estado);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "INV-IDEMPOTENCIA-EN-CURSO")]
    [InlineData(HttpStatusCode.Conflict, "INV-IDEMPOTENCIA-EN-CONFLICTO")]
    [InlineData(HttpStatusCode.NotFound, "INV-NO-ENCONTRADO")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "INV-EDICION-NO-IMPRESA")]
    [InlineData(HttpStatusCode.Forbidden, "INV-PROHIBIDO")]
    [InlineData(HttpStatusCode.Unauthorized, "INV-NO-AUTENTICADO")]
    public async Task RegistrarIngreso_ConUnRechazo_DevuelveSuCodigoEstable(HttpStatusCode http, string codigo)
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Problema(http, codigo)));
        var (cliente, _) = Crear(servidor);

        var resultado = await cliente.RegistrarIngreso(new IngresoARegistrar(Guid.NewGuid(), null, 5, null, null), Clave);

        Assert.Equal(EstadoDeLlamadaAlInventario.Rechazada, resultado.Estado);
        Assert.Equal(codigo, resultado.Codigo);
        Assert.Equal((int)http, resultado.EstadoHttp);
    }

    [Fact]
    public async Task ListarIngresos_PideLosDeLaEdicionConElTokenYValidaQueTodosSeanDeEsaEdicion()
    {
        var edicion = Guid.NewGuid();
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json($"{{\"ingresos\":[{Ingreso(edicion, 3)},{Ingreso(edicion, 2)}]}}")));
        var (cliente, _) = Crear(servidor);

        var resultado = await cliente.ListarIngresos(edicion, 10);

        Assert.True(resultado.Exitosa);
        Assert.Equal(2, resultado.Valor.Count);
        Assert.Equal($"/api/inventario/v1/ingresos-de-mercaderia?edicionId={edicion:D}&limite=10", Assert.Single(servidor.Rutas));
        var cabeceras = Assert.Single(servidor.Cabeceras);
        Assert.Equal("Bearer " + TokenDelUsuario, cabeceras["Authorization"]);
        Assert.Equal(SecretoDeUsuario, cabeceras[CredencialesDeSalidaHandler.CabeceraDeCredencial]);
    }

    [Fact]
    public async Task ListarIngresos_ConIngresosDeOtraEdicionOMasDeLoPedido_EsNoDisponible()
    {
        var edicion = Guid.NewGuid();
        var respuestas = new Queue<string>([$"{{\"ingresos\":[{Ingreso(edicion)},{Ingreso(Guid.NewGuid())}]}}", $"{{\"ingresos\":[{Ingreso(edicion)},{Ingreso(edicion)}]}}"]);
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json(respuestas.Dequeue())));
        var (cliente, _) = Crear(servidor);

        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, (await cliente.ListarIngresos(edicion, 10)).Estado);
        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, (await cliente.ListarIngresos(edicion, 1)).Estado);
    }

    // ---------- Pendientes y bajas de un titular (T12) ----------

    [Fact]
    [Trait("Criterio", "AC-IDE-27")]
    public async Task ConsultarPendientes_LlevaElTitularEnElCuerpo_ConSuCredencialYSinToken()
    {
        var titular = Guid.NewGuid();
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json("{\"reservasActivas\":2}")));
        var (cliente, token) = Crear(servidor);

        var resultado = await cliente.ConsultarPendientes(titular);

        Assert.True(resultado.Exitosa);
        Assert.Equal(2, resultado.Valor.ReservasActivas);
        Assert.Equal(HttpMethod.Post, Assert.Single(servidor.Metodos));
        var ruta = Assert.Single(servidor.Rutas);
        Assert.Equal("/api/inventario/v1/titulares/consultas-de-pendientes", ruta);
        Assert.DoesNotContain(titular.ToString("D"), ruta, StringComparison.Ordinal);
        Assert.Contains(titular.ToString("D"), Assert.Single(servidor.Cuerpos), StringComparison.Ordinal);
        var cabeceras = Assert.Single(servidor.Cabeceras);
        Assert.Equal(SecretoDePendientes, cabeceras[CredencialesDeSalidaHandler.CabeceraDeCredencial]);
        Assert.False(cabeceras.ContainsKey("Authorization"));
        SinCabecerasDeActor(cabeceras);
        Assert.Equal(0, token.Pedidos);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-27")]
    public async Task ConsultarPendientes_ConUnConteoNegativoOInvalido_EsNoDisponible_YUnaFallaNoEsUnaExcepcion()
    {
        var respuestas = new Queue<string>(["{\"reservasActivas\":-1}", "no es json"]);
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json(respuestas.Dequeue())));
        var (cliente, _) = Crear(servidor);

        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, (await cliente.ConsultarPendientes(Guid.NewGuid())).Estado);
        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, (await cliente.ConsultarPendientes(Guid.NewGuid())).Estado);
        await Assert.ThrowsAsync<ArgumentException>(() => cliente.ConsultarPendientes(Guid.Empty));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-53")]
    public async Task DarDeBajaAlTitular_SaleConLaCredencialDeBaja_SinTokenYSinElTitularEnLaRuta()
    {
        var titular = Guid.NewGuid();
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        var (cliente, token) = Crear(servidor);

        var resultado = await cliente.DarDeBajaAlTitular(titular);

        Assert.True(resultado.Exitosa);
        Assert.Equal("/api/inventario/v1/bajas-de-titular", Assert.Single(servidor.Rutas));
        Assert.Contains(titular.ToString("D"), Assert.Single(servidor.Cuerpos), StringComparison.Ordinal);
        var cabeceras = Assert.Single(servidor.Cabeceras);
        Assert.Equal(SecretoDeBaja, cabeceras[CredencialesDeSalidaHandler.CabeceraDeCredencial]);
        Assert.False(cabeceras.ContainsKey("Authorization"));
        Assert.Equal(0, token.Pedidos);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-53")]
    public async Task DarDeBajaAlTitular_SeReintentaAnteUnaFallaTransitoria_PorqueRepetirlaNoTieneEfectoDuplicado()
    {
        var servidor = new ServidorSimulado((_, n) => Task.FromResult(new HttpResponseMessage(n == 1 ? HttpStatusCode.BadGateway : HttpStatusCode.NoContent)));
        var (cliente, _) = Crear(servidor, opciones: new OpcionesDelClienteDeServicio { ReintentosMaximos = 2, RetrasoDelReintento = TimeSpan.FromMilliseconds(1) });

        var resultado = await cliente.DarDeBajaAlTitular(Guid.NewGuid());

        Assert.True(resultado.Exitosa);
        Assert.Equal(2, servidor.Rutas.Count);
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-53")]
    [InlineData(HttpStatusCode.Unauthorized, "INV-NO-AUTENTICADO", EstadoDeLlamadaAlInventario.Rechazada)]
    [InlineData(HttpStatusCode.BadRequest, "INV-VALIDACION-INVALIDA", EstadoDeLlamadaAlInventario.Rechazada)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "INV-SERVICIO-NO-DISPONIBLE", EstadoDeLlamadaAlInventario.NoDisponible)]
    public async Task LaBajaRechazadaOCaida_DevuelveUnEstado_ParaQueIdentidadLaReintente(HttpStatusCode http, string codigo, EstadoDeLlamadaAlInventario esperado)
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Problema(http, codigo)));
        var (cliente, _) = Crear(servidor);

        var resultado = await cliente.DarDeBajaAlTitular(Guid.NewGuid());

        Assert.False(resultado.Exitosa);
        Assert.Equal(esperado, resultado.Estado);
    }

    // ---------- Sucursales ----------

    [Fact]
    public async Task ListarSucursales_UsaLaCredencialDeLecturaSinToken()
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json("{\"sucursales\":[{\"codigo\":\"lima\",\"nombre\":\"Lima\",\"region\":\"lima\"},{\"codigo\":\"cusco\",\"nombre\":\"Cusco\",\"region\":\"cusco\"}]}")));
        var (cliente, token) = Crear(servidor);

        var resultado = await cliente.ListarSucursales();

        Assert.True(resultado.Exitosa);
        Assert.Equal(["lima", "cusco"], resultado.Valor.Select(s => s.Codigo));
        Assert.Equal("/api/inventario/v1/sucursales", Assert.Single(servidor.Rutas));
        var cabeceras = Assert.Single(servidor.Cabeceras);
        Assert.Equal(SecretoDeLectura, cabeceras[CredencialesDeSalidaHandler.CabeceraDeCredencial]);
        Assert.False(cabeceras.ContainsKey("Authorization"));
        Assert.Equal(0, token.Pedidos);
    }

    [Fact]
    public async Task ListarSucursales_ConCodigosRepetidosOSinNombre_EsNoDisponible()
    {
        var respuestas = new Queue<string>(
        [
            "{\"sucursales\":[{\"codigo\":\"lima\",\"nombre\":\"Lima\",\"region\":\"lima\"},{\"codigo\":\"lima\",\"nombre\":\"Otra\",\"region\":\"lima\"}]}",
            "{\"sucursales\":[{\"codigo\":\"lima\",\"nombre\":\"\",\"region\":\"lima\"}]}",
        ]);
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json(respuestas.Dequeue())));
        var (cliente, _) = Crear(servidor);

        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, (await cliente.ListarSucursales()).Estado);
        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, (await cliente.ListarSucursales()).Estado);
    }

    // ---------- Fallas: siempre un estado, nunca una excepción ----------

    [Fact]
    public async Task UnaFallaDelInventario_NuncaEsUnaExcepcion()
    {
        var caso = 0;
        var servidor = new ServidorSimulado((_, _) =>
        {
            caso++;
            return caso switch
            {
                1 => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
                2 => Task.FromResult(Json("esto no es json")),
                3 => Task.FromResult(Json("{\"sucursales\":null}")),
                4 => throw new HttpRequestException("corte"),
                5 => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)),
                6 => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', HttpClienteDelInventario.MaximoDeBytesDeLaRespuesta + 10)) }),
                _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)),
            };
        });
        var (cliente, _) = Crear(servidor);

        for (var i = 0; i < 7; i++)
        {
            var resultado = await cliente.ListarSucursales();
            Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, resultado.Estado);
            Assert.Null(resultado.Codigo);
            Assert.Throws<InvalidOperationException>(() => resultado.Valor);
        }
    }

    [Fact]
    public async Task UnCodigoDeErrorQueNoTieneLaFormaDelContrato_NoSeCopia()
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json("{\"code\":\"<script>alert(1)</script>\",\"title\":\"x\"}", HttpStatusCode.Conflict)));
        var (cliente, _) = Crear(servidor);

        var resultado = await cliente.RegistrarEdicion(new EdicionARegistrar(Guid.NewGuid(), "impreso"));

        Assert.Equal(EstadoDeLlamadaAlInventario.Rechazada, resultado.Estado);
        Assert.Null(resultado.Codigo);
    }

    [Fact]
    public async Task ElCortacircuitosAbierto_DaNoDisponibleAlInstante()
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var (cliente, _) = Crear(servidor, opciones: new OpcionesDelClienteDeServicio { ReintentosMaximos = 0, LlamadasMinimasParaCortar = 3, DuracionDelCorte = TimeSpan.FromSeconds(30) });

        for (var i = 0; i < 6; i++)
        {
            Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, (await cliente.ListarSucursales()).Estado);
        }

        Assert.True(servidor.Rutas.Count < 6, "Con el cortacircuitos abierto las llamadas ya no salen.");
    }

    // ---------- Falla cerrada ante una respuesta que no es la del contrato (revisión de seguridad) ----------

    [Theory]
    [Trait("Criterio", "AC-IDE-57")]
    [InlineData("{}")]
    [InlineData("{\"otro\":1}")]
    [InlineData("{\"reservasActivas\":\"2\"}")]
    [InlineData("[]")]
    public async Task ConsultarPendientes_ConUnCuerpoSinElConteo_EsNoDisponible_NuncaCeroReservas(string cuerpo)
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json(cuerpo)));
        var (cliente, _) = Crear(servidor);

        var resultado = await cliente.ConsultarPendientes(Guid.NewGuid());

        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, resultado.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-53")]
    public async Task UnCodigoDeExitoQueNoEsElDelContrato_NoSeDaPorBueno()
    {
        var edicion = Guid.NewGuid();
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Ingreso(edicion), Encoding.UTF8, "application/json") }));
        var (cliente, _) = Crear(servidor);

        var baja = await cliente.DarDeBajaAlTitular(Guid.NewGuid());
        var edicionRegistrada = await cliente.RegistrarEdicion(new EdicionARegistrar(Guid.NewGuid(), "impreso"));
        var ingreso = await cliente.RegistrarIngreso(new IngresoARegistrar(edicion, null, 5, null, null), Clave);

        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, baja.Estado);
        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, edicionRegistrada.Estado);
        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, ingreso.Estado);
    }

    // ---------- Consulta previa, reservar y liberar (carril de Carrito) ----------

    private static readonly Guid Edicion1 = Guid.NewGuid();

    private static string LineaReserva(Guid lineaId, Guid edicion, int cantidad = 1) =>
        $"{{\"lineaId\":\"{lineaId}\",\"edicionId\":\"{edicion}\",\"cantidad\":{cantidad},\"estado\":\"pendiente\",\"codigoDeFuente\":\"central\",\"requiereTraslado\":false,\"plazoEstimadoDias\":null}}";

    private static string Reserva(Guid pedido, params string[] lineas) =>
        $"{{\"pedidoId\":\"{pedido}\",\"estado\":\"pendiente\",\"creadaEn\":\"{DateTimeOffset.UtcNow:O}\",\"expiraEn\":\"{DateTimeOffset.UtcNow.AddMinutes(15):O}\",\"lineas\":[{string.Join(',', lineas)}],\"traslados\":[]}}";

    private static SolicitudDeReservaDeStock SolicitudDeReserva(Guid pedido, Guid linea) =>
        new(pedido, TipoDeEntregaDeLaReserva.Recojo, "lima", null, [new LineaDeSolicitud(linea, Edicion1, 2)]);

    [Fact]
    [Trait("Criterio", "AC-CAR-04")]
    public async Task ConsultarPrevia_UsaLaCredencialDeLecturaSinToken_ReenviaElOrigen_YValidaLasLineas()
    {
        var linea = Guid.NewGuid();
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json($"{{\"lineas\":[{{\"lineaId\":\"{linea}\",\"edicionId\":\"{Edicion1}\",\"resultado\":\"disponible\",\"codigoDeFuente\":\"central\",\"requiereTraslado\":true,\"plazoEstimadoDias\":null,\"disponibleEnLaRed\":4}}],\"consultadaEn\":\"{DateTimeOffset.UtcNow:O}\"}}")));
        var origen = new OrigenDeSolicitud(HuellaDeOrigen.Crear("k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE", "v1"), "203.0.113.7");
        var (cliente, token) = Crear(servidor, origen: origen);

        var resultado = await cliente.ConsultarPrevia(new ConsultaPrevia(TipoDeEntregaDeLaReserva.Domicilio, null, "lima", [new LineaDeSolicitud(linea, Edicion1, 2)]));

        Assert.True(resultado.Exitosa);
        var seleccion = Assert.Single(resultado.Valor.Lineas);
        Assert.True(seleccion.Disponible && seleccion.RequiereTraslado);
        Assert.Null(seleccion.PlazoEstimadoDias);
        Assert.Equal("/api/inventario/v1/reservas/consulta-previa", Assert.Single(servidor.Rutas));
        var cabeceras = Assert.Single(servidor.Cabeceras);
        Assert.Equal(SecretoDeLectura, cabeceras[CredencialesDeSalidaHandler.CabeceraDeCredencial]);
        Assert.Equal("203.0.113.7", cabeceras[CredencialesDeSalidaHandler.CabeceraDeOrigenReenviado]);
        Assert.False(cabeceras.ContainsKey("Authorization"));
        Assert.Equal(0, token.Pedidos);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-04")]
    public async Task ConsultarPrevia_ConUnaLineaQueNoSePidio_OUnResultadoInventado_EsNoDisponible()
    {
        var linea = Guid.NewGuid();
        var respuestas = new Queue<string>(
        [
            $"{{\"lineas\":[{{\"lineaId\":\"{Guid.NewGuid()}\",\"edicionId\":\"{Edicion1}\",\"resultado\":\"disponible\",\"codigoDeFuente\":\"c\",\"requiereTraslado\":false,\"plazoEstimadoDias\":null,\"disponibleEnLaRed\":1}}],\"consultadaEn\":\"{DateTimeOffset.UtcNow:O}\"}}",
            $"{{\"lineas\":[{{\"lineaId\":\"{linea}\",\"edicionId\":\"{Edicion1}\",\"resultado\":\"quizas\",\"codigoDeFuente\":\"c\",\"requiereTraslado\":false,\"plazoEstimadoDias\":null,\"disponibleEnLaRed\":1}}],\"consultadaEn\":\"{DateTimeOffset.UtcNow:O}\"}}",
        ]);
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json(respuestas.Dequeue())));
        var (cliente, _) = Crear(servidor);
        var consulta = new ConsultaPrevia(TipoDeEntregaDeLaReserva.Recojo, "lima", null, [new LineaDeSolicitud(linea, Edicion1, 1)]);

        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, (await cliente.ConsultarPrevia(consulta)).Estado);
        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, (await cliente.ConsultarPrevia(consulta)).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-12")]
    public async Task Reservar_LlevaLaClaveElTokenYLaCredencialDeUsuario_YDevuelveLaReservaValidada()
    {
        var pedido = Guid.NewGuid();
        var linea = Guid.NewGuid();
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json(Reserva(pedido, LineaReserva(linea, Edicion1, 2)), HttpStatusCode.Created)));
        var (cliente, _) = Crear(servidor);

        var resultado = await cliente.Reservar(SolicitudDeReserva(pedido, linea), Clave);

        Assert.True(resultado.Exitosa);
        Assert.Equal(pedido, resultado.Valor.PedidoId);
        Assert.Equal("pendiente", resultado.Valor.Estado);
        Assert.Equal("central", Assert.Single(resultado.Valor.Lineas).CodigoDeFuente);
        Assert.Equal("/api/inventario/v1/reservas", Assert.Single(servidor.Rutas));
        var cabeceras = Assert.Single(servidor.Cabeceras);
        Assert.Equal(Clave, cabeceras["Idempotency-Key"]);
        Assert.Equal(SecretoDeUsuario, cabeceras[CredencialesDeSalidaHandler.CabeceraDeCredencial]);
        Assert.Equal("Bearer " + TokenDelUsuario, cabeceras["Authorization"]);
        SinCabecerasDeActor(cabeceras);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-12")]
    public async Task Reservar_ConUnaReservaDeOtroPedidoOConLineasAjenas_EsNoDisponible()
    {
        var pedido = Guid.NewGuid();
        var linea = Guid.NewGuid();
        var respuestas = new Queue<string>([Reserva(Guid.NewGuid(), LineaReserva(linea, Edicion1)), Reserva(pedido, LineaReserva(Guid.NewGuid(), Edicion1))]);
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json(respuestas.Dequeue(), HttpStatusCode.Created)));
        var (cliente, _) = Crear(servidor);

        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, (await cliente.Reservar(SolicitudDeReserva(pedido, linea), Clave)).Estado);
        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, (await cliente.Reservar(SolicitudDeReserva(pedido, linea), Clave)).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-04")]
    public async Task Reservar_ConUn409DeStock_TraeLasLineasSinStock()
    {
        var linea = Guid.NewGuid();
        var cuerpo = $"{{\"type\":\"about:blank\",\"title\":\"x\",\"status\":409,\"detail\":\"d\",\"code\":\"INV-STOCK-INSUFICIENTE\",\"traceId\":\"t\",\"lineas\":[{{\"lineaId\":\"{linea}\",\"edicionId\":\"{Edicion1}\",\"solicitada\":2,\"disponibleEnLaRed\":1}}]}}";
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/problem+json") }));
        var (cliente, _) = Crear(servidor);

        var resultado = await cliente.Reservar(SolicitudDeReserva(Guid.NewGuid(), linea), Clave);

        Assert.Equal(EstadoDeLlamadaAlInventario.Rechazada, resultado.Estado);
        Assert.Equal("INV-STOCK-INSUFICIENTE", resultado.Codigo);
        var sin = Assert.Single(resultado.LineasSinStock!);
        Assert.Equal((linea, 2, 1), (sin.LineaId, sin.Solicitada, sin.DisponibleEnLaRed));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-12")]
    public async Task Reservar_Con429_TraeElMotivoLaEsperaYElInstanteDeReintento()
    {
        var disponible = DateTimeOffset.UtcNow.AddMinutes(10);
        var cuerpo = $"{{\"code\":\"INV-LIMITE-EXCEDIDO\",\"status\":429,\"motivo\":\"reservas-por-hora-por-cliente\",\"retryAfterSeconds\":600,\"disponibleDesde\":\"{disponible:O}\"}}";
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/problem+json") }));
        var (cliente, _) = Crear(servidor);

        var resultado = await cliente.Reservar(SolicitudDeReserva(Guid.NewGuid(), Guid.NewGuid()), Clave);

        Assert.Equal(EstadoDeLlamadaAlInventario.LimiteExcedido, resultado.Estado);
        Assert.Equal(("reservas-por-hora-por-cliente", 600), (resultado.Limite!.Motivo, resultado.Limite.EsperaEnSegundos));
        Assert.Equal(disponible.ToUnixTimeSeconds(), resultado.Limite.DisponibleDesde.ToUnixTimeSeconds());
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-12")]
    public async Task Reservar_ConArgumentosQueElContratoRechaza_NoSale()
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json("{}", HttpStatusCode.Created)));
        var (cliente, _) = Crear(servidor);
        var pedido = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() => cliente.Reservar(SolicitudDeReserva(pedido, Guid.NewGuid()), "corta"));
        await Assert.ThrowsAsync<ArgumentException>(() => cliente.Reservar(new SolicitudDeReservaDeStock(pedido, TipoDeEntregaDeLaReserva.Recojo, "lima", "lima", [new LineaDeSolicitud(Guid.NewGuid(), Edicion1, 1)]), Clave));
        await Assert.ThrowsAsync<ArgumentException>(() => cliente.Reservar(new SolicitudDeReservaDeStock(pedido, TipoDeEntregaDeLaReserva.Recojo, "lima", null, []), Clave));
        await Assert.ThrowsAsync<ArgumentException>(() => cliente.Reservar(new SolicitudDeReservaDeStock(pedido, TipoDeEntregaDeLaReserva.Recojo, "lima", null, [new LineaDeSolicitud(Guid.NewGuid(), Edicion1, 0)]), Clave));
        Assert.Empty(servidor.Rutas);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-14")]
    public async Task Liberar_UsaLaCredencialDeExpiracionSinToken_SeReintenta_YValidaElMotivo()
    {
        var pedido = Guid.NewGuid();
        var servidor = new ServidorSimulado((_, n) => Task.FromResult(n == 1 ? new HttpResponseMessage(HttpStatusCode.BadGateway) : Json(Reserva(pedido).Replace("\"pendiente\"", "\"liberada\"", StringComparison.Ordinal))));
        var (cliente, token) = Crear(servidor, opciones: new OpcionesDelClienteDeServicio { ReintentosMaximos = 2, RetrasoDelReintento = TimeSpan.FromMilliseconds(1) });

        var resultado = await cliente.Liberar(pedido, "checkout-cancelado");
        await Assert.ThrowsAsync<ArgumentException>(() => cliente.Liberar(pedido, "motivo-inventado"));

        Assert.True(resultado.Exitosa);
        Assert.Equal("liberada", resultado.Valor.Estado);
        Assert.Equal(2, servidor.Rutas.Count);
        Assert.All(servidor.Rutas, r => Assert.Equal($"/api/inventario/v1/reservas/{pedido:D}/liberar", r));
        Assert.All(servidor.Cabeceras, c => Assert.False(c.ContainsKey("Authorization")));
        Assert.Equal(SecretoDeExpiracion, servidor.Cabeceras[0][CredencialesDeSalidaHandler.CabeceraDeCredencial]);
        Assert.Equal(0, token.Pedidos);
    }

    // ---------- Consultar, extender y confirmar una reserva (PLA-Y; SEL-03, SEL-04, SEL-22, SEL-30) ----------

    [Fact]
    public async Task ConsultarReserva_LlevaElTokenYLaCredencialDeUsuario_SinClave_YDevuelveLaReservaValidada()
    {
        var pedido = Guid.NewGuid();
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json(Reserva(pedido, LineaReserva(Guid.NewGuid(), Edicion1, 2)))));
        var (cliente, _) = Crear(servidor);

        var resultado = await cliente.ConsultarReserva(pedido);

        Assert.True(resultado.Exitosa);
        Assert.Equal(pedido, resultado.Valor.PedidoId);
        Assert.Equal(HttpMethod.Get, Assert.Single(servidor.Metodos));
        Assert.Equal($"/api/inventario/v1/reservas/{pedido:D}", Assert.Single(servidor.Rutas));
        var cabeceras = Assert.Single(servidor.Cabeceras);
        Assert.False(cabeceras.ContainsKey("Idempotency-Key"));
        Assert.Equal(SecretoDeUsuario, cabeceras[CredencialesDeSalidaHandler.CabeceraDeCredencial]);
        Assert.Equal("Bearer " + TokenDelUsuario, cabeceras["Authorization"]);
        SinCabecerasDeActor(cabeceras);
    }

    [Theory]
    [InlineData(404, "INV-NO-ENCONTRADO")]
    [InlineData(403, "INV-PROHIBIDO")]
    public async Task ConsultarReserva_ConUnRechazoDelInventario_TraeElCodigo_SinExcepcion(int estado, string codigo)
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Problema((HttpStatusCode)estado, codigo)));
        var (cliente, _) = Crear(servidor);

        var resultado = await cliente.ConsultarReserva(Guid.NewGuid());

        Assert.Equal(EstadoDeLlamadaAlInventario.Rechazada, resultado.Estado);
        Assert.Equal(codigo, resultado.Codigo);
    }

    [Fact]
    public async Task ConsultarReserva_ConUnaReservaDeOtroPedido_EsNoDisponible_YSinPedidoNoSale()
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json(Reserva(Guid.NewGuid()))));
        var (cliente, _) = Crear(servidor);

        var resultado = await cliente.ConsultarReserva(Guid.NewGuid());

        Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, resultado.Estado);
        await Assert.ThrowsAsync<ArgumentException>(() => cliente.ConsultarReserva(Guid.Empty));
        Assert.Single(servidor.Rutas);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Criterio", "AC-SEL-24")]
    public async Task ExtenderReserva_UsaLaCredencialDeExpiracionSinTokenNiCuerpo_SeReintenta_YDevuelveSiSeExtendio(bool extendida)
    {
        var pedido = Guid.NewGuid();
        var cuerpo = $"{{\"extendida\":{(extendida ? "true" : "false")},\"reserva\":{Reserva(pedido, LineaReserva(Guid.NewGuid(), Edicion1, 1))}}}";
        var servidor = new ServidorSimulado((_, n) => Task.FromResult(n == 1 ? new HttpResponseMessage(HttpStatusCode.BadGateway) : Json(cuerpo)));
        var (cliente, token) = Crear(servidor, opciones: new OpcionesDelClienteDeServicio { ReintentosMaximos = 2, RetrasoDelReintento = TimeSpan.FromMilliseconds(1) });

        var resultado = await cliente.ExtenderReserva(pedido);

        Assert.True(resultado.Exitosa);
        Assert.Equal(extendida, resultado.Valor.Extendida);
        Assert.Equal(pedido, resultado.Valor.Reserva.PedidoId);
        Assert.Equal(2, servidor.Rutas.Count);
        Assert.All(servidor.Rutas, r => Assert.Equal($"/api/inventario/v1/reservas/{pedido:D}/extender", r));
        Assert.All(servidor.Metodos, m => Assert.Equal(HttpMethod.Post, m));
        Assert.All(servidor.Cabeceras, c => Assert.False(c.ContainsKey("Authorization")));
        Assert.Equal(SecretoDeExpiracion, servidor.Cabeceras[0][CredencialesDeSalidaHandler.CabeceraDeCredencial]);
        Assert.All(servidor.Cuerpos, c => Assert.True(string.IsNullOrEmpty(c)));
        Assert.Equal(0, token.Pedidos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-24")]
    public async Task ExtenderReserva_ConUnaRespuestaQueNoEsLaDelPedido_OSinReserva_EsNoDisponible()
    {
        var pedido = Guid.NewGuid();
        var respuestas = new Queue<string>([$"{{\"extendida\":true,\"reserva\":{Reserva(Guid.NewGuid())}}}", "{\"extendida\":true}", "no es json"]);
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json(respuestas.Dequeue())));
        var (cliente, _) = Crear(servidor);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(EstadoDeLlamadaAlInventario.NoDisponible, (await cliente.ExtenderReserva(pedido)).Estado);
        }

        await Assert.ThrowsAsync<ArgumentException>(() => cliente.ExtenderReserva(Guid.Empty));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-03")]
    public async Task ConfirmarReserva_LlevaLaClaveYLaCredencialDeExpiracion_SinToken_YDevuelveLaReserva()
    {
        var pedido = Guid.NewGuid();
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Json(Reserva(pedido, LineaReserva(Guid.NewGuid(), Edicion1, 1)).Replace("\"pendiente\"", "\"confirmada\"", StringComparison.Ordinal))));
        var (cliente, token) = Crear(servidor);

        var resultado = await cliente.ConfirmarReserva(pedido, Clave);

        Assert.True(resultado.Exitosa);
        Assert.Equal("confirmada", resultado.Valor.Estado);
        Assert.Equal(HttpMethod.Post, Assert.Single(servidor.Metodos));
        Assert.Equal($"/api/inventario/v1/reservas/{pedido:D}/confirmar", Assert.Single(servidor.Rutas));
        var cabeceras = Assert.Single(servidor.Cabeceras);
        Assert.Equal(Clave, cabeceras["Idempotency-Key"]);
        Assert.Equal(SecretoDeExpiracion, cabeceras[CredencialesDeSalidaHandler.CabeceraDeCredencial]);
        Assert.False(cabeceras.ContainsKey("Authorization"));
        Assert.Equal(0, token.Pedidos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-03")]
    public async Task ConfirmarReserva_ConUnEstadoInvalido_TraeElCodigo_YSinClaveValidaNoSale()
    {
        var servidor = new ServidorSimulado((_, _) => Task.FromResult(Problema(HttpStatusCode.Conflict, "INV-RESERVA-ESTADO-INVALIDO")));
        var (cliente, _) = Crear(servidor);

        var resultado = await cliente.ConfirmarReserva(Guid.NewGuid(), Clave);

        Assert.Equal(EstadoDeLlamadaAlInventario.Rechazada, resultado.Estado);
        Assert.Equal("INV-RESERVA-ESTADO-INVALIDO", resultado.Codigo);
        await Assert.ThrowsAsync<ArgumentException>(() => cliente.ConfirmarReserva(Guid.NewGuid(), "corta"));
        await Assert.ThrowsAsync<ArgumentException>(() => cliente.ConfirmarReserva(Guid.Empty, Clave));
        Assert.Single(servidor.Rutas);
    }

    [Fact]
    public void ElNombreDelClienteEsElQueRecibeElTokenDeLaSesion() =>
        Assert.Equal(TokenDelUsuarioDeLaSesionExtensiones.ClienteDelInventario, HttpClienteDelInventario.NombreDelCliente);
}

internal static class RegistroDeManejadoresDePrueba
{
    /// <summary>El manejador del token de la sesión va primero en la cadena del cliente del Inventario, como lo deja la Api (<c>AgregarTokenDelUsuarioDeLaSesion</c>).</summary>
    public static IServiceCollection TryAddTransientDelToken(this IServiceCollection servicios)
    {
        servicios.AddTransient<TokenDelUsuarioDeSalidaHandler>();
        servicios.AddHttpClient(HttpClienteDelInventario.NombreDelCliente)
            .ConfigureAdditionalHttpMessageHandlers((manejadores, proveedor) => manejadores.Insert(0, proveedor.GetRequiredService<TokenDelUsuarioDeSalidaHandler>()));
        return servicios;
    }
}
