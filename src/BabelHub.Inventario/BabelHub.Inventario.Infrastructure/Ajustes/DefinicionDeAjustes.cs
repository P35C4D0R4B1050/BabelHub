namespace BabelHub.Inventario.Infrastructure.Ajustes;

/// <summary>
/// La migración del esquema <c>ajustes</c> del Inventario (<c>inventario-ajustes</c> §3, T10), hacia adelante y repetible. La corre la cuenta de despliegue; el propietario de las tablas y la contraseña de los roles con
/// inicio de sesión los pone el aprovisionamiento aparte, como con <c>fuentes</c> y <c>reservas</c>. Las restricciones son la <b>última defensa</b> de las reglas del Dominio:
/// <list type="bullet">
/// <item><c>ajustes</c>: motivo cerrado, cantidades de 0 a 1 000 000, nota de hasta 200, y por motivo (la merma y el daño bajan el físico; el ingreso y la devolución lo suben; la corrección deja el físico en el conteo real, distinto
/// del anterior); la devolución exige <c>devolucion_id</c> y <c>pedido_id</c> y solo ella los lleva; <b>único <c>(devolucion_id, edicion_id)</c></b> (AJU-16).</item>
/// <item><c>ingresos</c>: cantidad de 1 a 5 000, referencia de hasta 60 con letras, dígitos, espacio, <c>-</c>, <c>/</c> y <c>.</c>, y nota de hasta 200.</item>
/// <item><c>operaciones_idempotentes</c>: clave primaria <c>(usuario_id, operacion, clave)</c> (AJU-22): la clave se acota por usuario y operación.</item>
/// </list>
/// <b>Solo inserción:</b> <c>rol_ajustes</c> inserta y lee; disparadores por fila y por sentencia rechazan <c>UPDATE</c>, <c>DELETE</c> y <c>TRUNCATE</c> en <c>ajustes</c> e <c>ingresos</c>, salvo el <c>UPDATE</c> de la función de
/// depuración (<c>ajustes.anonimizar_registros()</c>, <c>SECURITY DEFINER</c>, sin parámetros, con su propietario propio y <c>search_path</c> fijo), que el disparador reconoce por <c>current_user</c> y <b>valida</b>: solo acepta
/// <c>usuario_id</c> a nulo y <c>nota</c> nula o vacía, en filas de más de 2 años y con las demás columnas intactas (AJU-21, D05). La purga de las operaciones idempotentes a los 30 días es otra función sin parámetros del mismo
/// propietario, porque <c>rol_ajustes</c> no tiene <c>DELETE</c>. <b>Seguridad por filas</b> (<c>ENABLE</c> y <c>FORCE</c>) sobre <c>fuente_id</c>, con la política de <c>stock</c> (<c>inventario-fuentes</c> D09): el alcance de datos
/// de la transacción es <c>red</c> o la sucursal de la fila; sin valor, deniega. Sin claves foráneas hacia <c>fuentes</c> (regla 13).
/// </summary>
public static class DefinicionDeAjustes
{
    public const string Esquema = "ajustes";

    /// <summary>El rol del esquema que usa <c>EjecutarComo</c>: <c>rol_&lt;esquema&gt;</c>.</summary>
    public const string RolDeLaAplicacion = "rol_ajustes";

    /// <summary>El propietario fijo de las funciones de depuración (sin inicio de sesión): el disparador guardián lo reconoce por <c>current_user</c>.</summary>
    public const string PropietarioDeLasFunciones = "ajustes_depuracion_dueno";

    /// <summary>El rol que ejecuta la depuración (con su propia credencial en producción; la aplicación no es miembro): solo <c>EXECUTE</c> de las dos funciones.</summary>
    public const string RolDeDepuracion = "ajustes_depuracion";

    /// <summary>Los tres roles sin inicio de sesión (idempotente).</summary>
    public const string Roles = """
        DO $roles$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rol_ajustes') THEN CREATE ROLE rol_ajustes NOLOGIN; END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ajustes_depuracion_dueno') THEN CREATE ROLE ajustes_depuracion_dueno NOLOGIN; END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ajustes_depuracion') THEN CREATE ROLE ajustes_depuracion NOLOGIN; END IF;
        END
        $roles$;
        """;

