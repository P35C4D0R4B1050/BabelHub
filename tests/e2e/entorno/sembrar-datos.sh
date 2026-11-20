#!/usr/bin/env bash
# Siembra el catálogo (obras y ediciones) y el inventario (sucursales, ediciones y stock) del entorno E2E LOCAL con datos FICTICIOS (AGENTS.md §5: ningún dato real) y la
# fila de las versiones vigentes de los textos legales que el sembrador de sesiones necesita. Es idempotente (ON CONFLICT DO NOTHING) y solo para desarrollo: escribe en las bases
# del proyecto aislado `bhe2e` con la cuenta propietaria del entorno, nunca con la de la aplicación.
# Uso (tras preparar-entorno.sh): bash tests/e2e/entorno/sembrar-datos.sh [--limpiar]
set -euo pipefail
cd "$(dirname "$0")/../../.."

archivo="${E2E_ENV_FILE:-${LOCALAPPDATA:-$HOME}/babelhub-e2e/env}"
set -a; source "$archivo"; set +a

psql_en() {  # psql_en <base>: lee el SQL de la entrada estándar
  docker compose -p bhe2e -f infra/docker-compose.local.yml --env-file "$archivo" exec -T postgres psql -v ON_ERROR_STOP=1 -q -U "$POSTGRES_ADMIN_USER" -d "$1"
}

# El identificador de una sucursal es el SHA-256 de «babelhub:sucursal:<código>» (16 primeros bytes, como un Guid .NET): D20 de docs/03-diseno-de-software/08.
FUNCION_GUID="
CREATE OR REPLACE FUNCTION pg_temp.guid_de_sucursal(codigo text) RETURNS uuid LANGUAGE sql AS \$\$
  SELECT (substr(h, 7, 2) || substr(h, 5, 2) || substr(h, 3, 2) || substr(h, 1, 2) || '-' || substr(h, 11, 2) || substr(h, 9, 2) || '-' || substr(h, 15, 2) || substr(h, 13, 2) || '-' || substr(h, 17, 4) || '-' || substr(h, 21, 12))::uuid
  FROM (SELECT encode(substring(sha256(convert_to('babelhub:sucursal:' || codigo, 'UTF8')) FROM 1 FOR 16), 'hex') AS h) t;
\$\$;"

# --limpiar: antes de sembrar, vacía lo transaccional del entorno E2E (carritos, checkouts, pagos, pedidos y las reservas del Inventario) para que cada corrida parta de un stock conocido.
if [ "${1:-}" = "--limpiar" ]; then
  psql_en ecommerce <<'SQL'
TRUNCATE carrito.avisos_de_carrito, carrito.fusiones_aplicadas, carrito.lineas_de_carrito, carrito.lineas_del_checkout, carrito.checkouts, carrito.carritos CASCADE;
TRUNCATE pagos.avisos_a_carrito, pagos.avisos_de_pago, pagos.compensaciones, pagos.intencion_lineas, pagos.intencion_reserva_lineas, pagos.intenciones, pagos.intentos, pagos.reembolso_lineas, pagos.reembolsos CASCADE;
TRUNCATE pedidos.pedidos CASCADE;
TRUNCATE auditoria.acumulado_de_limites;
SQL
  psql_en inventario <<'SQL'
TRUNCATE reservas.reserva_lineas, reservas.reservas, reservas.operaciones_idempotentes CASCADE;
UPDATE fuentes.stock SET reservado = 0;
TRUNCATE auditoria.acumulado_de_limites, plataforma.eventos_limite;
SQL
  echo "estado transaccional del entorno E2E reiniciado"
fi

# ---------- E-commerce: textos legales vigentes y catálogo ----------
psql_en ecommerce <<'SQL'
BEGIN;
INSERT INTO identidad.versiones_de_textos (id, terminos, privacidad, reembolso) VALUES (1, 'e2e-2026-11', 'e2e-2026-11', 'e2e-2026-11') ON CONFLICT DO NOTHING;

-- Obras ficticias (autores, editoriales e ISBN inventados; la portada es una clave sin archivo: el almacén local no existe).
INSERT INTO catalogo.obras (id, titulo, autor, editorial, categoria, portada_clave, portada_huella, portada_tipo, estado, version, creada_en) VALUES
  ('a0000000-0000-0000-0000-000000000001', 'El zorro y el cóndor del Ande', 'Rosa Quispe Ficticia', 'Ediciones Andinas de Prueba', 'Narrativa', 'e2e00000000000000000000000000001', decode(repeat('01', 32), 'hex'), 'jpeg', 'activa', 1, now()),
  ('a0000000-0000-0000-0000-000000000002', 'Cuentos del río Pampas', 'Luis Huamán Ficticio', 'Ediciones Andinas de Prueba', 'Narrativa', 'e2e00000000000000000000000000002', decode(repeat('02', 32), 'hex'), 'jpeg', 'activa', 1, now()),
  ('a0000000-0000-0000-0000-000000000003', 'Manual de quechua ayacuchano', 'Ana Cárdenas Ficticia', 'Ediciones Andinas de Prueba', 'Educación', 'e2e00000000000000000000000000003', decode(repeat('03', 32), 'hex'), 'jpeg', 'activa', 1, now())
ON CONFLICT DO NOTHING;

