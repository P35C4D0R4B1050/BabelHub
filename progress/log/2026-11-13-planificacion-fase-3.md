# 2026-11-13 — Planificación de la fase 3 (planes y tareas)

## Qué se hizo

- Decisiones de la persona responsable (en `progress/decisiones-previas-al-plan.md`): módulos propios `Plataforma`, `Auditoria` y `Notificaciones` en el E-commerce (el Inventario con seis módulos); retiro de respaldos por el almacén; tope por número de 5 por hora y 20 en 24 horas **solo para clientes** (contacto de sucursal exento) y riesgo aceptado de omitir avisos críticos; aviso de TRA-22 por correo; retención de avisos (90 días) y de números desactivados; historial de n8n y Evolution a 7 días.
- Se escribieron `plan.md`, `decisions.md` y `tasks.md` de `transversal-plataforma`, `transversal-auditoria`, `transversal-seguridad`, `transversal-notificaciones` e `identidad-seguridad`. Cada `tasks.md` cubre todos los requisitos y criterios de su spec (verificado con bucles de shell).
- `transversal-notificaciones` (spec Draft) se reabrió y se amplió: NOT-14 (tope), NOT-15 (límites o Inventario no responden) y NOT-16 (exención de sucursal y precedencia), con AC-NOT-14 a AC-NOT-16.
- Se actualizaron `docs/03/04` (módulos y contratos), `docs/03/05` (avisos), `docs/05/04` (avisos y números desactivados), `docs/02/05` y `docs/02/07` (Redis dedicado de contadores, regla de red de n8n), la prueba de arquitectura y el esqueleto de carpetas.

## Revisiones

Cuatro rondas de `arch-reviewer` y `security-reviewer` sobre cada plan (la cuarta, una ronda extra autorizada por la persona el 2026-11-13) más `spec-auditor` sobre la spec reabierta y, en la última, sobre los planes de plataforma y seguridad.

- Sin Bloqueantes ni Importantes en la ronda final: `transversal-seguridad` (arquitectura y seguridad), `transversal-plataforma` (arquitectura y seguridad), `transversal-notificaciones` (arquitectura, seguridad y puerta de la spec), `transversal-auditoria` (seguridad) e `identidad-seguridad` (seguridad).
- Importantes abiertos tras la ronda final: `transversal-auditoria` y `identidad-seguridad` (arquitectura; ya corregidos en el texto, sin revisión posterior); `spec-auditor` sobre `transversal-plataforma` (5 Importantes: PLA-41 sin cubrir en sus tareas, orden de tareas, respaldo bajo demanda, canal del informe de intenciones y puerto de `trabajos_de_fondo`; ya corregidos en el texto) y sobre `transversal-seguridad` (4 Importantes: contrato de `IControlDeLimites` para Notificaciones, algoritmo de bloqueo de SEG-29, tareas sin dueño para AC-SEG-09 y AC-SEG-23, y `IMotorDeLimites` como puerto de Aplicación; sin corregir).

## Estado

**Ningún plan ni la spec reabierta se aprobó.** La puerta permite tres rondas y la persona autorizó una más; todo cambio posterior a una revisión limpia exige revisar de nuevo. Los cinco planes siguen en Draft, `transversal-notificaciones` sigue en Draft y las otras 19 specs, Approved. `SpecCheck` da 0 errores y 5 avisos (planes presentes con la spec sin pasar a Planned), `dotnet build -warnaserror` y las pruebas están en verde. No se hizo commit ni push.

## Pendiente

- Corregir los 4 Importantes de `spec-auditor` sobre `transversal-seguridad` y volver a pasar la puerta completa (spec-auditor, arch-reviewer y security-reviewer sobre el mismo texto) por cada plan.
- Aprobar `transversal-notificaciones` (spec) y luego los planes limpios con la skill `spec-approve`.
- Planificar `web-base` (hueco registrado para pantallas de avisos, `web.sesiones_web`, paso de restauración y prueba de que el JWT no llega al navegador).
- Registrar en `progress/hallazgos.md` los Menores que queden y los heredados nuevos (ya hay filas de esta sesión).

## Actualización: segunda ronda extra (autorizada por la persona el 2026-11-13)

