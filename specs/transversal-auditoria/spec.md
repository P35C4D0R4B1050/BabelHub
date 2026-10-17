---
id: transversal-auditoria
titulo: Auditoría de operaciones sensibles
estado: Approved
servicio: Transversal
prefijo: AUD
origen: [RF20, NN09, NN18]
calidad: [RNF08, RNF20, RNF23, AC03, AC05, AC11]
depende: []
aprobado_por: Diego Amiquero
aprobado_en: 2026-10-16
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
| AUD-01 | Ubicuo | El sistema deberá registrar, por cada operación sensible, el usuario y su rol o, si la origina el propio sistema, una integración o una persona sin sesión, el actor «sistema», «integración» o «anónimo», además de la fecha y hora en UTC, la acción, un detalle legible y el resultado. | RF20 |
| AUD-02 | Evento | Cuando ocurre un cambio de precio, una anulación de pedido, un ajuste de inventario, una aprobación, despacho, recepción o cancelación de un traslado o un reembolso, el sistema deberá registrarlo en la auditoría. | RF20, NN09 |
| AUD-03 | Ubicuo | El sistema deberá impedir que un registro de auditoría se modifique o se elimine una vez creado, salvo la anonimización de AUD-19. | RNF08 |
| AUD-04 | No deseado | Si no es posible registrar la auditoría de una operación sensible, entonces el sistema deberá cancelar la operación y no aplicar sus efectos. | RNF08 |
| AUD-05 | No deseado | Si un usuario intenta una acción fuera de su rol o de su sucursal, entonces el sistema deberá rechazarla y registrar el intento como un único evento de seguridad de acceso denegado, con su usuario, rol, recurso solicitado y motivo. | RNF05, AC05 |
| AUD-06 | Ubicuo | El sistema deberá excluir de los registros los datos que `transversal-seguridad` prohíbe en registros y mensajes (SEG-10). | RNF08, AC05 |
| AUD-07 | Evento | Cuando el administrador central consulta la auditoría y los eventos de seguridad, el sistema deberá permitir filtrar por acción, usuario u origen, rango de fechas y texto del detalle. | RF20, RNF20 |
| AUD-08 | Ubicuo | El sistema deberá mostrar al encargado de sucursal únicamente el historial de ajustes y el de los traslados en que su propia sucursal es origen o destino. | RNF05 |
| AUD-09 | Ubicuo | El sistema deberá presentar en una sola lista los registros de auditoría y los eventos de seguridad de todos los servicios, distinguidos por tipo, en orden cronológico inverso y en páginas de 20. | RF20 |
| AUD-10 | Evento | Cuando ocurre un ingreso fallido, un bloqueo temporal, un límite de uso excedido, un acceso denegado, una recuperación de acceso, un segundo factor fallido, el uso de un código de respaldo o un cambio de rol, el sistema deberá registrarlo como evento de seguridad con su fecha, el usuario u origen y el motivo. | RNF20, AC05 |
| AUD-11 | Ubicuo | El sistema deberá conservar los registros de auditoría y de eventos de seguridad durante dos años. | RNF23, AC11 |
| AUD-12 | No deseado | Si no es posible registrar un evento de seguridad de una acción ya rechazada, entonces el sistema deberá mantener el rechazo y generar una alerta técnica que aparezca en la lista de alertas, llegue por correo a la administración central y, si la auditoría no responde, se conserve en el registro técnico del servicio. | RNF20 |
| AUD-13 | Evento | Cuando el administrador central abre la lista de alertas de seguridad, el sistema deberá mostrar las de todos los servicios de la más reciente a la más antigua con su fecha, servicio, motivo y estado (pendiente o revisada), y permitir solo a la administración central marcarlas como revisadas registrándolo en la auditoría; si no se puede registrar la marca, la alerta sigue pendiente. | RNF20, AC05 |
| AUD-14 | Evento | Cuando ocurre una entrega de pedido, el paso de un envío a en reparto, la confirmación o la liberación de una reserva, el sistema deberá registrar el movimiento de stock en la auditoría. | RNF08, RF20 |
| AUD-15 | Evento | Cuando ocurre el alta, la edición o la baja de una obra o edición, un cambio de licencia, el retiro de un libro digital, o el alta o la edición de una fuente o de una ruta entre fuentes, el sistema deberá registrarlo en la auditoría. | RF20, NN09 |
| AUD-16 | Evento | Cuando ocurre el alta, la desactivación o la reactivación de un usuario o un cambio de rol, el restablecimiento de la contraseña o del segundo factor de un usuario, la cancelación de una cuenta, la atención de una solicitud sobre datos personales, la restitución de descargas, o la respuesta o el descarte de una hoja de reclamación, el sistema deberá registrarlo en la auditoría. | RF20, NN09, RNF23 |
| AUD-17 | Ubicuo | El sistema deberá permitir que cada servicio entregue su auditoría y sus eventos de seguridad con los mismos filtros y paginación continua, para que la capa web los presente mezclados por fecha sin aplicar reglas de negocio. | RF20, RNF20 |
| AUD-18 | No deseado | Si un servicio no responde a la consulta unificada en 10 segundos, entonces el sistema deberá mostrar los registros de los demás servicios e indicar cuál no respondió. | RF20, AC03 |
| AUD-19 | Evento | Cuando corre la depuración diaria, el sistema deberá anonimizar, incluida la dirección IP, los datos personales de los registros con más de dos años y de las cuentas canceladas desde la depuración anterior, conservando la acción, la fecha y el resultado, y registrar la ejecución con el actor «sistema». | RNF23, AC11 |
| AUD-20 | Ubicuo | El sistema deberá registrar un cambio de rol como un único registro, etiquetado como operación sensible y como evento de seguridad. | RF20, RNF20 |
| AUD-21 | Evento | Cuando la administración consulta la biblioteca de un cliente, una hoja de reclamación o una solicitud sobre datos personales, el sistema deberá registrar la consulta en la auditoría. | RF20, RNF23 |
| AUD-22 | Evento | Cuando se rota la clave de firma de las credenciales o la administración desactiva los avisos de un número, el sistema deberá registrarlo en la auditoría. | RF20, RNF08 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-AUD-01 | AUD-01, AUD-02 | **Dado** un administrador que cambia el precio de una edición, **cuando** guarda el cambio, **entonces** existe un registro con su usuario, rol, fecha UTC, la acción «Cambio de precio», el precio anterior y el nuevo, y el resultado exitoso. |
| AC-AUD-02 | AUD-03 | **Dado** un registro de auditoría existente, **cuando** cualquier usuario o servicio intenta modificarlo o eliminarlo, **entonces** la operación se rechaza y el registro permanece igual. |
| AC-AUD-03 | AUD-04 | **Dado** que el almacén de auditoría no está disponible, **cuando** un encargado intenta registrar un ajuste de inventario, **entonces** el ajuste no se aplica y el usuario recibe un error de servicio no disponible. |
| AC-AUD-04 | AUD-05, AUD-10 | **Dado** un encargado de Ayacucho, **cuando** pide el inventario de Cusco, **entonces** la solicitud se rechaza y queda un único registro «Acceso denegado», como evento de seguridad, con su usuario, rol y el recurso solicitado. |
| AC-AUD-05 | AUD-06 | **Dado** un reembolso con tarjeta o la emisión de un enlace de descarga, **cuando** se registra la operación, **entonces** el detalle no contiene número de tarjeta, token ni la dirección del enlace. |
| AC-AUD-06 | AUD-07, AUD-09 | **Dado** cien registros de varios usuarios, **cuando** el administrador filtra por la acción «Anulación de pedido» y un rango de fechas, **entonces** ve solo los registros coincidentes, del más reciente al más antiguo, en páginas. |
| AC-AUD-07 | AUD-08 | **Dado** ajustes realizados en Ayacucho y en Cusco y un traslado de Cusco a Ayacucho, **cuando** el encargado de Ayacucho consulta el historial, **entonces** solo ve los ajustes de Ayacucho y el traslado en que Ayacucho es destino. |
| AC-AUD-08 | AUD-10 | **Dado** cinco ingresos fallidos de un usuario, **cuando** se consulta el registro de seguridad, **entonces** figuran los intentos y el bloqueo temporal, sin contraseñas. |
| AC-AUD-09 | AUD-11, AUD-19, AUD-03 | **Dado** un evento de seguridad con más de dos años con su dirección IP y otro registro de una cuenta cancelada ayer, **cuando** corre la depuración diaria, **entonces** sus datos personales, incluida la IP, se anonimizan conservando acción, fecha y resultado, queda un registro de la ejecución con el actor «sistema»; uno reciente de una cuenta activa no cambia; y cualquier otra modificación sigue rechazándose. |
| AC-AUD-10 | AUD-12 | **Dado** que el almacén de auditoría no responde, **cuando** se rechaza un acceso, **entonces** el rechazo se mantiene y se genera una alerta técnica que llega por correo a la administración central. |
| AC-AUD-11 | AUD-16, AUD-21 | **Dado** un cliente que cancela su cuenta y un administrador que consulta la biblioteca de un cliente, responde y descarta hojas de reclamación y restablece el segundo factor de un encargado, **cuando** se revisa la auditoría, **entonces** cada acción tiene su registro. |
| AC-AUD-12 | AUD-13 | **Dado** una alerta generada, **cuando** el administrador la abre y la marca como revisada, **entonces** la lista muestra su estado y queda una entrada de auditoría. |
| AC-AUD-13 | AUD-14 | **Dado** un pedido de recojo entregado, un envío que pasa a en reparto y una reserva confirmada que luego se libera, **cuando** se revisa la auditoría, **entonces** cada movimiento tiene su registro con la edición, la fuente y la cantidad. |
| AC-AUD-14 | AUD-15 | **Dado** un administrador que da de alta una obra, cambia una licencia y registra una ruta entre dos fuentes, **cuando** se revisa la auditoría, **entonces** cada acción tiene su registro con el valor anterior y el nuevo cuando corresponda. |
| AC-AUD-15 | AUD-17, AUD-09 | **Dado** registros recientes en Ventas, Inventario e Identidad, **cuando** el administrador abre la auditoría, **entonces** ve una sola lista con los registros y eventos de los tres servicios, del más reciente al más antiguo, en páginas de 20; y al pasar a la segunda página no se repiten ni se omiten registros entre servicios. |
| AC-AUD-16 | AUD-18 | **Dado** que Inventario no responde, **cuando** el administrador abre la auditoría, **entonces** ve los registros de Ventas e Identidad y un aviso de que faltan los de Inventario. |
| AC-AUD-17 | AUD-20 | **Dado** un administrador que cambia el rol de un usuario, **cuando** se revisa, **entonces** existe un único registro etiquetado como operación sensible y como evento de seguridad. |
| AC-AUD-18 | AUD-01, AUD-14 | **Dado** el aviso de la pasarela que confirma una reserva, **cuando** se revisa la auditoría, **entonces** el registro indica el actor «integración» o «sistema» en lugar de un usuario. |
| AC-AUD-19 | AUD-22 | **Dado** una rotación de la clave de firma y la desactivación de avisos de un número, **cuando** se revisa la auditoría, **entonces** ambas acciones tienen su registro. |

