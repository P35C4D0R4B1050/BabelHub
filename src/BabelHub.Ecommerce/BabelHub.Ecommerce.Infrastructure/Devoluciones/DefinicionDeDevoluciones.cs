namespace BabelHub.Ecommerce.Infrastructure.Devoluciones;

/// <summary>
/// Definición de las tablas del esquema <c>devoluciones</c> (<c>plan.md</c> §3, T19) con el permiso mínimo de su rol <c>rol_devoluciones</c>; la migración las incorpora tal cual (como <c>DefinicionDeEnvios</c>): un propietario aparte es dueño de las tablas y
/// la aplicación entra con <c>EjecutarComo(devoluciones)</c> y solo hace lo que el módulo necesita. <b>Ninguna tabla guarda la cuenta</b> (D01): lo de una cuenta se localiza por el pedido; la cuenta solo aparece como responsable de un cambio, y ese identificador
/// se quita al cancelar la cuenta y a los dos años. El motivo y el texto son los únicos datos libres: columnas redactables con un valor fijo. Los <c>CHECK</c> y los disparadores repiten las invariantes del Dominio (la máquina de estados de la línea, el marcador de reingreso
/// que solo se limpia sin intentos inciertos, la redacción que solo reemplaza por el valor fijo) para que una escritura que las rompa falle aunque el código se equivoque.
/// </summary>
public static class DefinicionDeDevoluciones
{
    public const string Sql = """
        CREATE SCHEMA IF NOT EXISTS devoluciones;

        CREATE TABLE IF NOT EXISTS devoluciones.devoluciones (
            id uuid PRIMARY KEY,
            pedido_id uuid NOT NULL,
            tipo text NOT NULL CHECK (tipo IN ('impreso', 'digital')),
            origen text NOT NULL CHECK (origen IN ('solicitud', 'reporte')),
            codigo_de_sucursal_receptora text CHECK (char_length(codigo_de_sucursal_receptora) BETWEEN 1 AND 40),
            motivo text NOT NULL CHECK (char_length(motivo) BETWEEN 1 AND 300),
            texto text CHECK (char_length(texto) BETWEEN 1 AND 2000),
            creada_en timestamptz NOT NULL,
            redactada_en timestamptz,
            reingreso_iniciado_en timestamptz,
            reingreso_incierto_en timestamptz,
            reingreso_confirmado_en timestamptz,
            CONSTRAINT devoluciones_sucursal_solo_impresos CHECK ((tipo = 'impreso') = (codigo_de_sucursal_receptora IS NOT NULL)),
            CONSTRAINT devoluciones_texto_solo_reportes CHECK ((origen = 'reporte') = (texto IS NOT NULL)),
            CONSTRAINT devoluciones_reporte_es_digital CHECK (origen = 'solicitud' OR tipo = 'digital'),
            CONSTRAINT devoluciones_reingreso_solo_impresos CHECK (tipo = 'impreso' OR (reingreso_iniciado_en IS NULL AND reingreso_incierto_en IS NULL AND reingreso_confirmado_en IS NULL)),
            CONSTRAINT devoluciones_reingreso_con_marcador CHECK ((reingreso_incierto_en IS NULL AND reingreso_confirmado_en IS NULL) OR reingreso_iniciado_en IS NOT NULL),
            CONSTRAINT devoluciones_redactada_con_valor_fijo CHECK (redactada_en IS NULL OR (motivo = '[redactado]' AND (texto IS NULL OR texto = '[redactado]')))
        );
        CREATE INDEX IF NOT EXISTS devoluciones_por_pedido ON devoluciones.devoluciones (pedido_id);
        CREATE INDEX IF NOT EXISTS devoluciones_por_sucursal ON devoluciones.devoluciones (codigo_de_sucursal_receptora, creada_en);
        CREATE INDEX IF NOT EXISTS devoluciones_por_fecha ON devoluciones.devoluciones (creada_en);

        -- Una línea del pedido se devuelve a lo más una vez por devolución (el único) y su estado nunca retrocede (el disparador); sin cuenta.
        CREATE TABLE IF NOT EXISTS devoluciones.lineas_de_devolucion (
            id uuid PRIMARY KEY,
            devolucion_id uuid NOT NULL REFERENCES devoluciones.devoluciones (id),
            linea_id uuid NOT NULL,
            edicion_id uuid NOT NULL,
            cantidad integer NOT NULL CHECK (cantidad > 0),
            precio_unitario numeric(12, 2) NOT NULL CHECK (precio_unitario >= 0),
            monto numeric(12, 2) NOT NULL CHECK (monto >= 0),
            estado text NOT NULL CHECK (estado IN ('solicitada', 'aprobada', 'rechazada', 'recibida', 'reembolsada')),
            estado_en timestamptz NOT NULL,
            aprobada_en timestamptz,
            acceso_id uuid,
            reembolso_registrado_en timestamptz,
            reembolsada_en timestamptz,
            CONSTRAINT lineas_de_devolucion_unica UNIQUE (devolucion_id, linea_id),
            CONSTRAINT lineas_aprobada_con_fecha CHECK (estado NOT IN ('aprobada', 'recibida', 'reembolsada') OR aprobada_en IS NOT NULL),
            CONSTRAINT lineas_fecha_de_reembolso CHECK ((estado = 'reembolsada') = (reembolsada_en IS NOT NULL)),
            CONSTRAINT lineas_reembolsada_con_reembolso_registrado CHECK (estado <> 'reembolsada' OR reembolso_registrado_en IS NOT NULL)
        );
        CREATE INDEX IF NOT EXISTS lineas_por_devolucion ON devoluciones.lineas_de_devolucion (devolucion_id);
        CREATE INDEX IF NOT EXISTS lineas_por_estado ON devoluciones.lineas_de_devolucion (estado, estado_en);

        -- El historial de cada línea: una fila por cambio, con su fecha y su responsable. Solo se añade; el identificador del responsable solo se quita, y solo el del cliente.
        CREATE TABLE IF NOT EXISTS devoluciones.cambios_de_linea (
            linea_de_devolucion_id uuid NOT NULL REFERENCES devoluciones.lineas_de_devolucion (id),
            orden integer NOT NULL CHECK (orden >= 0),
            estado text NOT NULL CHECK (estado IN ('solicitada', 'aprobada', 'rechazada', 'recibida', 'reembolsada')),
            cambiado_en timestamptz NOT NULL,
            responsable_tipo text NOT NULL CHECK (responsable_tipo IN ('cliente', 'personal', 'sistema')),
            responsable_id uuid,
            PRIMARY KEY (linea_de_devolucion_id, orden),
            CONSTRAINT cambios_sin_id_para_el_sistema CHECK (responsable_tipo <> 'sistema' OR responsable_id IS NULL),
            CONSTRAINT cambios_personal_con_id CHECK (responsable_tipo <> 'personal' OR responsable_id IS NOT NULL)
        );

        -- El tope de unidades por línea del pedido (DEV-03): la atomicidad es del UPDATE condicional, y el CHECK impide pasar de lo comprado aunque el código se equivoque.
        CREATE TABLE IF NOT EXISTS devoluciones.cupos_de_linea (
            linea_id uuid PRIMARY KEY,
            pedido_id uuid NOT NULL,
            cantidad_comprada integer NOT NULL CHECK (cantidad_comprada >= 1),
            cantidad_comprometida integer NOT NULL DEFAULT 0 CHECK (cantidad_comprometida >= 0),
            CONSTRAINT cupos_sin_pasar_de_lo_comprado CHECK (cantidad_comprometida <= cantidad_comprada)
        );

        CREATE OR REPLACE FUNCTION devoluciones.proteger_la_devolucion() RETURNS trigger
            LANGUAGE plpgsql SET search_path = pg_catalog, devoluciones
            AS $$
        BEGIN
            IF OLD.redactada_en IS NOT NULL AND NEW.redactada_en IS DISTINCT FROM OLD.redactada_en THEN
                RAISE EXCEPTION 'La fecha de redacción no cambia.' USING ERRCODE = '23514';
            END IF;
            IF NEW.motivo IS DISTINCT FROM OLD.motivo AND NEW.motivo <> '[redactado]' THEN
                RAISE EXCEPTION 'El motivo solo se reemplaza por el valor fijo.' USING ERRCODE = '23514';
            END IF;
            IF NEW.texto IS DISTINCT FROM OLD.texto AND (NEW.texto IS NULL OR NEW.texto <> '[redactado]') THEN
                RAISE EXCEPTION 'El texto solo se reemplaza por el valor fijo.' USING ERRCODE = '23514';
            END IF;
            IF OLD.reingreso_incierto_en IS NOT NULL AND NEW.reingreso_incierto_en IS DISTINCT FROM OLD.reingreso_incierto_en THEN
                RAISE EXCEPTION 'El reingreso incierto no se borra.' USING ERRCODE = '23514';
            END IF;
            IF OLD.reingreso_confirmado_en IS NOT NULL AND NEW.reingreso_confirmado_en IS DISTINCT FROM OLD.reingreso_confirmado_en THEN
                RAISE EXCEPTION 'El reingreso confirmado no cambia.' USING ERRCODE = '23514';
            END IF;
            IF OLD.reingreso_iniciado_en IS NOT NULL AND NEW.reingreso_iniciado_en IS DISTINCT FROM OLD.reingreso_iniciado_en
               AND (NEW.reingreso_iniciado_en IS NOT NULL OR OLD.reingreso_incierto_en IS NOT NULL OR OLD.reingreso_confirmado_en IS NOT NULL) THEN
                RAISE EXCEPTION 'El marcador de reingreso solo se limpia sin intentos inciertos ni confirmación.' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END
        $$;
        DROP TRIGGER IF EXISTS devoluciones_proteger ON devoluciones.devoluciones;
        CREATE TRIGGER devoluciones_proteger BEFORE UPDATE ON devoluciones.devoluciones
            FOR EACH ROW EXECUTE FUNCTION devoluciones.proteger_la_devolucion();

        CREATE OR REPLACE FUNCTION devoluciones.proteger_la_linea() RETURNS trigger
            LANGUAGE plpgsql SET search_path = pg_catalog, devoluciones
            AS $$
        BEGIN
            -- Una guardada puede aplicar varias transiciones seguidas (aprobar, recibir y reembolsar): se admite lo alcanzable, nunca un retroceso ni salir de un estado final.
            IF NEW.estado IS DISTINCT FROM OLD.estado AND NOT (
                (OLD.estado = 'solicitada' AND NEW.estado IN ('aprobada', 'rechazada', 'recibida', 'reembolsada'))
                OR (OLD.estado = 'aprobada' AND NEW.estado IN ('recibida', 'reembolsada', 'rechazada'))
                OR (OLD.estado = 'recibida' AND NEW.estado = 'reembolsada')) THEN
                RAISE EXCEPTION 'Una línea no pasa de % a %.', OLD.estado, NEW.estado USING ERRCODE = '23514';
            END IF;
            IF NEW.acceso_id IS DISTINCT FROM OLD.acceso_id AND NEW.acceso_id IS NOT NULL THEN
                RAISE EXCEPTION 'La referencia al acceso solo se quita.' USING ERRCODE = '23514';
            END IF;
            IF OLD.reembolso_registrado_en IS NOT NULL AND NEW.reembolso_registrado_en IS DISTINCT FROM OLD.reembolso_registrado_en THEN
                RAISE EXCEPTION 'La fecha del reembolso registrado no cambia.' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END
        $$;
        DROP TRIGGER IF EXISTS lineas_proteger ON devoluciones.lineas_de_devolucion;
        CREATE TRIGGER lineas_proteger BEFORE UPDATE ON devoluciones.lineas_de_devolucion
            FOR EACH ROW EXECUTE FUNCTION devoluciones.proteger_la_linea();

        CREATE OR REPLACE FUNCTION devoluciones.proteger_el_cambio() RETURNS trigger
            LANGUAGE plpgsql SET search_path = pg_catalog, devoluciones
            AS $$
        BEGIN
            IF NEW.responsable_id IS DISTINCT FROM OLD.responsable_id AND NOT (NEW.responsable_id IS NULL AND OLD.responsable_tipo = 'cliente') THEN
                RAISE EXCEPTION 'Solo se quita el identificador del cliente.' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END
        $$;
        DROP TRIGGER IF EXISTS cambios_proteger ON devoluciones.cambios_de_linea;
        CREATE TRIGGER cambios_proteger BEFORE UPDATE ON devoluciones.cambios_de_linea
            FOR EACH ROW EXECUTE FUNCTION devoluciones.proteger_el_cambio();
        """;

