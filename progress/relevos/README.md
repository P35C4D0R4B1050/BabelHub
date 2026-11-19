# Relevos de carril (migración de chat)

Una sesión larga cuesta más en cada turno. Cuando un carril cierra un bloque grande, la persona abre una **sesión nueva** que parte de una nota de relevo y se archiva la anterior (plan en `docs/07-proceso-de-desarrollo/06-plan-sprint-2.md` §5).

## Cómo se escribe `progress/relevos/<carril>.md` (máximo una página)

1. **Rol y rama:** carril, rama de trabajo y qué módulos son suyos.
2. **Hecho:** specs y tareas cerradas (con el último commit) y qué hay sin commitear.
3. **En curso:** la tarea o el bloque, qué falta y qué espera a otro carril.
4. **Decisiones vigentes:** las del coordinador y de la persona que afectan a este carril (con la fecha).
5. **Trampas conocidas:** errores que ya ocurrieron (por ejemplo: fixtures de PostgreSQL sin literales de contraseña, espejar los catálogos de auditoría en los dos servicios, no agregar excepciones a las pruebas de arquitectura).
6. **Siguiente encargo** y cómo avisar al coordinador.

## Cómo arranca la sesión nueva

Lee, en este orden: `AGENTS.md`, `progress/relevos/<carril>.md`, `progress/coordinacion.md`, la spec, el plan y las tareas en curso, y `progress/ajustes-al-implementar.md`. No relee el historial ni las bitácoras viejas salvo que el relevo lo pida.
