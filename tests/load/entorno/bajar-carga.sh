#!/usr/bin/env bash
# Apaga el entorno de carga y el servidor de claves (conserva el volumen; con -v lo borra).
set -euo pipefail
cd "$(dirname "$0")/../../.."
CARGA_CARPETA="${CARGA_CARPETA:-$(cygpath -m "$(cd "$(git rev-parse --git-common-dir)/../.." && pwd)")/babelhub-carga}"
set -a; source "$CARGA_CARPETA/env"; set +a
export CARGA_ENV_CONTENEDOR="$CARGA_CARPETA/env-contenedor"
docker compose -p bhcarga -f tests/load/entorno/docker-compose.carga.yml --env-file "$CARGA_CARPETA/env" down "$@"
