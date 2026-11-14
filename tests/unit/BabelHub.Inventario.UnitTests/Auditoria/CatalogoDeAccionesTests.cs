using BabelHub.Inventario.Domain.Auditoria;

namespace BabelHub.Inventario.UnitTests.Auditoria;

/// <summary>
/// El catálogo de acciones (AUD-01, AUD-02) y <c>PlantillaDeRuta</c> (AUD-05, AUD-06): cada acción declara su clase, el tipo de su registro, las
/// claves permitidas de su detalle, si su objeto y su rol son personales (obligatorio, sin valor por defecto) y sus motivos, de catálogo cerrado.
/// </summary>
public sealed class CatalogoDeAccionesTests
{
    private static AccionDelCatalogo Accion(
        string id = "CambioDePrecio",
        ClaseDeAccion clase = ClaseDeAccion.ConcedeOCambia,
        TipoDeRegistro tipo = TipoDeRegistro.Auditoria,
        bool aislada = false,
        bool objetoPersonal = false,
        bool rolPersonal = false,
        string[]? motivos = null,
        bool exigeMotivo = false,
        ClaveDeDetalle[]? claves = null) =>
        new(id, clase, tipo, aislada, claves ?? [new("precioAnterior", false), new("precioNuevo", false)], objetoPersonal, rolPersonal, motivos ?? (tipo == TipoDeRegistro.Auditoria ? [] : ["motivo-de-prueba"]), exigeMotivo || tipo != TipoDeRegistro.Auditoria);

