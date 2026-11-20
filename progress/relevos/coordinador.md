# Relevo del coordinador (orquestador de carriles) — 2026-11-20, mediodía

Lo lee la sesión que sustituya a esta coordinadora (la anterior fue «BabelHub documentación y specs», `local_d8be7a47-23e4-4bfc-bb70-a6ab3180bb2b`; la sesión que la supervisa es `local_4f688b16-77a2-4d47-be4c-5f74ffd2114b`). Rol: **orquestar los carriles, fundir sus ramas a `main`, validar y documentar**; no implementar. Responder en español y conciso. Commits en inglés (`tipo: descripción`) con `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. **`push` solo si la persona lo pide de forma expresa** (el del 2026-11-19 ya se hizo: `main` en GitHub `P35C4D0R4B1050/BabelHub`, repositorio público; `main` va ~120 commits por delante de `origin`). La identidad de git está configurada solo en el repositorio. No commitear `.claude/launch.json`.

## 1. Plazo y plan
Entrega del Sprint 2 el **28-nov-2026** (código + demostración de atributos de calidad; sin despliegue). Plan: `docs/07-proceso-de-desarrollo/06-plan-sprint-2.md`. Alcance congelado: se mantiene la **biblioteca digital (ECO-8)**; **diferidos** asistente (ECO-12) e índice infinito (ECO-13). Tablero y protocolo de carriles: `progress/coordinacion.md`. Reglas de aceptación (delegadas por la persona el 2026-11-20): nada se da por aprobado sin revisores (`arch-reviewer`, `security-reviewer`, `spec-auditor`) sin Bloqueantes ni Importantes, pruebas verdes (unitarias, arquitectura, integración, E2E, SpecCheck 0 avisos, `build -warnaserror`) y como máximo 3 rondas; las decisiones de negocio y los cambios a `AGENTS.md` son de la persona.

## 2. Estado
`main` (commit más reciente `git log -1`): compilación sin advertencias, SpecCheck 0 avisos, unitarias 5376 (E-commerce) y 2375 (Inventario), arquitectura 243, integración 7161 de 7165 (fallos: `HumoDeImagenes` ×2 conocidos, una carrera de biblioteca que pasa sola, y la copia de adaptadores ya espejada), escáner de secretos limpio. **Evidencias hechas:** OpenAPI por servicio (`docs/03/…`, OAS-1), escenarios k6 y corrida oficial (`progress/evidencias/k6/`, K6-1), análisis de base de datos y caché (`docs/03/09`, DOC-1), reporte de cobertura 94–95 % (`progress/evidencias/cobertura/`). **Pendiente de evidencias:** la rampa final 50→2000 con 0,6/0,4/512m (la serie se colgó y **Docker Desktop quedó caído**: lo arranca la persona; sin él no hay integración completa ni E2E) y el informe final de la rúbrica (`progress/evidencias/README.md`). Avance de tareas por spec: ver `specs/TRACEABILITY.md` y `progress/bloques.md` (~80 %).

## 3. Carriles (sesiones y ramas)
| Carril | Sesión | Rama | Encargo vigente |
| :--- | :--- | :--- | :--- |
| A | `local_12f04d8c-43d9-41bc-9592-054fc014da73` | `claude/competent-khayyam-1d6d28` | Composición restante, Importantes de plataforma del Sprint 2 (TRA-22, SEG-31, tope de 24 correos/IDE-7), `spec-verify` de identidad, auditoría y seguridad |
| B | `local_75d2d3d4-8736-4c3e-965a-cde0b2dd3686` | `claude/affectionate-cerf-885469` | `ecommerce-pedidos` ECO-6g (T33–T36 con módulos reales, T41 E2E+`ui-review`), Menores, E2E del carrito mixto/digital/visitante, `spec-verify` de pedidos y carrito |
| C | `local_a7fa1f27-83bc-4a14-9260-371f3c15326f` | `claude/keen-ishizaka-8f8cfa` | Pagos T31 (T37/T38 esperan llaves B05), reclamaciones T22, WEB-2 `ui-review`/E2E, resto de `web-base` (FUE-09/FUE-11 diferidas), `spec-verify` |
| V | `local_119e83ae-6a50-4013-b1bc-766c8e6ad703` (sustituye a `local_2fc08a84-…`) | `claude/youthful-knuth-d27c46` | ECO-10d revisores, devoluciones T23–T34, envíos T14b/T20/T21b/T22/T24, `spec-verify`; si su contexto está saturado, relevo (`carril-v.md`) y sesión nueva |
| Biblioteca | `local_5aa35e79-1741-4cfc-8a8b-9e07b1f770c2` | `claude/biblioteca-6cca64` | ECO-8 T01–T24 en `main`; T25 (E2E + `ui-review`) y `spec-verify` |
| Evidencias | `local_da271b21-9668-412c-af5b-e70ff3800a1a` | `claude/evidencias-431ff7` | Rampa final y DOC-1 con 0,6/0,4/512m; informe final de la rúbrica |
| Rendimiento | `local_e8867e8e-bc7d-4168-92b4-ff0551656b27` | `claude/eager-davinci-c26d43` | Cerrado (~31 → ~18 sentencias por solicitud; memoria opt-in apagada) |
Turnos de máquina (Docker): evidencias → B → C → biblioteca; V y A, filtrados y cortos; integración completa final del coordinador.

## 4. Cómo se funde y se valida (procedimiento probado)
1. `git merge --no-ff -m "merge: <rama>" claude/<rama>`; los conflictos en `progress/` (p. ej. `hallazgos.md`, filas nuevas en ambos lados) se resuelven quitando marcas (unión). **Los conflictos en código se resuelven a mano**, nunca quitando marcas a ciegas.
2. `dotnet build BabelHub.sln -warnaserror -v q`, `dotnet run --project tools/SpecCheck --no-build -- --write-matrix`, unitarias de los dos servicios y arquitectura; commitear la matriz.
3. Integración completa solo en un **turno**: `dotnet test tests/integration/BabelHub.IntegrationTests --no-build` (21–40 min, con Docker; en segundo plano, salida a archivo) + `bash scripts/escanear-secretos.sh repositorio`. Se repiten sueltos los fallos que parezcan carreras o contención. Flaky conocidos: `HumoDeImagenes`, `CargaDelAcumulado`, `ArranqueSeguroEcommerce`, el escáner por tiempo, conciliaciones simultáneas del catálogo, una carrera de concesión de la biblioteca (corregida; vigilar).
4. Documentar: `progress/bloques.md`, `progress/CHANGELOG.md` (entrada nueva arriba), `progress/hallazgos.md` (siete columnas), `progress/coordinacion.md`; commitear. Python no está instalado: usar `sed`/Edit.

## 5. Decisiones de la persona vigentes
Alcance congelado (sección 1); tope de reserva de 20 unidades por línea y 30 líneas (DD-16); residual de 75 min; hora de Lima: se registra el desvío; lista de contraseñas SecLists; **memoria de 5 s de referencias del Inventario apagada** y **VM-INVENTARIO = Inventario 0,6 CPU y 512 MB, PostgreSQL 0,4 CPU** (DD-17, `docs/02/07` con nota fechada); FUE-09 y FUE-11 diferidas («converge salvo FUE-09/FUE-11» en `verificacion.md` de `inventario-fuentes`); Firefox informativo, obligatorios Chromium escritorio/teléfono y WebKit. **Pendientes suyos:** abrir Docker Desktop, tareas externas B03–B07 y spikes T01–T05 (fuera del Sprint 2), llaves de Culqi (B05) y del LLM (B06), `push` cuando quiera.

## 6. Trampas conocidas
Fixtures de PostgreSQL y de claves sin literales (`Password=…`, claves AWS): se arman por variable o concatenación; catálogos de auditoría y alertas y las copias de adaptadores se **espejan en los dos servicios** (`CopiasDeAdaptadoresTests`) y cada motivo nuevo lleva su texto español en el presentador; ningún carril declara contratos de un módulo ajeno; nada de excepciones nuevas a pruebas de arquitectura; `Shared` solo aditivo; pruebas con reloj fijo no deben compararse con `now()`; no matar procesos `dotnet` globalmente; un solo cliente k6 por corrida (una serie colgada por duplicados dejó caído Docker); las sesiones son largas: pedir relevo.

## 7. Qué hacer al arrancar
1. `git status`, `git log --oneline -5`, ramas por delante de `main` (`git rev-list --count main..<rama>`), últimos informes de `progress/log/`.
2. Comprobar Docker (`docker ps`); si no responde, pedir a la persona que abra Docker Desktop.
3. Fundir y validar lo pendiente (sección 4); repartir turnos de máquina.
4. Cuando todo esté cerrado: `spec-verify` por spec terminada, integración completa final, escáner de secretos, matriz, CHANGELOG/bloques y resumen para la persona (tareas por spec, lo que quedó fuera con motivo, resultados de pruebas, cobertura y k6).
