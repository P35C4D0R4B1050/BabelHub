#!/bin/sh
# Crea una base y un rol por servicio con estado (AD03, AD18). Solo para desarrollo local:
# en las VMs de Oracle lo hace el aprovisionamiento de VM-DATOS con las mismas reglas.
# Cada rol solo puede conectarse a su propia base; las demás le niegan CONNECT.
set -eu

crear_base() {
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<EOSQL
CREATE ROLE $1 LOGIN PASSWORD '$2' CONNECTION LIMIT 20;
CREATE DATABASE $1 OWNER $1;
REVOKE CONNECT ON DATABASE $1 FROM PUBLIC;
GRANT CONNECT ON DATABASE $1 TO $1;
EOSQL
}

crear_base ecommerce "$POSTGRES_ECOMMERCE_PASSWORD"
crear_base inventario "$POSTGRES_INVENTARIO_PASSWORD"
crear_base identidad "$POSTGRES_IDENTIDAD_PASSWORD"
