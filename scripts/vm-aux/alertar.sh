#!/usr/bin/env bash
# Envuelve una comprobación de VM-AUX y, si detecta algo, avisa por correo a la persona responsable (PLA-20, AC-PLA-20). El correo se
# origina aquí, fuera de la VM de datos: si esa VM está caída o el respaldo falló, nadie allí puede avisarlo.
#
#   scripts/vm-aux/alertar.sh <nombre> <comando> [argumentos…]
#   p. ej.:  alertar.sh respaldos /opt/babelhub/scripts/vm-aux/comprobar-respaldos.sh
#
# Si el comando termina con 0, no hay aviso (y se olvida una alerta anterior del mismo nombre, para volver a avisar si reaparece). Si
# termina con otro código, el correo lleva lo que imprimió. Para no repetir el mismo aviso cada 5 minutos, la misma alerta (mismo texto)
# no se reenvía hasta pasadas AUX_REAVISO_HORAS horas (por defecto 6); un texto distinto sí avisa de inmediato. Si el correo no se puede
# enviar, termina con 3 y no recuerda la alerta, de modo que el siguiente intento vuelve a avisar.
#
# Códigos de salida: 0 todo bien, 1 alerta (avisada, o ya avisada antes), 2 uso inválido, 3 no se pudo enviar el correo.
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/comun.sh"

[[ $# -ge 2 ]] || { echo "Uso: $0 <nombre> <comando> [argumentos…]" >&2; exit 2; }
nombre="$1"
shift
[[ "$nombre" =~ ^[a-z][a-z0-9-]*$ ]] || { echo "Error: el nombre de la alerta no es válido" >&2; exit 2; }

REAVISO_HORAS="${AUX_REAVISO_HORAS:-6}"
estado="$AUX_ESTADO/alertas"
mkdir -p "$estado"
registro="$estado/$nombre"

codigo=0
salida="$("$@" 2>&1)" || codigo=$?
if [[ $codigo -eq 0 ]]; then
  rm -f "$registro"
  exit 0
fi

huella="$(printf '%s' "$salida" | sha256sum | cut -d' ' -f1)"
ahora="$(date -u +%s)"
if [[ -f "$registro" ]]; then
  read -r huella_anterior epoca_anterior <"$registro" || true
  if [[ "${huella_anterior:-}" == "$huella" && -n "${epoca_anterior:-}" && $((ahora - epoca_anterior)) -lt $((REAVISO_HORAS * 3600)) ]]; then
    echo "Alerta «$nombre» vigente: ya se avisó." >&2
    exit 1
  fi
fi

if ! {
  echo "VM-AUX detectó un problema en «$nombre» ($(date -u '+%Y-%m-%d %H:%M UTC'), código $codigo):"
  echo
  echo "$salida"
} | enviar_correo "[BabelHub] ALERTA: $nombre"; then
  echo "vm-aux: no se pudo enviar el correo de la alerta «$nombre»" >&2
  exit 3
fi
printf '%s %s\n' "$huella" "$ahora" >"$registro"
echo "Alerta «$nombre» enviada por correo." >&2
exit 1
