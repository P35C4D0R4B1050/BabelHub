#!/usr/bin/env bash
# Latido externo de VM-AUX (PLA-20, AM29): un pulso periódico a un servicio gratuito de vigilancia fuera de la nube (de los que avisan por
# correo cuando el pulso deja de llegar). Así, si VM-AUX misma cae, alguien lo sabe: sus alertas por correo no pueden avisar de su propia
# caída. El pulso es una petición GET vacía a AUX_LATIDO_URL: no lleva ningún dato, ni nombre de máquina ni contenido. La dirección es un
# secreto (quien la conozca podría fingir el pulso) y no se imprime.
#
#   scripts/vm-aux/latido-externo.sh
#
# Códigos de salida: 0 pulso enviado, 1 no se pudo enviar o falta AUX_LATIDO_URL.
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/comun.sh"

[[ -n "${AUX_LATIDO_URL:-}" ]] || fallo "falta AUX_LATIDO_URL"
config="$(mktemp)"
trap 'rm -f "$config"' EXIT
chmod 600 "$config"
printf 'url = "%s"\n' "$AUX_LATIDO_URL" >"$config"
if ! "$CURL" --silent --show-error --fail --max-time 10 --retry 2 --config "$config" --output /dev/null; then
  fallo "no se pudo enviar el latido"
fi
