using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Shared.Contratos;

namespace BabelHub.Inventario.UnitTests.Soporte;

/// <summary>
/// Usuario actual de prueba: implementa el puerto <see cref="IActorActual"/> para las pruebas de casos de uso mientras el módulo Identidad no
/// emite credenciales reales (decisión de la fase 3, <c>01-flujo-sdd.md</c>). Solo existe en las pruebas: ninguna Api lo registra y una
/// cabecera de actor nunca reemplaza el contexto de un usuario (SEG-15).
/// </summary>
public sealed class ActorDePrueba : IActorActual
{
    private ActorDePrueba(ActorActual? actor) => Actual = actor;

    public ActorActual? Actual { get; private set; }

    /// <summary>Cambia el actor durante la prueba (por ejemplo, de un cliente a la administración central).</summary>
    public void Establecer(ActorActual? actor) => Actual = actor;

    public static ActorDePrueba DeCliente(Guid? usuarioId = null) => new(ActorActual.DeUsuario(usuarioId ?? Guid.NewGuid(), RolesDeUsuario.Cliente));

    public static ActorDePrueba DeEncargadoDe(Guid sucursalId, Guid? usuarioId = null) =>
        new(ActorActual.DeUsuario(usuarioId ?? Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, sucursalId));

    public static ActorDePrueba DePersonalDeLogistica(Guid? usuarioId = null) =>
        new(ActorActual.DeUsuario(usuarioId ?? Guid.NewGuid(), RolesDeUsuario.PersonalDeLogistica));

    public static ActorDePrueba DeAdministradorCentral(Guid? usuarioId = null) =>
        new(ActorActual.DeUsuario(usuarioId ?? Guid.NewGuid(), RolesDeUsuario.AdministradorCentral));

    public static ActorDePrueba DelSistema() => new(ActorActual.DelSistema());

    public static ActorDePrueba DeIntegracion(string nombre) => new(ActorActual.DeIntegracion(nombre));

    public static ActorDePrueba Anonimo() => new(ActorActual.Anonimo());

    /// <summary>Nadie estableció el contexto: toda llamada que lo exija se rechaza (SEG-15).</summary>
    public static ActorDePrueba SinContexto() => new(null);
}
