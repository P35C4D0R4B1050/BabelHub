#!/usr/bin/env bash
# Serie de capacidad: la rampa de 50 a 2000 usuarios virtuales con varias configuraciones de CPU (Inventario / PostgreSQL / memoria del Inventario).
# Uso: bash tests/load/entorno/serie-de-capacidad.sh ["cpus_inv cpus_pg mem" ...]   (por omisión, las cinco del doc 09 §6b)
# Resultados: progress/evidencias/k6/$CARGA_FECHA/inventario-<cpus>_postgres-<cpus>_pool-16/. Registro completo fuera del repositorio ($CARGA_CARPETA/serie.log).
# A prueba de duplicados: un bloqueo impide dos series a la vez; cada paso elimina residuos de k6 y tiene tope de tiempo (TOPE_PASO_S, 1500 s); un paso que falla no detiene los demás.
# Ejecutar directamente (en segundo plano de la herramienta, no con `nohup … &`, que el shell de la herramienta mata).
set -uo pipefail
cd "$(dirname "$0")/../../.."
export CARGA_CARPETA="${CARGA_CARPETA:-$(cygpath -m "$(cd "$(git rev-parse --git-common-dir)/../.." && pwd)")/babelhub-carga}"
export CARGA_FECHA="${CARGA_FECHA:-$(date +%Y-%m-%d)-final}"
bloqueo="$CARGA_CARPETA/serie.bloqueo"
mkdir "$bloqueo" 2>/dev/null || { echo "ya hay una serie en curso ($bloqueo); si no es así, bórralo" >&2; exit 2; }
trap 'rmdir "$bloqueo" 2>/dev/null; docker rm -f "${CARGA_PROYECTO:-bhcarga}-k6" >/dev/null 2>&1 || true' EXIT
registro="$CARGA_CARPETA/serie.log"
exec > >(tee -a "$registro") 2>&1

configuraciones=("$@")
[ ${#configuraciones[@]} -gt 0 ] || configuraciones=("0.3 0.8 350m" "0.6 0.4 512m" "1 1 512m" "2 1 512m" "4 2 512m")
tope="${TOPE_PASO_S:-1500}"

for cfg in "${configuraciones[@]}"; do
  read -r cinv cpg mem <<<"$cfg"; mem="${mem:-512m}"
  echo "=== Inventario $cinv CPU / PostgreSQL $cpg CPU / $mem — $(date +%T)"
  docker rm -f "${CARGA_PROYECTO:-bhcarga}-k6" >/dev/null 2>&1 || true
  timeout 300 bash tests/load/entorno/ajustar-limites.sh "$cinv" "$cpg" "$mem" || { echo "no se pudo ajustar los límites: se omite"; continue; }
  timeout "$tope" bash tests/load/ejecutar.sh rampa || echo "la rampa terminó con umbral incumplido, error o tope de tiempo (código $?)"
  docker rm -f "${CARGA_PROYECTO:-bhcarga}-k6" >/dev/null 2>&1 || true
  destino="progress/evidencias/k6/$CARGA_FECHA/inventario-${cinv}cpu_postgres-${cpg}cpu_pool-16"
  mkdir -p "$destino"
  for a in progress/evidencias/k6/"$CARGA_FECHA"/rampa-*; do [ -e "$a" ] && mv -f "$a" "$destino/"; done
done
echo "SERIE FIN $(date +%T)"
