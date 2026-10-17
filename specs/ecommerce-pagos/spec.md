---
id: ecommerce-pagos
titulo: Pagos con Yape y tarjeta
estado: Approved
servicio: E-commerce
prefijo: PAG
origen: [RF05, RF07, NN02]
calidad: [AC01, AC03, AC05, AC13, RNF06, RNF07, RNF08, RNF21]
depende: [ecommerce-carrito-checkout, identidad-seguridad, inventario-seleccion-fuente, transversal-auditoria]
aprobado_por: Diego Amiquero
aprobado_en: 2026-10-16
---

# Spec — Pagos con Yape y tarjeta

## 1. Propósito

Cobrar el pedido una sola vez y con seguridad, con Yape o tarjeta, sin que el sistema vea ni guarde datos de tarjeta. El cobro confirma las reservas y concede los accesos digitales.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Cobro único por pedido, idempotencia, rechazos, reintentos y reembolsos solicitados. | El carrito y la reserva previa (`ecommerce-carrito-checkout`). |
| Confirmación del pedido al aprobarse el pago. | Emisión de comprobantes electrónicos (descartada, proyecto académico). |
| Conciliación ante respuestas dudosas de la pasarela. | Decidir cuándo corresponde un reembolso (`ecommerce-devoluciones`, `ecommerce-pedidos`). |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| PAG-01 | Ubicuo | El sistema deberá cobrar cada compra una sola vez mediante una pasarela de pago externa. | RF05 |
| PAG-02 | Ubicuo | El sistema deberá recibir de la pasarela únicamente un identificador del medio de pago, sin procesar ni guardar números de tarjeta ni códigos de seguridad. | RNF07, AC05 |
| PAG-03 | Ubicuo | El sistema deberá permitir pagar con Yape o con tarjeta. | RF05 |
| PAG-04 | Evento | Cuando se repite una solicitud de cobro con la misma clave de idempotencia de un intento, el sistema deberá devolver el resultado original sin cobrar otra vez. | RF05 |
| PAG-05 | Evento | Cuando la pasarela aprueba el pago, el sistema deberá crear el pedido confirmado, pedir la confirmación de las reservas de impresos y pedir la concesión de los accesos a los libros digitales. | RF05, NN02 |
| PAG-06 | No deseado | Si la pasarela rechaza el pago, entonces el sistema deberá informar el motivo, no registrar ningún cobro, mantener la reserva hasta su expiración y permitir reintentar con otro medio. | RF05 |
| PAG-07 | No deseado | Si la pasarela no responde en 10 segundos o la respuesta es dudosa, entonces el sistema deberá dejar el pago pendiente de confirmación, consultar su estado más tarde y no cobrar de nuevo. | RF05 |
| PAG-08 | No deseado | Si la reserva de la compra expiró o la intención de pago fue cancelada, entonces el sistema deberá negarse a iniciar el cobro. | RF05, AC01 |
| PAG-09 | Ubicuo | El sistema deberá cobrar exactamente el total del pedido, incluido el envío, en soles. | RF05 |
| PAG-10 | Evento | Cuando se solicita un reembolso total o parcial, el sistema deberá pedirlo a la pasarela y registrar su trazabilidad. | RF05 |
| PAG-11 | Evento | Cuando se intenta un cobro, el sistema deberá registrar el intento y su resultado sin datos sensibles. | RNF08 |
| PAG-12 | Ubicuo | El sistema deberá obtener las claves de la pasarela de la configuración del entorno y nunca del código fuente. | RNF06 |
| PAG-13 | Evento | Cuando un pago queda pendiente de confirmación, el sistema deberá pedir al inventario que extienda la reserva de la compra hasta 30 minutos desde que se inició el pago. | RF05, AC01 |
| PAG-14 | Evento | Cuando un pago queda pendiente de confirmación, y cuando se resuelve como aprobado, rechazado o vencido, el sistema deberá informarlo al cliente en pantalla y por correo, sin datos de pago en el mensaje. | RF05, RF07 |
| PAG-15 | No deseado | Si un pago pendiente se aprueba cuando ya venció (la reserva expiró o pasaron 30 minutos), entonces el sistema deberá reembolsarlo automáticamente, no confirmar el pedido y avisar al cliente. | RF05, AC01 |
| PAG-16 | Evento | Cuando el cliente inicia el pago, el sistema deberá crear una intención de pago con identificador único, con un intento nuevo y una clave de idempotencia propia por cada medio de pago que pruebe. | RF05, AC01 |
| PAG-17 | No deseado | Si una intención de pago ya tiene un cobro pendiente de confirmación o aprobado, entonces el sistema deberá rechazar otro cobro de esa intención, aunque llegue desde otra pestaña o dispositivo. | RF05, AC01 |
| PAG-18 | Estado | Mientras un pago de una compra sin impresos está pendiente de confirmación, el sistema deberá consultar su estado cada minuto hasta los 30 minutos desde su inicio y, pasado ese plazo, marcarlo como vencido. | RF05 |
| PAG-19 | Ubicuo | El sistema deberá confirmar con la pasarela el estado de un pago informado por una notificación entrante antes de darlo por aprobado, además de verificar su firma. | RF05, RNF21 |
| PAG-20 | No deseado | Si falla la confirmación de las reservas o la concesión de un acceso después de aprobarse el pago, entonces el sistema deberá liberar las reservas, revocar los accesos concedidos y reembolsar el total, dejando el reembolso pendiente visible a la administración y reintentable hasta completarse. | RF05, AC01 |
| PAG-21 | No deseado | Si el correo de la cuenta no está verificado, entonces el sistema deberá negarse a cobrar. | RF05, AC13 |
| PAG-22 | Evento | Cuando un pago queda pendiente o se resuelve y el cliente dio su consentimiento, el sistema deberá avisarle además por WhatsApp. | RF07 |
| PAG-23 | No deseado | Si falla un aviso del estado del pago, entonces el sistema deberá mantener el estado del cobro y no interrumpirlo. | RF07, AC03 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-PAG-01 | PAG-01, PAG-03, PAG-09 | **Dado** una compra de S/ 123,90 con envío, **cuando** el cliente paga con Yape, **entonces** se cobra una sola vez S/ 123,90 en soles; y lo mismo ocurre al pagar con una tarjeta aprobada. |
| AC-PAG-02 | PAG-02 | **Dado** un pago con tarjeta, **cuando** se procesa, **entonces** el sistema solo recibe el identificador del medio de pago y ningún dato de tarjeta aparece en su almacenamiento ni en sus registros. |
| AC-PAG-03 | PAG-04 | **Dado** una solicitud de cobro con una clave de idempotencia, **cuando** el cliente la repite por una doble pulsación, **entonces** se devuelve el mismo resultado y se cobra una sola vez. |
| AC-PAG-04 | PAG-05 | **Dado** un pedido mixto, **cuando** la pasarela aprueba el pago, **entonces** el pedido queda confirmado, las reservas pasan a confirmadas y el libro digital aparece en la biblioteca. |
| AC-PAG-05 | PAG-06, PAG-16 | **Dado** una tarjeta rechazada, **cuando** la pasarela responde el rechazo, **entonces** el cliente ve el motivo, no hay cobro y puede reintentar con Yape mientras la reserva siga vigente, en un intento nuevo con clave nueva de la misma intención. |
| AC-PAG-06 | PAG-07 | **Dado** que la pasarela no responde, **cuando** pasan 10 segundos, **entonces** el pago queda pendiente de confirmación, se consulta su estado más tarde y no se duplica el cobro. |
| AC-PAG-07 | PAG-08 | **Dado** una reserva expirada, **cuando** el cliente intenta pagar, **entonces** el cobro se niega y se invita a reiniciar el checkout. |
| AC-PAG-08 | PAG-10 | **Dado** un pedido pagado, **cuando** se solicita el reembolso de una línea, **entonces** se pide a la pasarela por el monto de esa línea y queda la trazabilidad. |
| AC-PAG-09 | PAG-11 | **Dado** un cobro rechazado, **cuando** ocurre, **entonces** queda un registro del intento y su resultado sin datos de tarjeta. |
| AC-PAG-10 | PAG-12 | **Dado** el repositorio, **cuando** se revisa, **entonces** no contiene claves de la pasarela y el servicio no arranca si falta la configuración. |
| AC-PAG-11 | PAG-13 | **Dado** un pago iniciado hace 14 minutos que acaba de quedar pendiente de confirmación, **cuando** el pago queda pendiente, **entonces** la reserva se extiende hasta los 30 minutos desde el inicio del pago; y si a los 30 minutos sigue pendiente, la reserva expira. |
| AC-PAG-12 | PAG-14, PAG-22 | **Dado** un pago que queda pendiente y luego se aprueba, **cuando** ocurre cada paso, **entonces** el cliente ve el estado en pantalla y recibe un correo sin datos de pago; y si dio su consentimiento, también un aviso por WhatsApp. |
| AC-PAG-13 | PAG-15 | **Dado** un pago pendiente cuya reserva expiró a los 30 minutos, o un pago solo digital vencido, **cuando** la pasarela lo aprueba, **entonces** se reembolsa automáticamente, el pedido no se confirma y el cliente recibe el aviso. |
| AC-PAG-14 | PAG-16, PAG-17 | **Dado** un cliente con la compra abierta en dos pestañas, **cuando** pulsa pagar en ambas a la vez, **entonces** solo se crea un cobro y el segundo se rechaza; y tras un rechazo puede reintentar con otro medio de pago. |
| AC-PAG-15 | PAG-18 | **Dado** un pago solo digital pendiente, **cuando** pasan 30 minutos sin confirmación, **entonces** se marca como vencido y se consultó su estado cada minuto. |
| AC-PAG-16 | PAG-19 | **Dado** una notificación entrante de pago aprobado con firma válida, **cuando** llega, **entonces** se confirma el estado con la pasarela antes de aprobar el pedido; y con firma inválida, se rechaza y se registra. |
| AC-PAG-17 | PAG-20 | **Dado** un pago aprobado de un pedido mixto, **cuando** falla la concesión del acceso digital, **entonces** se liberan las reservas, se revoca lo concedido y se reembolsa el total; y si el reembolso también falla, queda pendiente, visible para la administración, y al reintentar se reembolsa una sola vez. |
| AC-PAG-18 | PAG-21 | **Dado** un cliente con el correo sin verificar, **cuando** se solicita el cobro de una compra solo digital, **entonces** el servicio se niega a cobrar. |
| AC-PAG-19 | PAG-23 | **Dado** que el servicio de correo falla, **cuando** un pago se aprueba, **entonces** el cobro y el pedido se completan y el aviso queda pendiente. |

