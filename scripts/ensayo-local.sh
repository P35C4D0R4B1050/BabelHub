#!/usr/bin/env bash
# Ensayo del despliegue en tu propio equipo, ANTES de tocar Oracle (guía: docs/07-proceso-de-desarrollo/08-guia-primer-despliegue.md, paso 0).
# Reproduce lo que harás en las VMs con los mismos scripts y los mismos Compose: construye las tres imágenes, las publica en un registro local
# (así el despliegue usa un «digest» de verdad, como con GHCR), genera secretos de mentira con scripts/generar-secretos.sh, y despliega
# VM-INVENTARIO y VM-VENTAS con scripts/desplegar.sh (migraciones, TLS entre servicios, Redis con TLS, salud de cada componente).
#
#   bash scripts/ensayo-local.sh [--limpiar]        --limpiar: al terminar (o si ya hay un ensayo), borra contenedores, volúmenes y archivos del ensayo
#
# Necesita Docker (con Compose v2.24 o más) y bash en Linux, macOS o WSL (en Windows con Git Bash también funciona). No usa tu dominio, ni Oracle,
# ni ningún secreto real: todo vive en una carpeta temporal fuera del repositorio y los puertos son locales (127.0.0.1:5xxxx).
#
# Lo que NO prueba: Cloudflare, el túnel, Evolution API (WhatsApp), el almacén de objetos de Oracle ni los cobros reales. Los dos servicios del
# E-commerce corren en «Staging» (el entorno de revisión de la tienda por Culqi: se endurece como «Production» y solo afloja el cobro, con la
# pasarela simulada) y, al final, el ensayo ejecuta el comando «cuenta-de-prueba» con la imagen real (guía, §15.1).
set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TRABAJO="${ENSAYO_CARPETA:-${TMPDIR:-/tmp}/babelhub-ensayo}"
REGISTRO_IMAGE="${REGISTRO_IMAGE:-registry@sha256:a3d8aaa63ed8681a604f1dea0aa03f100d5895b6a58ace528858a7b332415373}"
CURL_IMAGE="${CURL_IMAGE:-curlimages/curl@sha256:3fb7e13e5d179a845c97d78204f3f1064d1907037f26a3ebf91710571151fd93}"
REGISTRO="localhost:5055"
LIMPIAR=0
[[ "${1:-}" == --limpiar ]] && LIMPIAR=1

limpiar() {
  for proyecto in inventario ventas; do
    BABELHUB_SECRETOS="$TRABAJO/x" IP_PRIVADA=127.0.0.1 IMAGEN_ECOMMERCE=x/x@sha256:0 IMAGEN_INVENTARIO=x/x@sha256:0 IMAGEN_POSTGRES=x/x@sha256:0 IP_VENTAS=127.0.0.1 IP_INVENTARIO=127.0.0.1 \
      docker compose -p "babelhub-$proyecto" -f "$RAIZ/infra/docker-compose.$proyecto.yml" down -v --remove-orphans >/dev/null 2>&1 || true
  done
  docker rm -f babelhub-registro-ensayo >/dev/null 2>&1 || true
  rm -rf "$TRABAJO"
  echo "Ensayo limpiado."
}
if [[ "$LIMPIAR" -eq 1 ]]; then limpiar; exit 0; fi

command -v docker >/dev/null || { echo "Error: falta Docker." >&2; exit 2; }
docker compose version >/dev/null 2>&1 || { echo "Error: falta Docker Compose v2." >&2; exit 2; }
[[ ! -e "$TRABAJO" ]] || { echo "Ya hay un ensayo en $TRABAJO. Bórralo con: bash scripts/ensayo-local.sh --limpiar" >&2; exit 1; }
mkdir -p "$TRABAJO"
paso() { echo; echo "==> $*"; }

# ---------- 1. Registro local y las tres imágenes ----------
paso "1/7 Registro local y construcción de las tres imágenes (la primera vez tarda varios minutos)"
docker run -d --name babelhub-registro-ensayo -p 127.0.0.1:5055:5000 "$REGISTRO_IMAGE" >/dev/null
sleep 2
declare -A DIGEST=()
publicar() { # nombre dockerfile contexto
  docker build --quiet --file "$2" --tag "$REGISTRO/babelhub-$1:ensayo" "$3" >/dev/null
  docker push "$REGISTRO/babelhub-$1:ensayo" >/dev/null
  DIGEST[$1]="$(docker image inspect --format '{{index .RepoDigests 0}}' "$REGISTRO/babelhub-$1:ensayo")"
  echo "    $1 -> ${DIGEST[$1]}"
}
publicar ecommerce "$RAIZ/src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api/Dockerfile" "$RAIZ"
publicar inventario "$RAIZ/src/BabelHub.Inventario/BabelHub.Inventario.Api/Dockerfile" "$RAIZ"
publicar postgres "$RAIZ/infra/postgres/Dockerfile" "$RAIZ/infra/postgres"

