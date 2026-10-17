# CHANGELOG

Qué se completó, cuándo y en qué feature. Se actualiza con la skill `progress-log`. El detalle de cada sesión está en `log/`.

## 2026-10-16 — Análisis inicial y cierre de brechas

### Documentación
- Informe de análisis `progress/R3-analisis-inicial.md`, con los hallazgos y sus resoluciones.
- Nuevo modelo C4 (niveles 1 a 3) en `docs/04-modelo-c4/`.
- Marco legal ampliado (Código del Consumidor, menores, cookies, plazos de reclamos, ARCO e incidentes, a verificar); privacidad, seguridad, estados, dominio y modelo de datos alineados con las specs.
- Revisadas AD22 (segundo factor para todo el personal), RNF15 (contraseña de 10 caracteres sin composición), RC18/AD30 (correo también para respuestas a reclamos, alertas y avisos de pago) y AD16 (plan de salida de WhatsApp). Sistema operativo documentado: Ubuntu Server LTS.

### Specs
- Las 19 specs ajustadas, sin preguntas abiertas y sin errores de `SpecCheck`; siguen en `Draft`, a la espera de aprobación.
- Revisión con `spec-auditor` y `security-reviewer` sobre auditoría, seguridad e identidad, y segunda ronda de ajustes (límites sin origen, registro de eventos agrupado, segundo factor, clave de firma, credencial del asistente, sesiones y auditoría ampliada). Nueva amenaza AM25 y AM26.
- Revisión de las specs de Inventario con `spec-auditor` y ajustes: ingreso inicial de stock, carreras entre reserva, traslado, anulación y entrega, reservas vencidas, traslados vinculados con aviso a las 48 horas, ajustes de la administración y criterios de concurrencia. Nuevo estado de reserva `cerrada`.
- Revisión de `identidad-seguridad`, `transversal-notificaciones` y `web-base` con `spec-auditor` y ajustes: cancelación de cuenta aplazada, renovación con vencimiento heredado, reto del segundo factor, cambio de correo según riesgo (nueva amenaza AM27), textos legales con versión validada por Identidad, correo verificado en la credencial, estados y reintentos de avisos y pantallas obligatorias de `web-base`.
- Revisión de `ecommerce-catalogo`, `ecommerce-carrito-checkout`, `ecommerce-pagos` y `ecommerce-pedidos` con `spec-auditor` y ajustes: intención de pago, ciclo de la línea con traslado, estado del pedido derivado, anulación y reembolso del envío, compensación del pago, pago pendiente solo digital, fusión del carrito de visitante y validaciones del catálogo.
- Barrido de coherencia de `docs/` (glosario, UX, procesos, requisitos, calidad, tácticas, arquitectura, diseño, datos y seguridad) y conflictos entre specs corregidos (TRA-09 frente a TRA-16, traslado perdido, carrito de visitante, envío por fuente, correo frente a WhatsApp).
- Nuevos o cambiados: rutas entre fuentes, reserva ligada al traslado, descuento de físico a domicilio, pedido no recogido, cancelación del cliente, pago pendiente con reserva extendida, límites de cuentas y de hojas por origen, sesión web, textos legales versionados, segundo factor del personal, mayoría de edad, auditoría unificada, alertas por correo y credencial de lectura del asistente.

## 2026-10-10 — Reformulación de la documentación

### Documentación
- `docs/` reorganizada en siete carpetas temáticas (análisis, arquitectura, diseño, modelo C4 reservado, seguridad, experiencia de usuario y proceso); documentos mixtos divididos y enlaces validados por `SpecCheck`.
- Línea base ampliada: NN16–NN18, OB07–OB08, RF26–RF29, RNF15–RNF28, RC14–RC18, AC10–AC14 (clasificados por ISO/IEC 25010:2023), DA13–DA17 y AD22–AD30 (catálogo en `progress/R0-catalogo-de-cambios.md`).
- Documentos nuevos: actores y permisos, procesos, modelo de dominio, tácticas, presupuesto de recursos, patrones, módulos y contratos, modelo de datos, estados y concurrencia, mapeo a IEEE 1016, seis documentos de UX, estrategia de pruebas y la carpeta `05-seguridad` (amenazas AM01–AM24 y controles CTL-01–CTL-38).

### Specs
- Nueva `transversal-seguridad` (SEG) y ajustes en nueve specs; dos pasadas del agente `spec-auditor` (ver `progress/R2-auditoria-de-specs.md`). Todas siguen en `Draft`.

### Marco SDD
- Nueva skill `security-review` y agente `security-reviewer`.
- Prototipo: el consentimiento de WhatsApp en el checkout pasa a ser opcional.

## 2026-10-10 — Fundación del proyecto

### Documentación
- `docs/01` a `docs/07`: necesidades, requisitos, atributos de calidad, drivers, decisiones AD01–AD21, estilo y enfoque arquitectónico.
- Libros digitales (NN13–NN15, RF22–RF25, RNF14, AC09, AD19–AD20) y marco legal con rigurosidad laxa por ser un proyecto académico.
- `docs/08` (interfaz de usuario) y `docs/09` (convenciones técnicas).
- `docs/10` (flujo SDD) y `docs/glossary.md`.

### Interfaz
- Prototipo `prototype/ui-v2/` con las áreas Tienda, Sucursal, Logística y Administración; temas claro y oscuro; estados de pantalla; ajustes de inventario guiados por motivo.

### Marco SDD
- `AGENTS.md`, `CLAUDE.md`, plantillas de `specs/`, 18 specs en borrador, validador `tools/SpecCheck`, skills y agentes en `.claude/`, integración continua.

### Esqueleto técnico
- Solución `BabelHub.sln` con los proyectos por servicio, proyectos de prueba, prueba de arquitectura e infraestructura local en `infra/`.
