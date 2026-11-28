# Plan de cierre del Sprint 2 (hasta el 28 de noviembre de 2026)

Decisión de la persona responsable (2026-11-19): entregar el **código** y la **demostración de los atributos de calidad** el 28 de noviembre; despliegue y lo posterior quedan fuera. Este documento fija el alcance, el calendario, cómo se demuestra cada punto de la rúbrica, cómo se ahorra contexto y tokens y cómo se registra lo que se hace. Complementa [05 Desarrollo por bloques](05-desarrollo-por-bloques.md) y el tablero `progress/coordinacion.md`.

## 1. Rúbrica del Sprint 2 y su evidencia

El criterio fundamental: la implementación respeta las decisiones arquitectónicas documentadas y se **demuestra con pruebas** que cumple los atributos de calidad.

| Punto de la rúbrica | Qué lo demuestra | Estado (2026-11-19) |
| :--- | :--- | :--- |
| 1. Estilo arquitectónico | `docs/02`, `docs/04` y las pruebas de arquitectura (238, reglas 1 a 13 de `AGENTS.md`) | Hecho |
| 2. Patrón (Clean Architecture, módulos) | Cuatro capas por servicio, módulos con contrato público; `tests/architecture` | Hecho |
| 3. SDD con criterios de aceptación y tareas técnicas | `specs/<feature>/`: spec EARS, plan, tareas, decisiones, `verificacion.md`; `specs/TRACEABILITY.md` | Hecho (verificación de 6 specs; faltan las demás) |
| 4. Endpoints REST con OpenAPI/Swagger | Contratos v1 (`docs/03/08`, `BabelHub.Shared/Contratos`) y **documento OpenAPI generado por servicio** | **Falta**: bloque OAS-1 |
| 5. Base de datos y caché | `docs/03/05`, `docs/02/07`; **análisis con mediciones** (consultas, índices, Redis de contadores, p95 con y sin caché) | Parcial: bloque DOC-1 |
| 6. SOLID y patrones | Estrategia de selección de fuente, decoradores, puertos y adaptadores; sección en el informe | Hecho (falta el texto de la evidencia) |
| 7. Pruebas automatizadas, cobertura y CI/CD | xUnit (referencial a Jest), Testcontainers, 6600 pruebas, umbrales (entonces Dominio y Aplicación 85 % y total 70 % por servicio; desde el 2026-11-27, por módulo según criticidad, RNF28), `ci.yml` de GitHub Actions | Casi: **reporte de cobertura (HTML)** y primera corrida en GitHub |
| 8. Pruebas de atributos de calidad (k6 hasta 2000 usuarios virtuales) | Escenarios k6 por atributo (`tests/load/`), resultados y gráficas en `progress/evidencias/` | **Parcial**: un solo escenario; bloque K6-1 |

## 2. Alcance congelado (decisión de la persona, 2026-11-19)

- **Se hace:** todo lo que ya está en marcha del recorrido de compra (carrito, pagos, pedidos, envíos, devoluciones, reclamaciones, pantallas de `web-base`) y la **biblioteca digital** (ECO-8).
- **Se difiere** (queda con spec y plan aprobados, sin código): `asistente-compra-ia` (ECO-12) y `web-indice-infinito` (ECO-13). Figuraban ya como los primeros en sacrificarse (`05-desarrollo-por-bloques.md` §6).
- **Fuera del Sprint 2:** despliegue en Oracle, spikes T01 a T05 de `transversal-plataforma`, restauración con carga, Trivy y presupuesto (PLA-R2 a PLA-R4), llaves reales de Culqi y del LLM (B05 y B06).

## 3. Calendario

| Fechas | Objetivo | Quién |
| :--- | :--- | :--- |
| 19 a 22 | Cierre de las specs del recorrido de compra: pedidos (12 tareas), devoluciones (12), envíos (5), reclamaciones (4), pagos (4), carrito (2), `web-base` (8); E2E del recorrido principal en Chromium escritorio, teléfono y WebKit | Carriles A, B, C, V |
| 20 a 26 | **Biblioteca digital** (ECO-8, 26 tareas) en un carril propio | Carril nuevo (relevo) |
| 22 a 25 | **OAS-1** (OpenAPI de los dos servicios), **K6-1** (escenarios y rampa), **DOC-1** (análisis de base de datos y caché), reporte de cobertura | Un carril de evidencias |
| 25 y 26 | `spec-verify` de las specs completas (FIN-1) y corrección de huecos | Carril de verificación |
| 27 | Informe de evidencias y ensayo de la demostración | Coordinador y persona |
| 28 | Entrega | Persona |

