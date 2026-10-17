---
id: ecommerce-pedidos
titulo: Pedidos y seguimiento
estado: Draft
servicio: E-commerce
prefijo: PED
origen: [RF06, RF07, NN03, NN13, NN09]
calidad: [AC01, AC03, RNF05, RNF08]
depende: [ecommerce-pagos, ecommerce-biblioteca-digital, inventario-seleccion-fuente, transversal-notificaciones, transversal-auditoria]
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
| PED-01 | Evento | Cuando se aprueba el pago de una compra, el sistema deberá crear un pedido confirmado con una línea por cada edición, con una copia de su título, ISBN, precio y cantidad, y su estado de cumplimiento. | RF06 |
| PED-02 | Ubicuo | El sistema deberá gestionar para las líneas impresas los estados confirmado, en tránsito entre sucursales (solo si hay traslado), recibido en la sucursal (tras el traslado), en preparación, listo para recojo o despachado, entregado, no recogido (en recojo) y anulado; una devolución posterior no cambia el estado de la línea. | RF06 |
| PED-03 | Evento | Cuando la biblioteca concede el acceso a una línea digital, el sistema deberá marcarla como disponible en la biblioteca del cliente. | RF06, NN13 |
| PED-04 | Evento | Cuando un cliente consulta sus pedidos, el sistema deberá mostrar el detalle y el estado de cada línea, incluido el traslado en curso y su fecha estimada. | RF06, NN03 |
| PED-05 | Evento | Cuando cambia el estado de un pedido o de una de sus líneas, el sistema deberá notificar al cliente y a la sucursal responsable: la de recojo o, en domicilio, la fuente del envío; en un pedido solo digital, solo al cliente. | RF07 |
| PED-06 | No deseado | Si el envío de un aviso falla, entonces el sistema deberá mantener el nuevo estado del pedido y dejar el aviso pendiente de reintento. | RF07, AC03 |
| PED-07 | Estado | Mientras un cliente consulta pedidos, el sistema deberá mostrarle únicamente los suyos y rechazar, registrando el acceso denegado, el pedido de otro cliente pedido por su identificador. | RNF05 |
| PED-08 | Estado | Mientras un usuario es encargado de sucursal, el sistema deberá mostrarle únicamente los pedidos de recojo de su sucursal y rechazar, registrando el acceso denegado, los de otra por su identificador. | RNF05 |
| PED-09 | Evento | Cuando el encargado prepara las líneas de un pedido de recojo, el sistema deberá pasarlas a en preparación, y al marcarlas listas, a listo para recojo. | RF06 |
| PED-10 | No deseado | Si una línea de recojo con traslado aún no recibido es llevada a preparación, entonces el sistema deberá impedirlo. | RF06 |
| PED-11 | Evento | Cuando el encargado entrega las líneas listas de un pedido de recojo, el sistema deberá pedir al inventario que descuente el físico y cierre la reserva de cada línea y, solo si lo logra, pasarla a entregada. | RF06 |
| PED-12 | Ubicuo | El sistema deberá marcar como entregado un pedido formado solo por líneas digitales en cuanto se concede el acceso. | RF06 |
| PED-13 | Evento | Cuando la administración central anula un pedido, el sistema deberá exigir un motivo, anular las líneas que no han salido (confirmadas, en tránsito, recibidas, en preparación, listas para recojo o no recogidas), liberar las reservas que sigan activas y registrarlo en la auditoría en la misma transacción. | RF20, NN09 |
| PED-14 | Ubicuo | El sistema deberá mostrar a la administración central todos los pedidos con filtros por estado. | NN09 |
| PED-15 | Estado | Mientras una línea de recojo lleva más de 15 días calendario, en hora de Lima, en listo para recojo sin entregarse, el sistema deberá marcarla como no recogida, pedir al inventario que libere su reserva y avisar al cliente y a la sucursal. | RF06, RF07 |
| PED-16 | Ubicuo | El sistema deberá permitir a la administración central anular un pedido no recogido con las mismas reglas de la anulación de PED-13. | RF06, NN09 |
| PED-17 | Evento | Cuando un cliente cancela un pedido formado solo por líneas impresas que están todas confirmadas, sin traslado en tránsito, el sistema deberá anularlo con el motivo «cancelado por el cliente», liberar sus reservas, cancelar sus traslados vinculados, solicitar el reembolso del total, incluido el envío, y registrarlo en la auditoría. | RF06, NN05 |
| PED-18 | No deseado | Si un cliente intenta cancelar un pedido que incluye una línea digital o una línea impresa en tránsito, en preparación o posterior, entonces el sistema deberá rechazar la cancelación e indicar la alternativa: devolución una vez entregado, o política de reembolso digital. | RF06, NN05 |
| PED-19 | Ubicuo | El sistema deberá llevar una línea con traslado de confirmada a en tránsito entre sucursales y, al recibirse el traslado, a recibida en la sucursal; y pasar a en preparación, por acción de quien prepara, tanto una línea recibida como una confirmada sin traslado. | RF06 |
| PED-20 | Ubicuo | El sistema deberá derivar el estado del pedido así: anulado si todas sus líneas lo están; en otro caso, ignorando las líneas anuladas y las digitales ya disponibles, entregado si las demás están entregadas, no recogido si alguna lo está y las demás están entregadas o no recogidas y, si no, el de su línea impresa menos avanzada. | RF06 |
| PED-21 | No deseado | Si dos operaciones compiten por una misma línea (cancelación del cliente, anulación, preparación, despacho del traslado o paso del envío a en reparto), entonces el sistema deberá aplicar la primera que confirma y rechazar las demás por estado inválido, sin efectos parciales. | RF06, AC01 |
| PED-22 | Evento | Cuando se anula un pedido, el sistema deberá revocar los accesos digitales anulados, aunque se hayan descargado, y solicitar el reembolso de las líneas anuladas, incluido el envío si ninguna línea impresa salió. | RF20, NN09 |
| PED-23 | No deseado | Si se intenta anular un pedido en el que no queda ninguna línea impresa por salir, entonces el sistema deberá rechazar la anulación e indicar que corresponde una devolución. | RF06, NN09 |
| PED-24 | No deseado | Si falla la solicitud de reembolso de una anulación, entonces el sistema deberá mantener el pedido anulado con el reembolso pendiente, visible para la administración, y permitir reintentarlo sin repetir los pasos ya hechos. | RF06, AC03 |
| PED-25 | No deseado | Si el inventario no responde o rechaza el descuento al entregar, entonces el sistema deberá mantener la línea como estaba, informar el error y permitir reintentar sin descontar dos veces. | RF06, AC03 |
| PED-26 | Ubicuo | El sistema deberá mostrar a logística los pedidos a domicilio de toda la red y a la administración central todos los pedidos. | RNF05, NN09 |
| PED-27 | Evento | Cuando se anula un pedido en el que algún impreso ya salió (despachado o entregado), el sistema deberá anular solo las líneas que no salieron, mantener las que salieron, cancelar los envíos que no salieron según `ecommerce-envios` y reembolsar solo las líneas anuladas, sin el envío. | RF06, NN09 |
| PED-28 | Evento | Cuando logística prepara las líneas de un pedido a domicilio en su fuente, el sistema deberá pasarlas a en preparación y, al marcarlas listas (un hito sin estado propio), avisar a `ecommerce-envios` para crear su envío; y rechazar esta acción a quien no sea logística o administración central. | RF06, RF11, RNF05 |
| PED-29 | Evento | Cuando una línea impresa pasa a entregada, el sistema deberá registrar la fecha de entrega de esa línea. | RF06 |
| PED-30 | Ubicuo | El sistema deberá ordenar los estados de una línea impresa, para PED-20, como confirmado, en tránsito, recibido, en preparación, listo para recojo o despachado, y entregado. | RF06 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-PED-01 | PED-01, PED-03 | **Dado** una compra con un impreso y un digital con el pago aprobado, **cuando** se crea el pedido, **entonces** tiene dos líneas con el título, ISBN y precio copiados: el impreso confirmado y el digital disponible en la biblioteca; y si luego cambia el precio de la edición, el pedido no cambia. |
| AC-PED-02 | PED-02 | **Dado** una línea confirmada, **cuando** alguien intenta pasarla directamente a entregada, **entonces** se rechaza y el estado no cambia. |
| AC-PED-03 | PED-04, PED-19 | **Dado** un pedido con un impreso en traslado, **cuando** el cliente lo consulta, **entonces** ve «en tránsito entre sucursales» con el traslado y su fecha estimada; y al recibirse el traslado, la línea queda recibida en la sucursal y solo entonces puede pasar a en preparación; una línea confirmada sin traslado pasa a en preparación cuando quien prepara lo hace. |
| AC-PED-04 | PED-05 | **Dado** un pedido, **cuando** pasa a listo para recojo, **entonces** el cliente y la sucursal reciben un aviso. |
| AC-PED-05 | PED-06 | **Dado** que el servicio de avisos no responde, **cuando** un pedido cambia de estado, **entonces** el cambio se aplica y el aviso queda pendiente de reintento. |
| AC-PED-06 | PED-07, PED-08, PED-26 | **Dado** pedidos de varios clientes, **cuando** un cliente consulta los suyos y un encargado de Ayacucho consulta los de recojo, **entonces** cada uno ve solo lo que le corresponde; si piden por identificador el pedido de otro cliente o de otra sucursal, se rechaza y queda un acceso denegado; y logística ve los pedidos a domicilio y la administración todos. |
| AC-PED-07 | PED-09 | **Dado** un pedido de recojo confirmado, **cuando** el encargado lo prepara y luego lo marca listo, **entonces** pasa por en preparación y listo para recojo. |
| AC-PED-08 | PED-10 | **Dado** un pedido con una línea que espera el traslado TR-0042 sin recibir y otra del mismo pedido ya en la sucursal, **cuando** el encargado intenta prepararlas, **entonces** se rechaza la primera indicando que espera el traslado y se puede preparar la segunda. |
| AC-PED-09 | PED-11, PED-25 | **Dado** un pedido listo para recojo, **cuando** el encargado entrega sus líneas, **entonces** el inventario descuenta el físico, cierra la reserva y cada línea pasa a entregada (y el pedido, a entregado si no queda otra); y si el inventario no responde, el pedido sigue listo, se informa el error y al reintentar el descuento se aplica una sola vez. |
| AC-PED-10 | PED-12 | **Dado** un pedido solo con un libro digital, **cuando** se concede el acceso, **entonces** el pedido queda entregado. |
| AC-PED-11 | PED-13, PED-22 | **Dado** un pedido con impreso confirmado y un digital ya descargado, **cuando** la administración lo anula con el motivo «pago duplicado», **entonces** se liberan las reservas, se revoca el acceso digital aunque se haya descargado, se solicita el reembolso del total, incluido el envío si lo hubo, y queda la auditoría. |
| AC-PED-12 | PED-14, PED-20, PED-30 | **Dado** pedidos en varios estados, **cuando** la administración filtra por «en tránsito», **entonces** ve solo los que están en ese estado; y un pedido con un digital disponible y un impreso en tránsito figura en tránsito. |
| AC-PED-13 | PED-15 | **Dado** una línea de recojo que lleva 15 días y 1 hora en listo para recojo, **cuando** corre la revisión de plazos, **entonces** pasa a no recogida, el inventario libera su reserva y el cliente y la sucursal reciben un aviso; y una con 14 días no cambia. |
| AC-PED-14 | PED-16 | **Dado** un pedido no recogido, **cuando** la administración lo anula con el motivo «no recogido», **entonces** se exige el motivo, se solicita el reembolso y queda la auditoría, sin liberar dos veces las reservas. |
| AC-PED-15 | PED-17 | **Dado** un pedido pagado de un impreso confirmado cuyo traslado está aprobado, **cuando** el cliente lo cancela, **entonces** el pedido queda anulado con el motivo «cancelado por el cliente», la reserva se libera, el traslado pasa a cancelado, se solicita el reembolso y queda la auditoría. |
| AC-PED-16 | PED-18 | **Dado** un pedido con un impreso en preparación, otro con un impreso en tránsito entre sucursales y otro con un libro digital, **cuando** el cliente intenta cancelarlos, **entonces** se rechaza en los tres casos indicando la devolución o la política de reembolso digital. |
| AC-PED-17 | PED-21 | **Dado** una línea confirmada, **cuando** el cliente la cancela a la vez que el encargado la prepara, o la administración la anula, **entonces** se aplica solo la primera que confirma, la otra se rechaza por estado inválido y una doble cancelación no reembolsa dos veces. |
| AC-PED-18 | PED-23 | **Dado** un pedido con un impreso entregado, **cuando** la administración intenta anularlo, **entonces** se rechaza indicando que corresponde una devolución. |
| AC-PED-19 | PED-24 | **Dado** que la pasarela no responde, **cuando** la administración anula un pedido pagado, **entonces** el pedido queda anulado con el reembolso pendiente, visible para la administración, y al reintentar se reembolsa una sola vez. |
| AC-PED-20 | PED-27, PED-22 | **Dado** un pedido a domicilio con un impreso en reparto desde Cusco y otro en preparación en Ayacucho con su envío pendiente de guía, **cuando** la administración lo anula, **entonces** solo se anula la línea de Ayacucho, su envío pasa a cancelado, se reembolsa esa línea sin el envío y la de Cusco sigue su curso. |
| AC-PED-21 | PED-20 | **Dado** el pedido anterior, **cuando** la línea de Cusco se entrega, **entonces** el pedido queda entregado pese a la línea anulada; y con todas las líneas anuladas, queda anulado. |
| AC-PED-22 | PED-13, PED-16 | **Dado** una línea en preparación, otra lista para recojo y otra no recogida sin que ninguna haya salido, **cuando** la administración anula el pedido, **entonces** las tres se anulan y sus reservas se liberan sin liberarse dos veces. |
| AC-PED-23 | PED-28 | **Dado** un pedido a domicilio con líneas confirmadas en Cusco, **cuando** logística las prepara y las marca listas, **entonces** pasan por en preparación y se crea su envío pendiente de guía. |
| AC-PED-24 | PED-29 | **Dado** una línea despachada, **cuando** su envío se entrega, **entonces** la línea queda entregada con su fecha de entrega. |
| AC-PED-25 | PED-28 | **Dado** un pedido a domicilio con líneas confirmadas, **cuando** un encargado de sucursal o el cliente intenta prepararlas, **entonces** se rechaza por falta de permiso y se registra el acceso denegado. |

