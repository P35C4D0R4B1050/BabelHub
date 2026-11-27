namespace BabelHub.Ecommerce.Infrastructure.Pagos;

/// <summary>
/// Definición de las tablas del esquema <c>pagos</c> (<c>plan.md</c> §3, T24) con el permiso mínimo de su rol <c>rol_pagos</c>; la migración las incorpora tal cual (como <c>DefinicionDelCarrito</c>): un propietario aparte es dueño de las
/// tablas y la aplicación entra con <c>EjecutarComo(pagos)</c> y solo hace lo que el módulo necesita. <b>Ninguna tabla guarda el token del medio, el número de tarjeta, el correo ni la respuesta cruda de la pasarela</b>; el contacto de
/// entrega vive solo en <c>intenciones</c>, con sus <c>CHECK</c> (solo a domicilio, completo y nulo tras liberarlo) y sin que un error de la base lo repita (<see cref="FallaDeLaBaseDePagosException"/>).
/// </summary>
public static class DefinicionDePagos
{
    public const string Sql = """
        CREATE SCHEMA IF NOT EXISTS pagos;

        -- La intención de pago (id = pedidoId). cuenta_id es nulo solo tras desvincular (PAG-26). El contacto: solo a domicilio, completo y nulo tras liberarlo (PAG-27).
        CREATE TABLE IF NOT EXISTS pagos.intenciones (
            id uuid PRIMARY KEY,
            cuenta_id uuid,
            estado text NOT NULL CHECK (estado IN ('abierta', 'con_cobro', 'aprobada', 'completada', 'compensada', 'cerrada_sin_cobro')),
            motivo_de_cierre text CHECK (motivo_de_cierre IN ('vencida', 'cancelada', 'rechazada_sin_reintento')),
            tiene_impresos boolean NOT NULL,
            tipo_de_entrega text CHECK (tipo_de_entrega IN ('sin_entrega', 'recojo', 'domicilio')),
            codigo_de_sucursal_de_recojo text CHECK (char_length(codigo_de_sucursal_de_recojo) BETWEEN 1 AND 40),
            region_de_entrega text CHECK (char_length(region_de_entrega) BETWEEN 1 AND 60),
            total numeric(12, 2) NOT NULL CHECK (total > 0),
            envio numeric(12, 2) NOT NULL CHECK (envio >= 0),
            moneda text NOT NULL DEFAULT 'PEN' CHECK (moneda = 'PEN'),
            huella_de_instantanea text CHECK (char_length(huella_de_instantanea) BETWEEN 1 AND 200),
            instantanea_obsoleta boolean NOT NULL DEFAULT false,
            iniciada_en timestamptz NOT NULL,
            aprobado_en timestamptz,
            confirmacion_iniciada_en timestamptz,
            reservas_confirmadas_en timestamptz,
            reclamada_hasta timestamptz,
            vence_en timestamptz NOT NULL,
            limite_pendiente_en timestamptz NOT NULL,
            reserva_expira_en timestamptz,
            reserva_extendida boolean NOT NULL DEFAULT false,
            terminos_version text CHECK (char_length(terminos_version) BETWEEN 1 AND 40),
            terminos_aceptado_en timestamptz,
            privacidad_version text CHECK (char_length(privacidad_version) BETWEEN 1 AND 40),
            privacidad_aceptado_en timestamptz,
            reembolso_digital_version text CHECK (char_length(reembolso_digital_version) BETWEEN 1 AND 40),
            reembolso_digital_aceptado_en timestamptz,
            contacto_nombre text CHECK (char_length(contacto_nombre) BETWEEN 1 AND 120),
            contacto_telefono text CHECK (char_length(contacto_telefono) BETWEEN 1 AND 40),
            contacto_direccion text CHECK (char_length(contacto_direccion) BETWEEN 1 AND 300),
            contacto_liberado_en timestamptz,
            desvinculada_en timestamptz,
            creada_en timestamptz NOT NULL,
            cerrada_en timestamptz,
            CONSTRAINT intenciones_cuenta_solo_nula_si_desvinculada CHECK (cuenta_id IS NOT NULL OR desvinculada_en IS NOT NULL),
            CONSTRAINT intenciones_cierre_con_motivo CHECK ((estado = 'cerrada_sin_cobro') = (motivo_de_cierre IS NOT NULL)),
            CONSTRAINT intenciones_contacto_completo CHECK ((contacto_nombre IS NULL) = (contacto_telefono IS NULL) AND (contacto_nombre IS NULL) = (contacto_direccion IS NULL)),
            CONSTRAINT intenciones_contacto_solo_a_domicilio_y_sin_liberar CHECK (contacto_nombre IS NULL OR (tipo_de_entrega = 'domicilio' AND contacto_liberado_en IS NULL)),
            CONSTRAINT intenciones_aceptacion_con_fecha_terminos CHECK ((terminos_version IS NULL) = (terminos_aceptado_en IS NULL)),
            CONSTRAINT intenciones_aceptacion_con_fecha_privacidad CHECK ((privacidad_version IS NULL) = (privacidad_aceptado_en IS NULL)),
            CONSTRAINT intenciones_aceptacion_con_fecha_reembolso CHECK ((reembolso_digital_version IS NULL) = (reembolso_digital_aceptado_en IS NULL))
        );
        -- Venta por sucursal (PAG-53): el envío de cada sucursal tal como llegó con la compra. Repetible sobre una base anterior, cuyas filas quedan nulas.
        ALTER TABLE pagos.intenciones ADD COLUMN IF NOT EXISTS envios_por_sucursal jsonb;
        CREATE INDEX IF NOT EXISTS intenciones_por_cuenta ON pagos.intenciones (cuenta_id) WHERE cuenta_id IS NOT NULL;
        CREATE INDEX IF NOT EXISTS intenciones_abiertas_por_vencimiento ON pagos.intenciones (vence_en) WHERE estado = 'abierta';
        CREATE INDEX IF NOT EXISTS intenciones_aprobadas ON pagos.intenciones (aprobado_en) WHERE estado = 'aprobada';
        CREATE INDEX IF NOT EXISTS intenciones_con_contacto ON pagos.intenciones (creada_en) WHERE contacto_nombre IS NOT NULL;
        CREATE INDEX IF NOT EXISTS intenciones_cerradas_por_depurar ON pagos.intenciones (cerrada_en) WHERE cuenta_id IS NOT NULL AND cerrada_en IS NOT NULL;

        -- Las líneas congeladas de la compra. Al desvincular se anulan edicion_id, titulo e isbn y quedan los importes (PAG-26).
        CREATE TABLE IF NOT EXISTS pagos.intencion_lineas (
            intencion_id uuid NOT NULL REFERENCES pagos.intenciones (id) ON DELETE CASCADE,
            linea_id uuid NOT NULL,
            posicion integer NOT NULL CHECK (posicion >= 0),
            edicion_id uuid,
            es_digital boolean NOT NULL,
            titulo text CHECK (char_length(titulo) BETWEEN 1 AND 200),
            isbn text CHECK (char_length(isbn) BETWEEN 1 AND 40),
            cantidad integer NOT NULL CHECK (cantidad >= 1),
            precio_unitario numeric(12, 2) NOT NULL CHECK (precio_unitario >= 0),
            importe numeric(12, 2) NOT NULL CHECK (importe >= 0),
            PRIMARY KEY (intencion_id, linea_id),
            CONSTRAINT intencion_lineas_digital_una_unidad CHECK (NOT es_digital OR cantidad = 1)
        );

        -- Lo que devolvió reservas.confirmar: se escribe con el paso reservas_confirmadas y se elimina al desvincular.
        CREATE TABLE IF NOT EXISTS pagos.intencion_reserva_lineas (
            intencion_id uuid NOT NULL REFERENCES pagos.intenciones (id) ON DELETE CASCADE,
            linea_id uuid NOT NULL,
            codigo_de_fuente text NOT NULL CHECK (char_length(codigo_de_fuente) BETWEEN 1 AND 40),
            requiere_traslado boolean NOT NULL,
            traslado_id uuid,
            fecha_estimada_de_llegada date,
            PRIMARY KEY (intencion_id, linea_id),
            CONSTRAINT intencion_reserva_lineas_traslado_coherente CHECK (requiere_traslado = (traslado_id IS NOT NULL))
        );

        -- El intento de cobro: su id es la clave de idempotencia y la referencia opaca. Sin token, tarjeta, correo, contacto ni respuesta cruda.
        CREATE TABLE IF NOT EXISTS pagos.intentos (
            id uuid PRIMARY KEY,
            intencion_id uuid NOT NULL REFERENCES pagos.intenciones (id),
            medio text NOT NULL CHECK (medio IN ('yape', 'tarjeta')),
            estado text NOT NULL CHECK (estado IN ('iniciado', 'pendiente_de_confirmacion', 'aprobado', 'rechazado', 'vencido', 'aprobado_sobrante')),
            referencia_pasarela text CHECK (char_length(referencia_pasarela) BETWEEN 1 AND 100),
            consulta_inmediata boolean NOT NULL DEFAULT false,
            motivo_de_rechazo text CHECK (motivo_de_rechazo IN ('tarjeta_rechazada', 'fondos_insuficientes', 'tarjeta_vencida', 'codigo_de_seguridad_invalido', 'yape_rechazado', 'medio_no_permitido', 'autenticacion_requerida', 'otro')),
            importe numeric(12, 2) NOT NULL CHECK (importe > 0),
            iniciado_en timestamptz NOT NULL,
            enviado_en timestamptz,
            resuelto_en timestamptz,
            consultas integer NOT NULL DEFAULT 0 CHECK (consultas >= 0),
            ultima_consulta_en timestamptz,
            CONSTRAINT intentos_rechazo_con_motivo CHECK ((estado = 'rechazado') = (motivo_de_rechazo IS NOT NULL)),
            CONSTRAINT intentos_aprobado_con_referencia CHECK (estado NOT IN ('aprobado', 'aprobado_sobrante') OR referencia_pasarela IS NOT NULL)
        );
        CREATE UNIQUE INDEX IF NOT EXISTS intentos_uno_activo ON pagos.intentos (intencion_id) WHERE estado IN ('iniciado', 'pendiente_de_confirmacion', 'aprobado');
        CREATE UNIQUE INDEX IF NOT EXISTS intentos_referencia_unica ON pagos.intentos (referencia_pasarela) WHERE referencia_pasarela IS NOT NULL;
        CREATE INDEX IF NOT EXISTS intentos_sin_resolver ON pagos.intentos (iniciado_en) WHERE estado IN ('iniciado', 'pendiente_de_confirmacion');
        CREATE INDEX IF NOT EXISTS intentos_resueltos_recientes ON pagos.intentos (resuelto_en) WHERE estado IN ('vencido', 'rechazado');
        CREATE INDEX IF NOT EXISTS intentos_por_intencion ON pagos.intentos (intencion_id);

        -- La compensación de un cobro (clave compuesta: una por cobro). Sin datos personales.
        CREATE TABLE IF NOT EXISTS pagos.compensaciones (
            intencion_id uuid NOT NULL REFERENCES pagos.intenciones (id),
            intento_id uuid NOT NULL REFERENCES pagos.intentos (id),
            variante text NOT NULL CHECK (variante IN ('completa', 'solo_reembolso')),
            motivo text NOT NULL CHECK (motivo IN ('pago_tardio', 'falla_posterior_al_pago', 'intencion_desvinculada', 'cancelacion_de_cuenta_en_curso')),
            iniciada_en timestamptz NOT NULL,
            reservas_liberadas_en timestamptz,
            motivo_de_liberacion text CHECK (motivo_de_liberacion IN ('falla_posterior_al_pago', 'pago_vencido')),
            reembolso_registrado_en timestamptz,
            reembolso_id uuid,
            checkout_cerrado_en timestamptz,
            completada_en timestamptz,
            PRIMARY KEY (intencion_id, intento_id),
            CONSTRAINT compensaciones_reembolso_con_fecha CHECK ((reembolso_id IS NULL) = (reembolso_registrado_en IS NULL))
        );
        CREATE INDEX IF NOT EXISTS compensaciones_sin_terminar ON pagos.compensaciones (iniciada_en) WHERE completada_en IS NULL;
        CREATE INDEX IF NOT EXISTS compensaciones_por_intento ON pagos.compensaciones (intento_id);

        -- Los reembolsos de toda la plataforma (compensación, anulación, cancelación del cliente y devolución). La clave natural es única por origen.
        CREATE TABLE IF NOT EXISTS pagos.reembolsos (
            id uuid PRIMARY KEY,
            origen text NOT NULL CHECK (origen IN ('compensacion', 'anulacion', 'cancelacion_del_cliente', 'devolucion')),
            clave_natural varchar(100) NOT NULL CHECK (char_length(clave_natural) >= 1),
            intencion_id uuid NOT NULL REFERENCES pagos.intenciones (id),
            intento_id uuid NOT NULL REFERENCES pagos.intentos (id),
            monto numeric(12, 2) NOT NULL CHECK (monto > 0),
            envio numeric(12, 2) NOT NULL DEFAULT 0 CHECK (envio >= 0),
            estado text NOT NULL CHECK (estado IN ('pendiente', 'solicitado', 'reembolsado')),
            referencia_pasarela text CHECK (char_length(referencia_pasarela) BETWEEN 1 AND 100),
            intentos integer NOT NULL DEFAULT 0 CHECK (intentos >= 0),
            ultimo_intento_en timestamptz,
            motivo_pendiente text CHECK (motivo_pendiente IN ('sin_respuesta', 'rechazado_por_pasarela', 'no_soportado')),
            creado_en timestamptz NOT NULL,
            resuelto_en timestamptz,
            CONSTRAINT reembolsos_clave_natural UNIQUE (origen, clave_natural),
            CONSTRAINT reembolsos_reembolsado_con_referencia CHECK (estado <> 'reembolsado' OR referencia_pasarela IS NOT NULL)
        );
        CREATE INDEX IF NOT EXISTS reembolsos_por_intencion ON pagos.reembolsos (intencion_id);
        CREATE INDEX IF NOT EXISTS reembolsos_por_intento ON pagos.reembolsos (intento_id);
        CREATE INDEX IF NOT EXISTS reembolsos_pendientes ON pagos.reembolsos (creado_en) WHERE estado IN ('pendiente', 'solicitado');

        CREATE TABLE IF NOT EXISTS pagos.reembolso_lineas (
            reembolso_id uuid NOT NULL REFERENCES pagos.reembolsos (id) ON DELETE CASCADE,
            linea_id uuid NOT NULL,
            monto numeric(12, 2) NOT NULL CHECK (monto > 0),
            PRIMARY KEY (reembolso_id, linea_id)
        );

        -- El aviso del estado del pago al cliente (correo y WhatsApp): sin destinatario ni cuerpo; se purga a los 90 días. El evento es la clave natural pago:<intención>:<intento>:<evento>.
        CREATE TABLE IF NOT EXISTS pagos.avisos_de_pago (
            id uuid PRIMARY KEY,
            intencion_id uuid NOT NULL REFERENCES pagos.intenciones (id),
            intento_id uuid NOT NULL,
            evento text NOT NULL CHECK (evento IN ('pendiente', 'aprobado', 'rechazado', 'vencido', 'reembolso_automatico')),
            estado text NOT NULL CHECK (estado IN ('pendiente', 'enviado', 'omitido')),
            creado_en timestamptz NOT NULL,
            enviado_en timestamptz,
            intentos integer NOT NULL DEFAULT 0 CHECK (intentos >= 0),
            proximo_intento_en timestamptz,
            CONSTRAINT avisos_de_pago_clave UNIQUE (intencion_id, intento_id, evento)
        );
        CREATE INDEX IF NOT EXISTS avisos_de_pago_listos ON pagos.avisos_de_pago (creado_en) WHERE estado = 'pendiente';
        CREATE INDEX IF NOT EXISTS avisos_de_pago_por_fecha ON pagos.avisos_de_pago (creado_en);

        -- El aviso a Carrito: se inserta en la misma transacción de la transición y un trabajo lo envía después de confirmarla; se purga a los 90 días.
        CREATE TABLE IF NOT EXISTS pagos.avisos_a_carrito (
            id uuid PRIMARY KEY,
            intencion_id uuid NOT NULL REFERENCES pagos.intenciones (id),
            intento_id uuid NOT NULL,
            transicion text NOT NULL CHECK (transicion IN ('pendiente', 'aprobado', 'rechazado', 'vencido', 'compensado')),
            estado text NOT NULL CHECK (estado IN ('retenido', 'pendiente', 'enviado', 'omitido')),
            creado_en timestamptz NOT NULL,
            enviado_en timestamptz,
            intentos integer NOT NULL DEFAULT 0 CHECK (intentos >= 0),
            proximo_intento_en timestamptz,
            CONSTRAINT avisos_a_carrito_clave UNIQUE (intencion_id, intento_id, transicion)
        );
        CREATE INDEX IF NOT EXISTS avisos_a_carrito_listos ON pagos.avisos_a_carrito (creado_en) WHERE estado = 'pendiente';
        CREATE INDEX IF NOT EXISTS avisos_a_carrito_por_fecha ON pagos.avisos_a_carrito (creado_en);
        """;

