---
id: inventario-fuentes
titulo: Fuentes y stock vendible
estado: Approved
servicio: Inventario
prefijo: FUE
origen: [RF12, RF13, RF15, RF02, NN06, NN07, NN01]
calidad: [AC01, AC02, AC04, RNF01, RNF02, RNF04, RNF05]
depende: [identidad-seguridad, transversal-auditoria]
aprobado_por: Diego Amiquero
aprobado_en: 2026-10-16
---

# Spec — Fuentes y stock vendible

## 1. Propósito

Cada sucursal es una **fuente** independiente de stock de libros impresos. El sistema debe conocer, por fuente y por edición, cuánto hay físicamente, cuánto está reservado y cuánto se puede vender, para mostrar solo stock real (OB01).

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Registro de fuentes, stock físico, reservado y vendible por edición impresa. | Crear reservas y elegir la fuente de un pedido (`inventario-seleccion-fuente`). |
| Consulta de disponibilidad consolidada y por fuente. | Ajustes del stock (`inventario-ajustes`) y traslados (`inventario-traslados`). |
| Consulta de stock de la sucursal para su encargado. | Libros digitales: no tienen stock ni fuente. |
| Rutas entre fuentes con su plazo estimado de traslado, como dato. | Cómo se usan las rutas para elegir la fuente de un pedido (`inventario-seleccion-fuente`). |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| FUE-01 | Ubicuo | El sistema deberá registrar cada sucursal como una fuente independiente, con código único, nombre, región y contacto de notificación. | RF12 |
| FUE-02 | Ubicuo | El sistema deberá mantener, por cada fuente y cada edición impresa, el stock físico y las unidades reservadas. | RF12 |
| FUE-03 | Ubicuo | El sistema deberá calcular el stock vendible de una edición en una fuente como el stock físico menos las reservas activas, es decir, las pendientes no vencidas y las confirmadas. | RF13 |
| FUE-04 | Ubicuo | El sistema deberá garantizar que el stock vendible de una edición en una fuente nunca sea negativo. | RNF01, AC01 |
| FUE-05 | Evento | Cuando se consulta la disponibilidad de una edición impresa, el sistema deberá responder el stock vendible consolidado de la red y el desglose por fuente. | RF13, RF02 |
| FUE-06 | Evento | Cuando la administración central registra una nueva fuente, el sistema deberá iniciarla con stock cero y dejarla disponible sin cambios de código ni de estructura. | RNF04, AC04 |
| FUE-07 | No deseado | Si se consulta la disponibilidad de una edición que no es impresa, entonces el sistema deberá rechazar la consulta indicando que no tiene stock físico. | RF13 |
| FUE-08 | Estado | Mientras un usuario es encargado de sucursal, el sistema deberá limitar el detalle de stock físico y reservado a su propia fuente. | RNF05 |
| FUE-09 | Evento | Cuando el encargado consulta el inventario de su sucursal, el sistema deberá permitir buscar por título, autor o ISBN y mostrar físico, reservado, vendible y un estado de stock. | RF12 |
| FUE-10 | Ubicuo | El sistema deberá responder las consultas de disponibilidad con un percentil 95 menor a 500 ms con hasta 50 usuarios concurrentes. | RNF02, AC02 |
| FUE-11 | Evento | Cuando el administrador central consulta una fuente, el sistema deberá mostrar sus totales de stock físico, reservado y vendible y su encargado asignado. | RF12 |
| FUE-12 | Evento | Cuando la administración central registra o modifica el plazo estimado, en días, del traslado desde una fuente hacia otra, el sistema deberá guardarlo como la ruta entre ambas fuentes. | RF15, RNF04 |
| FUE-13 | Evento | Cuando se consulta el plazo entre dos fuentes, el sistema deberá responder los días de su ruta o indicar que la ruta no está definida. | RF15, NN07 |
| FUE-14 | Evento | Cuando la administración central registra o modifica una ruta, el sistema deberá registrarlo en la auditoría. | RF15, RNF08 |
| FUE-15 | No deseado | Si se consulta una edición o una fuente inexistente, entonces el sistema deberá rechazar la consulta como no encontrada. | RF13 |
| FUE-16 | Evento | Cuando la administración central registra o edita una fuente, el sistema deberá registrarlo en la auditoría. | RF12, RNF08 |
| FUE-17 | No deseado | Si se registra una ruta con un plazo que no es mayor que cero, o entre una fuente y ella misma, o si quien la registra no es la administración central, entonces el sistema deberá rechazarla. | RF15, RNF05 |
| FUE-18 | Ubicuo | El sistema deberá mostrar el estado de stock de una edición en una fuente según su vendible: sin stock si es 0, bajo si es de 1 a 2 y normal si es de 3 o más. | RF12 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-FUE-01 | FUE-01, FUE-02 | **Dado** una fuente «Ayacucho» con físico 9 de una edición impresa y una reserva confirmada de 1, **cuando** se consulta, **entonces** muestra físico 9, reservado 1 y vendible 8; y registrar otra fuente con el mismo código se rechaza. |
| AC-FUE-02 | FUE-03, FUE-04 | **Dado** una edición con físico 5 y reservado 5, y otra con una reserva pendiente vencida de 2 que aún no se procesó, **cuando** se consulta su vendible, **entonces** la primera es 0 y la segunda no descuenta las 2 vencidas; y dos clientes que intentan reservar 1 de la primera no logran que el vendible sea negativo. |
| AC-FUE-03 | FUE-05 | **Dado** una edición con vendible 8 en Lima y 3 en Cusco, **cuando** se consulta su disponibilidad, **entonces** la respuesta indica 11 en la red y el desglose Lima 8 y Cusco 3. |
| AC-FUE-04 | FUE-06 | **Dado** cinco fuentes activas, **cuando** la administración registra una sexta, **entonces** aparece en las consultas con stock cero y no se requiere ningún cambio de código ni de estructura. |
| AC-FUE-05 | FUE-07 | **Dado** una edición digital, **cuando** se consulta su stock, **entonces** la consulta se rechaza indicando que no tiene stock físico. |
| AC-FUE-06 | FUE-08 | **Dado** un encargado de Ayacucho, **cuando** pide el detalle de Cusco, **entonces** la solicitud se rechaza por falta de alcance y se audita. |
| AC-FUE-07 | FUE-09 | **Dado** el inventario de Ayacucho, **cuando** el encargado busca «Catedral», **entonces** ve solo los títulos coincidentes con físico, reservado, vendible y estado de stock. |
| AC-FUE-08 | FUE-10 | **Dado** 50 usuarios concurrentes consultando disponibilidad, **cuando** se mide la prueba de carga, **entonces** el percentil 95 es menor a 500 ms. |
| AC-FUE-09 | FUE-11 | **Dado** un administrador central, **cuando** abre la fuente Cusco, **entonces** ve sus totales y a su encargado, y no puede modificar el stock desde esa vista. |
| AC-FUE-10 | FUE-12, FUE-14 | **Dado** un administrador central, **cuando** registra que de Cusco a Ayacucho el traslado tarda 2 días, **entonces** la ruta queda guardada con ese plazo y existe una entrada de auditoría; y si la cambia a 3 días, la ruta muestra 3 y la auditoría registra ambos valores. |
| AC-FUE-11 | FUE-13 | **Dado** una ruta de Cusco a Ayacucho de 2 días y ninguna de Lima a Ayacucho, **cuando** se consulta el plazo entre cada par, **entonces** la primera responde 2 días y la segunda indica que la ruta no está definida. |
| AC-FUE-12 | FUE-15 | **Dado** una edición que no existe y una fuente inexistente, **cuando** se consulta su disponibilidad, **entonces** ambas consultas se rechazan como no encontradas. |
| AC-FUE-13 | FUE-16 | **Dado** la administración central, **cuando** registra la fuente «Piura» y luego edita su contacto, **entonces** cada acción queda en la auditoría. |
| AC-FUE-14 | FUE-17 | **Dado** una ruta con plazo 0, otra de Lima a Lima y otra registrada por un encargado, **cuando** se envían, **entonces** las tres se rechazan. |
| AC-FUE-15 | FUE-18 | **Dado** vendibles de 0, 2 y 3, **cuando** el encargado consulta su inventario, **entonces** ve sin stock, bajo y normal respectivamente. |

