using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Domain.Identidad;
using BabelHub.Ecommerce.Infrastructure.Identidad;
using Konscious.Security.Cryptography;
using DominioProposito = BabelHub.Ecommerce.Domain.Identidad.PropositoDeCodigo;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// La criptografía de Identidad (identidad-seguridad T31; AC-IDE-08, AC-IDE-37, AC-IDE-42; IDE-09, IDE-40, IDE-59): Argon2id con los parámetros de D03, el semáforo global y el cálculo señuelo, la lista de contraseñas comunes,
/// AES-GCM con clave versionada por propósito, las huellas con pimienta y los aleatorios del CSPRNG.
/// </summary>
public sealed class CriptografiaDeIdentidadTests
{
    private static ClaveVersionadaDeIdentidad Clave(string version) =>
        ClaveVersionadaDeIdentidad.Analizar($"{version}:{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}", "clave");

    private static Contrasena Frase(string texto) => Contrasena.Presentada(texto)!;

    // ---------------------------------------------------------------- Argon2id

    [Fact]
    [Trait("Criterio", "AC-IDE-08")]
    public async Task ElResumen_EsArgon2idPHC_ConLosParametrosDeD03_YLaSalEsUnicaPorCuenta()
    {
        using var hasher = new Argon2idHasherDeContrasenas();
        var frase = Frase("una frase larga y segura de prueba");

        var uno = await hasher.Resumir(frase);
        var otro = await hasher.Resumir(frase);

        Assert.NotEqual(uno, otro);
        var leido = ResumenDeContrasena.Leer(uno);
        Assert.NotNull(leido);
        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", uno, StringComparison.Ordinal);
        Assert.False(leido.NecesitaActualizarse());
        Assert.DoesNotContain("frase", uno, StringComparison.Ordinal);
        Assert.Equal("[resumen]", leido.ToString());
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-08")]
    public async Task Verificar_AceptaLaCorrecta_YRechazaLaIncorrecta_YLaDeOtraNormalizacionEsLaMisma()
    {
        using var hasher = new Argon2idHasherDeContrasenas();
        var resumen = await hasher.Resumir(Frase("clave del señor ÅNGSTRÖM 1234"));

        Assert.True(await hasher.Verificar(resumen, Frase("clave del señor ÅNGSTRÖM 1234")));
        Assert.False(await hasher.Verificar(resumen, Frase("clave del señor angstrom 1234")));
        Assert.False(await hasher.Verificar(resumen, Frase("otra frase distinta por completo")));
    }

    [Fact]
    public async Task UnResumenGuardadoConParametrosMasDebiles_SigueVerificando_YPideActualizarse()
    {
        using var hasher = new Argon2idHasherDeContrasenas();
        var sal = RandomNumberGenerator.GetBytes(16);
        byte[] resumen;
        using (var argon = new Argon2id(Encoding.UTF8.GetBytes("frase con parametros viejos")) { Salt = sal, MemorySize = 8 * 1024, Iterations = 1, DegreeOfParallelism = 1 })
        {
            resumen = argon.GetBytes(32);
        }

        var phc = $"$argon2id$v=19$m=8192,t=1,p=1${Convert.ToBase64String(sal).TrimEnd('=')}${Convert.ToBase64String(resumen).TrimEnd('=')}";

        Assert.True(await hasher.Verificar(phc, Frase("frase con parametros viejos")));
        Assert.False(await hasher.Verificar(phc, Frase("otra frase")));
        Assert.True(ResumenDeContrasena.Leer(phc)!.NecesitaActualizarse());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-es-un-resumen")]
    [InlineData("$argon2id$v=19$m=999999999,t=2,p=1$c2FsLXNhbC1zYWwtc2Fs$c2FsLXNhbC1zYWwtc2Fsc2FsLXNhbC1zYWwtc2Fsc2E")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$!!!$!!!")]
    [Trait("Criterio", "AC-IDE-37")]
    public async Task UnResumenAusenteOIlegible_NuncaAcierta_YNoLanza(string? resumen)
    {
        using var hasher = new Argon2idHasherDeContrasenas();

        Assert.False(await hasher.Verificar(resumen, Frase("cualquier frase presentada")));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-37")]
    public async Task ElCalculoSenuelo_PasaPorElMismoSemaforoQueLaVerificacionReal()
    {
        using var hasher = new Argon2idHasherDeContrasenas(concurrenciaMaxima: 1, espera: TimeSpan.FromMilliseconds(1));
        var real = await hasher.Resumir(Frase("frase para el semaforo global"));
        var frase = Frase("frase para el semaforo global");

        var tareas = Enumerable.Range(0, 12).Select(async i =>
        {
            try
            {
                await (i % 2 == 0 ? hasher.Verificar(null, frase) : hasher.Verificar(real, frase));
                return false;
            }
            catch (CalculoDeContrasenasSaturadoException)
            {
                return true;
            }
        }).ToList();
        var saturadas = await Task.WhenAll(tareas);

        Assert.Contains(saturadas.Where((_, i) => i % 2 == 0), s => s);
        Assert.Contains(saturadas.Where((_, i) => i % 2 == 1), s => s);
        Assert.Equal(1, hasher.TurnosLibres);
    }

    [Fact]
    public void LosParametrosDelHasher_SeValidan()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Argon2idHasherDeContrasenas(concurrenciaMaxima: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Argon2idHasherDeContrasenas(espera: TimeSpan.Zero));
    }

    // ---------------------------------------------------------------- lista de contraseñas comunes

    [Fact]
    [Trait("Criterio", "AC-IDE-42")]
    public void LaListaIncrustada_TieneMasDe10000_EnMinusculas_YDeLaLongitudMinimaDeLaPolitica()
    {
        var lista = new RecursoListaDeContrasenasComunes();

        Assert.True(lista.Cantidad >= 10_000, $"Solo {lista.Cantidad} entradas.");
        Assert.True(lista.EsComun("qwertyuiop"));
        Assert.True(lista.EsComun("password123"));
        Assert.True(lista.EsComun("1234567890"));
        Assert.False(lista.EsComun("una frase larga que nadie ha usado antes"));
        Assert.False(lista.EsComun(string.Empty));

        var recurso = typeof(RecursoListaDeContrasenasComunes).Assembly.GetManifestResourceStream(RecursoListaDeContrasenasComunes.NombreDelRecurso)!;
        using var lector = new StreamReader(recurso, Encoding.UTF8);
        var lineas = lector.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.All(lineas, l =>
        {
            Assert.True(l.Length >= PoliticaDeContrasenas.LongitudMinima);
            Assert.Equal(l.ToLowerInvariant(), l);
        });
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-42")]
    public void LaPolitica_RechazaUnaComun_EnCualquierNormalizacion_YAceptaUnaLarga()
    {
        var lista = new RecursoListaDeContrasenasComunes();

        Assert.Equal(ResultadoDeContrasena.Comun, PoliticaDeContrasenas.Evaluar("PASSWORD123", lista.EsComun));
        Assert.Equal(ResultadoDeContrasena.Comun, PoliticaDeContrasenas.Evaluar("ｑｗｅｒｔｙｕｉｏｐ", lista.EsComun));
        Assert.Equal(ResultadoDeContrasena.Aceptable, PoliticaDeContrasenas.Evaluar("mi gato come pescado los viernes", lista.EsComun));
    }

    // ---------------------------------------------------------------- AES-GCM

    [Fact]
    [Trait("Criterio", "AC-IDE-37")]
    public void Cifrar_Y_Descifrar_VuelveAlSecreto_ConUnNonceDistintoEnCadaCifrado()
    {
        var cifrador = new AesGcmCifradorDeSecretos(Clave("v1"));
        var usuario = Guid.NewGuid();
        var secreto = RandomNumberGenerator.GetBytes(20);

        var uno = cifrador.Cifrar(secreto, usuario);
        var otro = cifrador.Cifrar(secreto, usuario);

        Assert.NotEqual(uno, otro);
        Assert.Equal(secreto, cifrador.Descifrar(uno, usuario));
        Assert.Equal(secreto, cifrador.Descifrar(otro, usuario));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-37")]
    public void UnSecretoCopiadoAOtraCuenta_NoDescifra_PorLosDatosAsociados()
    {
        var cifrador = new AesGcmCifradorDeSecretos(Clave("v1"));
        var cifrado = cifrador.Cifrar(RandomNumberGenerator.GetBytes(20), Guid.NewGuid());

        Assert.ThrowsAny<CryptographicException>(() => cifrador.Descifrar(cifrado, Guid.NewGuid()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    [InlineData(-1)]
    [Trait("Criterio", "AC-IDE-37")]
    public void UnByteAlterado_ElCifradoTruncado_OVacio_Lanza(int posicion)
    {
        var cifrador = new AesGcmCifradorDeSecretos(Clave("v1"));
        var usuario = Guid.NewGuid();
        var cifrado = cifrador.Cifrar(RandomNumberGenerator.GetBytes(20), usuario);

        var alterado = (byte[])cifrado.Clone();
        alterado[posicion < 0 ? alterado.Length - 1 : posicion] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => cifrador.Descifrar(alterado, usuario));
        Assert.ThrowsAny<CryptographicException>(() => cifrador.Descifrar(cifrado.AsSpan(0, 10), usuario));
        Assert.ThrowsAny<CryptographicException>(() => cifrador.Descifrar([], usuario));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-37")]
    public void AlRotarLaClave_LoViejoSigueDescifrando_LoNuevoUsaLaActual_YUnaVersionDesconocidaLanza()
    {
        var vieja = Clave("v1");
        var nueva = Clave("v2");
        var usuario = Guid.NewGuid();
        var secreto = RandomNumberGenerator.GetBytes(20);
        var conLaVieja = new AesGcmCifradorDeSecretos(vieja).Cifrar(secreto, usuario);

        var rotado = new AesGcmCifradorDeSecretos(nueva, [vieja]);
        var conLaNueva = rotado.Cifrar(secreto, usuario);

        Assert.Equal(secreto, rotado.Descifrar(conLaVieja, usuario));
        Assert.Equal(secreto, rotado.Descifrar(conLaNueva, usuario));
        Assert.Equal((byte)2, conLaNueva[0]);
        Assert.Equal("v2", Encoding.ASCII.GetString(conLaNueva, 1, 2));
        Assert.ThrowsAny<CryptographicException>(() => new AesGcmCifradorDeSecretos(nueva).Descifrar(conLaVieja, usuario));
    }

    [Fact]
    public void LasClavesDelCifrado_SeValidan()
    {
        var clave = Clave("v1");

        Assert.Throws<ArgumentException>(() => new AesGcmCifradorDeSecretos(clave, [Clave("v1")]));
        Assert.Throws<ArgumentException>(() => ClaveVersionadaDeIdentidad.Analizar("sin-separador", "c"));
        Assert.Throws<ArgumentException>(() => ClaveVersionadaDeIdentidad.Analizar("v1:AAAA", "c"));
        Assert.Throws<ArgumentException>(() => ClaveVersionadaDeIdentidad.Analizar("v1:no es base64!", "c"));
        Assert.Throws<ArgumentException>(() => ClaveVersionadaDeIdentidad.Analizar("version-demasiado-larga-x:AAAA", "c"));
        Assert.Equal("[clave]", clave.ToString());
    }

    [Fact]
    public void LaClaveDelCifrado_NoEsLaMaestraDirecta_SinoDerivadaPorProposito()
    {
        var maestra = RandomNumberGenerator.GetBytes(32);
        var configurada = ClaveVersionadaDeIdentidad.Analizar($"v1:{Convert.ToBase64String(maestra)}", "c");
        var usuario = Guid.NewGuid();
        var cifrado = new AesGcmCifradorDeSecretos(configurada).Cifrar(RandomNumberGenerator.GetBytes(20), usuario);

        // Con la clave maestra usada directamente como clave de AES no se puede descifrar.
        var nonce = cifrado[3..15];
        var etiqueta = cifrado[15..31];
        var texto = cifrado[31..];
        using var aes = new AesGcm(maestra, 16);
        var datosAsociados = Encoding.ASCII.GetBytes($"babelhub/identidad/segundo-factor/{usuario:N}/v1");
        Assert.ThrowsAny<CryptographicException>(() => aes.Decrypt(nonce, texto, etiqueta, new byte[texto.Length], datosAsociados));
    }

    // ---------------------------------------------------------------- huellas con pimienta

    [Fact]
    [Trait("Criterio", "AC-IDE-37")]
    public void LaHuellaEsDeterminista_En64Hexadecimales_YCambiaConElPropositoElUsuarioElValorYLaPimienta()
    {
        var huellas = new HmacHuellasDeIdentidad(Clave("p1"));
        var usuario = Guid.NewGuid();

        var verificacion = huellas.DeCodigo(usuario, DominioProposito.VerificacionDeCorreo, "123456");

        Assert.Equal(verificacion, huellas.DeCodigo(usuario, DominioProposito.VerificacionDeCorreo, "123456"));
        Assert.Matches("^[0-9a-f]{64}$", verificacion);
        Assert.NotEqual(verificacion, huellas.DeCodigo(usuario, DominioProposito.RecuperacionDeAcceso, "123456"));
        Assert.NotEqual(verificacion, huellas.DeCodigo(Guid.NewGuid(), DominioProposito.VerificacionDeCorreo, "123456"));
        Assert.NotEqual(verificacion, huellas.DeCodigo(usuario, DominioProposito.VerificacionDeCorreo, "123457"));
        Assert.NotEqual(verificacion, huellas.DeCodigoDeRespaldo(usuario, "123456"));
        Assert.NotEqual(verificacion, new HmacHuellasDeIdentidad(Clave("p1")).DeCodigo(usuario, DominioProposito.VerificacionDeCorreo, "123456"));
        Assert.DoesNotContain("123456", verificacion, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- aleatorios
}
