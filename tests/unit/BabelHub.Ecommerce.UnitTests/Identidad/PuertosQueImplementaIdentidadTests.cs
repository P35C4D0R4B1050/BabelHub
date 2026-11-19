using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// Los puertos que implementa Identidad (identidad-seguridad T27; AC-IDE-43, AC-IDE-51, AC-TRA-21; IDE-62, IDE-75, PLA-19, PLA-41, PLA-43, AUD-19, AUD-29, AUD-31, TRA-22): los destinatarios de las alertas (administración
/// y, para traslados, logística), las cuentas por anonimizar, la reaplicación de la supresión tras restaurar y los pasos de restauración.
/// </summary>
public sealed class PuertosQueImplementaIdentidadTests
{
    // ---------------------------------------------------------------- destinatarios (AUD-29, TRA-22)

    [Fact]
    [Trait("Criterio", "AC-TRA-21")]
    public async Task LosDestinatarios_SonLosAdministradoresActivos_YLogisticaPorSeparado_SinClientesNiEncargadosNiInactivos()
    {
        var e = new EntornoDeIdentidad();
        var admin = e.Cuenta("admin@example.com", RolDeUsuario.AdministradorCentral);
        var adminInactivo = e.Cuenta("inactivo@example.com", RolDeUsuario.AdministradorCentral, activa: false);
        var logistica = e.Cuenta("logistica@example.com", RolDeUsuario.Logistica);
        var logisticaCancelada = e.Cuenta("cancelada@example.com", RolDeUsuario.Logistica);
        logisticaCancelada.CanceladaEn = e.Reloj.Ahora;
        e.Cuenta("encargado@example.com", RolDeUsuario.EncargadoDeSucursal);
        e.Cuenta("cliente@example.com");
        var destinatarios = e.Destinatarios();

        var administracion = await destinatarios.Activos();
        var deLogistica = await destinatarios.DeLogistica();

        Assert.Equal([admin.Usuario.Id], administracion.Select(d => d.CuentaId));
        Assert.Equal([logistica.Usuario.Id], deLogistica.Select(d => d.CuentaId));
        Assert.DoesNotContain(administracion, d => d.CuentaId == adminInactivo.Usuario.Id);
    }

    [Fact]
    public async Task SinAdministradoresActivos_NoHayDestinatarios()
    {
        var e = new EntornoDeIdentidad();
        e.Cuenta("cliente@example.com");

        Assert.Empty(await e.Destinatarios().Activos());
        Assert.Empty(await e.Destinatarios().DeLogistica());
    }

    // ---------------------------------------------------------------- por anonimizar (AUD-19, AUD-31)

    [Fact]
    [Trait("Criterio", "AC-IDE-51")]
    public async Task LaCuentaCancelada_QuedaPorAnonimizar_ConSuIdentificadorOpaco_YElLoteRespetaElMaximo()
    {
        var e = new EntornoDeIdentidad();
        var cliente = e.Cuenta("ana@example.com");
        e.ActuarComo(cliente);
        await e.CancelacionReal().Solicitar(cliente.Usuario.Id, new ValorSecreto(EntornoDeIdentidad.ContrasenaSegura), null);
        var otra = e.Cuenta("otra@example.com");
        e.ActuarComo(otra);
        await e.CancelacionReal().Solicitar(otra.Usuario.Id, new ValorSecreto(EntornoDeIdentidad.ContrasenaSegura), null);
        var puerto = new CuentasCanceladasPendientesDeIdentidad(e.PorAnonimizarGuardadas);

        var todas = await puerto.Listar(10);
        var una = await puerto.Listar(1);

        Assert.Equal([cliente.Usuario.Id, otra.Usuario.Id], todas);
        Assert.Single(una);
    }

    [Fact]
    public async Task SiLaCancelacionSeRevierte_NoQuedaPorAnonimizar()
    {
        var e = new EntornoDeIdentidad();
        var cliente = e.Cuenta("ana@example.com");
        e.ActuarComo(cliente);
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => e.CancelacionReal().Solicitar(cliente.Usuario.Id, new ValorSecreto(EntornoDeIdentidad.ContrasenaSegura), null));

