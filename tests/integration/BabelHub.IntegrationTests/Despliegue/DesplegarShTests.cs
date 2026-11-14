using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using BabelHub.IntegrationTests.Imagenes;

namespace BabelHub.IntegrationTests.Despliegue;

/// <summary>
/// <c>scripts/desplegar.sh</c> (PLA-04, AC-PLA-04): un comando por VM que no continúa si falta una variable, un secreto, una imagen publicada
/// o si algo está mal (permisos, clave privada de age, claves obligatorias), instala la tarea de respaldo solo en la VM de datos, levanta
/// los componentes y espera su salud, y es idempotente. Nunca imprime un valor secreto. Con un doble de <c>docker</c> sobre los Compose
/// reales de cada VM, y con <c>docker</c> de verdad sobre una VM mínima para comprobar la espera de la salud.
/// </summary>
[Collection(ProcesosPesadosCollection.Nombre)]
public sealed class DesplegarShTests : IDisposable
{
    private const string Digest = "@sha256:0000000000000000000000000000000000000000000000000000000000000000";
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "desplegar-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _valoresSecretos = [];
    private readonly string _secretos;
    private readonly string _cron;

    public DesplegarShTests()
    {
        _secretos = Path.Combine(_carpeta, "secretos");
        _cron = Path.Combine(_carpeta, "cron");
        Directory.CreateDirectory(_secretos);
        Directory.CreateDirectory(_cron);
        File.WriteAllText(Path.Combine(_carpeta, "docker-doble.sh"), DobleDeDocker.ReplaceLineEndings("\n"));
        File.WriteAllText(Path.Combine(_carpeta, "stat-doble.sh"), DobleDeStat.ReplaceLineEndings("\n"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_carpeta, recursive: true);
        }
        catch (IOException)
        {
            // Carpeta temporal.
        }
    }

    // El doble de docker anota cada llamada, responde a lo que el script pregunta y se comporta según variables de la prueba.
    private const string DobleDeDocker = """
        #!/usr/bin/env bash
        echo "$*" >>"$DOBLE_REGISTRO"
        case "$*" in
          *" config --quiet"*) exit 0 ;;
          *" config --services"*) printf '%s\n' $DOBLE_SERVICIOS; exit 0 ;;
          *" up -d --wait"*) [[ "${DOBLE_UP_FALLA:-0}" == 1 ]] && exit 1; exit 0 ;;
          *" ps -q "*) echo "id-${@: -1}"; exit 0 ;;
          inspect*)
            id="${@: -1}"; servicio="${id#id-}"
            [[ "$servicio" == "${DOBLE_NO_SANO:-}" ]] && echo unhealthy || echo healthy
            exit 0 ;;
        esac
        exit 0
        """;

    private const string DobleDeStat = """
        #!/usr/bin/env bash
        case "$3" in *"${STAT_ABIERTO:-__ninguno__}"*) echo 644 ;; *) echo 600 ;; esac
        """;

