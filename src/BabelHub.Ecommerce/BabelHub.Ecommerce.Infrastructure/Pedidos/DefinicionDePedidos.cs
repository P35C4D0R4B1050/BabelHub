namespace BabelHub.Ecommerce.Infrastructure.Pedidos;

/// <summary>
/// Definición de las tablas del esquema <c>pedidos</c> (<c>plan.md</c> §3, T28) con el permiso mínimo de su rol <c>rol_pedidos</c>; la migración las incorpora tal cual (como <c>DefinicionDeEnvios</c>): un rol aparte es dueño de las tablas y la aplicación entra con <c>EjecutarComo(pedidos)</c>.
/// El <b>contacto de entrega</b> (nombre, teléfono y dirección) vive solo en <c>pedidos.pedidos</c> y nunca en una tabla de operaciones, avisos ni registros; los <c>CHECK</c> repiten las reglas del Dominio (el contacto solo a domicilio, completo o ausente y nulo al anonimizarse; el estado de cada línea coherente con su tipo y sus fechas).
/// Los índices únicos parciales hacen que <b>una línea tenga a lo más una anulación o una entrega en curso</b> y un pedido una sola anulación en curso (PED-21).
/// </summary>
public static class DefinicionDePedidos
{
    public const string Sql = """
        CREATE SCHEMA IF NOT EXISTS pedidos;

        CREATE TABLE IF NOT EXISTS pedidos.pedidos (
            id uuid PRIMARY KEY,
            numero text NOT NULL CHECK (char_length(numero) = 10),
            cuenta_id uuid NOT NULL,
            tipo_de_entrega text NOT NULL CHECK (tipo_de_entrega IN ('recojo', 'domicilio', 'digital')),
            codigo_de_sucursal_de_recojo text CHECK (char_length(codigo_de_sucursal_de_recojo) BETWEEN 1 AND 40),
            region_de_entrega text CHECK (char_length(region_de_entrega) BETWEEN 1 AND 80),
            costo_de_envio numeric(12, 2) NOT NULL CHECK (costo_de_envio >= 0),
            total numeric(12, 2) NOT NULL CHECK (total > 0),
            estado text NOT NULL CHECK (estado IN ('confirmado', 'en_transito', 'recibido', 'en_preparacion', 'listo_para_recojo', 'despachado', 'entregado', 'no_recogido', 'anulado')),
            creado_en timestamptz NOT NULL,
            fecha_estado_final timestamptz,
            contacto_nombre text CHECK (char_length(contacto_nombre) BETWEEN 1 AND 120),
            contacto_telefono text CHECK (char_length(contacto_telefono) BETWEEN 1 AND 20),
            contacto_direccion text CHECK (char_length(contacto_direccion) BETWEEN 1 AND 200),
            contacto_anonimizado_en timestamptz,
            terminos_version text NOT NULL,
            terminos_aceptado_en timestamptz NOT NULL,
            privacidad_version text NOT NULL,
            privacidad_aceptado_en timestamptz NOT NULL,
            reembolso_digital_version text,
            reembolso_digital_aceptado_en timestamptz,
            version integer NOT NULL CHECK (version >= 1),
            CONSTRAINT pedidos_numero_unico UNIQUE (numero),
            CONSTRAINT pedidos_contacto_completo_o_ausente CHECK (
                (contacto_nombre IS NULL) = (contacto_telefono IS NULL) AND (contacto_nombre IS NULL) = (contacto_direccion IS NULL)),
            CONSTRAINT pedidos_contacto_solo_a_domicilio CHECK (contacto_nombre IS NULL OR tipo_de_entrega = 'domicilio'),
            CONSTRAINT pedidos_contacto_anonimizado_sin_datos CHECK (contacto_anonimizado_en IS NULL OR contacto_nombre IS NULL),
            CONSTRAINT pedidos_domicilio_con_contacto CHECK (tipo_de_entrega <> 'domicilio' OR contacto_anonimizado_en IS NOT NULL OR contacto_nombre IS NOT NULL),
            CONSTRAINT pedidos_recojo_con_sucursal CHECK ((tipo_de_entrega = 'recojo') = (codigo_de_sucursal_de_recojo IS NOT NULL)),
            CONSTRAINT pedidos_domicilio_con_region CHECK ((tipo_de_entrega = 'domicilio') = (region_de_entrega IS NOT NULL)),
            CONSTRAINT pedidos_reembolso_digital_junto CHECK ((reembolso_digital_version IS NULL) = (reembolso_digital_aceptado_en IS NULL)),
            CONSTRAINT pedidos_fecha_final_con_estado_final CHECK ((estado IN ('entregado', 'anulado')) = (fecha_estado_final IS NOT NULL))
        );
        CREATE INDEX IF NOT EXISTS pedidos_por_cuenta ON pedidos.pedidos (cuenta_id, creado_en);
        CREATE INDEX IF NOT EXISTS pedidos_por_sucursal ON pedidos.pedidos (codigo_de_sucursal_de_recojo, estado);
        CREATE INDEX IF NOT EXISTS pedidos_por_tipo_y_estado ON pedidos.pedidos (tipo_de_entrega, estado);
        CREATE INDEX IF NOT EXISTS pedidos_por_estado ON pedidos.pedidos (estado, creado_en);

        CREATE TABLE IF NOT EXISTS pedidos.lineas_de_pedido (
            id uuid PRIMARY KEY,
            pedido_id uuid NOT NULL REFERENCES pedidos.pedidos (id),
            edicion_id uuid NOT NULL,
            posicion integer NOT NULL CHECK (posicion >= 0),
            es_digital boolean NOT NULL,
            titulo text NOT NULL CHECK (char_length(titulo) BETWEEN 1 AND 300),
            isbn text CHECK (char_length(isbn) BETWEEN 1 AND 20),
            cantidad integer NOT NULL CHECK (cantidad >= 1),
            precio_unitario numeric(12, 2) NOT NULL CHECK (precio_unitario > 0),
            importe numeric(12, 2) NOT NULL CHECK (importe > 0),
            estado text NOT NULL CHECK (estado IN ('confirmado', 'en_transito', 'recibido', 'en_preparacion', 'listo_para_recojo', 'despachado', 'entregado', 'no_recogido', 'anulado', 'disponible')),
            codigo_de_fuente text CHECK (char_length(codigo_de_fuente) BETWEEN 1 AND 40),
            requiere_traslado boolean NOT NULL,
            traslado_id uuid,
            fecha_estimada_de_llegada date,
            listo_en timestamptz,
            entregada_en timestamptz,
            no_recogida_en timestamptz,
            anulada_en timestamptz,
            envio_id uuid,
            version integer NOT NULL CHECK (version >= 1),
            CONSTRAINT lineas_una_por_edicion UNIQUE (pedido_id, edicion_id),
            CONSTRAINT lineas_digital_sin_fuente CHECK (NOT es_digital OR (codigo_de_fuente IS NULL AND NOT requiere_traslado AND traslado_id IS NULL AND envio_id IS NULL)),
            CONSTRAINT lineas_impresa_con_fuente CHECK (es_digital OR codigo_de_fuente IS NOT NULL),
            CONSTRAINT lineas_estado_segun_tipo CHECK ((es_digital AND estado IN ('disponible', 'anulado')) OR (NOT es_digital AND estado <> 'disponible')),
            CONSTRAINT lineas_traslado_solo_con_traslado CHECK (requiere_traslado = (traslado_id IS NOT NULL)),
            CONSTRAINT lineas_fecha_de_entrega CHECK ((estado = 'entregado') = (entregada_en IS NOT NULL)),
            CONSTRAINT lineas_fecha_de_anulacion CHECK ((estado = 'anulado') = (anulada_en IS NOT NULL))
        );
        CREATE INDEX IF NOT EXISTS lineas_por_pedido ON pedidos.lineas_de_pedido (pedido_id, posicion);
        CREATE INDEX IF NOT EXISTS lineas_por_envio ON pedidos.lineas_de_pedido (envio_id) WHERE envio_id IS NOT NULL;
        CREATE INDEX IF NOT EXISTS lineas_por_traslado ON pedidos.lineas_de_pedido (traslado_id) WHERE traslado_id IS NOT NULL;
        CREATE INDEX IF NOT EXISTS lineas_por_estado ON pedidos.lineas_de_pedido (estado, no_recogida_en);

        -- Las operaciones en curso: la fila durable de cada anulación o entrega (D03). Guardan el iniciador (dato personal del personal) que se anonimiza a los dos años (PED-34).
        CREATE TABLE IF NOT EXISTS pedidos.anulaciones (
            id uuid PRIMARY KEY,
            pedido_id uuid NOT NULL REFERENCES pedidos.pedidos (id),
            origen text NOT NULL CHECK (origen IN ('administracion', 'cliente')),
            motivo text NOT NULL CHECK (motivo IN ('pago-duplicado', 'no-recogido', 'otro', 'cancelado-por-el-cliente')),
            incluye_envio boolean NOT NULL,
            hay_digitales boolean NOT NULL,
            monto numeric(12, 2) NOT NULL CHECK (monto >= 0),
            estado text NOT NULL CHECK (estado IN ('en_curso', 'completada', 'fallida', 'descartada')),
            reembolso_id uuid,
            envios_cancelados_en timestamptz,
            reservas_liberadas_en timestamptz,
            reembolso_registrado_en timestamptz,
            accesos_revocados_en timestamptz,
            iniciada_por uuid NOT NULL,
            rol_del_iniciador text CHECK (char_length(rol_del_iniciador) BETWEEN 1 AND 32),
            sucursal_del_iniciador uuid,
            proximo_intento_en timestamptz,
            intentos integer NOT NULL CHECK (intentos >= 0),
            creada_en timestamptz NOT NULL,
            completada_en timestamptz,
            CONSTRAINT anulaciones_reembolso_con_su_paso CHECK ((reembolso_id IS NULL) = (reembolso_registrado_en IS NULL)),
            CONSTRAINT anulaciones_completada_con_fecha CHECK (estado NOT IN ('completada') OR completada_en IS NOT NULL)
        );
        CREATE UNIQUE INDEX IF NOT EXISTS anulaciones_una_en_curso_por_pedido ON pedidos.anulaciones (pedido_id) WHERE estado = 'en_curso';
        CREATE INDEX IF NOT EXISTS anulaciones_por_pedido ON pedidos.anulaciones (pedido_id, creada_en);
        CREATE INDEX IF NOT EXISTS anulaciones_por_reanudar ON pedidos.anulaciones (proximo_intento_en) WHERE estado = 'en_curso';

        CREATE TABLE IF NOT EXISTS pedidos.anulacion_lineas (
            anulacion_id uuid NOT NULL REFERENCES pedidos.anulaciones (id),
            linea_id uuid NOT NULL REFERENCES pedidos.lineas_de_pedido (id),
            posicion integer NOT NULL CHECK (posicion >= 0),
            en_curso boolean NOT NULL,
            PRIMARY KEY (anulacion_id, linea_id)
        );
        CREATE UNIQUE INDEX IF NOT EXISTS anulacion_lineas_una_en_curso ON pedidos.anulacion_lineas (linea_id) WHERE en_curso;

        CREATE TABLE IF NOT EXISTS pedidos.entregas (
            id uuid PRIMARY KEY,
            pedido_id uuid NOT NULL REFERENCES pedidos.pedidos (id),
            estado text NOT NULL CHECK (estado IN ('en_curso', 'completada', 'fallida', 'descartada')),
            iniciada_por uuid NOT NULL,
            rol_del_iniciador text CHECK (char_length(rol_del_iniciador) BETWEEN 1 AND 32),
            sucursal_del_iniciador uuid,
            proximo_intento_en timestamptz,
            intentos integer NOT NULL CHECK (intentos >= 0),
            creada_en timestamptz NOT NULL
        );
        CREATE INDEX IF NOT EXISTS entregas_por_pedido ON pedidos.entregas (pedido_id, creada_en);
        CREATE INDEX IF NOT EXISTS entregas_por_reanudar ON pedidos.entregas (proximo_intento_en) WHERE estado = 'en_curso';

        CREATE TABLE IF NOT EXISTS pedidos.entrega_lineas (
            entrega_id uuid NOT NULL REFERENCES pedidos.entregas (id),
            linea_id uuid NOT NULL REFERENCES pedidos.lineas_de_pedido (id),
            posicion integer NOT NULL CHECK (posicion >= 0),
            clave_de_cierre text NOT NULL CHECK (char_length(clave_de_cierre) BETWEEN 1 AND 80),
            cierre_confirmado_en timestamptz,
            resultado_del_cierre text CHECK (resultado_del_cierre IN ('cerrada', 'ya_cerrada', 'ya_salio')),
            en_curso boolean NOT NULL,
            PRIMARY KEY (entrega_id, linea_id),
            CONSTRAINT entrega_lineas_resultado_con_fecha CHECK ((cierre_confirmado_en IS NULL) = (resultado_del_cierre IS NULL))
        );
        CREATE UNIQUE INDEX IF NOT EXISTS entrega_lineas_una_en_curso ON pedidos.entrega_lineas (linea_id) WHERE en_curso;

        CREATE TABLE IF NOT EXISTS pedidos.liberaciones_de_reserva (
            linea_id uuid PRIMARY KEY REFERENCES pedidos.lineas_de_pedido (id),
            pedido_id uuid NOT NULL REFERENCES pedidos.pedidos (id),
            motivo text NOT NULL CHECK (motivo IN ('no-recogido')),
            pendiente_desde timestamptz NOT NULL,
            intentos integer NOT NULL CHECK (intentos >= 0)
        );

        -- Solo sirve para la idempotencia de la entrada del Inventario (PED-19): se purga a los 90 días.
        CREATE TABLE IF NOT EXISTS pedidos.estados_de_traslado_recibidos (
            evento_id text PRIMARY KEY CHECK (char_length(evento_id) BETWEEN 1 AND 120),
            traslado_id uuid NOT NULL,
            pedido_id uuid NOT NULL,
            estado text NOT NULL CHECK (estado IN ('en-transito', 'recibido', 'cancelado')),
            cuerpo_huella text NOT NULL CHECK (char_length(cuerpo_huella) BETWEEN 1 AND 128),
            recibido_en timestamptz NOT NULL
        );
        CREATE INDEX IF NOT EXISTS estados_de_traslado_por_fecha ON pedidos.estados_de_traslado_recibidos (recibido_en);

        CREATE TABLE IF NOT EXISTS pedidos.liberaciones_de_contacto (
            pedido_id uuid PRIMARY KEY REFERENCES pedidos.pedidos (id),
            desde timestamptz NOT NULL,
            intentos integer NOT NULL CHECK (intentos >= 0),
            ultimo_intento_en timestamptz
        );

        -- Una sola fila: cuándo terminó con éxito la última depuración (la alerta de «sin corrida en 24 horas», PED-39).
        CREATE TABLE IF NOT EXISTS pedidos.depuraciones_de_pedidos (
            id smallint PRIMARY KEY CHECK (id = 1),
            ultima_exitosa_en timestamptz NOT NULL
        );
        """;

