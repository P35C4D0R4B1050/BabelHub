#!/bin/sh
# Vigila el espacio local reservado del archivado de cambios (PLA-21, AC-PLA-21; D11). Si el almacén de objetos no está disponible, la base
# sigue operando (DA03) y lo que no se pudo subir se acumula en la VM: los segmentos de WAL que esperan su archivado (`pg_wal` con
# `archive_status/*.ready`, que PostgreSQL conserva) y lo cifrado que espera su subida (`BABELHUB_ESPERA`). Ese espacio está reservado y es
# menor de 5 GB (por defecto 4 GiB, BABELHUB_ESPACIO_RESERVADO en bytes): lo que sume se mide contra esa reserva.
#
#   vigilar-espacio.sh        (cada minuto, desde el cron del anfitrión, envuelto por alertar.sh: `docker exec <contenedor> …`)
#
#   - menos del 80 % de la reserva: `ok …` y código 0;
#   - del 80 % en adelante: `ALERTA espacio reservado al NN %…` y código 1, para que la persona responsable actúe antes de que se agote;
#   - al agotarse (100 %): se DECLARA ROTO el archivo de cambios: se escribe la marca `archivo-roto` en el volumen reservado, `archivar-wal.sh`
#     pasa a descartar los segmentos (PostgreSQL los recicla y el disco no se llena: la base sigue operando) y se avisa, porque desde ese
#     punto no hay recuperación a un instante intermedio hasta que exista un respaldo completo nuevo (el RPO pasa de 15 minutos a 24 horas);
#   - con la marca puesta, cada corrida repite la alerta (con el instante de la declaración) hasta que `respaldar.sh` suba un respaldo
#     completo nuevo, que es lo único que retira la marca: eso es «exigir un respaldo completo nuevo».
# El aviso por correo no lo manda este script (el contenedor no tiene cliente de correo): lo manda el envoltorio del anfitrión con su propia
# credencial mínima (ver infra/respaldos/README.md). Nunca imprime secretos: solo cantidades y fechas.
#
# Variables: BABELHUB_ESPACIO_RESERVADO (bytes; por defecto 4294967296, debe ser menor de 5000000000), BABELHUB_ESPERA, BABELHUB_PGWAL
# (por defecto $PGDATA/pg_wal). Códigos: 0 por debajo del aviso, 1 alerta o archivo roto, 2 configuración inválida.
set -eu
. /opt/babelhub/comun.sh

PGDATA="${PGDATA:-/var/lib/postgresql/data}"
PGWAL="${BABELHUB_PGWAL:-$PGDATA/pg_wal}"
AVISO_PORCENTAJE=80

verificar_reserva || exit 2

# Suma el tamaño de los archivos cuyas rutas llegan por la entrada estándar.
sumar_tamanos() {
  _total=0
  while IFS= read -r _ruta; do
    [ -f "$_ruta" ] || continue
    _tam="$(stat -c %s "$_ruta" 2>/dev/null || echo 0)"
    _total=$((_total + _tam))
  done
  echo "$_total"
}

mib() { echo $(($1 / 1048576)); }

if [ -f "$ARCHIVO_ROTO" ]; then
  echo "ALERTA el archivo de cambios está ROTO desde $(cat "$ARCHIVO_ROTO" 2>/dev/null || echo '?'): se descartaron cambios sin archivar y no hay recuperación a un instante intermedio. Se exige un respaldo completo nuevo (respaldar.sh o scripts/respaldar-ahora.sh), que es lo único que retira esta marca."
  exit 1
fi

pendiente="$(
  for _lista in "$PGWAL"/archive_status/*.ready; do
    [ -e "$_lista" ] || continue
    echo "$PGWAL/$(basename "$_lista" .ready)"
  done | sumar_tamanos
)"
en_espera="$(find "$ESPERA" -type f ! -name 'archivo-roto*' 2>/dev/null | sumar_tamanos)"
total=$((pendiente + en_espera))
porcentaje=$((total * 100 / RESERVADO))
detalle="$(mib "$total") MiB de $(mib "$RESERVADO") MiB reservados ($(mib "$pendiente") MiB de cambios por archivar y $(mib "$en_espera") MiB de respaldo en espera)"

if [ "$total" -ge "$RESERVADO" ]; then
  _tmp="$ARCHIVO_ROTO.$$.tmp"
  date -u '+%Y-%m-%dT%H:%M:%SZ' >"$_tmp"
  mv -f "$_tmp" "$ARCHIVO_ROTO"
  echo "ALERTA espacio reservado AGOTADO ($detalle): se declara ROTO el archivo de cambios. La base sigue operando, pero los cambios sin archivar se descartan y se exige un respaldo completo nuevo."
  exit 1
fi

if [ "$porcentaje" -ge "$AVISO_PORCENTAJE" ]; then
  echo "ALERTA espacio reservado del archivo de cambios al $porcentaje % ($detalle): el almacén de respaldos no está recibiendo lo pendiente. Si se agota se declara roto el archivo de cambios."
  exit 1
fi

echo "ok espacio reservado al $porcentaje % ($detalle)"
