using System.Text.Json;

namespace BabelHub.IntegrationTests.Imagenes;

/// <summary>
/// PLA-26 y PLA-27 (AC-PLA-26, AC-PLA-27): inspección de los archivos de Compose por VM. Con todo cumplido pasan; un contenedor que corre
/// como administrador, privilegiado, con el socket del motor, con capacidades añadidas, sin límite de memoria o de procesador, sin salud,
/// con el sistema de archivos escribible o con puertos abiertos a todas las direcciones se rechaza indicando el incumplimiento, salvo
/// una excepción registrada con su motivo y su mitigación. Las imágenes de terceros van fijadas por digest (<c>VersionesFijadasTests</c>).
/// </summary>
[Collection(ProcesosPesadosCollection.Nombre)]
public sealed class ComposePorVmTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "compose-" + Guid.NewGuid().ToString("N"));

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

    public static TheoryData<string> Vms => new(ComposeDeVms.Vms);

    // ---------- Los archivos reales ----------

    [Theory]
    [MemberData(nameof(Vms))]
    [Trait("Criterio", "AC-PLA-27")]
    public void ElComposeDeCadaVm_CumpleLasReglasDeContenedoresOTieneSuExcepcionRegistrada(string vm)
    {
        var incumplimientos = ReglasDeContenedores.Verificar(ComposeDeVms.ComposeResuelto(vm, _carpeta));

        Assert.True(incumplimientos.Count == 0, string.Join("\n", incumplimientos));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-27")]
    public void LasUnicasExcepcionesRegistradasSonLasDeEvolutionApi_ConSuMotivoYSuMitigacion()
    {
        var registradas = new List<string>();
        foreach (var vm in ComposeDeVms.Vms)
        {
            using var documento = JsonDocument.Parse(ComposeDeVms.ComposeResuelto(vm, _carpeta));
            if (documento.RootElement.TryGetProperty("x-excepciones", out var excepciones) && excepciones.ValueKind == JsonValueKind.Array)
            {
                registradas.AddRange(excepciones.EnumerateArray().Select(x => $"{x.GetProperty("servicio").GetString()}:{x.GetProperty("regla").GetString()}"));
            }
        }

        registradas.Sort(StringComparer.Ordinal);

        Assert.Equal(["evolution-api:sistema-de-archivos-de-solo-lectura", "evolution-api:usuario-no-administrador"], registradas);
    }

    [Theory]
    [MemberData(nameof(Vms))]
    [Trait("Criterio", "AC-PLA-26")]
    public void LasImagenesPropiasLlegaranPorVariableObligatoriaYLasDeTercerosVanFijadasPorDigest(string vm)
    {
        var texto = ComposeDeVms.Texto(vm);
        var imagenes = System.Text.RegularExpressions.Regex.Matches(texto, @"(?m)^\s*image:\s*(?<ref>[^#\r\n]+?)\s*(?:#.*)?$").Select(m => m.Groups["ref"].Value).ToList();

        Assert.NotEmpty(imagenes);
        foreach (var imagen in imagenes)
        {
            var propia = imagen.StartsWith("${IMAGEN_", StringComparison.Ordinal);
            Assert.True(
                propia ? imagen.Contains(":?", StringComparison.Ordinal) : System.Text.RegularExpressions.Regex.IsMatch(imagen, @"^[\w./-]+:[\w.-]+@sha256:[0-9a-f]{64}$"),
                $"{vm}: la imagen «{imagen}» no es una variable IMAGEN_… obligatoria ni una imagen con etiqueta y digest.");
        }
    }

    /// <summary>Los archivos de secretos que cada VM puede pedir: nunca los de otra (credenciales mínimas por VM).</summary>
    private static readonly Dictionary<string, string[]> SecretosPermitidos = new(StringComparer.Ordinal)
    {
        ["borde"] = ["cloudflared.env"],
        ["ventas"] = ["ca-bundle.pem", "cloudflared.env", "ecommerce.env", "evolution.env", "n8n.env", "tls-ecommerce-cert.pem", "tls-ecommerce-key.pem", "redis-contadores-ca.pem", "redis-contadores-cert.pem", "redis-contadores-key.pem", "redis-contadores.env", "redis.env"],
        ["inventario"] = ["ca-bundle.pem", "ecommerce-gestion.env", "inventario.env", "postgres.env", "tls-gestion-cert.pem", "tls-gestion-key.pem", "tls-inventario-cert.pem", "tls-inventario-key.pem"],
    };

    [Theory]
    [MemberData(nameof(Vms))]
    [Trait("Criterio", "AC-PLA-27")]
    public void CadaVmSoloPideLosSecretosQueLeCorresponden(string vm)
    {
        var pedidos = ComposeDeVms.ArchivosDeSecretos(vm);

        Assert.NotEmpty(pedidos);
        Assert.All(pedidos, p => Assert.Contains(p, SecretosPermitidos[vm]));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-27")]
    public void LaBaseYSuContrasenaSoloEstanEnLaVmDelInventario_YLosSecretosDelEcommerceNoSalenDeVentasYLaReplica()
    {
        Assert.DoesNotContain("postgres.env", ComposeDeVms.ArchivosDeSecretos("ventas"));
        Assert.DoesNotContain("postgres.env", ComposeDeVms.ArchivosDeSecretos("borde"));
        Assert.DoesNotContain("ecommerce.env", ComposeDeVms.ArchivosDeSecretos("inventario"));
        Assert.DoesNotContain("evolution.env", ComposeDeVms.ArchivosDeSecretos("inventario"));
        Assert.DoesNotContain("inventario.env", ComposeDeVms.ArchivosDeSecretos("ventas"));
    }

    [Theory]
    [MemberData(nameof(Vms))]
    public void ElComposeNoLlevaUnSecretoNiUnaDireccionFija(string vm)
    {
        var texto = ComposeDeVms.Texto(vm);

        Assert.DoesNotMatch(@"(?mi)^[ \t]*[\w_]*(password|secret|clave|token)[\w_]*[ \t]*[:=][ \t]*[^\s$\x22'].{6,}$", texto);
        // Solo el bucle local (127.0.0.1) puede ir escrito; las direcciones privadas vienen de IP_PRIVADA.
        Assert.DoesNotMatch(@"(?m)^[ \t]*-[ \t]*""?(?!127\.0\.0\.1:)\d{1,3}(\.\d{1,3}){3}:\d+:\d+""?[ \t]*$", texto);
    }

    // ---------- Las reglas con incumplimientos a propósito ----------

    private static string Servicio(string cambios = "", bool completo = true) =>
        "{\"services\":{\"servicio\":{" +
        "\"user\":\"1654:1654\",\"read_only\":true,\"cap_drop\":[\"ALL\"],\"security_opt\":[\"no-new-privileges:true\"]," +
        "\"mem_limit\":\"268435456\",\"cpus\":0.5,\"healthcheck\":{\"test\":[\"CMD\",\"true\"]}," +
        "\"ports\":[{\"host_ip\":\"10.0.0.5\",\"published\":\"8080\",\"target\":8080}]" +
        (cambios.Length > 0 ? "," + cambios : string.Empty) + "}}" + (completo ? "}" : string.Empty);

    private static string ConExcepcion(string servicioJson, string excepcion) => servicioJson[..^1] + ",\"x-excepciones\":[" + excepcion + "]}";

    [Fact]
    [Trait("Criterio", "AC-PLA-27")]
    public void UnContenedorQueCumpleTodo_Pasa()
    {
        Assert.Empty(ReglasDeContenedores.Verificar(Servicio()));
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-27")]
    [InlineData("\"user\":\"root\"", "usuario-no-administrador")]
    [InlineData("\"user\":\"0:0\"", "usuario-no-administrador")]
    [InlineData("\"user\":\"root:root\"", "usuario-no-administrador")]
    [InlineData("\"user\":\"\"", "usuario-no-administrador")]
    [InlineData("\"privileged\":true", "sin-modo-privilegiado")]
    [InlineData("\"volumes\":[{\"type\":\"bind\",\"source\":\"/var/run/docker.sock\",\"target\":\"/var/run/docker.sock\"}]", "sin-acceso-al-motor")]
    [InlineData("\"network_mode\":\"host\"", "sin-espacios-del-anfitrion")]
    [InlineData("\"pid\":\"host\"", "sin-espacios-del-anfitrion")]
    [InlineData("\"cap_add\":[\"NET_ADMIN\"]", "sin-capacidades-anadidas")]
    [InlineData("\"cap_drop\":[\"NET_RAW\"]", "capacidades-descartadas")]
    [InlineData("\"security_opt\":[]", "capacidades-descartadas")]
    [InlineData("\"mem_limit\":\"0\"", "limite-de-memoria")]
    [InlineData("\"cpus\":0", "limite-de-procesador")]
    [InlineData("\"healthcheck\":{\"disable\":true}", "comprobacion-de-salud")]
    [InlineData("\"healthcheck\":{\"test\":[\"NONE\"]}", "comprobacion-de-salud")]
    [InlineData("\"read_only\":false", "sistema-de-archivos-de-solo-lectura")]
    [InlineData("\"ports\":[{\"published\":\"8080\",\"target\":8080}]", "puertos-en-direccion-privada")]
    [InlineData("\"ports\":[{\"host_ip\":\"0.0.0.0\",\"published\":\"5432\",\"target\":5432}]", "puertos-en-direccion-privada")]
    [InlineData("\"environment\":{\"POSTGRES_PASSWORD\":\"una-contrasena-en-el-archivo\"}", "sin-secretos-en-el-entorno")]
    public void UnContenedorQueIncumpleUnaRegla_SeRechazaIndicandoElServicioYLaRegla(string cambio, string regla)
    {
        // El cambio reemplaza lo correcto: las propiedades repetidas se resuelven quitando la original.
        var propiedad = cambio[..cambio.IndexOf(':', StringComparison.Ordinal)];
        var original = Servicio();
        var sinLaOriginal = System.Text.RegularExpressions.Regex.Replace(original, propiedad + @":(\[[^\]]*\]|\{[^}]*\}\]?\}?|""[^""]*""|[\w.]+),?", string.Empty);
        var json = sinLaOriginal.Replace("\"servicio\":{", "\"servicio\":{" + cambio + ",", StringComparison.Ordinal).Replace(",}", "}", StringComparison.Ordinal);

        var incumplimientos = ReglasDeContenedores.Verificar(json);

        Assert.Contains(incumplimientos, i => i.StartsWith("servicio:", StringComparison.Ordinal) && i.Contains($"«{regla}»", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-27")]
    public void UnIncumplimientoConExcepcionRegistradaPasa_YSinMotivoOMitigacionFalla()
    {
        const string SinLectura = "\"read_only\":false";
        var propiedad = Servicio().Replace("\"read_only\":true,", SinLectura + ",", StringComparison.Ordinal);
        var completa = ConExcepcion(propiedad, "{\"servicio\":\"servicio\",\"regla\":\"sistema-de-archivos-de-solo-lectura\",\"motivo\":\"escribe sesiones\",\"mitigacion\":\"volumen propio\"}");
        var sinMitigacion = ConExcepcion(propiedad, "{\"servicio\":\"servicio\",\"regla\":\"sistema-de-archivos-de-solo-lectura\",\"motivo\":\"escribe sesiones\",\"mitigacion\":\" \"}");

        Assert.Empty(ReglasDeContenedores.Verificar(completa));
        Assert.Contains(ReglasDeContenedores.Verificar(sinMitigacion), i => i.Contains("motivo y su mitigación", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-27")]
    public void UnaExcepcionQueYaNoHaceFaltaUnaReglaInexistenteOUnServicioInexistente_SeSenalan()
    {
        var innecesaria = ConExcepcion(Servicio(), "{\"servicio\":\"servicio\",\"regla\":\"limite-de-memoria\",\"motivo\":\"x\",\"mitigacion\":\"y\"}");
        var reglaInexistente = ConExcepcion(Servicio(), "{\"servicio\":\"servicio\",\"regla\":\"inventada\",\"motivo\":\"x\",\"mitigacion\":\"y\"}");
        var servicioInexistente = ConExcepcion(Servicio(), "{\"servicio\":\"otro\",\"regla\":\"limite-de-memoria\",\"motivo\":\"x\",\"mitigacion\":\"y\"}");

        Assert.Contains(ReglasDeContenedores.Verificar(innecesaria), i => i.Contains("ya no hace falta", StringComparison.Ordinal));
        Assert.Contains(ReglasDeContenedores.Verificar(reglaInexistente), i => i.Contains("no existe", StringComparison.Ordinal));
        Assert.Contains(ReglasDeContenedores.Verificar(servicioInexistente), i => i.Contains("el servicio «otro» no existe", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-27")]
    public void ElLimiteTambienPuedeIrEnDeploy()
    {
        var json = Servicio().Replace("\"mem_limit\":\"268435456\",\"cpus\":0.5,", "\"deploy\":{\"resources\":{\"limits\":{\"memory\":\"256M\",\"cpus\":\"0.5\"}}},", StringComparison.Ordinal);

        Assert.Empty(ReglasDeContenedores.Verificar(json));
    }
}
