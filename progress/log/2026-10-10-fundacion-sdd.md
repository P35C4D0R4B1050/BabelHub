# 2026-10-10 — Fundación del marco SDD

## Qué se hizo

- Se ajustó el ajuste de inventario: la merma y el daño solo descuentan; la corrección de conteo se indica como conteo real (RF17).
- Se investigaron referencias de Spec-Driven Development (GitHub Spec Kit) y de skills para .NET (repositorio oficial `dotnet/skills`).
- Se creó el marco: `AGENTS.md`, `CLAUDE.md`, glosario, plantillas, 18 specs en borrador, validador `tools/SpecCheck`, skills y agentes, integración continua, esqueleto de la solución e infraestructura local.

## Decisiones

- Las skills del proyecto se escribieron a mano; las skills externas se **recomiendan** pero no se instalan sin que la persona responsable lo decida.
- El ciclo SDD tiene dos puertas humanas (spec y plan) y una verificación de convergencia automática antes de `Done`.
- `SpecCheck` valida estructura, EARS, trazabilidad y puertas; se ejecuta en CI.

## Pendiente / siguiente

- Revisar y aprobar las specs en orden: `transversal-auditoria` → `inventario-fuentes` → `inventario-seleccion-fuente` → `inventario-ajustes` → `inventario-traslados`.
- Resolver B02, B05 y B06 antes de planificar las specs que dependen de ellos.
