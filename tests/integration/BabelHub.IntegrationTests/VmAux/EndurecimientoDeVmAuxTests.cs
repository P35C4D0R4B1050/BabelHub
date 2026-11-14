using System.Text;
using System.Text.RegularExpressions;
using BabelHub.IntegrationTests.Imagenes;

namespace BabelHub.IntegrationTests.VmAux;

/// <summary>
/// El endurecimiento de VM-AUX (T26; PLA-20, AM29, D19; AC-PLA-20): la VM es el salto SSH hacia las demás y vigila los respaldos, así que no
/// debe tener llaves privadas, agente reenviado, credenciales de más ni scripts de root modificables; sshd acota el salto; un ingreso SSH o un
/// proceso desconocido avisan por correo. Los valores secretos de las pruebas se generan en cada ejecución y nunca deben aparecer en la salida.
/// </summary>
[Collection(ProcesosPesadosCollection.Nombre)]
public sealed class EndurecimientoDeVmAuxTests : IDisposable
{
    private const string Salto = "salto";

    private const string SshdConforme = """
        port 22
        passwordauthentication no
        kbdinteractiveauthentication no
        permitrootlogin no
        allowagentforwarding no
        x11forwarding no
        permitemptypasswords no
        usepam yes
        allowstreamlocalforwarding no
        gatewayports no
        permittunnel false
        pubkeyauthentication yes
        permituserenvironment no
        maxauthtries 3
        permittty no
        forcecommand /usr/sbin/nologin
        allowtcpforwarding local
        permitopen 10.0.1.10:22 10.0.2.10:22
        allowusers salto admin
        """;

    private const string DobleDeStat = """
        #!/usr/bin/env bash
        # stat -c <formato> <ruta>: los permisos del archivo de entorno y de los scripts de root los fija la prueba.
        formato="$2"; ruta="$3"; modo=644; dueno=root
        [[ "$ruta" == "$DOBLE_ENV" ]] && dueno="${DOBLE_DUENO_ENV:-root}"
        [[ "$ruta" == "$DOBLE_SCRIPTS" ]] && { modo="${DOBLE_MODO_DIR:-755}"; dueno="${DOBLE_DUENO_DIR:-root}"; }
        [[ "$ruta" == "$DOBLE_ENV" ]] && modo="${DOBLE_MODO_ENV:-600}"
        [[ "$ruta" == "$DOBLE_SCRIPTS"/* ]] && { modo="${DOBLE_MODO_SCRIPTS:-755}"; dueno="${DOBLE_DUENO_SCRIPTS:-root}"; }
        case "$formato" in '%a') echo "$modo" ;; '%U %a') echo "$dueno $modo" ;; *) exit 1 ;; esac
        """;

    private const string DobleDeSshd = """
        #!/usr/bin/env bash
        [[ -n "${DOBLE_SSHD_FALLA:-}" ]] && exit 1
        # sshd -T -C user=<usuario>,…: la configuración de otro usuario puede ser distinta (un Match User).
        todos="$*"; usuario="${todos#*user=}"; usuario="${usuario%%,*}"
        if [[ -f "$DOBLE_SSHD_CONFIG.$usuario" ]]; then cat "$DOBLE_SSHD_CONFIG.$usuario"; else cat "$DOBLE_SSHD_CONFIG"; fi
        """;

    private const string DobleDePs = """
        #!/usr/bin/env bash
        cat "$DOBLE_PROCESOS"
        """;

    private readonly EntornoDeVmAux _entorno = new();
    private readonly string _raiz;
    private readonly string _casa;
    private readonly string _scripts;
    private readonly string _auxEnv;
    private readonly string _sshdConfig;
    private readonly string _procesos;
    private readonly string _clienteSsh;
    private readonly string _autorizadas;
    private readonly string _valorSecreto = "v" + Guid.NewGuid().ToString("N");

    public EndurecimientoDeVmAuxTests()
    {
        _raiz = Path.Combine(_entorno.Carpeta, "vm");
        _casa = Path.Combine(_raiz, "home");
        _scripts = Path.Combine(_raiz, "scripts-vm-aux");
        _auxEnv = Path.Combine(_raiz, "aux.env");
        _sshdConfig = Path.Combine(_raiz, "sshd.txt");
        _procesos = Path.Combine(_raiz, "procesos.txt");
        _clienteSsh = Path.Combine(_raiz, "ssh_config");
        _autorizadas = Path.Combine(_casa, Salto, ".ssh", "authorized_keys");
        Directory.CreateDirectory(Path.GetDirectoryName(_autorizadas)!);
        Directory.CreateDirectory(_scripts);
        foreach (var nombre in new[] { "verificar-endurecimiento.sh", "alertar.sh", "comun.sh" })
        {
            File.WriteAllText(Path.Combine(_scripts, nombre), "#\n");
        }

        File.WriteAllText(Path.Combine(_raiz, "stat-doble.sh"), DobleDeStat.ReplaceLineEndings("\n"));
        File.WriteAllText(Path.Combine(_raiz, "sshd-doble.sh"), DobleDeSshd.ReplaceLineEndings("\n"));
        File.WriteAllText(Path.Combine(_raiz, "ps-doble.sh"), DobleDePs.ReplaceLineEndings("\n"));
        File.WriteAllText(_sshdConfig, SshdConforme.ReplaceLineEndings("\n") + "\n");
        File.WriteAllText(Path.Combine(_raiz, "pam-sshd"), "@include common-auth\nsession optional pam_exec.so quiet /opt/babelhub/scripts/vm-aux/avisar-ingreso-ssh.sh\n");
        File.WriteAllText(_clienteSsh, "Host *\n    ForwardAgent no\n");
        File.WriteAllText(_autorizadas, "restrict,port-forwarding,permitopen=\"10.0.1.10:22\",permitopen=\"10.0.2.10:22\" ssh-ed25519 AAAAC3Nza-publica persona\n");
        File.WriteAllText(_procesos, "systemd\nsshd\ncron\nbash\nkworker/0:1\n");
        File.WriteAllText(_auxEnv, EntornoPropio());
    }

