---
id: inventario-seleccion-fuente
titulo: Reservas y selección de fuente
estado: Approved
servicio: Inventario
prefijo: SEL
origen: [RF04, RF14, RF15, RF28, NN02, NN06, NN07, NN18]
calidad: [AC01, AC06, AC13, RNF01, RNF09]
depende: [inventario-fuentes, identidad-seguridad, transversal-auditoria, transversal-seguridad]
aprobado_por: Diego Amiquero
aprobado_en: 2026-10-16
---

# Spec — Reservas y selección de fuente

## 1. Propósito

Evitar la sobreventa. Antes de cobrar, el E-commerce pide reservar los impresos; el Inventario crea reservas temporales, elige desde qué fuente se atiende cada línea y genera el traslado cuando la fuente no es la sucursal de recojo elegida (AD08 validada).

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Reservas temporales con expiración, confirmación y liberación. | El ciclo de vida del traslado (`inventario-traslados`). |
| Selección de la fuente de cada línea impresa y su consulta previa sin reservar. | El cobro y el pedido (`ecommerce-pagos`, `ecommerce-pedidos`). |
| Atomicidad ante solicitudes simultáneas. | Libros digitales: nunca se reservan. |
| Límite de reservas temporales por cliente (una pendiente y 5 por hora) y por origen (10 por hora). | El formato de rechazo, el registro del evento y los demás límites de uso (`transversal-seguridad`). |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| SEL-01 | Evento | Cuando el E-commerce solicita una reserva para un pedido, el sistema deberá crear una reserva temporal por cada línea impresa, con expiración a los 15 minutos desde que se inicia el pago. | RF14 |
| SEL-02 | Evento | Cuando se reserva una línea impresa de un pedido de recojo, el sistema deberá seleccionar la sucursal de recojo elegida y, si no tiene stock vendible suficiente, la fuente con stock vendible de menor plazo de ruta hacia ella. | RF15 |
| SEL-03 | Evento | Cuando se confirman las reservas de un pedido de recojo, el sistema deberá generar, en la misma transacción, una solicitud de traslado vinculada al pedido por cada fuente y edición cuya fuente no es la sucursal de recojo, desde esa fuente hacia la sucursal de recojo. | RF15, NN07 |
| SEL-04 | No deseado | Si dos solicitudes compiten por la última unidad de una edición en una fuente, entonces el sistema deberá conceder la reserva a una sola. | AC01, RNF01 |
| SEL-05 | No deseado | Si ninguna fuente tiene stock vendible suficiente para una línea, entonces el sistema deberá rechazar la reserva indicando falta de stock y no reservar ninguna otra línea del pedido. | RF14 |
| SEL-06 | Evento | Cuando el E-commerce confirma el pago de un pedido, el sistema deberá pasar sus reservas de pendientes a confirmadas. | RF14 |
| SEL-07 | Evento | Cuando un pedido se cancela, se anula o se marca como no recogido, el sistema deberá liberar sus reservas y devolver las unidades al stock vendible. | RF14 |
| SEL-08 | Estado | Mientras una reserva está pendiente y su fecha de expiración ya pasó, el sistema deberá considerarla expirada y liberar sus unidades automáticamente. | RF14 |
| SEL-09 | Evento | Cuando el mismo cliente repite una solicitud de reserva con la misma clave de idempotencia, el sistema deberá devolver el resultado original sin crear otra reserva. | RF14, AC01 |
| SEL-10 | Opcional | Donde se defina otra regla de selección de fuente, el sistema deberá permitir sustituirla sin cambiar el contrato de reservas ni al E-commerce. | RNF09, AC06 |
| SEL-11 | Evento | Cuando la sucursal marca como entregado un pedido de recojo, el sistema deberá descontar el stock físico de la fuente que mantiene la reserva confirmada y cerrarla. | RF14 |
| SEL-12 | Ubicuo | El sistema deberá rechazar cualquier solicitud de reserva que incluya una edición digital. | RF14 |
| SEL-13 | No deseado | Si un cliente ya tiene una reserva temporal pendiente de otro pedido, entonces el sistema deberá rechazar la nueva solicitud indicando que debe completar o liberar la anterior. | RF28, AC13 |
| SEL-14 | No deseado | Si un cliente ya inició 5 reservas temporales en una ventana deslizante de una hora, entonces el sistema deberá rechazar la nueva solicitud; las solicitudes rechazadas por falta de stock o por estos límites no cuentan como iniciadas. | RF28, AC13 |
| SEL-15 | Evento | Cuando una solicitud de reserva es rechazada por los límites, el sistema deberá responder y registrar el evento de seguridad con el cliente y el motivo según `transversal-seguridad` (SEG-07 y SEG-08). | RF28, AC13 |
| SEL-16 | Evento | Cuando el cliente reinicia su checkout, modifica las cantidades o reintenta tras un pago rechazado en un pedido que ya tiene una reserva pendiente, el sistema deberá reemplazar esa reserva sin contarla como otra reserva ni como una nueva iniciada, y conservar la anterior si la nueva no puede crearse por falta de stock. | RF28, AC13 |
| SEL-17 | Evento | Cuando el cliente cancela su checkout, el sistema deberá liberar de inmediato sus reservas pendientes. | RF14, RF28 |
| SEL-18 | Ubicuo | El sistema deberá comprobar y registrar los límites de reservas de forma atómica ante solicitudes simultáneas del mismo cliente o del mismo origen. | RF28, AC01, AC13 |
| SEL-19 | No deseado | Si un mismo origen ya inició 10 reservas temporales en una ventana deslizante de una hora, entonces el sistema deberá rechazar la nueva solicitud. | RF28, AC13 |
| SEL-20 | Evento | Cuando el E-commerce consulta la selección de fuentes de un carrito, el sistema deberá responder, por cada línea impresa, la fuente que se seleccionaría, si implica un traslado y su plazo estimado, o la falta de stock, sin crear reservas ni modificar el stock vendible, ignorando las líneas digitales y sujeto al límite general de solicitudes. | RF04, RF15 |
| SEL-21 | Evento | Cuando logística pasa a en reparto un envío a domicilio, el sistema deberá descontar el stock físico de la fuente de cada línea impresa del envío y cerrar sus reservas confirmadas. | RF14 |
| SEL-22 | Evento | Cuando el E-commerce informa que el pago de un pedido quedó pendiente de confirmación, el sistema deberá extender una sola vez la expiración de sus reservas pendientes hasta 30 minutos desde que se inició el pago. | RF14 |
| SEL-23 | No deseado | Si la región de la dirección no tiene fuente, o dos fuentes tienen el mismo plazo de ruta, entonces el sistema deberá elegir la de mayor stock vendible y, si empatan, la de código menor. | RF15 |
| SEL-24 | Evento | Cuando se reserva una línea impresa de un pedido a domicilio, el sistema deberá seleccionar la fuente de la región de la dirección y, si no tiene stock vendible suficiente, la de menor plazo de ruta hacia la sucursal de esa región, sin generar un traslado. | RF15 |
| SEL-25 | Ubicuo | El sistema deberá tratar como la menos cercana a una fuente cuya ruta no está definida, y no dividir una línea entre varias fuentes. | RF15 |
| SEL-26 | No deseado | Si una solicitud pierde la última unidad de una fuente pero otra fuente tiene stock vendible suficiente, entonces el sistema deberá reasignarla a esa fuente; y si ninguna lo tiene, rechazarla por falta de stock según SEL-05. | AC01, RF14 |
| SEL-27 | Ubicuo | El sistema deberá excluir del stock vendible las reservas pendientes vencidas y liberar las vencidas de una edición, en la misma transacción, al reservar esa edición. | RF14, AC01 |
| SEL-28 | No deseado | Si se intenta entregar, pasar a en reparto o despachar un pedido cuya reserva no está confirmada (liberada, expirada o ya cerrada), entonces el sistema deberá rechazar la operación por estado inválido sin cambiar el stock. | RF14, AC01 |
| SEL-29 | No deseado | Si dos operaciones compiten por la misma reserva (anulación, no recogido, entrega, reparto, despacho de traslado o expiración), entonces el sistema deberá aplicar la primera que confirma y rechazar las demás por estado inválido, sin efectos parciales. | AC01, RNF01 |
| SEL-30 | No deseado | Si el aviso de pago pendiente llega con la reserva ya vencida, confirmada o liberada, entonces el sistema deberá ignorarlo y mantener su estado. | RF14 |
| SEL-31 | No deseado | Si se repite una clave de idempotencia con una carga distinta, entonces el sistema deberá rechazar la solicitud indicando el conflicto. | RF14, AC01 |
| SEL-32 | Evento | Cuando una reserva se confirma o se libera, o se descuenta el físico por una entrega o un paso a en reparto, el sistema deberá registrarlo en la auditoría en la misma transacción. | RNF08, RF14 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-SEL-01 | SEL-01 | **Dado** un pedido con una línea impresa, **cuando** el E-commerce pide la reserva, **entonces** se crea una reserva pendiente con su fecha de expiración y el vendible de la fuente baja en la cantidad reservada. |
| AC-SEL-02 | SEL-02 | **Dado** que Ayacucho no tiene vendible pero Cusco sí, **cuando** se reserva el libro para recojo en Ayacucho, **entonces** la fuente seleccionada es Cusco. |
| AC-SEL-03 | SEL-03 | **Dado** una reserva pendiente cuya fuente es Cusco y recojo en Ayacucho, **cuando** se confirma el pago y con él la reserva, **entonces** existe una solicitud de traslado de Cusco a Ayacucho vinculada al pedido; con dos líneas de ediciones distintas desde Cusco y Lima hay un traslado por cada fuente y edición; y mientras la reserva está pendiente o si expira, no existe ningún traslado. |
| AC-SEL-04 | SEL-04 | **Dado** una edición con una sola unidad vendible, **cuando** cien clientes la reservan a la vez, **entonces** solo uno obtiene la reserva y el vendible nunca es negativo. |
| AC-SEL-05 | SEL-05 | **Dado** un pedido con dos líneas y la segunda sin stock en toda la red, **cuando** se pide la reserva, **entonces** se rechaza por falta de stock y la primera línea queda sin reservar. |
| AC-SEL-06 | SEL-06 | **Dado** una reserva pendiente, **cuando** el E-commerce confirma el pago, **entonces** la reserva pasa a confirmada y ya no expira. |
| AC-SEL-07 | SEL-07 | **Dado** una reserva pendiente o confirmada, **cuando** el pedido se anula o el cliente lo cancela, **entonces** la reserva se libera y las unidades vuelven al vendible; y si se repite la liberación, el stock no cambia otra vez. |
| AC-SEL-08 | SEL-08 | **Dado** una reserva pendiente de 15 minutos, **cuando** pasa el tiempo sin confirmarse, **entonces** la reserva queda expirada y sus unidades vuelven al vendible. |
| AC-SEL-09 | SEL-09 | **Dado** una solicitud de reserva con una clave de idempotencia, **cuando** se envía otra vez con la misma clave, **entonces** se devuelve la misma reserva y el vendible no baja dos veces. |
| AC-SEL-10 | SEL-10 | **Dado** una regla de selección alternativa registrada, **cuando** se activa, **entonces** las reservas la usan sin modificar el contrato ni el E-commerce. |
| AC-SEL-11 | SEL-11 | **Dado** una reserva confirmada de 1 unidad con físico 9, **cuando** la sucursal entrega el pedido, **entonces** el físico pasa a 8 y la reserva queda cerrada; y si las unidades llegaron por traslado, el descuento se aplica en la sucursal de recojo. |
| AC-SEL-12 | SEL-12 | **Dado** una solicitud de reserva con una edición digital, **cuando** se envía, **entonces** se rechaza indicando que los libros digitales no se reservan. |
| AC-SEL-13 | SEL-13 | **Dado** un cliente con una reserva pendiente, **cuando** pide reservar otro pedido, **entonces** se rechaza con el motivo del límite y el vendible no cambia. |
| AC-SEL-14 | SEL-14 | **Dado** un cliente que inició 5 reservas en la última hora, **cuando** intenta una sexta, **entonces** se rechaza con el instante en que podrá reintentar; y una solicitud rechazada por falta de stock no cuenta entre las 5. |
| AC-SEL-15 | SEL-15 | **Dado** una solicitud rechazada por límite, **cuando** se revisa el registro de seguridad, **entonces** figura el cliente y el motivo, y la respuesta tiene el formato de `transversal-seguridad`. |
| AC-SEL-16 | SEL-16 | **Dado** un cliente con una reserva pendiente, **cuando** cambia la cantidad de su pedido, **entonces** la reserva se reemplaza, el vendible refleja la nueva cantidad y no se consume ninguna de las 5 reservas por hora; y si la nueva cantidad supera el stock, se rechaza y se conserva la reserva anterior. |
| AC-SEL-17 | SEL-17 | **Dado** una reserva pendiente, **cuando** el cliente cancela su checkout, **entonces** las unidades vuelven al vendible de inmediato. |
| AC-SEL-18 | SEL-18 | **Dado** un cliente sin reservas, **cuando** envía 10 solicitudes de reserva simultáneas de pedidos distintos, **entonces** exactamente una se acepta. |
| AC-SEL-19 | SEL-19, SEL-18 | **Dado** un origen con 10 reservas iniciadas en la última hora por cuentas distintas, **cuando** otra cuenta del mismo origen intenta reservar, **entonces** se rechaza con la respuesta de límite excedido; y si 12 cuentas del mismo origen reservan a la vez, exactamente 10 se aceptan. |
| AC-SEL-20 | SEL-20 | **Dado** un carrito con un libro que solo está en Cusco y recojo en Ayacucho, con una ruta de 2 días, **cuando** el E-commerce consulta la selección, **entonces** responde fuente Cusco, con traslado y plazo de 2 días, y el vendible y las reservas no cambian; y con una línea sin stock en la red responde la falta de stock, y una línea digital se ignora. |
| AC-SEL-21 | SEL-21 | **Dado** un pedido a domicilio con una reserva confirmada de 1 unidad en Cusco con físico 9, **cuando** logística pasa el envío a en reparto, **entonces** el físico de Cusco pasa a 8 y la reserva queda cerrada; y si se repite la operación, el físico no baja otra vez. |
| AC-SEL-22 | SEL-07 | **Dado** una reserva confirmada de 1 unidad con físico 9 de un pedido de recojo no recogido, **cuando** el pedido se marca como no recogido, **entonces** la reserva se libera, el vendible sube en 1 y el físico sigue en 9. |
| AC-SEL-23 | SEL-02 | **Dado** un libro con vendible en Cusco y en Lima, rutas de 2 días de Cusco a Ayacucho y de 4 días de Lima a Ayacucho, y una sucursal Ayacucho sin vendible, **cuando** se reserva para recojo en Ayacucho, **entonces** se elige Cusco; y **dado** un pedido a domicilio con dirección en la región de Cusco y vendible en Cusco, **cuando** se reserva, **entonces** la fuente es Cusco sin traslado; y con una ruta sin definir, esa fuente se trata como la menos cercana. |
| AC-SEL-24 | SEL-22 | **Dado** una reserva pendiente que vence a los 15 minutos y un pago pendiente de confirmación, **cuando** el E-commerce lo informa, **entonces** la reserva vence a los 30 minutos desde el inicio del pago; y si se informa otra vez, no se extiende de nuevo. |
| AC-SEL-25 | SEL-23 | **Dado** un pedido a domicilio con dirección en una región sin sucursal, y vendible 5 en Lima y 5 en Cusco, **cuando** se reserva, **entonces** se elige la de código menor; y con vendible 5 en Lima y 8 en Cusco, se elige Cusco. |
| AC-SEL-26 | SEL-26 | **Dado** una edición con una unidad en Ayacucho y 5 en Cusco, **cuando** dos clientes con recojo en Ayacucho la reservan a la vez, **entonces** uno recibe Ayacucho, el otro es reasignado a Cusco, y si Cusco no tuviera stock, el otro se rechaza por falta de stock. |
| AC-SEL-27 | SEL-27 | **Dado** una edición con físico 5 y una reserva pendiente vencida de 2 sin procesar, **cuando** se consulta su vendible, **entonces** es 5; y **cuando** un cliente reserva esa edición, **entonces** la reserva vencida se libera en la misma operación; y confirmar la vencida se rechaza. |
| AC-SEL-28 | SEL-28 | **Dado** un pedido de recojo no recogido cuya reserva se liberó, **cuando** la sucursal intenta entregarlo, **entonces** se rechaza por estado inválido y el stock no cambia. |
| AC-SEL-29 | SEL-29 | **Dado** una reserva confirmada, **cuando** la anulación del pedido y la entrega ocurren a la vez, **entonces** se aplica solo la primera que confirma, la otra se rechaza por estado inválido y el físico baja una sola vez o ninguna. |
| AC-SEL-30 | SEL-30 | **Dado** un aviso de pago pendiente que llega con la reserva ya expirada, **cuando** se procesa, **entonces** se ignora y la reserva sigue expirada. |
| AC-SEL-31 | SEL-31, SEL-09 | **Dado** una clave de idempotencia ya usada por el cliente, **cuando** la envía con otras cantidades, **entonces** se rechaza indicando el conflicto; y otro cliente puede usar la misma clave sin conflicto. |
| AC-SEL-32 | SEL-32 | **Dado** que la auditoría no está disponible, **cuando** se intenta confirmar una reserva o entregar un pedido, **entonces** la operación no se aplica y el stock no cambia. |
| AC-SEL-33 | SEL-24 | **Dado** un pedido a domicilio con dirección en Cusco y sin vendible en Cusco, y vendible en Lima y en Arequipa con una ruta de 1 día de Arequipa a Cusco y de 3 días de Lima a Cusco, **cuando** se reserva, **entonces** la fuente es Arequipa y no se genera ningún traslado. |
| AC-SEL-34 | SEL-25 | **Dado** dos fuentes con vendible suficiente, una con ruta definida de 4 días y otra sin ruta, **cuando** se reserva para recojo, **entonces** se elige la de ruta definida; y una línea de 5 unidades nunca se divide entre dos fuentes: si ninguna tiene 5, se rechaza por falta de stock. |

