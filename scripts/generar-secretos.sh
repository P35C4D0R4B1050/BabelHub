#!/usr/bin/env bash
# Genera, en el equipo de la persona responsable, los archivos de secretos de las VMs de Oracle (PLA-04, PLA-17; guía:
# docs/07-proceso-de-desarrollo/08-guia-primer-despliegue.md). Cada archivo lleva SOLO lo que su VM necesita (mínimo privilegio, AGENTS §5):
#
#   <destino>/inventario/   postgres.env inventario.env ecommerce-gestion.env migrador.env aviso.env   -> /etc/babelhub/secretos de VM-INVENTARIO
#   <destino>/ventas/       ecommerce.env redis.env redis-contadores.env n8n.env evolution.env cloudflared.env y los .pem de Redis -> VM-VENTAS
#   <destino>/borde/        cloudflared.env                                                          -> VM-BORDE
#   <destino>/custodia/     la clave PRIVADA de age y la CA privada: se quedan en tu equipo y en la copia fuera de línea; NUNCA en una VM
#
# Lo que se puede inventar (contraseñas, claves HMAC, credenciales entre servicios, certificados de la red privada, el par de claves de age)
# se genera aquí con valores aleatorios. Lo que solo puede dar un tercero (la cuenta de correo, Turnstile, las llaves del almacén de Oracle,
# el token del túnel de Cloudflare, Culqi, WhatsApp) queda como «CAMBIAR» y se anota en PENDIENTES.txt: `scripts/desplegar.sh` se niega a
# desplegar una VM con algún «CAMBIAR» sin completar, así que no se puede olvidar uno.
#
#   scripts/generar-secretos.sh --destino ~/babelhub-secretos --dominio ejemplo.pe --ip-ventas 10.0.0.10 --ip-inventario 10.0.1.10 --ip-borde 10.0.2.10
#   scripts/generar-secretos.sh --destino /tmp/ensayo --ensayo        (ensayo local: dominio «localhost» y todo en 127.0.0.1)
#
# Se niega a escribir sobre una carpeta que ya tiene archivos (nunca pisa un secreto). Los archivos quedan con permisos 600 y la salida
# nunca imprime un valor. Requiere bash, openssl y /dev/urandom; para el par de age, `age-keygen` o Docker (con --imagen-postgres).
set -euo pipefail
umask 077
export MSYS_NO_PATHCONV=1   # en Git Bash para Windows, openssl no debe convertir «/CN=…» en una ruta

uso() {
  sed -n '2,18p' "$0" | sed 's/^# \{0,1\}//' >&2
  echo "Opciones: --destino <carpeta> --dominio <dominio> --ip-ventas <ip> --ip-inventario <ip> --ip-borde <ip> [--region sa-santiago-1] [--ensayo] [--imagen-postgres <imagen>]" >&2
  exit 2
}

