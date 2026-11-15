using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>El usuario, sus roles y las reglas de administración (T02; AC-IDE-04, AC-IDE-05, AC-IDE-44, AC-IDE-66; IDE-04, IDE-05, IDE-06, IDE-49, IDE-87).</summary>
public sealed class UsuariosTests
{
    private static readonly Guid Sucursal = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid OtraSucursal = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");

    private static Usuario Persona(RolDeUsuario rol = RolDeUsuario.Cliente, bool verificado = true, bool activo = true, Guid? sucursal = null) =>
        Usuario.Restaurar(Guid.NewGuid(), $"u{Guid.NewGuid():N}@correo.pe", rol, sucursal, verificado, activo);

    [Fact]
    [Trait("Criterio", "AC-IDE-04")]
    public void Un_usuario_nuevo_es_cliente_activo_y_con_el_correo_sin_verificar_y_normalizado()
    {
        var usuario = Usuario.NuevoCliente(Guid.NewGuid(), "  Ana.Perez@Correo.PE ");

        Assert.Equal("ana.perez@correo.pe", usuario.Correo);
        Assert.Equal(RolDeUsuario.Cliente, usuario.Rol);
        Assert.True(usuario.Activo);
        Assert.False(usuario.CorreoVerificado);
        Assert.Null(usuario.Sucursal);
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-04")]
    [InlineData(RolDeUsuario.EncargadoDeSucursal, false)]
    [InlineData(RolDeUsuario.Cliente, true)]
    [InlineData(RolDeUsuario.Logistica, true)]
    [InlineData(RolDeUsuario.AdministradorCentral, true)]
    public void Un_encargado_sin_sucursal_o_otro_rol_con_sucursal_se_rechaza(RolDeUsuario rol, bool conSucursal)
    {
        var cliente = Persona();

        var decision = cliente.CambiarRol(rol, conSucursal ? Sucursal : null);

        Assert.Equal(MotivoDeRechazoDeUsuario.SucursalInvalida, decision.Motivo);
        Assert.Equal(RolDeUsuario.Cliente, cliente.Rol);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-04")]
    public void Un_encargado_con_una_sucursal_se_acepta_y_el_guid_vacio_no_cuenta_como_sucursal()
    {
        var usuario = Persona();

        Assert.Equal(MotivoDeRechazoDeUsuario.SucursalInvalida, usuario.CambiarRol(RolDeUsuario.EncargadoDeSucursal, Guid.Empty).Motivo);
        Assert.True(usuario.CambiarRol(RolDeUsuario.EncargadoDeSucursal, Sucursal).EsPermitida);
        Assert.Equal(Sucursal, usuario.Sucursal);
        Assert.True(usuario.CambiarRol(RolDeUsuario.Logistica, null).EsPermitida);
        Assert.Null(usuario.Sucursal);
        Assert.Throws<ArgumentException>(() => Usuario.Restaurar(Guid.NewGuid(), "a@b.pe", RolDeUsuario.EncargadoDeSucursal, null, true, true));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-66")]
    public void Un_rol_de_personal_exige_el_correo_verificado()
    {
        var sinVerificar = Persona(verificado: false);
        var verificado = Persona(verificado: true);

        Assert.Equal(MotivoDeRechazoDeUsuario.CorreoSinVerificar, sinVerificar.CambiarRol(RolDeUsuario.Logistica, null).Motivo);
        Assert.Equal(RolDeUsuario.Cliente, sinVerificar.Rol);
        Assert.True(verificado.CambiarRol(RolDeUsuario.Logistica, null).EsPermitida);

        sinVerificar.VerificarCorreo();
        Assert.True(sinVerificar.CambiarRol(RolDeUsuario.Logistica, null).EsPermitida);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-05")]
    public void Un_segundo_encargado_activo_de_la_misma_sucursal_se_rechaza_indicando_quien_la_tiene()
    {
        var admin = Persona(RolDeUsuario.AdministradorCentral);
        var otroAdmin = Persona(RolDeUsuario.AdministradorCentral);
        var titular = Guid.NewGuid();
        var nuevo = Persona();

        var decision = ReglasDeAdministracion.CambiarRolODesactivar(admin, nuevo, RolDeUsuario.EncargadoDeSucursal, Sucursal, administradoresActivos: 2, new EncargadoDeSucursal(titular, Sucursal));

        Assert.Equal(MotivoDeRechazoDeUsuario.SucursalYaTieneEncargado, decision.Motivo);
        Assert.Equal(titular, decision.EncargadoActual);
        Assert.Equal(RolDeUsuario.Cliente, nuevo.Rol);

        // Otra sucursal, o el mismo encargado reasignado a su sucursal, sí se permite.
        Assert.True(ReglasDeAdministracion.CambiarRolODesactivar(admin, nuevo, RolDeUsuario.EncargadoDeSucursal, OtraSucursal, 2, new EncargadoDeSucursal(titular, Sucursal)).EsPermitida);
        Assert.True(ReglasDeAdministracion.CambiarRolODesactivar(otroAdmin, nuevo, RolDeUsuario.EncargadoDeSucursal, Sucursal, 2, new EncargadoDeSucursal(nuevo.Id, Sucursal)).EsPermitida);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-44")]
    public void Un_administrador_no_cambia_ni_desactiva_su_propio_usuario_ni_restablece_su_propio_segundo_factor()
    {
        var admin = Persona(RolDeUsuario.AdministradorCentral);

        Assert.Equal(MotivoDeRechazoDeUsuario.SobreSiMismo, ReglasDeAdministracion.CambiarRolODesactivar(admin, admin, RolDeUsuario.Cliente, null, 2, null).Motivo);
        Assert.Equal(MotivoDeRechazoDeUsuario.SobreSiMismo, ReglasDeAdministracion.CambiarRolODesactivar(admin, admin, null, null, 2, null).Motivo);
        Assert.Equal(MotivoDeRechazoDeUsuario.SobreSiMismo, ReglasDeAdministracion.Restablecer(admin, admin).Motivo);
        Assert.True(ReglasDeAdministracion.Restablecer(admin, Persona()).EsPermitida);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-44")]
    public void No_se_desactiva_ni_se_degrada_al_ultimo_administrador_activo()
    {
        var actor = Persona(RolDeUsuario.AdministradorCentral);
        var ultimo = Persona(RolDeUsuario.AdministradorCentral);

        Assert.Equal(MotivoDeRechazoDeUsuario.UltimoAdministrador, ReglasDeAdministracion.CambiarRolODesactivar(actor, ultimo, null, null, administradoresActivos: 1, null).Motivo);
        Assert.Equal(MotivoDeRechazoDeUsuario.UltimoAdministrador, ReglasDeAdministracion.CambiarRolODesactivar(actor, ultimo, RolDeUsuario.Logistica, null, 1, null).Motivo);
        Assert.True(ReglasDeAdministracion.CambiarRolODesactivar(actor, ultimo, null, null, 2, null).EsPermitida);
        Assert.True(ReglasDeAdministracion.CambiarRolODesactivar(actor, ultimo, RolDeUsuario.AdministradorCentral, null, 1, null).EsPermitida);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-44")]
    public void Solo_un_administrador_central_activo_administra()
    {
        var cliente = Persona();
        var logistica = Persona(RolDeUsuario.Logistica);
        var adminInactivo = Persona(RolDeUsuario.AdministradorCentral, activo: false);
        var objetivo = Persona();

        Assert.Equal(MotivoDeRechazoDeUsuario.NoEsAdministrador, ReglasDeAdministracion.CambiarRolODesactivar(cliente, objetivo, RolDeUsuario.Logistica, null, 2, null).Motivo);
        Assert.Equal(MotivoDeRechazoDeUsuario.NoEsAdministrador, ReglasDeAdministracion.Restablecer(logistica, objetivo).Motivo);
        Assert.Equal(MotivoDeRechazoDeUsuario.NoEsAdministrador, ReglasDeAdministracion.Restablecer(adminInactivo, objetivo).Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-66")]
    public void La_administracion_aplica_las_reglas_del_usuario_sin_modificarlo_si_solo_consulta()
    {
        var admin = Persona(RolDeUsuario.AdministradorCentral);
        var sinVerificar = Persona(verificado: false);

        var decision = ReglasDeAdministracion.CambiarRolODesactivar(admin, sinVerificar, RolDeUsuario.Logistica, null, 2, null);

        Assert.Equal(MotivoDeRechazoDeUsuario.CorreoSinVerificar, decision.Motivo);
        Assert.Equal(RolDeUsuario.Cliente, sinVerificar.Rol);
        var valido = Persona();
        Assert.True(ReglasDeAdministracion.CambiarRolODesactivar(admin, valido, RolDeUsuario.Logistica, null, 2, null).EsPermitida);
        Assert.Equal(RolDeUsuario.Cliente, valido.Rol);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-04")]
    public void Un_usuario_desactivado_no_cambia_de_rol_y_la_vigencia_de_la_renovacion_depende_del_rol()
    {
        var inactivo = Persona(activo: false);

        Assert.Equal(MotivoDeRechazoDeUsuario.CuentaNoActiva, inactivo.CambiarRol(RolDeUsuario.Logistica, null).Motivo);
        Assert.Equal(TimeSpan.FromDays(7), RolDeUsuario.Cliente.VigenciaDeLaRenovacion());
        Assert.Equal(TimeSpan.FromHours(8), RolDeUsuario.EncargadoDeSucursal.VigenciaDeLaRenovacion());
        Assert.Equal(TimeSpan.FromHours(8), RolDeUsuario.AdministradorCentral.VigenciaDeLaRenovacion());
        Assert.True(RolDeUsuario.Logistica.EsDePersonal());
        Assert.False(RolDeUsuario.Cliente.EsDePersonal());
    }
}