    public void Dispose() => _entorno.Dispose();

    private string EntornoPropio() =>
        "AUX_SMTP_URL=smtps://smtp.ejemplo.interno:465\n"
        + "AUX_SMTP_USUARIO=usuario-de-correo\n"
        + $"AUX_SMTP_CLAVE={_valorSecreto}\n"
        + "AUX_CORREO_DE=vm-aux@ejemplo.interno\n"
        + "AUX_CORREO_PARA=responsable@ejemplo.interno\n"
        + $"RCLONE_CONFIG_VIGILANTE_SECRET_ACCESS_KEY={_valorSecreto}\n"
        + $"RCLONE_CONFIG_SELLO_SECRET_ACCESS_KEY={_valorSecreto}\n"
        + $"RCLONE_CONFIG_COPIA_SECRET_ACCESS_KEY={_valorSecreto}\n";

    private Dictionary<string, string> Entorno() => new()
    {
        ["SSHD_T"] = EntornoDeVmAux.EnLinux(Path.Combine(_raiz, "sshd-doble.sh")),
        ["PS"] = EntornoDeVmAux.EnLinux(Path.Combine(_raiz, "ps-doble.sh")),
        ["STAT"] = EntornoDeVmAux.EnLinux(Path.Combine(_raiz, "stat-doble.sh")),
        ["DOBLE_SSHD_CONFIG"] = EntornoDeVmAux.EnLinux(_sshdConfig),
        ["DOBLE_PROCESOS"] = EntornoDeVmAux.EnLinux(_procesos),
        ["DOBLE_ENV"] = EntornoDeVmAux.EnLinux(_auxEnv),
        ["DOBLE_SCRIPTS"] = EntornoDeVmAux.EnLinux(_scripts),
        ["AUX_ENV"] = EntornoDeVmAux.EnLinux(_auxEnv),
        ["AUX_SCRIPTS"] = EntornoDeVmAux.EnLinux(_scripts),
        ["AUX_USUARIO_SALTO"] = Salto,
        ["AUX_RUTAS_CLAVES"] = EntornoDeVmAux.EnLinux(_casa),
        ["AUX_AUTORIZADAS"] = EntornoDeVmAux.EnLinux(_autorizadas),
        ["AUX_CONFIGS_SSH"] = EntornoDeVmAux.EnLinux(_clienteSsh),
        ["AUX_PAM_SSHD"] = EntornoDeVmAux.EnLinux(Path.Combine(_raiz, "pam-sshd")),
    };

    private (int Codigo, string Salida, string Errores) Verificar(Action<Dictionary<string, string>>? ajustar = null)
    {
        var entorno = Entorno();
        ajustar?.Invoke(entorno);
        return _entorno.Ejecutar("verificar-endurecimiento.sh", null, entorno);
    }

    private void CambiarSshd(string clave, string? valor)
    {
        var lineas = File.ReadAllLines(_sshdConfig).Where(l => !l.StartsWith(clave + " ", StringComparison.Ordinal)).ToList();
        if (valor is not null)
        {
            lineas.Add($"{clave} {valor}");
        }

        File.WriteAllText(_sshdConfig, string.Join('\n', lineas) + "\n");
    }

