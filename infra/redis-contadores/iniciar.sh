#!/bin/sh
# Genera el archivo ACL con la contraseña del entorno y arranca Redis; con REDIS_TLS=1 solo escucha por TLS.
set -eu
: "${BABELHUB__Seguridad__ClaveDeRedisDeLimites:?falta BABELHUB__Seguridad__ClaveDeRedisDeLimites}"
case "${#BABELHUB__Seguridad__ClaveDeRedisDeLimites}" in
  [0-9]|1[0-9]|2[0-9]|3[0-1]) echo "La contraseña del Redis de contadores debe tener al menos 32 caracteres." >&2; exit 1 ;;
esac
sed "s|__CLAVE_LIMITES__|${BABELHUB__Seguridad__ClaveDeRedisDeLimites}|" /etc/redis-contadores/users.acl.plantilla > /tmp/users.acl
chmod 600 /tmp/users.acl
if [ "${REDIS_TLS:-0}" = "1" ]; then
  exec redis-server /etc/redis-contadores/redis.conf --include /etc/redis-contadores/redis.tls.conf
fi
exec redis-server /etc/redis-contadores/redis.conf
