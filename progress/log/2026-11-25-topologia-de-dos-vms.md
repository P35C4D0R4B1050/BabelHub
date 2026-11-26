# 2026-11-25 — Topología de dos VMs (carril `dos-vms-y-r2`, entrega 1)

## Hecho

- Compose de ventas con Nginx y `cloudflared` (redes `tunel` y `entrada`), Compose del borde eliminado, `nginx.conf`, `desplegar.sh`, generador de secretos y su tabla, ensayo local, pruebas, `respaldar-ahora.sh`, documentación y guía 08 (DD-100 a DD-103).

## Problemas y cómo se resolvieron

| Problema | Resolución |
| :--- | :--- |
| Un cliente podía falsificar su IP por el segundo conector (en `main`). | Un solo conector y confianza por dirección fija en redes propias. |
| `gestion.<dominio>` iba al proceso de la tienda y recibía 400 (en `main`). | La gestión va solo a la réplica. |
| Con una IP fija en el E-commerce, `docker compose run` fallaba («Address already in use»). | Fijas solo en `cloudflared` y Nginx, con `ip_range` para las dinámicas. |
| Recrear el E-commerce podía dejar a Nginx con la dirección antigua. | `server ecommerce:8080 resolve` con el DNS de Docker. |

## Revisiones

Arquitectura y seguridad (agentes en contexto aislado): «No cumple» en la primera pasada (un Bloqueante en la guía y varios Importantes). Corregidos: orden de la guía, referencias a la topología anterior en `docs/`, regresión de confianza de la réplica de gestión (DD-104), contenedor del registro de supresiones (heredado), declaración de que no hay respaldos y avisos de la guía. Quedan abiertos en `progress/hallazgos.md` los que son decisión de la persona.

Segunda pasada de seguridad sobre las correcciones: «Cumple con observaciones», cinco Menores, corregidos (control positivo de la regla y de los puertos en la guía, raíz del repositorio en la comprobación de dueño, gestión cerrada a lo que no venga del túnel y aviso de datos reales sin respaldo en `Staging`).

## Qué se probó

- `dotnet build -warnaserror`, SpecCheck, unitarias de los dos servicios (5764 y 2454), arquitectura (261) e integración afectada (685 de 685), y de nuevo las de la entrada tras los retoques finales.
- `scripts/ensayo-local.sh` completo (pasos 1 a 7, con el 6c): las dos «VMs» en Docker Desktop con las imágenes construidas localmente.
- Escáner de secretos del repositorio.

## No probado

- La regla `DOCKER-USER` contra un Docker real (solo con dobles): comprobación en la VM, guía §13.2.
- Cortafuegos `iptables` de Oracle, `cloudflared` real, Cloudflare, el dominio, CPU y memoria en la VM ARM.
- Un contenedor hacia un puerto publicado en la IP privada de su propia VM: el diseño ya no lo necesita; la comprobación opcional queda en la guía §13.2.
