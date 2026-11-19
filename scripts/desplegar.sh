#!/usr/bin/env bash
# Despliega una VM con un solo comando (PLA-04, AC-PLA-04): levanta todos sus componentes y espera a que cada uno responda su comprobación
# de salud. Es idempotente (`docker compose up -d --wait`): repetirlo no cambia lo que ya está sano. Se ejecuta EN la VM, con sus secretos.
#
#   BABELHUB_SECRETOS=/etc/babelhub/secretos IP_PRIVADA=10.0.0.10 IMAGEN_ECOMMERCE=<imagen@sha256:…> \
#       scripts/desplegar.sh <borde|ventas|inventario> [--secretos <carpeta>] [--infra <carpeta>] [--espera <segundos>] [--cron <carpeta>]
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
# Variables opcionales: DOCKER (comando de docker, por defecto `docker`), STAT (por defecto `stat`).
# Códigos de salida: 0 desplegado y sano, 1 falta algo o algún componente no quedó sano, 2 uso inválido.
set -euo pipefail

DOCKER="${DOCKER:-docker}"
STAT="${STAT:-stat}"
RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INFRA="$RAIZ/infra"
ESPERA=300
CARPETA_CRON="/etc/cron.d"
VM=""

uso() {
  echo "Uso: $0 <borde|ventas|inventario> [--secretos <carpeta>] [--infra <carpeta>] [--espera <segundos>] [--cron <carpeta>]" >&2
  exit 2
}

falta=()
anotar() { falta+=("$1"); }

[[ $# -ge 1 ]] || uso
VM="$1"
shift
case "$VM" in borde | ventas | inventario) ;; *) uso ;; esac
while [[ $# -gt 0 ]]; do
  case "$1" in
    --secretos) [[ $# -ge 2 ]] || uso; export BABELHUB_SECRETOS="$2"; shift 2 ;;
    --infra) [[ $# -ge 2 ]] || uso; INFRA="$2"; shift 2 ;;
    --espera) [[ $# -ge 2 && "$2" =~ ^[0-9]+$ ]] || uso; ESPERA="$2"; shift 2 ;;
    --cron) [[ $# -ge 2 ]] || uso; CARPETA_CRON="$2"; shift 2 ;;
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

if [[ -n "$SECRETOS" ]]; then
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

compose() { "$DOCKER" compose --project-directory "$INFRA" -f "$COMPOSE" "$@"; }

echo "Desplegando $VM..."
compose config --quiet || { echo "El Compose de $VM no es válido." >&2; exit 1; }
# La migración de las bases antes de levantar los servicios (COMP-2): primero PostgreSQL sano, después los esquemas y roles de cada servicio con la
# cuenta propietaria de migrador.env, que solo vive en el entorno de ese proceso (nunca en un argumento ni en un archivo de los servicios). Es idempotente.
if [[ "$VM" == inventario ]]; then
  echo "Migrando las bases (esquemas y roles)..."
  if ! compose up -d --wait --wait-timeout "$ESPERA" postgres; then
    echo "PostgreSQL de $VM no quedó sano en $ESPERA segundos: no se migra." >&2
    exit 1
  fi
  migrar() { # servicio variable-de-la-cuenta-propietaria
    local servicio="$1" variable="$2"
    (
      export "$variable=$(leer_valor "$MIGRADOR" "$variable")"
      compose run --rm --no-deps -e "$variable" "$servicio" migrar
    ) || { echo "La migración de la base de $servicio falló: no se despliega $VM." >&2; exit 1; }
  }
  migrar inventario BABELHUB__Migrador__Inventario__Propietaria
  migrar ecommerce-gestion BABELHUB__Migrador__Ecommerce__Propietaria
fi

if ! compose up -d --wait --wait-timeout "$ESPERA"; then
  echo "Algún componente de $VM no quedó sano en $ESPERA segundos." >&2
  compose ps >&2 || true
  exit 1
fi

mapfile -t SERVICIOS < <(compose config --services)
no_sanos=0
for servicio in "${SERVICIOS[@]}"; do
  id="$(compose ps -q "$servicio" | head -n 1)"
  estado="$([[ -n "$id" ]] && "$DOCKER" inspect --format '{{.State.Health.Status}}' "$id" 2>/dev/null || echo ausente)"
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
