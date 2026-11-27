---
id: ecommerce-carrito-checkout
titulo: Carrito y checkout
estado: Planned
plan_aprobado_por: Claude (delegado por Diego Amiquero)
plan_aprobado_en: 2026-11-27
aprobado_por: Claude (delegado por Diego Amiquero)
aprobado_en: 2026-11-27
servicio: E-commerce
prefijo: CAR
origen: [RF02, RF03, RF04, RF11, RF15, RF18, RF20, RF27, RF28, NN02, NN13, NN15, NN18]
calidad: [AC01, AC03, AC06, AC04, RNF01, RNF03, RNF05, RNF08, RNF13, RNF23]
depende: [ecommerce-catalogo, inventario-seleccion-fuente, identidad-seguridad, transversal-seguridad, transversal-auditoria]
---

# Spec — Carrito y checkout

## 1. Propósito

Que el cliente arme un carrito que se conserva entre sesiones y dispositivos, mezcle libros impresos y digitales, y llegue al pago con la certeza de que lo que compra existe (NN02, NN13). Cada libro impreso se compra en la sucursal que la persona elige, que atiende esa línea con su stock real, sin combinarse con otras (venta por sucursal, decidida el 2026-11-27). Un carrito solo digital no pide entrega ni consulta al Inventario.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Carrito persistente por cuenta, líneas mixtas y una sucursal por línea impresa, elegida en la ficha del libro. | El cobro (`ecommerce-pagos`) y el pedido resultante (`ecommerce-pedidos`). |
| Validación de disponibilidad por sucursal, entrega, costo de envío por sucursal (y su configuración) y reserva temporal. | La reserva en el stock de la sucursal (`inventario-seleccion-fuente`). |
| Consentimientos y aceptación de términos antes del pago. | La creación de la cuenta (`identidad-seguridad`). |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| CAR-01 | Ubicuo | El sistema deberá conservar el carrito de cada cliente autenticado entre sesiones y dispositivos, y el de un visitante en su navegador (CAR-29). | RF03 |
| CAR-02 | Evento | Cuando el cliente agrega, modifica o elimina una línea, el sistema deberá actualizar el carrito y su total. | RF03 |
| CAR-03 | Ubicuo | El sistema deberá permitir que un mismo carrito contenga ediciones impresas y digitales, y limitar cada edición digital a una unidad. | RF03, NN13 |
| CAR-04 | No deseado | Si el cliente agrega o aumenta unidades de un impreso y las líneas de esa sucursal pasan a sumar más que su stock vendible, entonces el sistema deberá rechazar la cantidad e indicar la máxima disponible en ella. | RF03, RF15, RNF01 |
| CAR-05 | Evento | Cuando el cliente inicia el checkout con impresos, el sistema deberá validar contra el inventario, antes de continuar, la disponibilidad real de cada línea en su sucursal, sumando las líneas del mismo libro y sucursal. | RF04, RF15 |
| CAR-06 | No deseado | Si el inventario no responde durante el checkout, entonces el sistema deberá informar que no puede validar el stock y no continuar con el cobro. | AC03, RNF03 |
| CAR-07 | Evento | Cuando el carrito contiene solo ediciones digitales, el sistema deberá omitir la entrega y no consultar el inventario. | RF04, NN13 |
| CAR-08 | Evento | Cuando el carrito contiene impresos, el sistema deberá pedir elegir entre recojo gratuito, cada línea en su sucursal, o despacho a domicilio con su dirección. | RF04 |
| CAR-09 | Evento | Cuando el cliente elige la entrega de sus impresos, el sistema deberá mostrar antes de pagar la sucursal que atiende cada línea y, a domicilio, el costo de envío de cada sucursal y su suma, comprobando la disponibilidad en cada sucursal sin reservar. | RF04, RF11 |
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
| CAR-23 | Evento | Cuando el carrito incluye impresos con despacho a domicilio, el sistema deberá sumar al total una sola vez el costo de envío de cada sucursal que atiende alguna línea, mostrarlo por sucursal antes del pago, congelar al crear la intención de pago el importe de cada sucursal y su suma, y guardar ambos en el pedido. | RF04, RF11 |
| CAR-24 | Evento | Cuando un visitante arma un carrito y luego ingresa o se registra, el sistema deberá fusionarlo con el de su cuenta agregando cada línea según CAR-40, recortando lo que exceda el stock de su sucursal o los topes de CAR-38, descartando las digitales poseídas y lo que dejó de venderse, avisando los ajustes y sin aplicar dos veces la fusión. | RF03 |
| CAR-25 | Estado | Mientras el pago está pendiente de confirmación, el sistema deberá mostrar el estado «estamos confirmando tu pago» y el tiempo restante hasta los 30 minutos desde el inicio del pago. | RF04 |
| CAR-26 | No deseado | Si el pago está pendiente de confirmación, entonces el sistema deberá impedir volver al carrito para editarlo y cancelar el checkout hasta que se resuelva. | RF04, AC01 |
| CAR-27 | No deseado | Si al reservar se rechaza una línea por falta de stock en su sucursal, entonces el sistema deberá volver a la revisión del carrito, indicar la línea, la sucursal y lo disponible en ella y conservar el carrito sin cambiarle la sucursal. | RF04, AC01 |
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
| CAR-38 | Ubicuo | El sistema deberá admitir en el carrito de cualquier cliente o visitante, como máximo, 30 líneas y 20 unidades por línea impresa y rechazar lo que los exceda con `ECO-CARRITO-LIMITE-DE-LINEAS`, `ECO-CARRITO-LIMITE-DE-UNIDADES` o `ECO-VALIDACION-INVALIDA`; el vendible de la sucursal manda si es menor (CAR-04) y la fusión (CAR-24) recorta en lugar de rechazar. | RF03, AC04 |
| CAR-39 | Evento | Cuando se agrega un impreso al carrito, el sistema deberá exigir la sucursal registrada que lo atenderá, guardarla en la línea y declararla al consultar y reservar su stock; una edición digital no lleva sucursal. | RF03, RF15 |
| CAR-40 | Evento | Cuando se agrega un impreso en una sucursal, el sistema deberá sumar la cantidad a la línea más antigua de esa edición y sucursal que la admita entera sin pasar de 20 unidades y, si ninguna la admite, abrir otra línea con la cantidad completa. | RF03, RF15 |
| CAR-41 | Ubicuo | El sistema no deberá cambiar la sucursal de una línea, dividirla, unirla con otra ni reasignarla sin que la persona lo elija; sumar una adición según CAR-40 no es unir líneas. | RF15 |
| CAR-42 | Evento | Cuando la persona abre la ficha de un impreso, el sistema deberá mostrar el stock vendible de cada sucursal, pedir elegir una, ofrecer como máximo la menor cantidad entre 20 y el vendible de la elegida y preseleccionar la última que eligió solo si consta que tiene stock. | RF02, RF15 |
| CAR-43 | No deseado | Si la ficha no puede confirmar el stock por sucursal, entonces el sistema deberá permitir elegir entre las sucursales de la última lista obtenida, que conserva sin caducidad, sin mostrar cantidades y con el aviso de CAT-12, y validar el stock al iniciar el checkout. | RF02, AC03 |
| CAR-44 | Evento | Cuando la persona cambia la cantidad en la tarjeta de una línea del carrito, el sistema deberá guardarla y mostrar actualizadas la línea y los totales sin que pulse otro control, mostrando siempre en cada tarjeta de un impreso su sucursal. | RF03, AC06 |
| CAR-45 | No deseado | Si un carrito conserva un impreso sin sucursal, anterior a la venta por sucursal, entonces el sistema deberá mantenerlo sin sumarlo a otro, pedir quitarlo y volver a agregarlo eligiendo sucursal e impedir iniciar, continuar o pagar su checkout, salvo lo de CAR-50. | RF03, RF15 |
| CAR-46 | Evento | Cuando la administración central cambia el costo de envío de una sucursal, el sistema deberá guardarlo, aplicarlo a las compras que aún no congelaron su total y auditar en la misma operación la sucursal y los importes anterior y nuevo, sin cambiar nada si no puede auditar. | RF11, RF20, RNF08 |
| CAR-47 | Ubicuo | El sistema deberá aplicar S/ 30,00, modificable sin cambiar el código, como costo de envío de toda sucursal sin uno propio y mostrar a la administración central el costo vigente de cada sucursal. | RF11 |
| CAR-48 | No deseado | Si quien consulta o cambia los costos de envío no es la administración central, la sucursal no está registrada o el importe no está entre S/ 0,00 y S/ 500,00 con hasta dos decimales, entonces el sistema deberá rechazarlo sin cambios y registrar el intento sin permiso como acceso denegado. | RF11, RNF05 |
| CAR-49 | No deseado | Si el total cambia entre lo mostrado y el paso al pago porque cambió un costo de envío, entonces el sistema deberá no crear la intención ni reservar con el total anterior, mostrar el nuevo y pedir confirmarlo. | RF04, RF11 |
| CAR-50 | Estado | Mientras un pago iniciado antes de la venta por sucursal está pendiente de confirmación, o aprobado sin completarse, el sistema deberá completarlo o abortarlo con las reglas, la entrega y el costo de envío con que se inició. | RF04, AC01 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-CAR-01 | CAR-01, CAR-02 | **Dado** un cliente con dos líneas en el carrito desde su celular, **cuando** abre su cuenta en otro dispositivo, **entonces** ve las mismas líneas y el mismo total. |
| AC-CAR-02 | CAR-03 | **Dado** un carrito con un impreso, **cuando** el cliente agrega una edición digital y trata de poner dos unidades de ella, **entonces** el carrito acepta ambas ediciones y deja la digital en una unidad. |
| AC-CAR-03 | CAR-04 | **Dado** un impreso con 3 unidades vendibles en Ayacucho y 10 en Cusco, **cuando** el cliente pide 5 en Ayacucho, o agrega allí 2 más teniendo ya 2, **entonces** se rechaza indicando que en Ayacucho el máximo es 3; y 5 en Cusco se aceptan. |
| AC-CAR-04 | CAR-05 | **Dado** un carrito con dos líneas de 3 unidades de un libro en Ayacucho, donde quedan 5, y otra de Cusco, **cuando** el cliente inicia el checkout, **entonces** se consulta el inventario declarando la sucursal de cada línea, se indica que en Ayacucho no alcanza para la suma y que Cusco la cubre, y no se habla de traslados. |
| AC-CAR-05 | CAR-06 | **Dado** que el inventario no responde, **cuando** el cliente inicia el checkout con impresos, **entonces** se le informa que no se puede validar el stock y no se le cobra. |
| AC-CAR-06 | CAR-07, CAR-15 | **Dado** un carrito solo con dos libros digitales, **cuando** el cliente hace el checkout, **entonces** no se le pide entrega, no se consulta el inventario y completa la compra en cuatro pasos o menos. |
| AC-CAR-07 | CAR-08 | **Dado** un carrito con impresos, **cuando** el cliente elige domicilio sin escribir la dirección, **entonces** no puede continuar. |
| AC-CAR-08 | CAR-08, CAR-09 | **Dado** un carrito con dos líneas de Ayacucho y una de Cusco y el costo por defecto, **cuando** el cliente elige domicilio, **entonces** ve S/ 30,00 por Ayacucho, S/ 30,00 por Cusco y un envío de S/ 60,00; y si elige recojo, ve envío gratis y la sucursal donde recogerá cada línea, sin elegir otra; en ambos casos el vendible no cambia. |
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
| AC-CAR-21 | CAR-23, CAR-08 | **Dado** un carrito de S/ 111,90 con dos líneas de impresos de Ayacucho y despacho a domicilio, **cuando** el cliente llega al pago, **entonces** ve un solo envío de S/ 30,00 y un total de S/ 141,90; con una línea de Cusco añadida paga además S/ 30,00 por Cusco; el total no cambia aunque cambie un costo durante el pago; el pedido guarda el importe de cada sucursal y la suma de los envíos; con recojo no hay envío y un carrito solo digital tampoco. |
| AC-CAR-22 | CAR-24, CAR-04 | **Dado** un visitante con un impreso en Ayacucho y una cuenta con ese mismo impreso en Ayacucho (1 unidad), un digital que el cliente ya posee y solo 1 unidad vendible en Ayacucho, **cuando** ingresa, **entonces** las líneas del impreso en Ayacucho suman la cantidad máxima disponible, se descarta el digital poseído y se avisa de ambos ajustes; y ingresar en dos dispositivos a la vez no duplica las líneas. |
| AC-CAR-23 | CAR-25, CAR-26 | **Dado** un pago pendiente de confirmación, **cuando** el cliente mira su checkout y trata de volver al carrito, **entonces** ve «estamos confirmando tu pago» con el tiempo restante hasta los 30 minutos y no puede editar el carrito hasta que se resuelva. |
| AC-CAR-24 | CAR-27, CAR-41 | **Dado** un checkout con 3 unidades en Ayacucho cuya reserva es rechazada porque allí quedan 2, aunque Cusco tenga stock, **cuando** el cliente intenta pagar, **entonces** vuelve a la revisión con la línea, la sucursal y las 2 disponibles, no se cobra, y la línea conserva su sucursal y su cantidad. |
| AC-CAR-25 | CAR-29 | **Dado** un visitante que vuelve a los 29 días de su última modificación y otro que vuelve a los 30 días o más, **cuando** abren su carrito, **entonces** el primero conserva el suyo y el segundo encuentra el carrito caducado y uno nuevo vacío. |
| AC-CAR-26 | CAR-30 | **Dado** un carrito con impresos y despacho a domicilio, **cuando** el cliente llega al paso de entrega, **entonces** se le piden la dirección, el nombre y el teléfono de contacto, no puede continuar sin alguno de ellos ni con un teléfono inválido, el pedido recibe el nombre, el teléfono y la dirección, y la pasarela, el notificador y el asistente no los reciben; y para el recojo en sucursal no se piden. |
| AC-CAR-27 | CAR-31, CAR-36, CAR-37 | **Dado** un cliente con una cancelación diferida, otro que no aceptó la versión vigente de los términos y otro cuya cuenta cambia de estado entre la revisión del carrito y el pago, **cuando** intentan iniciar el pago, **entonces** se les impide con el motivo, no se crea ninguna reserva ni intención de pago y su carrito se conserva; y si Identidad no responde, el pago no se inicia. |
| AC-CAR-28 | CAR-32 | **Dado** una cuenta de cliente sin verificar con un carrito y datos de entrega, **cuando** Identidad pide eliminarlos para reemplazarla, **entonces** ambos se eliminan y la cuenta nueva parte sin carrito heredado; y si no se pueden eliminar, se informa para que la cuenta no se reemplace. |
| AC-CAR-29 | CAR-33 | **Dado** un cliente con un carrito, datos de entrega y un checkout abierto, **cuando** se ejecuta la cancelación de su cuenta, **entonces** el carrito y esos datos dejan de existir; y si no se pueden eliminar, se informa para que la cancelación no se ejecute y no queda ningún efecto parcial. |
| AC-CAR-30 | CAR-34 | **Dado** un checkout con nombre, teléfono y dirección de entrega, **cuando** Pagos informa que el pago se aprobó, o el cliente cancela o reinicia el checkout, o pasan 30 días desde su última modificación, **entonces** esos datos dejan de existir en el checkout. |
| AC-CAR-31 | CAR-35 | **Dado** un cliente A que pide el carrito o el checkout de un cliente B, **cuando** envía la solicitud, **entonces** se rechaza sin revelar si existe, no se devuelve ningún dato de entrega y queda un evento de seguridad. |
| AC-CAR-32 | CAR-38, CAR-40 | **Dado** un carrito con 29 líneas, **cuando** el cliente agrega la línea 30 y luego la 31, **entonces** la 30 se acepta, la 31 se rechaza con `ECO-CARRITO-LIMITE-DE-LINEAS` y el carrito queda intacto; **dado** una línea impresa con 20 unidades, **cuando** la modifica a 21, **entonces** se rechaza con `ECO-CARRITO-LIMITE-DE-UNIDADES`; si agrega 21 de una vez se rechaza con `ECO-VALIDACION-INVALIDA`; y si agrega 1 más del mismo libro y sucursal se abre otra línea. |
| AC-CAR-33 | CAR-24, CAR-38 | **Dado** un visitante y una cuenta cuya fusión superaría 30 líneas, **cuando** el visitante ingresa, **entonces** la fusión se recorta a 30 líneas, descarta las últimas líneas del visitante en el orden en que las agregó, avisa con el motivo `tope_de_lineas` y no quita nada de lo que la cuenta ya tenía; y dos líneas de 15 del mismo libro y sucursal quedan como dos líneas si hay stock para 30. |
| AC-CAR-34 | CAR-39 | **Dado** un impreso y un digital, **cuando** se pide agregar el impreso sin sucursal, o con una sucursal que no existe, **entonces** se rechaza sin cambiar el carrito; con una sucursal registrada la línea la guarda y el inventario la recibe al validar y reservar; y el digital se agrega sin sucursal. |
| AC-CAR-35 | CAR-40, CAR-41, CAR-04 | **Dado** un impreso con 30 vendibles en Ayacucho y 10 en Cusco, **cuando** el cliente agrega 18 en Ayacucho y 6 en Cusco, **entonces** quedan dos líneas, cada una en su sucursal; al agregar 5 más en Ayacucho se abre una tercera línea de 5; al agregar 2 más en Cusco la de Cusco pasa a 8; y pedir 5 más en Cusco se rechaza sin tocar la sucursal ni la cantidad de ninguna línea. |
| AC-CAR-36 | CAR-42 | **Dado** un impreso con 25 en Ayacucho, 4 en Cusco y ninguno en Lima, y una persona cuya última sucursal elegida fue Cusco, **cuando** abre la ficha, **entonces** ve el stock de cada sucursal, Cusco preseleccionada con máximo 4, al elegir Ayacucho el máximo es 20 y no puede agregar desde Lima; y si la preferida no tiene stock no se preselecciona. |
| AC-CAR-37 | CAR-43 | **Dado** que el inventario no responde y no hay un dato vigente, **cuando** la persona abre la ficha, **entonces** puede elegir una sucursal de la última lista conocida y agregar sin ver cantidades, con el aviso de que no se puede confirmar, y el checkout valida el stock. |
| AC-CAR-38 | CAR-44 | **Dado** un carrito con dos líneas de sucursales distintas, **cuando** la persona cambia la cantidad de una tarjeta, **entonces** la cantidad se guarda y la línea y los totales se actualizan sin pulsar «Actualizar», cada tarjeta muestra su sucursal, una cantidad rechazada se explica y conserva la anterior, y sin código en el navegador un botón aplica el cambio. |
| AC-CAR-39 | CAR-45, CAR-50 | **Dado** una base anterior con un carrito de visitante y otro de cuenta con impresos sin sucursal, un checkout en el paso de pago sin pago pendiente y otro con el pago pendiente de confirmación, **cuando** se actualiza el esquema dos veces, **entonces** las líneas se conservan y no se suman al fusionar; el primer checkout no entrega su intención para pagar, libera su reserva al volver a la revisión y no avanza hasta volver a agregar las líneas; y el pago pendiente se completa con su entrega y costo originales y quita del carrito lo comprado. |
| AC-CAR-40 | CAR-46, CAR-47 | **Dado** una instalación sin costos propios, **cuando** la administración central consulta los costos, **entonces** cada sucursal muestra S/ 30,00; al cambiar Cusco a S/ 18,50 las compras sin total congelado lo usan, las demás no, y queda la auditoría con el anterior y el nuevo; y si no se puede auditar, el costo no cambia. |
| AC-CAR-41 | CAR-48 | **Dado** un encargado de sucursal, logística, un cliente y un visitante, **cuando** intentan ver o cambiar los costos de envío, **entonces** se rechaza y queda un acceso denegado; y la administración no puede guardar un importe negativo, mayor que 500,00 o con tres decimales, ni el de una sucursal que no existe. |
| AC-CAR-42 | CAR-49 | **Dado** un checkout a domicilio que muestra un total, **cuando** la administración cambia el costo de una de sus sucursales antes de que el cliente pase al pago, **entonces** no se crea intención ni reserva con el total anterior, se muestra el nuevo y el cliente puede confirmarlo. |

