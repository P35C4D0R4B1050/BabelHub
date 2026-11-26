#!/usr/bin/env bash
# Corre en VM-AUX (PLA-18, PLA-20): comprueba, desde fuera de la VM de datos, que el almacén no retiene más de lo esperado. Si el ciclo de vida
# no retira (la regla se apagó, el almacén no la ejecuta, o alguien guarda lo que no debe), los respaldos crecen en silencio, la cuota gratuita
# se agota (PLA-29, PLA-30) y se conserva lo que ya debió retirarse. Termina con código 1 y una línea «ALERTA …» por cada exceso; el aviso por
# correo a la persona responsable lo envía quien programa esta comprobación (T25), con el código de salida.
#
#   scripts/vm-aux/vigilar-retencion.sh
#
# Usa dos credenciales a través de rclone, configuradas en el entorno de VM-AUX (ver comun.sh):
#   - remoto `vigilante`: `respaldos-vigilante`, de solo lectura sobre los contenedores de respaldos en Cloudflare R2 (D26);
#   - remoto `registro`: `registro-vigilante`, que solo lista `supresiones-copia` en Oracle.
# Los plazos salen de las definiciones: infra/almacen/r2/contenedores.txt (respaldos) e infra/almacen/contenedores.txt (el resto)
# (ALMACEN_DEFINICION cambia la carpeta). RCLONE es el comando de rclone.
#
# Qué comprueba, por cada contenedor que tiene ciclo de vida:
#   - en R2, que ningún objeto tenga más días que su plazo más 1 (el almacén retira lo vencido en un día como mucho, PLA-18: el archivo de
#     cambios nunca supera 8 días); en Oracle, que no supere su plazo más 2 (el ciclo de vida corre una vez al día, con algo de desfase);
#   - en los respaldos por clase, que no haya más copias distintas (días de respaldo) que las esperadas más una de margen:
#     diarios 8 (7), semanales 5 (4) y mensuales 5 (3 o 4).
# Los contenedores que no expiran (libros, portadas, el registro de supresiones y sus sellos) no se miden aquí.
#
# Códigos de salida: 0 conforme, 1 hay excesos o no se pudo consultar un contenedor, 2 definición inválida.
set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DEFINICION="${ALMACEN_DEFINICION:-$RAIZ/infra/almacen}"
. "$(dirname "${BASH_SOURCE[0]}")/comun.sh"

declare -A COPIAS_MAXIMAS=([respaldos-diarios]=8 [respaldos-semanales]=5 [respaldos-mensuales]=5)

ahora="$(date -u +%s)"
alertas=0

# vigilar <remoto> <contenedor> <días de retiro> <días de margen>
vigilar() {
  local remoto="$1" nombre="$2" expira="$3" margen="$4" listado umbral total vencidos copias mas_viejo_texto mas_viejo mas_viejo_epoca estado maximo
  [[ "$expira" =~ ^[0-9]+$ ]] || { echo "Error: la expiración de $nombre no es un número" >&2; exit 2; }

  if ! listado="$(listar "$remoto:$nombre" 2>/dev/null)"; then
    echo "ALERTA $nombre: no se pudo consultar el contenedor"
    alertas=$((alertas + 1))
    return
  fi

  # Una sola pasada por el listado (el archivo de cambios tiene miles de objetos): las horas son «AAAA-MM-DD HH:MM:SS» en UTC,
  # así que comparar el texto es comparar el tiempo. Un objeto vence si es anterior al plazo más el margen.
  umbral="$(hora_texto $((ahora - (expira + margen) * 86400)))"
  read -r total vencidos copias mas_viejo_texto < <(
    awk -F';' -v umbral="$umbral" '
      NF >= 2 {
        total++
        if ($2 < umbral) vencidos++
        if ($1 ~ /^[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]\//) dias[substr($1, 1, 10)] = 1
        if (minimo == "" || $2 < minimo) minimo = $2
      }
      END { n = 0; for (d in dias) n++; gsub(/ /, "T", minimo); print total + 0, vencidos + 0, n, (minimo == "" ? "-" : minimo) }' <<<"$listado"
  )
  mas_viejo=0
  if [[ "$mas_viejo_texto" != "-" ]]; then
    mas_viejo_epoca="$(date -u -d "${mas_viejo_texto/T/ }" +%s 2>/dev/null || echo "$ahora")"
    mas_viejo=$(((ahora - mas_viejo_epoca) / 86400))
  fi
  estado=ok
  if [[ $vencidos -gt 0 ]]; then
    echo "ALERTA $nombre: $vencidos objetos con más de $((expira + margen)) días (el plazo es de $expira): el ciclo de vida no los retira"
    alertas=$((alertas + 1))
    estado=alerta
  fi
  maximo="${COPIAS_MAXIMAS[$nombre]:-}"
  if [[ -n "$maximo" && $copias -gt $maximo ]]; then
    echo "ALERTA $nombre: hay $copias copias distintas y se esperan $maximo como máximo"
    alertas=$((alertas + 1))
    estado=alerta
  fi
  if [[ "$estado" == ok ]]; then echo "ok $nombre ($total objetos, $copias copias, el más viejo de $mas_viejo días)"; fi
}

# Las líneas útiles de un archivo de definición (sin comentarios ni vacías).
definicion() {
  [[ -f "$1" ]] || { echo "Error: no existe $1" >&2; return 2; }
  sed -e 's/#.*//' -e 's/\r$//' "$1" | awk 'NF > 0'
}

respaldos="$(definicion "$DEFINICION/r2/contenedores.txt")" || exit 2
oracle="$(definicion "$DEFINICION/contenedores.txt")" || exit 2

while read -r nombre _bloqueo expira resto; do
  [[ -n "$expira" && -z "$resto" ]] || { echo "Error: línea inválida en r2/contenedores.txt" >&2; exit 2; }
  vigilar vigilante "$nombre" "$expira" 1
done <<<"$respaldos"

while read -r nombre _retencion _bloqueada expira _resto; do
  [[ -n "$expira" ]] || { echo "Error: línea inválida en contenedores.txt" >&2; exit 2; }
  [[ "$expira" == nunca ]] && continue
  vigilar registro "$nombre" "$expira" 2
done <<<"$oracle"

[[ $alertas -eq 0 ]]
