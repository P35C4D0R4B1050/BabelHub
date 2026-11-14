
namespace BabelHub.Inventario.Infrastructure.Fuentes;

/// <summary>
/// La migración del esquema <c>fuentes</c> del Inventario (<c>inventario-fuentes</c> §3, T17), hacia adelante y repetible. La corre la cuenta de despliegue; el propietario de
/// las tablas y la contraseña de los roles con inicio de sesión los pone el aprovisionamiento aparte, como con el resto de las definiciones de la Infraestructura. Define el
/// rol <c>rol_fuentes</c> (sin inicio de sesión ni propiedad de las tablas: es el único con permiso de escritura sobre <c>stock</c>, regla 13) y las tablas con las restricciones que
/// son la <b>última defensa</b> de las reglas del Dominio:
/// <list type="bullet">
/// <item><c>fuentes</c>: código y nombre únicos y un <b>índice único parcial</b> que impide dos fuentes de recepción (FUE-19).</item>
/// <item><c>ediciones</c>: la ficha (solo el formato) con <c>(edicion_id, formato)</c> único, destino de la clave compuesta de <c>stock</c>.</item>
/// <item><c>stock</c>: <c>CHECK (formato = 'impreso')</c> y la clave compuesta hacia <c>ediciones</c> (una digital nunca tiene stock, regla 12) y
/// <c>CHECK (físico ≥ 0 ∧ reservado ≥ 0 ∧ reservado ≤ físico)</c> (FUE-04).</item>
/// <item><c>rutas</c>: claves foráneas y <c>CHECK (plazo &gt; 0 ∧ origen ≠ destino)</c> (FUE-17).</item>
/// </list>
/// <c>stock</c> lleva además <b>seguridad por filas</b> (<c>ENABLE</c> y <c>FORCE</c>): una política permite la fila solo si el alcance de datos de la transacción
/// (<c>babelhub.alcance</c>, que fija <c>UnidadDeTrabajoPostgres</c> según el actor) es <c>red</c> o es la sucursal de la fila; <b>sin valor deniega</b>, así que una transacción
/// sin actor devuelve cero filas sin error y por eso la Aplicación y la Infraestructura exigen un actor antes de tocar el stock (hallazgo del 2026-11-14).
/// </summary>
public static class DefinicionDeFuentes
{
    /// <summary>El nombre del esquema del módulo; el rol del esquema es <c>rol_fuentes</c> (convención de <c>OpcionesDeUnidadDeTrabajo</c>).</summary>
    public const string Esquema = "fuentes";

    /// <summary>El rol sin inicio de sesión del módulo (idempotente).</summary>
    public const string Rol = """
        DO $rol$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rol_fuentes') THEN
                CREATE ROLE rol_fuentes NOLOGIN;
            END IF;
        END
        $rol$;
        """;

    /// <summary>
    /// Esquema, tablas, restricciones, índices y política de filas (idempotente). El parámetro <c>babelhub.alcance</c> y el valor <c>red</c> van como literales (la Infraestructura de un módulo no depende de la de
    /// <c>Plataforma</c>, regla 13); una prueba comprueba que coinciden con <c>AlcanceDeDatosDelActor</c>.
    /// </summary>
    public const string Sql = """
        CREATE SCHEMA IF NOT EXISTS fuentes;

        CREATE TABLE IF NOT EXISTS fuentes.fuentes (
            id uuid PRIMARY KEY,
            codigo text NOT NULL,
            nombre text NOT NULL,
            region text NOT NULL,
            contacto_aviso text NULL,
            es_recepcion boolean NOT NULL DEFAULT false,
            CONSTRAINT uq_fuentes_codigo UNIQUE (codigo),
            CONSTRAINT uq_fuentes_nombre UNIQUE (nombre),
            CONSTRAINT ck_fuentes_codigo CHECK (codigo ~ '^[a-z0-9-]{2,20}$')
        );

        -- Exactamente una fuente de recepción (FUE-19): un índice único parcial sobre la marca.
        CREATE UNIQUE INDEX IF NOT EXISTS ux_fuentes_una_recepcion ON fuentes.fuentes (es_recepcion) WHERE es_recepcion;

        CREATE TABLE IF NOT EXISTS fuentes.ediciones (
            edicion_id uuid PRIMARY KEY,
            formato text NOT NULL,
            CONSTRAINT ck_ediciones_formato CHECK (formato IN ('impreso', 'digital')),
            CONSTRAINT uq_ediciones_id_formato UNIQUE (edicion_id, formato)
        );

        CREATE TABLE IF NOT EXISTS fuentes.stock (
            fuente_id uuid NOT NULL REFERENCES fuentes.fuentes (id),
            edicion_id uuid NOT NULL,
            formato text NOT NULL DEFAULT 'impreso',
            fisico integer NOT NULL,
            reservado integer NOT NULL,
            version bigint NOT NULL DEFAULT 0,
            CONSTRAINT pk_stock PRIMARY KEY (fuente_id, edicion_id),
            CONSTRAINT ck_stock_solo_impresas CHECK (formato = 'impreso'),
            CONSTRAINT fk_stock_edicion_impresa FOREIGN KEY (edicion_id, formato) REFERENCES fuentes.ediciones (edicion_id, formato),
            CONSTRAINT ck_stock_cantidades CHECK (fisico >= 0 AND reservado >= 0 AND reservado <= fisico)
        );

        CREATE INDEX IF NOT EXISTS ix_stock_edicion ON fuentes.stock (edicion_id);

        CREATE TABLE IF NOT EXISTS fuentes.rutas (
            origen_id uuid NOT NULL REFERENCES fuentes.fuentes (id),
            destino_id uuid NOT NULL REFERENCES fuentes.fuentes (id),
            plazo_dias integer NOT NULL,
            actualizada_en timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT pk_rutas PRIMARY KEY (origen_id, destino_id),
            CONSTRAINT ck_rutas_plazo_y_extremos CHECK (plazo_dias > 0 AND origen_id <> destino_id)
        );

        -- Seguridad por filas (D09, AD12, AD31): el alcance de datos de la transacción es «red» o la sucursal de la fila; sin valor, deniega.
        ALTER TABLE fuentes.stock ENABLE ROW LEVEL SECURITY;
        ALTER TABLE fuentes.stock FORCE ROW LEVEL SECURITY;
        DROP POLICY IF EXISTS por_alcance ON fuentes.stock;
        CREATE POLICY por_alcance ON fuentes.stock FOR ALL
            USING (
                current_setting('babelhub.alcance', true) = 'red'
                OR current_setting('babelhub.alcance', true) = fuente_id::text)
            WITH CHECK (
                current_setting('babelhub.alcance', true) = 'red'
                OR current_setting('babelhub.alcance', true) = fuente_id::text);
        """;

    /// <summary>
    /// El permiso mínimo de <c>rol_fuentes</c>: uso del esquema y lectura, alta y cambio de cada tabla. <b>Ninguna baja</b>: no hay desactivación de fuentes, las rutas se cambian y
    /// el stock se mueve con <c>UPDATE</c>. Se aplica después de que el aprovisionamiento deja las tablas a su propietario.
    /// </summary>
    public const string Permisos = """
        GRANT USAGE ON SCHEMA fuentes TO rol_fuentes;
        GRANT SELECT, INSERT, UPDATE ON fuentes.fuentes, fuentes.ediciones, fuentes.stock, fuentes.rutas TO rol_fuentes;
        """;
}
