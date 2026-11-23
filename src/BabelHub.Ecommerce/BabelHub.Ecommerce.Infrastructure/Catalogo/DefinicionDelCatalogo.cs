namespace BabelHub.Ecommerce.Infrastructure.Catalogo;

/// <summary>
/// Definición de las tablas del esquema <c>catalogo</c> (<c>plan.md</c> §3) con el permiso mínimo de su rol <c>rol_catalogo</c>; la migración las incorpora tal cual (como <c>DefinicionDeLaWeb</c>). Las tablas y las funciones son del
/// propietario; la aplicación entra con <c>EjecutarComo(catalogo)</c> y solo hace lo que el módulo necesita. Necesita las extensiones <c>unaccent</c> y <c>pg_trgm</c> (módulos de contribución de la imagen oficial; R02).
/// </summary>
public static class DefinicionDelCatalogo
{
    public const string Sql = """
        CREATE SCHEMA IF NOT EXISTS catalogo;
        CREATE EXTENSION IF NOT EXISTS unaccent;
        CREATE EXTENSION IF NOT EXISTS pg_trgm;

        -- Texto sin tildes ni mayúsculas para la búsqueda (D10): una función inmutable, porque unaccent() no lo es y no podría estar en un índice.
        CREATE OR REPLACE FUNCTION catalogo.normalizar(texto text) RETURNS text
            LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
            AS $$ SELECT lower(public.unaccent('public.unaccent', texto)) $$;

        -- Una obra es la raíz del agregado: una sola versión cubre la edición concurrente de la obra y de sus ediciones.
        CREATE TABLE IF NOT EXISTS catalogo.obras (
            id uuid PRIMARY KEY,
            titulo text NOT NULL CHECK (char_length(titulo) BETWEEN 1 AND 200),
            autor text NOT NULL CHECK (char_length(autor) BETWEEN 1 AND 200),
            editorial text NOT NULL CHECK (char_length(editorial) BETWEEN 1 AND 200),
            categoria text NOT NULL CHECK (char_length(categoria) BETWEEN 1 AND 100),
            portada_clave text NOT NULL CHECK (portada_clave ~ '^[0-9a-f]{32}$'),
            portada_huella bytea NOT NULL CHECK (octet_length(portada_huella) = 32),
            portada_tipo text NOT NULL CHECK (portada_tipo IN ('jpeg', 'png', 'webp')),
            estado text NOT NULL CHECK (estado IN ('activa', 'baja')),
            dada_de_baja_en timestamptz,
            version bigint NOT NULL CHECK (version >= 1),
            creada_en timestamptz NOT NULL,
            CONSTRAINT obras_baja_con_fecha CHECK ((estado = 'baja') = (dada_de_baja_en IS NOT NULL))
        );
        -- La descripción opcional (CAT-01, D18): nula cuando falta, nunca una cadena vacía; la agrega una migración idempotente sin reescribir filas.
        ALTER TABLE catalogo.obras ADD COLUMN IF NOT EXISTS descripcion text CHECK (char_length(descripcion) BETWEEN 1 AND 600);
        CREATE UNIQUE INDEX IF NOT EXISTS obras_portada_clave ON catalogo.obras (portada_clave);
        CREATE INDEX IF NOT EXISTS obras_portada_huella ON catalogo.obras (portada_huella);
        CREATE INDEX IF NOT EXISTS obras_por_categoria ON catalogo.obras (categoria);
        CREATE INDEX IF NOT EXISTS obras_titulo_trgm ON catalogo.obras USING gin (catalogo.normalizar(titulo) gin_trgm_ops);
        CREATE INDEX IF NOT EXISTS obras_autor_trgm ON catalogo.obras USING gin (catalogo.normalizar(autor) gin_trgm_ops);
        CREATE INDEX IF NOT EXISTS obras_editorial_trgm ON catalogo.obras USING gin (catalogo.normalizar(editorial) gin_trgm_ops);
        CREATE INDEX IF NOT EXISTS obras_orden ON catalogo.obras (titulo, id);

        -- Una edición: a lo más una impresa y una digital por obra entre las que no están dadas de baja (índice parcial). El ISBN-13 es único entre todas, también las dadas de
        -- baja (sigue reservado): la unicidad la garantiza el índice, nunca una consulta previa (CAT-17).
        CREATE TABLE IF NOT EXISTS catalogo.ediciones (
            id uuid PRIMARY KEY,
            obra_id uuid NOT NULL REFERENCES catalogo.obras (id),
            tipo text NOT NULL CHECK (tipo IN ('impresa', 'digital')),
            isbn13 text NOT NULL CHECK (isbn13 ~ '^97[89][0-9]{10}$'),
            isbn_ingresado text NOT NULL CHECK (char_length(isbn_ingresado) BETWEEN 10 AND 40),
            precio numeric(12, 2) NOT NULL CHECK (precio >= 0),
            estado text NOT NULL CHECK (estado IN ('activa', 'retirada_de_venta', 'baja')),
            motivo_de_retiro text CHECK (char_length(motivo_de_retiro) <= 300),
            retirada_en timestamptz,
            dada_de_baja_en timestamptz,
            CONSTRAINT ediciones_isbn13_unico UNIQUE (isbn13),
            CONSTRAINT ediciones_retiro_solo_digital CHECK (estado <> 'retirada_de_venta' OR tipo = 'digital'),
            CONSTRAINT ediciones_retiro_completo CHECK (estado <> 'retirada_de_venta' OR (retirada_en IS NOT NULL AND motivo_de_retiro IS NOT NULL)),
            CONSTRAINT ediciones_activa_sin_retiro CHECK (estado <> 'activa' OR retirada_en IS NULL),
            CONSTRAINT ediciones_baja_con_fecha CHECK ((estado = 'baja') = (dada_de_baja_en IS NOT NULL))
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ediciones_un_tipo_por_obra ON catalogo.ediciones (obra_id, tipo) WHERE estado <> 'baja';
        CREATE INDEX IF NOT EXISTS ediciones_por_obra ON catalogo.ediciones (obra_id);
        CREATE INDEX IF NOT EXISTS ediciones_por_precio ON catalogo.ediciones (precio);

        -- La licencia de una edición digital (CAT-05). La regla «vigencia posterior a hoy» la aplica el Dominio solo al registrar o cambiar la licencia, no aquí.
        CREATE TABLE IF NOT EXISTS catalogo.licencias (
            edicion_id uuid PRIMARY KEY REFERENCES catalogo.ediciones (id),
            tipo text NOT NULL CHECK (tipo IN ('comercial', 'dominio_publico')),
            titular text NOT NULL CHECK (char_length(titular) BETWEEN 1 AND 200),
            territorios text[] NOT NULL CHECK (cardinality(territorios) BETWEEN 1 AND 250),
            vigente_hasta date,
            formato text NOT NULL CHECK (formato IN ('epub', 'pdf')),
            CONSTRAINT licencias_vigencia CHECK ((tipo = 'comercial') = (vigente_hasta IS NOT NULL))
        );

        -- Solo la referencia al libro: el archivo nunca se guarda aquí (regla 11).
        CREATE TABLE IF NOT EXISTS catalogo.archivos_digitales (
            edicion_id uuid PRIMARY KEY REFERENCES catalogo.ediciones (id),
            clave_en_almacen text NOT NULL CHECK (clave_en_almacen ~ '^[0-9a-f]{32}$'),
            tipo_detectado text NOT NULL CHECK (tipo_detectado IN ('epub', 'pdf')),
            tamano bigint NOT NULL CHECK (tamano BETWEEN 1 AND 52428800),
            huella_sha256 bytea NOT NULL CHECK (octet_length(huella_sha256) = 32),
            subido_en timestamptz NOT NULL,
            subido_por uuid NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS archivos_digitales_clave ON catalogo.archivos_digitales (clave_en_almacen);
        CREATE INDEX IF NOT EXISTS archivos_digitales_huella ON catalogo.archivos_digitales (huella_sha256);

        -- Archivos subidos antes de referenciarlos (D05): la fila nace «subiendo» antes de escribir el objeto. Las claves del almacén son únicas en cada tabla que las guarda.
        CREATE TABLE IF NOT EXISTS catalogo.archivos_pendientes (
            id uuid PRIMARY KEY,
            usuario_id uuid NOT NULL,
            finalidad text NOT NULL CHECK (finalidad IN ('portada', 'libro')),
            clave_en_almacen text NOT NULL CHECK (clave_en_almacen ~ '^[0-9a-f]{32}$'),
            estado text NOT NULL CHECK (estado IN ('subiendo', 'lista', 'consumida', 'descarte')),
            tipo_detectado text CHECK (tipo_detectado IN ('epub', 'pdf', 'jpeg', 'png', 'webp')),
            tamano bigint CHECK (tamano BETWEEN 1 AND 52428800),
            huella_sha256 bytea CHECK (octet_length(huella_sha256) = 32),
            creado_en timestamptz NOT NULL,
            vence_en timestamptz NOT NULL,
            consumido_en timestamptz,
            CONSTRAINT archivos_pendientes_completa CHECK (estado IN ('subiendo', 'descarte') OR (tipo_detectado IS NOT NULL AND tamano IS NOT NULL AND huella_sha256 IS NOT NULL)),
            CONSTRAINT archivos_pendientes_consumo CHECK ((estado = 'consumida' AND consumido_en IS NOT NULL) OR (estado IN ('subiendo', 'lista') AND consumido_en IS NULL) OR estado = 'descarte')
        );
        CREATE UNIQUE INDEX IF NOT EXISTS archivos_pendientes_clave ON catalogo.archivos_pendientes (clave_en_almacen);
        CREATE INDEX IF NOT EXISTS archivos_pendientes_por_estado ON catalogo.archivos_pendientes (estado, creado_en);

        -- Constancia local de los ingresos de mercadería pedidos al Inventario (D04): un solo «pendiente» por edición y una clave de idempotencia única.
        CREATE TABLE IF NOT EXISTS catalogo.ingresos_solicitados (
            id uuid PRIMARY KEY,
            edicion_id uuid NOT NULL REFERENCES catalogo.ediciones (id),
            clave_de_idempotencia uuid NOT NULL,
            usuario_id uuid NOT NULL,
            estado text NOT NULL CHECK (estado IN ('pendiente', 'registrado', 'fallido')),
            ingreso_id uuid,
            cantidad integer NOT NULL CHECK (cantidad BETWEEN 1 AND 5000),
            referencia text CHECK (char_length(referencia) <= 80),
            nota text CHECK (char_length(nota) <= 500),
            solicitado_en timestamptz NOT NULL,
            intentos integer NOT NULL CHECK (intentos >= 1),
            ultimo_intento_en timestamptz NOT NULL,
            resultado_en timestamptz,
            CONSTRAINT ingresos_solicitados_clave_unica UNIQUE (clave_de_idempotencia),
            CONSTRAINT ingresos_solicitados_registrado_con_ingreso CHECK ((estado = 'registrado') = (ingreso_id IS NOT NULL)),
            CONSTRAINT ingresos_solicitados_resuelto_con_fecha CHECK ((estado = 'pendiente') = (resultado_en IS NULL))
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ingresos_solicitados_un_pendiente_por_edicion ON catalogo.ingresos_solicitados (edicion_id) WHERE estado = 'pendiente';
        CREATE UNIQUE INDEX IF NOT EXISTS ingresos_solicitados_ingreso_unico ON catalogo.ingresos_solicitados (ingreso_id) WHERE ingreso_id IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ingresos_solicitados_por_edicion ON catalogo.ingresos_solicitados (edicion_id, solicitado_en DESC);

        -- Al confirmar: una digital tiene licencia y archivo y una impresa no. Es una restricción diferida y su función es SECURITY DEFINER de dueño propio con el search_path
        -- fijo, porque en el COMMIT rige el rol de acceso y no rol_catalogo (como auditoria.anonimizar_registros()).
        CREATE OR REPLACE FUNCTION catalogo.verificar_edicion() RETURNS trigger
            LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, catalogo
            AS $$
        DECLARE
            v_fila jsonb := to_jsonb(COALESCE(NEW, OLD));
            v_id uuid := (CASE WHEN TG_TABLE_NAME = 'ediciones' THEN v_fila ->> 'id' ELSE v_fila ->> 'edicion_id' END)::uuid;
            v_tipo text;
        BEGIN
            SELECT tipo INTO v_tipo FROM catalogo.ediciones WHERE id = v_id;
            IF v_tipo IS NULL THEN
                RETURN NULL;
            END IF;
            IF v_tipo = 'digital' AND (NOT EXISTS (SELECT 1 FROM catalogo.licencias WHERE edicion_id = v_id)
                                       OR NOT EXISTS (SELECT 1 FROM catalogo.archivos_digitales WHERE edicion_id = v_id)) THEN
                RAISE EXCEPTION 'Una edición digital tiene licencia y archivo.' USING ERRCODE = '23514';
            END IF;
            IF v_tipo = 'impresa' AND (EXISTS (SELECT 1 FROM catalogo.licencias WHERE edicion_id = v_id)
                                       OR EXISTS (SELECT 1 FROM catalogo.archivos_digitales WHERE edicion_id = v_id)) THEN
                RAISE EXCEPTION 'Una edición impresa no tiene licencia ni archivo.' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END
        $$;

        DROP TRIGGER IF EXISTS ediciones_completa ON catalogo.ediciones;
        CREATE CONSTRAINT TRIGGER ediciones_completa AFTER INSERT OR UPDATE ON catalogo.ediciones
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION catalogo.verificar_edicion();
        DROP TRIGGER IF EXISTS licencias_completa ON catalogo.licencias;
        CREATE CONSTRAINT TRIGGER licencias_completa AFTER INSERT OR UPDATE OR DELETE ON catalogo.licencias
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION catalogo.verificar_edicion();
        DROP TRIGGER IF EXISTS archivos_digitales_completa ON catalogo.archivos_digitales;
        CREATE CONSTRAINT TRIGGER archivos_digitales_completa AFTER INSERT OR UPDATE OR DELETE ON catalogo.archivos_digitales
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION catalogo.verificar_edicion();
        """;

