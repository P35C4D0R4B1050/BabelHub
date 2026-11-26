#!/usr/bin/env bash
# Prepara el archivo de cambios para una restauración (PLA-10, PLA-17; D25, D28): descifra y descomprime. Corre en el equipo de la persona
# responsable o en la máquina de ensayo, nunca con la clave en una VM. Lee una carpeta con los objetos descargados del almacén
# («<nombre>-<sha256 del cifrado>.zst.age») y deja en una carpeta NUEVA los segmentos y la historia con su nombre de PostgreSQL, para que
# `restore_command` los lea. Esa carpeta lleva datos en claro: va en almacenamiento protegido (el volumen cifrado del ensayo o el disco
# cifrado de la VM destino, adonde la lleva quien restaura) y se borra al terminar la restauración.
#
#   scripts/descifrar-wal.sh --entrada <carpeta> --destino <carpeta-nueva> --desde <segmento> [--hasta <segmento>] [--restaurada <carpeta>] [--custodia <custodia.md>] < clave-privada
#   scripts/descifrar-wal.sh --entrada <carpeta> --destino <carpeta-nueva> --desde <segmento> --sin-cambios
#
# `--desde` es el primer segmento que pide la copia elegida (`START WAL LOCATION … (file …)` de su backup_label) y fija la LÍNEA DE TIEMPO: solo
# se preparan los segmentos de esa línea, seguidos y sin huecos, y la recuperación se configura con `recovery_target_timeline = 'current'`.
# `--restaurada` es la carpeta extraída de esa copia: una línea distinta de la 1 necesita su archivo de historia, que se archiva una sola vez
# (al restaurar) y el almacén retira como todo lo demás; la copia lo trae en `pg_wal` y esa es la que se usa. Si además se descarga uno y
# difiere, se detiene.
#
# Con la clave pública cualquiera puede subir un candidato, así que no se elige entre candidatos (D28): se descartan los que no coinciden con la
# suma de su nombre, no se descifran, no se descomprimen o no miden lo que deben (16 MiB por segmento). La preparación SE DETIENE antes de un
# segmento (código 4) si falta, si no queda ningún candidato válido o si dos candidatos difieren: el archivo se trata como incompleto desde ahí.
# De las OTRAS líneas de tiempo no se lee nada: por cada historia ajena se deja un archivo VACÍO con su nombre, para que el clúster recuperado
# no reutilice ese número; si alguien olvidara fijar `recovery_target_timeline`, PostgreSQL no arranca en vez de seguir una línea ajena.
# `--sin-cambios` deja solo esos archivos vacíos (no pide clave): es la restauración sin archivo de cambios, que también cambia de línea.
#
# Códigos: 0 listo, 1 falló, 2 uso inválido, 3 la clave no corresponde a la custodia, 4 detenido antes de un segmento (lo anterior queda listo).
set -uo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ZSTD="${ZSTD:-zstd}"
TAMANO_DE_SEGMENTO="${BABELHUB_TAMANO_DE_SEGMENTO:-16777216}"
TOPE_DE_HISTORIA=1048576
ENTRADA="" DESTINO="" DESDE="" HASTA="" CUSTODIA="" RESTAURADA="" SIN_CAMBIOS=0

