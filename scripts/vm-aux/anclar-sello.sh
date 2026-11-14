#!/usr/bin/env bash
# Ancla semanal del sello (PLA-38, D15): envía por correo a la persona responsable el último sello del registro de supresiones. El correo
# vive fuera del almacén, así que aunque alguien con acceso a él reescribiera el registro y los sellos, la suma que la persona recibió cada
# semana no cambia: es contra la que se contrasta el sello al restaurar. El correo no lleva ningún dato personal (una cantidad y una suma).
# El sello lo guardó sellar-registro.sh en AUX_ESTADO/ultimo-sello.json (la credencial de VM-AUX no puede leer sus propios sellos).
#
#   scripts/vm-aux/anclar-sello.sh
#
# Códigos de salida: 0 enviado, 1 no hay sello que anclar (alerta), 3 no se pudo enviar el correo.
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/comun.sh"

sello="$AUX_ESTADO/ultimo-sello.json"
if [[ ! -s "$sello" ]]; then
  echo "ALERTA ancla del sello: no hay ningún sello guardado (¿corre sellar-registro.sh?)"
  exit 1
fi

if ! {
  echo "Ancla semanal del sello del registro de supresiones de BabelHub."
  echo
  echo "Guarde este correo: al restaurar un respaldo, la suma del sello que se verifica se contrasta con la que recibió aquí."
  echo
  cat "$sello"
} | enviar_correo "[BabelHub] Ancla semanal del sello del registro de supresiones"; then
  echo "vm-aux: no se pudo enviar el correo del ancla" >&2
  exit 3
fi
echo "ok ancla enviada"
