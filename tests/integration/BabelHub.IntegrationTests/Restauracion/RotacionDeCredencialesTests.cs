using System.Diagnostics;
using System.Text;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.Restauracion;

/// <summary>
/// PLA-43 y AC-PLA-43: al restaurar en producción se rotan las credenciales de servicio, las contraseñas de los roles de base y los
/// secretos de integraciones internas (<c>scripts/rotar-credenciales.sh</c>), con el mismo valor nuevo en el E-commerce y en el
/// Inventario, sin dejar vigente la anterior y sin que ningún valor salga por pantalla. Los valores de estas pruebas se generan en cada
/// ejecución: nunca hay un secreto fijo en el repositorio.
/// </summary>
public sealed class RotacionDeCredencialesTests : IAsyncLifetime
{
    private const string Contenido = "BABELHUB__Seguridad__Credenciales__";
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "rotacion-" + Guid.NewGuid().ToString("N"));

    private readonly PostgreSqlContainer _base = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pruebas")
        .WithUsername("postgres")
        .WithPassword("postgres-" + Guid.NewGuid().ToString("N"))
        .Build();

    private static string Secreto() => "s" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_carpeta);
        await _base.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _base.DisposeAsync();
        try
        {
            Directory.Delete(_carpeta, recursive: true);
        }
        catch (IOException)
        {
            // Limpieza de una carpeta temporal: no debe hacer fallar la prueba.
        }
    }

    private static string RaizDelRepositorio()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "BabelHub.sln")))
        {
            carpeta = carpeta.Parent;
        }

        return carpeta?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

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

    private (int Codigo, string Salida) Rotar(Dictionary<string, string>? entorno, params string[] argumentos)
    {
        var informacion = new ProcessStartInfo(Bash())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = RaizDelRepositorio(),
        };
        informacion.ArgumentList.Add("scripts/rotar-credenciales.sh");
        foreach (var argumento in argumentos)
        {
            informacion.ArgumentList.Add(argumento);
        }

        foreach (var (clave, valor) in entorno ?? [])
        {
            informacion.Environment[clave] = valor;
        }

        using var proceso = Process.Start(informacion)!;
        var salida = new StringBuilder();
        proceso.OutputDataReceived += (_, e) => salida.AppendLine(e.Data);
        proceso.ErrorDataReceived += (_, e) => salida.AppendLine(e.Data);
        proceso.BeginOutputReadLine();
        proceso.BeginErrorReadLine();
        if (!proceso.WaitForExit(TimeSpan.FromMinutes(2)))
        {
            proceso.Kill(entireProcessTree: true);
            throw new TimeoutException("El script no terminó a tiempo.");
        }

        proceso.WaitForExit();
        return (proceso.ExitCode, salida.ToString());
    }

    private string Archivo(string nombre, params string[] lineas)
    {
        var ruta = Path.Combine(_carpeta, nombre);
        File.WriteAllText(ruta, string.Join('\n', lineas) + "\n", new UTF8Encoding(false));
        return ruta;
    }

    private static Dictionary<string, string> Variables(string ruta) =>
        File.ReadAllLines(ruta)
            .Where(l => l.Length > 0 && !l.StartsWith('#') && l.Contains('='))
            .ToDictionary(l => l[..l.IndexOf('=', StringComparison.Ordinal)], l => l[(l.IndexOf('=', StringComparison.Ordinal) + 1)..], StringComparer.Ordinal);

    private static IConfiguration ComoConfiguracion(string ruta) =>
        new ConfigurationBuilder().AddInMemoryCollection(Variables(ruta)
            .Where(p => p.Key.StartsWith("BABELHUB__", StringComparison.Ordinal))
            .Select(p => new KeyValuePair<string, string?>(p.Key["BABELHUB__".Length..].Replace("__", ":", StringComparison.Ordinal), p.Value))).Build();

    private static string ClaveDeHuella() => "v1:" + Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    private static string[] Alcances => ["OperacionDeUsuario", "OrigenReenviado"];

    private ConfiguracionDeSeguridad Cargar(string ruta) =>
        ConfiguracionDeSeguridad.Cargar(ComoConfiguracion(ruta), [AlcanceDeServicio.OperacionDeUsuario, AlcanceDeServicio.OrigenReenviado], []);

    // ---------- Credenciales de servicio ----------

    private sealed record Entornos(string Ecommerce, string Inventario, Dictionary<string, string> Viejos);

    private Entornos DosEntornos(params string[] lineasExtraDelEcommerce)
    {
        var viejos = new Dictionary<string, string>
        {
            ["OperacionDeUsuario"] = Secreto(),
            ["OperacionDeUsuarioAnterior"] = Secreto(),
            ["OrigenReenviado"] = Secreto(),
            ["LecturaDeDisponibilidad"] = Secreto(),
        };
        var huella = ClaveDeHuella();
        var ecommerce = Archivo(
            "ecommerce.env",
            [
                "# secretos del E-commerce (no están en el repositorio)",
                $"BABELHUB__Seguridad__ClaveDeHuella={huella}",
                $"{Contenido}OperacionDeUsuario={viejos["OperacionDeUsuario"]}",
                $"{Contenido}OperacionDeUsuarioAnterior={viejos["OperacionDeUsuarioAnterior"]}",
                $"{Contenido}OrigenReenviado={viejos["OrigenReenviado"]}",
                $"{Contenido}LecturaDeDisponibilidad={viejos["LecturaDeDisponibilidad"]}",
                "ASPNETCORE_ENVIRONMENT=Production",
                .. lineasExtraDelEcommerce,
            ]);
        var inventario = Archivo(
            "inventario.env",
            [
                $"BABELHUB__Seguridad__ClaveDeHuella={ClaveDeHuella()}",
                $"{Contenido}OperacionDeUsuario={viejos["OperacionDeUsuario"]}",
                $"{Contenido}OrigenReenviado={viejos["OrigenReenviado"]}",
                $"{Contenido}LecturaDeDisponibilidad={viejos["LecturaDeDisponibilidad"]}",
            ]);
        return new Entornos(ecommerce, inventario, viejos);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public void LasCredencialesDeServicioSeRotanConElMismoValorNuevoEnAmbosServicios_YLaAnteriorYaNoSirve()
    {
        var e = DosEntornos();
        var antesDelInventario = Cargar(e.Inventario);

        var (codigo, salida) = Rotar(null, "--entorno", $"ecommerce={EnLinux(e.Ecommerce)}", "--entorno", $"inventario={EnLinux(e.Inventario)}");

        Assert.True(codigo == 0, salida);
        var nuevoEcommerce = Variables(e.Ecommerce);
        var nuevoInventario = Variables(e.Inventario);
        foreach (var alcance in new[] { "OperacionDeUsuario", "OrigenReenviado", "LecturaDeDisponibilidad" })
        {
            var variable = Contenido + alcance;
            // El mismo valor nuevo en los dos servicios (si no, dejarían de entenderse) y distinto del viejo.
            Assert.Equal(nuevoEcommerce[variable], nuevoInventario[variable]);
            Assert.NotEqual(e.Viejos[alcance], nuevoEcommerce[variable]);
            Assert.True(nuevoEcommerce[variable].Length >= 48);
        }

        // La «Anterior» desaparece: no hay rotación en dos pasos, la credencial vieja deja de valer de inmediato.
        Assert.DoesNotContain(Contenido + "OperacionDeUsuarioAnterior", nuevoEcommerce.Keys);
        Assert.Equal(3, new[] { "OperacionDeUsuario", "OrigenReenviado", "LecturaDeDisponibilidad" }.Select(a => nuevoEcommerce[Contenido + a]).Distinct().Count());

        // Con la configuración real del servicio: la credencial anterior se rechaza y la nueva se acepta.
        var despues = Cargar(e.Inventario);
        Assert.NotNull(antesDelInventario.CredencialesDeEntrada.Identificar(e.Viejos["OperacionDeUsuario"]).Alcance);
        Assert.Null(despues.CredencialesDeEntrada.Identificar(e.Viejos["OperacionDeUsuario"]).Alcance);
        Assert.Null(despues.CredencialesDeEntrada.Identificar(e.Viejos["OperacionDeUsuarioAnterior"]).Alcance);
        Assert.Equal(AlcanceDeServicio.OperacionDeUsuario, despues.CredencialesDeEntrada.Identificar(nuevoInventario[Contenido + "OperacionDeUsuario"]).Alcance);
        Assert.Equal(AlcanceDeServicio.OrigenReenviado, despues.CredencialesDeEntrada.Identificar(nuevoEcommerce[Contenido + "OrigenReenviado"]).Alcance);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public void LoQueNoEsUnaCredencialDeServicioSeConservaIntactoYElArchivoConservaSusPermisos()
    {
        var e = DosEntornos("OTRA=cosa-que-no-se-toca");
        var antes = File.ReadAllLines(e.Ecommerce);
        UnixFileMode? modoAntes = null;
        if (!OperatingSystem.IsWindows())
        {
            modoAntes = File.GetUnixFileMode(e.Ecommerce);
        }

        var (codigo, salida) = Rotar(null, "--entorno", $"ecommerce={EnLinux(e.Ecommerce)}", "--entorno", $"inventario={EnLinux(e.Inventario)}");

        Assert.True(codigo == 0, salida);
        var despues = File.ReadAllLines(e.Ecommerce);
        Assert.Contains("# secretos del E-commerce (no están en el repositorio)", despues);
        Assert.Contains("ASPNETCORE_ENVIRONMENT=Production", despues);
        Assert.Contains("OTRA=cosa-que-no-se-toca", despues);
        Assert.Equal(antes.Count(l => !l.Contains("Anterior", StringComparison.Ordinal)), despues.Length);
        Assert.Equal(antes.First(l => l.StartsWith("BABELHUB__Seguridad__ClaveDeHuella=", StringComparison.Ordinal)), despues.First(l => l.StartsWith("BABELHUB__Seguridad__ClaveDeHuella=", StringComparison.Ordinal)));
        Assert.Empty(Directory.GetFiles(_carpeta, "*.rotando.*"));
        if (modoAntes is not null && !OperatingSystem.IsWindows())
        {
            Assert.Equal(modoAntes, File.GetUnixFileMode(e.Ecommerce));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public void NingunValorSaleNiPorPantallaNiPorLosArgumentos_SoloLosNombres()
    {
        var e = DosEntornos();

        var (codigo, salida) = Rotar(null, "--entorno", $"ecommerce={EnLinux(e.Ecommerce)}", "--entorno", $"inventario={EnLinux(e.Inventario)}");

        Assert.True(codigo == 0, salida);
        foreach (var viejo in e.Viejos.Values.Concat(Variables(e.Ecommerce).Where(p => p.Key.StartsWith(Contenido, StringComparison.Ordinal)).Select(p => p.Value)))
        {
            Assert.DoesNotContain(viejo, salida, StringComparison.Ordinal);
        }

        Assert.Contains("rotada: BABELHUB__Seguridad__Credenciales__OperacionDeUsuario", salida, StringComparison.Ordinal);
        Assert.Contains("Identidad", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public void LoQueEmiteUnProveedorNoSeRotaYSeListaComoPendienteSoloPorNombre()
    {
        var almacen = Secreto();
        var pasarela = Secreto();
        var e = DosEntornos(
            $"BABELHUB__Almacen__Supresiones__ClaveSecreta={almacen}",
            $"BABELHUB__Integraciones__Culqi__Secreto={pasarela}");

        var (codigo, salida) = Rotar(null, "--entorno", $"ecommerce={EnLinux(e.Ecommerce)}", "--entorno", $"inventario={EnLinux(e.Inventario)}");

        Assert.True(codigo == 0, salida);
        var despues = Variables(e.Ecommerce);
        Assert.Equal(almacen, despues["BABELHUB__Almacen__Supresiones__ClaveSecreta"]);
        Assert.Equal(pasarela, despues["BABELHUB__Integraciones__Culqi__Secreto"]);
        Assert.Contains("pendiente: BABELHUB__Almacen__Supresiones__ClaveSecreta (ecommerce)", salida, StringComparison.Ordinal);
        Assert.Contains("pendiente: BABELHUB__Integraciones__Culqi__Secreto (ecommerce)", salida, StringComparison.Ordinal);
        Assert.DoesNotContain(almacen, salida, StringComparison.Ordinal);
        Assert.DoesNotContain(pasarela, salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public void UnaIntegracionInternaSeRotaYLaDeUnProveedorSigueComoPendiente()
    {
        var n8n = Secreto();
        var culqi = Secreto();
        var e = DosEntornos($"BABELHUB__Integraciones__N8n__Secreto={n8n}", $"BABELHUB__Integraciones__N8n__SecretoAnterior={Secreto()}", $"BABELHUB__Integraciones__Culqi__Secreto={culqi}");

        var (codigo, salida) = Rotar(null, "--entorno", $"ecommerce={EnLinux(e.Ecommerce)}", "--entorno", $"inventario={EnLinux(e.Inventario)}", "--integracion-interna", "N8n");

        Assert.True(codigo == 0, salida);
        var despues = Variables(e.Ecommerce);
        Assert.NotEqual(n8n, despues["BABELHUB__Integraciones__N8n__Secreto"]);
        Assert.DoesNotContain("BABELHUB__Integraciones__N8n__SecretoAnterior", despues.Keys);
        Assert.Equal(culqi, despues["BABELHUB__Integraciones__Culqi__Secreto"]);
        Assert.Contains("rotada: BABELHUB__Integraciones__N8n__Secreto", salida, StringComparison.Ordinal);
        Assert.Contains("pendiente: BABELHUB__Integraciones__Culqi__Secreto", salida, StringComparison.Ordinal);
    }

    // ---------- Contraseñas de roles de base de datos ----------

    private string ComandoPsql() => $"docker exec -i {_base.Id} psql -U postgres -d pruebas -X -q -v ON_ERROR_STOP=1";

    private async Task CrearRol(string rol, string contrasena)
    {
        await using var conexion = new NpgsqlConnection(_base.GetConnectionString());
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand($"CREATE ROLE \"{rol}\" LOGIN PASSWORD '{contrasena}'", conexion);
        await comando.ExecuteNonQueryAsync();
    }

    private async Task<bool> PuedeIngresar(string rol, string contrasena)
    {
        var cadena = new NpgsqlConnectionStringBuilder(_base.GetConnectionString()) { Username = rol, Password = contrasena, Pooling = false };
        try
        {
            await using var conexion = new NpgsqlConnection(cadena.ConnectionString);
            await conexion.OpenAsync();
            return true;
        }
        catch (PostgresException error) when (error.SqlState == "28P01")
        {
            return false;
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public async Task LaContrasenaDeUnRolDeBaseSeRotaEnLaBaseYEnElArchivo_LaAnteriorYaNoEntra()
    {
        var vieja = Secreto();
        var intacta = Secreto();
        await CrearRol("acceso_ecommerce", vieja);
        await CrearRol("acceso_inventario", intacta);
        var e = DosEntornos($"BABELHUB__Base__Contrasena={vieja}");

        var (codigo, salida) = Rotar(
            new Dictionary<string, string> { ["PSQL_ROTACION"] = ComandoPsql() },
            "--entorno", $"ecommerce={EnLinux(e.Ecommerce)}",
            "--entorno", $"inventario={EnLinux(e.Inventario)}",
            "--rol", "acceso_ecommerce=BABELHUB__Base__Contrasena@ecommerce");

        Assert.True(codigo == 0, salida);
        var nueva = Variables(e.Ecommerce)["BABELHUB__Base__Contrasena"];
        Assert.NotEqual(vieja, nueva);
        Assert.False(await PuedeIngresar("acceso_ecommerce", vieja));
        Assert.True(await PuedeIngresar("acceso_ecommerce", nueva));
        Assert.True(await PuedeIngresar("acceso_inventario", intacta));
        Assert.DoesNotContain(vieja, salida, StringComparison.Ordinal);
        Assert.DoesNotContain(nueva, salida, StringComparison.Ordinal);
        Assert.Contains("rotada: BABELHUB__Base__Contrasena", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public async Task SiFallaElCambioDeUnRol_LosYaCambiadosVuelvenAlaAnteriorYNingunArchivoSeToca()
    {
        var viejaUno = Secreto();
        var viejaDos = Secreto();
        await CrearRol("rol_uno", viejaUno);
        // El segundo rol no existe: el ALTER ROLE falla después de haber cambiado el primero.
        var e = DosEntornos($"BABELHUB__Base__ContrasenaUno={viejaUno}", $"BABELHUB__Base__ContrasenaDos={viejaDos}");
        var antesEcommerce = File.ReadAllText(e.Ecommerce);
        var antesInventario = File.ReadAllText(e.Inventario);

        var (codigo, salida) = Rotar(
            new Dictionary<string, string> { ["PSQL_ROTACION"] = ComandoPsql() },
            "--entorno", $"ecommerce={EnLinux(e.Ecommerce)}",
            "--entorno", $"inventario={EnLinux(e.Inventario)}",
            "--rol", "rol_uno=BABELHUB__Base__ContrasenaUno@ecommerce",
            "--rol", "rol_que_no_existe=BABELHUB__Base__ContrasenaDos@ecommerce");

        Assert.Equal(1, codigo);
        Assert.Equal(antesEcommerce, File.ReadAllText(e.Ecommerce));
        Assert.Equal(antesInventario, File.ReadAllText(e.Inventario));
        Assert.True(await PuedeIngresar("rol_uno", viejaUno), "El primer rol no volvió a su contraseña anterior.");
        Assert.Contains("se revierte", salida, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_carpeta, "*.rotando.*"));
    }

    // ---------- Uso inválido: sin cambios ----------

    [Theory]
    [Trait("Criterio", "AC-PLA-43")]
    [InlineData("sin-argumentos")]
    [InlineData("archivo-inexistente")]
    [InlineData("nada-que-rotar")]
    [InlineData("rol-mal-formado")]
    [InlineData("rol-con-variable-ausente")]
    [InlineData("rol-de-un-entorno-no-indicado")]
    [InlineData("integracion-ausente")]
    [InlineData("opcion-desconocida")]
    [InlineData("nombre-de-entorno-invalido")]
    public void UnUsoInvalidoSaleConDosYNoCambiaNingunArchivo(string caso)
    {
        var e = DosEntornos();
        var sinCredenciales = Archivo("sin-credenciales.env", "ASPNETCORE_ENVIRONMENT=Production");
        var antes = File.ReadAllText(e.Ecommerce);
        string[] argumentos = caso switch
        {
            "sin-argumentos" => [],
            "archivo-inexistente" => ["--entorno", $"ecommerce={EnLinux(Path.Combine(_carpeta, "no-existe.env"))}"],
            "nada-que-rotar" => ["--entorno", $"ecommerce={EnLinux(sinCredenciales)}"],
            "rol-mal-formado" => ["--entorno", $"ecommerce={EnLinux(e.Ecommerce)}", "--rol", "rol; DROP TABLE x=VAR@ecommerce"],
            "rol-con-variable-ausente" => ["--entorno", $"ecommerce={EnLinux(e.Ecommerce)}", "--rol", "acceso=BABELHUB__Base__NoExiste@ecommerce"],
            "rol-de-un-entorno-no-indicado" => ["--entorno", $"ecommerce={EnLinux(e.Ecommerce)}", "--rol", "acceso=BABELHUB__Base__Contrasena@inventario"],
            "integracion-ausente" => ["--entorno", $"ecommerce={EnLinux(e.Ecommerce)}", "--integracion-interna", "NoExiste"],
            "opcion-desconocida" => ["--entorno", $"ecommerce={EnLinux(e.Ecommerce)}", "--otra-cosa"],
            _ => ["--entorno", $"Con Espacio={EnLinux(e.Ecommerce)}"],
        };

        var (codigo, _) = Rotar(null, argumentos);

        Assert.Equal(2, codigo);
        Assert.Equal(antes, File.ReadAllText(e.Ecommerce));
    }

    [Fact]
    public void DosRotacionesSeguidasDanValoresDistintos()
    {
        var e = DosEntornos();
        string[] argumentos = ["--entorno", $"ecommerce={EnLinux(e.Ecommerce)}", "--entorno", $"inventario={EnLinux(e.Inventario)}"];

        Assert.Equal(0, Rotar(null, argumentos).Codigo);
        var primera = Variables(e.Ecommerce)[Contenido + "OperacionDeUsuario"];
        Assert.Equal(0, Rotar(null, argumentos).Codigo);
        var segunda = Variables(e.Ecommerce)[Contenido + "OperacionDeUsuario"];

        Assert.NotEqual(primera, segunda);
    }
}
