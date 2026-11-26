using System.Text;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace BabelHub.IntegrationTests.Api;

/// <summary>
/// Prueba el <c>infra/nginx/nginx.conf</c> real: un Nginx con esa configuración delante de un servidor que devuelve las cabeceras que le
/// llegan. Un contenedor hace de cloudflared (con la dirección fija en la que Nginx confía) y otro de cualquier otro equipo de la red.
/// Lo único que se cambia del archivo es esa dirección, por una de una red de prueba que no choque con la de un ensayo en marcha; que sea la de
/// cloudflared en el Compose lo comprueba <c>ComposePorVmTests</c>.
/// </summary>
public sealed class NginxDelBordeTests : IAsyncLifetime
{
    private const string DireccionDeCloudflaredEnElArchivo = "10.213.10.2";
    private const string DireccionDeCloudflaredEnLaPrueba = "10.214.10.2";

    private const string ConfiguracionDelServidorDeEco = """
        events {}
        http {
            server {
                listen 8080;
                listen 8081;
                location / {
                    default_type text/plain;
                    return 200 "origen=[$http_x_origen_cliente];xff=[$http_x_forwarded_for];fwd=[$http_forwarded];real=[$http_x_real_ip];servicio=[$http_x_credencial_de_servicio];reenviado=[$http_x_origen_reenviado];credorigen=[$http_x_credencial_de_origen];puerto=[$server_port]";
                }
            }
        }
        """;

    private INetwork? _red;
    private IContainer? _eco;
    private IContainer? _borde;
    private IContainer? _tunel;

    public async Task InitializeAsync()
    {
        _red = new NetworkBuilder()
            .WithCreateParameterModifier(p => p.IPAM = new IPAM { Config = [new IPAMConfig { Subnet = "10.214.10.0/28", IPRange = "10.214.10.8/29" }] })
            .Build();
        await _red.CreateAsync();

        _eco = new ContainerBuilder("nginx:1.27-alpine")
            .WithNetwork(_red)
            .WithNetworkAliases("ecommerce", "ecommerce-gestion")
            .WithResourceMapping(Encoding.UTF8.GetBytes(ConfiguracionDelServidorDeEco), "/etc/nginx/nginx.conf")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("wget", "-qO-", "-T", "2", "http://127.0.0.1:8080/"))
            .Build();
        await _eco.StartAsync();

        var configuracion = await File.ReadAllTextAsync(Path.Combine(BuscarRaiz(), "infra", "nginx", "nginx.conf"));
        Assert.Contains($"set_real_ip_from {DireccionDeCloudflaredEnElArchivo};", configuracion, StringComparison.Ordinal);
        configuracion = configuracion.Replace(DireccionDeCloudflaredEnElArchivo, DireccionDeCloudflaredEnLaPrueba, StringComparison.Ordinal);