INSERT INTO catalogo.ediciones (id, obra_id, tipo, isbn13, isbn_ingresado, precio, estado) VALUES
  ('b0000000-0000-0000-0000-000000000011', 'a0000000-0000-0000-0000-000000000001', 'impresa', '9786123450014', '9786123450014', 45.00, 'activa'),
  ('b0000000-0000-0000-0000-000000000012', 'a0000000-0000-0000-0000-000000000001', 'digital', '9786123450021', '9786123450021', 18.00, 'activa'),
  ('b0000000-0000-0000-0000-000000000021', 'a0000000-0000-0000-0000-000000000002', 'impresa', '9786123450038', '9786123450038', 38.50, 'activa'),
  ('b0000000-0000-0000-0000-000000000022', 'a0000000-0000-0000-0000-000000000002', 'digital', '9786123450045', '9786123450045', 15.00, 'activa'),
  ('b0000000-0000-0000-0000-000000000031', 'a0000000-0000-0000-0000-000000000003', 'impresa', '9786123450052', '9786123450052', 52.00, 'activa')
ON CONFLICT DO NOTHING;

-- Una edición digital exige su licencia y la referencia de su archivo (el archivo no existe: el almacén local es inválido a propósito; la compra no lo descarga).
INSERT INTO catalogo.licencias (edicion_id, tipo, titular, territorios, vigente_hasta, formato) VALUES
  ('b0000000-0000-0000-0000-000000000012', 'dominio_publico', 'Titular de prueba', ARRAY['PE'], NULL, 'epub'),
  ('b0000000-0000-0000-0000-000000000022', 'dominio_publico', 'Titular de prueba', ARRAY['PE'], NULL, 'epub')
ON CONFLICT DO NOTHING;
INSERT INTO catalogo.archivos_digitales (edicion_id, clave_en_almacen, tipo_detectado, tamano, huella_sha256, subido_en, subido_por) VALUES
  ('b0000000-0000-0000-0000-000000000012', 'f2e00000000000000000000000000012', 'epub', 1024, decode(repeat('12', 32), 'hex'), now(), '00000000-0000-0000-0000-000000000000'),
  ('b0000000-0000-0000-0000-000000000022', 'f2e00000000000000000000000000022', 'epub', 1024, decode(repeat('22', 32), 'hex'), now(), '00000000-0000-0000-0000-000000000000')
ON CONFLICT DO NOTHING;
COMMIT;
SQL

# ---------- Inventario: sucursales, ediciones impresas y stock ----------
{
  echo "$FUNCION_GUID"
  cat <<'SQL'
INSERT INTO fuentes.fuentes (id, codigo, nombre, region, contacto_aviso, es_recepcion) VALUES
  (pg_temp.guid_de_sucursal('lima'),      'lima',      'Sucursal Lima (prueba)',      'lima',      NULL, true),
  (pg_temp.guid_de_sucursal('ayacucho'),  'ayacucho',  'Sucursal Ayacucho (prueba)',  'ayacucho',  NULL, false),
  (pg_temp.guid_de_sucursal('cusco'),     'cusco',     'Sucursal Cusco (prueba)',     'cusco',     NULL, false)
ON CONFLICT DO NOTHING;

INSERT INTO fuentes.ediciones (edicion_id, formato) VALUES
  ('b0000000-0000-0000-0000-000000000011', 'impreso'),
  ('b0000000-0000-0000-0000-000000000021', 'impreso'),
  ('b0000000-0000-0000-0000-000000000031', 'impreso')
ON CONFLICT DO NOTHING;

-- El zorro y el cóndor: stock en Ayacucho y en Lima; Cuentos del río Pampas: solo en Lima (se trae); el manual: agotado en toda la red.
INSERT INTO fuentes.stock (fuente_id, edicion_id, formato, fisico, reservado, version) VALUES
  (pg_temp.guid_de_sucursal('ayacucho'), 'b0000000-0000-0000-0000-000000000011', 'impreso', 8, 0, 0),
  (pg_temp.guid_de_sucursal('lima'),     'b0000000-0000-0000-0000-000000000011', 'impreso', 12, 0, 0),
  (pg_temp.guid_de_sucursal('lima'),     'b0000000-0000-0000-0000-000000000021', 'impreso', 6, 0, 0),
  (pg_temp.guid_de_sucursal('ayacucho'), 'b0000000-0000-0000-0000-000000000031', 'impreso', 0, 0, 0)
ON CONFLICT DO NOTHING;

-- Plazos de traslado entre sucursales (en días).
INSERT INTO fuentes.rutas (origen_id, destino_id, plazo_dias) VALUES
  (pg_temp.guid_de_sucursal('lima'),     pg_temp.guid_de_sucursal('ayacucho'), 3),
  (pg_temp.guid_de_sucursal('ayacucho'), pg_temp.guid_de_sucursal('lima'),     3),
  (pg_temp.guid_de_sucursal('lima'),     pg_temp.guid_de_sucursal('cusco'),    2),
  (pg_temp.guid_de_sucursal('cusco'),    pg_temp.guid_de_sucursal('lima'),     2)
ON CONFLICT DO NOTHING;
SQL
} | psql_en inventario
echo "datos ficticios sembrados: 3 obras, 5 ediciones, 3 sucursales y su stock"
