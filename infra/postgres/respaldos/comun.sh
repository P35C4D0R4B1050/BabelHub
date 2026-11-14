# Lo que comparten el archivado de cambios y el respaldo (PLA-09, PLA-15, PLA-17; D09, D10). Se carga con «. /opt/babelhub/comun.sh».
# POSIX sh (Alpine, busybox): la imagen de PostgreSQL no trae bash.
#
# Variables (las pone el Compose desde el archivo de secretos de VM-INVENTARIO; ninguna se imprime):
#   BABELHUB_AGE_DESTINATARIO              la clave PÚBLICA de age: en esta VM solo se puede cifrar, nunca descifrar
#   RCLONE_CONFIG_ALMACEN_TYPE=s3 y las demás RCLONE_CONFIG_ALMACEN_* (endpoint, región, clave de acceso y clave secreta de la credencial
#                                          `respaldos-escritor`, que solo puede CREAR objetos)
#   BABELHUB_ESPERA (por defecto /var/respaldos)   volumen local y reservado para lo que espera su subida
#   BABELHUB_CONFIG (por defecto /config)           la carpeta de configuración de la VM, sin secretos

ESPERA="${BABELHUB_ESPERA:-/var/respaldos}"
CONFIG="${BABELHUB_CONFIG:-/config}"
CONTENEDOR_DIARIOS=respaldos-diarios
CONTENEDOR_SEMANALES=respaldos-semanales
CONTENEDOR_MENSUALES=respaldos-mensuales
CONTENEDOR_WAL=respaldos-wal

fallo() {
  echo "respaldos: $*" >&2
  exit 1
}

verificar_entorno() {
  [ -n "${BABELHUB_AGE_DESTINATARIO:-}" ] || fallo "falta BABELHUB_AGE_DESTINATARIO (la clave pública de age)"
  case "$BABELHUB_AGE_DESTINATARIO" in
    age1*) ;;
    *) fallo "BABELHUB_AGE_DESTINATARIO no es un destinatario de age (debe empezar por age1)" ;;
  esac
  # La clave que descifra no puede estar nunca en esta VM (PLA-17): si aparece en el entorno, se rechaza todo.
  if env | grep -q 'AGE-SECRET-KEY-'; then
    fallo "hay una clave privada de age en el entorno de esta VM: no se continúa"
  fi
  [ "${RCLONE_CONFIG_ALMACEN_TYPE:-}" = s3 ] || fallo "falta RCLONE_CONFIG_ALMACEN_TYPE=s3"
  [ -n "${RCLONE_CONFIG_ALMACEN_ACCESS_KEY_ID:-}" ] || fallo "falta la clave de acceso del almacén"
  [ -n "${RCLONE_CONFIG_ALMACEN_SECRET_ACCESS_KEY:-}" ] || fallo "falta la clave secreta del almacén"
  mkdir -p "$ESPERA" 2>/dev/null || true
  [ -d "$ESPERA" ] && [ -w "$ESPERA" ] || fallo "no se puede escribir en $ESPERA"
}

# Sube un archivo ya cifrado a un contenedor sin comprobar ni sobrescribir nada del destino: la credencial solo puede crear (PLA-16), así
# que ni lista ni pide metadatos. El nombre lleva la suma del contenido cifrado, de modo que repetir una subida nunca pisa otro contenido.
subir() {
  rclone copyto "$1" "almacen:$2/$3" \
    --no-check-dest --s3-no-check-bucket --s3-no-head \
    --retries 3 --low-level-retries 3 --contimeout 20s --timeout 120s --quiet \
    || fallo "no se pudo subir a $2"
}

suma_de() {
  sha256sum "$1" | cut -d' ' -f1
}

# Las copias de más duración que se hacen además del respaldo diario, según el día UTC de la fecha dada (segundos desde 1970):
# «semanal» el domingo y «mensual» el último día del mes (el día siguiente es el 1).
clases_del_dia() {
  _dia_de_la_semana=$(date -u -d "@$1" +%u)
  _dia_siguiente=$(date -u -d "@$(($1 + 86400))" +%d)
  _clases=""
  [ "$_dia_de_la_semana" = 7 ] && _clases="semanal"
  [ "$_dia_siguiente" = 01 ] && _clases="${_clases:+$_clases }mensual"
  echo "$_clases"
}

# ---------- Espacio local reservado y archivo de cambios roto (PLA-21, D11) ----------
# El espacio que puede ocupar lo pendiente cuando el almacén no está disponible (cambios sin archivar y respaldo en espera) está reservado y
# es menor de 5 GB: 4 GiB por defecto (BABELHUB_ESPACIO_RESERVADO, en bytes). `vigilar-espacio.sh` avisa al 80 % y, al agotarse, escribe la
# marca ARCHIVO_ROTO: mientras exista, `archivar-wal.sh` descarta los segmentos (la base no se llena) y solo un respaldo completo nuevo
# (`respaldar.sh`) la retira.
ARCHIVO_ROTO="$ESPERA/archivo-roto"
RESERVADO="${BABELHUB_ESPACIO_RESERVADO:-4294967296}"
RESERVADO_MAXIMO=5000000000

verificar_reserva() {
  case "$RESERVADO" in
    "" | *[!0-9]*)
      echo "respaldos: BABELHUB_ESPACIO_RESERVADO debe ser un número de bytes" >&2
      return 1
      ;;
  esac
  if [ "$RESERVADO" -le 0 ] || [ "$RESERVADO" -ge "$RESERVADO_MAXIMO" ]; then
    echo "respaldos: el espacio reservado debe ser positivo y menor de 5 GB (5000000000 bytes)" >&2
    return 1
  fi
}
