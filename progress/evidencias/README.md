# Informe de evidencias del Sprint 2

Punto por punto de la rúbrica de `docs/07-proceso-de-desarrollo/06-plan-sprint-2.md` §1. El criterio fundamental: la implementación respeta las decisiones arquitectónicas documentadas y se **demuestra con pruebas** que cumple los atributos de calidad. Cada punto indica **qué se demuestra, dónde se ve y cómo se reproduce**; lo que no se pudo demostrar (o se demuestra a medias) se dice en «Límites».

Fechas: mediciones de carga y cobertura del 2026-11-20 y estado de cierre del 2026-11-21 (`progress/bloques.md`, «Estado de cierre»). Máquina de desarrollo con Docker Desktop; no es la VM ARM de despliegue. La hoja única para la entrega es [`docs/07-proceso-de-desarrollo/07-evidencias-sprint-2.md`](../../docs/07-proceso-de-desarrollo/07-evidencias-sprint-2.md); este informe es el detalle.

| # | Punto de la rúbrica | Evidencia principal |
| :-- | :--- | :--- |
| 1 | Estilo arquitectónico | [`docs/02`](../../docs/02-arquitectura-de-software/03-estilo-arquitectonico.md) y 244 pruebas de arquitectura |
| 2 | Patrón (Clean Architecture, módulos) | [`docs/03/01`](../../docs/03-diseno-de-software/01-clean-architecture.md), [`docs/03/04`](../../docs/03-diseno-de-software/04-modulos-y-contratos.md), `tests/architecture` |
| 3 | SDD: criterios de aceptación y tareas | `specs/<feature>/`, [`specs/TRACEABILITY.md`](../../specs/TRACEABILITY.md), `tools/SpecCheck` |
| 4 | REST con OpenAPI | [`docs/03/openapi/`](../../docs/03-diseno-de-software/openapi/README.md) (generado del código) |
| 5 | Base de datos y caché | [`docs/03/09`](../../docs/03-diseno-de-software/09-analisis-de-base-de-datos-y-cache.md) con mediciones de k6 |
| 6 | SOLID y patrones | [`docs/03/03`](../../docs/03-diseno-de-software/03-principios-y-patrones.md) y los ejemplos de abajo |
| 7 | Pruebas, cobertura y CI/CD | [`cobertura/index.html`](cobertura/index.html), `.github/workflows/ci.yml` |
| 8 | Atributos de calidad con k6 (hasta 2 000 usuarios) | [`k6/`](k6/) y [`tests/load/`](../../tests/load/README.md) |

## 1. Estilo arquitectónico

- **Qué:** dos servicios (E-commerce e Inventario), cada uno un **monolito modular**, comunicados solo por REST; el estilo, sus tácticas y su presupuesto de recursos están decididos y justificados en `docs/02-arquitectura-de-software/` (drivers, decisiones AD, estilo, escenarios, despliegue, tácticas, presupuesto de recursos).
- **Se demuestra:** las reglas 1, 2, 5, 6 y 13 de `AGENTS.md` (capas, referencias entre servicios, `Shared` solo contratos, módulos por contrato público) y parte de la 9 las hace cumplir `tests/architecture` (**244 pruebas**, verdes): si una referencia de proyecto o de tipo viola la regla, la compilación de pruebas falla.
- **Reproducir:** `dotnet test tests/architecture`.

## 2. Patrón: Clean Architecture con módulos

- **Qué:** cuatro capas por servicio (`Domain`, `Application`, `Infrastructure`, `Api`), con puertos en la Aplicación y adaptadores en la Infraestructura; los módulos se ven como carpetas con **contrato público** (la raíz) e `Interno`.
- **Se demuestra:** las mismas pruebas de arquitectura (`ReglasDeReferenciasTests`, `ReglasDeModulosTests`, `ContratoPublicoDe*Tests`); el Dominio no referencia nada de infraestructura; ningún módulo toca el `Interno` de otro.
- **Dónde leerlo:** `docs/03-diseno-de-software/01-clean-architecture.md`, `02-organizacion-interna.md` y `04-modulos-y-contratos.md`.

