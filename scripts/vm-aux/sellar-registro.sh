#!/usr/bin/env bash
# Sella el registro de supresiones (PLA-38, D15): cada hora escribe en `supresiones-sellos` el objeto `sellos/AAAA-MM-DD-HHmm.json` con
# `{instante, cantidad, sumaDelConjunto}`. Al restaurar, el servicio compara lo que ve en el registro con el sello más reciente anterior al
# respaldo: si alguien borró o cambió una entrada (o agregó una falsa) desde que se selló, la suma no coincide y no se abre (PLA-19).
# Corre en VM-AUX con la credencial `supresiones-sello` (remoto `sello`): crea en `supresiones-sellos` y lee las entradas; no puede
# alterarlas ni borrar nada, y el servicio comprometido no puede escribir sellos (su credencial no llega a ese contenedor).
#
#   scripts/vm-aux/sellar-registro.sh
#
# La forma canónica es la del verificador del E-commerce (`SelloDelRegistro.SumaDelConjuntoDe`), con una prueba de conformidad:
#   - entran las entradas cuya hora de modificación EN EL ALMACÉN es anterior, de forma estricta, al instante del sello menos 15 minutos
#     (lo que se escribe mientras se sella no cambia el resultado);
#   - una línea «clave<TAB>suma<LF>» por entrada, ordenadas por clave en orden ordinal (LC_ALL=C), con la suma que trae cada objeto;
#   - sumaDelConjunto es el SHA-256 en minúscula de ese texto (el conjunto vacío es el SHA-256 de la cadena vacía).
# Si una entrada no se puede interpretar (no trae su suma) no se sella: es una entrada alterada y se avisa (el sello la daría por buena).
# Guarda el último sello en AUX_ESTADO/ultimo-sello.json para el ancla semanal por correo (anclar-sello.sh).
#
# Códigos de salida: 0 sellado, 1 no se pudo sellar (alerta).
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/comun.sh"

MARGEN_SEGUNDOS=900
ahora="$(date -u +%s)"
instante="$(date -u -d "@$ahora" '+%Y-%m-%dT%H:%M:%SZ')"
umbral="$(hora_texto $((ahora - MARGEN_SEGUNDOS)))"
clave_del_sello="sellos/$(date -u -d "@$ahora" '+%Y-%m-%d-%H%M').json"

if ! listado="$(listar sello:supresiones-entradas)"; then
  echo "ALERTA sello del registro: no se pudo consultar supresiones-entradas"
  exit 1
fi

temporal="$(mktemp -d)"
trap 'rm -rf "$temporal"' EXIT
conjunto="$temporal/conjunto.txt"
: >"$conjunto"
cantidad=0

# Las claves cubiertas (modificadas antes del umbral), en orden ordinal.
mapfile -t claves < <(awk -F';' -v umbral="$umbral" 'NF >= 2 && $2 < umbral { print $1 }' <<<"$listado" | LC_ALL=C sort)
for clave in "${claves[@]}"; do
  if ! contenido="$("$RCLONE" cat "sello:supresiones-entradas/$clave" 2>/dev/null)"; then
    echo "ALERTA sello del registro: no se pudo leer una entrada ($clave)"
    exit 1
  fi
  suma="$(sed -n 's/.*"suma"[[:space:]]*:[[:space:]]*"\([0-9a-f]\{64\}\)".*/\1/p' <<<"$contenido" | head -n 1)"
  if [[ -z "$suma" ]]; then
    echo "ALERTA sello del registro: la entrada $clave no se puede interpretar (no trae su suma): no se sella"
    exit 1
  fi
  printf '%s\t%s\n' "$clave" "$suma" >>"$conjunto"
  cantidad=$((cantidad + 1))
done

suma_del_conjunto="$(suma_de "$conjunto")"
sello="$temporal/sello.json"
printf '{"instante":"%s","cantidad":%s,"sumaDelConjunto":"%s"}\n' "$instante" "$cantidad" "$suma_del_conjunto" >"$sello"

if ! subir "$sello" "sello:supresiones-sellos/$clave_del_sello"; then
  echo "ALERTA sello del registro: no se pudo escribir $clave_del_sello (¿ya existe el de este minuto?)"
  exit 1
fi

mkdir -p "$AUX_ESTADO"
cp "$sello" "$AUX_ESTADO/ultimo-sello.json.nuevo"
mv -f "$AUX_ESTADO/ultimo-sello.json.nuevo" "$AUX_ESTADO/ultimo-sello.json"
echo "ok sello $clave_del_sello ($cantidad entradas)"
