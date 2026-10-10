# Infraestructura

| Ruta | Estado | Contenido |
| :--- | :--- | :--- |
| `docker-compose.local.yml` | Disponible | PostgreSQL (Ventas e Inventario) y Redis para desarrollo local. Los servicios .NET y la Web se suman con sus Dockerfiles. |
| `nginx/nginx.conf` | Plantilla | Enrutamiento interno hacia la Web y las API (AD14). |
| `n8n/` | Pendiente | Flujos de n8n exportados (AD16), con la spec `transversal-notificaciones`. |
| `docker-compose.oracle.yml` | Pendiente | Despliegue en las VMs de Oracle (VM-APP y VM-DATOS), con la fase de despliegue. |

Reglas (ver `docs/05` y `docs/09`): imágenes multi-arquitectura, usuario sin privilegios, `healthcheck`, límites de memoria y puertos de bases de datos solo en `127.0.0.1` en local. Los secretos viven en `.env` (ignorado por Git); se versiona `.env.example`.

```bash
cp .env.example .env
docker compose -f infra/docker-compose.local.yml --env-file .env up -d
```
