# Respaldo y archivado de cambios

Cómo se respalda la base de VM-INVENTARIO (PLA-09, PLA-15, PLA-16, PLA-17; decisiones D07, D09, D10 y D24 a D28 de `specs/transversal-plataforma/`). Los respaldos van a **Cloudflare R2** (provisional, D26), **comprimidos con `zstd` y después cifrados** con `age` (D25). Los scripts van **dentro de la imagen de PostgreSQL** (`infra/postgres/respaldos/`, POSIX sh porque Alpine no trae bash) y corren en el contenedor, con el material de cifrado y la credencial del almacén que ese contenedor ya tiene.

## Qué se hace

| Pieza | Qué hace |
| :--- | :--- |
| `archivar-wal.sh %p %f` | Es el `archive_command`. Comprime cada segmento de WAL, lo cifra con la clave pública de `age` y lo sube a `respaldos-wal` como `<segmento>-<sha256 del cifrado>.zst.age`. Si falla la compresión, el cifrado o la subida, PostgreSQL lo repite y mientras tanto conserva el segmento en `pg_wal` (el archivo de cambios pendiente, protegido por el disco cifrado). |
| `respaldar.sh` | Respaldo diario: `pg_basebackup` toma todo el clúster (las dos bases de negocio y las demás, en un mismo instante), comprime y cifra al vuelo (el claro no toca el disco) y sube dos objetos a `respaldos-diarios`: `AAAA-MM-DD/<hora UTC>-<sha256>.tar.zst.age` (el clúster) y `AAAA-MM-DD/<hora>-config-<sha256>.tar.zst.age` (la configuración de la VM, sin `*.env`, `*.pem`, `*.key` ni `*.pfx`). El domingo deja además los mismos objetos en `respaldos-semanales` y el último día del mes en `respaldos-mensuales` (días UTC). Si algo falla, termina con 1 y no deja nada local. |
| `respaldar.sh --bajo-demanda` | Un respaldo adicional tras una corrección urgente con migración: solo a `respaldos-diarios`, con la misma retención, sin copias semanales ni mensuales. |
| `vigilar-espacio.sh` | Cada minuto: mide lo pendiente contra el espacio reservado, avisa al 80 % y declara roto el archivo de cambios al agotarse (ver más abajo). |
| `scripts/respaldar-ahora.sh` | Lanza el anterior desde el equipo de la persona responsable, por SSH directo (o por `ProxyJump` con `--salto` cuando exista VM-AUX) y sin reenvío de agente. No lleva ni recibe secretos. |

La retención no la ejecuta ningún script: la hacen el bloqueo y el ciclo de vida de R2 (`../almacen/r2/`, aplicados con `scripts/almacen.sh r2-aplicar`): diarios 7 días, semanales 28, mensuales 92 y archivo de cambios 7. La credencial de esta VM, `respaldos-escritor`, es un token de objetos limitado a esos cuatro contenedores: **R2 no ofrece «solo crear»**, así que puede leer y listar (lo leído va cifrado) y subir objetos nuevos; el bloqueo impide que borre o sobrescriba dentro del plazo y no puede cambiar las reglas. Quien administre la cuenta de Cloudflare sí puede quitar el bloqueo (riesgo aceptado, spec §5).

## Variables del contenedor de PostgreSQL

Van en `postgres.env`, en la carpeta de secretos de VM-INVENTARIO (`BABELHUB_SECRETOS`), nunca en el repositorio.

| Variable | Valor |
| :--- | :--- |
| `BABELHUB_AGE_DESTINATARIO` | La clave **pública** de `age` (`age1…`). La que descifra **no** puede estar en esta VM: si una clave privada aparece en el entorno, los scripts se niegan a continuar. |
| `RCLONE_CONFIG_ALMACEN_TYPE` | `s3` |
| `RCLONE_CONFIG_ALMACEN_PROVIDER`, `_ENDPOINT`, `_REGION` | `Cloudflare`, `https://<account-id>.r2.cloudflarestorage.com` y `auto`. |
| `RCLONE_CONFIG_ALMACEN_ACCESS_KEY_ID`, `_SECRET_ACCESS_KEY` | La clave del token de objetos `respaldos-escritor`. |

