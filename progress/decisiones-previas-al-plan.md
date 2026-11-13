# Decisiones de la persona responsable previas al plan (2026-11-12)

Respuestas a las preguntas cortas de cierre de la fase 2. Son **entradas de los planes** (`spec-plan`): los valores son iniciales y configurables, no requisitos nuevos. Cada decisión indica de qué hallazgo de `progress/hallazgos.md` sale y dónde se aplica.

## Cuota de correo (`identidad-seguridad`, IDE-66, IDE-73, IDE-74, IDE-77 a IDE-80)

| Decisión | Valor | Aplica en |
| :--- | :--- | :--- |
| Cuota total diaria de la cuenta de envío | **300 correos por día** (margen frente a los unos 500 de Gmail) | IDE-66, plan de identidad |
| Cupos | **15 %** alertas a la administración, **15 %** alertas críticas de cuenta, hasta **25 %** códigos de verificación y recuperación y **45 %** para todo lo demás (avisos no críticos, reclamos, pagos, reintentos de alertas técnicas) | IDE-66, IDE-77 |
| Tope por cuenta de avisos de seguridad no críticos | **3 cada 24 horas**; el cuarto no sale por correo y queda visible en la cuenta con su evento | IDE-74, IDE-79 |
| Cola de correos pendientes por cuota agotada | Tabla `correos_pendientes` en la base del E-commerce, respaldada y reintentada al renovarse la cuota diaria (ajusta AD30, que dice «sin cola», solo para lo pendiente por cuota) | IDE-77, IDE-78, IDE-80 |
| Carpeta Enviados de la cuenta de Gmail | Se vacía **cada 7 días** | §5 de la spec, privacidad |

## Identidad (`identidad-seguridad`)

| Decisión | Valor | Aplica en |
| :--- | :--- | :--- |
| Tope absoluto de la cancelación diferida | **30 días desde la solicitud**; pasados, se descarta y el titular vuelve a poder comprar (nuevo IDE-81) | IDE-68, IDE-76, IDE-81 |
| «Región habitual» | **País según la IP** | IDE-53, IDE-54 |
| Longitud de los códigos de un solo uso | **6 dígitos** (vigencia 10 minutos y 5 intentos, IDE-19) | IDE-19 |

## Plataforma (`transversal-plataforma`; specs Approved, sin cambio de texto)

| Decisión | Valor | Aplica en |
| :--- | :--- | :--- |
| Corrección urgente tras una migración | Se toma un **respaldo bajo demanda** antes del ensayo; el despliegue sigue exigiendo ensayo | PLA-11, PLA-42 |
| Contraseñas y segundo factor cambiados después del respaldo restaurado | **Solo informar** a la persona responsable; cada usuario cambia la suya. **Riesgo aceptado:** una contraseña cambiada por sospecha de robo vuelve al valor anterior hasta que su dueño la cambie; lo mitigan la invalidación de sesiones y la rotación de claves (PLA-43) | PLA-19, PLA-43 |
| Entorno de restauración | **Equipo de la persona responsable**, con disco cifrado y datos destruidos al terminar; excepción documentada en `docs/05-seguridad/07-operacion-segura.md` §3 | PLA-22, PLA-39 |
| Registro de supresiones | **Contenedor propio en Object Storage** con credencial de solo agregar, protegido contra borrado y copiado con el archivo de cambios | PLA-37, PLA-38 |

## Para el plan, sin decisión de la persona

Mapa de credenciales de respaldo y retiro, herramienta de archivado de WAL (B11), tamaño del almacén frente a la cuota, cadena de hashes del registro de supresiones, caché de claves del Inventario al rotar, definición de «alerta a la administración» y de «alerta crítica de cuenta» en el glosario, y los demás Menores de `progress/hallazgos.md`.

## Decisiones tras la auditoría de cierre (2026-11-12)

