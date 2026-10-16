# Actores y permisos

Este documento amplía los actores de la [necesidad del negocio](01-necesidad-del-negocio.md) con lo que cada uno puede hacer y hasta dónde llega. Es la base del control de acceso (RNF05) y de los casos de abuso del [modelo de amenazas](../05-seguridad/01-modelo-de-amenazas.md).

## 1. Roles del sistema

| Rol | Cómo se identifica | Alcance | Segundo factor |
| :--- | :--- | :--- | :---: |
| **Visitante** | Sin sesión. | Solo lectura pública y el Libro de Reclamaciones. | No |
| **Cliente** | Cuenta propia, mayor de 18 años y con correo verificado (el número de WhatsApp es opcional). | Sus propios datos, carrito, pedidos y biblioteca. | Opcional |
| **Encargado de sucursal** | Cuenta creada por la administración, con exactamente una sucursal; su contraseña la restablece el administrador. | Solo su sucursal. | **Sí (TOTP)** |
| **Personal de logística** | Cuenta creada por la administración, sin sucursal. | Traslados y envíos de toda la red, sin ajustar stock. | **Sí (TOTP)** |
| **Administrador central** | Cuenta creada por la administración. | Toda la red; gestiona catálogo, usuarios y auditoría. | **Sí (TOTP)** |

Soporte (ayudas del cliente) lo cubre hoy el administrador central. Un rol de soporte separado se evaluará si la operación crece.

## 2. Matriz de permisos por capacidad

**Convenciones:** ● permitido; ◐ permitido solo sobre lo propio (sus datos o su sucursal); — no permitido; ? pendiente de decisión en la spec. El alcance se comprueba en la Aplicación de cada servicio y no solo en el controlador (AGENTS.md, sección 5).

| Capacidad | Visitante | Cliente | Encargado | Logística | Admin. central | Spec |
| :--- | :---: | :---: | :---: | :---: | :---: | :--- |
| Buscar libros y ver disponibilidad | ● | ● | ● | ● | ● | `ecommerce-catalogo` |
| Gestionar catálogo y licencias digitales | — | — | — | — | ● | `ecommerce-catalogo` |
| Armar carrito y pagar | — | ◐ | — | — | — | `ecommerce-carrito-checkout`, `ecommerce-pagos` |
| Ver y seguir pedidos | — | ◐ | ◐ (recojo de su sucursal) | ◐ (envíos) | ● | `ecommerce-pedidos`, `ecommerce-envios` |
| Preparar y entregar pedidos de recojo | — | — | ◐ | — | — | `ecommerce-pedidos` |
| Cancelar un pedido de impresos antes de su preparación | — | ◐ | — | — | ● (anula) | `ecommerce-pedidos` |
| Solicitar una devolución | — | ◐ | — | — | — | `ecommerce-devoluciones` |
| Aprobar y reembolsar devoluciones | — | — | ◐ (impresos de su sucursal receptora) | — | ● | `ecommerce-devoluciones` |
| Mi biblioteca y descarga de libros digitales | — | ◐ | — | — | ◐ (consulta) | `ecommerce-biblioteca-digital` |
| Registrar una hoja del Libro de Reclamaciones | ● | ● | ● | ● | ● | `ecommerce-libro-reclamaciones` |
| Responder hojas de reclamaciones | — | — | — | — | ● | `ecommerce-libro-reclamaciones` |
| Ver stock de una fuente | — | — | ◐ | — | ● | `inventario-fuentes` |
| Registrar fuentes (sucursales) | — | — | — | — | ● | `inventario-fuentes` |
| Gestionar envíos y guías | — | — | — | ● | ● | `ecommerce-envios` |
| Ajustar inventario | — | — | ◐ | — | ● | `inventario-ajustes` |
| Solicitar un traslado | — | — | ◐ | — | ● | `inventario-traslados` |
| Aprobar, rechazar y despachar traslados | — | — | ◐ (solo origen) | — | ● | `inventario-traslados` |
| Recibir traslados | — | — | ◐ (solo destino) | — | ● | `inventario-traslados` |
| Seguir traslados | — | — | ◐ | ● | ● | `inventario-traslados` |
| Usar el asistente de compra | ◐ (solo catálogo) | ● | ● | ● | ● | `asistente-compra-ia` |
| Gestionar usuarios, roles y sucursales | — | — | — | — | ● | `identidad-seguridad` |
| Gestionar mi cuenta, consentimientos y solicitudes ARCO | — | ◐ | — | — | — | `identidad-seguridad` |
| Atender solicitudes ARCO y restablecer contraseñas del personal | — | — | — | — | ● | `identidad-seguridad` |
| Consultar la auditoría | — | — | ◐ (historial de su sucursal) | — | ● | `transversal-auditoria` |
| Consultar la biblioteca de un cliente | — | — | — | — | ● (auditado) | `ecommerce-biblioteca-digital` |
| Recibir alertas de seguridad | — | — | — | — | ● | `transversal-seguridad` |

## 3. Identidades que no son personas

| Identidad | Para qué | Cómo se protege |
| :--- | :--- | :--- |
| **Servicio a servicio** | Llamadas del E-commerce al Inventario y del Asistente a los servicios. | Credencial de servicio verificable; mínimo privilegio (AD22). |
| **Integraciones entrantes** | Avisos de la pasarela de pago y del notificador. | Secreto o firma verificada (RNF21). |
| **Asistente de compra** | Consulta datos para responder. | Solo lectura, sin acceso a las bases de datos (AD28). |

## 4. Actor malicioso

No es un rol: es un modelo para pensar en abusos. Se describe en el [modelo de amenazas](../05-seguridad/01-modelo-de-amenazas.md) con los casos más probables: acaparar stock con reservas, adivinar contraseñas, descargar sin comprar, manipular al asistente y escalar privilegios entre sucursales.

## 5. Reglas

1. Un encargado de sucursal tiene exactamente una sucursal; los demás roles, ninguna.
2. El administrador central no puede modificar ni borrar registros de auditoría.
3. Ningún rol ve datos de pago: solo los conoce la pasarela.
4. Cambiar el rol de un usuario invalida sus credenciales vigentes en la siguiente renovación.
