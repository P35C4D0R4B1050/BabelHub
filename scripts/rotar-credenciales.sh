#!/usr/bin/env bash
# Rota las credenciales de servicio al restaurar un respaldo en producción (PLA-43): lo que un respaldo restaurado podría haber
# dejado en manos de otros deja de servir antes de abrir el servicio al público.
#
#   scripts/rotar-credenciales.sh --entorno ecommerce=<archivo> --entorno inventario=<archivo> \
#       [--rol <rol-de-base>=<VARIABLE>@<entorno> ...] [--integracion-interna <Proveedor> ...] [--psql "<comando psql>"]
#
# Qué rota (todo con valores aleatorios nuevos, sin dejar la anterior vigente):
#   - Las credenciales de servicio BABELHUB__Seguridad__Credenciales__<Alcance>: una por alcance, con el MISMO valor nuevo en todos los
#     archivos de entorno que la tienen (el E-commerce y el Inventario siguen entendiéndose), y se borra su …Anterior, de modo que la
#     credencial vieja se rechaza de inmediato (no es la rotación en dos pasos de una rotación normal).
#   - Las contraseñas de los roles de base de datos indicados con --rol: ALTER ROLE por psql y la variable del archivo.
#   - Los secretos de integraciones que genera el propio sistema (--integracion-interna), no los que emite un proveedor.
# Lo que emite un proveedor (almacén de objetos, pasarela, Turnstile…) no se puede rotar desde aquí: se lista como pendiente a mano.
# La clave de firma y las sesiones las rota Identidad en el paso de restauración (IRestauracionDeIdentidad).
#
# Seguridad: los archivos de entorno son secretos del despliegue y NO están en el repositorio. Ningún valor sale por pantalla ni por
# argumentos de procesos (los valores viajan por archivos temporales 0600 y por la entrada estándar de psql). Se prepara todo antes de
# aplicar; si un ALTER ROLE falla, los roles ya cambiados vuelven a su contraseña anterior y ningún archivo se toca. Los archivos se
# reemplazan de forma atómica y conservan sus permisos; no queda copia con los valores viejos.
#
# Códigos de salida: 0 rotado, 1 falló y se revirtió, 2 uso inválido (sin cambios).
set -euo pipefail
umask 077

PSQL="${PSQL_ROTACION:-psql -X -q -v ON_ERROR_STOP=1}"

declare -A ARCHIVOS=()
ROLES=()
INTERNAS=()

uso() {
  echo "Uso: $0 --entorno <nombre>=<archivo> ... [--rol <rol>=<VARIABLE>@<entorno> ...] [--integracion-interna <Proveedor> ...] [--psql \"<comando>\"]" >&2
  exit 2
}