## 5. Reglas e invariantes

- La disponibilidad se valida por sucursal contra el inventario real al iniciar el checkout y al reservar; nunca solo con la caché.
- Una **línea impresa** es un libro en una sucursal; puede haber varias (lotes) del mismo libro y sucursal mientras la suma no pase de su stock. La compra del cliente no genera traslados y el precio no cambia según la sucursal.
- La reserva sigue siendo una sola y atómica para todas las líneas impresas del checkout.
- El cambio de cantidad sin otro control (CAR-44) es una mejora progresiva: sin código en el navegador, un botón aplica el cambio.
- La sucursal que la persona eligió se recuerda solo para preseleccionarla en la ficha (WEB-16); la cabecera no tiene selector de sucursal.
- Un checkout anterior en preparación, reservando o en el paso de pago, sin pago pendiente de confirmación, no se puede pagar: vuelve a la revisión del carrito y su reserva se libera (CAR-45). Solo un pago pendiente de confirmación o ya aprobado se resuelve como se inició (CAR-50).
- Un carrito solo digital no genera consulta ni reserva en el inventario.
- La reserva dura 15 minutos desde que se inicia el pago (cuando el cliente pasa al paso de pago) y cubre todas las líneas impresas del carrito; con un pago pendiente de confirmación se extiende hasta 30 minutos (`ecommerce-pagos`).
- El total del carrito es la suma de las líneas más el envío cuando corresponde.
- El costo de envío a domicilio es uno por cada sucursal que atiende el pedido, aunque tenga varias líneas o su mercadería salga en más de un envío; el total de envío es la suma. El recojo es gratuito.
- La aceptación de los textos legales tiene dos momentos: la de la cuenta, que Identidad pide al ingresar o renovar la sesión (IDE-43), y la del checkout, cuya versión se guarda en la intención de pago (CAR-22); ambas deben citar la versión vigente (IDE-57) y el bloqueo de CAR-36 se levanta cuando la cuenta acepta la versión vigente.
- El nombre, el teléfono y la dirección de entrega viven en el checkout y en la intención de pago hasta que se crea el pedido, que guarda su propia copia (PED-32); el uso «solo para el despacho» incluye esa copia y la guía del operador (ENV-02).