## 5. Reglas e invariantes

- Reserva: pendiente → confirmada, o pendiente → liberada / expirada. Una reserva confirmada pasa a cerrada al entregar un pedido de recojo o al pasar a en reparto un envío a domicilio; se libera si el pedido se anula o se marca como no recogido; y, si su fuente despacha un traslado vinculado al pedido, se consume en el origen y se vuelve a crear en el destino al recibirlo (`inventario-traslados`).
- Una reserva no descuenta el stock físico; lo reduce el vendible. El físico baja al entregar (recojo), al pasar a en reparto (domicilio) o al despachar el traslado (origen).
- La consulta previa de selección (SEL-20) es de solo lectura: no cuenta para los límites de reservas ni para el vendible.
- El tiempo de expiración es de 15 minutos desde que se inicia el pago, y de hasta 30 minutos si el pago queda pendiente de confirmación (SEL-22).
- La reserva de un pedido es atómica: se reservan todas las líneas impresas o ninguna.
- El límite de «una pendiente» cuenta solo las reservas temporales pendientes; una reserva expirada, liberada o confirmada deja de contar. Las reservas confirmadas de pedidos pagados no limitan nuevas compras. Todas las iniciadas con éxito cuentan para el límite de 5 por hora del cliente y de 10 por hora del origen.
- La regla del dominio no basta ante la concurrencia: la atomicidad la garantiza la infraestructura y se prueba con solicitudes simultáneas.