`BABELHUB_ESPERA` (por defecto `/var/respaldos`, el volumen `respaldo-espera`) y `BABELHUB_CONFIG` (por defecto `/config`, la carpeta `infra` de la VM en solo lectura) rara vez se cambian.

## Programación

El respaldo diario lo lanza el cron del anfitrión (UTC), que instala `scripts/desplegar.sh inventario` (`/etc/cron.d/babelhub-respaldo`); el archivado lo lanza PostgreSQL solo. La entrada es:

```bash
30 2 * * * docker exec babelhub-inventario-postgres-1 /opt/babelhub/respaldar.sh >> /var/log/babelhub-respaldo.log 2>&1
```

El archivado necesita actividad para generar segmentos: `archive_timeout=300` más el latido de la base (una fila por minuto, T16) garantizan un segmento nuevo cada cinco minutos aunque nadie use el sistema, de modo que el atraso solo supera los 15 minutos si algo falla (PLA-15). Quien mide el atraso y la antigüedad del último respaldo, y avisa por correo (PLA-20), es VM-AUX (T24).

## Espacio local reservado y archivo de cambios roto (T27; PLA-21, D11)

Si el almacén no está disponible, la base **sigue operando** (DA03) y lo que no se pudo subir se acumula en la VM: los segmentos de WAL que esperan su archivado (PostgreSQL los conserva en `pg_wal`) y lo cifrado que espera su subida (`/var/respaldos`). Ese espacio está **reservado y es menor de 5 GB**: 4 GiB por defecto (`BABELHUB_ESPACIO_RESERVADO`, en bytes, en `postgres.env`; los scripts rechazan 5 000 000 000 o más).

`vigilar-espacio.sh` corre **cada minuto** desde el cron del anfitrión (lo instala `scripts/desplegar.sh inventario`, junto con el respaldo diario) y mide lo pendiente contra la reserva:

| Uso de la reserva | Qué pasa |
| :--- | :--- |
| Menos del 80 % | `ok …`; nada que hacer. |
| 80 % o más | `ALERTA espacio reservado… al NN %`: llega un correo a la persona responsable (no más de uno cada 6 horas por el mismo texto). La base sigue operando. |
| 100 % (agotado) | **Se declara roto el archivo de cambios**: se escribe la marca `archivo-roto` en `/var/respaldos`, `archivar-wal.sh` pasa a **descartar** los segmentos (PostgreSQL los recicla y el disco no se llena) y llega la alerta. Sin archivo de cambios no hay recuperación a un instante intermedio: el RPO pasa de 15 minutos a 24 horas hasta que haya un respaldo completo nuevo. |
| Con la marca puesta | Cada minuto se repite la alerta («se exige un respaldo completo nuevo») hasta que **`respaldar.sh` suba un respaldo completo que termine bien**: es lo único que retira la marca (el respaldo diario de las 02:30 lo hace solo si el almacén ya responde; también `scripts/respaldar-ahora.sh`). Si ese respaldo falla, la marca vuelve a su lugar. |

**Quién manda el correo.** El contenedor de PostgreSQL no tiene cliente de correo y VM-AUX no puede ver el espacio local de la VM de datos (sin llaves privadas ni acceso entrante), así que el aviso sale **desde la propia VM de datos**: el cron envuelve la vigilancia con `scripts/vm-aux/alertar.sh` (el mismo envoltorio de VM-AUX: TLS obligatorio, sin repetir la misma alerta, reintento si el correo falla) y una credencial de correo **propia y mínima de esta VM**, `aviso.env` en la carpeta de secretos (`AUX_SMTP_URL`, `AUX_SMTP_USUARIO`, `AUX_SMTP_CLAVE`, `AUX_CORREO_DE`, `AUX_CORREO_PARA`; 0600). `desplegar.sh inventario` no despliega sin ese archivo: sin él el aviso del 80 % no saldría. Esa credencial solo envía los avisos de esta VM (una VM comprometida podría mandar avisos falsos, no leer ni borrar nada). Por separado, VM-AUX sigue avisando si el archivado lleva más de 15 minutos de atraso (T25): ese aviso sale **fuera** de la VM de datos.

