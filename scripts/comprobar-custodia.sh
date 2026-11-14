#!/usr/bin/env bash
# Comprueba el registro de custodia de la clave que descifra los respaldos (PLA-17, AC-PLA-17; infra/respaldos/custodia.md). Corre en el
# equipo de la persona responsable (y puede correr en el flujo de lanzamiento: solo lee datos públicos).
#
#   scripts/comprobar-custodia.sh [--custodia <archivo>] [--entorno <postgres.env> | --destinatario <age1…>] [--hoy AAAA-MM-DD]
#
# Qué exige (una línea `ok …` o `ALERTA …` por comprobación; código 0 si todo cumple, 1 si alguna falla, 2 si el uso es inválido):
#   - ningún campo del registro con «pendiente»: titular, destinatario público, ubicación de la copia fuera de línea y de la SEGUNDA copia,
#     fecha de la última prueba de descifrado y periodicidad;
#   - el destinatario registrado tiene la forma de una clave pública de age, y **el archivo de custodia no contiene ninguna clave privada**;
#   - la segunda copia está en un lugar distinto de la primera (una sola copia es perder todo si se pierde);
#   - la última prueba de descifrado es una fecha válida, no futura, y no tiene más de «Periodicidad» días (90 por defecto);
#   - con --entorno o --destinatario: el destinatario con el que la VM de datos CIFRA coincide con el registrado. Si no coincide, los respaldos
#     se cifrarían con una clave que la custodia no tiene y nadie podría restaurarlos.
# Nunca imprime valores de claves: solo si coinciden.
set -uo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CUSTODIA="$RAIZ/infra/respaldos/custodia.md"
ENTORNO=""
DESTINATARIO_VM=""
HOY="$(date -u +%Y-%m-%d)"

