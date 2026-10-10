---
id: inventario-seleccion-fuente
titulo: Reservas y selección de fuente
estado: Draft
servicio: Inventario
prefijo: SEL
origen: [RF14, RF15, RF28, NN02, NN06, NN07, NN18]
calidad: [AC01, AC06, AC13, RNF01, RNF09]
depende: [inventario-fuentes, transversal-auditoria, transversal-seguridad]
---

# Spec — Reservas y selección de fuente

## 1. Propósito

Evitar la sobreventa. Antes de cobrar, el E-commerce pide reservar los impresos; el Inventario crea reservas temporales, elige desde qué fuente se atiende cada línea y genera el traslado cuando la fuente no es la sucursal de recojo elegida (AD08 validada).

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Reservas temporales con expiración, confirmación y liberación. | El ciclo de vida del traslado (`inventario-traslados`). |
| Selección de la fuente de cada línea impresa. | El cobro y el pedido (`ecommerce-pagos`, `ecommerce-pedidos`). |
| Atomicidad ante solicitudes simultáneas. | Libros digitales: nunca se reservan. |
| Límite de reservas temporales por cliente (una pendiente y 5 por hora) y por origen (10 por hora). | El formato de rechazo, el registro del evento y los demás límites de uso (`transversal-seguridad`). |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| SEL-01 | Evento | Cuando el E-commerce solicita una reserva para un pedido, el sistema deberá crear una reserva temporal con fecha de expiración por cada línea impresa. | RF14 |
| SEL-02 | Evento | Cuando se reserva una línea impresa, el sistema deberá seleccionar la fuente priorizando la sucursal elegida por el cliente o la más cercana a su dirección, y, si no tiene stock vendible suficiente, otra fuente con stock vendible. | RF15 |
| SEL-03 | Evento | Cuando la fuente seleccionada no es la sucursal de recojo elegida, el sistema deberá generar una solicitud de traslado desde esa fuente hacia la sucursal de recojo. | RF15, NN07 |
| SEL-04 | No deseado | Si dos solicitudes compiten por la última unidad de una edición en una fuente, entonces el sistema deberá conceder la reserva a una sola y reasignar o rechazar la otra. | AC01, RNF01 |
| SEL-05 | No deseado | Si ninguna fuente tiene stock vendible suficiente para una línea, entonces el sistema deberá rechazar la reserva indicando falta de stock y no reservar ninguna otra línea del pedido. | RF14 |
| SEL-06 | Evento | Cuando el E-commerce confirma el pago de un pedido, el sistema deberá pasar sus reservas de pendientes a confirmadas. | RF14 |
| SEL-07 | Evento | Cuando un pedido se cancela o se anula, el sistema deberá liberar sus reservas y devolver las unidades al stock vendible. | RF14 |
| SEL-08 | Estado | Mientras una reserva está pendiente y su fecha de expiración ya pasó, el sistema deberá considerarla expirada y liberar sus unidades automáticamente. | RF14 |
| SEL-09 | Evento | Cuando se repite una solicitud de reserva con la misma clave de idempotencia, el sistema deberá devolver el resultado original sin crear otra reserva. | RF14, AC01 |
| SEL-10 | Opcional | Donde se defina otra regla de selección de fuente, el sistema deberá permitir sustituirla sin cambiar el contrato de reservas ni al E-commerce. | RNF09, AC06 |
| SEL-11 | Evento | Cuando la sucursal marca como entregado un pedido de recojo, el sistema deberá descontar el stock físico de la reserva confirmada y cerrarla. | RF14 |
| SEL-12 | Ubicuo | El sistema deberá rechazar cualquier solicitud de reserva que incluya una edición digital. | RF14 |
| SEL-13 | No deseado | Si un cliente ya tiene una reserva temporal pendiente de otro pedido, entonces el sistema deberá rechazar la nueva solicitud indicando que debe completar o liberar la anterior. | RF28, AC13 |
| SEL-14 | No deseado | Si un cliente ya inició 5 reservas temporales en una ventana deslizante de una hora, entonces el sistema deberá rechazar la nueva solicitud; las solicitudes rechazadas por falta de stock o por estos límites no cuentan como iniciadas. | RF28, AC13 |
| SEL-15 | Evento | Cuando una solicitud de reserva es rechazada por los límites, el sistema deberá responder y registrar el evento de seguridad con el cliente y el motivo según `transversal-seguridad` (SEG-07 y SEG-08). | RF28, AC13 |
| SEL-16 | Evento | Cuando el cliente reinicia su checkout, modifica las cantidades o reintenta tras un pago rechazado en un pedido que ya tiene una reserva pendiente, el sistema deberá reemplazar esa reserva sin contarla como otra reserva ni como una nueva iniciada. | RF28, AC13 |
| SEL-17 | Evento | Cuando el cliente cancela su checkout, el sistema deberá liberar de inmediato sus reservas pendientes. | RF14, RF28 |
| SEL-18 | Ubicuo | El sistema deberá comprobar y registrar los límites de reservas de forma atómica ante solicitudes simultáneas del mismo cliente o del mismo origen. | RF28, AC01, AC13 |
| SEL-19 | No deseado | Si un mismo origen ya inició 10 reservas temporales en una ventana deslizante de una hora, entonces el sistema deberá rechazar la nueva solicitud. | RF28, AC13 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-SEL-01 | SEL-01 | **Dado** un pedido con una línea impresa, **cuando** el E-commerce pide la reserva, **entonces** se crea una reserva pendiente con su fecha de expiración y el vendible de la fuente baja en la cantidad reservada. |
| AC-SEL-02 | SEL-02 | **Dado** que Ayacucho no tiene vendible pero Cusco sí, **cuando** se reserva el libro para recojo en Ayacucho, **entonces** la fuente seleccionada es Cusco. |
| AC-SEL-03 | SEL-03 | **Dado** una reserva cuya fuente es Cusco y recojo en Ayacucho, **cuando** se confirma la selección, **entonces** existe una solicitud de traslado de Cusco a Ayacucho vinculada al pedido. |
| AC-SEL-04 | SEL-04 | **Dado** una edición con una sola unidad vendible, **cuando** dos clientes la reservan a la vez, **entonces** solo uno obtiene la reserva y el vendible nunca es negativo. |
| AC-SEL-05 | SEL-05 | **Dado** un pedido con dos líneas y la segunda sin stock en toda la red, **cuando** se pide la reserva, **entonces** se rechaza por falta de stock y la primera línea queda sin reservar. |
| AC-SEL-06 | SEL-06 | **Dado** una reserva pendiente, **cuando** el E-commerce confirma el pago, **entonces** la reserva pasa a confirmada y ya no expira. |
| AC-SEL-07 | SEL-07 | **Dado** una reserva pendiente o confirmada, **cuando** el pedido se anula, **entonces** la reserva se libera y las unidades vuelven al vendible. |
| AC-SEL-08 | SEL-08 | **Dado** una reserva pendiente de 15 minutos, **cuando** pasa el tiempo sin confirmarse, **entonces** la reserva queda expirada y sus unidades vuelven al vendible. |
| AC-SEL-09 | SEL-09 | **Dado** una solicitud de reserva con una clave de idempotencia, **cuando** se envía otra vez con la misma clave, **entonces** se devuelve la misma reserva y el vendible no baja dos veces. |
| AC-SEL-10 | SEL-10 | **Dado** una regla de selección alternativa registrada, **cuando** se activa, **entonces** las reservas la usan sin modificar el contrato ni el E-commerce. |
| AC-SEL-11 | SEL-11 | **Dado** una reserva confirmada de 1 unidad con físico 9, **cuando** la sucursal entrega el pedido, **entonces** el físico pasa a 8 y la reserva queda cerrada. |
| AC-SEL-12 | SEL-12 | **Dado** una solicitud de reserva con una edición digital, **cuando** se envía, **entonces** se rechaza indicando que los libros digitales no se reservan. |
| AC-SEL-13 | SEL-13 | **Dado** un cliente con una reserva pendiente, **cuando** pide reservar otro pedido, **entonces** se rechaza con el motivo del límite y el vendible no cambia. |
| AC-SEL-14 | SEL-14 | **Dado** un cliente que inició 5 reservas en la última hora, **cuando** intenta una sexta, **entonces** se rechaza con el instante en que podrá reintentar; y una solicitud rechazada por falta de stock no cuenta entre las 5. |
| AC-SEL-15 | SEL-15 | **Dado** una solicitud rechazada por límite, **cuando** se revisa el registro de seguridad, **entonces** figura el cliente y el motivo, y la respuesta tiene el formato de `transversal-seguridad`. |
| AC-SEL-16 | SEL-16 | **Dado** un cliente con una reserva pendiente, **cuando** cambia la cantidad de su pedido, **entonces** la reserva se reemplaza, el vendible refleja la nueva cantidad y no se consume ninguna de las 5 reservas por hora. |
| AC-SEL-17 | SEL-17 | **Dado** una reserva pendiente, **cuando** el cliente cancela su checkout, **entonces** las unidades vuelven al vendible de inmediato. |
| AC-SEL-18 | SEL-18 | **Dado** un cliente sin reservas, **cuando** envía 10 solicitudes de reserva simultáneas de pedidos distintos, **entonces** exactamente una se acepta. |
| AC-SEL-19 | SEL-19 | **Dado** un origen con 10 reservas iniciadas en la última hora por cuentas distintas, **cuando** otra cuenta del mismo origen intenta reservar, **entonces** se rechaza con la respuesta de límite excedido. |

