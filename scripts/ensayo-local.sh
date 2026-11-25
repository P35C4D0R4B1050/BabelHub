#!/usr/bin/env bash
# Ensayo del despliegue en tu propio equipo, ANTES de tocar Oracle (guía: docs/07-proceso-de-desarrollo/08-guia-primer-despliegue.md, paso 0).
# Reproduce lo que harás en las VMs con los mismos scripts y los mismos Compose: construye las tres imágenes, las publica en un registro local
# (así el despliegue usa un «digest» de verdad, como con GHCR), genera secretos de mentira con scripts/generar-secretos.sh, y despliega
# VM-INVENTARIO y VM-VENTAS con scripts/desplegar.sh (migraciones, TLS entre servicios, Redis con TLS, salud de cada componente).
#
#   bash scripts/ensayo-local.sh [--limpiar]        --limpiar: al terminar (o si ya hay un ensayo), borra contenedores, volúmenes y archivos del ensayo
#
# Necesita Docker (con Compose v2.24 o más) y bash en Linux, macOS o WSL (en Windows con Git Bash también funciona). No usa tu dominio, ni Oracle,
# ni ningún secreto real: todo vive en una carpeta temporal fuera del repositorio y los puertos son locales (127.0.0.1:3xxxx, por debajo del rango dinámico de Windows).
#
# Prueba también el TLS de PostgreSQL (AM24, paso 6b): rechazo sin TLS, con otra CA y con otro nombre; conexiones de red todas cifradas; respaldo por el socket local; y Evolution API (Prisma) si su imagen está en el equipo (ENSAYO_EVOLUTION=1 la descarga).
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
        *Smtp__Cuenta | *Smtp__Remitente | AUX_SMTP_USUARIO | AUX_CORREO_DE | AUX_CORREO_PARA) valor="ensayo@invalido.invalid" ;;
        AUX_SMTP_URL) valor="smtps://smtp.invalido.local:465" ;;
        # El contacto del comercio (COM-02): ficticios claramente falsos y entre comillas; un valor aleatorio no pasaría la validación de formato y el servicio no arrancaría.
        *Contacto__Telefono) valor='"999 000 000"' ;;
        *Contacto__Correo) valor="contacto@ejemplo.test" ;;
        *Contacto__Direccion) valor='"Jr. Ejemplo 123, Ayacucho (dato de ejemplo)"' ;;
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
sed -i.bak 's/Host=127.0.0.1;Port=5432/Host=host.docker.internal;Port=35432/' "$TRABAJO/secretos/ventas/ecommerce.env" && rm -f "$TRABAJO/secretos/ventas/ecommerce.env.bak"
sed -i.bak 's/^BABELHUB__Web__HostsPermitidos=.*/BABELHUB__Web__HostsPermitidos="gestion.localhost,ecommerce-gestion,localhost"/' "$TRABAJO/secretos/inventario/ecommerce-gestion.env" && rm -f "$TRABAJO/secretos/inventario/ecommerce-gestion.env.bak"

# ---------- 3. Lo que hace diferente a un equipo cualquiera de una VM de Oracle ----------
# Puertos del equipo (el 5432 y el 8080 suelen estar ocupados), las raíces públicas (se toman de la imagen del E-commerce) y un «stat» que dice que todo es de root.
cat >"$TRABAJO/inventario.override.yml" <<'EOF'
services:
  postgres:
    ports: !override ["127.0.0.1:35432:5432"]
  inventario:
    ports: !override ["127.0.0.1:38082:8443"]
  ecommerce-gestion:
    ports: !override ["127.0.0.1:38081:8080"]
