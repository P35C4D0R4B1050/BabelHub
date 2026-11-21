---
spec: web-base
fecha: 2026-11-21
---

# Verificación — Base de la interfaz web

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

No se hizo la matriz requisito por requisito de esta spec: **no se declara Converge sin ella**. La evidencia por tarea está en `specs/web-base/decisiones-de-implementacion.md`.

## 3. Diferencias abiertas

- **T28** y **T35** sin marcar por un defecto real abierto (hallazgo Importante, dueño: carril A con la Web): aceptar los textos legales no avanza porque la versión de la Web (provisional) nunca coincide con la de Identidad; la prueba «al aceptar los textos vigentes…» está como `fixme`.
- **T32, T33 y T34**: las partes automáticas (T32a, T33a, T34a) pasan; las partes humanas (navegadores y móviles reales, lista manual con lector de pantalla, dos rondas de usabilidad con personas) siguen abiertas, dueño: la persona; fase: posterior al Sprint 2. Entregables listos: `docs/06-experiencia-de-usuario/10-lista-manual-de-accesibilidad.md` y `progress/usabilidad/rondas.md`.
- Falta la matriz requisito por requisito.

## 4. Conclusión

No converge: T28 y T35 dependen de cerrar el hallazgo de las versiones de los textos legales; T32 a T34 esperan sus partes humanas; falta la matriz de requisitos.