    // ---------- La acción ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-01")]
    public void UnaAccionDeclaraSuClaseSuTipoSusClavesYSiSuObjetoYSuRolSonPersonales()
    {
        var accion = Accion("CambioDeRol", ClaseDeAccion.ConcedeOCambia, TipoDeRegistro.Ambos, aislada: true, objetoPersonal: true, rolPersonal: true, motivos: ["solicitud-de-la-administracion", "correccion"], claves: [new("rolAnterior", false), new("rolNuevo", false), new("nombre", true)]);

        Assert.Equal("CambioDeRol", accion.Identificador);
        Assert.Equal(ClaseDeAccion.ConcedeOCambia, accion.Clase);
        Assert.Equal(TipoDeRegistro.Ambos, accion.Tipo);
        Assert.True(accion.Aislada);
        Assert.True(accion.ObjetoPersonal);
        Assert.True(accion.RolPersonal);
        Assert.Equal(["rolAnterior", "rolNuevo", "nombre"], accion.Detalle.Select(c => c.Nombre));
        Assert.Equal(["nombre"], accion.Detalle.Where(c => c.Personal).Select(c => c.Nombre));
        Assert.Equal(["correccion", "solicitud-de-la-administracion"], accion.Motivos.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("cambioDePrecio")]
    [InlineData("Cambio-de-precio")]
    [InlineData("Cambio De Precio")]
    [InlineData("C")]
    [InlineData("Ca")]
    public void UnIdentificadorDeAccionInvalido_SeRechaza(string id) =>
        Assert.Throws<ArgumentException>(() => Accion(id));

    [Theory]
    [InlineData("Mayuscula")]
    [InlineData("con espacio")]
    [InlineData("")]
    [InlineData("x")]
    public void UnMotivoFueraDelFormatoDeCatalogo_SeRechaza(string motivo) =>
        Assert.Throws<ArgumentException>(() => Accion(motivos: [motivo]));

    [Fact]
    public void UnMotivoRepetidoOUnaClaveRepetida_SeRechaza()
    {
        Assert.Throws<ArgumentException>(() => Accion(motivos: ["a-b", "a-b"]));
        Assert.Throws<ArgumentException>(() => Accion(claves: [new("a", false), new("a", true)]));
    }

    [Fact]
    public void SiLaAccionExigeMotivo_DebeTenerAlMenosUnoEnElCatalogo() =>
        Assert.Throws<ArgumentException>(() => Accion(exigeMotivo: true, motivos: []));

    [Theory]
    [InlineData(TipoDeRegistro.Seguridad, ClaseDeAccion.NoConcede)]
    [InlineData(TipoDeRegistro.Ambos, ClaseDeAccion.ConcedeOCambia)]
    public void UnEventoDeSeguridadLlevaSiempreSuMotivo_LaAccionDebeExigirlo(TipoDeRegistro tipo, ClaseDeAccion clase) =>
        Assert.Throws<ArgumentException>(() => new AccionDelCatalogo("AccionDePrueba", clase, tipo, false, [], false, false, ["un-motivo"], false));

    [Theory]
    [InlineData(ClaseDeAccion.ConcedeOCambia, TipoDeRegistro.Auditoria, true)]
    [InlineData(ClaseDeAccion.ConcedeOCambia, TipoDeRegistro.Ambos, true)]
    [InlineData(ClaseDeAccion.ConcedeOCambia, TipoDeRegistro.Seguridad, false)]
    [InlineData(ClaseDeAccion.NoConcede, TipoDeRegistro.Seguridad, true)]
    [InlineData(ClaseDeAccion.NoConcede, TipoDeRegistro.Auditoria, false)]
    [InlineData(ClaseDeAccion.NoConcede, TipoDeRegistro.Ambos, false)]
    public void LaClaseDeLaAccionYElTipoDeSuRegistroDebenSerCoherentes(ClaseDeAccion clase, TipoDeRegistro tipo, bool valida)
    {
        if (valida)
        {
            Assert.Equal(tipo, Accion(clase: clase, tipo: tipo).Tipo);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => Accion(clase: clase, tipo: tipo));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("correccion")]
    public void SinMotivosDeclarados_NingunMotivoEsValido_ConMotivos_SoloLosDelCatalogo(string? motivo)
    {
        var sin = Accion();
        var con = Accion(motivos: ["correccion", "solicitud"]);

        Assert.Equal(motivo is null, sin.AdmiteMotivo(motivo));
        Assert.True(con.AdmiteMotivo(motivo));
        Assert.False(con.AdmiteMotivo("otro-motivo"));
        Assert.False(con.AdmiteMotivo("Correccion"));
    }

    [Fact]
    public void UnaAccionQueExigeMotivo_NoAdmiteSuAusencia()
    {
        var accion = Accion(motivos: ["correccion"], exigeMotivo: true);

        Assert.False(accion.AdmiteMotivo(null));
        Assert.True(accion.AdmiteMotivo("correccion"));
    }

    [Fact]
    public void LasColeccionesDeLaAccion_NoSePuedenAlterarDesdeAfuera()
    {
        var claves = new List<ClaveDeDetalle> { new("a", false) };
        var motivos = new List<string> { "uno" };
        var accion = new AccionDelCatalogo("Prueba", ClaseDeAccion.ConcedeOCambia, TipoDeRegistro.Auditoria, false, claves, false, false, motivos, false);

        claves.Add(new("b", true));
        motivos.Add("dos");

        Assert.Single(accion.Detalle);
        Assert.Single(accion.Motivos);
    }

    // ---------- El catálogo ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-01")]
    public void ElCatalogoBuscaPorIdentificador_ConservaElOrden_YNoAceptaDuplicados()
    {
        var catalogo = new CatalogoDeAcciones([Accion("CambioDePrecio"), Accion("AnulacionDePedido"), Accion("AccesoDenegado", ClaseDeAccion.NoConcede, TipoDeRegistro.Seguridad)]);

        Assert.Equal(["CambioDePrecio", "AnulacionDePedido", "AccesoDenegado"], catalogo.Acciones.Select(a => a.Identificador));
        Assert.True(catalogo.TryObtener("AnulacionDePedido", out var anulacion));
        Assert.Equal("AnulacionDePedido", anulacion!.Identificador);
        Assert.False(catalogo.TryObtener("Inexistente", out _));
        Assert.False(catalogo.TryObtener("anulaciondepedido", out _));
        Assert.Throws<ArgumentException>(() => new CatalogoDeAcciones([Accion("CambioDePrecio"), Accion("CambioDePrecio")]));
    }

    [Fact]
    public void ObtenerUnaAccionInexistente_Lanza()
    {
        var catalogo = new CatalogoDeAcciones([Accion()]);

        Assert.Throws<KeyNotFoundException>(() => catalogo.Obtener("NoExiste"));
        Assert.Equal("CambioDePrecio", catalogo.Obtener("CambioDePrecio").Identificador);
    }

    [Fact]
    public void ElCatalogoVacioEsValido_PeroNoAdmiteNulos()
    {
        Assert.Empty(new CatalogoDeAcciones([]).Acciones);
        Assert.Throws<ArgumentNullException>(() => new CatalogoDeAcciones(null!));
    }

    [Fact]
    public void ElCatalogoDeclaraQueAccionesVanAlAlmacenAislado()
    {
        var catalogo = new CatalogoDeAcciones([Accion("IngresoFallido", ClaseDeAccion.NoConcede, TipoDeRegistro.Seguridad, aislada: true), Accion("CambioDePrecio")]);

        Assert.Equal(["IngresoFallido"], catalogo.Acciones.Where(a => a.Aislada).Select(a => a.Identificador));
    }

    // ---------- PlantillaDeRuta ----------

    private static readonly string[] Plantillas =
    [
        "/api/ecommerce/v1/pedidos",
        "/api/ecommerce/v1/pedidos/{id}",
        "/api/ecommerce/v1/pedidos/{id}/anulacion",
        "/api/ecommerce/v1/pedidos/resumen",
        "/api/inventario/v1/auditoria",
    ];

    [Theory]
    [InlineData("/api/ecommerce/v1/pedidos", "/api/ecommerce/v1/pedidos")]
    [InlineData("/api/ecommerce/v1/pedidos/", "/api/ecommerce/v1/pedidos")]
    [InlineData("/api/ecommerce/v1/pedidos/3f2a9c1e-0000-4000-8000-000000000001", "/api/ecommerce/v1/pedidos/{id}")]
    [InlineData("/api/ecommerce/v1/pedidos/3f2a9c1e/anulacion", "/api/ecommerce/v1/pedidos/{id}/anulacion")]
    [InlineData("/API/Ecommerce/V1/Pedidos/42", "/api/ecommerce/v1/pedidos/{id}")]
    [InlineData("/api/ecommerce/v1/pedidos/resumen", "/api/ecommerce/v1/pedidos/resumen")]
    [InlineData("/api/inventario/v1/auditoria", "/api/inventario/v1/auditoria")]
    [Trait("Criterio", "AC-AUD-05")]
    public void LaRutaCrudaSeReemplazaPorSuPlantilla_ConLaMasEspecificaPrimero(string cruda, string esperada) =>
        Assert.Equal(esperada, PlantillaDeRuta.Resolver(cruda, Plantillas));

    [Theory]
    [InlineData("/api/ecommerce/v1/pedidos?clave=secreto&token=abc", "/api/ecommerce/v1/pedidos")]
    [InlineData("/api/ecommerce/v1/pedidos/42?x=1#fragmento", "/api/ecommerce/v1/pedidos/{id}")]
    [InlineData("/api/ecommerce/v1/pedidos;jsessionid=ABC123", "/api/ecommerce/v1/pedidos")]
    [InlineData("/api/ecommerce/v1/pedidos/42;v=1/anulacion", "/api/ecommerce/v1/pedidos/{id}/anulacion")]
    [Trait("Criterio", "AC-AUD-05")]
    public void LosParametrosDeConsultaLosParametrosDeRutaYElFragmentoNuncaSobreviven(string cruda, string esperada)
    {
        var plantilla = PlantillaDeRuta.Resolver(cruda, Plantillas);

        Assert.Equal(esperada, plantilla);
        Assert.DoesNotContain('?', plantilla);
        Assert.DoesNotContain(';', plantilla);
        Assert.DoesNotContain('#', plantilla);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/api/otra/cosa")]
    [InlineData("/api/ecommerce/v1/pedidos/42/otra/mas/segmentos")]
    [InlineData("api/ecommerce/v1/pedidos")]
    [InlineData("https://externo.ejemplo/api/ecommerce/v1/pedidos")]
    [InlineData("/api/ecommerce/v1/pedidos/42/%2e%2e/secreto")]
    [Trait("Criterio", "AC-AUD-05")]
    public void SiNingunaPlantillaCoincide_LaRutaEsSinCoincidencia_NuncaLaCruda(string? cruda)
    {
        var resultado = PlantillaDeRuta.Resolver(cruda, Plantillas);

        Assert.Equal(PlantillaDeRuta.SinCoincidencia, resultado);
        Assert.Equal("sin-coincidencia", resultado);
    }

    [Fact]
    public void UnaRutaMasLargaQueElTope_EsSinCoincidencia_AunqueSeaDeUnaPlantilla()
    {
        var larga = "/api/ecommerce/v1/pedidos/" + new string('7', PlantillaDeRuta.MaximoDeCaracteres);

        Assert.Equal(PlantillaDeRuta.SinCoincidencia, PlantillaDeRuta.Resolver(larga, Plantillas));
        Assert.Equal("/api/ecommerce/v1/pedidos/{id}", PlantillaDeRuta.Resolver("/api/ecommerce/v1/pedidos/" + new string('7', 20), Plantillas));
    }

    [Theory]
    [InlineData("/api/v1/pedidos/{id}", true)]
    [InlineData("/", true)]
    [InlineData("/api/v1/{a}/{b}", true)]
    [InlineData("", false)]
    [InlineData("api/v1", false)]
    [InlineData("/api/v1/pedidos?x=1", false)]
    [InlineData("/api/v1/pedidos;x", false)]
    [InlineData("/api/v1/pedidos#f", false)]
    [InlineData("/api//pedidos", false)]
    [InlineData("/api/v1/ped{id}", false)]
    [InlineData("/api/v1/{id", false)]
    [InlineData("/api/v1/%41", false)]
    [InlineData("/api/v1/espacio aqui", false)]
    public void UnaPlantillaSoloEsValidaSinParametrosPuntoYComaNiFragmento(string plantilla, bool valida) =>
        Assert.Equal(valida, PlantillaDeRuta.EsPlantillaValida(plantilla));

    [Fact]
    public void UnaPlantillaInvalidaEnLaListaSeRechazaAlResolver() =>
        Assert.Throws<ArgumentException>(() => PlantillaDeRuta.Resolver("/api/v1/x", ["/api/v1/x?y=1"]));
}
