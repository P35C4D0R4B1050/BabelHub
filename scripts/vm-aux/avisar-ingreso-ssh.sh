#!/usr/bin/env bash
# Avisa por correo cada vez que alguien abre una sesión SSH en VM-AUX (PLA-20, AM29, D19). Lo llama PAM al abrir la sesión de sshd, lo que
# cubre también el salto `ssh -J` (que no ejecuta shell ni ~/.ssh/rc): un ingreso que la persona responsable no hizo es la señal de que VM-AUX
# está comprometida. Instalación en /etc/pam.d/sshd (la hace la persona responsable, ver infra/vm-aux/README.md):
#
#   session optional pam_exec.so quiet /opt/babelhub/scripts/vm-aux/avisar-ingreso-ssh.sh
#
# PAM entrega PAM_TYPE, PAM_SERVICE, PAM_USER y PAM_RHOST. Solo actúa con PAM_TYPE=open_session de sshd. El correo lleva el usuario, el origen
# y la hora, nunca una llave ni un contenido. Como la regla de PAM es `optional` y este script termina siempre con 0, un fallo del correo no
# impide el ingreso (que la persona responsable pueda entrar a corregir VM-AUX): queda anotado en AUX_ESTADO/ingresos.log. Corre como el usuario
# sin privilegios `babelhub-aux` (si PAM lo lanza como root, se relanza con `runuser`), el único que lee el entorno de correo.
set -uo pipefail

[[ "${PAM_TYPE:-}" == open_session && "${PAM_SERVICE:-sshd}" == sshd ]] || exit 0

if [[ "${AUX_UID:-$(id -u)}" == 0 ]]; then
  exec "${RUNUSER:-runuser}" -u "${AUX_USUARIO:-babelhub-aux}" -- "$0" "$@"
fi

CARPETA="$(dirname "${BASH_SOURCE[0]}")"
. "$CARPETA/comun.sh"

# Lo que viene de la red se limpia: un nombre con saltos de línea podría inyectar cabeceras en el correo.
limpio() { printf '%s' "$1" | tr -cd 'A-Za-z0-9._:@%-' | cut -c1-64; }
usuario="$(limpio "${PAM_USER:-desconocido}")"
origen="$(limpio "${PAM_RHOST:-local}")"
momento="$(date -u '+%Y-%m-%d %H:%M:%S UTC')"

mkdir -p "$AUX_ESTADO" 2>/dev/null || true
cuerpo="$(mktemp)"
trap 'rm -f "$cuerpo"' EXIT
{
  echo "Se abrió una sesión SSH en VM-AUX."
  echo
  echo "Usuario: ${usuario:-desconocido}"
  echo "Origen:  ${origen:-local}"
  echo "Hora:    $momento"
  echo
  echo "Si no fue la persona responsable, VM-AUX puede estar comprometida: rota sus credenciales (scripts/rotar-credenciales.sh) y revisa los sellos."
} >"$cuerpo"

# Un plazo corto: PAM espera a este script antes de dejar entrar, y el correo no debe retrasar el ingreso.
if ! timeout 25 bash -c '. "$1"; enviar_correo "[BabelHub] ALERTA: ingreso SSH en VM-AUX" <"$2"' _ "$CARPETA/comun.sh" "$cuerpo" 2>>"$AUX_ESTADO/ingresos.log"; then
  echo "$momento no se pudo enviar el aviso del ingreso de ${usuario:-desconocido} desde ${origen:-local}" >>"$AUX_ESTADO/ingresos.log" 2>/dev/null || true
fi
exit 0
