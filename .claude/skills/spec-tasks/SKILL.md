---
name: spec-tasks
description: Divide el plan de una feature en tareas atómicas ordenadas de adentro hacia afuera (tasks.md), cada una con su criterio de aceptación. Úsala cuando exista plan.md y se pida «generar las tareas» o «dividir el trabajo».
---

# spec-tasks — Tareas atómicas

Produce `specs/<feature>/tasks.md` a partir de `plan.md` y de la spec.

## Formato obligatorio

Una línea por tarea (lo valida `SpecCheck`):

```
- [ ] T01 [Dominio] Descripción concreta (cubre: AC-XXX-01, XXX-01)
```

## Reglas

1. **Orden de adentro hacia afuera:** Dominio → Aplicación → Infraestructura → Api → Web → pruebas de integración y E2E. Las dependencias entre tareas van de menor a mayor número.
2. **Atómicas:** cada tarea cabe en un commit pequeño, deja el proyecto compilando y empieza por una prueba que falla.
3. **Cobertura:** cada criterio `AC-XXX-NN` aparece en al menos una tarea; si no, `SpecCheck` falla.
4. **Verbos concretos:** «Crear la regla de stock vendible no negativo», no «Trabajar en inventario».
5. **Riesgo primero:** las tareas de concurrencia, idempotencia e integraciones externas van lo antes posible.
6. Separa claramente: reglas del dominio (pruebas unitarias sin base de datos), casos de uso (puertos simulados), adaptadores (Testcontainers) y pantallas (E2E con Playwright).
7. Incluye tareas de pruebas de arquitectura o de contrato solo si el plan las exige.

## Pasos

1. Lee el plan y la spec.
2. Redacta las tareas por capa y numéralas `T01`, `T02`…
3. Ejecuta `dotnet run --project tools/SpecCheck` y corrige.
4. Detente: la persona responsable aprueba el plan y las tareas juntos y escribe `plan_aprobado_en`. Tú no cambias el estado a `Planned`.
