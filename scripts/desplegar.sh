#!/usr/bin/env bash
# Despliega una VM con un solo comando (PLA-04, AC-PLA-04): levanta todos sus componentes y espera a que cada uno responda su comprobación
# de salud. Es idempotente (`docker compose up -d --wait`): repetirlo no cambia lo que ya está sano. Se ejecuta EN la VM, con sus secretos.
#
#   BABELHUB_SECRETOS=/etc/babelhub/secretos IP_PRIVADA=10.0.0.10 IP_INVENTARIO=10.0.0.20 IMAGEN_ECOMMERCE=<imagen@sha256:…> \
#       scripts/desplegar.sh <ventas|inventario> [--secretos <carpeta>] [--infra <carpeta>] [--espera <segundos>] [--cron <carpeta>] [--sin <servicio[,servicio]>]
#   Topología de dos VMs (DD-100): la entrada pública (cloudflared y Nginx) forma parte de «ventas»; no hay VM-BORDE.
#
# Antes de tocar nada comprueba, y no continúa si falla (sin imprimir nunca un valor secreto):
#   - que estén definidas todas las variables obligatorias del Compose de la VM (BABELHUB_SECRETOS, las direcciones privadas, las imágenes);
#   - que las imágenes propias (IMAGEN_*) sean ya publicadas: con su digest `@sha256:…` (este script solo despliega lo publicado; la puerta
#     del ensayo de restauración, PLA-11, la ejecuta el flujo de lanzamiento antes de publicar);
#   - que cada archivo de secretos que el Compose usa exista, no esté vacío y solo lo pueda leer su dueño;
#   - en la VM de datos, que migrador.env (las cuentas propietarias de las bases, COMP-2) exista con sus permisos y sus dos claves;
#   - que tenga las claves de infra/secretos-requeridos.txt, con el largo mínimo de las que genera el sistema;
#   - que ninguna clave privada de age esté en la VM (en la VM de datos solo puede haber la clave pública, PLA-17).
# En VM-INVENTARIO instala además la tarea diaria del respaldo (el archivado de cambios ya va en el Compose) y la vigilancia por minuto del espacio
# local reservado (PLA-21: exige aviso.env, la credencial de correo propia de esa VM) y, tras levantar, comprueba que cada
# componente esté «healthy» y lo informa.
#
# TLS hacia PostgreSQL (AM24): además rechaza una cadena de conexión sin «SSL Mode=VerifyFull» (o la URI de Evolution sin sslmode=require + sslaccept=strict + sslcert), por nombre de clave y sin imprimir valores.
#
# En VM-VENTAS, antes de levantar nada, deja que solo Nginx llegue desde los contenedores a la réplica de gestión (scripts/proteger-gestion.sh, DD-104) e instala
# la tarea de cron que repone esa regla cada minuto.
#
# Variables opcionales: DOCKER (comando de docker, por defecto `docker`), STAT (por defecto `stat`), CHOWN (por defecto `chown`), IPTABLES (por defecto `iptables`), BABELHUB_RAICES_PUBLICAS.
# Códigos de salida: 0 desplegado y sano, 1 falta algo o algún componente no quedó sano, 2 uso inválido.
set -euo pipefail

DOCKER="${DOCKER:-docker}"
STAT="${STAT:-stat}"
CHOWN="${CHOWN:-chown}"
RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INFRA="$RAIZ/infra"
ESPERA=300
CARPETA_CRON="/etc/cron.d"
SIN=()
EXTRA=""
VM=""

uso() {
  echo "Uso: $0 <ventas|inventario> [--secretos <carpeta>] [--infra <carpeta>] [--espera <segundos>] [--cron <carpeta>] [--sin <servicio[,servicio]>]" >&2
  exit 2
}

falta=()
anotar() { falta+=("$1"); }

[[ $# -ge 1 ]] || uso
VM="$1"
shift
case "$VM" in
  ventas | inventario) ;;
  borde) echo "Error: VM-BORDE ya no existe: cloudflared y Nginx se despliegan con «ventas» (docs/07-proceso-de-desarrollo/08-guia-primer-despliegue.md)." >&2; exit 2 ;;
  *) uso ;;
