namespace BabelHub.Ecommerce.Infrastructure.Biblioteca;

/// <summary>
/// Definición de las tablas del esquema <c>biblioteca</c> (<c>plan.md</c> §3, T12) con el permiso mínimo de su rol <c>rol_biblioteca</c>: un propietario aparte es dueño de las tablas y la aplicación entra con <c>EjecutarComo(biblioteca)</c> y solo hace lo que el módulo necesita.
/// <b>Ninguna tabla guarda la URL firmada, la clave de objeto del archivo, un correo, un nombre ni un texto libre</b>: la clave de un solo uso es un resumen SHA-256 y los motivos son de lista cerrada. La cuenta es un identificador opaco sin clave foránea (un módulo no ve el esquema de otro).
/// Los <c>CHECK</c> repiten la coherencia que valida <c>Acceso.Reconstruir</c>.
/// </summary>
public static class DefinicionDeBiblioteca
{
    public const string Sql = """
        CREATE SCHEMA IF NOT EXISTS biblioteca;

        -- Un acceso personal por cuenta y edición (BIB-19) y por compra (lo busca Devoluciones por pedido y edición). El archivo se localiza por la edición con IReferenciaDeArchivosDelCatalogo: aquí no hay clave de objeto ni URL.
        CREATE TABLE IF NOT EXISTS biblioteca.accesos (
            id uuid PRIMARY KEY,
            cuenta_id uuid NOT NULL,
            edicion_id uuid NOT NULL,
            pedido_id uuid NOT NULL,
            estado text NOT NULL CHECK (estado IN ('vigente', 'suspendido', 'revocado')),
            descargas_usadas integer NOT NULL CHECK (descargas_usadas BETWEEN 0 AND 5),
            enlaces_entregados integer NOT NULL CHECK (enlaces_entregados >= 0),
            concedido_en timestamptz NOT NULL,
            reactivado_en timestamptz,
            revocado_en timestamptz,
            motivo_de_revocacion text CHECK (motivo_de_revocacion IN ('anulacion-de-pedido', 'reembolso', 'reporte-aprobado')),
            suspendido_en timestamptz,
            CONSTRAINT accesos_cuenta_edicion_unica UNIQUE (cuenta_id, edicion_id),
            CONSTRAINT accesos_pedido_edicion_unica UNIQUE (pedido_id, edicion_id),
            CONSTRAINT accesos_revocado_con_fecha_y_motivo CHECK (estado <> 'revocado' OR (revocado_en IS NOT NULL AND motivo_de_revocacion IS NOT NULL)),
            CONSTRAINT accesos_sin_revocacion_si_no_esta_revocado CHECK (estado = 'revocado' OR (revocado_en IS NULL AND motivo_de_revocacion IS NULL)),
            CONSTRAINT accesos_suspendido_con_fecha CHECK ((estado = 'suspendido') = (suspendido_en IS NOT NULL))
        );
        CREATE INDEX IF NOT EXISTS accesos_por_cuenta ON biblioteca.accesos (cuenta_id, concedido_en DESC);

        -- Una fila por enlace entregado (BIB-11). La depuración de BIB-26 pone el acceso, el usuario y la hora en nulo y deja la edición y el día; la cascada de BIB-25 solo alcanza las filas con acceso. Sin la URL, la clave ni el origen.
        CREATE TABLE IF NOT EXISTS biblioteca.descargas (
            id uuid PRIMARY KEY,
            acceso_id uuid REFERENCES biblioteca.accesos (id) ON DELETE CASCADE,
            usuario_id uuid,
            edicion_id uuid NOT NULL,
            entregada_en timestamptz,
            dia date NOT NULL,
            CONSTRAINT descargas_acceso_y_usuario_juntos CHECK ((acceso_id IS NULL) = (usuario_id IS NULL)),
            CONSTRAINT descargas_hora_solo_con_acceso CHECK (entregada_en IS NULL OR acceso_id IS NOT NULL)
        );
        CREATE INDEX IF NOT EXISTS descargas_por_dia ON biblioteca.descargas (dia);
        CREATE INDEX IF NOT EXISTS descargas_por_acceso ON biblioteca.descargas (acceso_id);

        -- Lo que permite reconstruir el mismo enlace de una repetición sin guardarlo (D02): el resumen de la clave, el instante de firma y el de vencimiento. Se purga desde una hora después de vencer.
        CREATE TABLE IF NOT EXISTS biblioteca.claves_de_descarga (
            acceso_id uuid NOT NULL REFERENCES biblioteca.accesos (id) ON DELETE CASCADE,
            clave_hash bytea NOT NULL CHECK (octet_length(clave_hash) = 32),
            firmado_en timestamptz NOT NULL,
            vence_en timestamptz NOT NULL,
            descarga_id uuid NOT NULL,
            PRIMARY KEY (acceso_id, clave_hash),
            CONSTRAINT claves_de_descarga_vence_despues CHECK (vence_en > firmado_en AND extract(epoch FROM (vence_en - firmado_en)) <= 300)
        );
        CREATE INDEX IF NOT EXISTS claves_de_descarga_por_vencimiento ON biblioteca.claves_de_descarga (vence_en);

        -- Una ayuda abierta por acceso como máximo; motivo de lista cerrada, sin texto libre (D06).
        CREATE TABLE IF NOT EXISTS biblioteca.solicitudes_de_ayuda (
            id uuid PRIMARY KEY,
            acceso_id uuid NOT NULL REFERENCES biblioteca.accesos (id) ON DELETE CASCADE,
            motivo text NOT NULL CHECK (motivo IN ('archivo-no-abre', 'archivo-no-disponible', 'descargas-agotadas', 'otro')),
            estado text NOT NULL CHECK (estado IN ('abierta', 'cerrada')),
            creada_en timestamptz NOT NULL,
            cerrada_en timestamptz,
            CONSTRAINT solicitudes_de_ayuda_cierre_coherente CHECK ((estado = 'cerrada') = (cerrada_en IS NOT NULL))
        );
        CREATE UNIQUE INDEX IF NOT EXISTS solicitudes_de_ayuda_una_abierta_por_acceso ON biblioteca.solicitudes_de_ayuda (acceso_id) WHERE estado = 'abierta';
        CREATE INDEX IF NOT EXISTS solicitudes_de_ayuda_por_creada ON biblioteca.solicitudes_de_ayuda (creada_en);
        """;

