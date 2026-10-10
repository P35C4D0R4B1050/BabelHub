---
id: transversal-auditoria
titulo: Auditoría de operaciones sensibles
estado: Draft
servicio: Transversal
prefijo: AUD
origen: [RF20, NN09]
calidad: [RNF08, AC05]
depende: []
---

# Spec — Auditoría de operaciones sensibles

## 1. Propósito

Dejar un registro confiable e inmutable de quién hizo qué, cuándo y con qué resultado en las operaciones sensibles, para que la administración central pueda revisarlo (NN09). No es un servicio aparte: define un contrato único que cada servicio cumple con su propio almacén.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Qué eventos se auditan, qué datos tiene cada registro y las garantías de inmutabilidad. | Los logs técnicos y las métricas (convenciones en `docs/09`). |
| Consulta con filtros para el administrador central. | Exportación de reportes e indicadores (RF21, fase posterior). |
| Registro de accesos denegados por falta de permiso o de alcance. | La lógica de cada operación (vive en la spec de su feature). |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| AUD-01 | Ubicuo | El sistema deberá registrar, por cada operación sensible, el usuario, su rol, la fecha y hora en UTC, la acción, un detalle legible y el resultado. | RF20 |
| AUD-02 | Evento | Cuando ocurre un cambio de precio, una anulación de pedido, un ajuste de inventario, una aprobación, despacho o recepción de traslado, un retiro de libro digital, un reembolso o un cambio de usuarios o roles, el sistema deberá registrarlo en la auditoría. | RF20, NN09 |
| AUD-03 | Ubicuo | El sistema deberá impedir que un registro de auditoría se modifique o se elimine una vez creado. | RNF08 |
| AUD-04 | No deseado | Si no es posible registrar la auditoría de una operación sensible, entonces el sistema deberá cancelar la operación y no aplicar sus efectos. | RNF08 |
| AUD-05 | No deseado | Si un usuario intenta una acción fuera de su rol o de su sucursal, entonces el sistema deberá rechazarla y registrar el intento como acceso denegado. | RNF05, AC05 |
| AUD-06 | Ubicuo | El sistema deberá excluir de los registros datos de tarjeta, contraseñas, tokens y enlaces de descarga. | RNF08, AC05 |
| AUD-07 | Evento | Cuando el administrador central consulta la auditoría, el sistema deberá permitir filtrar por acción, usuario, rango de fechas y texto del detalle. | RF20 |
| AUD-08 | Ubicuo | El sistema deberá mostrar al encargado de sucursal únicamente el historial de ajustes y traslados de su propia sucursal. | RNF05 |
| AUD-09 | Ubicuo | El sistema deberá presentar los registros de auditoría en orden cronológico inverso con paginación. | RF20 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-AUD-01 | AUD-01, AUD-02 | **Dado** un administrador que cambia el precio de una edición, **cuando** guarda el cambio, **entonces** existe un registro con su usuario, rol, fecha UTC, la acción «Cambio de precio», el precio anterior y el nuevo, y el resultado exitoso. |
| AC-AUD-02 | AUD-03 | **Dado** un registro de auditoría existente, **cuando** cualquier usuario o servicio intenta modificarlo o eliminarlo, **entonces** la operación se rechaza y el registro permanece igual. |
| AC-AUD-03 | AUD-04 | **Dado** que el almacén de auditoría no está disponible, **cuando** un encargado intenta registrar un ajuste de inventario, **entonces** el ajuste no se aplica y el usuario recibe un error de servicio no disponible. |
| AC-AUD-04 | AUD-05 | **Dado** un encargado de Ayacucho, **cuando** pide el inventario de Cusco, **entonces** la solicitud se rechaza y queda un registro «Acceso denegado» con su usuario y el recurso solicitado. |
| AC-AUD-05 | AUD-06 | **Dado** un reembolso con tarjeta o la emisión de un enlace de descarga, **cuando** se registra la operación, **entonces** el detalle no contiene número de tarjeta, token ni la dirección del enlace. |
| AC-AUD-06 | AUD-07, AUD-09 | **Dado** cien registros de varios usuarios, **cuando** el administrador filtra por la acción «Anulación de pedido» y un rango de fechas, **entonces** ve solo los registros coincidentes, del más reciente al más antiguo, en páginas. |
| AC-AUD-07 | AUD-08 | **Dado** ajustes realizados en Ayacucho y en Cusco, **cuando** el encargado de Ayacucho consulta el historial, **entonces** solo ve los de Ayacucho. |

## 5. Reglas e invariantes

- Un registro de auditoría es de solo inserción: no existe edición ni borrado, ni siquiera para el administrador.
- El registro de auditoría y el cambio que describe ocurren en la misma unidad de trabajo: o ambos se aplican o ninguno.
- La hora se guarda en UTC y se muestra en hora de Lima.
- Cada servicio guarda sus propios registros; la consulta unificada la compone la capa web.

## 6. Casos límite y errores

- Operaciones simultáneas del mismo usuario generan registros separados, ordenados por fecha y por un número de secuencia.
- Un detalle demasiado largo se trunca con aviso, sin perder la acción ni el resultado.
- Intentos repetidos de acceso denegado generan un registro cada uno.

## 7. Preguntas abiertas

- [ ] ¿Por cuánto tiempo se conserva la auditoría? Propuesta: dos años, igual que el Libro de Reclamaciones. Responde: responsable del proyecto.
- [ ] ¿La consulta unificada de los tres almacenes (ventas, inventario, identidad) se compone en la capa web o se centraliza más adelante? Propuesta: capa web.
