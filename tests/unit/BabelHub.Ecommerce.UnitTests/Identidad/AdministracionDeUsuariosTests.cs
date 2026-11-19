using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// La administración de usuarios (identidad-seguridad T19; AC-IDE-04, AC-IDE-05, AC-IDE-06, AC-IDE-21, AC-IDE-35, AC-IDE-44, AC-IDE-66; IDE-05 a IDE-07, IDE-23, IDE-38, IDE-44, IDE-49, IDE-87): solo el administrador central
/// activo opera, las reglas del Dominio se aplican, cada cambio se audita en la misma transacción y la contraseña temporal se muestra una sola vez y vence a las 24 horas.
/// </summary>
public sealed class AdministracionDeUsuariosTests
{
    private static ValorSecreto S(string texto) => new(texto);

    private static (EntornoDeIdentidad E, CuentaDeIdentidad Admin, AdministracionDeUsuarios Administracion) Escenario()
    {
        var e = new EntornoDeIdentidad();
        var admin = e.ActuarComoAdministrador();
        return (e, admin, e.Administracion());
    }

    // ---------------------------------------------------------------- crear

    [Fact]
    [Trait("Criterio", "AC-IDE-21")]
    [Trait("Criterio", "AC-IDE-06")]
    public async Task Crear_UnEncargado_DaUnaContrasenaTemporalDe24Horas_YAudita_SinRegistrarLaContrasena()
    {
        var (e, admin, administracion) = Escenario();
        var sucursal = IdentificadorDeSucursal.DesdeCodigo("ayacucho")!.Value;

        var creado = await administracion.Crear(admin.Usuario.Id, "  Beatriz  ", "Beatriz@Example.com", RolDeCuenta.EncargadoDeSucursal, "  Ayacucho ");

        Assert.True(creado.EsExito);
        Assert.Equal("beatriz@example.com", creado.Valor.Usuario.Correo);
        Assert.Equal("Beatriz", creado.Valor.Usuario.Nombre);
        Assert.Equal(RolDeCuenta.EncargadoDeSucursal, creado.Valor.Usuario.Rol);
        Assert.Equal(sucursal, creado.Valor.Usuario.Sucursal);
        Assert.True(creado.Valor.Usuario.Activo);
        Assert.True(creado.Valor.Usuario.CorreoVerificado);
        Assert.Equal(e.Reloj.Ahora.AddHours(24), creado.Valor.VenceEn);
        Assert.True(creado.Valor.ContrasenaTemporal.Revelar().Length >= 20);

        var cuenta = e.Bd.Cuentas[creado.Valor.Usuario.UsuarioId];
        Assert.True(cuenta.DebeCambiarLaTemporal);
        Assert.Equal(e.Reloj.Ahora.AddHours(24), cuenta.ContrasenaTemporalHasta);

        var auditada = Assert.Single(e.Auditoria.Registradas);
        Assert.Equal(AccionesAuditables.UsuarioDadoDeAlta, auditada.Accion);
        Assert.Equal("usuario", auditada.ObjetoTipo);
        Assert.Equal(nameof(RolDeUsuario.EncargadoDeSucursal), auditada.Detalle["rolAsignado"]);
        Assert.DoesNotContain(creado.Valor.ContrasenaTemporal.Revelar(), string.Join('|', auditada.Detalle.Values), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-21")]
    public async Task LaContrasenaTemporal_PermiteIngresar_ObligaACambiarla_YVenceALas24Horas()
    {
        var (e, admin, administracion) = Escenario();
        var creado = (await administracion.Crear(admin.Usuario.Id, "Luis", "luis@example.com", RolDeCuenta.Logistica, null)).Valor;
        var temporal = creado.ContrasenaTemporal;

        var dentro = await e.Ingreso().Ingresar(new SolicitudDeIngreso("luis@example.com", temporal));
        Assert.Equal(PasoPendienteDelIngreso.CambiarContrasenaTemporal, dentro.Valor.Desafio!.Paso);
        Assert.Null(dentro.Valor.Credencial);

        // Sigue valida hasta vencer aunque no se cambie.
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddHours(23);
        Assert.True((await e.Ingreso().Ingresar(new SolicitudDeIngreso("luis@example.com", temporal))).EsExito);

        // Vencida, se rechaza como cualquier otra credencial inválida.
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddHours(24);
        var vencida = await e.Ingreso().Ingresar(new SolicitudDeIngreso("luis@example.com", temporal));
        Assert.Equal(FallosDeIdentidad.CredencialesInvalidas, vencida.Error);
    }

    [Theory]
    [InlineData(RolDeCuenta.EncargadoDeSucursal, false)]
    [InlineData(RolDeCuenta.Logistica, true)]
    [InlineData(RolDeCuenta.AdministradorCentral, true)]
    [Trait("Criterio", "AC-IDE-04")]
    public async Task Crear_ConUnaSucursalQueNoCorresponde_AlRol_SeRechaza(RolDeCuenta rol, bool conSucursal)
    {
        var (e, admin, administracion) = Escenario();

        var resultado = await administracion.Crear(admin.Usuario.Id, "Persona", "otra@example.com", rol, conSucursal ? "lima" : null);

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Single(e.Bd.Cuentas);
        Assert.Empty(e.Auditoria.Registradas);
    }

    [Fact]
    public async Task Crear_UnCliente_SeRechaza_PorqueLosClientesSeRegistranSolos()
    {
        var (_, admin, administracion) = Escenario();

        var resultado = await administracion.Crear(admin.Usuario.Id, "Persona", "otra@example.com", RolDeCuenta.Cliente, null);

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
    }

    [Theory]
    [InlineData("   ", "otra@example.com")]
    [InlineData("Persona", "no-es-un-correo")]
    public async Task Crear_ConNombreOCorreoInvalidos_SeRechaza(string nombre, string correo)
    {
        var (_, admin, administracion) = Escenario();

        var resultado = await administracion.Crear(admin.Usuario.Id, nombre, correo, RolDeCuenta.Logistica, null);

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-05")]
    public async Task Crear_UnEncargado_EnUnaSucursalOcupada_SeRechazaIndicandoQuienLaTiene()
    {
        var (e, admin, administracion) = Escenario();
        var sucursal = IdentificadorDeSucursal.DesdeCodigo("lima")!.Value;
        var actual = e.Cuenta("actual@example.com", RolDeUsuario.EncargadoDeSucursal, sucursal: sucursal);

        var resultado = await administracion.Crear(admin.Usuario.Id, "Otro", "otro@example.com", RolDeCuenta.EncargadoDeSucursal, "lima");

        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
        Assert.Equal("sucursal-con-encargado", resultado.Error.Clave);
        Assert.Equal(actual.Usuario.Id.ToString("D"), Assert.Single(resultado.Error.Errores).Clave);
        Assert.DoesNotContain(e.Bd.Cuentas.Values, c => c.Usuario.Correo == "otro@example.com");
    }

    [Fact]
    public async Task Crear_ConUnCorreoYaUsado_EsUnConflicto()
    {
        var (e, admin, administracion) = Escenario();
        e.Cuenta("existe@example.com");

        var resultado = await administracion.Crear(admin.Usuario.Id, "Otro", "EXISTE@example.com", RolDeCuenta.Logistica, null);

        Assert.Equal(FallosDeIdentidad.CorreoEnUso, resultado.Error);
    }

    [Fact]
    public async Task Crear_SiLaAuditoriaFalla_NoSeCreaNada()
    {
        var (e, admin, administracion) = Escenario();
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => administracion.Crear(admin.Usuario.Id, "Persona", "otra@example.com", RolDeCuenta.Logistica, null));

        Assert.Single(e.Bd.Cuentas);
    }

    // ---------------------------------------------------------------- quién puede

    [Fact]
    [Trait("Criterio", "AC-IDE-06")]
    public async Task CualquierOperacion_DeQuienNoEsAdministrador_EsProhibida_YQuedaRegistrada()
    {
        var e = new EntornoDeIdentidad();
        var encargado = e.Cuenta("encargado@example.com", RolDeUsuario.EncargadoDeSucursal);
        var objetivo = e.Cuenta("objetivo@example.com", RolDeUsuario.Logistica);
        e.Actor.Actual = ActorActual.DeUsuario(encargado.Usuario.Id, "encargado-de-sucursal");
        var administracion = e.Administracion();
        var id = encargado.Usuario.Id;

        var errores = new[]
        {
            (await administracion.Crear(id, "Persona", "otra@example.com", RolDeCuenta.Logistica, null)).Error,
            (await administracion.CambiarElRol(id, objetivo.Usuario.Id, RolDeCuenta.AdministradorCentral, null)).Error,
            (await administracion.Desactivar(id, objetivo.Usuario.Id)).Error,
            (await administracion.Reactivar(id, objetivo.Usuario.Id)).Error,
            (await administracion.RestablecerLaContrasena(id, objetivo.Usuario.Id)).Error,
            (await administracion.RestablecerElSegundoFactor(id, objetivo.Usuario.Id)).Error,
        };

        Assert.All(errores, error => Assert.Equal(AlcanceDeLaCuenta.Prohibido, error));
        Assert.Equal(errores.Length, e.Denegaciones.Registradas.Count);
        Assert.Equal(RolDeUsuario.Logistica, objetivo.Usuario.Rol);
        Assert.True(objetivo.Usuario.Activo);
        Assert.Empty(e.Auditoria.Registradas);
    }

    [Fact]
    public async Task UnAdministradorQueYaNoEstaActivo_NoPuedeOperarAunqueSuCredencialSigaVigente()
    {
        var (e, admin, administracion) = Escenario();
        var objetivo = e.Cuenta("objetivo@example.com", RolDeUsuario.Logistica);
        admin.Usuario.Desactivar();

        var resultado = await administracion.Desactivar(admin.Usuario.Id, objetivo.Usuario.Id);

        Assert.Equal(AlcanceDeLaCuenta.Prohibido, resultado.Error);
        Assert.True(objetivo.Usuario.Activo);
    }

    [Fact]
    public async Task UnAdministrador_NoPuedeActuarEnNombreDeOtro()
    {
        var (e, _, administracion) = Escenario();
        var otroAdmin = e.Cuenta("otro-admin@example.com", RolDeUsuario.AdministradorCentral);
        var objetivo = e.Cuenta("objetivo@example.com", RolDeUsuario.Logistica);

        var resultado = await administracion.Desactivar(otroAdmin.Usuario.Id, objetivo.Usuario.Id);

        Assert.Equal(AlcanceDeLaCuenta.Prohibido, resultado.Error);
    }

    // ---------------------------------------------------------------- cambiar el rol

    [Fact]
    [Trait("Criterio", "AC-IDE-66")]
    public async Task AsignarUnRolDePersonal_SoloSiElCorreoEstaVerificado()
    {
        var (e, admin, administracion) = Escenario();
        var sinVerificar = e.Cuenta("sin@example.com", verificada: false);
        var verificado = e.Cuenta("con@example.com");

        var rechazado = await administracion.CambiarElRol(admin.Usuario.Id, sinVerificar.Usuario.Id, RolDeCuenta.Logistica, null);
        var asignado = await administracion.CambiarElRol(admin.Usuario.Id, verificado.Usuario.Id, RolDeCuenta.Logistica, null);

        Assert.Equal("correo-sin-verificar", rechazado.Error!.Clave);
        Assert.Equal(RolDeUsuario.Cliente, sinVerificar.Usuario.Rol);
        Assert.True(asignado.EsExito);
        Assert.Equal(RolDeCuenta.Logistica, asignado.Valor.Rol);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-06")]
    public async Task CambiarElRol_Audita_RevocaLasSesiones_YElRolNuevoSeExigeEnUnIngresoCompleto()
    {
        var (e, admin, administracion) = Escenario();
        var objetivo = e.Cuenta("persona@example.com");
        var sesion = await e.Sesiones.Nueva(objetivo.Usuario, e.Reloj.Ahora, default);
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddMinutes(5);

        var cambio = await administracion.CambiarElRol(admin.Usuario.Id, objetivo.Usuario.Id, RolDeCuenta.EncargadoDeSucursal, "cusco");

        Assert.True(cambio.EsExito);
        var auditada = Assert.Single(e.Auditoria.Registradas);
        Assert.Equal(AccionesAuditables.CambioDeRol, auditada.Accion);
        Assert.Equal("asignacion-de-administracion", auditada.Motivo);
        Assert.Equal(nameof(RolDeUsuario.Cliente), auditada.Detalle["rolAnterior"]);
        Assert.Equal(nameof(RolDeUsuario.EncargadoDeSucursal), auditada.Detalle["rolNuevo"]);
        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, (await e.Gestor().Renovar(sesion.Renovacion)).Error);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-04")]
    public async Task CambiarElRol_ADeEncargadoSinSucursal_OAOtroRolConSucursal_SeRechaza()
    {
        var (e, admin, administracion) = Escenario();
        var objetivo = e.Cuenta("persona@example.com");

        var sinSucursal = await administracion.CambiarElRol(admin.Usuario.Id, objetivo.Usuario.Id, RolDeCuenta.EncargadoDeSucursal, null);
        var conSucursal = await administracion.CambiarElRol(admin.Usuario.Id, objetivo.Usuario.Id, RolDeCuenta.Logistica, "cusco");

        Assert.Equal(CategoriaDeError.Validacion, sinSucursal.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, conSucursal.Error!.Categoria);
        Assert.Equal(RolDeUsuario.Cliente, objetivo.Usuario.Rol);
        Assert.Empty(e.Auditoria.Registradas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-05")]
    public async Task CambiarElRol_ASegundoEncargadoDeUnaSucursal_SeRechazaIndicandoQuienLaTiene()
    {
        var (e, admin, administracion) = Escenario();
        var sucursal = IdentificadorDeSucursal.DesdeCodigo("lima")!.Value;
        var actual = e.Cuenta("actual@example.com", RolDeUsuario.EncargadoDeSucursal, sucursal: sucursal);
        var candidato = e.Cuenta("candidato@example.com", RolDeUsuario.Logistica);

        var resultado = await administracion.CambiarElRol(admin.Usuario.Id, candidato.Usuario.Id, RolDeCuenta.EncargadoDeSucursal, "lima");

        Assert.Equal("sucursal-con-encargado", resultado.Error!.Clave);
        Assert.Equal(actual.Usuario.Id.ToString("D"), Assert.Single(resultado.Error.Errores).Clave);
        Assert.Equal(RolDeUsuario.Logistica, candidato.Usuario.Rol);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-44")]
    public async Task UnAdministrador_NoPuedeCambiarSuPropioRol_NiDegradarseASiMismo()
    {
        var (e, admin, administracion) = Escenario();
        e.Cuenta("otro-admin@example.com", RolDeUsuario.AdministradorCentral);

        var resultado = await administracion.CambiarElRol(admin.Usuario.Id, admin.Usuario.Id, RolDeCuenta.Logistica, null);

        Assert.Equal("sobre-si-mismo", resultado.Error!.Clave);
        Assert.Equal(RolDeUsuario.AdministradorCentral, admin.Usuario.Rol);
    }

    [Fact]
    public async Task CambiarElRol_SinCambio_NoAuditaNiRevoca()
    {
        var (e, admin, administracion) = Escenario();
        var objetivo = e.Cuenta("persona@example.com", RolDeUsuario.Logistica);

        var resultado = await administracion.CambiarElRol(admin.Usuario.Id, objetivo.Usuario.Id, RolDeCuenta.Logistica, null);

        Assert.True(resultado.EsExito);
        Assert.Empty(e.Auditoria.Registradas);
    }

    // ---------------------------------------------------------------- desactivar y reactivar

    [Fact]
    [Trait("Criterio", "AC-IDE-06")]
    public async Task Desactivar_AplicaElCambio_Audita_RevocaLasSesiones_CancelaLosAvisos_YNoDejaIngresar()
    {
        var (e, admin, administracion) = Escenario();
        var objetivo = e.Cuenta("persona@example.com", RolDeUsuario.Logistica);
        var sesion = await e.Sesiones.Nueva(objetivo.Usuario, e.Reloj.Ahora, default);

        var resultado = await administracion.Desactivar(admin.Usuario.Id, objetivo.Usuario.Id);

        Assert.True(resultado.EsExito);
        Assert.False(resultado.Valor.Activo);
        Assert.False(objetivo.Usuario.Activo);
        Assert.Equal(AccionesAuditables.UsuarioDesactivado, Assert.Single(e.Auditoria.Registradas).Accion);
        Assert.Contains(objetivo.Usuario.Id, e.CancelacionDeAvisos.Cancelados);
        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, (await e.Gestor().Renovar(sesion.Renovacion)).Error);
        Assert.Equal(FallosDeIdentidad.CredencialesInvalidas, (await e.Ingreso().Ingresar(new SolicitudDeIngreso("persona@example.com", S(EntornoDeIdentidad.ContrasenaSegura)))).Error);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-44")]
    public async Task UnAdministrador_NoPuedeDesactivarseASiMismo()
    {
        var (e, admin, administracion) = Escenario();
        e.Cuenta("otro-admin@example.com", RolDeUsuario.AdministradorCentral);

        var resultado = await administracion.Desactivar(admin.Usuario.Id, admin.Usuario.Id);

        Assert.Equal("sobre-si-mismo", resultado.Error!.Clave);
        Assert.True(admin.Usuario.Activo);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-44")]
    public async Task Desactivar_AlUltimoAdministradorActivo_SeRechaza()
    {
        var (e, admin, administracion) = Escenario();
        var otro = e.Cuenta("otro-admin@example.com", RolDeUsuario.AdministradorCentral);
        // Una desactivación simultánea ya dejó a uno solo: la lectura bajo bloqueo cuenta 1.
        e.Cuentas.ForzarAdministradoresActivos = 1;

        var resultado = await administracion.Desactivar(admin.Usuario.Id, otro.Usuario.Id);

        Assert.Equal("ultimo-administrador", resultado.Error!.Clave);
        Assert.True(otro.Usuario.Activo);
    }

    [Fact]
    public async Task Desactivar_UnaCuentaYaDesactivada_EsUnConflicto()
    {
        var (e, admin, administracion) = Escenario();
        var objetivo = e.Cuenta("persona@example.com", RolDeUsuario.Logistica, activa: false);

        var resultado = await administracion.Desactivar(admin.Usuario.Id, objetivo.Usuario.Id);

        Assert.Equal("cuenta-no-activa", resultado.Error!.Clave);
    }

    [Fact]
    public async Task Desactivar_SiLaAuditoriaFalla_NoSeAplicaNada()
    {
        var (e, admin, administracion) = Escenario();
        var objetivo = e.Cuenta("persona@example.com", RolDeUsuario.Logistica);
        var sesion = await e.Sesiones.Nueva(objetivo.Usuario, e.Reloj.Ahora, default);
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => administracion.Desactivar(admin.Usuario.Id, objetivo.Usuario.Id));

        Assert.True(objetivo.Usuario.Activo);
        Assert.True((await e.Gestor().Renovar(sesion.Renovacion)).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-06")]
    public async Task Reactivar_AplicaElCambio_YAudita()
    {
        var (e, admin, administracion) = Escenario();
        var objetivo = e.Cuenta("persona@example.com", RolDeUsuario.Logistica, activa: false);

        var resultado = await administracion.Reactivar(admin.Usuario.Id, objetivo.Usuario.Id);

        Assert.True(resultado.EsExito);
        Assert.True(objetivo.Usuario.Activo);
        Assert.Equal(AccionesAuditables.UsuarioReactivado, Assert.Single(e.Auditoria.Registradas).Accion);
    }

    [Fact]
    public async Task Reactivar_UnaCuentaActivaOCancelada_EsUnConflicto()
    {
        var (e, admin, administracion) = Escenario();
        var activa = e.Cuenta("activa@example.com", RolDeUsuario.Logistica);
        var cancelada = e.Cuenta("cancelada@example.com", RolDeUsuario.Logistica, activa: false);
        cancelada.CanceladaEn = EntornoDeIdentidad.Inicio.AddDays(-1);

        Assert.Equal("cuenta-no-activa", (await administracion.Reactivar(admin.Usuario.Id, activa.Usuario.Id)).Error!.Clave);
        Assert.Equal("cuenta-no-activa", (await administracion.Reactivar(admin.Usuario.Id, cancelada.Usuario.Id)).Error!.Clave);
        Assert.False(cancelada.Usuario.Activo);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-05")]
    public async Task Reactivar_UnEncargadoCuyaSucursalYaTieneOtro_SeRechaza()
    {
        var (e, admin, administracion) = Escenario();
        var sucursal = Guid.NewGuid();
        var anterior = e.Cuenta("anterior@example.com", RolDeUsuario.EncargadoDeSucursal, activa: false, sucursal: sucursal);
        var nuevo = e.Cuenta("nuevo@example.com", RolDeUsuario.EncargadoDeSucursal, sucursal: sucursal);

        var resultado = await administracion.Reactivar(admin.Usuario.Id, anterior.Usuario.Id);

        Assert.Equal("sucursal-con-encargado", resultado.Error!.Clave);
        Assert.Equal(nuevo.Usuario.Id.ToString("D"), Assert.Single(resultado.Error.Errores).Clave);
        Assert.False(anterior.Usuario.Activo);
    }

    // ---------------------------------------------------------------- restablecer la contraseña

    [Fact]
    [Trait("Criterio", "AC-IDE-21")]
    [Trait("Criterio", "AC-IDE-40")]
    public async Task RestablecerLaContrasena_DaUnaTemporal_RevocaTodasLasRenovaciones_Audita_Avisa_YObligaACambiarla()
    {
        var (e, admin, administracion) = Escenario();
        var encargado = e.Cuenta("encargado@example.com", RolDeUsuario.EncargadoDeSucursal);
        var sesion = await e.Sesiones.Nueva(encargado.Usuario, e.Reloj.Ahora, default);
        var otra = await e.Sesiones.Nueva(encargado.Usuario, e.Reloj.Ahora, default);
        var anterior = encargado.ResumenDeAcceso;

        var resultado = await administracion.RestablecerLaContrasena(admin.Usuario.Id, encargado.Usuario.Id);

        Assert.True(resultado.EsExito);
        Assert.NotEqual(anterior, encargado.ResumenDeAcceso);
        Assert.True(encargado.DebeCambiarLaTemporal);
        Assert.Equal(e.Reloj.Ahora.AddHours(24), resultado.Valor.VenceEn);
        Assert.Equal(AccionesAuditables.ContrasenaRestablecidaPorAdministracion, Assert.Single(e.Auditoria.Registradas).Accion);
        Assert.Equal(AvisosDeCuenta.PlantillaDeContrasenaCambiada, Assert.Single(e.Correo.Correos).Plantilla);
        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, (await e.Gestor().Renovar(sesion.Renovacion)).Error);
        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, (await e.Gestor().Renovar(otra.Renovacion)).Error);

        var ingreso = await e.Ingreso().Ingresar(new SolicitudDeIngreso("encargado@example.com", resultado.Valor.ContrasenaTemporal));
        Assert.Equal(PasoPendienteDelIngreso.CambiarContrasenaTemporal, ingreso.Valor.Desafio!.Paso);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-21")]
    public async Task UnaTemporalVencida_SoloLaAdministracionLaRestablece_YLaNuevaSirve()
    {
        var (e, admin, administracion) = Escenario();
        var creado = (await administracion.Crear(admin.Usuario.Id, "Luis", "luis@example.com", RolDeCuenta.Logistica, null)).Valor;
        e.Reloj.Ahora = EntornoDeIdentidad.Inicio.AddHours(25);
        Assert.Equal(FallosDeIdentidad.CredencialesInvalidas, (await e.Ingreso().Ingresar(new SolicitudDeIngreso("luis@example.com", creado.ContrasenaTemporal))).Error);

        var nueva = await administracion.RestablecerLaContrasena(admin.Usuario.Id, creado.Usuario.UsuarioId);

        Assert.True((await e.Ingreso().Ingresar(new SolicitudDeIngreso("luis@example.com", nueva.Valor.ContrasenaTemporal))).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-44")]
    public async Task RestablecerLaContrasena_Propia_SeRechaza()
    {
        var (_, admin, administracion) = Escenario();

        var resultado = await administracion.RestablecerLaContrasena(admin.Usuario.Id, admin.Usuario.Id);

        Assert.Equal("sobre-si-mismo", resultado.Error!.Clave);
    }

    [Fact]
    public async Task RestablecerLaContrasena_DeUnCliente_SeRechaza_PorqueLaAdministracionNuncaLaConoce()
    {
        var (e, admin, administracion) = Escenario();
        var cliente = e.Cuenta("cliente@example.com");
        var antes = cliente.ResumenDeAcceso;

        var resultado = await administracion.RestablecerLaContrasena(admin.Usuario.Id, cliente.Usuario.Id);

        Assert.Equal("solo-personal", resultado.Error!.Clave);
        Assert.Equal(antes, cliente.ResumenDeAcceso);
    }

    [Fact]
    public async Task RestablecerLaContrasena_DeUnaCuentaDesactivada_SeRechaza()
    {
        var (e, admin, administracion) = Escenario();
        var inactiva = e.Cuenta("persona@example.com", RolDeUsuario.Logistica, activa: false);

        var resultado = await administracion.RestablecerLaContrasena(admin.Usuario.Id, inactiva.Usuario.Id);

        Assert.Equal("cuenta-no-activa", resultado.Error!.Clave);
    }

    [Fact]
    public async Task RestablecerLaContrasena_SiLaAuditoriaFalla_NoCambiaNada()
    {
        var (e, admin, administracion) = Escenario();
        var encargado = e.Cuenta("encargado@example.com", RolDeUsuario.EncargadoDeSucursal);
        var antes = encargado.ResumenDeAcceso;
        e.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => administracion.RestablecerLaContrasena(admin.Usuario.Id, encargado.Usuario.Id));

        Assert.Equal(antes, encargado.ResumenDeAcceso);
        Assert.False(encargado.DebeCambiarLaTemporal);
        Assert.Empty(e.Correo.Correos);
    }

    // ---------------------------------------------------------------- restablecer el segundo factor

    [Fact]
    [Trait("Criterio", "AC-IDE-35")]
    public async Task RestablecerElSegundoFactor_BorraLaConfiguracion_RevocaTodas_Avisa_Audita_YElSiguienteIngresoLoConfigura()
    {
        var (e, admin, administracion) = Escenario();
        var encargado = e.Cuenta("encargado@example.com", RolDeUsuario.EncargadoDeSucursal);
        e.ConSegundoFactor(encargado);
        var sesion = await e.Sesiones.Nueva(encargado.Usuario, e.Reloj.Ahora, default);

        var resultado = await administracion.RestablecerElSegundoFactor(admin.Usuario.Id, encargado.Usuario.Id);

        Assert.True(resultado.EsExito);
        Assert.Null(await e.SegundoFactor.Obtener(encargado.Usuario.Id));
        Assert.Equal(FallosDeIdentidad.SesionNoRenovable, (await e.Gestor().Renovar(sesion.Renovacion)).Error);
        Assert.Equal(AccionesAuditables.SegundoFactorRestablecidoPorAdministracion, Assert.Single(e.Auditoria.Registradas).Accion);
        Assert.Equal(AvisosDeCuenta.PlantillaDeSegundoFactorRestablecido, Assert.Single(e.Correo.Correos).Plantilla);

        var ingreso = await e.Ingreso().Ingresar(new SolicitudDeIngreso("encargado@example.com", S(EntornoDeIdentidad.ContrasenaSegura)));
        Assert.Equal(PasoPendienteDelIngreso.ConfigurarSegundoFactor, ingreso.Valor.Desafio!.Paso);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-44")]
    public async Task RestablecerElSegundoFactor_Propio_SeRechaza_YNoSeBorraNada()
    {
        var (e, admin, administracion) = Escenario();
        e.ConSegundoFactor(admin);

        var resultado = await administracion.RestablecerElSegundoFactor(admin.Usuario.Id, admin.Usuario.Id);

        Assert.Equal("sobre-si-mismo", resultado.Error!.Clave);
        Assert.NotNull(await e.SegundoFactor.Obtener(admin.Usuario.Id));
        Assert.Empty(e.Auditoria.Registradas);
    }

    [Fact]
    public async Task RestablecerElSegundoFactor_SiElCorreoFalla_ElRestablecimientoYaEstaHecho()
    {
        var (e, admin, administracion) = Escenario();
        var encargado = e.Cuenta("encargado@example.com", RolDeUsuario.EncargadoDeSucursal);
        e.ConSegundoFactor(encargado);
        e.Correo.Falla = true;

        var resultado = await administracion.RestablecerElSegundoFactor(admin.Usuario.Id, encargado.Usuario.Id);

        Assert.True(resultado.EsExito);
        Assert.Null(await e.SegundoFactor.Obtener(encargado.Usuario.Id));
    }

    [Fact]
    public async Task UnRolFueraDelCatalogo_SeRechazaComoValidacion_SinLanzar()
    {
        var (e, admin, administracion) = Escenario();
        var objetivo = e.Cuenta("persona@example.com", RolDeUsuario.Logistica);

        var crear = await administracion.Crear(admin.Usuario.Id, "Persona", "otra@example.com", (RolDeCuenta)99, null);
        var cambiar = await administracion.CambiarElRol(admin.Usuario.Id, objetivo.Usuario.Id, (RolDeCuenta)99, null);

        Assert.Equal(CategoriaDeError.Validacion, crear.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, cambiar.Error!.Categoria);
    }

    [Fact]
    public async Task QuienNoEsAdministrador_QuedaRegistradoComoFueraDeRol()
    {
        var e = new EntornoDeIdentidad();
        var encargado = e.Cuenta("encargado@example.com", RolDeUsuario.EncargadoDeSucursal);
        e.Actor.Actual = ActorActual.DeUsuario(encargado.Usuario.Id, "encargado-de-sucursal");

        await e.Administracion().Desactivar(encargado.Usuario.Id, Guid.NewGuid());
        await e.Solicitudes().Listar(encargado.Usuario.Id);

        Assert.All(e.Denegaciones.Registradas, d => Assert.Equal("fuera-de-rol", d.Motivo));
        Assert.Equal(2, e.Denegaciones.Registradas.Count);
    }

    // ---------------------------------------------------------------- D20: la sucursal del encargado se asigna por código

    [Theory]
    [InlineData("Ayacucho", "ayacucho")]
    [InlineData("  LIMA  ", "lima")]
    [Trait("Criterio", "AC-IDE-05")]
    public async Task Crear_UnEncargado_PorCodigo_GuardaElIdentificadorDerivado_YLoEmiteEnLaCredencial(string escrito, string canonico)
    {
        var (e, admin, administracion) = Escenario();

        var creado = await administracion.Crear(admin.Usuario.Id, "Beatriz", "beatriz@example.com", RolDeCuenta.EncargadoDeSucursal, escrito);

        var esperado = IdentificadorDeSucursal.DesdeCodigo(canonico);
        Assert.True(creado.EsExito);
        Assert.Equal(esperado, creado.Valor.Usuario.Sucursal);
        Assert.Equal(esperado, (await e.Cuentas.PorId(creado.Valor.Usuario.UsuarioId, paraActualizar: false, default))!.Usuario.Sucursal);
        Assert.Equal(1, e.Inventario.Consultas);
    }

    [Theory]
    [InlineData("no-existe", "validacion-invalida", "desconocida")]
    [InlineData("lima central", "validacion-invalida", "invalida")]
    [InlineData("../lima", "validacion-invalida", "invalida")]
    [Trait("Criterio", "AC-IDE-05")]
    public async Task Crear_UnEncargado_ConUnCodigoQueNoEsDeUnaSucursalDelInventario_SeRechaza_SinCrearLaCuenta(string codigo, string claveEsperada, string claveDeCampo)
    {
        var (e, admin, administracion) = Escenario();

        var resultado = await administracion.Crear(admin.Usuario.Id, "Beatriz", "beatriz@example.com", RolDeCuenta.EncargadoDeSucursal, codigo);

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal(claveEsperada, resultado.Error.Clave);
        Assert.Equal(claveDeCampo, Assert.Single(resultado.Error.Errores!).Clave);
        Assert.Null(await e.Cuentas.PorCorreo("beatriz@example.com", default));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-05")]
    public async Task AsignarUnaSucursal_SiElInventarioNoResponde_FallaCerrado_NuncaSeAsignaSinComprobar()
    {
        var (e, admin, administracion) = Escenario();
        var objetivo = e.Cuenta("persona@example.com");
        var rolOriginal = objetivo.Usuario.Rol;
        e.Inventario.NoResponde = true;

        var crear = await administracion.Crear(admin.Usuario.Id, "Beatriz", "beatriz@example.com", RolDeCuenta.EncargadoDeSucursal, "lima");
        var cambiar = await administracion.CambiarElRol(admin.Usuario.Id, objetivo.Usuario.Id, RolDeCuenta.EncargadoDeSucursal, "lima");

        Assert.Equal("servicio-no-disponible", crear.Error!.Clave);
        Assert.Equal("servicio-no-disponible", cambiar.Error!.Clave);
        Assert.Equal(rolOriginal, (await e.Cuentas.PorId(objetivo.Usuario.Id, paraActualizar: false, default))!.Usuario.Rol);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-05")]
    public async Task CambiarElRol_AEncargado_PorCodigo_GuardaElIdentificadorDerivado()
    {
        var (e, admin, administracion) = Escenario();
        var objetivo = e.Cuenta("persona@example.com");

        var cambio = await administracion.CambiarElRol(admin.Usuario.Id, objetivo.Usuario.Id, RolDeCuenta.EncargadoDeSucursal, "cusco");

        Assert.True(cambio.EsExito);
        Assert.Equal(IdentificadorDeSucursal.DesdeCodigo("cusco"), cambio.Valor.Sucursal);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-05")]
    public async Task UnRolQueNoLlevaSucursal_NoConsultaAlInventario_YRechazaElCodigo()
    {
        var (e, admin, administracion) = Escenario();

        var resultado = await administracion.Crear(admin.Usuario.Id, "Luis", "luis@example.com", RolDeCuenta.Logistica, "lima");

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal("sucursal", Assert.Single(resultado.Error.Errores).Campo);
        Assert.Equal(0, e.Inventario.Consultas);
    }

    // ---------------------------------------------------------------- listar (WEB-35)

    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task Listar_SinCorreo_EntregaSoloElPersonal_OrdenadoPorCorreo_YNuncaLosClientes()
    {
        var (e, admin, administracion) = Escenario();
        e.Cuenta("zeta@example.com", RolDeUsuario.Logistica);
        e.Cuenta("ana@example.com", RolDeUsuario.Logistica);
        e.Cuenta("cliente@example.com", RolDeUsuario.Cliente);

        var pagina = (await administracion.Listar(admin.Usuario.Id, new FiltroDeUsuarios())).Valor;

        Assert.Equal(["admin@example.com", "ana@example.com", "zeta@example.com"], pagina.Usuarios.Select(u => u.Correo));
        Assert.DoesNotContain(pagina.Usuarios, u => u.Rol == RolDeCuenta.Cliente);
        Assert.Null(pagina.Siguiente);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task Listar_UnClienteSoloSeEncuentraPorSuCorreoExacto()
    {
        var (e, admin, administracion) = Escenario();
        e.Cuenta("cliente@example.com", RolDeUsuario.Cliente);

        var exacto = (await administracion.Listar(admin.Usuario.Id, new FiltroDeUsuarios(Correo: " Cliente@Example.com "))).Valor;
        var otro = (await administracion.Listar(admin.Usuario.Id, new FiltroDeUsuarios(Correo: "otro@example.com"))).Valor;
        var porRol = await administracion.Listar(admin.Usuario.Id, new FiltroDeUsuarios(Rol: RolDeCuenta.Cliente));

        Assert.Equal("cliente@example.com", Assert.Single(exacto.Usuarios).Correo);
        Assert.Empty(otro.Usuarios);
        Assert.Equal("cliente-solo-por-correo", Assert.Single(porRol.Error!.Errores).Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task Listar_PaginaPorCursor_YFiltraPorRolYEstado()
    {
        var (e, admin, administracion) = Escenario();
        e.Cuenta("a@example.com", RolDeUsuario.Logistica);
        e.Cuenta("b@example.com", RolDeUsuario.Logistica);
        e.Cuenta("c@example.com", RolDeUsuario.Logistica, activa: false);

        var primera = (await administracion.Listar(admin.Usuario.Id, new FiltroDeUsuarios(Rol: RolDeCuenta.Logistica, Tamano: 2))).Valor;
        var segunda = (await administracion.Listar(admin.Usuario.Id, new FiltroDeUsuarios(Rol: RolDeCuenta.Logistica, Tamano: 2, Despues: primera.Siguiente))).Valor;
        var inactivos = (await administracion.Listar(admin.Usuario.Id, new FiltroDeUsuarios(Activo: false))).Valor;

        Assert.Equal(["a@example.com", "b@example.com"], primera.Usuarios.Select(u => u.Correo));
        Assert.Equal("b@example.com", primera.Siguiente);
        Assert.Equal(["c@example.com"], segunda.Usuarios.Select(u => u.Correo));
        Assert.Null(segunda.Siguiente);
        Assert.Equal(["c@example.com"], inactivos.Usuarios.Select(u => u.Correo));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task Listar_ElTamanoSeAcota_YUnFiltroInvalidoSeRechaza()
    {
        var (e, admin, administracion) = Escenario();
        for (var i = 0; i < 55; i++)
        {
            e.Cuenta($"p{i:D2}@example.com", RolDeUsuario.Logistica);
        }

        var grande = (await administracion.Listar(admin.Usuario.Id, new FiltroDeUsuarios(Tamano: 500))).Valor;
        var porOmision = (await administracion.Listar(admin.Usuario.Id, new FiltroDeUsuarios(Tamano: 0))).Valor;
        var rolInvalido = await administracion.Listar(admin.Usuario.Id, new FiltroDeUsuarios(Rol: (RolDeCuenta)99));
        var correoInvalido = await administracion.Listar(admin.Usuario.Id, new FiltroDeUsuarios(Correo: "no es un correo"));
        var cursorLargo = await administracion.Listar(admin.Usuario.Id, new FiltroDeUsuarios(Despues: new string('a', 255)));

        Assert.Equal(50, grande.Usuarios.Count);
        Assert.NotNull(grande.Siguiente);
        Assert.Equal(20, porOmision.Usuarios.Count);
        Assert.All(new[] { rolInvalido, correoInvalido, cursorLargo }, r => Assert.Equal(CategoriaDeError.Validacion, r.Error!.Categoria));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-29")]
    public async Task Listar_SoloLoHaceElAdministradorCentralActivo()
    {
        var (e, _, administracion) = Escenario();
        var encargado = e.Cuenta("encargado@example.com", RolDeUsuario.EncargadoDeSucursal, sucursal: Guid.NewGuid());
        e.Actor.Actual = ActorActual.DeUsuario(encargado.Usuario.Id, "encargado-de-sucursal");

        var resultado = await administracion.Listar(encargado.Usuario.Id, new FiltroDeUsuarios());

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Single(e.Denegaciones.Registradas);
    }
}
