---
id: ecommerce-devoluciones
titulo: Devoluciones y reembolsos
estado: Draft
servicio: E-commerce
prefijo: DEV
origen: [RF09, NN05, NN15]
calidad: [AC01, AC09, RNF08]
depende: [ecommerce-pedidos, ecommerce-pagos, ecommerce-biblioteca-digital, inventario-ajustes, transversal-auditoria]
---

# Spec — Devoluciones y reembolsos

## 1. Propósito

Que el cliente pueda devolver un impreso entregado o pedir el reembolso de un libro digital, con trazabilidad, y que el stock y los accesos se actualicen de forma coherente (NN05). Los impresos y los digitales siguen políticas distintas.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Solicitud, aprobación, rechazo y reembolso. | El derecho de arrepentimiento general (no hay norma confirmada, ver `docs/01-analisis-de-sistema/03-marco-legal.md`). |
| Reingreso de stock de impresos y revocación de accesos digitales. | La mecánica del cobro y el reembolso (`ecommerce-pagos`). |
| Política propia de reembolso de libros digitales. | Logística inversa de devoluciones a domicilio. |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| DEV-01 | Evento | Cuando un cliente solicita una devolución, el sistema deberá registrar las líneas, las cantidades y el motivo en estado solicitada. | RF09 |
| DEV-02 | No deseado | Si se solicita la devolución de una línea impresa de un pedido que aún no está entregado, entonces el sistema deberá rechazar la solicitud. | RF09 |
| DEV-03 | No deseado | Si se solicitan más unidades de las compradas o ya devueltas, entonces el sistema deberá rechazar la solicitud. | RF09 |
| DEV-04 | Estado | Mientras el libro digital no fue descargado, el sistema deberá aceptar la solicitud de reembolso de esa línea. | NN15 |
| DEV-05 | No deseado | Si el libro digital ya fue descargado y el cliente no reporta un archivo defectuoso, entonces el sistema deberá rechazar el reembolso explicando la política. | NN15 |
| DEV-06 | Evento | Cuando el cliente reporta un archivo defectuoso, el sistema deberá registrar el reporte y tratar el reembolso como aceptable. | NN15 |
| DEV-07 | Evento | Cuando se aprueba la devolución de un impreso y la sucursal receptora confirma su recepción, el sistema deberá pedir al inventario que reingrese las unidades a esa sucursal. | RF09 |
| DEV-08 | Evento | Cuando la sucursal confirma la recepción de un impreso devuelto y aprobado, o se aprueba el reembolso de un libro digital, el sistema deberá solicitar el monto de la línea a la pasarela de pago. | RF09 |
| DEV-09 | Evento | Cuando se reembolsa una línea digital, el sistema deberá revocar el acceso y no reingresar stock. | RF09, NN15 |
| DEV-10 | Ubicuo | El sistema deberá gestionar los estados solicitada, aprobada o rechazada, recibida (solo impresos) y reembolsada, con fecha y responsable en cada cambio. | RF09 |
| DEV-11 | Evento | Cuando cambia el estado de una devolución, el sistema deberá notificar al cliente. | RF09 |
| DEV-12 | Evento | Cuando se aprueba, rechaza o reembolsa una devolución, el sistema deberá registrarlo en la auditoría. | RNF08 |
| DEV-13 | Ubicuo | El sistema deberá permitir aprobar o rechazar la devolución de un impreso solo al encargado de la sucursal receptora o a la administración central. | RF09, RNF05 |
| DEV-14 | No deseado | Si una devolución aprobada de un impreso lleva más de 10 días calendario sin que la sucursal confirme su recepción, entonces el sistema deberá pasarla a rechazada sin reembolso y avisar al cliente. | RF09 |
| DEV-15 | No deseado | Si se solicita la devolución de un impreso entregado hace más de 15 días calendario, entonces el sistema deberá rechazar la solicitud indicando el plazo. | RF09, NN05 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-DEV-01 | DEV-01, DEV-10 | **Dado** un pedido entregado, **cuando** el cliente solicita devolver un impreso con el motivo «llegó dañado», **entonces** se registra una devolución solicitada con fecha y la línea afectada. |
| AC-DEV-02 | DEV-02 | **Dado** un pedido en tránsito, **cuando** el cliente solicita devolver su impreso, **entonces** se rechaza porque aún no fue entregado. |
| AC-DEV-03 | DEV-03 | **Dado** una línea de 1 unidad ya devuelta, **cuando** se solicita devolverla otra vez, **entonces** se rechaza. |
| AC-DEV-04 | DEV-04 | **Dado** un libro digital sin descargas, **cuando** el cliente pide el reembolso, **entonces** se acepta la solicitud. |
| AC-DEV-05 | DEV-05 | **Dado** un libro digital descargado dos veces, **cuando** el cliente pide el reembolso sin reportar defecto, **entonces** se rechaza explicando la política. |
| AC-DEV-06 | DEV-06 | **Dado** un libro digital descargado, **cuando** el cliente reporta que el archivo no abre, **entonces** el reporte queda registrado y el reembolso es aceptable. |
| AC-DEV-07 | DEV-07 | **Dado** una devolución aprobada de un impreso, **cuando** Ayacucho confirma su recepción, **entonces** el inventario suma 1 unidad al físico de Ayacucho y la devolución pasa a recibida. |
| AC-DEV-08 | DEV-08 | **Dado** una devolución aprobada de un impreso, **cuando** aún no se confirmó la recepción, **entonces** no se solicita ningún reembolso; y **cuando** Ayacucho confirma la recepción y el reingreso, **entonces** se solicita a la pasarela el monto de la línea; y para un libro digital, el reembolso se solicita al aprobarse. |
| AC-DEV-09 | DEV-09 | **Dado** un reembolso aprobado de un libro digital, **cuando** se completa, **entonces** el cliente pierde el acceso y el inventario no cambia. |
| AC-DEV-10 | DEV-11 | **Dado** una devolución, **cuando** pasa a aprobada, **entonces** el cliente recibe un aviso. |
| AC-DEV-11 | DEV-12 | **Dado** un reembolso completado, **cuando** termina, **entonces** existe una entrada de auditoría con el usuario, el pedido y el monto. |
| AC-DEV-12 | DEV-13 | **Dado** una devolución solicitada de un impreso, **cuando** el encargado de otra sucursal intenta aprobarla, **entonces** se rechaza; y cuando lo hace el de la sucursal receptora o la administración, se aprueba. |
| AC-DEV-13 | DEV-14 | **Dado** una devolución aprobada hace 11 días sin recepción, **cuando** corre la revisión de plazos, **entonces** pasa a rechazada, no se reembolsa y el cliente recibe un aviso; y una de 9 días no cambia. |
| AC-DEV-14 | DEV-15 | **Dado** un impreso entregado hace 16 días, **cuando** el cliente solicita devolverlo, **entonces** se rechaza indicando el plazo de 15 días; y uno de 14 días se registra. |

