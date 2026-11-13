---
id: ecommerce-devoluciones
titulo: Devoluciones y reembolsos
estado: Approved
aprobado_por: Claude (delegado por Diego Amiquero)
aprobado_en: 2026-11-13
servicio: E-commerce
prefijo: DEV
origen: [RF09, RF27, NN05, NN15]
calidad: [AC01, AC03, AC04, RNF05, RNF08, RNF23]
depende: [ecommerce-pedidos, ecommerce-pagos, ecommerce-biblioteca-digital, inventario-ajustes, transversal-auditoria, transversal-notificaciones, identidad-seguridad, transversal-seguridad]
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
| DEV-01 | Evento | Cuando un cliente solicita una devolución, el sistema deberá registrar, de un solo tipo (impreso o digital), las líneas, las cantidades, el motivo y, para un impreso, la sucursal receptora que eligió, con cada línea en estado solicitada. | RF09 |
| DEV-02 | No deseado | Si se solicita la devolución de una línea impresa que aún no está entregada, entonces el sistema deberá rechazar la solicitud. | RF09 |
| DEV-03 | No deseado | Si se solicitan más unidades de las compradas, ya devueltas o ya reembolsadas por una anulación, aunque la solicitud llegue a la vez que otra, entonces el sistema deberá rechazar la solicitud. | RF09, AC01 |
| DEV-04 | Estado | Mientras no se haya entregado ningún enlace de descarga del acceso de un libro digital desde su concesión o reactivación y no hayan pasado 14 días calendario desde la aprobación del pago, el sistema deberá aprobar automáticamente la solicitud de reembolso de esa línea, con el responsable «sistema», revocando el acceso en la misma operación. | NN15 |
| DEV-05 | No deseado | Si ya se entregó un enlace de descarga del libro digital, o pasaron más de 14 días calendario desde el pago, y el cliente no reporta un archivo defectuoso, entonces el sistema deberá rechazar el reembolso explicando la política. | NN15 |
| DEV-06 | Evento | Cuando el cliente reporta, dentro de 30 días calendario desde el pago, un archivo defectuoso de un acceso suyo vigente, el sistema deberá registrar el reporte, suspender las descargas de ese acceso y dejar la solicitud para que la administración central la apruebe o la rechace. | NN15 |
| DEV-07 | Evento | Cuando la sucursal receptora confirma la recepción de una devolución aprobada de un impreso, el sistema deberá pedir al inventario que reingrese las unidades a esa sucursal y, solo si lo logra, pasarla a recibida. | RF09 |
| DEV-08 | Evento | Cuando una devolución de un impreso pasa a recibida, o se aprueba el reembolso de un libro digital, el sistema deberá solicitar a la pasarela el precio unitario por la cantidad devuelta, sin el envío y sin superar lo cobrado acumulado de esa línea, y pasar la línea a reembolsada solo cuando la pasarela confirme. | RF09 |
| DEV-09 | Evento | Cuando se aprueba el reembolso de una línea digital, el sistema deberá revocar su acceso antes de pedir el reembolso a la pasarela, mantenerlo revocado aunque el reembolso falle y no reingresar stock. | RF09, NN15 |
| DEV-10 | Ubicuo | El sistema deberá gestionar, por línea, los estados solicitada, aprobada, rechazada, recibida (solo impresos) y reembolsada, con fecha y responsable, que puede ser el sistema, en cada cambio. | RF09 |
| DEV-11 | Evento | Cuando cambia el estado de una devolución, el sistema deberá notificar al cliente. | RF09 |
| DEV-12 | Evento | Cuando se solicita, aprueba, rechaza (también por plazo), recibe, reingresa o reembolsa una devolución, el sistema deberá registrarlo en la auditoría, con el monto cuando lo haya. | RNF08 |
| DEV-13 | Ubicuo | El sistema deberá permitir aprobar, rechazar o confirmar la recepción de la devolución de un impreso solo al encargado de la sucursal receptora elegida o a la administración central. | RF09, RNF05 |
| DEV-14 | No deseado | Si una devolución aprobada de un impreso lleva más de 10 días calendario, en hora de Lima, desde su aprobación sin que la sucursal confirme su recepción, entonces el sistema deberá pasarla a rechazada sin reembolso y avisar al cliente. | RF09 |
| DEV-15 | No deseado | Si se solicita la devolución de un impreso cuya línea fue entregada hace más de 15 días calendario, en hora de Lima, entonces el sistema deberá rechazar la solicitud indicando el plazo. | RF09, NN05 |
| DEV-16 | No deseado | Si la confirmación de la recepción y el vencimiento del plazo de DEV-14 ocurren a la vez, entonces el sistema deberá aplicar solo el primero que confirma y rechazar el otro por estado inválido. | RF09, AC01 |
| DEV-17 | No deseado | Si se repite la aprobación, la recepción o el reembolso de una línea, entonces el sistema deberá no reembolsar ni reingresar dos veces, usando una clave de idempotencia por devolución y línea. | RF09, AC01 |
| DEV-18 | Ubicuo | El sistema deberá permitir volver a solicitar una línea cuya devolución fue rechazada, por plazo o por decisión, si aún está dentro del plazo de DEV-15 (o de DEV-06 en un digital, repitiendo un reporte rechazado), y no contarla como devuelta para DEV-03. | RF09 |
| DEV-19 | No deseado | Si se solicita la devolución de una línea anulada, ya reembolsada o con el acceso digital revocado, entonces el sistema deberá rechazar la solicitud sin crear un segundo reembolso. | RF09, AC01 |
| DEV-20 | No deseado | Si la entrega de un enlace de descarga y la aprobación automática del reembolso de DEV-04 ocurren a la vez, entonces el sistema deberá aplicar solo la primera que confirma y rechazar la otra por estado inválido. | NN15, AC01 |
| DEV-21 | No deseado | Si falla la solicitud de reembolso a la pasarela, entonces el sistema deberá mantener la línea con el reembolso pendiente, visible para la administración, y permitir reintentarlo sin repetir los pasos ya hechos. | RF09, AC03 |
| DEV-22 | No deseado | Si se reporta un archivo defectuoso de un acceso ya revocado, suspendido o con una solicitud abierta, entonces el sistema deberá rechazar el reporte sin crear otro. | NN15 |
| DEV-23 | Evento | Cuando Identidad consulta los pendientes de una cuenta, el sistema deberá informar solo tres números de esa cuenta, sin contar un mismo caso en dos: devoluciones abiertas (alguna línea de un impreso solicitada o aprobada), reportes de archivo defectuoso sin decidir (la solicitud digital que crean) y reembolsos pendientes (línea recibida o aprobada de un digital hasta confirmar su reembolso), sin revelar los de otra cuenta. | RF09, RF27 |
| DEV-24 | Evento | Cuando se ejecuta la cancelación de una cuenta, el sistema deberá redactar el motivo y el texto de las solicitudes y reportes de sus devoluciones, abiertas o cerradas, quitar de ellas el responsable cuando es esa cuenta y la referencia al acceso de sus reportes, y conservar el pedido, las líneas, cantidades, importes, estados y fechas. | RF09, RF27, RNF23 |
| DEV-25 | No deseado | Si no puede redactar o quitar los datos de alguna solicitud o reporte al cancelar una cuenta, entonces el sistema deberá informarlo para que no se ejecute la cancelación, sin dejar ninguna redactada a medias, de modo que pueda reintentarse (IDE-70). | RF27, RNF23 |
| DEV-26 | No deseado | Si se solicita una devolución o se reporta un archivo de una cuenta después de una consulta de pendientes sin pendientes que autoriza ejecutar su cancelación (IDE-70, IDE-71) y antes de que se redacten sus datos (DEV-24), entonces el sistema deberá rechazarlo sin escribir ningún registro. | RF09, RF27, AC01 |
| DEV-27 | Evento | Cuando todas las líneas de una solicitud o de un reporte están en reembolsada o rechazada y han pasado dos años calendario, en hora de Lima, desde el último de esos cambios, el sistema deberá redactar su motivo y su texto, quitar el responsable cuando es el cliente y registrar la depuración en la auditoría con actor «sistema» y conteo, sin datos personales. | RF09, RNF08, RNF23 |
| DEV-28 | Ubicuo | El sistema deberá comprobar en la Aplicación que el pedido o el acceso de una solicitud o un reporte es del cliente, mostrarle solo lo suyo, al encargado solo las devoluciones de impresos de su sucursal receptora y a la administración central todas, y rechazar lo ajeno con la misma respuesta exista o no, registrándolo como acceso denegado. | RF09, RNF05, AC04 |
| DEV-29 | Ubicuo | El sistema deberá excluir el motivo y el texto de las solicitudes y reportes de los avisos, la auditoría y los registros técnicos. | RF09, RNF23 |
| DEV-30 | No deseado | Si no puede auditar o redactar al correr la depuración de DEV-27, entonces el sistema deberá no redactar nada ese día y reintentarlo al día siguiente. | RNF08, RNF23 |
| DEV-31 | Ubicuo | El sistema deberá no admitir adjuntos en las solicitudes ni en los reportes en esta versión. | RF09, RNF23 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-DEV-01 | DEV-01, DEV-10 | **Dado** un pedido con un impreso entregado y un libro digital, **cuando** el cliente solicita devolver el impreso con el motivo «llegó dañado» eligiendo la sucursal Ayacucho, **entonces** se registra una devolución solicitada con fecha, la línea y la sucursal receptora; y si incluye también el digital, se rechaza por mezclar tipos y debe pedir dos devoluciones. |
| AC-DEV-02 | DEV-02 | **Dado** un pedido con una línea entregada y otra en tránsito, **cuando** el cliente solicita devolver la que está en tránsito, **entonces** se rechaza porque aún no fue entregada; y la entregada sí se acepta. |
| AC-DEV-03 | DEV-03 | **Dado** una línea de 1 unidad ya devuelta, **cuando** se solicita devolverla otra vez, o dos solicitudes simultáneas piden devolverla, **entonces** se rechaza la repetida y solo una queda registrada. |
| AC-DEV-04 | DEV-04 | **Dado** un libro digital sin enlaces de descarga entregados y comprado hace menos de 14 días, **cuando** el cliente pide el reembolso, **entonces** se aprueba solo, con el responsable «sistema», el acceso se revoca y avanza al reembolso. |
| AC-DEV-05 | DEV-05 | **Dado** un libro digital del que se entregó un enlace de descarga, aunque no se haya usado, **cuando** el cliente pide el reembolso sin reportar defecto, **entonces** se rechaza explicando la política. |
| AC-DEV-06 | DEV-06 | **Dado** un libro digital descargado, **cuando** el cliente reporta que el archivo no abre, **entonces** el reporte queda registrado, las descargas del acceso se suspenden y la solicitud espera la decisión de la administración; y un reporte sobre un acceso ajeno se rechaza. |
| AC-DEV-07 | DEV-07 | **Dado** una devolución aprobada de un impreso, **cuando** Ayacucho confirma su recepción, **entonces** el inventario suma 1 unidad al físico de Ayacucho y solo entonces la devolución pasa a recibida; y si el reingreso falla, no pasa a recibida y se puede reintentar. |
| AC-DEV-08 | DEV-08 | **Dado** una devolución aprobada de un impreso, **cuando** aún no se confirmó la recepción, **entonces** no se solicita ningún reembolso; y **cuando** pasa a recibida, **entonces** se solicita a la pasarela el precio unitario por las unidades devueltas, sin el envío; y para un libro digital, el reembolso se solicita al aprobarse. |
| AC-DEV-09 | DEV-09 | **Dado** un reembolso aprobado de un libro digital, **cuando** se aprueba, **entonces** el cliente pierde el acceso antes de pedirse el reembolso, lo sigue sin acceso aunque el reembolso falle y el inventario no cambia. |
| AC-DEV-10 | DEV-11 | **Dado** una devolución, **cuando** pasa a aprobada, **entonces** el cliente recibe un aviso. |
| AC-DEV-11 | DEV-12 | **Dado** una devolución que se solicita, se aprueba, se recibe, se reingresa y se reembolsa, o se rechaza por plazo, **cuando** termina cada paso, **entonces** existe una entrada de auditoría con el usuario o el actor «sistema», el pedido y el monto. |
| AC-DEV-12 | DEV-13 | **Dado** una devolución solicitada de un impreso con sucursal receptora Ayacucho, **cuando** el encargado de Cusco intenta aprobarla, **entonces** se rechaza; y cuando lo hace el de Ayacucho o la administración, se aprueba. |
| AC-DEV-13 | DEV-14 | **Dado** una devolución aprobada hace 10 días y 1 hora sin recepción, **cuando** corre la revisión de plazos, **entonces** pasa a rechazada, no se reembolsa y el cliente recibe un aviso; y una de 10 días exactos no cambia. |
| AC-DEV-14 | DEV-15 | **Dado** un impreso cuya línea se entregó hace 15 días y 1 hora, **cuando** el cliente solicita devolverlo, **entonces** se rechaza indicando el plazo de 15 días; y uno de 15 días exactos se registra. |
| AC-DEV-15 | DEV-16 | **Dado** una devolución aprobada al límite del plazo, **cuando** la recepción y el vencimiento ocurren a la vez, **entonces** gana la primera que confirma y la otra se rechaza por estado inválido. |
| AC-DEV-16 | DEV-17 | **Dado** una devolución recibida, **cuando** se repiten la recepción o el reembolso, o la pasarela cae y se reintenta, **entonces** el físico sube una vez y el reembolso se hace una sola vez. |
| AC-DEV-17 | DEV-18 | **Dado** una devolución rechazada por plazo de recepción o por decisión de la sucursal pero con la línea aún dentro de los 15 días de entrega, **cuando** el cliente la solicita otra vez, **entonces** se acepta; y si ya pasaron los 15 días, se rechaza. |
| AC-DEV-18 | DEV-19 | **Dado** una línea digital ya anulada y reembolsada por una anulación del pedido, **cuando** el cliente pide su reembolso, **entonces** se rechaza y no se crea un segundo reembolso. |
| AC-DEV-19 | DEV-20 | **Dado** un acceso sin enlaces entregados, **cuando** el cliente pide el reembolso a la vez que descarga, **entonces** solo gana el primero que confirma: o se entrega el enlace y el reembolso se rechaza por política, o se aprueba el reembolso y la descarga se rechaza. |
| AC-DEV-20 | DEV-21 | **Dado** que la pasarela no responde, **cuando** una devolución llega a recibida, **entonces** la línea queda con el reembolso pendiente, visible para la administración, y al reintentar se reembolsa una sola vez. |
| AC-DEV-21 | DEV-22 | **Dado** un acceso suspendido con un reporte abierto, **cuando** el cliente reporta otra vez, **entonces** se rechaza sin crear otro reporte. |
| AC-DEV-22 | DEV-04, DEV-05 | **Dado** un libro digital sin enlaces comprado hace 15 días, **cuando** el cliente pide el reembolso sin reportar defecto, **entonces** se rechaza por plazo; y con 14 días exactos se aprueba. |
| AC-DEV-23 | DEV-06 | **Dado** un reporte de archivo defectuoso, **cuando** un encargado de sucursal intenta decidirlo, **entonces** se rechaza por falta de permiso; y la administración central puede aprobarlo o rechazarlo. |
| AC-DEV-24 | DEV-06, DEV-18 | **Dado** un acceso pagado hace 31 días, **cuando** el cliente reporta un archivo defectuoso, **entonces** se rechaza por plazo; y con 30 días exactos se registra, y si la administración lo rechaza puede repetirse solo dentro de ese plazo. |
| AC-DEV-25 | DEV-23 | **Dado** un cliente con una devolución de un impreso solicitada, una línea recibida con el reembolso en curso, un reporte sin decidir y un libro digital aprobado, otro con todo resuelto, otro con una devolución aprobada vencida a 10 días y 1 hora ya rechazada por la revisión de plazos, y un cliente ajeno con casos abiertos, **cuando** Identidad consulta los pendientes de cada uno, **entonces** el primero figura con una devolución abierta, un reporte y dos reembolsos pendientes (la línea recibida y el digital aprobado, ninguno contado además como abierta ni como reporte), los demás con ninguno, sin motivo, texto ni importes y sin los del cliente ajeno. |
| AC-DEV-26 | DEV-24 | **Dado** un cliente con una solicitud abierta con motivo, un reporte de archivo defectuoso con texto, una devolución ya reembolsada, una sin devoluciones y un cliente ajeno, **cuando** se ejecuta la cancelación de su cuenta, **entonces** el motivo y el texto quedan redactados, el responsable que era el cliente y la referencia al acceso se quitan, se conservan el pedido, las líneas, cantidades, importes, estados y fechas, el cliente sin devoluciones termina sin error y el ajeno no cambia. |
| AC-DEV-27 | DEV-25 | **Dado** un cliente con tres solicitudes de las que la segunda no se puede redactar, **cuando** se ejecuta la cancelación, **entonces** se informa para que no se ejecute, ninguna queda redactada a medias, y el reintento posterior redacta las tres sin fallar con lo ya hecho. |
| AC-DEV-28 | DEV-26 | **Dado** una cuenta con una solicitud registrada antes de la consulta de pendientes, otra con la cancelación diferida porque había pendientes y otra cuya consulta ya autorizó ejecutar la cancelación, **cuando** piden una devolución o reportan un archivo, **entonces** la primera se acepta y cuenta como pendiente, la segunda sigue aceptándose, y la tercera se rechaza sin escribir ningún registro con el motivo ni el texto. |
| AC-DEV-29 | DEV-27 | **Dado** solicitudes y reportes cuyo último cambio a reembolsada o rechazada fue hace 2 años y un día, otros hace 2 años menos un día, una solicitud con dos líneas de las que solo una es final y uno aún abierto, **cuando** corre la depuración, **entonces** los primeros quedan con motivo y texto redactados y sin el responsable cliente, con una entrada de auditoría con actor «sistema» y conteo sin datos personales, la solicitud con una línea no final y los demás no cambian y repetirla no cambia nada. |
| AC-DEV-30 | DEV-28, DEV-13 | **Dado** una devolución de un impreso con sucursal receptora Ayacucho y un reporte digital, **cuando** un cliente ajeno la pide, o pide una de un pedido inexistente, o el encargado de Cusco intenta listarla o confirmar su recepción, o un encargado intenta ver el reporte digital, **entonces** se rechaza con la misma respuesta exista o no, queda un acceso denegado y la confirma el de Ayacucho o la administración. |
| AC-DEV-31 | DEV-29, DEV-31 | **Dado** una devolución con motivo, un reporte con texto y un intento de adjuntar un archivo, **cuando** se envían avisos, se audita y se registra la actividad, **entonces** ni el motivo ni el texto aparecen en ellos y el adjunto se rechaza. |
| AC-DEV-32 | DEV-30 | **Dado** una depuración en la que no se puede auditar y otra en la que no se puede redactar, **cuando** corren, **entonces** no se redacta nada ese día y al día siguiente, ya disponible, se redacta y se audita. |

