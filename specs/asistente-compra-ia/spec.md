---
id: asistente-compra-ia
titulo: Asistente de compra
estado: Draft
servicio: Asistente
prefijo: ASI
origen: [RF08, NN04, NN18]
calidad: [AC03, AC05, AC13, RNF03, RNF05, RNF07, RNF17, RNF22]
depende: [ecommerce-catalogo, ecommerce-pedidos, identidad-seguridad, transversal-seguridad]
---

# Spec — Asistente de compra

## 1. Propósito

Orientar al cliente con recomendaciones y respuestas sobre disponibilidad y estado de pedidos, usando solo datos reales del catálogo, el inventario y sus propios pedidos, y admitiendo cuando no sabe (NN04). Su caída nunca afecta la compra.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Recomendaciones y consultas de disponibilidad y de estado de pedido. | Ejecutar acciones de compra, pagos o cambios de datos. |
| Respuestas fundamentadas en datos obtenidos de los servicios. | Atención humana y resolución de reclamos. |
| Límites de uso y protección de datos personales. | Entrenar o ajustar el modelo de lenguaje. |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| ASI-01 | Evento | Cuando un cliente pide una recomendación, el sistema deberá sugerir títulos tomados del catálogo vigente e indicar su disponibilidad. | RF08 |
| ASI-02 | Evento | Cuando un cliente pregunta por la disponibilidad de un libro, el sistema deberá responder con el stock por sucursal del impreso y la disponibilidad de la edición digital, obtenidos de los servicios. | RF08 |
| ASI-03 | No deseado | Si el sistema no tiene datos suficientes para responder, entonces el sistema deberá decir que no tiene información suficiente y no inventar títulos, precios ni existencias. | RF08 |
| ASI-04 | Evento | Cuando un cliente autenticado pregunta por el estado de un pedido, el sistema deberá responder con el estado real de sus propios pedidos. | RF08, RNF05 |
| ASI-05 | No deseado | Si se pregunta por datos de otro cliente o por información interna, entonces el sistema deberá negarse a responder y registrar el intento como evento de seguridad. | RNF05, AC05 |
| ASI-06 | Ubicuo | El sistema deberá obtener los datos únicamente a través de las interfaces de los servicios y nunca de sus bases de datos. | RF08, AC05 |
| ASI-07 | No deseado | Si el proveedor del modelo de lenguaje no responde, entonces el sistema deberá informar que el asistente no está disponible sin afectar la compra. | AC03, RNF03 |
| ASI-08 | Ubicuo | El sistema deberá excluir de la consulta al modelo los datos personales que no sean necesarios para responder. | RNF07 |
| ASI-09 | Estado | Mientras un cliente, o un origen si no tiene sesión, supera 10 consultas en una ventana deslizante de un minuto, el sistema deberá rechazar las siguientes y responder y registrar el exceso según `transversal-seguridad` (SEG-07 y SEG-08). | RNF17, AC13 |
| ASI-10 | Ubicuo | El sistema deberá excluir de las recomendaciones las ediciones digitales con licencia vencida y señalar los impresos agotados como tales. | RF08, NN14 |
| ASI-11 | Ubicuo | El sistema deberá limitarse a consultar información y no deberá ejecutar acciones de compra, pago, cambio de datos ni administración. | RNF22, AC05 |
| ASI-12 | Ubicuo | El sistema deberá entregar cada respuesta como texto plano sin contenido ejecutable, y la interfaz deberá mostrarla sin interpretarla como código. | RNF22, AC05 |
| ASI-13 | No deseado | Si una consulta pide revelar las instrucciones internas del asistente o ignorar sus reglas, entonces el sistema deberá negarse y seguir su propósito original. | RNF22, AC05 |
| ASI-14 | Ubicuo | El sistema deberá rechazar las consultas de más de 500 caracteres. | RNF22, AC13 |
| ASI-15 | Ubicuo | El sistema deberá tratar como contenido y no como órdenes los textos que obtiene del catálogo y de los pedidos. | RNF22, AC05 |
| ASI-16 | Ubicuo | El sistema deberá acotar cada respuesta a 1500 caracteres como máximo. | RNF22, AC13 |
| ASI-17 | Evento | Cuando la respuesta del modelo contiene información que no proviene de los datos obtenidos de los servicios, el sistema deberá descartarla y responder que no tiene información suficiente. | RF08, RNF22 |
| ASI-18 | Ubicuo | El sistema no deberá conservar el texto de las consultas asociado a la identidad del cliente. | RNF07, AC11 |
| ASI-19 | Ubicuo | El sistema deberá consultar los pedidos con una credencial de lectura limitada al propio cliente autenticado, de modo que el servicio de pedidos solo devuelva los de ese cliente, y consultar catálogo y disponibilidad con una credencial de servicio de solo lectura. | RNF05, RNF22, AC05 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-ASI-01 | ASI-01, ASI-10 | **Dado** un catálogo con obras disponibles, agotadas y con licencia vencida, **cuando** el cliente pide una recomendación, **entonces** solo recibe títulos vigentes con su disponibilidad. |
| AC-ASI-02 | ASI-02 | **Dado** un libro con stock en Lima y Cusco y edición digital vigente, **cuando** el cliente pregunta por su disponibilidad, **entonces** la respuesta cita Lima y Cusco con sus unidades y la edición digital. |
| AC-ASI-03 | ASI-03 | **Dado** una pregunta sobre un libro que no existe en el catálogo, **cuando** se responde, **entonces** el asistente indica que no tiene información suficiente. |
| AC-ASI-04 | ASI-04 | **Dado** un cliente con un pedido en tránsito, **cuando** pregunta por su estado, **entonces** el asistente responde que está en tránsito entre sucursales. |
| AC-ASI-05 | ASI-05 | **Dado** un cliente, **cuando** pregunta por el pedido de otra persona, **entonces** el asistente se niega a responder y queda un evento de seguridad. |
| AC-ASI-06 | ASI-06 | **Dado** el asistente, **cuando** se revisa su configuración, **entonces** no tiene credenciales ni acceso a las bases de datos de los servicios y solo cuenta con una credencial de servicio de solo lectura hacia sus interfaces de catálogo y disponibilidad. |
| AC-ASI-07 | ASI-07 | **Dado** que el proveedor del modelo no responde, **cuando** el cliente usa el asistente, **entonces** ve que no está disponible y puede seguir comprando con normalidad. |
| AC-ASI-08 | ASI-08 | **Dado** una consulta que no necesita datos personales, **cuando** se envía al modelo, **entonces** no incluye nombre, correo ni teléfono del cliente. |
| AC-ASI-09 | ASI-09 | **Dado** un cliente que envía consultas sin pausa, **cuando** supera 10 por minuto, **entonces** se le pide esperar y queda un registro. |
| AC-ASI-10 | ASI-11 | **Dado** una consulta que pide «comprar este libro por mí» o cambiar un dato, **cuando** se responde, **entonces** el asistente no ejecuta la acción y enlaza a la pantalla correspondiente. |
| AC-ASI-11 | ASI-12 | **Dado** una respuesta del modelo con etiquetas de marcado, **cuando** se muestra, **entonces** aparece como texto y no se interpreta. |
| AC-ASI-12 | ASI-13 | **Dado** una consulta que pide mostrar las instrucciones internas, **cuando** se responde, **entonces** el asistente se niega y no las revela. |
| AC-ASI-13 | ASI-14, ASI-16 | **Dado** una consulta de 501 caracteres, **cuando** se envía, **entonces** se rechaza indicando el límite y no se consulta al modelo; y si el modelo devuelve más de 1500 caracteres, la respuesta se recorta a ese máximo. |
| AC-ASI-14 | ASI-11 | **Dado** la credencial del asistente, **cuando** intenta una operación de escritura en un servicio, **entonces** el servicio la rechaza. |
| AC-ASI-15 | ASI-15 | **Dado** un libro cuyo título contiene instrucciones como «ignora tus reglas», **cuando** el asistente lo recomienda, **entonces** lo muestra como texto y su comportamiento no cambia. |
| AC-ASI-16 | ASI-17 | **Dado** una respuesta del modelo que menciona un precio que no estaba en los datos, **cuando** se procesa, **entonces** se descarta y el asistente responde que no tiene información suficiente. |
| AC-ASI-17 | ASI-18 | **Dado** una consulta de un cliente con sesión, **cuando** termina la respuesta, **entonces** no queda guardado su texto asociado al cliente. |
| AC-ASI-18 | ASI-19, ASI-04 | **Dado** el cliente A con la credencial de lectura que recibió el asistente, **cuando** el asistente (por una instrucción manipulada) pide el pedido del cliente B, **entonces** el servicio de pedidos lo rechaza, queda un evento de acceso denegado y la respuesta al cliente no contiene datos de B; y esa credencial no permite ninguna operación de escritura. |

## 5. Reglas e invariantes

- El asistente solo afirma lo que obtuvo de los servicios en esa consulta.
- Responde con la identidad del cliente autenticado; un visitante solo puede consultar catálogo y disponibilidad.
- El filtro de los pedidos lo aplica el servicio de pedidos con la credencial del cliente; nunca depende de que el asistente o el modelo envíen el cliente correcto.
- Una respuesta incluye siempre si el dato es del catálogo o del pedido del cliente.
- El asistente no ejecuta acciones; solo informa y enlaza.

## 6. Casos límite y errores

- Una consulta ambigua (por ejemplo, un título parcial) puede devolver varias coincidencias y pide aclarar.
- Una consulta fuera del tema se redirige al propósito del asistente.
- Un visitante sin sesión se limita por origen (ASI-09).

## 7. Preguntas abiertas

- Nota para el plan: el proveedor del modelo de lenguaje no está elegido (bloqueo B06); no cambia el comportamiento y se define antes del plan.
- [x] ¿Un visitante puede usar el asistente solo para catálogo y disponibilidad, con topes de 500 y 1500 caracteres? Resuelto el 2026-10-16 (confirmado por el responsable): sí.