## 5. Reglas e invariantes

- Una línea impresa solo se devuelve si está entregada; una línea digital solo se reembolsa si no fue descargada o si el archivo está defectuoso.
- El reingreso de stock de una devolución impresa lo hace el inventario de la sucursal que recibe, no el E-commerce.
- Un reembolso nunca supera lo cobrado por esa línea.
- Un impreso se reembolsa solo después de confirmada su recepción y su reingreso al stock; un libro digital, al aprobarse, porque no hay mercadería que recibir.
- Ciclo de una devolución de impreso: solicitada → aprobada → recibida → reembolsada; de un digital: solicitada → aprobada → reembolsada; en ambos, solicitada → rechazada.
- Los digitales nunca reingresan stock.

## 6. Casos límite y errores

- Una devolución aprobada cuyo reingreso o reembolso falla queda reintentable, sin repetir el paso que ya se completó.
- Una solicitud sobre varias líneas se evalúa línea por línea.
- Si el cliente reporta un archivo defectuoso, el sistema verifica que el reporte esté asociado a un acceso real del cliente.

## 7. Preguntas abiertas

- [x] ¿Quién aprueba la devolución de un impreso? Resuelto el 2026-10-16 (confirmado por el responsable): el encargado de la sucursal receptora o la administración (DEV-13).
- [x] ¿Cuándo se reembolsa un impreso devuelto? Resuelto el 2026-10-16: después de confirmar la recepción y el reingreso (DEV-08); el digital, al aprobarse.
- [x] ¿Cuántos días tiene el cliente para llevar el impreso a la sucursal tras aprobarse la devolución? Resuelto el 2026-10-16: 10 días calendario; vencida, pasa a rechazada sin reembolso (DEV-14).
- [x] ¿Cuál es el plazo máximo para devolver un impreso entregado? Resuelto el 2026-10-16: 15 días calendario, alineado con las propuestas de Indecopi; a verificar (DEV-15).
