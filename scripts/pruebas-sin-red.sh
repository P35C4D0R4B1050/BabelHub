#!/usr/bin/env bash
# Ejecuta las pruebas de Dominio y Aplicación sin base de datos ni red (PLA-01): cada ensamblado de pruebas corre en un contenedor con
# la red desactivada (`docker run --network none`). Una prueba que intente conectarse falla, y con ella toda la ejecución.
#
#   scripts/pruebas-sin-red.sh                  compila y ejecuta las pruebas unitarias de los dos servicios
#   scripts/pruebas-sin-red.sh <ensamblado>...  ejecuta los ensamblados de pruebas indicados (.dll ya compilados)
#
# Las pruebas marcadas [Trait("Entorno", "ConRepositorio")] leen archivos versionados del repositorio (tools/carga-inicial): no caben en este contenedor, que solo monta los binarios, y corren en el paso de cobertura de dotnet test BabelHub.sln.
# Las pruebas unitarias de cada servicio referencian solo su Dominio y su Aplicación (lo vigila la prueba de arquitectura), así que
# este contenedor no necesita nada más. La imagen del SDK está fijada por «digest» (PLA-26).
# Códigos de salida: 0 todas pasan, 1 alguna prueba falla (también por intentar usar la red), 2 error de uso o de ejecución.
set -euo pipefail

SDK_IMAGE="${SDK_IMAGE:-mcr.microsoft.com/dotnet/sdk:10.0@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb}"
PROYECTOS_POR_DEFECTO=(
  "tests/unit/BabelHub.Ecommerce.UnitTests"
  "tests/unit/BabelHub.Inventario.UnitTests"
)

# En Git Bash para Windows, Docker necesita la ruta de Windows y que no se convierta la del contenedor.
if [[ "$(uname -s)" == MINGW* || "$(uname -s)" == MSYS* ]]; then
  export MSYS_NO_PATHCONV=1
  ruta_docker() { (cd "$1" && pwd -W); }
else
  ruta_docker() { (cd "$1" && pwd); }
fi

ensamblados=("$@")
if [[ ${#ensamblados[@]} -eq 0 ]]; then
  for proyecto in "${PROYECTOS_POR_DEFECTO[@]}"; do
    nombre="$(basename "$proyecto")"
    dotnet build "$proyecto" --nologo -warnaserror -c Debug >/dev/null || { echo "No se pudo compilar $proyecto" >&2; exit 2; }
    ensamblados+=("$proyecto/bin/Debug/net10.0/$nombre.dll")
  done
fi

estado_total=0
for ensamblado in "${ensamblados[@]}"; do
  [[ -f "$ensamblado" ]] || { echo "No existe el ensamblado: $ensamblado" >&2; exit 2; }
  carpeta="$(dirname "$ensamblado")"
  archivo="$(basename "$ensamblado")"
  montaje="$(ruta_docker "$carpeta")"
  echo "== $archivo (sin red)"
  # Solo lectura: las pruebas no escriben junto a sus binarios; el SDK y los resultados usan /tmp.
  docker run --rm --network none \
    -e DOTNET_CLI_HOME=/tmp -e DOTNET_NOLOGO=1 -e DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    -v "$montaje:/pruebas:ro" -w /pruebas "$SDK_IMAGE" \
    dotnet test "/pruebas/$archivo" --nologo --filter "Entorno!=ConRepositorio" --results-directory /tmp/resultados && estado=0 || estado=$?
  if [[ "$estado" -ne 0 ]]; then
    estado_total=1
  fi
done

exit "$estado_total"
