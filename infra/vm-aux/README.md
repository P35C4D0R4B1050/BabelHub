# VM-AUX: vigilancia y sellos

VM-AUX (AMD micro) es el salto SSH hacia las demás VMs y, desde T25, quien **vigila desde fuera de la VM de datos**: si esa VM cae o su respaldo falla, nadie dentro de ella puede avisarlo. Sus scripts están en `scripts/vm-aux/` y no guardan ninguna clave privada ni credencial de borrado. El endurecimiento de la propia VM-AUX (acceso, correo propio, disco cifrado, alerta de ingreso SSH) es T26.

| Tarea programada (`babelhub-aux.cron`) | Cada | Qué hace | Credencial del almacén |
| :--- | :--- | :--- | :--- |
| `comprobar-respaldos.sh` (con `alertar.sh`) | 5 min | Avisa por correo si no hay un respaldo completado en las últimas 24 horas, si el archivado de cambios tiene más de 15 minutos de atraso o si falta la copia diaria del registro (PLA-20). | `respaldos-vigilante` (solo lista) |
| `sellar-registro.sh` | hora | Escribe `sellos/AAAA-MM-DD-HHmm.json` con `{instante, cantidad, sumaDelConjunto}` del registro de supresiones, en la misma forma canónica que verifica el E-commerce (PLA-38). No sella si una entrada no se puede interpretar. | `supresiones-sello` (crea en `supresiones-sellos`, lee `supresiones-entradas`) |
| `copiar-registro.sh` | día | Copia el registro a `supresiones-copia` como `AAAA-MM-DD/<hora>-<sha256>.tar`. | `supresiones-sello` (lee) y `respaldos-copia-registro` (crea) |
| `anclar-sello.sh` | semana | Envía el último sello por correo: el ancla contra la que se verifica el registro al restaurar. | ninguna (usa el último sello guardado) |
| `vigilar-retencion.sh` (con `alertar.sh`) | día | Avisa si el almacén retiene más de lo esperado (PLA-18). | `respaldos-vigilante` |
| `latido-externo.sh` | 5 min | Un pulso vacío a un servicio gratuito de vigilancia fuera de la nube: si VM-AUX cae, alguien lo sabe. | ninguna |

`alertar.sh <nombre> <comando>` manda el correo cuando el comando detecta algo, no repite la misma alerta durante `AUX_REAVISO_HORAS` (6) y vuelve a intentar si el correo no se pudo enviar. El correo sale de VM-AUX, no de la VM de datos.

## Instalación (la hace la persona responsable, o `desplegar.sh` de VM-AUX en T26)

1. Un usuario sin privilegios `babelhub-aux`, los scripts en `/opt/babelhub/scripts/vm-aux/` y `infra/vm-aux/babelhub-aux.cron` copiado a `/etc/cron.d/babelhub-aux`.
2. `/etc/babelhub-aux/aux.env` (0600, del usuario `babelhub-aux`, **fuera del repositorio**) con los nombres que describe `scripts/vm-aux/comun.sh`:
   - un remoto de rclone por credencial: `RCLONE_CONFIG_VIGILANTE_*`, `RCLONE_CONFIG_SELLO_*` y `RCLONE_CONFIG_COPIA_*` (`_TYPE=s3`, `_PROVIDER`, `_ENDPOINT`, `_REGION`, `_ACCESS_KEY_ID`, `_SECRET_ACCESS_KEY`);
   - el correo: `AUX_SMTP_URL`, `AUX_SMTP_USUARIO`, `AUX_SMTP_CLAVE`, `AUX_CORREO_DE`, `AUX_CORREO_PARA`;
   - `AUX_LATIDO_URL` (la dirección de pulso, un secreto) y, si se quiere, `AUX_ESTADO`, `AUX_REAVISO_HORAS`, `AUX_MAX_HORAS_RESPALDO`, `AUX_MAX_MINUTOS_ATRASO`.
3. Comprobar a mano: `scripts/vm-aux/comprobar-respaldos.sh` debe terminar en 0 con todo al día.

La credencial `cuota-lectura` (uso del almacén) la usa T40, no estos scripts.

## Lo que no se puede probar sin Oracle

Las pruebas usan un almacén de mentira que aplica la matriz de `infra/almacen/credenciales.txt` y un `curl` de mentira. Queda para el spike T02 y el ensayo: que la hora de `rclone lsf` contra Oracle sea la esperada (se fuerza `TZ=UTC`), que `OBJECT_CREATE` sin `OBJECT_OVERWRITE` rechace un sello repetido, y un correo real contra el servidor SMTP elegido.