## 6. Casos límite y errores

- Si el cliente cambia la cantidad durante el checkout, la reserva se rehace.
- Un carrito con una línea no disponible se puede corregir sin perder las demás.
- Elegir otra sucursal en la ficha no cambia las líneas que ya están en el carrito.

## 7. Preguntas abiertas

- [x] ¿El importe de envío de cada sucursal se guarda o solo la suma? Resuelto el 2026-11-27 por la persona responsable (`progress/decisiones-delegadas.md` DD-136): se guarda lo que cobra cada sucursal, además de la suma, y se devuelve exactamente eso si no llegó a enviar (CAR-23).
- [x] ¿Qué pasa con lo que la regla no cubre (agregar a una línea existente, carritos y pagos anteriores, rango del costo, sucursal preferida)? Resuelto el 2026-11-27 por delegación, con la alternativa recomendada, registrado en `progress/decisiones-delegadas.md` (CAR-40, CAR-45, CAR-48, CAR-50).
- [x] ¿Un visitante puede armar un carrito antes de iniciar sesión? Resuelto el 2026-10-16 (confirmado por el responsable): sí, y se fusiona con el de su cuenta al ingresar (CAR-24). Si el stock no alcanza para la suma, la cantidad se ajusta al máximo con aviso; el carrito de visitante dura 30 días (CAR-29) y todo carrito admite 30 líneas y 20 unidades por línea (CAR-38; topes confirmados por la persona el 2026-11-19, DD-16, en lugar de los 50 y 99 de esta respuesta). Desde la venta por sucursal (2026-11-27) la línea es libro y sucursal, con el mismo tope de 20 y lotes.
- [x] ¿Cuál es el costo del despacho a domicilio? Resuelto el 2026-10-16: fijo de S/ 12,00, configurable sin modificar el código (CAR-23). **Sustituido el 2026-11-27** (venta por sucursal, aprobada por la persona): cada sucursal que atiende cobra su envío, incluida la primera, S/ 30,00 por defecto y configurable por la administración central con auditoría (CAR-23, CAR-46, CAR-47).
