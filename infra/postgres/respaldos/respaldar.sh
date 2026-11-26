#!/bin/sh
# Respaldo de la copia consistente del clúster (PLA-09, PLA-16, PLA-17; D07, D09, D10, D24, D25).
#
#   respaldar.sh                         respaldo diario programado: el domingo deja además copia en respaldos-semanales y el último día
#                                        del mes en respaldos-mensuales (días UTC)
#   respaldar.sh --bajo-demanda          un respaldo adicional tras una corrección urgente con migración: va solo a respaldos-diarios, con
#                                        la misma retención, y no genera copias semanales ni mensuales
#   respaldar.sh --clases semanal,mensual  fuerza las copias (p. ej. al empezar); no se combina con --bajo-demanda
#
# `pg_basebackup` toma las dos bases de negocio y las demás en un mismo instante (es una copia del clúster). Lo que sale se comprime con zstd
# y se cifra al vuelo con la clave pública de age: el contenido sin cifrar nunca toca el disco. En esta VM solo existe, y hasta confirmar su subida, la copia
# cifrada en el volumen reservado. Sube dos objetos por respaldo, con el nombre «AAAA-MM-DD/<hora UTC>-<sha256 del cifrado>.tar.zst.age»:
# el clúster y, con «-config-» en el nombre, la configuración de la VM sin secretos (nada *.env, *.pem, *.key ni *.pfx).
#
# Si el archivo de cambios estaba declarado roto por falta de espacio (PLA-21, vigilar-espacio.sh), un respaldo completo que termina bien retira
# la marca: es lo único que la retira.
#
# Códigos de salida: 0 hecho, 1 falló (no queda nada local), 2 uso inválido, 75 ya hay otro respaldo en curso.
set -eu
. /opt/babelhub/comun.sh

uso() {
  echo "Uso: respaldar.sh [--bajo-demanda | --clases semanal,mensual]" >&2
  exit 2
}

bajo_demanda=0
clases=""
clases_dadas=0
while [ $# -gt 0 ]; do
  case "$1" in
    --bajo-demanda) bajo_demanda=1 ;;
    --clases)
      [ $# -ge 2 ] || uso
      clases="$(echo "$2" | tr ',' ' ')"
      clases_dadas=1
      shift
      ;;
    *) uso ;;
  esac
  shift
done
if [ "$bajo_demanda" = 1 ] && [ "$clases_dadas" = 1 ]; then
  echo "respaldos: el respaldo bajo demanda no genera copias semanales ni mensuales" >&2
  exit 2
fi
for clase in $clases; do
  case "$clase" in
    semanal | mensual) ;;
    *) echo "respaldos: la clase «$clase» no existe (semanal o mensual)" >&2; exit 2 ;;
  esac
done

verificar_entorno
[ -d "$CONFIG" ] || fallo "falta la carpeta de configuración $CONFIG"

if [ "$bajo_demanda" = 0 ] && [ "$clases_dadas" = 0 ]; then
  clases="$(clases_del_dia "$(date -u +%s)")"
fi

bloqueo="$ESPERA/respaldo.bloqueo"
if ! mkdir "$bloqueo" 2>/dev/null; then
  echo "respaldos: ya hay un respaldo en curso" >&2
  exit 75
fi

base="$ESPERA/base.$$.tmp"
configuracion="$ESPERA/config.$$.tmp"
resultado="$ESPERA/resultado.$$.tmp"
# Si el archivo de cambios estaba roto (PLA-21), este respaldo completo lo repara: la marca se aparta antes de empezar (así lo que se genere desde
# ahora vuelve a archivarse) y solo se borra si todo termina bien; si algo falla, vuelve a su lugar y el archivo sigue roto.
recuperando="$ESPERA/archivo-roto.recuperando"
terminado=0
limpiar() {
  if [ -f "$recuperando" ]; then
    if [ "$terminado" = 1 ]; then rm -f "$recuperando"; else mv -f "$recuperando" "$ARCHIVO_ROTO"; fi
  fi
  rm -f "$base" "$configuracion" "$resultado.orden" "$resultado.zstd"
  rmdir "$bloqueo" 2>/dev/null || true
}
trap limpiar EXIT INT TERM
if [ -f "$ARCHIVO_ROTO" ]; then
  mv -f "$ARCHIVO_ROTO" "$recuperando"
fi

dia="$(date -u +%Y-%m-%d)"
hora="$(date -u +%H%M%S)"

# El clúster: la salida de pg_basebackup pasa directamente por la compresión al cifrado.
comprimir_y_cifrar "$base" "$resultado" pg_basebackup -D - -Ft -X fetch -c fast \
  || fallo "no se pudo copiar, comprimir y cifrar el clúster"

# La configuración, sin secretos.
comprimir_y_cifrar "$configuracion" "$resultado" tar -cf - -C "$CONFIG" --exclude='*.env' --exclude='*.pem' --exclude='*.key' --exclude='*.pfx' . \
  || fallo "no se pudo comprimir y cifrar la configuración"

subir_con_copias() {
  _archivo="$1"
  _nombre="$dia/$hora-$2$(suma_de "$_archivo").tar.zst.age"
  subir "$_archivo" "$CONTENEDOR_DIARIOS" "$_nombre"
  for clase in $clases; do
    case "$clase" in
      semanal) subir "$_archivo" "$CONTENEDOR_SEMANALES" "$_nombre" ;;
      mensual) subir "$_archivo" "$CONTENEDOR_MENSUALES" "$_nombre" ;;
    esac
  done
  rm -f "$_archivo"
  echo "respaldos: subido $CONTENEDOR_DIARIOS/$_nombre${clases:+ y copias: $clases}"
}

subir_con_copias "$base" ""
subir_con_copias "$configuracion" "config-"
if [ -f "$recuperando" ]; then
  echo "respaldos: el archivo de cambios estaba roto (PLA-21) y este respaldo completo lo repara"
fi
terminado=1
