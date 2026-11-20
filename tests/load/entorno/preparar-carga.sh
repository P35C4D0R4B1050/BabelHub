#!/usr/bin/env bash
# Prepara el entorno LOCAL de las pruebas de carga (K6-1): secretos aleatorios solo de esta máquina, FUERA del repositorio, en una carpeta hermana del repositorio (.../babelhub-carga: Docker Desktop no comparte la carpeta AppData del usuario),
# certificado autofirmado, PostgreSQL y el Inventario en contenedores con los límites de VM-INVENTARIO, la base migrada y el servidor de claves de carga.
# Uso: bash tests/load/entorno/preparar-carga.sh   (apagar: bash tests/load/entorno/bajar-carga.sh)
set -euo pipefail
cd "$(dirname "$0")/../../.."
export CARGA_CARPETA="${CARGA_CARPETA:-$(cygpath -m "$(cd "$(git rev-parse --git-common-dir)/../.." && pwd)")/babelhub-carga}"
mkdir -p "$CARGA_CARPETA/certificado"
env_host="$CARGA_CARPETA/env"; env_cont="$CARGA_CARPETA/env-contenedor"
alea() { head -c 36 /dev/urandom | base64 | tr -d '/+=' | head -c "${1:-40}"; }
versionada() { printf 'v1:%s' "$(head -c 32 /dev/urandom | base64)"; }
poner() { grep -q "^$1=" "$env_host" 2>/dev/null || printf "%s='%s'\n" "$1" "$2" >> "$env_host"; }
touch "$env_host"
poner POSTGRES_ADMIN_USER postgres
poner POSTGRES_ADMIN_PASSWORD "$(alea 24)"
poner POSTGRES_ECOMMERCE_PASSWORD "$(alea 24)"
poner POSTGRES_INVENTARIO_PASSWORD "$(alea 24)"
poner POSTGRES_PORT 56432
poner CARGA_PUERTO 8443
poner CARGA_CLAVE_PFX "$(alea 24)"
poner CARGA_PUERTO_CLAVES 8083
set -a; source "$env_host"; set +a
base="Host=localhost;Port=$POSTGRES_PORT"
poner BABELHUB__Inventario__Base__Aplicacion "$base;Database=inventario;Username=inventario_acceso;Password=$(alea 32)"
poner BABELHUB__Inventario__Base__DepuracionDeAjustes "$base;Database=inventario;Username=inventario_depurador_ajustes;Password=$(alea 32)"
poner BABELHUB__Inventario__Base__DepuracionDeAuditoria "$base;Database=inventario;Username=inventario_depurador_auditoria;Password=$(alea 32)"
poner BABELHUB__Migrador__Inventario__Propietaria "$base;Database=inventario;Username=$POSTGRES_ADMIN_USER;Password=$POSTGRES_ADMIN_PASSWORD"
poner BABELHUB__Inventario__Ecommerce__Direccion "http://host.docker.internal:8080/"
poner BABELHUB__Inventario__ClaveDeBajas "$(alea 40)"
poner BABELHUB__Auditoria__ClaveDeCursor "$(alea 40)"
poner BABELHUB__Seguridad__ClaveDeHuella "$(versionada)"
poner BABELHUB__Identidad__UrlDeLaClaveDeVerificacion "http://localhost:$CARGA_PUERTO_CLAVES/jwks.json"
poner BABELHUB__Identidad__UrlDeLasRevocaciones "http://localhost:$CARGA_PUERTO_CLAVES/revocaciones"
for alcance in LecturaDeDisponibilidad OperacionDeUsuario LecturaDeContacto ExpiracionDeReservas ConsultaDePendientes BajaDeTitular OrigenReenviado ConsultaDeAuditoria LecturaDeAlertas MarcarAlerta Historial SolicitarAviso; do
  poner "BABELHUB__Seguridad__Credenciales__${alcance}" "$(alea 40)"
done
set -a; source "$env_host"; set +a
# El contenedor ve la base por el nombre del servicio; el migrador (en el anfitrión), por el puerto publicado. Sin las variables del migrador ni las de Docker.
grep -E '^BABELHUB__' "$env_host" | grep -v '^BABELHUB__Migrador__' | sed "s#Host=localhost;Port=$POSTGRES_PORT#Host=postgres;Port=5432#" > "$env_cont"

if [ ! -f "$CARGA_CARPETA/certificado/carga.pfx" ]; then
  MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -days 7 -subj "/CN=localhost" -keyout "$CARGA_CARPETA/certificado/k.pem" -out "$CARGA_CARPETA/certificado/c.pem" 2>/dev/null
  openssl pkcs12 -export -inkey "$CARGA_CARPETA/certificado/k.pem" -in "$CARGA_CARPETA/certificado/c.pem" -out "$CARGA_CARPETA/certificado/carga.pfx" -passout "pass:$CARGA_CLAVE_PFX"
  rm -f "$CARGA_CARPETA/certificado/k.pem"
fi
export CARGA_ENV_CONTENEDOR="$env_cont"
compose() { docker compose -p bhcarga -f tests/load/entorno/docker-compose.carga.yml --env-file "$env_host" "$@"; }
compose build inventario
compose up -d postgres
for i in $(seq 1 30); do compose exec -T postgres pg_isready -U "$POSTGRES_ADMIN_USER" >/dev/null 2>&1 && break; sleep 2; done
dotnet run --project src/BabelHub.Inventario/BabelHub.Inventario.Api -- migrar
compose exec -T postgres psql -U "$POSTGRES_ADMIN_USER" -d inventario -qc "CREATE EXTENSION IF NOT EXISTS pg_stat_statements" >/dev/null
mkdir -p "$CARGA_CARPETA/claves"
node tests/load/entorno/servidor-de-claves.mjs emitir "$CARGA_CARPETA/claves" 1 >/dev/null
compose up -d inventario claves
for i in $(seq 1 40); do curl -fsk "https://localhost:$CARGA_PUERTO/health/live" >/dev/null 2>&1 && { echo "entorno de carga listo en https://localhost:$CARGA_PUERTO"; exit 0; }; sleep 2; done
echo "el Inventario no respondió: docker compose -p bhcarga logs inventario" >&2; exit 1