## 5. Reglas e invariantes

- Un registro de auditoría es de solo inserción: no existe edición ni borrado, ni siquiera para el administrador; la única modificación permitida es la anonimización de AUD-19.
- El registro de auditoría y el cambio que describe ocurren en la misma unidad de trabajo: o ambos se aplican o ninguno.
- La hora se guarda en UTC y se muestra en hora de Lima.
- Cada servicio guarda sus propios registros; la consulta unificada la compone la capa web, que pide a cada servicio la siguiente página con los mismos filtros y las mezcla por fecha UTC (AUD-17), sin reglas de negocio.
- Un acceso denegado es un único registro, un evento de seguridad (AUD-05, AUD-10); no se duplica como registro de auditoría.
- Las alertas de seguridad se generan en cada servicio y la capa web las presenta juntas; cada alerta se marca como revisada en el servicio que la generó.

## 6. Casos límite y errores

- Operaciones simultáneas del mismo usuario generan registros separados, ordenados por fecha y por un número de secuencia dentro de su servicio; entre servicios el orden es por fecha UTC y puede diferir en milisegundos por diferencias de reloj.
- La expiración automática de una reserva pendiente no se audita (queda en la propia reserva); sí la confirmación y la liberación (AUD-14).
- La inmutabilidad se garantiza con permisos de base de datos, no con firmas criptográficas: quien administre el servidor de datos queda fuera del alcance (riesgo aceptado, `docs/05-seguridad/01-modelo-de-amenazas.md`).
- Un detalle truncado conserva siempre la acción y el resultado.
- Intentos repetidos de acceso denegado generan un registro cada uno.
- Un cambio de rol genera un único registro (AUD-20).
- Los límites excedidos se registran agrupados por ventana (`transversal-seguridad`, SEG-08); los accesos denegados, uno por uno.
- `transversal-seguridad` depende de esta spec para el contrato de registro; AUD-06 y AUD-12 solo se apoyan en SEG-10 y SEG-09 por referencia, sin dependencia de implementación.
- Un detalle de más de 1000 caracteres se trunca con aviso; la hora se muestra en hora de Lima aunque se guarde en UTC.

## 7. Preguntas abiertas

- [x] ¿Por cuánto tiempo se conserva la auditoría? Resuelto el 2026-10-10: dos años, igual que el Libro de Reclamaciones (AUD-11).
- [x] ¿La consulta unificada de los tres almacenes se compone en la capa web o se centraliza? Resuelto el 2026-10-16: la capa web la compone mezclando por fecha las páginas de cada servicio, sin reglas de negocio (AUD-09, AUD-17, AUD-18).
- [x] ¿Qué eventos adicionales se auditan? Resuelto el 2026-10-16: movimientos de stock, catálogo y licencias, fuentes y rutas, y consultas de datos personales por la administración (AUD-14 a AUD-16).
