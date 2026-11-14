#!/usr/bin/env bash
# Detecta secretos en el repositorio (gitleaks) y en las imágenes (Trivy) y rechaza si hay alguno (PLA-28).
#
#   scripts/escanear-secretos.sh repositorio [carpeta] [--historial]
#   scripts/escanear-secretos.sh imagen <nombre[:etiqueta]>
#
# La salida nunca copia el secreto: gitleaks enmascara el 100 % de cada coincidencia y Trivy sustituye el valor por asteriscos.
# Las herramientas corren en contenedores fijados por versión y por «digest» (PLA-26), sin red ni secretos del entorno.
# Códigos de salida: 0 sin secretos, 1 con secretos, 2 error de uso o de ejecución.
set -euo pipefail

GITLEAKS_IMAGE="${GITLEAKS_IMAGE:-zricethezav/gitleaks:v8.24.3@sha256:e1b35e12a8c6fa8901f060459cfb6b2fc4c484d3afbe3b029733a3bbfab07055}"
TRIVY_IMAGE="${TRIVY_IMAGE:-aquasec/trivy:0.58.1@sha256:ab70a02200597efa04748f210f793936eb647cbcdb0ea69cc30b226d6f5a22c7}"

uso() {
  echo "Uso: $0 repositorio [carpeta] [--historial] | imagen <nombre[:etiqueta]>" >&2
  exit 2
}

# En Git Bash para Windows, Docker necesita la ruta de Windows y que no se convierta la del contenedor.
if [[ "$(uname -s)" == MINGW* || "$(uname -s)" == MSYS* ]]; then
  export MSYS_NO_PATHCONV=1
  ruta_docker() { (cd "$1" && pwd -W); }
  ruta_nativa() { cygpath -w "$1"; }
else
  ruta_docker() { (cd "$1" && pwd); }
  ruta_nativa() { echo "$1"; }
fi

comando="${1:-}"
[[ -n "$comando" ]] || uso
shift

case "$comando" in
  repositorio)
    carpeta="."
    historial=0
    for argumento in "$@"; do
      case "$argumento" in
        --historial) historial=1 ;;
        -*) uso ;;
        *) carpeta="$argumento" ;;
      esac
    done
    [[ -d "$carpeta" ]] || { echo "No existe la carpeta: $carpeta" >&2; exit 2; }
    montaje="$(ruta_docker "$carpeta")"
    opciones=(detect --source /repo --redact=100 --no-banner --verbose)
    [[ -f "$carpeta/.gitleaks.toml" ]] && opciones+=(--config /repo/.gitleaks.toml)
    # Sin --historial se revisa el árbol de trabajo (lo versionado y lo nuevo); con él, todos los commits.
    [[ "$historial" -eq 1 ]] || opciones+=(--no-git)
    docker run --rm --network none -v "$montaje:/repo:ro" "$GITLEAKS_IMAGE" "${opciones[@]}" && estado=0 || estado=$?
    # gitleaks sale con 1 si encuentra secretos; cualquier otro código es un error de ejecución.
    [[ "$estado" -le 1 ]] || estado=2
    exit "$estado"
    ;;

  imagen)
    imagen="${1:-}"
    [[ -n "$imagen" ]] || uso
    trabajo="$(mktemp -d)"
    trap 'rm -rf "$trabajo"' EXIT
    docker save -o "$(ruta_nativa "$trabajo")/imagen.tar" "$imagen" || { echo "No se pudo exportar la imagen: $imagen" >&2; exit 2; }
    montaje="$(ruta_docker "$trabajo")"
    # Trivy revisa las capas de la imagen y muestra cada coincidencia con el valor sustituido por asteriscos.
    docker run --rm --network none -v "$montaje:/work:ro" "$TRIVY_IMAGE" image --input /work/imagen.tar \
      --scanners secret --exit-code 1 --no-progress --skip-db-update --skip-java-db-update && estado=0 || estado=$?
    [[ "$estado" -le 1 ]] || estado=2
    exit "$estado"
    ;;

  *)
    uso
    ;;
esac
