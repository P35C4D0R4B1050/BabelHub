using BabelHub.Inventario.Domain.Plataforma;

namespace BabelHub.Inventario.UnitTests.Plataforma;

public sealed class RegistroDeSupresionesTests
{
    private const string Cuenta = "3f2a9c41b8d74e6a9d015c7e2b4f8a60";
    private const string OtraCuenta = "9b1e7d52c4a8436fa0e3d6b7c8f12a45";
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static EntradaDeSupresion Entrada(string cuenta = Cuenta, TipoDeSupresion tipo = TipoDeSupresion.Cancelacion, FaseDeSupresion fase = FaseDeSupresion.Intencion, DateTimeOffset? fecha = null) =>
        EntradaDeSupresion.Crear(cuenta, tipo, fase, fecha ?? Ahora);

    /// <summary>La entrada tal como la devuelve el almacén: con su clave real y la hora en que se modificó.</summary>
    private static EntradaLeida Leida(EntradaDeSupresion entrada, TimeSpan haceCuanto) => new(entrada.Clave, entrada, Ahora - haceCuanto);

    /// <summary>El SHA-256 en hexadecimal minúscula, calculado aparte para no depender de la implementación que se prueba.</summary>
    private static string Sha256(string texto) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(texto))).ToLowerInvariant();

    // ---------- EntradaDeSupresion ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void LaClaveEsDeterministaYLlevaElIdentificadorElTipoYLaFase()
    {
        Assert.Equal($"entradas/{Cuenta}/cancelacion-intencion.json", Entrada().Clave);
        Assert.Equal($"entradas/{Cuenta}/cancelacion-cierre.json", Entrada(fase: FaseDeSupresion.Cierre).Clave);
        Assert.Equal($"entradas/{Cuenta}/anonimizacion-intencion.json", Entrada(tipo: TipoDeSupresion.Anonimizacion).Clave);
        Assert.Equal(Entrada().Clave, Entrada(fecha: Ahora.AddDays(3)).Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void LaSumaEsUnSha256EnHexadecimalYCambiaConCadaCampo()
    {
        var baseline = Entrada();

        Assert.Matches("^[0-9a-f]{64}$", baseline.Suma);
        Assert.Equal(baseline.Suma, Entrada().Suma);
        Assert.NotEqual(baseline.Suma, Entrada(cuenta: OtraCuenta).Suma);
        Assert.NotEqual(baseline.Suma, Entrada(tipo: TipoDeSupresion.Anonimizacion).Suma);
        Assert.NotEqual(baseline.Suma, Entrada(fase: FaseDeSupresion.Cierre).Suma);
        Assert.NotEqual(baseline.Suma, Entrada(fecha: Ahora.AddTicks(1)).Suma);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void LaFechaSeNormalizaAUtcYElContenidoCanonicoTieneCuatroCamposSinDatosPersonales()
    {
        var limaDeLas7 = new DateTimeOffset(2026, 11, 13, 7, 0, 0, TimeSpan.FromHours(-5));

        var entrada = Entrada(fecha: limaDeLas7);

        Assert.Equal(Entrada().Suma, entrada.Suma);
        Assert.Equal(TimeSpan.Zero, entrada.Fecha.Offset);
        Assert.Equal(
            $"identificadorOpaco={Cuenta}\ntipo=cancelacion\nfase=intencion\nfecha=2026-11-13T12:00:00.0000000Z",
            entrada.ContenidoCanonico);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-37")]
    [InlineData("lector@correo.pe")]
    [InlineData("corto")]
    [InlineData("")]
    [InlineData("con espacios en el identificador 1234")]
    [InlineData("-3f2a9c41b8d74e6a9d015c7e2b4f8a60")]
    [InlineData("3f2a9c41b8d74e6a9d015c7e2b4f8a60/../otra")]
    [InlineData("3f2a9c41b8d74e6a9d015c7e2b4f8a60\nfase=cierre")]
    public void UnIdentificadorQueNoEsElInternoAleatorioSeRechaza(string identificador)
    {
        Assert.Throws<ArgumentException>(() => EntradaDeSupresion.Crear(identificador, TipoDeSupresion.Cancelacion, FaseDeSupresion.Intencion, Ahora));
        Assert.Throws<ArgumentException>(() => EntradaDeSupresion.Reconstruir(identificador, TipoDeSupresion.Cancelacion, FaseDeSupresion.Intencion, Ahora, new string('a', 64)));
    }

    [Fact]
    public void UnIdentificadorCompletoEnFormatoGuidSeAcepta()
    {
        var entrada = Entrada(cuenta: Guid.NewGuid().ToString("D"));

        Assert.True(entrada.SumaEsValida());
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-37")]
    public void UnaEntradaReconstruidaConSuSumaOriginalEsValidaYConOtraNo()
    {
        var original = Entrada();

        var fiel = EntradaDeSupresion.Reconstruir(Cuenta, original.Tipo, original.Fase, original.Fecha, original.Suma);
        var cambiada = EntradaDeSupresion.Reconstruir(Cuenta, original.Tipo, original.Fase, original.Fecha.AddMinutes(1), original.Suma);
        var otroTipo = EntradaDeSupresion.Reconstruir(Cuenta, TipoDeSupresion.Anonimizacion, original.Fase, original.Fecha, original.Suma);

        Assert.True(fiel.SumaEsValida());
        Assert.False(cambiada.SumaEsValida());
        Assert.False(otroTipo.SumaEsValida());
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void UnaSumaQueNoEsUnSha256EnMinusculaSeRechaza(string suma)
    {
        Assert.Throws<ArgumentException>(() => EntradaDeSupresion.Reconstruir(Cuenta, TipoDeSupresion.Cancelacion, FaseDeSupresion.Cierre, Ahora, suma));
    }

    // ---------- SelloDelRegistro ----------

    [Fact]
    public void LaSumaDelConjuntoNoDependeDelOrdenDeLasEntradas()
    {
        var a = Leida(Entrada(), TimeSpan.FromHours(3));
        var b = Leida(Entrada(cuenta: OtraCuenta, fase: FaseDeSupresion.Cierre), TimeSpan.FromHours(2));
        var c = Leida(Entrada(tipo: TipoDeSupresion.Anonimizacion), TimeSpan.FromHours(1));

        Assert.Equal(SelloDelRegistro.SumaDelConjuntoDe([a, b, c]), SelloDelRegistro.SumaDelConjuntoDe([c, a, b]));
    }

    [Fact]
    public void LaSumaDelConjuntoTieneLaFormaCanonicaDeClaveTabulacionSumaYSaltoDeLinea()
    {
        var a = Leida(Entrada(), TimeSpan.FromHours(3));
        var b = Leida(Entrada(cuenta: OtraCuenta), TimeSpan.FromHours(2));

        // Las claves se ordenan en orden ordinal: «entradas/3f2a…» va antes que «entradas/9b1e…».
        var esperado = Sha256($"{a.ClaveEnElAlmacen}\t{a.Entrada.Suma}\n{b.ClaveEnElAlmacen}\t{b.Entrada.Suma}\n");

        Assert.Equal(esperado, SelloDelRegistro.SumaDelConjuntoDe([b, a]));
        Assert.Equal(Sha256(string.Empty), SelloDelRegistro.SumaDelConjuntoDe([]));
    }

    [Fact]
    public void LaSumaDelConjuntoCambiaSiCambiaUnaEntradaOSeAgregaOSeQuitaUna()
    {
        var a = Leida(Entrada(), TimeSpan.FromHours(3));
        var b = Leida(Entrada(cuenta: OtraCuenta), TimeSpan.FromHours(2));
        var baseline = SelloDelRegistro.SumaDelConjuntoDe([a, b]);

        Assert.NotEqual(baseline, SelloDelRegistro.SumaDelConjuntoDe([a]));
        Assert.NotEqual(baseline, SelloDelRegistro.SumaDelConjuntoDe([a, b, Leida(Entrada(fase: FaseDeSupresion.Cierre), TimeSpan.FromHours(1))]));
        Assert.NotEqual(baseline, SelloDelRegistro.SumaDelConjuntoDe([a, Leida(Entrada(cuenta: OtraCuenta, fecha: Ahora.AddSeconds(1)), TimeSpan.FromHours(2))]));
    }

    [Fact]
    public void ElSelloSoloCubreLoModificadoAntesDeSuInstanteMenosElMargen()
    {
        var antiguo = Leida(Entrada(), TimeSpan.FromHours(3));
        var justoEnElMargen = Leida(Entrada(cuenta: OtraCuenta), SelloDelRegistro.Margen);
        var reciente = Leida(Entrada(tipo: TipoDeSupresion.Anonimizacion), TimeSpan.FromMinutes(5));

        var sello = SelloDelRegistro.Sellar(Ahora, [antiguo, justoEnElMargen, reciente]);

        // «Antes de» es estricto: la que se modificó exactamente en el límite todavía no está sellada.
        Assert.Equal(1, sello.Cantidad);
        Assert.Equal(SelloDelRegistro.SumaDelConjuntoDe([antiguo]), sello.SumaDelConjunto);
        Assert.Equal(Ahora, sello.Instante);
    }

    [Fact]
    public void UnSelloNoAceptaUnaCantidadNegativa()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SelloDelRegistro(Ahora, -1, "x"));
    }

    // ---------- VerificadorDelRegistro ----------

    private static (List<EntradaLeida> Entradas, SelloDelRegistro Sello) RegistroCorrecto()
    {
        var entradas = new List<EntradaLeida>
        {
            Leida(Entrada(), TimeSpan.FromHours(6)),
            Leida(Entrada(fase: FaseDeSupresion.Cierre, fecha: Ahora.AddMinutes(1)), TimeSpan.FromHours(5)),
            Leida(Entrada(cuenta: OtraCuenta, tipo: TipoDeSupresion.Anonimizacion), TimeSpan.FromHours(4)),
        };
        return (entradas, SelloDelRegistro.Sellar(Ahora, entradas));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public void UnRegistroQueCoincideConSuSello_PermiteAbrir()
    {
        var (entradas, sello) = RegistroCorrecto();

        var resultado = VerificadorDelRegistro.Verificar(entradas, sello);

        Assert.True(resultado.PermiteAbrir);
        Assert.Equal(EstadoDeVerificacionDelRegistro.Valido, resultado.Estado);
        Assert.Empty(resultado.ClavesAfectadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public void UnRegistroVacioConSuSelloDeCero_PermiteAbrir()
    {
        var resultado = VerificadorDelRegistro.Verificar([], SelloDelRegistro.Sellar(Ahora, []));

        Assert.True(resultado.PermiteAbrir);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public void ConElRegistroAusente_NoPermiteAbrir()
    {
        var (_, sello) = RegistroCorrecto();

        var resultado = VerificadorDelRegistro.Verificar(null, sello);

        Assert.False(resultado.PermiteAbrir);
        Assert.Equal(EstadoDeVerificacionDelRegistro.RegistroAusente, resultado.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public void SinSelloContraElCualVerificar_NoPermiteAbrir()
    {
        var (entradas, _) = RegistroCorrecto();

        var resultado = VerificadorDelRegistro.Verificar(entradas, null);

        Assert.False(resultado.PermiteAbrir);
        Assert.Equal(EstadoDeVerificacionDelRegistro.SelloAusente, resultado.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public void ConUnaEntradaAlteradaEnSuContenido_NoPermiteAbrirYSenalaLaClave()
    {
        var (entradas, sello) = RegistroCorrecto();
        var original = entradas[1].Entrada;
        // El contenido cambió (otra fecha) pero la suma que trae es la vieja.
        var alterada = EntradaDeSupresion.Reconstruir(Cuenta, original.Tipo, original.Fase, original.Fecha.AddDays(-30), original.Suma);
        entradas[1] = entradas[1] with { Entrada = alterada };

        var resultado = VerificadorDelRegistro.Verificar(entradas, sello);

        Assert.False(resultado.PermiteAbrir);
        Assert.Equal(EstadoDeVerificacionDelRegistro.EntradaAlterada, resultado.Estado);
        Assert.Equal([original.Clave], resultado.ClavesAfectadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public void ConUnaEntradaEnUnaClaveQueNoEsLaSuya_NoPermiteAbrir()
    {
        var (entradas, sello) = RegistroCorrecto();
        entradas[0] = entradas[0] with { ClaveEnElAlmacen = $"entradas/{OtraCuenta}/cancelacion-intencion.json" };

        var resultado = VerificadorDelRegistro.Verificar(entradas, sello);

        Assert.Equal(EstadoDeVerificacionDelRegistro.EntradaAlterada, resultado.Estado);
        Assert.Equal([$"entradas/{OtraCuenta}/cancelacion-intencion.json"], resultado.ClavesAfectadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public void ConUnaEntradaBorrada_NoPermiteAbrir()
    {
        var (entradas, sello) = RegistroCorrecto();
        entradas.RemoveAt(1);

        var resultado = VerificadorDelRegistro.Verificar(entradas, sello);

        Assert.False(resultado.PermiteAbrir);
        Assert.Equal(EstadoDeVerificacionDelRegistro.EntradasAusentes, resultado.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public void ConUnaEntradaAgregadaAntesDelSelloQueElSelloNoCuenta_NoPermiteAbrir()
    {
        var (entradas, sello) = RegistroCorrecto();
        // Una entrada válida por sí sola (suma correcta, clave correcta) pero que el sello no vio, con una hora de modificación anterior a él.
        entradas.Add(Leida(Entrada(cuenta: "7c3d5e91a2b84f06b1d9e8a7c6f50d32", fase: FaseDeSupresion.Cierre), TimeSpan.FromHours(2)));

        var resultado = VerificadorDelRegistro.Verificar(entradas, sello);

        Assert.False(resultado.PermiteAbrir);
        Assert.Equal(EstadoDeVerificacionDelRegistro.SumaDelConjuntoNoCoincide, resultado.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public void ConUnaEntradaSustituidaPorOtraValidaDeLaMismaClave_NoPermiteAbrir()
    {
        var (entradas, sello) = RegistroCorrecto();
        // Mismo objeto, misma clave, contenido distinto pero con su propia suma correcta (quien lo sobrescribió también sabe calcular la suma).
        var falsa = Entrada(fecha: Ahora.AddDays(-10));
        entradas[0] = entradas[0] with { Entrada = falsa };

        var resultado = VerificadorDelRegistro.Verificar(entradas, sello);

        Assert.False(resultado.PermiteAbrir);
        Assert.Equal(EstadoDeVerificacionDelRegistro.SumaDelConjuntoNoCoincide, resultado.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public void ConUnaSumaDelSelloQueNoCoincide_NoPermiteAbrir()
    {
        var (entradas, sello) = RegistroCorrecto();
        var otro = new SelloDelRegistro(sello.Instante, sello.Cantidad, new string('0', 64));

        var resultado = VerificadorDelRegistro.Verificar(entradas, otro);

        Assert.Equal(EstadoDeVerificacionDelRegistro.SumaDelConjuntoNoCoincide, resultado.Estado);
    }

    [Fact]
    public void LoEscritoDespuesDelSelloMenosElMargenNoAlteraLaVerificacion()
    {
        var (entradas, sello) = RegistroCorrecto();
        // Dos entradas nuevas: una modificada hace 5 minutos y otra exactamente en el límite del margen; ninguna está en el conjunto sellado.
        entradas.Add(Leida(Entrada(cuenta: "7c3d5e91a2b84f06b1d9e8a7c6f50d32", fase: FaseDeSupresion.Cierre), TimeSpan.FromMinutes(5)));
        entradas.Add(Leida(Entrada(cuenta: "a1b2c3d4e5f60718293a4b5c6d7e8f90"), SelloDelRegistro.Margen));

        var resultado = VerificadorDelRegistro.Verificar(entradas, sello);

        Assert.True(resultado.PermiteAbrir);
    }

    [Fact]
    public void UnaEntradaAlteradaTambienSeDetectaSiEsReciente()
    {
        var (entradas, sello) = RegistroCorrecto();
        var reciente = Entrada(cuenta: "7c3d5e91a2b84f06b1d9e8a7c6f50d32", fase: FaseDeSupresion.Cierre);
        var alterada = EntradaDeSupresion.Reconstruir("7c3d5e91a2b84f06b1d9e8a7c6f50d32", reciente.Tipo, reciente.Fase, reciente.Fecha.AddHours(1), reciente.Suma);
        entradas.Add(new EntradaLeida(reciente.Clave, alterada, Ahora - TimeSpan.FromMinutes(1)));

        var resultado = VerificadorDelRegistro.Verificar(entradas, sello);

        Assert.Equal(EstadoDeVerificacionDelRegistro.EntradaAlterada, resultado.Estado);
    }
}
