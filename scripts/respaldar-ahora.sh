#!/usr/bin/env bash
# Respaldo bajo demanda tras una corrección urgente con migración (PLA-09, PLA-16): lo lanza la persona responsable desde su equipo, por SSH
# directo a VM-INVENTARIO (o con --salto, por ProxyJump a través de VM-AUX cuando exista; hoy está aplazada, DD-100), y corre dentro del contenedor de PostgreSQL de VM-INVENTARIO con la credencial `respaldos-escritor` que ese
# contenedor ya tiene (limitada a los contenedores de respaldos; el bloqueo impide borrar o sobrescribir dentro del plazo). Va a respaldos-diarios con la misma retención y no genera copias semanales ni mensuales.
#
#   scripts/respaldar-ahora.sh --destino <usuario@vm-inventario> [--salto <usuario@vm-aux>] [--contenedor <nombre>]
#
# En la VM el usuario SSH no está en el grupo docker (equivale a ser administrador): el comando remoto usa `sudo -n`, sin preguntar contraseña.
# No lleva ni recibe secretos: la llave SSH está en el equipo (agente local, sin reenvío), la clave de cifrado que descifra no sale de ahí y la
# credencial del almacén ya está en la VM. Ejecutar un respaldo no sustituye el ensayo de restauración vigente que exige el despliegue (PLA-11).
#
# Códigos de salida: el del respaldo remoto (0 hecho, 1 falló, 75 ya hay otro en curso), o 2 si el uso es inválido.
set -euo pipefail

SSH="${SSH:-ssh}"
SALTO=""
DESTINO=""
CONTENEDOR="babelhub-inventario-postgres-1"

uso() {
  echo "Uso: $0 --destino <usuario@vm-inventario> [--salto <usuario@vm-aux>] [--contenedor <nombre>]" >&2
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --salto) [[ $# -ge 2 && -n "$2" ]] || uso; SALTO="$2"; shift 2 ;;
    --destino) [[ $# -ge 2 ]] || uso; DESTINO="$2"; shift 2 ;;
    --contenedor) [[ $# -ge 2 ]] || uso; CONTENEDOR="$2"; shift 2 ;;
    *) uso ;;
  esac
done

[[ -n "$DESTINO" ]] || uso
patron='^[A-Za-z0-9._-]+@[A-Za-z0-9._-]+(:[0-9]+)?$'
[[ -z "$SALTO" || "$SALTO" =~ $patron ]] || { echo "Error: --salto debe ser usuario@servidor" >&2; exit 2; }
[[ "$DESTINO" =~ $patron ]] || { echo "Error: --destino debe ser usuario@servidor" >&2; exit 2; }
[[ "$CONTENEDOR" =~ ^[A-Za-z0-9_.-]+$ ]] || { echo "Error: el nombre del contenedor no es válido" >&2; exit 2; }

echo "Respaldo bajo demanda de $CONTENEDOR (${SALTO:+por $SALTO, }en $DESTINO)..." >&2
salto=()
[[ -z "$SALTO" ]] || salto=(-J "$SALTO")
codigo=0
"$SSH" -o ForwardAgent=no -o BatchMode=yes -o RequestTTY=no ${salto[@]+"${salto[@]}"} "$DESTINO" \
  sudo -n docker exec "$CONTENEDOR" /opt/babelhub/respaldar.sh --bajo-demanda || codigo=$?
if [[ $codigo -eq 0 ]]; then
  echo "Respaldo bajo demanda terminado." >&2
else
  echo "El respaldo bajo demanda terminó con el código $codigo." >&2
fi
exit "$codigo"
