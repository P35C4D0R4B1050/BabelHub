---
id: transversal-seguridad
titulo: Seguridad transversal de la aplicación
estado: Draft
servicio: Transversal
prefijo: SEG
origen: [RNF16, RNF17, RNF20, RNF21, NN18]
calidad: [AC05, AC13, RNF06]
depende: [transversal-auditoria]
---

# Spec — Seguridad transversal de la aplicación

## 1. Propósito

Definir las protecciones que todos los servicios y la interfaz web comparten, para que ninguna feature las reimplemente a su manera: validación de entradas, errores seguros, límites de uso con una única respuesta, registro y alertas de seguridad, e integraciones autenticadas (NN18). Las reglas propias de una feature (recuperar contraseña, reservas por cliente, descargas, asistente) viven en la spec de esa feature y **reutilizan** lo que aquí se define; ninguna otra spec redefine el formato de rechazo, el registro del evento ni la lista de datos prohibidos en registros.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Validación de entradas y codificación de salidas. | Política de contraseñas, recuperación y segundo factor (`identidad-seguridad`). |
| Formato uniforme de errores y fallo seguro. | Qué cuenta como reserva activa y el límite por cliente (`inventario-seleccion-fuente`). |
| Origen de las solicitudes, límites de uso y la respuesta uniforme cuando se exceden. | Qué es una descarga usada (`ecommerce-biblioteca-digital`). |
| Límites de ingreso, de códigos de un solo uso y de segundo factor. | Cabeceras de seguridad y cookie de sesión (`web-base`). |
| Registro de eventos de seguridad, alertas y datos prohibidos en registros. | Almacenamiento y consulta de los registros (`transversal-auditoria`). |
| Autenticación de integraciones, validación de sus respuestas y tiempos de espera. | Respaldo y recuperación, y escaneo de dependencias (infraestructura y CI). |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| SEG-01 | Ubicuo | El sistema deberá validar en el servidor el tipo, la longitud, el formato y el rango de toda entrada antes de procesarla, y rechazar las que no cumplan. | RNF16, AC05 |
| SEG-02 | Ubicuo | El sistema deberá codificar toda salida hacia la interfaz según su contexto, de modo que el contenido ingresado por una persona nunca se interprete como código. | RNF16, AC05 |
| SEG-03 | Ubicuo | El sistema deberá responder los errores con un formato uniforme que incluya un código estable y un identificador de seguimiento, sin trazas ni datos internos. | RNF20, AC05 |
| SEG-04 | No deseado | Si ocurre un error inesperado durante una operación, entonces el sistema deberá negar la operación y no dejar efectos parciales, salvo la falla de una dependencia no necesaria para completarla (por ejemplo, los avisos por WhatsApp, los correos de alerta y la caché), que no deberá interrumpirla. | RNF20, AC05 |
| SEG-05 | Estado | Mientras un usuario autenticado supera 100 solicitudes en una ventana deslizante de un minuto, el sistema deberá rechazar las siguientes con la respuesta de límite excedido. | RNF17, AC13 |
| SEG-06 | Estado | Mientras una combinación de identificador de cuenta y origen acumula 5 ingresos fallidos en 15 minutos, el sistema deberá rechazar los ingresos de esa combinación durante 15 minutos, incluso con credenciales correctas, sin extender el bloqueo por los intentos hechos durante él, y contar igual los identificadores que no existen. | RNF17, AC13 |
| SEG-07 | Ubicuo | El sistema deberá responder todo límite excedido con una respuesta única que incluya un código estable, el motivo, la espera sugerida en segundos y el instante de nueva disponibilidad, y que nunca revele datos de cuentas. | RNF17, AC13 |
| SEG-08 | Evento | Cuando se excede un límite de uso, el sistema deberá registrar un evento de seguridad por usuario u origen, límite y ventana de 5 minutos, con la fecha y el número de rechazos acumulados en esa ventana. | RNF17, RNF20 |
| SEG-09 | Evento | Cuando en cinco minutos se acumulan en un servicio más de 10 accesos denegados o más de 20 ingresos fallidos, ese servicio deberá generar una alerta de seguridad con su fecha, servicio y motivo, visible en la lista de la administración central y enviada por correo a los administradores centrales activos. | RNF20, AC05 |
| SEG-10 | Ubicuo | El sistema deberá excluir de los registros técnicos, de auditoría y de seguridad y de los avisos por WhatsApp las contraseñas (incluida la temporal del personal), las credenciales y renovaciones (incluidas las del asistente y las claves de firma), los identificadores de sesión web, los códigos de un solo uso y de respaldo, los secretos del segundo factor, los datos de tarjeta y los enlaces firmados de descarga. | RNF20, AC05 |
| SEG-11 | Ubicuo | El sistema deberá leer sus secretos de la configuración del entorno y no incluirlos en el repositorio, los registros ni los mensajes. | RNF06, AC05 |
| SEG-12 | Ubicuo | El sistema deberá autenticar cada llamada entrante de una integración con un secreto o una firma verificable, rechazar y registrar las que no cumplan, y procesar una sola vez las repetidas respondiendo el mismo resultado exitoso. | RNF21, AC05 |
| SEG-13 | Ubicuo | El sistema deberá tratar como no confiable toda respuesta de un servicio externo, validar su formato antes de usarla y descartar las inválidas como si el servicio no hubiera respondido; el caso del pago se rige por `ecommerce-pagos`. | RNF21, AC05 |
| SEG-14 | No deseado | Si una dependencia necesaria para una operación no responde en 10 segundos (30 para el proveedor del modelo de lenguaje), entonces el sistema deberá cortar la espera, informar que el servicio no está disponible y seguir atendiendo las demás operaciones; si la dependencia no es necesaria, deberá continuar sin ella. | RNF21, AC05 |
| SEG-15 | Ubicuo | El sistema deberá exigir que cada llamada entre servicios lleve una credencial verificable, y rechazar las que no la tengan. | RNF21, AC05 |
| SEG-16 | Ubicuo | El sistema deberá responder los errores inesperados y de seguridad con un mensaje genérico, y los errores de validación, de reglas de negocio y de límite excedido con el motivo específico, sin datos internos. | RNF20, AC05 |
| SEG-17 | Ubicuo | El sistema deberá identificar el origen de cada solicitud como la dirección del cliente final entregada por el borde de confianza, ignorar cualquier cabecera de reenvío enviada por el cliente, y no usar la dirección de un componente interno. | RNF17, AC13 |
| SEG-18 | Estado | Mientras un mismo origen sin sesión supera 60 solicitudes en una ventana deslizante de un minuto, el sistema deberá rechazar las siguientes con la respuesta de límite excedido. | RNF17, AC13 |
| SEG-19 | Estado | Mientras un mismo correo destino o un mismo origen supera 3 solicitudes de código de un solo uso de un mismo propósito (verificación o recuperación) por hora, el sistema deberá rechazar las siguientes con la respuesta de límite excedido, exista o no la cuenta. | RNF17, AC13 |
| SEG-20 | Estado | Mientras una combinación de cuenta y origen acumula 5 códigos de segundo factor incorrectos en 15 minutos, el sistema deberá rechazar los siguientes de esa combinación durante 15 minutos. | RNF17, AC13 |
| SEG-21 | Ubicuo | El sistema deberá aplicar los límites por tiempo con una ventana deslizante. | RNF17, AC13 |
| SEG-22 | Evento | Cuando se repite una solicitud con la misma clave de idempotencia, el sistema deberá no contarla para los límites de reservas, descargas, códigos, registros y hojas, y sí para el límite general de solicitudes. | RNF17, AC13 |
| SEG-23 | No deseado | Si no es posible determinar el origen de una solicitud, entonces el sistema deberá registrar un evento de seguridad y contarla en un único «origen desconocido», sujeto a los mismos límites por origen, además de los límites por usuario. | RNF17, AC13 |
| SEG-24 | Estado | Mientras un mismo origen supera 3 cuentas nuevas registradas en una ventana deslizante de una hora, el sistema deberá rechazar los registros siguientes con la respuesta de límite excedido. | RNF17, AC13 |
| SEG-25 | Ubicuo | El sistema deberá guardar el origen de las solicitudes en los contadores de límites solo como una huella calculada con una clave secreta del entorno que no permita recuperar la dirección, eliminarla a las 24 horas, y conservar la dirección completa únicamente en los eventos de seguridad. | RNF17, RNF23 |
| SEG-26 | No deseado | Si no está disponible el almacén de contadores de límites, entonces el sistema deberá rechazar el ingreso, el registro, las solicitudes de código, las reservas y las descargas con la respuesta de servicio no disponible, y aplicar a las demás operaciones un límite local por servicio. | RNF17, RNF21 |
| SEG-27 | Evento | Cuando se repite una alerta de seguridad por el mismo motivo y servicio en 15 minutos, el sistema deberá agruparla en una sola entrada de la lista y en un solo correo. | RNF20 |
| SEG-28 | Ubicuo | El sistema deberá enviar un código de un solo uso únicamente en el correo dirigido a su propio destinatario. | RNF20, AC05 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-SEG-01 | SEG-01 | **Dado** un campo numérico, **cuando** se envía un texto, un valor fuera de rango o una cadena demasiado larga, **entonces** la entrada se rechaza y no se procesa. |
| AC-SEG-02 | SEG-02 | **Dado** un comentario o un nombre con etiquetas de marcado, **cuando** se muestra en una pantalla, **entonces** aparece como texto y no se ejecuta. |
| AC-SEG-03 | SEG-03, SEG-16 | **Dado** una solicitud inválida, una regla de negocio incumplida y un error inesperado en cualquier servicio, **cuando** se reciben los errores, **entonces** todos traen código estable e identificador; los dos primeros indican el motivo, el tercero un mensaje genérico, y ninguno trae trazas ni datos internos. |
| AC-SEG-04 | SEG-04 | **Dado** una operación que falla a la mitad por un error inesperado, **cuando** termina, **entonces** no quedan efectos parciales; y si solo falla el aviso por WhatsApp o el correo de una alerta, la operación se completa. |
| AC-SEG-05 | SEG-05, SEG-21 | **Dado** un usuario con 100 solicitudes en el último minuto, **cuando** envía la siguiente, **entonces** se rechaza con la respuesta de límite excedido; y pasado el tiempo sugerido, vuelve a funcionar. |
| AC-SEG-06 | SEG-06 | **Dado** una cuenta con 5 ingresos fallidos desde un origen, **cuando** ese origen ingresa con la credencial correcta, **entonces** se rechaza durante 15 minutos; y desde otro origen la cuenta sigue accesible. |
| AC-SEG-07 | SEG-06, SEG-07 | **Dado** un correo que no existe y otro que sí, **cuando** cada uno acumula 5 ingresos fallidos, **entonces** ambos reciben exactamente la misma respuesta de límite excedido. |
| AC-SEG-08 | SEG-07, SEG-08 | **Dado** un límite excedido en descargas, en el asistente, en reservas o en el ingreso, **cuando** se rechaza, **entonces** la respuesta tiene el mismo formato y queda un evento de seguridad; y si el mismo origen es rechazado 50 veces en 5 minutos por el mismo límite, queda un solo evento con el contador en 50. |
| AC-SEG-09 | SEG-09 | **Dado** 11 accesos denegados en cinco minutos en Inventario, **cuando** se supera el umbral, **entonces** aparece una alerta de Inventario en la lista de la administración central y llega un correo a los administradores activos sin datos sensibles; y lo mismo ocurre con 21 ingresos fallidos en Identidad; con exactamente 10 accesos denegados o 20 ingresos fallidos no hay alerta. |
| AC-SEG-10 | SEG-10, SEG-28 | **Dado** cualquier flujo con contraseña (incluida la temporal), código de uso único o de respaldo, identificador de sesión, credencial del asistente, tarjeta o enlace firmado, **cuando** se revisan los registros técnicos, de auditoría y de seguridad y los avisos por WhatsApp, **entonces** no aparece ninguno de esos valores; y el único mensaje que lleva un código es el correo a su destinatario. |
| AC-SEG-11 | SEG-12 | **Dado** una llamada entrante de una integración sin firma o con firma inválida, **cuando** llega, **entonces** se rechaza y se registra; y con firma válida se procesa, y si se repite, no se procesa otra vez y se responde el mismo éxito. |
| AC-SEG-12 | SEG-13 | **Dado** una respuesta externa con un formato inesperado, **cuando** se recibe, **entonces** se descarta y la operación se trata como sin respuesta. |
| AC-SEG-13 | SEG-14 | **Dado** una dependencia necesaria que no responde, **cuando** pasan 10 segundos, **entonces** se informa servicio no disponible y las demás operaciones siguen funcionando; y si la dependencia no era necesaria, la operación continúa. |
| AC-SEG-14 | SEG-15 | **Dado** una llamada entre servicios sin credencial, **cuando** llega, **entonces** se rechaza. |
| AC-SEG-15 | SEG-17 | **Dado** una solicitud que llega por el borde con la dirección del cliente final, **cuando** la Web llama a un servicio, **entonces** el servicio aplica los límites con la dirección del cliente y no con la de la Web; y si el cliente envía una cabecera de reenvío con otra dirección, se ignora y se usa la que entregó el borde. |
| AC-SEG-16 | SEG-18 | **Dado** un visitante con 60 solicitudes en el último minuto, **cuando** envía otra, **entonces** se rechaza con la respuesta de límite excedido. |
| AC-SEG-17 | SEG-19 | **Dado** un correo con 3 solicitudes de código en la última hora, **cuando** se pide otra, **entonces** se rechaza con la misma respuesta exista o no la cuenta. |
| AC-SEG-18 | SEG-20 | **Dado** una cuenta con 5 códigos de segundo factor incorrectos desde un origen, **cuando** presenta uno correcto desde ese origen, **entonces** se rechaza durante 15 minutos; y desde otro origen el titular puede ingresar. |
| AC-SEG-19 | SEG-22 | **Dado** una reserva repetida con la misma clave de idempotencia, **cuando** se envía otra vez, **entonces** no cuenta para el límite de reservas pero sí para el límite general de solicitudes. |
| AC-SEG-20 | SEG-23 | **Dado** una solicitud sin origen determinable, **cuando** llega, **entonces** queda un evento de seguridad y se cuenta en el «origen desconocido» con los mismos límites por origen, además de los límites por usuario. |
| AC-SEG-21 | SEG-24 | **Dado** un origen que registró 3 cuentas en la última hora, **cuando** intenta registrar una cuarta, **entonces** se rechaza con la respuesta de límite excedido y queda un evento de seguridad; y desde otro origen el registro funciona. |
| AC-SEG-22 | SEG-25 | **Dado** solicitudes de un origen, **cuando** se inspeccionan los contadores de límites, **entonces** solo hay una huella del origen, calculada con una clave secreta del entorno (sin la clave no se puede recuperar la dirección), que desaparece a las 24 horas; y los eventos de seguridad conservan la dirección completa. |
| AC-SEG-23 | SEG-27 | **Dado** una alerta de Inventario ya enviada, **cuando** se repite el mismo motivo dentro de 15 minutos, **entonces** la lista muestra una sola entrada con el contador y no sale otro correo; y pasados los 15 minutos, una nueva entrada y un correo. |
| AC-SEG-24 | SEG-26 | **Dado** que el almacén de contadores no está disponible, **cuando** alguien intenta ingresar o registrarse, **entonces** recibe servicio no disponible; y consultar el catálogo sigue funcionando con el límite local. |
| AC-SEG-25 | SEG-11 | **Dado** el repositorio y los registros, **cuando** se revisan, **entonces** no contienen ningún secreto ni clave. |