esac
while [[ $# -gt 0 ]]; do
  case "$1" in
    --secretos) [[ $# -ge 2 ]] || uso; export BABELHUB_SECRETOS="$2"; shift 2 ;;
    --infra) [[ $# -ge 2 ]] || uso; INFRA="$2"; shift 2 ;;
    --espera) [[ $# -ge 2 && "$2" =~ ^[0-9]+$ ]] || uso; ESPERA="$2"; shift 2 ;;
    --cron) [[ $# -ge 2 ]] || uso; CARPETA_CRON="$2"; shift 2 ;;
    # Deja sin levantar algunos servicios (por ejemplo n8n y evolution-api hasta tener el número de WhatsApp, B07). Solo lo que se nombra; el resto se despliega y se espera igual.
    --sin) [[ $# -ge 2 ]] || uso; IFS=, read -r -a SIN <<<"$2"; shift 2 ;;
    # Solo para el ensayo local (BABELHUB_ENSAYO=1): un Compose adicional que, por ejemplo, cambia los puertos del equipo. Nunca en una VM de verdad.
    --extra) [[ $# -ge 2 && "${BABELHUB_ENSAYO:-0}" == 1 ]] || uso; EXTRA="$2"; shift 2 ;;
    *) uso ;;
  esac
done

COMPOSE="$INFRA/docker-compose.$VM.yml"
[[ -f "$COMPOSE" ]] || { echo "Error: no existe $COMPOSE" >&2; exit 2; }
TEXTO="$(cat "$COMPOSE")"

# ---------- 1. Variables obligatorias del Compose ----------

mapfile -t VARIABLES < <(grep -oE '\$\{[A-Z][A-Z0-9_]*:\?' <<<"$TEXTO" | sed -E 's/^\$\{//; s/:\?$//' | sort -u)
for variable in "${VARIABLES[@]}"; do
  [[ -n "${!variable:-}" ]] || anotar "falta la variable $variable"
done

for variable in "${VARIABLES[@]}"; do
  valor="${!variable:-}"
  [[ -n "$valor" ]] || continue
  case "$variable" in
    IMAGEN_*)
      [[ "$valor" =~ ^[A-Za-z0-9._/:-]+@sha256:[0-9a-f]{64}$ ]] || anotar "$variable no es una imagen publicada con su digest (@sha256:…): solo se despliega lo ya publicado"
      ;;
    IP_*)
      if [[ ! "$valor" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ || "$valor" == 0.0.0.0 ]]; then
        anotar "$variable no es una dirección privada válida"
      fi
      ;;
  esac
done

# ---------- 2. Archivos de secretos ----------

SECRETOS="${BABELHUB_SECRETOS:-}"
mapfile -t ARCHIVOS < <(grep -oE '\$\{BABELHUB_SECRETOS:\?[^}]*\}/[A-Za-z0-9._-]+' <<<"$TEXTO" | sed -E 's#^.*\}/##' | sort -u)

leer_valor() { # archivo clave -> el valor (vacío si no está); nunca se imprime fuera de las comprobaciones
  local linea clave="$2" valor=""
  while IFS= read -r linea || [[ -n "$linea" ]]; do
    linea="${linea//$'\r'/}"
    [[ "$linea" =~ ^[[:space:]]*# ]] && continue
    if [[ "$linea" == "$clave="* ]]; then
      valor="${linea#*=}"
      valor="${valor#\"}"
      valor="${valor%\"}"
    fi
  done <"$1"
  printf '%s' "$valor"
}

# TLS hacia PostgreSQL (AM24, docs/02-arquitectura-de-software/05-despliegue.md §4): ninguna cadena de conexión a la base puede ir sin TLS ni sin verificar al servidor, porque la contraseña de la cuenta viajaría en claro por la red
# privada o a quien se haga pasar por la base. Se lee cada archivo .env sin ejecutarlo y se anota (por NOMBRE de clave, nunca con el valor) lo que no cumpla:
#   - una cadena de Npgsql (las cuentas de los servicios BABELHUB__{Ecommerce,Inventario}__Base__*, las propietarias BABELHUB__Migrador__*__Propietaria o cualquier valor con forma de cadena: Host=…;Username=…) exige `SSL Mode=VerifyFull`
#     (todas las veces que aparezca: gana la última) y no admite `Trust Server Certificate=true`;
#   - una URI postgres:// (la de Evolution API, que usa Prisma) exige `sslmode=require` con `sslaccept=strict` y `sslcert=` (la CA privada). Prisma NO soporta `sslmode=verify-full`/`verify-ca`: los ignora y vuelve a «prefer» (cifra pero
#     no verifica al servidor), de modo que aquí se rechazan en vez de dar una falsa seguridad; tampoco `sslaccept=accept_invalid_certs`, `disable`, `allow` ni `prefer`.
comprobar_tls_de_postgres() { # archivo ruta
  local archivo="$1" ruta="$2" linea clave valor modo parametros parametro modo_uri="" aceptar="" certificado="" resto repetidos
  local -a modos partes
  [[ "$archivo" == *.env ]] || return 0
  local nocase_antes; nocase_antes="$(shopt -p nocasematch || true)"   # «shopt -p» sale con 1 si la opción está apagada
  while IFS= read -r linea || [[ -n "$linea" ]]; do
    linea="${linea//$'\r'/}"
    [[ "$linea" =~ ^[[:space:]]*# || "$linea" != *=* ]] && continue
    # Se lee como lo lee Docker Compose (env_file): sin «export», con espacios recortados y con un par de comillas, dobles o simples, quitado.
    clave="${linea%%=*}"; clave="${clave#"${clave%%[![:space:]]*}"}"; clave="${clave%"${clave##*[![:space:]]}"}"; clave="${clave#export }"
    valor="${linea#*=}"; valor="${valor#"${valor%%[![:space:]]*}"}"; valor="${valor%"${valor##*[![:space:]]}"}"
    if [[ "$valor" =~ ^\"(.*)\"$ || "$valor" =~ ^\'(.*)\'$ ]]; then valor="${BASH_REMATCH[1]}"; fi
    [[ "$valor" == CAMBIAR ]] && continue   # ya lo anota la comprobación de «CAMBIAR»
    shopt -s nocasematch   # Npgsql no distingue mayúsculas en las palabras clave ni en los valores
    if [[ "$clave" =~ ^BABELHUB__(Ecommerce|Inventario)__Base__ || "$clave" =~ ^BABELHUB__Migrador__.*__Propietaria$ ]] \
      || [[ "$valor" =~ (^|\;)[[:space:]]*(Host|Server|Hostname)[[:space:]]*= && "$valor" =~ (Username|User[[:space:]]?Name|User[[:space:]]?ID|UID)[[:space:]]*= ]]; then
      eval "$nocase_antes"
      mapfile -t modos < <(grep -oiE 'ssl[ _]?mode[[:space:]]*=[[:space:]]*[A-Za-z-]*' <<<"$valor" | sed -E 's/^[^=]*=[[:space:]]*//' | tr '[:upper:]' '[:lower:]')
      if [[ ${#modos[@]} -eq 0 ]]; then
        anotar "$archivo: $clave no pide TLS a PostgreSQL (falta «SSL Mode=VerifyFull»): la contraseña de la cuenta viajaría sin cifrar por la red privada (AM24)"
      else
        for modo in "${modos[@]}"; do
          [[ "$modo" == verifyfull ]] || anotar "$archivo: $clave usa «SSL Mode=$modo»: debe ser «SSL Mode=VerifyFull» (cifrado y servidor verificado con la CA privada, AM24)"
        done
      fi
      # Npgsql lee como verdadero true, t, yes, y, on y 1 (cualquier mayúscula).
      if grep -qiE 'trust[ _]?server[ _]?certificate[[:space:]]*=[[:space:]]*(true|t|yes|y|1|on)([^A-Za-z0-9]|$)' <<<"$valor"; then
        anotar "$archivo: $clave lleva «Trust Server Certificate» activo: no se verificaría al servidor de PostgreSQL (AM24)"
      fi
    elif [[ "$valor" =~ ^postgres(ql)?:// ]]; then
      eval "$nocase_antes"
      modo_uri="" aceptar="" certificado=""
      # Los parámetros van tras el último «@» (una contraseña puede llevar «?» o «@»): sin «?» no hay ninguno.
      resto="${valor#*://}"; resto="${resto##*@}"
      parametros=""; [[ "$resto" == *\?* ]] && parametros="${resto#*\?}"
      IFS='&' read -r -a partes <<<"$parametros"
      repetidos="$(printf '%s\n' "${partes[@]:-}" | { grep -iE '^ssl' || true; } | cut -d= -f1 | tr '[:upper:]' '[:lower:]' | sort | uniq -d | tr '\n' ' ')"
      [[ -z "$repetidos" ]] || anotar "$archivo: $clave repite parámetros de TLS ($repetidos): no se sabe cuál valdría (AM24)"
      for parametro in "${partes[@]:-}"; do
        case "${parametro%%=*}" in
          sslmode) modo_uri="${parametro#*=}" ;;
          sslaccept) aceptar="${parametro#*=}" ;;
          sslcert) certificado="${parametro#*=}" ;;
        esac
      done
      [[ "$modo_uri" == require ]] || anotar "$archivo: $clave usa «sslmode=${modo_uri:-(ausente)}»: debe ser «sslmode=require» con «sslaccept=strict» y «sslcert=» (Prisma ignora verify-full y verify-ca y no verificaría al servidor, AM24)"
      [[ "$aceptar" == strict ]] || anotar "$archivo: $clave usa «sslaccept=${aceptar:-(ausente)}»: debe ser «sslaccept=strict» (valida la cadena y el nombre del servidor de PostgreSQL, AM24)"
      [[ -n "$certificado" ]] || anotar "$archivo: $clave no lleva «sslcert=» con la CA privada: el servidor de PostgreSQL no se podría verificar (AM24)"
    fi
    eval "$nocase_antes"
  done <"$ruta"
}

if [[ -n "$SECRETOS" ]]; then
  # La raíz de confianza de los procesos (SSL_CERT_FILE, DD-55): las autoridades públicas de ESTA VM más la CA privada de la cuenta, para que el HTTPS entre servicios y el TLS
  # del Redis de contadores se verifiquen sin desactivar ninguna comprobación. Lo arma la propia VM cada vez (si se actualizan sus autoridades, el paquete las sigue); nunca viaja.
  if [[ -s "$SECRETOS/tls-ca.pem" ]]; then
    raices="${BABELHUB_RAICES_PUBLICAS:-/etc/ssl/certs/ca-certificates.crt}"
    if [[ -s "$raices" ]]; then
      paquete="$(mktemp "$SECRETOS/.ca-bundle.XXXXXX")"
      { cat "$raices"; echo; cat "$SECRETOS/tls-ca.pem"; } >"$paquete"
      chmod 0644 "$paquete"
      mv -f "$paquete" "$SECRETOS/ca-bundle.pem"
    else
      anotar "no existe $raices (las autoridades de certificación públicas de la VM): instala ca-certificates"
    fi
  fi
  # Cada contenedor corre con un usuario sin privilegios (los servicios .NET, 1654; Redis, 999:1000) y un secreto montado conserva el dueño y los permisos del archivo de la VM:
  # sin esto un archivo de root con permisos 600 no lo podría leer su proceso. Se entrega a quien lo lee y queda en 400 (solo lectura, y de nadie más).
  entregar() { # archivo dueño:grupo
    [[ -e "$SECRETOS/$1" && " ${ARCHIVOS[*]} " == *" $1 "* ]] || return 0
    "$CHOWN" "$2" "$SECRETOS/$1" 2>/dev/null && chmod 0400 "$SECRETOS/$1" || anotar "no se pudo entregar $1 a su usuario del contenedor ($2): ejecuta scripts/desplegar.sh con sudo"
  }
  for archivo in tls-ecommerce-cert.pem tls-ecommerce-key.pem tls-inventario-cert.pem tls-inventario-key.pem tls-gestion-cert.pem tls-gestion-key.pem ca-bundle.pem; do entregar "$archivo" 1654:1654; done
  for archivo in redis-contadores-cert.pem redis-contadores-key.pem redis-contadores-ca.pem; do entregar "$archivo" 999:1000; done
  # PostgreSQL (alpine) corre como «postgres» (70:70): su arranque copia el certificado y la clave a memoria con los permisos que exige el servidor (infra/postgres/servidor/iniciar.sh).
  for archivo in tls-postgres-cert.pem tls-postgres-key.pem; do entregar "$archivo" 70:70; done
  # La CA privada que lee Evolution API (VM-VENTAS): corre como root con las capacidades descartadas, así que solo lee lo que es suyo; se entrega a root y queda en 400.
  entregar tls-ca.pem 0:0
  for archivo in "${ARCHIVOS[@]}"; do
    ruta="$SECRETOS/$archivo"
    if [[ ! -f "$ruta" ]]; then
      anotar "falta el archivo de secretos $archivo"
      continue
    fi
    if [[ ! -s "$ruta" ]]; then
      anotar "el archivo de secretos $archivo está vacío"
      continue
    fi
    permisos="$("$STAT" -c %a "$ruta" 2>/dev/null || echo 777)"
    if [[ ! "$permisos" =~ ^[0-7]*[0-7]00$ ]]; then
      anotar "el archivo de secretos $archivo lo pueden leer otros usuarios (permisos $permisos): debe ser 600 o 400"
    fi
    if grep -q 'AGE-SECRET-KEY-' "$ruta" 2>/dev/null; then
      anotar "el archivo $archivo contiene una clave privada de age: no puede estar en la VM"
    fi
    # Un valor «CAMBIAR» es lo que dejó scripts/generar-secretos.sh para lo que solo puede dar un tercero (la cuenta de correo, las llaves del almacén, el token del túnel…).
    if grep -qE '^[A-Za-z0-9_]+="?CAMBIAR"?[[:space:]]*$' "$ruta" 2>/dev/null; then
      anotar "el archivo de secretos $archivo tiene valores «CAMBIAR» sin completar (los lista PENDIENTES.txt de la carpeta que generó scripts/generar-secretos.sh)"
    fi
    comprobar_tls_de_postgres "$archivo" "$ruta"
    # La cuenta propietaria de las bases (administración, COMP-2) solo puede estar en migrador.env: un servicio con ella en su entorno la tendría en un proceso expuesto.
    if grep -q 'BABELHUB__Migrador__' "$ruta" 2>/dev/null; then
      anotar "el archivo $archivo lleva claves BABELHUB__Migrador__*: la cuenta propietaria de las bases solo puede estar en migrador.env"
    fi
  done

  # Ninguna clave privada que no use el Compose de esta VM puede estar en su carpeta de secretos (la CA privada y la clave de age se quedan en la carpeta custodia/ del equipo de la persona responsable).
  for ruta in "$SECRETOS"/* "$SECRETOS"/.[!.]*; do
    [[ -f "$ruta" ]] || continue
    nombre="$(basename "$ruta")"
    [[ " ${ARCHIVOS[*]} " == *" $nombre "* ]] && continue
    if [[ "$nombre" == ca-clave.pem || "$nombre" == age-clave-privada* ]] || grep -qE 'PRIVATE KEY|AGE-SECRET-KEY-' "$ruta" 2>/dev/null; then
      anotar "$nombre es una clave privada que no usa esta VM: no debe estar en su carpeta de secretos (¿copiaste la carpeta entera? copia solo la de esta VM)"
    fi
  done
  tabla="$INFRA/secretos-requeridos.txt"
  if [[ -f "$tabla" ]]; then
    while read -r archivo clave minimo resto; do
      [[ -z "${archivo:-}" || "$archivo" == \#* ]] && continue
      [[ " ${ARCHIVOS[*]} " == *" $archivo "* ]] || continue
      [[ -s "$SECRETOS/$archivo" ]] || continue
      valor="$(leer_valor "$SECRETOS/$archivo" "$clave")"
      if [[ -z "$valor" ]]; then
        anotar "falta la clave $clave en $archivo"
      elif [[ -n "${minimo:-}" && ${#valor} -lt $minimo ]]; then
        anotar "la clave $clave de $archivo tiene menos de $minimo caracteres"
      fi
      if [[ "$clave" == BABELHUB_AGE_DESTINATARIO && -n "$valor" && "$valor" != age1* ]]; then
        anotar "BABELHUB_AGE_DESTINATARIO de $archivo no es una clave pública de age (age1…)"
      fi
    done <"$tabla"
  else
    anotar "falta la tabla de secretos requeridos $tabla"
  fi
  # El entorno del E-commerce y su pasarela de pago (PAG-34), en cada archivo del E-commerce de esta VM. Nunca se cobra «a ciegas»: Staging (la revisión de la tienda por Culqi) solo con la pasarela SIMULADA, que no
  # cobra nada; Production solo con la real (culqi) y su llave de producción (sk_live_…). Cualquier otro entorno, o una mezcla (por ejemplo Production con el simulador), no se despliega.
  for archivo in ecommerce.env ecommerce-gestion.env; do
    [[ " ${ARCHIVOS[*]} " == *" $archivo "* && -s "$SECRETOS/$archivo" ]] || continue
    entorno="$(leer_valor "$SECRETOS/$archivo" ASPNETCORE_ENVIRONMENT)"
    pasarela="$(leer_valor "$SECRETOS/$archivo" BABELHUB__Pagos__Pasarela)"
    case "$entorno" in
      Staging)
        [[ "$pasarela" == simulador ]] || anotar "$archivo: ASPNETCORE_ENVIRONMENT=Staging solo admite BABELHUB__Pagos__Pasarela=simulador (la pasarela real se prueba con llaves en Production)"
        ;;
      Production)
        [[ "$pasarela" == culqi ]] || anotar "$archivo: ASPNETCORE_ENVIRONMENT=Production exige BABELHUB__Pagos__Pasarela=culqi; el simulador no cobra y no se acepta en Production"
        [[ "$(leer_valor "$SECRETOS/$archivo" BABELHUB__Pagos__LlavePrivada)" == sk_live_* ]] || anotar "$archivo: ASPNETCORE_ENVIRONMENT=Production exige BABELHUB__Pagos__LlavePrivada con la llave de producción de Culqi (sk_live_…)"
        ;;
      "") ;;   # la falta de la clave ya la anotó la tabla de secretos requeridos
      *) anotar "$archivo: ASPNETCORE_ENVIRONMENT debe ser Staging o Production (no «$entorno»)" ;;
    esac
    # El operador logístico es el simulado (DD-22, PAG-42): no existe uno real y sin operador el E-commerce no arranca en un entorno de tipo producción.
    [[ -z "$entorno" || "$(leer_valor "$SECRETOS/$archivo" BABELHUB__Envios__Operador)" == simulador ]] || anotar "$archivo: BABELHUB__Envios__Operador debe ser simulador (no hay operador logístico real, DD-22)"
    # Una llave de Culqi solo sirve en Production: en Staging no hay adaptador real, y una llave suelta en un entorno que no cobra es un secreto sin uso.
    if [[ "$entorno" == Staging && -n "$(leer_valor "$SECRETOS/$archivo" BABELHUB__Pagos__LlavePrivada)" ]]; then
      anotar "$archivo: ASPNETCORE_ENVIRONMENT=Staging no debe llevar BABELHUB__Pagos__LlavePrivada (la llave de Culqi va solo en Production)"
    fi
  done
fi

# ---------- 3. Tarea de respaldo de la VM de datos ----------

TAREA_DE_RESPALDO="$CARPETA_CRON/babelhub-respaldo"
if [[ "$VM" == inventario && ! -d "$CARPETA_CRON" ]]; then
  anotar "no existe $CARPETA_CRON: la VM de datos necesita cron para el respaldo diario"
fi

# La vigilancia del espacio reservado del archivado (PLA-21) avisa por correo desde esta VM con una credencial mínima propia (aviso.env, en la
# carpeta de secretos: solo sirve para enviar los avisos de esta VM). Sin ella el aviso del 80 % no saldría, así que no se despliega.
if [[ "$VM" == inventario && -n "$SECRETOS" ]]; then
  AVISOS="$SECRETOS/aviso.env"
  if [[ ! -s "$AVISOS" ]]; then
    anotar "falta el archivo de secretos aviso.env (la credencial de correo para avisar del espacio reservado, PLA-21)"
  else
    # Lo carga alertar.sh como root desde el cron: debe ser de root y no escribible ni legible por otros (se lee sin ejecutarlo, pero no se confía).
    lectura="$("$STAT" -c '%U %a' "$AVISOS" 2>/dev/null || echo '? 777')"
    [[ "${lectura% *}" == root && "${lectura#* }" =~ ^[0-7]?[46]00$ ]] || anotar "el archivo de secretos aviso.env es de ${lectura% *} con permisos ${lectura#* }: debe ser de root y 600 o 400 (lo carga el cron como root); además lo pueden leer otros usuarios si los permisos no son 600 o 400"
    if grep -qE '^[A-Za-z0-9_]+="?CAMBIAR"?[[:space:]]*$' "$AVISOS" 2>/dev/null; then
      anotar "el archivo de secretos aviso.env tiene valores «CAMBIAR» sin completar"
    fi
    for clave in AUX_SMTP_URL AUX_SMTP_USUARIO AUX_SMTP_CLAVE AUX_CORREO_DE AUX_CORREO_PARA; do
      [[ -n "$(leer_valor "$AVISOS" "$clave")" ]] || anotar "falta la clave $clave en aviso.env"
    done
    [[ "$(leer_valor "$AVISOS" AUX_SMTP_URL)" != smtp://* ]] || anotar "AUX_SMTP_URL de aviso.env usa smtp:// sin TLS implícito: usa smtps://"
    if grep -q 'AGE-SECRET-KEY-' "$AVISOS" 2>/dev/null; then
      anotar "el archivo aviso.env contiene una clave privada de age: no puede estar en la VM"
    fi
  fi
  # El cron corre alertar.sh COMO ROOT con esta credencial: esos scripts del repositorio deben ser de root y no escribibles por otros (si no,
  # quien pueda editarlos gana root en la siguiente corrida).
  for guion in alertar.sh comun.sh; do
    lectura="$("$STAT" -c '%U %a' "$RAIZ/scripts/vm-aux/$guion" 2>/dev/null || echo '? ?')"
    if [[ "${lectura% *}" != root ]] || [[ ! "${lectura#* }" =~ ^[0-7]?[0-7][0145][0145]$ ]]; then
      anotar "scripts/vm-aux/$guion es de ${lectura% *} con permisos ${lectura#* }: debe ser de root y sin escritura para otros (lo ejecuta el cron como root)"
    fi
  done
  # Y la carpeta de esos scripts (quien pueda escribir en ella puede renombrar uno por otro).
  lectura="$("$STAT" -c '%U %a' "$RAIZ/scripts/vm-aux" 2>/dev/null || echo '? ?')"
  if [[ "${lectura% *}" != root ]] || [[ ! "${lectura#* }" =~ ^[0-7]?[0-7][0145][0145]$ ]]; then
    anotar "la carpeta scripts/vm-aux es de ${lectura% *} con permisos ${lectura#* }: debe ser de root y sin escritura para otros (la ejecuta el cron como root)"
  fi
  [[ "$RAIZ" =~ ^[A-Za-z0-9._:/-]+$ && "$SECRETOS" =~ ^[A-Za-z0-9._:/-]+$ ]] || anotar "las rutas del repositorio y de los secretos solo pueden tener letras, números y . _ : / - (se escriben en la tarea de cron)"
fi

# La protección de la réplica de gestión (DD-104) la ejecuta cron como root cada minuto: su script y la carpeta deben ser de root y sin escritura para otros.
PROTEGER_GESTION=0
[[ "$VM" == ventas && -n "${IP_INVENTARIO:-}" ]] && PROTEGER_GESTION=1
if [[ "$PROTEGER_GESTION" -eq 1 ]]; then
  [[ -d "$CARPETA_CRON" ]] || anotar "no existe $CARPETA_CRON: VM-VENTAS necesita cron para reponer la protección de la réplica de gestión"
  for ruta in "$RAIZ/scripts" "$RAIZ/scripts/proteger-gestion.sh"; do
    lectura="$("$STAT" -c '%U %a' "$ruta" 2>/dev/null || echo '? ?')"
    if [[ "${lectura% *}" != root ]] || [[ ! "${lectura#* }" =~ ^[0-7]?[0-7][0145][0145]$ ]]; then
      anotar "${ruta#"$RAIZ"/} es de ${lectura% *} con permisos ${lectura#* }: debe ser de root y sin escritura para otros (lo ejecuta el cron como root)"
    fi
  done
  [[ "$RAIZ" =~ ^[A-Za-z0-9._:/-]+$ ]] || anotar "la ruta del repositorio solo puede tener letras, números y . _ : / - (se escribe en la tarea de cron)"
fi

# migrador.env solo existe en la VM de datos: en cualquier otra sería una credencial de administración sin uso.
if [[ "$VM" != inventario && -n "$SECRETOS" && -e "$SECRETOS/migrador.env" ]]; then
  anotar "existe migrador.env en la VM $VM: la cuenta propietaria de las bases solo puede estar en la VM de datos"
fi

# La migración de las bases (COMP-2): la VM de datos aloja PostgreSQL de los dos servicios y es la única que migra, con las cuentas propietarias de
# migrador.env (administración de cada base, solo para migrar: nunca están en los archivos de los servicios, que solo llevan sus cuentas de conexión).
if [[ "$VM" == inventario && -n "$SECRETOS" ]]; then
  MIGRADOR="$SECRETOS/migrador.env"
  if [[ ! -s "$MIGRADOR" ]]; then
    anotar "falta el archivo de secretos migrador.env (las cuentas propietarias de las bases para migrar sus esquemas y roles, COMP-2)"
  else
    permisos="$("$STAT" -c %a "$MIGRADOR" 2>/dev/null || echo 777)"
    [[ "$permisos" =~ ^[0-7]*[0-7]00$ ]] || anotar "el archivo de secretos migrador.env lo pueden leer otros usuarios (permisos $permisos): debe ser 600 o 400"
    for clave in BABELHUB__Migrador__Inventario__Propietaria BABELHUB__Migrador__Ecommerce__Propietaria; do
      [[ -n "$(leer_valor "$MIGRADOR" "$clave")" ]] || anotar "falta la clave $clave en migrador.env"
    done
    comprobar_tls_de_postgres migrador.env "$MIGRADOR"   # las cuentas propietarias también se conectan por TLS verificado (AM24)
    if grep -q 'AGE-SECRET-KEY-' "$MIGRADOR" 2>/dev/null; then
      anotar "el archivo migrador.env contiene una clave privada de age: no puede estar en la VM"
    fi
  fi
fi

if [[ ${#falta[@]} -gt 0 ]]; then
  echo "No se despliega $VM: faltan o están mal ${#falta[@]} cosas:" >&2
  printf '  - %s\n' "${falta[@]}" >&2
  exit 1
fi

# ---------- 4. Levantar y esperar la salud ----------

compose() { "$DOCKER" compose --project-directory "$INFRA" -f "$COMPOSE" ${EXTRA:+-f "$EXTRA"} "$@"; }

echo "Desplegando $VM..."
compose config --quiet || { echo "El Compose de $VM no es válido." >&2; exit 1; }
# Los servicios que se levantan: todos los del Compose menos los de --sin (que deben existir: un error de escritura no se pasa por alto).
mapfile -t SERVICIOS_DEL_COMPOSE < <(compose config --services)
SERVICIOS=()
for servicio in "${SERVICIOS_DEL_COMPOSE[@]}"; do
  [[ " ${SIN[*]:-} " == *" $servicio "* ]] || SERVICIOS+=("$servicio")
done
for omitido in "${SIN[@]:-}"; do
  [[ -z "$omitido" || " ${SERVICIOS_DEL_COMPOSE[*]} " == *" $omitido "* ]] || { echo "--sin: $VM no tiene el servicio «$omitido» (tiene: ${SERVICIOS_DEL_COMPOSE[*]})." >&2; exit 2; }
done
[[ ${#SIN[@]} -eq 0 ]] || echo "Se deja sin levantar: ${SIN[*]}"
[[ ${#SIN[@]} -eq 0 || ${#SERVICIOS[@]} -gt 0 ]] || { echo "--sin deja $VM sin ningún servicio." >&2; exit 2; }
# Antes de levantar ningún contenedor de VM-VENTAS: la regla que deja llegar a la réplica de gestión solo a Nginx, y su reposición (DD-104).
if [[ "$PROTEGER_GESTION" -eq 1 ]]; then
  if ! bash "$RAIZ/scripts/proteger-gestion.sh" "$IP_INVENTARIO"; then
    echo "No se despliega $VM: sin la regla, cualquier contenedor de esta VM podría inventar el origen del cliente ante la réplica de gestión." >&2
    exit 1
  fi
  temporal="$(mktemp "$CARPETA_CRON/.babelhub-gestion.XXXXXX")"
  cat >"$temporal" <<EOF
# Instalado por scripts/desplegar.sh: de los contenedores de esta VM, solo Nginx llega a la réplica de gestión (DD-104). Se repone cada minuto.
PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
* * * * * root /bin/bash $RAIZ/scripts/proteger-gestion.sh $IP_INVENTARIO >> /var/log/babelhub-gestion.log 2>&1
EOF
  chmod 0644 "$temporal"
  mv -f "$temporal" "$CARPETA_CRON/babelhub-gestion"
  echo "ok protección de la réplica de gestión ($CARPETA_CRON/babelhub-gestion)"
fi

# La migración de las bases antes de levantar los servicios (COMP-2): primero PostgreSQL sano, después los esquemas y roles de cada servicio con la
# cuenta propietaria de migrador.env, que solo vive en el entorno de ese proceso (nunca en un argumento ni en un archivo de los servicios). Es idempotente.
if [[ "$VM" == inventario ]]; then
  echo "Migrando las bases (esquemas y roles)..."
  if ! compose up -d --wait --wait-timeout "$ESPERA" postgres; then
    echo "PostgreSQL de $VM no quedó sano en $ESPERA segundos: no se migra." >&2
    exit 1
  fi
  migrar() { # servicio variable-de-la-cuenta-propietaria [variable-opcional-de-migrador.env ...]
    local servicio="$1" variable="$2" opcional valor
    shift 2
    (
      export "$variable=$(leer_valor "$MIGRADOR" "$variable")"
      adicionales=()
      # Lo opcional de migrador.env (por ejemplo, la versión con la que se siembran los textos legales vigentes) solo viaja si está: sin la variable la migración no siembra nada.
      for opcional in "$@"; do
        valor="$(leer_valor "$MIGRADOR" "$opcional")"
        if [[ -n "$valor" ]]; then
          export "$opcional=$valor"
          adicionales+=(-e "$opcional")
        fi
      done
      compose run --rm --no-deps -e "$variable" ${adicionales[@]+"${adicionales[@]}"} "$servicio" migrar
    ) || { echo "La migración de la base de $servicio falló: no se despliega $VM." >&2; exit 1; }
  }
  migrar inventario BABELHUB__Migrador__Inventario__Propietaria
  migrar ecommerce-gestion BABELHUB__Migrador__Ecommerce__Propietaria BABELHUB__Migrador__Ecommerce__VersionDeTextos
fi

# Sin --sin se levanta todo el Compose (el comando de siempre); con --sin, solo los servicios que quedan.
SELECCION=()
[[ ${#SIN[@]} -eq 0 ]] || SELECCION=("${SERVICIOS[@]}")
if ! compose up -d --wait --wait-timeout "$ESPERA" ${SELECCION[@]+"${SELECCION[@]}"}; then
  echo "Algún componente de $VM no quedó sano en $ESPERA segundos." >&2
  compose ps >&2 || true
  exit 1
fi

no_sanos=0
for servicio in "${SERVICIOS[@]}"; do
  id="$(compose ps -q "$servicio" | head -n 1)"
  # Un componente con tiempo de arranque propio (n8n, Evolution) puede seguir en «starting» un instante tras el `up --wait`: se le dan hasta 60 s antes de darlo por no sano.
  for _ in $(seq 1 30); do
    estado="$([[ -n "$id" ]] && "$DOCKER" inspect --format '{{.State.Health.Status}}' "$id" 2>/dev/null || echo ausente)"
    [[ "$estado" == starting ]] || break
    sleep 2
  done
  if [[ "$estado" == healthy ]]; then
    echo "ok $servicio"
  else
    echo "x $servicio: $estado" >&2
    no_sanos=$((no_sanos + 1))
  fi
done
if [[ $no_sanos -gt 0 ]]; then
  echo "$no_sanos componentes de $VM no responden su comprobación de salud." >&2
  exit 1
fi

if [[ "$VM" == inventario ]]; then
  temporal="$(mktemp "$CARPETA_CRON/.babelhub-respaldo.XXXXXX")"
  cat >"$temporal" <<EOF
# Instalado por scripts/desplegar.sh: respaldo diario cifrado de PostgreSQL hacia el almacén (PLA-09). Hora UTC.
30 2 * * * root docker exec babelhub-inventario-postgres-1 /opt/babelhub/respaldar.sh >> /var/log/babelhub-respaldo.log 2>&1
# Vigilancia del espacio local reservado del archivado de cambios (PLA-21), cada minuto: avisa por correo (credencial propia de aviso.env) al 80 % y al declarar roto el archivo de cambios.
* * * * * root AUX_ENV=$SECRETOS/aviso.env AUX_ESTADO=/var/lib/babelhub-aviso $RAIZ/scripts/vm-aux/alertar.sh espacio-respaldos docker exec babelhub-inventario-postgres-1 /opt/babelhub/vigilar-espacio.sh
EOF
  chmod 0644 "$temporal"
  mv -f "$temporal" "$TAREA_DE_RESPALDO"
  echo "ok tarea diaria de respaldo ($TAREA_DE_RESPALDO)"
fi

echo "VM $VM desplegada y sana."
