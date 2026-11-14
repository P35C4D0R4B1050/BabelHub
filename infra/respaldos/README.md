# Respaldo y archivado de cambios

Cómo se respalda la base de VM-INVENTARIO (PLA-09, PLA-15, PLA-16, PLA-17; decisiones D07, D09, D10 y D24 de `specs/transversal-plataforma/`). Los scripts van **dentro de la imagen de PostgreSQL** (`infra/postgres/respaldos/`, POSIX sh porque Alpine no trae bash) y corren en el contenedor, con el material de cifrado y la credencial del almacén que ese contenedor ya tiene.

## Qué se hace

| Pieza | Qué hace |
| :--- | :--- |
| `archivar-wal.sh %p %f` | Es el `archive_command`. Cifra cada segmento de WAL con la clave pública de `age` y lo sube a `respaldos-wal` como `<segmento>-<sha256 del cifrado>.age`. Si falla, PostgreSQL lo repite y mientras tanto conserva el segmento en `pg_wal` (el archivo de cambios pendiente, protegido por el disco cifrado). |
| `respaldar.sh` | Respaldo diario: `pg_basebackup` toma todo el clúster (las dos bases de negocio y las demás, en un mismo instante), cifra al vuelo con `age` (el claro no toca el disco) y sube dos objetos a `respaldos-diarios`: `AAAA-MM-DD/<hora UTC>-<sha256>.tar.age` (el clúster) y `AAAA-MM-DD/<hora>-config-<sha256>.tar.age` (la configuración de la VM, sin `*.env`, `*.pem`, `*.key` ni `*.pfx`). El domingo deja además los mismos objetos en `respaldos-semanales` y el último día del mes en `respaldos-mensuales` (días UTC). Si algo falla, termina con 1 y no deja nada local. |
| `respaldar.sh --bajo-demanda` | Un respaldo adicional tras una corrección urgente con migración: solo a `respaldos-diarios`, con la misma retención, sin copias semanales ni mensuales. |
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

El respaldo diario lo lanza el cron del anfitrión (UTC); el archivado lo lanza PostgreSQL solo:

```bash
30 2 * * * docker exec babelhub-inventario-postgres-1 /opt/babelhub/respaldar.sh >> /var/log/babelhub-respaldo.log 2>&1
```

El archivado necesita actividad para generar segmentos: `archive_timeout=300` más el latido de la base (una fila por minuto, T16) garantizan un segmento nuevo cada cinco minutos aunque nadie use el sistema, de modo que el atraso solo supera los 15 minutos si algo falla (PLA-15). Quien mide el atraso y la antigüedad del último respaldo, y avisa por correo (PLA-20), es VM-AUX (T24).

## Lo que queda fuera de esta tarea

- La clave privada de `age`, su copia fuera de línea y el registro de su custodia (`custodia.md`): los entrega T27/T28.
- Vigilar el espacio local reservado (4 GB) y declarar roto el archivado si se agota: T26.
- Restaurar: `scripts/restaurar.sh` (T29) elige entre candidatos por continuidad de línea de tiempo y LSN, y extrae el `tar` sin rutas absolutas ni propietarios.
- Compresión: el respaldo no se comprime (el nombre es `.tar.age`); si la medición de cuota (T03) la exige, se añade aquí.