## 3. SDD con criterios de aceptación y tareas técnicas

- **Qué:** nada se programa sin spec **Approved** (EARS, criterios `AC-XXX-NN`) y plan **Planned**; cada feature tiene `spec.md`, `plan.md`, `tasks.md`, `decisions.md` y, al cerrar, `verificacion.md`.
- **Se demuestra:** `tools/SpecCheck` valida estructura, tareas y trazabilidad (23 pruebas propias) y regenera [`specs/TRACEABILITY.md`](../../specs/TRACEABILITY.md): cada criterio tiene al menos una prueba que lo nombra con `[Trait("Criterio", "AC-XXX-NN")]`. Las aprobaciones delegadas quedan en `progress/aprobaciones.md` y los hallazgos en `progress/hallazgos.md`.
- **Estado de cierre (2026-11-21, `progress/bloques.md`):** 547 de 611 tareas (89 %). **Siete specs en `Done`** con `Resultado: Converge`: `ecommerce-biblioteca-digital`, `ecommerce-carrito-checkout`, `ecommerce-catalogo`, `ecommerce-devoluciones`, `ecommerce-libro-reclamaciones`, `ecommerce-pedidos` e `inventario-traslados`. Las demás con verificación convergen **salvo** excepciones registradas: `ecommerce-envios` (operador real, DD-22), `ecommerce-pagos` (llaves de Culqi y WhatsApp real), `web-base` (partes humanas y WEB-25), `identidad-seguridad` (D5 y D6), `inventario-ajustes` (AJU-22), `inventario-fuentes` (FUE-09/FUE-11 diferidas y FUE-10, DD-18), `inventario-seleccion-fuente` (S2, k6 previo a producción), `transversal-auditoria`, `transversal-notificaciones` (NOT-02) y `transversal-seguridad` (SEG-17/23 y SEG-31). `transversal-plataforma` (despliegue y spikes) queda sin verificación por estar fuera del Sprint 2; `asistente-compra-ia` y `web-indice-infinito` están diferidas (ECO-12 y ECO-13).
- **Reproducir:** `dotnet run --project tools/SpecCheck`.

## 4. Endpoints REST con OpenAPI

- **Qué:** un documento OpenAPI 3 por servicio, **generado desde el código** (no escrito a mano): [`inventario.v1.json`](../../docs/03-diseno-de-software/openapi/inventario.v1.json) (las 41 operaciones del contrato v1 más 3 rutas de usuario y las sondas) y [`ecommerce.v1.json`](../../docs/03-diseno-de-software/openapi/ecommerce.v1.json) (las rutas que el host mapea hoy). Rutas, esquemas de los DTO de `BabelHub.Shared`, errores `application/problem+json`, alcance de la credencial y esquemas de seguridad (credencial de servicio, JWT de usuario, sesión web). En `Development` cada servicio lo sirve en `/openapi/v1.json`.
- **Se demuestra:** `DocumentacionOpenApiInventarioTests` y `DocumentacionOpenApiEcommerceTests` **fallan si una ruta pública no está documentada** y comparan los archivos publicados con el documento generado.
- **Reproducir:** `dotnet test tests/integration/BabelHub.IntegrationTests --filter "FullyQualifiedName~DocumentacionOpenApi"` (regeneración en el README de la carpeta). Informe: `progress/log/2026-11-20-OAS-1.md`.
- **Límites:** el documento del E-commerce lista lo que el host mapea hoy; las rutas de claves públicas (JWKS), n8n, pasarela y auditoría están descritas y aparecen cuando el host las mapee (hallazgos abiertos del carril de composición).

## 5. Base de datos y caché

