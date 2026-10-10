---
id: inventario-ajustes
titulo: Ajustes de inventario
estado: Draft
servicio: Inventario
prefijo: AJU
origen: [RF17, RF09, NN06]
calidad: [RNF01, RNF05, RNF08]
depende: [inventario-fuentes, transversal-auditoria]
---

# Spec — Ajustes de inventario

## 1. Propósito

Permitir que cada sucursal corrija su stock físico con un motivo y un responsable, sin poder dejar el sistema en un estado imposible. La merma y el daño solo restan; la corrección de conteo se expresa como conteo real; el ingreso de mercadería llega por traslado o por devolución.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Ajustes por merma, producto dañado y corrección de conteo. | Traslados entre sucursales (`inventario-traslados`). |
| Reingreso de stock por devolución de un impreso. | La decisión de aprobar una devolución (`ecommerce-devoluciones`). |
| Historial de ajustes por sucursal. | Compras a proveedores o ingreso inicial de mercadería. |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| AJU-01 | Ubicuo | El sistema deberá registrar cada ajuste con su motivo, la edición, la sucursal, el usuario responsable, las cantidades anterior y posterior y una nota opcional. | RF17 |
| AJU-02 | Ubicuo | El sistema deberá aceptar solamente los motivos merma, producto dañado y corrección de conteo para los ajustes manuales. | RF17 |
| AJU-03 | Evento | Cuando el motivo es merma o producto dañado, el sistema deberá descontar del stock físico únicamente una cantidad positiva indicada por el usuario. | RF17 |
| AJU-04 | No deseado | Si la cantidad a descontar por merma o daño supera el stock vendible de la edición, entonces el sistema deberá rechazar el ajuste. | RF17, RNF01 |
| AJU-05 | Evento | Cuando el motivo es corrección de conteo, el sistema deberá recibir el conteo físico real y calcular la diferencia frente al físico registrado. | RF17 |
| AJU-06 | No deseado | Si el conteo real es menor que las unidades reservadas, entonces el sistema deberá rechazar la corrección. | RF17, RNF01 |
| AJU-07 | No deseado | Si el conteo real es igual al físico registrado, entonces el sistema deberá rechazar la corrección indicando que no hay nada que ajustar. | RF17 |
| AJU-08 | Evento | Cuando se aplica un ajuste válido, el sistema deberá actualizar el stock físico y recalcular el vendible de forma atómica. | RF17, RNF01 |
| AJU-09 | Estado | Mientras un usuario es encargado de sucursal, el sistema deberá permitirle ajustar únicamente el stock de su propia sucursal. | RNF05 |
| AJU-10 | Evento | Cuando se aplica un ajuste, el sistema deberá registrarlo en la auditoría de forma inmutable. | RNF08 |
| AJU-11 | Evento | Cuando se acepta una devolución de un libro impreso en una sucursal, el sistema deberá sumar las unidades devueltas al stock físico de esa sucursal con el motivo devolución. | RF09 |
| AJU-12 | Ubicuo | El sistema deberá rechazar cualquier ajuste manual que aumente el stock por merma o por daño. | RF17 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-AJU-01 | AJU-01, AJU-10 | **Dado** un encargado de Ayacucho, **cuando** registra una merma de 2 unidades con la nota «caja mojada», **entonces** queda un ajuste con motivo, edición, sucursal, usuario, físico 9 → 7 y la nota, y una entrada de auditoría. |
| AC-AJU-02 | AJU-02 | **Dado** un ajuste con el motivo «regalo», **cuando** se envía, **entonces** se rechaza por motivo inválido. |
| AC-AJU-03 | AJU-03, AJU-12 | **Dado** una merma, **cuando** se envía una cantidad negativa o cero, **entonces** se rechaza y el stock no cambia. |
| AC-AJU-04 | AJU-04 | **Dado** una edición con físico 9, reservado 1 y vendible 8, **cuando** se intenta descontar 9 por daño, **entonces** se rechaza porque supera el vendible. |
| AC-AJU-05 | AJU-05, AJU-08 | **Dado** una edición con físico 5 y reservado 5, **cuando** se registra un conteo real de 7, **entonces** el físico pasa a 7, el vendible a 2 y el ajuste registra +2. |
| AC-AJU-06 | AJU-06 | **Dado** una edición con 5 reservadas, **cuando** el conteo real indicado es 4, **entonces** se rechaza porque es menor que lo reservado. |
| AC-AJU-07 | AJU-07 | **Dado** una edición con físico 9, **cuando** el conteo real indicado es 9, **entonces** se rechaza sin cambiar nada. |
| AC-AJU-08 | AJU-09 | **Dado** un encargado de Ayacucho, **cuando** intenta ajustar el stock de Cusco, **entonces** se rechaza por falta de alcance y se registra como acceso denegado. |
| AC-AJU-09 | AJU-11 | **Dado** una devolución aceptada de 1 impreso en Ayacucho, **cuando** se confirma la recepción, **entonces** el físico de Ayacucho sube en 1 con el motivo devolución. |

## 5. Reglas e invariantes

- Merma y daño solo restan y nunca más que el stock vendible; así las unidades reservadas de pedidos en curso no se pierden.
- La corrección de conteo no puede dejar el físico por debajo de las unidades reservadas.
- Ningún ajuste manual suma por merma o daño. Las únicas entradas son traslados recibidos y devoluciones aceptadas.
- Todo ajuste deja una auditoría; si no se puede auditar, no se aplica (`transversal-auditoria`).

## 6. Casos límite y errores

- Dos ajustes simultáneos sobre la misma edición se resuelven de forma ordenada; el segundo se valida contra el stock ya actualizado.
- Si el vendible es 0, no se puede registrar merma ni daño; el usuario recibe el motivo (todo lo restante está reservado).
- Un conteo mayor al físico con la nota vacía es válido; la nota es opcional.

## 7. Preguntas abiertas

- [ ] ¿Se exige una nota cuando la diferencia de un conteo supera cierto porcentaje (por ejemplo, 20 %)? Propuesta: no en esta versión. Responde: responsable del proyecto.