    /// <summary>
    /// Lo que <c>rol_biblioteca</c> puede hacer: leer e insertar; actualizar <b>solo las columnas que cambian</b> (la cuenta, la edición y la fecha de concesión nunca cambian); eliminar los accesos (la cancelación de la cuenta, BIB-25), las claves vencidas y las ayudas de dos años; sin
    /// <c>TRUNCATE</c> ni <c>DROP</c>. El registro de descargas no se elimina nunca directamente: solo en cascada con su acceso (BIB-25) y se depura con <c>UPDATE</c> (BIB-26).
    /// </summary>
    public const string Permisos = """
        GRANT USAGE ON SCHEMA biblioteca TO rol_biblioteca;
        GRANT SELECT, INSERT, DELETE ON biblioteca.accesos TO rol_biblioteca;
        GRANT UPDATE (pedido_id, estado, descargas_usadas, enlaces_entregados, reactivado_en, revocado_en, motivo_de_revocacion, suspendido_en) ON biblioteca.accesos TO rol_biblioteca;
        GRANT SELECT, INSERT ON biblioteca.descargas TO rol_biblioteca;
        GRANT UPDATE (acceso_id, usuario_id, entregada_en) ON biblioteca.descargas TO rol_biblioteca;
        GRANT SELECT, INSERT, DELETE ON biblioteca.claves_de_descarga TO rol_biblioteca;
        GRANT UPDATE (firmado_en, vence_en, descarga_id) ON biblioteca.claves_de_descarga TO rol_biblioteca;
        GRANT SELECT, INSERT, DELETE ON biblioteca.solicitudes_de_ayuda TO rol_biblioteca;
        GRANT UPDATE (estado, cerrada_en) ON biblioteca.solicitudes_de_ayuda TO rol_biblioteca;
        """;
}
