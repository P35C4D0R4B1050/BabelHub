using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;
using TipoDeActor = BabelHub.Ecommerce.Domain.Auditoria.TipoDeActor;

namespace BabelHub.Ecommerce.UnitTests.Auditoria;

/// <summary>
/// Los puertos que <c>Auditoria</c> define y otros módulos implementan (T09, D09, D17): el almacén aislado de Identidad, su lectura y su conteo, su
/// depuración y las cuentas canceladas pendientes. Un registro de una acción «aislada» va al almacén de Identidad y nunca al de Auditoría.
/// </summary>
public sealed class PuertosDeOtrosModulosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    private static readonly AccionDelCatalogo IngresoFallido = new(
        "IngresoFallido",
        ClaseDeAccion.NoConcede,
        TipoDeRegistro.Seguridad,
        true,
        [new("cuenta", true)],
        false,
        false,
        ["credenciales-invalidas"],
        true);

    private static readonly AccionDelCatalogo CambioDePrecio = new(
        "CambioDePrecio",
        ClaseDeAccion.ConcedeOCambia,
        TipoDeRegistro.Auditoria,
        false,
        [new("precioNuevo", false)],
        false,
        false,
        [],
        false);

    private sealed class AlmacenAisladoEnMemoria : IAlmacenDeRegistrosAislados
    {
        public List<RegistroAuditado> Registros { get; } = [];

        public Exception? Falla { get; set; }

        public Task Insertar(RegistroAuditado registro, CancellationToken cancelacion = default)
        {
            if (Falla is not null)
            {
                return Task.FromException(Falla);
            }

            Registros.Add(registro);
            return Task.CompletedTask;
        }
    }

    private static RegistroDeAuditoria Crear(AccionDelCatalogo accion, Actor? actor = null, string? motivo = null, string? origen = null) =>
        RegistroDeAuditoria.Crear(
            Guid.NewGuid(),
            ServicioDeOrigen.Ecommerce,
            accion,
            actor ?? Actor.Usuario(Guid.NewGuid(), "cliente"),
            Ahora,
            "cuenta",
            "u-1",
            null,
            null,
            motivo,
            origen,
            PlantillaDeRuta.ResolverComoRuta("/api/x", ["/api/x"]),
            DetalleSeguro.Construir(accion.Detalle, accion.Detalle.ToDictionary(c => c.Nombre, c => (string?)"Ana Perez")),
            accion.Tipo == TipoDeRegistro.Auditoria ? ResultadoDeOperacion.Exitoso : ResultadoDeOperacion.Rechazado,
            "corr-12345678");

    [Fact]
    [Trait("Criterio", "AC-AUD-15")]
    public async Task Una_accion_aislada_va_al_almacen_de_Identidad_y_no_al_de_Auditoria()
    {
        var propio = new AlmacenDeRegistrosEnMemoria();
        var aislado = new AlmacenAisladoEnMemoria();
        var enrutador = new EnrutadorDeRegistros(propio, aislado);

        await enrutador.Insertar(Crear(IngresoFallido, motivo: "credenciales-invalidas", origen: "v1:huella"));

        Assert.Empty(propio.Registros);
        Assert.Equal("IngresoFallido", Assert.Single(aislado.Registros).Accion);
    }

    [Fact]
    public async Task Una_accion_no_aislada_va_al_almacen_de_Auditoria()
    {
        var propio = new AlmacenDeRegistrosEnMemoria();
        var aislado = new AlmacenAisladoEnMemoria();
        var enrutador = new EnrutadorDeRegistros(propio, aislado);

        await enrutador.Insertar(Crear(CambioDePrecio));

        Assert.Single(propio.Registros);
        Assert.Empty(aislado.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-10")]
    public async Task Si_el_almacen_aislado_falla_IRegistroDeSeguridad_deja_la_alerta_y_no_interrumpe()
    {
        var propio = new AlmacenDeRegistrosEnMemoria();
        var aislado = new AlmacenAisladoEnMemoria { Falla = new InvalidOperationException("identidad no responde") };
        var alertas = new AlertasTecnicasEspia();
        var reloj = new RelojFijo(Ahora);
        var constructor = new ConstructorDeRegistros(
            ServicioDeOrigen.Ecommerce,
            new CatalogoDeAcciones([IngresoFallido, CambioDePrecio]),
            reloj,
            ActorDePrueba.Anonimo(),
            new OrigenFijo(OrigenDeSolicitud.Desconocido));
        var seguridad = new RegistroDeSeguridadDeAplicacion(constructor, new EnrutadorDeRegistros(propio, aislado), alertas, reloj);

        await seguridad.Registrar(new SolicitudDeRegistro(new AccionAuditable("IngresoFallido", aislada: true)) { Motivo = "credenciales-invalidas", Resultado = ResultadoAuditado.Rechazado });

        Assert.Single(alertas.Alertas);
        Assert.Empty(propio.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-03")]
    public async Task Si_el_almacen_aislado_falla_IAuditoria_cancela_la_operacion()
    {
        var aislado = new AlmacenAisladoEnMemoria { Falla = new InvalidOperationException("identidad no responde") };
        var cambioAislado = new AccionDelCatalogo("CambioDeCorreo", ClaseDeAccion.ConcedeOCambia, TipoDeRegistro.Ambos, true, [], false, false, ["solicitud"], true);
        var constructor = new ConstructorDeRegistros(
            ServicioDeOrigen.Ecommerce,
            new CatalogoDeAcciones([cambioAislado]),
            new RelojFijo(Ahora),
            ActorDePrueba.DeCliente(),
            new OrigenFijo(OrigenDeSolicitud.Desconocido));
        var auditoria = new AuditoriaDeAplicacion(constructor, new EnrutadorDeRegistros(new AlmacenDeRegistrosEnMemoria(), aislado));

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(
            () => auditoria.Registrar(new SolicitudDeRegistro(new AccionAuditable("CambioDeCorreo", aislada: true)) { Motivo = "solicitud" }));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public void El_conteo_de_una_accion_solo_admite_una_ventana_de_hasta_5_minutos_en_UTC()
    {
        var accion = new AccionAuditable("IngresoFallido", aislada: true);

        var valida = new SolicitudDeConteo(accion, Ahora.AddMinutes(-5), Ahora);

        Assert.Equal(TimeSpan.FromMinutes(5), valida.Hasta - valida.Desde);
        Assert.Throws<ArgumentException>(() => new SolicitudDeConteo(accion, Ahora.AddMinutes(-6), Ahora));
        Assert.Throws<ArgumentException>(() => new SolicitudDeConteo(accion, Ahora, Ahora));
        Assert.Throws<ArgumentException>(() => new SolicitudDeConteo(accion, Ahora, Ahora.AddMinutes(-1)));
        Assert.Throws<ArgumentException>(() => new SolicitudDeConteo(accion, Ahora.AddMinutes(-1).ToOffset(TimeSpan.FromHours(-5)), Ahora));
        Assert.Throws<ArgumentException>(() => new SolicitudDeConteo(default, Ahora.AddMinutes(-1), Ahora));
    }

    [Fact]
    public void Un_destinatario_de_alerta_es_una_cuenta_y_nunca_una_direccion()
    {
        var cuenta = Guid.NewGuid();

        Assert.Equal(cuenta, new DestinatarioDeAlerta(cuenta).CuentaId);
        Assert.Throws<ArgumentException>(() => new DestinatarioDeAlerta(Guid.Empty));
        Assert.DoesNotContain(typeof(DestinatarioDeAlerta).GetProperties(), p => p.PropertyType == typeof(string));
    }
}
