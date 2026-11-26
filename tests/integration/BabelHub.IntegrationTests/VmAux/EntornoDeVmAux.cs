using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace BabelHub.IntegrationTests.VmAux;

/// <summary>
/// Un almacén y un servidor de correo de mentira para probar los scripts de VM-AUX. El <c>rclone</c> de prueba guarda cada contenedor en una
/// carpeta y <b>aplica las matrices de credenciales de <c>infra/almacen/credenciales.txt</c> (Oracle) y
/// <c>infra/almacen/r2/credenciales.txt</c> (R2)</b>: cada remoto (<c>vigilante</c>, <c>registro</c>, <c>sello</c>, <c>copia</c>) es una credencial y solo puede listar, leer o crear lo que esa matriz le da; crear sobre un objeto que existe falla, como con
/// <c>OBJECT_CREATE</c> sin <c>OBJECT_OVERWRITE</c>. Así una prueba que pasa demuestra que el script solo usa lo permitido a su credencial.
/// El <c>curl</c> de prueba anota los argumentos, la configuración y el cuerpo de cada correo.
/// </summary>
internal sealed class EntornoDeVmAux : IDisposable
{
    private const string DobleDeRclone = """
        #!/usr/bin/env bash
        orden="$1"; shift
        echo "$orden $*" >>"$DOBLE_REGISTRO"
        posicionales=()
        while [[ $# -gt 0 ]]; do
          case "$1" in
            --format | --separator | --retries | --low-level-retries | --contimeout | --timeout) shift 2 ;;
            -*) shift ;;
            *) posicionales+=("$1"); shift ;;
          esac
        done
        permiso() { # remoto contenedor accion
          local credencial
          if [[ "$1" == vigilante ]]; then # token de R2 de solo lectura: lista y lee; nunca crea
            [[ "$3" == listar || "$3" == leer ]] && grep -qE "^respaldos-vigilante[[:space:]]+$2[[:space:]]+lectura([[:space:]]|$)" <(tr -d '\r' <"$DOBLE_CREDENCIALES_R2")
            return
          fi
          case "$1" in registro) credencial=registro-vigilante ;; sello) credencial=supresiones-sello ;; copia) credencial=respaldos-copia-registro ;; *) return 1 ;; esac
          while read -r c b a _; do
            [[ "$c" == "$credencial" && "$b" == "$2" && ",$a," == *",$3,"* ]] && return 0
          done < <(grep -v '^[[:space:]]*#' "$DOBLE_CREDENCIALES")
          return 1
        }
        destino() { remoto="${1%%:*}"; ruta="${1#*:}"; bucket="${ruta%%/*}"; clave="${ruta#*/}"; [[ "$ruta" == */* ]] || clave=""; }
        case "$orden" in
          lsf)
            destino "${posicionales[0]}"
            permiso "$remoto" "$bucket" listar || { echo "AccessDenied: $remoto no puede listar $bucket" >&2; exit 1; }
            [[ -f "$DOBLE_ALMACEN/$bucket.tiempos" ]] || { echo "no existe $bucket" >&2; exit 3; }
            [[ "${DOBLE_ROTO:-}" == "$bucket" ]] && { echo "error de red" >&2; exit 1; }
            cat "$DOBLE_ALMACEN/$bucket.tiempos" ;;
          cat)
            destino "${posicionales[0]}"
            permiso "$remoto" "$bucket" leer || { echo "AccessDenied: $remoto no puede leer $bucket" >&2; exit 1; }
            [[ -f "$DOBLE_ALMACEN/$bucket/$clave" ]] || { echo "no existe $clave" >&2; exit 1; }
            cat "$DOBLE_ALMACEN/$bucket/$clave" ;;
          copyto)
            destino "${posicionales[1]}"
            permiso "$remoto" "$bucket" crear || { echo "AccessDenied: $remoto no puede crear en $bucket" >&2; exit 1; }
            [[ "${DOBLE_ROTO:-}" == "$bucket" ]] && { echo "error de red" >&2; exit 1; }
            [[ -e "$DOBLE_ALMACEN/$bucket/$clave" ]] && { echo "ya existe $clave" >&2; exit 1; }
            mkdir -p "$(dirname "$DOBLE_ALMACEN/$bucket/$clave")"
            cp "${posicionales[0]}" "$DOBLE_ALMACEN/$bucket/$clave"
            echo "$clave;$(date -u '+%Y-%m-%d %H:%M:%S')" >>"$DOBLE_ALMACEN/$bucket.tiempos" ;;
          *) echo "orden no permitida: $orden" >&2; exit 1 ;;
        esac
        """;

