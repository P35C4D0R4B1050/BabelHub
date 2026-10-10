# Glosario

Lenguaje común del dominio. El código, las specs y la interfaz usan estos términos. Si un término nuevo aparece en una spec, se agrega aquí.

## Inventario

| Término | Definición | No confundir con |
| :--- | :--- | :--- |
| **Fuente** | Lugar que guarda stock de libros impresos. Cada sucursal es una fuente. Se registra como dato. | «Sucursal» como unidad comercial: la sucursal es la tienda; la fuente es su rol en el inventario. |
| **Stock físico** | Ejemplares impresos que realmente hay en una fuente. | Stock vendible. |
| **Reserva** | Apartado de unidades para un pedido en curso. Estados: pendiente, confirmada, liberada, expirada. | Venta: una reserva todavía no descuenta el stock físico. |
| **Stock vendible** | *Stock físico − reservas activas*. Nunca es negativo. | Stock físico. |
| **Selección de fuente** | Regla que decide desde qué fuente se atiende cada línea impresa de un pedido. | Elección de sucursal de recojo, que hace el cliente. |
| **Traslado** | Movimiento de ejemplares de una fuente de origen a una de destino. Estados: solicitado, aprobado, en tránsito, recibido, rechazado. | Envío a domicilio. |
| **Ajuste** | Cambio del stock físico de una fuente con motivo y responsable: merma, producto dañado o corrección de conteo. | Traslado. |
| **Merma** | Pérdida de unidades (robo, extravío, desgaste). Solo descuenta. | Producto dañado. |
| **Producto dañado** | Unidades que ya no se pueden vender. Solo descuenta. | Merma. |
| **Corrección de conteo** | Ajuste que se expresa como el conteo físico real; el sistema calcula la diferencia. | Un ajuste con signo libre: no existe. |
| **Alcance de sucursal** | Sucursal a la que un usuario puede acceder; viaja en el token. | Rol. |

## Catálogo

| Término | Definición | No confundir con |
| :--- | :--- | :--- |
| **Obra** | Título abstracto: nombre, autor, editorial, categoría. | Edición. |
| **Edición** | Presentación concreta de una obra, impresa o digital, con su propio ISBN y precio. | Obra. |
| **ISBN** | Identificador de una edición. Es único en todo el catálogo. | Identificador interno. |
| **Libro digital** | Edición en formato EPUB o PDF, sin stock ni sucursal. | Libro impreso. |
| **Titular de derechos** | Autor o editorial que autoriza la venta de una edición digital. | Cliente. |
| **Licencia** | Autorización de venta de un libro digital, con tipo, territorios y vigencia. | Acceso digital. |

## Compra y entrega

| Término | Definición | No confundir con |
| :--- | :--- | :--- |
| **Carrito** | Selección de ediciones de un cliente, que puede mezclar impresas y digitales. Persiste entre sesiones y dispositivos. | Pedido. |
| **Pedido** | Compra confirmada, formada por **líneas**. | Carrito. |
| **Línea** | Una edición con su cantidad dentro de un pedido; tiene su propio cumplimiento. | Pedido. |
| **Recojo** | Entrega de los impresos en una sucursal elegida por el cliente. | Despacho a domicilio. |
| **Despacho a domicilio** | Entrega de los impresos en la dirección del cliente, con guía de envío. | Recojo. |
| **Guía de envío** | Documento del operador logístico que identifica un despacho a domicilio. | Guía de traslado. |
| **Reserva temporal** | Reserva de impresos con vencimiento (15 minutos) desde que se inicia el pago. | Reserva confirmada. |
| **Clave de idempotencia** | Valor que hace que repetir una operación no la duplique (cobros y reservas). | Número de pedido. |

## Biblioteca digital

| Término | Definición | No confundir con |
| :--- | :--- | :--- |
| **Acceso digital** | Derecho de un cliente a descargar una edición digital que compró. Personal e intransferible. | Licencia. |
| **Biblioteca** | Lista de accesos digitales de un cliente («Mi biblioteca»). | Catálogo. |
| **Descarga** | Entrega de un archivo al titular de un acceso, con enlace firmado de corta duración. Hay un máximo de 5 por acceso. | Lectura en línea (fuera de alcance). |
| **Enlace firmado** | URL temporal (5 minutos como máximo) para descargar un archivo del almacén. | URL pública. |

## Seguridad y cumplimiento

| Término | Definición | No confundir con |
| :--- | :--- | :--- |
| **Rol** | Cliente, encargado de sucursal, personal de logística o administrador central. | Alcance de sucursal. |
| **Consentimiento** | Autorización opcional del cliente para recibir avisos por WhatsApp. Revocable. | Aceptación de términos y del tratamiento de datos, que es obligatoria para comprar. |
| **Derechos ARCO** | Acceso, rectificación, cancelación y oposición sobre los datos personales (Ley N.° 29733). | — |
| **Código de un solo uso** | Código corto enviado por correo (verificación y recuperación) o generado por una aplicación (segundo factor), que caduca y no puede reutilizarse. | Contraseña. |
| **Segundo factor (TOTP)** | Código temporal que el administrador central presenta además de su contraseña. | Código de verificación del correo. |
| **Límite de uso** | Tope de solicitudes u operaciones por usuario u origen en un tiempo, con una respuesta única al excederse. | Reserva que expira. |
| **Origen** | Dirección del cliente final que realiza una solicitud, tomada del borde de confianza. | Dirección de un componente interno. |
| **Evento de seguridad** | Registro de un ingreso fallido, bloqueo, acceso denegado, límite excedido, cambio de rol o uso de un código de respaldo. | Log técnico. |
| **Amenaza (AM)** | Escenario de abuso identificado en el modelo de amenazas (`AM01`…). | Riesgo del proyecto. |
| **Control (CTL)** | Verificación de seguridad de la lista basada en ASVS (`CTL-01`…). | Requisito de una spec. |
| **Auditoría** | Registro inmutable de operaciones sensibles. | Log técnico. |
| **Libro de Reclamaciones** | Registro de reclamos y quejas de los consumidores, con **hojas** numeradas. | Auditoría. |

## Método

| Término | Definición |
| :--- | :--- |
| **Spec** | Descripción del comportamiento de una feature en EARS, sin tecnología. |
| **EARS** | *Easy Approach to Requirements Syntax*: plantillas ubicua, de evento, de estado, de comportamiento no deseado y opcional. |
| **Criterio de aceptación** | Escenario *Dado/Cuando/Entonces* que cubre uno o más requisitos. Se identifica como `AC-XXX-NN` y tiene prueba. |
| **Puerto** | Interfaz definida en la Aplicación para algo externo (repositorio, pasarela, notificador). |
| **Adaptador** | Implementación de un puerto en la Infraestructura. |
| **Convergencia** | Estado en que la spec, el código y las pruebas coinciden y la verificación no encuentra diferencias. |
