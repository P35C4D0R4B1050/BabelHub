@AGENTS.md

# Notas para Claude Code

- Responde en **español** y sé conciso. Los commits van en inglés (ver `AGENTS.md`, sección 7).
- Antes de tocar código, identifica la spec de la feature en `specs/` y su estado. Si no está **Approved** y **Planned**, usa las skills `spec-new`, `spec-clarify`, `spec-plan` o `spec-tasks` en lugar de programar.
- Usa las skills del proyecto (`.claude/skills/`) en lugar de improvisar el flujo. Para revisar en contexto aislado, usa los agentes `spec-auditor`, `arch-reviewer` y `security-reviewer`.
- Tras editar una spec, un plan o `tasks.md`, ejecuta `dotnet run --project tools/SpecCheck`. Si toca seguridad, privacidad o integraciones, usa también `security-review`.
- Tras editar código, ejecuta `dotnet build BabelHub.sln -warnaserror` y las pruebas afectadas.
- Para verificar pantallas, usa el navegador integrado o Playwright contra la interfaz web del E-commerce (`BabelHub.Ecommerce.Api`) y compara con `prototype/ui-v2/` (sirve con `python -m http.server 8081 --directory prototype/ui-v2`).
- Nunca hagas `push`. Puedes aprobar specs y planes **solo** con la skill `spec-approve` y las reglas de `docs/07-proceso-de-desarrollo/04-aprobacion-delegada.md` (puerta sin hallazgos Bloqueantes ni Importantes, registro en `progress/aprobaciones.md` y Menores en `progress/hallazgos.md`); ante un hallazgo grave o una contradicción, no apruebes y repórtalo. Las decisiones de negocio siguen siendo de la persona responsable.
- Para pedir una decisión puntual al usuario, usa preguntas cortas con opciones y una recomendación.