El tamaño de 4 GiB lo valida el spike T03 (cuota del almacén) y T04 (memoria y disco de VM-INVENTARIO); los 80 % y 100 % no cambian.

## Custodia de la clave y restauración de ensayo (T28; PLA-17, AC-PLA-17)

La clave que **descifra** vive fuera de la VM, del repositorio y del contenedor de respaldos; su custodia está registrada en [`custodia.md`](custodia.md) (titular, destinatario público, **segunda copia obligatoria** en otro lugar, fecha de la última prueba de descifrado y su periodicidad, y el procedimiento de rotación). Los campos están en `pendiente` hasta que la persona responsable genere la clave: así el registro sin llenar **no pasa** `scripts/comprobar-custodia.sh`.

| Script (en el equipo de la persona responsable o en la máquina de ensayo, nunca en una VM) | Qué hace |
| :--- | :--- |
| `scripts/comprobar-custodia.sh [--entorno postgres.env \| --destinatario age1…]` | Falla si algún campo está `pendiente`, si la segunda copia está en el mismo lugar que la primera, si la prueba de descifrado venció (o es futura o inválida), si el archivo de custodia contiene una clave privada, y si **el destinatario con el que la VM de datos cifra no es el registrado** (los respaldos no se podrían descifrar con la copia registrada). Solo lee. |
| `scripts/descifrar-respaldo.sh --entrada <respaldo>.zst.age < clave` | Descifra con la clave por la **entrada estándar** (solo en memoria) y **solo si corresponde al destinatario registrado** (si no, sale con 3 sin descifrar nada). Lo que sale sigue comprimido. `--probar --registrar` descifra a la nada y anota la fecha de hoy en la custodia (la prueba periódica). |
| `scripts/extraer-respaldo.sh --destino <carpeta-nueva>` | Recibe el `tar` (comprimido o no): lo descomprime con la memoria limitada a 128 MiB y la salida acotada (`--tope-bytes`, y nunca más de la mitad del espacio libre) y lo **valida antes de extraer**: rechaza rutas absolutas, `..`, enlaces duros, archivos especiales y enlaces simbólicos con destino absoluto o con `..`. Extrae **sin propietarios**. **Sanea** lo que trae el respaldo: vacía `postgresql.auto.conf`, **reemplaza** `postgresql.conf` por uno generado, borra `postmaster.pid` y las señales de réplica y de recuperación, y **regenera `pg_hba.conf`** (solo el socket local de `postgres` y TCP desde 127.0.0.1 con contraseña). |
| `scripts/descifrar-wal.sh --entrada <carpeta> --destino <carpeta-nueva> --desde <segmento> [--hasta <segmento>] [--restaurada <carpeta>] < clave` | Prepara el archivo de cambios (D28): descifra y descomprime los segmentos de **una línea de tiempo**, la de `--desde`, seguidos. **No elige entre candidatos**: ante un hueco, ningún candidato válido o dos distintos se detiene antes de ese segmento (código 4) y el archivo se trata como incompleto. La historia propia la toma de la copia extraída (`--restaurada`); de las otras líneas solo deja un archivo vacío. Con `--sin-cambios` (sin clave) hace solo eso. |

### Restauración manual (mientras `restaurar.sh` no exista)

En el equipo de la persona responsable o en la máquina de ensayo, **todo dentro de un volumen cifrado** (`<v>`) y con las claves de `respaldos-lector`. La copia se elige **por el nombre anotado** en la comprobación diaria (guía §14), nunca «la más reciente»: quien tenga la VM de datos puede subir copias falsas.

```bash
set -o pipefail
rclone copy "lector:respaldos-diarios/<fecha>/<nombre anotado>" <v>/copia/
sha256sum <v>/copia/*.tar.zst.age          # DEBE coincidir con la suma que lleva el nombre; si no, para
scripts/descifrar-respaldo.sh --entrada <v>/copia/<archivo> < /ruta/al/dispositivo/clave-respaldos.txt | scripts/extraer-respaldo.sh --destino <v>/restaurada
grep 'START WAL LOCATION' <v>/restaurada/backup_label          # el segmento que va en --desde
rclone copy lector:respaldos-wal <v>/objetos/
```