## 5. Reglas e invariantes

- Ante la duda, se niega: un error o una dependencia caída nunca abre un acceso.
- Los límites son por usuario y por origen; la respuesta tiene siempre la misma forma y no revela información de cuentas.
- Un mensaje de error nunca contiene trazas, rutas internas, nombres de tablas ni credenciales.
- Los eventos de seguridad se registran en el contrato de `transversal-auditoria`; esta spec define cuándo se registran, no cómo se almacenan.
- Los umbrales numéricos son metas iniciales y se validan con pruebas de carga.
- Las otras specs solo **citan** SEG-07 y SEG-08 para el rechazo y el registro de sus propios límites; no los redefinen.

## 6. Casos límite y errores

- Un origen compartido (por ejemplo, una red universitaria) puede alcanzar el límite por origen entre varias personas; el mensaje lo explica de forma neutral. Por eso el bloqueo de ingreso es por combinación de cuenta y origen y no por origen.
- Si el registro de un evento de seguridad falla, el rechazo se mantiene y se genera una alerta técnica (`transversal-auditoria`).
- Una alerta repetida por el mismo motivo en 15 minutos se agrupa (SEG-27).
- **Riesgo aceptado:** los límites por origen (SEG-19, SEG-24 y el de hojas de `ecommerce-libro-reclamaciones`) pueden bloquear a varias personas de una red compartida, como una red universitaria; el mensaje explica el motivo y la hora de reintento sin acusar. Los umbrales son parámetros configurables.
- Los accesos denegados se registran uno por uno (cuentan para el umbral de SEG-09); solo los límites excedidos se agrupan por ventana.
- Si el correo de una alerta no se puede enviar, la alerta sigue visible en la lista y el fallo no interrumpe ninguna operación (SEG-04).
- **Riesgo aceptado:** como cada servicio cuenta sus propios eventos, un ataque repartido entre servicios por debajo del umbral de cada uno puede no generar alerta; se mitiga con los límites de uso (SEG-05 y SEG-18) y la revisión de la lista unificada.
- Un atacante que bloquea la combinación de la cuenta y su propio origen no impide que el titular ingrese desde otro origen.
- **Riesgo aceptado:** un ataque distribuido desde muchos orígenes puede probar 5 contraseñas por origen cada 15 minutos. Se mitiga con la política de contraseñas, la alerta de SEG-09 y el segundo factor del personal.

## 7. Preguntas abiertas

- [x] ¿Confirmas 3 cuentas nuevas por hora por origen (SEG-24)? Resuelto el 2026-10-16 (confirmado por el responsable): sí, como meta inicial.
- [x] ¿La alerta de seguridad necesita además un aviso fuera del panel? Resuelto el 2026-10-16: sí, un correo a los administradores centrales activos, agrupado como la alerta (SEG-09, AUD-12).
- [x] ¿Dónde se evalúa el umbral de alertas? Resuelto el 2026-10-16: en cada servicio; la capa web presenta las alertas juntas (SEG-09, `transversal-auditoria` AUD-13).
- Nota para el plan: qué cabecera concreta del borde se considera confiable para obtener el origen (configuración de Cloudflare y Nginx); el comportamiento de ignorar las cabeceras del cliente ya está exigido por SEG-17.
- [x] ¿Confirmas 3 solicitudes de código por hora (SEG-19), el bloqueo de 15 minutos tras 5 códigos de segundo factor incorrectos (SEG-20) y los límites de 60 y 100 solicitudes por minuto? Resuelto el 2026-10-16 (confirmado por el responsable): sí, como metas iniciales a validar con pruebas de carga.