fallo_de_uso() {
  echo "Error: $1" >&2
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --entorno)
      [[ $# -ge 2 && "$2" == *=* ]] || uso
      nombre="${2%%=*}"
      [[ "$nombre" =~ ^[a-z][a-z0-9-]*$ ]] || fallo_de_uso "el nombre de entorno «$nombre» no es válido"
      ARCHIVOS["$nombre"]="${2#*=}"
      shift 2
      ;;
    --rol)
      [[ $# -ge 2 ]] || uso
      ROLES+=("$2")
      shift 2
      ;;
    --integracion-interna)
      [[ $# -ge 2 ]] || uso
      [[ "$2" =~ ^[A-Za-z][A-Za-z0-9]*$ ]] || fallo_de_uso "el proveedor «$2» no es válido"
      INTERNAS+=("$2")
      shift 2
      ;;
    --psql)
      [[ $# -ge 2 ]] || uso
      PSQL="$2"
      shift 2
      ;;
    *) uso ;;
  esac
done

[[ ${#ARCHIVOS[@]} -gt 0 ]] || uso
for nombre in "${!ARCHIVOS[@]}"; do
  ruta="${ARCHIVOS[$nombre]}"
  [[ -f "$ruta" && -r "$ruta" && -w "$ruta" ]] || fallo_de_uso "el archivo del entorno «$nombre» no existe o no se puede leer y escribir"
done

# ---------- Qué se rota ----------

valor_de() { # valor_de <archivo> <variable>: el valor actual o vacío
  awk -F= -v clave="$2" '$1 == clave { sub(/^[^=]*=/, ""); print; exit }' "$1"
}

# Credenciales de servicio: los nombres distintos, de todos los archivos (sin las «Anterior»).
declare -A A_ROTAR=()
for ruta in "${ARCHIVOS[@]}"; do
  while IFS= read -r variable; do
    [[ -n "$variable" ]] && A_ROTAR["$variable"]=1
  done < <(awk -F= '/^BABELHUB__Seguridad__Credenciales__[A-Za-z0-9]+=/ && $1 !~ /Anterior$/ { print $1 }' "$ruta")
done

# Secretos de integraciones internas.
for proveedor in "${INTERNAS[@]}"; do
  variable="BABELHUB__Integraciones__${proveedor}__Secreto"
  encontrada=0
  for ruta in "${ARCHIVOS[@]}"; do
    [[ -n "$(valor_de "$ruta" "$variable")" ]] && encontrada=1
  done
  [[ "$encontrada" -eq 1 ]] || fallo_de_uso "ningún archivo tiene $variable"
  A_ROTAR["$variable"]=1
done

# Roles de base de datos.
ROL_NOMBRE=()
ROL_VARIABLE=()
ROL_ENTORNO=()
for especificacion in "${ROLES[@]}"; do
  [[ "$especificacion" =~ ^([a-z_][a-z0-9_]*)=(BABELHUB__[A-Za-z0-9_]+)@([a-z][a-z0-9-]*)$ ]] || fallo_de_uso "el rol «$especificacion» no tiene la forma <rol>=<VARIABLE>@<entorno>"
  rol="${BASH_REMATCH[1]}"; variable="${BASH_REMATCH[2]}"; entorno="${BASH_REMATCH[3]}"
  [[ -n "${ARCHIVOS[$entorno]:-}" ]] || fallo_de_uso "el entorno «$entorno» del rol $rol no se indicó con --entorno"
  [[ -n "$(valor_de "${ARCHIVOS[$entorno]}" "$variable")" ]] || fallo_de_uso "el entorno «$entorno» no tiene $variable"
  ROL_NOMBRE+=("$rol"); ROL_VARIABLE+=("$variable"); ROL_ENTORNO+=("$entorno")
  A_ROTAR["$variable"]=1
done

[[ ${#A_ROTAR[@]} -gt 0 ]] || fallo_de_uso "no hay nada que rotar (ninguna credencial de servicio, rol ni integración interna)"

# ---------- Valores nuevos ----------

nuevo_valor() { # 48 bytes aleatorios en base64 apto para URL y para una contraseña SQL entre comillas (sin ' ni = ni +)
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -base64 48 | tr '+/' '-_' | tr -d '=\r\n'
  else
    head -c 48 /dev/urandom | base64 | tr '+/' '-_' | tr -d '=\r\n'
  fi
}

TEMPORAL="$(mktemp -d)"
chmod 700 "$TEMPORAL"
NUEVOS="$TEMPORAL/nuevos.tsv"
: > "$NUEVOS"
declare -A VALOR_NUEVO=()
for variable in "${!A_ROTAR[@]}"; do
  valor="$(nuevo_valor)"
  VALOR_NUEVO["$variable"]="$valor"
  printf '%s\t%s\n' "$variable" "$valor" >> "$NUEVOS"
done

# Anteriores que se borran: la «Anterior» de cada variable rotada.
ANTERIORES="$TEMPORAL/anteriores.txt"
for variable in "${!A_ROTAR[@]}"; do
  echo "${variable}Anterior"
done > "$ANTERIORES"

limpiar() {
  rm -rf "$TEMPORAL"
  for ruta in "${ARCHIVOS[@]}"; do
    rm -f "${ruta}.rotando.$$"
  done
}
trap limpiar EXIT

# ---------- Archivos nuevos, sin tocar los originales ----------

for nombre in "${!ARCHIVOS[@]}"; do
  ruta="${ARCHIVOS[$nombre]}"
  nuevo="${ruta}.rotando.$$"
  cp -p "$ruta" "$nuevo"
  awk -F'\t' -v nuevos="$NUEVOS" -v anteriores="$ANTERIORES" '
    BEGIN {
      while ((getline linea < nuevos) > 0) { split(linea, p, "\t"); valor[p[1]] = p[2] }
      while ((getline linea < anteriores) > 0) { quitar[linea] = 1 }
    }
    {
      igual = index($0, "=")
      clave = igual > 0 ? substr($0, 1, igual - 1) : ""
      if (clave in quitar) next
      if (clave in valor) { print clave "=" valor[clave]; next }
      print
    }
  ' "$ruta" > "$nuevo"
done

# ---------- Roles de base de datos (con vuelta atrás) ----------

aplicados=()
cambiar_rol() { # cambiar_rol <rol> <contraseña>
  printf 'ALTER ROLE "%s" PASSWORD '"'"'%s'"'"';\n' "$1" "$2" | $PSQL >/dev/null
}

revertir_roles() {
  for indice in "${aplicados[@]}"; do
    anterior="$(valor_de "${ARCHIVOS[${ROL_ENTORNO[$indice]}]}" "${ROL_VARIABLE[$indice]}")"
    cambiar_rol "${ROL_NOMBRE[$indice]}" "$anterior" || echo "No se pudo devolver el rol ${ROL_NOMBRE[$indice]} a su contraseña anterior: revisarlo a mano." >&2
  done
}

for indice in "${!ROL_NOMBRE[@]}"; do
  if cambiar_rol "${ROL_NOMBRE[$indice]}" "${VALOR_NUEVO[${ROL_VARIABLE[$indice]}]}"; then
    aplicados+=("$indice")
  else
    echo "Error: no se pudo cambiar la contraseña del rol ${ROL_NOMBRE[$indice]}; se revierte lo hecho y no se toca ningún archivo." >&2
    revertir_roles
    exit 1
  fi
done

# ---------- Reemplazo atómico ----------

for nombre in "${!ARCHIVOS[@]}"; do
  ruta="${ARCHIVOS[$nombre]}"
  mv -f "${ruta}.rotando.$$" "$ruta"
done

# ---------- Informe: solo nombres ----------

echo "Rotadas (${#A_ROTAR[@]}):"
for variable in $(printf '%s\n' "${!A_ROTAR[@]}" | sort); do
  echo "  rotada: $variable"
done

echo "Pendientes de rotar a mano (los emite un proveedor o no son credenciales de acceso):"
pendientes=0
for nombre in $(printf '%s\n' "${!ARCHIVOS[@]}" | sort); do
  while IFS= read -r variable; do
    [[ -n "$variable" && -z "${A_ROTAR[$variable]:-}" ]] && { echo "  pendiente: $variable ($nombre)"; pendientes=1; }
  done < <(awk -F= '
    /^BABELHUB__Integraciones__/ && $1 !~ /Anterior$/ { print $1 }
    /^BABELHUB__Almacen__/ { print $1 }
    /^BABELHUB__Seguridad__Turnstile__Secreto=/ { print $1 }
    /^BABELHUB__Seguridad__ClaveDeRedisDeLimites=/ { print $1 }
    /^BABELHUB__Seguridad__ClaveDeHuella=/ { print $1 }
  ' "${ARCHIVOS[$nombre]}")
done
[[ "$pendientes" -eq 1 ]] || echo "  (ninguna)"

echo "Recuerda: la clave de firma y las sesiones las rota Identidad en el paso de restauración; reinicia el Inventario para vaciar su caché de claves y comprueba que la credencial anterior se rechaza."
