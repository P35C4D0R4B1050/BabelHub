---
name: spec-new
description: Crea o completa el borrador (Draft) de la spec de una feature de BabelHub a partir de la plantilla, los requisitos de docs/ y el prototipo. Úsala cuando se pida «escribir la spec de X», «crear una spec nueva» o cuando una feature de specs/ no tenga spec.md.
---

# spec-new — Escribir el borrador de una spec

Produce `specs/<feature>/spec.md` en estado **Draft**. Describe **qué y por qué**, nunca cómo. No escribe código ni plan.

## Antes de empezar

1. Lee `AGENTS.md`, `specs/README.md` y `docs/glossary.md`.
2. Identifica en `docs/01` a `docs/03` las necesidades (NN), requisitos (RF, RNF, RC) y atributos de calidad (AC) que la feature cubre. Usa **solo** identificadores que existan.
3. Si la feature tiene pantalla en `prototype/ui-v2/`, léela para entender el comportamiento esperado (estados, validaciones, textos). El prototipo informa, no manda: si contradice a `docs/`, gana `docs/` y se anota la diferencia como pregunta abierta.
4. Mira una spec ya aprobada o escrita del repositorio para igualar el nivel de detalle.

## Pasos

1. Copia `specs/_template/spec.md` a `specs/<feature>/spec.md` y completa el frontmatter: `id` (igual a la carpeta), `titulo`, `estado: Draft`, `servicio`, `prefijo` (3 letras únicas), `origen`, `calidad` y `depende`.
2. **Propósito** y **Alcance** (dentro y fuera, con el nombre de la spec que cubre lo que queda fuera).
3. **Requisitos EARS** (tabla `ID | Patrón | Requisito | Origen`):
   - Un comportamiento verificable por requisito.
   - Patrones: *Ubicuo*, *Evento* («Cuando…»), *Estado* («Mientras…»), *No deseado* («Si…, entonces…») y *Opcional* («Donde…»).
   - Siempre con «deberá». Sin tecnologías (bases de datos, frameworks, proveedores, JWT, REST…); `speccheck.json` lista las prohibidas.
   - Cada requisito con su origen (RF, NN, RNF, AC, RC).
4. **Criterios de aceptación** (`AC-XXX-NN`, columna «Cubre» y escenario **Dado / cuando / entonces**). Cada requisito debe quedar cubierto; incluye los casos de rechazo y de permisos.
5. **Reglas e invariantes**, **Casos límite y errores**.
6. **Preguntas abiertas**: todo lo que no esté en `docs/` y necesite una decisión. Con propuesta y responsable. **No inventes la respuesta.**
7. Ejecuta `dotnet run --project tools/SpecCheck -- --write-matrix` y corrige hasta que pase.

## Reglas

- Nunca escribas `aprobado_por`, `aprobado_en` ni cambies el estado a `Approved`: lo decide una persona.
- Si la spec obliga a cambiar `docs/`, propón el cambio y espera confirmación.
- Termina deteniéndote para la revisión humana: resume la spec en pocas líneas y lista las preguntas abiertas.
