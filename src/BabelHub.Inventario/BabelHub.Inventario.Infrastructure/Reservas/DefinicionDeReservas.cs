namespace BabelHub.Inventario.Infrastructure.Reservas;

/// <summary>
/// La migración del esquema <c>reservas</c> del Inventario (<c>inventario-seleccion-fuente</c> §3, T08), hacia adelante y repetible. La corre la cuenta de despliegue; el propietario de las tablas y la contraseña del
/// rol con inicio de sesión los pone el aprovisionamiento aparte, como con <c>fuentes</c>. Define el rol <c>rol_reservas</c> (sin inicio de sesión ni propiedad de las tablas) y las tablas con las restricciones que son la
/// <b>última defensa</b> de las reglas del Dominio:
/// <list type="bullet">
/// <item><c>reservas</c> (la cabecera de una solicitud): <b>único <c>(cliente_id, clave_idempotencia)</c></b> (SEL-09, SEL-31: el mismo cliente no repite la clave y otro cliente puede usar la misma), coherencia entre el tipo de
/// entrega y la sucursal o la región, y los reemplazos entre 0 y el tope de 3 (SEL-36).</item>
/// <item><c>reserva_lineas</c>: <c>CHECK (cantidad &gt; 0)</c>, estados y causas de cierre cerrados y coherentes entre sí, y un <b>índice único parcial <c>(pedido_id, linea_id)</c> donde la línea está pendiente o
/// confirmada</b>: una línea de pedido tiene una sola línea activa (SEL-16) aunque dos transacciones lleguen a la vez. El <c>pedido_id</c> de la línea debe ser el de su cabecera (clave compuesta).</item>
/// <item><c>operaciones_idempotentes</c>: clave primaria <c>(pedido_id, tipo, clave)</c>: la clave de confirmar o cerrar se acota por pedido y operación (ECO-0 D06).</item>
/// <item><c>bajas_recibidas</c>: los titulares dados de baja que <c>Auditoria</c> aún no consumió (SEL-34).</item>
/// </list>
/// Ninguna tabla lleva clave foránea hacia <c>fuentes</c>: un módulo no depende del esquema de otro (regla 13); la existencia de la fuente y de la sucursal la comprueba la Aplicación por el contrato de <c>Fuentes</c>.
/// Tampoco hay seguridad por filas: las reservas no se filtran por sucursal, sus consumidores son procesos del sistema y la Aplicación comprueba el alcance de cada operación; el acceso lo cierra <c>rol_reservas</c>.
/// </summary>
public static class DefinicionDeReservas
{
    /// <summary>El nombre del esquema del módulo; el rol del esquema es <c>rol_reservas</c> (convención de <c>OpcionesDeUnidadDeTrabajo</c>).</summary>
    public const string Esquema = "reservas";

    /// <summary>El rol sin inicio de sesión del módulo (idempotente).</summary>
    public const string Rol = """
        DO $rol$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rol_reservas') THEN
                CREATE ROLE rol_reservas NOLOGIN;
            END IF;
        END
        $rol$;
        """;

