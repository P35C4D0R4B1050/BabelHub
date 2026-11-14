using System.Diagnostics;
using System.Text;

namespace BabelHub.IntegrationTests.Almacen;

/// <summary>
/// <c>scripts/almacen.sh</c> (PLA-16, PLA-35, PLA-38): genera el ciclo de vida, las sentencias de política y los comandos del CLI de Oracle
/// desde la definición, rechaza una definición inválida, no ejecuta nada en <c>plan</c>, no cambia lo que ya existe y <c>comprobar</c>
/// detecta cada diferencia con el almacén (contenedor público, retención ausente, corta o sin bloqueo, bloqueo pendiente, ciclo de vida
/// distinto). El CLI se sustituye por un doble que responde con archivos de prueba; la sintaxis real la confirma el spike T02.
/// </summary>
[Collection(BabelHub.IntegrationTests.Imagenes.ProcesosPesadosCollection.Nombre)]
public sealed class AlmacenShTests : IDisposable
{
    private const string Pasado = "2020-01-01T00:00:00Z";
    private const string Futuro = "2999-01-01T00:00:00Z";
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "almacen-" + Guid.NewGuid().ToString("N"));
    private readonly string _estado;
    private readonly string _doble;

    public AlmacenShTests()
    {
        _estado = Path.Combine(_carpeta, "estado");
        Directory.CreateDirectory(_estado);
        _doble = Path.Combine(_carpeta, "oci-doble.sh");
        File.WriteAllText(_doble, DobleDeOci.ReplaceLineEndings("\n"));
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

    // El doble responde a las consultas que hace el script y anota cada comando que cambia algo.
    private const string DobleDeOci = """
        #!/usr/bin/env bash
        bucket=""; consulta=""
        argumentos=("$@")
        for ((i = 0; i < ${#argumentos[@]}; i++)); do
          case "${argumentos[$i]}" in
            --bucket-name | --name) bucket="${argumentos[$((i + 1))]}" ;;
            --query) consulta="${argumentos[$((i + 1))]}" ;;
          esac
        done
        orden="$1 $2 $3"
        case "$orden" in
          "os bucket create" | "os retention-rule create" | "os object-lifecycle-policy put")
            echo "$orden $bucket" >>"$ALMACEN_ESTADO/cambios.log"
            exit 0 ;;
        esac
        archivo="$ALMACEN_ESTADO/$bucket.env"
        [[ -f "$archivo" ]] || { echo "ServiceError: no existe $bucket" >&2; exit 1; }
        source "$archivo"
        case "$orden" in
          "os bucket get") echo "${ACCESO:-null}" ;;
          "os retention-rule list")
            case "$consulta" in
              *time-amount*) echo "${RET_MONTO:-null}" ;;
              *time-unit*) echo "${RET_UNIDAD:-null}" ;;
              *time-rule-locked*) echo "${RET_BLOQUEO:-null}" ;;
            esac ;;
          "os object-lifecycle-policy get")
            case "$consulta" in
              *time-amount*) echo "${CICLO_MONTO:-null}" ;;
              *action*) echo "${CICLO_ACCION:-null}" ;;
              *is-enabled*) echo "${CICLO_HABILITADA:-null}" ;;
            esac ;;
        esac
        """;

    private static string Raiz() => Path.GetDirectoryName(Path.GetDirectoryName(PoliticasDelAlmacen.Carpeta()))!;

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

    private (int Codigo, string Salida, string Errores) Ejecutar(string? definicion, params string[] argumentos)
    {
        var informacion = new ProcessStartInfo(Bash()) { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        informacion.ArgumentList.Add(EnLinux(Path.Combine(Raiz(), "scripts", "almacen.sh")));
        foreach (var argumento in argumentos)
        {
            informacion.ArgumentList.Add(argumento);
        }

        informacion.Environment["OCI"] = EnLinux(_doble);
        informacion.Environment["ALMACEN_ESTADO"] = EnLinux(_estado);
        if (definicion is not null)
        {
            informacion.Environment["ALMACEN_DEFINICION"] = EnLinux(definicion);
        }

        using var proceso = Process.Start(informacion)!;
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var errores = proceso.StandardError.ReadToEndAsync();
        Assert.True(proceso.WaitForExit(TimeSpan.FromMinutes(2)), "El script no terminó a tiempo.");
        proceso.WaitForExit();
        return (proceso.ExitCode, salida.Result, errores.Result);
    }

    private string DefinicionCon(string contenedores, string? credenciales = null)
    {
        var carpeta = Path.Combine(_carpeta, "definicion-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(Path.Combine(carpeta, "contenedores.txt"), contenedores);
        File.WriteAllText(Path.Combine(carpeta, "credenciales.txt"), credenciales ?? "# credencial contenedor acciones\nescritor uno crear\n");
        return carpeta;
    }

    private void Estado(string bucket, string acceso = "NoPublicAccess", string? retencion = null, string unidad = "DAYS", string? bloqueo = null, string? ciclo = null, string accion = "DELETE", string habilitada = "true")
    {
        var texto = new StringBuilder();
        texto.AppendLine(CultureInfo(acceso, "ACCESO"));
        if (retencion is not null)
        {
            texto.AppendLine(CultureInfo(retencion, "RET_MONTO")).AppendLine(CultureInfo(unidad, "RET_UNIDAD"));
        }

        if (bloqueo is not null)
        {
            texto.AppendLine(CultureInfo(bloqueo, "RET_BLOQUEO"));
        }

        if (ciclo is not null)
        {
            texto.AppendLine(CultureInfo(ciclo, "CICLO_MONTO")).AppendLine(CultureInfo(accion, "CICLO_ACCION")).AppendLine(CultureInfo(habilitada, "CICLO_HABILITADA"));
        }

        File.WriteAllText(Path.Combine(_estado, bucket + ".env"), texto.ToString());
    }

    private static string CultureInfo(string valor, string nombre) => $"{nombre}={valor}";

    /// <summary>El almacén tal como debe quedar según la definición real.</summary>
    private void EstadoConforme(string bloqueo = Pasado)
    {
        foreach (var c in PoliticasDelAlmacen.Cargar().Contenedores)
        {
            Estado(
                c.Nombre,
                retencion: c.RetencionDias > 0 ? c.RetencionDias.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
                bloqueo: c.Bloqueada ? bloqueo : null,
                ciclo: c.ExpiraDias?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    // ---------- Salidas derivadas ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-16")]
    public void ElCicloDeVidaPublicado_EsElQueSeGeneraDeLaDefinicion()
    {
        var (codigo, salida, errores) = Ejecutar(null, "ciclo-de-vida");

        Assert.True(codigo == 0, errores);
        var publicado = File.ReadAllText(Path.Combine(PoliticasDelAlmacen.Carpeta(), "ciclo-de-vida.json")).ReplaceLineEndings("\n");
        Assert.Equal(publicado, salida.ReplaceLineEndings("\n"));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-16")]
    public void LasSentenciasDePolitica_DanASoloCrearALaCredencialDeRespaldo_YNuncaBorrarFueraDeLosLibros()
    {
        var (codigo, salida, errores) = Ejecutar(null, "politicas-iam", "--compartimento-nombre", "babelhub");

        Assert.True(codigo == 0, errores);
        var sentencias = salida.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(PoliticasDelAlmacen.Cargar().Concesiones.Count, sentencias.Length);
        Assert.All(sentencias.Where(s => s.StartsWith("Allow group respaldos-escritor ", StringComparison.Ordinal)), s =>
        {
            Assert.Contains("request.permission='OBJECT_CREATE'", s, StringComparison.Ordinal);
            Assert.DoesNotContain("OBJECT_READ", s, StringComparison.Ordinal);
            Assert.DoesNotContain("OBJECT_DELETE", s, StringComparison.Ordinal);
            Assert.DoesNotContain("OBJECT_OVERWRITE", s, StringComparison.Ordinal);
        });
        Assert.All(
            sentencias.Where(s => !s.StartsWith("Allow group libros ", StringComparison.Ordinal)),
            s =>
            {
                Assert.DoesNotContain("OBJECT_DELETE", s, StringComparison.Ordinal);
                Assert.DoesNotContain("OBJECT_OVERWRITE", s, StringComparison.Ordinal);
            });
        Assert.All(sentencias.Where(s => s.StartsWith("Allow group libros ", StringComparison.Ordinal)), s => Assert.Contains("target.bucket.name='libros'", s, StringComparison.Ordinal));
        Assert.All(sentencias.Where(s => s.StartsWith("Allow group supresiones-registro ", StringComparison.Ordinal)), s => Assert.Contains("target.bucket.name='supresiones-entradas'", s, StringComparison.Ordinal));
        Assert.DoesNotContain(sentencias, s => s.Contains("*", StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_MuestraLosComandosSinEjecutarNinguno()
    {
        var (codigo, salida, errores) = Ejecutar(null, "plan", "--compartimento-id", "ocid1.compartment.pruebas", "--compartimento-nombre", "babelhub", "--espacio", "espacio");

        Assert.True(codigo == 0, errores);
        Assert.False(File.Exists(Path.Combine(_estado, "cambios.log")), "plan no debe ejecutar nada");
        Assert.Equal(8, salida.Split('\n').Count(l => l.Contains(" os bucket create ", StringComparison.Ordinal)));
        Assert.Equal(7, salida.Split('\n').Count(l => l.Contains(" os retention-rule create ", StringComparison.Ordinal)));
        Assert.Equal(7, salida.Split('\n').Count(l => l.Contains("--time-rule-locked", StringComparison.Ordinal)));
        Assert.Equal(5, salida.Split('\n').Count(l => l.Contains(" os object-lifecycle-policy put ", StringComparison.Ordinal)));
        Assert.All(salida.Split('\n').Where(l => l.Contains(" os bucket create ", StringComparison.Ordinal)), l => Assert.Contains("--public-access-type NoPublicAccess", l, StringComparison.Ordinal));
        Assert.Matches(@"--time-rule-locked 20\d\d-\d\d-\d\dT\d\d:\d\d:\d\dZ", salida);
    }

    [Fact]
    public void Aplicar_CreaLoQueFalta_ConElBloqueoALos15Dias()
    {
        var (codigo, salida, errores) = Ejecutar(null, "aplicar", "--compartimento-id", "ocid1.compartment.pruebas", "--compartimento-nombre", "babelhub", "--espacio", "espacio");

        Assert.True(codigo == 0, errores);
        var cambios = File.ReadAllLines(Path.Combine(_estado, "cambios.log"));
        Assert.Equal(8, cambios.Count(c => c.StartsWith("os bucket create", StringComparison.Ordinal)));
        Assert.Equal(7, cambios.Count(c => c.StartsWith("os retention-rule create", StringComparison.Ordinal)));
        Assert.Equal(5, cambios.Count(c => c.StartsWith("os object-lifecycle-policy put", StringComparison.Ordinal)));
        Assert.Contains("Allow group respaldos-escritor", salida, StringComparison.Ordinal);
        Assert.Contains("queda efectivo el", salida, StringComparison.Ordinal);
    }

    [Fact]
    public void Aplicar_NoCambiaLoQueYaExiste_PorqueUnaReglaBloqueadaNoSeToca()
    {
        EstadoConforme();

        var (codigo, salida, errores) = Ejecutar(null, "aplicar", "--compartimento-id", "ocid1.compartment.pruebas", "--compartimento-nombre", "babelhub", "--espacio", "espacio");

        Assert.True(codigo == 0, errores);
        var cambios = File.Exists(Path.Combine(_estado, "cambios.log")) ? File.ReadAllLines(Path.Combine(_estado, "cambios.log")) : [];
        Assert.DoesNotContain(cambios, c => c.StartsWith("os bucket create", StringComparison.Ordinal));
        Assert.DoesNotContain(cambios, c => c.StartsWith("os retention-rule create", StringComparison.Ordinal));
        Assert.Contains("ya existe", salida, StringComparison.Ordinal);
    }

    // ---------- La definición inválida se rechaza ----------

    [Theory]
    [InlineData("uno 7 si 3", "antes de que venza su retención")]
    [InlineData("uno 0 si nunca", "bloqueo sin días")]
    [InlineData("uno siete si nunca", "no es un número de días")]
    [InlineData("uno 7 quizas nunca", "debe ser si o no")]
    [InlineData("uno 7 si pronto", "número o «nunca»")]
    [InlineData("UNO 7 si nunca", "no es válido")]
    [InlineData("uno 7 si nunca\nuno 7 si nunca", "repetido")]
    [InlineData("uno 7 si nunca extra", "sobran columnas")]
    public void UnaDefinicionDeContenedoresInvalida_SeRechazaConCodigo2(string contenedores, string mensaje)
    {
        var (codigo, _, errores) = Ejecutar(DefinicionCon(contenedores), "ciclo-de-vida");

        Assert.Equal(2, codigo);
        Assert.Contains(mensaje, errores, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("escritor otro crear", "contenedor desconocido")]
    [InlineData("escritor uno destruir", "no existe")]
    [InlineData("escritor uno", "debe tener credencial, contenedor y acciones")]
    public void UnaDefinicionDeCredencialesInvalida_SeRechazaConCodigo2(string credenciales, string mensaje)
    {
        var (codigo, _, errores) = Ejecutar(DefinicionCon("uno 7 si 7", credenciales), "politicas-iam", "--compartimento-nombre", "babelhub");

        Assert.Equal(2, codigo);
        Assert.Contains(mensaje, errores, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("desconocida")]
    [InlineData("plan")]
    [InlineData("comprobar")]
    [InlineData("politicas-iam")]
    public void UnUsoInvalido_SeRechazaConCodigo2(string orden)
    {
        var (codigo, _, errores) = Ejecutar(null, orden);

        Assert.Equal(2, codigo);
        Assert.False(string.IsNullOrWhiteSpace(errores));
    }

    // ---------- Comprobar contra el almacén ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-16")]
    public void Comprobar_ConElAlmacenConforme_TerminaBien()
    {
        EstadoConforme();

        var (codigo, salida, errores) = Ejecutar(null, "comprobar", "--espacio", "espacio");

        Assert.True(codigo == 0, salida + errores);
        Assert.Equal(8, salida.Split('\n').Count(l => l.StartsWith("ok ", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-16")]
    public void Comprobar_ConElBloqueoPendiente_TerminaConCodigo3_ASalvoQueSeAcepte()
    {
        EstadoConforme(Futuro);

        var (codigo, salida, _) = Ejecutar(null, "comprobar", "--espacio", "espacio");
        var (aceptado, _, _) = Ejecutar(null, "comprobar", "--espacio", "espacio", "--aceptar-bloqueo-pendiente");

        Assert.Equal(3, codigo);
        Assert.Contains("bloqueo pendiente", salida, StringComparison.Ordinal);
        Assert.Equal(0, aceptado);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-16")]
    public void Comprobar_SiUnaRetencionNoTieneBloqueo_Falla()
    {
        EstadoConforme();
        Estado("respaldos-wal", retencion: "100", ciclo: "100");

        var (codigo, salida, _) = Ejecutar(null, "comprobar", "--espacio", "espacio");

        Assert.Equal(1, codigo);
        Assert.Contains("x respaldos-wal: la retención no tiene bloqueo", salida, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-16")]
    [InlineData("respaldos-diarios", "PublicRead", "no es privado")]
    public void Comprobar_SiUnContenedorEsPublico_Falla(string contenedor, string acceso, string mensaje)
    {
        EstadoConforme();
        Estado(contenedor, acceso: acceso, retencion: "7", bloqueo: Pasado, ciclo: "7");

        var (codigo, salida, _) = Ejecutar(null, "comprobar", "--espacio", "espacio");

        Assert.Equal(1, codigo);
        Assert.Contains(mensaje, salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void Comprobar_SiLaRetencionDelRegistroEsMenorDe100Dias_Falla()
    {
        EstadoConforme();
        Estado("supresiones-entradas", retencion: "30", bloqueo: Pasado);

        var (codigo, salida, _) = Ejecutar(null, "comprobar", "--espacio", "espacio");

        Assert.Equal(1, codigo);
        Assert.Contains("x supresiones-entradas: la retención es de 30 DAYS y debe ser de al menos 100 días", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-16")]
    public void Comprobar_SiFaltaLaReglaDeRetencion_OElContenedorNoExiste_Falla()
    {
        EstadoConforme();
        Estado("respaldos-semanales", ciclo: "28");
        File.Delete(Path.Combine(_estado, "supresiones-copia.env"));

        var (codigo, salida, _) = Ejecutar(null, "comprobar", "--espacio", "espacio");

        Assert.Equal(1, codigo);
        Assert.Contains("x respaldos-semanales: no tiene regla de retención", salida, StringComparison.Ordinal);
        Assert.Contains("x supresiones-copia: no existe", salida, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-16")]
    [InlineData("14", "DELETE", "true")]
    [InlineData("7", "ARCHIVE", "true")]
    [InlineData("7", "DELETE", "false")]
    public void Comprobar_SiElCicloDeVidaNoCoincide_Falla(string dias, string accion, string habilitada)
    {
        EstadoConforme();
        Estado("respaldos-diarios", retencion: "7", bloqueo: Pasado, ciclo: dias, accion: accion, habilitada: habilitada);

        var (codigo, salida, _) = Ejecutar(null, "comprobar", "--espacio", "espacio");

        Assert.Equal(1, codigo);
        Assert.Contains("x respaldos-diarios: el ciclo de vida debe retirar a los 7 días", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void Comprobar_SiElRegistroDeSupresionesTieneCicloDeVida_Falla_PorqueNoDebeExpirar()
    {
        EstadoConforme();
        Estado("supresiones-sellos", retencion: "100", bloqueo: Pasado, ciclo: "100");

        var (codigo, salida, _) = Ejecutar(null, "comprobar", "--espacio", "espacio");

        Assert.Equal(1, codigo);
        Assert.Contains("x supresiones-sellos: tiene un ciclo de vida de 100 días y no debe expirar", salida, StringComparison.Ordinal);
    }

    [Fact]
    public void Comprobar_SiLaFechaDeBloqueoNoSeEntiende_Falla()
    {
        EstadoConforme();
        Estado("respaldos-mensuales", retencion: "90", bloqueo: "pronto", ciclo: "90");

        var (codigo, salida, _) = Ejecutar(null, "comprobar", "--espacio", "espacio");

        Assert.Equal(1, codigo);
        Assert.Contains("la fecha de bloqueo «pronto» no se entiende", salida, StringComparison.Ordinal);
    }
}