| Decisión | Valor | Aplica en |
| :--- | :--- | :--- |
| Retención de pedidos, dirección y teléfono de entrega | **2 años**; luego se anonimiza el contacto y queda lo estadístico | Reaperturas de pedidos, envíos y carrito-checkout; privacidad §1 |
| Retención del registro de descargas (BIB-11) y de las ayudas (BIB-16) | **2 años** | Reapertura de biblioteca digital |
| Teléfono de entrega | Se pide en el checkout como dato de contacto de entrega, con finalidad y retención propias | Reaperturas de carrito-checkout y envíos |
| Cuándo se reabren las specs aprobadas (pedidos, envíos, devoluciones, reclamaciones, pagos, biblioteca, carrito, selección de fuente, seguridad, auditoría y web-base) | **Al planificar cada fase**, justo antes de su plan y con su propia revisión de la puerta | Fase 3 en adelante |
| Controles de red y transporte (TLS interno, puertos cerrados, Cloudflare Access con MFA en el host de gestión, sincronización de hora) | **Tareas de infraestructura con prueba** (escaneo de puertos y de TLS) en el plan de plataforma; no reabre `transversal-plataforma` | Plan de plataforma, fase 8 |
| Retención del contacto de entrega de un checkout abandonado (checkout e intención de pago) | **30 días** desde su última modificación o creación; antes si se crea el pedido, se cancela el checkout o se resuelve el pago (decisión del 2026-11-13) | CAR-34, PAG-27, privacidad §1 |
| Reembolso pendiente al pedir cancelar la cuenta | **Difiere la cancelación** como un pago pendiente (IDE-27, PAG-24); el reembolso se completa y luego se cancela (decisión del 2026-11-13) | IDE-27, AC-IDE-24, PAG-24 |
| Identificador de la cuenta en el pedido tras cancelar la cuenta | **Se conserva** el identificador opaco de la cuenta junto con el pedido, sus líneas, importes y fechas; solo se anonimiza el contacto (decisión del 2026-11-13; residual en AM31) | PED-33, privacidad §1 y §5 |
| Pedido no recogido y cancelación de la cuenta | **Cuenta como pendiente** hasta que se anule o se entregue, con el tope de 30 días de IDE-81 (decisión del 2026-11-13) | PED-31 |
| Dueño de la copia del contacto de entrega | **Pedidos** guarda la única copia; Envíos la lee del pedido y no guarda copia propia (diseño del 2026-11-13; Envíos se ajusta en su reapertura) | PED-32, ENV-02, ENV-15, ENV-16 |
| Reembolso de una compensación aprobado durante una cancelación (PAG-32) | **No difiere la cancelación en curso**: es una excepción acotada a la decisión del reembolso pendiente, porque la consulta de pendientes ya pasó; si la cancelación se reintenta, PAG-24 lo contará (diseño del 2026-11-13) | PAG-32, IDE-27 |
| Retención del texto libre de Devoluciones (motivo y reportes de archivo defectuoso) y su vínculo con la cuenta | **2 años** desde el último cambio de estado final; luego se redacta el texto y se quita el vínculo (decisión del 2026-11-13) | DEV-27, privacidad §1 y §5 |
| Avisos de cambio de estado de un envío | **Se aceptan ambos**: Pedidos avisa el cambio de línea y Envíos añade la guía sin «reemplazar» el aviso de Pedidos; no se reabre Pedidos (decisión del 2026-11-13) | ENV-06, PED-05 |
| Estructura de módulos para la auditoría, los avisos y los límites | **Módulos propios**: en el E-commerce, `Plataforma` (límites, entradas de integración, supresiones, correo con su cuota y su cola, reloj, actor, unidad de trabajo), `Auditoria` y `Notificaciones`; en el Inventario, `Plataforma` y `Auditoria`. Identidad implementa los puertos que Auditoría define; Notificaciones consulta el contrato de Identidad (decisión del 2026-11-13) | docs/03/04 §1 y §1.1, AD02 y `ReglasDeModulosTests` |
| Retiro de respaldos vencidos | **Confirmado**: lo hace el almacén de objetos con reglas de ciclo de vida y retención con bloqueo; ninguna VM tiene credencial de borrado (decisión del 2026-11-13) | plan de transversal-plataforma D07 |
| Tope de avisos por número destino | **5 por hora y 20 por día** por número (huella); los excesos pasan a «omitido» con un evento de seguridad; reabre `transversal-notificaciones` (decisión del 2026-11-13) | NOT-14 |
| Historial de n8n y Evolution API | **7 días**: n8n con purga a 168 horas y Evolution sin guardar mensajes ni chats; lo respaldado sigue el plazo de los respaldos (decisión del 2026-11-13) | plan de transversal-notificaciones, privacidad §4 |

## 2026-11-13 — Notificaciones: tope, TRA-22 y retención

- El tope por número de NOT-14 (5 por hora deslizante y 20 por día) aplica solo a números de clientes; el contacto de una sucursal queda exento (spec y plan de `transversal-notificaciones` ajustados; la spec sigue en Draft hasta su nueva aprobación).
- El aviso de TRA-22 (traslado vinculado con más de 48 horas sin aprobar) sale como alerta por correo a la administración central y a logística (correo con cuota, fuera del Notificador); se quita de la fila del contrato `solicitar-aviso` y lo resuelve el plan de `inventario-traslados`.
- Retención de avisos: los enviados, cancelados u omitidos se purgan a los 90 días (solo estado y fechas); los números desactivados conservan su huella mientras dure la desactivación y se reactivan solo a pedido del titular.
