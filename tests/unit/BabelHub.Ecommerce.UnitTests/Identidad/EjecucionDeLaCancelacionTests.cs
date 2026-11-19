using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;
using DominioEstado = BabelHub.Ecommerce.Domain.Identidad.EstadoDeCancelacion;
using DominioTipo = BabelHub.Ecommerce.Domain.Identidad.TipoDeSolicitudDeDatos;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// La ejecución de la cancelación y la entrega de la baja (identidad-seguridad T23 y T24; AC-IDE-43, AC-IDE-51, AC-IDE-53, AC-IDE-57; IDE-47, IDE-48, IDE-62, IDE-64, IDE-69, IDE-70, IDE-75, IDE-89, PLA-41): una sola
/// transacción que anonimiza la cuenta, deja la baja y el cierre pendientes y audita; cualquier módulo que falla revierte todo; el cierre y la entrega se reintentan sin deshacer nada.
/// </summary>
public sealed class EjecucionDeLaCancelacionTests
{
    private static ValorSecreto Contrasena => new(EntornoDeIdentidad.ContrasenaSegura);

    private static (EntornoDeIdentidad E, CuentaDeIdentidad Cliente, CancelacionDeCuenta Cancelacion) Escenario()
    {
        var e = new EntornoDeIdentidad();
        var cliente = e.Cuenta("ana@example.com");
        e.ActuarComo(cliente);
        return (e, cliente, e.CancelacionReal());
    }

    // ---------------------------------------------------------------- la ejecución (T23)

    [Fact]
    [Trait("Criterio", "AC-IDE-43")]
    [Trait("Criterio", "AC-IDE-51")]
    public async Task AlEjecutarse_LaCuentaQuedaAnonimizada_ConLaIntencionDeSupresionYLaBajaPendiente_YAuditadaConElTitularComoActor()
    {
        var (e, cliente, cancelacion) = Escenario();
        var id = cliente.Usuario.Id;
        e.ConSegundoFactor(cliente);
        await e.DispositivosDeLaCuenta().RegistrarElIngresoCompleto(id, null, "PE");
        e.AvisosGuardados.Sembrar(id, "contrasena-cambiada", e.Reloj.Ahora);
        var sesion = await e.Sesiones.Nueva(cliente.Usuario, e.Reloj.Ahora, default);
        var codigo = new ValorSecreto(e.Totp(e.Secretos.SecretoDelSegundoFactor()));
        await cancelacion.Solicitar(id, Contrasena, codigo);

        Assert.NotNull(cliente.CanceladaEn);
        Assert.False(cliente.Usuario.Activo);
        Assert.DoesNotContain("example.com", cliente.Usuario.Correo, StringComparison.Ordinal);
        Assert.EndsWith("@anonimo.invalid", cliente.Usuario.Correo, StringComparison.Ordinal);
        Assert.Equal(EjecutorDeLaCancelacion.NombreAnonimo, cliente.Nombre);
        Assert.Null(cliente.ResumenDeAcceso);
        Assert.Null(cliente.Whatsapp.Numero);
        Assert.Empty(cliente.Aceptacion.Aceptadas);

        // La intención está en el registro fuera de las bases y todavía no el cierre.
        Assert.Contains($"{id:D}/Cancelacion-Intencion", e.Supresiones.Escrituras);
        Assert.DoesNotContain($"{id:D}/Cancelacion-Cierre", e.Supresiones.Escrituras);

        // Cada módulo recibió al titular con su identificador opaco; el esquema identidad aplicó su parte.
        Assert.All(e.Aplicadores, a => Assert.Equal([(id, id)], a.Aplicados));
        Assert.Equal(1, e.Purga.Llamadas);
        Assert.Empty(e.Bd.Dispositivos);
        Assert.Empty(e.Bd.Avisos);
        Assert.Contains(id, e.CancelacionDeAvisos.Cancelados);
        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, (await e.Gestor().Renovar(sesion.Renovacion)).Error);

        // La baja y el cierre quedan pendientes en la misma transacción.
        var baja = Assert.Single(e.Bd.Bajas);
        Assert.Equal(id, baja.IdentificadorOpaco);
        Assert.True(baja.Pendiente);

