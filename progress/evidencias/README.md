# Informe de evidencias del Sprint 2

Punto por punto de la rúbrica de `docs/07-proceso-de-desarrollo/06-plan-sprint-2.md` §1. El criterio fundamental: la implementación respeta las decisiones arquitectónicas documentadas y se **demuestra con pruebas** que cumple los atributos de calidad. Cada punto indica **qué se demuestra, dónde se ve y cómo se reproduce**; lo que no se pudo demostrar (o se demuestra a medias) se dice en «Límites».

Fecha de las mediciones: 2026-11-20 (máquina de desarrollo con Docker Desktop; no es la VM ARM de despliegue).

| # | Punto de la rúbrica | Evidencia principal |
| :-- | :--- | :--- |
| 1 | Estilo arquitectónico | [`docs/02`](../../docs/02-arquitectura-de-software/03-estilo-arquitectonico.md) y 243 pruebas de arquitectura |
| 2 | Patrón (Clean Architecture, módulos) | [`docs/03/01`](../../docs/03-diseno-de-software/01-clean-architecture.md), [`docs/03/04`](../../docs/03-diseno-de-software/04-modulos-y-contratos.md), `tests/architecture` |
| 3 | SDD: criterios de aceptación y tareas | `specs/<feature>/`, [`specs/TRACEABILITY.md`](../../specs/TRACEABILITY.md), `tools/SpecCheck` |
| 4 | REST con OpenAPI | [`docs/03/openapi/`](../../docs/03-diseno-de-software/openapi/README.md) (generado del código) |
| 5 | Base de datos y caché | [`docs/03/09`](../../docs/03-diseno-de-software/09-analisis-de-base-de-datos-y-cache.md) con mediciones de k6 |
| 6 | SOLID y patrones | [`docs/03/03`](../../docs/03-diseno-de-software/03-principios-y-patrones.md) y los ejemplos de abajo |
| 7 | Pruebas, cobertura y CI/CD | [`cobertura/index.html`](cobertura/index.html), `.github/workflows/ci.yml` |
| 8 | Atributos de calidad con k6 (hasta 2 000 usuarios) | [`k6/`](k6/) y [`tests/load/`](../../tests/load/README.md) |

## 1. Estilo arquitectónico

- **Qué:** dos servicios (E-commerce e Inventario), cada uno un **monolito modular**, comunicados solo por REST; el estilo, sus tácticas y su presupuesto de recursos están decididos y justificados en `docs/02-arquitectura-de-software/` (drivers, decisiones AD, estilo, escenarios, despliegue, tácticas, presupuesto de recursos).
- **Se demuestra:** las reglas 1, 2, 5, 6 y 13 de `AGENTS.md` (capas, referencias entre servicios, `Shared` solo contratos, módulos por contrato público) y parte de la 9 las hace cumplir `tests/architecture` (**243 pruebas**, verdes): si una referencia de proyecto o de tipo viola la regla, la compilación de pruebas falla.
- **Reproducir:** `dotnet test tests/architecture`.

## 2. Patrón: Clean Architecture con módulos

- **Qué:** cuatro capas por servicio (`Domain`, `Application`, `Infrastructure`, `Api`), con puertos en la Aplicación y adaptadores en la Infraestructura; los módulos se ven como carpetas con **contrato público** (la raíz) e `Interno`.
- **Se demuestra:** las mismas pruebas de arquitectura (`ReglasDeReferenciasTests`, `ReglasDeModulosTests`, `ContratoPublicoDe*Tests`); el Dominio no referencia nada de infraestructura; ningún módulo toca el `Interno` de otro.
- **Dónde leerlo:** `docs/03-diseno-de-software/01-clean-architecture.md`, `02-organizacion-interna.md` y `04-modulos-y-contratos.md`.

## 3. SDD con criterios de aceptación y tareas técnicas

