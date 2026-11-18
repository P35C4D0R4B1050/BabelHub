using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Domain.Traslados;

namespace BabelHub.Inventario.Application.Traslados.Interno;

/// <summary>
/// <see cref="IGeneradorDeTrasladosDePedido"/> (SEL-03, TRA-21): <c>Reservas</c> lo invoca <b>dentro de la transacción de la confirmación</b> con una solicitud por fuente y edición cuya fuente no es la sucursal de recojo. Crea un
/// traslado <c>solicitado</c> vinculado al pedido por cada una, con la fecha estimada del plazo de su ruta de ese momento (o «por confirmar» si la ruta no está definida, y sin cambiarla si la ruta cambia después), y devuelve los
/// traslados del pedido, <b>creados o ya existentes</b>: repetir la confirmación no crea otro (índice único parcial, <c>INSERT … ON CONFLICT DO NOTHING</c>, sin abortar la transacción). <b>No pasa por <see cref="PermisosDeTraslado"/></b>
/// ni consume el presupuesto del personal: <c>Reservas</c> ya autorizó al llamador y el actor es el del contexto (el sistema, una integración o el usuario de la confirmación), que es el que audita. No depende de
/// <c>IMovimientosDeStock</c> ni de <c>IReservasParaTraslados</c> (no cierra el ciclo de inyección) y no es alcanzable desde la Api.
/// </summary>
internal sealed class GenerarTrasladosDePedido(
    IActorActual actores,
    IDirectorioDeFuentes fuentes,
    IRepositorioDeTraslados traslados,
    IBuzonDeSalida buzon,
    IAuditoria auditoria,
    IReloj reloj) : IGeneradorDeTrasladosDePedido
{
    public async Task<IReadOnlyList<TrasladoVinculadoAlPedido>> Generar(Guid pedidoId, Guid sucursalDeRecojoId, IReadOnlyCollection<SolicitudDeTrasladoDePedido> solicitudes, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(solicitudes);
        if (pedidoId == Guid.Empty || sucursalDeRecojoId == Guid.Empty)
        {
            throw new ArgumentException("El pedido y la sucursal de recojo son obligatorios.");
        }

        if (solicitudes.Count == 0)
        {
            return [];
        }

        var destino = await fuentes.PorId(sucursalDeRecojoId, cancelacion).ConfigureAwait(false)
            ?? throw new InvalidOperationException("La sucursal de recojo del pedido no existe.");
        // Solo una persona del personal queda como solicitante (el plan §3 dice «identificador de personal»): el id de un cliente nunca se guarda aquí.
        var solicitadoPor = actores.Actual is { Tipo: TipoDeActor.Usuario, UsuarioId: { } id, Rol: RolesDelActor.AdministradorCentral or RolesDelActor.EncargadoDeSucursal or RolesDelActor.PersonalDeLogistica } && id != Guid.Empty ? id : (Guid?)null;
        var resultado = new List<TrasladoVinculadoAlPedido>();

        // Un orden determinista (fuente y edición) hace reproducible la respuesta; el bloqueo del pedido de la confirmación ya serializa a quien inserta sobre estos índices.
        foreach (var solicitud in solicitudes.OrderBy(s => s.FuenteDeOrigenId).ThenBy(s => s.EdicionId))
        {
            var origen = await fuentes.PorId(solicitud.FuenteDeOrigenId, cancelacion).ConfigureAwait(false)
                ?? throw new InvalidOperationException("La fuente de origen de un traslado del pedido no existe.");
            var traslado = await Crear(pedidoId, solicitud, origen, destino, solicitadoPor, cancelacion).ConfigureAwait(false);
            resultado.Add(new TrasladoVinculadoAlPedido(traslado.Id, traslado.EdicionId, traslado.Cantidad, origen.Codigo, destino.Codigo, traslado.Estado.Identificador(), traslado.FechaEstimada));
        }

        return resultado;
    }

    private async Task<Traslado> Crear(Guid pedidoId, SolicitudDeTrasladoDePedido solicitud, FuenteDeStock origen, FuenteDeStock destino, Guid? solicitadoPor, CancellationToken cancelacion)
    {
        var existente = await traslados.VinculadoActivo(pedidoId, solicitud.EdicionId, origen.Id, cancelacion).ConfigureAwait(false);
        if (existente is not null)
        {
            return existente;
        }

        var plazo = await fuentes.PlazoDeRuta(origen.Id, destino.Id, cancelacion).ConfigureAwait(false);
        var ahora = reloj.Ahora;
        var nuevo = Traslado.VincularAUnPedido(Guid.NewGuid(), solicitud.EdicionId, origen.Id, destino.Id, solicitud.Cantidad, pedidoId, EstimacionDeLlegada.Calcular(reloj.HoyEnLima, plazo), solicitadoPor, ahora);

        if (!await traslados.InsertarVinculado(nuevo, cancelacion).ConfigureAwait(false))
        {
            // Otra confirmación del mismo pedido lo creó entre la lectura y la inserción: se devuelve el que existe.
            return await traslados.VinculadoActivo(pedidoId, solicitud.EdicionId, origen.Id, cancelacion).ConfigureAwait(false)
                ?? throw new InvalidOperationException("El traslado vinculado del pedido chocó con uno que ya no existe.");
        }

        await auditoria.Registrar(ConversionDeTraslados.Auditar(nuevo, estadoAnterior: null), cancelacion).ConfigureAwait(false);
        await buzon.Insertar(ConversionDeTraslados.Mensajes(nuevo, origen.Codigo, destino.Codigo, ahora), cancelacion).ConfigureAwait(false);
        return nuevo;
    }
}
