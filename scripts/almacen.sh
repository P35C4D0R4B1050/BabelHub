#!/usr/bin/env bash
# Contenedores, políticas, retención con bloqueo de tiempo y ciclo de vida del almacén de objetos de Oracle (PLA-16, PLA-35, PLA-38; D07,
# D08, D23, D24). La definición está en infra/almacen/ (contenedores.txt y credenciales.txt) y es la misma que leen las pruebas.
#
#   scripts/almacen.sh ciclo-de-vida                       el JSON de infra/almacen/ciclo-de-vida.json, desde contenedores.txt
#   scripts/almacen.sh politicas-iam --compartimento-nombre <nombre>
#                                                          las sentencias de política IAM, una por credencial y contenedor
#   scripts/almacen.sh plan     --compartimento-id <ocid> --compartimento-nombre <nombre> --espacio <espacio-de-nombres>
#                                                          muestra los comandos de `oci` sin ejecutar ninguno
#   scripts/almacen.sh aplicar  (mismos argumentos)        crea lo que falta (contenedores privados, retención y ciclo de vida)
#   scripts/almacen.sh comprobar --espacio <espacio-de-nombres> [--aceptar-bloqueo-pendiente]
#                                                          compara lo que hay en el almacén con la definición
#
# `OCI` es el comando del CLI de Oracle (por defecto `oci`). Se ejecuta con la credencial de administración de la persona responsable, en
# su equipo: nunca en una VM ni en CI. Este script no recibe ni imprime secretos.
#
# El bloqueo de la retención es irreversible y Oracle lo hace efectivo recién 14 días después de crear la regla: `aplicar` fija la fecha de
# bloqueo a 15 días y `comprobar` distingue «bloqueo pendiente» (todavía se podría quitar la regla) de «bloqueado». Mientras esté pendiente
# termina con código 3, salvo que se pase --aceptar-bloqueo-pendiente. El permiso por prefijo no se usa: la separación entre los registros
# de entradas y de sellos es por contenedor (ver infra/almacen/politicas.md).
#
# Códigos de salida: 0 todo conforme, 1 hay diferencias o falló un comando, 2 uso o definición inválidos, 3 conforme salvo bloqueo pendiente.
set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEFINICION="${ALMACEN_DEFINICION:-$RAIZ/infra/almacen}"
OCI="${OCI:-oci}"
DIAS_DE_ESPERA_DEL_BLOQUEO=15

fallo_de_uso() {
  echo "Error: $1" >&2
  exit 2
}

# ---------- Lectura de la definición ----------

NOMBRES=()
declare -A RETENCION=() BLOQUEADA=() EXPIRA=()
CRED_CONTENEDOR=()
CRED_NOMBRE=()
CRED_ACCIONES=()

limpiar() {
  local linea="${1%%#*}"
  linea="${linea//$'\r'/}"
  echo "$linea"
}

