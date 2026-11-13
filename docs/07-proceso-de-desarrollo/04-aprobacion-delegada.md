# Aprobación delegada

El 2026-11-12 la persona responsable del proyecto (Diego Amiquero) **delegó en el agente** la aprobación de specs y planes, con una condición: que no haya observaciones graves, vacíos, contradicciones, problemas ni incidencias. El fin es automatizar las auditorías sin bajar el rigor. Este documento fija las reglas; `AGENTS.md` §2 las hace obligatorias y `tools/SpecCheck` las hace cumplir.

**Ajuste del mismo día (puerta por gravedad).** La primera exigencia, «cero hallazgos de cualquier gravedad», no converge: cada ronda de revisión profundiza y descubre hallazgos nuevos, muchos heredados de specs ya aprobadas. La persona responsable decidió que la puerta se mida **por gravedad**: los hallazgos graves y las contradicciones bloquean; los menores y los heredados no se pierden, pasan a un registro con dueño y fase (`progress/hallazgos.md`).

## 1. Qué se delega y qué no

| Se delega | Sigue siendo de la persona responsable |
| :--- | :--- |
| Pasar una spec de **Draft** a **Approved**. | Decidir comportamiento de negocio o responder una pregunta abierta. |
| Pasar un plan a **Planned**. | Cambiar requisitos (RF, RNF, RC), atributos de calidad o decisiones (AD) con efecto de negocio. |
| Dar una spec por **Done** tras `spec-verify` (ya era del agente). | Modificar `AGENTS.md` o esta delegación, y revocarla. |
| | `git push`, reescribir historia y borrar ramas. |

Una decisión que el agente toma sin que la persona la haya dado (una opción, una cifra, un alcance) **no es aprobable**: se convierte en pregunta abierta en la spec y se consulta. Si la persona ya decidió algo en la conversación, el agente lo registra con su fecha y no lo reinterpreta.

## 2. Gravedad de un hallazgo

Cada revisor clasifica cada hallazgo en una de tres gravedades. Cuando no la indica, el agente la asigna con este criterio:

| Gravedad | Qué es | ¿Bloquea la aprobación? |
| :--- | :--- | :---: |
| **Bloqueante** | Viola una regla de `AGENTS.md`, rompe una garantía de seguridad o de datos, o hace la spec incumplible. | **Sí** |
| **Importante** | Contradicción con `AGENTS.md`, `docs/` u otra spec; requisito o criterio no verificable; vacío de comportamiento en lo que se agregó o cambió; decisión de negocio asumida. | **Sí** |
| **Menor** | Redacción, referencia obsoleta sin efecto, mejora de cobertura, caso límite poco probable, o un hueco **heredado** que ya existía antes del cambio. | No: se registra en `progress/hallazgos.md` |

Un hallazgo heredado que el cambio vuelve **peor o más expuesto** se trata como Importante.

## 3. La puerta: todas las condiciones

La aprobación solo procede si se cumplen **todas**. Si falla una, el agente **no aprueba**, lo reporta y corrige o escala.

| # | Condición | Cómo se comprueba |
| :---: | :--- | :--- |
| G1 | `SpecCheck` sin errores y **sin avisos**. | `dotnet run --project tools/SpecCheck` |
| G2 | **Cero preguntas abiertas** y ninguna decisión pendiente o supuesta en el texto («a definir», «pendiente», «por confirmar»). | `SpecCheck` y lectura de la spec o el plan |
| G3 | **Spec:** el agente `spec-auditor` da **«Lista para aprobar»** o **«Con cambios menores»**, con **cero hallazgos Bloqueantes e Importantes**. **Plan:** consistente con la spec, cubre cada requisito y criterio, y sin hallazgos Bloqueantes ni Importantes. | Agente `spec-auditor` |
| G4 | El agente `arch-reviewer` da **«Cumple»** o **«Cumple con observaciones»**, con cero hallazgos Bloqueantes e Importantes. «No cumple» nunca pasa. | Agente `arch-reviewer` |
| G5 | El agente `security-reviewer` da **«Cumple»** o **«Cumple con observaciones»**, con cero hallazgos Bloqueantes e Importantes. Se ejecuta **siempre**. | Agente `security-reviewer` |
| G6 | **Sin contradicciones** con `AGENTS.md`, `docs/` ni otras specs (términos, cifras, decisiones, dependencias). Una contradicción es siempre al menos Importante. | Los tres agentes y una búsqueda cruzada |
| G7 | Todas las specs de las que depende están **Approved** (o más). Para un plan, además, su spec está Approved. | Encabezado `depende` y `SpecCheck` |
| G8 | Ningún bloqueo abierto de `progress/blockers.md` impide **planificar** esta spec. Los que solo impiden desplegar no cuentan, pero se anotan. | `progress/blockers.md` |
| G9 | La base está sana: `dotnet build BabelHub.sln -warnaserror` y `dotnet test BabelHub.sln` en verde. | Compilación y pruebas |
| G10 | La fila del registro está escrita **antes** de cambiar el estado, con la huella del contenido auditado, y los hallazgos Menores y heredados están en `progress/hallazgos.md`. | `progress/aprobaciones.md` y `progress/hallazgos.md` |

