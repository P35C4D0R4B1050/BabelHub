# CHANGELOG

Qué se completó, cuándo y en qué feature. Se actualiza con la skill `progress-log`. El detalle de cada sesión está en `log/`.

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