uso() {
  echo "Uso: $0 [--custodia <archivo>] [--entorno <postgres.env> | --destinatario <age1…>] [--hoy AAAA-MM-DD]" >&2
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --custodia) [[ $# -ge 2 ]] || uso; CUSTODIA="$2"; shift 2 ;;
    --entorno) [[ $# -ge 2 ]] || uso; ENTORNO="$2"; shift 2 ;;
    --destinatario) [[ $# -ge 2 ]] || uso; DESTINATARIO_VM="$2"; shift 2 ;;
    --hoy) [[ $# -ge 2 && "$2" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]] || uso; HOY="$2"; shift 2 ;;
    *) uso ;;
  esac
done
[[ -z "$ENTORNO" || -z "$DESTINATARIO_VM" ]] || { echo "Error: usa --entorno o --destinatario, no los dos" >&2; exit 2; }
[[ -f "$CUSTODIA" ]] || { echo "Error: no existe $CUSTODIA" >&2; exit 2; }

fallos=0
ok() { echo "ok $*"; }
alerta() { echo "ALERTA $*"; fallos=$((fallos + 1)); }

# El valor de una fila de la tabla del registro: «| Campo | Valor |».
campo() {
  awk -F'|' -v c="$1" '
    { n = $2; gsub(/^[ \t]+|[ \t]+$/, "", n) }
    index(n, c) == 1 { v = $3; gsub(/^[ \t]+|[ \t]+$/, "", v); print v; exit }' "$CUSTODIA"
}

TITULAR="$(campo 'Titular de la clave')"
DESTINATARIO="$(campo 'Destinatario público (age)')"
COPIA_1="$(campo 'Ubicación de la copia fuera de línea')"
COPIA_2="$(campo 'Ubicación de la segunda copia')"
PRUEBA="$(campo 'Última prueba de descifrado')"
PERIODO="$(campo 'Periodicidad de la prueba')"

# Ninguna clave privada en el archivo (ni ninguna otra clave con la forma de una).
if grep -Eq 'AGE-SECRET-KEY-[0-9A-Z]{10,}' "$CUSTODIA"; then
  alerta "el archivo de custodia contiene una clave privada de age: bórrala y considérala comprometida"
else
  ok "el archivo de custodia no contiene claves privadas"
fi

pendientes=""
for par in "titular:$TITULAR" "destinatario:$DESTINATARIO" "copia-fuera-de-linea:$COPIA_1" "segunda-copia:$COPIA_2" "ultima-prueba:$PRUEBA" "periodicidad:$PERIODO"; do
  valor="${par#*:}"
  if [[ -z "$valor" || "${valor,,}" == pendiente* ]]; then pendientes+=" ${par%%:*}"; fi
done
if [[ -n "$pendientes" ]]; then
  alerta "faltan datos en el registro de custodia (pendiente):$pendientes"
else
  ok "el registro de custodia está completo"
fi

if [[ -n "$DESTINATARIO" && "${DESTINATARIO,,}" != pendiente* ]]; then
  if [[ "$DESTINATARIO" =~ ^age1[0-9a-z]{58}$ ]]; then ok "el destinatario registrado tiene la forma de una clave pública de age"; else alerta "el destinatario registrado no tiene la forma de una clave pública de age (age1… de 62 caracteres)"; fi
fi

if [[ -n "$COPIA_1" && -n "$COPIA_2" && "${COPIA_1,,}" != pendiente* && "${COPIA_2,,}" != pendiente* ]]; then
  if [[ "${COPIA_1,,}" == "${COPIA_2,,}" ]]; then alerta "la segunda copia está en el mismo lugar que la primera: debe estar en otro"; else ok "hay una segunda copia en un lugar distinto"; fi
fi

if [[ -n "$PRUEBA" && "${PRUEBA,,}" != pendiente* ]]; then
  if [[ ! "$PRUEBA" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]] || ! fecha_prueba="$(date -u -d "$PRUEBA" +%s 2>/dev/null)"; then
    alerta "la fecha de la última prueba de descifrado no es una fecha válida (AAAA-MM-DD)"
  else
    hoy_epoca="$(date -u -d "$HOY" +%s)"
    maximo="${PERIODO:-90}"
    [[ "$maximo" =~ ^[0-9]+$ && "$maximo" -ge 1 && "$maximo" -le 365 ]] || maximo=90
    if [[ "$fecha_prueba" -gt "$hoy_epoca" ]]; then
      alerta "la última prueba de descifrado es una fecha futura"
    elif [[ $(((hoy_epoca - fecha_prueba) / 86400)) -gt "$maximo" ]]; then
      alerta "la última prueba de descifrado ($PRUEBA) tiene más de $maximo días: hay que repetirla (scripts/descifrar-respaldo.sh --probar --registrar)"
    else
      ok "la última prueba de descifrado es reciente ($PRUEBA, cada $maximo días como máximo)"
    fi
  fi
fi

# El destinatario con el que la VM de datos cifra.
if [[ -n "$ENTORNO" ]]; then
  [[ -f "$ENTORNO" ]] || { echo "Error: no existe $ENTORNO" >&2; exit 2; }
  DESTINATARIO_VM="$(sed -n 's/^[[:space:]]*\(export \)\{0,1\}BABELHUB_AGE_DESTINATARIO=//p' "$ENTORNO" | tail -n 1 | tr -d '\r"\047')"
  [[ -n "$DESTINATARIO_VM" ]] || alerta "$ENTORNO no define BABELHUB_AGE_DESTINATARIO: la VM de datos no tendría con qué cifrar"
fi
if [[ -n "$DESTINATARIO_VM" ]]; then
  if [[ "$DESTINATARIO_VM" != age1* ]]; then
    alerta "el destinatario de la VM de datos no es una clave pública de age (age1…): ¿es una clave privada? No se imprime"
  elif [[ -z "$DESTINATARIO" || "${DESTINATARIO,,}" == pendiente* ]]; then
    alerta "no se puede comparar: la custodia no registra todavía el destinatario público"
  elif [[ "$DESTINATARIO_VM" == "$DESTINATARIO" ]]; then
    ok "el destinatario con el que la VM de datos cifra coincide con el registrado en la custodia"
  else
    alerta "el destinatario con el que la VM de datos cifra NO coincide con el registrado en la custodia: los respaldos no se podrían descifrar con la copia registrada"
  fi
fi

if [[ $fallos -gt 0 ]]; then
  echo "Custodia de la clave: $fallos problema(s)."
  exit 1
fi
echo "Custodia de la clave: en regla."