        _borde = new ContainerBuilder("nginx:1.27-alpine")
            .WithNetwork(_red)
            .WithNetworkAliases("borde")
            .WithResourceMapping(Encoding.UTF8.GetBytes(configuracion), "/etc/nginx/nginx.conf")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("wget", "-qO-", "-T", "2", "http://127.0.0.1:8080/health"))
            .Build();
        await _borde.StartAsync();

        var nombreDeLaRed = _red.Name;
        _tunel = new ContainerBuilder("nginx:1.27-alpine")
            .WithNetwork(_red)
            .WithCreateParameterModifier(p => p.NetworkingConfig!.EndpointsConfig![nombreDeLaRed].IPAMConfig = new EndpointIPAMConfig { IPv4Address = DireccionDeCloudflaredEnLaPrueba })
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("true"))
            .Build();
        await _tunel.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_tunel is not null)
        {
            await _tunel.DisposeAsync();
        }

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

    /// <summary>Hace una solicitud al borde desde el contenedor dado, con las cabeceras dadas, y devuelve lo que vio el servidor de eco.</summary>
    private static async Task<Dictionary<string, string>> PedirDesde(IContainer cliente, params string[] cabeceras)
    {
        var argumentos = new List<string> { "wget", "-qO-", "-T", "5" };
        argumentos.AddRange(cabeceras.Select(c => "--header=" + c));
        argumentos.Add("http://borde:8080/cualquier-ruta");

        var resultado = await cliente.ExecAsync(argumentos);
        Assert.True(resultado.ExitCode == 0, resultado.Stderr + resultado.Stdout);

        return resultado.Stdout.Trim().Split(';')
            .Select(par => par.Split('=', 2))
            .ToDictionary(p => p[0], p => p[1].Trim('[', ']'));
    }

    /// <summary>Como cloudflared: desde la dirección en la que Nginx confía.</summary>
    private Task<Dictionary<string, string>> Pedir(params string[] cabeceras) => PedirDesde(_tunel!, cabeceras);

    /// <summary>El código HTTP que da el borde a una ruta, desde el contenedor que hace de cloudflared.</summary>
    private async Task<string> EstadoDe(string servidor, string ruta)
    {
        var resultado = await _tunel!.ExecAsync(["sh", "-c", $"wget -S -O /dev/null -T 5 --header='Host: {servidor}' http://borde:8080{ruta} 2>&1"]);
        var coincidencia = System.Text.RegularExpressions.Regex.Match(resultado.Stdout + resultado.Stderr, @"HTTP/\S+\s+(\d{3})", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(2));
        return coincidencia.Success ? coincidencia.Groups[1].Value : resultado.Stdout.Replace('\n', ' ') + "|" + resultado.ExitCode;
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task LaGestion_SoloSeSirveALoQueLlegaPorElTunel()
    {
        var resultado = await _eco!.ExecAsync(["sh", "-c", "wget -S -O /dev/null -T 5 --header='Host: gestion.example.org' http://borde:8080/ingreso 2>&1"]);

        Assert.Contains("403", resultado.Stdout + resultado.Stderr, StringComparison.Ordinal);
        Assert.Equal("200", await EstadoDe("gestion.example.org", "/ingreso"));
    }

    [Theory]
    [InlineData("tienda.example.org", "8080")]
    [InlineData("gestion.example.org", "8081")]
    [Trait("Criterio", "AC-PLA-04")]
    public async Task LaTiendaVaAlEcommerceDeEstaVm_YLaGestionSoloALaReplicaDeGestion(string servidor, string puerto)
    {
        var visto = await Pedir("Host: " + servidor);

        Assert.Equal(puerto, visto["puerto"]);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task QuienNoEsCloudflared_NoPuedeFijarElOrigen_NiConCfConnectingIpNiConXOrigenCliente()
    {
        var visto = await PedirDesde(_eco!, "CF-Connecting-IP: 203.0.113.7", "X-Origen-Cliente: 198.51.100.99", "X-Forwarded-For: 198.51.100.99");

        Assert.Equal(string.Empty, visto["origen"]);
        Assert.Equal(string.Empty, visto["xff"]);
    }

    [Theory]
    [InlineData("tienda.example.org")]
    [InlineData("gestion.example.org")]
    [Trait("Criterio", "AC-NOT-11")]
    public async Task LasIntegracionesInternasDeNotificaciones_NoExistenEnElBordePublico_ElRestoSiPasa(string servidor)
    {
        Assert.Equal("404", await EstadoDe(servidor, "/api/ecommerce/v1/integraciones/n8n/estados"));
        Assert.Equal("404", await EstadoDe(servidor, "/api/ecommerce/v1/integraciones/n8n/otra-cosa"));
        Assert.Equal("404", await EstadoDe(servidor, "/api/ecommerce/v1/integraciones/inventario/avisos"));
        // El enrutado de la aplicación no distingue mayúsculas: tampoco el borde.
        Assert.Equal("404", await EstadoDe(servidor, "/API/ecommerce/v1/integraciones/N8N/estados"));
        Assert.Equal("404", await EstadoDe(servidor, "/api/ecommerce/v1/Integraciones/Inventario/avisos"));
        Assert.Equal("200", await EstadoDe(servidor, "/api/ecommerce/v1/integraciones/pasarela/avisos"));
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
