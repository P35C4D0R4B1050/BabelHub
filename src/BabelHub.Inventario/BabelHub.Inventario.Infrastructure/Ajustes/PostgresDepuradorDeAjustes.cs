using BabelHub.Inventario.Application.Ajustes.Interno;
using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Ajustes;

/// <summary>
/// <see cref="IDepuradorDeAjustes"/>: ejecuta <c>ajustes.anonimizar_registros()</c> y <c>ajustes.purgar_operaciones()</c> con <b>otra fuente de datos</b>, la del rol de depuración (<c>ajustes_depuracion</c>: la aplicación no es
/// miembro de ese rol ni del propietario de las funciones), en su propia conexión y transacción, <b>un lote por llamada</b> (cada lote confirma) hasta que devuelva menos del tamaño del lote. Las funciones no reciben parámetros:
/// la aplicación no puede pedir anonimizar a nadie en particular. En producción la cuenta de depuración (la de <c>Auditoria</c>) es miembro de este rol además del suyo.
/// </summary>
internal sealed class PostgresDepuradorDeAjustes(NpgsqlDataSource depuracion) : IDepuradorDeAjustes
{
    /// <summary>La función anonimiza hasta 5 000 filas por tabla y por llamada: mientras devuelva esto o más, quedan filas por procesar.</summary>
    public const int TamanoDelLote = 5000;

    /// <summary>Un tope de lotes por corrida: la siguiente continúa.</summary>
    public const int MaximoDeLotesPorCorrida = 2000;

    public static readonly TimeSpan EsperaPorLote = TimeSpan.FromMinutes(2);

    public async Task<int> Anonimizar(CancellationToken cancelacion = default)
    {
        var total = 0;
        for (var lotes = 0; lotes < MaximoDeLotesPorCorrida; lotes++)
        {
            var lote = await Ejecutar("SELECT ajustes.anonimizar_registros()", cancelacion).ConfigureAwait(false);
            total += lote;
            if (lote < TamanoDelLote)
            {
                break;
            }
        }

        return total;
    }

    public Task<int> PurgarOperaciones(CancellationToken cancelacion = default) => Ejecutar("SELECT ajustes.purgar_operaciones()", cancelacion);

    private async Task<int> Ejecutar(string sentencia, CancellationToken cancelacion)
    {
        await using var conexion = await depuracion.OpenConnectionAsync(cancelacion).ConfigureAwait(false);
        await using var comando = new NpgsqlCommand(sentencia, conexion) { CommandTimeout = (int)EsperaPorLote.TotalSeconds };
        return (int)(await comando.ExecuteScalarAsync(cancelacion).ConfigureAwait(false))!;
    }
}
