---
id: ecommerce-envios
titulo: Guías de envío a domicilio
estado: Draft
servicio: E-commerce
prefijo: ENV
origen: [RF11, RF14, NN11, NN03]
calidad: [AC03, RNF05]
depende: [ecommerce-pedidos, ecommerce-carrito-checkout, inventario-seleccion-fuente, identidad-seguridad, transversal-notificaciones, transversal-auditoria, transversal-seguridad]
---

# Spec — Guías de envío a domicilio

## 1. Propósito

Completar el ciclo de entrega fuera de la tienda: cuando un pedido es a domicilio, logística genera la guía de envío con un operador externo y da seguimiento hasta la entrega (NN11).

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Creación del envío, generación de la guía, seguimiento y entrega. | El transporte físico y los sistemas internos del operador. |
| Avisos al cliente en cada hito. | Tarifas y costo de despacho (`ecommerce-carrito-checkout`). |
| Solo líneas impresas. | Libros digitales: nunca tienen envío. |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| ENV-01 | Evento | Cuando las líneas impresas de un pedido con despacho a domicilio quedan listas en su fuente, el sistema deberá crear un envío pendiente de guía por cada fuente de la que salen, con las líneas que ya están listas en ella. | RF11 |
| ENV-02 | Evento | Cuando logística solicita la guía de un envío, el sistema deberá pedirla al operador logístico, enviándole solo el nombre, la dirección y el teléfono del destinatario, y registrar su número, el operador y el responsable en la auditoría. | RF11, RNF08 |
| ENV-03 | Ubicuo | El sistema deberá gestionar los estados pendiente de guía, guía generada, en reparto y entregado, y permitir solo las transiciones en ese orden, además de cancelado desde pendiente de guía o guía generada. | RF11 |
| ENV-04 | No deseado | Si el operador no responde o rechaza la solicitud, entonces el sistema deberá mantener el envío pendiente, mostrar el error y permitir reintentar. | RF11, AC03 |
| ENV-05 | Evento | Cuando un cliente consulta su pedido a domicilio, el sistema deberá mostrar el estado del envío y su número de guía. | RF11, NN03 |
| ENV-06 | Evento | Cuando el envío pasa a guía generada, en reparto o entregado, el sistema deberá notificar al cliente con un solo aviso por cambio, que reemplaza al aviso genérico de cambio de línea de `ecommerce-pedidos` (que sí se conserva para la sucursal de la fuente), y avisarle también cuando el envío pasa a cancelado. | RF11, NN03 |
| ENV-07 | Evento | Cuando un envío pasa a entregado, el sistema deberá marcar sus líneas como entregadas, con la fecha de esa entrega, y dejar que el estado del pedido se derive según `ecommerce-pedidos`. | RF11 |
| ENV-08 | Ubicuo | El sistema deberá permitir gestionar envíos solo a logística y a la administración central. | RNF05 |
| ENV-09 | Ubicuo | El sistema deberá excluir de los envíos las líneas digitales. | RF11 |
| ENV-10 | No deseado | Si la dirección de entrega está incompleta, entonces el sistema deberá rechazar la generación de la guía indicando qué falta. | RF11 |
| ENV-11 | Evento | Cuando un envío pasa a en reparto, el sistema deberá pedir al inventario que descuente el stock físico de sus líneas impresas y cierre sus reservas confirmadas. | RF11, RF14 |
| ENV-12 | No deseado | Si el inventario no responde o rechaza el descuento al pasar a en reparto, entonces el sistema deberá mantener el envío en guía generada, mostrar el error y permitir reintentar. | RF11, AC03 |
| ENV-13 | Evento | Cuando se anula un pedido con envíos, el sistema deberá cancelar los envíos que aún no están en reparto, mantener los que ya salieron y registrarlo en la auditoría. | RF11, RNF08 |
| ENV-14 | Evento | Cuando logística marca un envío como entregado, el sistema deberá registrar la fecha y el responsable en la auditoría. | RF11, RNF08 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-ENV-01 | ENV-01, ENV-09 | **Dado** un pedido mixto a domicilio con sus impresos listos, **cuando** se prepara el despacho, **entonces** se crea un envío pendiente que incluye solo las líneas impresas; y si salen de dos fuentes distintas, se crean dos envíos, uno por fuente. |
| AC-ENV-02 | ENV-02 | **Dado** un envío pendiente, **cuando** logística solicita la guía, **entonces** el operador devuelve un número que se registra junto con su nombre. |
| AC-ENV-03 | ENV-03 | **Dado** un envío pendiente de guía, **cuando** alguien intenta marcarlo en reparto, **entonces** se rechaza. |
| AC-ENV-04 | ENV-04 | **Dado** que el operador no responde, **cuando** logística solicita la guía, **entonces** el envío sigue pendiente, se muestra el error y puede reintentar. |
| AC-ENV-05 | ENV-05 | **Dado** un pedido a domicilio con guía CA-882013, **cuando** el cliente lo consulta, **entonces** ve el estado del envío y la guía. |
| AC-ENV-06 | ENV-06 | **Dado** un envío, **cuando** pasa a en reparto, **entonces** el cliente recibe un aviso. |
| AC-ENV-07 | ENV-07 | **Dado** un pedido con dos envíos en reparto, **cuando** se entrega el primero, **entonces** sus líneas quedan entregadas con su fecha y ya se puede pedir su devolución, y el pedido sigue en curso; y cuando se entrega el segundo, el pedido queda entregado. |
| AC-ENV-08 | ENV-08 | **Dado** un cliente o un encargado de sucursal, **cuando** intenta gestionar un envío, **entonces** se rechaza por falta de permiso. |
| AC-ENV-09 | ENV-10 | **Dado** un envío con la dirección vacía, **cuando** logística solicita la guía, **entonces** se rechaza indicando que falta la dirección. |
| AC-ENV-10 | ENV-11 | **Dado** un envío con guía generada de una línea impresa con reserva confirmada, **cuando** logística lo pasa a en reparto, **entonces** el inventario descuenta el físico de esa línea, cierra la reserva y la línea queda despachada. |
| AC-ENV-11 | ENV-12 | **Dado** que el inventario no responde, **cuando** logística pasa el envío a en reparto, **entonces** el envío sigue en guía generada, se muestra el error y al reintentar con el inventario disponible el descuento se aplica una sola vez. |
| AC-ENV-12 | ENV-13, ENV-03 | **Dado** un pedido con un envío en reparto y otro pendiente de guía, **cuando** se anula el pedido, **entonces** el pendiente pasa a cancelado, el que está en reparto sigue y queda una entrada de auditoría; y un envío entregado no puede cancelarse. |
| AC-ENV-13 | ENV-14 | **Dado** un envío en reparto, **cuando** logística lo marca entregado, **entonces** queda la fecha de entrega y una entrada de auditoría con el responsable. |
| AC-ENV-15 | ENV-02 | **Dado** un envío pendiente, **cuando** dos solicitudes de guía llegan a la vez, **entonces** se genera una sola guía; y al operador solo se le envían el nombre, la dirección y el teléfono del destinatario. |
| AC-ENV-16 | ENV-11, ENV-12 | **Dado** un envío con guía generada, **cuando** se pasa dos veces a en reparto, **entonces** el físico se descuenta una sola vez. |
## 5. Reglas e invariantes