    /// <summary>
    /// Lo que <c>rol_devoluciones</c> puede hacer: leer e insertar; actualizar <b>solo las columnas que cambian</b>; sin <c>DELETE</c>, <c>TRUNCATE</c> ni <c>DROP</c>. <c>SELECT … FOR UPDATE</c> necesita el privilegio <c>UPDATE</c> sobre la tabla.
    /// </summary>
    public const string Permisos = """
        GRANT USAGE ON SCHEMA devoluciones TO rol_devoluciones;
        GRANT SELECT, INSERT ON devoluciones.devoluciones TO rol_devoluciones;
        GRANT UPDATE (motivo, texto, redactada_en, reingreso_iniciado_en, reingreso_incierto_en, reingreso_confirmado_en) ON devoluciones.devoluciones TO rol_devoluciones;
        GRANT SELECT, INSERT ON devoluciones.lineas_de_devolucion TO rol_devoluciones;
        GRANT UPDATE (estado, estado_en, aprobada_en, acceso_id, reembolso_registrado_en, reembolsada_en) ON devoluciones.lineas_de_devolucion TO rol_devoluciones;
        GRANT SELECT, INSERT ON devoluciones.cambios_de_linea TO rol_devoluciones;
        GRANT UPDATE (responsable_id) ON devoluciones.cambios_de_linea TO rol_devoluciones;
        GRANT SELECT, INSERT ON devoluciones.cupos_de_linea TO rol_devoluciones;
        GRANT UPDATE (cantidad_comprometida) ON devoluciones.cupos_de_linea TO rol_devoluciones;
        GRANT EXECUTE ON FUNCTION devoluciones.proteger_la_devolucion() TO rol_devoluciones;
        GRANT EXECUTE ON FUNCTION devoluciones.proteger_la_linea() TO rol_devoluciones;
        GRANT EXECUTE ON FUNCTION devoluciones.proteger_el_cambio() TO rol_devoluciones;
        """;
}
