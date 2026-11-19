namespace BabelHub.Ecommerce.Infrastructure.Identidad;

/// <summary>
/// El esquema <c>identidad</c> del E-commerce (identidad-seguridad T30; plan §3, D08): sus tablas, con el permiso mínimo de su rol <c>rol_identidad</c> (<c>EjecutarComo</c>), y los registros aislados
/// <c>identidad.registros</c> y <c>identidad.por_anonimizar</c> con la plantilla de inmutabilidad, los roles, los disparadores y la función de depuración de <c>transversal-auditoria</c> (T23 y T24). La aplicación
/// se conecta con un rol de acceso <c>NOINHERIT</c> que <b>no</b> hereda nada: sin <c>SET LOCAL ROLE rol_identidad</c> no puede leer ni escribir el esquema. Se aplica <b>después</b> de la migración de
/// <c>Auditoria</c> (que crea los roles y la política de la depuración). Todo es idempotente.
/// </summary>
public static class DefinicionDeIdentidad
{
    public const string Esquema = "identidad";

    /// <summary>El rol del esquema que usa <c>EjecutarComo</c>: <c>rol_&lt;esquema&gt;</c> (convención de la unidad de trabajo). Sin login: lo asume el rol de acceso de la aplicación.</summary>
    public const string RolDeLaAplicacion = "rol_identidad";

    public const string Roles = """
        DO $roles$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rol_identidad') THEN CREATE ROLE rol_identidad NOLOGIN; END IF;
        END
        $roles$;
        """;

