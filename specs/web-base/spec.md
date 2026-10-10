---
id: web-base
titulo: Estructura base de la interfaz web
estado: Draft
servicio: Web
prefijo: WEB
origen: [RC01, RNF13, RF25, NN15]
calidad: [AC05, AC08, RNF05, RNF06]
depende: [identidad-seguridad, ecommerce-libro-reclamaciones]
---

# Spec — Estructura base de la interfaz web

## 1. Propósito

La base común de `BabelHub.Web`: áreas por rol, sesión, estados de pantalla estándar, accesibilidad y pie legal. Todas las pantallas de las demás specs se apoyan en ella. El diseño y los componentes están en `docs/08`.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Áreas Tienda, Sucursal y Administración y su navegación según rol. | El contenido de cada pantalla (se describe en el plan de cada feature). |
| Sesión, protección de formularios, estados de pantalla y errores. | Aplicación móvil nativa. |
| Temas claro y oscuro, accesibilidad y formato regional. | Lectura en línea de libros. |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| WEB-01 | Ubicuo | El sistema deberá presentar una interfaz web responsiva que funcione en celulares y en computadoras. | RC01, AC08 |
| WEB-02 | Ubicuo | El sistema deberá organizar la interfaz en las áreas Tienda, Sucursal y Administración, y mostrar en cada una solo las opciones permitidas al rol y al alcance del usuario. | RNF05 |
| WEB-03 | No deseado | Si un usuario abre una pantalla que su rol no permite, entonces el sistema deberá mostrar una pantalla de sin permiso con salidas a lo que sí puede usar. | RNF05, AC05 |
| WEB-04 | Ubicuo | El sistema deberá mantener la sesión sin exponer la credencial del usuario al navegador. | RNF06, AC05 |
| WEB-05 | Ubicuo | El sistema deberá proteger los formularios contra solicitudes falsificadas entre sitios. | AC05 |
| WEB-06 | Ubicuo | El sistema deberá mostrar en cada pantalla de datos los estados cargando, vacío, error, sin permiso y servicio no disponible, con un mismo patrón visual. | RNF13 |
| WEB-07 | No deseado | Si un servicio devuelve un error, entonces el sistema deberá mostrar un mensaje comprensible con una acción de reintento y no mostrar detalles técnicos. | RNF13 |
| WEB-08 | Ubicuo | El sistema deberá mostrar en el pie de todas las páginas de la tienda el enlace al Libro de Reclamaciones y los enlaces a términos, privacidad y política de reembolso. | RF25, NN15 |
| WEB-09 | Ubicuo | El sistema deberá ofrecer un tema claro y uno oscuro, respetar la preferencia del dispositivo y recordar la elección del usuario. | RNF13 |
| WEB-10 | Ubicuo | El sistema deberá poder usarse completamente con teclado, con foco visible y un enlace para saltar al contenido. | AC08 |
| WEB-11 | Ubicuo | El sistema deberá cumplir un contraste de texto de al menos 4,5 a 1 en ambos temas. | AC08 |
| WEB-12 | Estado | Mientras el usuario prefiere movimiento reducido, el sistema deberá eliminar las animaciones sin perder funcionalidad. | AC08 |
| WEB-13 | Ubicuo | El sistema deberá indicar la disponibilidad y los estados con texto e icono, y nunca solo con color. | AC08 |
| WEB-14 | Ubicuo | El sistema deberá mostrar importes en soles y fechas con formato peruano. | RNF13 |
| WEB-15 | Evento | Cuando el cliente descarga un libro digital, el sistema deberá redirigir el navegador al enlace firmado en lugar de transmitir el archivo. | RNF14 |
| WEB-16 | Ubicuo | El sistema deberá recordar la sucursal preferida del cliente entre sesiones. | RNF13 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-WEB-01 | WEB-01 | **Dado** un celular de 375 píxeles de ancho, **cuando** se abre el catálogo, **entonces** no hay desplazamiento horizontal y todos los controles son utilizables. |
| AC-WEB-02 | WEB-02, WEB-03 | **Dado** un encargado de sucursal, **cuando** entra, **entonces** su menú no muestra Administración; y si abre esa dirección, ve la pantalla de sin permiso. |
| AC-WEB-03 | WEB-04, WEB-05 | **Dado** un usuario con sesión, **cuando** se inspecciona el navegador, **entonces** no hay credenciales en el almacenamiento accesible por scripts y los formularios llevan protección contra falsificación. |
| AC-WEB-04 | WEB-06, WEB-07 | **Dado** un servicio que falla, **cuando** el usuario abre una pantalla de datos, **entonces** ve el estado de error con un botón de reintento y sin detalles técnicos. |
| AC-WEB-05 | WEB-08 | **Dado** cualquier página de la tienda, **cuando** se muestra, **entonces** el pie tiene los cinco enlaces legales y el del Libro de Reclamaciones. |
| AC-WEB-06 | WEB-09 | **Dado** un dispositivo con preferencia de tema oscuro y sin elección previa, **cuando** se abre la tienda, **entonces** se muestra el tema oscuro, y la elección posterior se recuerda. |
| AC-WEB-07 | WEB-10, WEB-11 | **Dado** cualquier pantalla, **cuando** se navega solo con teclado, **entonces** todos los controles reciben foco visible y el texto cumple el contraste en ambos temas. |
| AC-WEB-08 | WEB-12 | **Dado** una preferencia de movimiento reducido, **cuando** se usa la interfaz, **entonces** no hay animaciones y todo funciona. |
| AC-WEB-09 | WEB-13 | **Dado** una edición agotada, **cuando** se muestra, **entonces** la etiqueta tiene texto e icono además del color. |
| AC-WEB-10 | WEB-14 | **Dado** un importe de 1234,5, **cuando** se muestra, **entonces** aparece en soles con el formato peruano. |
| AC-WEB-11 | WEB-15 | **Dado** un titular con descargas disponibles, **cuando** pulsa descargar, **entonces** el navegador es redirigido al enlace firmado y la capa web no recibe el archivo. |
| AC-WEB-12 | WEB-16 | **Dado** un cliente que eligió Ayacucho, **cuando** vuelve otro día, **entonces** su sucursal sigue siendo Ayacucho. |

## 5. Reglas e invariantes

- La Web no contiene reglas de negocio ni referencia a los servicios más que por sus contratos.
- La credencial del usuario vive solo en el servidor.
- Toda pantalla de datos cubre los cinco estados estándar.
- Los colores salen de los tokens del sistema de diseño; no hay colores sueltos.

## 6. Casos límite y errores

- Una sesión que vence a mitad de una acción redirige al ingreso y conserva lo que el usuario tenía.
- Una pantalla sin conexión muestra el estado de servicio no disponible y no pierde el carrito.
- El asistente de compra no tapa precios ni acciones en pantallas pequeñas.

## 7. Preguntas abiertas

- [ ] ¿Las fuentes tipográficas se autoalojan desde el inicio? Propuesta: sí, para no depender de servidores externos. Se decide en el plan.