uso() {
  echo "Uso: $0 --entrada <carpeta> --destino <carpeta-nueva> --desde <segmento> [--hasta <segmento>] [--restaurada <carpeta>] [--custodia <custodia.md>] < clave-privada" >&2
  echo "     $0 --entrada <carpeta> --destino <carpeta-nueva> --desde <segmento> --sin-cambios" >&2
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --entrada) [[ $# -ge 2 ]] || uso; ENTRADA="$2"; shift 2 ;;
    --destino) [[ $# -ge 2 ]] || uso; DESTINO="$2"; shift 2 ;;
    --desde) [[ $# -ge 2 && "$2" =~ ^[0-9A-F]{24}$ ]] || uso; DESDE="$2"; shift 2 ;;
    --hasta) [[ $# -ge 2 && "$2" =~ ^[0-9A-F]{24}$ ]] || uso; HASTA="$2"; shift 2 ;;
    --custodia) [[ $# -ge 2 ]] || uso; CUSTODIA="$2"; shift 2 ;;
    --restaurada) [[ $# -ge 2 ]] || uso; RESTAURADA="$2"; shift 2 ;;
    --sin-cambios) SIN_CAMBIOS=1; shift ;;
    *) uso ;;
  esac
done
[[ -n "$ENTRADA" && -n "$DESTINO" && -n "$DESDE" ]] || uso
LINEA="${DESDE:0:8}"
if [[ -n "$HASTA" && ( "${HASTA:0:8}" != "$LINEA" || "$HASTA" < "$DESDE" ) ]]; then
  echo "Error: --hasta debe ser de la misma línea de tiempo que --desde y no anterior" >&2
  exit 2
fi
[[ -d "$ENTRADA" ]] || { echo "Error: no existe la carpeta $ENTRADA" >&2; exit 2; }
[[ -z "$RESTAURADA" || -d "$RESTAURADA/pg_wal" ]] || { echo "Error: $RESTAURADA no es una copia extraída (no tiene pg_wal)" >&2; exit 2; }
if [[ -e "$DESTINO" ]] && [[ -n "$(ls -A "$DESTINO" 2>/dev/null)" ]]; then
  echo "Error: $DESTINO ya existe y no está vacía: se escribe solo en una carpeta nueva" >&2
  exit 2
fi
identidad=""
if [[ $SIN_CAMBIOS -eq 0 ]]; then
  if [[ -t 0 ]]; then
    echo "Error: la clave privada entra por la entrada estándar (desde la copia fuera de línea), no se teclea aquí" >&2
    exit 2
  fi
  command -v "$ZSTD" >/dev/null || { echo "Error: falta zstd" >&2; exit 2; }
  # La clave solo vive en esta variable (no exportada) mientras corre el script; cada objeto la recibe por la entrada estándar.
  identidad="$(cat)"
  # La clave, la custodia y las herramientas se comprueban una vez, ANTES de leer ningún objeto: un problema de este equipo no debe
  # confundirse con un archivo incompleto.
  printf '%s\n' "$identidad" | bash "$RAIZ/scripts/descifrar-respaldo.sh" --comprobar-clave ${CUSTODIA:+--custodia "$CUSTODIA"} || exit $?
fi

umask 077
mkdir -p "$DESTINO" && chmod 700 "$DESTINO" || { echo "Error: no se pudo crear $DESTINO" >&2; exit 1; }
parcial="$DESTINO/.parcial"
trap 'rm -f "$parcial" "$parcial.otro"' EXIT

patron='^([0-9A-F]{24}|[0-9A-F]{8}\.history)-([0-9a-f]{64})\.zst\.age$'
declare -A CANDIDATOS=() AJENAS=()
historia_propia=""
for ruta in "$ENTRADA"/*; do
  [[ -f "$ruta" ]] || continue
  archivo="$(basename "$ruta")"
  # Los «.backup» y los «.partial» no hacen falta para recuperar.
  if [[ ! "$archivo" =~ $patron ]]; then
    echo "aviso: se ignora $archivo" >&2
    continue
  fi
  nombre="${BASH_REMATCH[1]}"
  if [[ "$nombre" == *.history ]]; then
    if [[ "${nombre%.history}" != "$LINEA" ]]; then AJENAS[$nombre]=1; continue; fi
    historia_propia="$nombre"
  else
    [[ "${nombre:0:8}" == "$LINEA" && ! "$nombre" < "$DESDE" ]] || continue
    [[ -z "$HASTA" || ! "$nombre" > "$HASTA" ]] || continue
  fi
  CANDIDATOS[$nombre]+="$archivo "
done

for nombre in "${!AJENAS[@]}"; do
  : >"$DESTINO/$nombre" || { echo "Error: no se pudo escribir en $DESTINO" >&2; exit 1; }
  echo "aviso: hay una historia de otra línea de tiempo ($nombre): no se lee; queda vacía para que no se reutilice su número" >&2
done

if [[ $SIN_CAMBIOS -eq 1 ]]; then
  echo "Sin archivo de cambios: $DESTINO solo reserva los números de línea de tiempo ya usados (${#AJENAS[@]})." >&2
  exit 0
fi

detener() {
  echo "DETENIDO antes de $1: $2. El archivo de cambios se trata como incompleto desde aquí (pérdida máxima, la del respaldo diario). Lo ya preparado en $DESTINO está en claro: bórralo al terminar." >&2
  exit 4
}

# abrir <objeto> <salida> <tope>: descifra y descomprime con tope; 0 solo si todo fue bien y no se pasa.
abrir() {
  local objeto="$1" salida="$2" tope="$3" argumentos=(--entrada "$ENTRADA/$1") codigos medida
  [[ -n "$CUSTODIA" ]] && argumentos+=(--custodia "$CUSTODIA")
  [[ "$(sha256sum "$ENTRADA/$objeto" | cut -d' ' -f1)" == "${objeto: -72:64}" ]] || { echo "aviso: $objeto no coincide con la suma de su nombre" >&2; return 1; }
  printf '%s\n' "$identidad" | bash "$RAIZ/scripts/descifrar-respaldo.sh" "${argumentos[@]}" | "$ZSTD" -q -d -c --memory=128MB | head -c "$((tope + 1))" >"$salida"
  codigos=("${PIPESTATUS[@]}")
  [[ ${codigos[1]} -eq 2 || ${codigos[1]} -eq 3 ]] && exit "${codigos[1]}"
  [[ ${codigos[3]} -eq 0 ]] || { echo "Error: no se pudo escribir en $DESTINO (¿disco lleno?)" >&2; exit 1; }
  medida="$(wc -c <"$salida")"
  if [[ ${codigos[1]} -ne 0 || ${codigos[2]} -ne 0 || $medida -gt $tope ]]; then
    echo "aviso: $objeto no se pudo descifrar y descomprimir entero" >&2
    return 1
  fi
}

# preparar <nombre>: deja en el destino el único contenido válido de ese nombre, o detiene.
preparar() {
  local nombre="$1" tope="$TAMANO_DE_SEGMENTO" elegido=0 objeto salida
  [[ ${#nombre} -eq 24 ]] || tope="$TOPE_DE_HISTORIA"
  for objeto in ${CANDIDATOS[$nombre]:-}; do
    salida="$parcial"; [[ $elegido -eq 1 ]] && salida="$parcial.otro"
    abrir "$objeto" "$salida" "$tope" || continue
    if [[ ${#nombre} -eq 24 && "$(wc -c <"$salida")" -ne $TAMANO_DE_SEGMENTO ]]; then
      echo "aviso: $objeto no mide un segmento entero" >&2
      continue
    fi
    if [[ $elegido -eq 1 ]]; then
      cmp -s "$parcial" "$parcial.otro" || detener "$nombre" "hay candidatos con contenido distinto y no se elige ninguno"
    else
      elegido=1
    fi
  done
  if [[ $elegido -eq 0 ]]; then
    if [[ -n "${CANDIDATOS[$nombre]:-}" ]]; then detener "$nombre" "no hay ningún candidato válido"; else detener "$nombre" "falta en el archivo"; fi
  fi
  mv -f "$parcial" "$DESTINO/$nombre" || { echo "Error: no se pudo escribir en $DESTINO" >&2; exit 1; }
  rm -f "$parcial.otro"
}

# El siguiente segmento de la misma línea (256 segmentos de 16 MiB por archivo lógico).
siguiente() {
  local logico=$((16#${1:8:8})) segmento=$((16#${1:16:8}))
  if [[ $segmento -ge 255 ]]; then logico=$((logico + 1)); segmento=0; else segmento=$((segmento + 1)); fi
  printf '%s%08X%08X' "${1:0:8}" "$logico" "$segmento"
}

segmentos=0
ultimo=""
for nombre in "${!CANDIDATOS[@]}"; do
  [[ ${#nombre} -eq 24 ]] || continue
  segmentos=$((segmentos + 1))
  [[ -z "$ultimo" || "$nombre" > "$ultimo" ]] && ultimo="$nombre"
done
[[ -n "$HASTA" ]] && ultimo="$HASTA"
[[ $segmentos -gt 0 ]] || detener "$DESDE" "no hay ningún segmento de la línea $LINEA desde ese punto"

# Debe caber todo lo que se va a preparar y dos copias más de trabajo.
libres="$(df -Pk "$DESTINO" | awk 'NR == 2 { print $4 }')"
necesarios=$(((segmentos + 2) * (TAMANO_DE_SEGMENTO / 1024 + 1)))
if [[ ! "$libres" =~ ^[0-9]+$ || $libres -lt $necesarios ]]; then
  echo "Error: no hay espacio en $DESTINO: se necesitan unos $((necesarios / 1024 + 1)) MiB (usa --hasta para preparar menos)" >&2
  exit 1
fi

# La línea 1 no tiene historia. Las demás necesitan la suya: manda la que trae la copia; sin ella, la descargada.
if [[ "$LINEA" != 00000001 ]]; then
  propia="$LINEA.history"
  de_la_copia=""
  [[ -n "$RESTAURADA" && -f "$RESTAURADA/pg_wal/$propia" ]] && de_la_copia="$RESTAURADA/pg_wal/$propia"
  if [[ -n "$de_la_copia" ]]; then
    for objeto in ${CANDIDATOS[$propia]:-}; do
      abrir "$objeto" "$parcial" "$TOPE_DE_HISTORIA" || continue
      cmp -s "$parcial" "$de_la_copia" || detener "$propia" "la historia descargada difiere de la que trae la copia"
    done
    rm -f "$parcial"
    cp "$de_la_copia" "$DESTINO/$propia" || { echo "Error: no se pudo escribir en $DESTINO" >&2; exit 1; }
  else
    [[ -n "$historia_propia" ]] || detener "$propia" "falta la historia de la línea de tiempo (indica con --restaurada la copia extraída, que la trae)"
    preparar "$historia_propia"
  fi
fi

actual="$DESDE"
listos=0
while :; do
  preparar "$actual"
  listos=$((listos + 1))
  [[ "$actual" == "$ultimo" ]] && break
  actual="$(siguiente "$actual")"
done
echo "Archivo de cambios listo en $DESTINO: $listos segmentos seguidos de $DESDE a $ultimo (línea $LINEA; recupera con recovery_target_timeline = 'current'). Bórralo al terminar: está en claro." >&2
