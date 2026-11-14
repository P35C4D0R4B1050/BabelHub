# Infraestructura

| Ruta | Estado | Contenido |
| :--- | :--- | :--- |
| `docker-compose.local.yml` | Disponible | Una instancia PostgreSQL con una base y un rol por servicio (`ecommerce` e `inventario`) y Redis para desarrollo local. Los servicios .NET y la Web se suman con sus Dockerfiles. |
| `postgres/init/` | Disponible | Crea las bases y los roles la primera vez que arranca el volumen (AD03). |
| `postgres/Dockerfile` | Disponible | PostgreSQL con `age` y `rclone` (cliente S3) para el archivado de cambios y el respaldo (D06, D10), multi-arquitectura y sin privilegios. |
| `almacen/` | Disponible | Contenedores, credenciales y retención con bloqueo del almacén de objetos de Oracle (PLA-16, PLA-35, PLA-38): `contenedores.txt`, `credenciales.txt`, `ciclo-de-vida.json` y `politicas.md`. Se aplican y se comprueban con `scripts/almacen.sh`, en el equipo de la persona responsable. |
| `postgres/respaldos/`, `respaldos/` | Disponible | Los scripts de archivado de cambios y respaldo diario cifrado con `age` que van dentro de la imagen de PostgreSQL (`archivar-wal.sh`, `respaldar.sh`) y cómo se usan y programan (`respaldos/README.md`); el respaldo bajo demanda es `scripts/respaldar-ahora.sh`. |
| `secretos-requeridos.txt` | Disponible | Los nombres de las claves obligatorias de cada archivo de secretos de una VM (sin valores). Lo lee `scripts/desplegar.sh <vm>`, que despliega una VM con un solo comando y no continúa si falta algo (PLA-04). |
| `scripts/vm-aux/` | En curso | Scripts de VM-AUX (credenciales mínimas, desde fuera de la VM de datos): `vigilar-retencion.sh` alerta si el almacén retiene más de lo esperado. Las comprobaciones de respaldo y atraso, los sellos y el correo llegan con T25 y T26. |
| `excepciones-de-arquitectura.json` | Disponible | Registro de las imágenes de terceros que no existen para una arquitectura en la que corren (PLA-03); hoy vacío. |
| `nginx/nginx.conf` | Plantilla | Enrutamiento por host (tienda y gestión) con límites por IP y servidor de respaldo para la gestión (AD14). |
| `n8n/` | Pendiente | Flujos de n8n exportados (AD16), con la spec `transversal-notificaciones`. |
| `docker-compose.borde.yml`, `docker-compose.ventas.yml`, `docker-compose.inventario.yml` | Disponible | Un Compose por VM de Oracle (PLA-26, PLA-27): versiones y `digest` fijos, usuario sin privilegios, capacidades descartadas, límites, `healthcheck`, `read_only` (con las excepciones de Evolution API registradas en `x-excepciones`) y solo los secretos de esa VM, desde `BABELHUB_SECRETOS`. Las imágenes propias llegan por `IMAGEN_<SERVICIO>` con su `digest` (las fija el flujo de lanzamiento). VM-AUX no usa Compose: sus scripts llegan con T24. |

Reglas (ver `docs/02-arquitectura-de-software/02-decisiones-arquitectonicas.md` y `docs/07-proceso-de-desarrollo/02-convenciones-tecnicas.md`): imágenes multi-arquitectura, usuario sin privilegios, `healthcheck`, límites de memoria y puertos de bases de datos solo en `127.0.0.1` en local. Los secretos viven en `.env` (ignorado por Git); se versiona `.env.example`.

```bash
cp .env.example .env
docker compose -f infra/docker-compose.local.yml --env-file .env up -d
```
