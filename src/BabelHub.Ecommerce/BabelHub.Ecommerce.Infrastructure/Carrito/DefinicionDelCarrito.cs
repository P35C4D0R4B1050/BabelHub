namespace BabelHub.Ecommerce.Infrastructure.Carrito;

/// <summary>
/// Definición de las tablas del esquema <c>carrito</c> (<c>plan.md</c> §3, T24) con el permiso mínimo de su rol <c>rol_carrito</c>; la migración las incorpora tal cual (como <c>DefinicionDelCatalogo</c>): un
/// propietario aparte es dueño de las tablas y la aplicación entra con <c>EjecutarComo(carrito)</c> y solo hace lo que el módulo necesita. Alinea los nombres con el plan y añade lo que las revisiones de ECO-4c exigieron:
/// <c>pedido_a_liberar</c> (el pedido cuya reserva falta liberar, explícito y nunca deducido del pedido vivo), <c>posicion</c> en las líneas (el orden del agregado), la identidad y la sucursal de cada línea con las tarifas de envío (venta por
/// sucursal, repetible sobre una base ya creada: las líneas anteriores quedan sin sucursal) y la cascada de <c>fusiones_aplicadas</c> al eliminar el carrito de una
/// cuenta (Ley N.° 29733: no queda el hash de la cookie ligado a un carrito borrado).
/// </summary>
public static class DefinicionDelCarrito
{
    public const string Sql = """
        CREATE SCHEMA IF NOT EXISTS carrito;

        -- El carrito de una cuenta (cuenta_id) o de un visitante (visitante_hash, el SHA-256 de su cookie): exactamente uno de los dos. Solo el de visitante vence (la última acción de la persona más 30 días).
        CREATE TABLE IF NOT EXISTS carrito.carritos (
            id uuid PRIMARY KEY,
            cuenta_id uuid,
            visitante_hash bytea,
            estado text NOT NULL CHECK (estado IN ('abierto', 'fusionado')),
            version integer NOT NULL CHECK (version >= 1),
            creado_en timestamptz NOT NULL,
            modificado_en timestamptz NOT NULL,
            vence_en timestamptz,
            CONSTRAINT carritos_un_propietario CHECK ((cuenta_id IS NOT NULL) <> (visitante_hash IS NOT NULL)),
            CONSTRAINT carritos_huella_de_32 CHECK (visitante_hash IS NULL OR octet_length(visitante_hash) = 32),
            CONSTRAINT carritos_vence_solo_visitante CHECK ((visitante_hash IS NOT NULL) = (vence_en IS NOT NULL)),
            CONSTRAINT carritos_fusionado_solo_visitante CHECK (estado = 'abierto' OR visitante_hash IS NOT NULL)
        );
        CREATE UNIQUE INDEX IF NOT EXISTS carritos_cuenta_unica ON carrito.carritos (cuenta_id) WHERE cuenta_id IS NOT NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS carritos_visitante_unico ON carrito.carritos (visitante_hash) WHERE visitante_hash IS NOT NULL;
        CREATE INDEX IF NOT EXISTS carritos_por_vencimiento ON carrito.carritos (vence_en) WHERE vence_en IS NOT NULL;
        CREATE INDEX IF NOT EXISTS carritos_de_cuenta_por_movimiento ON carrito.carritos (modificado_en) WHERE cuenta_id IS NOT NULL;

        -- Una línea es un libro impreso en una sucursal (puede haber varias del mismo libro y sucursal) o una digital. Un impreso sin sucursal es anterior a la venta por sucursal.
        CREATE TABLE IF NOT EXISTS carrito.lineas_de_carrito (
            carrito_id uuid NOT NULL REFERENCES carrito.carritos (id) ON DELETE CASCADE,
            linea_id uuid NOT NULL,
            edicion_id uuid NOT NULL,
            codigo_de_sucursal text,
            posicion integer NOT NULL CHECK (posicion >= 0),
            cantidad integer NOT NULL CHECK (cantidad BETWEEN 1 AND 20),
            es_digital boolean NOT NULL,
            agregada_en timestamptz NOT NULL,
            retiro_pendiente boolean NOT NULL DEFAULT false,
            PRIMARY KEY (carrito_id, linea_id),
            CONSTRAINT lineas_de_carrito_digital_una_unidad CHECK (NOT es_digital OR cantidad = 1)
        );
        -- Sobre una base anterior (una línea por edición): cada línea recibe su identidad y queda sin sucursal; no se asigna ninguna. Repetible.
        ALTER TABLE carrito.lineas_de_carrito ADD COLUMN IF NOT EXISTS linea_id uuid;
        ALTER TABLE carrito.lineas_de_carrito ADD COLUMN IF NOT EXISTS codigo_de_sucursal text;
        UPDATE carrito.lineas_de_carrito SET linea_id = gen_random_uuid() WHERE linea_id IS NULL;
        ALTER TABLE carrito.lineas_de_carrito ALTER COLUMN linea_id SET NOT NULL;
        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM pg_index i JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY (i.indkey)
                WHERE i.indrelid = 'carrito.lineas_de_carrito'::regclass AND i.indisprimary AND a.attname = 'linea_id') THEN
                ALTER TABLE carrito.lineas_de_carrito DROP CONSTRAINT IF EXISTS lineas_de_carrito_pkey;
                ALTER TABLE carrito.lineas_de_carrito ADD CONSTRAINT lineas_de_carrito_pkey PRIMARY KEY (carrito_id, linea_id);
            END IF;
        END
        $$;
        ALTER TABLE carrito.lineas_de_carrito DROP CONSTRAINT IF EXISTS lineas_de_carrito_sucursal;
        ALTER TABLE carrito.lineas_de_carrito ADD CONSTRAINT lineas_de_carrito_sucursal
            CHECK (codigo_de_sucursal IS NULL OR (NOT es_digital AND char_length(codigo_de_sucursal) BETWEEN 1 AND 40));
        CREATE INDEX IF NOT EXISTS lineas_de_carrito_por_edicion ON carrito.lineas_de_carrito (edicion_id);
        CREATE UNIQUE INDEX IF NOT EXISTS lineas_de_carrito_digital_unica ON carrito.lineas_de_carrito (carrito_id, edicion_id) WHERE es_digital;

        -- Un carrito fusionado no admite líneas.
        CREATE OR REPLACE FUNCTION carrito.rechazar_linea_en_carrito_fusionado() RETURNS trigger
            LANGUAGE plpgsql SET search_path = pg_catalog, carrito
            AS $$
        BEGIN
            IF EXISTS (SELECT 1 FROM carrito.carritos WHERE id = NEW.carrito_id AND estado = 'fusionado') THEN
                RAISE EXCEPTION 'Un carrito fusionado no admite líneas.' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END
        $$;
        DROP TRIGGER IF EXISTS lineas_de_carrito_no_fusionado ON carrito.lineas_de_carrito;
        CREATE TRIGGER lineas_de_carrito_no_fusionado BEFORE INSERT ON carrito.lineas_de_carrito
            FOR EACH ROW EXECUTE FUNCTION carrito.rechazar_linea_en_carrito_fusionado();

        -- Avisos persistentes (edición retirada, línea ajustada por la fusión): sin datos personales; se purgan 30 días después de vistos o a los 90 días.
        CREATE TABLE IF NOT EXISTS carrito.avisos_de_carrito (
            id uuid PRIMARY KEY,
            cuenta_id uuid NOT NULL,
            tipo text NOT NULL CHECK (tipo IN ('edicion_retirada', 'fusion_ajustada')),
            edicion_id uuid NOT NULL,
            titulo text NOT NULL CHECK (char_length(titulo) BETWEEN 1 AND 200),
            detalle text CHECK (detalle IN ('sin_stock', 'ya_poseida', 'no_vendible', 'recortada_al_maximo', 'digital_a_una_unidad', 'tope_de_lineas', 'tope_de_unidades')),
            cantidad_final integer CONSTRAINT avisos_de_carrito_cantidad_final_check CHECK (cantidad_final BETWEEN 0 AND 600),
            creado_en timestamptz NOT NULL,
            visto_en timestamptz,
            CONSTRAINT avisos_de_carrito_ajuste_con_detalle CHECK ((tipo = 'fusion_ajustada') = (detalle IS NOT NULL))
        );
        -- Las líneas de un libro en una sucursal pueden sumar más de 20 (varias líneas): el aviso lleva esa suma.
        ALTER TABLE carrito.avisos_de_carrito DROP CONSTRAINT IF EXISTS avisos_de_carrito_cantidad_final_check;
        ALTER TABLE carrito.avisos_de_carrito ADD CONSTRAINT avisos_de_carrito_cantidad_final_check CHECK (cantidad_final BETWEEN 0 AND 600);
        CREATE INDEX IF NOT EXISTS avisos_de_carrito_por_cuenta ON carrito.avisos_de_carrito (cuenta_id, creado_en DESC);
        CREATE INDEX IF NOT EXISTS avisos_de_carrito_por_vencer ON carrito.avisos_de_carrito (creado_en);

        -- La compra en curso de una cuenta. El CHECK del contacto: solo existe completo, con el checkout vivo y a domicilio (CAR-34). El estado vivo a lo más una vez por cuenta lo garantiza el índice único parcial.
        CREATE TABLE IF NOT EXISTS carrito.checkouts (
            id uuid PRIMARY KEY,
            cuenta_id uuid NOT NULL,
            carrito_id uuid NOT NULL REFERENCES carrito.carritos (id),
            estado text NOT NULL CHECK (estado IN ('en_edicion', 'reservando', 'en_pago', 'pago_pendiente', 'pagado', 'cancelado', 'reiniciado')),
            paso smallint NOT NULL CHECK (paso BETWEEN 1 AND 3),
            version integer NOT NULL CHECK (version >= 1),
            tipo_de_entrega text CHECK (tipo_de_entrega IN ('recojo', 'domicilio', 'digital')),
            codigo_de_sucursal_de_recojo text CHECK (char_length(codigo_de_sucursal_de_recojo) BETWEEN 1 AND 40),
            region_de_entrega text CHECK (char_length(region_de_entrega) BETWEEN 1 AND 60),
            direccion text CHECK (char_length(direccion) BETWEEN 1 AND 200),
            contacto_nombre text CHECK (char_length(contacto_nombre) BETWEEN 1 AND 80),
            contacto_telefono text CHECK (contacto_telefono ~ '^[0-9]{9}$'),
            envio_congelado numeric(12, 2) CHECK (envio_congelado >= 0),
            total_congelado numeric(12, 2) CHECK (total_congelado >= 0),
            envios_congelados jsonb,
            pedido_id uuid,
            reservando_desde timestamptz,
            huella_de_reserva text CHECK (huella_de_reserva ~ '^[0-9a-f]{64}$'),
            generacion_de_reserva integer NOT NULL DEFAULT 0 CHECK (generacion_de_reserva >= 0),
            huella_de_instantanea text CHECK (char_length(huella_de_instantanea) BETWEEN 1 AND 100),
            reserva_expira_en timestamptz,
            pedido_descartado uuid,
            reserva_a_liberar_desde timestamptz,
            pedido_a_liberar uuid,
            limite_pendiente_en timestamptz,
            pago_pendiente_desde timestamptz,
            terminos_version text CHECK (char_length(terminos_version) BETWEEN 1 AND 40),
            terminos_aceptado_en timestamptz,
            privacidad_version text CHECK (char_length(privacidad_version) BETWEEN 1 AND 40),
            privacidad_aceptado_en timestamptz,
            reembolso_digital_version text CHECK (char_length(reembolso_digital_version) BETWEEN 1 AND 40),
            reembolso_digital_aceptado_en timestamptz,
            creado_en timestamptz NOT NULL,
            modificado_en timestamptz NOT NULL,
            CONSTRAINT checkouts_contacto_completo CHECK ((direccion IS NULL) = (contacto_nombre IS NULL) AND (contacto_nombre IS NULL) = (contacto_telefono IS NULL)),
            CONSTRAINT checkouts_contacto_solo_vivo_a_domicilio CHECK (
                contacto_nombre IS NULL
                OR (estado IN ('en_edicion', 'reservando', 'en_pago', 'pago_pendiente') AND tipo_de_entrega = 'domicilio' AND region_de_entrega IS NOT NULL)),
            CONSTRAINT checkouts_liberacion_completa CHECK ((reserva_a_liberar_desde IS NULL) = (pedido_a_liberar IS NULL)),
            CONSTRAINT checkouts_aceptacion_con_fecha_terminos CHECK ((terminos_version IS NULL) = (terminos_aceptado_en IS NULL)),
            CONSTRAINT checkouts_aceptacion_con_fecha_privacidad CHECK ((privacidad_version IS NULL) = (privacidad_aceptado_en IS NULL)),
            CONSTRAINT checkouts_aceptacion_con_fecha_reembolso CHECK ((reembolso_digital_version IS NULL) = (reembolso_digital_aceptado_en IS NULL))
        );
        -- Sobre una base anterior: lo que cobró cada sucursal se guarda junto a la suma (nulo en lo anterior). Repetible.
        ALTER TABLE carrito.checkouts ADD COLUMN IF NOT EXISTS envios_congelados jsonb;
        CREATE UNIQUE INDEX IF NOT EXISTS checkouts_uno_vivo_por_cuenta ON carrito.checkouts (cuenta_id) WHERE estado IN ('en_edicion', 'reservando', 'en_pago', 'pago_pendiente');
        CREATE UNIQUE INDEX IF NOT EXISTS checkouts_pedido_unico ON carrito.checkouts (pedido_id) WHERE pedido_id IS NOT NULL;
        CREATE INDEX IF NOT EXISTS checkouts_por_cuenta ON carrito.checkouts (cuenta_id);
        CREATE INDEX IF NOT EXISTS checkouts_por_carrito ON carrito.checkouts (carrito_id);
        CREATE INDEX IF NOT EXISTS checkouts_por_movimiento ON carrito.checkouts (estado, modificado_en);
        CREATE INDEX IF NOT EXISTS checkouts_por_pedido_descartado ON carrito.checkouts (pedido_descartado) WHERE pedido_descartado IS NOT NULL;
        CREATE INDEX IF NOT EXISTS checkouts_por_liberacion ON carrito.checkouts (reserva_a_liberar_desde) WHERE reserva_a_liberar_desde IS NOT NULL;
        CREATE INDEX IF NOT EXISTS checkouts_por_pedido_a_liberar ON carrito.checkouts (pedido_a_liberar) WHERE pedido_a_liberar IS NOT NULL;
        CREATE INDEX IF NOT EXISTS checkouts_reservando ON carrito.checkouts (reservando_desde) WHERE estado = 'reservando';
        CREATE INDEX IF NOT EXISTS checkouts_pago_pendiente ON carrito.checkouts (pago_pendiente_desde) WHERE estado = 'pago_pendiente';

        -- Las líneas se reconstruyen al volver a en_edicion conservando el linea_id de las líneas del carrito que siguen. Sin datos personales.
        CREATE TABLE IF NOT EXISTS carrito.lineas_del_checkout (
            checkout_id uuid NOT NULL REFERENCES carrito.checkouts (id) ON DELETE CASCADE,
            edicion_id uuid NOT NULL,
            posicion integer NOT NULL CHECK (posicion >= 0),
            linea_id uuid NOT NULL,
            linea_de_carrito_id uuid,
            codigo_de_sucursal text,
            es_digital boolean NOT NULL,
            titulo text NOT NULL CHECK (char_length(titulo) BETWEEN 1 AND 200),
            isbn text NOT NULL CHECK (char_length(isbn) BETWEEN 1 AND 40),
            cantidad integer NOT NULL CHECK (cantidad BETWEEN 1 AND 20),
            precio_unitario numeric(12, 2) NOT NULL CHECK (precio_unitario >= 0),
            codigo_de_fuente_informado text CHECK (char_length(codigo_de_fuente_informado) BETWEEN 1 AND 40),
            requiere_traslado_informado boolean,
            plazo_informado_dias integer CHECK (plazo_informado_dias >= 0),
            consultado_en timestamptz,
            PRIMARY KEY (checkout_id, linea_id),
            CONSTRAINT lineas_del_checkout_linea_unica UNIQUE (linea_id),
            CONSTRAINT lineas_del_checkout_digital_una_unidad CHECK (NOT es_digital OR cantidad = 1)
        );
        -- Sobre una base anterior: sin línea de carrito ni sucursal (una compra anterior se completa como se inició). Repetible.
        ALTER TABLE carrito.lineas_del_checkout ADD COLUMN IF NOT EXISTS linea_de_carrito_id uuid;
        ALTER TABLE carrito.lineas_del_checkout ADD COLUMN IF NOT EXISTS codigo_de_sucursal text;
        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM pg_index i JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY (i.indkey)
                WHERE i.indrelid = 'carrito.lineas_del_checkout'::regclass AND i.indisprimary AND a.attname = 'linea_id') THEN
                ALTER TABLE carrito.lineas_del_checkout DROP CONSTRAINT IF EXISTS lineas_del_checkout_pkey;
                ALTER TABLE carrito.lineas_del_checkout ADD CONSTRAINT lineas_del_checkout_pkey PRIMARY KEY (checkout_id, linea_id);
            END IF;
        END
        $$;
        ALTER TABLE carrito.lineas_del_checkout DROP CONSTRAINT IF EXISTS lineas_del_checkout_sucursal;
        ALTER TABLE carrito.lineas_del_checkout ADD CONSTRAINT lineas_del_checkout_sucursal
            CHECK (codigo_de_sucursal IS NULL OR (NOT es_digital AND char_length(codigo_de_sucursal) BETWEEN 1 AND 40));
        CREATE UNIQUE INDEX IF NOT EXISTS lineas_del_checkout_digital_unica ON carrito.lineas_del_checkout (checkout_id, edicion_id) WHERE es_digital;

        -- El costo de envío propio de cada sucursal (CAR-46, CAR-47); sin fila rige el predeterminado. Quién lo cambió queda solo en la auditoría.
        CREATE TABLE IF NOT EXISTS carrito.tarifas_de_envio (
            codigo_de_sucursal text PRIMARY KEY CHECK (char_length(codigo_de_sucursal) BETWEEN 1 AND 40),
            importe numeric(8, 2) NOT NULL CHECK (importe BETWEEN 0 AND 500),
            actualizada_en timestamptz NOT NULL
        );

        -- La constancia de una fusión aplicada: hace idempotente la fusión desde un segundo dispositivo. Se borra con el carrito de la cuenta (cascada) y se purga a los 30 días.
        CREATE TABLE IF NOT EXISTS carrito.fusiones_aplicadas (
            visitante_hash bytea PRIMARY KEY CHECK (octet_length(visitante_hash) = 32),
            carrito_de_cuenta_id uuid NOT NULL REFERENCES carrito.carritos (id) ON DELETE CASCADE,
            aplicada_en timestamptz NOT NULL
        );
        CREATE INDEX IF NOT EXISTS fusiones_aplicadas_por_fecha ON carrito.fusiones_aplicadas (aplicada_en);
        """;