    /// <summary>
    /// Lo que <c>rol_pedidos</c> puede hacer: leer e insertar; actualizar <b>solo las columnas que cambian</b> (el contacto solo para anonimizarlo); borrar únicamente lo que es una cola o una retención (las líneas de una anulación que se recalcula, las liberaciones y los estados de traslado vencidos); sin <c>TRUNCATE</c> ni <c>DROP</c>.
    /// <c>SELECT … FOR UPDATE</c> necesita el privilegio <c>UPDATE</c> sobre la tabla.
    /// </summary>
    public const string Permisos = """
        GRANT USAGE ON SCHEMA pedidos TO rol_pedidos;
        GRANT SELECT, INSERT ON pedidos.pedidos TO rol_pedidos;
        GRANT UPDATE (estado, fecha_estado_final, contacto_nombre, contacto_telefono, contacto_direccion, contacto_anonimizado_en, version) ON pedidos.pedidos TO rol_pedidos;
        GRANT SELECT, INSERT ON pedidos.lineas_de_pedido TO rol_pedidos;
        GRANT UPDATE (estado, fecha_estimada_de_llegada, listo_en, entregada_en, no_recogida_en, anulada_en, envio_id, version) ON pedidos.lineas_de_pedido TO rol_pedidos;
        GRANT SELECT, INSERT ON pedidos.anulaciones TO rol_pedidos;
        GRANT UPDATE (
            incluye_envio, hay_digitales, monto, estado, reembolso_id, envios_cancelados_en, reservas_liberadas_en, reembolso_registrado_en, accesos_revocados_en, iniciada_por, rol_del_iniciador,
            sucursal_del_iniciador, proximo_intento_en, intentos, completada_en
        ) ON pedidos.anulaciones TO rol_pedidos;
        GRANT SELECT, INSERT, UPDATE (en_curso), DELETE ON pedidos.anulacion_lineas TO rol_pedidos;
        GRANT SELECT, INSERT ON pedidos.entregas TO rol_pedidos;
        GRANT UPDATE (estado, iniciada_por, rol_del_iniciador, sucursal_del_iniciador, proximo_intento_en, intentos) ON pedidos.entregas TO rol_pedidos;
        GRANT SELECT, INSERT, UPDATE (cierre_confirmado_en, resultado_del_cierre, en_curso) ON pedidos.entrega_lineas TO rol_pedidos;
        GRANT SELECT, INSERT, UPDATE (intentos), DELETE ON pedidos.liberaciones_de_reserva TO rol_pedidos;
        GRANT SELECT, INSERT, DELETE ON pedidos.estados_de_traslado_recibidos TO rol_pedidos;
        GRANT SELECT, INSERT, UPDATE (intentos, ultimo_intento_en), DELETE ON pedidos.liberaciones_de_contacto TO rol_pedidos;
        GRANT SELECT, INSERT, UPDATE (ultima_exitosa_en) ON pedidos.depuraciones_de_pedidos TO rol_pedidos;
        """;
}