- **Qué:** [`docs/03/09`](../../docs/03-diseno-de-software/09-analisis-de-base-de-datos-y-cache.md): consultas críticas con sus índices (plan de ejecución y `pg_stat_statements`), bloqueos y concurrencia (`SELECT … FOR UPDATE` ordenado, AC01), seguridad por filas, qué se cachea y qué no, y la **capacidad medida frente a recursos** (§6b) y el costo por solicitud antes y después de reducirlo (§6c).
- **Hallazgos con arreglo y prueba:** el tope del grupo de conexiones superaba el límite de la cuenta (500 «too many connections»; ahora 16 por omisión, `LimiteDeConexionesDeLaBaseTests`); la base caída respondía 500 (ahora 503 uniforme, `BaseCaidaEs503Tests`); ≈ 31 → ≈ 18 idas a la base por solicitud (`CostoPorSolicitudDeDisponibilidadTests`).
- **Se dice sin maquillar:** la caché de disponibilidad **no existe** (la regla de stock manda; D05 de `inventario-fuentes`); el análisis explica por qué y qué la reemplazaría. Hay caché implementada para los contadores de límites del E-commerce (Redis dedicado) y las claves públicas de Identidad (en memoria). La conclusión inicial «la base no es el cuello de botella» era errónea y se corrigió con `docker stats`: **el cuello es la CPU (la de PostgreSQL y la del servicio)**.

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

- **Qué:** xUnit (unitarias sin base ni red; integración con Testcontainers), pruebas de arquitectura, E2E con Playwright y carga con k6. Estado de `main` al 2026-11-21: 244 de arquitectura, 5 437 y 2 380 unitarias y **7 601 de 7 603 de integración** (los 2 fallos son `HumoDeImagenes`, que necesita Docker y arquitecturas que esta máquina no tiene); compilación sin advertencias y `SpecCheck` sin avisos. La corrida de cobertura del 2026-11-20 (el reporte archivado) fue de 243 de arquitectura, 2 368 y 5 376 unitarias y 6 960 de 6 973 de integración.
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

- **Paneles interactivos de k6** (panel web integrado, un HTML autocontenido por escenario que se abre en el navegador), serie nueva del 2026-11-21 con el mismo código y el mismo dimensionamiento, para confirmar la serie final:

| Escenario | Panel HTML | Resultado en esta serie | ¿Coincide con la serie final? |
| :--- | :--- | :--- | :--- |
| `disponibilidad` (0,6 / 0,4 CPU) | [panel](../../progress/evidencias/k6/2026-11-21-paneles/disponibilidad-panel.html) | p95 **802 ms**, mediana 297 ms: **no cumple** RNF02 | Mismo veredicto (908 y 897 ms); algo mejor, dentro del ruido |
| `disponibilidad` (Inventario 1 CPU, PostgreSQL 1 CPU; DD-18) | [panel](../../progress/evidencias/k6/2026-11-21-paneles-1cpu/disponibilidad-panel.html) | p95 **207 ms**, mediana 44 ms, 0 % de errores: **cumple** RNF02 | Nueva medición (demostración de DD-18) |
| `checkout-confirmacion` | [panel](../../progress/evidencias/k6/2026-11-21-paneles/checkout-confirmacion-panel.html) | p95 100 ms (umbral 3 s): cumple | Sí (108 ms) |
| `sobreventa` | [panel](../../progress/evidencias/k6/2026-11-21-paneles/sobreventa-panel.html) | 10 reservas y 90 rechazos: cumple | Sí |
| `limites` | [panel](../../progress/evidencias/k6/2026-11-21-paneles/limites-panel.html) | 100 permitidas, el resto 429, vecino sin afectar: cumple | Sí |
| `resiliencia` | [panel](../../progress/evidencias/k6/2026-11-21-paneles/resiliencia-panel.html) | 503 uniforme, 0 respuestas inesperadas: cumple | Sí |
| `rampa` (0,6 / 0,4 CPU, perfil de estrés) | [panel](../../progress/evidencias/k6/2026-11-21-paneles/rampa-panel.html) | p95 por nivel de 50 / 100 / 250 / 500 / 1 000 / 2 000 usuarios: 0,9 s / 1,6 s / 4,0 s / 10,9 s / 16,4 s (13 % de errores) / 16,9 s (28 % de errores): **no cumple** | Mismo veredicto y misma forma; con 1 000 y 2 000 usuarios la tasa de errores varió (13 % y 28 % frente a 11,5 % y 51 %) y a 500 el p95 fue mayor (10,9 s frente a 7,4 s): el límite se degrada de forma controlada pero la cifra exacta varía entre corridas |

  Conclusión: **la serie de paneles confirma la serie final**: mismos veredictos en todos los escenarios y mismas conclusiones (RNF02 no se cumple con 0,6 / 0,4 y sí sin el tope de CPU de la VM; los 2 000 usuarios del perfil de estrés no se sostienen). Difieren las cifras absolutas (ruido de una máquina compartida), no los veredictos.