    /// <summary>
    /// Lo que <c>rol_carrito</c> puede hacer: leer, insertar y actualizar <b>solo las columnas que cambian</b> (el identificador, la cuenta, el visitante y la fecha de creación nunca cambian) y borrar solo donde la retención o la eliminación de
    /// una cuenta lo exigen. <c>SELECT … FOR UPDATE</c> necesita el privilegio <c>UPDATE</c> sobre la tabla. Sin <c>TRUNCATE</c> ni <c>DROP</c>.
    /// </summary>
    public const string Permisos = """
        GRANT USAGE ON SCHEMA carrito TO rol_carrito;
        GRANT SELECT, INSERT, DELETE ON carrito.carritos TO rol_carrito;
        GRANT UPDATE (estado, version, modificado_en, vence_en) ON carrito.carritos TO rol_carrito;
        GRANT SELECT, INSERT, DELETE ON carrito.lineas_de_carrito TO rol_carrito;
        GRANT UPDATE (posicion, cantidad, es_digital, agregada_en, retiro_pendiente) ON carrito.lineas_de_carrito TO rol_carrito;
        GRANT EXECUTE ON FUNCTION carrito.rechazar_linea_en_carrito_fusionado() TO rol_carrito;
        GRANT SELECT, INSERT, DELETE ON carrito.avisos_de_carrito TO rol_carrito;
        GRANT UPDATE (visto_en) ON carrito.avisos_de_carrito TO rol_carrito;
        GRANT SELECT, INSERT, DELETE ON carrito.checkouts TO rol_carrito;
        GRANT UPDATE (
            estado, paso, version, tipo_de_entrega, codigo_de_sucursal_de_recojo, region_de_entrega, direccion, contacto_nombre, contacto_telefono, envio_congelado, total_congelado, envios_congelados, pedido_id, reservando_desde,
            huella_de_reserva, generacion_de_reserva, huella_de_instantanea, reserva_expira_en, pedido_descartado, reserva_a_liberar_desde, pedido_a_liberar, limite_pendiente_en, pago_pendiente_desde,
            terminos_version, terminos_aceptado_en, privacidad_version, privacidad_aceptado_en, reembolso_digital_version, reembolso_digital_aceptado_en, modificado_en
        ) ON carrito.checkouts TO rol_carrito;
        GRANT SELECT, INSERT, DELETE ON carrito.lineas_del_checkout TO rol_carrito;
        GRANT SELECT, INSERT, DELETE ON carrito.fusiones_aplicadas TO rol_carrito;
        GRANT SELECT, INSERT ON carrito.tarifas_de_envio TO rol_carrito;
        GRANT UPDATE (importe, actualizada_en) ON carrito.tarifas_de_envio TO rol_carrito;
        """;
}
