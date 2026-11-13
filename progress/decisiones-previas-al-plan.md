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