## 5. Reglas e invariantes

- Una línea impresa solo se devuelve si está entregada y dentro de los 15 días desde su entrega; una línea digital solo se reembolsa si no se entregó un enlace de descarga o si el archivo está defectuoso y la administración lo aprueba.
- Una devolución es de un solo tipo y cada línea tiene su propio estado; un pedido mixto genera dos devoluciones.
- La «sucursal receptora» es la que el cliente elige al solicitar; ella aprueba, recibe y reingresa.
- El envío no se reembolsa en una devolución.
- El reingreso de stock de una devolución impresa lo hace el inventario de la sucursal que recibe, no el E-commerce.
- Un reembolso nunca supera lo cobrado por esa línea.
- Un impreso se reembolsa solo después de confirmada su recepción y su reingreso al stock; un libro digital, al aprobarse, porque no hay mercadería que recibir.
- Ciclo de una devolución de impreso: solicitada → aprobada → recibida → reembolsada; de un digital: solicitada → aprobada → reembolsada; en ambos, solicitada → rechazada, y en un impreso aprobada → rechazada si vence el plazo de recepción.
- Los digitales nunca reingresan stock.
- Devoluciones no guarda identificador de cuenta salvo como responsable de un cambio de estado, y localiza lo de una cuenta por el pedido, sin que importe el orden frente a BIB-25.
- Devoluciones implementa los puertos de pendientes y de cancelación que define `identidad-seguridad` (IDE-47) sin que Identidad dependa de ella; el motivo y el texto de los reportes son texto libre, sin adjuntos, y se redactan al cancelar la cuenta y a los dos años (decisión del 2026-11-13).

