---
id: inventario-traslados
titulo: Traslados entre sucursales
estado: Draft
servicio: Inventario
prefijo: TRA
origen: [RF16, RF15, RF07, RF20, NN07]
calidad: [AC01, RNF01, RNF05, RNF08]
depende: [inventario-fuentes, inventario-seleccion-fuente, identidad-seguridad, transversal-auditoria, transversal-notificaciones]
---

# Spec — Traslados entre sucursales

## 1. Propósito

Mover ejemplares de una sucursal a otra con trazabilidad completa, para abastecer pedidos y reponer stock (NN07). Cada paso lo hace quien corresponde y deja constancia.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Solicitar, aprobar, rechazar, despachar y recibir traslados. | Cómo nace un traslado desde un pedido (`inventario-seleccion-fuente`). |
| Efecto en el stock físico de origen y destino. | Guías de envío a domicilio (`ecommerce-envios`). |
| Registro de la guía del traslado por logística. | El transporte físico y su seguimiento externo. |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| TRA-01 | Evento | Cuando un encargado o la administración solicita un traslado, el sistema deberá registrar la edición, la cantidad, la sucursal de origen, la de destino y el motivo en estado solicitado. | RF16 |
| TRA-02 | Ubicuo | El sistema deberá ofrecer como origen de una solicitud únicamente las fuentes con stock vendible de esa edición. | RF15 |
| TRA-03 | Ubicuo | El sistema deberá permitir solo las transiciones solicitado a aprobado, rechazado o cancelado, aprobado a en tránsito o cancelado, y en tránsito a recibido, y rechazar cualquier otra. | RF16 |
| TRA-04 | Evento | Cuando la sucursal de origen aprueba una solicitud, el sistema deberá pasarla a aprobado. | RF16 |
| TRA-05 | Evento | Cuando la sucursal de origen rechaza una solicitud no vinculada a un pedido, el sistema deberá pasarla a rechazado y registrar el motivo. | RF16 |
| TRA-06 | No deseado | Si al despachar un traslado no vinculado a un pedido el origen no tiene stock vendible suficiente, o si uno vinculado no tiene la reserva confirmada de su pedido por esa cantidad, entonces el sistema deberá rechazar el despacho y mantener el traslado aprobado. | RF16, RNF01 |
| TRA-07 | Evento | Cuando la sucursal de origen despacha un traslado, el sistema deberá pasarlo a en tránsito, descontar la cantidad del stock físico del origen y, si está vinculado a un pedido, consumir la misma cantidad de su reserva confirmada en el origen, todo en la misma transacción. | RF16 |
| TRA-08 | Evento | Cuando la sucursal de destino recibe un traslado, el sistema deberá pasarlo a recibido y sumar la cantidad al stock físico del destino, creando en la misma transacción la reserva de TRA-14 cuando corresponda. | RF16 |
| TRA-09 | Ubicuo | El sistema deberá permitir la aprobación, el rechazo y el despacho solo a la sucursal de origen, la recepción solo a la de destino y el registro de la guía a logística, salvo a la administración central, que puede ejecutar todas excepto rechazar un traslado vinculado a un pedido (TRA-16), y rechazar a cualquier otro registrando el intento como acceso denegado. | RNF05 |
| TRA-10 | Evento | Cuando cambia el estado de un traslado, el sistema deberá notificar a las sucursales de origen y de destino. | RF07, RF16 |
| TRA-11 | Evento | Cuando un traslado vinculado a un pedido de recojo pasa a recibido, el sistema deberá informar al E-commerce que el pedido puede prepararse y notificar a la sucursal de destino. | RF07 |
| TRA-12 | Evento | Cuando logística registra la guía de un traslado aprobado o en tránsito, el sistema deberá asociarla al traslado sin cambiar su estado. | RF16 |
| TRA-13 | Evento | Cuando cambia el estado de un traslado, el sistema deberá registrarlo en la auditoría. | RF20, RNF08 |
| TRA-14 | Evento | Cuando el destino recibe un traslado vinculado a un pedido activo, el sistema deberá dejar las unidades recibidas como reserva confirmada de ese pedido en el destino; un pedido está activo mientras no esté anulado ni marcado como no recogido. | RF15, RF16 |
| TRA-15 | Evento | Cuando se anula un pedido con un traslado vinculado solicitado o aprobado, el sistema deberá pasar el traslado a cancelado y registrar el motivo. | RF16 |
| TRA-16 | No deseado | Si se intenta rechazar un traslado vinculado a un pedido, entonces el sistema deberá impedirlo e indicar que solo puede aprobarse y que, para detenerlo, se debe anular el pedido. | RF16 |
| TRA-17 | Evento | Cuando el destino recibe un traslado vinculado a un pedido ya anulado, el sistema deberá sumar las unidades al stock físico del destino sin reservarlas. | RF16 |
| TRA-18 | Evento | Cuando la sucursal de origen o la administración central cancela, con un motivo, un traslado solicitado o aprobado que no está vinculado a un pedido, el sistema deberá pasarlo a cancelado. | RF16 |
| TRA-19 | Evento | Cuando se anula un pedido con un traslado vinculado en tránsito, el sistema deberá marcar el traslado como de pedido anulado, para que al recibirse sus unidades queden libres según TRA-17. | RF16 |
| TRA-20 | No deseado | Si una solicitud de traslado tiene el mismo origen y destino, una cantidad que no es positiva, una edición no impresa, una cantidad mayor que el vendible del origen o, para un encargado, un destino distinto de su sucursal, entonces el sistema deberá rechazarla. | RF16, RNF05 |
| TRA-21 | Evento | Cuando se crea un traslado vinculado a un pedido, el sistema deberá guardar la fecha estimada de llegada calculada con el plazo de su ruta en ese momento, sin cambiarla si la ruta cambia después, o indicar que está por confirmar si la ruta no está definida. | RF15, RF16 |
| TRA-22 | Estado | Mientras un traslado vinculado a un pedido lleva más de 48 horas solicitado sin aprobarse, el sistema deberá avisar a la administración central y a logística, que pueden aprobarlo o anular el pedido. | RF16, RF07 |
| TRA-23 | No deseado | Si el despacho de un traslado vinculado y la anulación de su pedido ocurren a la vez, entonces el sistema deberá aplicar solo la primera que confirma según SEL-29 y rechazar la otra por estado inválido. | AC01, RNF01 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-TRA-01 | TRA-01, TRA-02 | **Dado** una edición con vendible solo en Lima y Cusco, **cuando** el encargado de Ayacucho solicita un traslado, **entonces** solo puede elegir Lima o Cusco como origen y el traslado nace solicitado. |
| AC-TRA-02 | TRA-03 | **Dado** un traslado solicitado, **cuando** alguien intenta despacharlo sin aprobarlo, **entonces** se rechaza y el estado no cambia. |
| AC-TRA-03 | TRA-04, TRA-09 | **Dado** un traslado solicitado a Ayacucho como origen, **cuando** su encargado lo aprueba, **entonces** pasa a aprobado; y si lo intenta el encargado de otra sucursal, se rechaza y queda un acceso denegado. |
| AC-TRA-04 | TRA-05 | **Dado** un traslado solicitado, **cuando** el origen lo rechaza con el motivo «sin stock», **entonces** pasa a rechazado con ese motivo. |
| AC-TRA-05 | TRA-06 | **Dado** un traslado aprobado no vinculado a un pedido, de 3 unidades y un origen con vendible 0, **cuando** se intenta despachar, **entonces** se rechaza indicando que el origen no tiene stock vendible suficiente; y **dado** uno vinculado a un pedido cuya reserva confirmada ya no existe, **cuando** se intenta despachar, **entonces** también se rechaza. |
| AC-TRA-06 | TRA-07 | **Dado** un traslado aprobado de 4 unidades y un origen con vendible 12, **cuando** se despacha, **entonces** pasa a en tránsito y el físico del origen baja en 4. |
| AC-TRA-07 | TRA-08 | **Dado** un traslado en tránsito de 4 unidades, **cuando** el destino lo recibe, **entonces** pasa a recibido y el físico del destino sube en 4. |
| AC-TRA-08 | TRA-10 | **Dado** un traslado, **cuando** cambia de estado, **entonces** se envía un aviso a la sucursal de origen y a la de destino; y si el notificador está caído, el cambio se aplica y el aviso queda pendiente. |
| AC-TRA-09 | TRA-11 | **Dado** un traslado vinculado a un pedido de recojo, **cuando** se recibe, **entonces** el E-commerce es informado de que el pedido puede prepararse y la sucursal de destino recibe el aviso. |
| AC-TRA-10 | TRA-12 | **Dado** un traslado en tránsito, **cuando** logística registra la guía CA-771204, **entonces** el traslado muestra la guía y su estado no cambia. |
| AC-TRA-11 | TRA-13 | **Dado** cualquier cambio de estado de un traslado, **cuando** se aplica, **entonces** existe una entrada de auditoría con el usuario, la ruta y el nuevo estado. |
| AC-TRA-12 | TRA-06, TRA-07 | **Dado** un traslado aprobado de 1 unidad vinculado a un pedido, con origen de físico 1, reservado 1 y vendible 0, **cuando** el origen lo despacha, **entonces** pasa a en tránsito y el origen queda con físico 0 y reservado 0. |
| AC-TRA-13 | TRA-14 | **Dado** un traslado en tránsito de 1 unidad vinculado a un pedido activo, **cuando** el destino lo recibe, **entonces** el físico del destino sube en 1, esa unidad queda reservada para el pedido y el vendible del destino no cambia. |
| AC-TRA-14 | TRA-16 | **Dado** un traslado solicitado vinculado a un pedido pagado, **cuando** el origen intenta rechazarlo, **entonces** se rechaza la acción con el motivo y el traslado sigue solicitado; y si es una reposición sin pedido, el origen sí puede rechazarlo. |
| AC-TRA-15 | TRA-15 | **Dado** un traslado aprobado vinculado a un pedido, **cuando** la administración anula el pedido, **entonces** el traslado pasa a cancelado con ese motivo y no se puede despachar. |
| AC-TRA-16 | TRA-17 | **Dado** un traslado en tránsito de un pedido que se anuló, **cuando** el destino lo recibe, **entonces** el físico del destino sube en la cantidad y esas unidades quedan vendibles. |
| AC-TRA-17 | TRA-18 | **Dado** un traslado aprobado de reposición, sin pedido, **cuando** el origen lo cancela con el motivo «error de pedido», **entonces** pasa a cancelado y ya no se puede despachar; y si lo intenta cancelar la sucursal de destino, se rechaza. |
| AC-TRA-18 | TRA-19 | **Dado** un traslado vinculado en tránsito, **cuando** la administración anula el pedido, **entonces** el traslado queda marcado como de pedido anulado y, al recibirse, las unidades quedan libres en el destino. |
| AC-TRA-19 | TRA-20 | **Dado** una solicitud con origen igual al destino, otra de cantidad 0, otra de una edición digital, otra mayor que el vendible del origen y otra de un encargado hacia otra sucursal, **cuando** se envían, **entonces** las cinco se rechazan. |
| AC-TRA-20 | TRA-21 | **Dado** un traslado vinculado con una ruta de 2 días, **cuando** se crea y luego la ruta cambia a 5 días, **entonces** la fecha estimada sigue siendo la de 2 días; y con una ruta sin definir, figura por confirmar. |
| AC-TRA-21 | TRA-22 | **Dado** un traslado vinculado solicitado hace 49 horas, **cuando** corre la revisión de plazos, **entonces** la administración y logística reciben un aviso; y uno de 47 horas no genera aviso. |
| AC-TRA-22 | TRA-23 | **Dado** un traslado aprobado vinculado, **cuando** el despacho y la anulación del pedido ocurren a la vez, **entonces** se aplica solo una; si gana el despacho, se rechaza la anulación del traslado; si gana la anulación, se rechaza el despacho. |
| AC-TRA-23 | TRA-06, TRA-03 | **Dado** un origen con vendible 3 y dos traslados aprobados de 2 unidades, **cuando** se despachan a la vez, **entonces** solo uno se despacha; y dos aprobaciones o recepciones simultáneas del mismo traslado producen una sola transición. |
| AC-TRA-24 | TRA-08, TRA-14 | **Dado** un traslado vinculado en tránsito, **cuando** se recibe a la vez que otro cliente reserva esa edición en el destino, **entonces** las unidades recibidas quedan reservadas para el pedido y el otro cliente no puede tomarlas. |

