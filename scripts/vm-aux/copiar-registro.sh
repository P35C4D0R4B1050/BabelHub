#!/usr/bin/env bash
# Copia diaria del registro de supresiones a `supresiones-copia` (PLA-38, D15): si el registro se pierde o se altera, la copia y el ancla del
# sello permiten verificarlo y decidir (la vía de excepción de PLA-19). La retención con bloqueo de ese contenedor (100 días) la hace el
# almacén. Corre en VM-AUX con DOS credenciales, cada una con lo mínimo: `supresiones-sello` (remoto `sello`) para LEER las entradas y
# `respaldos-copia-registro` (remoto `copia`) para CREAR en `supresiones-copia`; ninguna puede borrar ni sobrescribir.
#
#   scripts/vm-aux/copiar-registro.sh
#
# La copia es un tar de todas las entradas (identificadores opacos, sin datos personales: no hace falta cifrarla), con el nombre
# «AAAA-MM-DD/<hora UTC>-<sha256 del tar>.tar». Si el registro está vacío se copia igual: la copia de cada día es la prueba de que la
# cadena funciona (comprobar-respaldos.sh alerta si no hay una de las últimas 36 horas).
#
# Códigos de salida: 0 copiado, 1 no se pudo copiar (alerta).
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/comun.sh"

ahora="$(date -u +%s)"
if ! listado="$(listar sello:supresiones-entradas)"; then
  echo "ALERTA copia del registro: no se pudo consultar supresiones-entradas"
  exit 1
fi

temporal="$(mktemp -d)"
trap 'rm -rf "$temporal"' EXIT
mkdir -p "$temporal/entradas"

total=0
while IFS=';' read -r clave _hora; do
  [[ -n "$clave" ]] || continue
  destino="$temporal/entradas/$clave"
  mkdir -p "$(dirname "$destino")"
  if ! "$RCLONE" cat "sello:supresiones-entradas/$clave" >"$destino" 2>/dev/null; then
    echo "ALERTA copia del registro: no se pudo leer una entrada ($clave)"
    exit 1
  fi
  total=$((total + 1))
done < <(LC_ALL=C sort <<<"$listado")

tar -cf "$temporal/copia.tar" -C "$temporal/entradas" .
suma="$(suma_de "$temporal/copia.tar")"
nombre="$(date -u -d "@$ahora" '+%Y-%m-%d/%H%M%S')-$suma.tar"
if ! subir "$temporal/copia.tar" "copia:supresiones-copia/$nombre"; then
  echo "ALERTA copia del registro: no se pudo subir la copia a supresiones-copia"
  exit 1
fi
echo "ok copia $nombre ($total entradas)"