Si una fecha se desliza, se recorta primero lo no demostrativo (pantallas de detalle, Menores) y se conserva la evidencia.

## 4. Bloques de evidencia

- **OAS-1:** documento OpenAPI 3 por servicio generado desde el código (no escrito a mano), con rutas, esquemas de los DTO de `Shared`, errores `application/problem+json`, alcances y seguridad (JWT y credencial de servicio); servido en `Development` y publicado como archivo en `docs/03-diseno-de-software/`; una prueba que falle si una ruta pública no está documentada.
- **K6-1:** un escenario por atributo con su umbral como criterio de paso: disponibilidad (p95 menor a 500 ms), confirmación del checkout (p95 menor a 3 s), **concurrencia sin sobreventa** (AC01), límites de uso y resiliencia ante la caída de un servicio; rampa progresiva de 50 hasta 2000 usuarios virtuales contra el entorno local de Docker, con CPU y memoria acotadas como en `docs/02/07`; resultados (JSON y resumen) en `progress/evidencias/`.
- **DOC-1:** documento corto con el análisis de la base de datos (índices usados por las consultas críticas, bloqueos, `FOR UPDATE`, seguridad por filas) y de la caché (qué se cachea, TTL, qué no se cachea y por qué), con las mediciones de K6-1.
- **Cobertura:** `dotnet test --collect:"XPlat Code Coverage"` más un reporte HTML (ReportGenerator) archivado por el CI y copiado a `progress/evidencias/`.

## 5. Control de tokens y contexto

Las sesiones de los carriles superan las 5000 y 7000 mensajes: cada turno arrastra mucho contexto y es lo que más cuesta. Medidas:

1. **Migración de chat por relevo.** Cuando un carril cierra un bloque grande, escribe una nota de relevo en `progress/relevos/<carril>.md` (plantilla en `progress/relevos/README.md`) y la persona abre una sesión nueva que lee solo esa nota, `progress/coordinacion.md` y la spec en curso. Las sesiones viejas se archivan. Se hace al **reiniciar el límite semanal**, no en medio de un bloque.
2. **Fusiones en lote.** El coordinador funde y valida una vez al día (o al cerrar un bloque), no tras cada aviso; los carriles siguen sin esperar.
3. **Integración completa una vez al día** (25 a 40 minutos); entre tanto, solo pruebas del área.
4. **Máximo tres carriles de implementación** durante la fase de evidencia y mensajes de una línea.
5. **Lo repetitivo, a scripts.** Resolución de conflictos de documentos, regeneración de la matriz y las comprobaciones de cierre se hacen con comandos fijos, no con razonamiento.
6. Lo que no demuestra un atributo de calidad no entra en el Sprint 2.

## 6. Registro de lo que se hace

- Cada bloque cerrado deja su **informe** en `progress/log/` (qué se hizo, pruebas, hallazgos, decisiones, lo no verificado).
- `progress/CHANGELOG.md` y `progress/bloques.md` los actualiza el coordinador al fundir.
- Las decisiones tomadas por el coordinador o por delegación de la persona quedan en `progress/decisiones-delegadas.md` o en la fila del hallazgo correspondiente.
- La evidencia del Sprint 2 se reúne en `progress/evidencias/` (resultados de k6, cobertura, OpenAPI y capturas de las pruebas).

## 7. CI/CD y dependencias

- **GitHub Actions:** `.github/workflows/ci.yml` (especificaciones y trazabilidad; compilación sin advertencias; pruebas sin red; pruebas con cobertura y umbrales) y `secretos.yml` (escaneo de secretos). El `push` lo hace la persona; con él corre el flujo y queda la evidencia del CI. Conviene hacerlo pronto para descubrir fallos del flujo con tiempo.
- **Dependabot:** `.github/dependabot.yml` abre pull requests semanales de NuGet (agrupados), acciones, npm de los E2E, imágenes y Compose. Ninguno se fusiona sin pasar el CI y sin revisión; respeta la política de versiones y huellas fijas.

## 8. Riesgos

| Riesgo | Mitigación |
| :--- | :--- |
| El límite de tokens corta el trabajo antes del 28 | Prioridad a la evidencia (sección 4); relevo de chats; alcance congelado |
| 2000 usuarios virtuales exceden la máquina local | Rampa progresiva y se documenta el techo medido; el atributo se demuestra con los umbrales a la carga soportada y la extrapolación documentada |
| Firefox no puede ejecutarse en esta máquina | Informativo (`progress/coordinacion.md` §6); los obligatorios son Chromium (escritorio y teléfono) y WebKit |
| La primera corrida de CI en GitHub revela fallos | Hacer el primer `push` temprano |
