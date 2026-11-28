# Evidencias del Sprint 2 (hoja única)

Esta hoja recorre la rúbrica del Sprint 2 punto por punto, con la **métrica**, el **resultado honesto** y el **enlace directo a la evidencia** de cada una. El detalle de cada punto está en [`progress/evidencias/README.md`](../../progress/evidencias/README.md); el plan que fijó qué se demuestra está en [`06-plan-sprint-2.md`](06-plan-sprint-2.md). Estado de las mediciones: 2026-11-20 y 2026-11-21, en una máquina de desarrollo con Docker Desktop (no es la VM ARM de despliegue). El despliegue queda fuera del Sprint 2 (la aplicación se desplegó después y esa instalación no se midió). **Actualizado el 2026-11-27:** las cifras de pruebas, cobertura y CI son las de `main` en `938a3306` (`v0.1.2`, [ejecución de CI](https://github.com/P35C4D0R4B1050/BabelHub/actions/runs/45991774756)); las de carga conservan sus fechas, y desde su commit no cambió el código de reservas, disponibilidad, fuentes ni persistencia del Inventario. La etiqueta `sprint-2` marca la versión entregada.

> **Sobre «Jest» en la rúbrica.** La rúbrica menciona Jest porque supone un stack JavaScript. BabelHub es **.NET 10**, y el equivalente de cada pieza es: **xUnit** (pruebas unitarias y de integración, en lugar de Jest), **Testcontainers** (PostgreSQL y Redis reales en las pruebas de integración), **Playwright** (extremo a extremo en Chromium, teléfono y WebKit) y **k6** (carga y atributos de calidad). Cobertura con coverlet y un reporte HTML propio (`tools/CoverageCheck`), umbrales exigidos en CI.

## 1. Resumen de resultados

| Punto de la rúbrica | Estado | Evidencia principal |
| :--- | :--- | :--- |
| 1. Estilo arquitectónico | Cumple | `docs/02` + 261 pruebas de arquitectura |
| 2. Patrón (Clean Architecture, módulos) | Cumple | `docs/03/01`, `docs/03/04`, `tests/architecture` |
| 3. SDD con criterios de aceptación y tareas | Cumple (11 de 23 specs en `Done`; 5 en curso y 7 planificadas) | `specs/`, `specs/TRACEABILITY.md` |
| 4. REST con OpenAPI | Cumple | `docs/03-diseno-de-software/openapi/` (generado del código) |
| 5. Base de datos y caché | Cumple, con hallazgos medidos | `docs/03/09` |
| 6. SOLID y patrones | Cumple | `docs/03/03` + ejemplos de este documento |
| 7. Pruebas, cobertura y CI/CD | Cumple en local y **en GitHub: corrida verde** de `ci` en `938a3306` ([ci](https://github.com/P35C4D0R4B1050/BabelHub/actions/runs/45991774756)), con el reporte de cobertura como artefacto | `progress/evidencias/cobertura/index.html`, `.github/workflows/` |
| 8. Atributos de calidad con k6 hasta 2 000 usuarios | **Parcial**: RNF02 a 50 usuarios con DD-17 y los 2 000 usuarios del perfil de estrés **no cumplen**; con el perfil realista, 2 000 sesiones **no** se sostienen con la configuración declarada (0,6 + 0,4 CPU, sostiene ≈ 1 000) y **sí** con Inventario 2 CPU + PostgreSQL 1 CPU (sección 2.3; se declara y se explica) | `progress/evidencias/k6/`, `docs/03/09` |

## 2. Métricas de los atributos de calidad

Todas medidas con k6 (imagen oficial `grafana/k6` v2.4.0 fijada por huella) contra el Inventario y PostgreSQL en contenedores con CPU y memoria acotadas. Dimensionamiento declarado (DD-17): **Inventario 0,6 CPU y 512 MB, PostgreSQL 0,4 CPU**. Serie final sobre `main`: [`k6/2026-11-20-final-escenarios/`](../../progress/evidencias/k6/2026-11-20-final-escenarios/) y [`k6/2026-11-20-final/`](../../progress/evidencias/k6/2026-11-20-final/); paneles interactivos en la serie de la sección 2.1.

| Atributo | Escenario | Umbral | Resultado | ¿Cumple? |
| :--- | :--- | :--- | :--- | :--- |
| Rendimiento (RNF02, AC-FUE-08), declarado por DD-17 | [`disponibilidad`](../../progress/evidencias/k6/2026-11-20-final-escenarios/disponibilidad.md): 50 usuarios, pausa de 0,5 s | p95 < 500 ms | p95 **908 ms** (897 ms con el servicio caliente), 0 % de errores | **No** |
| Rendimiento (RNF02), entorno de prueba sin el tope de CPU de la VM (DD-18, opción A) | `disponibilidad` con Inventario 1 CPU y PostgreSQL 1 CPU | p95 < 500 ms | p95 **207 ms**, mediana 44 ms, 0 % de errores ([panel](../../progress/evidencias/k6/2026-11-21-paneles-1cpu/disponibilidad-panel.html)) | **Sí** |
| Rendimiento del checkout | [`checkout-confirmacion`](../../progress/evidencias/k6/2026-11-20-final-escenarios/checkout-confirmacion.md): reservar y confirmar, hasta 50 usuarios y 5 compras/s | p95 < 3 s | p95 **108 ms** | **Sí** |
| Concurrencia sin sobreventa (AC01) | [`sobreventa`](../../progress/evidencias/k6/2026-11-20-final-escenarios/sobreventa.md): 100 usuarios por 10 unidades | exactamente 10 reservas, 90 rechazos, vendible final 0 | 10 reservas, 90 rechazos `INV-STOCK-INSUFICIENTE`, sin respuestas inesperadas | **Sí** |
| Seguridad: límites de uso (API4) | [`limites-de-uso`](../../progress/evidencias/k6/2026-11-20-final-escenarios/limites-de-uso.md) | más de 100 solicitudes/min de un usuario: 429 con `Retry-After`; el vecino no se ve afectado | 100 permitidas, el resto 429; el vecino sin limitar | **Sí** |
| Disponibilidad ante caída (RNF03) | [`resiliencia`](../../progress/evidencias/k6/2026-11-20-final-escenarios/resiliencia.md): PostgreSQL pausado 25 s | proceso vivo, 503 uniforme (nunca 500), recuperación sola | 503 uniforme, 0 respuestas inesperadas, recuperación sola | **Sí** |
| Capacidad realista: 2 000 sesiones con pausa de persona (≈ 39 solicitudes/s al Inventario), dimensionamiento de DD-17 | [`perfil-realista`](../../progress/evidencias/k6/2026-11-21-perfil-realista/perfil-realista.md) | p95 < 500 ms (disponibilidad), p95 < 3 s (reservar + confirmar), < 1 % de errores, por nivel | Cumple hasta **1 000 sesiones** (2 corridas); **1 500 en el límite**; **2 000 no** (sección 2.2) | **Hasta 1 000**; 2 000 **no** con el dimensionamiento declarado (con 2 + 1 CPU **sí**, sección 2.3) |
| Escalabilidad, perfil de estrés: 50 → 2 000 usuarios con pausa de 0,5 s (límite y degradación) | [`rampa`](../../progress/evidencias/k6/2026-11-20-final/inventario-0.6cpu_postgres-0.4cpu_pool-16/rampa-de-usuarios.md), declarado por DD-17 | p95 < 500 ms y < 1 % de errores por nivel | 50 usuarios: p95 1,4 s; 100: 1,9 s; 500: 7,4 s; 1 000: 16,4 s (11,5 % de errores); **2 000: 16,9 s y 51 % de errores** | **No** |
| Escalabilidad, mejor configuración medida (4 CPU + 2 CPU) | [`rampa`](../../progress/evidencias/k6/2026-11-20-final/inventario-4cpu_postgres-2cpu_pool-16/rampa-de-usuarios.md) | p95 < 500 ms por nivel | 100 usuarios: 47 ms; 250: 937 ms; **2 000: 10,1 s sin errores (cifra vigente)** | Hasta 100 usuarios; **2 000 no** |

**Lectura honesta.**

- **RNF02 con el dimensionamiento declarado no se cumple.** 50 usuarios con 0,5 s de pausa piden unas 80 solicitudes por segundo; con 0,6 CPU para el Inventario y 0,4 para PostgreSQL el entorno atiende unas 44. El cuello es la CPU (PostgreSQL y servicio). La decisión de la persona (**DD-18, opción A**) es demostrar RNF02 con el entorno de prueba sin el tope de CPU de la VM y **declarar** que en la VM de 1 OCPU compartida con PostgreSQL y la réplica de gestión no se sostiene (p95 ≈ 900 ms; 1,4 s en rampa). Palancas medidas o identificadas: memoria de referencias (apagada en las mediciones del 2026-11-20 y encendida después por DD-51), separar PostgreSQL, ARM con más OCPU.
- **2 000 usuarios del perfil de estrés no se sostienen** con p95 < 500 ms en ninguna configuración (y 2 000 sesiones del perfil realista tampoco con el dimensionamiento de DD-17; sección 2.2): el mejor resultado es 10,1 s sin errores con 4 + 2 CPU, que es **la cifra vigente** (una primera serie anterior midió 7,2 s con el código previo a la reducción de idas a la base y no se reprodujo; ver `docs/03/09` §6b-bis, punto 3). Con 2 000 usuarios y 0,5 s de pausa se piden ≈ 4 000 solicitudes por segundo, inalcanzables en 1 OCPU; con pausas de persona (5 a 10 s) serían 200 a 400 por segundo, lo que sostiene la configuración de 4 + 2 CPU con latencia degradada.
- **Lo que sí es sólido:** no hay sobreventa (AC01), los límites responden 429 uniforme, la caída de la base responde 503 (nunca 500) y el servicio se recupera sola. Bajo sobrecarga el sistema se degrada de forma controlada.
- Las cifras absolutas son de esta máquina (k6 comparte CPU con el entorno) y varían entre corridas; entre dos mediciones del mismo límite se vio hasta 3 veces de diferencia. El orden entre configuraciones es lo reproducible.

### 2.2 Perfil realista de 2 000 sesiones (la prueba representativa)

La rampa de la sección 2 es un **perfil de estrés** (pausa de 0,5 s entre solicitudes: 2 000 usuarios ≈ 4 000 solicitudes por segundo) y se conserva como evidencia del **límite** y de la degradación controlada. La prueba **representativa** de uso real es el [`perfil-realista`](../../tests/load/escenarios/perfil-realista.js): 2 000 sesiones concurrentes de lectores de una librería con varias sucursales, con pausa de persona de 5 a 30 s (media de 15 s) y mezcla del 75 % catálogo y búsqueda, 18 % detalle con disponibilidad, 5 % carrito y 2 % checkout con reserva. Eso son ≈ 133 acciones por segundo y **≈ 39 solicitudes por segundo** al Inventario; la justificación de cada cifra está en [`docs/03/09` §6d](../03-diseno-de-software/09-analisis-de-base-de-datos-y-cache.md). Dimensionamiento fijo de producción (DD-17: Inventario 0,6 CPU y 512 MB, PostgreSQL 0,4 CPU); umbrales de RNF02 por nivel. Corrida completa: [`k6/2026-11-21-perfil-realista/`](../../progress/evidencias/k6/2026-11-21-perfil-realista/) ([panel](../../progress/evidencias/k6/2026-11-21-perfil-realista/perfil-realista-panel.html)); repetición de los niveles altos: [`k6/2026-11-21-perfil-realista-repeticion/`](../../progress/evidencias/k6/2026-11-21-perfil-realista-repeticion/) ([panel](../../progress/evidencias/k6/2026-11-21-perfil-realista-repeticion/perfil-realista-panel.html)).

| Sesiones concurrentes | Disponibilidad p95 (umbral 500 ms) | Reservar + confirmar p95 (umbral 3 s) | Errores (umbral 1 %) | ¿Cumple? |
| :--- | :--- | :--- | :--- | :--- |
| 100 | 82 ms | 190 ms | 0 % | **Sí** |
| 250 | 114 ms | 198 ms | 0 % | **Sí** |
| 500 | 29 ms | 92 ms | 0 % | **Sí** |
| 1 000 | 23 ms (repetición: 136 ms) | 83 ms (581 ms) | 0,72 % (0 %) | **Sí**, en las dos corridas |
| 1 500 | 3,3 s (repetición: 375 ms) | 11,2 s (703 ms) | 0,37 % (0,24 %) | **En el límite**: una corrida cumple y la otra no |
| 2 000 | 1,2 s (repetición: 13,9 s) | 2,9 s (42,3 s) | 6,4 % (0,29 %) | **No** |

**Lectura honesta:** con el dimensionamiento declarado, **VM-INVENTARIO sostiene ≈ 1 000 sesiones concurrentes con p95 < 500 ms** (≈ 20 solicitudes por segundo al Inventario), **1 500 está en el límite y 2 000 sesiones no se sostienen**. Para 2 000 hace falta más capacidad que 1 OCPU compartida con PostgreSQL: **se midió en 2.3** (Inventario 2 CPU + PostgreSQL 1 CPU sí cumple); separar PostgreSQL o una ARM con más OCPU son las palancas (DD-18). **Alcance:** se mide el Inventario con el tráfico que el E-commerce le enviaría; el catálogo, el carrito y el pedido del E-commerce (VM-VENTAS) y la latencia de extremo a extremo **no se midieron**. Una sola corrida completa y una repetición parcial: con la mayor carga dos corridas con la misma configuración dan resultados muy distintos.

### 2.3 Qué recursos hacen falta para 2 000 sesiones (perfil realista)

Encargo de la persona (2026-11-21): el mismo perfil de 2.2, a **2 000 sesiones concurrentes**, con el dimensionamiento que **sí** las sostenga, del menor al mayor tamaño. La configuración declarada de la VM de 1 OCPU (**0,6 + 0,4 CPU, DD-17**) **no cambia**; la de abajo es el **dimensionamiento recomendado para 2 000 sesiones**. Método: una corrida por configuración, solo el nivel de 2 000 sesiones (30 s de subida, 240 s sostenidos), 512 MB, tope de 16 conexiones, umbrales de RNF02 sin cambios. Evidencia (panel HTML, JSON y resumen por corrida): [`k6/2026-11-21-perfil-realista-2000/`](../../progress/evidencias/k6/2026-11-21-perfil-realista-2000/).

| Inventario + PostgreSQL (CPU) | Disponibilidad p95 (< 500 ms) | Reservar + confirmar p95 (< 3 s) | Errores (< 1 %) | ¿Sostiene 2 000 sesiones? |
| :--- | :--- | :--- | :--- | :--- |
| 0,6 + 0,4 (declarada, DD-17; **no cambia**) | 1 576 ms | 4 401 ms | 0,01 % | **No** |
| 1,0 + 0,5 | 1 481 ms | 5 600 ms | 0,02 % | **No** |
| 1,5 + 0,75 | 560 ms | 1 169 ms | 0,11 % | **No** (por 60 ms) |
| **2 + 1 (mínima probada que cumple; recomendada para 2 000)** | **201 ms** (repetición 313 ms) | 286 ms (710 ms) | 0,07 % (0,04 %) | **Sí**, en las dos corridas; en la curva de 1 000 / 1 500 / 2 000 sesiones también (56 / 366 / 147 ms) |

**Lectura honesta.** La configuración mínima **probada** que cumple los tres umbrales a 2 000 sesiones es **Inventario 2 CPU + PostgreSQL 1 CPU** (3 CPU en total, ≈ 3 veces el dimensionamiento declarado de 1 OCPU compartida): cumplió en las tres corridas que la incluyen (búsqueda, repetición y curva; [panel de la curva](../../progress/evidencias/k6/2026-11-21-perfil-realista-2000/curva-inv2-pg1/perfil-realista-panel.html)). «Mínima» es la menor de la escalera probada: no se midieron valores intermedios (por ejemplo 1,5 + 1,0), la configuración 4 + 2 no hizo falta y la búsqueda se detuvo al primer cumplimiento. A 1 500 sesiones con 2 + 1 la disponibilidad quedó en 366 ms (sin holgura amplia). **Alcance:** solo el Inventario; **la ruta del E-commerce (catálogo, búsqueda, carrito, pedido) y la latencia de extremo a extremo no se midieron**; la capacidad de VM-VENTAS tampoco; 20 ediciones de prueba, pasarela simulada, máquina de desarrollo compartida con k6.

**Corrección de validez de 2.2.** Las credenciales de la carga duran 14 minutos (el Inventario no admite más de 15) y la corrida completa de 2.2 duró 17,9 minutos: sus niveles de 1 500 y, sobre todo, de 2 000 sesiones hicieron las reservas con credenciales vencidas, por lo que parte de sus errores (6,4 % a 2 000) y tiempos **no son de capacidad**. Las corridas de 2.3 duran menos de 14 minutos. La conclusión de 2.2 para 0,6 + 0,4 se confirmó con credenciales vigentes (1,6 s y 4,4 s a 2 000 sesiones).

### 2.1 Paneles interactivos de k6

Cada escenario, ejecutado de nuevo con el panel web integrado de k6 y exportado como un HTML autocontenido (se abre en el navegador, sin servidor): serie [`k6/2026-11-21-paneles/`](../../progress/evidencias/k6/2026-11-21-paneles/) y, para la demostración de RNF02 sin el tope de CPU de la VM (DD-18), [`k6/2026-11-21-paneles-1cpu/`](../../progress/evidencias/k6/2026-11-21-paneles-1cpu/).

| Escenario | Panel HTML | Resultado en esta serie | ¿Coincide con la serie final? |
| :--- | :--- | :--- | :--- |
| `disponibilidad` (0,6 / 0,4 CPU) | [panel](../../progress/evidencias/k6/2026-11-21-paneles/disponibilidad-panel.html) | p95 **802 ms**, mediana 297 ms: **no cumple** RNF02 | Mismo veredicto (908 y 897 ms); algo mejor, dentro del ruido |
| `disponibilidad` (Inventario 1 CPU, PostgreSQL 1 CPU; DD-18) | [panel](../../progress/evidencias/k6/2026-11-21-paneles-1cpu/disponibilidad-panel.html) | p95 **207 ms**, mediana 44 ms, 0 % de errores: **cumple** RNF02 | Nueva medición (demostración de DD-18) |
| `checkout-confirmacion` | [panel](../../progress/evidencias/k6/2026-11-21-paneles/checkout-confirmacion-panel.html) | p95 100 ms (umbral 3 s): cumple | Sí (108 ms) |
| `sobreventa` | [panel](../../progress/evidencias/k6/2026-11-21-paneles/sobreventa-panel.html) | 10 reservas y 90 rechazos: cumple | Sí |
| `limites` | [panel](../../progress/evidencias/k6/2026-11-21-paneles/limites-panel.html) | 100 permitidas, el resto 429, vecino sin afectar: cumple | Sí |
| `resiliencia` | [panel](../../progress/evidencias/k6/2026-11-21-paneles/resiliencia-panel.html) | 503 uniforme, 0 respuestas inesperadas: cumple | Sí |
| `rampa` (0,6 / 0,4 CPU, perfil de estrés) | [panel](../../progress/evidencias/k6/2026-11-21-paneles/rampa-panel.html) | p95 por nivel de 50 / 100 / 250 / 500 / 1 000 / 2 000 usuarios: 0,9 s / 1,6 s / 4,0 s / 10,9 s / 16,4 s (13 % de errores) / 16,9 s (28 % de errores): **no cumple** | Mismo veredicto y misma forma; con 1 000 y 2 000 usuarios la tasa de errores varió (13 % y 28 % frente a 11,5 % y 51 %) y a 500 el p95 fue mayor (10,9 s frente a 7,4 s): el límite se degrada de forma controlada pero la cifra exacta varía entre corridas |

**La serie de paneles confirma la serie final** (mismos veredictos); las cifras absolutas varían entre corridas por el ruido de una máquina compartida. **RNF02 se demuestra sin el tope de CPU de la VM (p95 207 ms con 50 usuarios, Inventario 1 CPU y PostgreSQL 1 CPU) y no se cumple con el dimensionamiento declarado (p95 802 a 908 ms).**

### 2.4 Palancas 3 y 4 del rendimiento (2026-11-21, antes y después)

La persona autorizó las dos palancas pospuestas (DD-50 a DD-54, ratificadas el 2026-11-22); detalle y método en [`docs/03/09` §6e](../03-diseno-de-software/09-analisis-de-base-de-datos-y-cache.md). Resultados en `progress/evidencias/k6/2026-11-21-palancas-3-4-*` (con paneles HTML).

- **Palanca 4 (caché de disponibilidad): ya existía** en el catálogo del E-commerce (30 s por edición, con invalidación y respaldo de 10 minutos). Simulada con el resolutor real, quita el 97,8 % de las consultas con 20 ediciones y el 56,8 % con 5 000 de popularidad tipo Zipf; el stock nunca se inventa y reservar no la usa. Se corrigió la documentación, que decía «no implementado».
- **Palanca 3 (menos idas a la base):** se enciende la memoria de referencias de 5 s del Inventario. Medición alternada en el peor caso (50 usuarios sin pausa, tres pares): **+45 % de solicitudes atendidas** (4 615 a 4 873 frente a 2 772 a 3 425) y mediana de 0,6 s frente a 0,9 a 1,1 s. En la rampa consecutiva baja el p95 de 1,4 a 0,8 s con 50 usuarios y de 7,1 a 4,7 s con 250.
- **Lo que no cambia:** ningún nivel de la rampa cumple RNF02 con 0,6 + 0,4 CPU, y a 1 000 y 2 000 usuarios la saturación iguala las dos configuraciones. Se descartó mover los límites generales a memoria (sin efecto medible: la consulta de servicio no los evalúa).
- **Honestidad de la medición:** el perfil realista de 2 000 sesiones cumplió al empezar la tarde (p95 70 ms) y falló horas después con el mismo build (15 s); el entorno derivó (otros carriles en la misma máquina o la base de la carga muy usada; no se aisló la causa), por eso solo se acepta la comparación alternada. Las dos corridas con el modelo de la caché del E-commerce (`FALLOS_DE_CACHE`) cumplen los umbrales (183 y 130 ms), pero son un **modelo del tráfico**, no una medición de VM-VENTAS. AC01 intacto: `sobreventa` dio 10 reservas y 90 rechazos.

### 2.5 Mediciones con la venta por sucursal (2026-11-27, posteriores a la entrega del Sprint 2)

**No forman parte de la entrega del Sprint 2 ni sustituyen sus cifras** (secciones anteriores): repiten sus escenarios tras la venta por sucursal (v0.1.3, `inventario-seleccion-fuente` T43), en la rama de ese cambio y antes de integrarlo. `checkout-confirmacion`, `sobreventa`, `tope-por-origen` y `perfil-realista` reservan ahora declarando la sucursal en cada línea, y se agrega `sobreventa-por-sucursal` (AC-SEL-47). Mismo dimensionamiento declarado (DD-17, comprobado en los contenedores: Inventario 0,6 CPU y 512 MB, PostgreSQL 0,4 CPU; memoria de referencias de 5 s encendida), mismos umbrales, solo en el entorno local y con una base nueva, en un proyecto propio (`CARGA_PROYECTO`). Resultados en [`k6/2026-11-27-venta-por-sucursal/`](../../progress/evidencias/k6/2026-11-27-venta-por-sucursal/).

| Escenario | Sprint 2 (dimensionamiento declarado) | Venta por sucursal | ¿Cumple hoy? |
| :--- | :--- | :--- | :--- |
| [`sobreventa-por-sucursal`](../../progress/evidencias/k6/2026-11-27-venta-por-sucursal/sobreventa-por-sucursal.md) (nuevo) | no existía | 100 clientes por 20 unidades de una sucursal, 1 a 3 líneas cada uno: 13 reservas apartan exactamente 20, 87 rechazos por falta de stock, vendible final 0 y las 20 de la otra sucursal intactas; ningún 500 ni 503 | **Sí** (sin sobreventa) |
| [`sobreventa`](../../progress/evidencias/k6/2026-11-27-venta-por-sucursal/sobreventa.md) | 10 reservas y 90 rechazos | 10 reservas, 90 rechazos, vendible final 0 | **Sí** |
| [`checkout-confirmacion`](../../progress/evidencias/k6/2026-11-27-venta-por-sucursal/checkout-confirmacion.md) | p95 108 ms | p95 171 ms, 300 de 300 reservas confirmadas | **Sí** (< 3 s) |
| [`tope-por-origen`](../../progress/evidencias/k6/2026-11-27-venta-por-sucursal/tope-por-origen.md) | cumple (2026-11-21) | 10 aceptadas por origen, el resto 429 con `Retry-After` | **Sí** |
| [`limites`](../../progress/evidencias/k6/2026-11-27-venta-por-sucursal/limites-de-uso.md) | cumple | usuario abusivo: 100 permitidas y 40 con 429; el vecino sin limitar | **Sí** |
| [`resiliencia`](../../progress/evidencias/k6/2026-11-27-venta-por-sucursal/resiliencia.md) | cumple | 503 uniforme (14), ningún 500, proceso vivo y recuperación sola | **Sí** |
| `disponibilidad` ([1](../../progress/evidencias/k6/2026-11-27-venta-por-sucursal/disponibilidad.md), [2](../../progress/evidencias/k6/2026-11-27-venta-por-sucursal-disponibilidad-1/disponibilidad.md), [3](../../progress/evidencias/k6/2026-11-27-venta-por-sucursal-disponibilidad-2/disponibilidad.md)) | p95 908, 897 y 802 ms: **no cumple** | p95 195, 235 y 211 ms, 0 % de errores | **Sí** en las tres corridas (ver la lectura) |
| [`rampa`](../../progress/evidencias/k6/2026-11-27-venta-por-sucursal/rampa-de-usuarios.md) (perfil de estrés) | p95 1,4 s a 50 usuarios; 16,9 s y 51 % de errores a 2 000 | p95 por nivel de 50 / 100 / 250 / 500 / 1 000 / 2 000: 277 ms / 900 ms / 2,1 s / 3,2 s / 8,7 s / 13,2 s (45,9 % de errores) | **Solo 50 usuarios**; la rampa **no cumple** |
| `perfil-realista`, [100 a 1 000](../../progress/evidencias/k6/2026-11-27-venta-por-sucursal-perfil-realista/perfil-realista.md) | sostiene ≈ 1 000 sesiones | disponibilidad p95 123 / 101 / 90 / 283 ms; reservar + confirmar 206 / 246 / 347 / 940 ms; errores ≤ 0,12 % | **Sí** hasta 1 000 |
| `perfil-realista`, [1 500 y 2 000](../../progress/evidencias/k6/2026-11-27-venta-por-sucursal-perfil-realista-altos/perfil-realista.md) | 1 500 en el límite; 2 000 no | 1 500: 358 ms y 2,6 s, 0 % de errores; 2 000: **1 158 ms y 3,4 s**, 0,04 % | 1 500 **sí** (una corrida); 2 000 **no** |

**Lectura honesta.**

- **La venta por sucursal no introduce sobreventa** (AC01 y AC-SEL-47) y la reserva sigue muy por debajo de su umbral.
- **`disponibilidad` cumple hoy y no cumplía en el Sprint 2, pero no se atribuye a este cambio:** la consulta de disponibilidad no se modificó. La causa **no se aisló** (entre las dos series cambian el código, la memoria de referencias de 5 s, la base nueva y el estado de la máquina; en esta documentación ya se vieron diferencias de hasta 3 veces entre corridas). **No se cambia la declaración de RNF02 ni DD-18**: decidirlo corresponde a la persona responsable, con una medición dedicada.
- **Máquina compartida:** el procesador estaba entre el 1 % y el 68 % antes de cada escenario y al **99 %** antes de la corrida de 1 500 y 2 000 sesiones (otros trabajos en la misma máquina), así que esa corrida es la menos fiable; hubo una sola de cada escenario salvo `disponibilidad`.
- **No repetido:** las series de capacidad con otros límites de CPU (incluido 2 + 1 para 2 000 sesiones, sección 2.3), los paneles HTML y el modelo de la caché del E-commerce. Como en el Sprint 2, solo se mide el Inventario.
- **Reproducir:** `CARGA_PROYECTO=<nombre> CARGA_CARPETA=<carpeta> bash tests/load/entorno/preparar-carga.sh` y `bash tests/load/ejecutar.sh <escenario>` con las mismas variables (en `perfil-realista`, `NIVELES=100,250,500,1000 SEGUNDOS_FINAL=100` y `NIVELES=1500,2000 SEGUNDOS_FINAL=180`, porque las credenciales duran 14 minutos).

## 3. Rúbrica punto por punto

### 3.1 Estilo arquitectónico

- **Qué:** dos servicios (E-commerce e Inventario), cada uno un monolito modular, comunicados solo por REST. Decisiones, tácticas y presupuesto de recursos en `docs/02-arquitectura-de-software/` ([estilo](../02-arquitectura-de-software/03-estilo-arquitectonico.md), [decisiones](../02-arquitectura-de-software/02-decisiones-arquitectonicas.md), [tácticas](../02-arquitectura-de-software/06-tacticas-arquitectonicas.md), [presupuesto de recursos](../02-arquitectura-de-software/07-presupuesto-de-recursos.md)) y modelo C4 en `docs/04`.
- **Prueba:** `tests/architecture` hace cumplir las reglas 1, 2, 5, 6 y 13 de [`AGENTS.md`](../../AGENTS.md) (**261 pruebas**, verdes): si una referencia viola la regla, falla.
- **Reproducir:** `dotnet test tests/architecture`.

### 3.2 Patrón: Clean Architecture con módulos

- **Qué:** cuatro capas por servicio (`Domain`, `Application`, `Infrastructure`, `Api`); puertos en la Aplicación, adaptadores en la Infraestructura; cada módulo expone un contrato público e `Interno` privado. [Clean Architecture](../03-diseno-de-software/01-clean-architecture.md), [organización interna](../03-diseno-de-software/02-organizacion-interna.md), [módulos y contratos](../03-diseno-de-software/04-modulos-y-contratos.md).
- **Prueba:** `ReglasDeReferenciasTests`, `ReglasDeModulosTests`, `ContratoPublicoDe*Tests` en `tests/architecture`.

### 3.3 SDD con criterios de aceptación y tareas técnicas

- **Qué:** cada feature tiene `spec.md` (EARS, criterios `AC-XXX-NN`), `plan.md`, `tasks.md`, `decisions.md` y `verificacion.md`; ninguna se programa sin spec **Approved** y plan **Planned** ([flujo SDD](01-flujo-sdd.md)). Matriz de trazabilidad: [`specs/TRACEABILITY.md`](../../specs/TRACEABILITY.md) (cada criterio, con la prueba que lo nombra por `[Trait("Criterio", "AC-XXX-NN")]`).
- **Estado de cierre (2026-11-21, [`progress/bloques.md`](../../progress/bloques.md)):** 547 de 611 tareas (89 %). **Siete specs en `Done`** (`Resultado: Converge`): [biblioteca digital](../../specs/ecommerce-biblioteca-digital/verificacion.md), [carrito y checkout](../../specs/ecommerce-carrito-checkout/verificacion.md), [catálogo](../../specs/ecommerce-catalogo/verificacion.md), [devoluciones](../../specs/ecommerce-devoluciones/verificacion.md), [libro de reclamaciones](../../specs/ecommerce-libro-reclamaciones/verificacion.md), [pedidos](../../specs/ecommerce-pedidos/verificacion.md) y [traslados](../../specs/inventario-traslados/verificacion.md). El resto con verificación converge **salvo** excepciones registradas (operador de envíos real, llaves de Culqi, partes humanas de la interfaz, etc.); `transversal-plataforma` (despliegue y spikes) queda fuera del Sprint 2 y dos features están diferidas (asistente y página con índice infinito).
- **Estado al 2026-11-27:** 23 specs (11 en `Done`, 5 en `InProgress` y 7 en `Planned`) y 650 de 700 tareas (93 %), contando `web-acceso`, integrada después de `v0.1.2`. El párrafo anterior es la foto del 2026-11-21.
- **Reproducir:** `dotnet run --project tools/SpecCheck` (estructura, tareas y trazabilidad; 0 avisos).

### 3.4 Endpoints REST con OpenAPI

- **Qué:** un documento OpenAPI 3 por servicio, **generado desde el código**: [`inventario.v1.json`](../03-diseno-de-software/openapi/inventario.v1.json) y [`ecommerce.v1.json`](../03-diseno-de-software/openapi/ecommerce.v1.json), con rutas, esquemas de los DTO de `BabelHub.Shared`, errores `application/problem+json`, alcance de la credencial y esquemas de seguridad. [README de la carpeta](../03-diseno-de-software/openapi/README.md). Cada servicio lo sirve en `/openapi/v1.json` en `Development`.
- **Visor interactivo (Scalar):** cada servicio sirve una interfaz en `/documentacion` solo en `Development`, sin CDN (los recursos van en el paquete) y sobre el mismo `/openapi/v1.json`; cómo abrirla en [el README de la carpeta `openapi`](../03-diseno-de-software/openapi/README.md) (`dotnet run --project src/BabelHub.Inventario/BabelHub.Inventario.Api` en `http://localhost:5081/documentacion`; E-commerce en `http://localhost:5080/documentacion`). Prueba automática: `InterfazDeLaDocumentacionOpenApiTests`. **Comprobación en navegador real: verificado (2026-11-21)** en el navegador integrado (Chromium) contra el host del Inventario en `Development` (entorno E2E, `http://localhost:8082/documentacion`): la interfaz se abre, muestra «BabelHub — API del Inventario» con OpenAPI 3.1.1, el menú de operaciones por grupo (plataforma, disponibilidad, sucursales, fuentes, rutas…) y su descripción, **solo se pidieron recursos locales** (`/documentacion/`, `scalar.js`, `scalar.aspnetcore.js` y `/openapi/v1.json`; ninguna petición a un CDN o a otro dominio) y la consola no mostró errores. Observación: la interfaz de Scalar trae botones propios («Share», «Generate SDK», «Ask AI», «Deploy .NET») que remiten a servicios del proveedor al usarlos; no se usaron ni se pidieron al cargar. No se revisó el host del E-commerce (misma puerta de código y misma prueba automática).
- **Prueba:** `DocumentacionOpenApiInventarioTests` y `DocumentacionOpenApiEcommerceTests` **fallan si una ruta pública no está documentada** y comparan los archivos publicados con el generado.
- **Reproducir:** `dotnet test tests/integration/BabelHub.IntegrationTests --filter "FullyQualifiedName~DocumentacionOpenApi"`.

### 3.5 Base de datos y caché

- **Qué:** [análisis con mediciones](../03-diseno-de-software/09-analisis-de-base-de-datos-y-cache.md): índices usados por las consultas críticas (`pg_stat_statements`), bloqueos (`SELECT … FOR UPDATE` ordenado, AC01), seguridad por filas, qué se cachea y qué no, capacidad medida frente a recursos y costo por solicitud antes y después de reducirlo. Modelo de datos: [`docs/03/05`](../03-diseno-de-software/05-modelo-de-datos.md) y [estados y concurrencia](../03-diseno-de-software/06-estados-y-concurrencia.md).
- **Hallazgos corregidos con prueba:** el tope del grupo de conexiones superaba el límite de la cuenta (500; ahora 16 por omisión); la base caída respondía 500 (ahora 503 uniforme); ≈ 31 → ≈ 18 idas a la base por solicitud.
- **Qué se cachea:** el catálogo del E-commerce guarda la disponibilidad por edición 30 s, con invalidación al registrar un ingreso y respaldo de hasta 10 minutos (DD-50); el Inventario guarda 5 s la lista de fuentes y el formato de las ediciones, **nunca el stock** (DD-51); y hay caché de los contadores de límites (Redis dedicado) y de las claves públicas de Identidad. Reservar no usa ninguna caché de stock. El cuello medido es la CPU, no una consulta lenta.

### 3.6 SOLID y patrones

Catálogo y criterios en [`docs/03/03`](../03-diseno-de-software/03-principios-y-patrones.md). Ejemplos en el código:

| Principio o patrón | Dónde | Qué se ve |
| :--- | :--- | :--- |
| Abierto/cerrado y **Estrategia** | `IEstrategiaDeSeleccion`, `RegistroDeEstrategiasDeSeleccion`, `SelectorDeFuentes` (`Inventario.Application/Reservas`) | Una regla nueva de elección de fuente se agrega como estrategia sin tocar el selector |
| **Decorador** | `TransaccionPorCasoDeUso`, `ValidacionDeEntrada`, `LecturaCoherentePorCasoDeUso`, `ReintentoPorInterbloqueo` | Transacción, validación y reintento envuelven los casos de uso sin ensuciarlos |
| Inversión de dependencias, **Puertos y adaptadores** | puertos en `Application`, adaptadores en `Infrastructure` (`INotificador`, almacén de objetos, pasarela) | Dominio y Aplicación no conocen EF Core, Redis ni `HttpClient`; lo hace cumplir `tests/architecture` |
| Responsabilidad única | un caso de uso por clase; módulos con contrato público | Cada módulo cambia por una sola razón |
| Segregación de interfaces | contratos por módulo y por operación (`CatalogoDeOperaciones`) | Cada cliente depende solo de lo que usa |

### 3.7 Pruebas automatizadas, cobertura y CI/CD

- **Pruebas (CI de `938a3306`, 2026-11-27):** 261 de arquitectura, 5 770 unitarias del E-commerce y 2 454 del Inventario, 23 de `SpecCheck`, 15 de `CoverageCheck` y **8 708 de integración correctas y 4 omitidas** (cobros reales con Culqi), sin fallos; además E2E con Playwright (fuera del CI) y carga con k6. Compilación sin advertencias (`-warnaserror`) y `SpecCheck` sin avisos. Estrategia: [`03-estrategia-de-pruebas.md`](03-estrategia-de-pruebas.md).
- **Cobertura:** [`cobertura/index.html`](../../progress/evidencias/cobertura/index.html) (HTML por servicio, capa y archivo) y su [resumen](../../progress/evidencias/cobertura/README.md): **E-commerce 95,6 % en Dominio y Aplicación y 95,2 % total; Inventario 95,4 % y 93,7 %** en el [CI de `938a3306`](https://github.com/P35C4D0R4B1050/BabelHub/actions/runs/45991774756), contra los umbrales vigentes entonces, por servicio, de 85 % y 70 %. El HTML guardado en el repositorio es el de la corrida del 2026-11-20; el vigente es el artefacto `reporte-de-cobertura` de esa ejecución (GitHub lo conserva hasta el 2026-12-27).
- **CI/CD:** [`ci.yml`](../../.github/workflows/ci.yml) (especificaciones y trazabilidad, compilación sin advertencias, pruebas sin red, pruebas con cobertura y umbrales; archiva el reporte HTML), [`secretos.yml`](../../.github/workflows/secretos.yml) (escaneo de secretos) y `dependabot.yml`. Se ejecuta en cada PR y en cada cambio de `main` ([ejecución citada en esta hoja](https://github.com/P35C4D0R4B1050/BabelHub/actions/runs/45991774756)).
- **Reproducir:** `dotnet build BabelHub.sln -warnaserror`; `dotnet test BabelHub.sln`; `dotnet test BabelHub.sln --collect:"XPlat Code Coverage" --settings tests/coverlet.runsettings` y `dotnet run --project tools/CoverageCheck -- --html`.

### 3.8 Atributos de calidad con k6

- **Qué:** un escenario por atributo con su umbral como criterio de paso, y una rampa de 50 a 2 000 usuarios virtuales (sección 2). Código en [`tests/load/`](../../tests/load/README.md); resultados en [`progress/evidencias/k6/`](../../progress/evidencias/k6/): serie final sobre `main`, series de capacidad por configuración (1, 2 y 4 CPU), reducción de idas a la base y paneles. Análisis en `docs/03/09` §6b y §6b-bis.
- **Reproducir** (Windows con Docker Desktop y Git Bash; `PANEL=1` además exporta el panel HTML):

```powershell
bash tests/load/entorno/preparar-carga.sh              # secretos aleatorios fuera del repo, PostgreSQL + Inventario con límites, base migrada
bash tests/load/ejecutar.sh disponibilidad             # o checkout-confirmacion | sobreventa | limites | resiliencia | rampa
bash tests/load/entorno/serie-de-capacidad.sh          # la rampa con varias configuraciones de CPU
bash tests/load/entorno/bajar-carga.sh
```

## 4. Qué no se demuestra (declarado)

- **CI en GitHub (historia):** la primera corrida verde de `ci` y `secretos` fue sobre `e0bc86df` (compilación sin advertencias, pruebas sin red, pruebas con cobertura y umbrales, escaneo de secretos del historial y de las imágenes). La primera corrida (`4ba37f6a`, 135 pruebas fallidas en Linux) y la segunda (7) se corrigieron por causa raíz: permisos de ejecución de los dobles y de los scripts, una prueba de tiempos, enlaces fuera de `docs/` en la copia de SpecCheck, y la prueba de arranque de imágenes, que no entregaba los secretos que la validación exige. Quedan 18 PRs de Dependabot abiertos sin fundir.
- **E2E:** no corren en el CI. En la última ejecución informada (2026-11-27), `biblioteca/biblioteca.spec.ts` falla por un defecto de la propia prueba (busca «@» y encuentra el correo de contacto del pie).
- **Instalación desplegada:** la capacidad en la VM ARM y la ruta completa de la tienda no se midieron.
- **RNF02 y 2 000 usuarios** con el dimensionamiento declarado: no cumplen (secciones 2 y 2.2; con el perfil realista se sostienen ≈ 1 000 sesiones); con Inventario 2 CPU + PostgreSQL 1 CPU, 2 000 sesiones **sí** se sostienen (sección 2.3; solo el Inventario). Decisión DD-18.
- **Perfil realista sin el E-commerce:** el catálogo, el carrito y el pedido (VM-VENTAS) y la latencia de extremo a extremo no se midieron; una sola corrida completa y una repetición parcial.
- **Medición en la VM ARM real**, despliegue, restauración con carga y llaves reales de pasarela y asistente: fuera del Sprint 2.
- **E-commerce bajo k6:** se midió el Inventario (donde se concentra la concurrencia de stock); el tramo del E-commerce del checkout no se midió con k6.
- **Hallazgos abiertos** de otros carriles: [`progress/hallazgos.md`](../../progress/hallazgos.md).
