#!/bin/sh
# `archive_command` de PostgreSQL (PLA-15, PLA-17; D09, D10):  archive_command = '/opt/babelhub/archivar-wal.sh %p %f'
# Cifra el segmento con la clave pública de age y lo sube a respaldos-wal con el nombre «<segmento>-<sha256 del cifrado>.age».
# PostgreSQL lo repite hasta que sale con código 0 y, mientras tanto, conserva el segmento en pg_wal: es el archivo de cambios pendiente,
# protegido por el disco cifrado. Lo que se repite no pisa nada: otro intento cifra de nuevo (age usa una clave efímera) y el nombre cambia;
# la restauración elige entre candidatos por continuidad de línea de tiempo y LSN (D09), así que un duplicado no daña.
set -eu
. /opt/babelhub/comun.sh

[ $# -eq 2 ] || fallo "uso: archivar-wal.sh <ruta-del-segmento> <nombre>"
ruta="$1"
nombre="$2"
[ -f "$ruta" ] || fallo "no existe el segmento $ruta"
case "$nombre" in
  "" | *[!0-9A-Za-z._]*) fallo "nombre de segmento inválido" ;;
esac
verificar_entorno

temporal="$ESPERA/wal.$$.tmp"
trap 'rm -f "$temporal"' EXIT INT TERM
age -r "$BABELHUB_AGE_DESTINATARIO" -o "$temporal" "$ruta"
suma="$(suma_de "$temporal")"
subir "$temporal" "$CONTENEDOR_WAL" "$nombre-$suma.age"