- **Qué:** nada se programa sin spec **Approved** (EARS, criterios `AC-XXX-NN`) y plan **Planned**; cada feature tiene `spec.md`, `plan.md`, `tasks.md`, `decisions.md` y, al cerrar, `verificacion.md`.
- **Se demuestra:** `tools/SpecCheck` valida estructura, tareas y trazabilidad (23 pruebas propias) y regenera [`specs/TRACEABILITY.md`](../../specs/TRACEABILITY.md): cada criterio tiene al menos una prueba que lo nombra con `[Trait("Criterio", "AC-XXX-NN")]`. Las aprobaciones delegadas quedan en `progress/aprobaciones.md` y los hallazgos en `progress/hallazgos.md`.
- **Verificaciones convergentes (`Resultado: Converge`):** `inventario-fuentes`, `inventario-seleccion-fuente`, `inventario-traslados`, `inventario-ajustes`, `transversal-auditoria` y `transversal-seguridad`. Las demás están en el cierre FIN-1.
- **Reproducir:** `dotnet run --project tools/SpecCheck`.

## 4. Endpoints REST con OpenAPI

- **Qué:** un documento OpenAPI 3 por servicio, **generado desde el código** (no escrito a mano): [`inventario.v1.json`](../../docs/03-diseno-de-software/openapi/inventario.v1.json) (las 41 operaciones del contrato v1 más 3 rutas de usuario y las sondas) y [`ecommerce.v1.json`](../../docs/03-diseno-de-software/openapi/ecommerce.v1.json) (las rutas que el host mapea hoy). Rutas, esquemas de los DTO de `BabelHub.Shared`, errores `application/problem+json`, alcance de la credencial y esquemas de seguridad (credencial de servicio, JWT de usuario, sesión web). En `Development` cada servicio lo sirve en `/openapi/v1.json`.
- **Se demuestra:** `DocumentacionOpenApiInventarioTests` y `DocumentacionOpenApiEcommerceTests` **fallan si una ruta pública no está documentada** y comparan los archivos publicados con el documento generado.
- **Reproducir:** `dotnet test tests/integration/BabelHub.IntegrationTests --filter "FullyQualifiedName~DocumentacionOpenApi"` (regeneración en el README de la carpeta). Informe: `progress/log/2026-11-20-OAS-1.md`.
- **Límites:** el documento del E-commerce lista lo que el host mapea hoy; las rutas de claves públicas (JWKS), n8n, pasarela y auditoría están descritas y aparecen cuando el host las mapee (hallazgos abiertos del carril de composición).

## 5. Base de datos y caché

- **Qué:** [`docs/03/09`](../../docs/03-diseno-de-software/09-analisis-de-base-de-datos-y-cache.md): consultas críticas con sus índices (plan de ejecución y `pg_stat_statements`), bloqueos y concurrencia (`SELECT … FOR UPDATE` ordenado, AC01), seguridad por filas, qué se cachea y qué no, y la **capacidad medida frente a recursos** (§6b) y el costo por solicitud antes y después de reducirlo (§6c).
- **Hallazgos con arreglo y prueba:** el tope del grupo de conexiones superaba el límite de la cuenta (500 «too many connections»; ahora 16 por omisión, `LimiteDeConexionesDeLaBaseTests`); la base caída respondía 500 (ahora 503 uniforme, `BaseCaidaEs503Tests`); ≈ 31 → ≈ 18 idas a la base por solicitud (`CostoPorSolicitudDeDisponibilidadTests`).
- **Se dice sin maquillar:** la caché de disponibilidad **no existe** (la regla de stock manda; D05 de `inventario-fuentes`); el análisis explica por qué y qué la reemplazaría. Hay caché implementada para los contadores de límites del E-commerce (Redis dedicado) y las claves públicas de Identidad (en memoria). La conclusión inicial «la base no es el cuello de botella» era errónea y se corrigió con `docker stats`: **el cuello es la CPU de PostgreSQL**.

## 6. SOLID y patrones

Catálogo y criterios en [`docs/03/03`](../../docs/03-diseno-de-software/03-principios-y-patrones.md). Ejemplos en el código:

