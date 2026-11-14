using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Plataforma;

/// <summary>
/// El latido de la base (PLA-15, D10): una sola fila en <c>plataforma.latido</c> que se actualiza cada minuto para que el archivado
/// continuo de cambios siempre tenga WAL nuevo que archivar, aunque la base no tenga actividad. Así el atraso se mide por la marca del
/// último objeto archivado y solo supera los 15 minutos si el archivado falla. Quien late es el rol <c>plataforma_latido</c>, que no
/// puede hacer otra cosa que actualizar esa fila.
/// </summary>
public static class DefinicionDeLatido
{
    /// <summary>La tabla de una sola fila (la restricción impide cualquier otra) y su primera fila, que la migración deja puesta.</summary>
    public const string Sql = """
        CREATE SCHEMA IF NOT EXISTS plataforma;
        CREATE TABLE IF NOT EXISTS plataforma.latido (
            id smallint PRIMARY KEY CHECK (id = 1),
            instante timestamptz NOT NULL,
            numero bigint NOT NULL
        );
        INSERT INTO plataforma.latido (id, instante, numero) VALUES (1, now(), 0) ON CONFLICT (id) DO NOTHING;
        """;

    /// <summary>
    /// El rol de base de datos mínimo, que es del clúster (la contraseña la pone el despliegue desde su secreto, nunca esta definición):
    /// puede conectarse a esta base y actualizar solo las columnas del latido; no inserta, no borra, no vacía, no crea objetos y no lee
    /// ninguna otra tabla. Con pocas conexiones y un plazo corto por sentencia.
    /// </summary>
    public const string Rol = """
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'plataforma_latido') THEN
                CREATE ROLE plataforma_latido LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS CONNECTION LIMIT 2;
            END IF;
            EXECUTE format('GRANT CONNECT ON DATABASE %I TO plataforma_latido', current_database());
        END
        $$;
        ALTER ROLE plataforma_latido SET statement_timeout = '5s';
        ALTER ROLE plataforma_latido SET lock_timeout = '2s';
        GRANT USAGE ON SCHEMA plataforma TO plataforma_latido;
        GRANT SELECT ON plataforma.latido TO plataforma_latido;
        GRANT UPDATE (instante, numero) ON plataforma.latido TO plataforma_latido;
        """;

    /// <summary>
    /// Lo que ejecuta cada latido: avanza el instante y el número de la única fila. Cada ejecución escribe WAL, que es lo que importa.
    /// Lo corre la operación de respaldo en la VM de datos con la credencial de <c>plataforma_latido</c>.
    /// </summary>
    public const string Latir = "UPDATE plataforma.latido SET instante = clock_timestamp(), numero = numero + 1 WHERE id = 1 RETURNING numero";
}

/// <summary>Ejecuta un latido con la fuente de datos del rol <c>plataforma_latido</c> (no con la de la aplicación).</summary>
public sealed class LatidoDeLaBase(NpgsqlDataSource fuenteDelRolDeLatido)
{
    /// <summary>Late una vez y devuelve el número del latido, o lanza si la fila no existe.</summary>
    public async Task<long> Latir(CancellationToken cancelacion = default)
    {
        await using var comando = fuenteDelRolDeLatido.CreateCommand(DefinicionDeLatido.Latir);
        return await comando.ExecuteScalarAsync(cancelacion).ConfigureAwait(false) is long numero
            ? numero
            : throw new InvalidOperationException("La tabla del latido no tiene su fila: falta aplicar la migración del esquema plataforma.");
    }
}
