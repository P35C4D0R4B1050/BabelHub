using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Migracion;

namespace BabelHub.Ecommerce.Api.Plataforma;

/// <summary>
/// El modo <c>migrar</c> del servicio (COMP-2): <c>dotnet BabelHub.Ecommerce.Api.dll migrar</c> aplica los esquemas y los roles de su base con la cuenta propietaria (<c>BABELHUB__Migrador__</c>) y las cuentas de conexión del propio servicio
/// (las mismas variables que usa al arrancar). Termina con 0 si la base quedó al día y con 1 si falta algo, nombrándolo: <b>nunca imprime una clave, una cadena de conexión ni un dato</b>. No arranca el servidor ni lee el resto de la configuración.
/// </summary>
public static class ModoMigrar
{
    public const string Argumento = "migrar";

    public static async Task<int> Ejecutar(IConfiguration configuracion, TextWriter salida, TextWriter error, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        try
        {
            var plan = PlanDeMigracionDelEcommerce.Leer(configuracion);
            var propietaria = PlanDeMigracionDelEcommerce.Propietaria(configuracion, plan);
            await using var origen = BasesDelEcommerce.Abrir(propietaria);
            return await new MigradorDeLaBase(origen, salida).Ejecutar(plan, cancelacion).ConfigureAwait(false);
        }
        catch (MigracionFallidaException e)
        {
            // Solo el mensaje: la excepción interna es de PostgreSQL y puede traer nombres de objetos.
            await error.WriteLineAsync("migracion: " + e.Message).ConfigureAwait(false);
            return 1;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Una falla que no es de la migración (la red, la cuenta): solo el tipo y el código de PostgreSQL, nunca la traza ni el texto, que pueden nombrar objetos.
            var codigo = e is Npgsql.PostgresException pg ? " (SQLSTATE " + pg.SqlState + ")" : string.Empty;
            await error.WriteLineAsync("migracion: falló la conexión o una consulta de la migración: " + e.GetType().Name + codigo).ConfigureAwait(false);
            return 1;
        }
    }
}
