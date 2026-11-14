#!/usr/bin/env bash
# Verifica el cifrado en reposo de los discos de una VM de Oracle Cloud (PLA-20, AM29, D19; lo reutiliza T38 para VM-INVENTARIO y VM-VENTAS).
# Corre en el equipo de la persona responsable, con su CLI de la nube (nunca en una VM: ninguna VM tiene credenciales de administración).
#
#   scripts/verificar-cifrado-de-disco.sh --instancia <ocid> --compartimento <ocid> --dominio <dominio-de-disponibilidad> [--exigir-clave-del-cliente]
#
# El cifrado en reposo de los volúmenes de Oracle no se puede desactivar: es siempre con una clave de Oracle (por defecto) o, si se configuró,
# con una clave del cliente (`kms-key-id`). Desde dentro de la VM no se ve, por eso se pregunta al proveedor. Para el volumen de arranque y
# cada volumen de bloque adjunto el script exige que exista, que esté DISPONIBLE (AVAILABLE) y que el proveedor informe su clave, y deja una
# línea por volumen con el tipo de clave: `ok <volumen> AVAILABLE clave-de-oracle` o `ok <volumen> AVAILABLE clave-del-cliente`. Con
# --exigir-clave-del-cliente, un volumen con la clave por defecto de Oracle falla. Un volumen que no se puede consultar nunca se da por bueno.
# Los identificadores se muestran abreviados (los últimos 8 caracteres).
#
# El comando de la CLI se toma de OCI (por defecto `oci`; las pruebas lo sustituyen). Códigos: 0 todo cifrado y disponible, 1 algún volumen
# no cumple o no se pudo consultar, 2 uso inválido.
set -uo pipefail

OCI="${OCI:-oci}"
INSTANCIA=""
COMPARTIMENTO=""
DOMINIO=""
EXIGIR_CLAVE_DEL_CLIENTE=0

uso() {
  echo "Uso: $0 --instancia <ocid> --compartimento <ocid> --dominio <dominio-de-disponibilidad> [--exigir-clave-del-cliente]" >&2
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --instancia) [[ $# -ge 2 ]] || uso; INSTANCIA="$2"; shift 2 ;;
    --compartimento) [[ $# -ge 2 ]] || uso; COMPARTIMENTO="$2"; shift 2 ;;
    --dominio) [[ $# -ge 2 ]] || uso; DOMINIO="$2"; shift 2 ;;
    --exigir-clave-del-cliente) EXIGIR_CLAVE_DEL_CLIENTE=1; shift ;;
    *) uso ;;
  esac
done
[[ -n "$INSTANCIA" && -n "$COMPARTIMENTO" && -n "$DOMINIO" ]] || uso
patron_ocid='^ocid1\.[a-z]+\.[a-z0-9]+\.[a-z0-9._-]*\.[a-z0-9]+$'
[[ "$INSTANCIA" =~ $patron_ocid && "$COMPARTIMENTO" =~ $patron_ocid ]] || { echo "Error: --instancia y --compartimento deben ser identificadores (ocid1…)" >&2; exit 2; }
[[ "$DOMINIO" =~ ^[A-Za-z0-9:._-]+$ ]] || { echo "Error: --dominio no es válido" >&2; exit 2; }

fallos=0
ok() { echo "ok $*"; }
alerta() { echo "ALERTA $*"; fallos=$((fallos + 1)); }
corto() { echo "…${1: -8}"; }

# Los identificadores que imprime la CLI en una lista JSON o en texto.
ocids() { grep -oE 'ocid1\.[a-z]+\.[a-z0-9]+\.[a-z0-9._-]*\.[a-z0-9]+' | sort -u; }

# Comprueba un volumen: $1 = «boot-volume» o «volume», $2 = su identificador.
comprobar_volumen() {
  local tipo="$1" id="$2" estado clave
  local grupo=(bv "$tipo")
  if ! estado="$("$OCI" "${grupo[@]}" get "--$tipo-id" "$id" --query 'data."lifecycle-state"' --raw-output 2>/dev/null)" || [[ -z "$estado" ]]; then
    alerta "$(corto "$id"): no se pudo consultar el volumen al proveedor"
    return
  fi
  if ! clave="$("$OCI" "${grupo[@]}" get "--$tipo-id" "$id" --query 'data."kms-key-id"' --raw-output 2>/dev/null)"; then
    alerta "$(corto "$id"): no se pudo consultar su clave de cifrado"
    return
  fi
  estado="${estado//\"/}"
  clave="${clave//\"/}"
  local origen=clave-de-oracle
  [[ -n "$clave" && "$clave" != null && "$clave" != None ]] && origen=clave-del-cliente
  if [[ "$estado" != AVAILABLE ]]; then
    alerta "$(corto "$id"): el volumen está en estado $estado y debe estar AVAILABLE"
  elif [[ $EXIGIR_CLAVE_DEL_CLIENTE -eq 1 && "$origen" != clave-del-cliente ]]; then
    alerta "$(corto "$id"): usa la clave por defecto de Oracle y se exigió una clave del cliente"
  else
    ok "$(corto "$id") $estado $origen"
  fi
}

volumenes=0

# El volumen de arranque.
if ! arranques="$("$OCI" compute boot-volume-attachment list --compartment-id "$COMPARTIMENTO" --availability-domain "$DOMINIO" --instance-id "$INSTANCIA" --query 'data[*]."boot-volume-id"' 2>/dev/null)"; then
  alerta "no se pudo listar el volumen de arranque de la instancia"
  arranques=""
fi
for id in $(ocids <<<"$arranques"); do
  volumenes=$((volumenes + 1))
  comprobar_volumen boot-volume "$id"
done
if [[ $volumenes -eq 0 && $fallos -eq 0 ]]; then
  alerta "la instancia no tiene volumen de arranque adjunto"
fi

# Los volúmenes de bloque adjuntos (puede no haber ninguno).
if adjuntos="$("$OCI" compute volume-attachment list --compartment-id "$COMPARTIMENTO" --instance-id "$INSTANCIA" --query 'data[*]."volume-id"' 2>/dev/null)"; then
  for id in $(ocids <<<"$adjuntos"); do
    volumenes=$((volumenes + 1))
    comprobar_volumen volume "$id"
  done
else
  alerta "no se pudo listar los volúmenes de bloque de la instancia"
fi

if [[ $fallos -gt 0 ]]; then
  echo "El cifrado de disco NO está verificado ($fallos problema(s))."
  exit 1
fi
echo "Cifrado de disco verificado en $volumenes volumen(es) (con la clave que informa el proveedor)."
