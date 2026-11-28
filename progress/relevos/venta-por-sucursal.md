# Relevo — Venta por sucursal (aprobada el 2026-11-27)

Punto de partida de los carriles que la especifican e implementan. **Son requisitos decididos por la persona responsable**: cambian specs ya aprobadas, que vuelven a `Draft` y pasan otra vez por su puerta (`AGENTS.md` §2).

## Pedido de la persona (2026-11-27)

- Al comprar se decide **la sucursal que atenderá cada pedido**, para usar el stock real. La sucursal se elige en la venta (ficha del libro), no en la cabecera.
- En la ficha se ve el stock por sucursal y el máximo es el de la sucursal elegida.
- Se puede pedir, por ejemplo, 18 unidades de una sucursal y 6 de otra: **son compras separadas, no se combinan**, y cada sucursal gestiona su envío.
- El carrito muestra en cada tarjeta de qué sucursal se pide y se actualiza al cambiar la cantidad, sin pulsar «Actualizar».
- «No lo quiero para v0.2.0»: entra en `v0.1.3` si puede terminarse.
- El código que no es solo UI/UX lo desarrolla Claude, con una interfaz funcional y sencilla; la mejora de la UI/UX viene después (desde el 2026-11-28 también la hace Claude).

## Reglas aprobadas

| Tema | Regla |
| :--- | :--- |
| Línea | Libro impreso + sucursal. Hasta 20 unidades por línea; se pueden añadir **más líneas (lotes) de la misma sucursal** si su stock alcanza. Se mantienen 30 líneas por compra. |
| Máximo | La suma de las líneas de un libro en una sucursal no supera el stock vendible de esa sucursal. |
| Sin dividir | Una línea nunca se divide ni se reasigna a otra sucursal sin que la persona lo elija. |
| Envío a domicilio | **Cada sucursal que atiende cobra su envío, incluida la primera**: S/ 30 por defecto; dos sucursales, S/ 60. Varias líneas de una sucursal se cobran una sola vez. |
| Tarifa | Configurable por sucursal por la **administración central**, auditada. |
| Recojo | Gratis, en la sucursal de cada línea. La compra del cliente no genera traslados. |
| Digitales | Sin cambios: sin sucursal, sin envío, una unidad. |
| Precio | Único por edición (no cambia por sucursal). |
| Cabecera | Se quita el selector «Tu sucursal» de la cabecera. |

## Lo que la persona no decidió

Resolver con la alternativa recomendada, registrarlo como `DD-xx` en `progress/decisiones-delegadas.md` y reportarlo. Entre otras: qué pasa con la sucursal preferida (WEB-16; propuesta: queda solo como preselección en la ficha); recepción y entrega de un pedido con varias sucursales de recojo; reembolso del envío en anulaciones y devoluciones parciales; carritos y pedidos existentes al migrar; el asistente (no implementado). Lo que contradiga una regla de arriba o `AGENTS.md` no se decide: se reporta.

## Estado actual del sistema (punto de partida)

Una línea por edición (CAR), tope de 20 por línea y 30 líneas (DD-16, SEL-40), máximo por stock de la **red** (CAR-04), selección automática de fuente y traslados al punto de recojo (SEL-02, SEL-03, SEL-24, SEL-25), un envío por fuente (ENV-01), costo de envío único configurado (`BABELHUB__Carrito__CostoDeEnvio`), selector de sucursal en la cabecera (WEB-16). Fallo conocido: en la carga inicial, 15 libros admiten cantidades que ninguna sucursal cubre sola.

## Despliegue

Lo lleva la sesión «Guía en vivo». Los carriles no tocan las VMs; su informe dice qué variables, migraciones y pasos necesita la versión.