## 5. Reglas e invariantes

- Ciclo de vida: solicitado → aprobado → en tránsito → recibido; solicitado → rechazado (solo si no está vinculado a un pedido); solicitado o aprobado → cancelado (al anularse el pedido vinculado, o por el origen o la administración si no está vinculado). No hay otras transiciones.
- El stock físico sale del origen al despachar y entra al destino al recibir; mientras viaja, no pertenece a ninguna fuente.
- Un traslado no vinculado solo se despacha si el origen tiene stock vendible suficiente en ese momento.
- Un traslado vinculado a un pedido mueve su reserva con las unidades: la reserva confirmada del origen se consume al despachar y se recrea en el destino al recibir; el vendible de ambas fuentes no cambia por ese movimiento, de modo que otro cliente nunca puede tomar esas unidades.
- Un traslado vinculado a un pedido pagado no se rechaza: el origen solo lo aprueba o la administración anula el pedido.
- La notificación nunca bloquea una transición (`transversal-notificaciones`).

## 6. Casos límite y errores

- Dos despachos simultáneos que juntos superan el vendible del origen: el segundo se rechaza.
- Si el despacho y la anulación del pedido compiten, rige SEL-29: gana la primera que confirma.
- Un traslado vinculado que el origen no aprueba no se rechaza ni se detiene: a las 48 horas se avisa a la administración y a logística (TRA-22).
- Un pedido anulado mientras su traslado ya está en tránsito no detiene el traslado: al recibirse, las unidades quedan como stock libre del destino (TRA-17).
- Un pedido con unidades que llegan por traslado y se anula después de recibirlas se libera como cualquier reserva confirmada en el destino (`inventario-seleccion-fuente`, SEL-07).
- Un traslado en tránsito que nunca llega queda visible para logística y administración; la resolución manual está fuera de esta versión.
- Una recepción repetida del mismo traslado no suma dos veces.

## 7. Preguntas abiertas

- [x] ¿Se permite cancelar un traslado ya aprobado antes de despacharse? Resuelto el 2026-10-16 (confirmado por el responsable): sí, por el origen o la administración, con motivo, si no está vinculado a un pedido (TRA-18).
- [x] ¿Qué se hace con un traslado en tránsito que se pierde? Resuelto el 2026-10-16: fuera de esta versión; queda visible para logística y administración. Se corrigió la propuesta original de usar una merma en el origen, porque el físico ya salió del origen al despachar y se descontaría dos veces.
