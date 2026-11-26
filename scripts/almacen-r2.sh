#!/usr/bin/env bash
# Los contenedores de respaldos en Cloudflare R2: bloqueo de retención, ciclo de vida y privacidad (PLA-16, PLA-18, PLA-44; D26, D27).
# La definición está en infra/almacen/r2/ y es la misma que leen las pruebas. Se llama por `scripts/almacen.sh r2-…`.
#
#   scripts/almacen.sh r2-plan                    muestra las llamadas y los cuerpos que se enviarían, sin token y sin llamar a nadie
#   scripts/almacen.sh r2-aplicar [--ubicacion <wnam|enam|weur|eeur|apac|oc>]
#                                                 crea los contenedores y bloqueos que falten y deja el ciclo de vida como dice la definición
#                                                 (lo reescribe siempre). No cambia un bloqueo que ya existe y difiere: lo informa y falla, para
#                                                 que lo decida la persona responsable
#   scripts/almacen.sh r2-comprobar               compara lo que hay en la cuenta con la definición (basta un token de solo lectura)
#
# La cuenta llega en CLOUDFLARE_ACCOUNT_ID. El token de API de administración es **temporal** (se crea para esto, con vencimiento, y se revoca al
# terminar): llega en CLOUDFLARE_API_TOKEN o, si no está, se pide sin mostrarlo (o se lee de la entrada estándar). Nunca va como argumento
# (llega a curl por `--config -`), no se imprime y no se escribe en disco. Solo corre en el equipo de la persona responsable: se niega si detecta
# un agente o una integración continua (la detección mira variables de entorno conocidas; no sustituye el cuidado de la persona).
# La API es la v4 de Cloudflare. R2 no tiene bloqueo irreversible: quien administre la cuenta puede quitar la regla (riesgo aceptado, spec §5).
#
# `CURL` es el comando curl (las pruebas usan un doble). Códigos: 0 conforme, 1 hay diferencias o falló una llamada, 2 uso o definición inválidos.
set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEFINICION="${ALMACEN_DEFINICION_R2:-${ALMACEN_DEFINICION:-$RAIZ/infra/almacen}/r2}"
CURL="${CURL:-curl}"
API=https://api.cloudflare.com/client/v4
SEGUNDOS_POR_DIA=86400

fallo_de_uso() {
  echo "Error: $1" >&2
  exit 2
}

NOMBRES=()
declare -A BLOQUEO=() EXPIRA=()

