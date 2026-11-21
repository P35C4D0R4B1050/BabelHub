---
spec: ecommerce-catalogo
fecha: 2026-11-21
---

# Verificación — Catálogo

## Resultado: No converge

## 1. Comprobaciones automáticas

| Comprobación | Comando | Resultado |
| :--- | :--- | :--- |
| Compilación sin advertencias | `dotnet build BabelHub.sln -warnaserror` | Correcta (0 errores) |
| Pruebas unitarias del E-commerce | `dotnet test tests/unit/BabelHub.Ecommerce.UnitTests` | 5388 correctas |
| Prueba de arquitectura | `dotnet test tests/architecture` | 243 correctas |
| Integración con PostgreSQL real (Pagos, Reclamaciones y Web) | `dotnet test tests/integration/BabelHub.IntegrationTests --filter …` | 1282 correctas; no se corrió toda la integración de la solución en este cierre |
| E2E (Chromium de escritorio y de teléfono y WebKit; Firefox, informativo, no se lanza en este equipo) | `npx playwright test acceso reclamaciones administracion pagos` | 113 de 113 correctas con el host compuesto, el de gestión y datos sembrados |
| Specs, planes, tareas y trazabilidad | `dotnet run --project tools/SpecCheck` | OK: 20 specs, 0 avisos |
| Búsqueda de secretos | `git grep -nIiE "password\s*=\|api[_-]?key\s*=\|secret\s*=" -- src` y `scripts/escanear-secretos.sh repositorio` | Sin fugas; el `git grep` da 3 falsos positivos (el URI `otpauth://…?secret=` que se muestra una vez al configurar el segundo factor y la propiedad `opciones.Password` que recibe una clave leída del entorno) |

## 2. Requisito por requisito

No se hizo la matriz requisito por requisito de esta spec en el cierre del 2026-11-21: **no se declara Converge sin ella**. La evidencia por tarea está en `specs/ecommerce-catalogo/decisiones-de-implementacion.md`.

## 3. Diferencias abiertas

- Todas sus tareas figuran marcadas, pero falta la matriz requisito por requisito y una corrida completa de integración y E2E del catálogo en este cierre (su E2E existe en `tests/e2e/catalogo`).

## 4. Conclusión

No converge: falta la revisión requisito por requisito y la corrida completa de integración y E2E del catálogo.
