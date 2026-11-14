# Respaldo y archivado de cambios

Cómo se respalda la base de VM-INVENTARIO (PLA-09, PLA-15, PLA-16, PLA-17; decisiones D07, D09, D10 y D24 de `specs/transversal-plataforma/`). Los scripts van **dentro de la imagen de PostgreSQL** (`infra/postgres/respaldos/`, POSIX sh porque Alpine no trae bash) y corren en el contenedor, con el material de cifrado y la credencial del almacén que ese contenedor ya tiene.

## Qué se hace

| Pieza | Qué hace |
| :--- | :--- |
| `archivar-wal.sh %p %f` | Es el `archive_command`. Cifra cada segmento de WAL con la clave pública de `age` y lo sube a `respaldos-wal` como `<segmento>-<sha256 del cifrado>.age`. Si falla, PostgreSQL lo repite y mientras tanto conserva el segmento en `pg_wal` (el archivo de cambios pendiente, protegido por el disco cifrado). |
| `respaldar.sh` | Respaldo diario: `pg_basebackup` toma todo el clúster (las dos bases de negocio y las demás, en un mismo instante), cifra al vuelo con `age` (el claro no toca el disco) y sube dos objetos a `respaldos-diarios`: `AAAA-MM-DD/<hora UTC>-<sha256>.tar.age` (el clúster) y `AAAA-MM-DD/<hora>-config-<sha256>.tar.age` (la configuración de la VM, sin `*.env`, `*.pem`, `*.key` ni `*.pfx`). El domingo deja además los mismos objetos en `respaldos-semanales` y el último día del mes en `respaldos-mensuales` (días UTC). Si algo falla, termina con 1 y no deja nada local. |
| `respaldar.sh --bajo-demanda` | Un respaldo adicional tras una corrección urgente con migración: solo a `respaldos-diarios`, con la misma retención, sin copias semanales ni mensuales. |
| `vigilar-espacio.sh` | Cada minuto: mide lo pendiente contra el espacio reservado, avisa al 80 % y declara roto el archivo de cambios al agotarse (ver más abajo). |
| `scripts/respaldar-ahora.sh` | Lanza el anterior desde el equipo de la persona responsable, por `ProxyJump` a través de VM-AUX y sin reenvío de agente. No lleva ni recibe secretos. |

La retención no la ejecuta ningún script: la hace el ciclo de vida del propio almacén, con retención bloqueada (ver [`../almacen/politicas.md`](../almacen/politicas.md)). Por eso la credencial de esta VM, `respaldos-escritor`, solo puede **crear** objetos: no lee, no lista, no sobrescribe y no borra.

## Variables del contenedor de PostgreSQL

Van en `postgres.env`, en la carpeta de secretos de VM-INVENTARIO (`BABELHUB_SECRETOS`), nunca en el repositorio.

| Variable | Valor |
| :--- | :--- |
| `BABELHUB_AGE_DESTINATARIO` | La clave **pública** de `age` (`age1…`). La que descifra **no** puede estar en esta VM: si una clave privada aparece en el entorno, los scripts se niegan a continuar. |
| `RCLONE_CONFIG_ALMACEN_TYPE` | `s3` |
| `RCLONE_CONFIG_ALMACEN_PROVIDER`, `_ENDPOINT`, `_REGION` | El almacén compatible con S3 de Oracle (`https://<espacio>.compat.objectstorage.<región>.oraclecloud.com`). |
| `RCLONE_CONFIG_ALMACEN_ACCESS_KEY_ID`, `_SECRET_ACCESS_KEY` | La clave de cliente de la credencial `respaldos-escritor`. |

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

## Lo que queda fuera de esta tarea

- La clave privada de `age`, su copia fuera de línea y el registro de su custodia (`custodia.md`): los entrega T27/T28.
- (Hecho en T27) Vigilar el espacio local reservado y declarar roto el archivado si se agota: ver la sección anterior.
- Restaurar: `scripts/restaurar.sh` (T29) elige entre candidatos por continuidad de línea de tiempo y LSN, y extrae el `tar` sin rutas absolutas ni propietarios.
- Compresión: el respaldo no se comprime (el nombre es `.tar.age`); si la medición de cuota (T03) la exige, se añade aquí.
