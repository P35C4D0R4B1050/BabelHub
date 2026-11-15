using BabelHub.Inventario.Api.Plataforma;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Inventario;

namespace BabelHub.Inventario.Api.Fuentes;

/// <summary>
/// Establece <b>el actor de integración</b> en las llamadas que solo traen la credencial de servicio (E-commerce → Inventario): sin él, el alcance de datos de la transacción no tiene valor y las políticas de
/// <c>stock</c> devuelven cero filas <i>sin error</i>, que parece «sin stock» (hallazgo del 2026-11-14, <c>inventario-fuentes</c> D09). Va después de <c>UsarCredencialDeServicio</c> y del middleware de contexto de
/// usuario, y <b>solo</b> actúa si el endpoint declaró su operación, la credencial ya se verificó y es de un alcance que no es «operación de usuario». Una operación de usuario no recibe un actor de integración
/// jamás: su actor sale del token verificado y, si falta, el middleware de contexto de usuario ya la rechazó con 401 (SEG-15). Un actor ya establecido no se pisa.
/// </summary>
public sealed class ActorDeServicioMiddleware(RequestDelegate siguiente)
{
    public async Task InvokeAsync(HttpContext contexto, IActorActual actores, IEstablecedorDeActor establecedor)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(actores);
        ArgumentNullException.ThrowIfNull(establecedor);

        var declarada = contexto.GetEndpoint()?.Metadata.GetMetadata<OperacionDeServicioAttribute>();
        if (declarada is not null
            && actores.Actual is null
            && contexto.Items[CredencialDeServicioMiddleware.ClaveDelAlcance] is AlcanceDeServicio alcance
            && alcance != AlcanceDeServicio.OperacionDeUsuario)
        {
            establecedor.Establecer(ActorActual.DeIntegracion(alcance.NombreEstable()));
        }

        await siguiente(contexto).ConfigureAwait(false);
    }
}

/// <summary>La matriz operación → alcance de la credencial de las operaciones de <c>Fuentes</c>, tomada del catálogo de contratos v1 (una operación declara un solo alcance, rechazo por defecto).</summary>
public static class MatrizDeFuentes
{
    /// <summary>Los nombres de las operaciones que implementa <see cref="EndpointsDeFuentes"/>.</summary>
    public static IReadOnlyList<string> Operaciones { get; } =
    [
        "inventario.ediciones.registrar", "inventario.disponibilidad.consultar", "inventario.disponibilidad.consultar-varias", "inventario.sucursales.listar", "inventario.sucursales.contacto",
        "inventario.fuentes.listar", "inventario.fuentes.consultar", "inventario.fuentes.registrar", "inventario.fuentes.editar", "inventario.fuentes.designar-recepcion",
        "inventario.fuentes.existencias", "inventario.rutas.listar", "inventario.rutas.consultar", "inventario.rutas.definir", "inventario.unidades-vendidas.consultar",
    ];

    public static IEnumerable<(OperacionDeServicio Operacion, AlcanceDeServicio Alcance)> Declaraciones() =>
        Operaciones.Select(nombre =>
        {
            var contrato = OperacionesDelInventario.Todas.Single(o => o.Nombre == nombre);
            return (new OperacionDeServicio(nombre), AlcanceDe(contrato.Alcance));
        });

    private static AlcanceDeServicio AlcanceDe(string alcance) => alcance switch
    {
        AlcancesDeCredencial.OperacionDeUsuario => AlcanceDeServicio.OperacionDeUsuario,
        AlcancesDeCredencial.LecturaDeDisponibilidad => AlcanceDeServicio.LecturaDeDisponibilidad,
        AlcancesDeCredencial.LecturaDeContacto => AlcanceDeServicio.LecturaDeContacto,
        _ => throw new ArgumentOutOfRangeException(nameof(alcance), "Una operación de Fuentes usa un alcance que este módulo no conoce."),
    };
}

public static class ActorDeServicioExtensiones
{
    /// <summary>Va después de <c>UsarContextoDeUsuarioDeServicio</c>.</summary>
    public static IApplicationBuilder UsarActorDeServicio(this IApplicationBuilder app) => app.UseMiddleware<ActorDeServicioMiddleware>();
}
