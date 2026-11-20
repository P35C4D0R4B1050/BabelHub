#!/usr/bin/env bash
# Recrea el Inventario (y la base) con otros límites de CPU y memoria para medir su efecto.
# Uso: ajustar-limites.sh [<cpus-inventario=0.6> [<cpus-postgres=0.4> [<mem-inventario=512m> [<tope-del-grupo>]]]]
# Por omisión el entorno usa el dimensionamiento declarado de VM-INVENTARIO (DD-17, infra/docker-compose.inventario.yml): Inventario 0.6 CPU y 512m, base 0.4 CPU y 2g, y el tope de 16 conexiones del grupo. Antes de DD-17 eran 0.3 CPU y 350m el Inventario y 0.8 CPU la base (histórico: `ajustar-limites.sh 0.3 0.8 350m`).
# Con <tope-del-grupo> se fija `Maximum Pool Size` del Inventario y el CONNECTION LIMIT de su cuenta (tope + 4), para medir cuánta concurrencia de base sostiene más CPU.
set -euo pipefail
cd "$(dirname "$0")/../../.."
CARGA_CARPETA="${CARGA_CARPETA:-$(cygpath -m "$(cd "$(git rev-parse --git-common-dir)/../.." && pwd)")/babelhub-carga}"
set -a; source "$CARGA_CARPETA/env"; set +a
export CARGA_CARPETA CARGA_ENV_CONTENEDOR="$CARGA_CARPETA/env-contenedor"
export CARGA_CPUS_INVENTARIO="${1:-0.6}" CARGA_CPUS_POSTGRES="${2:-0.4}" CARGA_MEM_INVENTARIO="${3:-512m}"
pool="${4:-}"
compose() { docker compose -p bhcarga -f tests/load/entorno/docker-compose.carga.yml --env-file "$CARGA_CARPETA/env" "$@"; }

# El tope del grupo va en la cadena de la cuenta de la aplicación (sin tocar la del anfitrión); sin valor se quita y rige el de 16 por omisión.
sed -i "s/;Maximum Pool Size=[0-9]*//" "$CARGA_ENV_CONTENEDOR"
if [ -n "$pool" ]; then
  sed -i "/^BABELHUB__Inventario__Base__Aplicacion=/ s/'\$/;Maximum Pool Size=$pool'/" "$CARGA_ENV_CONTENEDOR"
fi

compose up -d --force-recreate postgres
for i in $(seq 1 30); do compose exec -T postgres pg_isready -U "$POSTGRES_ADMIN_USER" >/dev/null 2>&1 && break; sleep 2; done
limite=$(( ${pool:-16} + 4 ))
compose exec -T postgres psql -U "$POSTGRES_ADMIN_USER" -d inventario -qc "ALTER ROLE inventario_acceso CONNECTION LIMIT $limite" >/dev/null
compose up -d --force-recreate inventario claves
for i in $(seq 1 40); do curl -fsk "https://localhost:$CARGA_PUERTO/health/live" >/dev/null 2>&1 && break; sleep 2; done
docker inspect bhcarga-inventario-1 bhcarga-postgres-1 --format '{{.Name}} cpus={{.HostConfig.NanoCpus}} mem={{.HostConfig.Memory}}'
echo "tope del grupo: ${pool:-16 (por omisión)}; límite de la cuenta: $limite"