    /// <summary>Esquema, tablas, restricciones, índices, política de filas y disparadores de solo inserción (idempotente).</summary>
    public const string Sql = """
        CREATE SCHEMA IF NOT EXISTS ajustes;

        CREATE TABLE IF NOT EXISTS ajustes.ajustes (
            id uuid PRIMARY KEY,
            fuente_id uuid NOT NULL,
            edicion_id uuid NOT NULL,
            motivo text NOT NULL,
            cantidad_anterior integer NOT NULL,
            cantidad_posterior integer NOT NULL,
            conteo_real integer NULL,
            nota text NULL,
            usuario_id uuid NULL,
            fecha timestamptz NOT NULL,
            devolucion_id uuid NULL,
            pedido_id uuid NULL,
            CONSTRAINT ck_ajustes_motivo CHECK (motivo IN ('merma', 'danio', 'correccion-de-conteo', 'ingreso-de-mercaderia', 'devolucion')),
            CONSTRAINT ck_ajustes_cantidades CHECK (cantidad_anterior BETWEEN 0 AND 1000000 AND cantidad_posterior BETWEEN 0 AND 1000000),
            CONSTRAINT ck_ajustes_nota CHECK (nota IS NULL OR char_length(nota) <= 200),
            CONSTRAINT ck_ajustes_resta CHECK (motivo NOT IN ('merma', 'danio') OR (cantidad_posterior < cantidad_anterior AND conteo_real IS NULL)),
            CONSTRAINT ck_ajustes_suma CHECK (motivo NOT IN ('ingreso-de-mercaderia', 'devolucion') OR (cantidad_posterior > cantidad_anterior AND conteo_real IS NULL)),
            CONSTRAINT ck_ajustes_conteo CHECK (motivo <> 'correccion-de-conteo' OR (conteo_real IS NOT NULL AND cantidad_posterior = conteo_real AND conteo_real <> cantidad_anterior)),
            CONSTRAINT ck_ajustes_devolucion CHECK ((motivo = 'devolucion') = (devolucion_id IS NOT NULL AND pedido_id IS NOT NULL)),
            CONSTRAINT ck_ajustes_devolucion_y_pedido CHECK ((devolucion_id IS NULL) = (pedido_id IS NULL))
        );

        -- El reingreso es idempotente por devolución y edición (AJU-16): la unicidad es la última defensa del bloqueo asesor de la devolución.
        CREATE UNIQUE INDEX IF NOT EXISTS ux_ajustes_devolucion_edicion ON ajustes.ajustes (devolucion_id, edicion_id) WHERE motivo = 'devolucion';
        CREATE INDEX IF NOT EXISTS ix_ajustes_historial ON ajustes.ajustes (fuente_id, fecha DESC, id DESC);
        CREATE INDEX IF NOT EXISTS ix_ajustes_edicion ON ajustes.ajustes (edicion_id);
        CREATE INDEX IF NOT EXISTS ix_ajustes_pedido ON ajustes.ajustes (pedido_id) WHERE pedido_id IS NOT NULL;

        CREATE TABLE IF NOT EXISTS ajustes.ingresos (
            id uuid PRIMARY KEY,
            fuente_id uuid NOT NULL,
            edicion_id uuid NOT NULL,
            cantidad integer NOT NULL,
            referencia text NULL,
            nota text NULL,
            usuario_id uuid NULL,
            fecha timestamptz NOT NULL,
            ajuste_id uuid NOT NULL,
            CONSTRAINT ck_ingresos_cantidad CHECK (cantidad BETWEEN 1 AND 5000),
            CONSTRAINT ck_ingresos_referencia CHECK (referencia IS NULL OR (char_length(referencia) BETWEEN 1 AND 60 AND referencia ~ '^[A-Za-z0-9 ./-]+$')),
            CONSTRAINT ck_ingresos_nota CHECK (nota IS NULL OR char_length(nota) <= 200)
        );

        CREATE INDEX IF NOT EXISTS ix_ingresos_fecha ON ajustes.ingresos (fecha DESC, id DESC);
        CREATE INDEX IF NOT EXISTS ix_ingresos_edicion ON ajustes.ingresos (edicion_id, fecha DESC);

        CREATE TABLE IF NOT EXISTS ajustes.operaciones_idempotentes (
            usuario_id uuid NOT NULL,
            operacion text NOT NULL,
            clave text NOT NULL,
            huella_de_carga text NOT NULL,
            resultado jsonb NOT NULL,
            creada_en timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT pk_ajustes_operaciones_idempotentes PRIMARY KEY (usuario_id, operacion, clave),
            CONSTRAINT ck_ajustes_operaciones_operacion CHECK (operacion IN ('ajuste', 'ingreso')),
            CONSTRAINT ck_ajustes_operaciones_clave CHECK (length(clave) BETWEEN 16 AND 80),
            CONSTRAINT ck_ajustes_operaciones_huella CHECK (huella_de_carga ~ '^[0-9a-f]{64}$')
        );

        CREATE INDEX IF NOT EXISTS ix_ajustes_operaciones_creada ON ajustes.operaciones_idempotentes (creada_en);

        -- Seguridad por filas (D09 de inventario-fuentes): el alcance de datos de la transacción es «red» o la sucursal de la fila; sin valor, deniega.
        ALTER TABLE ajustes.ajustes ENABLE ROW LEVEL SECURITY;
        ALTER TABLE ajustes.ajustes FORCE ROW LEVEL SECURITY;
        DROP POLICY IF EXISTS por_alcance ON ajustes.ajustes;
        CREATE POLICY por_alcance ON ajustes.ajustes FOR ALL
            USING (current_setting('babelhub.alcance', true) = 'red' OR current_setting('babelhub.alcance', true) = fuente_id::text)
            WITH CHECK (current_setting('babelhub.alcance', true) = 'red' OR current_setting('babelhub.alcance', true) = fuente_id::text);

        ALTER TABLE ajustes.ingresos ENABLE ROW LEVEL SECURITY;
        ALTER TABLE ajustes.ingresos FORCE ROW LEVEL SECURITY;
        DROP POLICY IF EXISTS por_alcance ON ajustes.ingresos;
        CREATE POLICY por_alcance ON ajustes.ingresos FOR ALL
            USING (current_setting('babelhub.alcance', true) = 'red' OR current_setting('babelhub.alcance', true) = fuente_id::text)
            WITH CHECK (current_setting('babelhub.alcance', true) = 'red' OR current_setting('babelhub.alcance', true) = fuente_id::text);

        -- Los disparadores guardianes: ningún UPDATE, DELETE ni TRUNCATE, salvo el UPDATE de la función de depuración (current_user = su propietario), que solo puede retirar el usuario y la nota de filas de más de 2 años.
        CREATE OR REPLACE FUNCTION ajustes.rechazar_modificacion_de_ajustes() RETURNS trigger
        LANGUAGE plpgsql
        SET search_path = pg_catalog
        AS $guardian$
        BEGIN
            IF TG_OP = 'UPDATE' AND current_user = 'ajustes_depuracion_dueno' THEN
                IF OLD.fecha < pg_catalog.now() - interval '2 years'
                   AND NEW.id = OLD.id AND NEW.fuente_id = OLD.fuente_id AND NEW.edicion_id = OLD.edicion_id AND NEW.motivo = OLD.motivo
                   AND NEW.cantidad_anterior = OLD.cantidad_anterior AND NEW.cantidad_posterior = OLD.cantidad_posterior
                   AND NEW.conteo_real IS NOT DISTINCT FROM OLD.conteo_real AND NEW.fecha = OLD.fecha
                   AND NEW.devolucion_id IS NOT DISTINCT FROM OLD.devolucion_id AND NEW.pedido_id IS NOT DISTINCT FROM OLD.pedido_id
                   AND (NEW.usuario_id IS NULL OR NEW.usuario_id IS NOT DISTINCT FROM OLD.usuario_id)
                   AND (NEW.nota IS NULL OR NEW.nota = '' OR NEW.nota IS NOT DISTINCT FROM OLD.nota) THEN
                    RETURN NEW;
                END IF;
            END IF;
            RAISE EXCEPTION 'Los ajustes son de solo inserción (%).', TG_OP USING ERRCODE = 'insufficient_privilege';
        END
        $guardian$;

        CREATE OR REPLACE FUNCTION ajustes.rechazar_modificacion_de_ingresos() RETURNS trigger
        LANGUAGE plpgsql
        SET search_path = pg_catalog
        AS $guardian$
        BEGIN
            IF TG_OP = 'UPDATE' AND current_user = 'ajustes_depuracion_dueno' THEN
                IF OLD.fecha < pg_catalog.now() - interval '2 years'
                   AND NEW.id = OLD.id AND NEW.fuente_id = OLD.fuente_id AND NEW.edicion_id = OLD.edicion_id AND NEW.cantidad = OLD.cantidad
                   AND NEW.referencia IS NOT DISTINCT FROM OLD.referencia AND NEW.fecha = OLD.fecha AND NEW.ajuste_id = OLD.ajuste_id
                   AND (NEW.usuario_id IS NULL OR NEW.usuario_id IS NOT DISTINCT FROM OLD.usuario_id)
                   AND (NEW.nota IS NULL OR NEW.nota = '' OR NEW.nota IS NOT DISTINCT FROM OLD.nota) THEN
                    RETURN NEW;
                END IF;
            END IF;
            RAISE EXCEPTION 'Los ingresos son de solo inserción (%).', TG_OP USING ERRCODE = 'insufficient_privilege';
        END
        $guardian$;

        CREATE OR REPLACE FUNCTION ajustes.rechazar_vaciado() RETURNS trigger
        LANGUAGE plpgsql
        SET search_path = pg_catalog
        AS $guardian$
        BEGIN
            RAISE EXCEPTION 'Los ajustes son de solo inserción (%).', TG_OP USING ERRCODE = 'insufficient_privilege';
        END
        $guardian$;

        DROP TRIGGER IF EXISTS ajustes_solo_insercion_por_fila ON ajustes.ajustes;
        CREATE TRIGGER ajustes_solo_insercion_por_fila BEFORE UPDATE OR DELETE ON ajustes.ajustes FOR EACH ROW EXECUTE FUNCTION ajustes.rechazar_modificacion_de_ajustes();
        DROP TRIGGER IF EXISTS ajustes_solo_insercion_por_sentencia ON ajustes.ajustes;
        CREATE TRIGGER ajustes_solo_insercion_por_sentencia BEFORE TRUNCATE ON ajustes.ajustes FOR EACH STATEMENT EXECUTE FUNCTION ajustes.rechazar_vaciado();
        ALTER TABLE ajustes.ajustes ENABLE ALWAYS TRIGGER ajustes_solo_insercion_por_fila;
        ALTER TABLE ajustes.ajustes ENABLE ALWAYS TRIGGER ajustes_solo_insercion_por_sentencia;

        DROP TRIGGER IF EXISTS ingresos_solo_insercion_por_fila ON ajustes.ingresos;
        CREATE TRIGGER ingresos_solo_insercion_por_fila BEFORE UPDATE OR DELETE ON ajustes.ingresos FOR EACH ROW EXECUTE FUNCTION ajustes.rechazar_modificacion_de_ingresos();
        DROP TRIGGER IF EXISTS ingresos_solo_insercion_por_sentencia ON ajustes.ingresos;
        CREATE TRIGGER ingresos_solo_insercion_por_sentencia BEFORE TRUNCATE ON ajustes.ingresos FOR EACH STATEMENT EXECUTE FUNCTION ajustes.rechazar_vaciado();
        ALTER TABLE ajustes.ingresos ENABLE ALWAYS TRIGGER ingresos_solo_insercion_por_fila;
        ALTER TABLE ajustes.ingresos ENABLE ALWAYS TRIGGER ingresos_solo_insercion_por_sentencia;
        """;