| Principio / patrón | Dónde | Qué se ve |
| :--- | :--- | :--- |
| **O (abierto/cerrado) y Estrategia** | `IEstrategiaDeSeleccion`, `RegistroDeEstrategiasDeSeleccion`, `SelectorDeFuentes` (`Inventario.Application/Reservas`) | Una nueva regla de elección de fuente se agrega como estrategia sin tocar el selector |
| **Decorador** | `TransaccionPorCasoDeUso`, `ValidacionDeEntrada`, `LecturaCoherentePorCasoDeUso`, `ReintentoPorInterbloqueo` | Transacción, validación y reintento envuelven a los casos de uso sin ensuciarlos |
| **D (inversión de dependencias) y Puertos/Adaptadores** | puertos en `Application`, adaptadores en `Infrastructure` (`INotificador`, almacén de objetos, pasarela) | El Dominio y la Aplicación no conocen EF Core, Redis ni `HttpClient`; lo hace cumplir `tests/architecture` |
| **S (responsabilidad única)** | un caso de uso por clase, módulos con contrato público | Cada módulo cambia por una sola razón; sin controladores con reglas |
| **I (segregación de interfaces)** | contratos por módulo y por operación (`CatalogoDeOperaciones`) | Cada cliente depende solo de las operaciones que usa |

## 7. Pruebas automatizadas, cobertura y CI/CD

- **Qué:** xUnit (unitarias sin base ni red; integración con Testcontainers), pruebas de arquitectura, E2E con Playwright y carga con k6. Corrida de cobertura del 2026-11-20: 243 de arquitectura, 2 368 y 5 376 unitarias, 6 960 de 6 973 de integración, 15 de `CoverageCheck` y 23 de `SpecCheck`.
- **Cobertura:** [`cobertura/index.html`](cobertura/index.html) (reporte HTML por servicio, capa y archivo) y su [resumen](cobertura/README.md): **E-commerce 95,5 % en Dominio y Aplicación y 94,6 % total; Inventario 95,7 % y 93,7 %**, contra los umbrales de 85 % y 70 %. Las 13 pruebas de integración que fallaron en esa corrida están listadas con su dueño en ese resumen (no son de esta evidencia).
- **CI/CD:** `.github/workflows/ci.yml` (especificaciones y trazabilidad, compilación sin advertencias, pruebas sin red, pruebas con cobertura y umbrales; archiva el reporte HTML) y `secretos.yml` (escaneo de secretos); `dependabot.yml`. **El flujo todavía no se ha ejecutado en GitHub** (el `push` lo hace la persona): no hay corrida de CI que mostrar.
- **Reproducir:** `dotnet build BabelHub.sln -warnaserror`, `dotnet test BabelHub.sln --collect:"XPlat Code Coverage" --settings tests/coverlet.runsettings` y `dotnet run --project tools/CoverageCheck -- --html`.

## 8. Atributos de calidad con k6 (50 hasta 2 000 usuarios virtuales)

- **Qué:** un escenario por atributo, con su umbral como criterio de paso, contra un entorno local en Docker con los límites de CPU y memoria de VM-INVENTARIO ([`tests/load/README.md`](../../tests/load/README.md)). k6 corre con la imagen oficial `grafana/k6` fijada por huella.
- **Resultados con el dimensionamiento declarado por DD-17 (Inventario 0,6 CPU / 512 MB, PostgreSQL 0,4 CPU)**, corrida final sobre `main` en [`k6/2026-11-20-final-escenarios/`](k6/2026-11-20-final-escenarios/):

