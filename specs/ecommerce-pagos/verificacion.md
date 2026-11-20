---
spec: ecommerce-pagos
fecha: 2026-11-20
---

# Verificación — Pagos

## Resultado: No converge

## 1. Comprobaciones automáticas

| Comprobación | Comando | Resultado |
| :--- | :--- | :--- |
| Compilación sin advertencias | `dotnet build BabelHub.sln -warnaserror` | Correcta (0 errores) |
| Pruebas unitarias del E-commerce | `dotnet test tests/unit/BabelHub.Ecommerce.UnitTests` | 5385 correctas |
| Prueba de arquitectura | `dotnet test tests/architecture` | 243 correctas |
| Integración (PostgreSQL real, `IntegrationTests.Web` y Identidad) | `dotnet test tests/integration/BabelHub.IntegrationTests --filter …` | 924 y 62 correctas en el turno de Docker del 2026-11-20; **no se corrió la solución completa de integración en este cierre** |
| Specs, planes, tareas y trazabilidad | `dotnet run --project tools/SpecCheck` | OK: 20 specs, 0 avisos |
| Búsqueda de secretos | `git grep -nIiE "password\s*=\|api[_-]?key\s*=\|secret\s*=" -- src` | 3 coincidencias, todas falsos positivos (el URI `otpauth://…?secret=` que se muestra una vez para configurar el segundo factor, y la propiedad `opciones.Password` que recibe una clave leída del entorno) |

## 2. Requisito por requisito

No se hizo la revisión requisito por requisito de esta spec en el cierre del 2026-11-20 (solo las comprobaciones automáticas y lo indicado abajo): **no se declara Converge sin ella**.

## 3. Diferencias abiertas

- T31 (batería de integración con el Carrito real), T36 (E2E del pago con Yape y con tarjeta y `ui-review`), T37 y T38 (Culqi real; esperan las llaves de pruebas de B05 de la persona responsable). Ver `specs/ecommerce-pagos/decisiones-de-implementacion.md`.

## 4. Conclusión

No converge: faltan tareas abiertas (T31, T36 a T38) y la matriz de requisitos.
