using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;
using DominioTipo = BabelHub.Ecommerce.Domain.Identidad.TipoDeSolicitudDeDatos;
using TipoDelContrato = BabelHub.Ecommerce.Application.Identidad.TipoDeSolicitudDeDatos;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// Las solicitudes sobre datos personales (identidad-seguridad T21; AC-IDE-11, AC-IDE-23, AC-IDE-26, AC-IDE-29, AC-IDE-52; IDE-12, IDE-26, IDE-29, IDE-32, IDE-63): el cliente con el correo verificado las presenta con su plazo
/// en días hábiles, la administración las ve ordenadas y sin datos personales, abrir el detalle se audita o no se muestra y responder audita, deja la respuesta en la cuenta y avisa sin el contenido.
/// </summary>
public sealed class SolicitudesDeDatosCasosDeUsoTests
{
    private static (EntornoDeIdentidad E, CuentaDeIdentidad Cliente, AtencionDeSolicitudesDeDatos Solicitudes) Cliente()
    {
        var e = new EntornoDeIdentidad();
        var cliente = e.Cuenta("ana@example.com");
        e.ActuarComo(cliente);
        return (e, cliente, e.Solicitudes());
    }

    private static (EntornoDeIdentidad E, CuentaDeIdentidad Admin, AtencionDeSolicitudesDeDatos Solicitudes) Administrador()
    {
        var e = new EntornoDeIdentidad();
        var admin = e.ActuarComoAdministrador();
        return (e, admin, e.Solicitudes());
    }

    private static SolicitudDeDatos Sembrar(EntornoDeIdentidad e, Guid usuarioId, DominioTipo tipo, DateOnly limite, DateTimeOffset? recibida = null, EstadoDeSolicitudDeDatos estado = EstadoDeSolicitudDeDatos.Recibida)
    {
        var solicitud = SolicitudDeDatos.Restaurar(Guid.NewGuid(), usuarioId, tipo, estado, recibida ?? e.Reloj.Ahora, limite, null);
        e.Bd.Solicitudes.Add(solicitud);
        return solicitud;
    }

    // ---------------------------------------------------------------- presentar (AC-IDE-11, AC-IDE-26)

    [Fact]
    [Trait("Criterio", "AC-IDE-11")]
    public async Task Presentar_Acceso_QuedaRecibida_ConVeinteDiasHabilesDeLimite()
    {
        var (e, cliente, solicitudes) = Cliente();

        var resultado = await solicitudes.Presentar(cliente.Usuario.Id, TipoDelContrato.Acceso);

        Assert.True(resultado.EsExito);
        Assert.Equal("recibida", resultado.Valor.Estado);
        Assert.Equal(TipoDelContrato.Acceso, resultado.Valor.Tipo);
        // El lunes 16 de noviembre de 2026 más 20 días hábiles.
        Assert.Equal(new DateOnly(2026, 12, 14), resultado.Valor.FechaLimite);
        Assert.Equal(new DateOnly(2026, 11, 16), resultado.Valor.Recibida);
        Assert.False(resultado.Valor.Vencida);
        Assert.Single(e.Bd.Solicitudes);
    }

