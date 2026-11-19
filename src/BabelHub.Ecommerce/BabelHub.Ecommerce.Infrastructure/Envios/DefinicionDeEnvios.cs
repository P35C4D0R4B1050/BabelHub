namespace BabelHub.Ecommerce.Infrastructure.Envios;

/// <summary>
/// Definición de las tablas del esquema <c>envios</c> (<c>plan.md</c> §3, T12) con el permiso mínimo de su rol <c>rol_envios</c>; la migración las incorpora tal cual (como <c>DefinicionDelCarrito</c>): un propietario
/// aparte es dueño de las tablas y la aplicación entra con <c>EjecutarComo(envios)</c> y solo hace lo que el módulo necesita. <b>Ninguna tabla tiene columnas de nombre, dirección, teléfono ni correo</b> (ENV-15):
/// el contacto viaja de Pedidos al operador y no se guarda. Los <c>CHECK</c> repiten la coherencia entre el estado, la guía y las fechas que valida <c>Envio.Rehidratar</c>.
/// </summary>
public static class DefinicionDeEnvios
{
    public const string Sql = """
        CREATE SCHEMA IF NOT EXISTS envios;

        CREATE TABLE IF NOT EXISTS envios.envios (
            id uuid PRIMARY KEY,
            pedido_id uuid NOT NULL,
            codigo_de_fuente text NOT NULL CHECK (char_length(codigo_de_fuente) BETWEEN 1 AND 40),
            estado text NOT NULL CHECK (estado IN ('pendiente_de_guia', 'guia_generada', 'en_reparto', 'entregado', 'cancelado')),
            numero_de_guia text CHECK (char_length(numero_de_guia) BETWEEN 6 AND 40 AND numero_de_guia ~ '^[A-Za-z0-9][A-Za-z0-9-]*[A-Za-z0-9]$'),
            operador text CHECK (char_length(operador) BETWEEN 1 AND 32),
            clave_de_solicitud uuid,
            guia_en_curso_hasta timestamptz,
            despacho_iniciado_en timestamptz,
            despacho_en_curso_hasta timestamptz,
            despacho_incierto_en timestamptz,
            creado_en timestamptz NOT NULL,
            guia_generada_en timestamptz,
            en_reparto_en timestamptz,
            entregado_en timestamptz,
            cancelado_en timestamptz,
            progreso_en_reparto_informado_en timestamptz,
            progreso_entregado_informado_en timestamptz,
            version integer NOT NULL CHECK (version >= 1),
            CONSTRAINT envios_clave_de_solicitud_unica UNIQUE (clave_de_solicitud),
            CONSTRAINT envios_guia_y_operador_juntos CHECK ((numero_de_guia IS NULL) = (operador IS NULL)),
            CONSTRAINT envios_guia_y_fecha_juntas CHECK ((numero_de_guia IS NULL) = (guia_generada_en IS NULL)),
            CONSTRAINT envios_guia_desde_guia_generada CHECK (estado NOT IN ('guia_generada', 'en_reparto', 'entregado') OR numero_de_guia IS NOT NULL),
            CONSTRAINT envios_pendiente_sin_guia CHECK (estado <> 'pendiente_de_guia' OR (numero_de_guia IS NULL AND despacho_iniciado_en IS NULL)),
            CONSTRAINT envios_fecha_de_reparto CHECK ((estado IN ('en_reparto', 'entregado')) = (en_reparto_en IS NOT NULL)),
            CONSTRAINT envios_fecha_de_entrega CHECK ((estado = 'entregado') = (entregado_en IS NOT NULL)),
            CONSTRAINT envios_fecha_de_cancelacion CHECK ((estado = 'cancelado') = (cancelado_en IS NOT NULL)),
            CONSTRAINT envios_despacho_solo_desde_guia CHECK (despacho_iniciado_en IS NULL OR estado IN ('guia_generada', 'en_reparto', 'entregado')),
            CONSTRAINT envios_incierto_con_marcador CHECK (despacho_incierto_en IS NULL OR despacho_iniciado_en IS NOT NULL OR estado = 'cancelado'),
            CONSTRAINT envios_arrendamiento_de_guia_solo_pendiente CHECK (guia_en_curso_hasta IS NULL OR estado = 'pendiente_de_guia')
        );
        -- Un número de guía pertenece a un solo envío de un operador: el operador que repite el de otro envío no puede hacer que se vea la guía de un pedido ajeno (ENV-04).
        CREATE UNIQUE INDEX IF NOT EXISTS envios_guia_unica ON envios.envios (operador, numero_de_guia) WHERE numero_de_guia IS NOT NULL;
        CREATE INDEX IF NOT EXISTS envios_por_pedido ON envios.envios (pedido_id);
        CREATE INDEX IF NOT EXISTS envios_por_estado ON envios.envios (estado, creado_en);

        -- Una línea pertenece a un solo envío: lo que hace idempotente IDespachoDeEnvios.Crear (ENV-01). Sin cantidades ni precios: los lee Pedidos.
        CREATE TABLE IF NOT EXISTS envios.envio_lineas (
            linea_id uuid PRIMARY KEY,
            envio_id uuid NOT NULL REFERENCES envios.envios (id),
            pedido_id uuid NOT NULL,
            posicion integer NOT NULL CHECK (posicion >= 0)
        );
        CREATE INDEX IF NOT EXISTS envio_lineas_por_envio ON envios.envio_lineas (envio_id, posicion);

        -- El número de guía y la clave de solicitud no cambian una vez fijados (ENV-02, ENV-18); un envío nunca sale de un estado terminal ni vuelve a pendiente.
        CREATE OR REPLACE FUNCTION envios.proteger_la_guia_y_la_clave() RETURNS trigger
            LANGUAGE plpgsql SET search_path = pg_catalog, envios
            AS $$
        BEGIN
            IF OLD.numero_de_guia IS NOT NULL AND NEW.numero_de_guia IS DISTINCT FROM OLD.numero_de_guia THEN
                RAISE EXCEPTION 'El número de guía no cambia.' USING ERRCODE = '23514';
            END IF;
            IF OLD.operador IS NOT NULL AND NEW.operador IS DISTINCT FROM OLD.operador THEN
                RAISE EXCEPTION 'El operador de la guía no cambia.' USING ERRCODE = '23514';
            END IF;
            IF OLD.clave_de_solicitud IS NOT NULL AND NEW.clave_de_solicitud IS DISTINCT FROM OLD.clave_de_solicitud THEN
                RAISE EXCEPTION 'La clave de solicitud no cambia.' USING ERRCODE = '23514';
            END IF;
            IF OLD.estado IN ('entregado', 'cancelado') AND NEW.estado <> OLD.estado THEN
                RAISE EXCEPTION 'Un envío entregado o cancelado no cambia de estado.' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END
        $$;
        DROP TRIGGER IF EXISTS envios_proteger_la_guia ON envios.envios;
        CREATE TRIGGER envios_proteger_la_guia BEFORE UPDATE ON envios.envios
            FOR EACH ROW EXECUTE FUNCTION envios.proteger_la_guia_y_la_clave();
        """;

    /// <summary>
    /// Lo que <c>rol_envios</c> puede hacer: leer e insertar; actualizar <b>solo las columnas que cambian</b> (el identificador, el pedido, la fuente y la fecha de creación nunca cambian); sin <c>DELETE</c>,
    /// <c>TRUNCATE</c> ni <c>DROP</c>. <c>SELECT … FOR UPDATE</c> necesita el privilegio <c>UPDATE</c> sobre la tabla.
    /// </summary>
    public const string Permisos = """
        GRANT USAGE ON SCHEMA envios TO rol_envios;
        GRANT SELECT, INSERT ON envios.envios TO rol_envios;
        GRANT UPDATE (
            estado, numero_de_guia, operador, clave_de_solicitud, guia_en_curso_hasta, despacho_iniciado_en, despacho_en_curso_hasta, despacho_incierto_en, guia_generada_en, en_reparto_en, entregado_en,
            cancelado_en, progreso_en_reparto_informado_en, progreso_entregado_informado_en, version
        ) ON envios.envios TO rol_envios;
        GRANT SELECT, INSERT ON envios.envio_lineas TO rol_envios;
        GRANT EXECUTE ON FUNCTION envios.proteger_la_guia_y_la_clave() TO rol_envios;
        """;
}
