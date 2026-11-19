#if DEBUG
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.IntegrationTests.Api;
using BabelHub.IntegrationTests.Despliegue;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// El sembrador de sesiones de desarrollo (pieza del carril A para los recorridos E2E de B; AC-WEB-04): contra una base de datos migrada de verdad, <c>POST /desarrollo/sesion</c> crea una cuenta, la hace ingresar por el camino real
/// (con el segundo factor del personal) y devuelve el <c>storageState</c> de Playwright con la cookie de la sesión web, que la sesión acepta; y los cuatro cerrojos que lo hacen imposible fuera de desarrollo (compilación Debug,
/// entorno Development, conexión de la propia máquina y cabecera propia) más el rechazo de lo que no corresponde.
/// </summary>
public sealed class SembradorDeSesionesTests(InstanciaParaMigrar instancia) : IClassFixture<InstanciaParaMigrar>
{
    /// <summary>La clave aleatoria del sembrador de esta ejecución (nunca un valor fijo en el repositorio).</summary>
    private static readonly string Clave = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));

    private static string Secreto() => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(36));

    private sealed record Servicio(WebApplication App, HttpClient Cliente, string Propietaria) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Cliente.Dispose();
            await App.DisposeAsync();
        }
    }

    private async Task<Servicio> Levantar(string host, string entorno = "Development", bool loopback = true, bool conClave = true)
    {
        var propietaria = await instancia.NuevaBase();
        var baseDeDatos = new NpgsqlConnectionStringBuilder(propietaria).Database!;
        var configuracion = AnfitrionDelEcommerceTests.ConfiguracionCompleta();
        var claves = new Dictionary<string, (string Usuario, string Clave)>
        {
            [OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeLaAplicacion] = ("ecommerce_sembrador", Secreto()),
            [OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeDepuracionDeAuditoria] = ("ecommerce_sembrador_aud", Secreto()),
            [OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeDepuracionDeRegistros] = ("ecommerce_sembrador_reg", Secreto()),
        };
        foreach (var (ruta, (usuario, clave)) in claves)
        {
            configuracion[ruta] = instancia.Cadena(baseDeDatos, usuario, clave);
        }

        configuracion["Web:Host"] = host;
        configuracion["Web:ClaveDeSesionesGestion"] = Secreto();
        if (conClave)
        {
            configuracion[SembradorDeSesionesEnLaApi.ClaveDeConfiguracion] = Clave;
        }


        var migracion = new Dictionary<string, string?> { ["Migrador:Ecommerce:Propietaria"] = propietaria };
        foreach (var (ruta, valor) in configuracion.Where(p => p.Key.StartsWith("Ecommerce:Base:", StringComparison.Ordinal)))
        {
            migracion[ruta] = valor;
        }

        var salida = new StringWriter();
        var error = new StringWriter();
        var codigo = await ModoMigrar.Ejecutar(new ConfigurationBuilder().AddInMemoryCollection(migracion).Build(), salida, error);
        Assert.True(codigo == 0, error.ToString());
        // Los textos legales vigentes los siembra la operación (la aplicación solo los lee): sin esa fila no se registra ni se ingresa nadie.
        await using (var administracion = NpgsqlDataSource.Create(propietaria))
        {
            await using var texto = administracion.CreateCommand("INSERT INTO identidad.versiones_de_textos (id, terminos, privacidad, reembolso) VALUES (1, 't1', 'p1', 'r1') ON CONFLICT DO NOTHING");
            await texto.ExecuteNonQueryAsync();
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = entorno });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(configuracion);
        builder.Services.AddControllersWithViews();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(typeof(ModoDeArranque), ModoDeArranque.Ensayo);
        builder.AgregarEcommerce(ModoDeArranque.Ensayo);
        var app = builder.Build();
        if (loopback)
        {
            app.Use(async (contexto, siguiente) =>
            {
                contexto.Connection.RemoteIpAddress = IPAddress.Loopback;
                contexto.Connection.LocalIpAddress = IPAddress.Loopback;
                await siguiente(contexto);
            });
        }

        app.UsarEcommerce();
        await app.StartAsync();
        var cliente = app.GetTestClient();
        cliente.BaseAddress = new Uri("https://localhost");
        return new Servicio(app, cliente, propietaria);
    }

    private static async Task<HttpResponseMessage> Pedir(HttpClient cliente, object cuerpo, bool conCabecera = true, string? clave = null, Action<HttpRequestMessage>? ajustar = null)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, SembradorDeSesionesEnLaApi.Ruta) { Content = JsonContent.Create(cuerpo) };
        if (conCabecera)
        {
            peticion.Headers.TryAddWithoutValidation(SembradorDeSesionesEnLaApi.CabeceraDeAutorizacion, clave ?? Clave);
        }

        ajustar?.Invoke(peticion);
        return await cliente.SendAsync(peticion);
    }

    private static async Task<(string Nombre, string Valor, JsonElement Cookie)> Cookie(HttpResponseMessage respuesta)
    {
        using var documento = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        var cookie = documento.RootElement.GetProperty("cookies")[0].Clone();
        return (cookie.GetProperty("name").GetString()!, cookie.GetProperty("value").GetString()!, cookie);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-04")]
    public async Task UnCliente_Sembrado_RecibeElStorageStateConUnaCookieQueLaSesionAcepta()
    {
        await using var servicio = await Levantar("tienda");

        var respuesta = await Pedir(servicio.Cliente, new { rol = "cliente" });

        Assert.True(respuesta.StatusCode == HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync());
        var (nombre, valor, cookie) = await Cookie(respuesta);
        Assert.Equal("__Host-bh-tienda", nombre);
        Assert.Equal("localhost", cookie.GetProperty("domain").GetString());
        Assert.True(cookie.GetProperty("httpOnly").GetBoolean());
        Assert.True(cookie.GetProperty("secure").GetBoolean());
        Assert.Equal("Lax", cookie.GetProperty("sameSite").GetString());
        Assert.Equal("no-store", respuesta.Headers.CacheControl?.ToString());

        // La sesión que respalda la cookie es la de un cliente verificado: la web la carga como cualquier otra.
        using var alcance = servicio.App.Services.CreateScope();
        var sesiones = alcance.ServiceProvider.GetRequiredService<ISesionesWeb>();
        var carga = await sesiones.Cargar(valor, HostWeb.Tienda, OrigenDeLaAccion.AccionDelUsuario);
        var usuario = Guid.Parse(respuesta.Headers.GetValues("X-Usuario-Id").Single());
        Assert.Equal(EstadoDeCarga.Valida, carga.Estado);
        Assert.Equal(usuario, carga.UsuarioId);
        var credencial = await sesiones.AsegurarCredencial(valor, HostWeb.Tienda);
        Assert.Equal(EstadoDeCredencial.Asegurada, credencial.Estado);
        Assert.Equal(RolWeb.Cliente, credencial.Actor!.Rol);
        var estado = await alcance.ServiceProvider.GetRequiredService<IEstadoDeCuenta>().Consultar(usuario);
        Assert.True(estado!.PuedeComprar);
        Assert.True(estado.CorreoVerificado);
    }

    [Theory]
    [InlineData("administrador", RolWeb.AdministradorCentral, null)]
    [InlineData("logistica", RolWeb.Logistica, null)]
    [InlineData("encargado", RolWeb.Encargado, "ayacucho")]
    [Trait("Criterio", "AC-WEB-04")]
    public async Task ElPersonal_Sembrado_IngresaPorElSegundoFactorReal_YLaSesionEsDeLaGestion(string rol, RolWeb esperado, string? sucursal)
    {
        await using var servicio = await Levantar("gestion");

        var respuesta = await Pedir(servicio.Cliente, new { rol, sucursal });

        Assert.True(respuesta.StatusCode == HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync());
        var (nombre, valor, _) = await Cookie(respuesta);
        Assert.Equal("__Host-bh-gestion", nombre);
        using var alcance = servicio.App.Services.CreateScope();
        var sesiones = alcance.ServiceProvider.GetRequiredService<ISesionesWeb>();
        Assert.Equal(EstadoDeCarga.Valida, (await sesiones.Cargar(valor, HostWeb.Gestion, OrigenDeLaAccion.AccionDelUsuario)).Estado);
        var credencial = await sesiones.AsegurarCredencial(valor, HostWeb.Gestion);
        Assert.Equal(EstadoDeCredencial.Asegurada, credencial.Estado);
        Assert.Equal(esperado, credencial.Actor!.Rol);
        Assert.Equal(sucursal is null ? null : IdentificadorDeSucursal.DesdeCodigo(sucursal), credencial.Actor.SucursalId);
        var usuario = Guid.Parse(respuesta.Headers.GetValues("X-Usuario-Id").Single());
        await using var origen = NpgsqlDataSource.Create(servicio.Propietaria);
        await using var consulta = origen.CreateCommand($"SELECT sucursal, correo_verificado FROM identidad.usuarios WHERE id = '{usuario}'");
        await using var filas = await consulta.ExecuteReaderAsync();
        Assert.True(await filas.ReadAsync());
        Assert.True(filas.GetBoolean(1));
        Assert.Equal(sucursal is null ? null : IdentificadorDeSucursal.DesdeCodigo(sucursal), filas.IsDBNull(0) ? null : filas.GetGuid(0));
    }

    [Theory]
    [InlineData("cliente", "gestion")]
    [InlineData("administrador", "tienda")]
    [InlineData("encargado", "gestion")]
    [InlineData("jefe", "tienda")]
    [Trait("Criterio", "AC-WEB-04")]
    public async Task UnRolQueNoCorrespondeAlHost_OUnEncargadoSinSucursalValida_SeRechaza(string rol, string host)
    {
        await using var servicio = await Levantar(host);

        var respuesta = await Pedir(servicio.Cliente, new { rol, sucursal = rol == "encargado" ? "lima central" : null });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.DoesNotContain("__Host-", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-04")]
    public async Task SinLaCabeceraPropia_OSinSerDeLaPropiaMaquina_NoExiste()
    {
        await using var servicio = await Levantar("tienda");
        await using var remoto = await Levantar("tienda", loopback: false);

        var sinCabecera = await Pedir(servicio.Cliente, new { rol = "cliente" }, conCabecera: false);
        var deOtraMaquina = await Pedir(remoto.Cliente, new { rol = "cliente" });

        Assert.Equal(HttpStatusCode.NotFound, sinCabecera.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deOtraMaquina.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-04")]
    public async Task UnaClaveErronea_UnHostAjeno_UnaCabeceraDeReenvio_OSinClaveConfigurada_NoExiste()
    {
        await using var servicio = await Levantar("tienda");
        await using var sinClave = await Levantar("tienda", conClave: false);

        var claveErronea = await Pedir(servicio.Cliente, new { rol = "cliente" }, clave: Clave + "x");
        var claveCorta = await Pedir(servicio.Cliente, new { rol = "cliente" }, clave: "1");

        // Un túnel o un ataque de reasignación de DNS llega con otro nombre de host; un proxy local, con cabeceras de reenvío.
        using var hostAjeno = servicio.App.GetTestClient();
        hostAjeno.BaseAddress = new Uri("https://sitio-del-atacante.example");
        var deOtroHost = await Pedir(hostAjeno, new { rol = "cliente" });
        var reenviada = await Pedir(servicio.Cliente, new { rol = "cliente" }, ajustar: p => p.Headers.TryAddWithoutValidation("Via", "1.1 proxy-local"));
        var sinClaveConfigurada = await Pedir(sinClave.Cliente, new { rol = "cliente" });

        // Un host que el filtro de hosts del anfitrión no admite responde 400 antes de llegar al endpoint (lo mismo que cualquier ruta): tampoco delata nada.
        Assert.Contains(deOtroHost.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.BadRequest });
        Assert.DoesNotContain("__Host-", await deOtroHost.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        foreach (var respuesta in new[] { claveErronea, claveCorta, reenviada, sinClaveConfigurada })
        {
            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
            Assert.DoesNotContain("__Host-", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-04")]
    public async Task UnaLlamadaNoAutorizada_ConUnCuerpoRoto_RespondeLoMismoQueSinCuerpo_YUnaAutorizadaConUnCuerpoRoto_Es400()
    {
        await using var servicio = await Levantar("tienda");

        using var sinPermiso = new HttpRequestMessage(HttpMethod.Post, SembradorDeSesionesEnLaApi.Ruta) { Content = new StringContent("{ esto no es json", Encoding.UTF8, "text/plain") };
        using var conPermiso = new HttpRequestMessage(HttpMethod.Post, SembradorDeSesionesEnLaApi.Ruta) { Content = new StringContent("{ esto no es json", Encoding.UTF8, "application/json") };
        conPermiso.Headers.TryAddWithoutValidation(SembradorDeSesionesEnLaApi.CabeceraDeAutorizacion, Clave);

        // Sin la clave no se delata el endpoint con un 415 o un 400 del binding: es el mismo 404 que si no existiera.
        Assert.Equal(HttpStatusCode.NotFound, (await servicio.Cliente.SendAsync(sinPermiso)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await servicio.Cliente.SendAsync(conPermiso)).StatusCode);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Test")]
    [Trait("Criterio", "AC-WEB-04")]
    public async Task FueraDeDevelopment_ElEndpointNoSeMapea(string entorno)
    {
        await using var servicio = await Levantar("tienda", entorno);

        var respuesta = await Pedir(servicio.Cliente, new { rol = "cliente" });

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Null(servicio.App.Services.GetService<BabelHub.Ecommerce.Infrastructure.Composicion.SembradorDeSesionesDeDesarrollo>());
    }
}
#endif
