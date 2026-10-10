---
id: web-base
titulo: Estructura base de la interfaz web
estado: Draft
servicio: Web
prefijo: WEB
origen: [RC01, RNF13, RF25, RF29, NN15, NN16, NN18]
calidad: [AC05, AC08, AC10, AC13, AC14, RNF05, RNF06, RNF15, RNF16, RNF25, RNF26, RNF27]
depende: [identidad-seguridad, ecommerce-libro-reclamaciones]
---

# Spec — Estructura base de la interfaz web

## 1. Propósito

La base común de `BabelHub.Web`: áreas por rol, sesión, estados de pantalla estándar, accesibilidad y pie legal. Todas las pantallas de las demás specs se apoyan en ella. El diseño y los componentes están en `docs/06-experiencia-de-usuario`.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Áreas Tienda, Sucursal y Administración y su navegación según rol. | El contenido de cada pantalla (se describe en el plan de cada feature). |
| Sesión, protección de formularios, estados de pantalla y errores. | Aplicación móvil nativa. |
| Temas claro y oscuro, accesibilidad, rendimiento percibido, cabeceras de seguridad y formato regional. | Lectura en línea de libros. |

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
| WEB-10 | Ubicuo | El sistema deberá poder usarse completamente con teclado, con foco visible y un enlace para saltar al contenido. | AC10 |
| WEB-11 | Ubicuo | El sistema deberá cumplir un contraste de texto de al menos 4,5 a 1 en ambos temas. | AC10 |
| WEB-12 | Estado | Mientras el usuario prefiere movimiento reducido, el sistema deberá eliminar las animaciones sin perder funcionalidad. | AC10 |
| WEB-13 | Ubicuo | El sistema deberá indicar la disponibilidad y los estados con texto e icono, y nunca solo con color. | AC10 |
| WEB-14 | Ubicuo | El sistema deberá mostrar importes en soles y fechas con formato peruano. | RNF13 |
| WEB-15 | Evento | Cuando el cliente descarga un libro digital, el sistema deberá redirigir el navegador al enlace firmado en lugar de transmitir el archivo. | RNF14 |
| WEB-16 | Ubicuo | El sistema deberá recordar la sucursal preferida del cliente entre sesiones. | RNF13 |
| WEB-17 | Evento | Cuando el visitante pulsa «Sorpréndeme», el sistema deberá elegir al azar un libro disponible y mostrar su ficha. | RF29, NN17 |
| WEB-18 | Ubicuo | El sistema deberá enviar en cada respuesta las cabeceras de seguridad: política de contenido restrictiva, transporte seguro obligatorio, no adivinar el tipo de contenido y política de referencia limitada. | RNF16, AC05 |
| WEB-19 | Ubicuo | El sistema deberá guardar la sesión en una cookie inaccesible a scripts, enviada solo por conexión segura y restringida al mismo sitio. | RNF15, AC05 |
| WEB-20 | Ubicuo | El sistema deberá conformar las pautas WCAG 2.2 nivel AA en inicio, catálogo, detalle, carrito y checkout, verificado con revisión automática y una lista manual de teclado y lector de pantalla. | RNF25, AC10 |
| WEB-21 | Ubicuo | El sistema deberá cargar inicio, catálogo y detalle con LCP de 2,5 s o menos (percentil 75 en 4G simulada), INP de 200 ms o menos y CLS de 0,1 o menos. | RNF26, AC02 |
| WEB-22 | Ubicuo | El sistema deberá permitir que al menos el 90 % de las personas de una prueba de usabilidad complete una compra con recojo sin ayuda, con una puntuación SUS de al menos 68. | RNF27, AC08 |
| WEB-23 | Ubicuo | El sistema deberá completar el recorrido de compra en las dos últimas versiones estables de Chrome, Edge, Firefox y Safari, y en Chrome para Android y Safari para iOS recientes. | AC14, RC14 |
| WEB-24 | No deseado | Si una operación es rechazada por un límite de uso, entonces el sistema deberá explicar el motivo y la espera sugerida sin perder lo que la persona tenía en pantalla. | RNF17, AC13 |

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
| AC-WEB-13 | WEB-17 | **Dado** un catálogo con libros disponibles y agotados, **cuando** el visitante pulsa «Sorpréndeme», **entonces** se muestra la ficha de un libro disponible. |
| AC-WEB-14 | WEB-18, WEB-19 | **Dado** cualquier página, **cuando** se inspeccionan sus respuestas, **entonces** llevan las cuatro cabeceras de seguridad y la cookie de sesión tiene los tres atributos exigidos. |
| AC-WEB-15 | WEB-20 | **Dado** el recorrido de compra, **cuando** se ejecuta la revisión automática de accesibilidad y la lista manual con teclado y lector de pantalla, **entonces** hay 0 incumplimientos críticos de nivel AA. |
| AC-WEB-16 | WEB-21 | **Dado** inicio, catálogo y detalle en 4G simulada, **cuando** se mide, **entonces** LCP, INP y CLS cumplen las metas. |
| AC-WEB-17 | WEB-22 | **Dado** una ronda de cinco personas de prueba, **cuando** compran un libro con recojo sin ayuda, **entonces** al menos 90 % lo logra y el SUS promedio es de 68 o más. |
| AC-WEB-18 | WEB-23 | **Dado** cada navegador soportado, **cuando** se recorre la compra, **entonces** se completa sin errores. |
| AC-WEB-19 | WEB-24 | **Dado** una operación rechazada por límite, **cuando** el usuario la ve, **entonces** lee el motivo y la espera sugerida, y su formulario conserva lo escrito. |

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
