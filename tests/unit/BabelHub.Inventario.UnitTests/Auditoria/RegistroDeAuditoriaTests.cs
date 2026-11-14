using BabelHub.Inventario.Domain.Auditoria;

namespace BabelHub.Inventario.UnitTests.Auditoria;

/// <summary>
/// <c>Actor</c> y <c>RegistroDeAuditoria</c> (AUD-01, AUD-23, AUD-25): el usuario con su rol, «sistema», «integración» y «anónimo»; fechas en UTC y
/// presentación en hora de Lima; el registro se crea solo coherente con su acción del catálogo.
/// </summary>
public sealed class RegistroDeAuditoriaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    private static readonly AccionDelCatalogo CambioDePrecio = new(
        "CambioDePrecio",
        ClaseDeAccion.ConcedeOCambia,
        TipoDeRegistro.Auditoria,
        false,
        [new("precioAnterior", false), new("precioNuevo", false), new("nombre", true)],
        false,
        false,
        [],
        false);

    private static readonly AccionDelCatalogo AccesoDenegado = new(
        "AccesoDenegado",
        ClaseDeAccion.NoConcede,
        TipoDeRegistro.Seguridad,
        false,
        [],
        false,
        false,
        ["fuera-de-sucursal", "sin-sesion"],
        true);

    private static readonly AccionDelCatalogo CambioDeRol = new(
        "CambioDeRol",
        ClaseDeAccion.ConcedeOCambia,
        TipoDeRegistro.Ambos,
        true,
        [new("rolNuevo", false)],
        true,
        false,
        ["solicitud"],
        true);

    private static DetalleSeguro Detalle(AccionDelCatalogo accion, params (string Clave, string? Valor)[] valores) =>
        DetalleSeguro.Construir(accion.Detalle, valores.ToDictionary(v => v.Clave, v => v.Valor));

    private static RegistroDeAuditoria Crear(
        AccionDelCatalogo? accion = null,
        Actor? actor = null,
        DateTimeOffset? instante = null,
        string? motivo = null,
        string? origen = null,
        RutaResuelta? ruta = null,
        string? objetoTipo = "edicion",
        string? objetoId = "ed-001",
        string? sucursalOrigen = "suc-lima",
        string? sucursalDestino = null,
        ResultadoDeOperacion resultado = ResultadoDeOperacion.Exitoso,
        DetalleSeguro? detalle = null,
        Guid? id = null)
    {
        accion ??= CambioDePrecio;
        return RegistroDeAuditoria.Crear(
            id ?? Guid.NewGuid(),
            ServicioDeOrigen.Ecommerce,
            accion,
            actor ?? Actor.Usuario(Guid.NewGuid(), "administrador-central"),
            instante ?? Ahora,
            objetoTipo,
            objetoId,
            sucursalOrigen,
            sucursalDestino,
            motivo,
            origen,
            ruta,
            detalle ?? Detalle(accion),
            resultado,
            correlacion: null);
    }

    // ---------- Actor ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-18")]
    public void LosCuatroTiposDeActor_QuedanDistinguidos_ElUsuarioConSuRol()
    {
        var id = Guid.NewGuid();
        var usuario = Actor.Usuario(id, "cliente");

        Assert.Equal(TipoDeActor.Usuario, usuario.Tipo);
        Assert.Equal(id, usuario.Id);
        Assert.Equal("cliente", usuario.Rol);
        Assert.Equal(TipoDeActor.Sistema, Actor.Sistema().Tipo);
        Assert.Equal(TipoDeActor.Integracion, Actor.Integracion().Tipo);
        Assert.Equal(TipoDeActor.Anonimo, Actor.Anonimo().Tipo);
        foreach (var actor in new[] { Actor.Sistema(), Actor.Integracion(), Actor.Anonimo() })
        {
            Assert.Null(actor.Id);
            Assert.Null(actor.Rol);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("Administrador")]
    [InlineData("con espacio")]
    [InlineData("x")]
    public void UnUsuarioSinRolValido_SeRechaza(string rol) =>
        Assert.Throws<ArgumentException>(() => Actor.Usuario(Guid.NewGuid(), rol));

    [Fact]
    public void UnUsuarioSinIdentificador_SeRechaza() =>
        Assert.Throws<ArgumentException>(() => Actor.Usuario(Guid.Empty, "cliente"));

    [Fact]
    public void SistemaIntegracionYAnonimo_SonValoresIguales_Entre_Si()
    {
        Assert.Equal(Actor.Sistema(), Actor.Sistema());
        Assert.NotEqual(Actor.Sistema(), Actor.Integracion());
        Assert.NotEqual(Actor.Anonimo(), Actor.Integracion());
    }

    [Theory]
    [InlineData(TipoDeActor.Sistema, "sistema")]
    [InlineData(TipoDeActor.Integracion, "integracion")]
    [InlineData(TipoDeActor.Anonimo, "anonimo")]
    [InlineData(TipoDeActor.Usuario, "usuario")]
    [Trait("Criterio", "AC-AUD-18")]
    public void CadaTipoDeActorTieneUnNombreEstable(TipoDeActor tipo, string nombre) =>
        Assert.Equal(nombre, tipo.Nombre());

    // ---------- Registro ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-01")]
    public void ElRegistroConservaLoQueExigeAUD01_ElPrecioAnteriorYElNuevoIncluidos()
    {
        var actor = Actor.Usuario(Guid.NewGuid(), "administrador-central");
        var detalle = Detalle(CambioDePrecio, ("precioAnterior", "10.00"), ("precioNuevo", "12.50"));

        var registro = Crear(actor: actor, detalle: detalle, ruta: null, sucursalOrigen: "suc-cusco", sucursalDestino: "suc-ayacucho");

        Assert.Equal(ServicioDeOrigen.Ecommerce, registro.Servicio);
        Assert.Equal(TipoDeRegistro.Auditoria, registro.Tipo);
        Assert.Equal(actor, registro.Actor);
        Assert.Equal(Ahora, registro.InstanteUtc);
        Assert.Equal("CambioDePrecio", registro.Accion);
        Assert.Equal("edicion", registro.ObjetoTipo);
        Assert.Equal("ed-001", registro.ObjetoId);
        Assert.Equal("suc-cusco", registro.SucursalOrigen);
        Assert.Equal("suc-ayacucho", registro.SucursalDestino);
        Assert.Equal(ResultadoDeOperacion.Exitoso, registro.Resultado);
        Assert.Equal("10.00", registro.Detalle.Valores["precioAnterior"]);
        Assert.Equal("12.50", registro.Detalle.Valores["precioNuevo"]);
        Assert.False(registro.Truncado);
        Assert.Null(registro.AnonimizadoEn);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-18")]
    public void LaFechaDelRegistroDebeEstarEnUtc_UnaConOtroDesfaseSeRechaza()
    {
        Assert.Throws<ArgumentException>(() => Crear(instante: new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(-5))));
        Assert.Equal(Ahora, Crear(instante: Ahora).InstanteUtc);
        Assert.Equal(TimeSpan.Zero, Crear(instante: Ahora).InstanteUtc.Offset);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-22")]
    public void UnRegistroDeLas1500Utc_SeMuestraALas1000HoraDeLima()
    {
        var registro = Crear(instante: new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero));

        var lima = registro.InstanteEnLima;

        Assert.Equal(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(-5)), lima);
        Assert.Equal(10, lima.Hour);
        Assert.Equal("2026-11-14 10:00:00", HoraDeLima.Formatear(registro.InstanteUtc));
        Assert.Equal(registro.InstanteUtc, lima.ToUniversalTime());
    }

    [Theory]
    [InlineData("2026-01-01T03:00:00Z", "2025-12-31 22:00:00")]
    [InlineData("2026-07-15T05:00:00Z", "2026-07-15 00:00:00")]
    [InlineData("2026-07-15T04:59:59Z", "2026-07-14 23:59:59")]
    [Trait("Criterio", "AC-AUD-22")]
    public void LaHoraDeLimaNoTieneHorarioDeVerano_YCambiaElDiaDeCalendario(string utc, string esperada) =>
        Assert.Equal(esperada, HoraDeLima.Formatear(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void LaConversionAHoraDeLimaExigeUtc() =>
        Assert.Throws<ArgumentException>(() => HoraDeLima.Convertir(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(-5))));

    [Fact]
    public void ElIdentificadorDelRegistroNoPuedeSerVacio() =>
        Assert.Throws<ArgumentException>(() => Crear(id: Guid.Empty));

    [Fact]
    public void UnEventoDeSeguridadExigeMotivoDelCatalogoYOrigen()
    {
        var detalle = Detalle(AccesoDenegado);
        var anonimo = Actor.Anonimo();

        var valido = Crear(AccesoDenegado, anonimo, motivo: "sin-sesion", origen: "huella-abc123", ruta: RutaResuelta.SinCoincidencia, detalle: detalle, objetoTipo: null, objetoId: null, sucursalOrigen: null);

        Assert.Equal(TipoDeRegistro.Seguridad, valido.Tipo);
        Assert.Equal("sin-sesion", valido.Motivo);
        Assert.Equal("huella-abc123", valido.Origen);
        Assert.Throws<ArgumentException>(() => Crear(AccesoDenegado, anonimo, motivo: null, origen: "huella", detalle: detalle));
        Assert.Throws<ArgumentException>(() => Crear(AccesoDenegado, anonimo, motivo: "texto libre de la solicitud", origen: "huella", detalle: detalle));
        Assert.Throws<ArgumentException>(() => Crear(AccesoDenegado, anonimo, motivo: "sin-sesion", origen: null, detalle: detalle));
        Assert.Throws<ArgumentException>(() => Crear(AccesoDenegado, anonimo, motivo: "sin-sesion", origen: "  ", detalle: detalle));
    }

    [Fact]
    public void UnaAccionDeAuditoriaSinMotivosNoAdmiteMotivo()
    {
        Assert.Throws<ArgumentException>(() => Crear(motivo: "algo"));
        Assert.Null(Crear(motivo: null).Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-18")]
    public void UnHechoQueEsOperacionYEventoDeSeguridad_EsUnSoloRegistroConElTipoAmbos()
    {
        var registro = Crear(CambioDeRol, motivo: "solicitud", origen: "huella-xyz", detalle: Detalle(CambioDeRol, ("rolNuevo", "encargado")));

        Assert.Equal(TipoDeRegistro.Ambos, registro.Tipo);
        Assert.True(registro.Aislada);
    }

    private static readonly string[] Plantillas = ["/api/ecommerce/v1/pedidos/{id}", "/api/ecommerce/v1/pedidos"];

    [Theory]
    [InlineData("/api/ecommerce/v1/pedidos/42?token=secreto", "/api/ecommerce/v1/pedidos/{id}")]
    [InlineData("/api/ecommerce/v1/pedidos;jsessionid=x", "/api/ecommerce/v1/pedidos")]
    [InlineData("/descargas/AbCdEf0123456789AbCdEf0123456789", "sin-coincidencia")]
    [InlineData("https://externo/x", "sin-coincidencia")]
    [InlineData("", "sin-coincidencia")]
    public void LaRutaGuardadaEsSiempreUnaPlantillaOLaConstante_PorqueSoloSeObtieneResolviendo(string cruda, string esperada) =>
        Assert.Equal(esperada, Crear(ruta: PlantillaDeRuta.ResolverComoRuta(cruda, Plantillas)).Ruta);

    [Fact]
    public void SinRuta_ElRegistroNoLaTiene_YLaConstanteSePuedeUsarDirectamente()
    {
        Assert.Null(Crear(ruta: null).Ruta);
        Assert.Equal("sin-coincidencia", Crear(ruta: RutaResuelta.SinCoincidencia).Ruta);
    }

    [Theory]
    [InlineData("objetoId", "con espacio")]
    [InlineData("objetoId", "ok\n")]
    [InlineData("objetoId", "eyJhbGciOiJIUzI1NiJ9.token@correo.pe")]
    [InlineData("objetoTipo", "Mayuscula")]
    [InlineData("objetoTipo", "tipo\n")]
    [InlineData("sucursalOrigen", "suc lima")]
    [InlineData("sucursalOrigen", "suc-lima\n")]
    [InlineData("sucursalDestino", "https://x")]
    [InlineData("correlacion", "corta")]
    [InlineData("correlacion", "con espacio aqui")]
    [InlineData("origen", "persona@ejemplo.pe")]
    [InlineData("origen", "200.37.12.4\n")]
    [InlineData("origen", "x y")]
    public void LosCamposDeTextoLibreTienenSuFormato_UnValorHostilNoLlegaAlRegistro(string campo, string valor)
    {
        var detalle = Detalle(AccesoDenegado);

        Assert.Throws<ArgumentException>(() => RegistroDeAuditoria.Crear(
            Guid.NewGuid(),
            ServicioDeOrigen.Ecommerce,
            AccesoDenegado,
            Actor.Anonimo(),
            Ahora,
            campo == "objetoTipo" ? valor : "edicion",
            campo == "objetoId" ? valor : "ed-1",
            campo == "sucursalOrigen" ? valor : "suc-lima",
            campo == "sucursalDestino" ? valor : null,
            "sin-sesion",
            campo == "origen" ? valor : "200.37.12.4",
            null,
            detalle,
            ResultadoDeOperacion.Rechazado,
            campo == "correlacion" ? valor : null));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public void UnDetalleConLaMarcaDePersonalDistintaDeLaDeLaAccion_SeRechaza_PorqueQuedariaSinAnonimizar()
    {
        // La acción declara «nombre» como personal; este detalle lo construyó otra lista que no lo marca.
        var sinMarca = DetalleSeguro.Construir([new("nombre", false), new("precioNuevo", false)], new Dictionary<string, string?> { ["nombre"] = "Ana Pérez" });

        Assert.Throws<ArgumentException>(() => Crear(detalle: sinMarca));
    }

    [Fact]
    public void ElServicioYElResultadoDebenSerValoresDefinidos()
    {
        Assert.Throws<ArgumentException>(() => Crear(resultado: (ResultadoDeOperacion)99));
        Assert.Throws<ArgumentException>(() => RegistroDeAuditoria.Crear(Guid.NewGuid(), (ServicioDeOrigen)7, CambioDePrecio, Actor.Sistema(), Ahora, null, null, null, null, null, null, null, Detalle(CambioDePrecio), ResultadoDeOperacion.Exitoso, null));
    }

    [Fact]
    public void ElDetalleDebeVenirDeLaListaDeClavesDeLaAccion()
    {
        var detalleAjeno = DetalleSeguro.Construir([new("otraClave", false)], new Dictionary<string, string?> { ["otraClave"] = "x" });

        Assert.Throws<ArgumentException>(() => Crear(detalle: detalleAjeno));
    }

    [Fact]
    public void ElDetalleDeLaAccionConClavePersonal_QuedaMarcado()
    {
        var registro = Crear(detalle: Detalle(CambioDePrecio, ("nombre", "Ana Pérez"), ("precioNuevo", "5.00")));

        Assert.Equal(["nombre"], registro.Detalle.ClavesPersonales);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-20")]
    public void UnDetalleTruncadoMarcaElRegistro_ConservandoLaAccionYElResultado()
    {
        var largo = Detalle(CambioDePrecio, ("precioAnterior", new string('x', 1500)));

        var registro = Crear(detalle: largo, resultado: ResultadoDeOperacion.Rechazado);

        Assert.True(registro.Truncado);
        Assert.Equal("CambioDePrecio", registro.Accion);
        Assert.Equal(ResultadoDeOperacion.Rechazado, registro.Resultado);
    }

    [Fact]
    public void ElObjetoSinTipoNoPuedeTenerIdentificador()
    {
        Assert.Throws<ArgumentException>(() => Crear(objetoTipo: null, objetoId: "ed-001"));
        Assert.Null(Crear(objetoTipo: "edicion", objetoId: null).ObjetoId);
    }

    [Theory]
    [InlineData(65)]
    [InlineData(200)]
    public void LosCamposDeTextoTienenTope(int largo)
    {
        var largoDeMas = new string('a', largo);

        Assert.Throws<ArgumentException>(() => Crear(objetoId: largoDeMas));
        Assert.Throws<ArgumentException>(() => Crear(sucursalOrigen: largoDeMas));
        Assert.Throws<ArgumentException>(() => Crear(sucursalDestino: largoDeMas));
        Assert.Throws<ArgumentException>(() => Crear(objetoTipo: largoDeMas));
    }

    [Fact]
    public void LaPresentacionEnLimaNoCambiaElInstanteGuardado()
    {
        var registro = Crear();

        _ = registro.InstanteEnLima;

        Assert.Equal(Ahora, registro.InstanteUtc);
        Assert.Equal(TimeSpan.Zero, registro.InstanteUtc.Offset);
    }

    [Fact]
    public void ElRegistroEsIgualSoloPorIdentificador()
    {
        var id = Guid.NewGuid();

        Assert.Equal(Crear(id: id), Crear(id: id, resultado: ResultadoDeOperacion.Fallido));
        Assert.NotEqual(Crear(), Crear());
    }
}
