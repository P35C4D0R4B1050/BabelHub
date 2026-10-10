---
name: spec-verify
description: Verifica que el código y las pruebas de una feature convergen con su spec (compilación, pruebas, arquitectura, trazabilidad, requisito por requisito) y escribe verificacion.md. Úsala al terminar las tareas de una feature, antes de pasarla a Done, o cuando se pida «verificar», «validar» o «comprobar» una spec.
---

# spec-verify — Convergencia entre spec, código y pruebas

Repite hasta que el resultado sea **Converge**. Si no converge, corrige el **código** o, si la spec estaba mal, vuelve a `Draft` y pide una nueva aprobación.

## Comprobaciones automáticas

Ejecuta y registra el resultado de cada una en `specs/<feature>/verificacion.md` (plantilla en `specs/_template/verificacion.md`):

1. `dotnet build BabelHub.sln -warnaserror`
2. `dotnet test BabelHub.sln` (unitarias, integración y arquitectura)
3. `dotnet run --project tools/SpecCheck`
4. Búsqueda de secretos: `git grep -nIiE "password\s*=|api[_-]?key\s*=|secret\s*=" -- src`
5. Que no haya archivos digitales, `.env` ni datos personales en el cambio (`git diff --stat`, `git status`).

## Revisión requisito por requisito

Para **cada** requisito de la spec:

1. Localiza la evidencia en el código (`ruta:línea`) y la prueba que lo ejercita (`AC-XXX-NN` y su ruta).
2. Clasifica: **Cumple**, **Parcial** o **No cumple**. «Cumple» exige que la prueba falle si se rompe la regla.
3. Revisa los casos límite de la sección 6 de la spec: ¿existe prueba?

Después revisa lo que la spec **no** pide y el código hace: funcionalidad de más es una diferencia.

## Verificación de reglas transversales

- Reglas de capas y referencias (`arch-reviewer` puede hacer esta parte en contexto aislado).
- Alcance por sucursal comprobado en la Aplicación.
- Auditoría en la misma transacción para operaciones sensibles.
- Idempotencia donde la spec la pide; concurrencia probada con solicitudes simultáneas.
- Pantallas: estados estándar, accesibilidad y paridad con el prototipo (skill `ui-review`).

## Resultado

- **Converge:** todo cumple, no hay diferencias abiertas y `SpecCheck` pasa. Escribe `Resultado: Converge`, marca la spec como `Done` y registra la sesión con `progress-log`.
- **No converge:** lista las diferencias, corrígelas (nuevas tareas en `tasks.md`) y vuelve a empezar. No escribas `Converge` para «avanzar».
