# 2026-11-27 — Venta por sucursal: specs, contrato, planes y tareas (carril `sucursal-specs`)

## Qué se hizo

- Reabiertas y reaprobadas por delegación (spec y plan) `inventario-seleccion-fuente`, `ecommerce-carrito-checkout`, `ecommerce-pedidos` y `web-base`, según las reglas de [`../relevos/venta-por-sucursal.md`](../relevos/venta-por-sucursal.md). Huellas en [`../aprobaciones.md`](../aprobaciones.md).
- Contrato aditivo (sucursal por línea) descrito en `inventario-seleccion-fuente` §11.1 y en `docs/03/08`; `BabelHub.Shared` se cambia en su T34. Documentos de análisis, arquitectura, diseño, seguridad y experiencia ajustados.
- Reparto en tres carriles, con variables, migraciones y despliegue: [`../relevos/venta-por-sucursal-carriles.md`](../relevos/venta-por-sucursal-carriles.md).
- Revisiones: carrito en 3 rondas; pedidos y web en 2; reservas en 3, más una cuarta (spec, con la alerta) y una quinta (solo el plan), ambas autorizadas por la persona. Hallazgos graves corregidos en el camino: unidades vendidas con una edición en dos sucursales, alcance del encargado, vuelta atrás, desglose del envío por sucursal, liberación de checkouts antiguos y, en la ronda 4, el registro del aviso sin puerto en la Aplicación.
- Comprobado: `SpecCheck` 23 specs, 0 errores y 0 avisos; `dotnet test BabelHub.sln` con 17 333 pruebas superadas, 4 omitidas y 0 fallos (integración, 35 min 52 s); compilación estricta sin cambios de código desde que pasó.

## Decisiones

- De la persona (2026-11-27): una reserva puede apartar hasta 600 unidades de un libro en una sucursal; hacer ya la alerta de acaparamiento (más de 100 unidades en 15 minutos, correo a los administradores centrales, sin bloquear; SEL-47); autorizar las rondas cuarta y quinta; el 28-nov es solo la entrega del Sprint 2 y esto es trabajo posterior.
- Del agente, pendientes de ratificar: DD-130 a DD-138 en [`../decisiones-delegadas.md`](../decisiones-delegadas.md). Destacan DD-135 (cada sucursal gestiona su envío como un envío por sucursal operado por logística), DD-136 (el reembolso del envío no cambiaba; la persona lo cambió después, ver la segunda parte) y los detalles de DD-138.

## Pendiente / siguiente

- No hay código: abrir los carriles A (Inventario), B (Carrito y tienda) y C (Pedidos, pagos y devoluciones). A entrega T34 primero.
- Antes de desplegar: decidir quién revisa el registro del Inventario ante la alerta (el correo no nombra libro ni sucursal) y ratificar las DD.
- Menores abiertos en [`../hallazgos.md`](../hallazgos.md). Las pruebas de extremo a extremo y las de carga no se ejecutaron en esta sesión.

## Segunda parte: reembolso del envío por sucursal (DD-136)

### Qué se hizo

- Reabiertas y reaprobadas por delegación `ecommerce-carrito-checkout`, `ecommerce-pagos` y `ecommerce-pedidos` (spec y plan): el envío de cada sucursal se congela al comprar, viaja con la compra aprobada y la anulación devuelve el de cada sucursal que no envió nada (CAR-23, T50; PAG-53, PAG-54, T53 a T55; PED-22, PED-27, PED-44, PED-45, T50 a T52). Reservas y web base no cambian.
- Revisiones: tres rondas, y una cuarta solo del plan de carrito autorizada por la persona. Corregidos en el camino: la huella la calcula Carrito, el reemplazo de una intención sobrescribe el detalle, el conteo por sucursal, y el despliegue, que debe detener `ecommerce` y `ecommerce-gestion` antes de `desplegar.sh inventario`.
- Comprobado: `SpecCheck` 23 specs, 0 errores y 0 avisos. Solo cambian documentos; la suite completa es la de la primera parte, sobre el mismo código, y no se repitió.

### Decisiones

- De la persona (2026-11-27): devolver el envío de la sucursal que no llegó a enviar, exactamente lo que cobró; no devolverlo si envió una parte; aplicar la regla a todo el sistema porque no hay pedidos hechos.

### Pendiente / siguiente

- La coordinadora abre el carril C. El carril B fusiona esta reaprobación (conflicto esperado en el estado de la spec de carrito), entrega T50 primero y rehace el congelado del envío.
- Menores abiertos en `progress/hallazgos.md`; antes de desplegar, comprobar los datos **después** de detener la tienda.