- **Spec aprobada:** `transversal-notificaciones` (16 requisitos y 16 criterios, NOT-14 a NOT-16) pasó a Approved con la fila de `progress/aprobaciones.md` (huella `fda7c4a56947e2ed`) escrita antes del cambio de estado.
- **Planes (todos siguen en Draft):** esta ronda revisó los cinco planes con `arch-reviewer`, `security-reviewer` y `spec-auditor`. Resultado final por revisor, sobre el texto que tenían en ese momento:
  - Sin Bloqueantes ni Importantes: arquitectura y seguridad de `transversal-auditoria`; arquitectura y seguridad de `transversal-seguridad`.
  - Importantes que se corrigieron después de su revisión: `spec-auditor` sobre seguridad (contrato de límites), plataforma (informe con identificadores en el repositorio, arquitectura y seguridad) , notificaciones (contradicción de intentos, D12 y un diagrama de docs/02), auditoría (docs/03/04 línea 84) e identidad (clasificación del bloqueo temporal, custodia de la clave de firma y orden entre planes).
- Cada corrección posterior a una revisión limpia obliga a repetir la revisión de los tres agentes sobre el mismo texto; todos los planes cambiaron después de su última revisión, así que ninguno cumple la puerta con el texto actual.
- Decisión de negocio pendiente (D26 de seguridad): con el vaciado de la ventana al crear `blq:`, la tasa efectiva por cuenta es de unos 40 fallos por hora en SEG-29; la alternativa es no vaciar el conjunto (bloqueo de hasta una hora).

## Cierre: planes aprobados (2026-11-13)

- Tras la verificación final autorizada por la persona, los cinco planes de la fase pasaron a **Planned** por delegación, con una fila de `progress/aprobaciones.md` cada uno (escrita antes del cambio de estado): `transversal-plataforma` (`aa18c37b85fabb6e`), `transversal-auditoria` (`182eb9c21a64edbd`), `identidad-seguridad` (`8fe773e86f811f25`), `transversal-notificaciones` (`4e0312a49dc049d8`) y `transversal-seguridad` (`7e95e7b771aa0902`). La spec de notificaciones se había aprobado antes (`fda7c4a56947e2ed`).
- Decisión de la persona sobre D26: la ventana de intentos no se vacía al crear un bloqueo (máximo 10 fallos por hora en SEG-29).
- `SpecCheck`: 0 errores y 0 avisos; `dotnet build -warnaserror` y las pruebas en verde. Nada commiteado ni subido.
- Los Menores quedaron en `progress/hallazgos.md` (varias filas de este día); los más relevantes para implementar: partir tareas grandes (T15 y T22 de plataforma, T34 de identidad), alinear textos de las specs de seguridad y auditoría al reabrirlas y la pantalla de avisos de `web-base`.
- Pendiente: planificar `web-base` y `web-indice-infinito`, y empezar a implementar con `spec-implement` por el orden `transversal-seguridad` T04 y T14, luego plataforma, auditoría, identidad y notificaciones.

## Corrección posterior: fixture de la puerta de aprobación

- `tests/tools/BabelHub.SpecCheck.Tests/PuertaDeAprobacionTests.cs`: el fixture solo devolvía a Draft las specs «Approved por delegación» de su copia temporal; con los planes en `Planned` dos pruebas fallaban. Se amplió la expresión regular para revertir también `Planned` y sus campos `plan_aprobado_*` (autorizado por la persona el 2026-11-13). No se debilitó ninguna verificación de la puerta. Resultado: `dotnet test BabelHub.sln` en verde.

## Implementación: T01 de transversal-seguridad y trazabilidad de SpecCheck

- T01 (política de límite del Dominio, ambos servicios) implementada con pruebas primero (26 por servicio) y commiteada.
- Con autorización de la persona («Sí, relajarla»), `SpecCheck` ya no exige desde `InProgress` que todos los criterios los nombre una prueba: en `InProgress` solo se exigen los criterios cuyas tareas citadas en `tasks.md` están todas marcadas; en `Done`, todos. Cuatro pruebas nuevas en `PuertaDeAprobacionTests` y el fixture ahora también devuelve a Draft las specs `InProgress`. Documentado en `docs/07/01-flujo-sdd.md` y `03-estrategia-de-pruebas.md`.
- `transversal-seguridad` pasó a `InProgress` (único cambio de estado que corresponde a `spec-implement`).
