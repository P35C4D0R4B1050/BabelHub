---
spec: nombre-de-la-feature
fecha: AAAA-MM-DD
---

# Verificación — Título de la feature

> La produce la skill `spec-verify` al terminar las tareas. Una spec solo pasa a `Done` si el resultado es **Converge**.

## Resultado: Converge | No converge

## 1. Comprobaciones automáticas

| Comprobación | Comando | Resultado |
| :--- | :--- | :--- |
| Compilación sin advertencias | `dotnet build BabelHub.sln -warnaserror` | |
| Pruebas | `dotnet test BabelHub.sln` | |
| Prueba de arquitectura | `dotnet test tests/architecture` | |
| Specs, planes, tareas y trazabilidad | `dotnet run --project tools/SpecCheck` | |
| Búsqueda de secretos | `git grep -nIiE "password\s*=|api[_-]?key\s*=|secret\s*=" -- src` | |

## 2. Requisito por requisito

| Requisito | Evidencia en código | Criterio y prueba | Estado |
| :--- | :--- | :--- | :--- |
| XXX-01 | `src/...:línea` | AC-XXX-01 · `tests/...` | Cumple / Parcial / No cumple |

## 3. Diferencias encontradas

Lista de diferencias entre la spec y el código, y qué se hizo con cada una (corregir el código o corregir la spec con nueva aprobación).

## 4. Conclusión

Una frase: por qué converge, o qué falta.
