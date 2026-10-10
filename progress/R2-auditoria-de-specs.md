# R2 — Auditoría de las specs ajustadas

Resultado de la revisión del agente `spec-auditor` sobre las 9 specs tocadas por el catálogo R0 (2026-10-10) y de las correcciones aplicadas. Todas las specs siguen en `Draft`.

## Decisiones de la persona responsable

| Tema | Decisión |
| :--- | :--- |
| Códigos de verificación y recuperación | Por **correo** con proveedor gratuito (AD30, RC18, bloqueo B08); WhatsApp queda para avisos y es opcional. |
| Consentimiento en el checkout | Términos y tratamiento de datos obligatorios; WhatsApp opcional (CAR-12, CAR-18). |
| Descarga digital | Punto de entrada propio que verifica, cuenta y redirige a un enlace firmado de 5 minutos (BIB-04, BIB-05). |
| Recuperación del personal | La restablece el administrador central (IDE-23); el administrador tiene códigos de respaldo (IDE-21, IDE-22). |

## Hallazgos corregidos

| Hallazgo | Corrección |
| :--- | :--- |
| B1 códigos por WhatsApp contra la regla 10 y `transversal-notificaciones` | Canal de correo (AD30) con puerto `EnviadorDeCorreo`, fuera del Notificador. |
| B2 límite «por origen» sin cubrir | SEG-17 (origen), SEG-18, SEG-19, SEL-19; la pregunta sobre visitantes se cerró. |
| B3 consentimiento de WhatsApp | CAR-12 dividido; CAR-18; prototipo y docs/06 ajustados. |
| I1 SEG-04 y SEG-14 contra la regla 10 | Excepción para dependencias no críticas y tiempo máximo de 10 segundos. |
| I2 mensajes genéricos contra mensajes específicos | SEG-03 y SEG-16 separan errores inesperados de errores de validación y de negocio. |
| I3 límites de ingreso duplicados | Regla única en SEG-06 (cuenta y origen, 15 minutos), SEG-19, SEG-20; IDE solo la cita. |
| I4 y I14 recuperación y derechos ARCO | IDE-19 a IDE-30 (códigos, personal, segundo factor, atención de solicitudes, cancelación). |
| I5 duración de credenciales | IDE-02 con 15 minutos y 7 días; IDE-09 con sal. |
| I6 auditoría inmutable y depuración | AUD-03 con excepción, AUD-11 anonimiza, cancelación y ARCO auditables. |
| I7 enumeración en el registro | Aceptada y acotada con límites; regla de IDE §5 corregida. |
| I8 límites de reservas | Se cuentan solo las pendientes; SEL-16 a SEL-19; CAR-19. |
| I9 mecanismo de descarga | BIB-04 a BIB-08, BIB-16, BIB-17. |
| I10 respuesta uniforme | Definida en SEG-07; las demás specs la citan. |
| I11 a I13 alertas, datos prohibidos, integraciones | SEG-09, SEG-10, SEG-12 a SEG-15, IDE-30, NOT-12, AUD-07. |
| I15 a I18 asistente, índice infinito, biblioteca y web | ASI-14 a ASI-18, IND-07 e IND-09, BIB-12, WEB-08, WEB-18, WEB-22, WEB-25, WEB-26. |

## Pendiente para la aprobación de cada spec

- Confirmar las propuestas numéricas (vigencia de códigos, 60 solicitudes por minuto para visitantes, 10 reservas por hora por origen, topes del asistente, plazo ARCO de 20 días hábiles).
- Elegir el proveedor de correo (B08) y la cabecera de reenvío confiable del borde.
- Decidir quién aprueba la devolución de un impreso (pregunta abierta en `ecommerce-devoluciones`).
- Definir en el plan qué pantalla muestra la lista de alertas de seguridad.
- Aclarar en AGENTS.md, si se desea, que la regla 10 aplica a avisos y no a los códigos de seguridad (hoy no choca porque el correo usa otro puerto).
