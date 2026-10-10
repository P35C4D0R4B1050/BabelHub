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

Definir las protecciones que todos los servicios y la interfaz web comparten, para que ninguna feature las reimplemente a su manera: validación de entradas, errores seguros, límites de uso, registro y alertas de seguridad, e integraciones autenticadas (NN18). Las reglas propias de una feature (recuperar contraseña, reservas por cliente, descargas, asistente) viven en la spec de esa feature y reutilizan lo que aquí se define.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Validación de entradas y codificación de salidas. | Recuperación de contraseña, política de contraseñas y segundo factor (`identidad-seguridad`). |
| Formato uniforme de errores y fallo seguro. | Límite de reservas por cliente (`inventario-seleccion-fuente`). |
| Límites de uso generales y respuesta uniforme cuando se exceden. | Límite de descargas por hora (`ecommerce-biblioteca-digital`) y de consultas del asistente (`asistente-compra-ia`), que reutilizan la respuesta uniforme. |
| Registro de eventos de seguridad, alertas y secretos fuera de los registros. | Cabeceras de seguridad y cookie de sesión (`web-base`). |
| Autenticación de integraciones, validación de sus respuestas y tiempos de espera. | Respaldo y recuperación, y escaneo de dependencias (infraestructura y CI). |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| SEG-01 | Ubicuo | El sistema deberá validar en el servidor el tipo, la longitud, el formato y el rango de toda entrada antes de procesarla, y rechazar las que no cumplan. | RNF16, AC05 |
| SEG-02 | Ubicuo | El sistema deberá codificar toda salida hacia la interfaz según su contexto, de modo que el contenido ingresado por una persona nunca se interprete como código. | RNF16, AC05 |
| SEG-03 | Ubicuo | El sistema deberá responder los errores con un formato uniforme que incluya un código estable, un mensaje genérico y un identificador de seguimiento, sin trazas ni datos internos. | RNF20, AC05 |
| SEG-04 | No deseado | Si ocurre un error inesperado durante una operación, entonces el sistema deberá negar la operación, no dejar efectos parciales y responder con el error genérico. | RNF20, AC05 |
| SEG-05 | Estado | Mientras un usuario supera 100 solicitudes por minuto, el sistema deberá rechazar las siguientes con una respuesta que indique la espera sugerida. | RNF17, AC13 |
| SEG-06 | Estado | Mientras un mismo origen supera 5 intentos de ingreso en 15 minutos, el sistema deberá rechazar los siguientes intentos con la misma respuesta uniforme. | RNF17, AC13 |
| SEG-07 | Ubicuo | El sistema deberá usar la misma respuesta uniforme y el mismo registro para los límites definidos en otras specs. | RNF17, AC13 |
| SEG-08 | Evento | Cuando se excede un límite de uso, el sistema deberá registrar un evento de seguridad con el usuario u origen, el límite y la fecha. | RNF17, RNF20 |
| SEG-09 | Evento | Cuando en cinco minutos se acumulan más de 10 accesos denegados o más de 20 ingresos fallidos, el sistema deberá generar una alerta de seguridad para la administración central. | RNF20, AC05 |
| SEG-10 | Ubicuo | El sistema deberá excluir de los registros técnicos y de seguridad las contraseñas, las credenciales, los datos de tarjeta y los enlaces de descarga. | RNF20, AC05 |
| SEG-11 | Ubicuo | El sistema deberá leer sus secretos de la configuración del entorno y no incluirlos en el repositorio, los registros ni los mensajes. | RNF06, AC05 |
| SEG-12 | Ubicuo | El sistema deberá autenticar cada llamada entrante de una integración con un secreto o una firma verificable, y rechazar y registrar las que no lo cumplan. | RNF21, AC05 |
| SEG-13 | Ubicuo | El sistema deberá tratar como no confiable toda respuesta de un servicio externo y validar su formato antes de usarla. | RNF21, AC05 |
| SEG-14 | No deseado | Si una dependencia externa no responde dentro del tiempo máximo, entonces el sistema deberá cortar la espera, informar que el servicio no está disponible y seguir atendiendo las demás operaciones. | RNF21, AC05 |
| SEG-15 | Ubicuo | El sistema deberá exigir que cada llamada entre servicios lleve una credencial verificable, y rechazar las que no la tengan. | RNF21, AC05 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-SEG-01 | SEG-01 | **Dado** un campo numérico, **cuando** se envía un texto, un valor fuera de rango o una cadena demasiado larga, **entonces** la entrada se rechaza con el error uniforme y no se procesa. |
| AC-SEG-02 | SEG-02 | **Dado** un comentario o un nombre con etiquetas de marcado, **cuando** se muestra en una pantalla, **entonces** aparece como texto y no se ejecuta. |
| AC-SEG-03 | SEG-03 | **Dado** una solicitud inválida en cualquier servicio, **cuando** se recibe el error, **entonces** trae código estable, mensaje genérico e identificador, y ninguna traza ni dato interno. |
| AC-SEG-04 | SEG-04 | **Dado** una operación que falla a la mitad por un error inesperado, **cuando** termina, **entonces** no quedan efectos parciales y la respuesta es el error genérico. |
| AC-SEG-05 | SEG-05 | **Dado** un usuario con 100 solicitudes en un minuto, **cuando** envía la siguiente, **entonces** se rechaza con la espera sugerida. |
| AC-SEG-06 | SEG-06 | **Dado** un origen con 5 intentos de ingreso en 15 minutos, **cuando** hace otro, **entonces** recibe la misma respuesta uniforme sin distinguir si la cuenta existe. |
| AC-SEG-07 | SEG-07, SEG-08 | **Dado** un límite excedido en descargas o en el asistente, **cuando** se rechaza, **entonces** la respuesta tiene el mismo formato y queda un evento de seguridad. |
| AC-SEG-08 | SEG-09 | **Dado** 11 accesos denegados en cinco minutos, **cuando** se supera el umbral, **entonces** se genera una alerta para la administración central. |
| AC-SEG-09 | SEG-10, SEG-11 | **Dado** cualquier flujo con contraseña, tarjeta o enlace firmado, **cuando** se revisan los registros y el repositorio, **entonces** no aparece ninguno de esos valores ni un secreto. |
| AC-SEG-10 | SEG-12 | **Dado** una llamada entrante de una integración sin firma o con firma inválida, **cuando** llega, **entonces** se rechaza y se registra; y con firma válida, se procesa. |
| AC-SEG-11 | SEG-13 | **Dado** una respuesta externa con un formato inesperado, **cuando** se recibe, **entonces** se descarta y la operación sigue como si el servicio no hubiera respondido. |
| AC-SEG-12 | SEG-14 | **Dado** una dependencia externa que no responde, **cuando** pasa el tiempo máximo, **entonces** se informa servicio no disponible y las demás operaciones siguen funcionando. |
| AC-SEG-13 | SEG-15 | **Dado** una llamada entre servicios sin credencial, **cuando** llega, **entonces** se rechaza. |

