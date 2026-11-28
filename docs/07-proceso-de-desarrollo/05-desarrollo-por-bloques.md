# Desarrollo por bloques, carriles y rigor graduado

El 2026-11-14 la persona responsable (Diego Amiquero) decidió, para llegar al producto completo en el plazo que le queda (2 a 3 semanas hasta producción): **graduar el rigor según el riesgo de cada pendiente, trabajar en bloques y avanzar por carriles en paralelo, con los mismos criterios de calidad**. Este documento fija cómo. No sustituye a `AGENTS.md` ni a [la aprobación delegada](04-aprobacion-delegada.md): lo que choca con ellas está en la sección 8 como propuesta pendiente de la persona, y **no rige** hasta que ella las modifique.

## 1. Qué cambia y qué no

| Cambia | No cambia (en ningún nivel de rigor) |
| :--- | :--- |
| Cuánto se valida y se revisa **por tarea** (según el nivel de la sección 2). | Una spec **Approved** y un plan **Planned** antes de programar; el ciclo y sus puertas de `01-flujo-sdd.md`. |
| La validación completa y la revisión pasan a hacerse **por bloque** (sección 3). | `dotnet build BabelHub.sln -warnaserror` sin advertencias y las pruebas de arquitectura. |
| Varias sesiones avanzan a la vez en **carriles** con un protocolo de integración (sección 4). | Cada criterio de aceptación con al menos una prueba que lo nombra (`SpecCheck`) y el informe de cobertura. |
| Tu intervención se concentra en los **cierres de bloque**. | Sin secretos, sin datos personales y sin archivos digitales en el repositorio; sin debilitar pruebas, validaciones ni hooks. |
| | Las decisiones de negocio, `AGENTS.md` y el `push` siguen siendo tuyos. |

## 2. Niveles de rigor

El nivel depende del **riesgo** de lo que toca la tarea, no de cuánto «importa» la feature. Cada tarea de un bloque lleva su nivel en `progress/bloques.md`. Una tarea que toque un área de un nivel superior **sube a ese nivel** (el nivel lo manda la parte más riesgosa).

| Nivel | Qué toca | Por tarea | Al cerrar el bloque |
| :--- | :--- | :--- | :--- |
| **Crítico (C)** | Autenticación y sesiones, autorización y alcance por sucursal, pagos y reembolsos, privacidad y datos personales, auditoría, stock y concurrencia (reservas, ajustes, traslados), archivos digitales y URLs firmadas, respaldo, restauración y despliegue, secretos e integraciones. | Prueba primero, con casos negativos y de concurrencia donde aplique; `build` estricto, pruebas de arquitectura y las pruebas del área; actualizar `decisiones-de-implementacion.md`. | Validación completa, `gitleaks`, **`security-reviewer` y `arch-reviewer`** sobre lo del bloque, y cualquier hallazgo Bloqueante o Importante se corrige antes de cerrarlo. |
| **Estándar (E)** | Reglas y flujos de negocio sin riesgo de seguridad directo: catálogo, carrito, pedidos, envíos, devoluciones, reclamaciones, consultas, trabajos de fondo. | Prueba primero por criterio; `build` estricto y las pruebas del área. | Validación completa, `gitleaks` y **`arch-reviewer`**. |
| **Bajo (B)** | Listados y pantallas de lectura, estados de pantalla, configuración, constancias, documentación, ajustes de estilo. | Una prueba por criterio y `build` estricto. | Validación completa y `gitleaks`; revisión de arquitectura solo si el bloque agregó referencias entre proyectos. |

Las pantallas siguen `ui-screen` y `ui-review` (paridad con el prototipo, temas, teclado, contraste, estados) y se revisan en el cierre del bloque, no por tarea.

## 3. Bloques

Un **bloque** son 4 a 10 tareas contiguas de una misma spec (o muy acopladas), de un solo carril, con un entregable verificable.