    /// <summary>Esquema, tablas, restricciones e índices (idempotente).</summary>
    public const string Sql = """
        CREATE SCHEMA IF NOT EXISTS reservas;

        CREATE TABLE IF NOT EXISTS reservas.reservas (
            id uuid PRIMARY KEY,
            pedido_id uuid NOT NULL,
            cliente_id uuid NOT NULL,
            huella_origen text NULL,
            clave_idempotencia text NOT NULL,
            huella_de_carga text NOT NULL,
            tipo_entrega text NOT NULL,
            sucursal_recojo_id uuid NULL,
            region_entrega text NULL,
            iniciada_en timestamptz NOT NULL,
            fecha_expiracion timestamptz NULL,
            extendida boolean NOT NULL DEFAULT false,
            reemplazos integer NOT NULL DEFAULT 0,
            creada_en timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT uq_reservas_cliente_clave UNIQUE (cliente_id, clave_idempotencia),
            CONSTRAINT uq_reservas_id_pedido UNIQUE (id, pedido_id),
            CONSTRAINT ck_reservas_tipo_entrega CHECK (tipo_entrega IN ('recojo', 'domicilio')),
            CONSTRAINT ck_reservas_destino CHECK (
                (tipo_entrega = 'recojo' AND sucursal_recojo_id IS NOT NULL AND region_entrega IS NULL)
                OR (tipo_entrega = 'domicilio' AND region_entrega IS NOT NULL AND sucursal_recojo_id IS NULL)),
            CONSTRAINT ck_reservas_reemplazos CHECK (reemplazos BETWEEN 0 AND 3),
            CONSTRAINT ck_reservas_huella_de_carga CHECK (huella_de_carga ~ '^[0-9a-f]{64}$')
        );

        CREATE INDEX IF NOT EXISTS ix_reservas_pedido ON reservas.reservas (pedido_id, iniciada_en DESC);
        CREATE INDEX IF NOT EXISTS ix_reservas_cliente ON reservas.reservas (cliente_id);
        CREATE INDEX IF NOT EXISTS ix_reservas_vencimiento ON reservas.reservas (fecha_expiracion) WHERE fecha_expiracion IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ix_reservas_huella_de_origen ON reservas.reservas (iniciada_en) WHERE huella_origen IS NOT NULL;

        CREATE TABLE IF NOT EXISTS reservas.reserva_lineas (
            id uuid PRIMARY KEY,
            linea_id uuid NOT NULL,
            reserva_id uuid NOT NULL,
            pedido_id uuid NOT NULL,
            edicion_id uuid NOT NULL,
            fuente_id uuid NOT NULL,
            cantidad integer NOT NULL,
            estado text NOT NULL,
            cerrada_por text NULL,
            traslado_id uuid NULL,
            creada_por_traslado boolean NOT NULL DEFAULT false,
            recreada_en_destino boolean NOT NULL DEFAULT false,
            confirmada_en timestamptz NULL,
            anulada_en timestamptz NULL,
            estado_cambiado_en timestamptz NOT NULL,
            CONSTRAINT fk_reserva_lineas_reserva FOREIGN KEY (reserva_id, pedido_id) REFERENCES reservas.reservas (id, pedido_id),
            CONSTRAINT ck_reserva_lineas_cantidad CHECK (cantidad > 0),
            CONSTRAINT ck_reserva_lineas_estado CHECK (estado IN ('pendiente', 'confirmada', 'cerrada', 'liberada', 'expirada')),
            CONSTRAINT ck_reserva_lineas_cerrada_por CHECK (cerrada_por IS NULL OR cerrada_por IN ('entrega', 'reparto', 'traslado')),
            CONSTRAINT ck_reserva_lineas_cierre CHECK ((estado = 'cerrada') = (cerrada_por IS NOT NULL)),
            CONSTRAINT ck_reserva_lineas_traslado CHECK ((recreada_en_destino = false AND anulada_en IS NULL) OR cerrada_por = 'traslado')
        );

        -- Una línea de pedido tiene una sola línea de reserva activa (pendiente o confirmada): aunque dos transacciones lleguen a la vez, la segunda no puede insertar otra (SEL-16).
        CREATE UNIQUE INDEX IF NOT EXISTS ux_reserva_lineas_activa ON reservas.reserva_lineas (pedido_id, linea_id) WHERE estado IN ('pendiente', 'confirmada');
        CREATE INDEX IF NOT EXISTS ix_reserva_lineas_stock ON reservas.reserva_lineas (fuente_id, edicion_id, estado);
        CREATE INDEX IF NOT EXISTS ix_reserva_lineas_reserva ON reservas.reserva_lineas (reserva_id);
        CREATE INDEX IF NOT EXISTS ix_reserva_lineas_pedido ON reservas.reserva_lineas (pedido_id);
        CREATE INDEX IF NOT EXISTS ix_reserva_lineas_traslado ON reservas.reserva_lineas (traslado_id) WHERE traslado_id IS NOT NULL;

        CREATE TABLE IF NOT EXISTS reservas.operaciones_idempotentes (
            pedido_id uuid NOT NULL,
            tipo text NOT NULL,
            clave text NOT NULL,
            huella_de_carga text NOT NULL,
            resultado jsonb NULL,
            creada_en timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT pk_operaciones_idempotentes PRIMARY KEY (pedido_id, tipo, clave),
            CONSTRAINT ck_operaciones_idempotentes_tipo CHECK (tipo IN ('confirmar', 'cerrar'))
        );

        CREATE INDEX IF NOT EXISTS ix_operaciones_idempotentes_creada ON reservas.operaciones_idempotentes (creada_en);

        CREATE TABLE IF NOT EXISTS reservas.bajas_recibidas (
            identificador_opaco uuid PRIMARY KEY,
            recibida_en timestamptz NOT NULL,
            entregada_a_auditoria_en timestamptz NULL
        );

        CREATE INDEX IF NOT EXISTS ix_bajas_recibidas_entregada ON reservas.bajas_recibidas (entregada_a_auditoria_en) WHERE entregada_a_auditoria_en IS NOT NULL;
        """;

    /// <summary>
    /// El permiso mínimo de <c>rol_reservas</c>: uso del esquema; lectura, alta y cambio de reservas y líneas (<b>ninguna baja</b>: las reservas se anonimizan, no se borran); y lectura, alta y baja de las operaciones
    /// idempotentes y de las bajas recibidas, que se purgan. Se aplica después de que el aprovisionamiento deja las tablas a su propietario.
    /// </summary>
    public const string Permisos = """
        GRANT USAGE ON SCHEMA reservas TO rol_reservas;
        GRANT SELECT, INSERT, UPDATE ON reservas.reservas, reservas.reserva_lineas TO rol_reservas;
        GRANT SELECT, INSERT, DELETE ON reservas.operaciones_idempotentes TO rol_reservas;
        GRANT SELECT, INSERT, UPDATE, DELETE ON reservas.bajas_recibidas TO rol_reservas;
        """;
}
