namespace BabelHub.Ecommerce.Infrastructure.Web;

/// <summary>
/// Definición de las tablas del esquema <c>web</c> (plan de <c>web-base</c> §3) con el permiso mínimo de su rol <c>rol_web</c>; la migración las incorpora tal cual
/// (como <c>DefinicionDeTrabajosDeFondo</c> en <c>Plataforma</c>). Las tablas son del propietario y el rol del esquema solo hace lo que el módulo necesita: la
/// aplicación entra con <c>EjecutarComo(web)</c>.
/// </summary>
public static class DefinicionDeLaWeb
{
    public const string Sql = """
        CREATE SCHEMA IF NOT EXISTS web;

        -- Una sesión web (WEB-27, WEB-28, WEB-31). El identificador es el SHA-256 del valor de la cookie (D01): un volcado no sirve para suplantar una sesión.
        -- No guarda el rol ni la sucursal: el actor sale de la credencial verificada en cada solicitud (D04). El estado se deduce: abierta si cerrada_en es nulo;
        -- si no, vencida o cerrada según el motivo (MotivoDeCierre.EstadoResultante).
        CREATE TABLE IF NOT EXISTS web.sesiones_web (
            id_hash bytea PRIMARY KEY CHECK (octet_length(id_hash) = 32),
            usuario_id uuid NOT NULL,
            host text NOT NULL CHECK (host IN ('tienda', 'gestion')),
            creada_en timestamptz NOT NULL,
            ultima_actividad timestamptz NOT NULL,
            expira_absoluta timestamptz NOT NULL,
            credencial_cifrada bytea,
            credencial_vence_en timestamptz,
            renovacion_cifrada bytea,
            renovacion_incierta boolean NOT NULL DEFAULT false,
            retorno_tras_aceptar text CHECK (retorno_tras_aceptar IS NULL OR char_length(retorno_tras_aceptar) <= 200),
            revocacion_pendiente boolean NOT NULL DEFAULT false,
            reintentos_de_revocacion integer NOT NULL DEFAULT 0 CHECK (reintentos_de_revocacion >= 0),
            version_de_clave smallint NOT NULL,
            cerrada_en timestamptz,
            motivo_de_cierre text CHECK (motivo_de_cierre IN ('voluntario', 'vencida_por_inactividad', 'vencida_por_tope_absoluto', 'renovacion_rechazada', 'cerrada_por_revocacion')),
            CONSTRAINT sesiones_web_vigencia CHECK (expira_absoluta > creada_en),
            CONSTRAINT sesiones_web_cierre_con_motivo CHECK ((cerrada_en IS NULL) = (motivo_de_cierre IS NULL)),
            -- Cerrar o vencer una sesión borra siempre la credencial cifrada y el retorno pendiente (D05): la base lo exige, no solo el código.
            CONSTRAINT sesiones_web_cerrada_sin_credencial CHECK (cerrada_en IS NULL OR (credencial_cifrada IS NULL AND credencial_vence_en IS NULL AND retorno_tras_aceptar IS NULL))
        );
        CREATE INDEX IF NOT EXISTS sesiones_web_por_usuario ON web.sesiones_web (usuario_id);
        CREATE INDEX IF NOT EXISTS sesiones_web_por_vencimiento ON web.sesiones_web (expira_absoluta);

        -- Lo que la persona tenía en pantalla cuando venció su sesión (WEB-25, D06): un dato personal, cifrado y de 30 minutos. La plantilla es de una lista cerrada que
        -- valida el Dominio; aquí solo se exige su forma, para que una pantalla nueva no obligue a migrar. Los 32 KB del Dominio se miden sobre el texto en UTF-8; el
        -- cuerpo cifrado es su JSON (que escapa lo que no es ASCII, hasta tres veces más) y el tope de la columna lo cubre con holgura.
        CREATE TABLE IF NOT EXISTS web.borradores (
            id uuid PRIMARY KEY,
            usuario_id uuid NOT NULL,
            plantilla_de_ruta text NOT NULL CHECK (plantilla_de_ruta ~ '^/[a-z0-9/_-]{1,98}$'),
            cuerpo_cifrado bytea NOT NULL CHECK (octet_length(cuerpo_cifrado) <= 131072),
            version_de_clave smallint NOT NULL,
            creado_en timestamptz NOT NULL,
            vence_en timestamptz NOT NULL,
            CONSTRAINT borradores_un_borrador_por_plantilla UNIQUE (usuario_id, plantilla_de_ruta),
            CONSTRAINT borradores_vigencia CHECK (vence_en > creado_en)
        );
        CREATE INDEX IF NOT EXISTS borradores_por_vencimiento ON web.borradores (vence_en);

        -- El anillo de claves de Data Protection (antiforgery, T12): uno por host. Cada réplica lee y escribe solo las filas de su host; el XML va cifrado con la clave
        -- maestra de ese host y nunca en claro. Las claves no se borran desde la aplicación: la restauración conserva el anillo (D14).
        CREATE TABLE IF NOT EXISTS web.claves_de_proteccion (
            id uuid PRIMARY KEY,
            host text NOT NULL CHECK (host IN ('tienda', 'gestion')),
            xml_cifrado bytea NOT NULL CHECK (octet_length(xml_cifrado) <= 65536),
            version_de_clave smallint NOT NULL,
            creada_en timestamptz NOT NULL
        );
        CREATE INDEX IF NOT EXISTS claves_de_proteccion_por_host ON web.claves_de_proteccion (host);
        """;

    /// <summary>
    /// Lo que <c>rol_web</c> puede hacer: leer, insertar y borrar sesiones y borradores y actualizar <b>solo las columnas que cambian</b> (el identificador, el usuario, el host, la
    /// creación y el tope absoluto de una sesión, y el usuario y la plantilla de un borrador, no cambian nunca: lo exige el plan §3). Sin <c>TRUNCATE</c> ni <c>DROP</c>: vaciar las
    /// tablas al restaurar lo hace el paso de restauración con el permiso de borrado, no con un permiso más amplio.
    /// </summary>
    public const string Permisos = """
        GRANT USAGE ON SCHEMA web TO rol_web;
        GRANT SELECT, INSERT, DELETE ON web.sesiones_web TO rol_web;
        GRANT UPDATE (ultima_actividad, credencial_cifrada, credencial_vence_en, renovacion_cifrada, renovacion_incierta, retorno_tras_aceptar, revocacion_pendiente,
                      reintentos_de_revocacion, version_de_clave, cerrada_en, motivo_de_cierre) ON web.sesiones_web TO rol_web;
        GRANT SELECT, INSERT, DELETE ON web.borradores TO rol_web;
        GRANT UPDATE (id, cuerpo_cifrado, version_de_clave, creado_en, vence_en) ON web.borradores TO rol_web;
        GRANT SELECT, INSERT ON web.claves_de_proteccion TO rol_web;
        GRANT UPDATE (xml_cifrado, version_de_clave) ON web.claves_de_proteccion TO rol_web;
        """;
}
