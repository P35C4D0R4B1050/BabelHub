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
| Pruebas unitarias del E-commerce | `dotnet test tests/unit/BabelHub.Ecommerce.UnitTests` | Correctas (5388 en la última corrida completa) |
| Prueba de arquitectura | `dotnet test tests/architecture` | Correctas (243 en la última corrida completa) |
| Integración con PostgreSQL real | `dotnet test tests/integration/BabelHub.IntegrationTests --filter …` | Reclamaciones y `PagosConElCarritoRealTests`: 142 correctas; `Pedidos\|Envios\|Devoluciones`: 409 correctas; `IntegrationTests.Web`: 938 de 939 (falla `CodificacionDeSalidaTests` por `Devoluciones/ControladorDeDevoluciones.cs`, del carril V, fuera de esta spec) |
| E2E (Chromium de escritorio y de teléfono y WebKit; Firefox, informativo, no se lanza en este equipo) | `npx playwright test acceso reclamaciones administracion pagos base sesion` | 148 correctas y 27 omitidas (rendimiento fuera de Chromium y la prueba pendiente de los textos legales) en la corrida completa; las que fallaron por mis propios selectores o por stock se corrigieron y repitieron en verde |
| Specs, planes, tareas y trazabilidad | `dotnet run --project tools/SpecCheck` | OK: 20 specs, 0 avisos |
| Búsqueda de secretos | `scripts/escanear-secretos.sh repositorio` | Sin fugas (la última corrida completa) |

## 2. Requisito por requisito

No se hizo la matriz requisito por requisito de esta spec: **no se declara Converge sin ella**. La evidencia por tarea está en `specs/ecommerce-pagos/decisiones-de-implementacion.md`.

## 3. Diferencias abiertas

- **T37 y T38** (Culqi real): esperan las llaves de pruebas B05 que debe obtener la persona responsable; sin ellas la pasarela real no se activa (el simulador cubre todo lo demás). Dueño: la persona responsable.
- Falta la matriz requisito por requisito.

## 4. Conclusión

Todas las tareas están cerradas salvo T37 y T38 (B05, de la persona); converge salvo T37/T38 en cuanto se haga la matriz requisito por requisito.
