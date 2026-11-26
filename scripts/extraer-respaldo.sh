#!/usr/bin/env bash
# Extrae con seguridad el `tar` de un respaldo (el clúster de PostgreSQL) en una carpeta nueva (PLA-17, plan §6 «Restauración de un respaldo
# manipulado»). Quien controle la VM de datos puede subir un candidato válido (tiene la clave pública), así que el contenido del respaldo
# no se confía: se valida ANTES de extraer y se sanea DESPUÉS.
#
#   scripts/descifrar-respaldo.sh --entrada <respaldo>.tar.zst.age < clave | scripts/extraer-respaldo.sh --destino <carpeta-nueva>
#   scripts/extraer-respaldo.sh --destino <carpeta-nueva> --tar <archivo.tar o .tar.zst>
#
# Si lo recibido está comprimido con zstd (se reconoce por sus primeros bytes, D25), lo descomprime con la memoria limitada a 128 MiB y la
# salida limitada: como mucho `--tope-bytes` (por defecto 20 GiB) y nunca más de la mitad del espacio libre de la carpeta de destino (el tar
# descomprimido y lo extraído conviven un momento). Si se pasa, si zstd falla o si el archivo está cortado, se descarta todo.
# Rechaza el tar entero (y no extrae nada) si alguna entrada:
#   - tiene una ruta absoluta o un componente `..` (escaparía de la carpeta), o caracteres de control o un salto de línea en el nombre;
#   - es un enlace duro, un archivo especial (dispositivo, tubería, socket) o un enlace simbólico con destino absoluto o con `..`.
# Extrae sin propietarios (`--no-same-owner`: nunca vuelve a ser de root ni de otro usuario) y con permisos de la máscara 077. Después sanea:
#   - vacía `postgresql.auto.conf` (un `ALTER SYSTEM` del respaldo no se hereda: podría traer un `archive_command` o un `restore_command`);
#   - REEMPLAZA `postgresql.conf` por uno generado (sin archivado, sin recuperación, sin bibliotecas precargadas y sin archivos incluidos): una lista
#     negra de parámetros se esquiva (los nombres no distinguen mayúsculas y hay muchos que ejecutan código);
#   - borra `postmaster.pid`, `standby.signal`, `recovery.signal` y `recovery.conf` (no se arranca como réplica de nadie ni se recupera por la
#     configuración del respaldo: la recuperación la decide quien restaura);
#   - **regenera `pg_hba.conf` y `pg_ident.conf`**: solo el socket local del usuario `postgres` y TCP desde 127.0.0.1 con contraseña.
# Necesita GNU tar (el de las distros de Linux, WSL y Git Bash) y, para un respaldo comprimido, zstd. El tar se guarda un momento en un archivo temporal DENTRO de la carpeta de destino (la
# validación necesita leerlo dos veces) y se borra al terminar: si la carpeta es el volumen cifrado del ensayo, el claro no sale de él (nunca se escribe en la carpeta de arriba, que puede estar sin cifrar).
#
# Códigos: 0 extraído y saneado, 1 el tar se rechazó o la extracción falló (la carpeta queda vacía), 2 uso inválido.
set -uo pipefail

TAR="${TAR:-tar}"
ZSTD="${ZSTD:-zstd}"
DESTINO=""
ARCHIVO=""
TOPE=21474836480

