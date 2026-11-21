---
spec: ecommerce-pagos
fecha: 2026-11-21
---

# Verificación — Pagos

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

No se hizo la matriz requisito por requisito de esta spec en el cierre del 2026-11-21: **no se declara Converge sin ella**. La evidencia por tarea está en `specs/ecommerce-pagos/decisiones-de-implementacion.md`.

## 3. Diferencias abiertas

- **T37 y T38** (Culqi real): esperan las llaves de pruebas de B05 que debe obtener la persona responsable; sin ellas la pasarela real no se activa (el simulador cubre el resto). Dueño: la persona responsable.
- Falta la matriz requisito por requisito (spec-verify completo).

## 4. Conclusión

No converge: T31 y T36 ya pasan y están marcadas, pero siguen abiertas T37 y T38 (dependen de B05) y falta la matriz de requisitos.
