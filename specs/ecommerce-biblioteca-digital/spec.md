---
id: ecommerce-biblioteca-digital
titulo: Biblioteca y descarga de libros digitales
estado: Planned
aprobado_por: Claude (delegado por Diego Amiquero)
aprobado_en: 2026-11-13
plan_aprobado_por: Claude (delegado por Diego Amiquero)
plan_aprobado_en: 2026-11-18
servicio: E-commerce
prefijo: BIB
origen: [RF22, RF23, RF24, RF27, NN13, NN15, NN18]
calidad: [AC04, RNF14, RNF05, RNF08, RNF17, RNF23]
depende: [ecommerce-catalogo, ecommerce-carrito-checkout, ecommerce-pagos, identidad-seguridad, transversal-auditoria, transversal-seguridad]
---

# Spec — Biblioteca y descarga de libros digitales

## 1. Propósito

Entregar al instante los libros digitales comprados y protegerlos: el archivo solo le llega a quien lo compró, con enlaces de corta duración y un máximo de descargas. Sin pasar por el inventario (NN13, AC04).

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Concesión del acceso al aprobarse el pago. | La compra (`ecommerce-carrito-checkout`, `ecommerce-pagos`). |
| Biblioteca personal, descarga con enlace firmado y tope de descargas. | Lectura en línea, DRM comercial y marca de agua (mejora posterior). |
| Registro de descargas y rechazo de accesos indebidos. | Reembolso y revocación (`ecommerce-devoluciones`). |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| BIB-01 | Evento | Cuando se aprueba el pago de una línea digital, el sistema deberá conceder un acceso a la cuenta del comprador sin reservar stock ni pedir entrega. | RF22 |
| BIB-02 | Evento | Cuando el cliente abre su biblioteca, el sistema deberá listar sus accesos con formato, fecha de compra y descargas usadas y restantes. | RF22 |
| BIB-03 | Ubicuo | El sistema deberá permitir la descarga de un libro digital únicamente al titular de su acceso. | RF23, RNF14 |
| BIB-04 | Evento | Cuando el titular de un acceso con descargas restantes pide descargar, el sistema deberá verificar su titularidad y su cupo y entregarle un enlace firmado que expire en un máximo de cinco minutos. | RF23, AC04 |
| BIB-05 | Ubicuo | El sistema deberá contabilizar una descarga en el momento de entregar el enlace firmado, aunque la descarga se interrumpa, y permitir reutilizar el mismo enlace dentro de su vigencia sin contar otra descarga. | RF23 |
| BIB-06 | No deseado | Si el acceso ya alcanzó el máximo de cinco descargas, entonces el sistema deberá rechazar la solicitud explicando el límite y ofreciendo pedir ayuda. | RF23, AC04 |
| BIB-07 | No deseado | Si quien pide descargar no es el titular del acceso, entonces el sistema deberá rechazar la solicitud y registrar el intento. | AC04, RNF14 |
| BIB-08 | Evento | Cuando la administración central restituye descargas a un acceso, el sistema deberá sumarlas al cupo y registrar la acción en la auditoría con el motivo. | RF23, RNF08 |
| BIB-09 | Estado | Mientras la licencia de una edición esté vencida o retirada de la venta, el sistema deberá mantener el acceso de quienes ya la compraron. | RF24 |
| BIB-10 | Estado | Mientras un acceso esté revocado por un reembolso o una anulación, o suspendido por un reporte de archivo defectuoso, el sistema deberá impedir su descarga. | RF23 |
| BIB-11 | Evento | Cuando se entrega un enlace de descarga, el sistema deberá registrar el acceso, el usuario, la edición y la fecha. | RF23, RNF08 |
| BIB-12 | Ubicuo | El sistema deberá mostrar la biblioteca de un cliente solo a ese cliente y al administrador central, y registrar en la auditoría cada consulta del administrador. | RNF05, NN15, AC04 |
| BIB-13 | No deseado | Si el archivo de una edición no está disponible en el almacén, entonces el sistema deberá informar el problema al cliente, no consumir una descarga y ofrecer reportarlo. | RF23 |
| BIB-14 | Ubicuo | El sistema deberá entregar el archivo directamente desde el almacén al cliente, sin que la capa web lo reciba ni lo reenvíe. | RNF14 |
| BIB-15 | No deseado | Si un cliente solicita más de 10 enlaces de descarga en una ventana deslizante de una hora, entonces el sistema deberá rechazar la solicitud sin consumir descargas y responder y registrar el evento según `transversal-seguridad` (SEG-07 y SEG-08). | RNF17, AC04 |
| BIB-16 | Evento | Cuando el titular pide ayuda sobre un acceso, el sistema deberá registrar la solicitud para la administración con el acceso, el motivo y la fecha. | RF23 |
| BIB-17 | Ubicuo | El sistema deberá comprobar y contar el cupo de descargas de forma atómica ante solicitudes simultáneas del mismo acceso. | RF23, AC04 |
| BIB-18 | No deseado | Si se usa un enlace de descarga cuya vigencia ya venció, entonces el sistema deberá no completar la descarga e invitar a pedir un enlace nuevo. | AC04, RNF14 |
| BIB-19 | No deseado | Si se repite la aprobación del pago de una línea digital, entonces el sistema deberá no conceder un segundo acceso. | RF22, AC01 |
| BIB-20 | Evento | Cuando un cliente vuelve a comprar un libro digital cuyo acceso fue revocado, el sistema deberá reactivar ese acceso y reiniciar su contador de descargas; si el acceso sigue vigente, deberá rechazar la compra repetida según `ecommerce-carrito-checkout`. | RF22 |
| BIB-21 | Evento | Cuando la administración resuelve un reporte de archivo defectuoso, el sistema deberá reanudar el acceso si se rechaza el reporte, o dejarlo revocado si se aprueba el reembolso, y registrarlo en la auditoría. | RF23, RNF08 |
| BIB-22 | Ubicuo | El sistema deberá informar a `ecommerce-devoluciones` si a un acceso se le entregó algún enlace de descarga desde su concesión o su última reactivación, con independencia de que se haya usado. | RF23, NN15 |
| BIB-23 | No deseado | Si la entrega de un enlace de descarga y la revocación o aprobación automática del reembolso de un mismo acceso ocurren a la vez, entonces el sistema deberá aplicar solo la primera que confirma y rechazar la otra. | RF23, AC01 |
| BIB-24 | Evento | Cuando una anulación, un reembolso o una decisión de la administración revoca un acceso, el sistema deberá dejarlo revocado una sola vez aunque se repita la orden, y registrar quién lo revocó y el motivo en la auditoría. | RF23, RNF08 |
| BIB-25 | Evento | Cuando se ejecuta la cancelación de una cuenta, el sistema deberá eliminar sus accesos, su biblioteca, sus solicitudes de ayuda y su registro de descargas. | RF22, RF27, RNF23 |
| BIB-26 | Evento | Cuando un registro de descarga (BIB-11) cumple dos años desde su fecha, el sistema deberá quitar de ese registro la referencia a su acceso y a su usuario, sin tocar el acceso, conservar solo la edición y el día, y registrar la depuración en la auditoría sin datos personales. | RF23, RNF23 |
| BIB-27 | No deseado | Si no puede eliminar algo de lo que pide BIB-25, entonces el sistema deberá informarlo para que no se ejecute la cancelación, sin dejar efectos parciales, de modo que pueda reintentarse (IDE-70). | RF27, RNF23 |
| BIB-28 | No deseado | Si una solicitud de enlace llega mientras se ejecuta la cancelación de la cuenta, entonces el sistema deberá rechazarla y no escribir ningún registro con el usuario si el acceso ya no existe. | RF23, RF27, AC01 |
| BIB-29 | Evento | Cuando una solicitud de ayuda (BIB-16) cumple dos años desde su fecha, cualquiera sea su estado, el sistema deberá eliminarla y registrar la depuración en la auditoría sin datos personales. | RF23, RNF23 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-BIB-01 | BIB-01 | **Dado** un pedido con un libro digital, **cuando** se aprueba el pago, **entonces** el acceso aparece en la cuenta del comprador, sin reserva de stock ni entrega. |
| AC-BIB-02 | BIB-02 | **Dado** un cliente con 4 libros digitales, **cuando** abre su biblioteca, **entonces** ve cada uno con formato, fecha de compra y «2 de 5» descargas usadas cuando corresponda. |
| AC-BIB-03 | BIB-03 | **Dado** un acceso del cliente A, **cuando** el cliente B pide descargarlo, **entonces** se rechaza y se registra el intento. |
| AC-BIB-04 | BIB-04, BIB-05, BIB-11 | **Dado** un acceso con 2 descargas usadas, **cuando** el titular pide descargar, **entonces** recibe un enlace que vence en cinco minutos como máximo, las descargas pasan a 3 aunque no lo use y queda un registro. |
| AC-BIB-05 | BIB-06 | **Dado** un acceso con 5 descargas usadas, **cuando** el titular pide descargar, **entonces** se rechaza explicando el límite. |
| AC-BIB-06 | BIB-07, BIB-18 | **Dado** un enlace emitido hace diez minutos, **cuando** se usa, **entonces** la descarga no se completa; y **dado** un cliente que no es titular, **cuando** pide descargar, **entonces** se rechaza y se registra el intento. |
| AC-BIB-07 | BIB-08 | **Dado** un acceso con 5 descargas usadas por una descarga fallida, **cuando** la administración restituye 1 con su motivo, **entonces** el titular puede descargar de nuevo y queda una entrada de auditoría. |
| AC-BIB-08 | BIB-09 | **Dado** una edición cuya licencia venció después de la compra, **cuando** el cliente abre su biblioteca, **entonces** sigue pudiendo descargarla. |
| AC-BIB-09 | BIB-10 | **Dado** un acceso revocado por un reembolso, **cuando** el cliente intenta descargar, **entonces** se rechaza. |
| AC-BIB-10 | BIB-12 | **Dado** la biblioteca de un cliente, **cuando** otro cliente intenta verla, **entonces** se rechaza y se registra el intento; y cuando un administrador central la consulta, puede verla y queda una entrada de auditoría. |
| AC-BIB-11 | BIB-13 | **Dado** un acceso cuyo archivo falta en el almacén, **cuando** el titular pide descargar, **entonces** se le informa del problema, no se consume una descarga y se le ofrece reportarlo. |
| AC-BIB-12 | BIB-14 | **Dado** una descarga, **cuando** el navegador la inicia, **entonces** el archivo viene directamente del almacén y ninguna otra capa del sistema lo recibe. |
| AC-BIB-13 | BIB-15 | **Dado** un cliente que ya pidió 10 enlaces en la última hora, **cuando** pide otro, **entonces** se rechaza con la respuesta de límite excedido, no se consume una descarga y queda un evento de seguridad. |
| AC-BIB-14 | BIB-16 | **Dado** un acceso sin descargas restantes, **cuando** el titular pide ayuda, **entonces** la solicitud queda visible para la administración con el acceso y el motivo. |
| AC-BIB-15 | BIB-17 | **Dado** un acceso con una sola descarga restante, **cuando** se envían dos solicitudes simultáneas, **entonces** solo una recibe el enlace y las descargas quedan en el máximo. |
| AC-BIB-16 | BIB-10, BIB-21 | **Dado** un acceso suspendido por un reporte de archivo defectuoso, **cuando** el titular intenta descargar, **entonces** se rechaza; y si la administración rechaza el reporte, el acceso se reanuda, y si aprueba el reembolso, queda revocado, con auditoría. |
| AC-BIB-17 | BIB-19 | **Dado** una aprobación de pago que llega dos veces, **cuando** se procesan ambas, **entonces** existe un solo acceso. |
| AC-BIB-18 | BIB-20 | **Dado** un acceso revocado por reembolso con 4 descargas usadas, **cuando** el cliente vuelve a comprar el libro, **entonces** el acceso se reactiva con 0 descargas usadas. |
| AC-BIB-19 | BIB-22 | **Dado** un acceso con un enlace entregado y no usado, **cuando** devoluciones pregunta, **entonces** se informa que sí se entregó un enlace; con ninguno entregado, que no; y tras una reactivación por recompra, solo cuentan los enlaces posteriores. |
| AC-BIB-20 | BIB-23 | **Dado** un acceso sin enlaces, **cuando** el titular pide descargar a la vez que se aprueba su reembolso automático, **entonces** solo uno gana: o recibe el enlace y el reembolso se rechaza, o queda revocado y la descarga se rechaza. |
| AC-BIB-21 | BIB-24 | **Dado** un acceso revocado por una anulación, **cuando** llega otra orden de revocación, **entonces** sigue revocado una sola vez y la auditoría conserva el primer responsable y motivo. |
| AC-BIB-22 | BIB-25 | **Dado** un cliente con dos accesos (uno revocado por un reembolso), descargas registradas y una solicitud de ayuda, **cuando** se ejecuta la cancelación de su cuenta, **entonces** sus accesos, su biblioteca, su solicitud de ayuda y su registro de descargas dejan de existir; la cancelación de una cuenta sin accesos termina sin error; y quien se registre luego con el mismo correo obtiene una biblioteca vacía y sin registros. |
| AC-BIB-23 | BIB-26 | **Dado** un registro de descarga de hace 2 años y un día y otro de hace 2 años menos un día, **cuando** corre la depuración, **entonces** el primero queda sin referencia a su acceso ni a su usuario, solo con su edición y su día, y con una entrada de auditoría sin datos personales, el acceso del cliente sigue existiendo y utilizable, el segundo no cambia, y repetir la depuración no cambia nada. |
| AC-BIB-24 | BIB-29 | **Dado** una solicitud de ayuda abierta de hace 2 años y un día y otra cerrada de hace 2 años menos un día, **cuando** corre la depuración, **entonces** la primera se elimina con una entrada de auditoría sin datos personales y la segunda no cambia. |
| AC-BIB-25 | BIB-27 | **Dado** una cancelación en la que falla uno de los pasos (por ejemplo, no se puede eliminar el registro de descargas), **cuando** se ejecuta, **entonces** se informa para que no se ejecute, los accesos siguen vigentes, la biblioteca, las ayudas y el registro siguen intactos y se puede reintentar. |
| AC-BIB-26 | BIB-28 | **Dado** una solicitud de enlace que llega a la vez que se ejecuta la cancelación de la cuenta, **cuando** ambas se procesan, **entonces** o el enlace se entregó antes y su registro se elimina con la cancelación, o la solicitud se rechaza porque el acceso ya no existe; nunca queda un registro con el usuario. |

