---
id: web-base
titulo: Estructura base de la interfaz web
estado: Draft
servicio: Web
prefijo: WEB
origen: [RC01, RNF13, RF25, RF29, NN15, NN16, NN18]
calidad: [AC05, AC08, AC10, AC13, AC14, RNF05, RNF06, RNF15, RNF16, RNF25, RNF26, RNF27]
depende: [identidad-seguridad, ecommerce-libro-reclamaciones, ecommerce-catalogo, ecommerce-carrito-checkout, ecommerce-biblioteca-digital, transversal-auditoria, transversal-seguridad]
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
| WEB-15 | Evento | Cuando el cliente pulsa descargar un libro digital, el sistema deberá pedir la descarga al E-commerce y redirigir el navegador al enlace firmado recibido, sin transmitir el archivo. | RNF14 |
| WEB-16 | Ubicuo | El sistema deberá recordar la sucursal preferida del cliente entre sesiones. | RNF13 |
| WEB-17 | Evento | Cuando el visitante pulsa «Sorpréndeme», el sistema deberá elegir al azar un libro disponible y mostrar su ficha. | RF29, NN17 |
| WEB-18 | Ubicuo | El sistema deberá enviar en cada respuesta las cabeceras de seguridad: política de contenido restrictiva, transporte seguro obligatorio, no adivinar el tipo de contenido y política de referencia limitada, y permitir en la política de contenido el script de la pasarela de pago solo en la página de pago. | RNF16, AC05 |
| WEB-19 | Ubicuo | El sistema deberá guardar la sesión en una cookie inaccesible a scripts, enviada solo por conexión segura y restringida al mismo sitio con la política que permite volver de la pasarela de pago sin perder la sesión. | RNF15, AC05 |
| WEB-20 | Ubicuo | El sistema deberá conformar las pautas WCAG 2.2 nivel AA en inicio, catálogo, detalle, carrito, checkout, registro, ingreso, segundo factor, recuperación y cuenta, permitiendo pegar contraseñas y autocompletar códigos, verificado con revisión automática y una lista manual de teclado y lector de pantalla. | RNF25, AC10 |
| WEB-21 | Ubicuo | El sistema deberá cargar inicio, catálogo y detalle con LCP de 2,5 s o menos (percentil 75 en 4G simulada), INP de 200 ms o menos y CLS de 0,1 o menos. | RNF26, AC02 |
| WEB-22 | Ubicuo | El sistema deberá permitir que al menos el 90 % de las personas de una prueba de usabilidad complete una compra con recojo sin ayuda, con una puntuación SUS de al menos 68 y menos de un error por tarea en promedio, medidos en rondas de 5 personas que sumen al menos 10. | RNF27, AC08 |
| WEB-23 | Ubicuo | El sistema deberá completar el recorrido de compra en las dos últimas versiones estables de Chrome, Edge, Firefox y Safari, y en Chrome para Android y Safari para iOS recientes. | AC14, RC14 |
| WEB-24 | No deseado | Si una operación es rechazada por un límite de uso, entonces el sistema deberá explicar el motivo y la espera sugerida sin perder lo que la persona tenía en pantalla y sin distinguir, en el ingreso, si la cuenta existe. | RNF17, AC13 |
| WEB-25 | Evento | Cuando la sesión vence a mitad de una acción, el sistema deberá llevar al ingreso y conservar en el servidor, ligado a ese usuario y por un máximo de 30 minutos, lo que tenía en pantalla, salvo contraseñas y códigos, sin mostrarlo a otra persona. | RNF13, RNF15 |
| WEB-26 | No deseado | Si la Web no logra comunicarse con los servicios, entonces el sistema deberá mostrar el estado de servicio no disponible y conservar el carrito. | RNF13, AC03 |
| WEB-27 | Estado | Mientras una persona no realiza ninguna acción iniciada por ella durante 30 minutos, el sistema deberá cerrar su sesión web; las consultas automáticas no renuevan ese plazo. | RNF15, AC05 |
| WEB-28 | Evento | Cuando una persona ingresa, cierra sesión o su sesión vence, el sistema deberá emitir un identificador de sesión nuevo al ingresar e invalidar en el servidor la sesión que se cierra o vence y su renovación. | RNF15, AC05 |
| WEB-29 | Ubicuo | El sistema deberá publicar los términos, la política de privacidad y la política de reembolso como textos con versión y fecha de vigencia visibles, incluir en la privacidad las cookies que usa el sitio, y pedir la aceptación de la versión vigente que informa Identidad. | NN15, RNF23 |
| WEB-30 | Ubicuo | El sistema deberá ofrecer a la administración central una pantalla de auditoría y eventos de seguridad con filtros por acción, usuario u origen, fechas y texto, y el aviso del servicio que no respondió. | RNF20, AC05 |
| WEB-31 | Estado | Mientras una sesión web lleva 7 días de un cliente o 8 horas del personal desde el ingreso, el sistema deberá cerrarla aunque haya actividad. | RNF15, AC05 |
| WEB-32 | No deseado | Si Identidad rechaza la renovación de una sesión (usuario desactivado, renovaciones revocadas, cambio de rol o clave comprometida), entonces el sistema deberá cerrar la sesión web y llevar a la persona al ingreso conservando lo que tenía en pantalla según WEB-25. | RNF15, AC05 |
| WEB-33 | Ubicuo | El sistema deberá ofrecer a la administración central una pantalla de alertas de seguridad con su estado de revisión y la acción de marcarlas como revisadas. | RNF20, AC05 |
| WEB-34 | Estado | Mientras una persona tiene pendiente configurar el segundo factor, cambiar una contraseña temporal, aceptar una versión nueva de los textos legales o declarar su mayoría de edad, el sistema deberá llevarla a ese paso antes de cualquier otra pantalla. | RNF15, NN15 |
| WEB-35 | Ubicuo | El sistema deberá ofrecer las pantallas de registro, ingreso, verificación y recuperación, cuenta (datos, consentimientos, segundo factor, solicitudes y cancelación con cierre de todas las sesiones), usuarios y roles, y solicitudes sobre datos personales con sus fechas límite. | RNF13, NN15 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-WEB-01 | WEB-01 | **Dado** un celular de 375 píxeles de ancho, **cuando** se abre el catálogo, **entonces** no hay desplazamiento horizontal y todos los controles son utilizables. |
| AC-WEB-02 | WEB-02, WEB-03 | **Dado** un encargado de sucursal, **cuando** entra, **entonces** su menú no muestra Administración; y si abre esa dirección, ve la pantalla de sin permiso. |
| AC-WEB-03 | WEB-04, WEB-05 | **Dado** un usuario con sesión, **cuando** se inspecciona el navegador, **entonces** no hay credenciales en el almacenamiento accesible por scripts y los formularios llevan protección contra falsificación. |
| AC-WEB-04 | WEB-06, WEB-07 | **Dado** un servicio que falla, **cuando** el usuario abre una pantalla de datos, **entonces** ve el estado de error con un botón de reintento y sin detalles técnicos. |
| AC-WEB-05 | WEB-08 | **Dado** cualquier página de la tienda, **cuando** se muestra, **entonces** el pie tiene el enlace al Libro de Reclamaciones y los enlaces a términos, privacidad y política de reembolso. |
| AC-WEB-06 | WEB-09 | **Dado** un dispositivo con preferencia de tema oscuro y sin elección previa, **cuando** se abre la tienda, **entonces** se muestra el tema oscuro, y la elección posterior se recuerda. |
| AC-WEB-07 | WEB-10, WEB-11 | **Dado** cualquier pantalla, **cuando** se navega solo con teclado, **entonces** todos los controles reciben foco visible y el texto cumple el contraste en ambos temas. |
| AC-WEB-08 | WEB-12 | **Dado** una preferencia de movimiento reducido, **cuando** se usa la interfaz, **entonces** no hay animaciones y todo funciona. |
| AC-WEB-09 | WEB-13 | **Dado** una edición agotada, **cuando** se muestra, **entonces** la etiqueta tiene texto e icono además del color. |
| AC-WEB-10 | WEB-14 | **Dado** un importe de 1234,5, **cuando** se muestra, **entonces** aparece en soles con el formato peruano. |
| AC-WEB-11 | WEB-15 | **Dado** un titular con descargas disponibles, **cuando** pulsa descargar, **entonces** el navegador es redirigido al enlace firmado y la capa web no recibe el archivo. |
| AC-WEB-12 | WEB-16 | **Dado** un cliente que eligió Ayacucho, **cuando** vuelve otro día, **entonces** su sucursal sigue siendo Ayacucho. |
| AC-WEB-13 | WEB-17 | **Dado** un catálogo con libros disponibles y agotados, **cuando** el visitante pulsa «Sorpréndeme», **entonces** se muestra la ficha de un libro disponible. |
| AC-WEB-14 | WEB-18, WEB-19 | **Dado** cualquier página, **cuando** se inspeccionan sus respuestas, **entonces** llevan las cuatro cabeceras de seguridad y la cookie de sesión tiene los tres atributos exigidos; solo la página de pago permite el script de la pasarela; y al volver de la pasarela de pago el cliente conserva su sesión. |
| AC-WEB-15 | WEB-20 | **Dado** el recorrido de compra, **cuando** se ejecuta la revisión automática de accesibilidad y la lista manual con teclado y lector de pantalla, **entonces** hay 0 incumplimientos críticos de nivel AA. |
| AC-WEB-16 | WEB-21 | **Dado** inicio, catálogo y detalle en 4G simulada, **cuando** se mide, **entonces** LCP, INP y CLS cumplen las metas. |
| AC-WEB-17 | WEB-22 | **Dado** dos rondas de cinco personas de prueba, **cuando** compran un libro con recojo sin ayuda, **entonces** al menos 9 de 10 lo logran, el SUS promedio es de 68 o más y el promedio de errores por tarea es menor que uno. |
| AC-WEB-18 | WEB-23 | **Dado** cada navegador soportado, **cuando** se recorre la compra, **entonces** se completa sin errores. |
| AC-WEB-19 | WEB-24 | **Dado** una operación rechazada por límite, **cuando** el usuario la ve, **entonces** lee el motivo y la espera sugerida, y su formulario conserva lo escrito; y en el ingreso el mensaje es igual para cuentas existentes e inexistentes. |
| AC-WEB-20 | WEB-25 | **Dado** un usuario con un formulario a medio llenar, **cuando** su sesión vence y envía, **entonces** es llevado al ingreso y, al volver, conserva lo escrito salvo contraseñas y códigos; y si otra persona ingresa en el mismo equipo, no ve lo escrito. |
| AC-WEB-21 | WEB-26 | **Dado** que los servicios no responden, **cuando** el cliente abre su carrito, **entonces** ve el estado de servicio no disponible y el carrito no se pierde. |
| AC-WEB-22 | WEB-27, WEB-31 | **Dado** un cliente y un encargado con sesión, **cuando** pasan 31 minutos sin acciones, **entonces** ambos deben ingresar de nuevo, y las consultas automáticas de la pantalla no renuevan el plazo; **cuando** el cliente actúa cada 20 minutos, **entonces** su sesión sigue hasta los 7 días y se cierra al cumplirse; y el encargado es llevado al ingreso a las 8 horas aunque haya actividad. |
| AC-WEB-23 | WEB-28 | **Dado** una sesión cerrada o vencida, **cuando** alguien reutiliza su identificador, **entonces** se rechaza, la otra sesión del mismo usuario sigue activa; y al ingresar de nuevo el identificador es distinto del anterior. |
| AC-WEB-24 | WEB-29 | **Dado** el pie de la tienda, **cuando** el cliente abre los términos, la privacidad y el reembolso, **entonces** cada uno muestra su versión y fecha de vigencia, y la aceptación en el registro o el checkout guarda esa versión. |
| AC-WEB-25 | WEB-30 | **Dado** un administrador central, **cuando** abre la auditoría, **entonces** puede filtrar por acción, usuario u origen, fechas y texto, y si un servicio no respondió ve el aviso; y un encargado que abre esa pantalla ve la de sin permiso. |
| AC-WEB-26 | WEB-32 | **Dado** un encargado desactivado mientras tiene la sesión abierta, **cuando** la Web intenta renovar su credencial y Identidad la rechaza, **entonces** la sesión se cierra y es llevado al ingreso. |
| AC-WEB-27 | WEB-33 | **Dado** una alerta de seguridad, **cuando** el administrador abre la pantalla de alertas, **entonces** la ve con su estado y puede marcarla como revisada. |
| AC-WEB-28 | WEB-34 | **Dado** un encargado que no tiene el segundo factor configurado, o un cliente con una versión nueva de los términos por aceptar, **cuando** intenta abrir cualquier otra pantalla, **entonces** es llevado primero a ese paso. |
| AC-WEB-29 | WEB-35 | **Dado** el sitio completo, **cuando** se recorren las áreas, **entonces** existen las pantallas de registro, ingreso, verificación, recuperación, cuenta, usuarios y roles, y solicitudes sobre datos personales con fechas límite. |

