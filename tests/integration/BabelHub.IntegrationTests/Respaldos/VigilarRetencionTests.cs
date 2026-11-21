using System.Diagnostics;
using System.Globalization;
using System.Text;
using BabelHub.IntegrationTests.Imagenes;

namespace BabelHub.IntegrationTests.Respaldos;

/// <summary>
/// VM-AUX alerta si el almacén retiene más de lo esperado (PLA-18): <c>scripts/vm-aux/vigilar-retencion.sh</c> lista cada contenedor con
/// la credencial <c>respaldos-vigilante</c> (aquí, un doble de rclone con lo que «hay» en el almacén) y termina con 1 por cada exceso.
/// Con el estado que deja el ciclo de vida sobre 100 días de respaldos simulados no hay alerta; con un ciclo de vida que no retira, sí.
/// </summary>
[Collection(ProcesosPesadosCollection.Nombre)]
public sealed class VigilarRetencionTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "vigilar-" + Guid.NewGuid().ToString("N"));
    private readonly string _almacen;

    public VigilarRetencionTests()
    {
        _almacen = Path.Combine(_carpeta, "almacen");
        Directory.CreateDirectory(_almacen);
        ArchivosDeDoble.Escribir(
            Path.Combine(_carpeta, "rclone-doble.sh"),
            """
            #!/usr/bin/env bash
            destino="${@: -1}"; bucket="${destino#vigilante:}"
            archivo="$ALMACEN_FIXTURE/$bucket.txt"
            [[ -f "$archivo" ]] || { echo "error: no existe $bucket" >&2; exit 3; }
            cat "$archivo"
            """.ReplaceLineEndings("\n"));
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

    /// <summary>Escribe lo que el almacén «tiene» tras correr una simulación hasta <paramref name="ahora"/>; las horas son relativas a hoy.</summary>
    private void Publicar(IEnumerable<ObjetoSimulado> objetos, TimeSpan desplazamiento)
    {
        foreach (var grupo in objetos.GroupBy(o => o.Contenedor))
        {
            Escribir(grupo.Key, grupo.Select(o => (o.Clave, o.Creado + desplazamiento)));
        }
    }

    private void Escribir(string contenedor, IEnumerable<(string Clave, DateTime Hora)> filas) =>
        File.WriteAllLines(
            Path.Combine(_almacen, contenedor + ".txt"),
            filas.Select(f => $"{f.Clave};{f.Hora.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}"));

    private (int Codigo, string Salida, string Errores) Vigilar(string? definicion = null)
    {
        var informacion = new ProcessStartInfo(Bash()) { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        informacion.ArgumentList.Add(EnLinux(Path.Combine(ComposeDeVms.Raiz(), "scripts", "vm-aux", "vigilar-retencion.sh")));
        informacion.Environment["RCLONE"] = EnLinux(Path.Combine(_carpeta, "rclone-doble.sh"));
        informacion.Environment["ALMACEN_FIXTURE"] = EnLinux(_almacen);
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

    /// <summary>100 días de respaldos con el ciclo de vida publicado, desplazados para que el último día sea hoy.</summary>
    private (AlmacenSimulado Almacen, DateTime Ultimo) CienDias()
    {
        var almacen = new AlmacenSimulado();
        var inicio = new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var d = 0; d < 100; d++)
        {
            var dia = inicio.AddDays(d);
            almacen.ArchivarCambios(dia.AddDays(-1).AddHours(3), dia.AddHours(3));
            almacen.Respaldar(dia.AddHours(2).AddMinutes(30));
            almacen.EjecutarCicloDeVida(dia.AddHours(3));
        }

        return (almacen, inicio.AddDays(99).AddHours(3));
    }

    private void PublicarElEstadoSano()
    {
        var (almacen, ultimo) = CienDias();
        Publicar(almacen.Objetos, DateTime.UtcNow - ultimo);
        foreach (var vacio in new[] { "supresiones-entradas", "supresiones-sellos", "supresiones-copia", "libros" })
        {
            File.WriteAllText(Path.Combine(_almacen, vacio + ".txt"), string.Empty);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-18")]
    public void ConLoQueDejaElCicloDeVidaSobre100DiasDeRespaldos_NoHayAlerta()
    {
        PublicarElEstadoSano();

        var (codigo, salida, errores) = Vigilar();

        Assert.True(codigo == 0, salida + errores);
        Assert.Contains("ok respaldos-diarios", salida, StringComparison.Ordinal);
        Assert.Contains("ok respaldos-wal", salida, StringComparison.Ordinal);
        Assert.DoesNotContain("ALERTA", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-18")]
    public void SiElAlmacenNoRetiraNada_ElVigilanteAlertaPorCadaContenedorConExceso()
    {
        // Un ciclo de vida que no corre: 130 días de respaldos siguen en el almacén.
        var almacen = new AlmacenSimulado();
        var inicio = new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var d = 0; d < 130; d++)
        {
            almacen.Respaldar(inicio.AddDays(d).AddHours(2).AddMinutes(30));
        }

        almacen.ArchivarCambios(inicio.AddDays(-1), inicio.AddDays(130));
        Publicar(almacen.Objetos, DateTime.UtcNow - inicio.AddDays(129).AddHours(3));
        foreach (var vacio in new[] { "supresiones-copia" })
        {
            File.WriteAllText(Path.Combine(_almacen, vacio + ".txt"), string.Empty);
        }

        var (codigo, salida, _) = Vigilar();

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA respaldos-diarios:", salida, StringComparison.Ordinal);
        Assert.Contains("ALERTA respaldos-semanales:", salida, StringComparison.Ordinal);
        Assert.Contains("ALERTA respaldos-wal:", salida, StringComparison.Ordinal);
        Assert.Contains("copias distintas", salida, StringComparison.Ordinal);
        Assert.Contains("el ciclo de vida no los retira", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-18")]
    public void UnSoloObjetoMuyViejoEnElArchivoDeCambios_BastaParaAlertar()
    {
        PublicarElEstadoSano();
        File.AppendAllLines(Path.Combine(_almacen, "respaldos-wal.txt"), [$"0000000100000000000000AA-{new string('a', 64)}.age;{DateTime.UtcNow.AddDays(-130).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}"]);

        var (codigo, salida, _) = Vigilar();

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA respaldos-wal: 1 objetos con más de 102 días (el plazo es de 100)", salida, StringComparison.Ordinal);
        Assert.Contains("ok respaldos-diarios", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-18")]
    public void LaTolerancia_DejaPasarUnDiaDeDesfaseDelCicloDeVidaPeroNoUnaCopiaDeMasSostenida()
    {
        PublicarElEstadoSano();
        // Un día más de diarios (el que el almacén todavía no retiró): dentro del margen.
        File.AppendAllLines(Path.Combine(_almacen, "respaldos-diarios.txt"), [$"2027-01-01/023000-{new string('b', 64)}.tar.age;{DateTime.UtcNow.AddDays(-7).AddHours(-1).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}"]);

        var (margen, _, _) = Vigilar();
        File.AppendAllLines(Path.Combine(_almacen, "respaldos-diarios.txt"), [$"2027-01-02/023000-{new string('c', 64)}.tar.age;{DateTime.UtcNow.AddDays(-7).AddHours(-2).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}"]);
        var (exceso, salida, _) = Vigilar();

        Assert.Equal(0, margen);
        Assert.Equal(1, exceso);
        Assert.Contains("ALERTA respaldos-diarios: hay 9 copias distintas y se esperan 8 como máximo", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-18")]
    public void UnContenedorQueNoSePuedeConsultar_SeAlerta_NoSeDaPorBueno()
    {
        PublicarElEstadoSano();
        File.Delete(Path.Combine(_almacen, "respaldos-mensuales.txt"));

        var (codigo, salida, _) = Vigilar();

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA respaldos-mensuales: no se pudo consultar el contenedor", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-18")]
    public void LosContenedoresQueNoExpiran_NoSeMiden_AunqueTenganObjetosViejos()
    {
        PublicarElEstadoSano();
        File.WriteAllLines(Path.Combine(_almacen, "supresiones-entradas.txt"), ["entradas/x/baja-intencion.json;2020-01-01 00:00:00"]);
        File.Delete(Path.Combine(_almacen, "supresiones-entradas.txt"));

        var (codigo, salida, _) = Vigilar();

        Assert.Equal(0, codigo);
        Assert.DoesNotContain("supresiones-entradas", salida, StringComparison.Ordinal);
    }

    [Fact]
    public void UnaDefinicionInvalida_SeRechazaConCodigo2()
    {
        var definicion = Path.Combine(_carpeta, "definicion");
        Directory.CreateDirectory(definicion);
        File.WriteAllText(Path.Combine(definicion, "contenedores.txt"), "uno 7 si pronto\n");

        var (codigo, _, errores) = Vigilar(definicion);

        Assert.Equal(2, codigo);
        Assert.Contains("no es un número", errores, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-18")]
    public void ElVigilanteSoloListaElAlmacen_NoEscribeNiBorraNiLeeObjetos()
    {
        var texto = File.ReadAllText(Path.Combine(ComposeDeVms.Raiz(), "scripts", "vm-aux", "vigilar-retencion.sh"));

        var ordenesDeRclone = System.Text.RegularExpressions.Regex.Matches(texto, @"""\$RCLONE""\s+(\w+)", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(2)).Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Equal(["lsf"], ordenesDeRclone);
    }
}