    /// <summary>Las tablas del módulo (propietario: el rol de migraciones de <c>Auditoria</c>, que también es dueño de los registros aislados) con sus restricciones.</summary>
    public const string Tablas = """
        CREATE SCHEMA IF NOT EXISTS identidad;
        ALTER SCHEMA identidad OWNER TO auditoria_migraciones;

        -- La cuenta (IDE-01, IDE-02, IDE-06, IDE-23, IDE-31). El correo guardado es el normalizado y único; la sucursal es de los encargados y solo de ellos; la contraseña es solo su resumen Argon2id.
        CREATE TABLE IF NOT EXISTS identidad.usuarios (
            id uuid PRIMARY KEY,
            nombre text NOT NULL CHECK (char_length(nombre) BETWEEN 1 AND 200),
            correo text NOT NULL CHECK (char_length(correo) BETWEEN 3 AND 254 AND correo = lower(correo)),
            correo_verificado boolean NOT NULL,
            rol text NOT NULL CHECK (rol IN ('cliente', 'encargado-de-sucursal', 'personal-de-logistica', 'administrador-central')),
            sucursal uuid,
            activo boolean NOT NULL,
            hash_contrasena text CHECK (hash_contrasena IS NULL OR hash_contrasena ~ '^\$argon2id\$v=[0-9]+\$m=[0-9]+,t=[0-9]+,p=[0-9]+\$[A-Za-z0-9+/]{22,}\$[A-Za-z0-9+/]{43,}$'),
            contrasena_temporal_hasta timestamptz,
            debe_cambiar boolean NOT NULL DEFAULT false,
            sesiones_validas_desde timestamptz,
            creada_en timestamptz NOT NULL,
            cancelada_en timestamptz,
            mayor_de_18_en timestamptz,
            acepto_terminos_version text,
            acepto_terminos_en timestamptz,
            acepto_privacidad_version text,
            acepto_privacidad_en timestamptz,
            acepto_reembolso_version text,
            acepto_reembolso_en timestamptz,
            numero_whatsapp text CHECK (numero_whatsapp IS NULL OR numero_whatsapp ~ '^\+[1-9][0-9]{7,14}$'),
            consentimiento_whatsapp boolean NOT NULL DEFAULT false,
            consentimiento_whatsapp_en timestamptz,
            CONSTRAINT usuarios_correo_unico UNIQUE (correo),
            CONSTRAINT usuarios_sucursal_del_encargado CHECK ((rol = 'encargado-de-sucursal') = (sucursal IS NOT NULL)),
            CONSTRAINT usuarios_consentimiento_con_numero CHECK (NOT consentimiento_whatsapp OR numero_whatsapp IS NOT NULL)
        );
        -- Un solo encargado activo por sucursal (IDE-06): la regla la respalda el índice, no una consulta previa.
        CREATE UNIQUE INDEX IF NOT EXISTS usuarios_un_encargado_activo_por_sucursal ON identidad.usuarios (sucursal)
            WHERE rol = 'encargado-de-sucursal' AND activo AND cancelada_en IS NULL;
        CREATE INDEX IF NOT EXISTS usuarios_por_rol_activo ON identidad.usuarios (rol) WHERE activo AND cancelada_en IS NULL;

        -- Los textos legales vigentes (IDE-34, IDE-43, IDE-57): una sola fila.
        CREATE TABLE IF NOT EXISTS identidad.versiones_de_textos (
            id smallint PRIMARY KEY CHECK (id = 1),
            terminos text NOT NULL CHECK (char_length(terminos) BETWEEN 1 AND 40),
            privacidad text NOT NULL CHECK (char_length(privacidad) BETWEEN 1 AND 40),
            reembolso text NOT NULL CHECK (char_length(reembolso) BETWEEN 1 AND 40)
        );

        -- El segundo factor (IDE-20, IDE-21, IDE-51): el secreto va cifrado con AES-GCM; el paso aceptado y los códigos de respaldo se consumen con actualizaciones condicionales atómicas.
        CREATE TABLE IF NOT EXISTS identidad.segundo_factor (
            usuario_id uuid PRIMARY KEY REFERENCES identidad.usuarios (id),
            secreto_cifrado bytea NOT NULL CHECK (octet_length(secreto_cifrado) BETWEEN 30 AND 200),
            estado text NOT NULL CHECK (estado IN ('sin-confirmar', 'activo')),
            confirmado_en timestamptz,
            ultimo_paso_usado bigint CHECK (ultimo_paso_usado IS NULL OR ultimo_paso_usado >= 0),
            CONSTRAINT segundo_factor_confirmado CHECK ((estado = 'activo') = (confirmado_en IS NOT NULL))
        );
        CREATE TABLE IF NOT EXISTS identidad.codigos_de_respaldo (
            usuario_id uuid NOT NULL REFERENCES identidad.usuarios (id),
            huella text NOT NULL CHECK (huella ~ '^[0-9a-f]{64}$'),
            usado_en timestamptz,
            PRIMARY KEY (usuario_id, huella)
        );

        -- Los desafíos de ingreso (5 minutos) y las renovaciones opacas: solo la huella del valor, nunca el valor.
        CREATE TABLE IF NOT EXISTS identidad.desafios_de_ingreso (
            id uuid PRIMARY KEY,
            huella text NOT NULL CHECK (huella ~ '^[0-9a-f]{64}$'),
            usuario_id uuid NOT NULL REFERENCES identidad.usuarios (id),
            modo text NOT NULL CHECK (modo IN ('presentar-segundo-factor', 'configurar-segundo-factor', 'cambiar-contrasena-temporal')),
            creado_en timestamptz NOT NULL,
            vence_en timestamptz NOT NULL,
            usado boolean NOT NULL DEFAULT false,
            CONSTRAINT desafios_huella_unica UNIQUE (huella),
            CONSTRAINT desafios_vigencia CHECK (vence_en > creado_en)
        );
        CREATE INDEX IF NOT EXISTS desafios_por_vencimiento ON identidad.desafios_de_ingreso (vence_en);

        CREATE TABLE IF NOT EXISTS identidad.renovaciones (
            huella text PRIMARY KEY CHECK (huella ~ '^[0-9a-f]{64}$'),
            usuario_id uuid NOT NULL REFERENCES identidad.usuarios (id),
            familia uuid NOT NULL,
            emitida_en timestamptz NOT NULL,
            expira_absoluta timestamptz NOT NULL,
            usada_en timestamptz,
            revocada_en timestamptz,
            CONSTRAINT renovaciones_vigencia CHECK (expira_absoluta > emitida_en)
        );
        CREATE INDEX IF NOT EXISTS renovaciones_por_usuario ON identidad.renovaciones (usuario_id, familia);
        CREATE INDEX IF NOT EXISTS renovaciones_por_expiracion ON identidad.renovaciones (expira_absoluta);

        -- Los códigos de un solo uso (IDE-19): solo la huella con clave; como máximo uno vigente por usuario y propósito (el nuevo invalida el anterior).
        CREATE TABLE IF NOT EXISTS identidad.codigos_de_un_solo_uso (
            id uuid PRIMARY KEY,
            usuario_id uuid NOT NULL REFERENCES identidad.usuarios (id),
            proposito text NOT NULL CHECK (proposito IN ('verificacion-de-correo', 'recuperacion-de-acceso')),
            huella text NOT NULL CHECK (huella ~ '^[0-9a-f]{64}$'),
            creado_en timestamptz NOT NULL,
            vence_en timestamptz NOT NULL,
            intentos integer NOT NULL DEFAULT 0 CHECK (intentos >= 0),
            usado boolean NOT NULL DEFAULT false,
            invalidado boolean NOT NULL DEFAULT false,
            CONSTRAINT codigos_vigencia CHECK (vence_en > creado_en)
        );
        CREATE UNIQUE INDEX IF NOT EXISTS codigos_un_vigente_por_proposito ON identidad.codigos_de_un_solo_uso (usuario_id, proposito) WHERE NOT usado AND NOT invalidado;
        CREATE INDEX IF NOT EXISTS codigos_por_vencimiento ON identidad.codigos_de_un_solo_uso (vence_en);

        -- La fila idéntica del camino síncrono de una solicitud de código (IDE-65): huella del correo, exista o no la cuenta; se purga a los 10 minutos.
        CREATE TABLE IF NOT EXISTS identidad.solicitudes_de_codigo (
            id uuid PRIMARY KEY,
            huella_correo text NOT NULL CHECK (huella_correo ~ '^[0-9a-f]{64}$'),
            proposito text NOT NULL CHECK (proposito IN ('verificacion-de-correo', 'recuperacion-de-acceso')),
            instante timestamptz NOT NULL
        );
        CREATE INDEX IF NOT EXISTS solicitudes_de_codigo_por_instante ON identidad.solicitudes_de_codigo (instante);

        -- Las solicitudes sobre datos personales (IDE-12, IDE-26, IDE-63): lo mínimo, sin copiar datos de la cuenta.
        CREATE TABLE IF NOT EXISTS identidad.solicitudes_de_datos (
            id uuid PRIMARY KEY,
            usuario_id uuid NOT NULL,
            tipo text NOT NULL CHECK (tipo IN ('acceso', 'rectificacion', 'cancelacion', 'oposicion')),
            estado text NOT NULL CHECK (estado IN ('recibida', 'en-atencion', 'atendida', 'rechazada')),
            recibida_en timestamptz NOT NULL,
            fecha_limite date NOT NULL,
            atendida_en timestamptz,
            respuesta text CHECK (respuesta IS NULL OR char_length(respuesta) <= 4000),
            motivo text CHECK (motivo IS NULL OR char_length(motivo) <= 1000)
        );
        CREATE INDEX IF NOT EXISTS solicitudes_de_datos_por_usuario ON identidad.solicitudes_de_datos (usuario_id, recibida_en DESC);
        CREATE INDEX IF NOT EXISTS solicitudes_de_datos_por_limite ON identidad.solicitudes_de_datos (fecha_limite) WHERE estado IN ('recibida', 'en-atencion');

        -- Las solicitudes de cancelación de cuenta (IDE-27, IDE-68, IDE-81): como máximo una abierta por usuario, respaldado por el índice.
        CREATE TABLE IF NOT EXISTS identidad.solicitudes_de_cancelacion (
            id uuid PRIMARY KEY,
            usuario_id uuid NOT NULL,
            estado text NOT NULL CHECK (estado IN ('diferida', 'esperando-reconfirmacion', 'ejecutada', 'descartada')),
            solicitada_en timestamptz NOT NULL,
            reconfirmar_hasta timestamptz,
            aviso_visto_en timestamptz,
            cerrada_en timestamptz,
            ejecucion_en_curso boolean NOT NULL DEFAULT false
        );
        CREATE UNIQUE INDEX IF NOT EXISTS cancelaciones_una_abierta_por_usuario ON identidad.solicitudes_de_cancelacion (usuario_id) WHERE estado IN ('diferida', 'esperando-reconfirmacion');
        CREATE INDEX IF NOT EXISTS cancelaciones_por_usuario ON identidad.solicitudes_de_cancelacion (usuario_id, solicitada_en DESC);

        -- Los dispositivos reconocidos (IDE-55, D11): solo la huella de la marca y la región (país de dos letras).
        CREATE TABLE IF NOT EXISTS identidad.dispositivos (
            usuario_id uuid NOT NULL REFERENCES identidad.usuarios (id),
            huella_marca text NOT NULL CHECK (huella_marca ~ '^[0-9a-f]{64}$'),
            region text NOT NULL CHECK (region ~ '^[A-Z]{2}$'),
            emitido_en timestamptz NOT NULL,
            congelado_hasta timestamptz,
            PRIMARY KEY (usuario_id, huella_marca)
        );

        -- El cambio de correo en curso (IDE-53, IDE-54): como máximo uno por usuario; el correo nuevo se elimina al cancelar la cuenta.
        CREATE TABLE IF NOT EXISTS identidad.cambios_de_correo_pendientes (
            id uuid PRIMARY KEY,
            usuario_id uuid NOT NULL REFERENCES identidad.usuarios (id),
            correo_nuevo text NOT NULL CHECK (char_length(correo_nuevo) BETWEEN 3 AND 254 AND correo_nuevo = lower(correo_nuevo)),
            creado_en timestamptz NOT NULL,
            vence_en timestamptz NOT NULL,
            estado text NOT NULL CHECK (estado IN ('esperando-verificacion', 'pendiente-de-confirmacion', 'aplicado', 'rechazado')),
            CONSTRAINT cambios_un_pendiente_por_usuario UNIQUE (usuario_id),
            CONSTRAINT cambios_vigencia CHECK (vence_en > creado_en)
        );

        -- Las claves de firma (IDE-35, IDE-36, IDE-61): el JWK público para publicar y la privada cifrada (AES-GCM) que nunca sale de Identidad. Una sola clave activa, respaldado por el índice.
        CREATE TABLE IF NOT EXISTS identidad.claves_de_firma (
            kid text PRIMARY KEY CHECK (kid ~ '^[A-Za-z0-9_-]{8,64}$'),
            creada_en timestamptz NOT NULL,
            estado text NOT NULL CHECK (estado IN ('activa', 'anterior', 'comprometida', 'retirada')),
            dejo_de_firmar_en timestamptz,
            comprometida_en timestamptz,
            jwk_publico text NOT NULL CHECK (char_length(jwk_publico) BETWEEN 50 AND 2048),
            privada_cifrada bytea NOT NULL CHECK (octet_length(privada_cifrada) BETWEEN 30 AND 1024),
            CONSTRAINT claves_comprometida_con_fecha CHECK ((estado = 'comprometida') = (comprometida_en IS NOT NULL))
        );
        CREATE UNIQUE INDEX IF NOT EXISTS claves_una_sola_activa ON identidad.claves_de_firma ((estado)) WHERE estado = 'activa';

        -- Los avisos que se muestran en la cuenta (IDE-53, IDE-76, IDE-79): solo el tipo del catálogo y la fecha.
        CREATE TABLE IF NOT EXISTS identidad.avisos_de_cuenta (
            id uuid PRIMARY KEY,
            usuario_id uuid NOT NULL,
            tipo text NOT NULL CHECK (tipo ~ '^[a-z][a-z0-9-]{1,63}$'),
            fecha timestamptz NOT NULL,
            visto_en timestamptz
        );
        CREATE INDEX IF NOT EXISTS avisos_por_usuario ON identidad.avisos_de_cuenta (usuario_id, fecha DESC);

        -- La baja del titular que se entrega al Inventario y cuyo cierre se agrega al registro de supresiones (IDE-64, IDE-75): solo el identificador opaco y fechas, sin clave foránea (sobrevive a la
        -- anonimización de la cuenta). Entregar y cerrar son idempotentes; una sola fila cubre la baja y su cierre pendiente.
        CREATE TABLE IF NOT EXISTS identidad.bajas_titulares (
            identificador_opaco uuid PRIMARY KEY,
            creada_en timestamptz NOT NULL,
            entregada_en timestamptz,
            cerrada_en timestamptz
        );
        CREATE INDEX IF NOT EXISTS bajas_pendientes ON identidad.bajas_titulares (creada_en) WHERE entregada_en IS NULL OR cerrada_en IS NULL;

        -- El propietario de todas las tablas es el rol de migraciones: la aplicación solo tiene los permisos de rol_identidad.
        DO $propietario$
        DECLARE t record;
        BEGIN
            FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'identidad' LOOP
                EXECUTE format('ALTER TABLE identidad.%I OWNER TO auditoria_migraciones', t.tablename);
            END LOOP;
        END
        $propietario$;
        """;

