---
id: ecommerce-carrito-checkout
titulo: Carrito y checkout
estado: Approved
aprobado_por: Claude (delegado por Diego Amiquero)
aprobado_en: 2026-11-20
plan_aprobado_por: Claude (delegado por Diego Amiquero)
plan_aprobado_en: 2026-11-18
servicio: E-commerce
prefijo: CAR
origen: [RF03, RF04, RF18, RF27, RF28, NN02, NN13, NN15, NN18]
calidad: [AC01, AC03, AC06, AC04, RNF01, RNF03, RNF13, RNF23]
depende: [ecommerce-catalogo, inventario-seleccion-fuente, identidad-seguridad, transversal-seguridad]
---

# Spec — Carrito y checkout

## 1. Propósito

Que el cliente arme un carrito que se conserva entre sesiones y dispositivos, mezcle libros impresos y digitales, y llegue al pago con la certeza de que lo que compra existe (NN02, NN13). Un carrito solo digital no pide entrega ni consulta al Inventario.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Carrito persistente por cuenta, líneas mixtas. | El cobro (`ecommerce-pagos`) y el pedido resultante (`ecommerce-pedidos`). |
| Validación de disponibilidad, entrega y reserva temporal. | La selección de fuente (`inventario-seleccion-fuente`). |
| Consentimientos y aceptación de términos antes del pago. | La creación de la cuenta (`identidad-seguridad`). |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| CAR-01 | Ubicuo | El sistema deberá conservar el carrito de cada cliente autenticado entre sesiones y dispositivos, y el de un visitante en su navegador (CAR-29). | RF03 |
| CAR-02 | Evento | Cuando el cliente agrega, modifica o elimina una línea, el sistema deberá actualizar el carrito y su total. | RF03 |
| CAR-03 | Ubicuo | El sistema deberá permitir que un mismo carrito contenga ediciones impresas y digitales, y limitar cada edición digital a una unidad. | RF03, NN13 |
| CAR-04 | No deseado | Si el cliente pide más unidades de un impreso que el stock vendible de la red, entonces el sistema deberá rechazar la cantidad e indicar la máxima disponible. | RF03, RNF01 |
| CAR-05 | Evento | Cuando el cliente inicia el checkout con impresos, el sistema deberá validar la disponibilidad real consultando el inventario antes de continuar. | RF04 |
| CAR-06 | No deseado | Si el inventario no responde durante el checkout, entonces el sistema deberá informar que no puede validar el stock y no continuar con el cobro. | AC03, RNF03 |
| CAR-07 | Evento | Cuando el carrito contiene solo ediciones digitales, el sistema deberá omitir la entrega y no consultar el inventario. | RF04, NN13 |
| CAR-08 | Evento | Cuando el carrito contiene impresos, el sistema deberá pedir elegir entre recojo en una sucursal o despacho a domicilio con su dirección. | RF04 |
| CAR-09 | Evento | Cuando la sucursal de recojo elegida no tiene el stock y otra fuente lo atenderá, el sistema deberá informar el traslado y su plazo estimado, como estimación obtenida de la consulta de selección del inventario, antes de pagar y sin reservar todavía; si el plazo no está definido, deberá indicarlo como «a confirmar». | RF04 |
| CAR-10 | Evento | Cuando el cliente pasa al paso de pago con impresos, el sistema deberá crear la intención de pago, reservar las unidades con expiración a los 15 minutos desde ese momento y mostrar el tiempo restante. | RF04, AC01 |
| CAR-11 | No deseado | Si la reserva expira antes de completar el pago, entonces el sistema deberá impedir el cobro e invitar a reiniciar el checkout. | RF04, AC01 |
| CAR-12 | Ubicuo | El sistema deberá exigir, antes del pago, la aceptación de los términos y del tratamiento de datos personales. | NN15 |
| CAR-13 | Estado | Mientras el carrito contiene libros digitales, el sistema deberá exigir además la aceptación de la política de reembolso de libros digitales. | NN15 |
| CAR-14 | No deseado | Si una edición digital deja de estar disponible por su licencia antes de iniciarse el pago, entonces el sistema deberá impedir su pago e invitar a quitarla; una compra ya iniciada se concede aunque la licencia venza mientras el pago está pendiente. | NN14 |
| CAR-15 | Ubicuo | El sistema deberá completar la compra en no más de cinco pasos (revisión del carrito, entrega, datos y aceptaciones, pago, confirmación), y en no más de cuatro cuando el carrito es solo digital. | RNF13, AC06 |
| CAR-16 | Ubicuo | El sistema deberá exigir que el cliente haya iniciado sesión para pagar. | RF03 |
| CAR-17 | No deseado | Si el inventario rechaza la reserva porque el cliente superó un límite de reservas, entonces el sistema deberá explicar el motivo, mostrar el instante en que podrá intentarlo otra vez y conservar el carrito. | RF28, AC04 |
| CAR-18 | Opcional | Donde el cliente aún no registró su consentimiento para recibir avisos por WhatsApp, el sistema deberá ofrecerlo en el checkout como opción, sin exigirlo para pagar; la decisión la guarda `identidad-seguridad`. | NN15 |
| CAR-19 | Evento | Cuando el cliente cancela el checkout o vuelve al carrito sin un pago pendiente de confirmación, el sistema deberá pedir al inventario la liberación de sus reservas pendientes. | RF04, RF28 |
| CAR-20 | No deseado | Si el cliente ya posee una edición digital, entonces el sistema deberá impedir agregarla de nuevo al carrito o pagarla e indicar que ya está en su biblioteca. | NN13 |
| CAR-21 | No deseado | Si el correo de la cuenta del cliente no está verificado, entonces el sistema deberá impedir iniciar el pago, invitarlo a verificarlo y conservar su carrito. | RF28, AC04 |
| CAR-22 | Evento | Cuando el cliente acepta los términos, el tratamiento de datos o la política de reembolso digital antes de pagar, el sistema deberá guardar en la intención de pago la versión y la fecha de cada texto aceptado y pasarlas al pedido al crearse. | NN15 |
| CAR-23 | Evento | Cuando el carrito incluye impresos con despacho a domicilio, el sistema deberá sumar al total un costo de envío fijo en soles, configurable sin modificar el código, mostrarlo antes del pago, congelarlo al crear la intención de pago y guardarlo en el pedido. | RF04 |
| CAR-24 | Evento | Cuando un visitante arma un carrito y luego ingresa o se registra, el sistema deberá fusionarlo con el carrito de su cuenta, sumando las cantidades de una misma edición hasta el máximo disponible, limitando las digitales a una unidad, recortando lo que exceda los topes de CAR-38, descartando las que ya posee o dejaron de venderse, y avisando cuáles ajustó; la fusión no debe aplicarse dos veces. | RF03 |
| CAR-25 | Estado | Mientras el pago está pendiente de confirmación, el sistema deberá mostrar el estado «estamos confirmando tu pago» y el tiempo restante hasta los 30 minutos desde el inicio del pago. | RF04 |
| CAR-26 | No deseado | Si el pago está pendiente de confirmación, entonces el sistema deberá impedir volver al carrito para editarlo y cancelar el checkout hasta que se resuelva. | RF04, AC01 |
| CAR-27 | No deseado | Si al reservar se rechaza una línea por falta de stock o la fuente asignada cambia el plazo informado, entonces el sistema deberá volver a la revisión del carrito, mostrar el cambio o el nuevo plazo y conservar el carrito. | RF04, AC01 |
| CAR-28 | Evento | Cuando cambia la versión vigente de un texto legal durante el checkout, el sistema deberá volver a pedir su aceptación antes de pagar. | NN15 |
| CAR-29 | Ubicuo | El sistema deberá conservar el carrito de un visitante en su navegador hasta que se cumplan 30 días desde su última modificación (abrir el carrito no renueva el plazo), momento en que caduca. | RF03, AC04 |
| CAR-30 | Evento | Cuando el carrito contiene impresos con despacho a domicilio, el sistema deberá exigir, además de la dirección, el nombre y un teléfono válido de contacto de la entrega, no permitir continuar sin ellos, guardarlos en el checkout y pasarlos con la intención de pago al pedido (PED-32), y no enviarlos a la pasarela, al notificador ni al asistente. | RF04, NN15 |
| CAR-31 | Estado | Mientras la cuenta del cliente tiene una cancelación diferida (IDE-27), el sistema deberá impedir iniciar el pago, indicar el motivo y conservar el carrito. | RF04, RF27 |
| CAR-32 | Evento | Cuando Identidad pide eliminar el carrito y los datos de entrega de una cuenta de cliente cuyo correo no está verificado y que va a reemplazar (IDE-84), el sistema deberá eliminarlos y, si no puede, informarlo para que no se reemplace la cuenta; la cuenta nueva parte sin carrito heredado. | RF04, RF18 |
| CAR-33 | Evento | Cuando se ejecuta la cancelación de una cuenta, el sistema deberá eliminar en la misma operación (IDE-47) su carrito y los datos de entrega que tenga en el checkout y, si no puede, informarlo para que no se ejecute la cancelación (IDE-70). | RF04, RF27, RNF23 |
| CAR-34 | Ubicuo | El sistema deberá eliminar el nombre, el teléfono y la dirección de entrega del checkout cuando Pagos informa que el pago se aprobó, cuando el cliente cancela o reinicia el checkout y a los 30 días de su última modificación. | RF04, RNF23 |
| CAR-35 | No deseado | Si una solicitud pide un carrito o un checkout que no es de la cuenta de su credencial, entonces el sistema deberá rechazarla sin revelar si existe y registrarla como evento de seguridad. | RF04, AC04 |
| CAR-36 | Estado | Mientras la cuenta del cliente no aceptó la versión vigente de los textos legales (IDE-58), el sistema deberá impedir iniciar el pago, invitarlo a aceptarla y conservar el carrito. | RF04, NN15 |
| CAR-37 | Evento | Cuando el cliente inicia el pago, el sistema deberá consultar en el servidor el estado de su cuenta (IDE-27, IDE-58) por el contrato público de Identidad, antes de reservar o crear la intención de pago, y no iniciar el pago si no puede consultarlo. | RF04, RF27, AC04 |
| CAR-38 | Ubicuo | El sistema deberá admitir en el carrito de cualquier cliente o visitante, como máximo, 30 líneas y 20 unidades por línea impresa y rechazar lo que los exceda con `ECO-CARRITO-LIMITE-DE-LINEAS`, `ECO-CARRITO-LIMITE-DE-UNIDADES` o `ECO-VALIDACION-INVALIDA`; el máximo de la red manda si es menor (CAR-04) y la fusión (CAR-24) recorta en lugar de rechazar. | RF03, AC04 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-CAR-01 | CAR-01, CAR-02 | **Dado** un cliente con dos líneas en el carrito desde su celular, **cuando** abre su cuenta en otro dispositivo, **entonces** ve las mismas líneas y el mismo total. |
| AC-CAR-02 | CAR-03 | **Dado** un carrito con un impreso, **cuando** el cliente agrega una edición digital y trata de poner dos unidades de ella, **entonces** el carrito acepta ambas ediciones y deja la digital en una unidad. |
| AC-CAR-03 | CAR-04 | **Dado** un impreso con 3 unidades vendibles en la red, **cuando** el cliente pide 5, **entonces** se rechaza indicando que el máximo es 3. |
| AC-CAR-04 | CAR-05 | **Dado** un carrito con un impreso, **cuando** el cliente inicia el checkout, **entonces** se consulta el inventario y se muestra si cada línea está en su sucursal o vendrá por traslado. |
| AC-CAR-05 | CAR-06 | **Dado** que el inventario no responde, **cuando** el cliente inicia el checkout con impresos, **entonces** se le informa que no se puede validar el stock y no se le cobra. |
| AC-CAR-06 | CAR-07, CAR-15 | **Dado** un carrito solo con dos libros digitales, **cuando** el cliente hace el checkout, **entonces** no se le pide entrega, no se consulta el inventario y completa la compra en cuatro pasos o menos. |
| AC-CAR-07 | CAR-08 | **Dado** un carrito con impresos, **cuando** el cliente elige domicilio sin escribir la dirección, **entonces** no puede continuar. |
| AC-CAR-08 | CAR-09 | **Dado** un libro que solo está en Cusco y recojo en Ayacucho, **cuando** el cliente elige recojo, **entonces** se le avisa del traslado y del plazo estimado antes de pagar, sin que el vendible cambie; y si la ruta no tiene plazo definido, ve «a confirmar». |
| AC-CAR-09 | CAR-10 | **Dado** un checkout con impresos, **cuando** el cliente pasa al paso de pago, **entonces** se crea la intención de pago, las unidades quedan reservadas y se muestra la cuenta regresiva de 15 minutos desde ese momento. |
| AC-CAR-10 | CAR-11 | **Dado** una reserva de 15 minutos, **cuando** vence antes de pagar, **entonces** el pago se bloquea y se invita a reiniciar el checkout. |
| AC-CAR-11 | CAR-13 | **Dado** un carrito con un libro digital, **cuando** el cliente intenta pagar sin aceptar la política de reembolso digital, **entonces** no puede continuar. |
| AC-CAR-12 | CAR-14 | **Dado** una edición digital cuya licencia vence antes de que el cliente inicie el pago, **cuando** confirma, **entonces** esa línea se bloquea y se le invita a quitarla; y si vence con el pago ya pendiente, la compra se concede. |
| AC-CAR-13 | CAR-16 | **Dado** un visitante sin sesión, **cuando** pulsa pagar, **entonces** se le pide iniciar sesión y su carrito se conserva. |
| AC-CAR-14 | CAR-17 | **Dado** un cliente que ya tiene una reserva pendiente, **cuando** inicia el pago de otro carrito, **entonces** ve el motivo del límite, el instante en que puede reintentar y su carrito intacto. |
| AC-CAR-15 | CAR-12 | **Dado** un carrito cualquiera, **cuando** el cliente intenta pagar sin aceptar los términos y el tratamiento de datos, **entonces** no puede continuar. |
| AC-CAR-16 | CAR-18 | **Dado** un cliente sin consentimiento de WhatsApp, **cuando** llega al pago sin marcarlo, **entonces** puede pagar y no recibe avisos por WhatsApp. |
| AC-CAR-17 | CAR-19 | **Dado** un checkout con reservas pendientes, **cuando** el cliente vuelve al carrito, **entonces** las unidades quedan libres de inmediato. |
| AC-CAR-18 | CAR-20 | **Dado** un cliente que ya compró una edición digital, **cuando** intenta agregarla al carrito o pagarla desde otro carrito abierto, **entonces** se rechaza indicando que ya está en su biblioteca. |
| AC-CAR-19 | CAR-21 | **Dado** un cliente con el correo sin verificar, con un carrito con impresos o solo digital, **cuando** pulsa pagar, **entonces** no se crea ninguna reserva ni intención de pago, se le invita a verificar su correo y su carrito sigue intacto. |
| AC-CAR-20 | CAR-22, CAR-28 | **Dado** un cliente que acepta los términos, la privacidad y la política de reembolso digital, **cuando** paga, **entonces** el pedido guarda la versión y la fecha de cada texto aceptado; y si una versión cambia durante el checkout, se vuelve a pedir su aceptación antes de pagar. |
| AC-CAR-21 | CAR-23 | **Dado** un carrito de S/ 111,90 con un impreso y despacho a domicilio y un costo de envío configurado en S/ 12,00, **cuando** el cliente llega al pago, **entonces** ve el envío de S/ 12,00 y un total de S/ 123,90, que no cambia aunque se modifique la configuración durante el pago; con recojo, el total es S/ 111,90; y un carrito solo digital no lleva envío. |
| AC-CAR-22 | CAR-24 | **Dado** un visitante con un impreso en su carrito y una cuenta con ese mismo impreso (1 unidad), un digital que el cliente ya posee y solo 1 unidad disponible en total, **cuando** ingresa, **entonces** el impreso queda con la cantidad máxima disponible, se descarta el digital poseído y se avisa de ambos ajustes; y ingresar en dos dispositivos a la vez no duplica las líneas. |
| AC-CAR-23 | CAR-25, CAR-26 | **Dado** un pago pendiente de confirmación, **cuando** el cliente mira su checkout y trata de volver al carrito, **entonces** ve «estamos confirmando tu pago» con el tiempo restante hasta los 30 minutos y no puede editar el carrito hasta que se resuelva. |
| AC-CAR-24 | CAR-27 | **Dado** un checkout cuya reserva es rechazada por falta de stock, o reasignada a una fuente con otro plazo, **cuando** el cliente intenta pagar, **entonces** vuelve a la revisión con el cambio explicado y su carrito conservado. |
| AC-CAR-25 | CAR-29 | **Dado** un visitante que vuelve a los 29 días de su última modificación y otro que vuelve a los 30 días o más, **cuando** abren su carrito, **entonces** el primero conserva el suyo y el segundo encuentra el carrito caducado y uno nuevo vacío. |
| AC-CAR-26 | CAR-30 | **Dado** un carrito con impresos y despacho a domicilio, **cuando** el cliente llega al paso de entrega, **entonces** se le piden la dirección, el nombre y el teléfono de contacto, no puede continuar sin alguno de ellos ni con un teléfono inválido, el pedido recibe el nombre, el teléfono y la dirección, y la pasarela, el notificador y el asistente no los reciben; y para el recojo en sucursal no se piden. |
| AC-CAR-27 | CAR-31, CAR-36, CAR-37 | **Dado** un cliente con una cancelación diferida, otro que no aceptó la versión vigente de los términos y otro cuya cuenta cambia de estado entre la revisión del carrito y el pago, **cuando** intentan iniciar el pago, **entonces** se les impide con el motivo, no se crea ninguna reserva ni intención de pago y su carrito se conserva; y si Identidad no responde, el pago no se inicia. |
| AC-CAR-28 | CAR-32 | **Dado** una cuenta de cliente sin verificar con un carrito y datos de entrega, **cuando** Identidad pide eliminarlos para reemplazarla, **entonces** ambos se eliminan y la cuenta nueva parte sin carrito heredado; y si no se pueden eliminar, se informa para que la cuenta no se reemplace. |
| AC-CAR-29 | CAR-33 | **Dado** un cliente con un carrito, datos de entrega y un checkout abierto, **cuando** se ejecuta la cancelación de su cuenta, **entonces** el carrito y esos datos dejan de existir; y si no se pueden eliminar, se informa para que la cancelación no se ejecute y no queda ningún efecto parcial. |
| AC-CAR-30 | CAR-34 | **Dado** un checkout con nombre, teléfono y dirección de entrega, **cuando** Pagos informa que el pago se aprobó, o el cliente cancela o reinicia el checkout, o pasan 30 días desde su última modificación, **entonces** esos datos dejan de existir en el checkout. |
| AC-CAR-31 | CAR-35 | **Dado** un cliente A que pide el carrito o el checkout de un cliente B, **cuando** envía la solicitud, **entonces** se rechaza sin revelar si existe, no se devuelve ningún dato de entrega y queda un evento de seguridad. |
| AC-CAR-32 | CAR-38 | **Dado** un carrito con 29 líneas, **cuando** el cliente agrega la línea 30 y luego la 31, **entonces** la 30 se acepta, la 31 se rechaza con `ECO-CARRITO-LIMITE-DE-LINEAS` y el carrito queda intacto; y **dado** una línea impresa con 20 unidades, **cuando** agrega una más a esa línea, **entonces** se rechaza con `ECO-CARRITO-LIMITE-DE-UNIDADES`, y si pide 21 de una edición nueva se rechaza con `ECO-VALIDACION-INVALIDA`; en ambos casos el carrito queda intacto. |
| AC-CAR-33 | CAR-24, CAR-38 | **Dado** un visitante y una cuenta cuya fusión superaría 30 líneas o 20 unidades en una línea impresa, **cuando** el visitante ingresa, **entonces** la fusión se recorta a 30 líneas y 20 unidades por línea, descarta las últimas líneas del visitante, en el orden en que las agregó, avisa con el motivo `tope_de_lineas` o `tope_de_unidades` y no quita nada de lo que la cuenta ya tenía. |

