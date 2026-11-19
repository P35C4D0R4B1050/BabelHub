namespace BabelHub.Ecommerce.Infrastructure.Notificaciones;

/// <summary>
/// Definición del esquema <c>notificaciones</c> (<c>plan.md</c> §3, T12): los avisos por WhatsApp y los números desactivados a pedido de su titular. Todo el SQL es idempotente. <b>Nunca se guarda un número de teléfono:</b>
/// el aviso lleva solo la referencia opaca de su destinatario (el id de una cuenta o el código de una sucursal) y los campos cerrados de su plantilla, que la base <b>obliga a vaciar</b> al terminar el aviso; los números
/// desactivados son una huella con la versión de su clave. Las claves únicas hacen de la base la última defensa de «un aviso por evento, destinatario y plantilla» (NOT-05) y de los estados terminales.
/// </summary>
public static class DefinicionDeNotificaciones
{
    /// <summary>Los estados de un aviso tal como se guardan (los siete de <c>EstadoDelAviso</c>).</summary>
    public const string Pendiente = "pendiente";

    public const string Enviando = "enviando";

    public const string Sql = """
        CREATE SCHEMA IF NOT EXISTS notificaciones;

        CREATE TABLE IF NOT EXISTS notificaciones.avisos (
            id uuid PRIMARY KEY,
            evento_id varchar(160) NOT NULL CHECK (char_length(evento_id) >= 5),
            destinatario_ref varchar(64) NOT NULL CHECK (char_length(destinatario_ref) >= 2),
            tipo_destinatario text NOT NULL CHECK (tipo_destinatario IN ('cliente', 'sucursal')),
            plantilla varchar(60) NOT NULL CHECK (char_length(plantilla) >= 1),
            version_plantilla integer NOT NULL CHECK (version_plantilla >= 1),
            campos jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(campos) = 'object'),
            estado text NOT NULL CHECK (estado IN ('pendiente', 'enviando', 'enviado', 'fallido', 'cancelado', 'omitido_sin_consentimiento', 'omitido_por_tope')),
            intentos integer NOT NULL DEFAULT 0 CHECK (intentos BETWEEN 0 AND 6),
            proxima_fecha timestamptz NOT NULL,
            arrendado_hasta timestamptz,
            cupos jsonb NOT NULL DEFAULT '[]'::jsonb CHECK (jsonb_typeof(cupos) = 'array'),
            repeticiones_de_duplicado integer NOT NULL DEFAULT 0 CHECK (repeticiones_de_duplicado BETWEEN 0 AND 4),
            motivo_de_cierre varchar(100),
            creado_en timestamptz NOT NULL,
            fecha_final timestamptz,
            CONSTRAINT avisos_evento_unico UNIQUE (evento_id, destinatario_ref, plantilla),
            CONSTRAINT avisos_final_con_fecha CHECK ((estado IN ('pendiente', 'enviando')) = (fecha_final IS NULL)),
            CONSTRAINT avisos_final_sin_datos CHECK (estado IN ('pendiente', 'enviando') OR (campos = '{}'::jsonb AND cupos = '[]'::jsonb AND arrendado_hasta IS NULL)),
            CONSTRAINT avisos_arrendado_solo_en_envio CHECK (arrendado_hasta IS NULL OR estado = 'enviando')
        );
        CREATE INDEX IF NOT EXISTS avisos_por_estado_y_fecha ON notificaciones.avisos (estado, proxima_fecha);
        CREATE INDEX IF NOT EXISTS avisos_por_destinatario_vigente ON notificaciones.avisos (tipo_destinatario, destinatario_ref) WHERE estado IN ('pendiente', 'enviando');
        CREATE INDEX IF NOT EXISTS avisos_por_fecha_final ON notificaciones.avisos (fecha_final) WHERE fecha_final IS NOT NULL;
        CREATE INDEX IF NOT EXISTS avisos_por_creacion ON notificaciones.avisos (creado_en DESC);

        -- Los números cuyos avisos desactivó su titular (NOT-13): solo la huella HMAC con la versión de su clave, nunca el número. Se conserva mientras dure la desactivación.
        CREATE TABLE IF NOT EXISTS notificaciones.numeros_desactivados (
            version_clave integer NOT NULL CHECK (version_clave >= 1),
            huella_numero varchar(128) NOT NULL CHECK (char_length(huella_numero) >= 16),
            desactivado_en timestamptz NOT NULL,
            motivo varchar(100) NOT NULL CHECK (char_length(motivo) >= 1),
            PRIMARY KEY (version_clave, huella_numero)
        );
        """;

    public const string Permisos = """
        GRANT USAGE ON SCHEMA notificaciones TO rol_notificaciones;
        GRANT SELECT, INSERT, UPDATE, DELETE ON notificaciones.avisos TO rol_notificaciones;
        GRANT SELECT, INSERT, DELETE ON notificaciones.numeros_desactivados TO rol_notificaciones;
        """;
}
