#!/usr/bin/env bash
# Arranca un host local para las pruebas E2E (solo Development, con el archivo de entorno de preparar-entorno.sh). Uso: arrancar-host.sh ecommerce|inventario
# E2E_TLS=1 sirve el E-commerce por HTTPS en https://localhost:8443 con el certificado de desarrollo de .NET (`dotnet dev-certs https`): el navegador envía entonces un Origin
# https que coincide con el esquema de la solicitud, como detrás del borde real (sin él, los POST de formulario se rechazan: Origin http contra un host que cree estar en https).
set -euo pipefail
cd "$(dirname "$0")/../../.."
set -a; source "${E2E_ENV_FILE:-${LOCALAPPDATA:-$HOME}/babelhub-e2e/env}"; set +a
# El servicio se niega a arrancar con las credenciales del migrador en su entorno (COMP-2): fuera.
for v in $(compgen -v BABELHUB__Migrador__); do unset "$v"; done
export ASPNETCORE_ENVIRONMENT=Development
case "$1" in
  ecommerce)
    if [ "${E2E_HOST:-tienda}" = "gestion" ]; then
      # El host de la gestión (personal): otro proceso sobre la misma base, con su propia cookie de sesión y su propio puerto.
      export ASPNETCORE_URLS="https://localhost:8444"
      export BABELHUB__Web__Host="gestion"
      export BABELHUB__Web__HostsPermitidos="localhost:8444"
    elif [ "${E2E_TLS:-0}" = "1" ]; then
      export ASPNETCORE_URLS="https://localhost:8443;http://localhost:8080"
      export BABELHUB__Web__HostsPermitidos="localhost:8443"
    else
      export ASPNETCORE_URLS="http://localhost:8080"
    fi
    exec dotnet run --no-build --no-launch-profile --project src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api ;;
  inventario) export ASPNETCORE_URLS="http://localhost:8082"; exec dotnet run --no-build --no-launch-profile --project src/BabelHub.Inventario/BabelHub.Inventario.Api ;;
  *) echo "uso: $0 ecommerce|inventario" >&2; exit 2 ;;
esac