    [Theory]
    [InlineData(TipoDelContrato.Rectificacion)]
    [InlineData(TipoDelContrato.Cancelacion)]
    [InlineData(TipoDelContrato.Oposicion)]
    [Trait("Criterio", "AC-IDE-11")]
    public async Task Presentar_LosDemasTipos_TienenDiezDiasHabilesDeLimite(TipoDelContrato tipo)
    {
        var (_, cliente, solicitudes) = Cliente();

        var resultado = await solicitudes.Presentar(cliente.Usuario.Id, tipo);

        Assert.Equal(new DateOnly(2026, 11, 30), resultado.Valor.FechaLimite);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-11")]
    public async Task Presentar_UnViernesPorLaNoche_CuentaDesdeElDiaSiguienteHabil_YLaFechaEsLaDeLima()
    {
        var (e, cliente, solicitudes) = Cliente();
        // Viernes 20 de noviembre a las 23:30 en Lima = sábado 21 a las 04:30 UTC.
        e.Reloj.Ahora = new DateTimeOffset(2026, 11, 21, 4, 30, 0, TimeSpan.Zero);

        var resultado = await solicitudes.Presentar(cliente.Usuario.Id, TipoDelContrato.Rectificacion);

        Assert.Equal(new DateOnly(2026, 11, 20), resultado.Valor.Recibida);
        Assert.Equal(new DateOnly(2026, 12, 4), resultado.Valor.FechaLimite);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-26")]
    public async Task Presentar_ConElCorreoSinVerificar_SeRechaza_PeroLaCuentaSigueFuncionando()
    {
        var e = new EntornoDeIdentidad();
        var sinVerificar = e.Cuenta("sin@example.com", verificada: false);
        e.ActuarComo(sinVerificar);

        var solicitud = await e.Solicitudes().Presentar(sinVerificar.Usuario.Id, TipoDelContrato.Acceso);
        var cuenta = await e.Gestion().Consultar(sinVerificar.Usuario.Id);

        Assert.Equal("correo-sin-verificar", solicitud.Error!.Clave);
        Assert.Empty(e.Bd.Solicitudes);
        Assert.True(cuenta.EsExito);
    }

    [Fact]
    public async Task Presentar_ParaOtraPersona_EsProhibido_YUnTipoInvalidoSeRechaza()
    {
        var (e, cliente, solicitudes) = Cliente();
        e.Actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), "cliente");

        var ajena = await solicitudes.Presentar(cliente.Usuario.Id, TipoDelContrato.Acceso);
        e.ActuarComo(cliente);
        var invalido = await solicitudes.Presentar(cliente.Usuario.Id, (TipoDelContrato)99);

        Assert.Equal(AlcanceDeLaCuenta.Prohibido, ajena.Error);
        Assert.Equal(CategoriaDeError.Validacion, invalido.Error!.Categoria);
        Assert.Empty(e.Bd.Solicitudes);
    }