    /// <summary>
    /// Los permisos mínimos de <c>rol_identidad</c>: lo que cada tabla necesita y nada más (nunca <c>TRUNCATE</c>, nunca <c>DELETE</c> de lo que se conserva: claves, bajas y solicitudes). Los registros aislados
    /// solo se leen y se insertan.
    /// </summary>
    public const string Permisos = """
        -- Primero se retira todo: la migración deja exactamente estos permisos, aunque un despliegue anterior haya concedido más.
        REVOKE ALL ON ALL TABLES IN SCHEMA identidad FROM rol_identidad;
        GRANT USAGE ON SCHEMA identidad TO rol_identidad;
        GRANT SELECT, INSERT, UPDATE, DELETE ON identidad.usuarios TO rol_identidad;
        GRANT SELECT ON identidad.versiones_de_textos TO rol_identidad;
        GRANT SELECT, INSERT, UPDATE, DELETE ON identidad.segundo_factor TO rol_identidad;
        GRANT SELECT, INSERT, UPDATE, DELETE ON identidad.codigos_de_respaldo TO rol_identidad;
        GRANT SELECT, INSERT, UPDATE, DELETE ON identidad.desafios_de_ingreso TO rol_identidad;
        GRANT SELECT, INSERT, UPDATE, DELETE ON identidad.renovaciones TO rol_identidad;
        GRANT SELECT, INSERT, UPDATE, DELETE ON identidad.codigos_de_un_solo_uso TO rol_identidad;
        GRANT SELECT, INSERT, DELETE ON identidad.solicitudes_de_codigo TO rol_identidad;
        GRANT SELECT, INSERT, UPDATE ON identidad.solicitudes_de_datos TO rol_identidad;
        GRANT SELECT, INSERT, UPDATE ON identidad.solicitudes_de_cancelacion TO rol_identidad;
        GRANT SELECT, INSERT, UPDATE, DELETE ON identidad.dispositivos TO rol_identidad;
        GRANT SELECT, INSERT, UPDATE, DELETE ON identidad.cambios_de_correo_pendientes TO rol_identidad;
        GRANT SELECT, INSERT ON identidad.claves_de_firma TO rol_identidad;
        GRANT UPDATE (estado, dejo_de_firmar_en, comprometida_en) ON identidad.claves_de_firma TO rol_identidad;
        GRANT SELECT, INSERT, UPDATE, DELETE ON identidad.avisos_de_cuenta TO rol_identidad;
        GRANT SELECT, INSERT, UPDATE ON identidad.bajas_titulares TO rol_identidad;
        GRANT SELECT, INSERT ON identidad.registros TO rol_identidad;
        GRANT SELECT, INSERT ON identidad.por_anonimizar TO rol_identidad;
        """;
}
