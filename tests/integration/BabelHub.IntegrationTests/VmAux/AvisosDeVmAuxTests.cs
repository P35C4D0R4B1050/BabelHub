using System.Text.RegularExpressions;
using BabelHub.IntegrationTests.Imagenes;

namespace BabelHub.IntegrationTests.VmAux;

/// <summary>
/// Los avisos de VM-AUX (PLA-20, PLA-38): el correo de una alerta se origina fuera de la VM de datos, no se repite cada cinco minutos pero sí
/// si el problema cambia o pasa el plazo, y si no se puede enviar se vuelve a intentar; el ancla semanal lleva el último sello sin datos
/// personales; el latido externo no lleva datos; y la programación (cron) usa un usuario sin privilegios y solo los scripts que existen. Los
/// secretos (la clave del correo, la dirección del latido) nunca van en los argumentos de un proceso ni en la salida.
/// </summary>
[Collection(ProcesosPesadosCollection.Nombre)]
public sealed class AvisosDeVmAuxTests : IDisposable
{
    private readonly EntornoDeVmAux _entorno = new();

    public void Dispose() => _entorno.Dispose();

    private static string Script(string nombre) => EntornoDeVmAux.EnLinux(Path.Combine(EntornoDeVmAux.RaizDelRepositorio(), "scripts", "vm-aux", nombre));

    /// <summary>Un comando de prueba que imprime lo dado y termina con el código dado.</summary>
    private string Comando(string texto, int codigo)
    {
        var ruta = Path.Combine(_entorno.Carpeta, $"cmd-{Guid.NewGuid():N}.sh");
        File.WriteAllText(ruta, $"#!/usr/bin/env bash\ncat <<'FIN'\n{texto}\nFIN\nexit {codigo}\n");
        return EntornoDeVmAux.EnLinux(ruta);
    }

