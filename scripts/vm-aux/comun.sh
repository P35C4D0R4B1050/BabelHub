# Lo que comparten los scripts de VM-AUX (PLA-20, PLA-38). Se carga con «. comun.sh».
#
# VM-AUX es el salto SSH y el vigilante: comprueba los respaldos desde FUERA de la VM de datos, sella el registro de supresiones y avisa por
# correo. No guarda ninguna clave privada ni ninguna credencial de borrado. Cada script usa SU credencial del almacén, configurada como un
# remoto de rclone en el archivo de entorno de VM-AUX (AUX_ENV, por defecto /etc/babelhub-aux/aux.env, 0600 y de su usuario):
#   RCLONE_CONFIG_VIGILANTE_*   respaldos-vigilante: solo lista
#   RCLONE_CONFIG_SELLO_*       supresiones-sello: crea sellos y lee las entradas del registro
#   RCLONE_CONFIG_COPIA_*       respaldos-copia-registro: solo crea en supresiones-copia
# (cada uno con _TYPE=s3, _PROVIDER, _ENDPOINT, _REGION, _ACCESS_KEY_ID y _SECRET_ACCESS_KEY), y para el correo y el latido:
#   AUX_SMTP_URL (smtps://…), AUX_SMTP_USUARIO, AUX_SMTP_CLAVE, AUX_CORREO_DE, AUX_CORREO_PARA (la credencial de correo propia y mínima, T26)
#   AUX_LATIDO_URL (la dirección de pulso de un servicio externo gratuito; es un secreto)
#   AUX_ESTADO (por defecto /var/lib/babelhub-aux): el último sello y las alertas ya avisadas
# Ningún script imprime un valor secreto. RCLONE y CURL son los comandos (las pruebas los sustituyen).

AUX_ENV="${AUX_ENV:-/etc/babelhub-aux/aux.env}"
if [[ -f "$AUX_ENV" ]]; then
  set -a
  # shellcheck disable=SC1090
  . "$AUX_ENV"
  set +a
fi
RCLONE="${RCLONE:-rclone}"
CURL="${CURL:-curl}"
AUX_ESTADO="${AUX_ESTADO:-/var/lib/babelhub-aux}"

fallo() {
  echo "vm-aux: $*" >&2
  exit 1
}

# La hora «AAAA-MM-DD HH:MM:SS» en UTC de unos segundos desde 1970: así comparar el texto es comparar el tiempo.
hora_texto() {
  date -u -d "@$1" '+%Y-%m-%d %H:%M:%S'
}

# «clave;hora» por objeto de un contenedor (remoto:contenedor), con la hora en UTC.
listar() {
  TZ=UTC "$RCLONE" lsf -R --files-only --format pt --separator ';' "$1"
}

# Sube un archivo sin comprobar ni sobrescribir nada del destino: las credenciales de VM-AUX que escriben solo pueden crear.
subir() {
  "$RCLONE" copyto "$1" "$2" \
    --no-check-dest --s3-no-check-bucket --s3-no-head \
    --retries 3 --low-level-retries 3 --contimeout 20s --timeout 120s --quiet
}

suma_de() {
  sha256sum "$1" | cut -d' ' -f1
}

# Envía un correo con el cuerpo que llega por la entrada estándar. La contraseña va en un archivo temporal 0600 (no en los argumentos del
# proceso) y el correo se origina aquí, fuera de la VM de datos. Devuelve distinto de cero si no se pudo enviar.
enviar_correo() {
  local asunto="$1" campo config mensaje codigo=0
  for campo in AUX_SMTP_URL AUX_SMTP_USUARIO AUX_SMTP_CLAVE AUX_CORREO_DE AUX_CORREO_PARA; do
    if [[ -z "${!campo:-}" ]]; then
      echo "vm-aux: falta $campo para enviar correo" >&2
      return 1
    fi
  done
  config="$(mktemp)"
  mensaje="$(mktemp)"
  chmod 600 "$config" "$mensaje"
  printf 'user = "%s:%s"\n' "$AUX_SMTP_USUARIO" "$AUX_SMTP_CLAVE" >"$config"
  {
    printf 'From: %s\r\nTo: %s\r\nSubject: %s\r\nDate: %s\r\nMIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\n\r\n' \
      "$AUX_CORREO_DE" "$AUX_CORREO_PARA" "$asunto" "$(date -R)"
    sed 's/$/\r/'
  } >"$mensaje"
  "$CURL" --silent --show-error --fail --max-time 30 --ssl-reqd --config "$config" --url "$AUX_SMTP_URL" \
    --mail-from "$AUX_CORREO_DE" --mail-rcpt "$AUX_CORREO_PARA" --upload-file "$mensaje" || codigo=$?
  rm -f "$config" "$mensaje"
  return "$codigo"
}
