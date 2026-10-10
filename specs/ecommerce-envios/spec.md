---
id: ecommerce-envios
titulo: Guías de envío a domicilio
estado: Draft
servicio: E-commerce
prefijo: ENV
origen: [RF11, NN11, NN03]
calidad: [AC03, RNF05]
depende: [ecommerce-pedidos, transversal-notificaciones]
---

# Spec — Guías de envío a domicilio

## 1. Propósito

Completar el ciclo de entrega fuera de la tienda: cuando un pedido es a domicilio, logística genera la guía de envío con un operador externo y da seguimiento hasta la entrega (NN11).

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Creación del envío, generación de la guía, seguimiento y entrega. | El transporte físico y los sistemas internos del operador. |
| Avisos al cliente en cada hito. | Tarifas y costo de despacho (`ecommerce-carrito-checkout`). |
| Solo líneas impresas. | Libros digitales: nunca tienen envío. |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| ENV-01 | Evento | Cuando un pedido con despacho a domicilio tiene sus impresos listos, el sistema deberá crear un envío pendiente de guía. | RF11 |
| ENV-02 | Evento | Cuando logística solicita la guía de un envío, el sistema deberá pedirla al operador logístico y registrar su número y el operador. | RF11 |
| ENV-03 | Ubicuo | El sistema deberá gestionar los estados pendiente de guía, guía generada, en reparto y entregado, y permitir solo las transiciones en ese orden. | RF11 |
| ENV-04 | No deseado | Si el operador no responde o rechaza la solicitud, entonces el sistema deberá mantener el envío pendiente, mostrar el error y permitir reintentar. | RF11, AC03 |
| ENV-05 | Evento | Cuando un cliente consulta su pedido a domicilio, el sistema deberá mostrar el estado del envío y su número de guía. | RF11, NN03 |
| ENV-06 | Evento | Cuando el envío pasa a guía generada, en reparto o entregado, el sistema deberá notificar al cliente. | RF11, NN03 |
| ENV-07 | Evento | Cuando el envío pasa a entregado, el sistema deberá marcar el pedido como entregado. | RF11 |
| ENV-08 | Ubicuo | El sistema deberá permitir gestionar envíos solo a logística y a la administración central. | RNF05 |
| ENV-09 | Ubicuo | El sistema deberá excluir de los envíos las líneas digitales. | RF11 |
| ENV-10 | No deseado | Si la dirección de entrega está incompleta, entonces el sistema deberá rechazar la generación de la guía indicando qué falta. | RF11 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-ENV-01 | ENV-01, ENV-09 | **Dado** un pedido mixto a domicilio con sus impresos listos, **cuando** se prepara el despacho, **entonces** se crea un envío pendiente que incluye solo las líneas impresas. |
| AC-ENV-02 | ENV-02 | **Dado** un envío pendiente, **cuando** logística solicita la guía, **entonces** el operador devuelve un número que se registra junto con su nombre. |
| AC-ENV-03 | ENV-03 | **Dado** un envío pendiente de guía, **cuando** alguien intenta marcarlo en reparto, **entonces** se rechaza. |
| AC-ENV-04 | ENV-04 | **Dado** que el operador no responde, **cuando** logística solicita la guía, **entonces** el envío sigue pendiente, se muestra el error y puede reintentar. |
| AC-ENV-05 | ENV-05 | **Dado** un pedido a domicilio con guía CA-882013, **cuando** el cliente lo consulta, **entonces** ve el estado del envío y la guía. |
| AC-ENV-06 | ENV-06 | **Dado** un envío, **cuando** pasa a en reparto, **entonces** el cliente recibe un aviso. |
| AC-ENV-07 | ENV-07 | **Dado** un envío en reparto, **cuando** se marca entregado, **entonces** el pedido queda entregado. |
| AC-ENV-08 | ENV-08 | **Dado** un cliente o un encargado de sucursal, **cuando** intenta gestionar un envío, **entonces** se rechaza por falta de permiso. |
| AC-ENV-09 | ENV-10 | **Dado** un envío con la dirección vacía, **cuando** logística solicita la guía, **entonces** se rechaza indicando que falta la dirección. |

## 5. Reglas e invariantes

- Estados en orden: pendiente de guía → guía generada → en reparto → entregado.
- El envío pertenece a un pedido y solo contiene líneas impresas.
- El número de guía lo asigna el operador y no se modifica.
- Un envío entregado no vuelve a otro estado.

## 6. Casos límite y errores

- Si el pedido se anula antes de generar la guía, el envío se cancela.
- Un envío que el operador devuelve (no entregado) queda a revisión manual; la resolución completa está fuera de esta versión.
- Dos solicitudes de guía para el mismo envío no generan dos guías.

## 7. Preguntas abiertas

- [ ] ¿Qué operador logístico se usará? En el prototipo es un operador ejemplo. Responde: responsable del proyecto; no bloquea la spec, sí el plan.
- [ ] ¿La «entrega» la confirma el operador mediante una notificación externa o la marca logística manualmente? Propuesta: manual en esta versión. Responde: responsable del proyecto.
