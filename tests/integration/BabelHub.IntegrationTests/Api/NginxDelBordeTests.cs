using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace BabelHub.IntegrationTests.Api;

/// <summary>
/// Prueba el <c>infra/nginx/nginx.conf</c> real: un Nginx con esa configuración delante de un servidor que devuelve las cabeceras que le
/// llegan. Las solicitudes salen de un contenedor de la misma red, así que la conexión viene de un rango de confianza (como el túnel).
/// </summary>
public sealed class NginxDelBordeTests : IAsyncLifetime
{
    private const string ConfiguracionDelServidorDeEco = """
        events {}
        http {
            server {
                listen 8080;
                location / {
                    default_type text/plain;
                    return 200 "origen=[$http_x_origen_cliente];xff=[$http_x_forwarded_for];fwd=[$http_forwarded];real=[$http_x_real_ip];servicio=[$http_x_credencial_de_servicio];reenviado=[$http_x_origen_reenviado];credorigen=[$http_x_credencial_de_origen]";
                }
            }
        }
        """;

    private INetwork? _red;
    private IContainer? _eco;
    private IContainer? _borde;

    public async Task InitializeAsync()
    {
        _red = new NetworkBuilder().Build();
        await _red.CreateAsync();

        _eco = new ContainerBuilder("nginx:1.27-alpine")
            .WithNetwork(_red)
            .WithNetworkAliases("ecommerce", "ecommerce-gestion")
            .WithResourceMapping(Encoding.UTF8.GetBytes(ConfiguracionDelServidorDeEco), "/etc/nginx/nginx.conf")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("wget", "-qO-", "-T", "2", "http://127.0.0.1:8080/"))
            .Build();
        await _eco.StartAsync();

        _borde = new ContainerBuilder("nginx:1.27-alpine")
            .WithNetwork(_red)
            .WithNetworkAliases("borde")
            .WithResourceMapping(await File.ReadAllBytesAsync(Path.Combine(BuscarRaiz(), "infra", "nginx", "nginx.conf")), "/etc/nginx/nginx.conf")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("wget", "-qO-", "-T", "2", "http://127.0.0.1:8080/health"))
            .Build();
        await _borde.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_borde is not null)
        {
            await _borde.DisposeAsync();
        }

        if (_eco is not null)
        {
            await _eco.DisposeAsync();
        }

        if (_red is not null)
        {
            await _red.DeleteAsync();
        }
    }

    /// <summary>Hace una solicitud al borde desde un contenedor de la misma red, con las cabeceras dadas, y devuelve lo que vio el servidor de eco.</summary>
    private async Task<Dictionary<string, string>> Pedir(params string[] cabeceras)
    {
        var argumentos = new List<string> { "wget", "-qO-", "-T", "5" };
        argumentos.AddRange(cabeceras.Select(c => "--header=" + c));
        argumentos.Add("http://borde:8080/cualquier-ruta");

        var resultado = await _eco!.ExecAsync(argumentos);
        Assert.True(resultado.ExitCode == 0, resultado.Stderr + resultado.Stdout);

        return resultado.Stdout.Trim().Split(';')
            .Select(par => par.Split('=', 2))
            .ToDictionary(p => p[0], p => p[1].Trim('[', ']'));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task ConLaDireccionDelClienteEntregadaPorElTunel_ElBordeLaFijaComoXOrigenCliente()
    {
        var visto = await Pedir("CF-Connecting-IP: 203.0.113.7");

        Assert.Equal("203.0.113.7", visto["origen"]);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task ConUnaDireccionIPv6_LaFijaTalCual()
    {
        var visto = await Pedir("CF-Connecting-IP: 2001:db8:85a3:1::1");

        Assert.Equal("2001:db8:85a3:1::1", visto["origen"]);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task LasCabecerasDeReenvioYDeOrigenDelCliente_NuncaLleganALaAplicacion()
    {
        var visto = await Pedir(
            "CF-Connecting-IP: 203.0.113.7",
            "X-Origen-Cliente: 198.51.100.99",
            "X-Forwarded-For: 198.51.100.99",
            "Forwarded: for=198.51.100.99",
            "X-Real-IP: 198.51.100.99",
            "X-Origen-Reenviado: 198.51.100.99",
            "X-Credencial-De-Origen: credencial-falsa",
            "X-Credencial-De-Servicio: credencial-falsa");

        Assert.Equal("203.0.113.7", visto["origen"]);
        Assert.Equal(string.Empty, visto["xff"]);
        Assert.Equal(string.Empty, visto["fwd"]);
        Assert.Equal(string.Empty, visto["real"]);
        Assert.Equal(string.Empty, visto["reenviado"]);
        Assert.Equal(string.Empty, visto["credorigen"]);
        Assert.Equal(string.Empty, visto["servicio"]);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task SinCfConnectingIpAunqueElClienteEnvieXOrigenCliente_NoSeFijaNingunOrigen()
    {
        var visto = await Pedir("X-Origen-Cliente: 198.51.100.99", "X-Forwarded-For: 198.51.100.99");

        Assert.Equal(string.Empty, visto["origen"]);
        Assert.Equal(string.Empty, visto["xff"]);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-20")]
    [InlineData("no-es-una-ip")]
    [InlineData("203.0.113.7, 198.51.100.1")]
    [InlineData("203.0.113.7 198.51.100.1")]
    [InlineData("<script>")]
    [InlineData("203.0.113.7:8080")]
    public async Task ConUnaCfConnectingIpInvalidaOConVariasDirecciones_NoSeFijaNingunOrigen(string valor)
    {
        var visto = await Pedir("CF-Connecting-IP: " + valor);

        Assert.Equal(string.Empty, visto["origen"]);
    }

    private static string BuscarRaiz()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null && !File.Exists(Path.Combine(directorio.FullName, "AGENTS.md")))
        {
            directorio = directorio.Parent;
        }

        return directorio?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }
}
