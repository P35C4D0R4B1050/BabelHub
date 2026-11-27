using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Domain.Carrito;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Carrito;

/// <summary>
/// Los costos de envío propios por sucursal en <c>carrito.tarifas_de_envio</c> (T44; CAR-46, CAR-47), bajo <c>rol_carrito</c>. <see cref="Fijar"/> inserta si no había fila y, si la había, la bloquea antes de leer el importe anterior y
/// escribir el nuevo: dos cambios simultáneos se serializan y cada uno recibe el que de verdad reemplazó.
/// </summary>
public sealed class RepositorioDeTarifasDeEnvioPostgres(AccesoAlEsquemaDelCarrito acceso) : IRepositorioDeTarifasDeEnvio
{
    public Task<IReadOnlyDictionary<string, CostoDeEnvio>> Leer(CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyDictionary<string, CostoDeEnvio>>(
            async (conexion, transaccion, token) =>
            {
                var tarifas = new Dictionary<string, CostoDeEnvio>(StringComparer.Ordinal);
                await using var comando = new NpgsqlCommand("SELECT codigo_de_sucursal, importe FROM carrito.tarifas_de_envio", conexion, transaccion);
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await filas.ReadAsync(token).ConfigureAwait(false))
                {
                    tarifas[filas.GetString(0)] = CostoDeEnvio.Crear(filas.GetDecimal(1));
                }

                return tarifas;
            },
            cancelacion);

    public Task<CostoDeEnvio?> Fijar(string codigoDeSucursal, CostoDeEnvio costo, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(codigoDeSucursal);
        ArgumentNullException.ThrowIfNull(costo);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                NpgsqlCommand Comando(string sql)
                {
                    var comando = new NpgsqlCommand(sql, conexion, transaccion);
                    comando.Parameters.AddWithValue("sucursal", codigoDeSucursal);
                    comando.Parameters.AddWithValue("importe", costo.Valor);
                    comando.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
                    return comando;
                }

                await using (var insertar = Comando("INSERT INTO carrito.tarifas_de_envio (codigo_de_sucursal, importe, actualizada_en) VALUES (@sucursal, @importe, @ahora) ON CONFLICT (codigo_de_sucursal) DO NOTHING"))
                {
                    if (await insertar.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1)
                    {
                        return null;
                    }
                }

                CostoDeEnvio anterior;
                await using (var leer = Comando("SELECT importe FROM carrito.tarifas_de_envio WHERE codigo_de_sucursal = @sucursal FOR UPDATE"))
                {
                    anterior = CostoDeEnvio.Crear((decimal)(await leer.ExecuteScalarAsync(token).ConfigureAwait(false))!);
                }

                await using (var cambiar = Comando("UPDATE carrito.tarifas_de_envio SET importe = @importe, actualizada_en = @ahora WHERE codigo_de_sucursal = @sucursal"))
                {
                    await cambiar.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                return (CostoDeEnvio?)anterior;
            },
            cancelacion);
    }
}