uso() {
  echo "Uso: $0 --destino <carpeta-nueva> [--tar <archivo>] [--tope-bytes <n>]   (sin --tar, llega por la entrada estándar)" >&2
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --destino) [[ $# -ge 2 ]] || uso; DESTINO="$2"; shift 2 ;;
    --tar) [[ $# -ge 2 ]] || uso; ARCHIVO="$2"; shift 2 ;;
    --tope-bytes) [[ $# -ge 2 && "$2" =~ ^[1-9][0-9]{0,17}$ ]] || uso; TOPE="$2"; shift 2 ;;
    *) uso ;;
  esac
done
[[ -n "$DESTINO" ]] || uso
"$TAR" --version 2>/dev/null | head -n 1 | grep -q 'GNU tar' || { echo "Error: se necesita GNU tar" >&2; exit 2; }
if [[ -e "$DESTINO" ]] && [[ -n "$(ls -A "$DESTINO" 2>/dev/null)" ]]; then
  echo "Error: $DESTINO ya existe y no está vacía: se extrae solo en una carpeta nueva" >&2
  exit 2
fi
if [[ -z "$ARCHIVO" && -t 0 ]]; then
  echo "Error: el tar llega por la entrada estándar (entúbalo desde scripts/descifrar-respaldo.sh) o indica --tar" >&2
  exit 2
fi
[[ -z "$ARCHIVO" || -f "$ARCHIVO" ]] || { echo "Error: no existe $ARCHIVO" >&2; exit 2; }

umask 077
mkdir -p "$DESTINO"
DESTINO="$(cd "$DESTINO" && pwd)"
temporal=""
descomprimido=""
borrar_temporales() {
  [[ -n "$temporal" ]] && rm -f "$temporal"
  [[ -n "$descomprimido" ]] && rm -f "$descomprimido"
  return 0
}
trap borrar_temporales EXIT
rechazar() {
  echo "Respaldo rechazado: $*" >&2
  borrar_temporales
  rm -rf "${DESTINO:?}"/* "${DESTINO:?}"/.[!.]* 2>/dev/null
  exit 1
}
if [[ -z "$ARCHIVO" ]]; then
  temporal="$(mktemp "$DESTINO/.respaldo-XXXXXX")" || rechazar "no se pudo crear el archivo temporal"
  cat >"$temporal" || rechazar "no se pudo leer el tar de la entrada estándar"
  ARCHIVO="$temporal"
fi

# ---------- 0. Descomprimir con límites si viene comprimido ----------

if [[ "$(head -c 4 "$ARCHIVO" | od -An -tx1 | tr -d ' \n')" == 28b52ffd ]]; then
  command -v "$ZSTD" >/dev/null || rechazar "el respaldo está comprimido y falta zstd en este equipo"
  libres="$(df -Pk "$DESTINO" | awk 'NR == 2 { print $4 }')"
  [[ "$libres" =~ ^[0-9]+$ ]] || rechazar "no se pudo medir el espacio libre"
  tope="$TOPE"
  [[ $((libres * 512)) -lt $tope ]] && tope=$((libres * 512))
  descomprimido="$(mktemp "$DESTINO/.descomprimido-XXXXXX")" || rechazar "no se pudo crear el archivo temporal"
  "$ZSTD" -q -d -c --memory=128MB <"$ARCHIVO" 2>/dev/null | head -c "$((tope + 1))" >"$descomprimido"
  codigos=("${PIPESTATUS[@]}")
  [[ "$(wc -c <"$descomprimido")" -le $tope ]] || rechazar "descomprimido supera el tope de $tope bytes (--tope-bytes y la mitad del espacio libre)"
  [[ ${codigos[0]} -eq 0 && ${codigos[1]} -eq 0 ]] || rechazar "no se pudo descomprimir (archivo cortado, dañado o que exige demasiada memoria)"
  [[ -n "$temporal" ]] && rm -f "$temporal"
  ARCHIVO="$descomprimido"
fi

# ---------- 1. Validar antes de extraer ----------

# A tar solo le llega un tar sin comprimir (marca «ustar») y por la entrada estándar: al leer un archivo, GNU tar descomprime por su cuenta lo
# que reconoce (gzip, xz…) y se saltaría los topes de la descompresión; tampoco vale otro nivel de compresión dentro del zstd.
[[ "$(dd if="$ARCHIVO" bs=1 skip=257 count=5 2>/dev/null | tr -d '\0')" == ustar ]] || rechazar "no es un tar válido"

nombres="$("$TAR" -tf - <"$ARCHIVO" 2>/dev/null)" || rechazar "no es un tar válido"
detalle="$("$TAR" -tvf - <"$ARCHIVO" 2>/dev/null)" || rechazar "no es un tar válido"
[[ -n "$nombres" ]] || rechazar "el tar está vacío"
[[ "$(wc -l <<<"$nombres")" -eq "$(wc -l <<<"$detalle")" ]] || rechazar "un nombre con salto de línea desajusta el listado"

while IFS= read -r nombre; do
  if [[ "$nombre" == /* || "$nombre" == \\* || "$nombre" =~ ^[A-Za-z]:[/\\] ]]; then rechazar "ruta absoluta: $nombre"; fi
  if [[ "$nombre" == ".." || "$nombre" == ../* || "$nombre" == */.. || "$nombre" == */../* || "$nombre" == *\\..* ]]; then rechazar "ruta con «..»: $nombre"; fi
  if [[ "$nombre" =~ [[:cntrl:]] ]]; then rechazar "carácter de control en un nombre"; fi
done <<<"$nombres"

while IFS= read -r linea; do
  tipo="${linea:0:1}"
  case "$tipo" in
    - | d) ;;
    l)
      destino_enlace="${linea##* -> }"
      if [[ "$destino_enlace" == /* || "$destino_enlace" == ".." || "$destino_enlace" == ../* || "$destino_enlace" == */.. || "$destino_enlace" == */../* ]]; then
        rechazar "enlace simbólico con destino absoluto o con «..»"
      fi
      ;;
    h) rechazar "enlace duro" ;;
    *) rechazar "entrada especial de tipo «$tipo» (dispositivo, tubería o socket)" ;;
  esac
done <<<"$detalle"

# ---------- 2. Extraer sin propietarios ----------

if ! "$TAR" -xf - -C "$DESTINO" --no-same-owner --no-same-permissions --no-overwrite-dir <"$ARCHIVO" 2>/dev/null; then
  rechazar "la extracción falló"
fi
chmod 700 "$DESTINO"
find "$DESTINO" -type d -exec chmod u+rwx,go-rwx {} + 2>/dev/null
find "$DESTINO" -type f -exec chmod u+rw,go-rwx {} + 2>/dev/null

# ---------- 3. Sanear ----------

: >"$DESTINO/postgresql.auto.conf"
# `postgresql.conf` NO se sanea con una lista negra (los nombres de parámetros no distinguen mayúsculas y hay decenas que ejecutan código:
# `archive_command`, `restore_command`, `ssl_passphrase_command`, `shared_preload_libraries`, `dynamic_library_path`, `include`…): se REEMPLAZA por
# uno generado. Lo que importa (memoria, puertos, módulos) lo fija quien arranca el clúster con `-c` (restaurar.sh y el ensayo, T29 y T31).
cat >"$DESTINO/postgresql.conf" <<'EOF'
# Generado al restaurar (scripts/extraer-respaldo.sh): el postgresql.conf del respaldo se ignora por completo. Los parámetros los fija quien arranca
# el clúster con -c. Sin archivado, sin recuperación por configuración, sin bibliotecas precargadas y sin archivos incluidos.
listen_addresses = ''
archive_mode = off
EOF
rm -f "$DESTINO/postmaster.pid" "$DESTINO/standby.signal" "$DESTINO/recovery.signal" "$DESTINO/recovery.conf"
cat >"$DESTINO/pg_hba.conf" <<'EOF'
# Regenerado al restaurar (scripts/extraer-respaldo.sh): se ignora el del respaldo.
local   all   postgres                 peer
host    all   all       127.0.0.1/32   scram-sha-256
EOF
: >"$DESTINO/pg_ident.conf"
chmod 600 "$DESTINO/pg_hba.conf" "$DESTINO/pg_ident.conf" "$DESTINO/postgresql.auto.conf"

echo "Respaldo extraído en $DESTINO: $(wc -l <<<"$nombres" | tr -d ' ') entradas, saneado (sin postgresql.auto.conf, restore_command ni archive_command; pg_hba.conf regenerado)." >&2
