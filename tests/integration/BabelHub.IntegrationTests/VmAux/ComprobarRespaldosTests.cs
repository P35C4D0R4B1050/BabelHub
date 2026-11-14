using BabelHub.IntegrationTests.Imagenes;

namespace BabelHub.IntegrationTests.VmAux;

/// <summary>
/// PLA-20 y AC-PLA-20: VM-AUX comprueba desde fuera de la VM de datos que hay un respaldo completado en las últimas 24 horas y que el archivado
/// de cambios no tiene más de 15 minutos de atraso, y avisa por correo si no es así; con todo al día no hay aviso. Solo usa la credencial
/// <c>respaldos-vigilante</c>, que únicamente lista.
/// </summary>
[Collection(ProcesosPesadosCollection.Nombre)]
public sealed class ComprobarRespaldosTests : IDisposable
{
    private readonly EntornoDeVmAux _entorno = new();

    public void Dispose() => _entorno.Dispose();

    private static string Base(DateTime dia) => $"{dia:yyyy-MM-dd}/{dia:HHmmss}-{new string('a', 64)}.tar.age";

    private static string Config(DateTime dia) => $"{dia:yyyy-MM-dd}/{dia:HHmmss}-config-{new string('b', 64)}.tar.age";

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void ConTodoAlDia_NoHayAlertaNiCorreo()
    {
        _entorno.PublicarElEstadoAlDia(DateTime.UtcNow);

        var (codigo, salida, errores) = _entorno.Ejecutar("alertar.sh", ["respaldos", EntornoDeVmAux.EnLinux(Path.Combine(EntornoDeVmAux.RaizDelRepositorio(), "scripts", "vm-aux", "comprobar-respaldos.sh"))]);

        Assert.True(codigo == 0, salida + errores);
        Assert.Equal(0, _entorno.CorreosEnviados);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void ElComprobadorSolo_InformaOkPorCadaCosaAlDia()
    {
        _entorno.PublicarElEstadoAlDia(DateTime.UtcNow);

        var (codigo, salida, _) = _entorno.Ejecutar("comprobar-respaldos.sh");

        Assert.Equal(0, codigo);
        Assert.Contains("ok respaldo del clúster", salida, StringComparison.Ordinal);
        Assert.Contains("ok configuración del respaldo", salida, StringComparison.Ordinal);
        Assert.Contains("ok archivado de cambios", salida, StringComparison.Ordinal);
        Assert.Contains("ok copia del registro de supresiones", salida, StringComparison.Ordinal);
        Assert.DoesNotContain("ALERTA", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void UnRespaldoDiarioQueFalla_ElUltimoTiene25Horas_AvisaPorCorreo()
    {
        var ahora = DateTime.UtcNow;
        _entorno.PublicarElEstadoAlDia(ahora);
        // El respaldo de hoy falló: lo último que hay es de hace 25 horas.
        File.WriteAllText(Path.Combine(_entorno.Almacen, "respaldos-diarios.tiempos"), string.Empty);
        _entorno.Publicar("respaldos-diarios", Base(ahora.AddHours(-25)), "x", ahora.AddHours(-25));
        _entorno.Publicar("respaldos-diarios", Config(ahora.AddHours(-25)), "x", ahora.AddHours(-25));

        var (codigo, _, _) = _entorno.Ejecutar("alertar.sh", ["respaldos", EntornoDeVmAux.EnLinux(Path.Combine(EntornoDeVmAux.RaizDelRepositorio(), "scripts", "vm-aux", "comprobar-respaldos.sh"))]);

        Assert.Equal(1, codigo);
        Assert.Equal(1, _entorno.CorreosEnviados);
        Assert.Contains("ALERTA respaldo del clúster: lo último es de hace 25 h", _entorno.Cuerpo(1), StringComparison.Ordinal);
        Assert.Contains("--mail-rcpt responsable@ejemplo.interno", _entorno.Argumentos(1), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void UnaVmCaida_SinRespaldosNiArchivadoRecientes_AvisaPorCorreo()
    {
        var ahora = DateTime.UtcNow;
        // La VM de datos cayó hace 30 horas: nada nuevo desde entonces.
        _entorno.Publicar("respaldos-diarios", Base(ahora.AddHours(-30)), "x", ahora.AddHours(-30));
        _entorno.Publicar("respaldos-diarios", Config(ahora.AddHours(-30)), "x", ahora.AddHours(-30));
        _entorno.Publicar("respaldos-wal", $"000000010000000000000007-{new string('c', 64)}.age", "x", ahora.AddHours(-30));
        _entorno.Publicar("supresiones-copia", $"2026-01-01/000000-{new string('d', 64)}.tar", "x", ahora.AddHours(-30));

        var (codigo, _, _) = _entorno.Ejecutar("alertar.sh", ["respaldos", EntornoDeVmAux.EnLinux(Path.Combine(EntornoDeVmAux.RaizDelRepositorio(), "scripts", "vm-aux", "comprobar-respaldos.sh"))]);

        Assert.Equal(1, codigo);
        Assert.Equal(1, _entorno.CorreosEnviados);
        var cuerpo = _entorno.Cuerpo(1);
        Assert.Contains("ALERTA respaldo del clúster", cuerpo, StringComparison.Ordinal);
        Assert.Contains("ALERTA archivado de cambios", cuerpo, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-20")]
    [InlineData(16, true)]
    [InlineData(14, false)]
    [InlineData(5, false)]
    [InlineData(120, true)]
    public void ElArchivadoConAtraso_AvisaSoloPasadosLos15Minutos(int minutos, bool alerta)
    {
        var ahora = DateTime.UtcNow;
        _entorno.PublicarElEstadoAlDia(ahora);
        File.WriteAllText(Path.Combine(_entorno.Almacen, "respaldos-wal.tiempos"), string.Empty);
        _entorno.Publicar("respaldos-wal", $"000000010000000000000009-{new string('c', 64)}.age", "x", ahora.AddMinutes(-minutos));
        // Segmentos anteriores más viejos: lo que cuenta es el último archivado.
        _entorno.Publicar("respaldos-wal", $"000000010000000000000001-{new string('e', 64)}.age", "x", ahora.AddHours(-5));

        var (codigo, salida, _) = _entorno.Ejecutar("comprobar-respaldos.sh");

        Assert.Equal(alerta ? 1 : 0, codigo);
        Assert.Equal(alerta, salida.Contains("ALERTA archivado de cambios", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void SiFaltaLaConfiguracionDelRespaldo_SeAlerta_AunqueElClusterEsteAlDia()
    {
        var ahora = DateTime.UtcNow;
        _entorno.PublicarElEstadoAlDia(ahora);
        File.WriteAllText(Path.Combine(_entorno.Almacen, "respaldos-diarios.tiempos"), string.Empty);
        _entorno.Publicar("respaldos-diarios", Base(ahora.AddHours(-2)), "x", ahora.AddHours(-2));

        var (codigo, salida, _) = _entorno.Ejecutar("comprobar-respaldos.sh");

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA configuración del respaldo: no hay ninguna configuración respaldada", salida, StringComparison.Ordinal);
        Assert.Contains("ok respaldo del clúster", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void SiNoHayNingunRespaldoNiSegmento_SeAlerta()
    {
        var (codigo, salida, _) = _entorno.Ejecutar("comprobar-respaldos.sh");

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA respaldo del clúster: no hay ningún respaldo", salida, StringComparison.Ordinal);
        Assert.Contains("ALERTA archivado de cambios: no hay ningún segmento archivado", salida, StringComparison.Ordinal);
        Assert.Contains("ALERTA copia del registro de supresiones: no hay ninguna copia del registro", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void UnContenedorQueNoSePuedeConsultar_SeAlerta_NoSeDaPorBueno()
    {
        _entorno.PublicarElEstadoAlDia(DateTime.UtcNow);

        var (codigo, salida, _) = _entorno.Ejecutar("comprobar-respaldos.sh", null, new Dictionary<string, string> { ["DOBLE_ROTO"] = "respaldos-wal" });

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA archivado de cambios: no se pudo consultar el contenedor respaldos-wal", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void ElComprobador_UsaSoloLaCredencialQueListaYNingunaOtra()
    {
        _entorno.PublicarElEstadoAlDia(DateTime.UtcNow);

        var (codigo, salida, errores) = _entorno.Ejecutar("comprobar-respaldos.sh");

        Assert.True(codigo == 0, salida + errores);
        Assert.NotEmpty(_entorno.Llamadas);
        Assert.All(_entorno.Llamadas, l => Assert.StartsWith("lsf ", l, StringComparison.Ordinal));
        Assert.All(_entorno.Llamadas, l => Assert.Contains("vigilante:", l, StringComparison.Ordinal));
        Assert.DoesNotContain("AccessDenied", errores, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void LosUmbralesSePuedenAjustar_ConVariablesDeEntorno()
    {
        var ahora = DateTime.UtcNow;
        _entorno.PublicarElEstadoAlDia(ahora);
        File.WriteAllText(Path.Combine(_entorno.Almacen, "respaldos-wal.tiempos"), string.Empty);
        _entorno.Publicar("respaldos-wal", $"000000010000000000000009-{new string('c', 64)}.age", "x", ahora.AddMinutes(-20));

        var (conDefecto, _, _) = _entorno.Ejecutar("comprobar-respaldos.sh");
        var (ajustado, _, _) = _entorno.Ejecutar("comprobar-respaldos.sh", null, new Dictionary<string, string> { ["AUX_MAX_MINUTOS_ATRASO"] = "30" });

        Assert.Equal(1, conDefecto);
        Assert.Equal(0, ajustado);
    }
}
