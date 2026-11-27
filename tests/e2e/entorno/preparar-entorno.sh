#!/usr/bin/env bash
# Prepara el entorno LOCAL de las pruebas E2E: genera el archivo de entorno (fuera del repositorio) con secretos aleatorios solo para esta máquina, levanta
# PostgreSQL y Redis aislados (proyecto `bhe2e`, puertos 55432 y 56379) y migra las bases de ambos servicios. No toca el Compose de nadie más.
# Solo desarrollo: ningún valor de aquí es un dato real. Uso: bash tests/e2e/entorno/preparar-entorno.sh
set -euo pipefail
cd "$(dirname "$0")/../../.."

# El archivo de entorno vive FUERA del repositorio (secretos aleatorios solo de esta máquina; el escáner de secretos recorre el árbol de trabajo).
archivo="${E2E_ENV_FILE:-${LOCALAPPDATA:-$HOME}/babelhub-e2e/env}"
mkdir -p "$(dirname "$archivo")"
alea() { head -c 36 /dev/urandom | base64 | tr -d '/+=' | head -c "${1:-40}"; }
versionada() { printf 'v1:%s' "$(head -c 32 /dev/urandom | base64)"; }
poner() { grep -q "^$1=" "$archivo" 2>/dev/null || printf "%s='%s'\n" "$1" "$2" >> "$archivo"; }

touch "$archivo"
poner POSTGRES_ADMIN_USER postgres
poner POSTGRES_ADMIN_PASSWORD "$(alea 24)"
poner POSTGRES_ECOMMERCE_PASSWORD "$(alea 24)"
poner POSTGRES_INVENTARIO_PASSWORD "$(alea 24)"
poner POSTGRES_PORT 55432
poner REDIS_PORT 56379
poner BABELHUB__Seguridad__ClaveDeRedisDeLimites "$(alea 40)"
set -a; source "$archivo"; set +a
base="Host=localhost;Port=$POSTGRES_PORT"

# E-commerce
poner BABELHUB__Ecommerce__Base__Aplicacion "$base;Database=ecommerce;Username=ecommerce_acceso;Password=$(alea 32)"
poner BABELHUB__Ecommerce__Base__DepuracionDeAuditoria "$base;Database=ecommerce;Username=ecommerce_depurador_auditoria;Password=$(alea 32)"
poner BABELHUB__Ecommerce__Base__DepuracionDeRegistros "$base;Database=ecommerce;Username=ecommerce_depurador_registros;Password=$(alea 32)"
poner BABELHUB__Migrador__Ecommerce__Propietaria "$base;Database=ecommerce;Username=$POSTGRES_ADMIN_USER;Password=$POSTGRES_ADMIN_PASSWORD"
poner BABELHUB__Ecommerce__Inventario__Direccion "http://localhost:8082/"
poner BABELHUB__Auditoria__ClaveDeCursor "$(alea 40)"
poner BABELHUB__Catalogo__ClaveDeIngresos "$(alea 40)"
poner BABELHUB__Correo__ClaveDeLaCola "$(alea 40)"
poner BABELHUB__Seguridad__ClaveDeHuella "$(versionada)"
poner BABELHUB__Seguridad__Turnstile__Secreto "$(alea 40)"
poner BABELHUB__Seguridad__Turnstile__Hostnames "localhost"
poner BABELHUB__Seguridad__Turnstile__ClaveDeSitio "0x4AAAAAAAdeSitioLocal"
poner BABELHUB__Identidad__ClaveDeCifradoDeClaves "$(versionada)"
poner BABELHUB__Identidad__Pimienta "$(versionada)"
# Clave aleatoria del sembrador de sesiones de desarrollo (solo Debug + Development, tests/e2e/README.md): sin ella el endpoint ni se mapea.
poner BABELHUB__Seguridad__ServicioSinHttps true
poner BABELHUB__Ecommerce__ProxiesConocidos "127.0.0.1,::1"
poner BABELHUB__Sembrador__Clave "$(alea 40)"
poner BABELHUB__Web__Host tienda
poner BABELHUB__Web__HostsPermitidos "localhost"
poner BABELHUB__Web__ClaveDeSesionesTienda "$(alea 40)"
poner BABELHUB__Web__ClaveDeSesionesGestion "$(alea 40)"
poner BABELHUB__Carrito__CostoDeEnvio "30.00"
poner BABELHUB__Pagos__Pasarela simulador
# Sin operador logístico real (DD-22): el simulador genera las guías de los envíos.
poner BABELHUB__Envios__Operador simulador
poner BABELHUB__Pagos__ClaveDeIntentos "$(alea 40)"
for almacen in Catalogo Supresiones; do
  poner "BABELHUB__Almacen__${almacen}__Endpoint" "https://almacen.invalido.local/"
  poner "BABELHUB__Almacen__${almacen}__Region" "sa-lima-1"
  poner "BABELHUB__Almacen__${almacen}__ClaveDeAcceso" "$(alea 32)"
  poner "BABELHUB__Almacen__${almacen}__ClaveSecreta" "$(alea 32)"
done
# Credenciales entre servicios: el mismo valor en el E-commerce y en el Inventario.
for alcance in LecturaDeDisponibilidad OperacionDeUsuario LecturaDeContacto ExpiracionDeReservas ConsultaDePendientes BajaDeTitular OrigenReenviado ConsultaDeAuditoria LecturaDeAlertas MarcarAlerta Historial SolicitarAviso; do
  poner "BABELHUB__Seguridad__Credenciales__${alcance}" "$(alea 40)"
done

# Inventario
poner BABELHUB__Inventario__Base__Aplicacion "$base;Database=inventario;Username=inventario_acceso;Password=$(alea 32)"
poner BABELHUB__Inventario__Base__DepuracionDeAjustes "$base;Database=inventario;Username=inventario_depurador_ajustes;Password=$(alea 32)"
poner BABELHUB__Inventario__Base__DepuracionDeAuditoria "$base;Database=inventario;Username=inventario_depurador_auditoria;Password=$(alea 32)"
poner BABELHUB__Migrador__Inventario__Propietaria "$base;Database=inventario;Username=$POSTGRES_ADMIN_USER;Password=$POSTGRES_ADMIN_PASSWORD"
poner BABELHUB__Inventario__Ecommerce__Direccion "http://localhost:8080/"
poner BABELHUB__Inventario__ClaveDeBajas "$(alea 40)"
poner BABELHUB__Identidad__UrlDeLaClaveDeVerificacion "http://localhost:8080/.well-known/jwks.json"
poner BABELHUB__Identidad__UrlDeLasRevocaciones "http://localhost:8080/.well-known/revocaciones"

docker compose -p bhe2e -f infra/docker-compose.local.yml --env-file "$archivo" up -d postgres redis redis-contadores
set -a; source "$archivo"; set +a
for i in $(seq 1 30); do
  docker compose -p bhe2e -f infra/docker-compose.local.yml --env-file "$archivo" exec -T postgres pg_isready -U "$POSTGRES_ADMIN_USER" >/dev/null 2>&1 && break
  sleep 2
done
# Las variables de la cuenta propietaria de cada servicio se pasan solo a su migración.
BABELHUB__Migrador__Inventario__Propietaria= dotnet run --project src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api -- migrar
BABELHUB__Migrador__Ecommerce__Propietaria= dotnet run --project src/BabelHub.Inventario/BabelHub.Inventario.Api -- migrar
echo "entorno e2e listo: bases migradas"
