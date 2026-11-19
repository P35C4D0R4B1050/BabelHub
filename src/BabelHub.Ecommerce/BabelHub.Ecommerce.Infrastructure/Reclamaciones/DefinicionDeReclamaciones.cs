namespace BabelHub.Ecommerce.Infrastructure.Reclamaciones;

/// <summary>
/// Definición del esquema <c>reclamaciones</c> (<c>plan.md</c> §3, T13): la hoja inmutable con su respuesta aparte, la secuencia por año, la marca de la última anonimización, los disparadores que la hacen inmutable <b>también para el dueño</b> y la función de
/// anonimización. Un propietario propio (<c>reclamaciones_dueno</c>, del que el rol de acceso no es miembro) es dueño de todo; <c>rol_reclamaciones</c> solo puede insertar, leer y actualizar <b>las columnas de estado, respuesta, aviso y marca</b>, nunca borrar, y las columnas
/// personales cambian únicamente por la función <c>SECURITY DEFINER</c> de dos años. Orden de aplicación: <see cref="Sql"/>, <see cref="Propiedad"/>, <see cref="FuncionDeAnonimizacion"/> y <see cref="Permisos"/>.
/// </summary>
public static class DefinicionDeReclamaciones
{
    /// <summary>El texto en que se redactan el detalle y la respuesta de una hoja anonimizada (REC-17): el mismo de <c>HojaDeReclamacion.TextoRedactado</c> (lo comprueba una prueba).</summary>
    public const string TextoRedactado = "[redactado]";

    /// <summary>El valor de nombre, documento, domicilio y correo anonimizados: el de <c>DatosDelConsumidor.ValorAnonimo</c> (lo comprueba una prueba).</summary>
    public const string ValorAnonimo = "[anonimizado]";

