using System.Diagnostics;
using System.Text;

namespace BabelHub.IntegrationTests.Almacen;

/// <summary>
/// <c>scripts/almacen.sh r2-…</c> (PLA-16, PLA-18, PLA-44; D26, D27): aplica y comprueba en Cloudflare R2 el bloqueo de retención, el ciclo de
/// vida y la privacidad de los contenedores de respaldos. El token temporal llega a <c>curl</c> por la entrada estándar y no aparece en
/// argumentos ni salidas; el script se niega a usarlo con un agente o una integración continua; no cambia un bloqueo que ya existe. La API se
/// sustituye por un doble que guarda lo que se le envía; el comportamiento real de R2 lo comprueba la constancia T47 en la cuenta.
/// </summary>
[Collection(BabelHub.IntegrationTests.Imagenes.ProcesosPesadosCollection.Nombre)]
public sealed class AlmacenR2ShTests : IDisposable
{
    private const string Cuenta = "0123456789abcdef0123456789abcdef";
    private static readonly string[] VariablesDeAgente = ["CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CODEX_SANDBOX", "CURSOR_AGENT", "GEMINI_CLI", "CI", "GITHUB_ACTIONS"];

    private readonly string _ficha = "ficha_de_prueba_" + Guid.NewGuid().ToString("N");
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "almacen-r2-" + Guid.NewGuid().ToString("N"));
    private readonly string _estado;
    private readonly string _doble;

    public AlmacenR2ShTests()
    {
        _estado = Path.Combine(_carpeta, "estado");
        Directory.CreateDirectory(_estado);
        _doble = Path.Combine(_carpeta, "curl-doble.sh");
        ArchivosDeDoble.Escribir(_doble, DobleDeCurl.ReplaceLineEndings("\n"));
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

    // El doble anota los argumentos y lo que llega por la entrada estándar, guarda lo que se envía y responde con lo guardado.
    private const string DobleDeCurl = """
        #!/usr/bin/env bash
        metodo=""; salida=""; cuerpo=""; url=""
        printf '%s\n' "$*" >>"$R2_ESTADO/argumentos.log"
        cat >>"$R2_ESTADO/entrada.log"
        while [[ $# -gt 0 ]]; do
          case "$1" in
            --request) metodo="$2"; shift 2 ;;
            --output) salida="$2"; shift 2 ;;
            --data-binary) cuerpo="$(cat "${2#@}")"; shift 2 ;;
            https://*) url="$1"; shift ;;
            --config | --max-time | --write-out | --header | --proto) shift 2 ;;
            *) shift ;;
          esac
        done
        ruta="${url#*/r2}"
        responder() { printf '%s' "$2" >"$salida"; printf '%s' "$1"; exit 0; }
        bien() { responder 200 "{\"success\":true,\"errors\":[],\"result\":$1}"; }
        mal() { responder "$1" '{"success":false,"errors":[{"code":10006}],"result":null}'; }
        [[ -f "$R2_ESTADO/estado-forzado" ]] && mal "$(cat "$R2_ESTADO/estado-forzado")"
        case "$metodo $ruta" in
          "POST /buckets")
            nombre="${cuerpo#*\"name\":\"}"; nombre="${nombre%%\"*}"
            echo "POST $ruta $cuerpo" >>"$R2_ESTADO/cambios.log"
            : >"$R2_ESTADO/$nombre.existe"
            bien '{}' ;;
        esac
        resto="${ruta#/buckets/}"
        nombre="${resto%%/*}"
        parte="${resto#"$nombre"}"
        [[ -f "$R2_ESTADO/$nombre.existe" ]] || mal 404
        case "$metodo $parte" in
          "GET ") bien "{\"name\":\"$nombre\"}" ;;
          "PUT /lock" | "PUT /lifecycle")
            echo "PUT $ruta $cuerpo" >>"$R2_ESTADO/cambios.log"
            printf '%s' "$cuerpo" >"$R2_ESTADO/$nombre.${parte#/}"
            bien '{}' ;;
          "GET /lock" | "GET /lifecycle")
            archivo="$R2_ESTADO/$nombre.${parte#/}"
            if [[ -f "$archivo" ]]; then bien "$(cat "$archivo")"; else bien '{"rules": []}'; fi ;;
          "GET /domains/managed")
            if [[ -f "$R2_ESTADO/$nombre.publico" ]]; then bien '{"enabled": true}'; else bien '{"enabled": false}'; fi ;;
          "GET /domains/custom")
            if [[ -f "$R2_ESTADO/$nombre.dominio" ]]; then bien '{"domains": [{"domain": "x.example"}]}'; else bien '{"domains": []}'; fi ;;
          *) mal 400 ;;
        esac
        """;

    private static string Bash()
    {
        const string DeGit = @"C:\Program Files\Git\bin\bash.exe";
        return OperatingSystem.IsWindows() && File.Exists(DeGit) ? DeGit : "bash";
    }

    private (int Codigo, string Salida, string Errores) Ejecutar(string[] argumentos, string? ficha = "", string? cuenta = Cuenta, string? agente = null, string? definicion = null)
    {
        var raiz = Path.GetDirectoryName(Path.GetDirectoryName(PoliticasDelAlmacen.Carpeta()))!;
        var informacion = new ProcessStartInfo(Bash()) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        informacion.ArgumentList.Add(Path.Combine(raiz, "scripts", "almacen.sh").Replace('\\', '/'));
        foreach (var argumento in argumentos)
        {
            informacion.ArgumentList.Add(argumento);
        }

        // La prueba corre quizá dentro de un agente o de la integración continua: se limpian sus marcas y cada caso pone la que quiere probar.
        foreach (var variable in VariablesDeAgente)
        {
            informacion.Environment.Remove(variable);
        }

        informacion.Environment.Remove("CLOUDFLARE_API_TOKEN");
        informacion.Environment.Remove("CLOUDFLARE_ACCOUNT_ID");
        informacion.Environment["CURL"] = _doble.Replace('\\', '/');
        informacion.Environment["R2_ESTADO"] = _estado.Replace('\\', '/');
        if (ficha is not null)
        {
            informacion.Environment["CLOUDFLARE_API_TOKEN"] = ficha.Length == 0 ? _ficha : ficha;
        }

        if (cuenta is not null)
        {
            informacion.Environment["CLOUDFLARE_ACCOUNT_ID"] = cuenta;
        }

        if (agente is not null)
        {
            informacion.Environment[agente] = "1";
        }

        if (definicion is not null)
        {
            informacion.Environment["ALMACEN_DEFINICION_R2"] = definicion.Replace('\\', '/');
        }

        using var proceso = Process.Start(informacion)!;
        proceso.StandardInput.Close();
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var errores = proceso.StandardError.ReadToEndAsync();
        Assert.True(proceso.WaitForExit(TimeSpan.FromMinutes(2)), "El script no terminó a tiempo.");
        proceso.WaitForExit();
        return (proceso.ExitCode, salida.Result, errores.Result);
    }

    private string[] Registro(string nombre)
    {
        var archivo = Path.Combine(_estado, nombre);
        return File.Exists(archivo) ? File.ReadAllLines(archivo) : [];
    }

    private static string[] Contenedores() => [.. PoliticasDeR2.Cargar().Contenedores.Select(c => c.Nombre)];

    private void CuentaConforme()
    {
        var (codigo, salida, errores) = Ejecutar(["r2-aplicar"]);
        Assert.True(codigo == 0, salida + errores);
        File.Delete(Path.Combine(_estado, "cambios.log"));
    }

    private void Cambiar(string archivo, string antes, string despues)
    {
        var ruta = Path.Combine(_estado, archivo);
        var texto = File.ReadAllText(ruta);
        Assert.Contains(antes, texto, StringComparison.Ordinal);
        File.WriteAllText(ruta, texto.Replace(antes, despues, StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_MuestraLoQueSeEnviaria_SinTokenYSinLlamarANadie()
    {
        var (codigo, salida, errores) = Ejecutar(["r2-plan"], ficha: null, cuenta: null, agente: "CLAUDECODE");

        Assert.True(codigo == 0, errores);
        Assert.Empty(Registro("argumentos.log"));
        foreach (var contenedor in PoliticasDeR2.Cargar().Contenedores)
        {
            Assert.Contains($"/r2/buckets/{contenedor.Nombre}/lock {{\"rules\":[{{\"id\":\"retencion-{contenedor.BloqueoDias}-dias\",\"enabled\":true,\"condition\":{{\"type\":\"Age\",\"maxAgeSeconds\":{contenedor.BloqueoDias * 86400}}}}}]}}", salida, StringComparison.Ordinal);
            Assert.Contains($"\"deleteObjectsTransition\":{{\"condition\":{{\"type\":\"Age\",\"maxAge\":{contenedor.ExpiraDias * 86400}}}}}", salida, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-16")]
    public void Aplicar_CreaLosContenedoresConSuBloqueoYSuRetiro_YTerminaComprobando()
    {
        var (codigo, salida, errores) = Ejecutar(["r2-aplicar", "--ubicacion", "enam"]);

        Assert.True(codigo == 0, salida + errores);
        var cambios = Registro("cambios.log");
        Assert.Equal(4, cambios.Count(c => c.StartsWith("POST /buckets ", StringComparison.Ordinal) && c.Contains("\"locationHint\":\"enam\"", StringComparison.Ordinal)));
        foreach (var nombre in Contenedores())
        {
            Assert.Single(cambios, c => c.StartsWith($"PUT /buckets/{nombre}/lock ", StringComparison.Ordinal));
            Assert.Single(cambios, c => c.StartsWith($"PUT /buckets/{nombre}/lifecycle ", StringComparison.Ordinal));
        }

        Assert.Contains("\"maxAgeSeconds\":604800", cambios.Single(c => c.StartsWith("PUT /buckets/respaldos-wal/lock ", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains("\"maxAge\":7948800", cambios.Single(c => c.StartsWith("PUT /buckets/respaldos-mensuales/lifecycle ", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Equal(4, salida.Split('\n').Count(l => l.StartsWith("ok ", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-44")]
    public void ElToken_LlegaACurlPorLaEntradaEstandar_YNoApareceEnArgumentosNiEnLasSalidas()
    {
        var (codigo, salida, errores) = Ejecutar(["r2-aplicar"]);

        Assert.True(codigo == 0, salida + errores);
        Assert.Contains(Registro("entrada.log"), l => l.Contains("Authorization: Bearer " + _ficha, StringComparison.Ordinal));
        Assert.All(Registro("argumentos.log"), l => Assert.DoesNotContain(_ficha, l, StringComparison.Ordinal));
        Assert.All(Registro("argumentos.log"), l => Assert.Contains("--config -", l, StringComparison.Ordinal));
        // curl no lee ~/.curlrc: «--disable» tiene que ser el primer argumento.
        Assert.All(Registro("argumentos.log"), l => Assert.StartsWith("--disable ", l, StringComparison.Ordinal));
        Assert.DoesNotContain(_ficha, salida + errores, StringComparison.Ordinal);
        Assert.DoesNotContain(_ficha, string.Join('\n', Registro("cambios.log")), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-44")]
    [InlineData("CLAUDECODE")]
    [InlineData("CI")]
    [InlineData("GITHUB_ACTIONS")]
    public void ConUnAgenteOIntegracionContinua_SeNiegaSinLlamarANadie(string variable)
    {
        foreach (var orden in new[] { "r2-aplicar", "r2-comprobar" })
        {
            var (codigo, _, errores) = Ejecutar([orden], agente: variable);

            Assert.Equal(2, codigo);
            Assert.Contains(variable, errores, StringComparison.Ordinal);
        }

        Assert.Empty(Registro("argumentos.log"));
    }

    [Theory]
    [InlineData(null, Cuenta)]
    [InlineData("corto", Cuenta)]
    [InlineData("con espacios y \"comillas\" que romperian la configuracion", Cuenta)]
    [InlineData("", null)]
    [InlineData("", "no-es-una-cuenta")]
    public void SinTokenOSinCuentaValidos_SeRechazaConCodigo2(string? ficha, string? cuenta)
    {
        var (codigo, _, errores) = Ejecutar(["r2-comprobar"], ficha, cuenta);

        Assert.Equal(2, codigo);
        Assert.False(string.IsNullOrWhiteSpace(errores));
        Assert.Empty(Registro("argumentos.log"));
    }

    [Fact]
    public void ElTokenNuncaSePasaComoArgumento()
    {
        var (codigo, _, errores) = Ejecutar(["r2-comprobar", "--token", "x"]);

        Assert.Equal(2, codigo);
        Assert.Contains("nunca se pasa como argumento", errores, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-16")]
    public void Aplicar_NoCambiaUnBloqueoQueYaExisteYDifiere()
    {
        CuentaConforme();
        Cambiar("respaldos-diarios.lock", "\"maxAgeSeconds\":604800", "\"maxAgeSeconds\":86400");

        var (codigo, _, errores) = Ejecutar(["r2-aplicar"]);

        Assert.Equal(1, codigo);
        Assert.Contains("no se cambia solo", errores, StringComparison.Ordinal);
        Assert.DoesNotContain(Registro("cambios.log"), c => c.Contains("respaldos-diarios/lock", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-16")]
    public void Comprobar_ConLaCuentaConforme_TerminaBien_SinCambiarNada()
    {
        CuentaConforme();

        var (codigo, salida, errores) = Ejecutar(["r2-comprobar"]);

        Assert.True(codigo == 0, salida + errores);
        Assert.Equal(4, salida.Split('\n').Count(l => l.StartsWith("ok ", StringComparison.Ordinal)));
        Assert.Empty(Registro("cambios.log"));
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-16")]
    [InlineData("respaldos-wal.lock", "\"maxAgeSeconds\":604800", "\"maxAgeSeconds\":86400", "el bloqueo no es de 7 días")]
    [InlineData("respaldos-wal.lock", "\"maxAgeSeconds\":604800", "\"maxAgeSeconds\":6048000", "el bloqueo no es de 7 días")]
    [InlineData("respaldos-wal.lock", "\"type\":\"Age\",\"maxAgeSeconds\":604800", "\"type\":\"Indefinite\"", "el bloqueo no es de 7 días")]
    [InlineData("respaldos-wal.lock", "\"enabled\":true", "\"enabled\":false", "deshabilitada")]
    [InlineData("respaldos-wal.lock", "\"enabled\":true", "\"enabled\":true,\"prefix\":\"2027/\"", "solo cubre un prefijo")]
    [InlineData("respaldos-wal.lifecycle", "\"maxAge\":604800", "\"maxAge\":8640000", "el retiro no es a los 7 días")]
    [InlineData("respaldos-wal.lifecycle", "\"enabled\":true", "\"enabled\":false", "la regla de retiro está deshabilitada")]
    [InlineData("respaldos-wal.lifecycle", "\"prefix\":\"\"", "\"prefix\":\"2027/\"", "la regla de retiro solo cubre un prefijo")]
    [InlineData("respaldos-wal.lifecycle", "deleteObjectsTransition", "otraTransicion", "debe tener una sola regla de retiro y tiene 0")]
    public void Comprobar_SiUnaReglaNoCoincide_Falla(string archivo, string antes, string despues, string mensaje)
    {
        CuentaConforme();
        Cambiar(archivo, antes, despues);

        var (codigo, salida, _) = Ejecutar(["r2-comprobar"]);

        Assert.Equal(1, codigo);
        Assert.Contains("x respaldos-wal: ", salida, StringComparison.Ordinal);
        Assert.Contains(mensaje, salida, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-16")]
    [InlineData("respaldos-diarios.lock", "x respaldos-diarios: debe tener una sola regla de bloqueo y tiene 0")]
    [InlineData("respaldos-diarios.existe", "x respaldos-diarios: no existe")]
    public void Comprobar_SiFaltaElBloqueoOElContenedor_Falla(string archivo, string mensaje)
    {
        CuentaConforme();
        File.Delete(Path.Combine(_estado, archivo));

        var (codigo, salida, _) = Ejecutar(["r2-comprobar"]);

        Assert.Equal(1, codigo);
        Assert.Contains(mensaje, salida, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-16")]
    [InlineData("publico", "dominio público r2.dev")]
    [InlineData("dominio", "dominio propio")]
    public void Comprobar_SiUnContenedorEsPublico_Falla(string marca, string mensaje)
    {
        CuentaConforme();
        File.WriteAllText(Path.Combine(_estado, "respaldos-semanales." + marca), string.Empty);

        var (codigo, salida, _) = Ejecutar(["r2-comprobar"]);

        Assert.Equal(1, codigo);
        Assert.Contains("x respaldos-semanales: ", salida, StringComparison.Ordinal);
        Assert.Contains(mensaje, salida, StringComparison.Ordinal);
    }

    [Fact]
    public void SiLaApiRechazaElToken_FallaSinCrearNada()
    {
        File.WriteAllText(Path.Combine(_estado, "estado-forzado"), "403");

        var (codigo, salida, errores) = Ejecutar(["r2-aplicar"]);

        Assert.Equal(1, codigo);
        Assert.Contains("estado 403", errores, StringComparison.Ordinal);
        Assert.Empty(Registro("cambios.log"));
        Assert.DoesNotContain(_ficha, salida + errores, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("respaldos 7", "cada línea lleva nombre")]
    [InlineData("respaldos 7 3", "antes de que venza su bloqueo")]
    [InlineData("respaldos 0 7", "mayores que cero")]
    [InlineData("MAL 7 7", "no es válido")]
    public void UnaDefinicionInvalida_SeRechazaConCodigo2(string contenedores, string mensaje)
    {
        var carpeta = Path.Combine(_carpeta, "definicion");
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(Path.Combine(carpeta, "contenedores.txt"), contenedores + "\n");
        File.WriteAllText(Path.Combine(carpeta, "credenciales.txt"), "# nada\n");

        var (codigo, _, errores) = Ejecutar(["r2-plan"], definicion: carpeta);

        Assert.Equal(2, codigo);
        Assert.Contains(mensaje, errores, StringComparison.Ordinal);
    }
}