## 5. Reglas e invariantes

- Reserva: pendiente → confirmada, o pendiente → liberada / expirada. Una reserva confirmada solo se cierra al entregar el pedido o se libera si el pedido se anula.
- Una reserva no descuenta el stock físico; lo reduce el vendible. El físico baja al entregar.
- El tiempo de expiración es de 15 minutos desde que se inicia el pago.
- La reserva de un pedido es atómica: se reservan todas las líneas impresas o ninguna.
- El límite de «una pendiente» cuenta solo las reservas temporales pendientes; una reserva expirada, liberada o confirmada deja de contar. Las reservas confirmadas de pedidos pagados no limitan nuevas compras. Todas las iniciadas con éxito cuentan para el límite de 5 por hora del cliente y de 10 por hora del origen.
- La regla del dominio no basta ante la concurrencia: la atomicidad la garantiza la infraestructura y se prueba con solicitudes simultáneas.

## 6. Casos límite y errores

- Un pedido con líneas de dos o más ediciones que se atienden desde fuentes distintas genera un traslado por cada fuente distinta de la sucursal de recojo.
- Una confirmación de reserva ya expirada se rechaza y obliga a reiniciar el pago.
- Una liberación repetida de la misma reserva no cambia el stock otra vez.
- Repetir una solicitud con la misma clave de idempotencia no cuenta como una reserva nueva para los límites.

## 7. Preguntas abiertas

- [ ] ¿Cómo se estima la «sucursal más cercana» cuando el despacho es a domicilio: por región de la dirección o por una tabla de cercanía entre sucursales? Responde: responsable del proyecto; no bloquea el comportamiento de recojo.
- [ ] ¿El físico se descuenta al entregar (recojo) y al despachar (domicilio)? Esta spec asume que sí; confirmar con el responsable del proyecto.
- [ ] ¿Confirmas el límite de 10 reservas por hora por origen (SEL-19)? Los orígenes compartidos pueden llegar a él; propuesta: sí, como meta inicial. Responde: responsable del proyecto.
