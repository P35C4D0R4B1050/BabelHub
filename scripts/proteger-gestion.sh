#!/usr/bin/env bash
# En VM-VENTAS (topología de dos VMs, DD-104): de todos los contenedores de esta VM, solo Nginx puede llegar a la réplica de gestión
# (VM-INVENTARIO, puerto 8081). La réplica cree el origen del cliente (X-Origen-Cliente) de la dirección privada de esta VM, y cualquier
# contenedor que saliera hacia ese puerto (n8n, Evolution API, el E-commerce) llevaría esa misma dirección: podría inventarse el origen y saltarse
# Cloudflare Access y los límites del borde. La regla va en la cadena DOCKER-USER (la que Docker deja para esto), que ve la dirección del
# contenedor antes de traducirla, y no toca las reglas del sistema (no ejecutes `netfilter-persistent save`).
#
#   scripts/proteger-gestion.sh <ip-privada-de-VM-INVENTARIO>        (como root; es idempotente y no imprime nada si ya está)
#
# La instala scripts/desplegar.sh ventas, que además la repone cada minuto desde cron (Docker no la conserva al reiniciar la VM: durante
# ese primer minuto la protección no está). No cubre lo que ejecute quien ya sea administrador de la VM.
#
# Variables: IPTABLES (por defecto `iptables`; un doble en las pruebas). Códigos: 0 la regla está puesta, 1 no se pudo poner, 2 uso inválido.
set -euo pipefail

IPTABLES="${IPTABLES:-iptables}"
# La dirección fija de Nginx en la red `tunel` (infra/docker-compose.ventas.yml): por ella sale hacia la otra VM.
NGINX=10.213.10.3

if [[ $# -ne 1 || ! "$1" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ || "$1" == 0.0.0.0 ]]; then
  echo "Uso: $0 <ip-privada-de-VM-INVENTARIO>" >&2
  exit 2
fi

regla=(DOCKER-USER -d "$1" -p tcp --dport 8081 ! -s "$NGINX" -j DROP)
"$IPTABLES" -w -C "${regla[@]}" >/dev/null 2>&1 && exit 0
if ! "$IPTABLES" -w -I "${regla[@]}"; then
  echo "proteger-gestion: no se pudo poner la regla en DOCKER-USER (¿se ejecuta como root y con Docker en marcha?)" >&2
  exit 1
fi
