using System.Globalization;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Shared.Contratos;
using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Plataforma;

/// <summary>
/// El valor de alcance de datos (<c>babelhub.alcance</c>) que <see cref="UnidadDeTrabajoPostgres"/> fija en cada transacción según el actor,
/// con una <b>lista blanca</b> (D09): la sucursal para un encargado con <c>SucursalId</c>; <c>red</c> solo para administración central,
/// logística, cliente, sistema, integración y la credencial de servicio sin actor; cualquier otro caso (un encargado sin sucursal, un rol
/// desconocido, un visitante) no fija valor y las políticas de filas deniegan. La Aplicación decide además el alcance de cada operación.
/// </summary>
public static class AlcanceDeDatosDelActor
{
    /// <summary>Nombre del parámetro de configuración de la transacción que leen las políticas de filas.</summary>
    public const string Parametro = "babelhub.alcance";

    /// <summary>Valor que ve toda la red.</summary>
    public const string Red = "red";

    /// <summary>El valor del alcance para el actor, o <see langword="null"/> si no debe fijarse (las políticas de filas deniegan).</summary>
    public static string? Para(ActorActual? actor)
    {
        // Sin actor: una llamada solo con la credencial de servicio (la operación de usuario sin actor ya falla cerrada en la Api).
        if (actor is null)
        {
            return Red;
        }

        return actor.Tipo switch
        {
            TipoDeActor.Sistema or TipoDeActor.Integracion => Red,
            TipoDeActor.Usuario => ParaUsuario(actor),
            _ => null, // Anonimo y cualquier tipo nuevo: sin valor
        };
    }

    private static string? ParaUsuario(ActorActual actor) => actor.Rol switch
    {
        RolesDeUsuario.AdministradorCentral or RolesDeUsuario.PersonalDeLogistica or RolesDeUsuario.Cliente => Red,
        RolesDeUsuario.EncargadoDeSucursal when actor.SucursalId is { } sucursal && sucursal != Guid.Empty =>
            sucursal.ToString("D", CultureInfo.InvariantCulture),
        _ => null,
    };
}

/// <summary>Reconoce y traduce el rechazo de la política de seguridad por filas, para no confundirlo con «sin stock» ni con un error inesperado.</summary>
public static class RechazoDeLaPolitica
{
    private const string PermisoInsuficiente = "42501";

    /// <summary>
    /// <see langword="true"/> si el error es la violación de una política de filas (<c>new row violates row-level security policy</c>). Un
    /// <c>42501</c> por falta de permiso sobre la tabla o el esquema no lo es y sigue siendo un error inesperado.
    /// </summary>
    public static bool Es(Exception error)
    {
        for (var actual = error; actual is not null; actual = actual.InnerException)
        {
            // La violación de una política llega con el nombre de la tabla (errtable) en cualquier idioma del servidor; la falta de permiso, no.
            if (actual is PostgresException { SqlState: PermisoInsuficiente } pg
                && (!string.IsNullOrEmpty(pg.TableName) || pg.MessageText.Contains("row-level security", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Ejecuta la operación y traduce el rechazo de la política a <see cref="AlcanceDeDatosDenegadoException"/>.</summary>
    public static async Task<T> Traducir<T>(Func<Task<T>> operacion)
    {
        ArgumentNullException.ThrowIfNull(operacion);

        try
        {
            return await operacion().ConfigureAwait(false);
        }
        catch (Exception ex) when (Es(ex))
        {
            throw new AlcanceDeDatosDenegadoException(ex);
        }
    }
}