EOF
cat >"$TRABAJO/ventas.override.yml" <<'EOF'
services:
  ecommerce:
    ports: !override ["127.0.0.1:38080:8080", "127.0.0.1:38443:8443"]
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
# En Git Bash para Windows, Docker necesita la ruta de Windows (C:/…): con MSYS_NO_PATHCONV=1 una ruta como /tmp/… se montaría vacía y curl diría «error adding trust anchors from file: /ca.pem».
ca_docker="$ca"; if [[ "$(uname -s)" == MINGW* || "$(uname -s)" == MSYS* ]]; then ca_docker="$(cygpath -m "$ca")"; fi
humo() { # red servicio puerto host-de-la-peticion
  MSYS_NO_PATHCONV=1 docker run --rm --network "$1" -v "$ca_docker:/ca.pem:ro" "$CURL_IMAGE" -sS --max-time 20 --cacert /ca.pem ${4:+-H "Host: $4"} "https://$2:$3/health/ready" -w '  [HTTP %{http_code}]\n'
}
echo -n "    Inventario (https://inventario:8443): ";  humo babelhub-inventario_default inventario 8443
echo -n "    E-commerce (https://ecommerce:8443):  ";  humo babelhub-ventas_default ecommerce 8443 localhost
paso "6/7 La tienda en Staging: el aviso de pagos simulados y la cuenta de prueba (con la imagen real)"
echo -n "    Aviso «Pagos simulados» en la portada: "
MSYS_NO_PATHCONV=1 docker run --rm --network babelhub-ventas_default -v "$ca_docker:/ca.pem:ro" "$CURL_IMAGE" -sS --max-time 20 --cacert /ca.pem -H "Host: localhost" -H "X-Forwarded-Proto: https" "https://ecommerce:8443/" | grep -c "data-bh-pagos-simulados" || true
echo -n "    Redirección a HTTPS de lo que el borde declara HTTP: "
MSYS_NO_PATHCONV=1 docker run --rm --network babelhub-ventas_default -v "$ca_docker:/ca.pem:ro" "$CURL_IMAGE" -sS --max-time 20 --cacert /ca.pem -H "Host: localhost" -H "X-Forwarded-Proto: http" -o /dev/null -w '[HTTP %{http_code}] -> %{redirect_url}\n' "https://ecommerce:8443/catalogo"
# El comando de consola de la cuenta de prueba corre dentro de la imagen del servicio, con su entorno (el mismo que usarás en la VM); la contraseña se oculta aquí (en la VM se muestra una sola vez).
salida_cuenta="$(BABELHUB_SECRETOS="$TRABAJO/secretos/ventas" IP_PRIVADA=127.0.0.1 docker compose -p babelhub-ventas --project-directory "$RAIZ/infra" -f "$RAIZ/infra/docker-compose.ventas.yml" -f "$TRABAJO/ventas.override.yml" run --rm --no-deps --no-TTY ecommerce cuenta-de-prueba 2>&1)" || { echo "$salida_cuenta" >&2; echo "Error: cuenta-de-prueba falló." >&2; exit 1; }
printf '%s\n' "$salida_cuenta" | sed -E 's/^(  contraseña: ).*/\1[oculta en el ensayo]/' | sed 's/^/    /'
echo -n "    Aviso «Los envíos son simulados» en la portada: "
MSYS_NO_PATHCONV=1 docker run --rm --network babelhub-ventas_default -v "$ca_docker:/ca.pem:ro" "$CURL_IMAGE" -sS --max-time 20 --cacert /ca.pem -H "Host: localhost" -H "X-Forwarded-Proto: https" "https://ecommerce:8443/" | grep -c "data-bh-envios-simulados" || true
# Los demás comandos de consola (PAG-43 a PAG-47), también con la imagen real; las contraseñas se ocultan aquí (en la VM se muestran una sola vez).
consola() { BABELHUB_SECRETOS="$TRABAJO/secretos/ventas" IP_PRIVADA=127.0.0.1 docker compose -p babelhub-ventas --project-directory "$RAIZ/infra" -f "$RAIZ/infra/docker-compose.ventas.yml" -f "$TRABAJO/ventas.override.yml" run --rm --no-deps --no-TTY ecommerce "$@" 2>&1 | sed -E 's/^(  contraseña[^:]*: ).*/\1[oculta en el ensayo]/' | sed 's/^/    /'; }
echo "    crear-administrador:"; consola crear-administrador --correo administracion@ensayo.example.test
echo "    cuenta-de-prueba --cantidad 2:"; consola cuenta-de-prueba --cantidad 2
echo "    cuenta-de-prueba --retirar --todas:"; consola cuenta-de-prueba --retirar --todas
# --purgar cancela cuentas y necesita el almacén de objetos del registro de supresiones (IDE-70), que este ensayo no levanta: aquí debe negarse con RegistroDeSupresionesNoDisponibleException y sin tocar nada.
echo "    cuenta-de-prueba --purgar (sin almacén de objetos en el ensayo: debe negarse sin tocar nada):"
# Con «pipefail», la negación esperada (salida distinta de 0) abortaría el script antes del paso 7: se captura y se comprueba el motivo.
salida_purga="$(consola cuenta-de-prueba --purgar || true)"
echo "$salida_purga"
if grep -q "RegistroDeSupresionesNoDisponibleException" <<<"$salida_purga"; then echo "    (negación esperada: correcto)"; else echo "    ERROR: --purgar no se negó por el motivo esperado." >&2; exit 1; fi

