using System.Globalization;
using System.Text;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Domain.Auditoria;

namespace BabelHub.Ecommerce.Infrastructure.Auditoria;

/// <summary>
/// El esquema <c>auditoria</c> de un servicio (T22, T23, T24; plan §3): <c>registros</c> (de solo inserción, con su secuencia, el índice único parcial de los accesos denegados y la búsqueda de
/// texto con <c>pg_trgm</c>), <c>alertas</c> (el registro técnico), <c>acumulado_de_limites</c>, <c>por_anonimizar</c>, el estado de la depuración y de la lectura de alertas, y la
/// tabla de solo lectura <c>politica_de_anonimizacion</c> generada desde el catálogo. Las tablas son del rol de migración <c>auditoria_migraciones</c> (sin login), del que la aplicación no es
/// miembro: no puede alterarlas, borrar sus disparadores ni crear funciones imitando a un propietario. La aplicación solo inserta y lee <c>registros</c>; los disparadores por fila y por
/// sentencia rechazan <c>UPDATE</c>, <c>DELETE</c> y <c>TRUNCATE</c>, salvo el <c>UPDATE</c> de la función de depuración, que reconocen porque <c>current_user</c> es el propietario fijo de esa
/// función (<c>auditoria_depuracion_dueno</c>) y que además no puede tocar la identidad del registro (acción, fecha, tipo y resultado). Los mismos roles, disparadores y función se reutilizan
/// para <c>identidad.registros</c> con <see cref="RegistrosInmutables"/> (plan de Identidad T30).
/// </summary>
public static class DefinicionDeAuditoria
{
    public const string RolDeMigraciones = "auditoria_migraciones";

    /// <summary>El rol del esquema que usa <c>EjecutarComo</c>: <c>rol_&lt;esquema&gt;</c> (convención de la unidad de trabajo).</summary>
    public const string RolDeLaAplicacion = "rol_auditoria";

    public const string PropietarioDeLaFuncion = "auditoria_depuracion_dueno";

    /// <summary>El rol de depuración: solo <c>EXECUTE</c> de la función (en producción, con su propia credencial; la aplicación no es miembro).</summary>
    public const string RolDeDepuracion = "auditoria_depuracion";

    /// <summary>La retención de lo ya procesado en <c>por_anonimizar</c>: 30 días (plan §3).</summary>
    public const int DiasDeRetencionDePorAnonimizar = 30;

    /// <summary>Los roles del módulo (sin login; las credenciales las pone el despliegue). Idempotente.</summary>
    public const string Roles = """
        DO $roles$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'auditoria_migraciones') THEN CREATE ROLE auditoria_migraciones NOLOGIN; END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'auditoria_depuracion_dueno') THEN CREATE ROLE auditoria_depuracion_dueno NOLOGIN; END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'auditoria_depuracion') THEN CREATE ROLE auditoria_depuracion NOLOGIN; END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rol_auditoria') THEN CREATE ROLE rol_auditoria NOLOGIN; END IF;
        END
        $roles$;
        """;

