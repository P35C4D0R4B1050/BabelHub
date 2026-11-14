#!/usr/bin/env bash
# Corre en VM-AUX (PLA-18, PLA-20): comprueba, desde fuera de la VM de datos, que el almacén no retiene más de lo esperado. Si el ciclo de vida
# no retira (la regla se apagó, el almacén no la ejecuta, o alguien guarda lo que no debe), los respaldos crecen en silencio, la cuota gratuita
# se agota (PLA-29, PLA-30) y se conserva lo que ya debió retirarse. Termina con código 1 y una línea «ALERTA …» por cada exceso; el aviso por
# correo a la persona responsable lo envía quien programa esta comprobación (T25), con el código de salida.
#
#   scripts/vm-aux/vigilar-retencion.sh
#
# Usa la credencial `respaldos-vigilante` (solo lista, sin leer ni escribir) a través de rclone, configurada en el entorno de VM-AUX:
# RCLONE_CONFIG_ALMACEN_TYPE=s3, _ENDPOINT, _REGION, _ACCESS_KEY_ID y _SECRET_ACCESS_KEY. La definición de los plazos es la de
# infra/almacen/contenedores.txt (ALMACEN_DEFINICION la cambia). RCLONE es el comando de rclone.
#
# Qué comprueba, por cada contenedor que tiene ciclo de vida:
#   - que ningún objeto tenga más días que su plazo más 2 de margen (el almacén ejecuta el ciclo una vez al día, con algo de desfase);
#   - en los de respaldo por clase, que no haya más copias distintas (días de respaldo) que las esperadas más una de margen:
#     diarios 8 (7), semanales 5 (4) y mensuales 5 (3 o 4).
# Los contenedores que no expiran (el registro de supresiones y sus sellos) no se miden aquí.
#
# Códigos de salida: 0 conforme, 1 hay excesos o no se pudo consultar un contenedor, 2 definición inválida.
set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DEFINICION="${ALMACEN_DEFINICION:-$RAIZ/infra/almacen}"
RCLONE="${RCLONE:-rclone}"
MARGEN_DE_DIAS=2

declare -A COPIAS_MAXIMAS=([respaldos-diarios]=8 [respaldos-semanales]=5 [respaldos-mensuales]=5)

archivo="$DEFINICION/contenedores.txt"
[[ -f "$archivo" ]] || { echo "Error: no existe $archivo" >&2; exit 2; }

ahora="$(date -u +%s)"
alertas=0

while IFS= read -r linea || [[ -n "$linea" ]]; do
  linea="${linea%%#*}"
  linea="${linea//$'\r'/}"
  [[ -z "${linea//[[:space:]]/}" ]] && continue
  read -r nombre _retencion _bloqueada expira _resto <<<"$linea"
  [[ -n "$nombre" && -n "$expira" ]] || { echo "Error: línea inválida en contenedores.txt" >&2; exit 2; }
  [[ "$expira" == nunca ]] && continue
  [[ "$expira" =~ ^[0-9]+$ ]] || { echo "Error: la expiración de $nombre no es un número" >&2; exit 2; }

  if ! listado="$(TZ=UTC "$RCLONE" lsf -R --files-only --format pt --separator ';' "almacen:$nombre" 2>/dev/null)"; then
    echo "ALERTA $nombre: no se pudo consultar el contenedor"
    alertas=$((alertas + 1))
    continue
  fi

  # Una sola pasada por el listado (el archivo de cambios tiene decenas de miles de objetos): las horas son «AAAA-MM-DD HH:MM:SS» en UTC,
  # así que comparar el texto es comparar el tiempo. Un objeto vence si es anterior al plazo más el margen.
  umbral="$(date -u -d "@$((ahora - (expira + MARGEN_DE_DIAS) * 86400))" '+%Y-%m-%d %H:%M:%S')"
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
    echo "ALERTA $nombre: $vencidos objetos con más de $((expira + MARGEN_DE_DIAS)) días (el plazo es de $expira): el ciclo de vida no los retira"
    alertas=$((alertas + 1))
    estado=alerta
  fi
  maximo="${COPIAS_MAXIMAS[$nombre]:-}"
  if [[ -n "$maximo" && $copias -gt $maximo ]]; then
    echo "ALERTA $nombre: hay $copias copias distintas y se esperan $maximo como máximo"
    alertas=$((alertas + 1))
    estado=alerta
  fi
  [[ "$estado" == ok ]] && echo "ok $nombre ($total objetos, $copias copias, el más viejo de $mas_viejo días)"
done <"$archivo"

[[ $alertas -eq 0 ]]