    /// <summary>
    /// Las funciones de depuración (T13): <c>ajustes.anonimizar_registros()</c> (AJU-21: a los 2 años <c>usuario_id</c> a nulo y <c>nota</c> a nulo, en lotes de 5 000 por tabla; devuelve cuántas filas cambió y repetirla no
    /// cambia nada) y <c>ajustes.purgar_operaciones()</c> (las operaciones idempotentes de más de 30 días). <c>SECURITY DEFINER</c>, <b>sin parámetros</b>, con <c>search_path</c> fijo y propietario propio; como la política de
    /// filas también alcanza al propietario, la función fija <c>babelhub.alcance = 'red'</c> dentro de su transacción. Solo el rol de depuración tiene <c>EXECUTE</c>.
    /// </summary>
    public const string FuncionesDeDepuracion = """
        CREATE OR REPLACE FUNCTION ajustes.anonimizar_registros() RETURNS integer
        LANGUAGE plpgsql
        SECURITY DEFINER
        SET search_path = pg_catalog
        AS $depurar$
        DECLARE
            cambiados integer := 0;
            n integer;
        BEGIN
            PERFORM pg_catalog.set_config('babelhub.alcance', 'red', true);

            UPDATE ajustes.ajustes a SET usuario_id = NULL, nota = NULL
            WHERE a.id IN (
                SELECT id FROM ajustes.ajustes
                WHERE fecha < pg_catalog.now() - interval '2 years' AND (usuario_id IS NOT NULL OR nota IS NOT NULL)
                ORDER BY fecha, id LIMIT 5000);
            GET DIAGNOSTICS n = ROW_COUNT;
            cambiados := cambiados + n;

            UPDATE ajustes.ingresos i SET usuario_id = NULL, nota = NULL
            WHERE i.id IN (
                SELECT id FROM ajustes.ingresos
                WHERE fecha < pg_catalog.now() - interval '2 years' AND (usuario_id IS NOT NULL OR nota IS NOT NULL)
                ORDER BY fecha, id LIMIT 5000);
            GET DIAGNOSTICS n = ROW_COUNT;
            cambiados := cambiados + n;

            RETURN cambiados;
        END
        $depurar$;

        CREATE OR REPLACE FUNCTION ajustes.purgar_operaciones() RETURNS integer
        LANGUAGE plpgsql
        SECURITY DEFINER
        SET search_path = pg_catalog
        AS $purgar$
        DECLARE
            n integer;
        BEGIN
            DELETE FROM ajustes.operaciones_idempotentes WHERE creada_en < pg_catalog.now() - interval '30 days';
            GET DIAGNOSTICS n = ROW_COUNT;
            RETURN n;
        END
        $purgar$;
        """;