# ---------- TLS de PostgreSQL (AM24): lo que debe RECHAZARSE y lo que debe funcionar ----------
paso "6b/7 TLS de PostgreSQL: una conexión sin TLS, con otra CA o con el nombre equivocado se rechaza; con la CA y el nombre correctos, funciona"
MAL=0
esperar_rechazo() { # descripción salida-de-psql patrón: debe haber fallado con ese motivo
  if grep -qiE "$3" <<<"$2"; then echo "    ok   $1: rechazada ($(grep -oiE "$3" <<<"$2" | head -n 1))"; else echo "    ERROR $1: no se rechazó por el motivo esperado. Salida: $(head -c 300 <<<"$2")" >&2; MAL=1; fi
}
valor_de() { # archivo clave -> valor sin comillas (los secretos del ensayo son de mentira; igual no se imprimen)
  local linea; linea="$(grep -m1 "^$2=" "$1")" || return 1; linea="${linea#*=}"; linea="${linea#\"}"; printf '%s' "${linea%\"}"
}
cadena_de_la_cuenta="$(valor_de "$TRABAJO/secretos/ventas/ecommerce.env" BABELHUB__Ecommerce__Base__Aplicacion)"
export PGPASSWORD="$(sed -E 's/.*Password=([^;]*).*/\1/' <<<"$cadena_de_la_cuenta")"
# Otra CA (de mentira), para comprobar que no basta con «una CA cualquiera».
(cd "$TRABAJO" && MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -days 2 -subj "/CN=Otra CA de ensayo" -keyout otra-ca-clave.pem -out otra-ca.pem 2>/dev/null) || { echo "Error: no se pudo crear la CA ajena del ensayo (falta openssl)." >&2; exit 1; }
otra_ca_docker="$TRABAJO/otra-ca.pem"; ca_pg_docker="$ca"
if [[ "$(uname -s)" == MINGW* || "$(uname -s)" == MSYS* ]]; then otra_ca_docker="$(cygpath -m "$otra_ca_docker")"; ca_pg_docker="$(cygpath -m "$ca")"; fi
# El cliente es psql de la propia imagen de PostgreSQL, en una red aparte, hacia el puerto local de VM-INVENTARIO (35432); el nombre «host.docker.internal» es el que lleva el certificado en el ensayo.
cliente() { # cadena-de-conexión [consulta]
  MSYS_NO_PATHCONV=1 docker run --rm --add-host host.docker.internal:host-gateway --add-host nombre-equivocado.invalido:host-gateway -e PGPASSWORD -e PGCONNECT_TIMEOUT=10 \
    -v "$ca_pg_docker:/ca.pem:ro" -v "$otra_ca_docker:/otra-ca.pem:ro" --entrypoint psql "${DIGEST[postgres]}" "$1" -At -c "${2:-select 1}" 2>&1 || true
}
BASE="host=host.docker.internal port=35432 dbname=ecommerce user=ecommerce_acceso"
esperar_rechazo "sin TLS (sslmode=disable)" "$(cliente "$BASE sslmode=disable")" "no encryption|rejects connection"
esperar_rechazo "con TLS pero una CA que no es la de la cuenta (verify-full)" "$(cliente "$BASE sslmode=verify-full sslrootcert=/otra-ca.pem")" "certificate verify failed"
esperar_rechazo "con la CA correcta pero un nombre que el certificado no tiene (verify-full)" \
  "$(cliente "host=nombre-equivocado.invalido port=35432 dbname=ecommerce user=ecommerce_acceso sslmode=verify-full sslrootcert=/ca.pem")" "does not match host name|certificate verify failed"
