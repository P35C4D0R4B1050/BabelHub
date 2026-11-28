#!/usr/bin/env bash
# Control de las entregas de interfaz de un segundo agente (docs/07-proceso-de-desarrollo/10-colaboracion-ui.md).
# Compara una rama con su base y rechaza todo cambio fuera de las rutas permitidas o con patrones prohibidos en las líneas
# agregadas. Marca «REVISAR» lo que puede romper una función (formularios, campos, protecciones). No sustituye la revisión.
# Quien revisa lo ejecuta desde «main», para que la rama revisada no pueda cambiarlo.
#
#   bash scripts/guardia-ui.sh <rama> [base]        (base por defecto: main)
# Códigos de salida: 0 sin rechazos, 1 hay rechazos, 2 uso inválido.
set -euo pipefail

[[ $# -ge 1 && $# -le 2 ]] || { echo "Uso: $0 <rama> [base]" >&2; exit 2; }
rama="$1"
base="${2:-main}"
for referencia in "$rama" "$base"; do
  git rev-parse --verify --quiet "$referencia^{commit}" >/dev/null || { echo "No existe la referencia «$referencia»." >&2; exit 2; }
done

API='src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api'
rechazos=0
revisiones=0
total=0

rechazar() { echo "RECHAZADO  $1"; rechazos=$((rechazos + 1)); }
revisar() { echo "REVISAR    $1"; revisiones=$((revisiones + 1)); }

permitida() {
  local ruta="$1"
  case "$ruta" in
    */_ViewImports.cshtml | */_ViewStart.cshtml | */_TokenAntifalsificacion.cshtml) return 1 ;;
    "$API"/Views/Legal/* | "$API"/Views/Textos/*) return 1 ;;
  esac
  [[ "$ruta" =~ ^$API/(Areas/[^/]+/Views|Views)/.+\.cshtml$ ]] && return 0
  [[ "$ruta" =~ ^$API/Web/Estilos/.+\.css$ ]] && return 0
  [[ "$ruta" =~ ^$API/wwwroot/js/[^/]+\.js$ ]] && return 0
  [[ "$ruta" =~ ^$API/wwwroot/img/.+\.(svg|png|webp)$ ]] && return 0
  [[ "$ruta" == docs/06-experiencia-de-usuario/* ]] && return 0
  [[ "$ruta" == prototype/ui-v2/* ]] && return 0
  [[ "$ruta" == prototype/ui-v3/* ]] && return 0
  [[ "$ruta" == progress/coordinacion-ui.md ]] && return 0
  return 1
}

# Las líneas agregadas (+) o quitadas (-) de un archivo, sin las cabeceras del parche.
lineas() { # signo ruta
  git diff --no-color --no-ext-diff --no-renames --unified=0 "$base...$rama" -- "$2" \
    | awk -v s="$1" '/^@@/ { dentro = 1; next } dentro && substr($0, 1, 1) == s { print substr($0, 2) }'
}

buscar() { # texto patrón
  grep -qiE -e "$2" <<<"$1"
}

while IFS= read -r -d '' estado && IFS= read -r -d '' ruta; do
  total=$((total + 1))
  if ! permitida "$ruta"; then
    rechazar "$ruta: fuera de las rutas permitidas"
    continue
  fi

  agregadas="$(lineas + "$ruta")"
  quitadas="$(lineas - "$ruta")"
  case "$ruta" in
    "$API"/*.cshtml)
      [[ "$estado" != D ]] || revisar "$ruta: se elimina una vista"
      ! buscar "$agregadas" '@inject|Html\.Raw' || rechazar "$ruta: agrega @inject o Html.Raw"
      ! buscar "$agregadas" '<style|[[:space:]]style[[:space:]]*=' || rechazar "$ruta: agrega estilos en línea (CSP)"
      ! buscar "$(grep -iE '<script' <<<"$agregadas" | grep -viE '<script[^>]*src="/' || true)" '<script' || rechazar "$ruta: agrega un script en línea o de otro origen (CSP)"
      ! buscar "$agregadas" '[[:space:]]on[a-z]+[[:space:]]*=[[:space:]]*"' || rechazar "$ruta: agrega un manejador en línea (CSP)"
      ! buscar "$agregadas" '(src|srcset)[[:space:]]*=[[:space:]]*"(https?:)?//' || rechazar "$ruta: carga un recurso de otro dominio"
      sensible='<form|</form|<input|<select|<textarea|<button|name=|action=|method=|asp-|hx-|_TokenAntifalsificacion|nonce| id='
      ! buscar "$quitadas" "$sensible" || revisar "$ruta: modifica o quita formularios, campos, id o protecciones"
      ! buscar "$agregadas" '<form|<input|name=|action=|asp-|hx-' || revisar "$ruta: agrega formularios, campos o atributos de comportamiento"
      ;;
    "$API"/wwwroot/js/*)
      # En un guion nuevo se rechaza; en uno que ya existía (algunos usan cookies de preferencias) se marca para revisar a mano.
      if buscar "$agregadas" 'eval\(|new Function|fetch\(|XMLHttpRequest|sendBeacon|WebSocket|document\.cookie|localStorage|sessionStorage|innerHTML|outerHTML|insertAdjacentHTML|https?://'; then
        if [[ "$estado" == A ]]; then
          rechazar "$ruta: el JavaScript visual no hace solicitudes, no usa cookies ni almacenamiento, ni inyecta HTML"
        else
          revisar "$ruta: agrega solicitudes, cookies, almacenamiento o inyección de HTML"
        fi
      fi
      [[ "$estado" == A ]] || revisar "$ruta: modifica un guion que ya existía"
      ;;
    "$API"/*.css)
      ! buscar "$agregadas" 'url\([[:space:]]*["'"'"']?(https?:)?//|@import[^;]*(https?:)?//' || rechazar "$ruta: carga un recurso de otro dominio"
      ! buscar "$agregadas" '#[0-9a-f]{3,8}\b|rgb\(|hsl\(' || revisar "$ruta: agrega colores; deben ser tokens de los temas"
      ;;
  esac
done < <(git diff --name-status --no-renames -z "$base...$rama")

echo "Archivos: $total · rechazos: $rechazos · por revisar: $revisiones"
[[ $rechazos -eq 0 ]]
