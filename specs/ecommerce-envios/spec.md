---
id: ecommerce-envios
titulo: Guías de envío a domicilio
estado: Planned
aprobado_por: Claude (delegado por Diego Amiquero)
aprobado_en: 2026-11-13
plan_aprobado_por: Claude (delegado por Diego Amiquero)
plan_aprobado_en: 2026-11-18
servicio: E-commerce
prefijo: ENV
origen: [RF11, RF14, RF27, NN11, NN03]
calidad: [AC01, AC03, AC04, RNF05, RNF08, RNF23]
depende: [ecommerce-pedidos, inventario-seleccion-fuente, identidad-seguridad, transversal-notificaciones, transversal-auditoria, transversal-seguridad]
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
| ENV-01 | Evento | Cuando Pedidos informa por su puerto de despacho que las líneas impresas de un pedido a domicilio quedan listas en su fuente, el sistema deberá crear un envío pendiente de guía por cada fuente de la que salen, con las líneas que ya están listas en ella, sin repetir el envío si Pedidos informa dos veces las mismas líneas. | RF11 |
| ENV-02 | Evento | Cuando logística solicita la guía de un envío, el sistema deberá pedirla al operador logístico, enviándole solo el nombre, la dirección y el teléfono de contacto de la entrega que lee del pedido (PED-32), y registrar su número, el operador y el responsable en la auditoría. | RF11, RNF08 |
| ENV-03 | Ubicuo | El sistema deberá gestionar los estados pendiente de guía, guía generada, en reparto y entregado, permitir solo las transiciones en ese orden, además de cancelado desde pendiente de guía o guía generada, y rechazar cualquier otra por estado inválido. | RF11 |
| ENV-04 | No deseado | Si el operador no responde, rechaza la solicitud o devuelve una respuesta inválida, entonces el sistema deberá mantener el envío pendiente sin número, sin auditoría ni aviso, mostrar el error sin datos de contacto y permitir reintentar. | RF11, AC03 |
| ENV-05 | Evento | Cuando el titular de un pedido a domicilio lo consulta, el sistema deberá mostrar el estado del envío y su número de guía, o «pendiente de guía» si aún no la tiene, y rechazar a cualquier otro cliente con la misma respuesta exista o no el pedido (PED-41). | RF11, NN03, RNF05 |
| ENV-06 | Evento | Cuando el envío pasa a guía generada o a en reparto, el sistema deberá avisar al cliente con el número de guía, además del aviso de cambio de línea que envía `ecommerce-pedidos` (PED-05), sin que una falla del Notificador interrumpa el cambio. | RF11, NN03 |
| ENV-07 | Evento | Cuando un envío pasa a entregado, el sistema deberá marcar sus líneas como entregadas con la fecha de esa entrega y dejar que el estado del pedido se derive según `ecommerce-pedidos`. | RF11 |
| ENV-08 | Ubicuo | El sistema deberá permitir ver envíos, pedir la guía, pasar a en reparto y marcar entregado solo a logística y a la administración central, y rechazar a otros usuarios y a visitantes registrándolo como acceso denegado. | RNF05, AC04 |
| ENV-09 | Ubicuo | El sistema deberá excluir de los envíos las líneas digitales y rechazar una que Pedidos informe. | RF11 |
| ENV-10 | No deseado | Si falta el nombre, la dirección o el teléfono del contacto del pedido, o ya está anonimizado, entonces el sistema deberá rechazar la generación de la guía indicando el campo faltante o la anonimización, sin llamar al operador. | RF11 |
| ENV-11 | Evento | Cuando un envío pasa a en reparto, el sistema deberá pedir al inventario que descuente el stock físico de todas sus líneas impresas y cierre sus reservas confirmadas, y marcar sus líneas como despachadas en `ecommerce-pedidos`. | RF11, RF14 |
| ENV-12 | No deseado | Si el inventario no responde o rechaza el descuento de alguna línea, entonces el sistema deberá mantener el envío en guía generada, sin descontar ni despachar ninguna línea, mostrar el error y permitir reintentar, tratando como ya aplicado el descuento que el inventario informe como hecho para ese envío. | RF11, AC03 |
| ENV-13 | Evento | Cuando Pedidos pide por su puerto de despacho cancelar los envíos de un pedido anulado, el sistema deberá cancelar los que aún no están en reparto, mantener los que ya salieron y registrarlo en la auditoría. | RF11, RNF08 |
| ENV-14 | Evento | Cuando logística marca un envío como entregado, el sistema deberá registrar la fecha y el responsable en la auditoría. | RF11, RNF08 |
| ENV-15 | Ubicuo | El sistema deberá guardar de un envío solo el pedido, la fuente, las líneas, la guía, el operador, la clave de solicitud, el estado y las fechas, sin nombre, dirección ni teléfono del destinatario, y conservarlos al cancelar la cuenta. | RF11, RF27, RNF23 |
| ENV-17 | Ubicuo | El sistema deberá excluir el nombre, la dirección y el teléfono del destinatario de la auditoría, los registros técnicos de la integración con el operador y los mensajes de error, y registrar al pedir la guía solo el número, el operador y el responsable. | RF11, RNF08, RNF23 |
| ENV-18 | No deseado | Si dos solicitudes de guía de un envío llegan a la vez, o falla la respuesta o la auditoría después de que el operador creó la guía, entonces el sistema deberá generar una sola guía, recuperando la ya creada al reintentar con la misma clave de solicitud y dejando el envío pendiente sin número hasta registrarla. | RF11, AC01, AC03 |
| ENV-19 | No deseado | Si la cancelación de un envío y su paso a en reparto ocurren a la vez, entonces el sistema deberá aplicar solo la primera que confirma y rechazar la otra por estado inválido, sin efectos parciales. | RF11, AC01 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-ENV-01 | ENV-01, ENV-09 | **Dado** un pedido mixto a domicilio con sus impresos listos, otro de recojo y una línea digital que Pedidos informe por error, **cuando** se prepara el despacho, **entonces** se crea un envío pendiente solo con las líneas impresas, y si salen de dos fuentes se crean dos envíos; el de recojo no crea envío; la línea digital se rechaza; y repetir el aviso de las mismas líneas no crea un envío duplicado. |
| AC-ENV-02 | ENV-02 | **Dado** un envío pendiente, **cuando** logística solicita la guía, **entonces** el operador devuelve un número que se registra junto con su nombre, y la auditoría guarda el número, el operador y el responsable. |
| AC-ENV-03 | ENV-03 | **Dado** un envío pendiente de guía, uno entregado y uno cancelado, **cuando** alguien intenta marcar el primero en reparto, retroceder el segundo o pedir guía sobre el tercero, **entonces** se rechaza cada intento por estado inválido. |
| AC-ENV-04 | ENV-04 | **Dado** que el operador no responde, otro que rechaza la solicitud y otro que devuelve una respuesta inválida, **cuando** logística solicita la guía, **entonces** el envío sigue pendiente sin número, sin auditoría ni aviso, se muestra el error sin datos de contacto y puede reintentar. |
| AC-ENV-05 | ENV-05 | **Dado** un pedido a domicilio con guía CA-882013, otro sin guía y un cliente ajeno, **cuando** el titular los consulta y el ajeno pide uno de ellos, **entonces** el titular ve el estado y la guía, o «pendiente de guía»; y el ajeno recibe la misma respuesta de rechazo exista o no el pedido. |
| AC-ENV-06 | ENV-06 | **Dado** un envío, **cuando** pasa a guía generada, a en reparto, a entregado y a cancelado, **entonces** Envíos avisa al cliente con la guía solo en guía generada y en reparto, además del aviso de línea de Pedidos, y no avisa por su cuenta en entregado ni en cancelado; ningún aviso lleva datos de contacto; y si el Notificador no responde, el cambio se aplica y el aviso queda pendiente. |
| AC-ENV-07 | ENV-07 | **Dado** un pedido con dos envíos en reparto, **cuando** se entrega el primero, **entonces** sus líneas quedan entregadas con su fecha y el pedido sigue en curso; y cuando se entrega el segundo, el pedido queda entregado. |
| AC-ENV-08 | ENV-08 | **Dado** un cliente, un encargado de sucursal y un visitante, **cuando** intentan ver envíos, pedir la guía, pasar a en reparto o marcar entregado, **entonces** se rechaza cada acción por falta de permiso y queda un acceso denegado; y logística y la administración central pueden hacerlas. |
| AC-ENV-09 | ENV-10 | **Dado** un envío con la dirección vacía, otro con el teléfono vacío, otro con el nombre vacío, otro con el contacto ya anonimizado y otro completo, **cuando** logística solicita la guía de cada uno, **entonces** los cuatro primeros se rechazan indicando el campo faltante o la anonimización sin llamar al operador, y el completo genera la guía. |
| AC-ENV-10 | ENV-11 | **Dado** un envío con guía generada de una línea impresa con reserva confirmada, **cuando** logística lo pasa a en reparto, **entonces** el inventario descuenta el físico de esa línea, cierra la reserva y la línea queda despachada en Pedidos. |
| AC-ENV-11 | ENV-12 | **Dado** que el inventario no responde o rechaza una de las dos líneas de un envío, **cuando** logística lo pasa a en reparto, **entonces** el envío sigue en guía generada, ninguna línea queda descontada ni despachada, se muestra el error y al reintentar con el inventario disponible el descuento se aplica una sola vez. |
| AC-ENV-12 | ENV-13, ENV-03 | **Dado** un pedido con un envío en reparto y otro pendiente de guía, **cuando** Pedidos pide cancelar los envíos por la anulación, **entonces** el pendiente pasa a cancelado, el que está en reparto sigue y queda una entrada de auditoría; y un envío entregado no puede cancelarse. |
| AC-ENV-13 | ENV-14 | **Dado** un envío en reparto, **cuando** logística lo marca entregado, **entonces** queda la fecha de entrega y una entrada de auditoría con el responsable. |
| AC-ENV-15 | ENV-02, ENV-18 | **Dado** un envío pendiente, **cuando** dos solicitudes de guía llegan a la vez, o el operador crea la guía pero la respuesta se pierde, o no se puede auditar después de crearla, **entonces** se genera una sola guía, el envío queda pendiente sin número hasta registrarla y el reintento recupera la misma guía; y al operador solo se le envían el nombre, la dirección y el teléfono, sin correo, documento ni datos de pago. |
| AC-ENV-16 | ENV-11, ENV-12 | **Dado** un envío con guía generada, **cuando** dos solicitudes lo pasan a en reparto a la vez, **entonces** el físico se descuenta una sola vez y la otra se rechaza por estado inválido. |
| AC-ENV-17 | ENV-15 | **Dado** un cliente con envíos entregados, en reparto y cancelado, **cuando** se ejecuta la cancelación de su cuenta, **entonces** el almacén de Envíos conserva el pedido, la fuente, las líneas, la guía, el operador, los estados y las fechas y no contiene nombre, dirección ni teléfono. |
| AC-ENV-18 | ENV-17 | **Dado** una solicitud de guía fallida y otra exitosa, **cuando** se revisan la auditoría, los registros técnicos de la integración y el mensaje de error, **entonces** ninguno contiene nombre, dirección ni teléfono, y la auditoría solo guarda número, operador y responsable. |
| AC-ENV-19 | ENV-19 | **Dado** un envío con guía generada, **cuando** Pedidos pide cancelarlo a la vez que logística lo pasa a en reparto, **entonces** gana la primera que confirma, la otra se rechaza por estado inválido y no queda un envío cancelado con el físico descontado. |
## 5. Reglas e invariantes

