---
name: spec-approve
description: Aprueba una spec (Draft → Approved) o un plan (→ Planned) por delegación de la persona responsable, solo si la puerta de revisión no tiene hallazgos Bloqueantes ni Importantes ni contradicciones; si los hay, no aprueba y lo reporta. Úsala cuando se pida «aprobar» una spec o un plan, o al cerrar la revisión de una spec o un plan.
---

# spec-approve — Aprobación delegada con puerta

Aplica `docs/07-proceso-de-desarrollo/04-aprobacion-delegada.md` (léelo antes de empezar). **Aprobar es lo último que se hace; ante la menor duda no se aprueba.**

## Pasos

1. **Identifica** el objeto: spec (`specs/<id>/spec.md`) o plan (`plan.md`, `tasks.md`, `decisions.md`). Si es un plan, la spec debe estar **Approved**.
2. **Comprueba G1, G2, G7, G8 y G9** tú mismo:
   - `dotnet run --project tools/SpecCheck`: 0 errores y **0 avisos**.
   - Cero preguntas abiertas y ningún «a definir», «pendiente» o «por confirmar» en el texto.
   - Las specs de las que depende están Approved o más.
   - Ningún bloqueo de `progress/blockers.md` impide planificar esta spec.
   - `dotnet build BabelHub.sln -warnaserror` y `dotnet test BabelHub.sln` en verde.
3. **Revisión independiente (G3 a G6).** Lanza los tres agentes **en paralelo**, cada uno con la ruta exacta, lo decidido por la persona (con su fecha) y la instrucción de buscar también contradicciones con `AGENTS.md`, `docs/` y las demás specs:
   - `spec-auditor` → **«Lista para aprobar»** o **«Con cambios menores»**.
   - `arch-reviewer` → **«Cumple»** o **«Cumple con observaciones»**.
   - `security-reviewer` → **«Cumple»** o **«Cumple con observaciones»** (siempre, aunque la spec no toque seguridad).
   - Pídeles que **clasifiquen cada hallazgo** como Bloqueante, Importante o Menor (criterio en el documento de la delegación, §2). Cero Bloqueantes e Importantes.
4. **Cualquier hallazgo Bloqueante o Importante, o contradicción:**
   - Si es un error o un vacío del texto: corrige el **origen** (la spec, el plan o el documento afectado), no el reporte.
   - Si exige una decisión de negocio o un cambio de requisito: **no lo resuelvas tú**; regístralo como pregunta abierta y pregúntale a la persona.
   - Tras corregir, **repite la revisión completa** (paso 3). Máximo **3 rondas**. Si persiste algo, **no apruebes**: deja el estado como está, anota el motivo y repórtalo.
5. **Si todo está limpio:**
   - Calcula la huella: `dotnet run --project tools/SpecCheck -- --huella <id> spec` (o `plan`).
   - Anota los hallazgos Menores y heredados en `progress/hallazgos.md` (spec, hallazgo, gravedad, dueño y fase).
   - Agrega al final de `progress/aprobaciones.md` la fila: `| AAAA-MM-DD | spec|plan | <id> | Claude (delegado) | 0/0 | <veredicto auditor> | <veredicto arquitectura> | <veredicto seguridad> | 0 | <n.º de Menores> | <huella> |`. **Antes** de tocar el estado.
   - Escribe en el frontmatter: spec → `estado: Approved`, `aprobado_por: Claude (delegado por Diego Amiquero)`, `aprobado_en: AAAA-MM-DD`; plan → `estado: Planned`, `plan_aprobado_por: Claude (delegado por Diego Amiquero)`, `plan_aprobado_en: AAAA-MM-DD`.
   - `dotnet run --project tools/SpecCheck -- --write-matrix` y luego `SpecCheck` otra vez: debe quedar en 0 errores y 0 avisos (si no, la aprobación no es válida: revierte el estado).
6. **Registra** en `progress/` (skill `progress-log`) lo aprobado, las rondas y los hallazgos corregidos, y **resume a la persona** antes de cualquier commit. No hagas `push`.

## Prohibido

- Firmar con el nombre de la persona; aprobar algo que cambiaste después de la última revisión limpia; aprobar con avisos, preguntas abiertas o un veredicto «No cumple» o «Necesita otra vuelta»; reclasificar un hallazgo grave como Menor para evitarlo.
- Quitar, suavizar o reinterpretar un requisito para que un hallazgo desaparezca; editar `speccheck.json`, las pruebas, los agentes o la delegación para que la puerta pase.
- Aprobar varias specs o planes con una sola revisión: cada uno se revisa y se registra por separado.