    private const string DobleDeCurl = """
        #!/usr/bin/env bash
        n=$(( $(cat "$DOBLE_CORREOS/contador" 2>/dev/null || echo 0) + 1 )); echo "$n" >"$DOBLE_CORREOS/contador"
        echo "$@" >"$DOBLE_CORREOS/$n.argumentos"
        while [[ $# -gt 0 ]]; do
          case "$1" in
            --upload-file) cp "$2" "$DOBLE_CORREOS/$n.cuerpo"; shift 2 ;;
            --config) cp "$2" "$DOBLE_CORREOS/$n.configuracion"; shift 2 ;;
            *) shift ;;
          esac
        done
        [[ "${DOBLE_CURL_FALLA:-0}" == 1 ]] && exit 22
        exit 0
        """;

    public EntornoDeVmAux()
    {
        Carpeta = Path.Combine(Path.GetTempPath(), "vm-aux-" + Guid.NewGuid().ToString("N"));
        Almacen = Path.Combine(Carpeta, "almacen");
        Correos = Path.Combine(Carpeta, "correos");
        Estado = Path.Combine(Carpeta, "estado");
        Directory.CreateDirectory(Almacen);
        Directory.CreateDirectory(Correos);
        Directory.CreateDirectory(Estado);
        ArchivosDeDoble.Escribir(Path.Combine(Carpeta, "rclone-doble.sh"), DobleDeRclone.ReplaceLineEndings("\n"));
        ArchivosDeDoble.Escribir(Path.Combine(Carpeta, "curl-doble.sh"), DobleDeCurl.ReplaceLineEndings("\n"));
        foreach (var contenedor in PoliticasDelAlmacenNombres())
        {
            Directory.CreateDirectory(Path.Combine(Almacen, contenedor));
            File.WriteAllText(Path.Combine(Almacen, contenedor + ".tiempos"), string.Empty);
        }
    }

    public string Carpeta { get; }

    public string Almacen { get; }

    public string Correos { get; }

    public string Estado { get; }

    public string ClaveDeCorreo { get; } = "c" + Guid.NewGuid().ToString("N");

    public string DireccionDelLatido { get; } = "https://latido.ejemplo.interno/" + Guid.NewGuid().ToString("N");

    public static string EnLinux(string ruta) => ruta.Replace('\\', '/');

