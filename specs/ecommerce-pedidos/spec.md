---
id: ecommerce-pedidos
titulo: Pedidos y seguimiento
estado: Draft
servicio: E-commerce
prefijo: PED
origen: [RF06, RF07, NN03, NN13, NN09]
calidad: [AC03, RNF05, RNF08]
depende: [ecommerce-pagos, inventario-seleccion-fuente, transversal-notificaciones, transversal-auditoria]
---

# Spec — Pedidos y seguimiento

## 1. Propósito

Que el cliente vea siempre en qué estado está su compra, incluso cuando viaja entre sucursales, y que la sucursal pueda preparar y entregar los pedidos de recojo. Un pedido agrupa **líneas** y cada línea tiene su propio cumplimiento.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Estados y transiciones del pedido y de sus líneas. | El cobro (`ecommerce-pagos`) y las guías de envío (`ecommerce-envios`). |
| Consulta de pedidos por cliente, sucursal y administración. | Devoluciones (`ecommerce-devoluciones`). |
| Gestión de recojo por el encargado y anulación por la administración. | El envío de los avisos (`transversal-notificaciones`). |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| PED-01 | Evento | Cuando se aprueba el pago de un carrito, el sistema deberá crear un pedido confirmado con una línea por cada edición y su estado de cumplimiento. | RF06 |
| PED-02 | Ubicuo | El sistema deberá gestionar para las líneas impresas los estados confirmado, en preparación, en tránsito entre sucursales, listo para recojo o despachado, entregado y, en recojo, no recogido, y permitir solo las transiciones válidas. | RF06 |
| PED-03 | Evento | Cuando se aprueba el pago de una línea digital, el sistema deberá marcarla como disponible en la biblioteca del cliente. | RF06, NN13 |
| PED-04 | Evento | Cuando un cliente consulta sus pedidos, el sistema deberá mostrar el detalle y el estado de cada línea, incluido el traslado en curso y su fecha estimada. | RF06, NN03 |
| PED-05 | Evento | Cuando cambia el estado de un pedido o de una de sus líneas, el sistema deberá notificar al cliente y a la sucursal responsable. | RF07 |
| PED-06 | No deseado | Si el envío de un aviso falla, entonces el sistema deberá mantener el nuevo estado del pedido y dejar el aviso pendiente de reintento. | RF07, AC03 |
| PED-07 | Estado | Mientras un cliente consulta pedidos, el sistema deberá mostrarle únicamente los suyos. | RNF05 |
| PED-08 | Estado | Mientras un usuario es encargado de sucursal, el sistema deberá mostrarle únicamente los pedidos de recojo de su sucursal. | RNF05 |
| PED-09 | Evento | Cuando el encargado prepara un pedido de recojo, el sistema deberá pasarlo a en preparación, y al marcarlo listo, a listo para recojo. | RF06 |
| PED-10 | No deseado | Si un pedido de recojo espera un traslado que aún no fue recibido, entonces el sistema deberá impedir prepararlo. | RF06 |
| PED-11 | Evento | Cuando el encargado entrega un pedido de recojo, el sistema deberá pasarlo a entregado y pedir al inventario que descuente el físico y cierre la reserva. | RF06 |
| PED-12 | Ubicuo | El sistema deberá marcar como entregado un pedido formado solo por líneas digitales en cuanto se concede el acceso. | RF06 |
| PED-13 | Evento | Cuando la administración central anula un pedido, el sistema deberá exigir un motivo, liberar las reservas, revocar los accesos digitales, solicitar el reembolso y registrarlo en la auditoría. | RF20, NN09 |
| PED-14 | Ubicuo | El sistema deberá mostrar a la administración central todos los pedidos con filtros por estado. | NN09 |
| PED-15 | Estado | Mientras un pedido de recojo lleva más de 15 días calendario en listo para recojo sin entregarse, el sistema deberá marcarlo como no recogido, pedir al inventario que libere sus reservas y avisar al cliente y a la sucursal. | RF06, RF07 |
| PED-16 | Ubicuo | El sistema deberá permitir a la administración central anular un pedido no recogido con las mismas reglas de la anulación de PED-13. | RF06, NN09 |
| PED-17 | Evento | Cuando un cliente cancela un pedido formado solo por líneas impresas que están todas confirmadas, el sistema deberá anularlo con el motivo «cancelado por el cliente», liberar sus reservas, cancelar sus traslados vinculados, solicitar el reembolso y registrarlo en la auditoría. | RF06, NN05 |
| PED-18 | No deseado | Si un cliente intenta cancelar un pedido que incluye una línea digital o una línea impresa ya en preparación o posterior, entonces el sistema deberá rechazar la cancelación e indicar la alternativa: devolución una vez entregado, o política de reembolso digital. | RF06, NN05 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-PED-01 | PED-01, PED-03 | **Dado** un carrito con un impreso y un digital con el pago aprobado, **cuando** se crea el pedido, **entonces** tiene dos líneas: el impreso confirmado y el digital disponible en la biblioteca. |
| AC-PED-02 | PED-02 | **Dado** un pedido confirmado, **cuando** alguien intenta pasarlo directamente a entregado, **entonces** se rechaza y el estado no cambia. |
| AC-PED-03 | PED-04 | **Dado** un pedido con un impreso en traslado, **cuando** el cliente lo consulta, **entonces** ve «en tránsito entre sucursales» con el traslado y su fecha estimada. |
| AC-PED-04 | PED-05 | **Dado** un pedido, **cuando** pasa a listo para recojo, **entonces** el cliente y la sucursal reciben un aviso. |
| AC-PED-05 | PED-06 | **Dado** que el servicio de avisos no responde, **cuando** un pedido cambia de estado, **entonces** el cambio se aplica y el aviso queda pendiente de reintento. |
| AC-PED-06 | PED-07, PED-08 | **Dado** pedidos de varios clientes, **cuando** un cliente consulta los suyos y un encargado de Ayacucho consulta los de recojo, **entonces** cada uno ve solo lo que le corresponde. |
| AC-PED-07 | PED-09 | **Dado** un pedido de recojo confirmado, **cuando** el encargado lo prepara y luego lo marca listo, **entonces** pasa por en preparación y listo para recojo. |
| AC-PED-08 | PED-10 | **Dado** un pedido que espera el traslado TR-0042 sin recibir, **cuando** el encargado intenta prepararlo, **entonces** se rechaza indicando que espera el traslado. |
| AC-PED-09 | PED-11 | **Dado** un pedido listo para recojo, **cuando** el encargado lo entrega, **entonces** pasa a entregado y el inventario descuenta el físico y cierra la reserva. |
| AC-PED-10 | PED-12 | **Dado** un pedido solo con un libro digital, **cuando** se concede el acceso, **entonces** el pedido queda entregado. |
| AC-PED-11 | PED-13 | **Dado** un pedido con impreso y digital, **cuando** la administración lo anula con el motivo «pago duplicado», **entonces** se liberan las reservas, se revoca el acceso digital, se solicita el reembolso y queda la auditoría. |
| AC-PED-12 | PED-14 | **Dado** pedidos en varios estados, **cuando** la administración filtra por «en tránsito», **entonces** ve solo los que están en ese estado. |
| AC-PED-13 | PED-15 | **Dado** un pedido de recojo que lleva 16 días en listo para recojo, **cuando** corre la revisión de plazos, **entonces** pasa a no recogido, el inventario libera sus reservas y el cliente y la sucursal reciben un aviso; y uno con 14 días no cambia. |
| AC-PED-14 | PED-16 | **Dado** un pedido no recogido, **cuando** la administración lo anula con el motivo «no recogido», **entonces** se exige el motivo, se solicita el reembolso y queda la auditoría, sin liberar dos veces las reservas. |
| AC-PED-15 | PED-17 | **Dado** un pedido pagado de un impreso confirmado cuyo traslado está aprobado, **cuando** el cliente lo cancela, **entonces** el pedido queda anulado con el motivo «cancelado por el cliente», la reserva se libera, el traslado pasa a cancelado, se solicita el reembolso y queda la auditoría. |
| AC-PED-16 | PED-18 | **Dado** un pedido con un impreso en preparación y otro con un libro digital, **cuando** el cliente intenta cancelarlos, **entonces** se rechaza en ambos casos indicando la devolución o la política de reembolso digital. |

