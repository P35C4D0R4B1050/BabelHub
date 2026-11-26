#!/usr/bin/env bash
# Descifra un respaldo (`…tar.zst.age`, `…-config-….tar.zst.age` o un segmento `…zst.age`; lo que sale sigue comprimido) SOLO con la copia fuera de línea de la clave privada (PLA-17,
# AC-PLA-17). Corre en el equipo de la persona responsable o en la máquina de ensayo, nunca en una VM de la plataforma. El descifrado sale por
# la salida estándar (para entubarlo a scripts/extraer-respaldo.sh); el claro no se escribe en disco aquí.
#
#   scripts/descifrar-respaldo.sh --entrada <archivo.age> [--custodia <custodia.md>] < clave-privada
#   scripts/descifrar-respaldo.sh --entrada <archivo.age> --probar [--registrar] < clave-privada     (descifra a la nada y, con --registrar,
#                                                                                         anota la fecha en la custodia)
#   scripts/descifrar-respaldo.sh --comprobar-clave [--custodia <custodia.md>] < clave-privada     (solo comprueba la clave, la custodia y que
#                                                                                         estén las herramientas)
#
# La clave privada entra por la **entrada estándar** y se guarda solo en memoria (nunca en un argumento, una variable exportada ni un archivo:
# age la lee de un descriptor temporal). Antes de tocar el respaldo comprueba que **la clave presentada corresponde al destinatario público
# registrado en infra/respaldos/custodia.md**: si no, se niega (exit 3), porque descifrar con otra clave solo funcionaría por casualidad y
# esconde un error de custodia. Se niega también a escribir el claro en una terminal. La negativa a correr con un agente o un proceso de desarrollo con acceso es de restaurar.sh y del ensayo (T29, T31).
#
# Códigos: 0 descifrado (o probado), 1 no se pudo descifrar, 2 uso inválido, 3 la clave no corresponde al destinatario registrado o la
# custodia no lo tiene registrado.
set -uo pipefail

AGE="${AGE:-age}"
AGE_KEYGEN="${AGE_KEYGEN:-age-keygen}"
RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CUSTODIA="$RAIZ/infra/respaldos/custodia.md"
ENTRADA=""
PROBAR=0
REGISTRAR=0
SOLO_CLAVE=0

uso() {
  echo "Uso: $0 --entrada <archivo.age> [--custodia <custodia.md>] [--probar [--registrar]] < clave-privada" >&2
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --entrada) [[ $# -ge 2 ]] || uso; ENTRADA="$2"; shift 2 ;;
    --custodia) [[ $# -ge 2 ]] || uso; CUSTODIA="$2"; shift 2 ;;
    --probar) PROBAR=1; shift ;;
    --registrar) REGISTRAR=1; shift ;;
    --comprobar-clave) SOLO_CLAVE=1; shift ;;
    *) uso ;;
  esac
done
[[ -n "$ENTRADA" || $SOLO_CLAVE -eq 1 ]] || uso
[[ $REGISTRAR -eq 0 || $PROBAR -eq 1 ]] || { echo "Error: --registrar solo se usa con --probar" >&2; exit 2; }
[[ $SOLO_CLAVE -eq 1 || -f "$ENTRADA" ]] || { echo "Error: no existe $ENTRADA" >&2; exit 2; }
[[ -f "$CUSTODIA" ]] || { echo "Error: no existe $CUSTODIA" >&2; exit 2; }
if [[ $PROBAR -eq 0 && $SOLO_CLAVE -eq 0 && -t 1 ]]; then
  echo "Error: el descifrado es binario y saldría a la terminal: entúbalo (a scripts/extraer-respaldo.sh) o redirígelo" >&2
  exit 2
fi
if [[ -t 0 ]]; then
  echo "Error: la clave privada entra por la entrada estándar (por ejemplo, desde el dispositivo de la copia fuera de línea), no se teclea aquí" >&2
  exit 2
fi

# La clave solo vive en esta variable (no exportada) mientras corre el script.
identidad="$(cat)"
if [[ ! "$identidad" =~ AGE-SECRET-KEY-[0-9A-Z]{10,} ]]; then
  echo "Error: lo que llegó por la entrada estándar no es una clave privada de age" >&2
  exit 1
fi

for herramienta in "$AGE" "$AGE_KEYGEN"; do
  command -v "$herramienta" >/dev/null || { echo "Error: falta $herramienta en este equipo" >&2; exit 2; }
done

# El destinatario público de esa clave y el que registra la custodia.
if ! presentada="$(printf '%s\n' "$identidad" | "$AGE_KEYGEN" -y 2>/dev/null)" || [[ ! "$presentada" =~ ^age1[0-9a-z]{58}$ ]]; then
  echo "Error: no se pudo derivar el destinatario público de la clave presentada" >&2
  exit 1
fi
registrado="$(awk -F'|' '{ n = $2; gsub(/^[ \t]+|[ \t]+$/, "", n) } index(n, "Destinatario público (age)") == 1 { v = $3; gsub(/^[ \t]+|[ \t]+$/, "", v); print v; exit }' "$CUSTODIA")"
if [[ -z "$registrado" || "${registrado,,}" == pendiente* ]]; then
  echo "Error: la custodia no registra todavía el destinatario público: no se puede comprobar que esta clave es la de los respaldos" >&2
  exit 3
fi
if [[ "$presentada" != "$registrado" ]]; then
  echo "Error: la clave presentada NO corresponde al destinatario registrado en la custodia: no se descifra" >&2
  exit 3
fi

[[ $SOLO_CLAVE -eq 0 ]] || exit 0

if [[ $PROBAR -eq 1 ]]; then
  if ! "$AGE" -d -i <(printf '%s\n' "$identidad") -o /dev/null "$ENTRADA" 2>/dev/null; then
    echo "Error: no se pudo descifrar $ENTRADA con la clave presentada" >&2
    exit 1
  fi
  echo "Descifrado probado: $(basename "$ENTRADA") se descifra con la clave presentada." >&2
  if [[ $REGISTRAR -eq 1 ]]; then
    hoy="$(date -u +%Y-%m-%d)"
    temporal="$(mktemp)"
    trap 'rm -f "$temporal"' EXIT
    awk -F'|' -v hoy="$hoy" '
      { n = $2; gsub(/^[ \t]+|[ \t]+$/, "", n) }
      index(n, "Última prueba de descifrado") == 1 { print "| " n " | " hoy " |"; next }
      { print }' "$CUSTODIA" >"$temporal" && cat "$temporal" >"$CUSTODIA"
    echo "Fecha de la última prueba registrada en la custodia: $hoy (revisa y commitea el cambio)." >&2
  fi
  exit 0
fi

if ! "$AGE" -d -i <(printf '%s\n' "$identidad") "$ENTRADA"; then
  echo "Error: no se pudo descifrar $ENTRADA con la clave presentada" >&2
  exit 1
fi
