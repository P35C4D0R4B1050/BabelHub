using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;

namespace BabelHub.Ecommerce.Infrastructure.Web;

/// <summary>
/// Implementación provisional de <see cref="IEstadoDeCuentaDeLaWeb"/> hasta que exista <c>IEstadoDeCuenta</c> de Identidad (R12): no sabe nada de nadie, así que responde
/// <see cref="EstadoDeAceptacionDeTextos.Desconocido"/> y la web falla cerrada (ninguna persona con sesión pasa del «servicio no disponible» salvo cerrar la sesión y leer los textos). Un registro real
/// anterior se respeta.
/// </summary>
public sealed class EstadoDeCuentaSinIdentidad : IEstadoDeCuentaDeLaWeb
{
    public Task<EstadoDeAceptacionDeTextos> Aceptacion(Guid usuarioId, CancellationToken cancelacion = default) => Task.FromResult(EstadoDeAceptacionDeTextos.Desconocido);
}

/// <summary>Implementación provisional de <see cref="IAceptacionDeTextosEnIdentidad"/>: nada se acepta mientras Identidad no exista.</summary>
public sealed class AceptacionDeTextosSinIdentidad : IAceptacionDeTextosEnIdentidad
{
    public Task<ResultadoDeAceptacion> Aceptar(Guid usuarioId, string versionMostrada, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeAceptacion.NoDisponible);
}
