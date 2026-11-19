#!/usr/bin/env bash
# Arranca un host local para las pruebas E2E (solo Development, con el archivo de entorno de preparar-entorno.sh). Uso: arrancar-host.sh ecommerce|inventario
set -euo pipefail
cd "$(dirname "$0")/../../.."
set -a; source "${E2E_ENV_FILE:-${LOCALAPPDATA:-$HOME}/babelhub-e2e/env}"; set +a
# El servicio se niega a arrancar con las credenciales del migrador en su entorno (COMP-2): fuera.
for v in $(compgen -v BABELHUB__Migrador__); do unset "$v"; done
export ASPNETCORE_ENVIRONMENT=Development
case "$1" in
  ecommerce) export ASPNETCORE_URLS="http://localhost:8080"; exec dotnet run --no-build --no-launch-profile --project src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api ;;
  inventario) export ASPNETCORE_URLS="http://localhost:8082"; exec dotnet run --no-build --no-launch-profile --project src/BabelHub.Inventario/BabelHub.Inventario.Api ;;
  *) echo "uso: $0 ecommerce|inventario" >&2; exit 2 ;;
esac