## 5. Reglas e invariantes

- Un pedido se confirma solo con el pago aprobado y, para los impresos, con las reservas confirmadas.
- El estado del pedido se deriva del estado de sus líneas; cada línea sigue su propio ciclo.
- Ciclo de una línea impresa con traslado: confirmado → en tránsito entre sucursales → recibido en la sucursal → en preparación → listo para recojo → entregado; sin traslado: confirmado → en preparación → listo para recojo o despachado → entregado. En recojo, listo para recojo → no recogido a los 15 días calendario. Una línea confirmada puede pasar a anulado.
- Una línea queda despachada cuando su envío pasa a en reparto (`ecommerce-envios`). Una línea digital pasa a disponible en la biblioteca al aprobarse el pago.
- La anulación es una operación sensible: motivo obligatorio y auditoría.

## 6. Casos límite y errores

- Un pedido con dos impresos en distintas fuentes puede tener un traslado en tránsito y el otro listo.
- Una entrega repetida del mismo pedido no descuenta el físico dos veces.
- Anular un pedido con todas sus líneas impresas despachadas o entregadas no procede: corresponde una devolución (PED-23, `ecommerce-devoluciones`); si solo algunas salieron, se anulan las demás (PED-27).
- El reembolso del envío solo corresponde al anular o cancelar el pedido completo antes de que salga; en una devolución posterior, parcial o no, no se reembolsa.

