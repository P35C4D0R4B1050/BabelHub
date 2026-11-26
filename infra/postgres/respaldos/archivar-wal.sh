#!/bin/sh
# `archive_command` de PostgreSQL (PLA-15, PLA-17; D09, D10, D25):  archive_command = '/opt/babelhub/archivar-wal.sh %p %f'
# Comprime el segmento con zstd, lo cifra con la clave pública de age (en una sola tubería: no hay archivo intermedio sin cifrar) y lo sube a
# respaldos-wal con el nombre «<segmento>-<sha256 del cifrado>.zst.age». Si falla la compresión, el cifrado o la subida, sale con error.
# PostgreSQL lo repite hasta que sale con código 0 y, mientras tanto, conserva el segmento en pg_wal: es el archivo de cambios pendiente,
# protegido por el disco cifrado. Lo que se repite no pisa nada: otro intento cifra de nuevo (age usa una clave efímera) y el nombre cambia;
# al restaurar, dos candidatos con el mismo contenido no dañan y, si difieren, la recuperación se detiene antes de ese segmento (D28).
set -eu
. /opt/babelhub/comun.sh

[ $# -eq 2 ] || fallo "uso: archivar-wal.sh <ruta-del-segmento> <nombre>"
ruta="$1"
nombre="$2"
[ -f "$ruta" ] || fallo "no existe el segmento $ruta"
case "$nombre" in
  "" | *[!0-9A-Za-z._]*) fallo "nombre de segmento inválido" ;;
esac
# Con el archivo de cambios declarado roto (espacio reservado agotado, PLA-21) se descarta el segmento para que PostgreSQL lo recicle y el disco
# no se llene: la base sigue operando y solo un respaldo completo nuevo retira la marca. Va antes de verificar el entorno: aunque el almacén o la
# configuración estén rotos, el espacio no debe seguir creciendo.
# Los archivos de historia de línea de tiempo (`.history`) pesan unos bytes y la restauración los necesita para elegir la línea: se siguen archivando.
if [ -f "$ARCHIVO_ROTO" ] && [ "${nombre%.history}" = "$nombre" ]; then
  echo "respaldos: el archivo de cambios está roto: se descarta el segmento $nombre hasta que haya un respaldo completo nuevo" >&2
  exit 0
fi
verificar_entorno

temporal="$ESPERA/wal.$$.tmp"
estado="$ESPERA/wal.$$.estado.tmp"
trap 'rm -f "$temporal" "$estado.orden" "$estado.zstd"' EXIT INT TERM
comprimir_y_cifrar "$temporal" "$estado" cat "$ruta" || fallo "no se pudo comprimir y cifrar el segmento $nombre"
suma="$(suma_de "$temporal")"
subir "$temporal" "$CONTENEDOR_WAL" "$nombre-$suma.zst.age"
