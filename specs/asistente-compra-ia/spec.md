---
id: asistente-compra-ia
titulo: Asistente de compra
estado: Draft
servicio: Asistente
prefijo: ASI
origen: [RF08, NN04]
calidad: [AC03, AC05, RNF03, RNF05, RNF07]
depende: [ecommerce-catalogo, ecommerce-pedidos, identidad-seguridad]
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
| ASI-05 | No deseado | Si se pregunta por datos de otro cliente o por información interna, entonces el sistema deberá negarse a responder. | RNF05, AC05 |
| ASI-06 | Ubicuo | El sistema deberá obtener los datos únicamente a través de las interfaces de los servicios y nunca de sus bases de datos. | RF08, AC05 |
| ASI-07 | No deseado | Si el proveedor del modelo de lenguaje no responde, entonces el sistema deberá informar que el asistente no está disponible sin afectar la compra. | AC03, RNF03 |
| ASI-08 | Ubicuo | El sistema deberá excluir de la consulta al modelo los datos personales que no sean necesarios para responder. | RNF07 |
| ASI-09 | Estado | Mientras un cliente supera el límite de consultas por minuto, el sistema deberá pedirle que espere antes de seguir consultando. | AC05 |
| ASI-10 | Ubicuo | El sistema deberá excluir de las recomendaciones las ediciones digitales con licencia vencida y señalar los impresos agotados como tales. | RF08, NN14 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-ASI-01 | ASI-01, ASI-10 | **Dado** un catálogo con obras disponibles, agotadas y con licencia vencida, **cuando** el cliente pide una recomendación, **entonces** solo recibe títulos vigentes con su disponibilidad. |
| AC-ASI-02 | ASI-02 | **Dado** un libro con stock en Lima y Cusco y edición digital vigente, **cuando** el cliente pregunta por su disponibilidad, **entonces** la respuesta cita Lima y Cusco con sus unidades y la edición digital. |
| AC-ASI-03 | ASI-03 | **Dado** una pregunta sobre un libro que no existe en el catálogo, **cuando** se responde, **entonces** el asistente indica que no tiene información suficiente. |
| AC-ASI-04 | ASI-04 | **Dado** un cliente con un pedido en tránsito, **cuando** pregunta por su estado, **entonces** el asistente responde que está en tránsito entre sucursales. |
| AC-ASI-05 | ASI-05 | **Dado** un cliente, **cuando** pregunta por el pedido de otra persona, **entonces** el asistente se niega a responder. |
| AC-ASI-06 | ASI-06 | **Dado** el asistente, **cuando** se revisa su configuración, **entonces** no tiene credenciales ni acceso a las bases de datos de los servicios. |
| AC-ASI-07 | ASI-07 | **Dado** que el proveedor del modelo no responde, **cuando** el cliente usa el asistente, **entonces** ve que no está disponible y puede seguir comprando con normalidad. |
| AC-ASI-08 | ASI-08 | **Dado** una consulta que no necesita datos personales, **cuando** se envía al modelo, **entonces** no incluye nombre, correo ni teléfono del cliente. |
| AC-ASI-09 | ASI-09 | **Dado** un cliente que envía consultas sin pausa, **cuando** supera el límite por minuto, **entonces** se le pide esperar. |

## 5. Reglas e invariantes

- El asistente solo afirma lo que obtuvo de los servicios en esa consulta.
- Responde con la identidad del cliente autenticado; un visitante solo puede consultar catálogo y disponibilidad.
- Una respuesta incluye siempre si el dato es del catálogo o del pedido del cliente.
- El asistente no ejecuta acciones; solo informa y enlaza.

## 6. Casos límite y errores

- Una consulta ambigua (por ejemplo, un título parcial) puede devolver varias coincidencias y pide aclarar.
- Una consulta fuera del tema se redirige al propósito del asistente.
- Un modelo que devuelve información que no está en los datos se descarta y se responde que no hay información suficiente.

## 7. Preguntas abiertas

- [ ] ¿Qué proveedor de modelo de lenguaje se usa? Es el bloqueo B06. Responde: responsable del proyecto.
- [ ] ¿Un visitante puede usar el asistente? Propuesta: sí, solo para catálogo y disponibilidad. Responde: responsable del proyecto.
