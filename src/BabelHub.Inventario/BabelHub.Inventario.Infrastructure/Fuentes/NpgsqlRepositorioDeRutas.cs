using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Domain.Fuentes;
using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Fuentes;

/// <summary>
/// <see cref="IRepositorioDeRutas"/> sobre PostgreSQL. <c>Definir</c> devuelve el plazo anterior <b>leído bajo el bloqueo de la fila</b> (FUE-14): primero intenta insertar (<c>ON CONFLICT DO NOTHING</c>; si se
/// insertó, no había anterior) y, si la ruta ya existía, la lee con <c>FOR UPDATE</c> y la actualiza. En <c>READ COMMITTED</c> cada sentencia ve lo que confirmó otro administrador, así que con dos cambios
/// simultáneos cada uno guarda en la auditoría el valor que de verdad reemplazó, incluso en la primera alta simultánea de una ruta.
/// </summary>
internal sealed class NpgsqlRepositorioDeRutas(AccesoAFuentes acceso) : IRepositorioDeRutas
{
    public async Task<RutaEntreFuentes?> Obtener(Guid origenId, Guid destinoId, CancellationToken cancelacion = default) =>
        (await Leer("SELECT origen_id, destino_id, plazo_dias FROM fuentes.rutas WHERE origen_id = @o AND destino_id = @d", [("o", origenId), ("d", destinoId)], cancelacion).ConfigureAwait(false))
        .FirstOrDefault();

    public Task<IReadOnlyList<RutaEntreFuentes>> HaciaDestino(Guid destinoId, CancellationToken cancelacion = default) =>
        Leer("SELECT origen_id, destino_id, plazo_dias FROM fuentes.rutas WHERE destino_id = @d ORDER BY plazo_dias, origen_id", [("d", destinoId)], cancelacion);

    public Task<IReadOnlyList<RutaEntreFuentes>> Todas(CancellationToken cancelacion = default) =>
        Leer("SELECT origen_id, destino_id, plazo_dias FROM fuentes.rutas ORDER BY origen_id, destino_id", [], cancelacion);

    public Task<int?> Definir(RutaEntreFuentes ruta, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(ruta);

        return acceso.ConAlcanceDelActor<int?>(
            async (conexion, transaccion, token) =>
            {
                await using (var insertar = new NpgsqlCommand(
                    "INSERT INTO fuentes.rutas (origen_id, destino_id, plazo_dias) VALUES (@o, @d, @plazo) ON CONFLICT (origen_id, destino_id) DO NOTHING",
                    conexion,
                    transaccion))
                {
                    insertar.Parameters.AddWithValue("o", ruta.OrigenId);
                    insertar.Parameters.AddWithValue("d", ruta.DestinoId);
                    insertar.Parameters.AddWithValue("plazo", ruta.PlazoEnDias);
                    if (await insertar.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1)
                    {
                        return (int?)null;
                    }
                }

                int anterior;
                await using (var leer = new NpgsqlCommand("SELECT plazo_dias FROM fuentes.rutas WHERE origen_id = @o AND destino_id = @d FOR UPDATE", conexion, transaccion))
                {
                    leer.Parameters.AddWithValue("o", ruta.OrigenId);
                    leer.Parameters.AddWithValue("d", ruta.DestinoId);
                    anterior = (int)(await leer.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                }

                await using var actualizar = new NpgsqlCommand(
                    "UPDATE fuentes.rutas SET plazo_dias = @plazo, actualizada_en = now() WHERE origen_id = @o AND destino_id = @d",
                    conexion,
                    transaccion);
                actualizar.Parameters.AddWithValue("o", ruta.OrigenId);
                actualizar.Parameters.AddWithValue("d", ruta.DestinoId);
                actualizar.Parameters.AddWithValue("plazo", ruta.PlazoEnDias);
                await actualizar.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return anterior;
            },
            cancelacion);
    }

    private Task<IReadOnlyList<RutaEntreFuentes>> Leer(string sql, (string Nombre, object Valor)[] parametros, CancellationToken cancelacion) =>
        acceso.ConAlcanceDelActor<IReadOnlyList<RutaEntreFuentes>>(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
                foreach (var (nombre, valor) in parametros)
                {
                    comando.Parameters.AddWithValue(nombre, valor);
                }

                var rutas = new List<RutaEntreFuentes>();
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    rutas.Add(RutaEntreFuentes.Definir(lector.GetGuid(0), lector.GetGuid(1), lector.GetInt32(2)));
                }

                return rutas;
            },
            cancelacion);
}
