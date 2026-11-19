using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.IntegrationTests.Restauracion;

/// <summary>
/// El arranque seguro del servicio (PLA-19, PLA-43, PLA-39; plan §5.1): el modo, la marca de un solo uso de <c>restaurar.sh</c> que exige
/// la producción, la negativa a arrancar el ensayo con credenciales de producción o con adaptadores que no corresponden al modo, los
/// trabajos de fondo apagados fuera del modo normal y la salud sin datos internos. Incluye arranques reales del proceso de la Api.
/// </summary>
public sealed class ArranqueSeguroEcommerceTests : IDisposable
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Un valor sembrado distinto en cada ejecución: nunca un secreto fijo en el repositorio.</summary>
    private static readonly string ValorSembrado = "Sup3r" + Guid.NewGuid().ToString("N");
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "arranque-" + Guid.NewGuid().ToString("N"));

    // Los secretos de estas pruebas se generan en cada ejecución: nunca hay uno fijo en el repositorio.
    private readonly string _secretoDeLaMarca = "marca-" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    private sealed class AlmacenEnMemoria : IAlmacenDeNoncesDeMarcas
    {
        private readonly HashSet<string> _usados = [];

        public bool Consumir(string nonce)
        {
            lock (_usados)
            {
                return _usados.Add(nonce);
            }
        }
    }

    public ArranqueSeguroEcommerceTests()
    {
        Directory.CreateDirectory(_carpeta);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_carpeta, recursive: true);
        }
        catch (IOException)
        {
            // Limpieza de una carpeta temporal: no debe hacer fallar la prueba.
        }
    }

    private byte[] Secreto => Encoding.UTF8.GetBytes(_secretoDeLaMarca);

    private static string Nonce() => Guid.NewGuid().ToString("N");

    private string Marca(ModoDeArranque modo = ModoDeArranque.Restauracion, DateTimeOffset? instante = null, string? nonce = null) =>
        MarcaDeRestauracion.Emitir(Secreto, modo, instante ?? Ahora, nonce ?? Nonce());

    private static IConfiguration Configuracion(params (string Clave, string? Valor)[] valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores.Select(v => new KeyValuePair<string, string?>(v.Clave, v.Valor))).Build();

    private IConfiguration DeProduccion(string modo, string? marca, string? secreto = null) =>
        Configuracion(("Modo", modo), ("Restauracion:Marca", marca), ("Restauracion:SecretoDeLaMarca", secreto ?? _secretoDeLaMarca));

    // ---------- La marca ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public void UnaMarcaEmitidaConElSecretoVerificaYSoloUnaVez()
    {
        var almacen = new AlmacenEnMemoria();
        var marca = Marca();

        Assert.Equal(ResultadoDeMarca.Valida, MarcaDeRestauracion.Verificar(marca, Secreto, ModoDeArranque.Restauracion, Ahora.AddSeconds(30), almacen));
        Assert.Equal(ResultadoDeMarca.Reutilizada, MarcaDeRestauracion.Verificar(marca, Secreto, ModoDeArranque.Restauracion, Ahora.AddSeconds(31), almacen));
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-43")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SinMarca_EsAusente(string? marca)
    {
        Assert.Equal(ResultadoDeMarca.Ausente, MarcaDeRestauracion.Verificar(marca, Secreto, ModoDeArranque.Restauracion, Ahora, new AlmacenEnMemoria()));
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-43")]
    [InlineData("v1")]
    [InlineData("v2.Restauracion.1.abcdefabcdefabcdef.aaaa")]
    [InlineData("v1.Restauracion.no-es-un-numero.abcdefabcdefabcdef.aaaa")]
    [InlineData("v1.Restauracion.1763035200.corto.aaaa")]
    public void UnaMarcaMalformada_SeRechaza(string marca)
    {
        Assert.Equal(ResultadoDeMarca.Malformada, MarcaDeRestauracion.Verificar(marca, Secreto, ModoDeArranque.Restauracion, Ahora, new AlmacenEnMemoria()));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public void UnaMarcaFirmadaConOtroSecretoOAlteradaSeRechazaYNoGastaElNonce()
    {
        var almacen = new AlmacenEnMemoria();
        var nonce = Nonce();
        var buena = Marca(nonce: nonce);
        var deOtro = MarcaDeRestauracion.Emitir(Encoding.UTF8.GetBytes("otro-secreto-" + Guid.NewGuid().ToString("N")), ModoDeArranque.Restauracion, Ahora, nonce);
        var alterada = buena.Replace(".Restauracion.", ".Restauracion.", StringComparison.Ordinal)[..^1] + (buena[^1] == 'a' ? 'b' : 'a');

        Assert.Equal(ResultadoDeMarca.FirmaInvalida, MarcaDeRestauracion.Verificar(deOtro, Secreto, ModoDeArranque.Restauracion, Ahora, almacen));
        Assert.Equal(ResultadoDeMarca.FirmaInvalida, MarcaDeRestauracion.Verificar(alterada, Secreto, ModoDeArranque.Restauracion, Ahora, almacen));
        // Las inválidas no consumieron el nonce: la buena todavía sirve.
        Assert.Equal(ResultadoDeMarca.Valida, MarcaDeRestauracion.Verificar(buena, Secreto, ModoDeArranque.Restauracion, Ahora, almacen));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public void UnaMarcaDeUnModoNoSirveParaOtro()
    {
        var almacen = new AlmacenEnMemoria();

        var resultado = MarcaDeRestauracion.Verificar(Marca(ModoDeArranque.Ensayo), Secreto, ModoDeArranque.Restauracion, Ahora, almacen);

        Assert.Equal(ResultadoDeMarca.DeOtroModo, resultado);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-43")]
    [InlineData(301, ResultadoDeMarca.Vencida)]
    [InlineData(300, ResultadoDeMarca.Valida)]
    [InlineData(-61, ResultadoDeMarca.Vencida)]
    [InlineData(-60, ResultadoDeMarca.Valida)]
    public void LaMarcaCaducaALosCincoMinutosYSoloTolerasesentaSegundosDeRelojAdelantado(int segundosDespuesDeEmitirla, ResultadoDeMarca esperado)
    {
        var emitida = Marca();

        var resultado = MarcaDeRestauracion.Verificar(emitida, Secreto, ModoDeArranque.Restauracion, Ahora.AddSeconds(segundosDespuesDeEmitirla), new AlmacenEnMemoria());

        Assert.Equal(esperado, resultado);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public async Task VeinteArranquesSimultaneosConLaMismaMarca_SoloUnoLaGastaYSobreviveAUnReinicio()
    {
        var carpeta = Path.Combine(_carpeta, "marcas");
        var marca = Marca();

        var resultados = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            Task.Run(() => MarcaDeRestauracion.Verificar(marca, Secreto, ModoDeArranque.Restauracion, Ahora, new AlmacenDeNoncesEnArchivos(carpeta)))));

        Assert.Equal(1, resultados.Count(r => r == ResultadoDeMarca.Valida));
        Assert.Equal(19, resultados.Count(r => r == ResultadoDeMarca.Reutilizada));
        // «Reiniciar»: otra instancia sobre la misma carpeta sigue sabiendo que se usó.
        Assert.Equal(ResultadoDeMarca.Reutilizada, MarcaDeRestauracion.Verificar(marca, Secreto, ModoDeArranque.Restauracion, Ahora, new AlmacenDeNoncesEnArchivos(carpeta)));
    }

    // ---------- Resolver el modo ----------

    [Theory]
    [Trait("Criterio", "AC-PLA-43")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Normal")]
    public void SinModoOEnModoNormal_ArrancaNormalYEnProduccionNoPideMarca(string? modo)
    {
        var resultado = ArranqueSeguro.Resolver(Configuracion(("Modo", modo)), esProduccion: true, Ahora, new AlmacenEnMemoria());

        Assert.Equal(ModoDeArranque.Normal, resultado);
    }

    [Theory]
    [InlineData("restauracion")]
    [InlineData("Otro")]
    [InlineData("1")]
    public void UnModoInvalidoSeRechazaNombrandoLaVariable(string modo)
    {
        var error = Assert.Throws<ArranqueRechazadoException>(() => ArranqueSeguro.Resolver(Configuracion(("Modo", modo)), esProduccion: false, Ahora, new AlmacenEnMemoria()));

        Assert.Contains("BABELHUB__Modo", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-43")]
    [InlineData("Restauracion")]
    [InlineData("Ensayo")]
    public void FueraDeProduccion_LosModosEspecialesArrancanSinMarca(string modo)
    {
        var configuracion = Configuracion(("Modo", modo), ("Ensayo:ProhibidosSha256", new string('0', 64)));

        var resultado = ArranqueSeguro.Resolver(configuracion, esProduccion: false, Ahora, new AlmacenEnMemoria());

        Assert.Equal(Enum.Parse<ModoDeArranque>(modo), resultado);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-43")]
    [InlineData("Restauracion")]
    [InlineData("Ensayo")]
    public void EnProduccion_UnModoEspecialSinMarcaSeRechaza(string modo)
    {
        var error = Assert.Throws<ArranqueRechazadoException>(() => ArranqueSeguro.Resolver(DeProduccion(modo, null), esProduccion: true, Ahora, new AlmacenEnMemoria()));

        Assert.Contains("BABELHUB__Restauracion__Marca", error.Message, StringComparison.Ordinal);
        Assert.Contains("Ausente", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public void EnProduccion_SinElSecretoDeLaMarcaOConUnoDebil_SeRechazaNombrandoLaVariable()
    {
        var sin = Configuracion(("Modo", "Restauracion"), ("Restauracion:Marca", Marca()));
        var debil = DeProduccion("Restauracion", Marca(), secreto: "corto");

        var errorSin = Assert.Throws<ArranqueRechazadoException>(() => ArranqueSeguro.Resolver(sin, esProduccion: true, Ahora, new AlmacenEnMemoria()));
        var errorDebil = Assert.Throws<ArranqueRechazadoException>(() => ArranqueSeguro.Resolver(debil, esProduccion: true, Ahora, new AlmacenEnMemoria()));

        Assert.Contains("BABELHUB__Restauracion__SecretoDeLaMarca", errorSin.Message, StringComparison.Ordinal);
        Assert.Contains("BABELHUB__Restauracion__SecretoDeLaMarca", errorDebil.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public void EnProduccion_UnaMarcaValidaPermiteArrancarUnaSolaVezYLosMensajesNuncaLlevanElSecretoNiLaMarca()
    {
        var almacen = new AlmacenEnMemoria();
        var marca = Marca();
        var configuracion = DeProduccion("Restauracion", marca);

        var modo = ArranqueSeguro.Resolver(configuracion, esProduccion: true, Ahora, almacen);
        var error = Assert.Throws<ArranqueRechazadoException>(() => ArranqueSeguro.Resolver(configuracion, esProduccion: true, Ahora, almacen));

        Assert.Equal(ModoDeArranque.Restauracion, modo);
        Assert.Contains("Reutilizada", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_secretoDeLaMarca, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(marca, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public void EnProduccion_UnaMarcaVencidaOdeOtroModoSeRechaza()
    {
        var vencida = DeProduccion("Restauracion", Marca(instante: Ahora.AddHours(-1)));
        var deEnsayo = DeProduccion("Restauracion", Marca(ModoDeArranque.Ensayo));

        Assert.Contains("Vencida", Assert.Throws<ArranqueRechazadoException>(() => ArranqueSeguro.Resolver(vencida, esProduccion: true, Ahora, new AlmacenEnMemoria())).Message, StringComparison.Ordinal);
        Assert.Contains("DeOtroModo", Assert.Throws<ArranqueRechazadoException>(() => ArranqueSeguro.Resolver(deEnsayo, esProduccion: true, Ahora, new AlmacenEnMemoria())).Message, StringComparison.Ordinal);
    }

    // ---------- El ensayo ----------

    private static string Huella(string valor) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor))).ToLowerInvariant();

    [Fact]
    [Trait("Criterio", "AC-PLA-22")]
    public void ElEnsayoNoArrancaSinLaListaDeHuellasDeProduccion()
    {
        var error = Assert.Throws<ArranqueRechazadoException>(() => ArranqueSeguro.Resolver(Configuracion(("Modo", "Ensayo")), esProduccion: false, Ahora, new AlmacenEnMemoria()));

        Assert.Contains("BABELHUB__Ensayo__ProhibidosSha256", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-22")]
    public void ElEnsayoNoArrancaConUnaCredencialDeProduccionYNombraLaVariableSinElValor()
    {
        var deProduccion = "credencial-de-produccion-" + Guid.NewGuid().ToString("N");
        var configuracion = Configuracion(
            ("Modo", "Ensayo"),
            ("Ensayo:ProhibidosSha256", $"{Huella("otra-cosa")}, {Huella(deProduccion).ToUpperInvariant()}"),
            ("Seguridad:Credenciales:OperacionDeUsuario", deProduccion),
            ("Seguridad:Turnstile:Secreto", "secreto-propio-del-ensayo-" + Guid.NewGuid().ToString("N")));

        var error = Assert.Throws<ArranqueRechazadoException>(() => ArranqueSeguro.Resolver(configuracion, esProduccion: false, Ahora, new AlmacenEnMemoria()));

        Assert.Contains("BABELHUB__Seguridad__Credenciales__OperacionDeUsuario", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Turnstile", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(deProduccion, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-22")]
    public void ElEnsayoConSecretosPropiosArranca()
    {
        var configuracion = Configuracion(
            ("Modo", "Ensayo"),
            ("Ensayo:ProhibidosSha256", Huella("una-credencial-de-produccion")),
            ("Seguridad:Credenciales:OperacionDeUsuario", "credencial-propia-del-ensayo-" + Guid.NewGuid().ToString("N")));

        Assert.Equal(ModoDeArranque.Ensayo, ArranqueSeguro.Resolver(configuracion, esProduccion: false, Ahora, new AlmacenEnMemoria()));
    }

    // ---------- Adaptadores y trabajos de fondo ----------

    private sealed record Adaptador(string Nombre, bool EsHaciaProveedor, bool EsNulo) : IAdaptadorSaliente;

    [Theory]
    [Trait("Criterio", "AC-PLA-22")]
    [InlineData(ModoDeArranque.Ensayo, false, "notificador", true, false, true)] // el ensayo con un notificador real: no arranca
    [InlineData(ModoDeArranque.Ensayo, false, "notificador", true, true, false)] // con el nulo: arranca
    [InlineData(ModoDeArranque.Restauracion, true, "pasarela", true, false, true)]
    [InlineData(ModoDeArranque.Restauracion, true, "pasarela", true, true, false)]
    [InlineData(ModoDeArranque.Restauracion, true, "cliente-del-inventario", false, false, false)] // el otro servicio no es un proveedor
    [InlineData(ModoDeArranque.Normal, true, "notificador", true, true, true)] // producción normal con un notificador nulo: no arranca
    [InlineData(ModoDeArranque.Normal, true, "notificador", true, false, false)]
    [InlineData(ModoDeArranque.Normal, false, "notificador", true, true, false)] // desarrollo con el simulado: arranca
    public void LosAdaptadoresSalientesDebenCorresponderAlModo(ModoDeArranque modo, bool esProduccion, string nombre, bool haciaProveedor, bool nulo, bool rechaza)
    {
        var accion = () => ArranqueSeguro.ExigirCoherencia(modo, esProduccion, [new Adaptador(nombre, haciaProveedor, nulo)]);

        if (rechaza)
        {
            var error = Assert.Throws<ArranqueRechazadoException>(accion);
            Assert.Contains(nombre, error.Message, StringComparison.Ordinal);
        }
        else
        {
            accion();
        }
    }

    private sealed class TrabajoDePrueba : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-22")]
    [InlineData(ModoDeArranque.Normal, 1)]
    [InlineData(ModoDeArranque.Restauracion, 0)]
    [InlineData(ModoDeArranque.Ensayo, 0)]
    public void LosTrabajosDeFondoSoloCorrenEnElModoNormal(ModoDeArranque modo, int registrados)
    {
        var servicios = new ServiceCollection().AgregarTrabajoDeFondo<TrabajoDePrueba>(modo);

        Assert.Equal(registrados, servicios.Count(s => s.ServiceType == typeof(IHostedService)));
    }

    // ---------- La salud ----------

    private sealed class Componente(string nombre, Func<CancellationToken, Task<bool>> comprobar) : IComprobacionDeSalud
    {
        public string Nombre { get; } = nombre;

        public Task<bool> Comprobar(CancellationToken cancelacion) => comprobar(cancelacion);
    }

    private static async Task<(HttpClient Cliente, WebApplication App)> ServidorDeSalud(string? direccionDeLaConexion, params IComprobacionDeSalud[] componentes)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("ECO"));
        foreach (var componente in componentes)
        {
            builder.Services.AddSingleton(componente);
        }

        var app = builder.Build();
        app.Use(async (contexto, siguiente) =>
        {
            contexto.Connection.RemoteIpAddress = direccionDeLaConexion is null ? null : IPAddress.Parse(direccionDeLaConexion);
            await siguiente(contexto);
        });
        app.UsarManejoDeErrores();
        app.MapearSalud(new OpcionesDeSalud { PlazoPorComponente = TimeSpan.FromMilliseconds(300) });
        await app.StartAsync();
        return (app.GetTestClient(), app);
    }

    private static Componente Sano(string nombre = "base-de-datos") => new(nombre, _ => Task.FromResult(true));

    private static Componente Caido(string nombre = "almacen-de-contadores") => new(nombre, _ => Task.FromResult(false));

    [Fact]
    public async Task LaSaludViva_DiceQueElProcesoRespondeSinDetallesYSinGuardarseEnCache()
    {
        var (cliente, app) = await ServidorDeSalud("203.0.113.9", Caido());
        await using var _ = app;

        var respuesta = await cliente.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("{\"estado\":\"vivo\"}", await respuesta.Content.ReadAsStringAsync());
        Assert.Contains("no-store", respuesta.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("203.0.113.9")]
    [InlineData("8.8.8.8")]
    public async Task ListoDesdeFuera_DiceSoloListoSinNombrarComponentes(string direccion)
    {
        var (cliente, app) = await ServidorDeSalud(direccion, Sano());
        await using var _ = app;

        var cuerpo = await (await cliente.GetAsync("/health/ready")).Content.ReadAsStringAsync();

        Assert.Equal("{\"estado\":\"listo\"}", cuerpo);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.20.30.40")]
    [InlineData("172.16.5.5")]
    [InlineData("192.168.1.7")]
    [InlineData("::ffff:10.0.0.9")]
    [InlineData("fd12:3456::1")]
    public async Task ListoDesdeLaRedPrivada_ListaLosComponentes(string direccion)
    {
        var (cliente, app) = await ServidorDeSalud(direccion, Sano("redis"), Sano("base-de-datos"));
        await using var _ = app;

        var respuesta = await cliente.GetAsync("/health/ready");
        var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("listo", cuerpo.GetProperty("estado").GetString());
        Assert.Equal(["base-de-datos", "redis"], cuerpo.GetProperty("componentes").EnumerateArray().Select(c => c.GetProperty("nombre").GetString()));
    }

    [Fact]
    public async Task ConUnComponenteCaidoDesdeLaRedPrivada_DiceCualFalla()
    {
        var (cliente, app) = await ServidorDeSalud("10.0.0.5", Sano(), Caido());
        await using var _ = app;

        var respuesta = await cliente.GetAsync("/health/ready");
        var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Equal("no-disponible", cuerpo.GetProperty("estado").GetString());
        var falla = cuerpo.GetProperty("componentes").EnumerateArray().Single(c => c.GetProperty("estado").GetString() == "falla");
        Assert.Equal("almacen-de-contadores", falla.GetProperty("nombre").GetString());
    }

    [Fact]
    public async Task ConUnComponenteCaidoDesdeFuera_ResponderElErrorUniformeSinNombrarNada()
    {
        var (cliente, app) = await ServidorDeSalud("203.0.113.9", Sano(), Caido());
        await using var _ = app;

        var respuesta = await cliente.GetAsync("/health/ready");
        var texto = await respuesta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Equal("ECO-SERVICIO-NO-DISPONIBLE", JsonDocument.Parse(texto).RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("almacen-de-contadores", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("base-de-datos", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnaCabeceraDeReenvioNoConvierteAUnExternoEnRedPrivada()
    {
        var (cliente, app) = await ServidorDeSalud("203.0.113.9", Caido());
        await using var _ = app;
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, "/health/ready");
        solicitud.Headers.Add("X-Forwarded-For", "10.0.0.1");
        solicitud.Headers.Add("X-Origen-Cliente", "127.0.0.1");

        var texto = await (await cliente.SendAsync(solicitud)).Content.ReadAsStringAsync();

        Assert.DoesNotContain("almacen-de-contadores", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SinDireccionDeConexion_SeTrataComoExterno()
    {
        var (cliente, app) = await ServidorDeSalud(null, Caido());
        await using var _ = app;

        var texto = await (await cliente.GetAsync("/health/ready")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("almacen-de-contadores", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnaExcepcionOUnPlazoVencidoCuentanComoCaidos_YSuMensajeNoSale()
    {
        var lanza = new Componente("redis", _ => throw new InvalidOperationException("falló con password=" + ValorSembrado + " en 10.9.8.7"));
        var lento = new Componente("base-de-datos", async token =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return true;
        });
        var (cliente, app) = await ServidorDeSalud("10.0.0.5", lanza, lento);
        await using var _ = app;

        var respuesta = await cliente.GetAsync("/health/ready");
        var texto = await respuesta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Equal(2, JsonDocument.Parse(texto).RootElement.GetProperty("componentes").EnumerateArray().Count(c => c.GetProperty("estado").GetString() == "falla"));
        Assert.DoesNotContain(ValorSembrado, texto, StringComparison.Ordinal);
        Assert.DoesNotContain("10.9.8.7", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", texto, StringComparison.Ordinal);
    }

    // ---------- El proceso real ----------

    private static string RaizDelRepositorio()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "BabelHub.sln")))
        {
            carpeta = carpeta.Parent;
        }

        return carpeta?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    private (int Codigo, string Salida) Arrancar(string entorno, string modo, string? marca)
    {
        var dll = Path.Combine(RaizDelRepositorio(), "src", "BabelHub.Ecommerce", "BabelHub.Ecommerce.Api", "bin", "Debug", "net10.0", "BabelHub.Ecommerce.Api.dll");
        var informacion = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(dll)!,
        };
        informacion.ArgumentList.Add(dll);
        informacion.Environment["ASPNETCORE_ENVIRONMENT"] = entorno;

        // La composición real (COMP-1) exige todos sus secretos: el proceso arranca con una configuración completa y desechable.
        foreach (var (ruta, valor) in BabelHub.IntegrationTests.Api.AnfitrionDelEcommerceTests.ConfiguracionCompleta())
        {
            informacion.Environment["BABELHUB__" + ruta.Replace(":", "__", StringComparison.Ordinal)] = valor;
        }

        if (entorno == "Production")
        {
            foreach (var (ruta, valor) in BabelHub.IntegrationTests.Api.AnfitrionDelEcommerceTests.ConfiguracionDeProduccion())
            {
                informacion.Environment["BABELHUB__" + ruta.Replace(":", "__", StringComparison.Ordinal)] = valor;
            }
        }

        informacion.Environment["BABELHUB__Modo"] = modo;
        informacion.Environment["BABELHUB__Restauracion__SecretoDeLaMarca"] = _secretoDeLaMarca;
        informacion.Environment["BABELHUB__Restauracion__CarpetaDeMarcas"] = Path.Combine(_carpeta, "marcas-del-proceso");
        // En producción el E-commerce exige el rol del proceso y los hosts que acepta (web-base, D03): sin ellos el arranque falla.
        informacion.Environment["BABELHUB__Web__Host"] = "tienda";
        informacion.Environment["BABELHUB__Web__HostsPermitidos"] = "www.babelhub.example";
        if (marca is not null)
        {
            informacion.Environment["BABELHUB__Restauracion__Marca"] = marca;
        }

        using var proceso = Process.Start(informacion)!;
        var salida = new StringBuilder();
        proceso.OutputDataReceived += (_, e) => { lock (salida) { salida.AppendLine(e.Data); } };
        proceso.ErrorDataReceived += (_, e) => { lock (salida) { salida.AppendLine(e.Data); } };
        proceso.BeginOutputReadLine();
        proceso.BeginErrorReadLine();
        if (!proceso.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            proceso.Kill(entireProcessTree: true);
            throw new TimeoutException("El servicio no terminó a tiempo.");
        }

        proceso.WaitForExit();
        return (proceso.ExitCode, salida.ToString());
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public void ElServicioEnProduccionNoArrancaEnRestauracionSinLaMarca()
    {
        var (codigo, salida) = Arrancar("Production", "Restauracion", marca: null);

        Assert.NotEqual(0, codigo);
        Assert.Contains("BABELHUB__Restauracion__Marca", salida, StringComparison.Ordinal);
        Assert.DoesNotContain(_secretoDeLaMarca, salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public void ElServicioEnProduccionArrancaEnRestauracionConLaMarcaYNoConLaMismaDosVeces()
    {
        var marca = MarcaDeRestauracion.Emitir(Secreto, ModoDeArranque.Restauracion, DateTimeOffset.UtcNow, Nonce());

        var primera = Arrancar("Production", "Restauracion", marca);
        var segunda = Arrancar("Production", "Restauracion", marca);

        // Con la composición real los pasos hablan con la base: sin ella la restauración no se abre, pero la marca válida sí se aceptó (llegó a los pasos) y se consumió.
        Assert.Contains("paso: identidad-rotar-claves-e-invalidar-sesiones", primera.Salida, StringComparison.Ordinal);
        Assert.DoesNotContain("Reutilizada", primera.Salida, StringComparison.Ordinal);
        Assert.NotEqual(0, segunda.Codigo);
        Assert.Contains("Reutilizada", segunda.Salida, StringComparison.Ordinal);
        Assert.DoesNotContain(marca, segunda.Salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public void ElServicioEnProduccionNoArrancaConUnaMarcaVencida()
    {
        var marca = MarcaDeRestauracion.Emitir(Secreto, ModoDeArranque.Restauracion, DateTimeOffset.UtcNow.AddMinutes(-10), Nonce());

        var (codigo, salida) = Arrancar("Production", "Restauracion", marca);

        Assert.NotEqual(0, codigo);
        Assert.Contains("Vencida", salida, StringComparison.Ordinal);
    }

    [Fact]
    public void FueraDeProduccion_LaRestauracionCorreSusPasosReales_YUnModoInvalidoNoArranca()
    {
        // Con la composición real los pasos de Identidad y de la web hablan con su base: sin base de datos el primero falla y la restauración no se abre, nombrando el paso.
        var sinBase = Arrancar("Development", "Restauracion", marca: null);
        var invalido = Arrancar("Development", "Cualquiera", marca: null);

        Assert.NotEqual(0, sinBase.Codigo);
        Assert.Contains("restauracion: no se abre, falló el paso identidad-rotar-claves-e-invalidar-sesiones", sinBase.Salida, StringComparison.Ordinal);
        Assert.NotEqual(0, invalido.Codigo);
        Assert.Contains("BABELHUB__Modo", invalido.Salida, StringComparison.Ordinal);
    }
}