# ---------- 2. Secretos de mentira ----------
paso "2/7 Secretos de mentira (mismo generador que usarás de verdad)"
bash "$RAIZ/scripts/generar-secretos.sh" --destino "$TRABAJO/secretos" --ensayo --imagen-postgres "${DIGEST[postgres]}" >/dev/null
# Lo que en la realidad da un tercero (Gmail, Turnstile, el almacén, el túnel) se rellena con valores sin sentido de la forma correcta.
alea() { head -c $(( $1 * 3 )) /dev/urandom | base64 | tr -d '/+=\n' | head -c "$1"; }
for archivo in "$TRABAJO"/secretos/*/*.env; do
  salida="$(mktemp)"
  while IFS= read -r linea || [[ -n "$linea" ]]; do
    if [[ "$linea" == *=CAMBIAR ]]; then
      clave="${linea%%=*}"
      case "$clave" in
        *Endpoint | *ENDPOINT) valor="https://almacen.invalido.local/" ;;
        *OrigenDelAlmacen) valor="https://almacen.invalido.local" ;;
        *Turnstile__ClaveDeSitio) valor="0x4AAAAAAAdeSitioLocal" ;;
        *Smtp__Servidor) valor="smtp.invalido.local" ;;
        *Smtp__Cuenta | *Smtp__Remitente | AUX_SMTP_USUARIO | AUX_CORREO_DE | AUX_CORREO_PARA) valor="ensayo@invalido.local" ;;
        AUX_SMTP_URL) valor="smtps://smtp.invalido.local:465" ;;
        # Datos de contacto INVENTADOS para el ensayo (los reales son de la persona responsable y nunca van al repositorio).
        *Contacto__Telefono) valor="+51 900 000 000" ;;
        *Contacto__Correo) valor="contacto@invalido.local" ;;
        *Contacto__Direccion) valor="Calle de Ensayo 123, Lima" ;;
        *) valor="$(alea 40)" ;;
      esac
      echo "$clave=$valor" >>"$salida"
    else
      echo "$linea" >>"$salida"
    fi
  done <"$archivo"
  cat "$salida" >"$archivo"; rm -f "$salida"
done
# En el ensayo el E-commerce de VM-VENTAS llega a la base por el puerto local de VM-INVENTARIO.
sed -i.bak 's/Host=127.0.0.1;Port=5432/Host=host.docker.internal;Port=55432/' "$TRABAJO/secretos/ventas/ecommerce.env" && rm -f "$TRABAJO/secretos/ventas/ecommerce.env.bak"
sed -i.bak 's/^BABELHUB__Web__HostsPermitidos=.*/BABELHUB__Web__HostsPermitidos="gestion.localhost,ecommerce-gestion,localhost"/' "$TRABAJO/secretos/inventario/ecommerce-gestion.env" && rm -f "$TRABAJO/secretos/inventario/ecommerce-gestion.env.bak"

# ---------- 3. Lo que hace diferente a un equipo cualquiera de una VM de Oracle ----------
# Puertos del equipo (el 5432 y el 8080 suelen estar ocupados), las raíces públicas (se toman de la imagen del E-commerce) y un «stat» que dice que todo es de root.
cat >"$TRABAJO/inventario.override.yml" <<'EOF'
services:
  postgres:
    ports: !override ["127.0.0.1:55432:5432"]
  inventario:
    ports: !override ["127.0.0.1:58082:8443"]
  ecommerce-gestion:
    ports: !override ["127.0.0.1:58081:8080"]
EOF
cat >"$TRABAJO/ventas.override.yml" <<'EOF'
services:
  ecommerce:
    ports: !override ["127.0.0.1:58080:8080", "127.0.0.1:58443:8443"]
  redis-contadores:
    ports: !override ["127.0.0.1:56380:6380"]
  n8n:
    ports: !override ["127.0.0.1:55678:5678"]
