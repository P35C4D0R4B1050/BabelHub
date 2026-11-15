using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Inventario.Infrastructure.Auditoria;

/// <summary>
/// <see cref="IHistorialLocal"/> sobre <c>auditoria.registros</c> (T25; AUD-08, AJU-17): los registros de ajuste y de traslado en que la sucursal indicada es origen <b>o destino</b>. El filtro por sucursal
/// va en la propia consulta, con parámetros, nunca con el valor de la solicitud sin pasar por la Aplicación (que ya comparó la sucursal del token); la paginación, el orden y la instantánea son las de
/// la auditoría.
/// </summary>
public sealed class PostgresHistorialLocal(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion) : IHistorialLocal
{
    /// <summary>Las acciones que forman el historial de una sucursal: los ajustes (y el reingreso por devolución, que es un ajuste) y cada transición de un traslado.</summary>
    public static readonly IReadOnlyList<string> AccionesDelHistorial =
    [
        nameof(AccionesAuditables.AjusteRegistrado),
        nameof(AccionesAuditables.ReingresoPorDevolucion),
        nameof(AccionesAuditables.TrasladoSolicitado),
        nameof(AccionesAuditables.TrasladoAprobado),
        nameof(AccionesAuditables.TrasladoRechazado),
        nameof(AccionesAuditables.TrasladoCancelado),
        nameof(AccionesAuditables.TrasladoDespachado),
        nameof(AccionesAuditables.TrasladoRecibido),
        nameof(AccionesAuditables.GuiaDeTrasladoRegistrada),
    ];

    public Task<PaginaDeRegistros> Consultar(Guid sucursal, string? cursor, CancellationToken cancelacion = default) =>
        ConsultaDePaginas.Consultar(
            unidad,
            conexion,
            new FiltroDeAuditoria(),
            cursor,
            (
                "(r.sucursal_origen = @h_sucursal OR r.sucursal_destino = @h_sucursal) AND r.accion = ANY(@h_acciones)",
                parametros =>
                {
                    parametros.AddWithValue("h_sucursal", sucursal.ToString("D"));
                    parametros.Add(new NpgsqlParameter("h_acciones", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = AccionesDelHistorial.ToArray() });
                }),
            cancelacion);
}