1. **Antes de empezar**: se lee la spec, el plan, el `tasks.md`, `decisions.md` y la sección de `progress/ajustes-al-implementar.md`; se anota el nivel de cada tarea en `progress/bloques.md`.
2. **Dentro del bloque** (ciclo rápido, una tarea a la vez dentro del carril): prueba que falla → código → `dotnet build -warnaserror` → solo las pruebas del área (`dotnet test --filter`) → `SpecCheck` si se tocó una spec, un plan o `tasks.md` → **un commit por tarea** (`feat: … (Tnn)`), con la tarea marcada `[x]`. Las pruebas pesadas o de tiempo real no se corren por tarea.
3. **Al cerrar el bloque** (puerta de cierre):
   - `dotnet build BabelHub.sln -warnaserror`, `dotnet run --project tools/SpecCheck` (con `--write-matrix`), la suite completa de cada proyecto **por separado** (la integración sola, para no provocar fallos de tiempo real) y `scripts/escanear-secretos.sh repositorio`.
   - Revisión por nivel (sección 2), con los agentes en paralelo.
   - Una sola actualización de `decisiones-de-implementacion.md`, la bitácora (`progress/log/`), el CHANGELOG y la memoria del proyecto.
   - **Informe del bloque** en `progress/log/AAAA-MM-DD-<bloque>.md`: qué se hizo, tareas y commits, pruebas, hallazgos y su gravedad, decisiones tomadas, lo no verificable y lo siguiente. Es lo único que la persona necesita leer.
4. **Paradas obligatorias** (el trabajo autónomo se detiene y se pregunta): una decisión de negocio o un requisito que no está en la spec, un hallazgo Bloqueante o Importante de seguridad, una puerta que falla y no se arregla corrigiendo el origen, un conflicto de integración entre carriles o algo que obligaría a cambiar `AGENTS.md` o un plan aprobado.

La persona ya no pide «Continúa con Tnn»: pide **«Ejecuta el bloque X»**.

## 4. Carriles en paralelo

Hasta **tres** sesiones simultáneas, cada una en su propio **worktree y rama**, sobre conjuntos de archivos distintos:

| Carril | Contenido |
| :--- | :--- |
| **A — Transversales** | Cierre de `transversal-plataforma` y `transversal-seguridad`, `transversal-auditoria`, `identidad-seguridad` y `transversal-notificaciones`. |
| **B — Inventario** | Las cuatro specs de `inventario-*`. |
| **C — E-commerce y web** | `web-base`, catálogo, carrito y checkout, pagos, pedidos, biblioteca digital, envíos, devoluciones, reclamaciones, asistente e índice infinito. |

Reglas de integración:

- **Contratos primero.** Antes de que B y C escriban código que se toque entre sí, se congelan los contratos REST entre el E-commerce y el Inventario y sus DTOs de `BabelHub.Shared`; hasta que Identidad esté lista, los dos usan el puerto de «usuario actual» de prueba (decisión de la fase 3 de `01-flujo-sdd.md`). Un cambio de contrato se hace en un bloque propio y avisa a los otros carriles.
- **Dueños de archivos.** Cada carril solo toca su spec y su código. `BabelHub.sln`, `BabelHub.Shared`, `progress/CHANGELOG.md`, `progress/hallazgos.md`, `specs/TRACEABILITY.md` y la memoria los toca **el integrador** al fundir un bloque (la persona o la sesión que ella designe); los carriles dejan lo que necesitan en su informe de bloque.
- **Fusión a `main`.** Un bloque cerrado se funde a `main` (sin `push`) y se corre la validación completa **sobre el resultado fundido**: es la puerta que hace seguro el paralelismo. Un carril no sigue con el bloque siguiente hasta que el anterior esté fundido.
- **Sin atajos.** Ninguna sesión debilita una prueba, una validación o un hook para que su bloque pase, ni edita una spec o un plan aprobados (vuelven a Draft y se reaprueban).

