@AGENTS.md

# Notas para Claude Code

- Responde en **español** y sé conciso. Los commits van en inglés (ver `AGENTS.md`, sección 7).
- Antes de tocar código, identifica la spec de la feature en `specs/` y su estado. Si no está **Approved** y **Planned**, usa las skills `spec-new`, `spec-clarify`, `spec-plan` o `spec-tasks` en lugar de programar.
- Usa las skills del proyecto (`.claude/skills/`) en lugar de improvisar el flujo. Para revisar en contexto aislado, usa los agentes `spec-auditor` y `arch-reviewer`.
- Tras editar una spec, un plan o `tasks.md`, ejecuta `dotnet run --project tools/SpecCheck`.
- Tras editar código, ejecuta `dotnet build BabelHub.sln -warnaserror` y las pruebas afectadas.
- Para verificar pantallas, usa el navegador integrado o Playwright contra `BabelHub.Web` y compara con `prototype/ui-v2/` (sirve con `python -m http.server 8081 --directory prototype/ui-v2`).
- Nunca hagas `push` ni aprobaciones de specs o planes: son decisiones de la persona responsable.
- Para pedir una decisión puntual al usuario, usa preguntas cortas con opciones y una recomendación.
