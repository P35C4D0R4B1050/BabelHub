#!/usr/bin/env bash
# Comprueba desde fuera de la VM de datos que los respaldos están al día (PLA-20, AC-PLA-20): si la VM cae o el respaldo falla, nadie en
# esa VM puede avisarlo. Usa `respaldos-vigilante` (remoto `vigilante`, solo lectura en Cloudflare R2) y, para la copia del registro,
# `registro-vigilante` (remoto `registro`, solo lista en Oracle). Corre cada 5 minutos desde cron,
# envuelto por alertar.sh, que manda el correo a la persona responsable.
#
#   scripts/vm-aux/comprobar-respaldos.sh
#
# Una línea «ALERTA …» por cada problema y código de salida 1; con todo al día, líneas «ok …» y código 0:
#   - no hay un respaldo del clúster completado en las últimas 24 horas (AUX_MAX_HORAS_RESPALDO), o su configuración tampoco;
#   - el archivado de cambios tiene más de 15 minutos de atraso (AUX_MAX_MINUTOS_ATRASO): el último segmento archivado es más viejo que eso.
#     Con el latido de la base y archive_timeout=300 el atraso normal es de unos 5 minutos aunque nadie use el sistema;
#   - la copia diaria del registro de supresiones tiene más de 36 horas (AUX_MAX_HORAS_COPIA_REGISTRO);
#   - algún contenedor no se puede consultar (no se da por bueno).
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/comun.sh"

MAX_HORAS="${AUX_MAX_HORAS_RESPALDO:-24}"
MAX_MINUTOS="${AUX_MAX_MINUTOS_ATRASO:-15}"
MAX_HORAS_COPIA="${AUX_MAX_HORAS_COPIA_REGISTRO:-36}"
ahora="$(date -u +%s)"
alertas=0

alerta() {
  echo "ALERTA $*"
  alertas=$((alertas + 1))
}

# La hora más reciente de los objetos de un «remoto:contenedor» cuya clave cumple un patrón (vacío: todos). Falla si no se puede consultar.
mas_reciente() {
  local listado
  listado="$(listar "$1")" || return 1
  awk -F';' -v filtro="$2" 'NF >= 2 && (filtro == "" || $1 ~ filtro) && $2 > maximo { maximo = $2 } END { print maximo }' <<<"$listado"
}

segundos_desde() {
  local epoca
  epoca="$(date -u -d "$1" +%s 2>/dev/null)" || return 1
  echo $((ahora - epoca))
}

comprobar() { # etiqueta remoto:contenedor patrón máximo-en-segundos descripción-del-máximo texto-sin-objetos
  local hora segundos
  if ! hora="$(mas_reciente "$2" "$3")"; then
    alerta "$1: no se pudo consultar el contenedor ${2#*:}"
    return
  fi
  if [[ -z "$hora" ]]; then
    alerta "$1: $6"
    return
  fi
  segundos="$(segundos_desde "$hora")" || { alerta "$1: la hora «$hora» no se entiende"; return; }
  if [[ $segundos -gt $4 ]]; then
    alerta "$1: lo último es de hace $((segundos / 3600)) h $(((segundos % 3600) / 60)) min y el máximo es $5"
  else
    echo "ok $1 (hace $((segundos / 60)) min)"
  fi
}

comprobar "respaldo del clúster" vigilante:respaldos-diarios '^[0-9-]+/[0-9]+-[0-9a-f]+\.tar\.zst\.age$' $((MAX_HORAS * 3600)) "$MAX_HORAS h" "no hay ningún respaldo"
comprobar "configuración del respaldo" vigilante:respaldos-diarios '-config-[0-9a-f]+\.tar\.zst\.age$' $((MAX_HORAS * 3600)) "$MAX_HORAS h" "no hay ninguna configuración respaldada"
comprobar "archivado de cambios" vigilante:respaldos-wal '\.zst\.age$' $((MAX_MINUTOS * 60)) "$MAX_MINUTOS min de atraso" "no hay ningún segmento archivado"
comprobar "copia del registro de supresiones" registro:supresiones-copia '' $((MAX_HORAS_COPIA * 3600)) "$MAX_HORAS_COPIA h" "no hay ninguna copia del registro"

[[ $alertas -eq 0 ]]