## 6. Casos límite y errores

- Un pedido con líneas de dos o más ediciones o fuentes distintas genera un traslado por cada fuente y edición distintas de la sucursal de recojo.
- Una línea se atiende desde una sola fuente: nunca se divide (SEL-25).
- Un pedido marcado como no recogido no puede entregarse después: solo se anula con reembolso (SEL-28).
- Una confirmación de reserva ya expirada se rechaza y obliga a reiniciar el pago.
- Una liberación repetida de la misma reserva no cambia el stock otra vez.
- Repetir una solicitud con la misma clave de idempotencia no cuenta como una reserva nueva para los límites.

## 7. Preguntas abiertas

- [x] ¿Cómo se estima la «sucursal más cercana» cuando el despacho es a domicilio? Resuelto el 2026-10-16: se prioriza la fuente de la región de la dirección y, si no alcanza, la de menor plazo según la tabla de rutas entre sucursales (SEL-02, `inventario-fuentes` FUE-12 a FUE-14).
- [x] ¿El físico se descuenta al entregar (recojo) y al despachar (domicilio)? Resuelto el 2026-10-16: en recojo al entregar (SEL-11) y en domicilio cuando el envío pasa a en reparto (SEL-21).
- [x] ¿Qué fuente se elige si la región de la dirección no tiene sucursal, y cómo se desempata? Resuelto el 2026-10-16 (confirmado por el responsable): la de mayor stock vendible y, si empatan, la de código menor (SEL-02).
- [x] ¿Un pedido a domicilio cuyas líneas salen de fuentes distintas se envía en un envío por fuente? Resuelto el 2026-10-16: sí (ENV-01, ENV-07).
- [x] ¿Confirmas el límite de 10 reservas por hora por origen (SEL-19)? Resuelto el 2026-10-16: sí, como meta inicial a validar con pruebas de carga.