## 5. Reglas e invariantes

- Un acceso es personal e intransferible, y se asocia a una cuenta y una edición.
- Máximo de 5 descargas por acceso; un enlace dura 5 minutos como máximo.
- Una descarga se cuenta cuando el sistema entrega el enlace firmado, no cuando el navegador termina de recibir el archivo.
- El archivo vive solo en el almacén de objetos; nunca en el repositorio, la base de datos ni las VMs.
- Un acceso concedido se mantiene aunque la licencia venza, salvo reembolso, anulación, orden legal o cancelación de la cuenta (BIB-25, que lo elimina y no lo revoca).
- El sistema no guarda enlaces firmados de descarga (SEG-10).
- Biblioteca implementa los puertos de cancelación que define `identidad-seguridad` (IDE-47) sin que Identidad dependa de ella; el registro de descargas y las ayudas son tablas propias de Biblioteca, separadas de la auditoría.

## 6. Casos límite y errores

- Dos solicitudes simultáneas con una sola descarga restante: solo una obtiene el enlace.
- Un cliente que pierde el archivo con el límite alcanzado pide ayuda (BIB-16) y la administración decide si restituye descargas (BIB-08).
- Una descarga interrumpida puede reintentarse con el mismo enlace mientras no venza; pasada su vigencia, pide un enlace nuevo, que cuenta, y la ayuda de BIB-16 cubre los casos restantes.
- **Riesgo aceptado:** comprar, descargar, obtener un reembolso por defecto y recomprar reinicia el contador; la aprobación de un defecto por la administración y la auditoría son el freno.
- Si el archivo falta en el almacén (BIB-13), «reportarlo» registra una ayuda (BIB-16); como no se entregó enlace, el cliente puede además pedir el reembolso automático dentro de su plazo (`ecommerce-devoluciones`).
- **Riesgo aceptado:** quien reciba un enlace de otra persona puede descargar durante sus cinco minutos de vigencia; se limita con la vigencia corta, el cupo y el registro de cada descarga.
- Un mensaje de aviso por WhatsApp nunca incluye el enlace de descarga (`transversal-notificaciones`).
- **Riesgo aceptado:** un enlace ya entregado sigue sirviendo hasta cinco minutos después de la cancelación porque el almacén no lo revoca; el sistema no guarda enlaces firmados (SEG-10).
- Tras la cancelación, Devoluciones no consulta accesos de esa cuenta: una cuenta con devoluciones abiertas no se cancela (IDE-27) y los datos del pedido los trata `ecommerce-devoluciones`.

## 7. Preguntas abiertas

- [x] ¿Cómo se cuenta la descarga? Resuelto el 2026-10-10: un punto de entrada propio verifica y cuenta, y redirige al almacén con un enlace de vida muy corta (BIB-04, BIB-05); el archivo no pasa por la Web (BIB-14).
- [x] ¿La marca de agua con los datos de la compra entra en esta versión? Resuelto el 2026-10-16 (confirmado por el responsable): no, queda como mejora posterior (AD20).
- [x] ¿Cuánto se conservan el registro de descargas y las ayudas? Resuelto el 2026-11-13 (decisión de la persona responsable): dos años desde su fecha; luego el registro queda solo con la edición y el día y las ayudas se eliminan (BIB-26, BIB-29).
