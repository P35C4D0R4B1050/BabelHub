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
# Imagen oficial fijada por digest (k6 v2.4.0): nunca `latest`.
IMAGEN_K6="${IMAGEN_K6:-grafana/k6@sha256:3d8e5c7f1d53ec59408c2ffbd9943602a0985ffcbd4a2430ec00f3ffca89796e}"
fecha="${CARGA_FECHA:-$(date +%Y-%m-%d)}"
resultados="progress/evidencias/k6/$fecha"
mkdir -p "$resultados" "$CARGA_CARPETA/k6"

# Credenciales de usuario de corta vida (14 minutos) firmadas por el servidor de claves de carga.
node tests/load/entorno/servidor-de-claves.mjs emitir "$CARGA_CARPETA/claves" 2200
mv -f "$CARGA_CARPETA/claves/credenciales.json" "$CARGA_CARPETA/k6/credenciales.json"

# Tras (re)crear el Inventario su caché de claves de Identidad puede tardar en cargar la publicación del servidor de claves (se renueva cada pocos minutos y, sin ella, rechaza toda
# credencial de usuario con 401): se espera a que acepte el token del administrador antes de medir.
token_admin="$(node -e "console.log(JSON.parse(require('fs').readFileSync(process.argv[1],'utf8')).administrador.token)" "$CARGA_CARPETA/k6/credenciales.json")"
listo=no
for i in $(seq 1 120); do
  codigo="$(curl -sk -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $token_admin" -H "X-Credencial-De-Servicio: $BABELHUB__Seguridad__Credenciales__OperacionDeUsuario" -H "X-Credencial-De-Origen: $BABELHUB__Seguridad__Credenciales__OrigenReenviado" -H "X-Origen-Reenviado: 198.51.100.7" "https://localhost:$CARGA_PUERTO/api/inventario/v1/ajustes" || true)"
  # 400: la autenticación pasó y falta un parámetro; 401: la clave de Identidad aún no está cargada.
  { [ "$codigo" = 200 ] || [ "$codigo" = 400 ]; } && { listo=si; break; }
  sleep 3
done
[ "$listo" = si ] || { echo "el Inventario no acepta aún las credenciales de usuario (último código $codigo)" >&2; exit 3; }

export BASE_URL="https://host.docker.internal:$CARGA_PUERTO"
export CREDENCIAL_DISPONIBILIDAD="$BABELHUB__Seguridad__Credenciales__LecturaDeDisponibilidad"
export CREDENCIAL_OPERACION_DE_USUARIO="$BABELHUB__Seguridad__Credenciales__OperacionDeUsuario"
export CREDENCIAL_EXPIRACION_DE_RESERVAS="$BABELHUB__Seguridad__Credenciales__ExpiracionDeReservas"
export CREDENCIAL_ORIGEN="$BABELHUB__Seguridad__Credenciales__OrigenReenviado"
export ARCHIVO_DE_CREDENCIALES=/credenciales/credenciales.json
export DIRECTORIO_DE_RESULTADOS=/resultados
export DESPLAZO_DE_ORIGEN="$(( ((RANDOM << 9) ^ RANDOM) % 15000000 ))"
export MSYS_NO_PATHCONV=1
win() { cygpath -w "$1"; }
extra=()
for v in DURACION RITMO UNIDADES COMPETIDORES NIVELES SEGUNDOS_POR_NIVEL SEGUNDOS_FINAL PENSAR_S FALLOS_DE_CACHE; do [ -n "${!v:-}" ] && { export "$v"; extra+=(-e "$v"); }; done
# Panel web integrado de k6 (PANEL=1): exporta un HTML autocontenido por escenario junto a los demás resultados (<escenario>-panel.html).
if [ "${PANEL:-}" = 1 ]; then
  export K6_WEB_DASHBOARD=true K6_WEB_DASHBOARD_PERIOD=1s K6_WEB_DASHBOARD_EXPORT="/resultados/$escenario-panel.html"
  extra+=(-e K6_WEB_DASHBOARD -e K6_WEB_DASHBOARD_PERIOD -e K6_WEB_DASHBOARD_EXPORT)
fi

estadisticas() { bash tests/load/entorno/estadisticas-de-base.sh "$@" || true; }
# Un solo cliente k6 a la vez: se elimina cualquier residuo del anterior, el contenedor lleva nombre fijo (un duplicado falla en vez de competir por la máquina) y un tope de tiempo
# (TOPE_K6_S, 1800 s por omisión) evita una corrida colgada; al salir se retira el contenedor.
nombre_k6=bhcarga-k6
docker rm -f "$nombre_k6" >/dev/null 2>&1 || true
trap 'docker rm -f "$nombre_k6" >/dev/null 2>&1 || true' EXIT
correr() {
  timeout "${TOPE_K6_S:-1800}" docker run --name "$nombre_k6" --rm -i --ulimit nofile=1048576:1048576 \
    -v "$(win "$PWD/tests/load"):/carga:ro" -v "$(win "$CARGA_CARPETA/k6"):/credenciales:ro" -v "$(win "$PWD/$resultados"):/resultados" \
    -e BASE_URL -e CREDENCIAL_DISPONIBILIDAD -e CREDENCIAL_OPERACION_DE_USUARIO -e CREDENCIAL_EXPIRACION_DE_RESERVAS -e CREDENCIAL_ORIGEN \
    -e ARCHIVO_DE_CREDENCIALES -e DIRECTORIO_DE_RESULTADOS -e DESPLAZO_DE_ORIGEN "${extra[@]}" \
    "$IMAGEN_K6" run "/carga/escenarios/$escenario.js" 2>&1 | tee >(grep -E "level=(warn|error)" > "$resultados/$escenario-avisos.log")
}

estadisticas reiniciar
if [ "$escenario" = "resiliencia" ]; then
  # PostgreSQL se pausa entre los 20 s y los 45 s desde que k6 empieza a generar carga (tras la siembra).
  registro="$CARGA_CARPETA/k6/resiliencia.log"
  correr > "$registro" 2>&1 &
  k6pid=$!
  for i in $(seq 1 120); do grep -q "running (0" "$registro" 2>/dev/null && break; sleep 1; done
  sleep 20; docker pause bhcarga-postgres-1 >/dev/null; echo "PostgreSQL pausado"
  sleep 25; docker unpause bhcarga-postgres-1 >/dev/null; echo "PostgreSQL reanudado"
  wait $k6pid && estado=0 || estado=$?
  estadisticas volcar "$resultados/$escenario-base.txt"
  cat "$registro" | tail -40
  exit $estado
fi
correr && estado=0 || estado=$?
estadisticas volcar "$resultados/$escenario-base.txt"
exit $estado
