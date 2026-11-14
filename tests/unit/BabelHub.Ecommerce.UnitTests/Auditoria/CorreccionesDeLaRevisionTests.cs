using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;
using TipoDeActor = BabelHub.Ecommerce.Domain.Auditoria.TipoDeActor;

namespace BabelHub.Ecommerce.UnitTests.Auditoria;

/// <summary>
/// Lo que dejó la revisión de seguridad y de arquitectura del bloque AUD-2: el evento de un rechazo no depende de que el cliente siga conectado, una
/// plantilla inválida no silencia los accesos denegados, el Inventario no acepta acciones de Identidad, los objetos y los valores del detalle tienen
/// su dominio y el modelo del contrato valida sus formatos.
/// </summary>
public sealed class CorreccionesDeLaRevisionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    private static readonly CatalogoDeAcciones Catalogo = CatalogoDeAccionesDeAuditoria.Crear();

    private static ConstructorDeRegistros Constructor(ActorDePrueba? actor = null, ServicioDeOrigen servicio = ServicioDeOrigen.Ecommerce) =>
        new(servicio, Catalogo, new RelojFijo(Ahora), actor ?? ActorDePrueba.DeAdministradorCentral(), new OrigenFijo(OrigenDeSolicitud.Desconocido));

    private static SolicitudDeRegistro Rechazo() =>
        new(AccionesAuditables.IngresoFallido) { Motivo = "credenciales-invalidas", Resultado = ResultadoAuditado.Rechazado };

    // ---------- Cancelación de la solicitud ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-10")]
    public async Task Un_evento_de_seguridad_no_recibe_el_token_de_la_solicitud_y_se_escribe_aunque_el_cliente_la_cancele()
    {
        var almacen = new AlmacenDeRegistrosEnMemoria();
        var alertas = new AlertasTecnicasEspia();
        var seguridad = new RegistroDeSeguridadDeAplicacion(Constructor(ActorDePrueba.Anonimo()), almacen, alertas, new RelojFijo(Ahora));
        using var cancelada = new CancellationTokenSource();
        await cancelada.CancelAsync();

        await seguridad.Registrar(Rechazo(), cancelada.Token);

        Assert.Single(almacen.Registros);
        Assert.False(almacen.TokenRecibido.IsCancellationRequested);
        Assert.Empty(alertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-10")]
    public async Task Un_acceso_denegado_tampoco_depende_de_que_el_cliente_siga_conectado()
    {
        var escritor = new EscritorConToken();
        var denegacion = new DenegacionDeAccesoDeAplicacion(
            Constructor(ActorDePrueba.Anonimo()),
            new ContextoDeDenegacion(),
            new ResolutorDeRutas(new PlantillasDePrueba("/api/x/{id}")),
            escritor,
            new AlertasTecnicasEspia(),
            new RelojFijo(Ahora));
        using var cancelada = new CancellationTokenSource();
        await cancelada.CancelAsync();

        await denegacion.Registrar(new DenegacionDeAcceso("sin-sesion", "/api/x/1"), cancelada.Token);

        Assert.Equal(1, escritor.Escrituras);
        Assert.False(escritor.TokenRecibido.IsCancellationRequested);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-03")]
    public async Task Una_cancelacion_por_plazo_interno_del_almacen_es_auditoria_no_disponible_y_no_una_cancelacion_del_cliente()
    {
        var almacen = new AlmacenDeRegistrosEnMemoria { Falla = new OperationCanceledException() };
        var auditoria = new AuditoriaDeAplicacion(Constructor(), almacen);

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(
            () => auditoria.Registrar(new SolicitudDeRegistro(AccionesAuditables.RutaRegistrada) { ObjetoTipo = "ruta" }));
    }

    // ---------- Plantillas de ruta ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-04")]
    public async Task Una_plantilla_invalida_no_silencia_los_accesos_denegados()
    {
        var escritor = new EscritorConToken();
        var denegacion = new DenegacionDeAccesoDeAplicacion(
            Constructor(ActorDePrueba.Anonimo()),
            new ContextoDeDenegacion(),
            new ResolutorDeRutas(new PlantillasDePrueba("/api/x/{id:guid}", "/api/y/{id}", "/api/z/{**resto}")),
            escritor,
            new AlertasTecnicasEspia(),
            new RelojFijo(Ahora));

        await denegacion.Registrar(new DenegacionDeAcceso("sin-sesion", "/api/y/7"));

        Assert.Equal(1, escritor.Escrituras);
        Assert.Equal("/api/y/{id}", escritor.Ultimo!.Ruta);
    }

    [Fact]
    public void Una_ruta_larga_o_vacia_no_toca_la_lista_de_plantillas()
    {
        var resolutor = new ResolutorDeRutas(new PlantillasDePrueba("/api/y/{id}"));

        Assert.Equal(PlantillaDeRuta.SinCoincidencia, resolutor.Resolver("/" + new string('a', 500)).Valor);
        Assert.Equal(PlantillaDeRuta.SinCoincidencia, resolutor.Resolver("").Valor);
        Assert.Equal(PlantillaDeRuta.SinCoincidencia, resolutor.Resolver(null).Valor);
        Assert.Equal("/api/y/{id}", resolutor.Resolver("/api/y/9?x=1").Valor);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-04")]
    public async Task El_codigo_del_error_figura_en_el_detalle_y_un_texto_que_no_es_un_codigo_se_rechaza()
    {
        var escritor = new EscritorConToken();
        var denegacion = new DenegacionDeAccesoDeAplicacion(Constructor(ActorDePrueba.DeCliente()), new ContextoDeDenegacion(), new ResolutorDeRutas(new PlantillasDePrueba()), escritor, new AlertasTecnicasEspia(), new RelojFijo(Ahora));

        await denegacion.Registrar(new DenegacionDeAcceso("recurso-ajeno", "/api/x") { CodigoDeError = "ECO-PROHIBIDO" });

        Assert.Equal("ECO-PROHIBIDO", escritor.Ultimo!.Detalle.Valores["codigoDeError"]);
        Assert.Throws<ArgumentException>(() => new DenegacionDeAcceso("recurso-ajeno", "/api/x") { CodigoDeError = "el usuario ana@correo.pe no puede" });
        Assert.Throws<ArgumentException>(() => new DenegacionDeAcceso("recurso-ajeno", "/api/x") { CodigoDeError = "eco-prohibido" });
    }

    // ---------- Servicio y almacén aislado ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-11")]
    public async Task El_Inventario_rechaza_una_accion_de_Identidad_y_el_ECommerce_la_acepta()
    {
        var almacen = new AlmacenDeRegistrosEnMemoria();
        var solicitud = new SolicitudDeRegistro(AccionesAuditables.UsuarioDadoDeAlta) { ObjetoTipo = "usuario", ObjetoId = Guid.NewGuid().ToString("D") };

        await Assert.ThrowsAsync<ArgumentException>(
            () => new AuditoriaDeAplicacion(Constructor(servicio: ServicioDeOrigen.Inventario), almacen).Registrar(solicitud));
        await new AuditoriaDeAplicacion(Constructor(servicio: ServicioDeOrigen.Ecommerce), almacen).Registrar(solicitud);

        Assert.Single(almacen.Registros);
    }

    // ---------- Objetos y valores del detalle ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public async Task Una_accion_no_acepta_un_tipo_de_objeto_que_no_declara()
    {
        var almacen = new AlmacenDeRegistrosEnMemoria();
        var auditoria = new AuditoriaDeAplicacion(Constructor(), almacen);

        // El identificador de un cliente como objeto de una acción cuyo objeto no es personal quedaría fuera de la anonimización.
        await Assert.ThrowsAsync<ArgumentException>(
            () => auditoria.Registrar(new SolicitudDeRegistro(AccionesAuditables.ReservaConfirmada) { ObjetoTipo = "cuenta", ObjetoId = Guid.NewGuid().ToString("D") }));
        await auditoria.Registrar(new SolicitudDeRegistro(AccionesAuditables.ReservaConfirmada) { ObjetoTipo = "pedido", ObjetoId = "pd-1" });
        await auditoria.Registrar(new SolicitudDeRegistro(AccionesAuditables.ReservaConfirmada));

        Assert.Equal(2, almacen.Registros.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public void Toda_accion_declara_sus_tipos_de_objeto_y_los_que_designan_personas_exigen_objeto_personal()
    {
        foreach (var accion in Catalogo.Acciones)
        {
            Assert.NotNull(accion.TiposDeObjeto);
            var esDePersonas = accion.TiposDeObjeto!.Any(t => CatalogoDeAccionesDeAuditoria.TiposDeObjetoPersonales.Contains(t));
            Assert.True(!esDePersonas || accion.ObjetoPersonal, $"{accion.Identificador} admite un objeto que designa a una persona y no lo declara personal.");
            Assert.True(!accion.ObjetoPersonal || accion.TiposDeObjeto.Count > 0, $"{accion.Identificador} declara objeto personal pero no admite objeto.");
        }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-05")]
    public async Task Un_texto_libre_en_una_clave_de_detalle_se_acorta_y_un_valor_fuera_del_conjunto_cerrado_se_reemplaza()
    {
        var almacen = new AlmacenDeRegistrosEnMemoria();
        var auditoria = new AuditoriaDeAplicacion(Constructor(), almacen);

        await auditoria.Registrar(new SolicitudDeRegistro(AccionesAuditables.ReembolsoRegistrado)
        {
            ObjetoTipo = "pedido",
            ObjetoId = "pd-1",
            Detalle = new Dictionary<string, string?>
            {
                ["monto"] = "59.90",
                ["moneda"] = "EUR",
                ["modalidad"] = "a pedido de Juan Perez, con DNI 12345678",
                ["medio"] = new string('x', 500),
            },
        });

        var detalle = Assert.Single(almacen.Registros).Detalle.Valores;
        Assert.Equal(ClaveDeDetalle.ValorNoPermitido, detalle["moneda"]);
        Assert.Equal(ClaveDeDetalle.ValorNoPermitido, detalle["modalidad"]);
        Assert.True(detalle["medio"].Length <= 32);
        Assert.EndsWith("…", detalle["medio"], StringComparison.Ordinal);
    }

    [Fact]
    public void Una_clave_de_detalle_con_conjunto_cerrado_y_un_tope_de_largo_se_comporta_como_se_declara()
    {
        var clave = new ClaveDeDetalle("estado", false) { Valores = ["a", "b"] };
        var corta = new ClaveDeDetalle("texto", false) { LargoMaximo = 5 };

        var detalle = DetalleSeguro.Construir([clave, corta], new Dictionary<string, string?> { ["estado"] = "a", ["texto"] = "abcdefghij" });

        Assert.Equal("a", detalle.Valores["estado"]);
        Assert.Equal("abcd…", detalle.Valores["texto"]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClaveDeDetalle("x", false) { LargoMaximo = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClaveDeDetalle("x", false) { LargoMaximo = DetalleSeguro.MaximoDeCaracteres + 1 });
    }

    [Fact]
    public void El_dominio_de_una_accion_rechaza_un_tipo_de_objeto_con_forma_de_texto_libre()
    {
        Assert.Throws<ArgumentException>(() => new AccionDelCatalogo("Prueba", ClaseDeAccion.ConcedeOCambia, TipoDeRegistro.Auditoria, false, [], false, false, [], false) { TiposDeObjeto = ["Cuenta de Ana"] });
    }

    // ---------- Modelo del contrato ----------

    [Theory]
    [InlineData("Texto libre del usuario")]
    [InlineData("ana@correo.pe")]
    public void El_modelo_del_contrato_rechaza_un_motivo_que_no_es_un_codigo(string motivo)
    {
        var registro = new RegistroAuditado(Guid.NewGuid(), ServicioAuditado.Ecommerce, TipoDeRegistroAuditado.Seguridad, BabelHub.Ecommerce.Application.Plataforma.TipoDeActor.Anonimo, null, null, Ahora, "IngresoFallido", ResultadoAuditado.Rechazado);

        Assert.Throws<ArgumentException>(() => registro with { Motivo = motivo });
    }

    [Fact]
    public void El_modelo_del_contrato_valida_la_ruta_el_origen_y_la_accion()
    {
        var registro = new RegistroAuditado(Guid.NewGuid(), ServicioAuditado.Ecommerce, TipoDeRegistroAuditado.Seguridad, BabelHub.Ecommerce.Application.Plataforma.TipoDeActor.Anonimo, null, null, Ahora, "IngresoFallido", ResultadoAuditado.Rechazado);

        Assert.Throws<ArgumentException>(() => registro with { Ruta = "/api/x?token=abc" });
        Assert.Throws<ArgumentException>(() => registro with { Origen = "198.51.100.7, 10.0.0.1" });
        Assert.Throws<ArgumentException>(() => registro with { ObjetoId = new string('a', 65) });
        Assert.Throws<ArgumentException>(() => new RegistroAuditado(Guid.NewGuid(), ServicioAuditado.Ecommerce, TipoDeRegistroAuditado.Seguridad, BabelHub.Ecommerce.Application.Plataforma.TipoDeActor.Anonimo, null, null, Ahora, "ingreso fallido", ResultadoAuditado.Rechazado));
        Assert.Equal("sin-coincidencia", (registro with { Ruta = "sin-coincidencia" }).Ruta);
        Assert.Equal("/api/y/{id}", (registro with { Ruta = "/api/y/{id}" }).Ruta);
    }
    // ---------- Dobles ----------

    private sealed class PlantillasDePrueba(params string[] plantillas) : IPlantillasDeRuta
    {
        public IReadOnlyCollection<string> Todas { get; } = plantillas;
    }

    private sealed class EscritorConToken : IEscritorDeDenegaciones
    {
        public int Escrituras { get; private set; }

        public RegistroDeAuditoria? Ultimo { get; private set; }

        public CancellationToken TokenRecibido { get; private set; }

        public Task Escribir(RegistroDeAuditoria registro, CancellationToken cancelacion = default)
        {
            Escrituras++;
            Ultimo = registro;
            TokenRecibido = cancelacion;
            return Task.CompletedTask;
        }
    }
}