    public const string Sql = """
        CREATE SCHEMA IF NOT EXISTS reclamaciones;

        CREATE TABLE IF NOT EXISTS reclamaciones.hojas_reclamacion (
            id uuid PRIMARY KEY,
            numero varchar(11) NOT NULL CONSTRAINT hojas_numero_unico UNIQUE CHECK (numero ~ '^[0-9]{4}-[0-9]{6}$'),
            anio integer NOT NULL CHECK (anio BETWEEN 2000 AND 9999),
            tipo text NOT NULL CHECK (tipo IN ('reclamo', 'queja')),
            nombre text NOT NULL CHECK (char_length(nombre) BETWEEN 1 AND 120),
            documento text NOT NULL CHECK (char_length(documento) BETWEEN 1 AND 20),
            domicilio text NOT NULL CHECK (char_length(domicilio) BETWEEN 1 AND 200),
            correo text NOT NULL CHECK (char_length(correo) BETWEEN 1 AND 254),
            correo_normalizado text CHECK (char_length(correo_normalizado) BETWEEN 1 AND 254),
            bien_contratado text NOT NULL CHECK (char_length(bien_contratado) BETWEEN 1 AND 200),
            monto_reclamado numeric(12, 2) NOT NULL CHECK (monto_reclamado >= 0),
            detalle text NOT NULL CHECK (char_length(detalle) BETWEEN 1 AND 3000),
            pedido_referencia text CHECK (char_length(pedido_referencia) BETWEEN 1 AND 40),
            contenido_hash text CHECK (char_length(contenido_hash) BETWEEN 1 AND 128),
            fecha_registro timestamptz NOT NULL,
            clave_hash text NOT NULL CONSTRAINT hojas_clave_unica UNIQUE CHECK (char_length(clave_hash) BETWEEN 1 AND 128),
            estado text NOT NULL CHECK (estado IN ('pendiente', 'respondido', 'descartado')),
            respuesta text CHECK (char_length(respuesta) BETWEEN 1 AND 3000),
            respondida_en timestamptz,
            respondida_por uuid,
            aviso_estado text CHECK (aviso_estado IN ('pendiente', 'enviado')),
            aviso_enviado_en timestamptz,
            aviso_en_curso_hasta timestamptz,
            descartada_en timestamptz,
            de_cuenta_cancelada boolean NOT NULL DEFAULT false,
            anonimizada_en timestamptz,
            CONSTRAINT hojas_respuesta_con_respondido CHECK ((estado = 'respondido') = (respuesta IS NOT NULL AND respondida_en IS NOT NULL AND respondida_por IS NOT NULL AND aviso_estado IS NOT NULL)),
            CONSTRAINT hojas_descarte_con_descartado CHECK ((estado = 'descartado') = (descartada_en IS NOT NULL)),
            CONSTRAINT hojas_aviso_enviado_con_fecha CHECK ((aviso_estado = 'enviado') = (aviso_enviado_en IS NOT NULL)),
            CONSTRAINT hojas_aviso_en_curso_solo_con_aviso CHECK (aviso_en_curso_hasta IS NULL OR aviso_estado IS NOT NULL),
            CONSTRAINT hojas_correo_normalizado_salvo_anonimizada CHECK ((correo_normalizado IS NULL) = (anonimizada_en IS NOT NULL)),
            CONSTRAINT hojas_contenido_hash_salvo_anonimizada CHECK (contenido_hash IS NOT NULL OR anonimizada_en IS NOT NULL)
        );
        CREATE INDEX IF NOT EXISTS hojas_por_correo ON reclamaciones.hojas_reclamacion (correo_normalizado) WHERE correo_normalizado IS NOT NULL;
        CREATE INDEX IF NOT EXISTS hojas_por_estado ON reclamaciones.hojas_reclamacion (estado, fecha_registro);
        CREATE INDEX IF NOT EXISTS hojas_por_fecha ON reclamaciones.hojas_reclamacion (fecha_registro);
        CREATE INDEX IF NOT EXISTS hojas_por_anonimizar ON reclamaciones.hojas_reclamacion (fecha_registro) WHERE anonimizada_en IS NULL;

        -- Una fila por año de Lima: el siguiente número se avanza con UPDATE … RETURNING bajo su bloqueo.
        CREATE TABLE IF NOT EXISTS reclamaciones.secuencias_de_hoja (
            anio integer PRIMARY KEY CHECK (anio BETWEEN 2000 AND 9999),
            ultimo integer NOT NULL CHECK (ultimo >= 0)
        );

        -- La última corrida completa de la anonimización: la lee la revisión de hojas sin anonimizar.
        CREATE TABLE IF NOT EXISTS reclamaciones.corridas_de_anonimizacion (
            id smallint PRIMARY KEY CHECK (id = 1),
            ultima_en timestamptz NOT NULL
        );

        -- La hoja es inmutable (REC-06): lo que el consumidor escribió no cambia; la respuesta se escribe una vez; el estado solo avanza; la marca de cuenta cancelada solo pasa a verdadero; y solo la
        -- anonimización cambia las columnas personales, al pasar anonimizada_en de nulo a no nulo. También rige para el dueño de la tabla.
        CREATE OR REPLACE FUNCTION reclamaciones.guardar_hoja() RETURNS trigger
        LANGUAGE plpgsql
        SET search_path = pg_catalog
        AS $guardar$
        DECLARE
            anonimizando boolean := OLD.anonimizada_en IS NULL AND NEW.anonimizada_en IS NOT NULL;
        BEGIN
            IF OLD.anonimizada_en IS NOT NULL AND NEW IS DISTINCT FROM OLD THEN
                RAISE EXCEPTION 'Una hoja anonimizada no se modifica' USING ERRCODE = '23000';
            END IF;
            IF NEW.anonimizada_en IS DISTINCT FROM OLD.anonimizada_en AND NOT anonimizando THEN
                RAISE EXCEPTION 'La marca de anonimización solo pasa de nulo a no nulo' USING ERRCODE = '23000';
            END IF;
            IF NEW.id IS DISTINCT FROM OLD.id OR NEW.numero IS DISTINCT FROM OLD.numero OR NEW.anio IS DISTINCT FROM OLD.anio OR NEW.tipo IS DISTINCT FROM OLD.tipo
               OR NEW.bien_contratado IS DISTINCT FROM OLD.bien_contratado OR NEW.monto_reclamado IS DISTINCT FROM OLD.monto_reclamado
               OR NEW.pedido_referencia IS DISTINCT FROM OLD.pedido_referencia OR NEW.fecha_registro IS DISTINCT FROM OLD.fecha_registro OR NEW.clave_hash IS DISTINCT FROM OLD.clave_hash THEN
                RAISE EXCEPTION 'Lo que el consumidor escribió no se modifica' USING ERRCODE = '23000';
            END IF;
            IF NOT anonimizando AND (NEW.nombre IS DISTINCT FROM OLD.nombre OR NEW.documento IS DISTINCT FROM OLD.documento OR NEW.domicilio IS DISTINCT FROM OLD.domicilio
               OR NEW.correo IS DISTINCT FROM OLD.correo OR NEW.correo_normalizado IS DISTINCT FROM OLD.correo_normalizado OR NEW.detalle IS DISTINCT FROM OLD.detalle
               OR NEW.contenido_hash IS DISTINCT FROM OLD.contenido_hash) THEN
                RAISE EXCEPTION 'Los datos personales solo cambian al anonimizar' USING ERRCODE = '23000';
            END IF;
            -- La respuesta se escribe una vez; solo su redacción al anonimizar la cambia.
            IF OLD.respuesta IS NOT NULL AND NEW.respuesta IS DISTINCT FROM OLD.respuesta AND NOT anonimizando THEN
                RAISE EXCEPTION 'La respuesta se escribe una sola vez' USING ERRCODE = '23000';
            END IF;
            IF OLD.respondida_en IS NOT NULL AND (NEW.respondida_en IS DISTINCT FROM OLD.respondida_en OR NEW.respondida_por IS DISTINCT FROM OLD.respondida_por) THEN
                RAISE EXCEPTION 'Quién y cuándo respondió no se modifica' USING ERRCODE = '23000';
            END IF;
            IF OLD.estado <> NEW.estado AND OLD.estado <> 'pendiente' THEN
                RAISE EXCEPTION 'El estado solo avanza desde pendiente' USING ERRCODE = '23000';
            END IF;
            IF OLD.descartada_en IS NOT NULL AND NEW.descartada_en IS DISTINCT FROM OLD.descartada_en THEN
                RAISE EXCEPTION 'La fecha de descarte no se modifica' USING ERRCODE = '23000';
            END IF;
            IF OLD.de_cuenta_cancelada AND NOT NEW.de_cuenta_cancelada THEN
                RAISE EXCEPTION 'La marca de cuenta cancelada solo pasa a verdadero' USING ERRCODE = '23000';
            END IF;
            IF OLD.aviso_estado = 'enviado' AND (NEW.aviso_estado IS DISTINCT FROM 'enviado' OR NEW.aviso_enviado_en IS DISTINCT FROM OLD.aviso_enviado_en) THEN
                RAISE EXCEPTION 'Un aviso enviado no se deshace' USING ERRCODE = '23000';
            END IF;
            RETURN NEW;
        END
        $guardar$;
        DROP TRIGGER IF EXISTS hojas_guardar ON reclamaciones.hojas_reclamacion;
        CREATE TRIGGER hojas_guardar BEFORE UPDATE ON reclamaciones.hojas_reclamacion FOR EACH ROW EXECUTE FUNCTION reclamaciones.guardar_hoja();

        -- Ninguna hoja se elimina ni se vacía la tabla (REC-06, AC-REC-05), tampoco el dueño.
        CREATE OR REPLACE FUNCTION reclamaciones.prohibir_borrado() RETURNS trigger
        LANGUAGE plpgsql
        SET search_path = pg_catalog
        AS $prohibir$
        BEGIN
            RAISE EXCEPTION 'Las hojas de reclamación no se eliminan' USING ERRCODE = '23000';
        END
        $prohibir$;
        DROP TRIGGER IF EXISTS hojas_sin_borrar ON reclamaciones.hojas_reclamacion;
        CREATE TRIGGER hojas_sin_borrar BEFORE DELETE ON reclamaciones.hojas_reclamacion FOR EACH ROW EXECUTE FUNCTION reclamaciones.prohibir_borrado();
        DROP TRIGGER IF EXISTS hojas_sin_vaciar ON reclamaciones.hojas_reclamacion;
        CREATE TRIGGER hojas_sin_vaciar BEFORE TRUNCATE ON reclamaciones.hojas_reclamacion FOR EACH STATEMENT EXECUTE FUNCTION reclamaciones.prohibir_borrado();
        DROP TRIGGER IF EXISTS secuencias_sin_borrar ON reclamaciones.secuencias_de_hoja;
        CREATE TRIGGER secuencias_sin_borrar BEFORE DELETE ON reclamaciones.secuencias_de_hoja FOR EACH ROW EXECUTE FUNCTION reclamaciones.prohibir_borrado();
        DROP TRIGGER IF EXISTS secuencias_sin_vaciar ON reclamaciones.secuencias_de_hoja;
        CREATE TRIGGER secuencias_sin_vaciar BEFORE TRUNCATE ON reclamaciones.secuencias_de_hoja FOR EACH STATEMENT EXECUTE FUNCTION reclamaciones.prohibir_borrado();

        -- La secuencia nunca retrocede: un número no se reutiliza.
        CREATE OR REPLACE FUNCTION reclamaciones.secuencia_solo_crece() RETURNS trigger
        LANGUAGE plpgsql
        SET search_path = pg_catalog
        AS $crece$
        BEGIN
            IF NEW.ultimo < OLD.ultimo OR NEW.anio IS DISTINCT FROM OLD.anio THEN
                RAISE EXCEPTION 'La secuencia de las hojas no retrocede' USING ERRCODE = '23000';
            END IF;
            RETURN NEW;
        END
        $crece$;
        DROP TRIGGER IF EXISTS secuencias_crecen ON reclamaciones.secuencias_de_hoja;
        CREATE TRIGGER secuencias_crecen BEFORE UPDATE ON reclamaciones.secuencias_de_hoja FOR EACH ROW EXECUTE FUNCTION reclamaciones.secuencia_solo_crece();
        """;

