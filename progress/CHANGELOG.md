# CHANGELOG

Qué se completó, cuándo y en qué feature. Se actualiza con la skill `progress-log`. El detalle de cada sesión está en `log/`.

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
