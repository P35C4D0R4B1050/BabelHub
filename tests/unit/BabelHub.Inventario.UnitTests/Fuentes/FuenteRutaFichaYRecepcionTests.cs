using BabelHub.Inventario.Domain.Fuentes;

namespace BabelHub.Inventario.UnitTests.Fuentes;

public sealed class FuenteTests
{
    private static Fuente Nueva(string codigo = "ayacucho", string nombre = "Ayacucho", string region = "ayacucho", string? contacto = "+51 966 123 456", bool recepcion = false) =>
        Fuente.Registrar(Guid.NewGuid(), codigo, nombre, region, contacto, recepcion);

    [Fact]
    [Trait("Criterio", "AC-FUE-01")]
    public void Registrar_ConDatosValidos_CreaLaFuente()
    {
        var id = Guid.NewGuid();

        var fuente = Fuente.Registrar(id, "ayacucho", "Ayacucho", "ayacucho", "+51 966 123 456", esRecepcion: false);

        Assert.Equal(id, fuente.Id);
        Assert.Equal("ayacucho", fuente.Codigo);
        Assert.Equal("Ayacucho", fuente.Nombre);
        Assert.Equal("ayacucho", fuente.Region);
        Assert.Equal("+51 966 123 456", fuente.ContactoDeNotificacion);
        Assert.False(fuente.EsRecepcion);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-01")]
    [InlineData("lima")]
    [InlineData("san-isidro")]
    [InlineData("s01")]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnopqrst")]
    public void EsCodigoValido_ConUnaFormaValida_Acepta(string codigo) => Assert.True(Fuente.EsCodigoValido(codigo));

    [Theory]
    [Trait("Criterio", "AC-FUE-01")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("abcdefghijklmnopqrstu")]
    [InlineData("Lima")]
    [InlineData("lima sur")]
    [InlineData("lima_sur")]
    [InlineData("límá")]
    [InlineData("lima;drop")]
    [InlineData("lima\n")]
    public void EsCodigoValido_ConUnaFormaInvalida_Rechaza(string? codigo) => Assert.False(Fuente.EsCodigoValido(codigo));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    [InlineData(" Lima")]
    [InlineData("Lima ")]
    [InlineData("Li\tma")]
    public void EsNombreValido_ConUnNombreInvalido_Rechaza(string? nombre) => Assert.False(Fuente.EsNombreValido(nombre));

    [Fact]
    public void EsNombreValido_AceptaTildesYElLargoMaximoYRechazaMas()
    {
        Assert.True(Fuente.EsNombreValido("Sucursal Huamanga – Plaza"));
        Assert.True(Fuente.EsNombreValido(new string('a', Fuente.LargoMaximoDelNombre)));
        Assert.False(Fuente.EsNombreValido(new string('a', Fuente.LargoMaximoDelNombre + 1)));
    }

    [Theory]
    [InlineData("lima", true)]
    [InlineData("madre-de-dios", true)]
    [InlineData("Lima", false)]
    [InlineData("la libertad", false)]
    [InlineData("-lima", false)]
    [InlineData("lima-", false)]
    [InlineData("lima--sur", false)]
    [InlineData("lima\n", false)]
    [InlineData("junín", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EsRegionValida_ExigeMinusculasSinTildesYConGuiones(string? region, bool esperado) => Assert.Equal(esperado, Fuente.EsRegionValida(region));

    [Theory]
    [InlineData(null, true)]
    [InlineData("+51966123456", true)]
    [InlineData("(064) 123-456", true)]
    [InlineData("966123456", true)]
    [InlineData("12345", false)]
    [InlineData("abc966123456", false)]
    [InlineData("966123456; DROP", false)]
    [InlineData("966123456\n", false)]
    [InlineData(" 966123456", false)]
    [InlineData("+51 966 123 456 789 000", false)]
    [InlineData("", false)]
    public void EsContactoValido_ExigeUnNumeroOAusencia(string? contacto, bool esperado) => Assert.Equal(esperado, Fuente.EsContactoValido(contacto));

    [Fact]
    public void Registrar_ConUnIdentificadorVacioOUnDatoInvalido_Lanza()
    {
        Assert.Throws<ArgumentException>(() => Fuente.Registrar(Guid.Empty, "lima", "Lima", "lima", null, true));
        Assert.Throws<ArgumentException>(() => Fuente.Registrar(Guid.NewGuid(), "Lima", "Lima", "lima", null, true));
        Assert.Throws<ArgumentException>(() => Fuente.Registrar(Guid.NewGuid(), "lima", " ", "lima", null, true));
        Assert.Throws<ArgumentException>(() => Fuente.Registrar(Guid.NewGuid(), "lima", "Lima", "Lima Norte", null, true));
        Assert.Throws<ArgumentException>(() => Fuente.Registrar(Guid.NewGuid(), "lima", "Lima", "lima", "no-es-un-numero", true));
    }

    [Fact]
    public void Registrar_SinContacto_Procede()
    {
        Assert.Null(Nueva(contacto: null).ContactoDeNotificacion);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-13")]
    public void Editar_DevuelveLosNombresDeLosCamposQueCambiaronYNuncaElContacto()
    {
        var fuente = Nueva(contacto: "+51 966 123 456");

        var (editada, cambiados) = fuente.Editar("Ayacucho Centro", "ayacucho", "+51 966 999 888");

        Assert.Equal(["nombre", "contactoDeNotificacion"], cambiados);
        Assert.DoesNotContain(cambiados, c => c.Contains("999", StringComparison.Ordinal));
        Assert.Equal("Ayacucho Centro", editada.Nombre);
        Assert.Equal("+51 966 999 888", editada.ContactoDeNotificacion);
        Assert.Equal(fuente.Id, editada.Id);
        Assert.Equal(fuente.Codigo, editada.Codigo);
        Assert.Equal("Ayacucho", fuente.Nombre); // la original no cambia
    }

    [Fact]
    public void Editar_SinCambiosReales_NoDevuelveCampos()
    {
        var fuente = Nueva();

        var (_, cambiados) = fuente.Editar(fuente.Nombre, fuente.Region, fuente.ContactoDeNotificacion);

        Assert.Empty(cambiados);
    }

    [Fact]
    public void Editar_ConUnDatoInvalido_Lanza()
    {
        Assert.Throws<ArgumentException>(() => Nueva().Editar("Ayacucho", "Ayacucho Región", null));
    }

    [Fact]
    public void Editar_ConservaLaMarcaDeRecepcion()
    {
        var (editada, _) = Nueva(recepcion: true).Editar("Otra", "lima", null);

        Assert.True(editada.EsRecepcion);
    }
}

public sealed class RutaEntreFuentesTests
{
    private static readonly Guid Cusco = Guid.NewGuid();
    private static readonly Guid Ayacucho = Guid.NewGuid();

    [Fact]
    [Trait("Criterio", "AC-FUE-10")]
    public void Definir_ConUnPlazoValido_CreaLaRuta()
    {
        var ruta = RutaEntreFuentes.Definir(Cusco, Ayacucho, 2);

        Assert.Equal(Cusco, ruta.OrigenId);
        Assert.Equal(Ayacucho, ruta.DestinoId);
        Assert.Equal(2, ruta.PlazoEnDias);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-14")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Validar_ConUnPlazoQueNoEsMayorQueCero_Rechaza(int dias)
    {
        Assert.Equal(MotivoDeRutaInvalida.PlazoNoPositivo, RutaEntreFuentes.Validar(Cusco, Ayacucho, dias));
        Assert.Throws<ArgumentException>(() => RutaEntreFuentes.Definir(Cusco, Ayacucho, dias));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-14")]
    public void Validar_ConUnaRutaDeUnaFuenteASiMisma_Rechaza()
    {
        Assert.Equal(MotivoDeRutaInvalida.OrigenIgualADestino, RutaEntreFuentes.Validar(Cusco, Cusco, 2));
        Assert.Throws<ArgumentException>(() => RutaEntreFuentes.Definir(Cusco, Cusco, 2));
    }

    [Fact]
    public void Validar_SinOrigenODestino_Rechaza()
    {
        Assert.Equal(MotivoDeRutaInvalida.FuenteVacia, RutaEntreFuentes.Validar(Guid.Empty, Ayacucho, 2));
        Assert.Equal(MotivoDeRutaInvalida.FuenteVacia, RutaEntreFuentes.Validar(Cusco, Guid.Empty, 2));
    }

    [Fact]
    public void Validar_ConUnaRutaValida_NoDevuelveMotivo() => Assert.Null(RutaEntreFuentes.Validar(Cusco, Ayacucho, 1));

    [Fact]
    [Trait("Criterio", "AC-FUE-11")]
    public void LaRuta_EsDireccional()
    {
        var ida = RutaEntreFuentes.Definir(Cusco, Ayacucho, 2);
        var vuelta = RutaEntreFuentes.Definir(Ayacucho, Cusco, 2);

        Assert.NotEqual(ida, vuelta);
        Assert.Equal(RutaEntreFuentes.Definir(Cusco, Ayacucho, 2), ida);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-10")]
    public void ConPlazo_CambiaSoloElPlazoYLoValida()
    {
        var ruta = RutaEntreFuentes.Definir(Cusco, Ayacucho, 2);

        var nueva = ruta.ConPlazo(3);

        Assert.Equal(3, nueva.PlazoEnDias);
        Assert.Equal(Cusco, nueva.OrigenId);
        Assert.Equal(Ayacucho, nueva.DestinoId);
        Assert.Equal(2, ruta.PlazoEnDias);
        Assert.Throws<ArgumentException>(() => ruta.ConPlazo(0));
    }
}

public sealed class FichaDeEdicionTests
{
    private static readonly Guid Edicion = Guid.NewGuid();

    [Fact]
    public void PuedeTenerStock_SoloLasImpresas()
    {
        Assert.True(FichaDeEdicion.Crear(Edicion, FormatoDeEdicion.Impreso).PuedeTenerStock);
        Assert.False(FichaDeEdicion.Crear(Edicion, FormatoDeEdicion.Digital).PuedeTenerStock);
    }

    [Fact]
    public void Crear_ConUnIdentificadorVacioOUnFormatoDesconocido_Lanza()
    {
        Assert.Throws<ArgumentException>(() => FichaDeEdicion.Crear(Guid.Empty, FormatoDeEdicion.Impreso));
        Assert.Throws<ArgumentOutOfRangeException>(() => FichaDeEdicion.Crear(Edicion, (FormatoDeEdicion)99));
    }

    [Fact]
    public void Actualizar_DeUnaEdicionDesconocida_LaRegistra()
    {
        var (resultado, ficha) = FichaDeEdicion.Actualizar(null, Edicion, FormatoDeEdicion.Digital, tieneStock: false);

        Assert.Equal(ResultadoDeFicha.Registrada, resultado);
        Assert.Equal(FormatoDeEdicion.Digital, ficha.Formato);
    }

    [Fact]
    public void Actualizar_ConElMismoFormato_EsIdempotenteAunqueTengaStock()
    {
        var actual = FichaDeEdicion.Crear(Edicion, FormatoDeEdicion.Impreso);

        var (resultado, ficha) = FichaDeEdicion.Actualizar(actual, Edicion, FormatoDeEdicion.Impreso, tieneStock: true);

        Assert.Equal(ResultadoDeFicha.SinCambios, resultado);
        Assert.Same(actual, ficha);
    }

    [Fact]
    public void Actualizar_ConOtroFormatoYSinStock_LoActualiza()
    {
        var actual = FichaDeEdicion.Crear(Edicion, FormatoDeEdicion.Impreso);

        var (resultado, ficha) = FichaDeEdicion.Actualizar(actual, Edicion, FormatoDeEdicion.Digital, tieneStock: false);

        Assert.Equal(ResultadoDeFicha.FormatoActualizado, resultado);
        Assert.Equal(FormatoDeEdicion.Digital, ficha.Formato);
    }

    [Fact]
    public void Actualizar_ConOtroFormatoYConStock_EsConflictoYNoCambiaLaFicha()
    {
        var actual = FichaDeEdicion.Crear(Edicion, FormatoDeEdicion.Impreso);

        var (resultado, ficha) = FichaDeEdicion.Actualizar(actual, Edicion, FormatoDeEdicion.Digital, tieneStock: true);

        Assert.Equal(ResultadoDeFicha.FormatoEnConflicto, resultado);
        Assert.Equal(FormatoDeEdicion.Impreso, ficha.Formato);
    }

    [Fact]
    public void Actualizar_ConLaFichaDeOtraEdicion_Lanza()
    {
        var otra = FichaDeEdicion.Crear(Guid.NewGuid(), FormatoDeEdicion.Impreso);

        Assert.Throws<ArgumentException>(() => FichaDeEdicion.Actualizar(otra, Edicion, FormatoDeEdicion.Impreso, tieneStock: false));
    }
}

public sealed class ReglaDeRecepcionTests
{
    private static Fuente Una(string codigo, bool recepcion = false) => Fuente.Registrar(Guid.NewGuid(), codigo, codigo.ToUpperInvariant(), "lima", null, recepcion);

    [Fact]
    [Trait("Criterio", "AC-FUE-16")]
    public void LaPrimeraFuenteRegistrada_EsLaDeRecepcionYLasSiguientesNo()
    {
        Assert.True(ReglaDeRecepcion.LaNuevaEsRecepcion(0));
        Assert.False(ReglaDeRecepcion.LaNuevaEsRecepcion(1));
        Assert.False(ReglaDeRecepcion.LaNuevaEsRecepcion(6));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReglaDeRecepcion.LaNuevaEsRecepcion(-1));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-16")]
    public void Designar_OtraFuente_DejaUnaSolaRecepcionYLaAnteriorYaNoLoEs()
    {
        var lima = Una("lima", recepcion: true);
        var cusco = Una("cusco");
        var arequipa = Una("arequipa");

        var designacion = ReglaDeRecepcion.Designar([lima, cusco, arequipa], arequipa.Id);

        Assert.Equal(ResultadoDeDesignacion.Cambiada, designacion.Resultado);
        Assert.Equal(lima.Id, designacion.Anterior!.Id);
        Assert.False(designacion.Anterior.EsRecepcion);
        Assert.Equal(arequipa.Id, designacion.Nueva!.Id);
        Assert.True(designacion.Nueva.EsRecepcion);
        Assert.Single(designacion.Fuentes, f => f.EsRecepcion);
        Assert.Equal(arequipa.Id, designacion.Fuentes.Single(f => f.EsRecepcion).Id);
        Assert.True(ReglaDeRecepcion.CumpleElInvariante(designacion.Fuentes));
        Assert.Equal(3, designacion.Fuentes.Count);
    }

    [Fact]
    public void Designar_LaQueYaEsRecepcion_NoCambiaNada()
    {
        var lima = Una("lima", recepcion: true);
        var cusco = Una("cusco");

        var designacion = ReglaDeRecepcion.Designar([lima, cusco], lima.Id);

        Assert.Equal(ResultadoDeDesignacion.SinCambios, designacion.Resultado);
        Assert.Null(designacion.Anterior);
        Assert.Equal([lima, cusco], designacion.Fuentes);
    }

    [Fact]
    public void Designar_UnaFuenteInexistente_NoCambiaNada()
    {
        var lima = Una("lima", recepcion: true);

        var designacion = ReglaDeRecepcion.Designar([lima], Guid.NewGuid());

        Assert.Equal(ResultadoDeDesignacion.FuenteInexistente, designacion.Resultado);
        Assert.Equal([lima], designacion.Fuentes);
        Assert.True(ReglaDeRecepcion.CumpleElInvariante(designacion.Fuentes));
    }

    [Fact]
    public void Designar_ConUnEstadoRotoConDosRecepciones_LoRepara()
    {
        var a = Una("lima", recepcion: true);
        var b = Una("cusco", recepcion: true);
        var c = Una("arequipa");

        var designacion = ReglaDeRecepcion.Designar([a, b, c], b.Id);

        Assert.Equal(ResultadoDeDesignacion.Cambiada, designacion.Resultado);
        Assert.True(ReglaDeRecepcion.CumpleElInvariante(designacion.Fuentes));
        Assert.Equal(b.Id, designacion.Fuentes.Single(f => f.EsRecepcion).Id);
    }

    [Fact]
    public void CumpleElInvariante_ExigeExactamenteUnaConFuentesYNingunaSinFuentes()
    {
        Assert.True(ReglaDeRecepcion.CumpleElInvariante([]));
        Assert.True(ReglaDeRecepcion.CumpleElInvariante([Una("lima", true), Una("cusco")]));
        Assert.False(ReglaDeRecepcion.CumpleElInvariante([Una("lima"), Una("cusco")]));
        Assert.False(ReglaDeRecepcion.CumpleElInvariante([Una("lima", true), Una("cusco", true)]));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-16")]
    public void Registrar_SeisFuentesEnOrden_DejaLaPrimeraComoLaUnicaRecepcion()
    {
        var fuentes = new List<Fuente>();
        foreach (var codigo in new[] { "lima", "cusco", "ayacucho", "arequipa", "piura", "tacna" })
        {
            fuentes.Add(Fuente.Registrar(Guid.NewGuid(), codigo, codigo.ToUpperInvariant(), "lima", null, ReglaDeRecepcion.LaNuevaEsRecepcion(fuentes.Count)));
        }

        Assert.True(ReglaDeRecepcion.CumpleElInvariante(fuentes));
        Assert.Equal("lima", fuentes.Single(f => f.EsRecepcion).Codigo);
    }
}
