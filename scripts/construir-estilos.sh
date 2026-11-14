#!/usr/bin/env bash
# Genera el CSS de la interfaz web (Tailwind CSS 4 + daisyUI 5, AD21 y web-base D09) sin Node.
#   scripts/construir-estilos.sh            # genera wwwroot/css/app.css (minificado)
#   scripts/construir-estilos.sh --observar # regenera al cambiar las vistas
# La CLI independiente de Tailwind y los dos archivos de daisyUI se descargan a .herramientas/ (fuera de Git) y se verifican por su
# huella SHA-256 (Web/versiones.json) antes de usarse: si no coincide, el script falla y borra la descarga.
set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API="$RAIZ/src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api"
VERSIONES="$API/Web/versiones.json"
HERRAMIENTAS="$RAIZ/.herramientas"

huella_de() { grep -o "\"$1\": { \"sha256\": \"[0-9a-f]*\"" "$VERSIONES" | grep -o '[0-9a-f]\{64\}' | head -n 1; }
version_de() { sed -n "/\"$1\": {/,/}/ s/.*\"version\": \"\([^\"]*\)\".*/\1/p" "$VERSIONES" | head -n 1; }
sha256_de() { if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi; }

case "$(uname -s)-$(uname -m)" in
  Linux-x86_64) ASSET="tailwindcss-linux-x64" ; BIN="tailwindcss" ;;
  Linux-aarch64|Linux-arm64) ASSET="tailwindcss-linux-arm64" ; BIN="tailwindcss" ;;
  MINGW*|MSYS*|CYGWIN*) ASSET="tailwindcss-windows-x64.exe" ; BIN="tailwindcss.exe" ;;
  *) echo "construir-estilos: plataforma no soportada: $(uname -s)-$(uname -m)" >&2; exit 1 ;;
esac

descargar() { # url destino huella-esperada
  local url="$1" destino="$2" esperada="$3"
  if [ -f "$destino" ] && [ "$(sha256_de "$destino")" = "$esperada" ]; then return 0; fi
  mkdir -p "$(dirname "$destino")"
  curl -fsSL -o "$destino.parcial" "$url"
  if [ "$(sha256_de "$destino.parcial")" != "$esperada" ]; then
    rm -f "$destino.parcial"
    echo "construir-estilos: la huella SHA-256 de $url no coincide con Web/versiones.json; se descartó la descarga." >&2
    exit 1
  fi
  mv "$destino.parcial" "$destino"
}

TW="$(version_de tailwindcss)"
DU="$(version_de daisyui)"
descargar "https://github.com/tailwindlabs/tailwindcss/releases/download/v$TW/$ASSET" "$HERRAMIENTAS/tailwind/$BIN" "$(huella_de "$ASSET")"
chmod +x "$HERRAMIENTAS/tailwind/$BIN"
descargar "https://github.com/saadeghi/daisyui/releases/download/v$DU/daisyui.mjs" "$HERRAMIENTAS/daisyui/daisyui.mjs" "$(huella_de daisyui.mjs)"
descargar "https://github.com/saadeghi/daisyui/releases/download/v$DU/daisyui-theme.mjs" "$HERRAMIENTAS/daisyui/daisyui-theme.mjs" "$(huella_de daisyui-theme.mjs)"

mkdir -p "$API/wwwroot/css"
exec "$HERRAMIENTAS/tailwind/$BIN" -i "$API/Web/Estilos/app.css" -o "$API/wwwroot/css/app.css" --minify "${@/--observar/--watch}"
