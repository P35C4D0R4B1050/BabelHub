# 2026-11-23 — Carril contacto y legales (Culqi): spec nueva y puerta de aprobación

Rama `worktree-agent-afc4644c57deeb3ef`.

**Hecho.** Spec nueva `specs/web-informacion-del-comercio/spec.md` (prefijo COM, 17 requisitos, 14 criterios, Draft) que complementa a `web-base` sin cambiar su texto: teléfono, correo y dirección por `BABELHUB__Contacto__*` (fallo cerrado fuera de Development/Test, formato validado en todo entorno), pie y `/contacto`, descripción del comercio, «Cambios y devoluciones» (la política de reembolso renombrada, redirección permanente), términos y privacidad con ley peruana, y la versión de los textos vigilada por huella. DD-75 a DD-79 en `progress/decisiones-delegadas.md`. Borrador de plan, tareas y decisiones en `progress/borradores/web-informacion-del-comercio/` (fuera de `specs/` para que SpecCheck no avise; se copian a la spec al aprobar).

**Puerta (3 rondas, `spec-auditor`, `arch-reviewer`, `security-reviewer`).** Ronda 1: 9, 7 y 3 Importantes; ronda 2: 1, 3 y 0; ronda 3: auditor «Lista para aprobar», seguridad «Cumple con observaciones», arquitectura «Cumple con observaciones» con **un Importante procedimental**: DD-75 a DD-79 siguen «pendiente de ratificar» y DD-76 y DD-77 son decisiones de negocio. Se agotaron las tres rondas, así que **no se aprobó**: la spec sigue en Draft. Los Menores de las tres rondas se corrigieron en la spec o quedan en `progress/hallazgos.md`.

**No hecho.** Nada de código ni de textos legales (AGENTS.md §2: sin spec Approved y plan Planned no se escribe código de producción). Tampoco `infra/secretos-requeridos.txt` ni docs.

**Para continuar.** La persona ratifica DD-75 a DD-79 (una línea) o los corrige; con eso, una ronda corta de reconfirmación sobre los ajustes menores posteriores a la ronda 3 (+51 sin `+` inicial, ruta `/contacto` nombrada, ASCII y U+0020, COM-15 como verificación previa), `spec-approve` de la spec, copiar el plan, las tareas y las decisiones del borrador, aprobar el plan y ejecutar T01 a T13.