## 5. Reglas e invariantes

- Ante la duda, se niega: un error o una dependencia caída nunca abre un acceso.
- Los límites son por usuario y por origen; la respuesta es siempre la misma forma y no revela información de cuentas.
- Un mensaje de error nunca contiene trazas, rutas internas, nombres de tablas ni credenciales.
- Los eventos de seguridad se registran en el contrato de `transversal-auditoria`; esta spec define cuándo se registran, no cómo se almacenan.
- Los umbrales numéricos son metas iniciales y se validan con pruebas de carga.

## 6. Casos límite y errores

- Un origen compartido (por ejemplo, una red universitaria) puede alcanzar el límite por varias personas; el mensaje lo indica sin acusar a nadie.
- Si el registro de un evento de seguridad falla, el rechazo se mantiene y se genera una alerta técnica (`transversal-auditoria`).
- Una alerta repetida por el mismo motivo se agrupa para no inundar a la administración.
- Los reintentos legítimos de un cliente con la misma clave de idempotencia no cuentan como solicitudes nuevas.

## 7. Preguntas abiertas

- [ ] ¿Cómo recibe la administración central las alertas de seguridad? Propuesta: un aviso por el notificador y una lista en el panel de administración. Responde: responsable del proyecto.
- [ ] ¿Las 100 solicitudes por minuto aplican por usuario autenticado y por origen para los visitantes? Propuesta: sí. Se decide en el plan.
