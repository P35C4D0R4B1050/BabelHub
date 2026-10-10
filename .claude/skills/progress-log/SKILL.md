---
name: progress-log
description: Registra el avance de una sesión de trabajo en progress/ (entrada de log, CHANGELOG y bloqueos). Úsala al terminar una tarea, una spec o una sesión, o cuando se pida «registrar el avance» o «actualizar la bitácora».
---

# progress-log — Bitácora de trabajo

Mantiene `progress/` como memoria del proyecto: qué se hizo, por qué, qué sigue y qué bloquea.

## Pasos

1. Revisa `git log --oneline` y `git status` para saber qué ocurrió realmente.
2. Crea `progress/log/AAAA-MM-DD-<tema>.md` (una entrada por sesión real) con las secciones **Qué se hizo**, **Decisiones**, **Pendiente / siguiente**. Sé concreto: nombres de specs, tareas y commits.
3. Actualiza `progress/CHANGELOG.md` con lo completado, agrupado por fecha y feature. Solo lo que terminó (no intenciones).
4. Actualiza `progress/blockers.md`: agrega bloqueos nuevos con ID (`Bnn`), dueño y qué bloquean; mueve los resueltos a «Cerrados» con su cierre.
5. Haz un commit `docs:` pequeño.

## Reglas

- No inventes avances: si algo no está en Git ni en los archivos, no se registra.
- Una decisión que cambie documentación de diseño se enlaza al documento o AD afectado.
- No registres secretos ni datos personales.
