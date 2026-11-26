# Lo que comparten los scripts de VM-AUX (PLA-20, PLA-38). Se carga con «. comun.sh».
#
# VM-AUX es el salto SSH y el vigilante: comprueba los respaldos desde FUERA de la VM de datos, sella el registro de supresiones y avisa por
# correo. No guarda ninguna clave privada ni ninguna credencial de borrado. Cada script usa SU credencial del almacén, configurada como un
# remoto de rclone en el archivo de entorno de VM-AUX (AUX_ENV, por defecto /etc/babelhub-aux/aux.env, de root, grupo babelhub-aux y 0640: el usuario lo lee pero no lo puede cambiar):
#   RCLONE_CONFIG_VIGILANTE_*   respaldos-vigilante: solo lectura en los respaldos de Cloudflare R2
#   RCLONE_CONFIG_REGISTRO_*    registro-vigilante: solo lista supresiones-copia en Oracle
#   RCLONE_CONFIG_SELLO_*       supresiones-sello: crea sellos y lee las entradas del registro
#   RCLONE_CONFIG_COPIA_*       respaldos-copia-registro: solo crea en supresiones-copia
# (cada uno con _TYPE=s3, _PROVIDER, _ENDPOINT, _REGION, _ACCESS_KEY_ID y _SECRET_ACCESS_KEY; en R2, proveedor Cloudflare y región auto),
# y para el correo y el latido:
#   AUX_SMTP_URL (smtps://…), AUX_SMTP_USUARIO, AUX_SMTP_CLAVE, AUX_CORREO_DE, AUX_CORREO_PARA (la credencial de correo propia y mínima, T26)
#   AUX_LATIDO_URL (la dirección de pulso de un servicio externo gratuito; es un secreto)
#   AUX_ESTADO (por defecto /var/lib/babelhub-aux): el último sello y las alertas ya avisadas
# Ningún script imprime un valor secreto. RCLONE y CURL son los comandos (las pruebas los sustituyen).

AUX_ENV="${AUX_ENV:-/etc/babelhub-aux/aux.env}"

# Carga el archivo de entorno SIN EJECUTARLO (nunca `source`): algunas tareas (la comprobación del endurecimiento) corren como root y el
# usuario sin privilegios `babelhub-aux` lee este archivo; si se ejecutara, quien comprometa a ese usuario ganaría root. Por eso, además de
# exigir que el archivo sea de root y no escribible por otros (lo comprueba verificar-endurecimiento.sh), solo se leen líneas `NOMBRE=valor`
# literales (el valor, opcionalmente entre comillas, no se expande: `$`, `` ` `` y `;` son texto) y solo de los nombres propios de VM-AUX.
# Cualquier otro nombre (SSHD_T, PS, STAT, AUX_ESTADO, AUX_SCRIPTS, CURL, RCLONE…) se ignora: esos solo los pone el entorno del proceso.
cargar_entorno() {
  local linea nombre valor
  [[ -f "$1" ]] || return 0
  while IFS= read -r linea || [[ -n "$linea" ]]; do
    linea="${linea%$'\r'}"
    [[ -z "${linea//[[:space:]]/}" || "${linea#"${linea%%[![:space:]]*}"}" == \#* ]] && continue
    linea="${linea#"${linea%%[![:space:]]*}"}"
    linea="${linea#export }"
    [[ "$linea" == *=* ]] || continue
    nombre="${linea%%=*}"
    valor="${linea#*=}"
    if [[ "$valor" == \"*\" && ${#valor} -ge 2 ]] || [[ "$valor" == \'*\' && ${#valor} -ge 2 ]]; then
      valor="${valor:1:${#valor}-2}"
    fi
    case "$nombre" in
      AUX_SMTP_URL | AUX_SMTP_USUARIO | AUX_SMTP_CLAVE | AUX_CORREO_DE | AUX_CORREO_PARA | AUX_LATIDO_URL | AUX_REAVISO_HORAS | AUX_MAX_HORAS_RESPALDO | AUX_MAX_MINUTOS_ATRASO | AUX_MAX_HORAS_COPIA_REGISTRO) ;;
      RCLONE_CONFIG_VIGILANTE_* | RCLONE_CONFIG_REGISTRO_* | RCLONE_CONFIG_SELLO_* | RCLONE_CONFIG_COPIA_* | RCLONE_CONFIG_CUOTA_*) [[ "$nombre" =~ ^RCLONE_CONFIG_[A-Z]+_[A-Z0-9_]+$ ]] || continue ;;
      *) continue ;;
    esac
    # El valor de un nombre ya fijado por el entorno del proceso (cron, pruebas) manda sobre el del archivo.
    [[ -n "${!nombre:-}" ]] && continue
    printf -v "$nombre" '%s' "$valor"
    export "$nombre"
  done <"$1"
}
cargar_entorno "$AUX_ENV"
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
# La hora es la que puso el almacén al recibir el objeto (--use-server-modtime): quien sube no la elige y no cuesta una consulta por objeto.
listar() {
  TZ=UTC "$RCLONE" lsf -R --files-only --use-server-modtime --format pt --separator ';' "$1"
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
  # Las comillas y las barras del usuario y la clave se escapan: una clave con `"` no debe romper ni inyectar el archivo de curl.
  printf 'user = "%s:%s"\n' "$(printf '%s' "$AUX_SMTP_USUARIO" | sed 's/[\\"]/\\&/g')" "$(printf '%s' "$AUX_SMTP_CLAVE" | sed 's/[\\"]/\\&/g')" >"$config"
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
