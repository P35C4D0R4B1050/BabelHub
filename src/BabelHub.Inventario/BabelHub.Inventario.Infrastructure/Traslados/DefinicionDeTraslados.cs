namespace BabelHub.Inventario.Infrastructure.Traslados;

/// <summary>
/// La migración del esquema <c>traslados</c> del Inventario (<c>inventario-traslados</c> §3, T12), hacia adelante y repetible. La corre la cuenta de despliegue; el propietario de las tablas y la contraseña del rol con inicio de
/// sesión los pone el aprovisionamiento aparte, como con <c>fuentes</c>, <c>reservas</c> y <c>ajustes</c>. Define el rol <c>rol_traslados</c> (sin inicio de sesión ni propiedad de las tablas) y las tablas con las restricciones que
/// son la <b>última defensa</b> de las reglas del Dominio:
/// <list type="bullet">
/// <item><c>traslados</c>: origen distinto de destino y cantidad positiva (TRA-20), estados cerrados, <b>un traslado vinculado a un pedido nunca está rechazado</b> (TRA-16), la marca de pedido anulado solo en un vinculado, la estimación
/// de llegada solo en un vinculado, textos de hasta 200 caracteres y guía de hasta 40, fechas coherentes con el estado y <b>único parcial <c>(pedido, edición, origen)</c></b> entre los no cancelados: una confirmación repetida no crea otro
/// traslado (TRA-21). El número es una columna de identidad (<c>TR-0042</c> en las pantallas).</item>
/// <item><c>mensajes_salientes</c> (el buzón de salida): tipo, estado y motivo de fallo cerrados, <b>único <c>(clave de evento, sucursal, tipo)</c></b> con la sucursal obligatoria (se insertan siempre con <c>ON CONFLICT DO NOTHING</c>)
/// y la carga como JSON sin texto libre.</item>
/// </list>
/// <b>Seguridad por filas</b> (<c>ENABLE</c> y <c>FORCE</c>) sobre <c>traslados</c>: el alcance de datos de la transacción es <c>red</c> o la sucursal de origen o de destino; sin valor, deniega (segunda capa del alcance, D09 de
/// <c>inventario-fuentes</c>). <c>rol_traslados</c> lee, inserta y actualiza solo las columnas que cambian; <b>nunca borra</b> un traslado; solo borra del buzón (su purga a los 30 días). Sin claves foráneas hacia <c>fuentes</c> ni hacia
/// los pedidos (regla 13): el pedido es un identificador opaco.
/// </summary>
public static class DefinicionDeTraslados
{
    public const string Esquema = "traslados";

    /// <summary>El rol del esquema que usa <c>EjecutarComo</c>: <c>rol_&lt;esquema&gt;</c>.</summary>
    public const string RolDeLaAplicacion = "rol_traslados";

    /// <summary>El rol sin inicio de sesión del módulo (idempotente).</summary>
    public const string Rol = """
        DO $rol$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rol_traslados') THEN
                CREATE ROLE rol_traslados NOLOGIN;
            END IF;
        END
        $rol$;
        """;