EOF
MSYS_NO_PATHCONV=1 docker run --rm --entrypoint cat "${DIGEST[ecommerce]}" /etc/ssl/certs/ca-certificates.crt >"$TRABAJO/raices-publicas.crt"
printf '#!/usr/bin/env bash\nif [[ "$2" == "%%U %%a" ]]; then case "$3" in *aviso.env) echo "root 600" ;; *) echo "root 755" ;; esac; else echo 600; fi\n' >"$TRABAJO/stat-ensayo.sh"
chmod +x "$TRABAJO/stat-ensayo.sh"
export BABELHUB_ENSAYO=1 STAT="$TRABAJO/stat-ensayo.sh" CHOWN=true BABELHUB_RAICES_PUBLICAS="$TRABAJO/raices-publicas.crt"
export IP_PRIVADA=127.0.0.1 IMAGEN_ECOMMERCE="${DIGEST[ecommerce]}" IMAGEN_INVENTARIO="${DIGEST[inventario]}" IMAGEN_POSTGRES="${DIGEST[postgres]}"

# ---------- 4 y 5. Despliegue de las dos VMs ----------
paso "3/7 VM-INVENTARIO: PostgreSQL, migraciones, Inventario y réplica de gestión"
mkdir -p "$TRABAJO/cron"
bash "$RAIZ/scripts/desplegar.sh" inventario --secretos "$TRABAJO/secretos/inventario" --cron "$TRABAJO/cron" --extra "$TRABAJO/inventario.override.yml" --espera 240

paso "4/7 VM-VENTAS: E-commerce, Redis (caché y contadores con TLS) y n8n (sin Evolution ni el túnel, que necesitan datos reales)"
bash "$RAIZ/scripts/desplegar.sh" ventas --secretos "$TRABAJO/secretos/ventas" --cron "$TRABAJO/cron" --extra "$TRABAJO/ventas.override.yml" --espera 240 --sin evolution-api,cloudflared

# ---------- 6. Humo ----------
paso "5/7 Humo: la salud de cada servicio por su puerto HTTPS, validando el certificado con la CA privada"
ca="$TRABAJO/secretos/ventas/tls-ca.pem"
humo() { # red servicio puerto host-de-la-peticion
  MSYS_NO_PATHCONV=1 docker run --rm --network "$1" -v "$ca:/ca.pem:ro" "$CURL_IMAGE" -sS --max-time 20 --cacert /ca.pem ${4:+-H "Host: $4"} "https://$2:$3/health/ready" -w '  [HTTP %{http_code}]\n'
}
echo -n "    Inventario (https://inventario:8443): ";  humo babelhub-inventario_default inventario 8443
echo -n "    E-commerce (https://ecommerce:8443):  ";  humo babelhub-ventas_default ecommerce 8443 localhost
paso "6/7 La tienda en Staging: el aviso de pagos simulados y la cuenta de prueba (con la imagen real)"
echo -n "    Aviso «Pagos simulados» en la portada: "
MSYS_NO_PATHCONV=1 docker run --rm --network babelhub-ventas_default -v "$ca:/ca.pem:ro" "$CURL_IMAGE" -sS --max-time 20 --cacert /ca.pem -H "Host: localhost" -H "X-Forwarded-Proto: https" "https://ecommerce:8443/" | grep -c "data-bh-pagos-simulados" || true
echo -n "    Redirección a HTTPS de lo que el borde declara HTTP: "
MSYS_NO_PATHCONV=1 docker run --rm --network babelhub-ventas_default -v "$ca:/ca.pem:ro" "$CURL_IMAGE" -sS --max-time 20 --cacert /ca.pem -H "Host: localhost" -H "X-Forwarded-Proto: http" -o /dev/null -w '[HTTP %{http_code}] -> %{redirect_url}\n' "https://ecommerce:8443/catalogo"
# El comando de consola de la cuenta de prueba corre dentro de la imagen del servicio, con su entorno (el mismo que usarás en la VM); la contraseña se oculta aquí (en la VM se muestra una sola vez).
salida_cuenta="$(BABELHUB_SECRETOS="$TRABAJO/secretos/ventas" IP_PRIVADA=127.0.0.1 docker compose -p babelhub-ventas --project-directory "$RAIZ/infra" -f "$RAIZ/infra/docker-compose.ventas.yml" -f "$TRABAJO/ventas.override.yml" run --rm --no-deps --no-TTY ecommerce cuenta-de-prueba 2>&1)" || { echo "$salida_cuenta" >&2; echo "Error: cuenta-de-prueba falló." >&2; exit 1; }
printf '%s\n' "$salida_cuenta" | sed -E 's/^(  contraseña: ).*/\1[oculta en el ensayo]/' | sed 's/^/    /'
paso "7/7 Listo. Los dos servicios respondieron «listo» (200) y los certificados se validaron."
echo "    Para borrar todo: bash scripts/ensayo-local.sh --limpiar"