        // Auditada con el titular como actor y sin datos personales.
        var auditada = Assert.Single(e.Auditoria.Registradas, a => a.Accion == AccionesAuditables.CuentaCancelada);
        Assert.Equal(EjecutorDeLaCancelacion.CausaPorSolicitudDelTitular, auditada.Detalle["causa"]);
        Assert.DoesNotContain("example.com", $"{auditada.ObjetoId}{string.Join('|', auditada.Detalle.Values)}", StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-51")]
    public async Task UnaCancelacionReconfirmadaTrasLosPendientes_QuedaAuditadaConSuPropiaCausa()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Pendientes.Cantidad = 1;
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        e.Pendientes.Cantidad = 0;
        e.Actor.Actual = ActorActual.DelSistema();
        await e.Revision().Revisar();
        e.ActuarComo(cliente);

        var resultado = await cancelacion.Reconfirmar(cliente.Usuario.Id, Contrasena, null);

        Assert.True(resultado.EsExito);
        var auditada = Assert.Single(e.Auditoria.Registradas, a => a.Accion == AccionesAuditables.CuentaCancelada);
        Assert.Equal(EjecutorDeLaCancelacion.CausaPorReconfirmacion, auditada.Detalle["causa"]);
        Assert.Equal(DominioEstado.Ejecutada, e.Bd.Cancelaciones.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-43")]
    public async Task ElCorreoAnonimizado_PuedeRegistrarseDeNuevoComoUnaCuentaNuevaSinRecuperarNada()
    {
        var (e, cliente, cancelacion) = Escenario();
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        Assert.Null(await e.Cuentas.PorCorreo("ana@example.com"));

        var registro = await e.Registro().Registrar(e.Alta("ana@example.com", "Otra Ana"));

        Assert.True(registro.EsExito);
        var nueva = await e.Cuentas.PorCorreo("ana@example.com");
        Assert.NotNull(nueva);
        Assert.NotEqual(cliente.Usuario.Id, nueva!.Usuario.Id);
        Assert.Equal("Otra Ana", nueva.Nombre);
        Assert.Null(nueva.Whatsapp.Numero);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-43")]
    public async Task LaSolicitudSobreDatos_ConservaSoloIdentificadorTipoEstadoYFechas()
    {
        var (e, cliente, cancelacion) = Escenario();
        var solicitud = SolicitudDeDatos.Restaurar(Guid.NewGuid(), cliente.Usuario.Id, DominioTipo.Acceso, EstadoDeSolicitudDeDatos.Recibida, e.Reloj.Ahora, new DateOnly(2026, 12, 14), null);
        solicitud.Atender(e.Reloj.Ahora, "Le enviamos un resumen de sus datos.");
        e.Bd.Solicitudes.Add(solicitud);

        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);

        var guardada = Assert.Single(e.Bd.Solicitudes);
        Assert.Null(guardada.Respuesta);
        Assert.Null(guardada.Motivo);
        Assert.Equal(EstadoDeSolicitudDeDatos.Atendida, guardada.Estado);
        Assert.Equal(DominioTipo.Acceso, guardada.Tipo);
    }

    // ---------------------------------------------------------------- fallar cerrado (AC-IDE-57)

    [Theory]
    [InlineData("pedidos")]
    [InlineData("pagos")]
    [InlineData("devoluciones")]
    [InlineData("biblioteca")]
    [InlineData("reclamaciones")]
    [InlineData("carrito")]
    [Trait("Criterio", "AC-IDE-57")]
    public async Task SiUnModuloNoPuedeAplicar_LaCancelacionNoSeEjecutaNiQuedaNingunEfectoParcial_YSePuedeReintentar(string modulo)
    {
        var (e, cliente, cancelacion) = Escenario();
        var id = cliente.Usuario.Id;
        await e.DispositivosDeLaCuenta().RegistrarElIngresoCompleto(id, null, "PE");
        e.Aplicadores.Single(a => a.Modulo == modulo).Falla = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => cancelacion.Solicitar(id, Contrasena, null));

        AssertSinEfectosParciales(e, cliente);

        // Se reintenta cuando el módulo se recupera: la cancelación se ejecuta una sola vez.
        e.Aplicadores.Single(a => a.Modulo == modulo).Falla = false;
        Assert.True((await cancelacion.Solicitar(id, Contrasena, null)).EsExito);
        Assert.NotNull(cliente.CanceladaEn);
        Assert.Single(e.Bd.Bajas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-57")]
    public async Task SiLaPurgaDelEsquemaIdentidadFalla_NoQuedaNingunEfectoParcial()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Purga.Falla = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null));

        AssertSinEfectosParciales(e, cliente);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-57")]
    public async Task SiLaAuditoriaNoEstaDisponible_LaCancelacionNoSeEjecutaNiSeConfirma()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null));

        AssertSinEfectosParciales(e, cliente);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-57")]
    public async Task SiElRegistroDeSupresionesNoResponde_NoSeEjecutaNada_NiSeLlamaAUnSoloModulo()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Supresiones.NoDisponible = true;

        await Assert.ThrowsAsync<RegistroDeSupresionesNoDisponibleException>(() => cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null));

        AssertSinEfectosParciales(e, cliente);
        Assert.All(e.Aplicadores, a => Assert.Empty(a.Aplicados));
        Assert.Equal(0, e.Purga.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-57")]
    public async Task SiElPuertoDePendientesNoResponde_NoSeEjecutaNada()
    {
        var (e, cliente, cancelacion) = Escenario();
        e.Pendientes.Falla = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null));

        AssertSinEfectosParciales(e, cliente);
    }

    private static void AssertSinEfectosParciales(EntornoDeIdentidad e, CuentaDeIdentidad cliente)
    {
        Assert.Null(cliente.CanceladaEn);
        Assert.True(cliente.Usuario.Activo);
        Assert.Equal("ana@example.com", cliente.Usuario.Correo);
        Assert.Equal("Persona", cliente.Nombre);
        Assert.NotNull(cliente.ResumenDeAcceso);
        Assert.Empty(e.Bd.Bajas);
        Assert.DoesNotContain(e.Auditoria.Registradas, a => a.Accion == AccionesAuditables.CuentaCancelada);
        // Lo que se pudo escribir fuera de las bases, la intención sin cierre, no cuenta (PLA-19).
        Assert.DoesNotContain(e.Supresiones.Escrituras, c => c.EndsWith("Cierre", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- el cierre y la entrega de la baja (T24)

    [Fact]
    [Trait("Criterio", "AC-IDE-53")]
    public async Task ElReintento_EntregaLaBajaConSoloElIdentificadorOpaco_YAgregaElCierreAlRegistro()
    {
        var (e, cliente, cancelacion) = Escenario();
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        e.Actor.Actual = ActorActual.DelSistema();

        var resultado = await e.Reintento().Procesar();

        Assert.Equal(new ResultadoDelReintentoDeBajas(1, 1, 1, 0), resultado);
        Assert.Equal([cliente.Usuario.Id], e.EntregaDeBajas.Entregadas);
        Assert.Contains($"{cliente.Usuario.Id:D}/Cancelacion-Cierre", e.Supresiones.Escrituras);
        Assert.False(e.Bd.Bajas.Single().Pendiente);
        // Ya completa: otra pasada no repite nada.
        Assert.Equal(new ResultadoDelReintentoDeBajas(0, 0, 0, 0), await e.Reintento().Procesar());
        Assert.Single(e.EntregaDeBajas.Entregadas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-53")]
    public async Task SiElInventarioNoEstaDisponible_ElCierreSeEscribe_LaBajaSeReintenta_YLaCancelacionNoSeDeshace()
    {
        var (e, cliente, cancelacion) = Escenario();
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        e.Actor.Actual = ActorActual.DelSistema();
        e.EntregaDeBajas.Falla = true;

        var primera = await e.Reintento().Procesar();

        Assert.Equal(1, primera.Cerradas);
        Assert.Equal(0, primera.Entregadas);
        Assert.NotNull(cliente.CanceladaEn);
        Assert.Contains(e.Bd.Bajas, b => b.CerradaEn is not null && b.EntregadaEn is null);

        e.EntregaDeBajas.Falla = false;
        var segunda = await e.Reintento().Procesar();
        Assert.Equal(1, segunda.Entregadas);
        Assert.Equal(0, segunda.Cerradas);
        Assert.False(e.Bd.Bajas.Single().Pendiente);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-53")]
    public async Task SiElRegistroDeSupresionesNoResponde_ElCierreQuedaPendiente_YLaBajaSeEntregaIgual_ElCierreSeReintenta()
    {
        var (e, cliente, cancelacion) = Escenario();
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        e.Actor.Actual = ActorActual.DelSistema();
        e.Supresiones.NoDisponible = true;

        var primera = await e.Reintento().Procesar();

        Assert.Equal(0, primera.Cerradas);
        Assert.Equal(1, primera.Entregadas);
        Assert.NotNull(cliente.CanceladaEn);
        e.Supresiones.NoDisponible = false;
        Assert.Equal(1, (await e.Reintento().Procesar()).Cerradas);
        Assert.False(e.Bd.Bajas.Single().Pendiente);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-53")]
    public async Task UnaBajaSinEntregar24Horas_PublicaLaAlertaTecnica_PeroNoAntes()
    {
        var (e, cliente, cancelacion) = Escenario();
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        e.Actor.Actual = ActorActual.DelSistema();
        e.EntregaDeBajas.Falla = true;

        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddHours(23);
        await e.Reintento().Procesar();
        Assert.Empty(e.AlertasDeSeguridad.Tecnicas);

        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddHours(24);
        var resultado = await e.Reintento().Procesar();

        Assert.Equal(1, resultado.SinEntregarPasadoElPlazo);
        Assert.Equal(ReintentoDeBajas.MotivoDeBajaSinEntregar, Assert.Single(e.AlertasDeSeguridad.Tecnicas));
        Assert.NotNull(cliente.CanceladaEn);
    }

    [Fact]
    public async Task UnaFallaEnUnaBaja_NoDetieneLasDemas()
    {
        var (e, cliente, cancelacion) = Escenario();
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        var otra = e.Cuenta("otra@example.com");
        e.ActuarComo(otra);
        await e.CancelacionReal().Solicitar(otra.Usuario.Id, Contrasena, null);
        e.Actor.Actual = ActorActual.DelSistema();

        var resultado = await e.Reintento().Procesar();

        Assert.Equal(2, resultado.Entregadas);
        Assert.Equal(2, resultado.Cerradas);
    }

    [Fact]
    public async Task NoSeCierraUnaSupresionSinIntencionPrevia()
    {
        var (e, _, _) = Escenario();
        e.Bd.Bajas.Add(new BajaDeTitular(Guid.NewGuid(), e.Reloj.Ahora));
        e.Actor.Actual = ActorActual.DelSistema();

        var resultado = await e.Reintento().Procesar();

        Assert.Equal(0, resultado.Cerradas);
        Assert.DoesNotContain(e.Supresiones.Escrituras, c => c.EndsWith("Cierre", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-53")]
    public async Task UnaBajaSinCerrarEn24Horas_PorElRegistroCaido_PublicaLaMismaAlertaTecnica_AunqueSeHayaEntregado()
    {
        var (e, cliente, cancelacion) = Escenario();
        await cancelacion.Solicitar(cliente.Usuario.Id, Contrasena, null);
        e.Actor.Actual = ActorActual.DelSistema();
        e.Supresiones.NoDisponible = true;

        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddHours(23);
        await e.Reintento().Procesar();
        Assert.Empty(e.AlertasDeSeguridad.Tecnicas);

        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddHours(25);
        var resultado = await e.Reintento().Procesar();

        Assert.Equal(1, resultado.SinEntregarPasadoElPlazo);
        Assert.Equal(ReintentoDeBajas.MotivoDeBajaSinEntregar, Assert.Single(e.AlertasDeSeguridad.Tecnicas));
        Assert.NotNull(e.Bd.Bajas.Single().EntregadaEn);
        Assert.Null(e.Bd.Bajas.Single().CerradaEn);
    }

    [Fact]
    public async Task UnaBajaSinIntencionPrevia_QueLlevaMasDe24Horas_TambienAlerta()
    {
        var (e, _, _) = Escenario();
        e.Bd.Bajas.Add(new BajaDeTitular(Guid.NewGuid(), e.Reloj.Ahora));
        e.Actor.Actual = ActorActual.DelSistema();
        e.Reloj.Ahora = e.Reloj.Ahora.AddHours(25);

        await e.Reintento().Procesar();

        Assert.Single(e.AlertasDeSeguridad.Tecnicas);
    }

    [Fact]
    public void ElMotivoDeLaAlertaDeBajas_EstaEnElCatalogoCerradoDeAlertas()
    {
        var catalogo = BabelHub.Ecommerce.Application.Auditoria.Interno.CatalogoDeAlertasDeAuditoria.Crear();

        Assert.True(catalogo.Contiene(ReintentoDeBajas.MotivoDeBajaSinEntregar));
    }
}
