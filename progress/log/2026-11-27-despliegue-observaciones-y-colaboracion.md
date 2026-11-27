# 2026-11-27 — Primer despliegue, observaciones de la persona y colaboración en la interfaz

**Sesión coordinadora.** Reúne lo publicado, lo informado por las sesiones de trabajo y lo que sigue abierto. Lo marcado «según la guía» lo informó la sesión que acompaña el montaje y no se comprobó desde la coordinación.

## Qué se hizo

- Etiqueta `v0.1.1` sobre `7f5580f2`: flujo `lanzamiento` en verde tras las dos aprobaciones de la persona; las tres imágenes se descargan sin iniciar sesión.
- Guía 08 §5: las dos aprobaciones y la etiqueta de `--source-ref` (`37ced2df`, PR 30).
- Según la guía: dos VMs desplegadas y sanas, túnel de Cloudflare, Access en la gestión, solo el puerto 22 abierto desde Internet, primer respaldo y archivo de cambios en R2 (restauración sin probar), correo de seguridad real recibido, catálogo cargado y carga de stock en marcha.
- Cambios hechos a mano en las VMs sobre `v0.1.1` (según la guía): webhook de n8n en `ecommerce-gestion.env` y `tmpfs` de n8n en `infra/docker-compose.ventas.yml`.
- **Operación manual en la base de datos (según la guía, hecha por la persona):** la única cuenta de administración había perdido su segundo factor y la aplicación no ofrece vía de recuperación. Tras un respaldo bajo demanda, borró en la base `ecommerce` su segundo factor, códigos y desafíos y revocó sus sesiones; después ingresó a la gestión. **No queda en la auditoría de la aplicación; esta línea es su registro.**
- Colaboración en la interfaz con Codex: `docs/07-proceso-de-desarrollo/10-colaboracion-ui.md`, `progress/coordinacion-ui.md` y `scripts/guardia-ui.sh` (PR 31).

## Observaciones de la persona sobre la tienda desplegada

El detalle y su estado vigente están en `progress/coordinacion-ui.md`.

- Contraseñas (ver, comprobar coincidencia, orientar), QR del segundo factor y acceso del personal desde la tienda: en la rama de acceso.
- No se podía comprar: la ficha no tenía botón. Corregido en su rama, sin publicar.
- Raíz de la gestión con error 404, títulos gigantes en los paneles, menú de los paneles, animaciones y selección de sucursal: abiertos.

## Decisiones

- Contraseñas: **se orienta, no se obliga**. IDE-15 sigue sin reglas de composición; una barra de rojo a verde recomienda mayúsculas, minúsculas, números y símbolos.
- Codex trabaja solo la interfaz y su documentación; reglas de negocio, base de datos y el resto quedan fuera. `AGENTS.md` no cambia.
- La referencia visual vigente es `prototype/ui-v2/`. La primera versión (`prototype/ui/`) está retirada y no se usa.

## Estado de las ramas

| Rama | Estado |
| :--- | :--- |
| `claude/dazzling-lewin-3cbe83` | Compra desde la ficha (`3b233ec1` a `0b365d1a`). Terminada según su sesión: compilación, `SpecCheck` y pruebas afectadas en verde; un E2E falla por un defecto de la propia prueba. Sin publicar. |
| `claude/acceso-v012-7467d0` | Errores de despliegue, mínimo de 10 caracteres y spec, plan y código de `web-acceso` (`1e6a5d06` a `d7a72532`). En curso, sin informe final. Sin publicar. |

## Pendiente / siguiente

- Publicar la compra desde la ficha, generar imágenes nuevas y actualizar las VMs **antes de enviar la tienda a Culqi** (B14).
- Al integrar las dos ramas: conflicto conocido en `progress/hallazgos.md` (conservar ambas filas y regenerar la matriz) y posible conflicto en `progress/CHANGELOG.md`.
- Terminar la carga de stock y recorrer la tienda hasta el paso de pago; crear la cuenta de prueba.
- Probar en producción la recuperación de contraseña y la verificación de correo, y una restauración real desde R2.
- Decidir: recuperación del segundo factor del único administrador (orden de consola auditada o segundo administrador desde el inicio) y raíz de la gestión.

## Publicación de `v0.1.2` (06:35, hora de Perú)

- `main` avanzó a `938a3306` con las cuatro comprobaciones en verde (PR 31 y 32 fusionados) y la persona pidió publicar y etiquetar: etiqueta `v0.1.2` sobre ese commit. Lanzamiento `45991781088` en marcha, pendiente de sus dos aprobaciones.
- Incluye la compra desde la ficha (`3b233ec1` a `0b365d1a`), tres errores del primer despliegue (webhook y `tmpfs` de n8n, guía 08) y la documentación de la colaboración en la interfaz.
- Reemplaza lo anotado arriba como «sin publicar» para la rama de compra. **Aún no está desplegada.**
- El despliegue pasa entero a la sesión «Guía en vivo», por decisión de la persona.
- La rama de acceso será `v0.1.3`.

## Venta por sucursal y reparto de la interfaz (decisiones de la persona)

- Aprobadas las reglas de venta por sucursal (`progress/relevos/venta-por-sucursal.md`); pide terminarlas en `v0.1.3` si es posible, con carriles en paralelo. Reemplazan lo anotado como abierto sobre la sucursal (O-9, O-10).
- Asignados O-5, O-11 y O-12 a un carril de accesos pendientes.
- La rama de acceso se integra en `main` sin etiqueta; `web-acceso` queda en `InProgress`.
- Toda la UI/UX pasa a Codex, también la de lo ya construido; se amplían sus rutas de JavaScript.