    [Fact]
    public async Task Presentar_TienePorTopeDiezSolicitudesAbiertas_YUnaCerradaLiberaElCupo()
    {
        var (e, cliente, solicitudes) = Cliente();
        for (var i = 0; i < AtencionDeSolicitudesDeDatos.MaximoDeAbiertasPorPersona; i++)
        {
            Assert.True((await solicitudes.Presentar(cliente.Usuario.Id, TipoDelContrato.Acceso)).EsExito);
        }

        var excedida = await solicitudes.Presentar(cliente.Usuario.Id, TipoDelContrato.Acceso);
        e.Bd.Solicitudes[0].Atender(e.Reloj.Ahora, "respuesta");
        var liberada = await solicitudes.Presentar(cliente.Usuario.Id, TipoDelContrato.Acceso);

        Assert.Equal("demasiadas-solicitudes-abiertas", excedida.Error!.Clave);
        Assert.True(liberada.EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-23")]
    public async Task ElCliente_VeSoloSusSolicitudes_ConLaRespuestaYElMotivo()
    {
        var (e, cliente, solicitudes) = Cliente();
        var atendida = Sembrar(e, cliente.Usuario.Id, DominioTipo.Acceso, new DateOnly(2026, 12, 14));
        atendida.Atender(e.Reloj.Ahora, "Le enviamos un resumen de sus datos.");
        var rechazada = Sembrar(e, cliente.Usuario.Id, DominioTipo.Oposicion, new DateOnly(2026, 11, 30));
        rechazada.Rechazar(e.Reloj.Ahora, "No se pudo verificar su identidad.");
        Sembrar(e, Guid.NewGuid(), DominioTipo.Acceso, new DateOnly(2026, 12, 14));

        var lista = (await solicitudes.ListarLasDelCliente(cliente.Usuario.Id)).Valor;

        Assert.Equal(2, lista.Count);
        Assert.Contains(lista, s => s.Solicitud.Estado == "atendida" && s.Respuesta == "Le enviamos un resumen de sus datos.");
        Assert.Contains(lista, s => s.Solicitud.Estado == "rechazada" && s.Motivo == "No se pudo verificar su identidad.");
    }

    // ---------------------------------------------------------------- lista de la administración (AC-IDE-29)

    [Fact]
    [Trait("Criterio", "AC-IDE-29")]
    public async Task LaLista_VaPorFechaLimite_YLaPrimeraSinAtenderPasadaDeSuLimiteFiguraComoVencida()
    {
        var (e, admin, solicitudes) = Administrador();
        var cliente = e.Cuenta("ana@example.com");
        var rectificacion = Sembrar(e, cliente.Usuario.Id, DominioTipo.Rectificacion, new DateOnly(2026, 11, 20));
        var acceso = Sembrar(e, cliente.Usuario.Id, DominioTipo.Acceso, new DateOnly(2026, 11, 13), recibida: e.Reloj.Ahora.AddDays(-30), estado: EstadoDeSolicitudDeDatos.EnAtencion);

        var lista = (await solicitudes.Listar(admin.Usuario.Id)).Valor;

        Assert.Equal([acceso.Id, rectificacion.Id], lista.Select(s => s.Id));
        Assert.True(lista[0].Vencida);
        Assert.False(lista[1].Vencida);
        Assert.Equal("en-atencion", lista[0].Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-29")]
    public async Task UnaSolicitudAtendidaORechazada_NoFiguraVencida()
    {
        var (e, admin, solicitudes) = Administrador();
        var cliente = e.Cuenta("ana@example.com");
        Sembrar(e, cliente.Usuario.Id, DominioTipo.Acceso, new DateOnly(2026, 10, 1), estado: EstadoDeSolicitudDeDatos.Recibida).Atender(e.Reloj.Ahora, "ok");
        Sembrar(e, cliente.Usuario.Id, DominioTipo.Acceso, new DateOnly(2026, 10, 1)).Rechazar(e.Reloj.Ahora, "no");

        var lista = (await solicitudes.Listar(admin.Usuario.Id)).Valor;

        Assert.All(lista, s => Assert.False(s.Vencida));
    }

    // ---------------------------------------------------------------- detalle auditado (AC-IDE-52)

    [Fact]
    [Trait("Criterio", "AC-IDE-52")]
    public async Task ElDetalle_SeAudita_ConLaSolicitud_SinDatosPersonalesDelCliente()
    {
        var (e, admin, solicitudes) = Administrador();
        var cliente = e.Cuenta("ana@example.com");
        var solicitud = Sembrar(e, cliente.Usuario.Id, DominioTipo.Acceso, new DateOnly(2026, 12, 14));

        var detalle = await solicitudes.Consultar(admin.Usuario.Id, solicitud.Id);

        Assert.True(detalle.EsExito);
        Assert.Equal(solicitud.Id, detalle.Valor.Solicitud.Id);
        var auditada = Assert.Single(e.Auditoria.Registradas);
        Assert.Equal(AccionesAuditables.SolicitudDeDatosConsultada, auditada.Accion);
        Assert.Equal("solicitud-de-datos", auditada.ObjetoTipo);
        Assert.Equal(solicitud.Id.ToString("D"), auditada.ObjetoId);
        Assert.Empty(auditada.Detalle);
        Assert.DoesNotContain("ana@example.com", $"{auditada.ObjetoId}{auditada.Motivo}", StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-52")]
    public async Task SiLaConsultaNoSePuedeAuditar_NoSeMuestraElDetalle()
    {
        var (e, admin, solicitudes) = Administrador();
        var solicitud = Sembrar(e, Guid.NewGuid(), DominioTipo.Acceso, new DateOnly(2026, 12, 14));
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => solicitudes.Consultar(admin.Usuario.Id, solicitud.Id));

        Assert.Empty(e.Auditoria.Registradas);
    }

    [Theory]
    [InlineData(RolDeUsuario.EncargadoDeSucursal)]
    [InlineData(RolDeUsuario.Logistica)]
    [InlineData(RolDeUsuario.Cliente)]
    [Trait("Criterio", "AC-IDE-52")]
    public async Task UnEncargadoLogisticaOUnCliente_NoAbreNiListaNiResponde_YQuedaRegistrado(RolDeUsuario rol)
    {
        var e = new EntornoDeIdentidad();
        var persona = e.Cuenta("persona@example.com", rol);
        e.Actor.Actual = ActorActual.DeUsuario(persona.Usuario.Id, "otro");
        var solicitud = Sembrar(e, Guid.NewGuid(), DominioTipo.Acceso, new DateOnly(2026, 12, 14));
        var solicitudes = e.Solicitudes();

        var errores = new[]
        {
            (await solicitudes.Consultar(persona.Usuario.Id, solicitud.Id)).Error,
            (await solicitudes.Listar(persona.Usuario.Id)).Error,
            (await solicitudes.Responder(persona.Usuario.Id, solicitud.Id, ResolucionDeSolicitud.Atendida, "respuesta", null)).Error,
        };

        Assert.All(errores, error => Assert.Equal(AlcanceDeLaCuenta.Prohibido, error));
        Assert.Equal(3, e.Denegaciones.Registradas.Count);
        Assert.Empty(e.Auditoria.Registradas);
        Assert.Equal(EstadoDeSolicitudDeDatos.Recibida, e.Bd.Solicitudes.Single().Estado);
    }

    [Fact]
    public async Task UnAdministradorDesactivado_NoOpera()
    {
        var (e, admin, solicitudes) = Administrador();
        admin.Usuario.Desactivar();

        Assert.Equal(AlcanceDeLaCuenta.Prohibido, (await solicitudes.Listar(admin.Usuario.Id)).Error);
    }

    [Fact]
    public async Task ConsultarUnaSolicitudInexistente_NoLaEncuentra_YNoAudita()
    {
        var (e, admin, solicitudes) = Administrador();

        var resultado = await solicitudes.Consultar(admin.Usuario.Id, Guid.NewGuid());

        Assert.Equal("solicitud-no-encontrada", resultado.Error!.Clave);
        Assert.Empty(e.Auditoria.Registradas);
    }

    // ---------------------------------------------------------------- responder (AC-IDE-23)

    [Fact]
    [Trait("Criterio", "AC-IDE-23")]
    public async Task Atender_GuardaLaRespuesta_Audita_DejaElAvisoEnLaCuenta_YAvisaPorCorreoSinElContenido()
    {
        var (e, admin, solicitudes) = Administrador();
        var cliente = e.Cuenta("ana@example.com");
        var solicitud = Sembrar(e, cliente.Usuario.Id, DominioTipo.Acceso, new DateOnly(2026, 12, 14));

        var resultado = await solicitudes.Responder(admin.Usuario.Id, solicitud.Id, ResolucionDeSolicitud.Atendida, "  Le enviamos el resumen de sus datos.  ", null);

        Assert.True(resultado.EsExito);
        Assert.Equal("atendida", resultado.Valor.Estado);
        var guardada = e.Bd.Solicitudes.Single();
        Assert.Equal("Le enviamos el resumen de sus datos.", guardada.Respuesta);
        var auditada = Assert.Single(e.Auditoria.Registradas);
        Assert.Equal(AccionesAuditables.SolicitudDeDatosAtendida, auditada.Accion);
        Assert.Equal("atendida", auditada.Detalle["resolucion"]);
        Assert.Equal(nameof(DominioTipo.Acceso), auditada.Detalle["tipoDeSolicitud"]);
        Assert.DoesNotContain("resumen", string.Join('|', auditada.Detalle.Values), StringComparison.Ordinal);
        Assert.Equal("solicitud-de-datos-respondida", Assert.Single(e.Bd.Avisos).Aviso.Tipo);
        var correo = Assert.Single(e.Correo.Correos);
        Assert.Equal("ana@example.com", correo.Destinatario);
        Assert.Equal(AvisosDeCuenta.PlantillaDeSolicitudDeDatosRespondida, correo.Plantilla);
        Assert.Empty(correo.Datos);

        // El cliente ve la respuesta en su cuenta.
        e.ActuarComo(cliente);
        var vista = (await e.Solicitudes().ListarLasDelCliente(cliente.Usuario.Id)).Valor.Single();
        Assert.Equal("Le enviamos el resumen de sus datos.", vista.Respuesta);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-23")]
    public async Task Rechazar_ExigeUnMotivo_YLoGuarda()
    {
        var (e, admin, solicitudes) = Administrador();
        var cliente = e.Cuenta("ana@example.com");
        var solicitud = Sembrar(e, cliente.Usuario.Id, DominioTipo.Oposicion, new DateOnly(2026, 11, 30));

        var sinMotivo = await solicitudes.Responder(admin.Usuario.Id, solicitud.Id, ResolucionDeSolicitud.Rechazada, null, "   ");
        var rechazada = await solicitudes.Responder(admin.Usuario.Id, solicitud.Id, ResolucionDeSolicitud.Rechazada, null, "No se pudo verificar la titularidad.");

        Assert.Equal(CategoriaDeError.Validacion, sinMotivo.Error!.Categoria);
        Assert.True(rechazada.EsExito);
        Assert.Equal("rechazada", rechazada.Valor.Estado);
        Assert.Equal("No se pudo verificar la titularidad.", e.Bd.Solicitudes.Single().Motivo);
        Assert.Equal("rechazada", Assert.Single(e.Auditoria.Registradas).Detalle["resolucion"]);
    }

    [Fact]
    public async Task Atender_SinRespuesta_OConUnaDemasiadoLarga_SeRechaza()
    {
        var (e, admin, solicitudes) = Administrador();
        var solicitud = Sembrar(e, e.Cuenta("ana@example.com").Usuario.Id, DominioTipo.Acceso, new DateOnly(2026, 12, 14));

        var vacia = await solicitudes.Responder(admin.Usuario.Id, solicitud.Id, ResolucionDeSolicitud.Atendida, " ", null);
        var larga = await solicitudes.Responder(admin.Usuario.Id, solicitud.Id, ResolucionDeSolicitud.Atendida, new string('a', AtencionDeSolicitudesDeDatos.LargoMaximoDeLaRespuesta + 1), null);

        Assert.Equal(CategoriaDeError.Validacion, vacia.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, larga.Error!.Categoria);
        Assert.Equal(EstadoDeSolicitudDeDatos.Recibida, e.Bd.Solicitudes.Single().Estado);
    }

    [Fact]
    public async Task PonerEnAtencion_CambiaElEstado_Audita_PeroNoAvisaAlCliente_YSigueSinAtender()
    {
        var (e, admin, solicitudes) = Administrador();
        var solicitud = Sembrar(e, e.Cuenta("ana@example.com").Usuario.Id, DominioTipo.Acceso, new DateOnly(2026, 10, 1));

        var resultado = await solicitudes.Responder(admin.Usuario.Id, solicitud.Id, ResolucionDeSolicitud.EnAtencion, null, null);

        Assert.Equal("en-atencion", resultado.Valor.Estado);
        Assert.True(resultado.Valor.Vencida);
        Assert.Equal("en-atencion", Assert.Single(e.Auditoria.Registradas).Detalle["resolucion"]);
        Assert.Empty(e.Bd.Avisos);
        Assert.Empty(e.Correo.Correos);
    }

    [Fact]
    public async Task UnaSolicitudCerrada_NoCambia()
    {
        var (e, admin, solicitudes) = Administrador();
        var solicitud = Sembrar(e, e.Cuenta("ana@example.com").Usuario.Id, DominioTipo.Acceso, new DateOnly(2026, 12, 14));
        await solicitudes.Responder(admin.Usuario.Id, solicitud.Id, ResolucionDeSolicitud.Atendida, "primera", null);

        var otra = await solicitudes.Responder(admin.Usuario.Id, solicitud.Id, ResolucionDeSolicitud.Rechazada, null, "motivo");

        Assert.Equal("solicitud-ya-resuelta", otra.Error!.Clave);
        Assert.Equal("primera", e.Bd.Solicitudes.Single().Respuesta);
        Assert.Single(e.Auditoria.Registradas);
    }

    [Fact]
    public async Task Responder_SiLaAuditoriaFalla_NoCambiaNada_NiAvisa()
    {
        var (e, admin, solicitudes) = Administrador();
        var solicitud = Sembrar(e, e.Cuenta("ana@example.com").Usuario.Id, DominioTipo.Acceso, new DateOnly(2026, 12, 14));
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => solicitudes.Responder(admin.Usuario.Id, solicitud.Id, ResolucionDeSolicitud.Atendida, "respuesta", null));

        Assert.Equal(EstadoDeSolicitudDeDatos.Recibida, e.Bd.Solicitudes.Single().Estado);
        Assert.Null(e.Bd.Solicitudes.Single().Respuesta);
        Assert.Empty(e.Bd.Avisos);
        Assert.Empty(e.Correo.Correos);
    }

    [Fact]
    public async Task Responder_SiElCorreoFalla_LaRespuestaYaEstaGuardada()
    {
        var (e, admin, solicitudes) = Administrador();
        var solicitud = Sembrar(e, e.Cuenta("ana@example.com").Usuario.Id, DominioTipo.Acceso, new DateOnly(2026, 12, 14));
        e.Correo.Falla = true;

        var resultado = await solicitudes.Responder(admin.Usuario.Id, solicitud.Id, ResolucionDeSolicitud.Atendida, "respuesta", null);

        Assert.True(resultado.EsExito);
        Assert.Equal("respuesta", e.Bd.Solicitudes.Single().Respuesta);
        Assert.Single(e.Bd.Avisos);
    }

    [Fact]
    public async Task ResponderUnaSolicitudInexistente_NoLaEncuentra()
    {
        var (_, admin, solicitudes) = Administrador();

        var resultado = await solicitudes.Responder(admin.Usuario.Id, Guid.NewGuid(), ResolucionDeSolicitud.Atendida, "respuesta", null);

        Assert.Equal("solicitud-no-encontrada", resultado.Error!.Clave);
    }
}