    /// <summary>El propietario de todo lo del esquema: el rol de acceso no es miembro de él, así que no puede saltarse nada de esto.</summary>
    public const string Propiedad = """
        ALTER SCHEMA reclamaciones OWNER TO reclamaciones_dueno;
        ALTER TABLE reclamaciones.hojas_reclamacion OWNER TO reclamaciones_dueno;
        ALTER TABLE reclamaciones.secuencias_de_hoja OWNER TO reclamaciones_dueno;
        ALTER TABLE reclamaciones.corridas_de_anonimizacion OWNER TO reclamaciones_dueno;
        ALTER FUNCTION reclamaciones.guardar_hoja() OWNER TO reclamaciones_dueno;
        ALTER FUNCTION reclamaciones.prohibir_borrado() OWNER TO reclamaciones_dueno;
        ALTER FUNCTION reclamaciones.secuencia_solo_crece() OWNER TO reclamaciones_dueno;
        """;

    /// <summary>
    /// La función <c>reclamaciones.anonimizar_hojas()</c> (REC-17, D10): <c>SECURITY DEFINER</c>, <b>sin parámetros</b>, de su propietario, con <c>search_path</c> fijo y solo <c>EXECUTE</c> para el rol de acceso. <b>Aplica ella misma el predicado de dos años por día de Lima</b> con la hora
    /// de la base (no recibe ids ni fechas del llamador): anonimiza nombre, documento, domicilio y correo, deja el correo normalizado y el resumen del contenido en nulo y redacta el detalle y la respuesta; conserva número, fecha, tipo, estado, bien, monto y referencia del pedido. Solo
    /// toca filas con <c>anonimizada_en</c> nulo (idempotente), por lotes de 5 000, y devuelve cuántas cambió; cuando un lote no se llena, marca la corrida como completa.
    /// </summary>
    public const string FuncionDeAnonimizacion = """
        CREATE OR REPLACE FUNCTION reclamaciones.anonimizar_hojas() RETURNS integer
        LANGUAGE plpgsql
        SECURITY DEFINER
        SET search_path = pg_catalog
        AS $anonimizar$
        DECLARE
            lote integer := 0;
        BEGIN
            PERFORM pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtext('reclamaciones.anonimizar_hojas'));
            WITH vencidas AS (
                SELECT id FROM reclamaciones.hojas_reclamacion
                WHERE anonimizada_en IS NULL
                  AND ((fecha_registro AT TIME ZONE 'America/Lima')::date + interval '2 years')::date <= (pg_catalog.now() AT TIME ZONE 'America/Lima')::date
                ORDER BY fecha_registro, id
                LIMIT 5000
                FOR UPDATE
            )
            UPDATE reclamaciones.hojas_reclamacion h
            SET nombre = '[anonimizado]', documento = '[anonimizado]', domicilio = '[anonimizado]', correo = '[anonimizado]', correo_normalizado = NULL, contenido_hash = NULL,
                detalle = '[redactado]', respuesta = CASE WHEN h.respuesta IS NULL THEN NULL ELSE '[redactado]' END, anonimizada_en = pg_catalog.now()
            FROM vencidas v
            WHERE h.id = v.id;
            GET DIAGNOSTICS lote = ROW_COUNT;
            IF lote < 5000 THEN
                INSERT INTO reclamaciones.corridas_de_anonimizacion (id, ultima_en) VALUES (1, pg_catalog.now())
                ON CONFLICT (id) DO UPDATE SET ultima_en = EXCLUDED.ultima_en;
            END IF;
            RETURN lote;
        END
        $anonimizar$;
        ALTER FUNCTION reclamaciones.anonimizar_hojas() OWNER TO reclamaciones_dueno;
        REVOKE ALL ON FUNCTION reclamaciones.anonimizar_hojas() FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION reclamaciones.anonimizar_hojas() TO rol_reclamaciones;
        """;

