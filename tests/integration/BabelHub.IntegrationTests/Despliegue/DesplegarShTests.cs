using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.IntegrationTests.Imagenes;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

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
        ArchivosDeDoble.Escribir(Path.Combine(_carpeta, "docker-doble.sh"), DobleDeDocker.ReplaceLineEndings("\n"));
        ArchivosDeDoble.Escribir(Path.Combine(_carpeta, "stat-doble.sh"), DobleDeStat.ReplaceLineEndings("\n"));
        ArchivosDeDoble.Escribir(Path.Combine(_carpeta, "iptables-doble.sh"), DobleDeIptables.ReplaceLineEndings("\n"));
    }

    public void Dispose()
    {
        try
        {
            // El script deja las llaves TLS en solo lectura (400): en Windows eso impide borrarlas.
            foreach (var archivo in Directory.EnumerateFiles(_carpeta, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(archivo, FileAttributes.Normal);
            }

            Directory.Delete(_carpeta, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
          *" run --rm "*) [[ "${DOBLE_RUN_FALLA:-0}" == 1 ]] && exit 1; exit 0 ;;
          *" ps -q "*) echo "id-${@: -1}"; exit 0 ;;
          inspect*)
            id="${@: -1}"; servicio="${id#id-}"
            [[ "$servicio" == "${DOBLE_NO_SANO:-}" ]] && echo unhealthy || echo healthy
            exit 0 ;;
        esac
        exit 0
        """;

    // El doble de iptables anota la llamada en el mismo registro que docker (para comprobar el orden): `-C` dice si la regla ya está.
    private const string DobleDeIptables = """
        #!/usr/bin/env bash
        echo "iptables $*" >>"$DOBLE_REGISTRO"
        case "$*" in
          *" -C "*) [[ "${DOBLE_REGLA_PUESTA:-0}" == 1 ]] && exit 0; exit 1 ;;
          *" -I "*) [[ "${DOBLE_IPTABLES_FALLA:-0}" == 1 ]] && exit 1; exit 0 ;;
        esac
        exit 0
        """;

    private const string DobleDeStat = """
        #!/usr/bin/env bash
        if [[ "$2" == '%U %a' ]]; then
          case "$3" in
            *aviso.env) case "$3" in *"${STAT_ABIERTO:-__ninguno__}"*) echo "${STAT_DUENO_AVISO:-root} 644" ;; *) echo "${STAT_DUENO_AVISO:-root} 600" ;; esac ;;
            */scripts/vm-aux) echo "${STAT_DUENO_CARPETA:-root} ${STAT_MODO_CARPETA:-755}" ;;
            "${DOBLE_RAIZ:-__ninguna__}") echo "${STAT_DUENO_RAIZ:-root} 755" ;;
            *) echo "${STAT_DUENO:-root} ${STAT_MODO_SCRIPTS:-755}" ;;
          esac
          exit 0
        fi
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

    private static string CadenaDeBase(string clave) => $"Host=10.0.1.10;Port=5432;Database=base;Username=cuenta;Pass{"word"}={clave};SSL Mode=VerifyFull;GSS Encryption Mode=Disable";

    private static string UriDeEvolution(string clave) => $"postgresql://evolution:{clave}@10.0.1.10:5432/evolution?schema=public&sslmode=require&sslaccept=strict&sslcert=/run/secrets/tls-ca.pem";

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
                        // PAG-34: el entorno y la pasarela del E-commerce deben ser coherentes (Staging con el simulador).
                        "ASPNETCORE_ENVIRONMENT" => "Staging",
                        "BABELHUB__Pagos__Pasarela" => "simulador",
                        "BABELHUB__Envios__Operador" => "simulador",
                        // AM24: las cadenas de conexión a PostgreSQL piden TLS verificado (si no, desplegar se niega).
                        var cadena when cadena.StartsWith("BABELHUB__Inventario__Base__", StringComparison.Ordinal) || cadena.StartsWith("BABELHUB__Ecommerce__Base__", StringComparison.Ordinal) => CadenaDeBase(Aleatorio(32)),
                        "DATABASE_CONNECTION_URI" => UriDeEvolution(Aleatorio(32)),
                        _ => Aleatorio(Math.Max(fila.Length > 2 ? int.Parse(fila[2], System.Globalization.CultureInfo.InvariantCulture) : 0, 12) + 8),
                    };
                    // Lo que no es un secreto (el tipo del almacén, el entorno y la pasarela) no se vigila en la salida del script: sus mensajes lo nombran.
                    if (fila[1] is not ("RCLONE_CONFIG_ALMACEN_TYPE" or "ASPNETCORE_ENVIRONMENT" or "BABELHUB__Pagos__Pasarela" or "BABELHUB__Envios__Operador"))
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

            var destinoDelArchivo = Path.Combine(_secretos, archivo);
            if (File.Exists(destinoDelArchivo))
            {
                File.SetAttributes(destinoDelArchivo, FileAttributes.Normal); // un despliegue anterior lo dejó en 400
            }

            File.WriteAllText(destinoDelArchivo, contenido.ToString().ReplaceLineEndings("\n"));
        }

        if (vm == "inventario")
        {
            // Las cuentas propietarias de las bases (COMP-2): solo la VM de datos las tiene, aparte de los archivos de los servicios.
            var claveInventario = Aleatorio(60);
            var claveEcommerce = Aleatorio(60);
            _valoresSecretos.Add(claveInventario);
            _valoresSecretos.Add(claveEcommerce);
            File.WriteAllText(
                Path.Combine(_secretos, "migrador.env"),
                $"BABELHUB__Migrador__Inventario__Propietaria=Host=postgres;Username=postgres;Pass{"word"}={claveInventario};SSL Mode=VerifyFull\nBABELHUB__Migrador__Ecommerce__Propietaria=Host=postgres;Username=postgres;Pass{"word"}={claveEcommerce};SSL Mode=VerifyFull\n");

            // La credencial de correo propia de la VM de datos (PLA-21): sirve solo para avisar del espacio reservado.
            var claveDeCorreo = Aleatorio(24);
            _valoresSecretos.Add(claveDeCorreo);
            File.WriteAllText(
                Path.Combine(_secretos, "aviso.env"),
                "AUX_SMTP_URL=smtps://smtp.ejemplo.interno:465\n" + $"AUX_SMTP_USUARIO=avisos-de-vm-inventario\nAUX_SMTP_CLAVE={claveDeCorreo}\n" + "AUX_CORREO_DE=vm-inventario@ejemplo.interno\nAUX_CORREO_PARA=responsable@ejemplo.interno\n");
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
        entorno["IPTABLES"] = EnLinux(Path.Combine(_carpeta, "iptables-doble.sh"));
        entorno["DOBLE_REGISTRO"] = EnLinux(Registro);
        entorno["DOBLE_SERVICIOS"] = "uno dos";
        // Evolution API monta la CA privada (tls-ca.pem, AM24): con ella desplegar arma el paquete de raíces y necesita las públicas de la VM, que un equipo de desarrollo (Windows) no tiene en /etc/ssl.
        var raices = Path.Combine(_carpeta, "raices-publicas-doble.crt");
        File.WriteAllText(raices, "-----BEGIN CERTIFICATE-----\nRAIZ-PUBLICA-DE-PRUEBA\n-----END CERTIFICATE-----\n");
        entorno["BABELHUB_RAICES_PUBLICAS"] = EnLinux(raices);
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

        // Las pruebas no corren como root: la entrega de las llaves TLS a sus usuarios de contenedor (chown) se sustituye por un doble inocuo.
        informacion.Environment["CHOWN"] = "true";
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

    // ---------- Vigilancia del espacio reservado de la VM de datos (PLA-21) ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-21")]
    public void LaVmDeDatos_InstalaLaVigilanciaDelEspacioCadaMinuto_ConSuCredencialDeCorreoPropia()
    {
        PrepararSecretos("inventario");

        var (codigo, salida, errores) = Desplegar("inventario");

        Assert.True(codigo == 0, salida + errores);
        var tarea = File.ReadAllText(Path.Combine(_cron, "babelhub-respaldo"));
        var linea = tarea.ReplaceLineEndings("\n").Split('\n').Single(l => l.Contains("vigilar-espacio.sh", StringComparison.Ordinal));
        Assert.StartsWith("* * * * * root ", linea, StringComparison.Ordinal);
        Assert.Contains($"AUX_ENV={EnLinux(_secretos)}/aviso.env", linea, StringComparison.Ordinal);
        Assert.Matches(@" AUX_ESTADO=/var/lib/babelhub-aviso \S+/scripts/vm-aux/alertar\.sh espacio-respaldos docker exec babelhub-inventario-postgres-1 /opt/babelhub/vigilar-espacio\.sh$", linea);
        SinSecretosEnLaSalida(tarea, string.Empty);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-21")]
    public void SinLaCredencialDeCorreoDeAviso_LaVmDeDatosNoSeDespliega_PorqueElAvisoDelOchentaPorCientoNoSaldria()
    {
        PrepararSecretos("inventario");
        File.Delete(Path.Combine(_secretos, "aviso.env"));

        var (codigo, _, errores) = Desplegar("inventario");

        Assert.Equal(1, codigo);
        Assert.Contains("falta el archivo de secretos aviso.env", errores, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-21")]
    public void ElAvisoEnv_ConPermisosAbiertos_SinUnaClave_ConSmtpSinTlsOConUnaClavePrivadaDeAge_NoSeDespliega()
    {
        PrepararSecretos("inventario");
        var ruta = Path.Combine(_secretos, "aviso.env");
        var valor = Aleatorio(30);
        _valoresSecretos.Add(valor);
        File.WriteAllText(ruta, "AUX_SMTP_URL=smtp://smtp.ejemplo.interno:25\n" + "AUX_SMTP_USUARIO=avisos\nAUX_CORREO_DE=de@ejemplo.interno\nAUX_CORREO_PARA=para@ejemplo.interno\nOTRO=AGE-SECRET-" + "KEY-" + valor.ToUpperInvariant() + "\n");
        var entorno = EntornoDe("inventario");
        entorno["STAT_ABIERTO"] = "aviso.env";

        var (codigo, salida, errores) = Desplegar("inventario", entorno);

        Assert.Equal(1, codigo);
        Assert.Contains("aviso.env es de root con permisos 644", errores, StringComparison.Ordinal);
        Assert.Contains("falta la clave AUX_SMTP_CLAVE en aviso.env", errores, StringComparison.Ordinal);
        Assert.Contains("AUX_SMTP_URL de aviso.env usa smtp:// sin TLS implícito", errores, StringComparison.Ordinal);
        Assert.Contains("aviso.env contiene una clave privada de age", errores, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
        SinSecretosEnLaSalida(salida, errores);
    }

    [Theory]
    [InlineData("babelhub-aux", "755")]
    [InlineData("root", "775")]
    [InlineData("root", "666")]
    [Trait("Criterio", "AC-PLA-21")]
    public void SiLosScriptsQueElCronCorreComoRootLosPuedeModificarOtro_LaVmDeDatosNoSeDespliega(string dueno, string modo)
    {
        PrepararSecretos("inventario");
        var entorno = EntornoDe("inventario");
        entorno["STAT_DUENO"] = dueno;
        entorno["STAT_MODO_SCRIPTS"] = modo;

        var (codigo, _, errores) = Desplegar("inventario", entorno);

        Assert.Equal(1, codigo);
        Assert.Contains("debe ser de root y sin escritura para otros (lo ejecuta el cron como root)", errores, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
    }

    [Theory]
    [InlineData("STAT_DUENO_AVISO", "babelhub-aux", "aviso.env es de babelhub-aux")]
    [InlineData("STAT_DUENO_CARPETA", "babelhub-aux", "la carpeta scripts/vm-aux es de babelhub-aux")]
    [InlineData("STAT_MODO_CARPETA", "775", "la carpeta scripts/vm-aux es de root con permisos 775")]
    [Trait("Criterio", "AC-PLA-21")]
    public void SiElAvisoEnvOLaCarpetaDeLosScriptsDeRootNoSonDeRoot_LaVmDeDatosNoSeDespliega(string variable, string valor, string esperado)
    {
        PrepararSecretos("inventario");
        var entorno = EntornoDe("inventario");
        entorno[variable] = valor;

        var (codigo, _, errores) = Desplegar("inventario", entorno);

        Assert.Equal(1, codigo);
        Assert.Contains(esperado, errores, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-21")]
    public void LasOtrasVms_NoPidenNiInstalanLaVigilanciaDelEspacio()
    {
        PrepararSecretos("ventas");

        var (codigo, salida, errores) = Desplegar("ventas");

        Assert.True(codigo == 0, salida + errores);
        Assert.Equal(["babelhub-gestion"], Directory.GetFiles(_cron).Select(Path.GetFileName));
        Assert.DoesNotContain("vigilar-espacio.sh", File.ReadAllText(Path.Combine(_cron, "babelhub-gestion")), StringComparison.Ordinal);
        Assert.DoesNotContain("aviso.env", salida + errores, StringComparison.Ordinal);
    }

    // ---------- Valores «CAMBIAR» y --sin (primer despliegue, guía 08) ----------

    [Theory]
    [InlineData("ecommerce.env")]
    [InlineData("cloudflared.env")]
    [Trait("Criterio", "AC-PLA-04")]
    public void UnValorCambiarSinCompletar_NoSeDespliega_NombrandoElArchivo_SinLlamarADocker(string archivo)
    {
        PrepararSecretos("ventas");
        File.AppendAllText(Path.Combine(_secretos, archivo), "BABELHUB__Correo__Smtp__Credencial=CAMBIAR\n");

        var (codigo, salida, errores) = Desplegar("ventas");

        Assert.Equal(1, codigo);
        Assert.Contains($"el archivo de secretos {archivo} tiene valores «CAMBIAR» sin completar", errores, StringComparison.Ordinal);
        Assert.DoesNotContain(LlamadasADocker, l => l.Contains(" up ", StringComparison.Ordinal));
        SinSecretosEnLaSalida(salida, errores);
    }

    // ---------- El entorno del E-commerce y su pasarela (PAG-34; AC-PAG-30): nunca se cobra «a ciegas» ----------

    private static string LlaveDeCulqi(string prefijo) => prefijo + Guid.NewGuid().ToString("N");

    [Theory]
    [Trait("Criterio", "AC-PAG-30")]
    [InlineData("ventas", "ecommerce.env", "ninguna")]
    [InlineData("inventario", "ecommerce-gestion.env", "ninguna")]
    [InlineData("ventas", "ecommerce.env", "simulador")]
    [InlineData("inventario", "ecommerce-gestion.env", "simulador")]
    public void StagingSeDespliegaSinPasarelaOConElSimulador(string vm, string archivo, string pasarela)
    {
        PrepararSecretos(vm);
        File.AppendAllText(Path.Combine(_secretos, archivo), "BABELHUB__Pagos__Pasarela=" + pasarela + "\n");

        var (codigo, salida, errores) = Desplegar(vm);

        Assert.True(codigo == 0, salida + errores);
        SinSecretosEnLaSalida(salida, errores);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-30")]
    public void ElGenerador_EnStaging_EscribeSinPasarelaPorDefecto_EnLosDosArchivosDelEcommerce()
    {
        var destino = Path.Combine(_carpeta, "generados-sin-pasarela");

        var (codigo, salida, errores) = Generar(destino);

        Assert.True(codigo == 0, salida + errores);
        foreach (var (carpeta, archivo) in new[] { ("ventas", "ecommerce.env"), ("inventario", "ecommerce-gestion.env") })
        {
            Assert.Equal("Staging", Valor(Path.Combine(destino, carpeta), archivo, "ASPNETCORE_ENVIRONMENT"));
            Assert.Equal("ninguna", Valor(Path.Combine(destino, carpeta), archivo, "BABELHUB__Pagos__Pasarela"));
            Assert.DoesNotContain("BABELHUB__Pagos__LlavePrivada", File.ReadAllText(Path.Combine(destino, carpeta, archivo)), StringComparison.Ordinal);
        }
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-30")]
    [InlineData("simulador")]
    [InlineData("ninguna")]
    public void ElGenerador_EnStaging_EscribeLaPasarelaPedida(string pasarela)
    {
        var destino = Path.Combine(_carpeta, "generados-con-pasarela");

        var (codigo, salida, errores) = Generar(destino, "--pasarela", pasarela);

        Assert.True(codigo == 0, salida + errores);
        Assert.Equal(pasarela, Valor(Path.Combine(destino, "ventas"), "ecommerce.env", "BABELHUB__Pagos__Pasarela"));
        Assert.Equal(pasarela, Valor(Path.Combine(destino, "inventario"), "ecommerce-gestion.env", "BABELHUB__Pagos__Pasarela"));
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-30")]
    [InlineData("staging", "culqi")]
    [InlineData("staging", "stripe")]
    [InlineData("production", "ninguna")]
    [InlineData("production", "simulador")]
    public void ElGenerador_RechazaUnaPasarelaQueNoEsDelEntorno_SinEscribirNada(string entorno, string pasarela)
    {
        var destino = Path.Combine(_carpeta, "generados-rechazados");

        var (codigo, _, errores) = Generar(destino, "--entorno", entorno, "--pasarela", pasarela);

        Assert.Equal(2, codigo);
        Assert.Contains("--pasarela", errores, StringComparison.Ordinal);
        Assert.False(Directory.Exists(destino));
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-30")]
    [InlineData("ventas", "ecommerce.env", "BABELHUB__Pagos__Pasarela=culqi", "Staging solo admite BABELHUB__Pagos__Pasarela=ninguna o simulador")]
    [InlineData("inventario", "ecommerce-gestion.env", "BABELHUB__Pagos__Pasarela=culqi", "Staging solo admite BABELHUB__Pagos__Pasarela=ninguna o simulador")]
    [InlineData("ventas", "ecommerce.env", "ASPNETCORE_ENVIRONMENT=Production", "Production exige BABELHUB__Pagos__Pasarela=culqi")]
    [InlineData("inventario", "ecommerce-gestion.env", "ASPNETCORE_ENVIRONMENT=Production", "Production exige BABELHUB__Pagos__Pasarela=culqi")]
    [InlineData("ventas", "ecommerce.env", "BABELHUB__Pagos__Pasarela=Ninguna", "Staging solo admite BABELHUB__Pagos__Pasarela=ninguna o simulador")]
    [InlineData("ventas", "ecommerce.env", "BABELHUB__Pagos__Pasarela=ninguno", "Staging solo admite BABELHUB__Pagos__Pasarela=ninguna o simulador")]
    [InlineData("ventas", "ecommerce.env", "ASPNETCORE_ENVIRONMENT=Production\nBABELHUB__Pagos__Pasarela=ninguna", "Production exige BABELHUB__Pagos__Pasarela=culqi")]
    [InlineData("inventario", "ecommerce-gestion.env", "ASPNETCORE_ENVIRONMENT=Production\nBABELHUB__Pagos__Pasarela=ninguna", "Production exige BABELHUB__Pagos__Pasarela=culqi")]
    [InlineData("ventas", "ecommerce.env", "ASPNETCORE_ENVIRONMENT=Qa", "debe ser Staging o Production")]
    [InlineData("ventas", "ecommerce.env", "ASPNETCORE_ENVIRONMENT=Development", "debe ser Staging o Production")]
    [InlineData("ventas", "ecommerce.env", "ASPNETCORE_ENVIRONMENT=production", "debe ser Staging o Production")]
    public void UnEntornoDelEcommerceIncoherenteConSuPasarela_NoSeDespliega_NombrandoElArchivo_SinLlamarADocker(string vm, string archivo, string linea, string mensaje)
    {
        PrepararSecretos(vm);
        File.AppendAllText(Path.Combine(_secretos, archivo), linea + "\n");

        var (codigo, salida, errores) = Desplegar(vm);

        Assert.Equal(1, codigo);
        Assert.Contains(archivo + ": ASPNETCORE_ENVIRONMENT", errores, StringComparison.Ordinal);
        Assert.Contains(mensaje, errores, StringComparison.Ordinal);
        Assert.DoesNotContain(LlamadasADocker, l => l.Contains(" up ", StringComparison.Ordinal));
        SinSecretosEnLaSalida(salida, errores);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-37")]
    public void UnOperadorLogisticoQueNoEsElSimulado_NoSeDespliega_PorqueNoHayOperadorReal()
    {
        PrepararSecretos("ventas");
        File.AppendAllText(Path.Combine(_secretos, "ecommerce.env"), "BABELHUB__Envios__Operador=nulo\n");

        var (codigo, _, errores) = Desplegar("ventas");

        Assert.Equal(1, codigo);
        Assert.Contains("BABELHUB__Envios__Operador debe ser simulador", errores, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-30")]
    [InlineData(null)]
    [InlineData("sk_test_")]
    [InlineData("sk_live_")]
    public void ProductionConCulqi_ExigeLaLlaveDeProduccion_YNuncaImprimeLaLlave(string? prefijoDeLaLlave)
    {
        PrepararSecretos("ventas");
        var archivo = Path.Combine(_secretos, "ecommerce.env");
        File.AppendAllText(archivo, "ASPNETCORE_ENVIRONMENT=Production\nBABELHUB__Pagos__Pasarela=culqi\n");
        if (prefijoDeLaLlave is not null)
        {
            var llave = LlaveDeCulqi(prefijoDeLaLlave);
            _valoresSecretos.Add(llave);
            File.AppendAllText(archivo, "BABELHUB__Pagos__LlavePrivada=" + llave + "\n");
        }

        var (codigo, salida, errores) = Desplegar("ventas");

        if (prefijoDeLaLlave == "sk_live_")
        {
            Assert.True(codigo == 0, salida + errores);
        }
        else
        {
            Assert.Equal(1, codigo);
            Assert.Contains("ecommerce.env: ASPNETCORE_ENVIRONMENT=Production exige BABELHUB__Pagos__LlavePrivada con la llave de producción de Culqi (sk_live_…)", errores, StringComparison.Ordinal);
            Assert.DoesNotContain(LlamadasADocker, l => l.Contains(" up ", StringComparison.Ordinal));
        }

        SinSecretosEnLaSalida(salida, errores);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-30")]
    public void StagingNoLlevaLlaveDeCulqi_NiLaDeProduccionNiLaDePruebas()
    {
        PrepararSecretos("ventas");
        var llave = LlaveDeCulqi("sk_live_");
        _valoresSecretos.Add(llave);
        File.AppendAllText(Path.Combine(_secretos, "ecommerce.env"), "BABELHUB__Pagos__LlavePrivada=" + llave + "\n");

        var (codigo, salida, errores) = Desplegar("ventas");

        Assert.Equal(1, codigo);
        Assert.Contains("ASPNETCORE_ENVIRONMENT=Staging no debe llevar BABELHUB__Pagos__LlavePrivada", errores, StringComparison.Ordinal);
        SinSecretosEnLaSalida(salida, errores);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-30")]
    public void SinASPNETCORE_ENVIRONMENT_NoSeDespliegaElEcommerce_NombrandoLaClave()
    {
        PrepararSecretos("ventas");
        var ruta = Path.Combine(_secretos, "ecommerce.env");
        File.WriteAllLines(ruta, File.ReadAllLines(ruta).Where(l => !l.StartsWith("ASPNETCORE_ENVIRONMENT=", StringComparison.Ordinal)));

        var (codigo, _, errores) = Desplegar("ventas");

        Assert.Equal(1, codigo);
        Assert.Contains("falta la clave ASPNETCORE_ENVIRONMENT en ecommerce.env", errores, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-30")]
    public void ElComposeNoFijaElEntornoDelEcommerce_QueSalgaDeSuArchivoDeSecretos()
    {
        foreach (var compose in new[] { "docker-compose.ventas.yml", "docker-compose.inventario.yml" })
        {
            var texto = File.ReadAllText(Path.Combine(ComposeDeVms.Raiz(), "infra", compose));
            var servicioDelEcommerce = compose.Contains("ventas", StringComparison.Ordinal) ? "ecommerce" : "ecommerce-gestion";
            var bloque = texto[texto.IndexOf($"\n  {servicioDelEcommerce}:\n", StringComparison.Ordinal)..];
            bloque = bloque[..bloque.IndexOf("\n    env_file:", StringComparison.Ordinal)];
            var lineasQueFijanElEntorno = bloque.Split('\n').Where(l => !l.TrimStart().StartsWith('#') && l.Contains("ASPNETCORE_ENVIRONMENT", StringComparison.Ordinal));

            Assert.Empty(lineasQueFijanElEntorno);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-33")]
    public void LaVersionDeLosTextosViajaSoloALaMigracionDelEcommerce_ComoNombre_YNuncaEnLosArgumentos()
    {
        PrepararSecretos("inventario");
        var version = "0.1-provisional-" + Aleatorio(8);
        File.AppendAllText(Path.Combine(_secretos, "migrador.env"), "BABELHUB__Migrador__Ecommerce__VersionDeTextos=" + version + "\n");

        var (codigo, salida, errores) = Desplegar("inventario");

        Assert.True(codigo == 0, salida + errores);
        var corridas = LlamadasADocker.Where(l => l.Contains(" run --rm ", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, corridas.Count);
        Assert.DoesNotContain("VersionDeTextos", corridas.Single(l => l.EndsWith(" inventario migrar", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains("-e BABELHUB__Migrador__Ecommerce__VersionDeTextos", corridas.Single(l => l.EndsWith(" ecommerce-gestion migrar", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.DoesNotContain(version, string.Join("\n", LlamadasADocker) + salida + errores, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-33")]
    public void SinLaVersionDeLosTextos_LaMigracionNoLlevaLaVariable()
    {
        PrepararSecretos("inventario");

        var (codigo, salida, errores) = Desplegar("inventario");

        Assert.True(codigo == 0, salida + errores);
        Assert.DoesNotContain(LlamadasADocker, l => l.Contains("VersionDeTextos", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void ConSin_SoloSeLevantanLosServiciosQueQuedan_YSeEsperaLaSaludDeEsos()
    {
        PrepararSecretos("ventas");

        var (codigo, salida, errores) = Desplegar("ventas", null, "--secretos", EnLinux(_secretos), "--cron", EnLinux(_cron), "--sin", "dos");

        Assert.True(codigo == 0, salida + errores);
        Assert.Contains("Se deja sin levantar: dos", salida, StringComparison.Ordinal);
        Assert.Contains("ok uno", salida, StringComparison.Ordinal);
        Assert.DoesNotContain("ok dos", salida, StringComparison.Ordinal);
        Assert.Contains(LlamadasADocker, l => l.EndsWith("up -d --wait --wait-timeout 300 uno", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void ConSinDeUnServicioQueNoExiste_NoSeDespliega_PorUnErrorDeEscritura()
    {
        PrepararSecretos("ventas");

        var (codigo, _, errores) = Desplegar("ventas", null, "--secretos", EnLinux(_secretos), "--cron", EnLinux(_cron), "--sin", "evolucion-api");

        Assert.True(codigo == 2, errores);
        Assert.Contains("--sin: ventas no tiene el servicio «evolucion-api»", errores, StringComparison.Ordinal);
        Assert.DoesNotContain(LlamadasADocker, l => l.Contains(" up ", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void ElComposeExtra_SoloSeAceptaEnUnEnsayo()
    {
        PrepararSecretos("ventas");

        var (codigo, _, errores) = Desplegar("ventas", null, "--secretos", EnLinux(_secretos), "--cron", EnLinux(_cron), "--extra", EnLinux(Path.Combine(_carpeta, "otro.yml")));

        Assert.Equal(2, codigo);
        Assert.Contains("Uso:", errores, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void ElPaqueteDeRaices_SeArmaConLasDeLaVmMasLaCaPrivada()
    {
        PrepararSecretos("ventas");
        var raices = Path.Combine(_carpeta, "raices.crt");
        File.WriteAllText(raices, "-----BEGIN CERTIFICATE-----\nRAIZ-PUBLICA\n-----END CERTIFICATE-----\n");
        File.WriteAllText(Path.Combine(_secretos, "tls-ca.pem"), "-----BEGIN CERTIFICATE-----\nCA-PRIVADA\n-----END CERTIFICATE-----\n");

        var (codigo, salida, errores) = Desplegar("ventas", new Dictionary<string, string>(EntornoDe("ventas")) { ["BABELHUB_RAICES_PUBLICAS"] = EnLinux(raices) });

        Assert.True(codigo == 0, salida + errores);
        var paquete = File.ReadAllText(Path.Combine(_secretos, "ca-bundle.pem"));
        Assert.Contains("RAIZ-PUBLICA", paquete, StringComparison.Ordinal);
        Assert.Contains("CA-PRIVADA", paquete, StringComparison.Ordinal);
    }

    // ---------- scripts/generar-secretos.sh: lo que genera pasa lo que exige desplegar.sh ----------

    private const string DobleDeAgeKeygen = """
        #!/usr/bin/env bash
        # -o <archivo>: escribe un par de claves de mentira con el formato de age-keygen.
        archivo="$2"
        printf '# created: 2026-11-23T00:00:00Z\n# public key: age1%s\nAGE-SECRET-%s\n' "qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq" "KEY-1DOBLEDEPRUEBA" > "$archivo"
        """;

    private (int Codigo, string Salida, string Errores) Generar(string destino, params string[] extra)
    {
        ArchivosDeDoble.Escribir(Path.Combine(_carpeta, "age-keygen-doble.sh"), DobleDeAgeKeygen.ReplaceLineEndings("\n"));
        var informacion = new ProcessStartInfo(Bash()) { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        informacion.ArgumentList.Add(EnLinux(Path.Combine(ComposeDeVms.Raiz(), "scripts", "generar-secretos.sh")));
        foreach (var argumento in new[] { "--destino", EnLinux(destino), "--dominio", "ejemplo.pe", "--ip-ventas", "10.0.0.10", "--ip-inventario", "10.0.1.10" }.Concat(extra))
        {
            informacion.ArgumentList.Add(argumento);
        }

        informacion.Environment["AGE_KEYGEN"] = EnLinux(Path.Combine(_carpeta, "age-keygen-doble.sh"));
        using var proceso = Process.Start(informacion)!;
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var errores = proceso.StandardError.ReadToEndAsync();
        Assert.True(proceso.WaitForExit(TimeSpan.FromMinutes(2)), "El generador no terminó a tiempo.");
        proceso.WaitForExit();
        return (proceso.ExitCode, salida.Result, errores.Result);
    }

    private static string Valor(string carpeta, string archivo, string clave)
    {
        var linea = File.ReadAllLines(Path.Combine(carpeta, archivo)).Single(l => l.StartsWith(clave + "=", StringComparison.Ordinal));
        return linea[(clave.Length + 1)..].Trim('"');
    }

    [Fact]
    [Trait("Criterio", "AC-COM-03")]
    public void ElGenerador_DejaElContactoDelComercioComoCAMBIAR_ParaQueDesplegarSeNieguePorOlvido()
    {
        var destino = Path.Combine(_carpeta, "generados-contacto");

        var (codigo, salida, errores) = Generar(destino);

        Assert.True(codigo == 0, salida + errores);
        foreach (var (carpeta, archivo) in new[] { ("ventas", "ecommerce.env"), ("inventario", "ecommerce-gestion.env") })
        {
            foreach (var clave in new[] { "BABELHUB__Contacto__Telefono", "BABELHUB__Contacto__Correo", "BABELHUB__Contacto__Direccion" })
            {
                Assert.Equal("CAMBIAR", Valor(Path.Combine(destino, carpeta), archivo, clave));
            }
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void ElGenerador_CreaTodasLasClavesDeLaTabla_ComparteLoQueDebeCompartir_YDejaLaClavePrivadaFueraDeLasVms()
    {
        var destino = Path.Combine(_carpeta, "generados");

        var (codigo, salida, errores) = Generar(destino);

        Assert.True(codigo == 0, salida + errores);
        // Cada clave de la tabla está en el archivo de su VM y las que genera el sistema tienen su largo mínimo.
        foreach (var fila in File.ReadAllLines(Path.Combine(ComposeDeVms.Raiz(), "infra", "secretos-requeridos.txt")).Where(l => !l.TrimStart().StartsWith('#') && l.Trim().Length > 0).Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
        {
            var carpetas = fila[0] switch
            {
                "postgres.env" or "inventario.env" or "ecommerce-gestion.env" => new[] { "inventario" },
                _ => ["ventas"],
            };
            foreach (var carpeta in carpetas)
            {
                var valor = Valor(Path.Combine(destino, carpeta), fila[0], fila[1]);
                Assert.True(valor.Length > 0, $"{carpeta}/{fila[0]}: {fila[1]} está vacía");
                if (fila.Length > 2 && valor != "CAMBIAR")
                {
                    Assert.True(valor.Length >= int.Parse(fila[2], System.Globalization.CultureInfo.InvariantCulture), $"{carpeta}/{fila[0]}: {fila[1]} es más corta que {fila[2]}");
                }
            }
        }

        // Lo que dos archivos tienen que compartir lo comparten: la credencial de cada alcance entre servicios, la clave de Identidad y las cuentas de las bases.
        var inventario = Path.Combine(destino, "inventario");
        var ventas = Path.Combine(destino, "ventas");
        const string Alcance = "BABELHUB__Seguridad__Credenciales__SolicitarAviso";
        Assert.Equal(Valor(inventario, "inventario.env", Alcance), Valor(ventas, "ecommerce.env", Alcance));
        Assert.Equal(Valor(inventario, "ecommerce-gestion.env", Alcance), Valor(ventas, "ecommerce.env", Alcance));
        Assert.Equal(Valor(inventario, "ecommerce-gestion.env", "BABELHUB__Identidad__Pimienta"), Valor(ventas, "ecommerce.env", "BABELHUB__Identidad__Pimienta"));
        Assert.Equal(Valor(inventario, "ecommerce-gestion.env", "BABELHUB__Notificaciones__N8n__ClaveSaliente"), Valor(ventas, "n8n.env", "BABELHUB_FIRMA_DE_AVISOS"));
        Assert.Equal(Valor(inventario, "ecommerce-gestion.env", "BABELHUB__Integraciones__n8n__Secreto"), Valor(ventas, "n8n.env", "BABELHUB_FIRMA_DE_ESTADOS"));
        Assert.Equal(Valor(inventario, "ecommerce-gestion.env", "BABELHUB__Seguridad__ClaveDeRedisDeLimites"), Valor(ventas, "redis-contadores.env", "BABELHUB__Seguridad__ClaveDeRedisDeLimites"));
        Assert.Contains("Password=" + Valor(inventario, "postgres.env", "POSTGRES_PASSWORD") + ";", Valor(inventario, "migrador.env", "BABELHUB__Migrador__Inventario__Propietaria"), StringComparison.Ordinal);
        static string Clave(string conexion) => conexion[(conexion.LastIndexOf("Password=", StringComparison.Ordinal) + 9)..].Split(';')[0];
        Assert.Equal(Clave(Valor(inventario, "ecommerce-gestion.env", "BABELHUB__Ecommerce__Base__Aplicacion")), Clave(Valor(ventas, "ecommerce.env", "BABELHUB__Ecommerce__Base__Aplicacion")));
        Assert.Contains("Host=10.0.1.10;", Valor(ventas, "ecommerce.env", "BABELHUB__Ecommerce__Base__Aplicacion"), StringComparison.Ordinal);
        // Cada servicio es distinto de los demás (cursor de auditoría, sesiones): nada se repite entre servicios que no lo comparten.
        Assert.NotEqual(Valor(inventario, "inventario.env", "BABELHUB__Auditoria__ClaveDeCursor"), Valor(inventario, "ecommerce-gestion.env", "BABELHUB__Auditoria__ClaveDeCursor"));
        Assert.NotEqual(Valor(ventas, "ecommerce.env", "BABELHUB__Web__ClaveDeSesionesTienda"), Valor(inventario, "ecommerce-gestion.env", "BABELHUB__Web__ClaveDeSesionesGestion"));

        // La clave privada de age y la CA se quedan en custodia/, nunca en una carpeta de VM; migrador.env solo en VM-INVENTARIO; ningún valor por la salida.
        Assert.False(Directory.Exists(Path.Combine(destino, "borde")), "No hay VM-BORDE (DD-100): el generador no crea su carpeta.");
        foreach (var carpeta in new[] { "inventario", "ventas" })
        {
            foreach (var archivo in Directory.EnumerateFiles(Path.Combine(destino, carpeta)))
            {
                Assert.DoesNotContain("AGE-SECRET-KEY-", File.ReadAllText(archivo), StringComparison.Ordinal);
            }
        }

        Assert.Contains("AGE-SECRET-KEY-", File.ReadAllText(Path.Combine(destino, "custodia", "age-clave-privada.txt")), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(destino, "custodia", "ca-clave.pem")));
        Assert.False(File.Exists(Path.Combine(ventas, "migrador.env")));
        Assert.StartsWith("age1", Valor(inventario, "postgres.env", "BABELHUB_AGE_DESTINATARIO"), StringComparison.Ordinal);
        // Los respaldos van a Cloudflare R2 (D26).
        Assert.Equal("Cloudflare", Valor(inventario, "postgres.env", "RCLONE_CONFIG_ALMACEN_PROVIDER"));
        Assert.Equal("auto", Valor(inventario, "postgres.env", "RCLONE_CONFIG_ALMACEN_REGION"));
        Assert.Contains("r2.cloudflarestorage.com", File.ReadAllText(Path.Combine(destino, "PENDIENTES.txt")), StringComparison.Ordinal);
        Assert.DoesNotContain(Valor(inventario, "postgres.env", "POSTGRES_PASSWORD"), salida + errores, StringComparison.Ordinal);
        Assert.Contains("ventas/cloudflared.env  TUNNEL_TOKEN", File.ReadAllText(Path.Combine(destino, "PENDIENTES.txt")), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void ElGenerador_NoPisaUnaCarpetaConArchivos_NiAceptaDatosInvalidos()
    {
        var destino = Path.Combine(_carpeta, "ocupada");
        Directory.CreateDirectory(destino);
        File.WriteAllText(Path.Combine(destino, "algo.env"), "A=1\n");

        var (codigo, _, errores) = Generar(destino);

        Assert.Equal(1, codigo);
        Assert.Contains("no pisa secretos", errores, StringComparison.Ordinal);
        Assert.Equal("A=1\n", File.ReadAllText(Path.Combine(destino, "algo.env")));
        Assert.Equal(2, Generar(Path.Combine(_carpeta, "otra"), "--region", "Sa Santiago").Codigo);
    }

    [Theory]
    [MemberData(nameof(Vms))]
    [Trait("Criterio", "AC-PLA-04")]
    public void LosSecretosGenerados_YCompletados_PasanTodasLasComprobacionesDeDesplegar(string vm)
    {
        var destino = Path.Combine(_carpeta, "generados");
        var (codigoDeGenerar, salidaDeGenerar, erroresDeGenerar) = Generar(destino);
        Assert.True(codigoDeGenerar == 0, salidaDeGenerar + erroresDeGenerar);
        var carpeta = Path.Combine(destino, vm);
        foreach (var archivo in Directory.EnumerateFiles(carpeta, "*.env"))
        {
            File.WriteAllText(archivo, System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(archivo), "=CAMBIAR\r?$", "=" + Aleatorio(40), System.Text.RegularExpressions.RegexOptions.Multiline, TimeSpan.FromSeconds(2)));
        }

        // Los archivos que el Compose de cada VM pide y que no genera el script (por ejemplo ca-bundle.pem, que arma desplegar) se rellenan como en las otras pruebas.
        foreach (var archivo in ComposeDeVms.ArchivosDeSecretos(vm).Where(a => !File.Exists(Path.Combine(carpeta, a))))
        {
            File.WriteAllText(Path.Combine(carpeta, archivo), Aleatorio(60) + "\n");
        }

        var raices = Path.Combine(_carpeta, "raices.crt");
        File.WriteAllText(raices, "-----BEGIN CERTIFICATE-----\nRAIZ\n-----END CERTIFICATE-----\n");
        var entorno = new Dictionary<string, string>(EntornoDe(vm)) { ["BABELHUB_RAICES_PUBLICAS"] = EnLinux(raices) };

        var (codigo, salida, errores) = Desplegar(vm, entorno, "--secretos", EnLinux(carpeta), "--cron", EnLinux(_cron));

        Assert.True(codigo == 0, salida + errores);
        Assert.Contains($"VM {vm} desplegada y sana.", salida, StringComparison.Ordinal);
    }

    // Lo que solo puede dar un tercero, con valores de prueba que tienen la forma que cada uno exige (dominios reservados; nada real).
    private static string ValorDeTercero(string clave) => clave switch
    {
        "BABELHUB__Contacto__Telefono" => "+51 987 654 321",
        "BABELHUB__Contacto__Correo" => "atencion@tienda.example",
        "BABELHUB__Contacto__Direccion" => "Jr. Ejemplo 123, Ayacucho (prueba)",
        "BABELHUB__Correo__Smtp__Servidor" => "smtp.example.org",
        "BABELHUB__Correo__Smtp__Cuenta" or "BABELHUB__Correo__Smtp__Remitente" => "avisos@example.org",
        "BABELHUB__Web__OrigenDelAlmacen" => "https://almacen.example.org",
        _ when clave.EndsWith("__Endpoint", StringComparison.Ordinal) => "https://almacen.example.org/",
        _ => Aleatorio(40),
    };

    [Theory]
    [InlineData("ventas", "ecommerce", "ecommerce.env")]
    [InlineData("inventario", "ecommerce-gestion", "ecommerce-gestion.env")]
    [Trait("Criterio", "AC-PLA-04")]
    public async Task LaConfiguracionGeneradaDeCadaReplicaDelEcommerce_PasaLaValidacionDelArranque(string vm, string servicio, string archivo)
    {
        var destino = Path.Combine(_carpeta, "generados-arranque");
        var (codigoDeGenerar, salidaDeGenerar, erroresDeGenerar) = Generar(destino);
        Assert.True(codigoDeGenerar == 0, salidaDeGenerar + erroresDeGenerar);
        var carpeta = Path.Combine(destino, vm);
        var ruta = Path.Combine(carpeta, archivo);
        File.WriteAllLines(ruta, File.ReadAllLines(ruta).Select(l => l.EndsWith("=CAMBIAR", StringComparison.Ordinal) ? l[..^"CAMBIAR".Length] + "\"" + ValorDeTercero(l[..^"=CAMBIAR".Length]) + "\"" : l));

        // El entorno del contenedor tal como lo arma Compose: el archivo de secretos más lo que fija el propio Compose, que manda.
        using var documento = System.Text.Json.JsonDocument.Parse(ComposeDeVms.ComposeResuelto(vm, carpeta));
        var entorno = documento.RootElement.GetProperty("services").GetProperty(servicio).GetProperty("environment").EnumerateObject().ToDictionary(v => v.Name, v => v.Value.GetString(), StringComparer.Ordinal);
        var configuracion = entorno
            .Where(v => v.Key.StartsWith("BABELHUB__", StringComparison.Ordinal))
            .ToDictionary(v => v.Key["BABELHUB__".Length..].Replace("__", ":", StringComparison.Ordinal), v => v.Value, StringComparer.OrdinalIgnoreCase);
        var constructor = BabelHub.IntegrationTests.Api.AnfitrionDelEcommerceTests.Constructor(configuracion, entorno["ASPNETCORE_ENVIRONMENT"]!);
        constructor.Services.AddSingleton(typeof(ModoDeArranque), ModoDeArranque.Normal);

        constructor.AgregarEcommerce(ModoDeArranque.Normal);
        await using var app = constructor.Build();
        ArranqueSeguro.ExigirCoherencia(ModoDeArranque.Normal, esProduccion: true, app.Services.GetServices<IAdaptadorSaliente>());
        app.UsarEcommerce();
        app.ExigirComposicionDeProduccion();

        // Solo la tienda envía avisos (corre en la VM de n8n); la réplica de gestión valida la misma configuración, pero no corre el trabajo de envío.
        var envia = app.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().Any(h => h is BabelHub.Ecommerce.Infrastructure.Notificaciones.ServicioDeTrabajosDeNotificaciones);
        Assert.Equal(servicio == "ecommerce", envia);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-44")]
    public void UnArchivoSueltoConLaAdministracionDeR2EnLaCarpetaDeSecretos_NoSeDespliega()
    {
        PrepararSecretos("inventario");
        var valor = Aleatorio(40);
        _valoresSecretos.Add(valor);
        File.WriteAllText(Path.Combine(_secretos, "r2.env"), $"CLOUDFLARE_API_TOKEN={valor}\n");

        var (codigo, salida, errores) = Desplegar("inventario");

        Assert.NotEqual(0, codigo);
        Assert.Contains("r2.env lleva un token", salida + errores, StringComparison.Ordinal);
        SinSecretosEnLaSalida(salida, errores);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void ConElEndpointDeR2YElProveedorAnterior_NoSeDespliega()
    {
        PrepararSecretos("inventario");
        CambiarValor("postgres.env", "RCLONE_CONFIG_ALMACEN_ENDPOINT", "https://0123456789abcdef0123456789abcdef.r2.cloudflarestorage.com");
        File.AppendAllText(Path.Combine(_secretos, "postgres.env"), "RCLONE_CONFIG_ALMACEN_PROVIDER=Other\nRCLONE_CONFIG_ALMACEN_REGION=sa-santiago-1\n");

        var (codigo, salida, errores) = Desplegar("inventario");

        Assert.NotEqual(0, codigo);
        Assert.Contains("PROVIDER=Cloudflare", salida + errores, StringComparison.Ordinal);
        SinSecretosEnLaSalida(salida, errores);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void UnPostgresEnvQueTodaviaApuntaElRespaldoAOracle_NoSeDespliega()
    {
        PrepararSecretos("inventario");
        CambiarValor("postgres.env", "RCLONE_CONFIG_ALMACEN_ENDPOINT", "https://espacio.compat.objectstorage.sa-santiago-1.oraclecloud.com");

        var (codigo, salida, errores) = Desplegar("inventario");

        Assert.NotEqual(0, codigo);
        Assert.Contains("los respaldos van a Cloudflare R2", salida + errores, StringComparison.Ordinal);
        Assert.DoesNotContain(LlamadasADocker, l => l.Contains(" up -d", StringComparison.Ordinal));
        SinSecretosEnLaSalida(salida, errores);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-44")]
    [InlineData("inventario", "postgres.env", "CLOUDFLARE_API_TOKEN")]
    [InlineData("ventas", "ecommerce.env", "export CF_API_KEY")]
    public void UnSecretoConLaAdministracionDeR2_NoSeDespliega(string vm, string archivo, string clave)
    {
        PrepararSecretos(vm);
        var valor = Aleatorio(40);
        _valoresSecretos.Add(valor);
        var ruta = Path.Combine(_secretos, archivo);
        File.AppendAllText(ruta, $"{clave}={valor}\n");

        var (codigo, salida, errores) = Desplegar(vm);

        Assert.NotEqual(0, codigo);
        Assert.Contains("la administración de R2 no puede estar en una VM", salida + errores, StringComparison.Ordinal);
        Assert.DoesNotContain(LlamadasADocker, l => l.Contains(" up -d", StringComparison.Ordinal));
        SinSecretosEnLaSalida(salida, errores);
    }

    // ---------- TLS hacia PostgreSQL (AM24): nadie despliega con una conexión sin cifrar ni sin verificar al servidor ----------

    /// <summary>Cambia el valor de una clave de un archivo de secretos por otro (para fabricar el incumplimiento).</summary>
    private void CambiarValor(string archivo, string clave, string nuevo)
    {
        var ruta = Path.Combine(_secretos, archivo);
        var lineas = File.ReadAllLines(ruta).Select(l => l.StartsWith(clave + "=", StringComparison.Ordinal) ? $"{clave}={nuevo}" : l);
        File.WriteAllLines(ruta, lineas);
    }

    private static string ConClave(string cadena, string clave) => cadena.Replace("CLAVE", clave, StringComparison.Ordinal);

    [Theory]
    [InlineData("inventario", "inventario.env", "BABELHUB__Inventario__Base__Aplicacion", "Host=10.0.1.10;Port=5432;Database=b;Username=u;Pass{word}=CLAVE;SSL Mode=Disable", "SSL Mode=disable")]
    [InlineData("inventario", "ecommerce-gestion.env", "BABELHUB__Ecommerce__Base__DepuracionDeAuditoria", "Host=postgres;Port=5432;Database=b;Username=u;Pass{word}=CLAVE;SSL Mode=Require", "SSL Mode=require")]
    [InlineData("inventario", "inventario.env", "BABELHUB__Inventario__Base__DepuracionDeAjustes", "Host=postgres;Port=5432;Database=b;Username=u;Pass{word}=CLAVE;SSL Mode=VerifyCA", "SSL Mode=verifyca")]
    [InlineData("ventas", "ecommerce.env", "BABELHUB__Ecommerce__Base__Aplicacion", "Host=10.0.1.10;Port=5432;Database=b;Username=u;Pass{word}=CLAVE;SSL Mode=Prefer", "SSL Mode=prefer")]
    [InlineData("ventas", "ecommerce.env", "BABELHUB__Ecommerce__Base__DepuracionDeRegistros", "Host=10.0.1.10;Port=5432;Database=b;Username=u;Pass{word}=CLAVE", "falta «SSL Mode=VerifyFull»")]
    [InlineData("ventas", "ecommerce.env", "BABELHUB__Ecommerce__Base__DepuracionDeAuditoria", "Host=10.0.1.10;Port=5432;Database=b;Username=u;Pass{word}=CLAVE;SSL Mode=VerifyFull;SSL Mode=Disable", "SSL Mode=disable")]
    [InlineData("ventas", "ecommerce.env", "BABELHUB__Ecommerce__Base__Aplicacion", "Host=10.0.1.10;Port=5432;Database=b;Username=u;Pass{word}=CLAVE;SslMode=Allow", "SSL Mode=allow")]
    [InlineData("ventas", "ecommerce.env", "BABELHUB__Ecommerce__Base__Aplicacion", "Host=10.0.1.10;Port=5432;Database=b;Username=u;Pass{word}=CLAVE;SSL Mode=VerifyFull;Trust Server Certificate=true", "Trust Server Certificate")]
    [InlineData("ventas", "ecommerce.env", "BABELHUB__Ecommerce__Base__Aplicacion", "Host=10.0.1.10;Port=5432;Database=b;Username=u;Pass{word}=CLAVE;SSL Mode=VerifyFull;trust server certificate=T", "Trust Server Certificate")]
    [InlineData("ventas", "ecommerce.env", "BABELHUB__Ecommerce__Base__Aplicacion", "'Host=10.0.1.10;Port=5432;Database=b;Username=u;Pass{word}=CLAVE;sslmode=disable'", "SSL Mode=disable")]
    [InlineData("ventas", "ecommerce.env", "BABELHUB__Ecommerce__Base__Aplicacion", "  Host=10.0.1.10;Port=5432;Database=b;Username=u;Pass{word}=CLAVE", "falta «SSL Mode=VerifyFull»")]
    [Trait("Criterio", "AC-PLA-04")]
    public void UnaCadenaDeConexionAPostgreSqlSinTlsVerificado_NoSeDespliega_NombrandoLaClave_SinImprimirElValor_NiLlamarADocker(string vm, string archivo, string clave, string cadena, string motivo)
    {
        PrepararSecretos(vm);
        var valorAleatorio = Aleatorio(32);
        _valoresSecretos.Add(valorAleatorio);
        CambiarValor(archivo, clave, ConClave(cadena, valorAleatorio).Replace("Pass{word}", "Password", StringComparison.Ordinal));

        var (codigo, salida, errores) = Desplegar(vm);

        Assert.Equal(1, codigo);
        Assert.Contains($"{archivo}: {clave}", errores, StringComparison.Ordinal);
        Assert.Contains(motivo, errores, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(LlamadasADocker);
        SinSecretosEnLaSalida(salida, errores);
    }

    [Theory]
    [InlineData("Host=postgres;Database=b;Username=u;Pass{word}=CLAVE;SSL Mode=VerifyFull")]
    [InlineData("Host=postgres;Database=b;Username=u;Pass{word}=CLAVE;ssl mode=verifyfull;GSS Encryption Mode=Disable")]
    [InlineData("Host=10.0.1.10;Port=5432;Database=b;Username=u;Pass{word}=CLAVE;SslMode=VerifyFull;Trust Server Certificate=false")]
    [Trait("Criterio", "AC-PLA-04")]
    public void UnaCadenaConVerifyFull_EnCualquierEscrituraDeNpgsql_SiSeDespliega(string cadena)
    {
        PrepararSecretos("inventario");
        CambiarValor("inventario.env", "BABELHUB__Inventario__Base__Aplicacion", ConClave(cadena, Aleatorio(32)).Replace("Pass{word}", "Password", StringComparison.Ordinal));

        var (codigo, salida, errores) = Desplegar("inventario");

        Assert.True(codigo == 0, salida + errores);
    }

    [Theory]
    [InlineData("BABELHUB__Migrador__Inventario__Propietaria", "Host=postgres;Port=5432;Database=inventario;Username=postgres;Pass{word}=CLAVE", "falta «SSL Mode=VerifyFull»")]
    [InlineData("BABELHUB__Migrador__Ecommerce__Propietaria", "Host=postgres;Port=5432;Database=ecommerce;Username=postgres;Pass{word}=CLAVE;SSL Mode=Require", "SSL Mode=require")]
    [Trait("Criterio", "AC-PLA-04")]
    public void LaCuentaPropietariaDeLaMigracion_TambienExigeTlsVerificado(string clave, string cadena, string motivo)
    {
        PrepararSecretos("inventario");
        CambiarValor("migrador.env", clave, ConClave(cadena, Aleatorio(32)).Replace("Pass{word}", "Password", StringComparison.Ordinal));

        var (codigo, _, errores) = Desplegar("inventario");

        Assert.Equal(1, codigo);
        Assert.Contains($"migrador.env: {clave}", errores, StringComparison.Ordinal);
        Assert.Contains(motivo, errores, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(LlamadasADocker);
    }

    [Theory]
    [InlineData("postgresql://evolution:CLAVE@10.0.1.10:5432/evolution?schema=public", "sslmode=(ausente)")]
    [InlineData("postgresql://evolution:CLAVE@10.0.1.10:5432/evolution?schema=public&sslmode=disable", "sslmode=disable")]
    [InlineData("postgresql://evolution:CLAVE@10.0.1.10:5432/evolution?schema=public&sslmode=prefer&sslaccept=strict&sslcert=/run/secrets/tls-ca.pem", "sslmode=prefer")]
    // Prisma ignora verify-full y verify-ca (los trata como «prefer»: cifra pero no verifica): aceptarlos daría una falsa seguridad.
    [InlineData("postgresql://evolution:CLAVE@10.0.1.10:5432/evolution?schema=public&sslmode=verify-full&sslcert=/run/secrets/tls-ca.pem", "sslmode=verify-full")]
    [InlineData("postgresql://evolution:CLAVE@10.0.1.10:5432/evolution?schema=public&sslmode=require&sslaccept=accept_invalid_certs&sslcert=/run/secrets/tls-ca.pem", "sslaccept=accept_invalid_certs")]
    [InlineData("postgresql://evolution:CLAVE@10.0.1.10:5432/evolution?schema=public&sslmode=require&sslcert=/run/secrets/tls-ca.pem", "sslaccept=(ausente)")]
    [InlineData("'postgresql://evolution:CLAVE@10.0.1.10:5432/evolution?schema=public&sslmode=disable'", "sslmode=disable")]
    [InlineData(" postgresql://evolution:CLAVE@10.0.1.10:5432/evolution?schema=public&sslmode=disable", "sslmode=disable")]
    [InlineData("postgresql://evolution:CLAVE@10.0.1.10:5432/evolution?schema=public&sslmode=disable&sslmode=require&sslaccept=strict&sslcert=/run/secrets/tls-ca.pem", "repite parámetros de TLS")]
    [InlineData("postgresql://evolution:CLAVE@10.0.1.10:5432/evolution?schema=public&sslmode=require&sslaccept=strict", "no lleva «sslcert=»")]
    [Trait("Criterio", "AC-PLA-04")]
    public void LaUriDeEvolutionSinTlsVerificado_NoSeDespliega_SinImprimirLaClave(string uri, string motivo)
    {
        PrepararSecretos("ventas");
        var clave = Aleatorio(32);
        _valoresSecretos.Add(clave);
        CambiarValor("evolution.env", "DATABASE_CONNECTION_URI", ConClave(uri, clave));

        var (codigo, salida, errores) = Desplegar("ventas");

        Assert.Equal(1, codigo);
        Assert.Contains("evolution.env: DATABASE_CONNECTION_URI", errores, StringComparison.Ordinal);
        Assert.Contains(motivo, errores, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
        SinSecretosEnLaSalida(salida, errores);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void LaUriDeEvolutionConLaFormaQueSiVerificaEnPrisma_SiSeDespliega()
    {
        PrepararSecretos("ventas");
        CambiarValor("evolution.env", "DATABASE_CONNECTION_URI", UriDeEvolution(Aleatorio(32)));

        var (codigo, salida, errores) = Desplegar("ventas");

        Assert.True(codigo == 0, salida + errores);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void UnaContrasenaConArrobaOInterrogacion_EnLaUriDeEvolution_NoEnganaALaGuarda()
    {
        PrepararSecretos("ventas");
        CambiarValor("evolution.env", "DATABASE_CONNECTION_URI", "postgresql://evolution:" + Aleatorio(8) + "?@" + Aleatorio(8) + "@10.0.1.10:5432/evolution?schema=public&sslmode=require&sslaccept=strict&sslcert=/run/secrets/tls-ca.pem");

        var (codigo, salida, errores) = Desplegar("ventas");

        Assert.True(codigo == 0, salida + errores);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void UnaCadenaDeNpgsqlEnCualquierOtraClave_EscritaEnMinusculas_SinTls_NoSeDespliega()
    {
        PrepararSecretos("ventas");
        File.AppendAllText(Path.Combine(_secretos, "ecommerce.env"), $"BABELHUB__Otra__Conexion=host=10.0.1.10;username=u;Pass{"word"}={Aleatorio(32)};sslmode=disable\n");

        var (codigo, _, errores) = Desplegar("ventas");

        Assert.Equal(1, codigo);
        Assert.Contains("ecommerce.env: BABELHUB__Otra__Conexion usa «SSL Mode=disable»", errores, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void LaCaPrivadaQueLeeEvolution_SeEntregaARoot()
    {
        PrepararSecretos("ventas");
        var entregas = Path.Combine(_carpeta, "entregas-ca.log");
        ArchivosDeDoble.Escribir(Path.Combine(_carpeta, "chown-ca-doble.sh"), $"#!/usr/bin/env bash\necho \"$@\" >> \"{EnLinux(entregas)}\"\n");
        var entorno = new Dictionary<string, string>(EntornoDe("ventas")) { ["CHOWN"] = EnLinux(Path.Combine(_carpeta, "chown-ca-doble.sh")) };

        var (codigo, salida, errores) = Desplegar("ventas", entorno);

        Assert.True(codigo == 0, salida + errores);
        Assert.Matches(@"0:0 \S*tls-ca\.pem", File.ReadAllText(entregas));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void LaLlaveDelServidorDePostgreSql_SeEntregaASuUsuarioDelContenedor()
    {
        PrepararSecretos("inventario");
        var entregas = Path.Combine(_carpeta, "entregas.log");
        ArchivosDeDoble.Escribir(Path.Combine(_carpeta, "chown-doble.sh"), $"#!/usr/bin/env bash\necho \"$@\" >> \"{EnLinux(entregas)}\"\n");
        var entorno = new Dictionary<string, string>(EntornoDe("inventario")) { ["CHOWN"] = EnLinux(Path.Combine(_carpeta, "chown-doble.sh")) };

        var (codigo, salida, errores) = Desplegar("inventario", entorno);

        Assert.True(codigo == 0, salida + errores);
        var registro = File.ReadAllText(entregas);
        Assert.Matches(@"70:70 \S*tls-postgres-key\.pem", registro);
        Assert.Matches(@"70:70 \S*tls-postgres-cert\.pem", registro);
        Assert.Matches(@"1654:1654 \S*tls-inventario-key\.pem", registro);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void ElGenerador_PideTlsVerificadoEnTodasLasCadenasDePostgreSql_YEmiteElCertificadoDelServidorConSusNombres()
    {
        var destino = Path.Combine(_carpeta, "generados-tls");

        var (codigo, salida, errores) = Generar(destino);

        Assert.True(codigo == 0, salida + errores);
        var inventario = Path.Combine(destino, "inventario");
        var ventas = Path.Combine(destino, "ventas");
        foreach (var (carpeta, archivo, clave) in new[]
        {
            (inventario, "inventario.env", "BABELHUB__Inventario__Base__Aplicacion"),
            (inventario, "inventario.env", "BABELHUB__Inventario__Base__DepuracionDeAjustes"),
            (inventario, "inventario.env", "BABELHUB__Inventario__Base__DepuracionDeAuditoria"),
            (inventario, "ecommerce-gestion.env", "BABELHUB__Ecommerce__Base__Aplicacion"),
            (inventario, "ecommerce-gestion.env", "BABELHUB__Ecommerce__Base__DepuracionDeAuditoria"),
            (inventario, "ecommerce-gestion.env", "BABELHUB__Ecommerce__Base__DepuracionDeRegistros"),
            (inventario, "migrador.env", "BABELHUB__Migrador__Inventario__Propietaria"),
            (inventario, "migrador.env", "BABELHUB__Migrador__Ecommerce__Propietaria"),
            (ventas, "ecommerce.env", "BABELHUB__Ecommerce__Base__Aplicacion"),
            (ventas, "ecommerce.env", "BABELHUB__Ecommerce__Base__DepuracionDeAuditoria"),
            (ventas, "ecommerce.env", "BABELHUB__Ecommerce__Base__DepuracionDeRegistros"),
        })
        {
            var cadena = Valor(carpeta, archivo, clave);
            Assert.Contains(";SSL Mode=VerifyFull", cadena, StringComparison.Ordinal);
            Assert.Contains(";GSS Encryption Mode=Disable", cadena, StringComparison.Ordinal);
            Assert.DoesNotContain("Trust Server Certificate", cadena, StringComparison.OrdinalIgnoreCase);
        }

        // Los servicios de la propia VM llegan por el nombre «postgres» (SAN del certificado); VM-VENTAS, por la IP privada de VM-INVENTARIO.
        Assert.Contains("Host=postgres;", Valor(inventario, "inventario.env", "BABELHUB__Inventario__Base__Aplicacion"), StringComparison.Ordinal);
        Assert.Contains("Host=10.0.1.10;", Valor(ventas, "ecommerce.env", "BABELHUB__Ecommerce__Base__Aplicacion"), StringComparison.Ordinal);
        var uri = Valor(ventas, "evolution.env", "DATABASE_CONNECTION_URI");
        Assert.Contains("@10.0.1.10:5432/", uri, StringComparison.Ordinal);
        Assert.Contains("sslmode=require&sslaccept=strict&sslcert=/run/secrets/tls-ca.pem", uri, StringComparison.Ordinal);
        Assert.DoesNotContain("verify-full", uri, StringComparison.Ordinal);

        // El certificado del servidor: lo firma la CA privada y nombra la IP de VM-INVENTARIO y «postgres»; su clave solo la tiene VM-INVENTARIO.
        Assert.Contains("PRIVATE KEY", File.ReadAllText(Path.Combine(inventario, "tls-postgres-key.pem")), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(ventas, "tls-postgres-key.pem")));
        Assert.Equal(File.ReadAllText(Path.Combine(inventario, "tls-ca.pem")), File.ReadAllText(Path.Combine(ventas, "tls-ca.pem")));
        var (verifica, textoDeVerificar) = Openssl(destino, "verify", "-CAfile", "inventario/tls-ca.pem", "inventario/tls-postgres-cert.pem");
        Assert.True(verifica == 0, textoDeVerificar);
        var (_, nombres) = Openssl(destino, "x509", "-in", "inventario/tls-postgres-cert.pem", "-noout", "-ext", "subjectAltName");
        Assert.Contains("DNS:postgres", nombres, StringComparison.Ordinal);
        Assert.Contains("IP Address:10.0.1.10", nombres, StringComparison.Ordinal);
        // La CA tiene restricción de nombres: debe permitir «postgres» y no el nombre que solo usa el ensayo local.
        var (_, restricciones) = Openssl(destino, "x509", "-in", "inventario/tls-ca.pem", "-noout", "-ext", "nameConstraints");
        Assert.Contains("DNS:postgres", restricciones, StringComparison.Ordinal);
        Assert.DoesNotContain("host.docker.internal", restricciones, StringComparison.Ordinal);
    }

    private static (int Codigo, string Texto) Openssl(string carpeta, params string[] argumentos)
    {
        var informacion = new ProcessStartInfo("openssl") { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = carpeta };
        foreach (var argumento in argumentos)
        {
            informacion.ArgumentList.Add(argumento);
        }

        using var proceso = Process.Start(informacion)!;
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var errores = proceso.StandardError.ReadToEndAsync();
        Assert.True(proceso.WaitForExit(TimeSpan.FromMinutes(1)), "openssl no terminó a tiempo.");
        proceso.WaitForExit();
        return (proceso.ExitCode, salida.Result + errores.Result);
    }

    // ---------- La migración de las bases (COMP-2) ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void LaVmDeDatos_LevantaPostgresMigraLasDosBasesYDespuesLevantaTodo_EnEseOrden()
    {
        PrepararSecretos("inventario");

        var (codigo, salida, errores) = Desplegar("inventario");

        Assert.True(codigo == 0, salida + errores);
        var llamadas = LlamadasADocker.ToList();
        var postgres = llamadas.FindIndex(l => l.Contains("up -d --wait --wait-timeout 300 postgres", StringComparison.Ordinal));
        var inventario = llamadas.FindIndex(l => l.Contains("run --rm --no-deps -e BABELHUB__Migrador__Inventario__Propietaria inventario migrar", StringComparison.Ordinal));
        var ecommerce = llamadas.FindIndex(l => l.Contains("run --rm --no-deps -e BABELHUB__Migrador__Ecommerce__Propietaria ecommerce-gestion migrar", StringComparison.Ordinal));
        var todo = llamadas.FindLastIndex(l => l.EndsWith("up -d --wait --wait-timeout 300", StringComparison.Ordinal));
        Assert.True(postgres >= 0 && postgres < inventario && inventario < ecommerce && ecommerce < todo, string.Join("\n", llamadas));
        SinSecretosEnLaSalida(salida, errores);
        Assert.DoesNotContain(llamadas, l => l.Contains("Propietaria=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ventas")]
    [Trait("Criterio", "AC-PLA-04")]
    public void LasOtrasVms_NoMigran_NiPidenMigradorEnv(string vm)
    {
        PrepararSecretos(vm);

        var (codigo, salida, errores) = Desplegar(vm);

        Assert.True(codigo == 0, salida + errores);
        Assert.DoesNotContain(LlamadasADocker, l => l.Contains(" run --rm ", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(_secretos, "migrador.env")));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void SinMigradorEnvOSinUnaDeSusClaves_LaVmDeDatosNoSeDespliega_SinLlamarADocker()
    {
        PrepararSecretos("inventario");
        File.Delete(Path.Combine(_secretos, "migrador.env"));
        var (sinArchivo, _, erroresSinArchivo) = Desplegar("inventario");
        PrepararSecretos("inventario");
        File.WriteAllText(Path.Combine(_secretos, "migrador.env"), "BABELHUB__Migrador__Inventario__Propietaria=algo\n");

        var (sinClave, _, erroresSinClave) = Desplegar("inventario");

        Assert.Equal(1, sinArchivo);
        Assert.Contains("migrador.env", erroresSinArchivo, StringComparison.Ordinal);
        Assert.Equal(1, sinClave);
        Assert.Contains("falta la clave BABELHUB__Migrador__Ecommerce__Propietaria en migrador.env", erroresSinClave, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void LaCuentaPropietariaEnElArchivoDeUnServicio_OMigradorEnEnUnaVmQueNoMigra_NoSeDespliega()
    {
        PrepararSecretos("inventario");
        File.AppendAllText(Path.Combine(_secretos, "inventario.env"), "BABELHUB__Migrador__Inventario__Propietaria=algo\n");
        var (enServicio, _, erroresEnServicio) = Desplegar("inventario");
        PrepararSecretos("ventas");
        File.WriteAllText(Path.Combine(_secretos, "migrador.env"), "BABELHUB__Migrador__Ecommerce__Propietaria=algo\n");

        var (enVentas, _, erroresEnVentas) = Desplegar("ventas");

        Assert.Equal(1, enServicio);
        Assert.Contains("inventario.env lleva claves BABELHUB__Migrador__*", erroresEnServicio, StringComparison.Ordinal);
        Assert.Equal(1, enVentas);
        Assert.Contains("existe migrador.env en la VM ventas", erroresEnVentas, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void SiLaMigracionFalla_NoSeLevantanLosServicios()
    {
        PrepararSecretos("inventario");
        var entorno = EntornoDe("inventario");
        entorno["DOBLE_RUN_FALLA"] = "1";

        var (codigo, salida, errores) = Desplegar("inventario", entorno);

        Assert.Equal(1, codigo);
        Assert.Contains("La migración de la base de inventario falló", errores, StringComparison.Ordinal);
        Assert.DoesNotContain(LlamadasADocker, l => l.EndsWith("up -d --wait --wait-timeout 300", StringComparison.Ordinal));
        SinSecretosEnLaSalida(salida, errores);
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
        var (codigo, _, errores) = Desplegar("ventas", null, "--cron", EnLinux(_cron));

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
    [InlineData("ventas", "cloudflared.env")]
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
        PrepararSecretos("ventas");
        File.WriteAllText(Path.Combine(_secretos, "cloudflared.env"), string.Empty);

        var (codigo, _, errores) = Desplegar("ventas");

        Assert.Equal(1, codigo);
        Assert.Contains("el archivo de secretos cloudflared.env está vacío", errores, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void SiUnArchivoDeSecretosLoPuedenLeerOtrosUsuarios_NoSeDespliega()
    {
        PrepararSecretos("ventas");
        var entorno = EntornoDe("ventas");
        entorno["STAT_ABIERTO"] = "cloudflared.env";

        var (codigo, salida, errores) = Desplegar("ventas", entorno);

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
    [InlineData("ventas", "--espera", "abc")]
    [InlineData("ventas", "--desconocida")]
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

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void LaVmDeBordeYaNoExiste_ElScriptLoDiceYNoHaceNada()
    {
        var (codigo, _, errores) = Desplegar("borde", EntornoDe("ventas"));

        Assert.Equal(2, codigo);
        Assert.Contains("VM-BORDE ya no existe: cloudflared y Nginx se despliegan con «ventas»", errores, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public void ElGenerador_NoEscribeEnLosSecretosQuienPuedeFijarElOrigen_PorqueLoFijanLosCompose()
    {
        var destino = Path.Combine(_carpeta, "generados-proxies");

        var (codigo, salida, errores) = Generar(destino);

        Assert.True(codigo == 0, salida + errores);
        Assert.DoesNotContain("ProxiesConocidos=", File.ReadAllText(Path.Combine(destino, "inventario", "ecommerce-gestion.env")), StringComparison.Ordinal);
        Assert.DoesNotContain("ProxiesConocidos=", File.ReadAllText(Path.Combine(destino, "ventas", "ecommerce.env")), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void LaVmDeVentas_ExigeLaDireccionDelInventario_PorqueNginxLlevaAlliLaGestion()
    {
        PrepararSecretos("ventas");
        var entorno = EntornoDe("ventas");
        entorno.Remove("IP_INVENTARIO");

        var (codigo, _, errores) = Desplegar("ventas", entorno);

        Assert.Equal(1, codigo);
        Assert.Contains("falta la variable IP_INVENTARIO", errores, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
    }

    // ---------- La réplica de gestión solo la alcanza Nginx (DD-104) ----------

    private const string Regla = "DOCKER-USER -d 10.0.0.10 -p tcp --dport 8081 ! -s 10.213.10.3 -j DROP";

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public void EnVentas_PoneLaReglaAntesDeLevantarNada_YDejaLaTareaQueLaRepone()
    {
        PrepararSecretos("ventas");

        var (codigo, salida, errores) = Desplegar("ventas");

        Assert.True(codigo == 0, salida + errores);
        var llamadas = LlamadasADocker.ToList();
        var insercion = llamadas.IndexOf("iptables -w -I " + Regla);
        var levantar = llamadas.FindIndex(l => l.Contains(" up -d --wait", StringComparison.Ordinal));
        Assert.True(insercion >= 0 && insercion < levantar, string.Join("\n", llamadas));
        var tarea = File.ReadAllText(Path.Combine(_cron, "babelhub-gestion"));
        Assert.Matches(@"(?m)^\* \* \* \* \* root /bin/bash \S+/scripts/proteger-gestion\.sh 10\.0\.0\.10 >> ", tarea);
        Assert.Contains("PATH=", tarea, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public void ConLaReglaYaPuesta_RepetirElDespliegue_NoLaDuplica()
    {
        PrepararSecretos("ventas");
        var entorno = EntornoDe("ventas");
        entorno["DOBLE_REGLA_PUESTA"] = "1";

        var (codigo, salida, errores) = Desplegar("ventas", entorno);

        Assert.True(codigo == 0, salida + errores);
        Assert.Contains("iptables -w -C " + Regla, LlamadasADocker);
        Assert.DoesNotContain(LlamadasADocker, l => l.StartsWith("iptables -w -I ", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public void SiLaReglaNoSePuedePoner_VentasNoSeDespliega()
    {
        PrepararSecretos("ventas");
        var entorno = EntornoDe("ventas");
        entorno["DOBLE_IPTABLES_FALLA"] = "1";

        var (codigo, _, errores) = Desplegar("ventas", entorno);

        Assert.Equal(1, codigo);
        Assert.Contains("No se despliega ventas: sin la regla", errores, StringComparison.Ordinal);
        Assert.DoesNotContain(LlamadasADocker, l => l.Contains(" up -d", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(_cron, "babelhub-gestion")));
    }

    [Theory]
    [InlineData("babelhub-aux", "755")]
    [InlineData("root", "775")]
    [Trait("Criterio", "AC-SEG-15")]
    public void SiElGuionDeLaReglaLoPuedeModificarOtro_VentasNoSeDespliega(string dueno, string modo)
    {
        PrepararSecretos("ventas");
        var entorno = EntornoDe("ventas");
        entorno["STAT_DUENO"] = dueno;
        entorno["STAT_MODO_SCRIPTS"] = modo;

        var (codigo, _, errores) = Desplegar("ventas", entorno);

        Assert.Equal(1, codigo);
        Assert.Contains("scripts/proteger-gestion.sh es de", errores, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public void SiLaRaizDelRepositorioNoEsDeRoot_VentasNoSeDespliega()
    {
        PrepararSecretos("ventas");
        var entorno = EntornoDe("ventas");
        // La raíz tal como la calcula el script: la del propio guion, resuelta por bash.
        entorno["DOBLE_RAIZ"] = RaizSegunBash();
        entorno["STAT_DUENO_RAIZ"] = "ubuntu";

        var (codigo, _, errores) = Desplegar("ventas", entorno);

        Assert.Equal(1, codigo);
        Assert.Contains("es de ubuntu con permisos 755: debe ser de root", errores, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
    }

    private static string RaizSegunBash()
    {
        var informacion = new ProcessStartInfo(Bash()) { RedirectStandardOutput = true };
        informacion.ArgumentList.Add("-c");
        informacion.ArgumentList.Add("cd \"$(dirname \"$1\")/..\" && pwd");
        informacion.ArgumentList.Add("--");
        informacion.ArgumentList.Add(EnLinux(Path.Combine(ComposeDeVms.Raiz(), "scripts", "desplegar.sh")));
        using var proceso = Process.Start(informacion)!;
        var salida = proceso.StandardOutput.ReadToEnd();
        proceso.WaitForExit();
        return salida.Trim();
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-04")]
    public void LaVmDeDatos_NoTocaElCortafuegos_YExigeLaDireccionDeVentas()
    {
        PrepararSecretos("inventario");

        var (codigo, salida, errores) = Desplegar("inventario");

        Assert.True(codigo == 0, salida + errores);
        Assert.DoesNotContain(LlamadasADocker, l => l.StartsWith("iptables ", StringComparison.Ordinal));

        File.Delete(Registro);
        var entorno = EntornoDe("inventario");
        entorno.Remove("IP_VENTAS");
        var (sinVentas, _, erroresSinVentas) = Desplegar("inventario", entorno);

        Assert.Equal(1, sinVentas);
        Assert.Contains("falta la variable IP_VENTAS", erroresSinVentas, StringComparison.Ordinal);
        Assert.Empty(LlamadasADocker);
    }

    // ---------- Con docker de verdad, sobre una VM mínima ----------

    private string VmMinima(string saludable)
    {
        var infra = Path.Combine(_carpeta, "infra");
        Directory.CreateDirectory(infra);
        File.WriteAllText(
            Path.Combine(infra, "docker-compose.ventas.yml"),
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
        return Desplegar("ventas", entorno, "--secretos", EnLinux(_secretos), "--infra", EnLinux(infra), "--espera", espera);
    }

    private void Retirar(string infra)
    {
        var compose = Path.Combine(infra, "docker-compose.ventas.yml");
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
