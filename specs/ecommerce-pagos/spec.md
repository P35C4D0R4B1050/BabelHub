---
id: ecommerce-pagos
titulo: Pagos con Yape y tarjeta
estado: Approved
aprobado_por: Claude (delegado por Diego Amiquero)
aprobado_en: 2026-11-13
servicio: E-commerce
prefijo: PAG
origen: [RF05, RF07, NN02]
calidad: [AC01, AC03, AC04, RNF06, RNF07, RNF08, RNF21]
depende: [ecommerce-carrito-checkout, identidad-seguridad, inventario-seleccion-fuente, transversal-auditoria]
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
| PAG-02 | Ubicuo | El sistema deberá recibir de la pasarela únicamente un identificador del medio de pago, sin procesar ni guardar números de tarjeta ni códigos de seguridad. | RNF07, AC04 |
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
| PAG-20 | No deseado | Si falla la creación del pedido, la confirmación de las reservas o la concesión de un acceso después de aprobarse el pago, entonces el sistema deberá liberar las reservas, revocar los accesos concedidos y reembolsar el total, dejando el reembolso pendiente visible a la administración y reintentable hasta completarse. | RF05, AC01 |
| PAG-21 | No deseado | Si el correo de la cuenta no está verificado, entonces el sistema deberá negarse a cobrar. | RF05, AC04 |
| PAG-22 | Evento | Cuando un pago queda pendiente o se resuelve y el cliente dio su consentimiento, el sistema deberá avisarle además por WhatsApp. | RF07 |
| PAG-23 | No deseado | Si falla un aviso del estado del pago, entonces el sistema deberá mantener el estado del cobro y no interrumpirlo. | RF07, AC03 |
| PAG-24 | Evento | Cuando Identidad consulta los pendientes de una cuenta, el sistema deberá informar cuántos pagos pendientes de confirmación, intentos sin resultado final y reembolsos pendientes (incluido el de una compensación, PAG-20) tiene esa cuenta, sin datos de pago y sin revelar los de otra cuenta. | RF05, RF27 |
| PAG-25 | No deseado | Si la cuenta del cliente tiene una cancelación diferida (IDE-27), entonces el sistema deberá negarse a iniciar un cobro nuevo e indicar el motivo, sin impedir resolver los pagos ya pendientes. | RF05, RF27 |
| PAG-26 | Evento | Cuando se ejecuta la cancelación de una cuenta, el sistema deberá desvincular de ella los datos de sus intenciones e intentos de pago, eliminando el contacto de entrega que lleven, y conservar solo los importes, fechas, estados e identificadores de la pasarela que exige una obligación legal y, si no puede, informarlo para que no se ejecute la cancelación (IDE-70). | RF05, RF27, RNF23 |
| PAG-27 | Evento | Cuando Pedidos pide liberarlos tras guardar su copia (PED-32), o la intención de pago se resuelve sin pedido (vencida, cancelada o rechazada sin reintento), o pasan 30 días desde su creación, el sistema deberá eliminar de la intención el nombre, el teléfono y la dirección de entrega que recibió del checkout (CAR-30). | RF05, RNF23 |
| PAG-28 | Evento | Cuando la pasarela aprueba el pago, el sistema deberá informar a Carrito y checkout para que elimine los datos de entrega del checkout (CAR-34). | RF05, RNF23 |
| PAG-29 | No deseado | Si un intento de pago se aprueba después de que se desvinculó su intención por la cancelación de la cuenta (PAG-26), entonces el sistema deberá reembolsarlo sin crear el pedido, sin reconstruir datos personales ni avisar al cliente, y dejar el reembolso pendiente, visible para la administración y reintentable hasta completarse (PAG-20). | RF05, RF27, AC01 |
| PAG-30 | Ubicuo | El sistema deberá enviar a la pasarela solo el importe, la moneda, una referencia opaca de la intención y el correo del titular si la pasarela lo exige, y nunca el nombre, el teléfono ni la dirección de entrega, ni guardarlos en el registro de intentos ni en los correos. | RF05, RNF08, RNF23 |
| PAG-31 | No deseado | Si la cuenta del cliente no aceptó la versión vigente de los textos legales (IDE-58), entonces el sistema deberá negarse a iniciar un cobro nuevo e indicar el motivo. | RF05, NN15 |
| PAG-32 | No deseado | Si el pago se aprueba después de que se consultaron los pendientes de la cuenta para ejecutar su cancelación (IDE-70, IDE-71) y antes de que PAG-26 desvincule su intención, entonces el sistema deberá hacer prevalecer la cancelación: compensarlo como en PAG-20 sin crear el pedido, sin avisar al cliente ni leer su correo, y sin que el reembolso resultante difiera la cancelación en curso. | RF05, RF27, AC01 |

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
| AC-PAG-17 | PAG-20 | **Dado** un pago aprobado de un pedido mixto y otro cuyo pedido no se puede crear, **cuando** falla la confirmación de las reservas, la concesión del acceso digital o la creación del pedido, **entonces** se liberan las reservas, se revoca lo concedido y se reembolsa el total; y si el reembolso también falla, queda pendiente, visible para la administración, y al reintentar se reembolsa una sola vez. |
| AC-PAG-18 | PAG-21 | **Dado** un cliente con el correo sin verificar, **cuando** se solicita el cobro de una compra solo digital, **entonces** el servicio se niega a cobrar. |
| AC-PAG-19 | PAG-23 | **Dado** que el servicio de correo falla, **cuando** un pago se aprueba, **entonces** el cobro y el pedido se completan y el aviso queda pendiente. |
| AC-PAG-20 | PAG-24 | **Dado** un cliente con dos pagos pendientes de confirmación y un reembolso pendiente de una compensación (PAG-20), otro con todos sus pagos resueltos y otro con un intento enviado a la pasarela sin respuesta, **cuando** Identidad consulta los pendientes de cada uno, **entonces** el primero figura con dos pagos y un reembolso, el segundo con ninguno y el tercero con un intento sin resultado, sin datos de pago y sin pendientes de otra cuenta. |
| AC-PAG-21 | PAG-25, PAG-31 | **Dado** un cliente con una cancelación diferida y otro que no aceptó la versión vigente de los textos, **cuando** intentan iniciar un cobro, incluso repitiendo una clave de idempotencia cuyo cobro no existía, **entonces** se les niega con el motivo y no se registra ningún cobro (si el cobro ya existía, se devuelve el original sin cobrar de nuevo); y un pago ya pendiente del primero se sigue resolviendo. |
| AC-PAG-22 | PAG-26 | **Dado** un cliente con pagos aprobados y una intención con nombre, teléfono y dirección de entrega, **cuando** se ejecuta la cancelación de su cuenta, **entonces** sus intenciones e intentos dejan de llevar su identificador de cuenta, su correo y el contacto de entrega, y se conservan los importes, fechas, estados e identificadores de la pasarela; y si Pagos no puede hacerlo, se informa para que la cancelación no se ejecute y no queda ningún efecto parcial. |
| AC-PAG-23 | PAG-27 | **Dado** una intención de pago con el nombre, el teléfono y la dirección de entrega, **cuando** Pedidos guarda su copia y pide liberarlos, o la intención se resuelve sin pedido, o pasan 30 días desde su creación, **entonces** esos datos dejan de existir en la intención y el pedido conserva su propia copia (PED-32); y mientras no ocurra ninguno de los tres, incluso a los 29 días y 23 horas, siguen en la intención. |
| AC-PAG-24 | PAG-28 | **Dado** un pago aprobado de una compra a domicilio, **cuando** la pasarela lo confirma, **entonces** Carrito y checkout recibe el aviso y elimina el nombre, el teléfono y la dirección del checkout. |
| AC-PAG-25 | PAG-29 | **Dado** un intento de pago cuya intención ya se desvinculó por la cancelación de la cuenta, **cuando** la pasarela lo aprueba después, **entonces** se reembolsa, no se crea ningún pedido, no se reconstruye ningún dato personal y el reembolso queda pendiente, visible para la administración y reintentable hasta completarse, sin avisar al cliente. |
| AC-PAG-26 | PAG-30 | **Dado** una compra a domicilio con nombre, teléfono y dirección de entrega, **cuando** se cobra, **entonces** la pasarela recibe solo importe, moneda, referencia opaca y, si lo exige, el correo; y el registro de intentos y los correos no llevan el contacto de entrega. |
| AC-PAG-27 | PAG-24 | **Dado** un cliente con un cobro enviado a la pasarela y sin respuesta, **cuando** pide cancelar su cuenta, **entonces** el intento figura como pendiente, la cancelación queda diferida (IDE-27, IDE-71) y no se desvincula ningún dato hasta que se resuelva. |
| AC-PAG-28 | PAG-32 | **Dado** una cancelación, directa o reconfirmada tras diferirse, cuya consulta de pendientes ya pasó sin pendientes y un pago que se aprueba antes de que PAG-26 desvincule la intención, **cuando** ambos se procesan, **entonces** prevalece la cancelación: no se crea el pedido, se liberan las reservas, se revocan los accesos concedidos, el cobro se reembolsa sin avisar al cliente, el reembolso queda pendiente y reintentable si falla, y la cancelación no se difiere. |

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
