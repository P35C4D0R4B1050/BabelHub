---
id: ecommerce-biblioteca-digital
titulo: Biblioteca y descarga de libros digitales
estado: Draft
servicio: E-commerce
prefijo: BIB
origen: [RF22, RF23, RF24, NN13, NN15]
calidad: [AC09, RNF14, RNF05, RNF08]
depende: [ecommerce-catalogo, ecommerce-pagos, transversal-auditoria]
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
| BIB-04 | Evento | Cuando el titular de un acceso con descargas restantes pide descargar, el sistema deberá emitir un enlace firmado que expire en un máximo de cinco minutos. | RF23, AC09 |
| BIB-05 | Ubicuo | El sistema deberá contabilizar una descarga únicamente cuando el enlace emitido se usa. | RF23 |
| BIB-06 | No deseado | Si el acceso ya alcanzó el máximo de cinco descargas, entonces el sistema deberá rechazar la solicitud explicando el límite y cómo pedir ayuda. | RF23, AC09 |
| BIB-07 | No deseado | Si se usa un enlace vencido o que pertenece a otra cuenta, entonces el sistema deberá rechazar la descarga y registrar el intento. | AC09, RNF14 |
| BIB-08 | Evento | Cuando vence el enlace sin usarse, el sistema deberá permitir emitir uno nuevo sin consumir una descarga. | RF23 |
| BIB-09 | Estado | Mientras la licencia de una edición esté vencida o retirada de la venta, el sistema deberá mantener el acceso de quienes ya la compraron. | RF24 |
| BIB-10 | Estado | Mientras un acceso esté revocado por un reembolso o una anulación, el sistema deberá impedir su descarga. | RF23 |
| BIB-11 | Evento | Cuando se emite o se usa un enlace de descarga, el sistema deberá registrar el usuario, la edición y la fecha. | RF23, RNF08 |
| BIB-12 | Ubicuo | El sistema deberá mostrar la biblioteca de un cliente solo a ese cliente y al personal autorizado. | RNF05, NN15 |
| BIB-13 | No deseado | Si el archivo de una edición no está disponible en el almacén, entonces el sistema deberá informar el problema al cliente, no consumir una descarga y ofrecer reportarlo. | RF23 |
| BIB-14 | Ubicuo | El sistema deberá entregar el archivo directamente desde el almacén al cliente, sin que la capa web lo reciba ni lo reenvíe. | RNF14 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-BIB-01 | BIB-01 | **Dado** un pedido con un libro digital, **cuando** se aprueba el pago, **entonces** el acceso aparece en la cuenta del comprador, sin reserva de stock ni entrega. |
| AC-BIB-02 | BIB-02 | **Dado** un cliente con 4 libros digitales, **cuando** abre su biblioteca, **entonces** ve cada uno con formato, fecha de compra y «2 de 5» descargas usadas cuando corresponda. |
| AC-BIB-03 | BIB-03 | **Dado** un acceso del cliente A, **cuando** el cliente B pide descargarlo, **entonces** se rechaza y se registra el intento. |
| AC-BIB-04 | BIB-04, BIB-05, BIB-11 | **Dado** un acceso con 2 descargas usadas, **cuando** el titular pide descargar y usa el enlace, **entonces** el enlace vence en cinco minutos como máximo, las descargas pasan a 3 y queda un registro. |
| AC-BIB-05 | BIB-06 | **Dado** un acceso con 5 descargas usadas, **cuando** el titular pide descargar, **entonces** se rechaza explicando el límite. |
| AC-BIB-06 | BIB-07 | **Dado** un enlace emitido hace diez minutos, **cuando** se usa, **entonces** se rechaza como vencido y se registra el intento. |
| AC-BIB-07 | BIB-08 | **Dado** un enlace vencido sin usar, **cuando** el titular pide uno nuevo, **entonces** se emite sin consumir una descarga. |
| AC-BIB-08 | BIB-09 | **Dado** una edición cuya licencia venció después de la compra, **cuando** el cliente abre su biblioteca, **entonces** sigue pudiendo descargarla. |
| AC-BIB-09 | BIB-10 | **Dado** un acceso revocado por un reembolso, **cuando** el cliente intenta descargar, **entonces** se rechaza. |
| AC-BIB-10 | BIB-12 | **Dado** la biblioteca de un cliente, **cuando** otro cliente intenta verla, **entonces** se rechaza; y un administrador autorizado sí puede consultarla. |
| AC-BIB-11 | BIB-13 | **Dado** un acceso cuyo archivo falta en el almacén, **cuando** el titular pide descargar, **entonces** se le informa del problema, no se consume una descarga y se le ofrece reportarlo. |
| AC-BIB-12 | BIB-14 | **Dado** una descarga, **cuando** el navegador la inicia, **entonces** el archivo viene directamente del almacén y ninguna otra capa del sistema lo recibe. |

## 5. Reglas e invariantes

- Un acceso es personal e intransferible, y se asocia a una cuenta y una edición.
- Máximo de 5 descargas por acceso; un enlace dura 5 minutos como máximo.
- Una descarga se cuenta cuando se usa el enlace, no cuando se emite.
- El archivo vive solo en el almacén de objetos; nunca en el repositorio, la base de datos ni las VMs.
- Un acceso concedido se mantiene aunque la licencia venza, salvo reembolso, anulación u orden legal.

## 6. Casos límite y errores

- Dos solicitudes simultáneas con una sola descarga restante: solo una obtiene el enlace.
- Un cliente que pierde el archivo con el límite alcanzado usa el canal de ayuda y la administración decide.
- Un mensaje de aviso por WhatsApp nunca incluye el enlace de descarga (`transversal-notificaciones`).

## 7. Preguntas abiertas

- [ ] ¿Cómo se cuenta la descarga cuando se usa el enlace? Propuesta: un punto de entrada propio que cuenta y redirige al almacén con una dirección de vida muy corta. Se decide en el plan.
- [ ] ¿La marca de agua con los datos de la compra entra en esta versión? Propuesta: no, queda como mejora posterior (AD20). Responde: responsable del proyecto.