- Estados en orden: pendiente de guía → guía generada → en reparto → entregado.
- El envío pertenece a un pedido y solo contiene líneas impresas de una misma fuente; un pedido puede tener varios envíos, cada uno con su guía.
- Las líneas de una misma fuente que quedan listas en momentos distintos forman envíos distintos, cada uno con su guía.
- El número de guía lo asigna el operador y no se modifica.
- El operador logístico es un tercero que recibe datos personales mínimos; la integración se autentica según `transversal-seguridad` y se documenta en la privacidad.
- Un envío entregado no vuelve a otro estado.
- Envíos implementa el puerto de despacho que define `ecommerce-pedidos` (crear y cancelar envíos) y llama al contrato público de Pedidos para marcar líneas despachadas y entregadas; Pedidos no depende de Envíos. El contacto de entrega vive solo en Pedidos (PED-32) y Envíos lo lee para generar la guía.
- El seguimiento es manual en esta versión: logística marca el paso a en reparto y a entregado; el sistema no consulta al operador.

## 6. Casos límite y errores

- Si el pedido se anula, se cancelan los envíos que no salieron (ENV-13); la guía ya generada de un envío cancelado queda sin uso y logística avisa al operador.
- Un envío que el operador devuelve (no entregado) queda a revisión manual; la resolución completa está fuera de esta versión.
- Dos solicitudes de guía para el mismo envío no generan dos guías.
- Pasar dos veces el mismo envío a en reparto no descuenta el físico dos veces.
- Una falla del Notificador no interrumpe el cambio de estado de un envío: el aviso queda pendiente de reintento.
- Cuando Pedidos pide por su puerto cancelar un envío con guía ya generada, la guía queda sin uso y avisar al operador es una gestión de logística fuera del sistema.
- Tras cancelar la cuenta, el operador conserva lo que recibió al generar la guía; su plazo de conservación y supresión se verifica antes de elegirlo (privacidad §3).

## 7. Preguntas abiertas

- Nota para el plan: el operador logístico real no está elegido (en el prototipo es un operador ejemplo); debe aceptar una clave de solicitud o una consulta por referencia (ENV-18), lo que es criterio de elección, y se define antes del plan.
- [x] ¿La «entrega» la confirma el operador o la marca logística manualmente? Resuelto el 2026-10-16 (confirmado por el responsable): manual en esta versión.
- [x] ¿Un pedido a domicilio con líneas de fuentes distintas genera un envío por fuente? Resuelto el 2026-10-16: sí, cada envío con su guía y su descuento de físico en su fuente; el pedido queda entregado cuando todos lo están (ENV-01, ENV-07).
