---
id: ecommerce-carrito-checkout
titulo: Carrito y checkout
estado: Draft
servicio: E-commerce
prefijo: CAR
origen: [RF03, RF04, RF28, NN02, NN13, NN15, NN18]
calidad: [AC01, AC03, AC08, AC13, RNF01, RNF03, RNF13]
depende: [ecommerce-catalogo, inventario-seleccion-fuente, transversal-seguridad]
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
| CAR-01 | Ubicuo | El sistema deberá conservar el carrito de cada cliente autenticado entre sesiones y dispositivos. | RF03 |
| CAR-02 | Evento | Cuando el cliente agrega, modifica o elimina una línea, el sistema deberá actualizar el carrito y su total. | RF03 |
| CAR-03 | Ubicuo | El sistema deberá permitir que un mismo carrito contenga ediciones impresas y digitales, y limitar cada edición digital a una unidad. | RF03, NN13 |
| CAR-04 | No deseado | Si el cliente pide más unidades de un impreso que el stock vendible de la red, entonces el sistema deberá rechazar la cantidad e indicar la máxima disponible. | RF03, RNF01 |
| CAR-05 | Evento | Cuando el cliente inicia el checkout con impresos, el sistema deberá validar la disponibilidad real consultando el inventario antes de continuar. | RF04 |
| CAR-06 | No deseado | Si el inventario no responde durante el checkout, entonces el sistema deberá informar que no puede validar el stock y no continuar con el cobro. | AC03, RNF03 |
| CAR-07 | Evento | Cuando el carrito contiene solo ediciones digitales, el sistema deberá omitir la entrega y no consultar el inventario. | RF04, NN13 |
| CAR-08 | Evento | Cuando el carrito contiene impresos, el sistema deberá pedir elegir entre recojo en una sucursal o despacho a domicilio con su dirección. | RF04 |
| CAR-09 | Evento | Cuando la sucursal de recojo elegida no tiene el stock y otra fuente lo atenderá, el sistema deberá informar el traslado y su plazo estimado antes de pagar. | RF04 |
| CAR-10 | Evento | Cuando el cliente inicia el pago de impresos, el sistema deberá reservar las unidades con expiración y mostrar el tiempo restante. | RF04, AC01 |
| CAR-11 | No deseado | Si la reserva expira antes de completar el pago, entonces el sistema deberá impedir el cobro e invitar a reiniciar el checkout. | RF04, AC01 |
| CAR-12 | Ubicuo | El sistema deberá exigir, antes del pago, la aceptación de los términos y del tratamiento de datos personales. | NN15 |
| CAR-13 | Estado | Mientras el carrito contiene libros digitales, el sistema deberá exigir además la aceptación de la política de reembolso de libros digitales. | NN15 |
| CAR-14 | No deseado | Si una edición digital deja de estar disponible por su licencia durante el checkout, entonces el sistema deberá impedir su pago e invitar a quitarla. | NN14 |
| CAR-15 | Ubicuo | El sistema deberá completar la compra en no más de cinco pasos, y en no más de cuatro cuando el carrito es solo digital. | RNF13, AC08 |
| CAR-16 | Ubicuo | El sistema deberá exigir que el cliente haya iniciado sesión para pagar. | RF03 |
| CAR-17 | No deseado | Si el inventario rechaza la reserva porque el cliente superó un límite de reservas, entonces el sistema deberá explicar el motivo, mostrar el instante en que podrá intentarlo otra vez y conservar el carrito. | RF28, AC13 |
| CAR-18 | Opcional | Donde el cliente aún no registró su consentimiento para recibir avisos por WhatsApp, el sistema deberá ofrecerlo en el checkout como opción, sin exigirlo para pagar; la decisión la guarda `identidad-seguridad`. | NN15 |
| CAR-20 | No deseado | Si el cliente ya posee una edición digital, entonces el sistema deberá impedir agregarla de nuevo al carrito e indicar que ya está en su biblioteca. | NN13 |
| CAR-19 | Evento | Cuando el cliente cancela el checkout o vuelve al carrito, el sistema deberá pedir al inventario la liberación de sus reservas pendientes. | RF04, RF28 |

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
| AC-CAR-08 | CAR-09 | **Dado** un libro que solo está en Cusco y recojo en Ayacucho, **cuando** el cliente elige recojo, **entonces** se le avisa del traslado y del plazo estimado antes de pagar. |
| AC-CAR-09 | CAR-10 | **Dado** un checkout con impresos, **cuando** el cliente llega al pago, **entonces** las unidades quedan reservadas y se muestra la cuenta regresiva de 15 minutos. |
| AC-CAR-10 | CAR-11 | **Dado** una reserva de 15 minutos, **cuando** vence antes de pagar, **entonces** el pago se bloquea y se invita a reiniciar el checkout. |
| AC-CAR-11 | CAR-13 | **Dado** un carrito con un libro digital, **cuando** el cliente intenta pagar sin aceptar la política de reembolso digital, **entonces** no puede continuar. |
| AC-CAR-15 | CAR-12 | **Dado** un carrito cualquiera, **cuando** el cliente intenta pagar sin aceptar los términos y el tratamiento de datos, **entonces** no puede continuar. |
| AC-CAR-16 | CAR-18 | **Dado** un cliente sin consentimiento de WhatsApp, **cuando** llega al pago sin marcarlo, **entonces** puede pagar y no recibe avisos por WhatsApp. |
| AC-CAR-18 | CAR-20 | **Dado** un cliente que ya compró una edición digital, **cuando** intenta agregarla al carrito, **entonces** se rechaza indicando que ya está en su biblioteca. |
| AC-CAR-17 | CAR-19 | **Dado** un checkout con reservas pendientes, **cuando** el cliente vuelve al carrito, **entonces** las unidades quedan libres de inmediato. |
| AC-CAR-12 | CAR-14 | **Dado** una edición digital cuya licencia vence mientras el cliente paga, **cuando** confirma, **entonces** esa línea se bloquea y se le invita a quitarla. |
| AC-CAR-13 | CAR-16 | **Dado** un visitante sin sesión, **cuando** pulsa pagar, **entonces** se le pide iniciar sesión y su carrito se conserva. |
| AC-CAR-14 | CAR-17 | **Dado** un cliente que ya tiene una reserva pendiente, **cuando** inicia el pago de otro carrito, **entonces** ve el motivo del límite, el instante en que puede reintentar y su carrito intacto. |

## 5. Reglas e invariantes

- La disponibilidad se valida contra el inventario real al iniciar el checkout y al reservar; nunca solo con la caché.
- Un carrito solo digital no genera consulta ni reserva en el inventario.
- La reserva dura 15 minutos desde que se inicia el pago y cubre todas las líneas impresas del carrito.
- El total del carrito es la suma de las líneas más el envío cuando corresponde.

## 6. Casos límite y errores

- Si el cliente cambia la cantidad durante el checkout, la reserva se rehace.
- Un carrito con una línea no disponible se puede corregir sin perder las demás.
- Un cliente que cambia de sucursal preferida mantiene su carrito.

## 7. Preguntas abiertas

- [ ] ¿Un visitante puede armar un carrito antes de iniciar sesión? Propuesta: sí, y se fusiona con el de su cuenta al ingresar. Responde: responsable del proyecto.
- [ ] ¿Cuál es el costo del despacho a domicilio: fijo (S/ 12,00 en el prototipo) o por zona? Responde: responsable del proyecto.