## 5. Reglas e invariantes

- Stock vendible = stock físico − reservas activas, siempre mayor o igual a cero (RNF01).
- Una edición digital nunca aparece en el inventario.
- Una fuente nueva se crea con stock cero y se asigna a un encargado por separado.
- El estado de stock se deriva del vendible (FUE-18).
- Una ruta es direccional (Cusco→Ayacucho no implica Ayacucho→Cusco), tiene un plazo en días mayor que cero y es un dato que la administración mantiene; agregar una sucursal no exige código.

## 6. Casos límite y errores

- Dos consultas simultáneas durante una reserva ven valores coherentes: el vendible nunca refleja un estado intermedio inválido.
- Una consulta con una fuente que no existe se rechaza como no encontrada.
- Si la caché de disponibilidad está desactualizada, la validación definitiva siempre es contra el almacén real al reservar.
- Una ruta sin definir no impide vender: el traslado se informa sin plazo estimado («a confirmar») y la selección de fuente la trata según `inventario-seleccion-fuente` (SEL-25).
- Una reserva pendiente vencida que el trabajo de fondo aún no procesó no cuenta como reserva activa.

## 7. Preguntas abiertas

- [x] ¿Se puede desactivar una fuente (cierre de una sucursal)? Resuelto el 2026-10-16 (confirmado por el responsable): fuera de alcance de esta versión.
- Nota para el plan: cómo identifica Inventario una edición (identificador de catálogo, ISBN o ambos); esta spec no depende de la respuesta.
