#!/usr/bin/env bash
# Ejecuta un escenario k6 (imagen oficial grafana/k6, sin instalar nada en Windows) contra el entorno de tests/load/entorno/ y deja los resultados en
# progress/evidencias/k6/<fecha>/. Uso: bash tests/load/ejecutar.sh disponibilidad|checkout-confirmacion|sobreventa|limites|resiliencia|rampa
# Requiere haber corrido antes entorno/preparar-carga.sh. Las credenciales salen del archivo de entorno (fuera del repositorio) y nunca se escriben en resultados.
set -euo pipefail
cd "$(dirname "$0")/../.."
escenario="${1:?uso: ejecutar.sh <escenario>}"
[ -f "tests/load/escenarios/$escenario.js" ] || { echo "no existe el escenario $escenario" >&2; exit 2; }
CARGA_CARPETA="${CARGA_CARPETA:-$(cygpath -m "$(cd "$(git rev-parse --git-common-dir)/../.." && pwd)")/babelhub-carga}"
set -a; source "$CARGA_CARPETA/env"; set +a
IMAGEN_K6="${IMAGEN_K6:-grafana/k6:latest}"
fecha="${CARGA_FECHA:-$(date +%Y-%m-%d)}"
resultados="progress/evidencias/k6/$fecha"
mkdir -p "$resultados" "$CARGA_CARPETA/k6"

# Credenciales de usuario de corta vida (14 minutos) firmadas por el servidor de claves de carga.
node tests/load/entorno/servidor-de-claves.mjs emitir "$CARGA_CARPETA/claves" 2200
mv -f "$CARGA_CARPETA/claves/credenciales.json" "$CARGA_CARPETA/k6/credenciales.json"

export BASE_URL="https://host.docker.internal:$CARGA_PUERTO"
export CREDENCIAL_DISPONIBILIDAD="$BABELHUB__Seguridad__Credenciales__LecturaDeDisponibilidad"
export CREDENCIAL_OPERACION_DE_USUARIO="$BABELHUB__Seguridad__Credenciales__OperacionDeUsuario"
export CREDENCIAL_EXPIRACION_DE_RESERVAS="$BABELHUB__Seguridad__Credenciales__ExpiracionDeReservas"
export CREDENCIAL_ORIGEN="$BABELHUB__Seguridad__Credenciales__OrigenReenviado"
export ARCHIVO_DE_CREDENCIALES=/credenciales/credenciales.json
export DIRECTORIO_DE_RESULTADOS=/resultados
export MSYS_NO_PATHCONV=1
win() { cygpath -w "$1"; }
extra=()
for v in DURACION RITMO UNIDADES COMPETIDORES NIVELES SEGUNDOS_POR_NIVEL PENSAR_S; do [ -n "${!v:-}" ] && { export "$v"; extra+=(-e "$v"); }; done

correr() {
  docker run --rm -i --ulimit nofile=1048576:1048576 \
    -v "$(win "$PWD/tests/load"):/carga:ro" -v "$(win "$CARGA_CARPETA/k6"):/credenciales:ro" -v "$(win "$PWD/$resultados"):/resultados" \
    -e BASE_URL -e CREDENCIAL_DISPONIBILIDAD -e CREDENCIAL_OPERACION_DE_USUARIO -e CREDENCIAL_EXPIRACION_DE_RESERVAS -e CREDENCIAL_ORIGEN \
    -e ARCHIVO_DE_CREDENCIALES -e DIRECTORIO_DE_RESULTADOS "${extra[@]}" \
    "$IMAGEN_K6" run "/carga/escenarios/$escenario.js"
}

if [ "$escenario" = "resiliencia" ]; then
  # PostgreSQL se pausa entre los 20 s y los 45 s desde que k6 empieza a generar carga (tras la siembra).
  registro="$CARGA_CARPETA/k6/resiliencia.log"
  correr > "$registro" 2>&1 &
  k6pid=$!
  for i in $(seq 1 120); do grep -q "running (0" "$registro" 2>/dev/null && break; sleep 1; done
  sleep 20; docker pause bhcarga-postgres-1 >/dev/null; echo "PostgreSQL pausado"
  sleep 25; docker unpause bhcarga-postgres-1 >/dev/null; echo "PostgreSQL reanudado"
  wait $k6pid && estado=0 || estado=$?
  cat "$registro" | tail -40
  exit $estado
fi
correr