## 5. Reglas e invariantes

- La Web no contiene reglas de negocio ni referencia a los servicios más que por sus contratos.
- La credencial del usuario vive solo en el servidor.
- Toda pantalla de datos cubre los cinco estados estándar.
- Los colores salen de los tokens del sistema de diseño; no hay colores sueltos.

## 6. Casos límite y errores

- Una sesión que vence a mitad de una acción redirige al ingreso y conserva lo que el usuario tenía.
- La sesión vive en el servidor y la cookie solo lleva un identificador opaco; si se pierde el almacenamiento de sesiones, todas las personas deben ingresar de nuevo y el carrito se conserva (CAR-01).
- El plazo de 30 minutos de inactividad es mayor que los 15 minutos de reserva del checkout, por lo que una reserva vigente no queda sin sesión.
- Una pantalla sin conexión muestra el estado de servicio no disponible y no pierde el carrito.
- El asistente de compra no tapa precios ni acciones en pantallas pequeñas.

## 7. Preguntas abiertas

- [x] ¿Cuánto dura la sesión web? Resuelto el 2026-10-16: 30 minutos de inactividad renovables con cada interacción y tope absoluto de 7 días (clientes) u 8 horas (personal) (WEB-27).
- [x] ¿Las fuentes tipográficas se autoalojan desde el inicio? Resuelto el 2026-10-16 (confirmado por el responsable): sí, para no depender de servidores externos.