- **Perfil realista de 2 000 sesiones (2026-11-21)**, la prueba representativa de uso real: sesiones de lectores con pausa de 5 a 30 s (media de 15 s), 75 % catálogo, 18 % detalle, 5 % carrito y 2 % checkout (≈ 133 acciones/s y ≈ 39 solicitudes/s al Inventario) con el dimensionamiento fijo de DD-17 y los umbrales de RNF02 por nivel. Resultado en [`k6/2026-11-21-perfil-realista/`](k6/2026-11-21-perfil-realista/) (con [panel](k6/2026-11-21-perfil-realista/perfil-realista-panel.html)) y [repetición](k6/2026-11-21-perfil-realista-repeticion/): **se sostienen 1 000 sesiones** con p95 < 500 ms (dos corridas); 1 500 en el límite (cumple en una, falla en la otra); **2 000 no se sostienen** con el dimensionamiento declarado. Detalle, justificación de las cifras y alcance (solo el Inventario) en [`docs/03/09` §6d](../../docs/03-diseno-de-software/09-analisis-de-base-de-datos-y-cache.md). La rampa de estrés (pausa de 0,5 s) queda como evidencia del límite y de la degradación controlada. **Corrección de validez:** esa corrida completa duró 17,9 min y las credenciales de carga duran 14 (el Inventario no admite más de 15), así que sus niveles de 1 500 y 2 000 hicieron las reservas con credenciales vencidas: parte de sus errores y tiempos no son de capacidad (DOC-1 §6d, punto 6).
- **Qué recursos hacen falta para 2 000 sesiones (2026-11-21, mismo perfil realista, solo el Inventario; la ruta del E-commerce no se midió).** La configuración declarada de la VM (0,6 + 0,4 CPU, DD-17) **no cambia**; la recomendada para 2 000 sesiones es la de la última fila. Evidencias (panel, JSON y resumen por corrida): [`k6/2026-11-21-perfil-realista-2000/`](k6/2026-11-21-perfil-realista-2000/); detalle en DOC-1 §6b y §6d-bis.

| Inventario + PostgreSQL (CPU) | Disponibilidad p95 (< 500 ms) | Reservar + confirmar p95 (< 3 s) | Errores (< 1 %) | ¿Sostiene 2 000 sesiones? |
| :--- | :--- | :--- | :--- | :--- |
| 0,6 + 0,4 (declarada, DD-17; **no cambia**) | 1 576 ms | 4 401 ms | 0,01 % | **No** |
| 1,0 + 0,5 | 1 481 ms | 5 600 ms | 0,02 % | **No** |
| 1,5 + 0,75 | 560 ms | 1 169 ms | 0,11 % | **No** (por 60 ms) |
| **2 + 1 (mínima probada que cumple; recomendada para 2 000)** | **201 ms** (repetición 313 ms) | 286 ms (710 ms) | 0,07 % (0,04 %) | **Sí**, en las dos corridas; en la curva de 1 000 / 1 500 / 2 000 sesiones también (56 / 366 / 147 ms) |

  La mínima **probada** es 2 + 1 CPU (tres corridas, todas cumplen; no se midieron valores intermedios y a 1 500 sesiones el margen es corto); con la declarada 2 000 sesiones no se sostienen.
