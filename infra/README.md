# Infraestructura

| Ruta | Estado | Contenido |
| :--- | :--- | :--- |
| `docker-compose.local.yml` | Disponible | Una instancia PostgreSQL con una base y un rol por servicio (`ecommerce`, `inventario`, `identidad`) y Redis para desarrollo local. Los servicios .NET y la Web se suman con sus Dockerfiles. |
| `postgres/init/` | Disponible | Crea las bases y los roles la primera vez que arranca el volumen (AD03, AD18). |
| `nginx/nginx.conf` | Plantilla | Enrutamiento interno hacia la Web y las API (AD14). |
| `n8n/` | Pendiente | Flujos de n8n exportados (AD16), con la spec `transversal-notificaciones`. |
| `docker-compose.oracle.yml` | Pendiente | Despliegue en las VMs de Oracle (un Compose por VM del perfil objetivo y del mínimo), con la fase de despliegue. |

Reglas (ver `docs/02-arquitectura-de-software/02-decisiones-arquitectonicas.md` y `docs/07-proceso-de-desarrollo/02-convenciones-tecnicas.md`): imágenes multi-arquitectura, usuario sin privilegios, `healthcheck`, límites de memoria y puertos de bases de datos solo en `127.0.0.1` en local. Los secretos viven en `.env` (ignorado por Git); se versiona `.env.example`.

```bash
cp .env.example .env
docker compose -f infra/docker-compose.local.yml --env-file .env up -d
```