    /// <summary>
    /// Las tablas del módulo. <c>registros</c> y los disparadores van aparte (<see cref="RegistrosInmutables"/>) porque se reutilizan para <c>identidad.registros</c>.
    /// </summary>
    public const string Tablas = """
        CREATE EXTENSION IF NOT EXISTS pg_trgm;
        CREATE SCHEMA IF NOT EXISTS auditoria;
        ALTER SCHEMA auditoria OWNER TO auditoria_migraciones;

        CREATE TABLE IF NOT EXISTS auditoria.alertas (
            id text PRIMARY KEY CHECK (id ~ '^(eco|inv):[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'),
            servicio text NOT NULL CHECK (servicio IN ('ecommerce', 'inventario')),
            motivo text NOT NULL CHECK (motivo ~ '^[a-z][a-z0-9-]{1,63}$'),
            clase text NOT NULL CHECK (clase IN ('seguridad', 'tecnica')),
            primera_ocurrencia timestamptz NOT NULL,
            ultima_ocurrencia timestamptz NOT NULL,
            ocurrencias integer NOT NULL CHECK (ocurrencias BETWEEN 1 AND 100000),
            revisadas_hasta integer NOT NULL DEFAULT 0 CHECK (revisadas_hasta >= 0 AND revisadas_hasta <= ocurrencias),
            revisada_por uuid,
            revisada_en timestamptz,
            enviada_por_correo boolean NOT NULL DEFAULT false,
            intentos_de_correo integer NOT NULL DEFAULT 0 CHECK (intentos_de_correo >= 0),
            proximo_intento_de_correo timestamptz,
            CHECK (ultima_ocurrencia >= primera_ocurrencia)
        );
        CREATE INDEX IF NOT EXISTS alertas_por_motivo ON auditoria.alertas (servicio, motivo, primera_ocurrencia DESC);
        CREATE INDEX IF NOT EXISTS alertas_de_seguridad ON auditoria.alertas (ultima_ocurrencia DESC, id) WHERE clase = 'seguridad';
        CREATE INDEX IF NOT EXISTS alertas_por_ultima ON auditoria.alertas (ultima_ocurrencia, id);
        CREATE INDEX IF NOT EXISTS alertas_con_correo_pendiente ON auditoria.alertas (primera_ocurrencia) WHERE NOT enviada_por_correo;

        CREATE TABLE IF NOT EXISTS auditoria.acumulado_de_limites (
            huella text NOT NULL CHECK (huella ~ '^[A-Za-z0-9_-]{4,128}$'),
            limite text NOT NULL CHECK (limite ~ '^[a-z][a-z0-9-]{1,63}$'),
            ventana_inicio timestamptz NOT NULL,
            rechazos integer NOT NULL CHECK (rechazos >= 1),
            direccion_completa text CHECK (direccion_completa IS NULL OR length(direccion_completa) <= 64),
            cerrada_en timestamptz,
            intentos_de_cierre integer NOT NULL DEFAULT 0 CHECK (intentos_de_cierre >= 0),
            PRIMARY KEY (huella, limite, ventana_inicio),
            CHECK (cerrada_en IS NULL OR direccion_completa IS NULL)
        );
        CREATE INDEX IF NOT EXISTS acumulado_por_ventana ON auditoria.acumulado_de_limites (ventana_inicio);

        CREATE TABLE IF NOT EXISTS auditoria.por_anonimizar (
            identificador_opaco uuid PRIMARY KEY,
            tipo text NOT NULL CHECK (tipo IN ('cuenta_cancelada', 'baja_de_titular')),
            registrado_en timestamptz NOT NULL,
            procesado_en timestamptz
        );

        CREATE TABLE IF NOT EXISTS auditoria.alertas_ya_enviadas (
            alerta_id text PRIMARY KEY CHECK (alerta_id ~ '^inv:[0-9a-f-]{36}$'),
            enviada_en timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS auditoria.marca_de_lectura_de_alertas (
            id smallint PRIMARY KEY CHECK (id = 1),
            marca timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS auditoria.estado_de_depuracion (
            id smallint PRIMARY KEY CHECK (id = 1),
            ultima_exitosa timestamptz,
            primer_fallo_sin_exito timestamptz,
            intentos_fallidos integer NOT NULL DEFAULT 0,
            proximo_intento timestamptz,
            anonimizados_sin_registrar integer NOT NULL DEFAULT 0 CHECK (anonimizados_sin_registrar >= 0)
        );

        CREATE TABLE IF NOT EXISTS auditoria.politica_de_anonimizacion (
            accion text PRIMARY KEY CHECK (accion ~ '^[A-Z][A-Za-z0-9]{2,63}$'),
            objeto_personal boolean NOT NULL,
            rol_personal boolean NOT NULL
        );

        ALTER TABLE auditoria.alertas OWNER TO auditoria_migraciones;
        ALTER TABLE auditoria.acumulado_de_limites OWNER TO auditoria_migraciones;
        ALTER TABLE auditoria.por_anonimizar OWNER TO auditoria_migraciones;
        ALTER TABLE auditoria.alertas_ya_enviadas OWNER TO auditoria_migraciones;
        ALTER TABLE auditoria.marca_de_lectura_de_alertas OWNER TO auditoria_migraciones;
        ALTER TABLE auditoria.estado_de_depuracion OWNER TO auditoria_migraciones;
        ALTER TABLE auditoria.politica_de_anonimizacion OWNER TO auditoria_migraciones;
        """;