- Estados en orden: pendiente de guía → guía generada → en reparto → entregado.
- El envío pertenece a un pedido y solo contiene líneas impresas de una misma fuente; un pedido puede tener varios envíos, cada uno con su guía.
- Las líneas de una misma fuente que quedan listas en momentos distintos forman envíos distintos, cada uno con su guía.
- El número de guía lo asigna el operador y no se modifica.
- El operador logístico es un tercero que recibe datos personales mínimos; la integración se autentica según `transversal-seguridad` y se documenta en la privacidad.
- Un envío entregado no vuelve a otro estado.

## 6. Casos límite y errores

- Si el pedido se anula, se cancelan los envíos que no salieron (ENV-13); la guía ya generada de un envío cancelado queda sin uso y logística avisa al operador.
- Un envío que el operador devuelve (no entregado) queda a revisión manual; la resolución completa está fuera de esta versión.
- Dos solicitudes de guía para el mismo envío no generan dos guías.
- Pasar dos veces el mismo envío a en reparto no descuenta el físico dos veces.

## 7. Preguntas abiertas

- Nota para el plan: el operador logístico real no está elegido (en el prototipo es un operador ejemplo); no cambia el comportamiento y se define antes del plan.
- [x] ¿La «entrega» la confirma el operador o la marca logística manualmente? Resuelto el 2026-10-16 (confirmado por el responsable): manual en esta versión.
- [x] ¿Un pedido a domicilio con líneas de fuentes distintas genera un envío por fuente? Resuelto el 2026-10-16: sí, cada envío con su guía y su descuento de físico en su fuente; el pedido queda entregado cuando todos lo están (ENV-01, ENV-07).
