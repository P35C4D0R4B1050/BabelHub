using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Plataforma;

/// <summary>
/// Comprueba que la base de datos de la aplicación responde (<c>/health/ready</c>, RNF03): abre una conexión con la cuenta de acceso y ejecuta <c>SELECT 1</c>, sin tocar ninguna tabla. Una base caída, un plazo vencido o un rechazo
/// de la conexión lanzan: quien llama lo cuenta como componente caído y su mensaje no sale de allí. No guarda estado.
/// </summary>
public sealed class ComprobacionDeLaBaseDeDatos(NpgsqlDataSource origen)
{
    public async Task<bool> Responde(CancellationToken cancelacion)
    {
        await using var conexion = await origen.OpenConnectionAsync(cancelacion).ConfigureAwait(false);
        await using var comando = new NpgsqlCommand("SELECT 1", conexion);
        return await comando.ExecuteScalarAsync(cancelacion).ConfigureAwait(false) is 1;
    }
}
