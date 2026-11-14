using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Domain.Fuentes;
using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Fuentes;

/// <summary>
/// <see cref="IRepositorioDeFichas"/> sobre PostgreSQL: la ficha de una edición es solo su formato (D03). El alta o el cambio es un <c>INSERT … ON CONFLICT (edicion_id) DO UPDATE</c> (dos <c>PUT</c> simultáneos
/// del Catálogo no chocan) y la clave compuesta <c>(edicion_id, formato)</c> de <c>stock</c> es la última defensa de «una digital nunca tiene stock»: si impide el cambio de formato, se devuelve como resultado.
/// </summary>
internal sealed class NpgsqlRepositorioDeFichas(AccesoAFuentes acceso) : IRepositorioDeFichas
{
    private const string ViolacionDeClaveForanea = "23503";

    public async Task<FichaDeEdicion?> PorId(Guid edicionId, CancellationToken cancelacion = default) =>
        (await PorIds([edicionId], cancelacion).ConfigureAwait(false)).FirstOrDefault();

    public Task<IReadOnlyList<FichaDeEdicion>> PorIds(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(edicionIds);

        return acceso.ConAlcanceDelActor<IReadOnlyList<FichaDeEdicion>>(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT edicion_id, formato FROM fuentes.ediciones WHERE edicion_id = ANY(@ids) ORDER BY edicion_id", conexion, transaccion);
                comando.Parameters.AddWithValue("ids", edicionIds.ToArray());

                var fichas = new List<FichaDeEdicion>();
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    fichas.Add(FichaDeEdicion.Crear(lector.GetGuid(0), lector.GetString(1) == "impreso" ? FormatoDeEdicion.Impreso : FormatoDeEdicion.Digital));
                }

                return fichas;
            },
            cancelacion);
    }

    public Task<bool> SonTodasImpresas(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(edicionIds);

        var distintas = edicionIds.Distinct().ToArray();
        return acceso.ConAlcanceDelActor(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT count(*) FROM fuentes.ediciones WHERE edicion_id = ANY(@ids) AND formato = 'impreso'", conexion, transaccion);
                comando.Parameters.AddWithValue("ids", distintas);
                return (long)(await comando.ExecuteScalarAsync(token).ConfigureAwait(false))! == distintas.Length;
            },
            cancelacion);
    }

    public Task<ResultadoDeGuardadoDeFicha> Guardar(FichaDeEdicion ficha, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(ficha);

        return acceso.ConAlcanceDelActor(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    "INSERT INTO fuentes.ediciones (edicion_id, formato) VALUES (@e, @formato) ON CONFLICT (edicion_id) DO UPDATE SET formato = EXCLUDED.formato",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("e", ficha.EdicionId);
                comando.Parameters.AddWithValue("formato", ficha.Formato == FormatoDeEdicion.Impreso ? "impreso" : "digital");

                try
                {
                    await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    return ResultadoDeGuardadoDeFicha.Guardada;
                }
                catch (PostgresException ex) when (ex.SqlState == ViolacionDeClaveForanea)
                {
                    // La transacción queda abortada: el caso de uso termina con un fallo y la unidad de trabajo la revierte.
                    return ResultadoDeGuardadoDeFicha.FormatoEnConflicto;
                }
            },
            cancelacion);
    }
}
