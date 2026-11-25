#!/bin/sh
# Arranque de PostgreSQL con TLS (AM24). Lo ejecuta el Compose de VM-INVENTARIO como `entrypoint` de la imagen (POSIX sh, usuario «postgres», sin privilegios).
#
# El certificado del servidor y su clave llegan como secretos montados de solo lectura (/run/secrets/tls-cert.pem y tls-key.pem, emitidos por la CA privada de la cuenta
# con scripts/generar-secretos.sh). PostgreSQL exige que la clave sea de quien ejecuta el servidor y no sea legible por grupo ni por otros (0600); un archivo montado
# conserva el dueño y los permisos de la VM y no siempre cumple eso, y el sistema de archivos del contenedor es de solo lectura. Por eso aquí se COPIAN a la memoria
# (tmpfs de /run/postgresql: nunca tocan el disco del contenedor) con esos permisos, y después se entrega el control al arranque normal de la imagen.
#
# Si falta el certificado o la clave NO arranca: sin TLS el `pg_hba.conf` rechazaría igual a todos los clientes de la red, y es mejor un error claro que una base muda.
set -eu

cert="${PG_TLS_CERT_ORIGEN:-/run/secrets/tls-cert.pem}"
clave="${PG_TLS_CLAVE_ORIGEN:-/run/secrets/tls-key.pem}"
destino="${PG_TLS_DESTINO:-/run/postgresql/tls}"

for archivo in "$cert" "$clave"; do
  if [ ! -r "$archivo" ] || [ ! -s "$archivo" ]; then
    echo "postgres: no se puede leer $archivo (certificado o clave TLS del servidor): no se arranca sin TLS." >&2
    exit 1
  fi
done

umask 077
mkdir -p "$destino"
chmod 0700 "$destino"
cp "$cert" "$destino/server.crt"
cp "$clave" "$destino/server.key"
chmod 0600 "$destino/server.crt" "$destino/server.key"

exec docker-entrypoint.sh "$@"
