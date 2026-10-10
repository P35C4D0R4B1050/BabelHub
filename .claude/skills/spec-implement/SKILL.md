---
name: spec-implement
description: Implementa una feature de BabelHub tarea por tarea con pruebas primero (TDD), siguiendo su tasks.md aprobado y las reglas de AGENTS.md. Úsala cuando una spec esté Planned o InProgress y se pida «implementar», «programar» o «continuar con la siguiente tarea».
---

# spec-implement — Una tarea a la vez

## Precondiciones (si no se cumplen, detente)

- La spec está `Planned` o `InProgress`, con `plan_aprobado_en`, `plan.md` y `tasks.md`.
- `dotnet build BabelHub.sln -warnaserror` y `dotnet test BabelHub.sln` pasan **antes** de empezar.
- La rama de trabajo está limpia (`git status`).

## Ciclo por tarea

1. **Elige** la primera tarea sin marcar de `tasks.md`. Si es la primera de la feature, cambia `estado: InProgress` en la spec (único cambio de estado que te corresponde aquí).
2. **Lee** la tarea, los criterios que cubre y las reglas de la capa (ver `AGENTS.md`). Para crear casos de uso, puertos y adaptadores, sigue la skill `dotnet-feature`.
3. **Escribe la prueba primero**, con `[Trait("Criterio", "AC-XXX-NN")]` y un nombre descriptivo (`Reservar_SinStockVendible_Rechaza`). Confirma que **falla por la razón correcta**.
4. **Implementa lo mínimo** para que pase, en la capa que corresponde. Sin lógica de negocio en `Api` ni `Web`; sin infraestructura en `Dominio` ni `Aplicación`.
5. **Ejecuta** `dotnet build BabelHub.sln -warnaserror`, las pruebas del proyecto afectado, las pruebas de arquitectura y `dotnet run --project tools/SpecCheck`.
6. **Marca** la tarea (`- [x]`) y haz **un commit pequeño** (`feat:`, `test:`, `refactor:`…; ver `docs/07-proceso-de-desarrollo/02-convenciones-tecnicas.md`).
7. Si hubo una decisión no trivial, anótala en `decisions.md`.
8. Pasa a la siguiente tarea. Si algo contradice la spec o el plan, **detente** y consulta: no cambies el comportamiento por tu cuenta.

## Reglas

- Nunca debilites una prueba, una validación ni la prueba de arquitectura para que algo pase.
- No agregues funcionalidad que no esté en la spec.
- Los secretos solo salen de variables de entorno; los archivos digitales, solo del almacén.
- Si una tarea es demasiado grande, divídela en `tasks.md` antes de seguir.
- Al terminar todas las tareas, ejecuta la skill `spec-verify`. No pases la spec a `Done` sin su informe.
