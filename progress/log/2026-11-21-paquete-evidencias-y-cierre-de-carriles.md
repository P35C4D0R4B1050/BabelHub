# 2026-11-21 — Paquete de evidencias del Sprint 2 y cierre de carriles

## Qué se hizo
- Los cuatro encargos de la persona quedaron en `main` (`610b3082`, `50b8c880`): Scalar con interfaz (`d78c9568`, fundido en `e984811e`), hoja maestra `docs/07-proceso-de-desarrollo/07-evidencias-sprint-2.md`, correcciones del README y del informe de evidencias, paneles HTML de k6 (`57c08f24`, `1f1a5a53`, `327bda01`).
- Perfil realista de 2 000 sesiones (`tests/load/escenarios/perfil-realista.js`) con una corrida completa y una repetición parcial.
- DD-23 registra la confirmación directa de la persona del paquete `Scalar.AspNetCore` («Sí, autoricé»; reconfirmada en el chat del orquestador).
- Verificación de cierre de carriles: las 11 sesiones están inactivas, ninguna rama tiene commits sin fundir (`git branch --no-merged main` vacío), los 9 worktrees están limpios y solo queda `.claude/launch.json` modificado en local (no se commitea).

## Decisiones
- Presentar el resultado honesto: el perfil realista sostiene ≈ 1 000 sesiones con 1 OCPU (DD-17); a 2 000 no. Se declara también que la ruta del E-commerce no se midió.
- Las palancas 3 (menos idas a la base) y 4 (caché de disponibilidad) quedan pospuestas por la persona hasta terminar los detalles finales.

## Pendiente / siguiente
- Abrir un chat nuevo de orquestación desde `progress/relevos/coordinador.md`.
- De la persona: `push` y primera corrida del CI en GitHub, llaves de Culqi (B05) y del LLM (B06), AJU-22, NOT-02, SEG-17/23, D6, S2, S3, AM01, ISBN de AC-CAT-02, partes humanas de T32–T34 y, opcional, una corrida a 2 000 sesiones con el dimensionamiento que las sostenga.
