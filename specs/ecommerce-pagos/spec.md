---
id: ecommerce-pagos
titulo: Pagos con Yape y tarjeta
estado: Draft
servicio: E-commerce
prefijo: PAG
origen: [RF05, NN02]
calidad: [AC01, AC05, RNF06, RNF07, RNF08]
depende: [ecommerce-carrito-checkout, inventario-seleccion-fuente, transversal-auditoria]
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
| PAG-01 | Ubicuo | El sistema deberá cobrar cada pedido una sola vez mediante una pasarela de pago externa. | RF05 |
| PAG-02 | Ubicuo | El sistema deberá recibir de la pasarela únicamente un identificador del medio de pago, sin procesar ni guardar números de tarjeta ni códigos de seguridad. | RNF07, AC05 |
| PAG-03 | Ubicuo | El sistema deberá permitir pagar con Yape o con tarjeta. | RF05 |
| PAG-04 | Evento | Cuando se repite una solicitud de cobro con la misma clave de idempotencia, el sistema deberá devolver el resultado original sin cobrar otra vez. | RF05 |
| PAG-05 | Evento | Cuando la pasarela aprueba el pago, el sistema deberá confirmar el pedido, pedir la confirmación de las reservas de impresos y conceder los accesos a los libros digitales. | RF05, NN02 |
| PAG-06 | No deseado | Si la pasarela rechaza el pago, entonces el sistema deberá informar el motivo, no registrar ningún cobro, mantener la reserva hasta su expiración y permitir reintentar con otro medio. | RF05 |
| PAG-07 | No deseado | Si la pasarela no responde o la respuesta es dudosa, entonces el sistema deberá dejar el pago pendiente de confirmación, consultar su estado más tarde y no cobrar de nuevo. | RF05 |
| PAG-08 | No deseado | Si la reserva del pedido expiró o el pedido fue anulado, entonces el sistema deberá negarse a iniciar el cobro. | RF05, AC01 |
| PAG-09 | Ubicuo | El sistema deberá cobrar exactamente el total del pedido, incluido el envío, en soles. | RF05 |
| PAG-10 | Evento | Cuando se solicita un reembolso total o parcial, el sistema deberá pedirlo a la pasarela y registrar su trazabilidad. | RF05 |
| PAG-11 | Evento | Cuando se intenta un cobro, el sistema deberá registrar el intento y su resultado sin datos sensibles. | RNF08 |
| PAG-12 | Ubicuo | El sistema deberá obtener las claves de la pasarela de la configuración del entorno y nunca del código fuente. | RNF06 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-PAG-01 | PAG-01, PAG-03, PAG-09 | **Dado** un pedido de S/ 123,90 con envío, **cuando** el cliente paga con Yape, **entonces** se cobra una sola vez S/ 123,90 en soles. |
| AC-PAG-02 | PAG-02 | **Dado** un pago con tarjeta, **cuando** se procesa, **entonces** el sistema solo recibe el identificador del medio de pago y ningún dato de tarjeta aparece en su almacenamiento ni en sus registros. |
| AC-PAG-03 | PAG-04 | **Dado** una solicitud de cobro con una clave de idempotencia, **cuando** el cliente la repite por una doble pulsación, **entonces** se devuelve el mismo resultado y se cobra una sola vez. |
| AC-PAG-04 | PAG-05 | **Dado** un pedido mixto, **cuando** la pasarela aprueba el pago, **entonces** el pedido queda confirmado, las reservas pasan a confirmadas y el libro digital aparece en la biblioteca. |
| AC-PAG-05 | PAG-06 | **Dado** una tarjeta rechazada, **cuando** la pasarela responde el rechazo, **entonces** el cliente ve el motivo, no hay cobro y puede reintentar con Yape mientras la reserva siga vigente. |
| AC-PAG-06 | PAG-07 | **Dado** que la pasarela no responde, **cuando** pasa el tiempo de espera, **entonces** el pago queda pendiente de confirmación, se consulta su estado más tarde y no se duplica el cobro. |
| AC-PAG-07 | PAG-08 | **Dado** una reserva expirada, **cuando** el cliente intenta pagar, **entonces** el cobro se niega y se invita a reiniciar el checkout. |
| AC-PAG-08 | PAG-10 | **Dado** un pedido pagado, **cuando** se solicita el reembolso de una línea, **entonces** se pide a la pasarela por el monto de esa línea y queda la trazabilidad. |
| AC-PAG-09 | PAG-11 | **Dado** un cobro rechazado, **cuando** ocurre, **entonces** queda un registro del intento y su resultado sin datos de tarjeta. |
| AC-PAG-10 | PAG-12 | **Dado** el repositorio, **cuando** se revisa, **entonces** no contiene claves de la pasarela y el servicio no arranca si falta la configuración. |

## 5. Reglas e invariantes

- Un pedido tiene como máximo un cobro aprobado.
- La confirmación del pago, de las reservas y de los accesos digitales es una sola unidad de negocio: si una parte falla, se compensa.
- La clave de idempotencia la genera el sistema al iniciar el pago y viaja en cada intento.
- Las comisiones de la pasarela no forman parte del precio al cliente.

## 6. Casos límite y errores

- Un pago aprobado cuya confirmación de reserva falla se reembolsa automáticamente y se avisa al cliente.
- Un reembolso parcial no puede superar el monto cobrado de esa línea.
- Una pasarela caída no bloquea el catálogo ni la biblioteca.

## 7. Preguntas abiertas

- [ ] ¿Cuánto tiempo se espera una respuesta de la pasarela antes de pasar a «pendiente de confirmación»? Propuesta: 10 segundos. Se decide en el plan.
- [ ] ¿Culqi permite una clave de idempotencia propia y cómo informa los reembolsos de Yape? Se confirma con su documentación antes del plan (bloqueo B05).
