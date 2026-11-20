---
spec: ecommerce-libro-reclamaciones
fecha: 2026-11-20
---

# Verificación — Libro de Reclamaciones

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

- T22: falta el E2E de la gestión de la administración (AC-REC-06 y AC-REC-10); el lado de la tienda está verificado en Chromium de escritorio y teléfono y WebKit (8 recorridos). T19 a T21 figuran sin marcar aunque su parte con PostgreSQL real está hecha: revisar qué falta antes de marcarlas.

## 4. Conclusión

No converge: faltan T19 a T22 por cerrar y la matriz de requisitos.
