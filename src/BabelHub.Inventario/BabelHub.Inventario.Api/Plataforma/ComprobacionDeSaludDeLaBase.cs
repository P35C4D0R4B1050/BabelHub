using BabelHub.Inventario.Infrastructure.Plataforma;

namespace BabelHub.Inventario.Api.Plataforma;

/// <summary>El componente <c>base-de-datos</c> de <c>/health/ready</c>: delega en <see cref="ComprobacionDeLaBaseDeDatos"/> (la Api solo la presenta). Sin esta comprobación el servicio diría «listo» con la base caída.</summary>
public sealed class ComprobacionDeSaludDeLaBase(ComprobacionDeLaBaseDeDatos comprobacion) : IComprobacionDeSalud
{
    public const string NombreDelComponente = "base-de-datos";

    public string Nombre => NombreDelComponente;

    public Task<bool> Comprobar(CancellationToken cancelacion) => comprobacion.Responde(cancelacion);
}
