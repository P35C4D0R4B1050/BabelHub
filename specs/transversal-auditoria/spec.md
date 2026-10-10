---
id: transversal-auditoria
titulo: Auditoría de operaciones sensibles
estado: Draft
servicio: Transversal
prefijo: AUD
origen: [RF20, NN09, NN18]
calidad: [RNF08, RNF20, RNF23, AC05, AC11]
depende: []
---

# Spec — Auditoría de operaciones sensibles

## 1. Propósito

Dejar un registro confiable e inmutable de quién hizo qué, cuándo y con qué resultado en las operaciones sensibles, para que la administración central pueda revisarlo (NN09). No es un servicio aparte: define un contrato único que cada servicio cumple con su propio almacén.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Qué eventos se auditan, qué datos tiene cada registro y las garantías de inmutabilidad. | Los logs técnicos y las métricas (convenciones en `docs/07-proceso-de-desarrollo/02-convenciones-tecnicas.md`). |
| Consulta con filtros para el administrador central. | Exportación de reportes e indicadores (RF21, fase posterior). |
| Registro de accesos denegados por falta de permiso o de alcance y de eventos de seguridad. | La lógica de cada operación (vive en la spec de su feature). |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| AUD-01 | Ubicuo | El sistema deberá registrar, por cada operación sensible, el usuario, su rol, la fecha y hora en UTC, la acción, un detalle legible y el resultado. | RF20 |
| AUD-02 | Evento | Cuando ocurre un cambio de precio, una anulación de pedido, un ajuste de inventario, una aprobación, despacho o recepción de traslado, un retiro de libro digital, un reembolso, un cambio de usuarios o roles, el restablecimiento de la contraseña de un usuario, la cancelación de una cuenta, la atención de una solicitud sobre datos personales, la restitución de descargas o la consulta de la biblioteca de un cliente por el administrador, el sistema deberá registrarlo en la auditoría. | RF20, NN09, RNF23 |
| AUD-03 | Ubicuo | El sistema deberá impedir que un registro de auditoría se modifique o se elimine una vez creado, salvo la anonimización de AUD-11. | RNF08 |
| AUD-04 | No deseado | Si no es posible registrar la auditoría de una operación sensible, entonces el sistema deberá cancelar la operación y no aplicar sus efectos. | RNF08 |
| AUD-05 | No deseado | Si un usuario intenta una acción fuera de su rol o de su sucursal, entonces el sistema deberá rechazarla y registrar el intento como acceso denegado. | RNF05, AC05 |
| AUD-06 | Ubicuo | El sistema deberá excluir de los registros los datos que `transversal-seguridad` prohíbe en registros y mensajes (SEG-10). | RNF08, AC05 |
| AUD-07 | Evento | Cuando el administrador central consulta la auditoría y los eventos de seguridad, el sistema deberá permitir filtrar por acción, usuario u origen, rango de fechas y texto del detalle. | RF20, RNF20 |
| AUD-08 | Ubicuo | El sistema deberá mostrar al encargado de sucursal únicamente el historial de ajustes y traslados de su propia sucursal. | RNF05 |
| AUD-09 | Ubicuo | El sistema deberá presentar los registros de auditoría en orden cronológico inverso con paginación. | RF20 |
| AUD-10 | Evento | Cuando ocurre un ingreso fallido, un bloqueo temporal, un límite de uso excedido, una recuperación de acceso, un segundo factor fallido, el uso de un código de respaldo o un cambio de rol, el sistema deberá registrarlo como evento de seguridad con su fecha, el usuario u origen y el motivo. | RNF20, AC05 |
| AUD-11 | Ubicuo | El sistema deberá conservar los registros de auditoría y de eventos de seguridad durante dos años y, mediante un proceso de depuración diario, anonimizar después sus datos personales y los de las cuentas canceladas, conservando la acción, la fecha y el resultado. | RNF23, AC11 |
| AUD-13 | Evento | Cuando el administrador central abre la lista de alertas de seguridad, el sistema deberá mostrarlas de la más reciente a la más antigua con su fecha, motivo y estado de revisión, y permitir marcarlas como revisadas registrándolo en la auditoría. | RNF20, AC05 |
| AUD-12 | No deseado | Si no es posible registrar un evento de seguridad de una acción ya rechazada, entonces el sistema deberá mantener el rechazo y generar una alerta técnica. | RNF20 |

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
| AC-AUD-08 | AUD-10 | **Dado** cinco ingresos fallidos de un usuario, **cuando** se consulta el registro de seguridad, **entonces** figuran los intentos y el bloqueo temporal, sin contraseñas. |
| AC-AUD-09 | AUD-11, AUD-03 | **Dado** un registro con más de dos años y otro de una cuenta cancelada, **cuando** se ejecuta la depuración, **entonces** sus datos personales se anonimizan conservando acción, fecha y resultado; uno reciente de una cuenta activa no cambia; y cualquier otra modificación sigue rechazándose. |
| AC-AUD-11 | AUD-02 | **Dado** un cliente que cancela su cuenta y un administrador que consulta la biblioteca de un cliente, **cuando** se revisa la auditoría, **entonces** ambas acciones tienen su registro. |
| AC-AUD-12 | AUD-13 | **Dado** una alerta generada, **cuando** el administrador la abre y la marca como revisada, **entonces** la lista muestra su estado y queda una entrada de auditoría. |
| AC-AUD-10 | AUD-12 | **Dado** que el almacén de auditoría no responde, **cuando** se rechaza un acceso, **entonces** el rechazo se mantiene y se genera una alerta técnica. |

## 5. Reglas e invariantes

- Un registro de auditoría es de solo inserción: no existe edición ni borrado, ni siquiera para el administrador; la única modificación permitida es la anonimización de AUD-11.
- El registro de auditoría y el cambio que describe ocurren en la misma unidad de trabajo: o ambos se aplican o ninguno.
- La hora se guarda en UTC y se muestra en hora de Lima.
- Cada servicio guarda sus propios registros; la consulta unificada la compone la capa web.

## 6. Casos límite y errores

- Operaciones simultáneas del mismo usuario generan registros separados, ordenados por fecha y por un número de secuencia.
- Un detalle demasiado largo se trunca con aviso, sin perder la acción ni el resultado.
- Intentos repetidos de acceso denegado generan un registro cada uno.
- Un cambio de rol genera un único registro, etiquetado como operación sensible y como evento de seguridad.

## 7. Preguntas abiertas

- [x] ¿Por cuánto tiempo se conserva la auditoría? Resuelto el 2026-10-10: dos años, igual que el Libro de Reclamaciones (AUD-11).
- [ ] ¿La consulta unificada de los tres almacenes (ventas, inventario, identidad) se compone en la capa web o se centraliza más adelante? Propuesta: capa web.
