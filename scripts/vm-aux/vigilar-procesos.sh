#!/usr/bin/env bash
# Avisa de procesos que no deberían correr en VM-AUX (PLA-20, AM29, D19): la VM hace pocas cosas (cron, sshd, rclone, curl y los scripts), así
# que un proceso desconocido (un minero, una shell inversa, un agente) es una señal. Compara los NOMBRES de los procesos (nunca sus argumentos,
# que podrían llevar secretos) con la lista de infra/vm-aux/procesos-permitidos.txt: un nombre por línea, `#` para comentar, y un `*` final
# como prefijo (los hilos del kernel). Es una heurística, no una garantía: un atacante puede llamar a su proceso `sshd`; por eso es una
# alerta más junto a la del ingreso SSH, los sellos y el latido externo.
#
#   scripts/vm-aux/vigilar-procesos.sh
#
# Una línea «ALERTA proceso nuevo: <nombre>» por cada nombre fuera de la lista (y el código 1); con todo en regla, «ok …» y 0. Corre cada
# 5 minutos desde cron, envuelto por alertar.sh. Variables: PS, AUX_PROCESOS_PERMITIDOS.
set -uo pipefail
PS="${PS:-ps}"
LISTA="${AUX_PROCESOS_PERMITIDOS:-/opt/babelhub/infra/vm-aux/procesos-permitidos.txt}"

[[ -f "$LISTA" ]] || { echo "ALERTA falta la lista de procesos permitidos ($LISTA)"; exit 1; }
permitidos="$(grep -v '^[[:space:]]*#' "$LISTA" | sed 's/[[:space:]]*$//' | grep -v '^$' || true)"
[[ -n "$permitidos" ]] || { echo "ALERTA la lista de procesos permitidos está vacía"; exit 1; }
if ! actuales="$("$PS" -eo comm= 2>/dev/null)"; then
  echo "ALERTA no se pudo listar los procesos"
  exit 1
fi

nuevos=0
while IFS= read -r nombre; do
  nombre="${nombre%$'\r'}"
  [[ -z "$nombre" ]] && continue
  conocido=0
  while IFS= read -r permitido; do
    permitido="${permitido%$'\r'}"
    if [[ "$permitido" == *'*' ]]; then
      [[ "$nombre" == "${permitido%\*}"* ]] && conocido=1
    else
      [[ "$nombre" == "$permitido" ]] && conocido=1
    fi
    [[ $conocido -eq 1 ]] && break
  done <<<"$permitidos"
  if [[ $conocido -eq 0 ]]; then
    echo "ALERTA proceso nuevo: $(printf '%s' "$nombre" | tr -cd 'A-Za-z0-9._:/@+-' | cut -c1-40)"
    nuevos=$((nuevos + 1))
  fi
done < <(sort -u <<<"$actuales")

[[ $nuevos -eq 0 ]] && echo "ok todos los procesos están en la lista permitida"
[[ $nuevos -eq 0 ]]