    /// <summary>
    /// Lo que <c>rol_reclamaciones</c> puede hacer: leer e insertar, actualizar <b>solo las columnas de estado, respuesta, aviso y marca</b> y avanzar la secuencia; nada de <c>DELETE</c> ni <c>TRUNCATE</c> (los disparadores lo prohíben también al dueño).
    /// <c>SELECT … FOR UPDATE</c> necesita el privilegio <c>UPDATE</c>.
    /// </summary>
    public const string Permisos = """
        GRANT USAGE ON SCHEMA reclamaciones TO rol_reclamaciones;
        GRANT SELECT, INSERT ON reclamaciones.hojas_reclamacion TO rol_reclamaciones;
        GRANT UPDATE (estado, respuesta, respondida_en, respondida_por, aviso_estado, aviso_enviado_en, aviso_en_curso_hasta, descartada_en, de_cuenta_cancelada) ON reclamaciones.hojas_reclamacion TO rol_reclamaciones;
        GRANT SELECT, INSERT ON reclamaciones.secuencias_de_hoja TO rol_reclamaciones;
        GRANT UPDATE (ultimo) ON reclamaciones.secuencias_de_hoja TO rol_reclamaciones;
        GRANT SELECT ON reclamaciones.corridas_de_anonimizacion TO rol_reclamaciones;
        """;
}
