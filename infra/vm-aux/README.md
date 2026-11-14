# VM-AUX: vigilancia y sellos

VM-AUX (AMD micro) es el salto SSH hacia las demás VMs y, desde T25, quien **vigila desde fuera de la VM de datos**: si esa VM cae o su respaldo falla, nadie dentro de ella puede avisarlo. Sus scripts están en `scripts/vm-aux/` y no guardan ninguna clave privada ni credencial de borrado. El endurecimiento de la propia VM-AUX (acceso, correo propio, disco cifrado, alerta de ingreso SSH) está en la sección «Endurecimiento» (T26).

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
2. `/etc/babelhub-aux/aux.env` (**de root, grupo `babelhub-aux`, 0640**: el usuario lo lee pero no lo puede cambiar, porque la comprobación del endurecimiento lo carga como root; **fuera del repositorio**) con los nombres que describe `scripts/vm-aux/comun.sh` (los scripts lo leen **sin ejecutarlo** y solo aceptan esos nombres):
   - un remoto de rclone por credencial: `RCLONE_CONFIG_VIGILANTE_*`, `RCLONE_CONFIG_SELLO_*` y `RCLONE_CONFIG_COPIA_*` (`_TYPE=s3`, `_PROVIDER`, `_ENDPOINT`, `_REGION`, `_ACCESS_KEY_ID`, `_SECRET_ACCESS_KEY`);
   - el correo: `AUX_SMTP_URL`, `AUX_SMTP_USUARIO`, `AUX_SMTP_CLAVE`, `AUX_CORREO_DE`, `AUX_CORREO_PARA`;
   - `AUX_LATIDO_URL` (la dirección de pulso, un secreto) y, si se quiere, `AUX_ESTADO`, `AUX_REAVISO_HORAS`, `AUX_MAX_HORAS_RESPALDO`, `AUX_MAX_MINUTOS_ATRASO`.
3. Comprobar a mano: `scripts/vm-aux/comprobar-respaldos.sh` debe terminar en 0 con todo al día.

La credencial `cuota-lectura` (uso del almacén) la usa T40, no estos scripts.

## Lo que no se puede probar sin Oracle

Las pruebas usan un almacén de mentira que aplica la matriz de `infra/almacen/credenciales.txt` y un `curl` de mentira. Queda para el spike T02 y el ensayo (y, de T26, lo que dice la sección «Endurecimiento»): que la hora de `rclone lsf` contra Oracle sea la esperada (se fuerza `TZ=UTC`), que `OBJECT_CREATE` sin `OBJECT_OVERWRITE` rechace un sello repetido, y un correo real contra el servidor SMTP elegido.

## Endurecimiento (T26; PLA-20, AM29, D19)

VM-AUX es el salto SSH hacia las demás VMs, así que lo peor que le puede pasar es tener en ella una llave privada, un agente reenviado o una credencial de más. Se endurece así:

| Medida | Dónde | Cómo se comprueba |
| :--- | :--- | :--- |
| **Acceso por ProxyJump, sin llaves privadas en la VM.** La persona responsable llega a las otras VMs con `ssh -J salto@vm-aux usuario@vm-destino`; la llave privada está en su equipo. El usuario `salto` no tiene terminal ni comando: solo abre canales TCP hacia las direcciones privadas de las otras VMs, puerto 22. | `sshd-babelhub-aux.conf` (a `/etc/ssh/sshd_config.d/50-babelhub-aux.conf`) y `authorized_keys.salto.ejemplo` (la llave **pública** con `restrict,port-forwarding,permitopen=…`). | `verificar-endurecimiento.sh`: `sshd -T` del usuario del salto, búsqueda de llaves privadas (SSH, PuTTY y age) y revisión de `authorized_keys`. |
| **Sin reenvío de agente.** Ni `AllowAgentForwarding` en sshd, ni `agent-forwarding` en las llaves, ni `ForwardAgent yes` en ningún cliente, ni un `ssh-agent` corriendo. | Ídem. | Ídem. |
| **Credencial de correo propia y mínima.** `AUX_SMTP_*` solo sirve para enviar los avisos de VM-AUX; el archivo de entorno (0600) no puede tener variables de otras VMs ni credenciales que escriban, lean respaldos o borren. | `/etc/babelhub-aux/aux.env`. | Ídem: rechaza por **nombre** cualquier variable que no sea `AUX_*` o un remoto `VIGILANTE`, `SELLO`, `COPIA` o `CUOTA`; nunca imprime un valor. |
| **Disco cifrado.** El cifrado en reposo de los volúmenes de Oracle no se puede desactivar y no se ve desde dentro de la VM: se pregunta al proveedor, desde el equipo de la persona responsable. | — | `scripts/verificar-cifrado-de-disco.sh --instancia <ocid> --compartimento <ocid> --dominio <AD>`: el volumen de arranque y los de bloque deben existir, estar `AVAILABLE` y informar su clave (`clave-de-oracle` o `clave-del-cliente`). T38 lo reutiliza para las otras dos VMs. |
| **Alerta de ingreso SSH.** Cada sesión que se abre (también el salto) manda un correo con usuario, origen y hora. | Una línea en `/etc/pam.d/sshd` (y `UsePAM yes`, que el verificador exige): `session optional pam_exec.so quiet /opt/babelhub/scripts/vm-aux/avisar-ingreso-ssh.sh`. Es `optional` y el script siempre termina en 0: un fallo del correo no impide entrar a corregir VM-AUX (queda en `ingresos.log`). | `avisar-ingreso-ssh.sh` (probado con PAM simulado). |
| **Procesos nuevos.** Un nombre de proceso fuera de `procesos-permitidos.txt` avisa por correo (cada 5 minutos, sin repetir la misma alerta). Es una heurística: complementa al ingreso SSH, a los sellos y al latido externo. | `procesos-permitidos.txt` (se ajusta con `ps -eo comm= \| sort -u` en una VM sana). | `vigilar-procesos.sh`. |
| **Lo que corre como root no lo cambia otro.** `verificar-endurecimiento.sh` corre **como root** (lee sshd y todas las carpetas; sin privilegios daría un «todo bien» falso), desde su propio archivo de cron. | `babelhub-aux-endurecimiento.cron` (a `/etc/cron.d/babelhub-aux-endurecimiento`). `/opt/babelhub/scripts/vm-aux/` debe ser de root y no escribible por otros. | La propia comprobación vigila el dueño y los permisos de esos scripts. |

Instalación de lo nuevo (la hace la persona responsable): copiar `sshd-babelhub-aux.conf`, validar con `sshd -t` y recargar sshd; crear el usuario `salto` con su `authorized_keys`; añadir la línea de PAM; copiar `babelhub-aux-endurecimiento.cron`; y ejecutar `sudo scripts/vm-aux/verificar-endurecimiento.sh` (debe terminar en 0). Riesgo residual (en `progress/hallazgos.md` por el plan): una VM-AUX con root comprometido puede suprimir sus propios avisos o falsear sellos; lo mitigan el latido externo, las credenciales mínimas y las anclas del sello por correo.