## 7. Preguntas abiertas

- [x] ¿Un pedido de recojo no recogido caduca después de cierto tiempo? Resuelto el 2026-10-16: a los 15 días calendario en listo para recojo pasa a no recogido, libera sus reservas y queda a revisión de la administración, que puede anularlo con reembolso (PED-15, PED-16).
- [x] ¿Se cuentan los 15 días de forma calendario corrida o se excluyen feriados? Resuelto el 2026-10-16 (confirmado por el responsable): corrida.
- [x] ¿Puede el cliente cancelar por sí mismo un pedido pagado? Resuelto el 2026-10-16: sí, mientras todas sus líneas sean impresas y estén confirmadas; después, por devolución (PED-17, PED-18).
- [x] ¿Cuál es el orden del ciclo de una línea con traslado? Resuelto el 2026-10-16 (confirmado por el responsable): confirmado → en tránsito entre sucursales → en preparación → listo (PED-19).
- [x] ¿Se reembolsa el costo de envío? Resuelto el 2026-10-16: solo al anular o cancelar todo antes de que salga (PED-17, PED-22).
- [x] ¿Qué pasa al anular un pedido mixto con un digital ya descargado? Resuelto el 2026-10-16: la administración anula todo, revoca el acceso y reembolsa el total, con motivo y auditoría (PED-22); la cancelación del cliente sigue sin permitirse con digitales (PED-18).
- [x] ¿Se permite cancelar solo las líneas impresas de un pedido mixto? Resuelto el 2026-10-16 (confirmado por el responsable): fuera de esta versión; un pedido con libros digitales sigue la política de reembolso digital (PED-18).