- **Capacidad medida frente a recursos:** tabla completa en [`docs/03/09` §6b](../../docs/03-diseno-de-software/09-analisis-de-base-de-datos-y-cache.md) (series en [`k6/2026-11-20-capacidad/`](k6/2026-11-20-capacidad/), [`k6/2026-11-20-costo-despues/`](k6/2026-11-20-costo-despues/) y la final sobre `main` en [`k6/2026-11-20-final/`](k6/2026-11-20-final/); §6b-bis). Conclusiones sin maquillar:
  1. **Ninguna configuración medida sostiene 2 000 usuarios virtuales con p95 < 500 ms** en esta máquina; la mejor (4 CPU de servicio + 2 de base) da **p95 de 10,1 s sin errores en la serie final sobre `main` (la cifra vigente)**. La primera serie de la mañana midió 7,2 s con la misma configuración, pero con el código anterior a la reducción de idas a la base, 350 MB de memoria y la máquina más descargada; no se reprodujo, y cuando dos series difieren vale la última sobre `main` (DOC-1 §6b-bis, punto 3).
  2. **El cuello de botella es la CPU** (de PostgreSQL y del Inventario, que cuestan unos 8 y 6 a 12 ms por solicitud). **Con el dimensionamiento declarado (DD-17), RNF02 no se cumple en la serie final** (≈ 900 ms a 50 usuarios con pausa de 0,5 s, unas 80 solicitudes por segundo frente a las 44 que se atienden); con 1 CPU para cada servicio sí en la rampa a 50 usuarios (425 ms).
  3. 2 000 usuarios con pausa de 0,5 s son ≈ 4 000 solicitudes por segundo, inalcanzables en 1 OCPU; con pausas de persona (5 a 10 s) son 200 a 400 por segundo, lo que sostiene 4 + 2 CPU con latencia degradada.
  4. Bajo sobrecarga el sistema **se degrada de forma controlada** (503 y plazos vencidos), sin 500 ni sobreventa.
- **Reproducir:** `bash tests/load/entorno/preparar-carga.sh`, `bash tests/load/ejecutar.sh <escenario>` y `bash tests/load/entorno/serie-de-capacidad.sh`; luego `bash tests/load/entorno/bajar-carga.sh`.
- **Límites:** los números absolutos son de esta máquina (k6 comparte CPU con el entorno), no de la VM ARM; se mide solo el Inventario (el tramo del E-commerce se medirá cuando su Web esté completa) y la pasarela no entra.

## Qué falta o se demuestra a medias

- **CI en GitHub:** el flujo (`ci.yml`, `secretos.yml`) aún **no se ha ejecutado** en GitHub porque depende del `push` de la persona; no hay corrida que mostrar. Todo lo del flujo se ejecuta y pasa en local.
- **Specs sin cierre completo:** 7 en `Done`; las demás convergen salvo las excepciones listadas en el punto 3 (operador de envíos real, llaves de Culqi, partes humanas de la interfaz, etc.); `transversal-plataforma` queda fuera del Sprint 2.
- **RNF02 con el dimensionamiento declarado (0,6 / 0,4)**: medido y no cumplido en la serie final (p95 ≈ 900 ms a 50 usuarios). **Decisión de la persona (DD-18, 2026-11-20, opción A):** se demuestra con el entorno de prueba sin el tope de CPU de la VM (1/1 CPU, p95 < 500 ms a 50 usuarios) y se declara con honestidad que en la VM de 1 OCPU compartida no se sostiene; palancas: memoria de referencias (apagada por D05), separar PostgreSQL, ARM con más OCPU.
- **2 000 usuarios virtuales:** no se sostienen con p95 < 500 ms en ninguna configuración medida. La cifra vigente es la de la serie final sobre `main`: con el dimensionamiento declarado, p95 de 16,9 s y 51 % de errores; con la mejor configuración (4 / 2 CPU), 10,1 s sin errores. Es la capacidad medida con su dimensionamiento.
- Hallazgos abiertos de otros carriles en `progress/hallazgos.md`.
