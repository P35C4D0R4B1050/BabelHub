namespace BabelHub.Inventario.Application.Plataforma;

/// <summary>
/// Los roles de usuario tal como los trae el actor verificado (IDE-04). Son los mismos textos que <c>RolesDeUsuario</c> de <c>BabelHub.Shared</c>, que la
/// Aplicación no puede referenciar (AGENTS.md regla 6): una prueba compara ambos para que no se separen.
/// </summary>
public static class RolesDelActor
{
    public const string Cliente = "cliente";

    public const string EncargadoDeSucursal = "encargado-de-sucursal";

    public const string PersonalDeLogistica = "personal-de-logistica";

    public const string AdministradorCentral = "administrador-central";

    /// <summary>El actor es un usuario con el rol de administración central.</summary>
    public static bool EsAdministracionCentral(this ActorActual actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.Tipo == TipoDeActor.Usuario && actor.Rol == AdministradorCentral;
    }

    /// <summary>El actor es un encargado de sucursal; <c>SucursalId</c> puede faltar (un encargado sin sucursal no tiene alcance sobre ninguna fuente).</summary>
    public static bool EsEncargado(this ActorActual actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.Tipo == TipoDeActor.Usuario && actor.Rol == EncargadoDeSucursal;
    }

    /// <summary>El actor es un encargado cuya sucursal es exactamente <paramref name="fuenteId"/> (FUE-08). Un <c>SucursalId</c> nulo nunca es comodín.</summary>
    public static bool EsEncargadoDe(this ActorActual actor, Guid fuenteId)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.EsEncargado() && actor.SucursalId is { } sucursal && sucursal != Guid.Empty && sucursal == fuenteId;
    }
}