## 6. Casos límite y errores

- Una devolución aprobada cuyo reingreso o reembolso falla queda reintentable, sin repetir el paso que ya se completó.
- Una solicitud sobre varias líneas del mismo tipo se evalúa línea por línea.
- **Riesgo aceptado:** un reporte de archivo defectuoso aprobado por la administración deja al cliente con el archivo ya descargado, porque no hay marca de agua ni DRM (AD20).
- Si el cliente reporta un archivo defectuoso, el sistema verifica que el reporte esté asociado a un acceso real del cliente.
- Una falla del Notificador no interrumpe una transición de la devolución (DEV-11, DEV-14): el aviso queda pendiente de reintento.

## 7. Preguntas abiertas

- [x] ¿Hasta cuándo se puede reportar un archivo defectuoso? Resuelto el 2026-10-16: 30 días calendario desde el pago (DEV-06); a verificar con B09.
- [x] ¿Tiene plazo el reembolso automático de un libro digital? Resuelto el 2026-10-16: 14 días calendario desde la aprobación del pago (DEV-04, DEV-05); a verificar con B09.
- [x] ¿Quién aprueba el reembolso de un libro digital? Resuelto el 2026-10-16: automático si no se entregó ningún enlace de descarga (responsable «sistema»); un reporte de archivo defectuoso lo decide la administración (DEV-04, DEV-06).
- [x] ¿Qué es la sucursal receptora? Resuelto el 2026-10-16: la que elige el cliente al solicitar (DEV-01, DEV-13).
- [x] ¿Desde cuándo se cuenta el plazo de 15 días? Resuelto el 2026-10-16: desde la entrega de esa línea, en hora de Lima (DEV-15).
- [x] ¿Cómo se estructura una devolución mixta? Resuelto el 2026-10-16: una devolución por tipo, con estado por línea (DEV-01, DEV-10).
- [x] ¿Quién aprueba la devolución de un impreso? Resuelto el 2026-10-16 (confirmado por el responsable): el encargado de la sucursal receptora o la administración (DEV-13).
- [x] ¿Cuándo se reembolsa un impreso devuelto? Resuelto el 2026-10-16: después de confirmar la recepción y el reingreso (DEV-08); el digital, al aprobarse.
- [x] ¿Cuántos días tiene el cliente para llevar el impreso a la sucursal tras aprobarse la devolución? Resuelto el 2026-10-16: 10 días calendario; vencida, pasa a rechazada sin reembolso (DEV-14).
- [x] ¿Cuál es el plazo máximo para devolver un impreso entregado? Resuelto el 2026-10-16: 15 días calendario, alineado con las propuestas de Indecopi; a verificar (DEV-15).
- [x] ¿Cuánto se conserva el texto libre de las devoluciones? Resuelto el 2026-11-13 (decisión de la persona responsable): dos años desde su último cambio a reembolsada o rechazada; luego se redacta (DEV-27).
