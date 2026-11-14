#!/usr/bin/env bash
# Extrae con seguridad el `tar` de un respaldo (el clúster de PostgreSQL) en una carpeta nueva (PLA-17, plan §6 «Restauración de un respaldo
# manipulado»). Quien controle la VM de datos puede subir un candidato válido (tiene la clave pública), así que el contenido del respaldo
# no se confía: se valida ANTES de extraer y se sanea DESPUÉS.
#
#   scripts/descifrar-respaldo.sh --entrada <respaldo>.tar.age < clave | scripts/extraer-respaldo.sh --destino <carpeta-nueva>
#   scripts/extraer-respaldo.sh --destino <carpeta-nueva> --tar <archivo.tar>
#
# Rechaza el tar entero (y no extrae nada) si alguna entrada:
#   - tiene una ruta absoluta o un componente `..` (escaparía de la carpeta), o caracteres de control o un salto de línea en el nombre;
#   - es un enlace duro, un archivo especial (dispositivo, tubería, socket) o un enlace simbólico con destino absoluto o con `..`.
# Extrae sin propietarios (`--no-same-owner`: nunca vuelve a ser de root ni de otro usuario) y con permisos de la máscara 077. Después sanea:
#   - vacía `postgresql.auto.conf` (un `ALTER SYSTEM` del respaldo no se hereda: podría traer un `archive_command` o un `restore_command`);
#   - comenta en `postgresql.conf` `archive_command`, `archive_mode`, `restore_command`, `archive_cleanup_command`, `recovery_end_command`,
#     `primary_conninfo` y las líneas `include*` (un archivo ajeno no se incluye);
#   - borra `postmaster.pid`, `standby.signal`, `recovery.signal` y `recovery.conf` (no se arranca como réplica de nadie ni se recupera por la
#     configuración del respaldo: la recuperación la decide quien restaura);
#   - **regenera `pg_hba.conf` y `pg_ident.conf`**: solo el socket local del usuario `postgres` y TCP desde 127.0.0.1 con contraseña.
# Necesita GNU tar (el de las distros de Linux, WSL y Git Bash). El tar se guarda un momento en un archivo temporal de la misma carpeta (la
# validación necesita leerlo dos veces) y se borra al terminar: si la carpeta es el volumen cifrado del ensayo, el claro no sale de él.
#
# Códigos: 0 extraído y saneado, 1 el tar se rechazó o la extracción falló (la carpeta queda vacía), 2 uso inválido.
set -uo pipefail

TAR="${TAR:-tar}"
DESTINO=""
ARCHIVO=""

uso() {
  echo "Uso: $0 --destino <carpeta-nueva> [--tar <archivo.tar>]   (sin --tar, el tar llega por la entrada estándar)" >&2
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --destino) [[ $# -ge 2 ]] || uso; DESTINO="$2"; shift 2 ;;
    --tar) [[ $# -ge 2 ]] || uso; ARCHIVO="$2"; shift 2 ;;
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
rechazar() {
  echo "Respaldo rechazado: $*" >&2
  [[ -n "$temporal" ]] && rm -f "$temporal"
  rm -rf "${DESTINO:?}"/* "${DESTINO:?}"/.[!.]* 2>/dev/null
  exit 1
}
if [[ -z "$ARCHIVO" ]]; then
  temporal="$(mktemp "$DESTINO/../.respaldo-XXXXXX")" || rechazar "no se pudo crear el archivo temporal"
  trap '[[ -n "$temporal" ]] && rm -f "$temporal"' EXIT
  cat >"$temporal" || rechazar "no se pudo leer el tar de la entrada estándar"
  ARCHIVO="$temporal"
fi

# ---------- 1. Validar antes de extraer ----------

nombres="$("$TAR" -tf "$ARCHIVO" 2>/dev/null)" || rechazar "no es un tar válido"
detalle="$("$TAR" -tvf "$ARCHIVO" 2>/dev/null)" || rechazar "no es un tar válido"
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

if ! "$TAR" -xf "$ARCHIVO" -C "$DESTINO" --no-same-owner --no-same-permissions --no-overwrite-dir 2>/dev/null; then
  rechazar "la extracción falló"
fi
chmod 700 "$DESTINO"
find "$DESTINO" -type d -exec chmod u+rwx,go-rwx {} + 2>/dev/null
find "$DESTINO" -type f -exec chmod u+rw,go-rwx {} + 2>/dev/null

# ---------- 3. Sanear ----------

: >"$DESTINO/postgresql.auto.conf"
if [[ -f "$DESTINO/postgresql.conf" ]]; then
  sed -E -i 's/^[[:space:]]*(archive_command|archive_mode|restore_command|archive_cleanup_command|recovery_end_command|primary_conninfo|include|include_dir|include_if_exists)([[:space:]=].*)?$/# (ignorado al restaurar) &/' "$DESTINO/postgresql.conf"
fi
rm -f "$DESTINO/postmaster.pid" "$DESTINO/standby.signal" "$DESTINO/recovery.signal" "$DESTINO/recovery.conf"
cat >"$DESTINO/pg_hba.conf" <<'EOF'
# Regenerado al restaurar (scripts/extraer-respaldo.sh): se ignora el del respaldo.
local   all   postgres                 peer
host    all   all       127.0.0.1/32   scram-sha-256
EOF
: >"$DESTINO/pg_ident.conf"
chmod 600 "$DESTINO/pg_hba.conf" "$DESTINO/pg_ident.conf" "$DESTINO/postgresql.auto.conf"

echo "Respaldo extraído en $DESTINO: $(wc -l <<<"$nombres" | tr -d ' ') entradas, saneado (sin postgresql.auto.conf, restore_command ni archive_command; pg_hba.conf regenerado)." >&2
