using BabelHub.Inventario.Infrastructure.Composicion;
using BabelHub.Inventario.Infrastructure.Migracion;

namespace BabelHub.Inventario.Api.Plataforma;

/// <summary>
/// El modo <c>migrar</c> del servicio (COMP-2): <c>dotnet BabelHub.Inventario.Api.dll migrar</c> aplica los esquemas y los roles de su base con la cuenta propietaria (<c>BABELHUB__Migrador__</c>) y las cuentas de conexión del propio servicio
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
            var plan = PlanDeMigracionDelInventario.Leer(configuracion);
            var propietaria = PlanDeMigracionDelInventario.Propietaria(configuracion, plan);
            await using var origen = BasesDelInventario.Abrir(propietaria);
            return await new MigradorDeLaBase(origen, salida).Ejecutar(plan, cancelacion).ConfigureAwait(false);
        }
        catch (MigracionFallidaException e)
        {
            // Solo el mensaje: la excepción interna es de PostgreSQL y puede traer nombres de objetos.
            await error.WriteLineAsync("migracion: " + e.Message).ConfigureAwait(false);
            return 1;
        }
    }
}