    /// <summary>
    /// La tabla <c>por_anonimizar</c> de otro esquema (la de Identidad, que llena con sus cuentas canceladas): la misma forma y los mismos permisos mínimos para el propietario de la
    /// función de depuración, que solo lee, marca como procesado y borra lo vencido.
    /// </summary>
    public static string PorAnonimizar(string esquema)
    {
        ValidarEsquema(esquema);
        return $"""
            CREATE TABLE IF NOT EXISTS {esquema}.por_anonimizar (
                identificador_opaco uuid PRIMARY KEY,
                tipo text NOT NULL CHECK (tipo IN ('cuenta_cancelada', 'baja_de_titular')),
                registrado_en timestamptz NOT NULL,
                procesado_en timestamptz
            );
            ALTER TABLE {esquema}.por_anonimizar OWNER TO auditoria_migraciones;
            GRANT USAGE ON SCHEMA {esquema} TO auditoria_depuracion_dueno;
            GRANT SELECT ON {esquema}.por_anonimizar TO auditoria_depuracion_dueno;
            GRANT UPDATE (procesado_en) ON {esquema}.por_anonimizar TO auditoria_depuracion_dueno;
            GRANT DELETE ON {esquema}.por_anonimizar TO auditoria_depuracion_dueno;
            """;
    }

    /// <summary>
    /// Lo que el rol de depuración necesita para ejecutar la función de otro esquema (<c>identidad.registros</c>): el propietario de la función lee y actualiza por columnas, el rol de
    /// depuración solo ejecuta.
    /// </summary>
    public static string PermisosDeLaDepuracion(string esquema)
    {
        ValidarEsquema(esquema);
        return $"""
            GRANT USAGE ON SCHEMA {esquema} TO auditoria_depuracion_dueno, auditoria_depuracion;
            GRANT SELECT ON {esquema}.registros TO auditoria_depuracion_dueno;
            GRANT UPDATE (actor_id, rol, objeto_id, origen, detalle, anonimizado_en) ON {esquema}.registros TO auditoria_depuracion_dueno;
            """;
    }