esperar_rechazo "otra base distinta de las de la plataforma, aun con TLS correcto" "$(cliente "host=host.docker.internal port=35432 dbname=postgres user=ecommerce_acceso sslmode=verify-full sslrootcert=/ca.pem")" "pg_hba.conf rejects"
correcta="$(cliente "$BASE sslmode=verify-full sslrootcert=/ca.pem" "select 'tls=' || ssl || ' version=' || version from pg_stat_ssl where pid = pg_backend_pid()")"
if grep -q "^tls=true version=TLSv1.3$" <<<"$correcta"; then echo "    ok   con la CA y el nombre correctos (verify-full): conecta ($correcta)"; else echo "    ERROR: la conexión correcta no funcionó con TLS 1.3: $(head -c 300 <<<"$correcta")" >&2; MAL=1; fi
unset PGPASSWORD
# Ninguna conexión de red a la base va sin TLS (las de los servicios y las del migrador ya pasaron por aquí al desplegar).
ids_pg="$(docker ps -q --filter "label=com.docker.compose.project=babelhub-inventario" --filter "label=com.docker.compose.service=postgres")"
sin_tls="$(docker exec "$ids_pg" psql -U postgres -At -c "select count(*) from pg_stat_activity a join pg_stat_ssl s using (pid) where a.client_addr is not null and not s.ssl")"
con_tls="$(docker exec "$ids_pg" psql -U postgres -At -c "select count(*) from pg_stat_activity a join pg_stat_ssl s using (pid) where a.client_addr is not null and s.ssl")"
if [[ "$sin_tls" == 0 && "$con_tls" -gt 0 ]]; then echo "    ok   PostgreSQL ve $con_tls conexiones de red, todas con TLS (y $sin_tls sin TLS)"; else echo "    ERROR: conexiones de red sin TLS: $sin_tls (con TLS: $con_tls)" >&2; MAL=1; fi
# El respaldo y el archivado siguen operando por el socket local (sin red): la copia base y la rotación de un segmento de WAL.
bytes="$(docker exec "$ids_pg" sh -c 'pg_basebackup -D - -Ft -X fetch -c fast | wc -c')"
if [[ "$bytes" -gt 1000000 ]]; then echo "    ok   pg_basebackup (lo que usa respaldar.sh) por el socket local: $bytes bytes"; else echo "    ERROR: pg_basebackup no produjo una copia ($bytes bytes)" >&2; MAL=1; fi
docker exec "$ids_pg" psql -U postgres -At -c "select pg_switch_wal() is not null" >/dev/null && echo "    ok   cambio de segmento de WAL (lo que dispara archivar-wal.sh)"
# Los clientes .NET: con la raíz de confianza de la VM funcionan (ya lo hicieron al migrar); sin la CA privada, o pidiendo SSL Mode=Disable, deben FALLAR (VerifyFull no es decorativo).
ventas_run() { BABELHUB_SECRETOS="$TRABAJO/secretos/ventas" IP_PRIVADA=127.0.0.1 docker compose -p babelhub-ventas --project-directory "$RAIZ/infra" -f "$RAIZ/infra/docker-compose.ventas.yml" -f "$TRABAJO/ventas.override.yml" run --rm --no-deps --no-TTY "$@" 2>&1 || true; }
sin_ca="$(ventas_run -e SSL_CERT_FILE=/etc/ssl/certs/ca-certificates.crt ecommerce cuenta-de-prueba --cantidad 1 | sed -E 's/^(  contraseña[^:]*: ).*/\1[oculta]/')"
if grep -q "no se pudo completar" <<<"$sin_ca" && ! grep -q "cuenta creada" <<<"$sin_ca"; then echo "    ok   Npgsql con VerifyFull sin la CA privada en su raíz de confianza: rechazado (la misma orden con el entorno normal sí conecta, paso anterior)"; else echo "    ERROR: Npgsql se conectó sin confiar en la CA privada: $(head -c 400 <<<"$sin_ca")" >&2; MAL=1; fi
export CADENA_DE_ENSAYO_SIN_TLS="${cadena_de_la_cuenta//SSL Mode=VerifyFull/SSL Mode=Disable}"
sin_tls_net="$(ventas_run -e CADENA_DE_ENSAYO_SIN_TLS -e "BABELHUB__Ecommerce__Base__Aplicacion=$CADENA_DE_ENSAYO_SIN_TLS" ecommerce cuenta-de-prueba --cantidad 1 | sed -E 's/^(  contraseña[^:]*: ).*/\1[oculta]/')"
unset CADENA_DE_ENSAYO_SIN_TLS
if grep -q "no se pudo completar" <<<"$sin_tls_net" && ! grep -q "cuenta creada" <<<"$sin_tls_net"; then echo "    ok   Npgsql con SSL Mode=Disable: la base lo rechaza (pg_hba.conf; el mismo comando con la cadena normal sí conecta)"; else echo "    ERROR: una cadena sin TLS no fue rechazada: $(head -c 400 <<<"$sin_tls_net")" >&2; MAL=1; fi
# Evolution API (Prisma): se prueba con su imagen fijada, si ya está en el equipo (pesa unos 2 GB; ENSAYO_EVOLUTION=1 la descarga).
imagen_evolution="$(grep -oE 'evoapicloud/evolution-api:[^ ]+' "$RAIZ/infra/docker-compose.ventas.yml" | head -n 1)"
if [[ "${ENSAYO_EVOLUTION:-0}" == 1 ]] || docker image inspect "$imagen_evolution" >/dev/null 2>&1; then
  uri="$(valor_de "$TRABAJO/secretos/ventas/evolution.env" DATABASE_CONNECTION_URI)"
  clave_evolution="$(sed -E 's#^postgresql://evolution:([^@]*)@.*#\1#' <<<"$uri")"
  # La base técnica se crea una vez, como en la guía (§15.3); la contraseña no viaja en la línea de comandos del anfitrión sino por la entrada de psql.
  docker exec -i "$ids_pg" psql -U postgres -v ON_ERROR_STOP=1 <<SQL >/dev/null || true