    // ---------- alertar.sh ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void SiLaComprobacionPasa_NoHayCorreo_YSeOlvidaLaAlertaAnterior()
    {
        var fallo = _entorno.Ejecutar("alertar.sh", ["prueba", Comando("ALERTA algo", 1)]);
        var bien = _entorno.Ejecutar("alertar.sh", ["prueba", Comando("ok", 0)]);
        var otraVez = _entorno.Ejecutar("alertar.sh", ["prueba", Comando("ALERTA algo", 1)]);

        Assert.Equal(1, fallo.Codigo);
        Assert.Equal(0, bien.Codigo);
        Assert.Equal(1, otraVez.Codigo);
        // La alerta reaparece tras haberse resuelto: vuelve a avisar aunque sea el mismo texto.
        Assert.Equal(2, _entorno.CorreosEnviados);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void ElCorreoDeUnaAlerta_LlevaElTextoDeLaComprobacion_ElAsuntoYElDestinatario()
    {
        _entorno.Ejecutar("alertar.sh", ["respaldos", Comando("ALERTA respaldo del clúster: lo último es de hace 25 h", 1)]);

        var cuerpo = _entorno.Cuerpo(1);
        var argumentos = _entorno.Argumentos(1);
        Assert.Contains("Subject: [BabelHub] ALERTA: respaldos", cuerpo, StringComparison.Ordinal);
        Assert.Contains("To: responsable@ejemplo.interno", cuerpo, StringComparison.Ordinal);
        Assert.Contains("ALERTA respaldo del clúster: lo último es de hace 25 h", cuerpo, StringComparison.Ordinal);
        Assert.Contains("--mail-from vm-aux@ejemplo.interno", argumentos, StringComparison.Ordinal);
        Assert.Contains("--ssl-reqd", argumentos, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void LaMismaAlerta_NoSeReenviaDentroDelPlazo_PeroUnaDistintaSiAvisaAlInstante()
    {
        _entorno.Ejecutar("alertar.sh", ["respaldos", Comando("ALERTA uno", 1)]);
        var repetida = _entorno.Ejecutar("alertar.sh", ["respaldos", Comando("ALERTA uno", 1)]);
        var distinta = _entorno.Ejecutar("alertar.sh", ["respaldos", Comando("ALERTA uno\nALERTA dos", 1)]);

        Assert.Equal(1, repetida.Codigo);
        Assert.Contains("vigente: ya se avisó", repetida.Errores, StringComparison.Ordinal);
        Assert.Equal(1, distinta.Codigo);
        Assert.Equal(2, _entorno.CorreosEnviados);
        Assert.Contains("ALERTA dos", _entorno.Cuerpo(2), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void PasadoElPlazoDeReaviso_LaMismaAlertaVuelveAAvisar()
    {
        var comando = Comando("ALERTA uno", 1);
        var entorno = new Dictionary<string, string> { ["AUX_REAVISO_HORAS"] = "0" };

        _entorno.Ejecutar("alertar.sh", ["respaldos", comando], entorno);
        _entorno.Ejecutar("alertar.sh", ["respaldos", comando], entorno);

        Assert.Equal(2, _entorno.CorreosEnviados);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void SiElCorreoNoSePuedeEnviar_TerminaCon3_YElSiguienteIntentoVuelveAAvisar()
    {
        var comando = Comando("ALERTA uno", 1);

        var fallido = _entorno.Ejecutar("alertar.sh", ["respaldos", comando], new Dictionary<string, string> { ["DOBLE_CURL_FALLA"] = "1" });
        var reintento = _entorno.Ejecutar("alertar.sh", ["respaldos", comando]);

        Assert.Equal(3, fallido.Codigo);
        Assert.Contains("no se pudo enviar el correo", fallido.Errores, StringComparison.Ordinal);
        Assert.Equal(1, reintento.Codigo);
        Assert.Equal(2, _entorno.CorreosEnviados);
        Assert.Contains("ALERTA uno", _entorno.Cuerpo(2), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void SiFaltaLaConfiguracionDelCorreo_NoSeFingeQueSeAvisó()
    {
        var resultado = _entorno.Ejecutar("alertar.sh", ["respaldos", Comando("ALERTA uno", 1)], new Dictionary<string, string> { ["AUX_SMTP_URL"] = string.Empty });

        Assert.Equal(3, resultado.Codigo);
        Assert.Contains("falta AUX_SMTP_URL", resultado.Errores, StringComparison.Ordinal);
        Assert.Equal(0, _entorno.CorreosEnviados);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void LaClaveDelCorreo_VaEnUnArchivoDeConfiguracion_NuncaEnLosArgumentosNiEnLaSalida()
    {
        var resultado = _entorno.Ejecutar("alertar.sh", ["respaldos", Comando("ALERTA uno", 1)]);

        Assert.DoesNotContain(_entorno.ClaveDeCorreo, _entorno.Argumentos(1), StringComparison.Ordinal);
        Assert.DoesNotContain(_entorno.ClaveDeCorreo, resultado.Salida + resultado.Errores, StringComparison.Ordinal);
        Assert.DoesNotContain(_entorno.ClaveDeCorreo, _entorno.Cuerpo(1), StringComparison.Ordinal);
        Assert.Contains("--config", _entorno.Argumentos(1), StringComparison.Ordinal);
        Assert.Contains($"user = \"usuario-de-correo:{_entorno.ClaveDeCorreo}\"", _entorno.Configuracion(1), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData]
    [InlineData("solo-nombre")]
    [InlineData("Nombre-Invalido", "echo")]
    public void UnUsoInvalidoDelEnvoltorio_SeRechazaConCodigo2(params string[] argumentos)
    {
        var resultado = _entorno.Ejecutar("alertar.sh", argumentos);

        Assert.Equal(2, resultado.Codigo);
    }

    // ---------- Ancla semanal ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void ElAnclaSemanal_EnviaElUltimoSelloPorCorreo_SinDatosPersonales()
    {
        const string Sello = "{\"instante\":\"2026-11-14T10:05:00Z\",\"cantidad\":3,\"sumaDelConjunto\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\"}";
        File.WriteAllText(Path.Combine(_entorno.Estado, "ultimo-sello.json"), Sello + "\n");

        var (codigo, salida, errores) = _entorno.Ejecutar("anclar-sello.sh");

        Assert.True(codigo == 0, salida + errores);
        Assert.Equal(1, _entorno.CorreosEnviados);
        var cuerpo = _entorno.Cuerpo(1);
        Assert.Contains("Subject: [BabelHub] Ancla semanal del sello del registro de supresiones", cuerpo, StringComparison.Ordinal);
        Assert.Contains(Sello, cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain(_entorno.ClaveDeCorreo, cuerpo, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void SiNoHaySelloQueAnclar_SeAlerta_SinEnviarUnAnclaVacia()
    {
        var (codigo, salida, _) = _entorno.Ejecutar("anclar-sello.sh");

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA ancla del sello: no hay ningún sello guardado", salida, StringComparison.Ordinal);
        Assert.Equal(0, _entorno.CorreosEnviados);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void SiElCorreoDelAnclaFalla_TerminaCon3()
    {
        File.WriteAllText(Path.Combine(_entorno.Estado, "ultimo-sello.json"), "{\"instante\":\"2026-11-14T10:05:00Z\",\"cantidad\":0,\"sumaDelConjunto\":\"x\"}\n");

        var (codigo, _, errores) = _entorno.Ejecutar("anclar-sello.sh", null, new Dictionary<string, string> { ["DOBLE_CURL_FALLA"] = "1" });

        Assert.Equal(3, codigo);
        Assert.Contains("no se pudo enviar el correo del ancla", errores, StringComparison.Ordinal);
    }

    // ---------- Latido externo ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void ElLatidoExterno_EsUnaPeticionVaciaQueNoLlevaDatos_NiImprimeLaDireccion()
    {
        var (codigo, salida, errores) = _entorno.Ejecutar("latido-externo.sh");

        Assert.True(codigo == 0, salida + errores);
        Assert.Equal(1, _entorno.CorreosEnviados);
        var argumentos = _entorno.Argumentos(1);
        Assert.DoesNotContain("--upload-file", argumentos, StringComparison.Ordinal);
        Assert.DoesNotContain("--data", argumentos, StringComparison.Ordinal);
        Assert.DoesNotContain("--request", argumentos, StringComparison.Ordinal);
        Assert.DoesNotContain(_entorno.DireccionDelLatido, argumentos + salida + errores, StringComparison.Ordinal);
        Assert.Contains(_entorno.DireccionDelLatido, _entorno.Configuracion(1), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void SiElLatidoNoSePuedeEnviarOFaltaLaDireccion_Falla()
    {
        var sinDireccion = _entorno.Ejecutar("latido-externo.sh", null, new Dictionary<string, string> { ["AUX_LATIDO_URL"] = string.Empty });
        var conFallo = _entorno.Ejecutar("latido-externo.sh", null, new Dictionary<string, string> { ["DOBLE_CURL_FALLA"] = "1" });

        Assert.Equal(1, sinDireccion.Codigo);
        Assert.Contains("falta AUX_LATIDO_URL", sinDireccion.Errores, StringComparison.Ordinal);
        Assert.Equal(1, conFallo.Codigo);
        Assert.DoesNotContain(_entorno.DireccionDelLatido, conFallo.Salida + conFallo.Errores, StringComparison.Ordinal);
    }

    // ---------- Programación y reglas de los scripts ----------

    private static string Texto(string ruta) => File.ReadAllText(Path.Combine(EntornoDeVmAux.RaizDelRepositorio(), ruta));

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void LaProgramacionDeVmAux_UsaUnUsuarioSinPrivilegiosYSoloScriptsQueExisten()
    {
        var lineas = Texto("infra/vm-aux/babelhub-aux.cron").ReplaceLineEndings("\n").Split('\n')
            .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#') && Regex.IsMatch(l, @"^[\d*/,]+\s"))
            .ToList();

        Assert.NotEmpty(lineas);
        Assert.All(lineas, l =>
        {
            Assert.Matches(@"^\S+ \S+ \S+ \S+ \S+ babelhub-aux \$AUX/", l);
            Assert.DoesNotMatch(@" root ", l);
        });
        var scripts = lineas.SelectMany(l => Regex.Matches(l, @"\$AUX/([\w-]+\.sh)").Select(m => m.Groups[1].Value)).Distinct().ToList();
        Assert.All(scripts, s => Assert.True(File.Exists(Path.Combine(EntornoDeVmAux.RaizDelRepositorio(), "scripts", "vm-aux", s)), s));
        foreach (var esperado in new[] { "comprobar-respaldos.sh", "sellar-registro.sh", "copiar-registro.sh", "anclar-sello.sh", "vigilar-retencion.sh", "latido-externo.sh" })
        {
            Assert.Contains(esperado, scripts);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void ElComprobadorDeRespaldosCorreCadaCincoMinutos_ConUnaAlertaPorCorreo()
    {
        var cron = Texto("infra/vm-aux/babelhub-aux.cron");

        Assert.Matches(@"(?m)^\*/5 \* \* \* \* babelhub-aux \$AUX/alertar\.sh respaldos \$AUX/comprobar-respaldos\.sh$", cron);
        Assert.Matches(@"(?m)^\*/5 \* \* \* \* babelhub-aux \$AUX/latido-externo\.sh$", cron);
        Assert.Matches(@"(?m)^\d+ \* \* \* \* babelhub-aux \$AUX/alertar\.sh sello-registro \$AUX/sellar-registro\.sh$", cron);
    }

    [Theory]
    [InlineData("comun.sh")]
    [InlineData("comprobar-respaldos.sh")]
    [InlineData("sellar-registro.sh")]
    [InlineData("copiar-registro.sh")]
    [InlineData("anclar-sello.sh")]
    [InlineData("alertar.sh")]
    [InlineData("latido-externo.sh")]
    [InlineData("vigilar-retencion.sh")]
    public void LosScriptsDeVmAux_NoTienenClavesPrivadasNiCredencialesDeBorrado_YNoActivanLaTraza(string nombre)
    {
        var texto = Texto($"scripts/vm-aux/{nombre}");
        var sinComentarios = Regex.Replace(texto, @"(?m)^\s*#.*$", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2));

        Assert.DoesNotContain('\r', texto);
        Assert.DoesNotMatch(@"AGE-SECRET-KEY-[0-9A-Z]{10,}", texto);
        Assert.DoesNotMatch(@"(?m)^\s*set\s+-[a-z]*x", texto);
        Assert.DoesNotMatch(@"""\$RCLONE""\s+(delete|deletefile|purge|rmdir|move|moveto|sync|rcat|copy)\b", sinComentarios);
        Assert.DoesNotMatch(@"\b(ssh-keygen|ssh-add|id_rsa|id_ed25519)\b", sinComentarios);
    }
}
