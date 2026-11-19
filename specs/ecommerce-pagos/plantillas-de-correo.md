# Plantillas de correo y avisos de WhatsApp de `ecommerce-pagos`

Nota de implementación (no es una spec ni un plan: no cambia el comportamiento aprobado). Dice qué pone Pagos, qué le toca a otros carriles y qué se puede hacer ya, para que nada quede sin dueño.

## 1. Correo (PAG-22, PAG-23): lo hace Pagos y se puede usar ya

Pagos es dueña del **contenido** de sus cinco plantillas: están en código (`Application/Pagos/Interno/PlantillasDeCorreoDePagos.cs`, con pruebas) y siguen la guía de `docs/06-experiencia-de-usuario/08-contenido-y-mensajes.md` (es-PE, tuteo respetuoso, frases cortas, qué pasó y qué hacer). Ningún texto lleva datos de pago, enlaces ni peticiones de datos (IDE-67). `EnviarAvisosDePago` las envía por `IEnviadorDeCorreo` con la categoría `Pago`.

| Plantilla (nombre en el catálogo) | Evento | Asunto | Campos |
| :--- | :--- | :--- | :--- |
| `pago-pendiente` | El cobro quedó pendiente de confirmación | Estamos confirmando tu pago | — |
| `pago-aprobado` | La compra se completó | Recibimos tu pago | — |
| `pago-rechazado` | La pasarela rechazó el cobro | No pudimos procesar tu pago | `motivo` (texto de la lista cerrada, p. ej. «tu tarjeta fue rechazada») |
| `pago-vencido` | El pendiente venció a los 30 minutos | Tu pago venció | — |
| `reembolso-automatico` | Se devuelve un cobro que no pudo ser compra (PAG-15, PAG-20) | Te devolvemos tu pago | `importe` (`S/ 1 234,50`) |

**Para el carril de `Plataforma` (`EnviadorDeCorreoSmtp`):** registrar estos cinco nombres en el catálogo de plantillas con esos asuntos, cuerpos y campos tomándolos de `PlantillasDeCorreoDePagos.Catalogo` (la fuente única); no reinterpretar el texto. Hasta entonces, el envío real de Pagos queda sin plantilla registrada: el código y sus pruebas ya funcionan con el doble.

## 2. WhatsApp (PAG-22, AC-PAG-12): qué se puede hacer ya y qué no

- **No hace falta desplegar nada para escribir el código.** El flujo de n8n, el adaptador firmado, el simulado y el nulo (`transversal-notificaciones` T14, T15 y T17) se construyen y se prueban en local con dobles. **Solo la operación real** necesita despliegue y configuración: la instancia de n8n y de Evolution, la clave HMAC del sentido saliente (`BABELHUB__Notificaciones__…`) y la URL del webhook; eso lo hace la persona responsable al desplegar (los avisos por WhatsApp en producción son parte del orden de sacrificio del documento 05).
- **Lo que bloquea a Pagos no es el despliegue sino el contrato:** `INotificador` y `SolicitudDeAviso` son de `transversal-notificaciones` T03, que depende de los bloques NOT-1 a NOT-3 y aún no existen. Un puerto propio de Pagos para WhatsApp sería inventar un contrato que Notificaciones debe definir.
- **Cómo se conecta cuando exista** (una tarea pequeña de Pagos, ya prevista en el plan de Notificaciones §«Los emisores»): al enviar cada fila de `avisos_de_pago`, `EnviarAvisosDePago` llama a `INotificador.Solicitar` con el evento determinista `pago:<intención>:<intento>:<evento>` (la clave natural de la fila, por lo que repetir no duplica), la plantilla del evento y el destinatario titular; Notificaciones comprueba el consentimiento y el tope y no lanza. Pagos no lee el número ni el consentimiento. Prueba: con consentimiento sale una solicitud por fila y sin él ninguna, y una falla de `Solicitar` no cambia el estado del cobro (regla 10).
- **Quién:** un carril que tome NOT-1 (T01 a T07) cierra el contrato; después Pagos añade esa llamada. Propuesto al coordinador.
