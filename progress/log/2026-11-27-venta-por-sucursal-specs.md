# 2026-11-27 — Venta por sucursal: specs, contrato, planes y tareas (carril `sucursal-specs`)

## Qué se hizo

- Reabiertas y reaprobadas por delegación (spec y plan) `inventario-seleccion-fuente`, `ecommerce-carrito-checkout`, `ecommerce-pedidos` y `web-base`, según las reglas de [`../relevos/venta-por-sucursal.md`](../relevos/venta-por-sucursal.md). Huellas en [`../aprobaciones.md`](../aprobaciones.md).
- Contrato aditivo (sucursal por línea) descrito en `inventario-seleccion-fuente` §11.1 y en `docs/03/08`; `BabelHub.Shared` se cambia en su T34. Documentos de análisis, arquitectura, diseño, seguridad y experiencia ajustados.
- Reparto en tres carriles, con variables, migraciones y despliegue: [`../relevos/venta-por-sucursal-carriles.md`](../relevos/venta-por-sucursal-carriles.md).
- Revisiones: carrito en 3 rondas; pedidos y web en 2; reservas en 3, más una cuarta (spec, con la alerta) y una quinta (solo el plan), ambas autorizadas por la persona. Hallazgos graves corregidos en el camino: unidades vendidas con una edición en dos sucursales, alcance del encargado, vuelta atrás, desglose del envío por sucursal, liberación de checkouts antiguos y, en la ronda 4, el registro del aviso sin puerto en la Aplicación.
- Comprobado: `SpecCheck` 23 specs, 0 errores y 0 avisos; `dotnet test BabelHub.sln` con 17 333 pruebas superadas, 4 omitidas y 0 fallos (integración, 35 min 52 s); compilación estricta sin cambios de código desde que pasó.

## Decisiones

- De la persona (2026-11-27): una reserva puede apartar hasta 600 unidades de un libro en una sucursal; hacer ya la alerta de acaparamiento (más de 100 unidades en 15 minutos, correo a los administradores centrales, sin bloquear; SEL-47); autorizar las rondas cuarta y quinta; el 28-nov es solo la entrega del Sprint 2 y esto es trabajo posterior.
- Del agente, pendientes de ratificar: DD-130 a DD-138 en [`../decisiones-delegadas.md`](../decisiones-delegadas.md). Destacan DD-135 (cada sucursal gestiona su envío como un envío por sucursal operado por logística), DD-136 (el reembolso del envío no cambia) y los detalles de DD-138.

## Pendiente / siguiente

- No hay código: abrir los carriles A (Inventario), B (Carrito y tienda) y C (Pedidos, pagos y devoluciones). A entrega T34 primero.
- Antes de desplegar: decidir quién revisa el registro del Inventario ante la alerta (el correo no nombra libro ni sucursal) y ratificar las DD.
- Menores abiertos en [`../hallazgos.md`](../hallazgos.md). Las pruebas de extremo a extremo y las de carga no se ejecutaron en esta sesión.