    private static IEnumerable<string> PoliticasDelAlmacenNombres() =>
        File.ReadAllLines(Path.Combine(RaizDelRepositorio(), "infra", "almacen", "contenedores.txt"))
            .Concat(File.ReadAllLines(Path.Combine(RaizDelRepositorio(), "infra", "almacen", "r2", "contenedores.txt")))
            .Select(l => l.Split('#')[0].Trim())
            .Where(l => l.Length > 0)
            .Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0]);

    public static string RaizDelRepositorio()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "BabelHub.sln")))
        {
            carpeta = carpeta.Parent;
        }

        return carpeta?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Carpeta, recursive: true);
        }
        catch (IOException)
        {
            // Carpeta temporal.
        }
    }

    public static string Hora(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Deja un objeto en un contenedor con la hora de modificación dada.</summary>
    public void Publicar(string contenedor, string clave, string contenido, DateTime modificado)
    {
        var ruta = Path.Combine(Almacen, contenedor, clave.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, contenido, new UTF8Encoding(false));
        File.AppendAllText(Path.Combine(Almacen, contenedor + ".tiempos"), $"{clave};{Hora(modificado)}\n");
    }

    public string? Leer(string contenedor, string clave)
    {
        var ruta = Path.Combine(Almacen, contenedor, clave.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(ruta) ? File.ReadAllText(ruta) : null;
    }

    public IReadOnlyList<string> Claves(string contenedor) =>
        File.ReadAllLines(Path.Combine(Almacen, contenedor + ".tiempos")).Where(l => l.Length > 0).Select(l => l.Split(';')[0]).ToList();

    public IReadOnlyList<string> Llamadas => File.Exists(Path.Combine(Carpeta, "llamadas.log")) ? File.ReadAllLines(Path.Combine(Carpeta, "llamadas.log")) : [];

    public int CorreosEnviados => File.Exists(Path.Combine(Correos, "contador")) ? int.Parse(File.ReadAllText(Path.Combine(Correos, "contador")).Trim(), CultureInfo.InvariantCulture) : 0;

    public string Cuerpo(int numero) => File.ReadAllText(Path.Combine(Correos, $"{numero}.cuerpo"));

    public string Argumentos(int numero) => File.ReadAllText(Path.Combine(Correos, $"{numero}.argumentos"));

    public string Configuracion(int numero) => File.ReadAllText(Path.Combine(Correos, $"{numero}.configuracion"));

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

    /// <summary>Ejecuta un script de <c>scripts/vm-aux</c> (o <c>alertar.sh</c> con su comando) con el entorno de prueba.</summary>
    public (int Codigo, string Salida, string Errores) Ejecutar(string script, IEnumerable<string>? argumentos = null, IDictionary<string, string>? entorno = null)
    {
        var informacion = new ProcessStartInfo(Bash()) { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        informacion.ArgumentList.Add(EnLinux(Path.Combine(RaizDelRepositorio(), "scripts", "vm-aux", script)));
        foreach (var argumento in argumentos ?? [])
        {
            informacion.ArgumentList.Add(argumento);
        }

        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RCLONE"] = EnLinux(Path.Combine(Carpeta, "rclone-doble.sh")),
            ["CURL"] = EnLinux(Path.Combine(Carpeta, "curl-doble.sh")),
            ["DOBLE_REGISTRO"] = EnLinux(Path.Combine(Carpeta, "llamadas.log")),
            ["DOBLE_ALMACEN"] = EnLinux(Almacen),
            ["DOBLE_CORREOS"] = EnLinux(Correos),
            ["DOBLE_CREDENCIALES"] = EnLinux(Path.Combine(RaizDelRepositorio(), "infra", "almacen", "credenciales.txt")),
            ["DOBLE_CREDENCIALES_R2"] = EnLinux(Path.Combine(RaizDelRepositorio(), "infra", "almacen", "r2", "credenciales.txt")),
            ["AUX_ENV"] = EnLinux(Path.Combine(Carpeta, "no-existe.env")),
            ["AUX_ESTADO"] = EnLinux(Estado),
            ["AUX_SMTP_URL"] = "smtps://smtp.ejemplo.interno:465",
            ["AUX_SMTP_USUARIO"] = "usuario-de-correo",
            ["AUX_SMTP_CLAVE"] = ClaveDeCorreo,
            ["AUX_CORREO_DE"] = "vm-aux@ejemplo.interno",
            ["AUX_CORREO_PARA"] = "responsable@ejemplo.interno",
            ["AUX_LATIDO_URL"] = DireccionDelLatido,
        };
        foreach (var (clave, valor) in entorno ?? new Dictionary<string, string>())
        {
            variables[clave] = valor;
        }

        foreach (var (clave, valor) in variables)
        {
            informacion.Environment[clave] = valor;
        }

        using var proceso = Process.Start(informacion)!;
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var errores = proceso.StandardError.ReadToEndAsync();
        Assert.True(proceso.WaitForExit(TimeSpan.FromMinutes(3)), "El script no terminó a tiempo.");
        proceso.WaitForExit();
        return (proceso.ExitCode, salida.Result, errores.Result);
    }

    /// <summary>El estado sano: respaldo y configuración recientes, archivado al día y una copia reciente del registro.</summary>
    public void PublicarElEstadoAlDia(DateTime ahora)
    {
        var dia = ahora.AddHours(-3);
        Publicar("respaldos-diarios", $"{dia:yyyy-MM-dd}/{dia:HHmmss}-{new string('a', 64)}.tar.zst.age", "x", dia);
        Publicar("respaldos-diarios", $"{dia:yyyy-MM-dd}/{dia:HHmmss}-config-{new string('b', 64)}.tar.zst.age", "x", dia);
        Publicar("respaldos-wal", $"000000010000000000000007-{new string('c', 64)}.zst.age", "x", ahora.AddMinutes(-4));
        Publicar("supresiones-copia", $"{dia:yyyy-MM-dd}/{dia:HHmmss}-{new string('d', 64)}.tar", "x", dia);
    }
}