    /// <summary>La tabla de registros del esquema <paramref name="esquema"/> con sus índices, sus disparadores de inmutabilidad y su función de depuración (la plantilla de T23 y T24).</summary>
    public static string RegistrosInmutables(string esquema)
    {
        ValidarEsquema(esquema);
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $$"""
            CREATE SCHEMA IF NOT EXISTS {{esquema}};
            ALTER SCHEMA {{esquema}} OWNER TO auditoria_migraciones;
            CREATE TABLE IF NOT EXISTS {{esquema}}.registros (
                id uuid PRIMARY KEY,
                secuencia bigint GENERATED ALWAYS AS IDENTITY,
                servicio text NOT NULL CHECK (servicio IN ('ecommerce', 'inventario')),
                tipo text NOT NULL CHECK (tipo IN ('auditoria', 'seguridad', 'ambos')),
                actor_tipo text NOT NULL CHECK (actor_tipo IN ('usuario', 'sistema', 'integracion', 'anonimo')),
                actor_id uuid,
                rol text CHECK (rol IS NULL OR rol ~ '^[a-z][a-z0-9-]{1,39}$'),
                instante_utc timestamptz NOT NULL,
                accion text NOT NULL CHECK (accion ~ '^[A-Z][A-Za-z0-9]{2,63}$'),
                objeto_tipo text CHECK (objeto_tipo IS NULL OR objeto_tipo ~ '^[a-z][a-z0-9-]{0,39}$'),
                objeto_id text CHECK (objeto_id IS NULL OR objeto_id ~ '^[A-Za-z0-9:_.-]{1,64}$'),
                sucursal_origen text CHECK (sucursal_origen IS NULL OR sucursal_origen ~ '^[A-Za-z0-9_-]{1,64}$'),
                sucursal_destino text CHECK (sucursal_destino IS NULL OR sucursal_destino ~ '^[A-Za-z0-9_-]{1,64}$'),
                motivo text CHECK (motivo IS NULL OR motivo ~ '^[a-z][a-z0-9-]{1,63}$'),
                origen text CHECK (origen IS NULL OR origen ~ '^[A-Za-z0-9:._-]{1,64}$'),
                ruta text CHECK (ruta IS NULL OR ruta ~ '^(sin-coincidencia|/[A-Za-z0-9._{}/-]{0,198})$'),
                detalle jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(detalle) = 'object' AND length(detalle::text) <= 4000),
                claves_personales text[] NOT NULL DEFAULT '{}',
                resultado text NOT NULL CHECK (resultado IN ('exitoso', 'rechazado', 'fallido')),
                truncado boolean NOT NULL DEFAULT false,
                correlacion text CHECK (correlacion IS NULL OR correlacion ~ '^[A-Za-z0-9_-]{8,64}$'),
                anonimizado_en timestamptz
            );
            CREATE UNIQUE INDEX IF NOT EXISTS registros_un_acceso_denegado_por_correlacion ON {{esquema}}.registros (servicio, correlacion, motivo) WHERE accion = 'AccesoDenegado';
            CREATE INDEX IF NOT EXISTS registros_por_instante ON {{esquema}}.registros (instante_utc DESC, servicio, sucursal_origen, id);
            CREATE INDEX IF NOT EXISTS registros_por_secuencia ON {{esquema}}.registros (secuencia);
            CREATE INDEX IF NOT EXISTS registros_por_accion ON {{esquema}}.registros (accion, instante_utc DESC);
            CREATE INDEX IF NOT EXISTS registros_por_actor ON {{esquema}}.registros (actor_id) WHERE actor_id IS NOT NULL;
            CREATE INDEX IF NOT EXISTS registros_por_objeto ON {{esquema}}.registros (objeto_id) WHERE objeto_id IS NOT NULL;
            CREATE INDEX IF NOT EXISTS registros_por_sucursal ON {{esquema}}.registros (sucursal_origen, instante_utc DESC) WHERE sucursal_origen IS NOT NULL;
            CREATE INDEX IF NOT EXISTS registros_de_umbral ON {{esquema}}.registros (accion, instante_utc) WHERE accion IN ('AccesoDenegado', 'IngresoFallido');
            CREATE INDEX IF NOT EXISTS registros_por_texto ON {{esquema}}.registros USING gin ((detalle::text) gin_trgm_ops);
            ALTER TABLE {{esquema}}.registros OWNER TO auditoria_migraciones;

            -- El disparador guardián: ningún UPDATE ni DELETE ni TRUNCATE, salvo el UPDATE de la función de depuración (current_user = su propietario) que no toca la identidad del registro.
            CREATE OR REPLACE FUNCTION {{esquema}}.rechazar_modificacion_de_registros() RETURNS trigger
            LANGUAGE plpgsql
            SET search_path = pg_catalog
            AS $guardian$
            BEGIN
                IF TG_OP = 'UPDATE' AND current_user = 'auditoria_depuracion_dueno' THEN
                    IF NEW.id = OLD.id AND NEW.secuencia = OLD.secuencia AND NEW.servicio = OLD.servicio AND NEW.tipo = OLD.tipo
                       AND NEW.actor_tipo = OLD.actor_tipo AND NEW.instante_utc = OLD.instante_utc AND NEW.accion = OLD.accion
                       AND NEW.resultado = OLD.resultado AND NEW.motivo IS NOT DISTINCT FROM OLD.motivo AND NEW.ruta IS NOT DISTINCT FROM OLD.ruta
                       AND NEW.objeto_tipo IS NOT DISTINCT FROM OLD.objeto_tipo AND NEW.sucursal_origen IS NOT DISTINCT FROM OLD.sucursal_origen
                       AND NEW.sucursal_destino IS NOT DISTINCT FROM OLD.sucursal_destino AND NEW.truncado = OLD.truncado
                       AND NEW.correlacion IS NOT DISTINCT FROM OLD.correlacion THEN
                        RETURN NEW;
                    END IF;
                END IF;
                RAISE EXCEPTION 'Los registros de auditoría son de solo inserción (%).', TG_OP USING ERRCODE = 'insufficient_privilege';
            END
            $guardian$;
            ALTER FUNCTION {{esquema}}.rechazar_modificacion_de_registros() OWNER TO auditoria_migraciones;

            DROP TRIGGER IF EXISTS registros_solo_insercion_por_fila ON {{esquema}}.registros;
            CREATE TRIGGER registros_solo_insercion_por_fila BEFORE UPDATE OR DELETE ON {{esquema}}.registros
                FOR EACH ROW EXECUTE FUNCTION {{esquema}}.rechazar_modificacion_de_registros();
            DROP TRIGGER IF EXISTS registros_solo_insercion_por_sentencia ON {{esquema}}.registros;
            CREATE TRIGGER registros_solo_insercion_por_sentencia BEFORE TRUNCATE ON {{esquema}}.registros
                FOR EACH STATEMENT EXECUTE FUNCTION {{esquema}}.rechazar_modificacion_de_registros();
            ALTER TABLE {{esquema}}.registros ENABLE ALWAYS TRIGGER registros_solo_insercion_por_fila;
            ALTER TABLE {{esquema}}.registros ENABLE ALWAYS TRIGGER registros_solo_insercion_por_sentencia;
            """);
        return sb.ToString();
    }

    /// <summary>Los permisos de <c>registros</c> del esquema <paramref name="esquema"/> para el rol de la aplicación <paramref name="rolDeLaAplicacion"/>: solo inserta y lee.</summary>
    public static string PermisosDeRegistros(string esquema, string rolDeLaAplicacion)
    {
        ValidarEsquema(esquema);
        ValidarEsquema(rolDeLaAplicacion);
        return $"""
            GRANT USAGE ON SCHEMA {esquema} TO {rolDeLaAplicacion};
            REVOKE ALL ON {esquema}.registros FROM PUBLIC;
            GRANT SELECT, INSERT ON {esquema}.registros TO {rolDeLaAplicacion};
            """;
    }

    /// <summary>Los permisos de las demás tablas del módulo para el rol de la aplicación y para el propietario de la función de depuración.</summary>
    public const string Permisos = """
        REVOKE ALL ON SCHEMA auditoria FROM PUBLIC;
        GRANT USAGE ON SCHEMA auditoria TO rol_auditoria, auditoria_depuracion_dueno, auditoria_depuracion;
        REVOKE ALL ON ALL TABLES IN SCHEMA auditoria FROM PUBLIC;

        GRANT SELECT, INSERT, UPDATE ON auditoria.alertas TO rol_auditoria;
        GRANT SELECT, INSERT, UPDATE, DELETE ON auditoria.acumulado_de_limites TO rol_auditoria;
        GRANT SELECT, INSERT ON auditoria.por_anonimizar TO rol_auditoria;
        GRANT SELECT, INSERT ON auditoria.alertas_ya_enviadas TO rol_auditoria;
        GRANT SELECT, INSERT, UPDATE ON auditoria.marca_de_lectura_de_alertas TO rol_auditoria;
        GRANT SELECT, INSERT, UPDATE ON auditoria.estado_de_depuracion TO rol_auditoria;

        GRANT SELECT ON auditoria.politica_de_anonimizacion TO auditoria_depuracion_dueno;
        GRANT SELECT ON auditoria.registros TO auditoria_depuracion_dueno;
        GRANT UPDATE (actor_id, rol, objeto_id, origen, detalle, anonimizado_en) ON auditoria.registros TO auditoria_depuracion_dueno;
        GRANT SELECT ON auditoria.por_anonimizar TO auditoria_depuracion_dueno;
        GRANT UPDATE (procesado_en) ON auditoria.por_anonimizar TO auditoria_depuracion_dueno;
        GRANT DELETE ON auditoria.por_anonimizar TO auditoria_depuracion_dueno;
        """;

    /// <summary>
    /// La función de depuración <c>auditoria.anonimizar_registros()</c> (T24, AUD-19, D16): <c>SECURITY DEFINER</c>, <b>sin parámetros</b>, con su propietario propio, <c>search_path</c> fijo y solo
    /// <c>EXECUTE</c> para el rol de depuración. Lee <c>por_anonimizar</c> (cuentas canceladas aún no procesadas o procesadas hace menos de 30 días) y anonimiza por lotes lo que la política dice:
    /// un registro de más de dos años se anonimiza completo; por cuenta cancelada, <c>actor_id</c> y <c>rol</c> solo si el actor es esa cuenta, <c>objeto_id</c> solo si coincide y el objeto
    /// identifica, y del detalle solo las claves marcadas personales cuyo valor coincide, con <c>NULL</c> o la constante <c>[anonimizado]</c> (nunca un hash). Es idempotente y devuelve cuántos
    /// registros cambió. Después marca como procesados los identificadores nuevos y borra los procesados hace más de 30 días.
    /// </summary>
    public static string FuncionDeDepuracion(string esquema = "auditoria", string esquemaDePorAnonimizar = "auditoria") => $$"""
        CREATE OR REPLACE FUNCTION {{esquema}}.anonimizar_registros() RETURNS integer
        LANGUAGE plpgsql
        SECURITY DEFINER
        SET search_path = pg_catalog
        AS $depurar$
        DECLARE
            total integer := 0;
            lote integer;
        BEGIN
            LOOP
                WITH canceladas AS (
                    SELECT replace(p.identificador_opaco::text, '-', '') AS norm
                    FROM {{esquemaDePorAnonimizar}}.por_anonimizar p
                    WHERE p.procesado_en IS NULL OR p.procesado_en >= pg_catalog.now() - interval '30 days'
                ),
                candidatos AS (
                    SELECT r.secuencia,
                           (r.instante_utc < pg_catalog.now() - interval '2 years') AS vencido,
                           (r.actor_id IS NOT NULL AND replace(r.actor_id::text, '-', '') IN (SELECT norm FROM canceladas)) AS actor_cancelado,
                           (r.objeto_id IS NOT NULL AND replace(pg_catalog.lower(r.objeto_id), '-', '') IN (SELECT norm FROM canceladas)) AS objeto_cancelado,
                           COALESCE(po.objeto_personal, false) AS objeto_personal,
                           COALESCE(po.rol_personal, false) AS rol_personal
                    FROM {{esquema}}.registros r
                    LEFT JOIN auditoria.politica_de_anonimizacion po ON po.accion = r.accion
                ),
                planes AS (
                    SELECT c.secuencia,
                           (r.actor_id IS NOT NULL AND (c.vencido OR c.actor_cancelado)) AS anon_actor_id,
                           (r.rol IS NOT NULL AND (c.actor_cancelado OR (c.vencido AND c.rol_personal))) AS anon_rol,
                           (r.objeto_id IS NOT NULL AND c.objeto_personal AND (c.vencido OR c.objeto_cancelado)) AS anon_objeto,
                           (r.origen IS NOT NULL AND (c.vencido OR c.actor_cancelado)) AS anon_origen,
                           ARRAY(
                               SELECT k FROM pg_catalog.unnest(r.claves_personales) AS k
                               WHERE r.detalle ? k
                                 AND (r.detalle ->> k) IS NOT NULL
                                 AND (r.detalle ->> k) <> '[anonimizado]'
                                 AND pg_catalog.length(r.detalle ->> k) > 0
                                 AND (c.vencido OR replace(pg_catalog.lower(r.detalle ->> k), '-', '') IN (SELECT norm FROM canceladas))
                           ) AS claves
                    FROM candidatos c
                    JOIN {{esquema}}.registros r ON r.secuencia = c.secuencia
                ),
                lote_a_cambiar AS (
                    SELECT * FROM planes
                    WHERE anon_actor_id OR anon_rol OR anon_objeto OR anon_origen OR pg_catalog.cardinality(claves) > 0
                    ORDER BY secuencia
                    LIMIT 5000
                )
                UPDATE {{esquema}}.registros r
                SET actor_id = CASE WHEN l.anon_actor_id THEN NULL ELSE r.actor_id END,
                    rol = CASE WHEN l.anon_rol THEN NULL ELSE r.rol END,
                    objeto_id = CASE WHEN l.anon_objeto THEN NULL ELSE r.objeto_id END,
                    origen = CASE WHEN l.anon_origen THEN NULL ELSE r.origen END,
                    detalle = CASE WHEN pg_catalog.cardinality(l.claves) > 0 THEN
                        COALESCE((SELECT pg_catalog.jsonb_object_agg(e.key, CASE WHEN e.key = ANY (l.claves) THEN pg_catalog.to_jsonb('[anonimizado]'::text) ELSE e.value END)
                                  FROM pg_catalog.jsonb_each(r.detalle) AS e), '{}'::jsonb)
                        ELSE r.detalle END,
                    anonimizado_en = COALESCE(r.anonimizado_en, pg_catalog.now())
                FROM lote_a_cambiar l
                WHERE r.secuencia = l.secuencia;

                GET DIAGNOSTICS lote = ROW_COUNT;
                total := total + lote;
                EXIT WHEN lote = 0;
            END LOOP;

            UPDATE {{esquemaDePorAnonimizar}}.por_anonimizar SET procesado_en = pg_catalog.now() WHERE procesado_en IS NULL;
            DELETE FROM {{esquemaDePorAnonimizar}}.por_anonimizar WHERE procesado_en < pg_catalog.now() - interval '30 days';
            RETURN total;
        END
        $depurar$;
        ALTER FUNCTION {{esquema}}.anonimizar_registros() OWNER TO auditoria_depuracion_dueno;
        REVOKE ALL ON FUNCTION {{esquema}}.anonimizar_registros() FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION {{esquema}}.anonimizar_registros() TO auditoria_depuracion;
        """;

    /// <summary>La tabla de solo lectura <c>politica_de_anonimizacion</c>, generada desde el catálogo: qué acciones tienen un objeto o un rol que identifican a una persona (AUD-19). Una prueba de deriva compara la tabla con el catálogo.</summary>
    public static string PoliticaDesdeElCatalogo(CatalogoDeAcciones catalogo)
    {
        ArgumentNullException.ThrowIfNull(catalogo);
        var sb = new StringBuilder("INSERT INTO auditoria.politica_de_anonimizacion (accion, objeto_personal, rol_personal) VALUES\n");
        var filas = catalogo.Acciones
            .OrderBy(a => a.Identificador, StringComparer.Ordinal)
            .Select(a => $"    ('{a.Identificador}', {(a.ObjetoPersonal ? "true" : "false")}, {(a.RolPersonal ? "true" : "false")})");
        sb.AppendJoin(",\n", filas);
        sb.Append("\nON CONFLICT (accion) DO UPDATE SET objeto_personal = EXCLUDED.objeto_personal, rol_personal = EXCLUDED.rol_personal;\n");
        return sb.ToString();
    }

    /// <summary>La migración completa del módulo en un servicio: roles, tablas, registros inmutables, función de depuración, política y permisos.</summary>
    public static string MigracionCompleta(CatalogoDeAcciones catalogo) =>
        Roles
        + Tablas
        + RegistrosInmutables("auditoria")
        + PermisosDeRegistros("auditoria", RolDeLaAplicacion)
        + PoliticaDesdeElCatalogo(catalogo)
        + Permisos
        + FuncionDeDepuracion("auditoria");

    private static void ValidarEsquema(string nombre)
    {
        if (string.IsNullOrEmpty(nombre) || !nombre.All(c => c is >= 'a' and <= 'z' or '_' or >= '0' and <= '9'))
        {
            throw new ArgumentException("El nombre es un identificador en minúsculas.", nameof(nombre));
        }
    }
}