Elige **una** de las dos vías. Las dos dejan el clúster en una **línea de tiempo nueva**. Si cambias de vía, borra antes `<v>/cambios` (el script solo escribe en una carpeta nueva).

```bash
# A) Con el archivo de cambios (llega hasta un instante intermedio o hasta el final):
scripts/descifrar-wal.sh --entrada <v>/objetos --destino <v>/cambios --desde <segmento> --restaurada <v>/restaurada < /ruta/al/dispositivo/clave-respaldos.txt
# B) Sin el archivo de cambios (queda en el instante de la copia):
scripts/descifrar-wal.sh --entrada <v>/objetos --destino <v>/cambios --desde <segmento> --sin-cambios
```

Al final de `<v>/restaurada/postgresql.auto.conf` va **solo el bloque de tu caso**; después se crea el archivo vacío `<v>/restaurada/recovery.signal`.

A hasta un instante (cambia la fecha y la hora por las tuyas, en UTC):

```ini
restore_command = 'cp <v>/cambios/%f %p'
recovery_target_timeline = 'current'
recovery_target_time = '<AAAA-MM-DD HH:MM:SS+00>'
recovery_target_action = 'promote'
```

A hasta el final del archivo:

```ini
restore_command = 'cp <v>/cambios/%f %p'
recovery_target_timeline = 'current'
```

B, solo la copia:

```ini
restore_command = 'cp <v>/cambios/%f %p'
recovery_target_timeline = 'current'
recovery_target = 'immediate'
recovery_target_action = 'promote'
```

`restore_command` lo ejecuta PostgreSQL: la ruta es **la que él ve** (si corre en un contenedor, la montada dentro), y `restaurada` y `cambios` deben ser del usuario con que corre (`chown -R postgres <v>/restaurada <v>/cambios`, o el UID del contenedor). Si no puede leer `cambios`, PostgreSQL recurre a su propia carpeta y **termina en el momento de la copia como si todo hubiera ido bien**: antes de dar nada por bueno, comprueba en su registro las líneas `restored log file`: en «A hasta el final», hasta **el último segmento que imprimió el script**; en «A hasta un instante», que las haya y que aparezca la parada en el objetivo (`recovery stopping before …`; si no llega al instante, PostgreSQL falla). En **las dos vías**, el número de la línea `selected new timeline ID` del registro debe ser **mayor** que el de todos los archivos `.history` de `<v>/cambios`: si no lo es, PostgreSQL no leyó la carpeta y reutilizó un número.

Si `descifrar-wal.sh` sale con código 4, el archivo está incompleto o alterado desde ese segmento: se puede recuperar hasta lo anterior o quedarse en la copia (vía B), y se anota. Al terminar (y solo tras esa comprobación): se borran `<v>/cambios` y `<v>/objetos`, se rota `respaldos-escritor` y se toma enseguida un respaldo completo (D28).

`scripts/restaurar.sh` (T29) y el ensayo aislado (T31) usan estas mismas piezas; lo que ellos añaden (negativa a correr con un agente o un proceso de desarrollo con acceso, volumen cifrado efímero, apertura en orden) no está aquí.

## Lo que queda fuera de esta tarea

- (Hecho en T28) La clave privada de `age`, su copia fuera de línea y el registro de su custodia: la genera y la guarda la persona responsable (`custodia.md`).
- (Hecho en T27) Vigilar el espacio local reservado y declarar roto el archivado si se agota: ver la sección anterior.
- Restaurar de principio a fin: `scripts/restaurar.sh` (T29), pendiente.

**Restauración y ensayo con el E-commerce (web-informacion-del-comercio).** El E-commerce que se abre sobre una base restaurada o en un ensayo arranca como producción: reutiliza el mismo `ecommerce.env` (o `ecommerce-gestion.env`) y por eso necesita `BABELHUB__Contacto__Telefono`, `…Correo` y `…Direccion` (no son secretos); sin ellos se niega a arrancar y el error nombra la variable. Ver `infra/README.md`.