DESTINO="" DOMINIO="" IP_VENTAS="" IP_INVENTARIO="" IP_BORDE="" REGION="sa-santiago-1" ENSAYO=0 IMAGEN_POSTGRES_PARA_AGE=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --destino) [[ $# -ge 2 ]] || uso; DESTINO="$2"; shift 2 ;;
    --dominio) [[ $# -ge 2 ]] || uso; DOMINIO="$2"; shift 2 ;;
    --ip-ventas) [[ $# -ge 2 ]] || uso; IP_VENTAS="$2"; shift 2 ;;
    --ip-inventario) [[ $# -ge 2 ]] || uso; IP_INVENTARIO="$2"; shift 2 ;;
    --ip-borde) [[ $# -ge 2 ]] || uso; IP_BORDE="$2"; shift 2 ;;
    --region) [[ $# -ge 2 ]] || uso; REGION="$2"; shift 2 ;;
    --imagen-postgres) [[ $# -ge 2 ]] || uso; IMAGEN_POSTGRES_PARA_AGE="$2"; shift 2 ;;
    --ensayo) ENSAYO=1; shift ;;
    *) uso ;;
  esac
done
[[ -n "$DESTINO" ]] || uso
if [[ "$ENSAYO" -eq 1 ]]; then
  DOMINIO="${DOMINIO:-localhost}" IP_VENTAS="${IP_VENTAS:-127.0.0.1}" IP_INVENTARIO="${IP_INVENTARIO:-127.0.0.1}" IP_BORDE="${IP_BORDE:-127.0.0.1}"
fi
[[ -n "$DOMINIO" && -n "$IP_VENTAS" && -n "$IP_INVENTARIO" && -n "$IP_BORDE" ]] || uso
# Solo direcciones privadas (RFC 1918) o de la propia máquina (el ensayo): 10/8, 172.16/12, 192.168/16 y 127/8, con cada octeto de 0 a 255.
ip_valida() {
  local a b c d
  IFS=. read -r a b c d <<<"$1"
  [[ "$1" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]] || return 1
  (( a <= 255 && b <= 255 && c <= 255 && d <= 255 )) || return 1
  (( a == 10 || a == 127 || (a == 172 && b >= 16 && b <= 31) || (a == 192 && b == 168) ))
}
ip_valida "$IP_VENTAS" && ip_valida "$IP_INVENTARIO" && ip_valida "$IP_BORDE" || { echo "Error: las direcciones deben ser IPv4 privadas (10.x.x.x, 172.16-31.x.x o 192.168.x.x), por ejemplo 10.0.0.10." >&2; exit 2; }
[[ "$DOMINIO" =~ ^[a-z0-9]([a-z0-9.-]*[a-z0-9])?$ ]] || { echo "Error: el dominio debe ir en minúsculas, sin https:// ni barras (ejemplo.pe)." >&2; exit 2; }
[[ "$REGION" =~ ^[a-z0-9-]+$ ]] || { echo "Error: región inválida." >&2; exit 2; }
command -v openssl >/dev/null || { echo "Error: falta openssl." >&2; exit 2; }
if [[ -d "$DESTINO" ]] && [[ -n "$(ls -A "$DESTINO" 2>/dev/null)" ]]; then
  echo "Error: $DESTINO ya tiene archivos; este script no pisa secretos. Usa una carpeta nueva." >&2
  exit 1
fi
mkdir -p "$DESTINO"/{inventario,ventas,borde,custodia}
chmod 700 "$DESTINO" "$DESTINO"/{inventario,ventas,borde,custodia}

# ---------- Valores aleatorios ----------
alea() { head -c $(( $1 * 3 )) /dev/urandom | base64 | tr -d '/+=\n' | head -c "$1"; }
versionada() { printf 'v1:%s' "$(head -c 32 /dev/urandom | base64)"; }
numerada() { printf '1:%s' "$(head -c 32 /dev/urandom | base64)"; }   # «versión:clave» con la versión un número (la huella de los números de WhatsApp)
# Aleatorios que se repiten en varios archivos: se generan una sola vez y se recuerdan en una carpeta temporal (las llamadas de $(…) corren en
# subprocesos, así que una variable no los conservaría). La carpeta se borra al terminar.
MEMORIA="$(mktemp -d)"
trap 'rm -rf "$MEMORIA"' EXIT
valor() { # nombre largo [versionada]: el mismo valor cada vez que se pide
  local archivo="$MEMORIA/$1"
  if [[ ! -f "$archivo" ]]; then
    case "${3:-}" in versionada) versionada > "$archivo" ;; numerada) numerada > "$archivo" ;; *) alea "$2" > "$archivo" ;; esac
  fi
  cat "$archivo"
}
PENDIENTES=()
# linea <archivo> <CLAVE> <valor> : agrega CLAVE="valor" entre comillas dobles, que es lo que entienden a la vez Docker Compose (env_file) y scripts/desplegar.sh.
# Ningún valor generado lleva comillas, «$» ni saltos de línea (base64 sin símbolos, direcciones y nombres).
linea() {
  local archivo="$1" clave="$2" valor="$3"
  [[ "$valor" =~ ^[^\"\$\`\\$'\n']*$ ]] || { echo "Error interno: el valor de $clave trae un carácter que no se puede escribir en un archivo de entorno." >&2; exit 1; }
  printf '%s="%s"\n' "$clave" "$valor" >> "$archivo"
}
# pendiente <archivo> <CLAVE> <qué debe ir>: valor que solo puede dar un tercero
pendiente() {
  printf '%s=CAMBIAR\n' "$2" >> "$1"
  PENDIENTES+=("${1#"$DESTINO"/}  $2  — $3")
}
inicio() { : > "$1"; chmod 600 "$1"; { echo "# Generado por scripts/generar-secretos.sh. Solo para esta VM. Fuera del repositorio. Permisos 600."; } >> "$1"; }

ALCANCES=(LecturaDeDisponibilidad OperacionDeUsuario LecturaDeContacto ExpiracionDeReservas ConsultaDePendientes BajaDeTitular OrigenReenviado ConsultaDeAuditoria LecturaDeAlertas MarcarAlerta Historial SolicitarAviso)
credenciales_de_servicio() { for a in "${ALCANCES[@]}"; do linea "$1" "BABELHUB__Seguridad__Credenciales__$a" "$(valor "cred-$a" 40)"; done; }

HOST_TIENDA="$DOMINIO"
HOST_GESTION="gestion.$DOMINIO"
HOSTS_TIENDA="$DOMINIO,www.$DOMINIO"
[[ "$ENSAYO" -eq 1 ]] && HOSTS_TIENDA="localhost" && HOST_GESTION="gestion.localhost"
TURNSTILE_HOSTS="$HOSTS_TIENDA,$HOST_GESTION"

cuenta() { # base usuario ruta-del-host : la cadena de conexión de una cuenta de servicio
  printf 'Host=%s;Port=5432;Database=%s;Username=%s;Password=%s' "$3" "$1" "$2" "$(valor "pg-$2" 32)"
}

# ---------- VM-INVENTARIO: postgres.env, inventario.env, ecommerce-gestion.env, migrador.env, aviso.env ----------
d="$DESTINO/inventario"
f="$d/postgres.env"; inicio "$f"
linea "$f" POSTGRES_PASSWORD "$(valor pg-admin 40)"
linea "$f" POSTGRES_ECOMMERCE_PASSWORD "$(valor pg-ecommerce 40)"
linea "$f" POSTGRES_INVENTARIO_PASSWORD "$(valor pg-inventario 40)"
# El destinatario de age (público) se completa al generar el par, más abajo.
AGE_DESTINATARIO_LINEA="$f"
linea "$f" RCLONE_CONFIG_ALMACEN_TYPE s3
linea "$f" RCLONE_CONFIG_ALMACEN_PROVIDER Other
pendiente "$f" RCLONE_CONFIG_ALMACEN_ENDPOINT "https://<espacio-de-nombres>.compat.objectstorage.$REGION.oraclecloud.com (credencial respaldos-escritor)"
linea "$f" RCLONE_CONFIG_ALMACEN_REGION "$REGION"
pendiente "$f" RCLONE_CONFIG_ALMACEN_ACCESS_KEY_ID "clave de acceso de la credencial respaldos-escritor"
pendiente "$f" RCLONE_CONFIG_ALMACEN_SECRET_ACCESS_KEY "clave secreta de la credencial respaldos-escritor"

f="$d/inventario.env"; inicio "$f"
linea "$f" BABELHUB__Inventario__Base__Aplicacion "$(cuenta inventario inventario_acceso postgres)"
linea "$f" BABELHUB__Inventario__Base__DepuracionDeAjustes "$(cuenta inventario inventario_depurador_ajustes postgres)"
linea "$f" BABELHUB__Inventario__Base__DepuracionDeAuditoria "$(cuenta inventario inventario_depurador_auditoria postgres)"
# El Inventario llama al E-commerce de VM-VENTAS (avisos de traslado) por la red privada y por HTTPS (SEG-15); las claves de identidad las sirve la réplica de gestión de esta misma VM.
linea "$f" BABELHUB__Inventario__Ecommerce__Direccion "https://$IP_VENTAS:8443/"
linea "$f" BABELHUB__Identidad__UrlDeLaClaveDeVerificacion "https://ecommerce-gestion:8443/.well-known/jwks.json"
linea "$f" BABELHUB__Identidad__UrlDeLasRevocaciones "https://ecommerce-gestion:8443/.well-known/revocaciones"
linea "$f" BABELHUB__Seguridad__ClaveDeHuella "$(valor huella-inventario 0 versionada)"
linea "$f" BABELHUB__Auditoria__ClaveDeCursor "$(valor cursor-inventario 40)"
linea "$f" BABELHUB__Inventario__ClaveDeBajas "$(valor bajas 40)"
credenciales_de_servicio "$f"

f="$d/ecommerce-gestion.env"; inicio "$f"
linea "$f" BABELHUB__Ecommerce__Base__Aplicacion "$(cuenta ecommerce ecommerce_acceso postgres)"
linea "$f" BABELHUB__Ecommerce__Base__DepuracionDeAuditoria "$(cuenta ecommerce ecommerce_depurador_auditoria postgres)"
linea "$f" BABELHUB__Ecommerce__Base__DepuracionDeRegistros "$(cuenta ecommerce ecommerce_depurador_registros postgres)"
linea "$f" BABELHUB__Ecommerce__Inventario__Direccion "https://inventario:8443/"
linea "$f" BABELHUB__Ecommerce__Redis__Direccion "$IP_VENTAS:6380"
linea "$f" BABELHUB__Ecommerce__Redis__Tls true
# El origen del cliente solo se acepta del borde: la VM-BORDE (Nginx) llega por su dirección privada.
linea "$f" BABELHUB__Ecommerce__ProxiesConocidos "$IP_BORDE"
linea "$f" BABELHUB__Web__Host gestion
# Además del dominio, el servidor acepta el nombre y la dirección privada con los que lo llaman los otros servicios (https://<IP>:8443): un Host que no esté en la lista se rechaza con 400.
linea "$f" BABELHUB__Web__HostsPermitidos "$HOST_GESTION,$IP_INVENTARIO,ecommerce-gestion"
linea "$f" BABELHUB__Web__ClaveDeSesionesGestion "$(valor sesiones-gestion 40)"
linea "$f" BABELHUB__Carrito__CostoDeEnvio "12.00"
pendiente "$f" BABELHUB__Contacto__Telefono "teléfono del comercio que se muestra en el pie y en /contacto (entre comillas dobles; sin dólar, comillas, almohadilla ni barra invertida)"
pendiente "$f" BABELHUB__Contacto__Correo "correo de atención al cliente (no la cuenta que envía los avisos)"
pendiente "$f" BABELHUB__Contacto__Direccion "dirección del comercio (entre comillas dobles; sin dólar, comillas, almohadilla ni barra invertida)"
linea "$f" BABELHUB__Seguridad__ClaveDeRedisDeLimites "$(valor redis-limites 40)"
linea "$f" BABELHUB__Seguridad__ClaveDeHuella "$(valor huella-ecommerce 0 versionada)"
linea "$f" BABELHUB__Auditoria__ClaveDeCursor "$(valor cursor-ecommerce 40)"
linea "$f" BABELHUB__Catalogo__ClaveDeIngresos "$(valor ingresos 40)"
linea "$f" BABELHUB__Correo__ClaveDeLaCola "$(valor cola-correo 40)"
linea "$f" BABELHUB__Identidad__ClaveDeCifradoDeClaves "$(valor cifrado-claves 0 versionada)"
linea "$f" BABELHUB__Identidad__Pimienta "$(valor pimienta 0 versionada)"
linea "$f" BABELHUB__Pagos__Pasarela culqi
linea "$f" BABELHUB__Pagos__ClaveDeIntentos "$(valor intentos 40)"
linea "$f" BABELHUB__Notificaciones__Envio n8n
linea "$f" BABELHUB__Notificaciones__N8n__Webhook "http://$IP_VENTAS:5678/webhook/babelhub-avisos"
linea "$f" BABELHUB__Notificaciones__N8n__ClaveSaliente "$(valor firma-avisos 40)"
linea "$f" BABELHUB__Notificaciones__ClaveDeHuellaDeNumero "$(valor huella-numero 0 numerada)"
linea "$f" BABELHUB__Integraciones__n8n__Secreto "$(valor firma-estados 40)"
linea "$f" BABELHUB__Seguridad__Turnstile__Hostnames "$TURNSTILE_HOSTS"
pendiente "$f" BABELHUB__Seguridad__Turnstile__ClaveDeSitio "clave de sitio (pública) del widget de Turnstile de Cloudflare"
pendiente "$f" BABELHUB__Seguridad__Turnstile__Secreto "clave secreta de Turnstile"
pendiente "$f" BABELHUB__Correo__Smtp__Servidor "servidor SMTP (smtp.gmail.com)"
pendiente "$f" BABELHUB__Correo__Smtp__Cuenta "la cuenta de correo que envía"
pendiente "$f" BABELHUB__Correo__Smtp__Remitente "el remitente (igual a la cuenta)"
pendiente "$f" BABELHUB__Correo__Smtp__Credencial "contraseña de aplicación de esa cuenta"
for a in Catalogo Biblioteca Supresiones; do   # la réplica de gestión también exige la credencial de lectura de la Biblioteca para arrancar (ensayo local, DD-55)
  pendiente "$f" "BABELHUB__Almacen__${a}__Endpoint" "https://<espacio-de-nombres>.compat.objectstorage.$REGION.oraclecloud.com"
  linea "$f" "BABELHUB__Almacen__${a}__Region" "$REGION"
done
pendiente "$f" BABELHUB__Almacen__Catalogo__ClaveDeAcceso "clave de acceso de catalogo-archivos"
pendiente "$f" BABELHUB__Almacen__Catalogo__ClaveSecreta "clave secreta de catalogo-archivos"
pendiente "$f" BABELHUB__Almacen__Biblioteca__ClaveDeAcceso "clave de acceso de biblioteca-lector"
pendiente "$f" BABELHUB__Almacen__Biblioteca__ClaveSecreta "clave secreta de biblioteca-lector"
pendiente "$f" BABELHUB__Almacen__Supresiones__ClaveDeAcceso "clave de acceso de supresiones-registro"
pendiente "$f" BABELHUB__Almacen__Supresiones__ClaveSecreta "clave secreta de supresiones-registro"
credenciales_de_servicio "$f"

# Las cuentas propietarias (administración) solo para migrar: nunca en los archivos de los servicios (COMP-2).
f="$d/migrador.env"; inicio "$f"
linea "$f" BABELHUB__Migrador__Inventario__Propietaria "Host=postgres;Port=5432;Database=inventario;Username=postgres;Password=$(valor pg-admin 40)"
linea "$f" BABELHUB__Migrador__Ecommerce__Propietaria "Host=postgres;Port=5432;Database=ecommerce;Username=postgres;Password=$(valor pg-admin 40)"

# La credencial de correo propia de esta VM para el aviso del espacio reservado (PLA-21): solo envía avisos.
f="$d/aviso.env"; inicio "$f"
pendiente "$f" AUX_SMTP_URL "smtps://smtp.gmail.com:465 (con TLS implícito: smtps://)"
pendiente "$f" AUX_SMTP_USUARIO "cuenta de correo que envía los avisos"
pendiente "$f" AUX_SMTP_CLAVE "contraseña de aplicación de esa cuenta"
pendiente "$f" AUX_CORREO_DE "remitente de los avisos"
pendiente "$f" AUX_CORREO_PARA "tu correo, que recibe los avisos"

# ---------- VM-VENTAS ----------
d="$DESTINO/ventas"
f="$d/ecommerce.env"; inicio "$f"
linea "$f" BABELHUB__Ecommerce__Base__Aplicacion "$(cuenta ecommerce ecommerce_acceso "$IP_INVENTARIO")"
linea "$f" BABELHUB__Ecommerce__Base__DepuracionDeAuditoria "$(cuenta ecommerce ecommerce_depurador_auditoria "$IP_INVENTARIO")"
linea "$f" BABELHUB__Ecommerce__Base__DepuracionDeRegistros "$(cuenta ecommerce ecommerce_depurador_registros "$IP_INVENTARIO")"
linea "$f" BABELHUB__Ecommerce__Inventario__Direccion "https://$IP_INVENTARIO:8082/"
linea "$f" BABELHUB__Ecommerce__Redis__Direccion "redis-contadores:6380"
linea "$f" BABELHUB__Ecommerce__Redis__Tls true
# El origen del cliente solo se acepta del borde: Nginx de VM-BORDE por su dirección privada y el segundo conector de cloudflared, que llega desde la red de Compose de esta VM.
linea "$f" BABELHUB__Ecommerce__ProxiesConocidos "$IP_BORDE,172.16.0.0/12"
linea "$f" BABELHUB__Web__Host tienda
linea "$f" BABELHUB__Web__HostsPermitidos "$HOSTS_TIENDA,$IP_VENTAS,ecommerce"
linea "$f" BABELHUB__Web__ClaveDeSesionesTienda "$(valor sesiones-tienda 40)"
linea "$f" BABELHUB__Carrito__CostoDeEnvio "12.00"
pendiente "$f" BABELHUB__Contacto__Telefono "teléfono del comercio que se muestra en el pie y en /contacto (entre comillas dobles; sin dólar, comillas, almohadilla ni barra invertida)"
pendiente "$f" BABELHUB__Contacto__Correo "correo de atención al cliente (no la cuenta que envía los avisos)"
pendiente "$f" BABELHUB__Contacto__Direccion "dirección del comercio (entre comillas dobles; sin dólar, comillas, almohadilla ni barra invertida)"
linea "$f" BABELHUB__Seguridad__ClaveDeRedisDeLimites "$(valor redis-limites 40)"
linea "$f" BABELHUB__Seguridad__ClaveDeHuella "$(valor huella-ecommerce 0 versionada)"
linea "$f" BABELHUB__Auditoria__ClaveDeCursor "$(valor cursor-ecommerce 40)"
linea "$f" BABELHUB__Catalogo__ClaveDeIngresos "$(valor ingresos 40)"
linea "$f" BABELHUB__Correo__ClaveDeLaCola "$(valor cola-correo 40)"
linea "$f" BABELHUB__Identidad__ClaveDeCifradoDeClaves "$(valor cifrado-claves 0 versionada)"
linea "$f" BABELHUB__Identidad__Pimienta "$(valor pimienta 0 versionada)"
linea "$f" BABELHUB__Pagos__Pasarela culqi
linea "$f" BABELHUB__Pagos__ClaveDeIntentos "$(valor intentos 40)"
linea "$f" BABELHUB__Notificaciones__Envio n8n
linea "$f" BABELHUB__Notificaciones__N8n__Webhook "http://n8n:5678/webhook/babelhub-avisos"
linea "$f" BABELHUB__Notificaciones__N8n__ClaveSaliente "$(valor firma-avisos 40)"
linea "$f" BABELHUB__Notificaciones__ClaveDeHuellaDeNumero "$(valor huella-numero 0 numerada)"
linea "$f" BABELHUB__Integraciones__n8n__Secreto "$(valor firma-estados 40)"
linea "$f" BABELHUB__Seguridad__Turnstile__Hostnames "$TURNSTILE_HOSTS"
pendiente "$f" BABELHUB__Seguridad__Turnstile__ClaveDeSitio "clave de sitio (pública) del widget de Turnstile de Cloudflare"
pendiente "$f" BABELHUB__Seguridad__Turnstile__Secreto "clave secreta de Turnstile"
pendiente "$f" BABELHUB__Correo__Smtp__Servidor "servidor SMTP (smtp.gmail.com)"
pendiente "$f" BABELHUB__Correo__Smtp__Cuenta "la cuenta de correo que envía"
pendiente "$f" BABELHUB__Correo__Smtp__Remitente "el remitente (igual a la cuenta)"
pendiente "$f" BABELHUB__Correo__Smtp__Credencial "contraseña de aplicación de esa cuenta"
for a in Catalogo Biblioteca Supresiones; do
  pendiente "$f" "BABELHUB__Almacen__${a}__Endpoint" "https://<espacio-de-nombres>.compat.objectstorage.$REGION.oraclecloud.com"
  linea "$f" "BABELHUB__Almacen__${a}__Region" "$REGION"
done
pendiente "$f" BABELHUB__Almacen__Catalogo__ClaveDeAcceso "clave de acceso de catalogo-archivos"
pendiente "$f" BABELHUB__Almacen__Catalogo__ClaveSecreta "clave secreta de catalogo-archivos"
pendiente "$f" BABELHUB__Almacen__Biblioteca__ClaveDeAcceso "clave de acceso de biblioteca-lector"
pendiente "$f" BABELHUB__Almacen__Biblioteca__ClaveSecreta "clave secreta de biblioteca-lector"
pendiente "$f" BABELHUB__Almacen__Supresiones__ClaveDeAcceso "clave de acceso de supresiones-registro"
pendiente "$f" BABELHUB__Almacen__Supresiones__ClaveSecreta "clave secreta de supresiones-registro"
pendiente "$f" BABELHUB__Web__OrigenDelAlmacen "el origen https exacto del almacén de libros (el mismo host del Endpoint de biblioteca-lector)"
credenciales_de_servicio "$f"

f="$d/redis.env"; inicio "$f"; linea "$f" REDIS_CACHE_PASSWORD "$(valor redis-cache 40)"
f="$d/redis-contadores.env"; inicio "$f"; linea "$f" BABELHUB__Seguridad__ClaveDeRedisDeLimites "$(valor redis-limites 40)"
f="$d/n8n.env"; inicio "$f"
linea "$f" N8N_ENCRYPTION_KEY "$(valor n8n-cifrado 40)"
linea "$f" BABELHUB_FIRMA_DE_AVISOS "$(valor firma-avisos 40)"
linea "$f" BABELHUB_FIRMA_DE_ESTADOS "$(valor firma-estados 40)"
linea "$f" ECOMMERCE_URL "http://ecommerce:8080"
linea "$f" EVOLUTION_URL "http://evolution-api:8080"
linea "$f" EVOLUTION_INSTANCIA "babelhub"
f="$d/evolution.env"; inicio "$f"
linea "$f" AUTHENTICATION_API_KEY "$(valor evolution-api 40)"
linea "$f" SERVER_URL "http://127.0.0.1:8081"
linea "$f" DATABASE_PROVIDER postgresql
linea "$f" DATABASE_CONNECTION_URI "postgresql://evolution:$(valor pg-evolution 32)@$IP_INVENTARIO:5432/evolution?schema=public"
linea "$f" CACHE_REDIS_ENABLED true
linea "$f" CACHE_REDIS_URI "redis://:$(valor redis-cache 40)@redis:6379/6"
linea "$f" CACHE_LOCAL_ENABLED false
# El mismo túnel tiene dos conectores (VM-BORDE y VM-VENTAS) y comparten el token.
f="$d/cloudflared.env"; inicio "$f"; pendiente "$f" TUNNEL_TOKEN "el token del túnel (Cloudflare Zero Trust → Networks → Tunnels)"
f="$DESTINO/borde/cloudflared.env"; inicio "$f"; pendiente "$f" TUNNEL_TOKEN "el mismo token del túnel"

# ---------- Certificados de la red privada (Redis de contadores y HTTPS entre servicios) ----------
# Una CA privada de la cuenta firma el certificado de cada VM. La clave de la CA se queda en custodia/ (tu equipo); a las VMs solo llegan
# su certificado, su clave y el certificado público de la CA.
c="$DESTINO/custodia"
# openssl recibe nombres relativos dentro de custodia/ (en Git Bash para Windows no entiende las rutas /c/…).
# La CA solo puede firmar lo de la red privada (restricción de nombres) y ningún intermedio: si su clave se filtrara, no serviría para suplantar a Culqi, al almacén ni al correo.
(cd "$c" && openssl req -x509 -newkey rsa:3072 -nodes -days 1825 -subj "/CN=BabelHub CA privada" -keyout ca-clave.pem -out ca.pem \
  -addext "basicConstraints=critical,CA:TRUE,pathlen:0" \
  -addext "nameConstraints=critical,permitted;IP:10.0.0.0/255.0.0.0,permitted;IP:172.16.0.0/255.240.0.0,permitted;IP:192.168.0.0/255.255.0.0,permitted;IP:127.0.0.0/255.0.0.0,permitted;DNS:localhost,permitted;DNS:ecommerce,permitted;DNS:inventario,permitted;DNS:ecommerce-gestion,permitted;DNS:redis-contadores" 2>/dev/null)
emitir() { # carpeta-de-la-VM prefijo ip nombre-alterno... : certificado de servidor con esos nombres y direcciones
  local carpeta="$1" prefijo="$2" ip="$3"; shift 3
  local san="IP:$ip,IP:127.0.0.1,DNS:localhost" nombre
  for nombre in "$@"; do san+=",DNS:$nombre"; done
  printf 'subjectAltName=%s\nextendedKeyUsage=serverAuth\nbasicConstraints=CA:FALSE\n' "$san" > "$c/$prefijo.ext"
  (cd "$c" \
    && openssl req -newkey rsa:3072 -nodes -subj "/CN=babelhub-$prefijo" -keyout "$prefijo-key.pem" -out "$prefijo.csr" 2>/dev/null \
    && openssl x509 -req -in "$prefijo.csr" -CA ca.pem -CAkey ca-clave.pem -CAcreateserial -CAserial ca.srl -days 825 -sha256 -extfile "$prefijo.ext" -out "$prefijo-cert.pem" 2>/dev/null)
  rm -f "$c/$prefijo.csr" "$c/$prefijo.ext"
  mv "$c/$prefijo-key.pem" "$c/$prefijo-cert.pem" "$carpeta/"
  chmod 600 "$carpeta/$prefijo-key.pem" "$carpeta/$prefijo-cert.pem"
}
# VM-VENTAS: Redis de contadores (6380) y el E-commerce (8443). VM-INVENTARIO: el Inventario (8443).
emitir "$DESTINO/ventas" redis-contadores "$IP_VENTAS" redis-contadores
cp "$c/ca.pem" "$DESTINO/ventas/redis-contadores-ca.pem"
emitir "$DESTINO/ventas" tls-ecommerce "$IP_VENTAS" ecommerce
# Un certificado y una llave por servicio: el que atiende al administrador (la réplica de gestión) no puede hacerse pasar por el Inventario.
emitir "$DESTINO/inventario" tls-inventario "$IP_INVENTARIO" inventario
emitir "$DESTINO/inventario" tls-gestion "$IP_INVENTARIO" ecommerce-gestion
cp "$c/ca.pem" "$DESTINO/ventas/tls-ca.pem"
cp "$c/ca.pem" "$DESTINO/inventario/tls-ca.pem"
chmod 600 "$DESTINO"/ventas/*.pem "$DESTINO"/inventario/*.pem

# ---------- El par de claves de age (cifrado de los respaldos) ----------
# La clave PRIVADA se queda en custodia/ y en una copia fuera de línea (infra/respaldos/custodia.md); a VM-INVENTARIO solo llega la pública.
AGE_KEYGEN="${AGE_KEYGEN:-age-keygen}"   # un doble en las pruebas
if command -v "$AGE_KEYGEN" >/dev/null; then
  "$AGE_KEYGEN" -o "$c/age-clave-privada.txt" 2>/dev/null
elif [[ -n "$IMAGEN_POSTGRES_PARA_AGE" ]] && command -v docker >/dev/null; then
  [[ "$IMAGEN_POSTGRES_PARA_AGE" =~ @sha256:[0-9a-f]{64}$ ]] || { echo "Error: --imagen-postgres debe llevar su digest (@sha256:…)." >&2; exit 2; }
  docker run --rm --network none --pull=never --entrypoint age-keygen "$IMAGEN_POSTGRES_PARA_AGE" > "$c/age-clave-privada.txt" 2>/dev/null
else
  echo "Error: falta age-keygen. Instálalo (sudo apt install age) o pasa --imagen-postgres <imagen> para usar el de la imagen de PostgreSQL." >&2
  exit 1
fi
chmod 600 "$c/age-clave-privada.txt"
destinatario="$(grep -m1 '^# public key: ' "$c/age-clave-privada.txt" | sed 's/^# public key: //')"
[[ "$destinatario" == age1* ]] || { echo "Error: no se pudo leer la clave pública de age." >&2; exit 1; }
linea "$AGE_DESTINATARIO_LINEA" BABELHUB_AGE_DESTINATARIO "$destinatario"

# ---------- La lista de lo pendiente ----------
{
  echo "# Valores que solo puede dar un tercero. Complétalos con un editor (nano) y vuelve a ejecutar scripts/desplegar.sh."
  echo "# Archivo  Clave  — qué va"
  printf '%s\n' "${PENDIENTES[@]}"
} > "$DESTINO/PENDIENTES.txt"
chmod 600 "$DESTINO/PENDIENTES.txt"

echo "Listo. Se generaron los secretos de las tres VMs en $DESTINO."
echo "  - Falta completar ${#PENDIENTES[@]} valores «CAMBIAR» (ver $DESTINO/PENDIENTES.txt); nada se despliega mientras haya alguno."
echo "  - $DESTINO/custodia/ (clave privada de age y CA) NO va a ninguna VM: haz ahora una copia fuera de línea (infra/respaldos/custodia.md)."
