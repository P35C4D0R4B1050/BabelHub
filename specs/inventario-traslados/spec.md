---
id: inventario-traslados
titulo: Traslados entre sucursales
estado: Draft
servicio: Inventario
prefijo: TRA
origen: [RF16, RF15, RF07, RF20, NN07]
calidad: [AC01, RNF01, RNF05, RNF08]
depende: [inventario-fuentes, inventario-seleccion-fuente, transversal-auditoria, transversal-notificaciones]
---

# Spec — Traslados entre sucursales

## 1. Propósito

Mover ejemplares de una sucursal a otra con trazabilidad completa, para abastecer pedidos y reponer stock (NN07). Cada paso lo hace quien corresponde y deja constancia.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Solicitar, aprobar, rechazar, despachar y recibir traslados. | Cómo nace un traslado desde un pedido (`inventario-seleccion-fuente`). |
| Efecto en el stock físico de origen y destino. | Guías de envío a domicilio (`ecommerce-envios`). |
| Registro de la guía del traslado por logística. | El transporte físico y su seguimiento externo. |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| TRA-01 | Evento | Cuando un encargado o la administración solicita un traslado, el sistema deberá registrar la edición, la cantidad, la sucursal de origen, la de destino y el motivo en estado solicitado. | RF16 |
| TRA-02 | Ubicuo | El sistema deberá ofrecer como origen de una solicitud únicamente las fuentes con stock vendible de esa edición. | RF15 |
| TRA-03 | Ubicuo | El sistema deberá permitir solo las transiciones solicitado a aprobado o rechazado, aprobado a en tránsito y en tránsito a recibido, y rechazar cualquier otra. | RF16 |
| TRA-04 | Evento | Cuando la sucursal de origen aprueba una solicitud, el sistema deberá pasarla a aprobado. | RF16 |
| TRA-05 | Evento | Cuando la sucursal de origen rechaza una solicitud, el sistema deberá pasarla a rechazado y registrar el motivo. | RF16 |
| TRA-06 | No deseado | Si al despachar un traslado el origen no tiene stock vendible suficiente, entonces el sistema deberá rechazar el despacho y mantener el traslado aprobado. | RF16, RNF01 |
| TRA-07 | Evento | Cuando la sucursal de origen despacha un traslado, el sistema deberá pasarlo a en tránsito y descontar la cantidad del stock físico del origen. | RF16 |
| TRA-08 | Evento | Cuando la sucursal de destino recibe un traslado, el sistema deberá pasarlo a recibido y sumar la cantidad al stock físico del destino. | RF16 |
| TRA-09 | Ubicuo | El sistema deberá permitir la aprobación, el rechazo y el despacho solo a la sucursal de origen, y la recepción solo a la de destino, salvo a la administración central, que puede ejecutar todas. | RNF05 |
| TRA-10 | Evento | Cuando cambia el estado de un traslado, el sistema deberá notificar a las sucursales de origen y de destino. | RF07, RF16 |
| TRA-11 | Evento | Cuando un traslado vinculado a un pedido de recojo pasa a recibido, el sistema deberá avisar para que el pedido pueda prepararse. | RF07 |
| TRA-12 | Evento | Cuando logística registra la guía de un traslado aprobado o en tránsito, el sistema deberá asociarla al traslado sin cambiar su estado. | RF16 |
| TRA-13 | Evento | Cuando cambia el estado de un traslado, el sistema deberá registrarlo en la auditoría. | RF20, RNF08 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-TRA-01 | TRA-01, TRA-02 | **Dado** una edición con vendible solo en Lima y Cusco, **cuando** el encargado de Ayacucho solicita un traslado, **entonces** solo puede elegir Lima o Cusco como origen y el traslado nace solicitado. |
| AC-TRA-02 | TRA-03 | **Dado** un traslado solicitado, **cuando** alguien intenta despacharlo sin aprobarlo, **entonces** se rechaza y el estado no cambia. |
| AC-TRA-03 | TRA-04, TRA-09 | **Dado** un traslado solicitado a Ayacucho como origen, **cuando** su encargado lo aprueba, **entonces** pasa a aprobado; y si lo intenta el encargado de otra sucursal, se rechaza. |
| AC-TRA-04 | TRA-05 | **Dado** un traslado solicitado, **cuando** el origen lo rechaza con el motivo «sin stock», **entonces** pasa a rechazado con ese motivo. |
| AC-TRA-05 | TRA-06 | **Dado** un traslado aprobado de 3 unidades y un origen con vendible 0, **cuando** se intenta despachar, **entonces** se rechaza indicando que el origen no tiene stock vendible suficiente. |
| AC-TRA-06 | TRA-07 | **Dado** un traslado aprobado de 4 unidades y un origen con vendible 12, **cuando** se despacha, **entonces** pasa a en tránsito y el físico del origen baja en 4. |
| AC-TRA-07 | TRA-08 | **Dado** un traslado en tránsito de 4 unidades, **cuando** el destino lo recibe, **entonces** pasa a recibido y el físico del destino sube en 4. |
| AC-TRA-08 | TRA-10 | **Dado** un traslado, **cuando** cambia de estado, **entonces** se envía un aviso a la sucursal de origen y a la de destino. |
| AC-TRA-09 | TRA-11 | **Dado** un traslado vinculado a un pedido de recojo, **cuando** se recibe, **entonces** el pedido queda habilitado para prepararse y la sucursal recibe el aviso. |
| AC-TRA-10 | TRA-12 | **Dado** un traslado en tránsito, **cuando** logística registra la guía CA-771204, **entonces** el traslado muestra la guía y su estado no cambia. |
| AC-TRA-11 | TRA-13 | **Dado** cualquier cambio de estado de un traslado, **cuando** se aplica, **entonces** existe una entrada de auditoría con el usuario, la ruta y el nuevo estado. |

## 5. Reglas e invariantes

- Ciclo de vida: solicitado → aprobado → en tránsito → recibido; solicitado → rechazado. No hay otras transiciones.
- El stock físico sale del origen al despachar y entra al destino al recibir; mientras viaja, no pertenece a ninguna fuente.
- Solo se puede despachar si el origen tiene stock vendible suficiente en ese momento.
- La notificación nunca bloquea una transición (`transversal-notificaciones`).

## 6. Casos límite y errores

- Dos despachos simultáneos que juntos superan el vendible del origen: el segundo se rechaza.
- Un traslado en tránsito que nunca llega queda visible para logística y administración; la resolución manual está fuera de esta versión.
- Una recepción repetida del mismo traslado no suma dos veces.

## 7. Preguntas abiertas

- [ ] ¿Se permite cancelar un traslado ya aprobado antes de despacharse? Propuesta: sí, por el origen o la administración, con motivo. Responde: responsable del proyecto.
- [ ] ¿Qué se hace con un traslado en tránsito que se pierde? Propuesta: fuera de esta versión; se atiende con un ajuste manual de merma en el origen. Responde: responsable del proyecto.
