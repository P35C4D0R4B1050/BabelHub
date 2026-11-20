#!/usr/bin/env bash
# Recrea el Inventario (y la base) con otros límites de CPU y memoria para medir su efecto. Uso: ajustar-limites.sh <cpus-inventario> [<cpus-postgres> [<mem-inventario>]]
# Por omisión el entorno usa los límites declarados en infra/docker-compose.inventario.yml (0.3 CPU y 350m el Inventario; 0.8 CPU y 2g la base).
set -euo pipefail
cd "$(dirname "$0")/../../.."
CARGA_CARPETA="${CARGA_CARPETA:-$(cygpath -m "$(cd "$(git rev-parse --git-common-dir)/../.." && pwd)")/babelhub-carga}"
set -a; source "$CARGA_CARPETA/env"; set +a
export CARGA_CARPETA CARGA_ENV_CONTENEDOR="$CARGA_CARPETA/env-contenedor"
export CARGA_CPUS_INVENTARIO="${1:?cpus del Inventario}" CARGA_CPUS_POSTGRES="${2:-0.8}" CARGA_MEM_INVENTARIO="${3:-350m}"
docker compose -p bhcarga -f tests/load/entorno/docker-compose.carga.yml --env-file "$CARGA_CARPETA/env" up -d --force-recreate postgres inventario claves
for i in $(seq 1 40); do curl -fsk "https://localhost:$CARGA_PUERTO/health/live" >/dev/null 2>&1 && break; sleep 2; done
docker inspect bhcarga-inventario-1 bhcarga-postgres-1 --format '{{.Name}} cpus={{.HostConfig.NanoCpus}} mem={{.HostConfig.Memory}}'