Para un plan, G4 y G5 revisan el plan y las tareas; G3 comprueba su consistencia con la spec. Un plan cuyo diseño obligue a cambiar la spec **no se aprueba**: la spec vuelve a Draft.

## 4. Procedimiento

1. Revisar con los tres agentes (G3 a G5). Cada uno recibe la ruta exacta, lo decidido por la persona (con su fecha) y la instrucción de **clasificar cada hallazgo** como Bloqueante, Importante o Menor.
2. Si hay hallazgos **Bloqueantes o Importantes**: corregir el origen, nunca el reporte; **repetir la revisión completa** de los tres agentes sobre el texto corregido. Un cambio posterior a una revisión limpia, por pequeño que sea, obliga a revisar de nuevo.
3. Los **Menores y los heredados** se anotan en `progress/hallazgos.md` (spec, hallazgo, gravedad, dueño y fase en que se resuelve); no se pierden ni bloquean.
4. Máximo **tres rondas** (excepción del 2026-11-12: la persona responsable autorizó rondas adicionales una a una para `transversal-auditoria`, `transversal-plataforma` e `identidad-seguridad`, hasta la 11 en esta última, con verificaciones acotadas a lo corregido desde la ronda 6; no sienta precedente). Si persisten hallazgos Bloqueantes o Importantes, o si uno exige una decisión de negocio, el agente **no aprueba**: deja la spec o el plan en su estado, registra el motivo y se lo plantea a la persona.
5. Con las condiciones cumplidas: calcular la huella (`dotnet run --project tools/SpecCheck -- --huella <spec> spec|plan`), agregar la fila a `progress/aprobaciones.md` y recién entonces escribir en el frontmatter de la spec `estado`, `aprobado_por` y `aprobado_en` (o `plan_aprobado_por` y `plan_aprobado_en`).
6. Regenerar `TRACEABILITY.md`, ejecutar `SpecCheck` otra vez y resumirle a la persona lo aprobado antes de cualquier commit.

La firma del agente es siempre **`Claude (delegado por Diego Amiquero)`**. El agente nunca firma con el nombre de la persona.

## 5. Lo que el agente no puede hacer

- Aprobar algo que él mismo cambió después de la última revisión limpia.
- Quitar o suavizar un requisito, un criterio o una regla para que un hallazgo desaparezca, ni reclasificar un hallazgo grave como Menor para evitarlo. Si el hallazgo es válido se corrige el origen; si parece erróneo, se explica y se pregunta.
- Editar `speccheck.json`, las pruebas, los agentes revisores o este documento para que una puerta pase.
- Aprobar con avisos de `SpecCheck`, con preguntas abiertas o con un veredicto «No cumple» o «Necesita otra vuelta».
- Aprobar en bloque sin revisar cada spec o plan por separado.

## 6. Lo que hace cumplir `SpecCheck`

Una spec o un plan firmados como «Claude (delegado …)» fallan la validación si:

- no hay fila de su tipo en `progress/aprobaciones.md`, o la fecha de la fila no coincide con la del frontmatter;
- la fila no tiene `0/0` en `SpecCheck`, un veredicto aceptable de cada revisor («Lista para aprobar» o «Con cambios menores»; «Cumple» o «Cumple con observaciones») y **0 hallazgos graves**;
- la fila declara hallazgos Menores y la spec no figura en `progress/hallazgos.md`;
- **el contenido cambió después de aprobar** (la huella no coincide). Una spec aprobada no se modifica en silencio: vuelve a Draft y se reaprueba.

Un plan en `Planned` exige además `plan_aprobado_por`. Las pruebas de esta puerta están en `tests/tools/BabelHub.SpecCheck.Tests` y fallan si alguien la debilita.

`SpecCheck` no puede comprobar que los agentes se ejecutaron: lo que ve es el registro y la huella. Por eso la persona puede auditar cuando quiera `progress/aprobaciones.md`, `progress/hallazgos.md` y los informes de revisión que cada aprobación deja en el log de la sesión.

## 7. Auditorías automáticas

| Cuándo | Qué se ejecuta | Quién |
| :--- | :--- | :--- |
| Antes de cada aprobación | La puerta G1 a G10. | Agente |
| Antes de cada commit | `SpecCheck`, compilación estricta y pruebas. | Agente |
| Al cerrar una fase | Los tres revisores sobre todas las specs y planes de la fase, y una búsqueda de contradicciones entre `docs/`, specs, planes, prototipo e infraestructura. Informe `progress/R<n>-…md`. Se revisa `progress/hallazgos.md`. | Agente |
| Al cerrar una spec | `spec-verify` (`Resultado: Converge`). | Agente |

Cualquier incidencia que aparezca en una auditoría se registra (hallazgo, decisión o bloqueo) y se corrige antes de seguir si es grave; si es Menor, va a `progress/hallazgos.md`.

## 8. Revocar o ajustar

La persona puede revocar la delegación o pedir que una aprobación se revierta en cualquier momento: basta con volver la spec a Draft. Cambiar esta delegación exige actualizar `AGENTS.md` y este documento en el mismo cambio.
