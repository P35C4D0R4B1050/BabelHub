---
name: spec-clarify
description: Revisa una spec en Draft buscando ambigüedades, requisitos no verificables y huecos, y resuelve sus preguntas abiertas con la persona responsable. Úsala antes de pedir la aprobación de una spec o cuando una spec tenga preguntas abiertas.
---

# spec-clarify — Dejar una spec lista para aprobar

Objetivo: que la spec pueda aprobarse sin sorpresas: sin preguntas abiertas, sin ambigüedad y con criterios que se puedan convertir en pruebas.

## Pasos

1. Lee la spec y ejecuta `dotnet run --project tools/SpecCheck`. Corrige primero lo que marque.
2. Revisa cada requisito con esta lista:
   - ¿Se puede escribir una prueba que lo demuestre? Si no, es ambiguo.
   - ¿Hay palabras vagas («rápido», «adecuado», «fácil», «etc.»)? Reemplázalas por una medida o quítalas.
   - ¿Mezcla dos comportamientos? Divídelo.
   - ¿Nombra tecnología? Muévela al plan.
   - ¿Contradice un documento de `docs/` o el prototipo? Anota la diferencia.
3. Revisa los **casos que suelen faltar**: permisos y alcance por sucursal, concurrencia, fallos de dependencias (inventario, pasarela, almacén, notificador), idempotencia, estados vacíos, datos inválidos, auditoría, privacidad.
4. Para cada pregunta abierta, usa **AskUserQuestion** con 2 a 4 opciones, tu recomendación primero y el impacto de cada una. Pregunta solo lo que cambie el comportamiento; lo técnico se decide en el plan.
5. Con cada respuesta: actualiza los requisitos y criterios afectados, marca la pregunta como `[x]` y escribe la decisión debajo.
6. Vuelve a ejecutar `SpecCheck` (con `--write-matrix`). Si cambió el comportamiento, revisa también las specs que dependen de esta.
7. Entrega un resumen: qué cambió, qué quedó resuelto y qué falta. **No** marques la spec como `Approved`: pide a la persona responsable que la apruebe.

## Reglas

- No resuelvas por tu cuenta una pregunta que cambie reglas del negocio.
- No amplíes el alcance: lo que sobra se anota como «Fuera» con su spec destino o como nueva pregunta.
- Para una revisión independiente, invoca al agente `spec-auditor`.