    /// <summary>
    /// Lo que <c>rol_pagos</c> puede hacer: leer e insertar, actualizar <b>solo las columnas que cambian</b> (el identificador, la cuenta de origen del intento y las fechas de creación nunca cambian) y borrar solo lo que el módulo
    /// reemplaza (las líneas al guardar la intención, las líneas confirmadas al desvincular) o purga (los avisos a los 90 días). <c>SELECT … FOR UPDATE</c> necesita el privilegio <c>UPDATE</c> sobre la tabla. Sin <c>TRUNCATE</c> ni <c>DROP</c>.
    /// </summary>
    public const string Permisos = """
        GRANT USAGE ON SCHEMA pagos TO rol_pagos;
        GRANT SELECT, INSERT ON pagos.intenciones TO rol_pagos;
        GRANT UPDATE (
            cuenta_id, estado, motivo_de_cierre, tiene_impresos, tipo_de_entrega, codigo_de_sucursal_de_recojo, region_de_entrega, total, envio, huella_de_instantanea, instantanea_obsoleta, iniciada_en, aprobado_en,
            confirmacion_iniciada_en, reservas_confirmadas_en, reclamada_hasta, vence_en, limite_pendiente_en, reserva_expira_en, reserva_extendida, terminos_version, terminos_aceptado_en, privacidad_version,
            privacidad_aceptado_en, reembolso_digital_version, reembolso_digital_aceptado_en, contacto_nombre, contacto_telefono, contacto_direccion, contacto_liberado_en, desvinculada_en, cerrada_en, envios_por_sucursal
        ) ON pagos.intenciones TO rol_pagos;
        GRANT SELECT, INSERT, DELETE ON pagos.intencion_lineas TO rol_pagos;
        GRANT SELECT, INSERT, DELETE ON pagos.intencion_reserva_lineas TO rol_pagos;
        GRANT SELECT, INSERT ON pagos.intentos TO rol_pagos;
        GRANT UPDATE (estado, referencia_pasarela, consulta_inmediata, motivo_de_rechazo, enviado_en, resuelto_en, consultas, ultima_consulta_en) ON pagos.intentos TO rol_pagos;
        GRANT SELECT, INSERT ON pagos.compensaciones TO rol_pagos;
        GRANT UPDATE (reservas_liberadas_en, motivo_de_liberacion, reembolso_registrado_en, reembolso_id, checkout_cerrado_en, completada_en) ON pagos.compensaciones TO rol_pagos;
        GRANT SELECT, INSERT ON pagos.reembolsos TO rol_pagos;
        GRANT UPDATE (estado, referencia_pasarela, intentos, ultimo_intento_en, motivo_pendiente, resuelto_en) ON pagos.reembolsos TO rol_pagos;
        GRANT SELECT, INSERT ON pagos.reembolso_lineas TO rol_pagos;
        GRANT SELECT, INSERT, DELETE ON pagos.avisos_de_pago TO rol_pagos;
        GRANT UPDATE (estado, enviado_en, intentos, proximo_intento_en) ON pagos.avisos_de_pago TO rol_pagos;
        GRANT SELECT, INSERT, DELETE ON pagos.avisos_a_carrito TO rol_pagos;
        GRANT UPDATE (estado, enviado_en, intentos, proximo_intento_en) ON pagos.avisos_a_carrito TO rol_pagos;
        """;
}