leer_definicion() {
  local archivo="$DEFINICION/contenedores.txt" linea nombre bloqueo expira resto credencial contenedor permiso
  [[ -f "$archivo" ]] || fallo_de_uso "no existe $archivo"
  while IFS= read -r linea || [[ -n "$linea" ]]; do
    linea="${linea%%#*}"
    linea="${linea//$'\r'/}"
    [[ -z "${linea//[[:space:]]/}" ]] && continue
    read -r nombre bloqueo expira resto <<<"$linea"
    [[ -z "$resto" && -n "$expira" ]] || fallo_de_uso "r2/contenedores.txt: cada línea lleva nombre, días de bloqueo y días de retiro"
    [[ "$nombre" =~ ^[a-z][a-z0-9-]{1,61}[a-z0-9]$ ]] || fallo_de_uso "r2/contenedores.txt: el nombre «$nombre» no es válido"
    [[ -z "${BLOQUEO[$nombre]:-}" ]] || fallo_de_uso "r2/contenedores.txt: «$nombre» está repetido"
    [[ "$bloqueo" =~ ^[1-9][0-9]*$ && "$expira" =~ ^[1-9][0-9]*$ ]] || fallo_de_uso "r2/contenedores.txt: los plazos de «$nombre» son días mayores que cero"
    [[ "$expira" -ge "$bloqueo" ]] || fallo_de_uso "r2/contenedores.txt: «$nombre» retira los objetos antes de que venza su bloqueo"
    NOMBRES+=("$nombre")
    BLOQUEO[$nombre]="$bloqueo"
    EXPIRA[$nombre]="$expira"
  done <"$archivo"
  [[ ${#NOMBRES[@]} -gt 0 ]] || fallo_de_uso "r2/contenedores.txt no define contenedores"

  archivo="$DEFINICION/credenciales.txt"
  [[ -f "$archivo" ]] || fallo_de_uso "no existe $archivo"
  while IFS= read -r linea || [[ -n "$linea" ]]; do
    linea="${linea%%#*}"
    linea="${linea//$'\r'/}"
    [[ -z "${linea//[[:space:]]/}" ]] && continue
    read -r credencial contenedor permiso resto <<<"$linea"
    [[ -z "$resto" && -n "$permiso" ]] || fallo_de_uso "r2/credenciales.txt: cada línea lleva credencial, contenedor y permiso"
    [[ -n "${BLOQUEO[$contenedor]:-}" ]] || fallo_de_uso "r2/credenciales.txt: «$credencial» usa el contenedor desconocido «$contenedor»"
    # R2 solo permite acotar un token de objetos a lectura o a lectura y escritura; un permiso de administración no es una credencial de la plataforma.
    [[ "$permiso" == lectura || "$permiso" == lectura-escritura ]] || fallo_de_uso "r2/credenciales.txt: el permiso «$permiso» de «$credencial» no existe (lectura o lectura-escritura)"
  done <"$archivo"
}

cuerpo_de_bloqueo() {
  local dias="${BLOQUEO[$1]}"
  printf '{"rules":[{"id":"retencion-%s-dias","enabled":true,"condition":{"type":"Age","maxAgeSeconds":%s}}]}' "$dias" "$((dias * SEGUNDOS_POR_DIA))"
}

cuerpo_de_ciclo() {
  local dias="${EXPIRA[$1]}"
  printf '{"rules":[{"id":"retiro-a-los-%s-dias","enabled":true,"conditions":{"prefix":""},"deleteObjectsTransition":{"condition":{"type":"Age","maxAge":%s}},"abortMultipartUploadsTransition":{"condition":{"type":"Age","maxAge":%s}}}]}' \
    "$dias" "$((dias * SEGUNDOS_POR_DIA))" "$SEGUNDOS_POR_DIA"
}

plan() {
  local nombre
  for nombre in "${NOMBRES[@]}"; do
    echo "POST /accounts/<cuenta>/r2/buckets {\"name\":\"$nombre\"}   (solo si no existe)"
    echo "PUT /accounts/<cuenta>/r2/buckets/$nombre/lock $(cuerpo_de_bloqueo "$nombre")"
    echo "PUT /accounts/<cuenta>/r2/buckets/$nombre/lifecycle $(cuerpo_de_ciclo "$nombre")"
  done
}

# ---------- Llamadas ----------

negarse_con_agentes() {
  local variable
  for variable in CLAUDECODE CLAUDE_CODE_ENTRYPOINT CODEX_SANDBOX CURSOR_AGENT GEMINI_CLI CI GITHUB_ACTIONS; do
    [[ -z "${!variable:-}" ]] || fallo_de_uso "hay un agente o una integración continua en este entorno ($variable): el token de administración no se usa aquí. Ejecútalo en tu propia terminal"
  done
}

leer_credencial() {
  [[ "${CLOUDFLARE_ACCOUNT_ID:-}" =~ ^[0-9a-f]{32}$ ]] || fallo_de_uso "falta CLOUDFLARE_ACCOUNT_ID (32 caracteres hexadecimales, del panel de R2)"
  ficha="${CLOUDFLARE_API_TOKEN:-}"
  unset CLOUDFLARE_API_TOKEN
  if [[ -z "$ficha" ]]; then
    if [[ -t 0 ]]; then
      read -r -s -p "Token de API temporal de Cloudflare (no se muestra): " ficha
      echo >&2
    else
      IFS= read -r ficha || true
    fi
  fi
  ficha="${ficha//$'\r'/}"
  [[ "$ficha" =~ ^[A-Za-z0-9_-]{20,200}$ ]] || fallo_de_uso "falta el token de API o no tiene la forma esperada"
}

TRABAJO=""
RESPUESTA=""
ESTADO=""

# llamar <método> <ruta> [<cuerpo>]: deja el estado HTTP en ESTADO y el cuerpo sin espacios en RESPUESTA.
llamar() {
  local metodo="$1" ruta="$2" cuerpo="${3:-}" argumentos
  # «--disable» va primero: curl no lee ~/.curlrc, que podría registrar o desviar la cabecera con el token.
  argumentos=(--disable --silent --show-error --max-time 60 --proto =https --config - --request "$metodo" --output "$TRABAJO/respuesta" --write-out '%{http_code}')
  if [[ -n "$cuerpo" ]]; then
    printf '%s' "$cuerpo" >"$TRABAJO/cuerpo"
    argumentos+=(--header "Content-Type: application/json" --data-binary "@$TRABAJO/cuerpo")
  fi
  : >"$TRABAJO/respuesta"
  if ! ESTADO="$(printf 'header = "Authorization: Bearer %s"\n' "$ficha" | "$CURL" "${argumentos[@]}" "$API/accounts/$CLOUDFLARE_ACCOUNT_ID/r2$ruta")"; then
    echo "Error: no se pudo llamar a la API de Cloudflare ($metodo $ruta)" >&2
    exit 1
  fi
  RESPUESTA="$(tr -d ' \t\r\n' <"$TRABAJO/respuesta")"
}

exito() {
  [[ "$ESTADO" == 2?? && "$RESPUESTA" == *'"success":true'* ]]
}

# Los objetos del arreglo «rules» de la respuesta, uno por línea (se cuentan las llaves: los identificadores y prefijos propios no las llevan).
reglas() {
  awk '{
    i = index($0, "\"rules\":[")
    if (!i) next
    nivel = 0; regla = ""
    for (p = i + 9; p <= length($0); p++) {
      c = substr($0, p, 1)
      if (c == "{") nivel++
      if (nivel > 0) regla = regla c
      if (c == "}") { nivel--; if (nivel == 0) { print regla; regla = "" } }
      if (c == "]" && nivel == 0) break
    }
  }' <<<"$RESPUESTA"
}

sin_prefijo() {
  [[ "$1" != *'"prefix":'* || "$1" == *'"prefix":""'* ]]
}

# Vacío si el bloqueo es exactamente el definido; si no, qué difiere.
diferencia_de_bloqueo() {
  local nombre="$1" segundos=$((BLOQUEO[$1] * SEGUNDOS_POR_DIA)) lista=() regla
  mapfile -t lista < <(reglas)
  if [[ ${#lista[@]} -ne 1 ]]; then
    echo "debe tener una sola regla de bloqueo y tiene ${#lista[@]}"
    return
  fi
  regla="${lista[0]}"
  [[ "$regla" == *'"enabled":true'* ]] || { echo "la regla de bloqueo está deshabilitada"; return; }
  sin_prefijo "$regla" || { echo "la regla de bloqueo solo cubre un prefijo"; return; }
  [[ "$regla" == *'"type":"Age"'* && "$regla" =~ \"maxAgeSeconds\":$segundos([^0-9]|$) ]] || echo "el bloqueo no es de ${BLOQUEO[$nombre]} días"
}

diferencia_de_ciclo() {
  local nombre="$1" segundos=$((EXPIRA[$1] * SEGUNDOS_POR_DIA)) lista=() regla retiros=0
  mapfile -t lista < <(reglas)
  for regla in "${lista[@]}"; do
    [[ "$regla" == *'"deleteObjectsTransition":{'* ]] || continue
    retiros=$((retiros + 1))
    [[ "$regla" == *'"enabled":true'* ]] || { echo "la regla de retiro está deshabilitada"; return; }
    sin_prefijo "$regla" || { echo "la regla de retiro solo cubre un prefijo"; return; }
    [[ "$regla" =~ \"deleteObjectsTransition\":\{\"condition\":\{[^}]*\"maxAge\":$segundos([^0-9]|$) ]] || { echo "el retiro no es a los ${EXPIRA[$nombre]} días"; return; }
  done
  [[ $retiros -eq 1 ]] || echo "debe tener una sola regla de retiro y tiene $retiros"
}

# Vacío si el contenedor no es público (ni dominio r2.dev ni dominios propios).
diferencia_de_privacidad() {
  llamar GET "/buckets/$1/domains/managed"
  exito || { echo "no se pudo comprobar el dominio público (estado $ESTADO)"; return; }
  [[ "$RESPUESTA" == *'"enabled":false'* && "$RESPUESTA" != *'"enabled":true'* ]] || { echo "tiene el dominio público r2.dev habilitado"; return; }
  llamar GET "/buckets/$1/domains/custom"
  exito || { echo "no se pudieron comprobar los dominios propios (estado $ESTADO)"; return; }
  [[ "$RESPUESTA" == *'"domains":[]'* ]] || echo "tiene un dominio propio conectado"
}

comprobar() {
  local nombre fallos=0 problemas diferencia
  for nombre in "${NOMBRES[@]}"; do
    problemas=()
    llamar GET "/buckets/$nombre"
    if ! exito; then
      echo "x $nombre: no existe o no se puede consultar (estado $ESTADO)"
      fallos=$((fallos + 1))
      continue
    fi
    llamar GET "/buckets/$nombre/lock"
    if exito; then diferencia="$(diferencia_de_bloqueo "$nombre")"; else diferencia="no se pudo leer el bloqueo (estado $ESTADO)"; fi
    [[ -z "$diferencia" ]] || problemas+=("$diferencia")
    llamar GET "/buckets/$nombre/lifecycle"
    if exito; then diferencia="$(diferencia_de_ciclo "$nombre")"; else diferencia="no se pudo leer el ciclo de vida (estado $ESTADO)"; fi
    [[ -z "$diferencia" ]] || problemas+=("$diferencia")
    diferencia="$(diferencia_de_privacidad "$nombre")"
    [[ -z "$diferencia" ]] || problemas+=("$diferencia")
    if [[ ${#problemas[@]} -eq 0 ]]; then
      echo "ok $nombre"
    else
      for diferencia in "${problemas[@]}"; do echo "x $nombre: $diferencia"; done
      fallos=$((fallos + 1))
    fi
  done
  if [[ $fallos -gt 0 ]]; then
    echo "R2 no coincide con la definición ($fallos contenedores con diferencias)." >&2
    return 1
  fi
  echo "R2 coincide con la definición. El bloqueo de R2 se puede quitar con la administración de la cuenta: revoca ahora el token temporal."
}

aplicar() {
  local nombre cuerpo
  for nombre in "${NOMBRES[@]}"; do
    llamar GET "/buckets/$nombre"
    if exito; then
      echo "= $nombre: ya existe"
    elif [[ "$ESTADO" == 404 ]]; then
      cuerpo="{\"name\":\"$nombre\"${UBICACION:+,\"locationHint\":\"$UBICACION\"}}"
      llamar POST /buckets "$cuerpo"
      exito || { echo "Error: no se pudo crear $nombre (estado $ESTADO)" >&2; exit 1; }
      echo "+ $nombre: creado"
    else
      echo "Error: no se pudo consultar $nombre (estado $ESTADO): revisa el token y la cuenta" >&2
      exit 1
    fi

    llamar GET "/buckets/$nombre/lock"
    exito || { echo "Error: no se pudo leer el bloqueo de $nombre (estado $ESTADO)" >&2; exit 1; }
    if [[ -z "$(reglas)" ]]; then
      llamar PUT "/buckets/$nombre/lock" "$(cuerpo_de_bloqueo "$nombre")"
      exito || { echo "Error: no se pudo poner el bloqueo de $nombre (estado $ESTADO)" >&2; exit 1; }
      echo "+ $nombre: bloqueo de ${BLOQUEO[$nombre]} días"
    elif [[ -n "$(diferencia_de_bloqueo "$nombre")" ]]; then
      echo "Error: $nombre ya tiene un bloqueo distinto ($(diferencia_de_bloqueo "$nombre")): no se cambia solo; revísalo en el panel" >&2
      exit 1
    else
      echo "= $nombre: ya tiene su bloqueo"
    fi

    llamar PUT "/buckets/$nombre/lifecycle" "$(cuerpo_de_ciclo "$nombre")"
    exito || { echo "Error: no se pudo poner el ciclo de vida de $nombre (estado $ESTADO)" >&2; exit 1; }
    echo "+ $nombre: retiro a los ${EXPIRA[$nombre]} días"
  done
  echo "# Aplicado. Comprobando contra la definición:"
  comprobar
}

[[ $# -ge 1 ]] || fallo_de_uso "falta la orden (r2-plan, r2-aplicar o r2-comprobar)"
ORDEN="$1"
shift
UBICACION=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --ubicacion)
      [[ "$ORDEN" == r2-aplicar && $# -ge 2 && "$2" =~ ^(wnam|enam|weur|eeur|apac|oc)$ ]] || fallo_de_uso "--ubicacion solo vale en r2-aplicar y admite wnam, enam, weur, eeur, apac u oc"
      UBICACION="$2"
      shift 2
      ;;
    *) fallo_de_uso "argumento desconocido «$1» (el token nunca se pasa como argumento)" ;;
  esac
done

leer_definicion

case "$ORDEN" in
  r2-plan) plan ;;
  r2-aplicar | r2-comprobar)
    negarse_con_agentes
    leer_credencial
    TRABAJO="$(mktemp -d)"
    trap 'rm -rf "$TRABAJO"' EXIT
    if [[ "$ORDEN" == r2-aplicar ]]; then aplicar; else comprobar; fi
    ;;
  *) fallo_de_uso "orden desconocida «$ORDEN»" ;;
esac