## 5. Reglas e invariantes

- Un pedido se confirma solo con el pago aprobado y, para los impresos, con las reservas confirmadas.
- El estado del pedido se deriva del estado de sus líneas; cada línea sigue su propio ciclo.
- Ciclo de una línea impresa: confirmado → en preparación → (en tránsito entre sucursales) → listo para recojo o despachado → entregado. En recojo, listo para recojo → no recogido a los 15 días calendario.
- Una línea queda despachada cuando su envío pasa a en reparto (`ecommerce-envios`). Una línea digital pasa a disponible en la biblioteca al aprobarse el pago.
- La anulación es una operación sensible: motivo obligatorio y auditoría.

## 6. Casos límite y errores

- Un pedido con dos impresos en distintas fuentes puede tener un traslado en tránsito y el otro listo.
- Una entrega repetida del mismo pedido no descuenta el físico dos veces.
- Anular un pedido ya entregado lo convierte en una devolución (`ecommerce-devoluciones`).

## 7. Preguntas abiertas

- [x] ¿Un pedido de recojo no recogido caduca después de cierto tiempo? Resuelto el 2026-10-16: a los 15 días calendario en listo para recojo pasa a no recogido, libera sus reservas y queda a revisión de la administración, que puede anularlo con reembolso (PED-15, PED-16).
- [x] ¿Se cuentan los 15 días de forma calendario corrida o se excluyen feriados? Resuelto el 2026-10-16 (confirmado por el responsable): corrida.
- [x] ¿Puede el cliente cancelar por sí mismo un pedido pagado? Resuelto el 2026-10-16: sí, mientras todas sus líneas sean impresas y estén confirmadas; después, por devolución (PED-17, PED-18).
- [x] ¿Se permite cancelar solo las líneas impresas de un pedido mixto? Resuelto el 2026-10-16 (confirmado por el responsable): fuera de esta versión; un pedido con libros digitales sigue la política de reembolso digital (PED-18).