    // ---------- verificar-endurecimiento.sh ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void ConUnaVmAuxEndurecida_ElVerificadorPasa()
    {
        var (codigo, salida, errores) = Verificar();

        Assert.True(codigo == 0, salida + errores);
        Assert.Contains("ok sshd allowagentforwarding no", salida, StringComparison.Ordinal);
        Assert.Contains("ok sshd permitopen solo a direcciones privadas", salida, StringComparison.Ordinal);
        Assert.Contains("ok ninguna llave privada en VM-AUX", salida, StringComparison.Ordinal);
        Assert.Contains("VM-AUX: el endurecimiento cumple.", salida, StringComparison.Ordinal);
        Assert.DoesNotContain("ALERTA", salida, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("passwordauthentication", "yes")]
    [InlineData("permitrootlogin", "yes")]
    [InlineData("allowagentforwarding", "yes")]
    [InlineData("x11forwarding", "yes")]
    [InlineData("permitemptypasswords", "yes")]
    [InlineData("pubkeyauthentication", "no")]
    [InlineData("kbdinteractiveauthentication", "yes")]
    [InlineData("permituserenvironment", "yes")]
    [InlineData("maxauthtries", "6")]
    [InlineData("permittty", "yes")]
    [InlineData("usepam", "no")]
    [InlineData("allowstreamlocalforwarding", "yes")]
    [InlineData("gatewayports", "clientspecified")]
    [InlineData("permittunnel", "yes")]
    [InlineData("allowtcpforwarding", "no")]
    [InlineData("forcecommand", null)]
    [InlineData("allowusers", null)]
    [Trait("Criterio", "AC-PLA-20")]
    public void UnAjusteInseguroDeSshd_HaceFallarElVerificadorNombrandoLaClave(string clave, string? valor)
    {
        CambiarSshd(clave, valor);

        var (codigo, salida, _) = Verificar();

        Assert.Equal(1, codigo);
        Assert.Matches($"(?m)^ALERTA sshd {clave}", salida);
    }

    [Theory]
    [InlineData("any")]
    [InlineData("none")]
    [InlineData("10.0.1.10:22 203.0.113.9:22")]
    [InlineData("10.0.1.10:5432")]
    [InlineData("172.32.0.5:22")]
    [Trait("Criterio", "AC-PLA-20")]
    public void SiElSaltoNoEstaAcotadoADireccionesPrivadasPuerto22_ElVerificadorFalla(string destinos)
    {
        CambiarSshd("permitopen", destinos);

        var (codigo, salida, _) = Verificar();

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA sshd permitopen", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void SiNoSePuedeLeerLaConfiguracionDeSshd_NoSeDaPorBueno()
    {
        var (codigo, salida, _) = Verificar(e => e["DOBLE_SSHD_FALLA"] = "1");

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA no se pudo leer la configuración efectiva de sshd", salida, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("openssh")]
    [InlineData("pem")]
    [InlineData("putty")]
    [InlineData("age")]
    [Trait("Criterio", "AC-PLA-20")]
    public void UnaLlavePrivadaEnVmAux_SeDetectaPorSuRuta_SinImprimirSuContenido(string tipo)
    {
        // Las cabeceras se arman en tiempo de ejecución: el código fuente de la prueba no lleva ninguna llave ni nada con su forma.
        var cuerpo = Convert.ToBase64String(Encoding.UTF8.GetBytes(_valorSecreto));
        var contenido = tipo switch
        {
            "openssh" => "-----BEGIN " + "OPENSSH PRIVATE KEY-----\n" + cuerpo + "\n-----END " + "OPENSSH PRIVATE KEY-----\n",
            "pem" => "-----BEGIN " + "RSA PRIVATE KEY-----\n" + cuerpo + "\n-----END " + "RSA PRIVATE KEY-----\n",
            "putty" => "PuTTY-User-" + "Key-File-3: ssh-ed25519\n" + cuerpo + "\n",
            _ => "# created: 2026-11-14\nAGE-SECRET-" + "KEY-" + cuerpo.ToUpperInvariant() + "\n",
        };
        var ruta = Path.Combine(_casa, "admin", ".ssh", "id_" + tipo);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, contenido);

        var (codigo, salida, errores) = Verificar();

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA hay una llave privada en VM-AUX: ", salida, StringComparison.Ordinal);
        Assert.Contains("id_" + tipo, salida, StringComparison.Ordinal);
        Assert.DoesNotContain(cuerpo, salida + errores, StringComparison.Ordinal);
        Assert.DoesNotContain(_valorSecreto, salida + errores, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void UnaLlavePublicaYUnArchivoConUnaFraseSobreLlaves_NoSonUnaLlavePrivada()
    {
        File.WriteAllText(Path.Combine(_casa, "salto", ".ssh", "id_ed25519.pub"), "ssh-ed25519 AAAAC3Nza-publica persona\n");
        File.WriteAllText(Path.Combine(_casa, "notas.txt"), "Las llaves privadas viven en el equipo de la persona responsable.\n");

        var (codigo, salida, errores) = Verificar();

        Assert.True(codigo == 0, salida + errores);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void UnAgenteSshCorriendo_HaceFallar()
    {
        File.AppendAllText(_procesos, "ssh-agent\n");

        var (codigo, salida, _) = Verificar();

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA hay un ssh-agent corriendo en VM-AUX", salida, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ForwardAgent yes")]
    [InlineData("  forwardagent   YES")]
    [Trait("Criterio", "AC-PLA-20")]
    public void UnForwardAgentActivoEnLaConfiguracionDeCliente_HaceFallar(string linea)
    {
        File.WriteAllText(_clienteSsh, "Host *\n" + linea + "\n");

        var (codigo, salida, _) = Verificar();

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA ForwardAgent activo en ", salida, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ssh-ed25519 AAAAC3Nza-publica persona", "no lleva «restrict»")]
    [InlineData("restrict,permitopen=\"10.0.1.10:22\" ssh-ed25519 AAAAC3Nza-publica persona", "no lleva «port-forwarding»")]
    [InlineData("restrict,port-forwarding ssh-ed25519 AAAAC3Nza-publica persona", "no acota los destinos con permitopen")]
    [InlineData("restrict,port-forwarding,permitopen=\"198.51.100.4:22\" ssh-ed25519 AAAAC3Nza-publica persona", "permitopen admite «198.51.100.4:22»")]
    [InlineData("restrict,port-forwarding,agent-forwarding,permitopen=\"10.0.1.10:22\" ssh-ed25519 AAAAC3Nza-publica persona", "agent-forwarding")]
    [Trait("Criterio", "AC-PLA-20")]
    public void UnaLlaveDelSaltoSinAcotar_HaceFallarElVerificador(string linea, string motivo)
    {
        File.WriteAllText(_autorizadas, linea + "\n");

        var (codigo, salida, _) = Verificar();

        Assert.Equal(1, codigo);
        Assert.Contains(motivo, salida, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("port-forwarding,permitopen=\"10.0.1.10:22\" ssh-ed25519 AAAAC3Nza-publica restrict,persona", "no lleva «restrict»")]
    [InlineData("restrict,port-forwarding,permitopen=\"10.0.1.10:22\",permitopen=\"203.0.113.7:22\" ssh-ed25519 AAAAC3Nza-publica persona", "permitopen admite «203.0.113.7:22»")]
    [Trait("Criterio", "AC-PLA-20")]
    public void ElVerificadorSoloMiraElCampoDeOpcionesDeLaLlave_NoSuComentario_NiSoloElPrimerPermitopen(string linea, string motivo)
    {
        File.WriteAllText(_autorizadas, linea + "\n");

        var (codigo, salida, _) = Verificar();

        Assert.Equal(1, codigo);
        Assert.Contains(motivo, salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void UnaOpcionNoAgentForwarding_NoEsUnaLlaveConReenvioDeAgente()
    {
        File.WriteAllText(_autorizadas, "restrict,port-forwarding,no-agent-forwarding,permitopen=\"10.0.1.10:22\" ssh-ed25519 AAAAC3Nza-publica persona\n");

        var (codigo, salida, errores) = Verificar();

        Assert.True(codigo == 0, salida + errores);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void UnMatchUserDelAdministradorQueReabreElReenvioDeAgente_SeDetecta()
    {
        File.WriteAllText(_sshdConfig + ".admin", File.ReadAllText(_sshdConfig).Replace("allowagentforwarding no", "allowagentforwarding yes", StringComparison.Ordinal));

        var (codigo, salida, _) = Verificar();

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA sshd allowagentforwarding para «admin» es «yes» y debe ser «no»", salida, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("session optional pam_exec.so quiet /opt/babelhub/scripts/vm-aux/otro.sh\n")]
    [InlineData("# session optional pam_exec.so quiet /opt/babelhub/scripts/vm-aux/avisar-ingreso-ssh.sh\n")]
    [Trait("Criterio", "AC-PLA-20")]
    public void SiLaAlertaDeIngresoSshNoEstaInstaladaEnPam_ElVerificadorFalla(string contenido)
    {
        File.WriteAllText(Path.Combine(_raiz, "pam-sshd"), contenido);

        var (codigo, salida, _) = Verificar();

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA la alerta de ingreso SSH no está instalada en ", salida, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DOBLE_DUENO_ENV", "babelhub-aux", "es de babelhub-aux con permisos 600")]
    [InlineData("DOBLE_MODO_ENV", "660", "es de root con permisos 660")]
    [InlineData("DOBLE_MODO_ENV", "666", "es de root con permisos 666")]
    [Trait("Criterio", "AC-PLA-20")]
    public void ElEntornoQueLeeRootNoPuedeSerCambiadoPorElUsuarioSinPrivilegios(string variable, string valor, string esperado)
    {
        var (codigo, salida, _) = Verificar(e => e[variable] = valor);

        Assert.Equal(1, codigo);
        Assert.Contains(esperado, salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void ElEntornoDeRootConPermisos0640_Pasa()
    {
        var (codigo, salida, errores) = Verificar(e => e["DOBLE_MODO_ENV"] = "640");

        Assert.True(codigo == 0, salida + errores);
    }

    [Theory]
    [InlineData("DOBLE_DUENO_DIR", "babelhub-aux")]
    [InlineData("DOBLE_MODO_DIR", "775")]
    [Trait("Criterio", "AC-PLA-20")]
    public void LaCarpetaDeLosScriptsDeRootTambienDebeSerDeRootYNoEscribible(string variable, string valor)
    {
        var (codigo, salida, _) = Verificar(e => e[variable] = valor);

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA la carpeta ", salida, StringComparison.Ordinal);
        Assert.Contains("debe ser de root y sin escritura para otros", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void ElArchivoDeEntorno_SeLeeSinEjecutarlo_YSoloAceptaSusNombresPropios()
    {
        // Quien comprometa a babelhub-aux no debe ganar root a través del archivo que la tarea de root carga.
        var marcador = Path.Combine(_raiz, "ejecutado.txt");
        File.WriteAllText(
            _auxEnv,
            $"AUX_SMTP_USUARIO=$(touch {EntornoDeVmAux.EnLinux(marcador)})\n"
            + $"AUX_SMTP_CLAVE=`touch {EntornoDeVmAux.EnLinux(marcador)}`\n"
            + "AUX_CORREO_DE=vm-aux@ejemplo.interno\nAUX_CORREO_PARA=responsable@ejemplo.interno\nAUX_SMTP_URL=smtps://smtp.ejemplo.interno:465\n"
            + $"PS={EntornoDeVmAux.EnLinux(marcador)}\nCURL={EntornoDeVmAux.EnLinux(marcador)}\nAUX_ESTADO={EntornoDeVmAux.EnLinux(_raiz)}/otro-estado\nSSHD_T=touch\n");
        var comando = Path.Combine(_raiz, "imprime.sh");
        File.WriteAllText(comando, "#!/usr/bin/env bash\necho \"PS=${PS:-vacio} ESTADO=${AUX_ESTADO}\"\nexit 1\n");

        var resultado = _entorno.Ejecutar(
            "alertar.sh",
            ["prueba", EntornoDeVmAux.EnLinux(comando)],
            new Dictionary<string, string> { ["AUX_ENV"] = EntornoDeVmAux.EnLinux(_auxEnv), ["AUX_SMTP_USUARIO"] = string.Empty, ["AUX_SMTP_CLAVE"] = string.Empty, ["AUX_SMTP_URL"] = string.Empty, ["AUX_CORREO_DE"] = string.Empty, ["AUX_CORREO_PARA"] = string.Empty });

        Assert.Equal(1, resultado.Codigo);
        Assert.False(File.Exists(marcador), "el contenido del archivo de entorno se ejecutó");
        Assert.Equal(1, _entorno.CorreosEnviados);
        // Las variables de prueba (PS, AUX_ESTADO…) no se aceptan desde el archivo: solo las pone el entorno del proceso.
        Assert.Contains("PS=vacio ESTADO=" + EntornoDeVmAux.EnLinux(_entorno.Estado), _entorno.Cuerpo(1), StringComparison.Ordinal);
        Assert.Contains("user = \"$(touch ", _entorno.Configuracion(1), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void UnaClaveDeCorreoConComillas_NoRompeNiInyectaElArchivoDeConfiguracionDeCurl()
    {
        var clave = "ab\"cd\\ef\nurl = \"http://evil.invalid\"";

        var resultado = _entorno.Ejecutar("alertar.sh", ["prueba", Comando("ALERTA x", 1)], new Dictionary<string, string> { ["AUX_SMTP_CLAVE"] = clave.Replace("\n", string.Empty, StringComparison.Ordinal) });

        Assert.Equal(1, resultado.Codigo);
        var configuracion = _entorno.Configuracion(1);
        Assert.Single(configuracion.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("ab\\\"cd\\\\ef", configuracion, StringComparison.Ordinal);
    }

    private string Comando(string texto, int codigo)
    {
        var ruta = Path.Combine(_raiz, $"cmd-{Guid.NewGuid():N}.sh");
        File.WriteAllText(ruta, $"#!/usr/bin/env bash\ncat <<'FIN'\n{texto}\nFIN\nexit {codigo}\n");
        return EntornoDeVmAux.EnLinux(ruta);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void UnaLlaveDeOtroUsuarioConReenvioDeAgente_HaceFallar()
    {
        var otro = Path.Combine(_casa, "admin", ".ssh", "authorized_keys");
        Directory.CreateDirectory(Path.GetDirectoryName(otro)!);
        File.WriteAllText(otro, "agent-forwarding ssh-ed25519 AAAAC3Nza-publica persona\n");

        var (codigo, salida, _) = Verificar(e => e["AUX_AUTORIZADAS"] = EntornoDeVmAux.EnLinux(_autorizadas) + " " + EntornoDeVmAux.EnLinux(otro));

        Assert.Equal(1, codigo);
        Assert.Contains("una llave con agent-forwarding", salida, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("POSTGRES_PASSWORD")]
    [InlineData("BABELHUB__Seguridad__ClaveDeHuella")]
    [InlineData("RCLONE_CONFIG_ESCRITOR_SECRET_ACCESS_KEY")]
    [InlineData("RCLONE_CONFIG_LECTOR_SECRET_ACCESS_KEY")]
    [InlineData("AUTHENTICATION_API_KEY")]
    [InlineData("BABELHUB_AGE_DESTINATARIO")]
    [Trait("Criterio", "AC-PLA-20")]
    public void UnaCredencialDeMasEnElEntornoDeVmAux_SeRechazaPorSuNombre_SinImprimirSuValor(string nombre)
    {
        File.AppendAllText(_auxEnv, $"{nombre}={_valorSecreto}\n");

        var (codigo, salida, errores) = Verificar();

        Assert.Equal(1, codigo);
        Assert.Contains($"una variable ajena a VM-AUX: {nombre} (credencial de más)", salida, StringComparison.Ordinal);
        Assert.DoesNotContain(_valorSecreto, salida + errores, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void ElEntornoDeVmAux_ConPermisosAbiertos_OSinSuCredencialDeCorreoPropia_OSinTls_Falla()
    {
        File.WriteAllText(_auxEnv, "AUX_SMTP_URL=smtp://smtp.ejemplo.interno:25\n" + "AUX_CORREO_DE=vm-aux@ejemplo.interno\n");

        var (codigo, salida, errores) = Verificar(e => e["DOBLE_MODO_ENV"] = "644");

        Assert.Equal(1, codigo);
        Assert.Contains("es de root con permisos 644: debe ser de root y 0600 o 0640", salida, StringComparison.Ordinal);
        Assert.Contains("falta AUX_SMTP_USUARIO: VM-AUX necesita su credencial de correo propia", salida, StringComparison.Ordinal);
        Assert.Contains("falta AUX_SMTP_CLAVE", salida, StringComparison.Ordinal);
        Assert.Contains("AUX_SMTP_URL usa smtp:// sin TLS implícito", salida, StringComparison.Ordinal);
        Assert.DoesNotContain(_valorSecreto, salida + errores, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void SiFaltaElEntornoDeVmAux_NoSeDaPorBueno()
    {
        File.Delete(_auxEnv);

        var (codigo, salida, _) = Verificar();

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA falta ", salida, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("root", "775")]
    [InlineData("root", "757")]
    [InlineData("root", "666")]
    [InlineData("babelhub-aux", "755")]
    [Trait("Criterio", "AC-PLA-20")]
    public void UnScriptQueCorreComoRootYSePuedeModificar_HaceFallar(string dueno, string modo)
    {
        var (codigo, salida, _) = Verificar(e =>
        {
            e["DOBLE_DUENO_SCRIPTS"] = dueno;
            e["DOBLE_MODO_SCRIPTS"] = modo;
        });

        Assert.Equal(1, codigo);
        Assert.Contains("debe ser de root y sin escritura para otros", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void SiFaltaUnScriptDeRoot_NoSeDaPorBueno()
    {
        File.Delete(Path.Combine(_scripts, "alertar.sh"));

        var (codigo, salida, _) = Verificar();

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA falta ", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void ElVerificadorSoloLee_NoCambiaNadaDeLaVm()
    {
        var antes = Huella();

        Verificar();

        Assert.Equal(antes, Huella());
    }

    private string Huella() =>
        string.Join('|', Directory.EnumerateFiles(_raiz, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(f => f + ":" + File.ReadAllText(f).GetHashCode(StringComparison.Ordinal)));

    // ---------- avisar-ingreso-ssh.sh ----------

    private (int Codigo, string Salida, string Errores) Ingreso(string tipo, string usuario, string origen, Action<Dictionary<string, string>>? ajustar = null)
    {
        var entorno = new Dictionary<string, string>
        {
            ["PAM_TYPE"] = tipo,
            ["PAM_SERVICE"] = "sshd",
            ["PAM_USER"] = usuario,
            ["PAM_RHOST"] = origen,
            ["AUX_UID"] = "1001",
        };
        ajustar?.Invoke(entorno);
        return _entorno.Ejecutar("avisar-ingreso-ssh.sh", null, entorno);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void UnIngresoSsh_AvisaPorCorreoConUsuarioOrigenYHora_SinSecretos()
    {
        var (codigo, salida, errores) = Ingreso("open_session", "salto", "198.51.100.23");

        Assert.True(codigo == 0, salida + errores);
        Assert.Equal(1, _entorno.CorreosEnviados);
        var cuerpo = _entorno.Cuerpo(1);
        Assert.Contains("Subject: [BabelHub] ALERTA: ingreso SSH en VM-AUX", cuerpo, StringComparison.Ordinal);
        Assert.Contains("To: responsable@ejemplo.interno", cuerpo, StringComparison.Ordinal);
        Assert.Contains("Usuario: salto", cuerpo, StringComparison.Ordinal);
        Assert.Contains("Origen:  198.51.100.23", cuerpo, StringComparison.Ordinal);
        Assert.Matches(@"Hora:\s+\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} UTC", cuerpo);
        Assert.DoesNotContain(_entorno.ClaveDeCorreo, cuerpo + _entorno.Argumentos(1) + salida + errores, StringComparison.Ordinal);
        Assert.Contains("--ssl-reqd", _entorno.Argumentos(1), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("close_session", "sshd")]
    [InlineData("open_session", "su")]
    [InlineData("auth", "sshd")]
    [InlineData("", "sshd")]
    [Trait("Criterio", "AC-PLA-20")]
    public void SoloLaAperturaDeUnaSesionDeSshdAvisa(string tipo, string servicio)
    {
        var (codigo, _, _) = Ingreso(tipo, "salto", "198.51.100.23", e => e["PAM_SERVICE"] = servicio);

        Assert.Equal(0, codigo);
        Assert.Equal(0, _entorno.CorreosEnviados);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void UnOrigenConSaltosDeLinea_NoPuedeInyectarCabecerasEnElCorreo()
    {
        var (codigo, _, _) = Ingreso("open_session", "salto", "203.0.113.5\r\nBcc: atacante@ejemplo.invalido\nSubject: otro");

        Assert.Equal(0, codigo);
        var cuerpo = _entorno.Cuerpo(1);
        var cabeceras = cuerpo[..cuerpo.IndexOf("\r\n\r\n", StringComparison.Ordinal)];
        Assert.DoesNotContain("Bcc", cabeceras, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(cabeceras, "Subject:"));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void SiElCorreoDelIngresoFalla_ElIngresoNoSeImpide_YQuedaAnotado()
    {
        var (codigo, _, _) = Ingreso("open_session", "admin", "198.51.100.23", e => e["DOBLE_CURL_FALLA"] = "1");

        Assert.Equal(0, codigo);
        var registro = File.ReadAllText(Path.Combine(_entorno.Estado, "ingresos.log"));
        Assert.Contains("no se pudo enviar el aviso del ingreso de admin desde 198.51.100.23", registro, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void SiPamLoLanzaComoRoot_SeRelanzaConElUsuarioSinPrivilegios()
    {
        var doble = Path.Combine(_raiz, "runuser-doble.sh");
        File.WriteAllText(doble, "#!/usr/bin/env bash\necho \"$*\" >>\"$DOBLE_RUNUSER\"\n");
        var registro = Path.Combine(_raiz, "runuser.log");

        var (codigo, _, _) = Ingreso("open_session", "salto", "198.51.100.23", e =>
        {
            e["AUX_UID"] = "0";
            e["RUNUSER"] = EntornoDeVmAux.EnLinux(doble);
            e["DOBLE_RUNUSER"] = EntornoDeVmAux.EnLinux(registro);
        });

        Assert.Equal(0, codigo);
        Assert.Contains("-u babelhub-aux -- ", File.ReadAllText(registro), StringComparison.Ordinal);
        Assert.Equal(0, _entorno.CorreosEnviados);
    }

    // ---------- vigilar-procesos.sh ----------

    private (int Codigo, string Salida, string Errores) Procesos(string? lista = null)
    {
        var permitidos = Path.Combine(EntornoDeVmAux.RaizDelRepositorio(), "infra", "vm-aux", "procesos-permitidos.txt");
        if (lista is not null)
        {
            permitidos = Path.Combine(_raiz, "permitidos.txt");
            File.WriteAllText(permitidos, lista);
        }

        return _entorno.Ejecutar("vigilar-procesos.sh", null, new Dictionary<string, string>
        {
            ["PS"] = EntornoDeVmAux.EnLinux(Path.Combine(_raiz, "ps-doble.sh")),
            ["DOBLE_PROCESOS"] = EntornoDeVmAux.EnLinux(_procesos),
            ["AUX_PROCESOS_PERMITIDOS"] = EntornoDeVmAux.EnLinux(permitidos),
        });
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void ConSoloProcesosPermitidos_IncluidosLosHilosDelKernelPorPrefijo_NoHayAlerta()
    {
        var (codigo, salida, errores) = Procesos();

        Assert.True(codigo == 0, salida + errores);
        Assert.Contains("ok todos los procesos están en la lista permitida", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void UnProcesoNuevo_SeAvisaPorSuNombre_ConLaAlertaPorCorreoDeAlertar()
    {
        File.AppendAllText(_procesos, "xmrig\nreverse-shell\n");

        var (codigo, salida, _) = Procesos();
        var alerta = _entorno.Ejecutar("alertar.sh", ["procesos", EntornoDeVmAux.EnLinux(Path.Combine(EntornoDeVmAux.RaizDelRepositorio(), "scripts", "vm-aux", "vigilar-procesos.sh"))], new Dictionary<string, string>
        {
            ["PS"] = EntornoDeVmAux.EnLinux(Path.Combine(_raiz, "ps-doble.sh")),
            ["DOBLE_PROCESOS"] = EntornoDeVmAux.EnLinux(_procesos),
            ["AUX_PROCESOS_PERMITIDOS"] = EntornoDeVmAux.EnLinux(Path.Combine(EntornoDeVmAux.RaizDelRepositorio(), "infra", "vm-aux", "procesos-permitidos.txt")),
        });

        Assert.Equal(1, codigo);
        Assert.Contains("ALERTA proceso nuevo: xmrig", salida, StringComparison.Ordinal);
        Assert.Contains("ALERTA proceso nuevo: reverse-shell", salida, StringComparison.Ordinal);
        Assert.Equal(1, alerta.Codigo);
        Assert.Equal(1, _entorno.CorreosEnviados);
        Assert.Contains("ALERTA proceso nuevo: xmrig", _entorno.Cuerpo(1), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void SiFaltaLaListaDeProcesosPermitidosOEstaVacia_NoSeDaPorBueno()
    {
        var vacia = Procesos("# solo comentarios\n");
        File.Delete(Path.Combine(_raiz, "permitidos.txt"));
        var sinArchivo = _entorno.Ejecutar("vigilar-procesos.sh", null, new Dictionary<string, string>
        {
            ["PS"] = EntornoDeVmAux.EnLinux(Path.Combine(_raiz, "ps-doble.sh")),
            ["DOBLE_PROCESOS"] = EntornoDeVmAux.EnLinux(_procesos),
            ["AUX_PROCESOS_PERMITIDOS"] = EntornoDeVmAux.EnLinux(Path.Combine(_raiz, "permitidos.txt")),
        });

        Assert.Equal(1, vacia.Codigo);
        Assert.Contains("está vacía", vacia.Salida, StringComparison.Ordinal);
        Assert.Equal(1, sinArchivo.Codigo);
        Assert.Contains("falta la lista", sinArchivo.Salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void ElNombreDeUnProcesoSeLimpia_ParaQueNoLlevePegadoNadaQueNoSeaUnNombre()
    {
        File.AppendAllText(_procesos, "raro;$(id)\n");

        var (codigo, salida, _) = Procesos();

        Assert.Equal(1, codigo);
        Assert.DoesNotContain("$(", salida, StringComparison.Ordinal);
        Assert.DoesNotContain(";", salida, StringComparison.Ordinal);
    }

    // ---------- Instalación ----------

    private static string Texto(string ruta) => File.ReadAllText(Path.Combine(EntornoDeVmAux.RaizDelRepositorio(), ruta));

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void LaProgramacionIncluyeLosProcesosYElEndurecimiento_ElSegundoComoRootEnSuPropioArchivo()
    {
        var comun = Texto("infra/vm-aux/babelhub-aux.cron");
        var raiz = Texto("infra/vm-aux/babelhub-aux-endurecimiento.cron");

        Assert.Matches(@"(?m)^\*/5 \* \* \* \* babelhub-aux \$AUX/alertar\.sh procesos \$AUX/vigilar-procesos\.sh$", comun);
        Assert.DoesNotContain("verificar-endurecimiento", Regex.Replace(comun, @"(?m)^\s*#.*$", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2)), StringComparison.Ordinal);
        var trabajos = raiz.ReplaceLineEndings("\n").Split('\n').Where(l => Regex.IsMatch(l, @"^[\d*/,]+\s")).ToList();
        var trabajo = Assert.Single(trabajos);
        Assert.Matches(@"^\d+ \d+ \* \* \* root \$AUX/alertar\.sh endurecimiento \$AUX/verificar-endurecimiento\.sh$", trabajo);
        Assert.Contains("AUX=/opt/babelhub/scripts/vm-aux", raiz, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void LaConfiguracionDeSshdQueSeInstala_CumpleLoQueExigeElVerificador()
    {
        // Lo que `sshd -T` mostraría para el usuario del salto: lo que dice el archivo de infra, más lo que no define (por defecto).
        var archivo = Texto("infra/vm-aux/sshd-babelhub-aux.conf").ReplaceLineEndings("\n");
        var efectivo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var linea in archivo.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#') && !l.StartsWith("Match", StringComparison.Ordinal)))
        {
            var partes = linea.Split(' ', 2);
            efectivo[partes[0]] = partes[1];
        }

        foreach (var (clave, valor) in new Dictionary<string, string>
        {
            ["PasswordAuthentication"] = "no", ["KbdInteractiveAuthentication"] = "no", ["PermitRootLogin"] = "no", ["AllowAgentForwarding"] = "no",
            ["X11Forwarding"] = "no", ["PermitEmptyPasswords"] = "no", ["PubkeyAuthentication"] = "yes", ["PermitUserEnvironment"] = "no",
            ["PermitTTY"] = "no", ["AllowTcpForwarding"] = "local", ["MaxAuthTries"] = "3",
        })
        {
            Assert.Equal(valor, efectivo[clave]);
        }

        Assert.Contains("ForceCommand", archivo, StringComparison.Ordinal);
        Assert.Matches(@"(?m)^\s+PermitOpen (10\.\d+\.\d+\.\d+:22 ?)+$", archivo);
        Assert.Contains("AllowUsers", archivo, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void ElEjemploDeLlaveDelSalto_CumpleLoQueExigeElVerificador_YNoTrae_UnaLlavePrivada()
    {
        var ejemplo = Texto("infra/vm-aux/authorized_keys.salto.ejemplo");
        var linea = ejemplo.ReplaceLineEndings("\n").Split('\n').Single(l => l.Length > 0 && !l.StartsWith('#'));

        Assert.StartsWith("restrict,", linea, StringComparison.Ordinal);
        Assert.Contains("port-forwarding", linea, StringComparison.Ordinal);
        Assert.Matches("permitopen=\"10\\.\\d+\\.\\d+\\.\\d+:22\"", linea);
        Assert.DoesNotContain("agent-forwarding", linea, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"PRIVATE KEY|AGE-SECRET-KEY", ejemplo);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-20")]
    public void LaListaInicialDeProcesosPermitidos_NoTieneNombresDeAgentesNiDeHerramientasDeAtaque()
    {
        var lista = Texto("infra/vm-aux/procesos-permitidos.txt").ReplaceLineEndings("\n").Split('\n')
            .Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();

        Assert.NotEmpty(lista);
        Assert.DoesNotContain("ssh-agent", lista);
        Assert.DoesNotContain("nc", lista);
        Assert.DoesNotContain("ncat", lista);
        Assert.DoesNotContain("python3", lista);
        Assert.Contains("rclone", lista);
        Assert.Contains("sshd", lista);
    }

    [Theory]
    [InlineData("verificar-endurecimiento.sh")]
    [InlineData("avisar-ingreso-ssh.sh")]
    [InlineData("vigilar-procesos.sh")]
    [Trait("Criterio", "AC-PLA-20")]
    public void LosScriptsNuevosDeVmAux_NoTienenCrNiLlavesNiTrazaNiCredencialesDeBorrado(string nombre)
    {
        var texto = Texto($"scripts/vm-aux/{nombre}");
        var sinComentarios = Regex.Replace(texto, @"(?m)^\s*#.*$", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2));

        Assert.DoesNotContain('\r', texto);
        Assert.DoesNotMatch(@"AGE-SECRET-KEY-[0-9A-Z]{10,}", texto);
        Assert.DoesNotMatch(@"(?m)^\s*set\s+-[a-z]*x", texto);
        Assert.DoesNotMatch(@"\b(ssh-keygen|ssh-add|id_rsa|id_ed25519|rclone\s+(delete|purge|sync))\b", sinComentarios);
    }
}