    private static string Bash()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var candidato in new[] { @"C:\Program Files\Git\bin\bash.exe", @"C:\Program Files (x86)\Git\bin\bash.exe" })
            {
                if (File.Exists(candidato))
                {
                    return candidato;
                }
            }
        }

        return "bash";
    }

    private static string EnLinux(string ruta) => ruta.Replace('\\', '/');

    private string Registro => Path.Combine(_carpeta, "docker.log");

    private string[] LlamadasADocker => File.Exists(Registro) ? File.ReadAllLines(Registro) : [];

    private static string Aleatorio(int largo)
    {
        var texto = new StringBuilder();
        while (texto.Length < largo)
        {
            texto.Append(Guid.NewGuid().ToString("N"));
        }

        return texto.ToString()[..largo];
    }

    /// <summary>Los secretos de la VM tal como deben estar: un archivo por cada uno que el Compose pide, con las claves de la tabla.</summary>
    private void PrepararSecretos(string vm)
    {
        var tabla = File.ReadAllLines(Path.Combine(ComposeDeVms.Raiz(), "infra", "secretos-requeridos.txt"))
            .Where(l => !l.TrimStart().StartsWith('#') && l.Trim().Length > 0)
            .Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToList();
        foreach (var archivo in ComposeDeVms.ArchivosDeSecretos(vm))
        {
            var contenido = new StringBuilder();
            if (archivo.EndsWith(".pem", StringComparison.Ordinal))
            {
                var valor = Aleatorio(60);
                _valoresSecretos.Add(valor);
                contenido.AppendLine(valor);
            }
            else
            {
                foreach (var fila in tabla.Where(f => f[0] == archivo))
                {
                    var valor = fila[1] switch
                    {
                        "BABELHUB_AGE_DESTINATARIO" => "age1" + Aleatorio(58),
                        "RCLONE_CONFIG_ALMACEN_TYPE" => "s3",
                        "RCLONE_CONFIG_ALMACEN_ENDPOINT" => "https://almacen.ejemplo.interno",
                        _ => Aleatorio(Math.Max(fila.Length > 2 ? int.Parse(fila[2], System.Globalization.CultureInfo.InvariantCulture) : 0, 12) + 8),
                    };
                    if (fila[1] != "RCLONE_CONFIG_ALMACEN_TYPE")
                    {
                        _valoresSecretos.Add(valor);
                    }

                    contenido.AppendLine($"{fila[1]}={valor}");
                }

                if (contenido.Length == 0)
                {
                    contenido.AppendLine("VARIABLE=" + Aleatorio(20));
                }
            }

            File.WriteAllText(Path.Combine(_secretos, archivo), contenido.ToString().ReplaceLineEndings("\n"));
        }
    }

    private Dictionary<string, string> EntornoDe(string vm)
    {
        var entorno = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var variable in ComposeDeVms.VariablesObligatorias(vm).Where(v => v != "BABELHUB_SECRETOS"))
        {
            entorno[variable] = variable.StartsWith("IMAGEN_", StringComparison.Ordinal) ? "registro.ejemplo/babelhub/" + variable[7..].ToLowerInvariant() + Digest : "10.0.0.10";
        }

        entorno["DOCKER"] = EnLinux(Path.Combine(_carpeta, "docker-doble.sh"));
        entorno["STAT"] = EnLinux(Path.Combine(_carpeta, "stat-doble.sh"));
        entorno["DOBLE_REGISTRO"] = EnLinux(Registro);
        entorno["DOBLE_SERVICIOS"] = "uno dos";
        return entorno;
    }

    private (int Codigo, string Salida, string Errores) Desplegar(string vm, Dictionary<string, string>? entorno = null, params string[] extra)
    {
        var informacion = new ProcessStartInfo(Bash()) { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        informacion.ArgumentList.Add(EnLinux(Path.Combine(ComposeDeVms.Raiz(), "scripts", "desplegar.sh")));
        informacion.ArgumentList.Add(vm);
        foreach (var argumento in extra.Length > 0 ? extra : ["--secretos", EnLinux(_secretos), "--cron", EnLinux(_cron)])
        {
            informacion.ArgumentList.Add(argumento);
        }

        foreach (var (clave, valor) in entorno ?? EntornoDe(vm))
        {
            informacion.Environment[clave] = valor;
        }

        using var proceso = Process.Start(informacion)!;
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var errores = proceso.StandardError.ReadToEndAsync();
        Assert.True(proceso.WaitForExit(TimeSpan.FromMinutes(5)), "El script no terminó a tiempo.");
        proceso.WaitForExit();
        return (proceso.ExitCode, salida.Result, errores.Result);
    }

    private void SinSecretosEnLaSalida(string salida, string errores)
    {
        foreach (var valor in _valoresSecretos)
        {
            Assert.DoesNotContain(valor, salida + errores, StringComparison.Ordinal);
        }
    }

    public static TheoryData<string> Vms => new(ComposeDeVms.Vms);

    // ---------- Despliegue correcto ----------

    [Theory]
    [MemberData(nameof(Vms))]
    [Trait("Criterio", "AC-PLA-04")]
    public void ConSusSecretos_UnSoloComandoLevantaLaVmYEsperaLaSaludDeCadaComponente(string vm)
    {
        PrepararSecretos(vm);

        var (codigo, salida, errores) = Desplegar(vm);

        Assert.True(codigo == 0, salida + errores);
        Assert.Contains($"VM {vm} desplegada y sana.", salida, StringComparison.Ordinal);
        Assert.Contains("ok uno", salida, StringComparison.Ordinal);
        Assert.Contains("ok dos", salida, StringComparison.Ordinal);
        Assert.Contains(LlamadasADocker, l => l.Contains($"docker-compose.{vm}.yml up -d --wait --wait-timeout 300", StringComparison.Ordinal));
        Assert.Contains(LlamadasADocker, l => l.Contains(" config --quiet", StringComparison.Ordinal));
        SinSecretosEnLaSalida(salida, errores);
    }

    [Theory]
    [MemberData(nameof(Vms))]
    [Trait("Criterio", "AC-PLA-04")]
    public void LaTareaDeRespaldoSoloSeInstalaEnLaVmDeDatos_Idempotente(string vm)
    {
        PrepararSecretos(vm);
        var tarea = Path.Combine(_cron, "babelhub-respaldo");

        Desplegar(vm);
        var primera = File.Exists(tarea) ? File.ReadAllText(tarea) : null;
        var (segundoCodigo, _, _) = Desplegar(vm);
        var segunda = File.Exists(tarea) ? File.ReadAllText(tarea) : null;

        Assert.Equal(0, segundoCodigo);
        if (vm == "inventario")
        {
            Assert.NotNull(primera);
            Assert.Contains("docker exec babelhub-inventario-postgres-1 /opt/babelhub/respaldar.sh", primera, StringComparison.Ordinal);
            Assert.Matches(@"(?m)^30 2 \* \* \* root docker exec ", primera);
            Assert.Equal(primera, segunda);
            Assert.DoesNotContain(Directory.GetFiles(_cron), f => Path.GetFileName(f) != "babelhub-respaldo");
        }
        else
        {
            Assert.Null(primera);
        }
    }

    // ---------- Lo que impide desplegar ----------

    [Theory]
    [MemberData(nameof(Vms))]
    [Trait("Criterio", "AC-PLA-04")]
    public void SiFaltaUnaVariable_NoSeDespliegaYSeNombraLaQueFalta_SinLlamarADocker(string vm)
    {
        PrepararSecretos(vm);
        var entorno = EntornoDe(vm);
        var primera = entorno.Keys.First(k => k.StartsWith("IP_", StringComparison.Ordinal) || k.StartsWith("IMAGEN_", StringComparison.Ordinal));
        entorno.Remove(primera);

        var (codigo, _, errores) = Desplegar(vm, entorno);

        Assert.Equal(1, codigo);
        Assert.Contains($"falta la variable {primera}", errores, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void SiNoSeIndicaLaCarpetaDeSecretos_NoSeDespliega()
    {
        var (codigo, _, errores) = Desplegar("borde", null, "--cron", EnLinux(_cron));

        Assert.Equal(1, codigo);
        Assert.Contains("falta la variable BABELHUB_SECRETOS", errores, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
    }

    [Theory]
    [InlineData("registro.ejemplo/babelhub/ecommerce:latest")]
    [InlineData("registro.ejemplo/babelhub/ecommerce@sha256:abc")]
    [InlineData("registro.ejemplo/babelhub/ecommerce")]
    public void UnaImagenPropiaSinDigest_NoSeDespliega_PorqueSoloSeDespliegaLoYaPublicado(string imagen)
    {
        PrepararSecretos("ventas");
        var entorno = EntornoDe("ventas");
        entorno["IMAGEN_ECOMMERCE"] = imagen;

        var (codigo, _, errores) = Desplegar("ventas", entorno);

        Assert.Equal(1, codigo);
        Assert.Contains("IMAGEN_ECOMMERCE no es una imagen publicada con su digest", errores, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("no-es-una-ip")]
    [InlineData("10.0.0")]
    public void UnaDireccionPrivadaInvalida_NoSeDespliega(string direccion)
    {
        PrepararSecretos("ventas");
        var entorno = EntornoDe("ventas");
        entorno["IP_PRIVADA"] = direccion;

        var (codigo, _, errores) = Desplegar("ventas", entorno);

        Assert.Equal(1, codigo);
        Assert.Contains("IP_PRIVADA no es una dirección privada válida", errores, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("inventario", "postgres.env")]
    [InlineData("ventas", "ecommerce.env")]
    [InlineData("borde", "cloudflared.env")]
    public void SiFaltaUnArchivoDeSecretos_NoSeDespliega(string vm, string archivo)
    {
        PrepararSecretos(vm);
        File.Delete(Path.Combine(_secretos, archivo));

        var (codigo, _, errores) = Desplegar(vm);

        Assert.Equal(1, codigo);
        Assert.Contains($"falta el archivo de secretos {archivo}", errores, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void SiUnArchivoDeSecretosEstaVacio_NoSeDespliega()
    {
        PrepararSecretos("borde");
        File.WriteAllText(Path.Combine(_secretos, "cloudflared.env"), string.Empty);

        var (codigo, _, errores) = Desplegar("borde");

        Assert.Equal(1, codigo);
        Assert.Contains("el archivo de secretos cloudflared.env está vacío", errores, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void SiUnArchivoDeSecretosLoPuedenLeerOtrosUsuarios_NoSeDespliega()
    {
        PrepararSecretos("borde");
        var entorno = EntornoDe("borde");
        entorno["STAT_ABIERTO"] = "cloudflared.env";

        var (codigo, salida, errores) = Desplegar("borde", entorno);

        Assert.Equal(1, codigo);
        Assert.Contains("cloudflared.env lo pueden leer otros usuarios (permisos 644)", errores, StringComparison.Ordinal);
        SinSecretosEnLaSalida(salida, errores);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void SiFaltaUnaClaveObligatoria_NoSeDespliega_NombrandoLaClaveYNoSuValor()
    {
        PrepararSecretos("inventario");
        var ruta = Path.Combine(_secretos, "postgres.env");
        File.WriteAllText(ruta, string.Join('\n', File.ReadAllLines(ruta).Where(l => !l.StartsWith("POSTGRES_INVENTARIO_PASSWORD=", StringComparison.Ordinal))) + "\n");

        var (codigo, salida, errores) = Desplegar("inventario");

        Assert.Equal(1, codigo);
        Assert.Contains("falta la clave POSTGRES_INVENTARIO_PASSWORD en postgres.env", errores, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
        SinSecretosEnLaSalida(salida, errores);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void SiUnaClaveGeneradaEsDemasiadoCorta_NoSeDespliega_SinImprimirSuValor()
    {
        PrepararSecretos("inventario");
        var corta = "corta-" + Aleatorio(4);
        var ruta = Path.Combine(_secretos, "postgres.env");
        File.WriteAllText(ruta, string.Join('\n', File.ReadAllLines(ruta).Select(l => l.StartsWith("POSTGRES_PASSWORD=", StringComparison.Ordinal) ? "POSTGRES_PASSWORD=" + corta : l)) + "\n");
        _valoresSecretos.Add(corta);

        var (codigo, salida, errores) = Desplegar("inventario");

        Assert.Equal(1, codigo);
        Assert.Contains("la clave POSTGRES_PASSWORD de postgres.env tiene menos de 24 caracteres", errores, StringComparison.Ordinal);
        SinSecretosEnLaSalida(salida, errores);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public void SiHayUnaClavePrivadaDeAgeEnLosSecretos_NoSeDespliega_SinImprimirla()
    {
        PrepararSecretos("inventario");
        var privada = "AGE-SECRET-KEY-" + Aleatorio(40).ToUpperInvariant();
        File.AppendAllText(Path.Combine(_secretos, "postgres.env"), "OTRA=" + privada + "\n");

        var (codigo, salida, errores) = Desplegar("inventario");

        Assert.Equal(1, codigo);
        Assert.Contains("contiene una clave privada de age", errores, StringComparison.Ordinal);
        Assert.DoesNotContain(privada, salida + errores, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-17")]
    public void ElDestinatarioDeAgeDebeSerUnaClavePublica()
    {
        PrepararSecretos("inventario");
        var ruta = Path.Combine(_secretos, "postgres.env");
        File.WriteAllText(ruta, string.Join('\n', File.ReadAllLines(ruta).Select(l => l.StartsWith("BABELHUB_AGE_DESTINATARIO=", StringComparison.Ordinal) ? "BABELHUB_AGE_DESTINATARIO=algo-que-no-es-age" : l)) + "\n");

        var (codigo, _, errores) = Desplegar("inventario");

        Assert.Equal(1, codigo);
        Assert.Contains("no es una clave pública de age", errores, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void SiFaltanVariosSecretos_SeListanTodosDeUnaVez()
    {
        PrepararSecretos("ventas");
        File.Delete(Path.Combine(_secretos, "n8n.env"));
        File.Delete(Path.Combine(_secretos, "redis.env"));

        var (codigo, _, errores) = Desplegar("ventas");

        Assert.Equal(1, codigo);
        Assert.Contains("n8n.env", errores, StringComparison.Ordinal);
        Assert.Contains("redis.env", errores, StringComparison.Ordinal);
        Assert.Matches(@"faltan o están mal [2-9]", errores);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void LaVmDeDatosSinCron_NoSeDesplieganiSeInstalaNada()
    {
        PrepararSecretos("inventario");

        var (codigo, _, errores) = Desplegar("inventario", null, "--secretos", EnLinux(_secretos), "--cron", EnLinux(Path.Combine(_carpeta, "no-existe")));

        Assert.Equal(1, codigo);
        Assert.Contains("la VM de datos necesita cron", errores, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
    }

    // ---------- Lo que pasa al levantar ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void SiUnComponenteNoQuedaSano_ElDespliegueFalla_NombrandoloYSinInstalarElRespaldo()
    {
        PrepararSecretos("inventario");
        var entorno = EntornoDe("inventario");
        entorno["DOBLE_NO_SANO"] = "dos";

        var (codigo, _, errores) = Desplegar("inventario", entorno);

        Assert.Equal(1, codigo);
        Assert.Contains("x dos: unhealthy", errores, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_cron, "babelhub-respaldo")));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void SiDockerComposeNoLograLevantarTodo_ElDespliegueFalla()
    {
        PrepararSecretos("inventario");
        var entorno = EntornoDe("inventario");
        entorno["DOBLE_UP_FALLA"] = "1";

        var (codigo, _, errores) = Desplegar("inventario", entorno);

        Assert.Equal(1, codigo);
        Assert.Contains("no quedó sano", errores, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_cron, "babelhub-respaldo")));
    }

    [Theory]
    [InlineData]
    [InlineData("nube")]
    [InlineData("borde", "--espera", "abc")]
    [InlineData("borde", "--desconocida")]
    public async Task UnUsoInvalido_SeRechazaConCodigo2(params string[] argumentos)
    {
        var informacion = new ProcessStartInfo(Bash()) { RedirectStandardOutput = true, RedirectStandardError = true };
        informacion.ArgumentList.Add(EnLinux(Path.Combine(ComposeDeVms.Raiz(), "scripts", "desplegar.sh")));
        foreach (var argumento in argumentos)
        {
            informacion.ArgumentList.Add(argumento);
        }

        using var proceso = Process.Start(informacion)!;
        var errores = proceso.StandardError.ReadToEndAsync();
        Assert.True(proceso.WaitForExit(TimeSpan.FromMinutes(1)));
        proceso.WaitForExit();

        Assert.Equal(2, proceso.ExitCode);
        Assert.Contains("Uso:", await errores, StringComparison.Ordinal);
    }

    // ---------- Con docker de verdad, sobre una VM mínima ----------

    private string VmMinima(string saludable)
    {
        var infra = Path.Combine(_carpeta, "infra");
        Directory.CreateDirectory(infra);
        File.WriteAllText(
            Path.Combine(infra, "docker-compose.borde.yml"),
            $$"""
            name: bhdesp{{Guid.NewGuid().ToString("N")[..8]}}
            services:
              cache:
                image: redis:7-alpine@sha256:7f7d30b7fdd09467bac58198a1f166dd69789d348d4fe859cccb20e0cf287fbe
                env_file:
                  - "${BABELHUB_SECRETOS:?}/cache.env"
                healthcheck:
                  test: {{saludable}}
                  interval: 2s
                  timeout: 2s
                  retries: 2
                  start_period: 1s
            """.ReplaceLineEndings("\n"));
        File.WriteAllText(Path.Combine(infra, "secretos-requeridos.txt"), "cache.env CLAVE 8\n");
        File.WriteAllText(Path.Combine(_secretos, "cache.env"), "CLAVE=" + Aleatorio(20) + "\n");
        return infra;
    }

    private (int Codigo, string Salida, string Errores) DesplegarMinima(string infra, string espera)
    {
        var entorno = new Dictionary<string, string> { ["STAT"] = EnLinux(Path.Combine(_carpeta, "stat-doble.sh")) };
        return Desplegar("borde", entorno, "--secretos", EnLinux(_secretos), "--infra", EnLinux(infra), "--espera", espera);
    }

    private void Retirar(string infra)
    {
        var compose = Path.Combine(infra, "docker-compose.borde.yml");
        ComposeDeVms.Docker(TimeSpan.FromMinutes(2), new Dictionary<string, string> { ["BABELHUB_SECRETOS"] = EnLinux(_secretos) }, "compose", "--project-directory", EnLinux(infra), "-f", EnLinux(compose), "down", "-v", "--remove-orphans");
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void ConDockerDeVerdad_UnaVmSanaQuedaEnMarchaYRepetirElComandoNoLaCambia()
    {
        var infra = VmMinima("[\"CMD\", \"redis-cli\", \"ping\"]");
        try
        {
            var (codigo, salida, errores) = DesplegarMinima(infra, "60");
            var (segundo, salida2, _) = DesplegarMinima(infra, "60");

            Assert.True(codigo == 0, salida + errores);
            Assert.Contains("ok cache", salida, StringComparison.Ordinal);
            Assert.Equal(0, segundo);
            Assert.Contains("ok cache", salida2, StringComparison.Ordinal);
        }
        finally
        {
            Retirar(infra);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void ConDockerDeVerdad_UnComponenteQueNoResponde_HaceFallarElDespliegue()
    {
        var infra = VmMinima("[\"CMD\", \"false\"]");
        try
        {
            var (codigo, _, errores) = DesplegarMinima(infra, "20");

            Assert.Equal(1, codigo);
            Assert.Contains("no quedó sano", errores, StringComparison.Ordinal);
        }
        finally
        {
            Retirar(infra);
        }
    }

    // ---------- La tabla de secretos ----------

    [Fact]
    public void LaTablaDeSecretosSoloTieneNombresDeClave_YSoloArchivosQueAlgunComposeUsa()
    {
        var usados = ComposeDeVms.Vms.SelectMany(ComposeDeVms.ArchivosDeSecretos).ToHashSet(StringComparer.Ordinal);
        var filas = File.ReadAllLines(Path.Combine(ComposeDeVms.Raiz(), "infra", "secretos-requeridos.txt"))
            .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'))
            .ToList();

        Assert.NotEmpty(filas);
        Assert.All(filas, f =>
        {
            Assert.DoesNotContain('=', f);
            Assert.Matches(@"^[\w.-]+\.env\s+[A-Za-z_][A-Za-z0-9_]*(\s+\d+)?\s*$", f);
            Assert.Contains(f.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0], usados);
        });
        foreach (var archivo in usados.Where(a => a.EndsWith(".env", StringComparison.Ordinal)))
        {
            Assert.Contains(filas, f => f.StartsWith(archivo + " ", StringComparison.Ordinal));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void ElScriptNoImprimeNingunaVariableDeEntornoNiExpandeSecretos()
    {
        var texto = File.ReadAllText(Path.Combine(ComposeDeVms.Raiz(), "scripts", "desplegar.sh"));

        // El único printf del valor es el que devuelve la lectura a quien la captura (nunca va a la pantalla); ningún echo lo muestra.
        Assert.Single(Regex.Matches(texto, @"printf '%s' ""\$valor""", RegexOptions.None, TimeSpan.FromSeconds(2)));
        Assert.DoesNotMatch(@"(?m)^\s*echo\b.*\$\{?valor", texto);
        Assert.DoesNotMatch(@"(?m)^\s*set\s+-[a-z]*x", texto);
        Assert.Matches(@"(?m)^set -euo pipefail$", texto);
        Assert.Matches(new Regex(@"up -d --wait", RegexOptions.None, TimeSpan.FromSeconds(2)), texto);
    }
}