| Escenario | Atributo | Resultado |
| :--- | :--- | :--- |
| [`disponibilidad`](k6/2026-11-20-final-escenarios/disponibilidad.md) | RNF02: 50 usuarios, p95 < 500 ms | **No cumple**: p95 908 ms; repetida con el servicio caliente, 897 ms ([caliente](k6/2026-11-20-final-escenarios-caliente/disponibilidad.md)); 0 % de errores |
| [`checkout-confirmacion`](k6/2026-11-20-final-escenarios/checkout-confirmacion.md) | checkout: p95 < 3 s | **Cumple** (p95 108 ms) |
| [`sobreventa`](k6/2026-11-20-final-escenarios/sobreventa.md) | AC01: 100 usuarios por 10 unidades | **Cumple**: 10 reservas y 90 rechazos |
| [`limites-de-uso`](k6/2026-11-20-final-escenarios/limites-de-uso.md) | límites (API4) | **Cumple**: 100 permitidas, el resto 429; el vecino sin afectar |
| [`resiliencia`](k6/2026-11-20-final-escenarios/resiliencia.md) | RNF03: PostgreSQL pausado 25 s | **Cumple**: 503 uniforme, ningún 500, recuperación sola |
| [`rampa-de-usuarios`](k6/2026-11-20-final/inventario-0.6cpu_postgres-0.4cpu_pool-16/rampa-de-usuarios.md) | escalabilidad 50 → 2 000 | **No cumple**: p95 1,4 s a 50 usuarios y 16,9 s con 2 000 (51 % de errores) |

  La corrida de la mañana, con el dimensionamiento anterior (0,3 CPU / 350 MB; [`k6/2026-11-20/`](k6/2026-11-20/)), cumplió RNF02 (p95 327 ms); esa cifra **no se reprodujo** en la serie final (hubo ruido del host y entró la reducción de idas a la base entre ambas), así que el informe no la presenta como vigente.

- **Capacidad medida frente a recursos:** tabla completa en [`docs/03/09` §6b](../../docs/03-diseno-de-software/09-analisis-de-base-de-datos-y-cache.md) (series en [`k6/2026-11-20-capacidad/`](k6/2026-11-20-capacidad/), [`k6/2026-11-20-costo-despues/`](k6/2026-11-20-costo-despues/) y la final sobre `main` en [`k6/2026-11-20-final/`](k6/2026-11-20-final/); §6b-bis). Conclusiones sin maquillar:
  1. **Ninguna configuración medida sostiene 2 000 usuarios virtuales con p95 < 500 ms** en esta máquina; la mejor (4 CPU de servicio + 2 de base) da p95 de 7,2 s sin errores.
  2. **El cuello de botella es la CPU** (de PostgreSQL y del Inventario, que cuestan unos 8 y 6 a 12 ms por solicitud). **Con el dimensionamiento declarado (DD-17), RNF02 no se cumple en la serie final** (≈ 900 ms a 50 usuarios con pausa de 0,5 s, unas 80 solicitudes por segundo frente a las 44 que se atienden); con 1 CPU para cada servicio sí en la rampa a 50 usuarios (425 ms).
  3. 2 000 usuarios con pausa de 0,5 s son ≈ 4 000 solicitudes por segundo, inalcanzables en 1 OCPU; con pausas de persona (5 a 10 s) son 200 a 400 por segundo, lo que sostiene 4 + 2 CPU con latencia degradada.
  4. Bajo sobrecarga el sistema **se degrada de forma controlada** (503 y plazos vencidos), sin 500 ni sobreventa.
- **Reproducir:** `bash tests/load/entorno/preparar-carga.sh`, `bash tests/load/ejecutar.sh <escenario>` y `bash tests/load/entorno/serie-de-capacidad.sh`; luego `bash tests/load/entorno/bajar-carga.sh`.
- **Límites:** los números absolutos son de esta máquina (k6 comparte CPU con el entorno), no de la VM ARM; se mide solo el Inventario (el tramo del E-commerce se medirá cuando su Web esté completa) y la pasarela no entra.

## Qué falta o se demuestra a medias

- Corrida de CI en GitHub (depende del `push` de la persona).
- Verificación (`spec-verify`) de las specs restantes (cierre FIN-1).
- **RNF02 con el dimensionamiento declarado (0,6 / 0,4)**: medido y no cumplido en la serie final; la decisión de qué hacer (más CPU, menor costo por solicitud o aceptar la lectura de 50 personas reales con 5 a 10 s entre acciones) es de la persona responsable.
- Hallazgos abiertos de otros carriles en `progress/hallazgos.md` (por ejemplo, rutas del E-commerce aún sin mapear en el host).