leer_definicion() {
  local archivo linea nombre dias bloqueo expira resto credencial contenedor acciones
  archivo="$DEFINICION/contenedores.txt"
  [[ -f "$archivo" ]] || fallo_de_uso "no existe $archivo"
  while IFS= read -r linea || [[ -n "$linea" ]]; do
    linea="$(limpiar "$linea")"
    [[ -z "${linea//[[:space:]]/}" ]] && continue
    read -r nombre dias bloqueo expira resto <<<"$linea"
    [[ -z "$resto" ]] || fallo_de_uso "contenedores.txt: sobran columnas en «$nombre»"
    [[ "$nombre" =~ ^[a-z][a-z0-9-]*$ ]] || fallo_de_uso "contenedores.txt: el nombre «$nombre» no es válido"
    [[ -z "${RETENCION[$nombre]:-}" ]] || fallo_de_uso "contenedores.txt: «$nombre» está repetido"
    [[ "$dias" =~ ^[0-9]+$ ]] || fallo_de_uso "contenedores.txt: la retención de «$nombre» no es un número de días"
    [[ "$bloqueo" == si || "$bloqueo" == no ]] || fallo_de_uso "contenedores.txt: el bloqueo de «$nombre» debe ser si o no"
    [[ "$expira" == nunca || "$expira" =~ ^[0-9]+$ ]] || fallo_de_uso "contenedores.txt: la expiración de «$nombre» debe ser un número o «nunca»"
    [[ "$bloqueo" == no || "$dias" -gt 0 ]] || fallo_de_uso "contenedores.txt: «$nombre» tiene bloqueo sin días de retención"
    if [[ "$expira" != nunca && "$expira" -lt "$dias" ]]; then
      fallo_de_uso "contenedores.txt: «$nombre» retira los objetos antes de que venza su retención"
    fi
    NOMBRES+=("$nombre")
    RETENCION[$nombre]="$dias"
    BLOQUEADA[$nombre]="$bloqueo"
    EXPIRA[$nombre]="$expira"
  done <"$archivo"
  [[ ${#NOMBRES[@]} -gt 0 ]] || fallo_de_uso "contenedores.txt no define contenedores"

  archivo="$DEFINICION/credenciales.txt"
  [[ -f "$archivo" ]] || fallo_de_uso "no existe $archivo"
  while IFS= read -r linea || [[ -n "$linea" ]]; do
    linea="$(limpiar "$linea")"
    [[ -z "${linea//[[:space:]]/}" ]] && continue
    read -r credencial contenedor acciones resto <<<"$linea"
    [[ -z "$resto" && -n "$acciones" ]] || fallo_de_uso "credenciales.txt: la línea de «$credencial» debe tener credencial, contenedor y acciones"
    [[ -n "${RETENCION[$contenedor]:-}" ]] || fallo_de_uso "credenciales.txt: «$credencial» usa el contenedor desconocido «$contenedor»"
    local accion
    for accion in ${acciones//,/ }; do
      case "$accion" in
        crear | leer | listar | uso | sobrescribir | borrar) ;;
        *) fallo_de_uso "credenciales.txt: la acción «$accion» de «$credencial» no existe" ;;
      esac
    done
    CRED_NOMBRE+=("$credencial")
    CRED_CONTENEDOR+=("$contenedor")
    CRED_ACCIONES+=("$acciones")
  done <"$archivo"
}

# ---------- Salidas derivadas ----------

elemento_de_ciclo() {
  printf '{"name": "retiro-a-los-%s-dias", "action": "DELETE", "isEnabled": true, "timeAmount": %s, "timeUnit": "DAYS", "target": "objects"}' "$1" "$1"
}

ciclo_de_vida() {
  local i nombre coma=""
  echo '{'
  echo '  "contenedores": {'
  for i in "${!NOMBRES[@]}"; do
    nombre="${NOMBRES[$i]}"
    [[ $i -lt $((${#NOMBRES[@]} - 1)) ]] && coma="," || coma=""
    if [[ "${EXPIRA[$nombre]}" == nunca ]]; then
      printf '    "%s": []%s\n' "$nombre" "$coma"
    else
      printf '    "%s": [%s]%s\n' "$nombre" "$(elemento_de_ciclo "${EXPIRA[$nombre]}")" "$coma"
    fi
  done
  echo '  }'
  echo '}'
}

permiso_de_oracle() {
  case "$1" in
    crear) echo OBJECT_CREATE ;;
    leer) echo OBJECT_READ ;;
    listar) echo OBJECT_INSPECT ;;
    sobrescribir) echo OBJECT_OVERWRITE ;;
    borrar) echo OBJECT_DELETE ;;
  esac
}

politicas_iam() {
  local compartimento="$1" i accion permisos
  for i in "${!CRED_NOMBRE[@]}"; do
    permisos=""
    local solo_uso=1
    for accion in ${CRED_ACCIONES[$i]//,/ }; do
      if [[ "$accion" == uso ]]; then
        continue
      fi
      solo_uso=0
      permisos+="${permisos:+, }request.permission='$(permiso_de_oracle "$accion")'"
    done
    if [[ $solo_uso -eq 1 ]]; then
      echo "Allow group ${CRED_NOMBRE[$i]} to read buckets in compartment $compartimento where target.bucket.name='${CRED_CONTENEDOR[$i]}'"
    else
      echo "Allow group ${CRED_NOMBRE[$i]} to manage objects in compartment $compartimento where all {target.bucket.name='${CRED_CONTENEDOR[$i]}', any {$permisos}}"
    fi
  done
}

# ---------- Ejecución en el almacén ----------

MODO=""
COMPARTIMENTO_ID=""
COMPARTIMENTO_NOMBRE=""
ESPACIO=""
ACEPTAR_PENDIENTE=0

ejecutar() {
  if [[ "$MODO" == plan ]]; then
    printf '%q ' "$OCI" "$@"
    echo
  else
    "$OCI" "$@"
  fi
}

consulta() {
  # Un valor del CLI sin comillas; el vacío, «null» y «None» cuentan como «no hay».
  local valor
  valor="$("$OCI" "$@" --raw-output 2>/dev/null)" || return 1
  valor="${valor//$'\r'/}"
  case "$valor" in null | None | "") echo "" ;; *) echo "$valor" ;; esac
}

bloqueo_futuro() {
  date -u -d "+${DIAS_DE_ESPERA_DEL_BLOQUEO} days" +%Y-%m-%dT%H:%M:%SZ
}

aplicar() {
  local nombre dias archivo fecha
  fecha="$(bloqueo_futuro)"
  for nombre in "${NOMBRES[@]}"; do
    if [[ "$MODO" == aplicar ]] && "$OCI" os bucket get --namespace-name "$ESPACIO" --name "$nombre" >/dev/null 2>&1; then
      echo "= $nombre: ya existe"
    else
      ejecutar os bucket create --compartment-id "$COMPARTIMENTO_ID" --namespace-name "$ESPACIO" --name "$nombre" --public-access-type NoPublicAccess
    fi

    dias="${RETENCION[$nombre]}"
    if [[ "$dias" -gt 0 ]]; then
      if [[ "$MODO" == aplicar && -n "$(consulta os retention-rule list --namespace-name "$ESPACIO" --bucket-name "$nombre" --query 'data.items[0]."time-amount"' || true)" ]]; then
        echo "= $nombre: ya tiene su regla de retención (no se cambia: puede estar bloqueada)"
      elif [[ "${BLOQUEADA[$nombre]}" == si ]]; then
        ejecutar os retention-rule create --namespace-name "$ESPACIO" --bucket-name "$nombre" --display-name "retencion-${dias}-dias" --time-amount "$dias" --time-unit days --time-rule-locked "$fecha"
      else
        ejecutar os retention-rule create --namespace-name "$ESPACIO" --bucket-name "$nombre" --display-name "retencion-${dias}-dias" --time-amount "$dias" --time-unit days
      fi
    fi

    if [[ "${EXPIRA[$nombre]}" != nunca ]]; then
      archivo="$(mktemp)"
      echo "[$(elemento_de_ciclo "${EXPIRA[$nombre]}")]" >"$archivo"
      if [[ "$MODO" == plan ]]; then
        printf '%q ' "$OCI" os object-lifecycle-policy put --namespace-name "$ESPACIO" --bucket-name "$nombre" --items "$(cat "$archivo")"
        echo
      else
        "$OCI" os object-lifecycle-policy put --namespace-name "$ESPACIO" --bucket-name "$nombre" --items "file://$archivo" --force >/dev/null
      fi
      rm -f "$archivo"
    fi
  done
  echo "# Sentencias de política IAM (se aplican con la consola o con «oci iam policy create»):"
  politicas_iam "$COMPARTIMENTO_NOMBRE"
  if [[ "$MODO" == aplicar ]]; then
    echo "# El bloqueo de las retenciones queda efectivo el $fecha (Oracle lo exige a 14 días como mínimo): comprobar entonces con «comprobar»."
  fi
}

epoch() {
  date -u -d "$1" +%s 2>/dev/null || echo ""
}

comprobar() {
  local nombre dias valor unidad bloqueo ahora fallos=0 pendientes=0
  ahora="$(date -u +%s)"
  for nombre in "${NOMBRES[@]}"; do
    local fallos_antes=$fallos
    valor="$(consulta os bucket get --namespace-name "$ESPACIO" --name "$nombre" --query 'data."public-access-type"' || echo "__no__")"
    if [[ "$valor" == "__no__" ]]; then
      echo "x $nombre: no existe o no se puede consultar"
      fallos=$((fallos + 1))
      continue
    fi
    [[ "$valor" == NoPublicAccess ]] || { echo "x $nombre: no es privado ($valor)"; fallos=$((fallos + 1)); }

    dias="${RETENCION[$nombre]}"
    if [[ "$dias" -gt 0 ]]; then
      valor="$(consulta os retention-rule list --namespace-name "$ESPACIO" --bucket-name "$nombre" --query 'data.items[0]."time-amount"' || true)"
      unidad="$(consulta os retention-rule list --namespace-name "$ESPACIO" --bucket-name "$nombre" --query 'data.items[0]."time-unit"' || true)"
      if [[ -z "$valor" ]]; then
        echo "x $nombre: no tiene regla de retención"
        fallos=$((fallos + 1))
      elif [[ ! "$valor" =~ ^[0-9]+$ || "${unidad,,}" != days || "$valor" -lt "$dias" ]]; then
        echo "x $nombre: la retención es de $valor ${unidad:-?} y debe ser de al menos $dias días"
        fallos=$((fallos + 1))
      fi

      if [[ "${BLOQUEADA[$nombre]}" == si && -n "$valor" ]]; then
        bloqueo="$(consulta os retention-rule list --namespace-name "$ESPACIO" --bucket-name "$nombre" --query 'data.items[0]."time-rule-locked"' || true)"
        if [[ -z "$bloqueo" ]]; then
          echo "x $nombre: la retención no tiene bloqueo (se podría quitar la regla)"
          fallos=$((fallos + 1))
        elif [[ -z "$(epoch "$bloqueo")" ]]; then
          echo "x $nombre: la fecha de bloqueo «$bloqueo» no se entiende"
          fallos=$((fallos + 1))
        elif [[ "$(epoch "$bloqueo")" -gt "$ahora" ]]; then
          echo "~ $nombre: bloqueo pendiente hasta $bloqueo (todavía se podría quitar la regla)"
          pendientes=$((pendientes + 1))
        fi
      fi
    fi

    if [[ "${EXPIRA[$nombre]}" == nunca ]]; then
      valor="$(consulta os object-lifecycle-policy get --namespace-name "$ESPACIO" --bucket-name "$nombre" --query 'data.items[0]."time-amount"' || true)"
      [[ -z "$valor" ]] || { echo "x $nombre: tiene un ciclo de vida de $valor días y no debe expirar"; fallos=$((fallos + 1)); }
    else
      valor="$(consulta os object-lifecycle-policy get --namespace-name "$ESPACIO" --bucket-name "$nombre" --query 'data.items[0]."time-amount"' || true)"
      local accion habilitada
      accion="$(consulta os object-lifecycle-policy get --namespace-name "$ESPACIO" --bucket-name "$nombre" --query 'data.items[0].action' || true)"
      habilitada="$(consulta os object-lifecycle-policy get --namespace-name "$ESPACIO" --bucket-name "$nombre" --query 'data.items[0]."is-enabled"' || true)"
      if [[ "$valor" != "${EXPIRA[$nombre]}" || "${accion^^}" != DELETE || "${habilitada,,}" != true ]]; then
        echo "x $nombre: el ciclo de vida debe retirar a los ${EXPIRA[$nombre]} días y está en «${valor:-sin política}» (${accion:-sin acción}, habilitada: ${habilitada:-?})"
        fallos=$((fallos + 1))
      fi
    fi

    [[ $fallos -eq $fallos_antes ]] && echo "ok $nombre"
  done

  if [[ $fallos -gt 0 ]]; then
    echo "El almacén no coincide con la definición ($fallos diferencias)." >&2
    return 1
  fi
  if [[ $pendientes -gt 0 && $ACEPTAR_PENDIENTE -eq 0 ]]; then
    echo "Conforme, pero $pendientes bloqueos siguen pendientes: aún no son irreversibles." >&2
    return 3
  fi
  return 0
}

# ---------- Argumentos ----------

[[ $# -ge 1 ]] || fallo_de_uso "falta la orden (ciclo-de-vida, politicas-iam, plan, aplicar o comprobar)"
ORDEN="$1"
shift
while [[ $# -gt 0 ]]; do
  case "$1" in
    --compartimento-id) [[ $# -ge 2 ]] || fallo_de_uso "falta el valor de $1"; COMPARTIMENTO_ID="$2"; shift 2 ;;
    --compartimento-nombre) [[ $# -ge 2 ]] || fallo_de_uso "falta el valor de $1"; COMPARTIMENTO_NOMBRE="$2"; shift 2 ;;
    --espacio) [[ $# -ge 2 ]] || fallo_de_uso "falta el valor de $1"; ESPACIO="$2"; shift 2 ;;
    --aceptar-bloqueo-pendiente) ACEPTAR_PENDIENTE=1; shift ;;
    *) fallo_de_uso "argumento desconocido «$1»" ;;
  esac
done

leer_definicion

case "$ORDEN" in
  ciclo-de-vida)
    ciclo_de_vida
    ;;
  politicas-iam)
    [[ -n "$COMPARTIMENTO_NOMBRE" ]] || fallo_de_uso "falta --compartimento-nombre"
    politicas_iam "$COMPARTIMENTO_NOMBRE"
    ;;
  plan | aplicar)
    [[ -n "$COMPARTIMENTO_ID" && -n "$COMPARTIMENTO_NOMBRE" && -n "$ESPACIO" ]] || fallo_de_uso "faltan --compartimento-id, --compartimento-nombre o --espacio"
    MODO="$ORDEN"
    aplicar
    ;;
  comprobar)
    [[ -n "$ESPACIO" ]] || fallo_de_uso "falta --espacio"
    MODO=comprobar
    comprobar
    ;;
  *)
    fallo_de_uso "orden desconocida «$ORDEN»"
    ;;
esac