    /// <summary>
    /// Lo que <c>rol_catalogo</c> puede hacer: leer e insertar y actualizar <b>solo las columnas que cambian</b> (el identificador, la obra, el tipo, el ISBN-13 y el ISBN ingresado de una edición no cambian nunca). Sin <c>DELETE</c> en
    /// obras, ediciones ni ingresos (se conservan, CAT-13) y sin <c>TRUNCATE</c> ni <c>DROP</c>; solo <c>archivos_pendientes</c> se borra (la depuración parte de sus filas).
    /// </summary>
    public const string Permisos = """
        GRANT USAGE ON SCHEMA catalogo TO rol_catalogo;
        GRANT EXECUTE ON FUNCTION catalogo.normalizar(text) TO rol_catalogo;
        GRANT SELECT, INSERT ON catalogo.obras TO rol_catalogo;
        GRANT UPDATE (titulo, autor, editorial, categoria, descripcion, portada_clave, portada_huella, portada_tipo, estado, dada_de_baja_en, version) ON catalogo.obras TO rol_catalogo;
        GRANT SELECT, INSERT ON catalogo.ediciones TO rol_catalogo;
        GRANT UPDATE (precio, estado, motivo_de_retiro, retirada_en, dada_de_baja_en) ON catalogo.ediciones TO rol_catalogo;
        GRANT SELECT, INSERT ON catalogo.licencias TO rol_catalogo;
        GRANT UPDATE (tipo, titular, territorios, vigente_hasta, formato) ON catalogo.licencias TO rol_catalogo;
        GRANT SELECT, INSERT ON catalogo.archivos_digitales TO rol_catalogo;
        GRANT UPDATE (clave_en_almacen, tipo_detectado, tamano, huella_sha256, subido_en, subido_por) ON catalogo.archivos_digitales TO rol_catalogo;
        GRANT SELECT, INSERT, DELETE ON catalogo.archivos_pendientes TO rol_catalogo;
        GRANT UPDATE (usuario_id, estado, tipo_detectado, tamano, huella_sha256, vence_en, consumido_en) ON catalogo.archivos_pendientes TO rol_catalogo;
        GRANT SELECT, INSERT ON catalogo.ingresos_solicitados TO rol_catalogo;
        GRANT UPDATE (usuario_id, estado, ingreso_id, referencia, nota, intentos, ultimo_intento_en, resultado_en) ON catalogo.ingresos_solicitados TO rol_catalogo;
        """;
}