## 5. Reglas e invariantes

- Una intención de pago tiene como máximo un cobro aprobado; el pedido nace al aprobarse y hereda ese cobro.
- La confirmación del pago, de las reservas y de los accesos digitales es una sola unidad de negocio: si una parte falla, se compensa (PAG-20).
- Cada intento de cobro lleva su propia clave de idempotencia, generada por el sistema; un reintento con otro medio es un intento nuevo de la misma intención.
- «Inicio del pago» es el momento en que el cliente pasa al paso de pago y se crea la intención; desde ahí corren los 15 y 30 minutos.
- Las comisiones de la pasarela no forman parte del precio al cliente.

## 6. Casos límite y errores

- Un pago aprobado cuya confirmación de reserva falla se reembolsa automáticamente y se avisa al cliente (PAG-20).
- Un reembolso de un pedido a domicilio incluye el envío solo si se anula o cancela todo antes de que salga (`ecommerce-pedidos`).
- Un pago pendiente de confirmación prolonga la reserva una sola vez, hasta 30 minutos desde el inicio del pago; pasado ese plazo, la reserva expira aunque la pasarela no haya respondido (PAG-15).
- Los avisos del estado del pago salen por todos los medios disponibles; el correo es transaccional y el de WhatsApp exige consentimiento.
- Un reembolso parcial no puede superar el monto cobrado de esa línea.
- Una pasarela caída no bloquea el catálogo ni la biblioteca.

## 7. Preguntas abiertas

- [x] ¿Cuánto se espera la respuesta de la pasarela antes de pasar a «pendiente de confirmación»? Resuelto el 2026-10-16 (confirmado por el responsable): 10 segundos, igual que el tiempo máximo de SEG-14.
- Nota para el plan: confirmar con la documentación de Culqi si admite una clave de idempotencia propia y cómo informa los reembolsos de Yape (bloqueo B05); no cambia el comportamiento.
