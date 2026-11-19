# Relevo del carril C (Catálogo, Pagos, Reclamaciones y WEB-2)

Lee primero `AGENTS.md`, `progress/coordinacion.md` y `progress/relevos/coordinador.md` §6 (trampas).

## Hecho (todo en `main`)
`ecommerce-catalogo` 28/28. `ecommerce-pagos` T01–T35 salvo T31 (parcial: usa doble de Carrito) y T36; incluye la corrección real de AC-PAG-03. `ecommerce-libro-reclamaciones` ECO-11 (19/23; falta el E2E T22 y el pulido de T17/T18 con `ui-review`). **WEB-2** (`web-base` T28–T31) en código con 868 pruebas de Web verdes: ingreso con segundo factor, registro y verificación, recuperación, cuenta, auditoría y alertas, usuarios y solicitudes; **T28–T31 sin marcar** hasta `ui-review` y E2E. Cambió el contrato público `IIngreso` de Identidad (`PasoDelDesafio`, `CancelarElDesafio`).

## Siguiente
1. **Renovación de la sesión actual** (hallazgo de A para WEB-2, dueño carril C): pantalla de cuenta.
2. **`ui-review` y E2E** de WEB-2, Reclamaciones (T22), Catálogo y Pagos (T36) con el host sembrado (`tests/e2e/entorno/`, `POST /desarrollo/sesion`, Node en `C:\Program Files\nodejs`). Obligatorios: Chromium escritorio, Chromium teléfono y WebKit; Firefox informativo (bloqueado por el sistema en esta PC).
3. **T31 de Pagos** con el Carrito real cuando el host esté compuesto; **T37/T38** (Culqi) esperan las llaves B05 de la persona: no tocar.

## Trampas
Fixtures de PostgreSQL sin `Password=` literal; endpoints de prueba que dupliquen rutas reales (pasó con `/checkout/pago` y `/reclamaciones/nueva`); espejar catálogos de auditoría; motivo de alerta nuevo = texto español en el presentador en el mismo commit; ninguna excepción nueva a pruebas de arquitectura.

## Cómo avisar
Mensaje de una línea al coordinador al cerrar cada bloque; turno de integración para pruebas con Docker.