    /// <summary>Esquema, tablas, restricciones, índices y política de filas (idempotente).</summary>
    public const string Sql = """
        CREATE SCHEMA IF NOT EXISTS traslados;

        CREATE TABLE IF NOT EXISTS traslados.traslados (
            id uuid PRIMARY KEY,
            numero bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
            edicion_id uuid NOT NULL,
            origen_id uuid NOT NULL,
            destino_id uuid NOT NULL,
            cantidad integer NOT NULL,
            estado text NOT NULL,
            motivo text NULL,
            motivo_de_cierre text NULL,
            pedido_id uuid NULL,
            pedido_anulado boolean NOT NULL DEFAULT false,
            fecha_estimada date NULL,
            estimada_por_confirmar boolean NOT NULL DEFAULT false,
            guia text NULL,
            solicitado_por uuid NULL,
            solicitado_en timestamptz NOT NULL,
            aprobado_en timestamptz NULL,
            despachado_en timestamptz NULL,
            recibido_en timestamptz NULL,
            cerrado_en timestamptz NULL,
            alerta_de_demora_en timestamptz NULL,
            version bigint NOT NULL DEFAULT 0,
            CONSTRAINT uq_traslados_numero UNIQUE (numero),
            CONSTRAINT ck_traslados_origen_destino CHECK (origen_id <> destino_id),
            CONSTRAINT ck_traslados_cantidad CHECK (cantidad > 0),
            CONSTRAINT ck_traslados_estado CHECK (estado IN ('solicitado', 'aprobado', 'en-transito', 'recibido', 'rechazado', 'cancelado')),
            CONSTRAINT ck_traslados_vinculado_no_rechazado CHECK (pedido_id IS NULL OR estado <> 'rechazado'),
            CONSTRAINT ck_traslados_pedido_anulado CHECK (NOT pedido_anulado OR pedido_id IS NOT NULL),
            CONSTRAINT ck_traslados_estimacion CHECK (
                (fecha_estimada IS NULL OR pedido_id IS NOT NULL)
                AND (NOT estimada_por_confirmar OR (pedido_id IS NOT NULL AND fecha_estimada IS NULL))),
            CONSTRAINT ck_traslados_textos CHECK (
                (motivo IS NULL OR char_length(motivo) <= 200)
                AND (motivo_de_cierre IS NULL OR char_length(motivo_de_cierre) <= 200)
                AND (guia IS NULL OR char_length(guia) <= 40)),
            CONSTRAINT ck_traslados_version CHECK (version >= 0),
            CONSTRAINT ck_traslados_fechas CHECK (
                (estado <> 'en-transito' OR despachado_en IS NOT NULL)
                AND (estado <> 'recibido' OR (despachado_en IS NOT NULL AND recibido_en IS NOT NULL))
                AND ((estado IN ('recibido', 'rechazado', 'cancelado')) = (cerrado_en IS NOT NULL)))
        );

        -- Una confirmación repetida no crea otro traslado del pedido (TRA-21): único por pedido, edición y origen mientras no esté cancelado.
        CREATE UNIQUE INDEX IF NOT EXISTS ux_traslados_pedido_edicion_origen ON traslados.traslados (pedido_id, edicion_id, origen_id) WHERE pedido_id IS NOT NULL AND estado <> 'cancelado';
        CREATE INDEX IF NOT EXISTS ix_traslados_estado ON traslados.traslados (estado);
        CREATE INDEX IF NOT EXISTS ix_traslados_origen ON traslados.traslados (origen_id, estado);
        CREATE INDEX IF NOT EXISTS ix_traslados_destino ON traslados.traslados (destino_id, estado);
        CREATE INDEX IF NOT EXISTS ix_traslados_pedido ON traslados.traslados (pedido_id) WHERE pedido_id IS NOT NULL;
        -- TRA-22: los vinculados solicitados sin aprobar (la revisión de plazos busca los de más de 48 horas).
        CREATE INDEX IF NOT EXISTS ix_traslados_vinculados_sin_aprobar ON traslados.traslados (solicitado_en) WHERE pedido_id IS NOT NULL AND estado = 'solicitado';

        CREATE TABLE IF NOT EXISTS traslados.mensajes_salientes (
            id uuid PRIMARY KEY,
            tipo text NOT NULL,
            clave_de_evento text NOT NULL,
            sucursal_codigo text NOT NULL,
            carga jsonb NOT NULL,
            estado text NOT NULL DEFAULT 'pendiente',
            intentos integer NOT NULL DEFAULT 0,
            proxima_fecha timestamptz NOT NULL,
            arrendado_hasta timestamptz NULL,
            motivo_de_fallo text NULL,
            creado_en timestamptz NOT NULL,
            enviado_en timestamptz NULL,
            CONSTRAINT uq_mensajes_salientes_evento UNIQUE (clave_de_evento, sucursal_codigo, tipo),
            CONSTRAINT ck_mensajes_salientes_tipo CHECK (tipo IN ('aviso', 'estado-de-pedido')),
            CONSTRAINT ck_mensajes_salientes_estado CHECK (estado IN ('pendiente', 'enviado', 'fallido')),
            CONSTRAINT ck_mensajes_salientes_clave CHECK (clave_de_evento ~ '^traslado:[0-9a-f-]{36}:[a-z-]{4,16}$'),
            CONSTRAINT ck_mensajes_salientes_sucursal CHECK (sucursal_codigo ~ '^[a-z0-9-]{2,20}$'),
            CONSTRAINT ck_mensajes_salientes_intentos CHECK (intentos >= 0),
            CONSTRAINT ck_mensajes_salientes_fallo CHECK (motivo_de_fallo IS NULL OR motivo_de_fallo IN ('permanente', 'credencial', 'vencido')),
            CONSTRAINT ck_mensajes_salientes_coherencia CHECK (
                (estado = 'enviado') = (enviado_en IS NOT NULL)
                AND (estado = 'fallido' OR motivo_de_fallo IS NULL))
        );

        -- La entrega toma los pendientes cuya fecha llegó, del más antiguo al más nuevo.
        CREATE INDEX IF NOT EXISTS ix_mensajes_salientes_pendientes ON traslados.mensajes_salientes (proxima_fecha, creado_en) WHERE estado = 'pendiente';
        CREATE INDEX IF NOT EXISTS ix_mensajes_salientes_purga ON traslados.mensajes_salientes (creado_en) WHERE estado <> 'pendiente';

        -- Seguridad por filas (D09 de inventario-fuentes): el alcance de datos de la transacción es «red», o la sucursal de origen o de destino del traslado; sin valor, deniega.
        ALTER TABLE traslados.traslados ENABLE ROW LEVEL SECURITY;
        ALTER TABLE traslados.traslados FORCE ROW LEVEL SECURITY;
        DROP POLICY IF EXISTS por_alcance ON traslados.traslados;
        CREATE POLICY por_alcance ON traslados.traslados FOR ALL
            USING (current_setting('babelhub.alcance', true) = 'red' OR current_setting('babelhub.alcance', true) IN (origen_id::text, destino_id::text))
            WITH CHECK (current_setting('babelhub.alcance', true) = 'red' OR current_setting('babelhub.alcance', true) IN (origen_id::text, destino_id::text));
        """;

