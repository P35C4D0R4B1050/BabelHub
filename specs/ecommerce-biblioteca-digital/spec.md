---
id: ecommerce-biblioteca-digital
titulo: Biblioteca y descarga de libros digitales
estado: Draft
servicio: E-commerce
prefijo: BIB
origen: [RF22, RF23, RF24, NN13, NN15, NN18]
calidad: [AC09, AC13, RNF14, RNF05, RNF08, RNF17]
depende: [ecommerce-catalogo, ecommerce-pagos, transversal-auditoria, transversal-seguridad]
---

# Spec — Biblioteca y descarga de libros digitales

## 1. Propósito

Entregar al instante los libros digitales comprados y protegerlos: el archivo solo le llega a quien lo compró, con enlaces de corta duración y un máximo de descargas. Sin pasar por el inventario (NN13, AC09).

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
| BIB-04 | Evento | Cuando el titular de un acceso con descargas restantes pide descargar, el sistema deberá verificar su titularidad y su cupo y entregarle un enlace firmado que expire en un máximo de cinco minutos. | RF23, AC09 |
| BIB-05 | Ubicuo | El sistema deberá contabilizar una descarga en el momento de entregar el enlace firmado, aunque la descarga se interrumpa, y permitir reutilizar el mismo enlace dentro de su vigencia sin contar otra descarga. | RF23 |
| BIB-06 | No deseado | Si el acceso ya alcanzó el máximo de cinco descargas, entonces el sistema deberá rechazar la solicitud explicando el límite y ofreciendo pedir ayuda. | RF23, AC09 |
| BIB-07 | No deseado | Si quien pide descargar no es el titular del acceso, entonces el sistema deberá rechazar la solicitud y registrar el intento. | AC09, RNF14 |
| BIB-08 | Evento | Cuando la administración central restituye descargas a un acceso, el sistema deberá sumarlas al cupo y registrar la acción en la auditoría con el motivo. | RF23, RNF08 |
| BIB-09 | Estado | Mientras la licencia de una edición esté vencida o retirada de la venta, el sistema deberá mantener el acceso de quienes ya la compraron. | RF24 |
| BIB-10 | Estado | Mientras un acceso esté revocado por un reembolso o una anulación, o suspendido por un reporte de archivo defectuoso, el sistema deberá impedir su descarga. | RF23 |
| BIB-11 | Evento | Cuando se entrega un enlace de descarga, el sistema deberá registrar el usuario, la edición y la fecha. | RF23, RNF08 |
| BIB-12 | Ubicuo | El sistema deberá mostrar la biblioteca de un cliente solo a ese cliente y al administrador central, y registrar en la auditoría cada consulta del administrador. | RNF05, NN15, AC11 |
| BIB-13 | No deseado | Si el archivo de una edición no está disponible en el almacén, entonces el sistema deberá informar el problema al cliente, no consumir una descarga y ofrecer reportarlo. | RF23 |
| BIB-14 | Ubicuo | El sistema deberá entregar el archivo directamente desde el almacén al cliente, sin que la capa web lo reciba ni lo reenvíe. | RNF14 |
| BIB-15 | No deseado | Si un cliente solicita más de 10 enlaces de descarga en una ventana deslizante de una hora, entonces el sistema deberá rechazar la solicitud sin consumir descargas y responder y registrar el evento según `transversal-seguridad` (SEG-07 y SEG-08). | RNF17, AC13 |
| BIB-16 | Evento | Cuando el titular pide ayuda sobre un acceso, el sistema deberá registrar la solicitud para la administración con el acceso y el motivo. | RF23 |
| BIB-17 | Ubicuo | El sistema deberá comprobar y contar el cupo de descargas de forma atómica ante solicitudes simultáneas del mismo acceso. | RF23, AC09 |
| BIB-18 | No deseado | Si se usa un enlace de descarga cuya vigencia ya venció, entonces el sistema deberá no completar la descarga e invitar a pedir un enlace nuevo. | AC09, RNF14 |
| BIB-19 | No deseado | Si se repite la aprobación del pago de una línea digital, entonces el sistema deberá no conceder un segundo acceso. | RF22, AC01 |
| BIB-20 | Evento | Cuando un cliente vuelve a comprar un libro digital cuyo acceso fue revocado, el sistema deberá reactivar ese acceso y reiniciar su contador de descargas; si el acceso sigue vigente, deberá rechazar la compra repetida según `ecommerce-carrito-checkout`. | RF22 |
| BIB-21 | Evento | Cuando la administración resuelve un reporte de archivo defectuoso, el sistema deberá reanudar el acceso si se rechaza el reporte, o dejarlo revocado si se aprueba el reembolso, y registrarlo en la auditoría. | RF23, RNF08 |
| BIB-22 | Ubicuo | El sistema deberá informar a `ecommerce-devoluciones` si a un acceso se le entregó algún enlace de descarga, con independencia de que se haya usado. | RF23, NN15 |

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
| AC-BIB-19 | BIB-22 | **Dado** un acceso con un enlace entregado y no usado, **cuando** devoluciones pregunta, **entonces** se informa que sí se entregó un enlace; y con ninguno entregado, que no. |

## 5. Reglas e invariantes

- Un acceso es personal e intransferible, y se asocia a una cuenta y una edición.
- Máximo de 5 descargas por acceso; un enlace dura 5 minutos como máximo.
- Una descarga se cuenta cuando el sistema entrega el enlace firmado, no cuando el navegador termina de recibir el archivo.
- El archivo vive solo en el almacén de objetos; nunca en el repositorio, la base de datos ni las VMs.
- Un acceso concedido se mantiene aunque la licencia venza, salvo reembolso, anulación u orden legal.

## 6. Casos límite y errores

- Dos solicitudes simultáneas con una sola descarga restante: solo una obtiene el enlace.
- Un cliente que pierde el archivo con el límite alcanzado pide ayuda (BIB-16) y la administración decide si restituye descargas (BIB-08).
- Una descarga interrumpida puede reintentarse con el mismo enlace mientras no venza; pasada su vigencia, pide un enlace nuevo, que cuenta, y la ayuda de BIB-16 cubre los casos restantes.
- **Riesgo aceptado:** quien reciba un enlace de otra persona puede descargar durante sus cinco minutos de vigencia; se limita con la vigencia corta, el cupo y el registro de cada descarga.
- Un mensaje de aviso por WhatsApp nunca incluye el enlace de descarga (`transversal-notificaciones`).

## 7. Preguntas abiertas

- [x] ¿Cómo se cuenta la descarga? Resuelto el 2026-10-10: un punto de entrada propio verifica y cuenta, y redirige al almacén con un enlace de vida muy corta (BIB-04, BIB-05); el archivo no pasa por la Web (BIB-14).
- [x] ¿La marca de agua con los datos de la compra entra en esta versión? Resuelto el 2026-10-16 (confirmado por el responsable): no, queda como mejora posterior (AD20).