    /// <summary>
    /// Propietario y permisos de las funciones y de las tablas: <c>rol_ajustes</c> inserta y lee (ninguna baja); el propietario de las funciones cambia solo <c>(usuario_id, nota)</c>, lee <c>fecha</c> y borra las
    /// operaciones idempotentes; el rol de depuración solo ejecuta las dos funciones. Se aplica después de que el aprovisionamiento deja las tablas a su propietario.
    /// </summary>
    public const string Permisos = """
        GRANT USAGE ON SCHEMA ajustes TO rol_ajustes, ajustes_depuracion_dueno, ajustes_depuracion;
        REVOKE ALL ON ajustes.ajustes, ajustes.ingresos, ajustes.operaciones_idempotentes FROM PUBLIC;
        GRANT SELECT, INSERT ON ajustes.ajustes, ajustes.ingresos, ajustes.operaciones_idempotentes TO rol_ajustes;
        GRANT SELECT ON ajustes.ajustes, ajustes.ingresos TO ajustes_depuracion_dueno;
        GRANT UPDATE (usuario_id, nota) ON ajustes.ajustes, ajustes.ingresos TO ajustes_depuracion_dueno;
        GRANT SELECT, DELETE ON ajustes.operaciones_idempotentes TO ajustes_depuracion_dueno;
        ALTER FUNCTION ajustes.anonimizar_registros() OWNER TO ajustes_depuracion_dueno;
        ALTER FUNCTION ajustes.purgar_operaciones() OWNER TO ajustes_depuracion_dueno;
        REVOKE EXECUTE ON FUNCTION ajustes.anonimizar_registros(), ajustes.purgar_operaciones() FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION ajustes.anonimizar_registros(), ajustes.purgar_operaciones() TO ajustes_depuracion;
        """;

    /// <summary>La migración completa del módulo, en el orden que exige (roles, tablas, funciones y permisos).</summary>
    public const string MigracionCompleta = Roles + Sql + FuncionesDeDepuracion + Permisos;
}
