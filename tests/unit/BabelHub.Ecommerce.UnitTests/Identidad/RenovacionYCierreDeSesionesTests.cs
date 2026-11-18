using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// Renovar, cerrar y cerrar todas las sesiones (identidad-seguridad T16; AC-IDE-07, AC-IDE-12, AC-IDE-39, AC-IDE-40, AC-IDE-50; IDE-08, IDE-14, IDE-43, IDE-44, IDE-46, IDE-60): rotación única con vencimiento absoluto
/// heredado, datos actuales del usuario, reutilización que revoca la familia con evento y alerta, y revocación de las demás o de todas.
/// </summary>
public sealed class RenovacionYCierreDeSesionesTests
{
    private static async Task<(CredencialDeSesion Sesion, CuentaDeIdentidad Cuenta)> Ingresar(EntornoDeIdentidad e, RolDeUsuario rol = RolDeUsuario.Cliente)
    {
        var cuenta = e.Cuenta("ana@example.com", rol);
        var sesion = await e.Sesiones.Nueva(cuenta.Usuario, e.Reloj.Ahora, default);
        return (sesion, cuenta);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-07")]
    public async Task Renovar_RotaLaRenovacion_YEmiteUnaCredencialNueva_ConElMismoVencimientoAbsoluto()
    {
        var e = new EntornoDeIdentidad();
        var (sesion, cuenta) = await Ingresar(e);
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddDays(3);

        var renovada = await e.Gestor().Renovar(sesion.Renovacion);

        Assert.True(renovada.EsExito);
        Assert.NotEqual(sesion.Renovacion.Revelar(), renovada.Valor.Renovacion.Revelar());
        Assert.Equal(sesion.RenovacionExpiraEn, renovada.Valor.RenovacionExpiraEn);
        Assert.Equal(EntornoDeIdentidad.Inicio.AddDays(3).AddMinutes(15), renovada.Valor.CredencialExpiraEn);
        Assert.Equal(cuenta.Usuario.Id, renovada.Valor.UsuarioId);
        Assert.Equal(2, e.Bd.Renovaciones.Count);
        Assert.Single(e.Bd.Renovaciones, r => r.UsadaEn is not null);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-07")]
    [Trait("Criterio", "AC-IDE-41")]
    public async Task UsarLaRenovacionNuncaExtiendeSuVencimiento_YPasadoElNoSeRenueva()
    {
        var e = new EntornoDeIdentidad();
        var (sesion, _) = await Ingresar(e);
        var gestor = e.Gestor();
        var actual = sesion;
        for (var dia = 1; dia <= 6; dia++)
        {
            e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddDays(dia);
            actual = (await gestor.Renovar(actual.Renovacion)).Valor;
            Assert.Equal(EntornoDeIdentidad.Inicio.AddDays(7), actual.RenovacionExpiraEn);
        }

        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddDays(7);
        var vencida = await gestor.Renovar(actual.Renovacion);

        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, vencida.Error);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-07")]
    [Trait("Criterio", "AC-IDE-41")]
    public async Task LaRenovacionDelPersonalVenceALas8Horas()
    {
        var e = new EntornoDeIdentidad();
        var (sesion, _) = await Ingresar(e, RolDeUsuario.Logistica);
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddHours(8);

        var resultado = await e.Gestor().Renovar(sesion.Renovacion);

        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, resultado.Error);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-39")]
    public async Task LaRenovacion_RecargaLosDatosActualesDelUsuario_NoLosDelIngreso()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("ana@example.com", verificada: false);
        var sesion = await e.Sesiones.Nueva(cuenta.Usuario, e.Reloj.Ahora, default);
        Assert.False(sesion.CorreoVerificado);
        cuenta.Usuario.VerificarCorreo();

        var renovada = await e.Gestor().Renovar(sesion.Renovacion);

        Assert.True(renovada.Valor.CorreoVerificado);
        Assert.True(e.Emisor.Emitidas[^1].CorreoVerificado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-40")]
    public async Task UnUsuarioDesactivadoOCancelado_NoRenueva_Ni_AvanzaLaRotacion()
    {
        var e = new EntornoDeIdentidad();
        var (sesion, cuenta) = await Ingresar(e);
        cuenta.Usuario.Desactivar();

        var desactivado = await e.Gestor().Renovar(sesion.Renovacion);
        cuenta.Usuario.Reactivar();
        cuenta.CanceladaEn = EntornoDeIdentidad.Inicio;
        var cancelado = await e.Gestor().Renovar(sesion.Renovacion);

        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, desactivado.Error);
        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, cancelado.Error);
        Assert.Single(e.Bd.Renovaciones);
        Assert.Null(e.Bd.Renovaciones[0].UsadaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-12")]
    public async Task ReutilizarUnaRenovacionYaRotada_RevocaToda_LaFamilia_ConEventoYAlerta()
    {
        var e = new EntornoDeIdentidad();
        var (sesion, cuenta) = await Ingresar(e);
        var gestor = e.Gestor();
        var siguiente = (await gestor.Renovar(sesion.Renovacion)).Valor;

        var reutilizada = await gestor.Renovar(sesion.Renovacion);
        var legitima = await gestor.Renovar(siguiente.Renovacion);

        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, reutilizada.Error);
        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, legitima.Error);
        Assert.All(e.Bd.Renovaciones, r => Assert.NotNull(r.RevocadaEn));
        var evento = Assert.Single(e.Seguridad.Eventos);
        Assert.Equal(AccionesAuditables.RenovacionReutilizada, evento.Accion);
        Assert.Equal(cuenta.Usuario.Id.ToString("D"), evento.ObjetoId);
        Assert.Equal([Sesiones.MotivoDeRenovacionReutilizada], e.AlertasDeSeguridad.DeSeguridad);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-12")]
    public async Task UnaFallaAlPublicarLaAlerta_NoCambiaElRechazoNiDeshaceLaRevocacion()
    {
        var e = new EntornoDeIdentidad();
        var (sesion, _) = await Ingresar(e);
        var gestor = e.Gestor();
        await gestor.Renovar(sesion.Renovacion);
        e.AlertasDeSeguridad.Falla = true;

        var reutilizada = await gestor.Renovar(sesion.Renovacion);

        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, reutilizada.Error);
        Assert.All(e.Bd.Renovaciones, r => Assert.NotNull(r.RevocadaEn));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-12")]
    public async Task DosRenovacionesSimultaneasDeLaMismaRenovacion_SoloUnaRota_YLaOtraEsUnaReutilizacion()
    {
        var e = new EntornoDeIdentidad();
        var (sesion, _) = await Ingresar(e);
        var gestor = e.Gestor();

        var resultados = await Task.WhenAll(gestor.Renovar(sesion.Renovacion), gestor.Renovar(sesion.Renovacion));

        Assert.Equal(1, resultados.Count(r => r.EsExito));
        Assert.Contains(e.Seguridad.Eventos, ev => ev.Accion == AccionesAuditables.RenovacionReutilizada);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-07")]
    public async Task UnaRenovacionDesconocida_SeRechazaConElMismoFallo_SinDatos()
    {
        var e = new EntornoDeIdentidad();

        var resultado = await e.Gestor().Renovar(new ValorSecreto("no-existe-" + new string('q', 33)));

        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, resultado.Error);
        Assert.Empty(e.Seguridad.Eventos);
        Assert.Empty(e.Emisor.Emitidas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-12")]
    public async Task CerrarSesion_RevocaLaRenovacionYSuFamilia_YEsIdempotente_SinRevelarSiExistia()
    {
        var e = new EntornoDeIdentidad();
        var (sesion, _) = await Ingresar(e);
        var gestor = e.Gestor();
        var siguiente = (await gestor.Renovar(sesion.Renovacion)).Valor;

        var cierre = await gestor.Cerrar(siguiente.Renovacion);
        var repetido = await gestor.Cerrar(siguiente.Renovacion);
        var desconocida = await gestor.Cerrar(new ValorSecreto("no-existe-" + new string('q', 33)));
        var luego = await gestor.Renovar(siguiente.Renovacion);

        Assert.True(cierre.EsExito);
        Assert.True(repetido.EsExito);
        Assert.True(desconocida.EsExito);
        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, luego.Error);
        Assert.All(e.Bd.Renovaciones, r => Assert.NotNull(r.RevocadaEn));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-50")]
    public async Task CerrarTodas_RevocaLasTresSesionesDelUsuario_YAvanzaElCorte_PeroNoLasDeOtro()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("ana@example.com");
        var otra = e.Cuenta("otra@example.com");
        var sesiones = new List<CredencialDeSesion>();
        for (var i = 0; i < 3; i++)
        {
            sesiones.Add(await e.Sesiones.Nueva(cuenta.Usuario, e.Reloj.Ahora, default));
        }

        var ajena = await e.Sesiones.Nueva(otra.Usuario, e.Reloj.Ahora, default);
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddMinutes(5);
        var gestor = e.Gestor();
        e.Actor.Actual = ActorActual.DeUsuario(cuenta.Usuario.Id, "cliente");

        var cierre = await gestor.CerrarTodas(cuenta.Usuario.Id);

        Assert.True(cierre.EsExito);
        Assert.NotNull(cuenta.Usuario.SesionesValidasDesde);
        foreach (var sesion in sesiones)
        {
            Assert.Equal(FallosDeIdentidad.SesionNoRenovable, (await gestor.Renovar(sesion.Renovacion)).Error);
        }

        Assert.True((await gestor.Renovar(ajena.Renovacion)).EsExito);
        var nueva = await e.Sesiones.Nueva(cuenta.Usuario, e.Reloj.Ahora, default);
        Assert.True((await gestor.Renovar(nueva.Renovacion)).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-40")]
    public async Task UnaRenovacionEmitidaAntesDelCorte_NoServiaAunqueNoSeHayaRevocado()
    {
        var e = new EntornoDeIdentidad();
        var (sesion, cuenta) = await Ingresar(e);
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddMinutes(1);
        cuenta.Usuario.AvanzarElCorteDeSesiones(e.Reloj.Ahora);

        var resultado = await e.Gestor().Renovar(sesion.Renovacion);

        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, resultado.Error);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-40")]
    public async Task CerrarTodasDeUnaCuentaInexistente_EsNoEncontrada()
    {
        var e = new EntornoDeIdentidad();
        var id = Guid.NewGuid();
        e.Actor.Actual = ActorActual.DeUsuario(id, "cliente");

        var resultado = await e.Gestor().CerrarTodas(id);

        Assert.Equal(CategoriaDeError.NoEncontrado, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-50")]
    public async Task CerrarTodas_SoloLoHaceLaPropiaPersona_ElActorAjenoOAnonimoSeRechazaYSeRegistra()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("ana@example.com");
        var sesion = await e.Sesiones.Nueva(cuenta.Usuario, e.Reloj.Ahora, default);
        var gestor = e.Gestor();

        e.Actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), "administrador-central");
        var ajeno = await gestor.CerrarTodas(cuenta.Usuario.Id);
        e.Actor.Actual = ActorActual.Anonimo();
        var anonimo = await gestor.CerrarTodas(cuenta.Usuario.Id);
        e.Actor.Actual = null;
        var sinContexto = await gestor.CerrarTodas(cuenta.Usuario.Id);

        Assert.All(new[] { ajeno, anonimo, sinContexto }, r => Assert.Equal(CategoriaDeError.Prohibido, r.Error!.Categoria));
        Assert.Equal(3, e.Denegaciones.Registradas.Count);
        Assert.All(e.Denegaciones.Registradas, d => Assert.Equal("recurso-ajeno", d.Motivo));
        Assert.Null(e.Bd.Renovaciones.Single().RevocadaEn);
        Assert.NotNull(sesion);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-40")]
    public async Task RevocarLasDemas_ConservaLaSesionActual_YTodasAvanzaElCorte()
    {
        var e = new EntornoDeIdentidad();
        var cuenta = e.Cuenta("ana@example.com");
        var actual = await e.Sesiones.Nueva(cuenta.Usuario, e.Reloj.Ahora, default);
        var otra = await e.Sesiones.Nueva(cuenta.Usuario, e.Reloj.Ahora, default);
        var familiaActual = e.Bd.Renovaciones.Single(r => r.Huella == e.HuellasDeIdentidad.DeValorOpaco(actual.Renovacion.Revelar())).Familia;

        await e.Revocacion.LasDemas(cuenta, familiaActual, default);

        Assert.Null(e.Bd.Renovaciones.Single(r => r.Familia == familiaActual).RevocadaEn);
        Assert.NotNull(e.Bd.Renovaciones.Single(r => r.Huella == e.HuellasDeIdentidad.DeValorOpaco(otra.Renovacion.Revelar())).RevocadaEn);

        await e.Revocacion.Todas(cuenta, default);

        Assert.All(e.Bd.Renovaciones, r => Assert.NotNull(r.RevocadaEn));
        Assert.Equal(EntornoDeIdentidad.Inicio, cuenta.Usuario.SesionesValidasDesde);
    }
}