CREATE ROLE evolution LOGIN PASSWORD '$clave_evolution';
SQL
  docker exec "$ids_pg" psql -U postgres -c "CREATE DATABASE evolution OWNER evolution" -c "REVOKE CONNECT ON DATABASE evolution FROM PUBLIC" >/dev/null || true
  # Se prueba con la URI de ventas/evolution.env tal cual, cambiando solo el host por el del ensayo y la ruta de la CA por la del contenedor de prueba.
  uri_ensayo="${uri//@127.0.0.1:5432/@host.docker.internal:35432}"
  evolution_prueba() { # uri
    MSYS_NO_PATHCONV=1 docker run --rm --add-host host.docker.internal:host-gateway --add-host nombre-equivocado.invalido:host-gateway -v "$ca_pg_docker:/ca.pem:ro" -v "$otra_ca_docker:/otra-ca.pem:ro" --entrypoint sh "$imagen_evolution" \
      -c 'echo "select 1 as ok" | ./node_modules/.bin/prisma db execute --url "$0" --stdin 2>&1 | tail -n 8' "$1" || true
  }
  salida_evo="$(evolution_prueba "${uri_ensayo/\/run\/secrets\/tls-ca.pem//ca.pem}")"
  if grep -q "Script executed successfully" <<<"$salida_evo"; then echo "    ok   Evolution API (Prisma) con la URI de evolution.env, la CA y el nombre correctos: conecta con TLS verificado"; else echo "    ERROR: Evolution (Prisma) no conectó: $(head -c 300 <<<"$salida_evo")" >&2; MAL=1; fi
  salida_evo="$(evolution_prueba "${uri_ensayo/\/run\/secrets\/tls-ca.pem//otra-ca.pem}")"
  if ! grep -q "Script executed successfully" <<<"$salida_evo" && grep -qE "P10[0-9][0-9]" <<<"$salida_evo"; then echo "    ok   Evolution API (Prisma) con otra CA: rechazada ($(grep -oE "P10[0-9][0-9]" <<<"$salida_evo" | head -n 1); con la CA correcta, la misma ruta conecta)"; else echo "    ERROR: Evolution (Prisma) aceptó otra CA: $(head -c 300 <<<"$salida_evo")" >&2; MAL=1; fi
  uri_nombre="${uri_ensayo/@host.docker.internal:/@nombre-equivocado.invalido:}"
  salida_evo="$(evolution_prueba "${uri_nombre/\/run\/secrets\/tls-ca.pem//ca.pem}")"
  if ! grep -q "Script executed successfully" <<<"$salida_evo" && grep -qE "P10[0-9][0-9]" <<<"$salida_evo"; then echo "    ok   Evolution API (Prisma) con la CA correcta pero otro nombre: rechazada ($(grep -oE "P10[0-9][0-9]" <<<"$salida_evo" | head -n 1))"; else echo "    ERROR: Evolution (Prisma) aceptó un nombre que el certificado no tiene: $(head -c 300 <<<"$salida_evo")" >&2; MAL=1; fi
  echo "    (Prisma ignora «sslmode=verify-full» y cae en «prefer» sin verificar: por eso la URI usa sslmode=require + sslaccept=strict + sslcert; desplegar.sh rechaza verify-full; DD-91)"
else
  echo "    -    Evolution API: omitido (su imagen no está en el equipo; con ENSAYO_EVOLUTION=1 el ensayo la descarga y la prueba)"
fi
[[ "$MAL" -eq 0 ]] || { echo "Error: el TLS de PostgreSQL no se comportó como debe (ver arriba)." >&2; exit 1; }
paso "7/7 Listo. Los dos servicios respondieron «listo» (200), los certificados se validaron y PostgreSQL solo habla TLS verificado."
echo "    Para borrar todo: bash scripts/ensayo-local.sh --limpiar"
