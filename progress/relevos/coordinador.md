# Relevo del coordinador (orquestador de carriles) — 2026-11-19, noche

Lo lee la sesión nueva que sustituya a «BabelHub documentación y specs» (`local_d8be7a47-23e4-4bfc-bb70-a6ab3180bb2b`). Rol: **orquestar los carriles, fundir sus ramas a `main`, validar y documentar**; no implementar. Responder en español y conciso. Commits en inglés (`tipo: descripción`) con `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. **`push` solo si la persona lo pide de forma expresa** (el del 2026-11-19 ya se hizo: `main` en GitHub `P35C4D0R4B1050/BabelHub`, repositorio público). La identidad de git está configurada solo en el repositorio.

## 1. Plazo y plan
Entrega del Sprint 2 el **28-nov-2026** (código + demostración de atributos de calidad; sin despliegue). Plan: `docs/07-proceso-de-desarrollo/06-plan-sprint-2.md` (rúbrica→evidencia, calendario, bloques de evidencia **OAS-1** OpenAPI, **K6-1** k6 hasta 2000 usuarios virtuales, **DOC-1** base de datos y caché, cobertura; control de tokens). Alcance: se mantiene la **biblioteca digital (ECO-8)** en un carril nuevo; **diferidos** asistente (ECO-12) e índice infinito (ECO-13). Tablero y protocolo de carriles: `progress/coordinacion.md`.

## 2. Estado (481 de 611 tareas, 79 %)
Completas: catálogo, identidad, inventario (4 specs), auditoría, notificaciones, seguridad, carrito (35/37). En curso: pedidos 30/42, devoluciones 22/34, envíos 20/25, reclamaciones 19/23, pagos 34/38, `web-base` 31/39 (WEB-2 en código, sin marcar hasta `ui-review`/E2E), plataforma 24/42 (fuera del Sprint 2). `main` validado: unitarias ~5200 y 2356, arquitectura 238, SpecCheck 0 avisos, integración 6655/6659 (4 fallos por contención/Docker). CI de GitHub en marcha tras el `push`; Dependabot configurado.

## 3. Carriles (sesiones y ramas)
| Carril | Sesión | Rama | Encargo vigente |
| :--- | :--- | :--- | :--- |
| A | `local_12f04d8c-43d9-41bc-9592-054fc014da73` | `claude/competent-khayyam-1d6d28` | D20 opción (b): `Id` de la fuente = `Guid` derivado del código (espejo de `IdentificadorDeSucursal` en el Inventario); luego 3 hallazgos de WEB-2 (lista de usuarios, dispositivo reconocido, renovación de la sesión actual); su relevo en `progress/relevos/carril-a.md` |
| B | `local_75d2d3d4-8736-4c3e-965a-cde0b2dd3686` | `claude/affectionate-cerf-885469` | Sembrado de catálogo e inventario para el E2E del carrito (T36) y ECO-6f (pedidos T31 en adelante) |
| C | `local_a7fa1f27-83bc-4a14-9260-371f3c15326f` | `claude/keen-ishizaka-8f8cfa` | WEB-2 cerrado (T28–T31 sin marcar); siguiente: E2E/`ui-review` cuando haya host sembrado, T36–T38 de pagos |
| V | `local_2fc08a84-e2a9-4cc6-8973-ffa07c63ba84` | `claude/silly-chaum-f7213a` | Devoluciones (ECO-10e a 10f), envíos pendientes; ECO-10d sin revisiones (se detuvo por límite) |
Las de planificación (`local_815ad1f2-…`) ya no tienen encargo.

## 4. Cómo se funde y se valida (procedimiento probado)
1. `git merge --no-ff --no-commit claude/<rama>`; si hay conflictos en `progress/`, `specs/`, `docs/`, `infra/secretos-requeridos.txt` se resuelven quitando marcas (unión). **Los conflictos en código se resuelven a mano**: nunca quitar marcas a ciegas (ocurrió y rompió el build; en `AccionesDeAuditoria.cs`, `ClienteDelInventario.cs` y `PlanDeMigracionDelEcommerce.cs` hay que unir).
2. `dotnet build BabelHub.sln -warnaserror`, `dotnet run --project tools/SpecCheck -- --write-matrix`, unitarias de los dos servicios y arquitectura; commit.
3. Integración completa solo en un **turno**: `dotnet test tests/integration/BabelHub.IntegrationTests --no-build` (25–40 min, requiere Docker Desktop; su disco está en `D:\Docker\wsl\disk` con enlace desde `C:`) + `bash scripts/escanear-secretos.sh repositorio`. Los carriles piden turno para pruebas filtradas y esperan mi aviso. Fallos conocidos y flaky que pasan sueltos: `HumoDeImagenesTests`, `CarrerasYLimitesDeReservasPostgresTests…Vencida`, `DependenciasExternasTests`, conciliaciones simultáneas del catálogo.
4. Documentar: `progress/bloques.md`, `progress/CHANGELOG.md` (entrada nueva arriba), hallazgos; commitear. No commitear `.claude/launch.json`.

## 5. Decisiones vigentes de la persona
Alcance congelado (sección 1); Firefox informativo, obligatorios Chromium escritorio/teléfono y WebKit (instalado, `webkit-2370`; Firefox bloqueado por el sistema, no tocar la seguridad); Node.js v26 instalado; umbral de 50 bloqueos por 15 min confirmado; delegación al carril B de los hallazgos de carrito; D18, D19 y D20 aprobadas; exención estrecha a `ReglasDeAuditoriaTests` registrada. **Resueltas el 2026-11-19:** tope de reserva 20 por línea y 30 líneas (el cambio de código es de B, prioritario); residual de 75 min confirmado; hora de Lima: se registra el desvío; lista de contraseñas: SecLists 10k (MIT) incorporada filtrada (ver `infra/licencias/contrasenas-comunes.md`). **Pendientes suyos:** FUE-09 y FUE-11 sin dueño en `web-base` (recomendado: diferirlas y anotar «converge salvo FUE-09/FUE-11» en `verificacion.md` de `inventario-fuentes`), tareas externas B03–B07 y spikes T01–T05 (fuera del Sprint 2), excepción del sistema para Firefox o E2E de Firefox en GitHub (opcional), reglas de rondas extra de planes si ocurre.

## 6. Trampas conocidas (decirlas a los carriles nuevos)
Fixtures de PostgreSQL sin literales `Password=…` (cuatro veces); catálogos de auditoría y alertas se **espejan en los dos servicios** (`CopiasDeAdaptadoresTests`) y cada motivo nuevo lleva su texto español en el presentador; ningún carril declara contratos de un módulo ajeno (los publica su dueño); nada de excepciones nuevas a pruebas de arquitectura; `Shared` solo aditivo; no matar procesos `dotnet` globalmente; las sesiones son largas: pedir relevo.

## 7. Qué hacer al arrancar
1. `git status`, `git log --oneline -5` y `git branch` con commits por delante de `main`; leer los últimos informes de `progress/log/`.
2. Fundir y validar lo pendiente (sección 4).
3. Abrir carriles nuevos por relevo (cards `start_session` con `source_branch: main`, brief corto que apunte a este archivo y a su relevo): **evidencias** (OAS-1, K6-1, DOC-1, cobertura), **biblioteca (ECO-8)**, y relevo de B, C y V cuando cierren su bloque.
4. Seguir el calendario del plan (sección 3 de `06-plan-sprint-2.md`).