    /// <summary>
    /// Los permisos mínimos de <c>rol_traslados</c> (se aplican después de que el aprovisionamiento deja las tablas a su propietario): lee e inserta los traslados y actualiza <b>solo las columnas que cambian</b> (nunca el
    /// origen, el destino, la cantidad, el pedido ni la estimación) y <b>no puede borrar</b> un traslado; el buzón lo lee, inserta y purga y actualiza solo sus columnas de entrega (nunca la carga, la clave, el tipo ni la sucursal).
    /// </summary>
    public const string Permisos = """
        GRANT USAGE ON SCHEMA traslados TO rol_traslados;
        REVOKE ALL ON traslados.traslados, traslados.mensajes_salientes FROM PUBLIC;
        GRANT SELECT, INSERT ON traslados.traslados TO rol_traslados;
        GRANT UPDATE (estado, motivo, motivo_de_cierre, pedido_anulado, guia, solicitado_por, aprobado_en, despachado_en, recibido_en, cerrado_en, alerta_de_demora_en, version) ON traslados.traslados TO rol_traslados;
        GRANT SELECT, INSERT, DELETE ON traslados.mensajes_salientes TO rol_traslados;
        GRANT UPDATE (estado, intentos, proxima_fecha, arrendado_hasta, motivo_de_fallo, enviado_en) ON traslados.mensajes_salientes TO rol_traslados;
        """;

    /// <summary>La migración completa del módulo, en el orden que exige (rol, tablas y permisos).</summary>
    public const string MigracionCompleta = Rol + Sql + Permisos;
}