        Assert.Empty(await new CuentasCanceladasPendientesDeIdentidad(e.PorAnonimizarGuardadas).Listar(10));
    }

    // ---------------------------------------------------------------- reaplicar la supresión (PLA-19, PLA-41)

    [Fact]
    [Trait("Criterio", "AC-IDE-43")]
    public async Task ReaplicarUnaCancelacion_CancelaLaCuentaRestaurada_SinIntencionNiAuditoriaNueva_YConBaja()
    {
        var e = new EntornoDeIdentidad();
        var cliente = e.Cuenta("ana@example.com");
        e.Actor.Actual = ActorActual.DelSistema();

        var resultado = await e.AplicadorDeSupresion().Aplicar(cliente.Usuario.Id.ToString("D"), ClaseDeSupresion.Cancelacion);

        Assert.Equal(ResultadoDeAplicacionDeSupresion.Aplicada, resultado);
        Assert.NotNull(cliente.CanceladaEn);
        Assert.False(cliente.Usuario.Activo);
        Assert.EndsWith("@anonimo.invalid", cliente.Usuario.Correo, StringComparison.Ordinal);
        Assert.Empty(e.Supresiones.Escrituras);
        Assert.DoesNotContain(e.Auditoria.Registradas, a => a.Accion == AccionesAuditables.CuentaCancelada);
        Assert.All(e.Aplicadores, a => Assert.Single(a.Aplicados));
        Assert.Single(e.Bd.Bajas);
    }

    [Fact]
    public async Task ReaplicarDosVeces_EsIdempotente_YLaSegundaEsYaAplicada()
    {
        var e = new EntornoDeIdentidad();
        var cliente = e.Cuenta("ana@example.com");
        var aplicador = e.AplicadorDeSupresion();

        await aplicador.Aplicar(cliente.Usuario.Id.ToString("D"), ClaseDeSupresion.Cancelacion);
        var otra = await aplicador.Aplicar(cliente.Usuario.Id.ToString("D"), ClaseDeSupresion.Cancelacion);

        Assert.Equal(ResultadoDeAplicacionDeSupresion.YaEstabaAplicada, otra);
        Assert.Single(e.Bd.Bajas);
        Assert.All(e.Aplicadores, a => Assert.Single(a.Aplicados));
    }

    [Fact]
    public async Task ReaplicarUnaCuentaQueYaEstabaCancelada_EsYaAplicada_SinTocarNada()
    {
        var e = new EntornoDeIdentidad();
        var cliente = e.Cuenta("ana@example.com");
        cliente.CanceladaEn = e.Reloj.Ahora.AddDays(-1);

        var resultado = await e.AplicadorDeSupresion().Aplicar(cliente.Usuario.Id.ToString("D"), ClaseDeSupresion.Cancelacion);

        Assert.Equal(ResultadoDeAplicacionDeSupresion.YaEstabaAplicada, resultado);
        Assert.Empty(e.Bd.Bajas);
        Assert.All(e.Aplicadores, a => Assert.Empty(a.Aplicados));
    }

    [Fact]
    public async Task UnaAnonimizacion_AnonimizaLaCuentaPeroNoEntregaBajaAlInventario()
    {
        var e = new EntornoDeIdentidad();
        var cliente = e.Cuenta("ana@example.com");

        var resultado = await e.AplicadorDeSupresion().Aplicar(cliente.Usuario.Id.ToString("D"), ClaseDeSupresion.Anonimizacion);
        var otra = await e.AplicadorDeSupresion().Aplicar(cliente.Usuario.Id.ToString("D"), ClaseDeSupresion.Anonimizacion);

        Assert.Equal(ResultadoDeAplicacionDeSupresion.Aplicada, resultado);
        Assert.Equal(ResultadoDeAplicacionDeSupresion.YaEstabaAplicada, otra);
        Assert.EndsWith("@anonimo.invalid", cliente.Usuario.Correo, StringComparison.Ordinal);
        Assert.Empty(e.Bd.Bajas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-43")]
    public async Task SiUnModuloFalla_LaReaplicacionEsFallida_NadaQuedaAMedias_YSeReintenta()
    {
        var e = new EntornoDeIdentidad();
        var cliente = e.Cuenta("ana@example.com");
        e.Aplicadores.Single(a => a.Modulo == "pagos").Falla = true;
        var aplicador = e.AplicadorDeSupresion();

        var primera = await aplicador.Aplicar(cliente.Usuario.Id.ToString("D"), ClaseDeSupresion.Cancelacion);

        Assert.Equal(ResultadoDeAplicacionDeSupresion.Fallida, primera);
        Assert.Null(cliente.CanceladaEn);
        Assert.Equal("ana@example.com", cliente.Usuario.Correo);
        Assert.Empty(e.Bd.Bajas);

        e.Aplicadores.Single(a => a.Modulo == "pagos").Falla = false;
        Assert.Equal(ResultadoDeAplicacionDeSupresion.Aplicada, await aplicador.Aplicar(cliente.Usuario.Id.ToString("D"), ClaseDeSupresion.Cancelacion));
    }

    [Theory]
    [InlineData("no-es-un-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task UnIdentificadorInvalido_EsFallida(string identificador)
    {
        var e = new EntornoDeIdentidad();

        Assert.Equal(ResultadoDeAplicacionDeSupresion.Fallida, await e.AplicadorDeSupresion().Aplicar(identificador, ClaseDeSupresion.Cancelacion));
    }

    [Fact]
    public async Task UnaCuentaQueNoExiste_EsYaAplicada_PorqueNoHayNadaQueSuprimir()
    {
        var e = new EntornoDeIdentidad();

        Assert.Equal(ResultadoDeAplicacionDeSupresion.YaEstabaAplicada, await e.AplicadorDeSupresion().Aplicar(Guid.NewGuid().ToString("D"), ClaseDeSupresion.Cancelacion));
    }

    // ---------------------------------------------------------------- restauración (PLA-43)

    [Fact]
    public async Task LaRestauracion_RotaLaClave_YRevocaTodasLasRenovaciones()
    {
        var e = new EntornoDeIdentidad();
        e.ConClaveActiva();
        var persona = e.Cuenta("ana@example.com");
        var sesion = await e.Sesiones.Nueva(persona.Usuario, e.Reloj.Ahora, default);
        e.Actor.Actual = ActorActual.DelSistema();

        var resultado = await e.Restauracion().RotarClavesEInvalidarSesiones();

        Assert.Equal(ResultadoDePaso.Correcto, resultado);
        Assert.Equal(1, e.GeneradorDeClaves.Generadas);
        Assert.Equal("kid-generada-0001", (await e.AlmacenDeClaves.Cargar(false)).Activa.Kid);
        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, (await e.Gestor().Renovar(sesion.Renovacion)).Error);
    }

    [Fact]
    public async Task SiLaRestauracionNoPuedeRotar_EsFallida_YElServicioNoSeAbre()
    {
        var e = new EntornoDeIdentidad();
        e.ConClaveActiva();
        e.Auditoria.Falla = true;

        Assert.Equal(ResultadoDePaso.Fallido, await e.Restauracion().RotarClavesEInvalidarSesiones());
    }

    [Fact]
    public async Task LosPasosDeIdentidad_CorrenDespuesDePlataforma_YAntesDeLaWeb_ConLaRotacionPrimero()
    {
        var e = new EntornoDeIdentidad();
        var rotar = new PasoDeRestauracionRotarClaves(e.Restauracion());
        var reenviar = new PasoDeRestauracionReenviarBajas(e.Reintento());

        Assert.True(rotar.Orden > OrdenDePasoDeRestauracion.Plataforma);
        Assert.True(rotar.Orden < reenviar.Orden);
        Assert.True(reenviar.Orden < OrdenDePasoDeRestauracion.Web);
        Assert.NotEqual(rotar.Nombre, reenviar.Nombre);
        await Task.CompletedTask;
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-53")]
    public async Task ElPasoDeReenviarBajas_EntregaLasPendientes_YSiElInventarioNoResponde_NoImpideAbrir()
    {
        var e = new EntornoDeIdentidad();
        var cliente = e.Cuenta("ana@example.com");
        e.ActuarComo(cliente);
        await e.CancelacionReal().Solicitar(cliente.Usuario.Id, new ValorSecreto(EntornoDeIdentidad.ContrasenaSegura), null);
        e.Actor.Actual = ActorActual.DelSistema();
        var paso = new PasoDeRestauracionReenviarBajas(e.Reintento());
        e.EntregaDeBajas.Falla = true;

        var caido = await paso.Ejecutar();
        Assert.Equal(ResultadoDePaso.Correcto, caido);
        Assert.True(e.Bd.Bajas.Single().Pendiente);

        e.EntregaDeBajas.Falla = false;
        Assert.Equal(ResultadoDePaso.Correcto, await paso.Ejecutar());
        Assert.False(e.Bd.Bajas.Single().Pendiente);
    }

    // ---------------------------------------------------------------- direcciones (T34, SEG-28)

    [Fact]
    public async Task LaDireccionDeUnaAlerta_EsLaRegistradaDeLaCuentaActiva_YNuncaLaDeUnaCuentaDesactivadaCanceladaOInexistente()
    {
        var e = new EntornoDeIdentidad();
        var activa = e.Cuenta("admin@example.com", RolDeUsuario.AdministradorCentral);
        var inactiva = e.Cuenta("inactiva@example.com", RolDeUsuario.AdministradorCentral, activa: false);
        var cancelada = e.Cuenta("cancelada@example.com", RolDeUsuario.AdministradorCentral);
        cancelada.CanceladaEn = e.Reloj.Ahora;
        var direcciones = new DireccionesDeCuentasDeIdentidad(e.Cuentas);

        Assert.Equal("admin@example.com", await direcciones.DeLaCuenta(activa.Usuario.Id));
        Assert.Null(await direcciones.DeLaCuenta(inactiva.Usuario.Id));
        Assert.Null(await direcciones.DeLaCuenta(cancelada.Usuario.Id));
        Assert.Null(await direcciones.DeLaCuenta(Guid.NewGuid()));
    }
}
