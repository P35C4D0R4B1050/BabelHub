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
        ["ventas"] = ["ca-bundle.pem", "cloudflared.env", "ecommerce.env", "evolution.env", "n8n.env", "tls-ca.pem", "tls-ecommerce-cert.pem", "tls-ecommerce-key.pem", "redis-contadores-ca.pem", "redis-contadores-cert.pem", "redis-contadores-key.pem", "redis-contadores.env", "redis.env"],
        ["inventario"] = ["ca-bundle.pem", "ecommerce-gestion.env", "inventario.env", "postgres.env", "tls-gestion-cert.pem", "tls-gestion-key.pem", "tls-inventario-cert.pem", "tls-inventario-key.pem", "tls-postgres-cert.pem", "tls-postgres-key.pem"],
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

    // ---------- TLS de PostgreSQL (AM24) ----------

    private JsonElement ServicioDelCompose(string vm, string servicio)
    {
        var documento = JsonDocument.Parse(ComposeDeVms.ComposeResuelto(vm, _carpeta));
        return documento.RootElement.GetProperty("services").GetProperty(servicio).Clone();
    }

    private static string[] Lista(JsonElement elemento, string propiedad) =>
        elemento.TryGetProperty(propiedad, out var valores) && valores.ValueKind == JsonValueKind.Array ? [.. valores.EnumerateArray().Select(v => v.GetString() ?? string.Empty)] : [];

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void PostgreSqlDeLaVmDeDatos_ArrancaConTlsPorSuEntrypoint_ConElControlDeAccesoDeLaImagen_YSinRomperElSoloLecturaNiLasCapacidades()
    {
        var postgres = ServicioDelCompose("inventario", "postgres");
        var orden = Lista(postgres, "command");
        string Opcion(string nombre) => orden.SingleOrDefault(a => a.StartsWith(nombre + "=", StringComparison.Ordinal)) ?? string.Empty;

        Assert.Equal(["/opt/babelhub/iniciar.sh"], Lista(postgres, "entrypoint"));
        Assert.Equal("ssl=on", Opcion("ssl"));
        Assert.Equal("ssl_cert_file=/run/postgresql/tls/server.crt", Opcion("ssl_cert_file"));
        Assert.Equal("ssl_key_file=/run/postgresql/tls/server.key", Opcion("ssl_key_file"));
        Assert.Equal("hba_file=/opt/babelhub/pg_hba.conf", Opcion("hba_file"));
        Assert.Equal("ssl_min_protocol_version=TLSv1.3", Opcion("ssl_min_protocol_version"));
        // Los secretos del servidor se montan de solo lectura como archivos de /run/secrets y el contenedor conserva sus restricciones.
        Assert.Equal(["tls-cert.pem", "tls-key.pem"], [.. postgres.GetProperty("secrets").EnumerateArray().Select(s => s.GetProperty("target").GetString()!).Order(StringComparer.Ordinal)]);
        Assert.True(postgres.GetProperty("read_only").GetBoolean());
        Assert.Equal(["ALL"], Lista(postgres, "cap_drop"));
        Assert.Equal("postgres", postgres.GetProperty("user").GetString());
        Assert.Contains(postgres.GetProperty("tmpfs").EnumerateArray().Select(t => t.GetString()), t => t == "/run/postgresql");
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void ElControlDeAccesoDePostgreSql_SoloAdmiteConexionesDeRedPorTls_YRechazaLoDemas()
    {
        var reglas = File.ReadAllLines(Path.Combine(ComposeDeVms.Raiz(), "infra", "postgres", "servidor", "pg_hba.conf"))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        // Por la red: solo `hostssl` con contraseña cifrada (scram) y solo a las bases de la plataforma; nunca `host` ni `hostnossl` admitidos, ni `trust` por la red.
        Assert.All(reglas.Where(r => r[0] is "host" or "hostssl" or "hostnossl" or "hostgssenc" or "hostnogssenc"), r =>
        {
            Assert.True(r[0] == "hostssl" || r[^1] == "reject", $"Una regla de red admite conexiones sin TLS: {string.Join(' ', r)}");
            Assert.True(r[^1] is "scram-sha-256" or "reject", $"Una regla de red no usa scram-sha-256: {string.Join(' ', r)}");
        });
        Assert.All(reglas.Where(r => r[0] == "hostssl"), r => Assert.Contains(r[1], new[] { "ecommerce", "inventario", "evolution" }));
        // El socket de Unix (dentro del contenedor) sigue sirviendo a la comprobación de salud, al respaldo y al archivado.
        Assert.Contains(reglas, r => r[0] == "local" && r[1] == "all");
        Assert.Contains(reglas, r => r[0] == "local" && r[1] == "replication");
        // La última regla rechaza todo lo que no coincidió antes (incluida cualquier conexión de red sin TLS).
        Assert.Equal(["host", "all", "all", "all", "reject"], reglas[^1]);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void LaImagenDePostgreSql_LlevaElControlDeAccesoYElArranqueConTls()
    {
        var dockerfile = File.ReadAllText(Path.Combine(ComposeDeVms.Raiz(), "infra", "postgres", "Dockerfile"));

        Assert.Contains("COPY servidor/ /opt/babelhub/", dockerfile, StringComparison.Ordinal);
        Assert.Contains("/opt/babelhub/pg_hba.conf", dockerfile, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(ComposeDeVms.Raiz(), "infra", "postgres", "servidor", "iniciar.sh")));
    }

    // ---------- La entrada pública en VM-VENTAS: quién puede decir cuál es la IP del cliente (SEG-17, SEG-23; DD-101) ----------

    private static string[] Redes(JsonElement servicio) => [.. servicio.GetProperty("networks").EnumerateObject().Select(r => r.Name).Order(StringComparer.Ordinal)];

    private static string DireccionFija(JsonElement servicio, string red) => servicio.GetProperty("networks").GetProperty(red).GetProperty("ipv4_address").GetString()!;

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public void LaEntradaPublica_VaPorDosRedesPropias_DondeSoloEstanLosDosExtremosDeCadaSalto()
    {
        using var documento = JsonDocument.Parse(ComposeDeVms.ComposeResuelto("ventas", _carpeta));
        var servicios = documento.RootElement.GetProperty("services").EnumerateObject().ToDictionary(s => s.Name, s => s.Value, StringComparer.Ordinal);
        string[] En(string red) => [.. servicios.Where(s => Redes(s.Value).Contains(red)).Select(s => s.Key).Order(StringComparer.Ordinal)];

        Assert.Equal(["cloudflared", "nginx"], En("tunel"));
        Assert.Equal(["ecommerce", "nginx"], En("entrada"));
        // Ni cloudflared ni Nginx están en la red general (donde viven Redis, n8n y Evolution API), y el segundo conector que saltaba Nginx no vuelve.
        Assert.Equal(["tunel"], Redes(servicios["cloudflared"]));
        Assert.Equal(["entrada", "tunel"], Redes(servicios["nginx"]));
        Assert.True(documento.RootElement.GetProperty("networks").GetProperty("entrada").GetProperty("internal").GetBoolean());
        Assert.DoesNotContain("aliases", servicios["ecommerce"].GetProperty("networks").GetRawText(), StringComparison.Ordinal);
        // Nada de la entrada publica un puerto en la VM.
        Assert.False(servicios["nginx"].TryGetProperty("ports", out _));
        Assert.False(servicios["cloudflared"].TryGetProperty("ports", out _));
        // Y del E-commerce solo sale el HTTPS entre servicios: su puerto HTTP no se publica en la VM.
        Assert.Equal([8443], servicios["ecommerce"].GetProperty("ports").EnumerateArray().Select(p => p.GetProperty("target").GetInt32()));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public void NginxSoloCreeACloudflared_YElEcommerceSoloCreeANginx_PorSusDireccionesFijas()
    {
        using var documento = JsonDocument.Parse(ComposeDeVms.ComposeResuelto("ventas", _carpeta));
        var servicios = documento.RootElement.GetProperty("services");
        var nginx = File.ReadAllText(Path.Combine(ComposeDeVms.Raiz(), "infra", "nginx", "nginx.conf"));
        var deCloudflared = DireccionFija(servicios.GetProperty("cloudflared"), "tunel");
        var deNginx = DireccionFija(servicios.GetProperty("nginx"), "entrada");

        var confiados = System.Text.RegularExpressions.Regex.Matches(nginx, @"(?m)^\s*set_real_ip_from\s+(\S+);").Select(m => m.Groups[1].Value);
        Assert.Equal([deCloudflared], confiados);
        var delGeo = System.Text.RegularExpressions.Regex.Matches(nginx, @"(?m)^\s*(\d+\.\d+\.\d+\.\d+(?:/\d+)?)\s+1;").Select(m => m.Groups[1].Value);
        Assert.Equal([deCloudflared + "/32"], delGeo);
        Assert.Equal(deNginx, servicios.GetProperty("ecommerce").GetProperty("environment").GetProperty("BABELHUB__Ecommerce__ProxiesConocidos").GetString());
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public void LaReplicaDeGestionSoloCreeALaVmDeVentas_YLaReglaDeVentasDejaPasarSoloALaDireccionDeNginx()
    {
        using var ventas = JsonDocument.Parse(ComposeDeVms.ComposeResuelto("ventas", _carpeta));
        using var inventario = JsonDocument.Parse(ComposeDeVms.ComposeResuelto("inventario", _carpeta));
        var replica = inventario.RootElement.GetProperty("services").GetProperty("ecommerce-gestion");
        var regla = File.ReadAllText(Path.Combine(ComposeDeVms.Raiz(), "scripts", "proteger-gestion.sh"));
        var nginx = File.ReadAllText(Path.Combine(ComposeDeVms.Raiz(), "infra", "nginx", "nginx.conf"));

        // El Compose exige IP_VENTAS (en la muestra, 10.0.0.10) y manda sobre el archivo de secretos.
        Assert.Equal("10.0.0.10", replica.GetProperty("environment").GetProperty("BABELHUB__Ecommerce__ProxiesConocidos").GetString());
        Assert.Contains("BABELHUB__Ecommerce__ProxiesConocidos: \"${IP_VENTAS:?", ComposeDeVms.Texto("inventario"), StringComparison.Ordinal);
        // La regla y Nginx hablan de la misma dirección (la de Nginx por la que sale a Internet y a la otra VM) y del mismo puerto.
        Assert.Contains("\nNGINX=" + DireccionFija(ventas.RootElement.GetProperty("services").GetProperty("nginx"), "tunel") + "\n", regla.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("--dport 8081 ", regla, StringComparison.Ordinal);
        Assert.Contains("server ecommerce-gestion:8081;", nginx, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void LasDireccionesFijasNoSalenDelRangoDinamico_NiElEcommerceLlevaUna_ParaQueLosComandosDeConsolaSigan()
    {
        using var documento = JsonDocument.Parse(ComposeDeVms.ComposeResuelto("ventas", _carpeta));
        var redes = documento.RootElement.GetProperty("networks");
        var servicios = documento.RootElement.GetProperty("services");
        foreach (var red in new[] { "tunel", "entrada" })
        {
            var configuracion = redes.GetProperty(red).GetProperty("ipam").GetProperty("config")[0];
            var dinamicas = System.Net.IPNetwork.Parse(configuracion.GetProperty("ip_range").GetString()!);
            foreach (var servicio in servicios.EnumerateObject())
            {
                if (servicio.Value.GetProperty("networks").TryGetProperty(red, out var enLaRed) && enLaRed.ValueKind == JsonValueKind.Object && enLaRed.TryGetProperty("ipv4_address", out var fija))
                {
                    Assert.False(dinamicas.Contains(System.Net.IPAddress.Parse(fija.GetString()!)), $"{servicio.Name} tiene en «{red}» una dirección fija dentro del rango dinámico.");
                    Assert.NotEqual("ecommerce", servicio.Name);
                }
            }
        }
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
