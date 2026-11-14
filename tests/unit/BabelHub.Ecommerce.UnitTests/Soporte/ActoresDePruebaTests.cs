using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Contratos;

namespace BabelHub.Ecommerce.UnitTests.Soporte;

/// <summary>
/// El usuario actual de prueba (puerto <see cref="IActorActual"/>) que usan las pruebas de casos de uso mientras el módulo Identidad no
/// emite credenciales reales: un actor por rol, con la sucursal solo para el encargado (IDE-04, IDE-05).
/// </summary>
public sealed class ActoresDePruebaTests
{
    [Fact]
    public void Un_cliente_es_un_usuario_con_el_rol_cliente_y_sin_sucursal()
    {
        var actor = ActorDePrueba.DeCliente().Exigir();

        Assert.Equal(TipoDeActor.Usuario, actor.Tipo);
        Assert.Equal(RolesDeUsuario.Cliente, actor.Rol);
        Assert.NotEqual(Guid.Empty, actor.UsuarioId);
        Assert.Null(actor.SucursalId);
    }

    [Fact]
    public void Un_encargado_lleva_exactamente_su_sucursal()
    {
        var sucursal = Guid.NewGuid();

        var actor = ActorDePrueba.DeEncargadoDe(sucursal).Exigir();

        Assert.Equal(RolesDeUsuario.EncargadoDeSucursal, actor.Rol);
        Assert.Equal(sucursal, actor.SucursalId);
    }

    [Fact]
    public void Logistica_y_administracion_central_no_tienen_sucursal()
    {
        var logistica = ActorDePrueba.DePersonalDeLogistica().Exigir();
        var administrador = ActorDePrueba.DeAdministradorCentral().Exigir();

        Assert.Equal(RolesDeUsuario.PersonalDeLogistica, logistica.Rol);
        Assert.Equal(RolesDeUsuario.AdministradorCentral, administrador.Rol);
        Assert.Null(logistica.SucursalId);
        Assert.Null(administrador.SucursalId);
    }

    [Fact]
    public void El_sistema_y_una_integracion_no_son_personas()
    {
        Assert.Equal(TipoDeActor.Sistema, ActorDePrueba.DelSistema().Exigir().Tipo);
        var integracion = ActorDePrueba.DeIntegracion("culqi").Exigir();

        Assert.Equal(TipoDeActor.Integracion, integracion.Tipo);
        Assert.Null(integracion.UsuarioId);
    }

    [Fact]
    public void Un_visitante_es_un_actor_con_contexto_y_la_ausencia_de_contexto_se_rechaza()
    {
        Assert.Equal(TipoDeActor.Anonimo, ActorDePrueba.Anonimo().Exigir().Tipo);

        var sinContexto = ActorDePrueba.SinContexto();

        Assert.Null(sinContexto.Actual);
        Assert.Throws<ContextoDeActorAusenteException>(() => sinContexto.Exigir());
    }

    [Fact]
    public void Se_puede_cambiar_de_usuario_durante_la_prueba_y_los_roles_son_los_cuatro_de_la_spec()
    {
        var actores = ActorDePrueba.DeCliente();

        actores.Establecer(ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.AdministradorCentral));

        Assert.Equal(RolesDeUsuario.AdministradorCentral, actores.Exigir().Rol);
        Assert.Equal(["cliente", "encargado-de-sucursal", "personal-de-logistica", "administrador-central"], RolesDeUsuario.Todos);
    }
}