## 5. Reglas e invariantes

- La disponibilidad se valida contra el inventario real al iniciar el checkout y al reservar; nunca solo con la caché.
- Un carrito solo digital no genera consulta ni reserva en el inventario.
- La reserva dura 15 minutos desde que se inicia el pago (cuando el cliente pasa al paso de pago) y cubre todas las líneas impresas del carrito; con un pago pendiente de confirmación se extiende hasta 30 minutos (`ecommerce-pagos`).
- El total del carrito es la suma de las líneas más el envío cuando corresponde.
- El costo de envío es uno por pedido a domicilio, aunque salga en más de un envío.
- La aceptación de los textos legales tiene dos momentos: la de la cuenta, que Identidad pide al ingresar o renovar la sesión (IDE-43), y la del checkout, cuya versión se guarda en la intención de pago (CAR-22); ambas deben citar la versión vigente (IDE-57) y el bloqueo de CAR-36 se levanta cuando la cuenta acepta la versión vigente.
- El nombre, el teléfono y la dirección de entrega viven en el checkout y en la intención de pago hasta que se crea el pedido, que guarda su propia copia (PED-32); el uso «solo para el despacho» incluye esa copia y la guía del operador (ENV-02).

## 6. Casos límite y errores

- Si el cliente cambia la cantidad durante el checkout, la reserva se rehace.
- Un carrito con una línea no disponible se puede corregir sin perder las demás.
- Un cliente que cambia de sucursal preferida mantiene su carrito.

## 7. Preguntas abiertas

- [x] ¿Un visitante puede armar un carrito antes de iniciar sesión? Resuelto el 2026-10-16 (confirmado por el responsable): sí, y se fusiona con el de su cuenta al ingresar (CAR-24). Si el stock no alcanza para la suma, la cantidad se ajusta al máximo con aviso; el carrito de visitante dura 30 días (CAR-29) y todo carrito admite 30 líneas y 20 unidades por línea (CAR-38; topes confirmados por la persona el 2026-11-19, DD-16, en lugar de los 50 y 99 de esta respuesta).
- [x] ¿Cuál es el costo del despacho a domicilio? Resuelto el 2026-10-16: fijo de S/ 12,00, configurable sin modificar el código (CAR-23).
