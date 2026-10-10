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
| SEG-04 | No deseado | Si ocurre un error inesperado durante una operación, entonces el sistema deberá negar la operación y no dejar efectos parciales, salvo la falla de una dependencia no necesaria para completarla (por ejemplo, los avisos por WhatsApp y la caché), que no deberá interrumpirla. | RNF20, AC05 |
| SEG-05 | Estado | Mientras un usuario autenticado supera 100 solicitudes en una ventana deslizante de un minuto, el sistema deberá rechazar las siguientes con la respuesta de límite excedido. | RNF17, AC13 |
| SEG-06 | Estado | Mientras una combinación de identificador de cuenta y origen acumula 5 ingresos fallidos en 15 minutos, el sistema deberá rechazar los ingresos de esa combinación durante 15 minutos, incluso con credenciales correctas, y contar igual los identificadores que no existen. | RNF17, AC13 |
| SEG-07 | Ubicuo | El sistema deberá responder todo límite excedido con una respuesta única que incluya un código estable, el motivo, la espera sugerida en segundos y el instante de nueva disponibilidad, y que nunca revele datos de cuentas. | RNF17, AC13 |
| SEG-08 | Evento | Cuando se excede un límite de uso, el sistema deberá registrar un evento de seguridad con el usuario u origen, el límite y la fecha. | RNF17, RNF20 |
| SEG-09 | Evento | Cuando en cinco minutos se acumulan en el sistema más de 10 accesos denegados o más de 20 ingresos fallidos, el sistema deberá generar una alerta de seguridad con su fecha y motivo, visible en una lista para la administración central. | RNF20, AC05 |
| SEG-10 | Ubicuo | El sistema deberá excluir de los registros técnicos, de auditoría y de seguridad y de los avisos por WhatsApp las contraseñas, las credenciales y renovaciones, los códigos de un solo uso, los secretos del segundo factor, los datos de tarjeta y los enlaces firmados de descarga; un código de un solo uso solo podrá viajar en el correo dirigido a su propio destinatario. | RNF20, AC05 |
| SEG-11 | Ubicuo | El sistema deberá leer sus secretos de la configuración del entorno y no incluirlos en el repositorio, los registros ni los mensajes. | RNF06, AC05 |
| SEG-12 | Ubicuo | El sistema deberá autenticar cada llamada entrante de una integración con un secreto o una firma verificable, rechazar y registrar las que no cumplan, y procesar una sola vez las repetidas respondiendo el mismo resultado exitoso. | RNF21, AC05 |
| SEG-13 | Ubicuo | El sistema deberá tratar como no confiable toda respuesta de un servicio externo, validar su formato antes de usarla y descartar las inválidas como si el servicio no hubiera respondido; el caso del pago se rige por `ecommerce-pagos`. | RNF21, AC05 |
| SEG-14 | No deseado | Si una dependencia necesaria para una operación no responde en 10 segundos (30 para el proveedor del modelo de lenguaje), entonces el sistema deberá cortar la espera, informar que el servicio no está disponible y seguir atendiendo las demás operaciones; si la dependencia no es necesaria, deberá continuar sin ella. | RNF21, AC05 |
| SEG-15 | Ubicuo | El sistema deberá exigir que cada llamada entre servicios lleve una credencial verificable, y rechazar las que no la tengan. | RNF21, AC05 |
| SEG-16 | Ubicuo | El sistema deberá responder los errores inesperados y de seguridad con un mensaje genérico, y los errores de validación, de reglas de negocio y de límite excedido con el motivo específico, sin datos internos. | RNF20, AC05 |
| SEG-17 | Ubicuo | El sistema deberá identificar el origen de cada solicitud como la dirección del cliente final entregada por el borde de confianza, y no como la dirección de un componente interno. | RNF17, AC13 |
| SEG-18 | Estado | Mientras un mismo origen sin sesión supera 60 solicitudes en una ventana deslizante de un minuto, el sistema deberá rechazar las siguientes con la respuesta de límite excedido. | RNF17, AC13 |
| SEG-19 | Estado | Mientras un mismo correo destino o un mismo origen supera 3 solicitudes de código de un solo uso de un mismo propósito (verificación o recuperación) por hora, el sistema deberá rechazar las siguientes con la respuesta de límite excedido, exista o no la cuenta. | RNF17, AC13 |
| SEG-20 | Estado | Mientras una cuenta acumula 5 códigos de segundo factor incorrectos en 15 minutos, el sistema deberá rechazar los siguientes durante 15 minutos. | RNF17, AC13 |
| SEG-21 | Ubicuo | El sistema deberá aplicar los límites por tiempo con una ventana deslizante. | RNF17, AC13 |
| SEG-23 | No deseado | Si no es posible determinar el origen de una solicitud, entonces el sistema deberá registrar un evento de seguridad y aplicar solo los límites por usuario. | RNF17, AC13 |
| SEG-22 | Evento | Cuando se repite una solicitud con la misma clave de idempotencia, el sistema deberá no contarla para los límites de negocio y sí para el límite general de solicitudes. | RNF17, AC13 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-SEG-01 | SEG-01 | **Dado** un campo numérico, **cuando** se envía un texto, un valor fuera de rango o una cadena demasiado larga, **entonces** la entrada se rechaza y no se procesa. |
| AC-SEG-02 | SEG-02 | **Dado** un comentario o un nombre con etiquetas de marcado, **cuando** se muestra en una pantalla, **entonces** aparece como texto y no se ejecuta. |
| AC-SEG-03 | SEG-03, SEG-16 | **Dado** una solicitud inválida, una regla de negocio incumplida y un error inesperado en cualquier servicio, **cuando** se reciben los errores, **entonces** todos traen código estable e identificador; los dos primeros indican el motivo, el tercero un mensaje genérico, y ninguno trae trazas ni datos internos. |
| AC-SEG-04 | SEG-04 | **Dado** una operación que falla a la mitad por un error inesperado, **cuando** termina, **entonces** no quedan efectos parciales; y si solo falla el aviso por WhatsApp, la operación se completa. |
| AC-SEG-05 | SEG-05, SEG-21 | **Dado** un usuario con 100 solicitudes en el último minuto, **cuando** envía la siguiente, **entonces** se rechaza con la respuesta de límite excedido; y pasado el tiempo sugerido, vuelve a funcionar. |
| AC-SEG-06 | SEG-06 | **Dado** una cuenta con 5 ingresos fallidos desde un origen, **cuando** ese origen ingresa con la credencial correcta, **entonces** se rechaza durante 15 minutos; y desde otro origen la cuenta sigue accesible. |
| AC-SEG-07 | SEG-06, SEG-07 | **Dado** un correo que no existe y otro que sí, **cuando** cada uno acumula 5 ingresos fallidos, **entonces** ambos reciben exactamente la misma respuesta de límite excedido. |
| AC-SEG-08 | SEG-07, SEG-08 | **Dado** un límite excedido en descargas, en el asistente, en reservas o en el ingreso, **cuando** se rechaza, **entonces** la respuesta tiene el mismo formato y queda un evento de seguridad. |
| AC-SEG-09 | SEG-09 | **Dado** 11 accesos denegados en cinco minutos, **cuando** se supera el umbral, **entonces** aparece una alerta en la lista de la administración central; y lo mismo ocurre con 21 ingresos fallidos. |
| AC-SEG-10 | SEG-10, SEG-11 | **Dado** cualquier flujo con contraseña, código, tarjeta o enlace firmado, **cuando** se revisan los registros, los avisos por WhatsApp y el repositorio, **entonces** no aparece ninguno de esos valores ni un secreto; y el único mensaje que lleva un código es el correo a su destinatario. |
| AC-SEG-11 | SEG-12 | **Dado** una llamada entrante de una integración sin firma o con firma inválida, **cuando** llega, **entonces** se rechaza y se registra; y con firma válida se procesa, y si se repite, no se procesa otra vez y se responde el mismo éxito. |
| AC-SEG-12 | SEG-13 | **Dado** una respuesta externa con un formato inesperado, **cuando** se recibe, **entonces** se descarta y la operación se trata como sin respuesta. |
| AC-SEG-20 | SEG-23 | **Dado** una solicitud sin origen determinable, **cuando** llega, **entonces** queda un evento de seguridad y se aplican solo los límites por usuario. |
| AC-SEG-13 | SEG-14 | **Dado** una dependencia necesaria que no responde, **cuando** pasan 10 segundos, **entonces** se informa servicio no disponible y las demás operaciones siguen funcionando; y si la dependencia no era necesaria, la operación continúa. |
| AC-SEG-14 | SEG-15 | **Dado** una llamada entre servicios sin credencial, **cuando** llega, **entonces** se rechaza. |
| AC-SEG-15 | SEG-17 | **Dado** una solicitud que llega por el borde con la dirección del cliente final, **cuando** la Web llama a un servicio, **entonces** el servicio aplica los límites con la dirección del cliente y no con la de la Web. |
| AC-SEG-16 | SEG-18 | **Dado** un visitante con 60 solicitudes en el último minuto, **cuando** envía otra, **entonces** se rechaza con la respuesta de límite excedido. |
| AC-SEG-17 | SEG-19 | **Dado** un correo con 3 solicitudes de código en la última hora, **cuando** se pide otra, **entonces** se rechaza con la misma respuesta exista o no la cuenta. |
| AC-SEG-18 | SEG-20 | **Dado** una cuenta con 5 códigos de segundo factor incorrectos, **cuando** presenta uno correcto, **entonces** se rechaza durante 15 minutos. |
| AC-SEG-19 | SEG-22 | **Dado** una reserva repetida con la misma clave de idempotencia, **cuando** se envía otra vez, **entonces** no cuenta para el límite de reservas pero sí para el límite general de solicitudes. |

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
- Una alerta repetida por el mismo motivo en 15 minutos se agrupa en una sola entrada de la lista.
- Un atacante que bloquea la combinación de la cuenta y su propio origen no impide que el titular ingrese desde otro origen.
- **Riesgo aceptado:** un ataque distribuido desde muchos orígenes puede probar 5 contraseñas por origen cada 15 minutos. Se mitiga con la política de contraseñas, la alerta de SEG-09 y el segundo factor del administrador central.

## 7. Preguntas abiertas

- [ ] ¿La alerta de seguridad necesita además un aviso fuera del panel (por ejemplo, por correo)? Propuesta: no en esta versión; solo la lista en el panel. Responde: responsable del proyecto.
- [ ] ¿Qué cabecera de reenvío del borde se considera confiable para obtener el origen y cómo se evita falsificarla? Se decide en el plan, con la configuración de Cloudflare y Nginx.
- [ ] ¿Confirmas 3 solicitudes de código por hora (SEG-19), el bloqueo de 15 minutos tras 5 códigos de segundo factor incorrectos (SEG-20; los códigos de respaldo también cuentan) y los límites de 60 y 100 solicitudes por minuto para visitantes y autenticados? Propuesta: sí, como metas iniciales a validar con pruebas de carga. Responde: responsable del proyecto.