## 5. Suite rápida y suite completa

| Cuándo | Qué corre |
| :--- | :--- |
| Por tarea | `dotnet build … -warnaserror` y `dotnet test <proyecto> --filter …` de lo que cambió. |
| Cierre de bloque | Todo, proyecto por proyecto, con la integración sola; `SpecCheck`; `gitleaks`. |
| Integración continua | Todo, más cobertura y pruebas sin red. |

Las pruebas que levantan contenedores o miden tiempo real están en colecciones aparte (`Procesos pesados`) y no se corren en paralelo con el resto.

## 6. Medición y orden de sacrificio

- Al cerrar cada bloque se anota en `progress/bloques.md` cuántas tareas se hicieron y cuánto tardó. Tras el primer bloque de cada carril se **reestima el calendario** y se informa a la persona.
- **El producto completo es el objetivo.** Si el calendario no alcanza, la persona decide qué se difiere, en este orden acordado de antemano (de lo que se difiere primero a lo último): `web-indice-infinito` → `asistente-compra-ia` → avisos por WhatsApp en producción (queda el notificador simulado) → `ecommerce-devoluciones` → `ecommerce-envios` avanzados → `ecommerce-biblioteca-digital`. No se difiere nada de identidad, inventario, catálogo, carrito, pagos, pedidos, auditoría, respaldo ni despliegue. Lo que se difiere queda con su spec y su plan intactos.

## 7. Lo que depende de la persona y corre en paralelo

La cuenta de Oracle y el almacén (B03), el dominio y Cloudflare (B04), las llaves de pruebas de Culqi (B05), la clave del proveedor de LLM (B06), el número de WhatsApp (B07), los spikes T01 a T05 de `transversal-plataforma` y las comprobaciones contra el almacén real que quedaron anotadas (`scripts/almacen.sh comprobar`, `infra/vm-aux/README.md`). Tienen plazos fuera del control de las sesiones: conviene empezarlos ya.

## 8. Propuestas pendientes de la persona (no rigen todavía)

Estas partes chocan con reglas que solo la persona puede cambiar. Aquí queda el texto exacto para que decida; ninguna sesión las aplica por su cuenta.

1. **`AGENTS.md` §2, «Se trabaja una tarea a la vez»** → *«Se trabaja una tarea a la vez **por carril** (cada sesión, en su rama), de adentro hacia afuera…»*. Sin este cambio, el paralelismo de la sección 4 contradice la letra de la regla.
2. **Puerta graduada de planes** (hoy los tres revisores en todo plan y toda spec, `AGENTS.md` §2 y `04-aprobacion-delegada.md` G3 a G5). Propuesta: `security-reviewer` obligatorio en los planes y las specs de nivel **Crítico** (identidad, auditoría, pagos, reservas y ajustes de stock, biblioteca digital, plataforma y seguridad); en el resto, `spec-auditor` y `arch-reviewer`, y `security-reviewer` solo si el plan toca autenticación, datos personales, pagos, archivos digitales, límites o integraciones (como ya dice `AGENTS.md` §5). Cambiarlo exige, **en el mismo cambio**: `AGENTS.md` §2, `04-aprobacion-delegada.md` (G5 y §3 a §6), la skill `spec-approve`, `tools/SpecCheck` (la fila de `progress/aprobaciones.md` con veredicto de seguridad opcional por nivel) y las pruebas de `tests/tools/BabelHub.SpecCheck.Tests`. Mientras no se haga, cada plan nuevo pasa por los tres revisores; se pueden **planificar y revisar en lote** las specs de una misma fase, que el flujo ya permite.
3. **Revisión por bloque en lugar de por tarea** no choca con `AGENTS.md` (el commit sigue siendo por tarea y `CLAUDE.md` pide `build` y las pruebas *afectadas* tras editar código), por lo que rige desde ya.
