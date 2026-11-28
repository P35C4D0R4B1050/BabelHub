# Recorte de pruebas de los módulos de negocio (carril 3)

Fecha: 2026-11-27. Rama: `claude/pruebas-negocio-c1f63f`. Alcance: `tests/unit/**` y las carpetas de negocio de `tests/integration/BabelHub.IntegrationTests/`. Sin cambios en `src/`.

Criterio (ratificado el 2026-11-27): críticos ≥ 85 % en Dominio y Aplicación y ≥ 70 % en total; no críticos (Asistente, Carrito, Catálogo, Devoluciones, Envíos, Notificaciones) ≥ 60 % y ≥ 40 %. Web y Reclamaciones son críticos.

## 1. Resultado

| | Antes | Después | Diferencia |
| :--- | ---: | ---: | ---: |
| Casos unitarios | 8511 | 8043 | −468 (−5,5 %) |
| Casos de integración (mis carpetas) | 4259 | 4166 | −93 (−2,2 %) |
| Casos unitarios en módulos no críticos | 1887 | 1419 | −468 (−25 %) |
| Casos de integración en módulos no críticos | 879 | 786 | −93 (−11 %) |

381 métodos de prueba menos y 70 teorías consolidadas; 111 archivos tocados y 5600 líneas menos. Todo en Carrito, Catálogo, Devoluciones, Envíos y Notificaciones.

**La ganancia de tiempo es pequeña.** En la línea base, lo quitado sumaba 39 s en integración (de 1970 s sumados) y 15 s en unitarias; en reloj de pared no se distingue del ruido. Mis carpetas de integración tardaron entre 6 y 9 minutos según la carga, así que la mayor parte de los 45 minutos de la suite parece estar en carpetas de otros carriles.

## 2. Cómo se midió

- Casos y segundos: informes `trx`. Los segundos son la **suma de las duraciones de cada prueba**, no el reloj de pared (las pruebas corren en paralelo).
- Cobertura: `tools/CoverageCheck` del commit `2bec81b2` (carril 1), ejecutado desde una copia temporal, sobre los informes de las unitarias y **mis** carpetas de integración. Faltan las de los otros carriles.
- Criterios: el conjunto de `[Trait("Criterio", …)]` de todo `tests/` es el mismo antes y después (646).

## 3. Casos y tiempo por módulo

| Módulo no crítico | Métodos quitados (unit. / integ.) | Casos unit. quitados (s base) | Casos integ. quitados (s base) |
| :--- | ---: | ---: | ---: |
| Carrito | 136 / 8 | 185 (4,1) | 30 (2,5) |
| Catálogo | 82 / 13 | 110 (6,8) | 14 (33,5) |
| Devoluciones | 55 / 4 | 66 (1,8) | 16 (1,6) |
| Envíos | 55 / 5 | 67 (2,1) | 20 (1,2) |
| Notificaciones | 19 / 4 | 40 (0,5) | 13 (0,6) |
| **Total** | **347 / 34** | **468 (15,3)** | **93 (39,2)** |

«s base» es la duración, en la línea base, de los casos quitados. En la tabla siguiente **los segundos de antes y después no son comparables**: la segunda corrida tuvo mucha más carga y módulos sin ningún cambio suben hasta el cuádruple (Web: 316 → 1306 s).

| Módulo | Unitarias: casos | Integración: casos | Unitarias: s | Integración: s |
| :--- | ---: | ---: | ---: | ---: |
| Ajustes (Inventario) | 165 | 48 | 1 → 2 | 33 → 79 |
| Auditoria (Inventario) | 814 | 90 | 2 → 4 | 41 → 36 |
| Auditoria | 877 | 187 | 14 → 11 | 90 → 167 |
| Biblioteca | 155 | 104 | 4 → 4 | 51 → 111 |
| Carrito | 679 → 494 | 248 → 218 | 19 → 15 | 55 → 106 |
| Catalogo | 466 → 356 | 204 → 190 | 20 → 13 | 154 → 154 |
| Devoluciones | 297 → 231 | 156 → 140 | 9 → 6 | 130 → 175 |
| Envios | 257 → 190 | 132 → 112 | 5 → 4 | 36 → 118 |
| Fuentes (Inventario) | 367 | 146 | 1 → 2 | 48 → 137 |
| Identidad | 750 | 342 | 17 → 13 | 124 → 235 |
| Notificaciones | 188 → 148 | 139 → 126 | 3 → 2 | 29 → 47 |
| Otros | 14 | 2 | 0 → 0 | 0 → 0 |
| Pagos | 583 | 401 | 18 → 16 | 192 → 230 |
| Pedidos | 351 | 226 | 13 → 10 | 263 → 361 |
| Plataforma (Inventario) | 421 | 0 | 1 → 3 | 0 → 0 |
| Plataforma | 646 | 0 | 11 → 8 | 0 → 0 |
| Reclamaciones | 312 | 161 | 6 → 5 | 35 → 82 |
| Reservas (Inventario) | 500 | 245 | 2 → 4 | 248 → 427 |
| Traslados (Inventario) | 263 | 155 | 1 → 2 | 123 → 185 |
| Web | 406 | 1273 | 8 → 8 | 316 → 1306 |
| **Total** | 8511 → 8043 | 4259 → 4166 | 156 → 132 | 1970 → 3955 |

Las carpetas de integración de Plataforma son de otro carril. Reloj de pared de mi corrida de integración: 418 s antes y 580 s después.

## 4. Cobertura por módulo (Dominio y Aplicación / total, %)

| Módulo | Nivel | Antes | Después |
| :--- | :--- | ---: | ---: |
| Ecommerce/Auditoria | crítico | 96.2 / 95.1 | 96.2 / 95.1 |
| Ecommerce/Biblioteca | crítico | 91.7 / 90.4 | 91.7 / 90.4 |
| Ecommerce/Carrito | no crítico | 94.3 / 94.8 | 92.3 / 92.9 |
| Ecommerce/Catalogo | no crítico | 94.8 / 94.6 | 92.9 / 92.8 |
| Ecommerce/Devoluciones | no crítico | 96.3 / 94.4 | 94.3 / 92.8 |
| Ecommerce/Envios | no crítico | 92.2 / 93.8 | 90.9 / 92.4 |
| Ecommerce/Identidad | crítico | 97.0 / 97.0 | 97.0 / 97.0 |
| Ecommerce/Notificaciones | no crítico | 96.5 / 94.0 | 95.2 / 92.9 |
| Ecommerce/Pagos | crítico | 96.5 / 96.6 | 96.5 / 96.6 |
| Ecommerce/Pedidos | crítico | 96.0 / 93.2 | 96.0 / 93.2 |
| Ecommerce/Plataforma | crítico | 94.9 / 78.7 | 94.9 / 78.7 |
| Ecommerce/Reclamaciones | crítico | 94.5 / 94.7 | 94.5 / 94.7 |
| Ecommerce/Web | crítico | 97.8 / 94.2 | 97.8 / 94.2 |
| Inventario/Ajustes | crítico | 92.3 / 93.4 | 92.3 / 93.4 |
| Inventario/Auditoria | crítico | 95.1 / 93.5 | 95.1 / 93.5 |
| Inventario/Fuentes | crítico | 98.7 / 98.8 | 98.7 / 98.8 |
| Inventario/Plataforma | crítico | 94.7 / 56.7 | 94.7 / 56.7 |
| Inventario/Reservas | crítico | 96.5 / 96.7 | 96.5 / 96.7 |
| Inventario/Traslados | crítico | 96.4 / 97.5 | 96.4 / 97.5 |

Todos los módulos cumplen sus pisos salvo `Inventario/Plataforma` en total (56,7 %), **igual antes y después**: probablemente sus pruebas de integración están en carpetas que no ejecuté. Hay que confirmarlo con la corrida completa.

## 5. Qué se quitó y por qué

Solo en módulos no críticos.

| Grupo | Dónde | Motivo |
| :--- | :--- | :--- |
| Filas equivalentes en teorías de 4 filas o más | Los cinco módulos | Varias filas recorrían la misma rama. Se conservan las clases de error y los bordes que distinguen; las teorías de 2 o 3 filas no se tocaron. |
| Matriz de roles repetida en cada caso de uso | Envíos (unitarias) | `MatrizDeRolesDeEnviosTests` ya cruza los roles con las operaciones. |
| Idempotencia, «no hace nada», variantes del camino feliz y órdenes internos de la misma regla | Los cinco módulos (sobre todo unitarias) | Redundantes con la prueba principal del criterio; se conservan los rechazos principales, la auditoría caída, la concurrencia, el dinero y la privacidad. |
| Pruebas de dobles de prueba | `DoblesDeEnviosTests`, `DoblesDelCatalogoTests` | Prueban código de pruebas, no del producto. |
| Constantes, formas de tipos, «los tipos existen», nombres y horarios de trabajos | Carrito, Envíos, Devoluciones, Notificaciones | Triviales; los límites entre módulos los exige `tests/architecture`. |
| Validaciones y reglas ya probadas en unitarias, repetidas contra la API, las pantallas o PostgreSQL | `IngresosConInventarioSimuladoTests` y API y pantallas de los cinco módulos | Segundo nivel sin nada propio. Quedan la persistencia, las restricciones, la concurrencia y los módulos reales. |
| Búsqueda de 16 obras y 20 pares sobre la misma unidad, repetidas dentro de integración | `LecturasDelCatalogoPostgresTests`, `RepositorioDeDevolucionesPostgresTests` | El mismo comportamiento está en `CatalogoConPostgresYAlmacenTests` y `ConcurrenciaYReintentosDeDevolucionesTests`. |
| Libro de 50 MiB contra el adaptador S3 (≈20 s) | `S3AlmacenDeArchivosTests` | `CatalogoConPostgresYAlmacenTests` prueba el tope exacto y el byte de más contra el almacén S3 real, como pide el plan. Se pierde la comprobación de la huella en ese tamaño a nivel del adaptador. |

El detalle prueba por prueba está en el diff de cada commit.

## 6. Qué no se tocó

- **Críticos** (Ajustes, Auditoría, Biblioteca, Fuentes, Identidad, Pagos, Pedidos, Plataforma, Reclamaciones, Reservas, Traslados y Web): sin recortes.
- `AuditoriaRealDelCatalogoTests` (Catálogo): sus filas prueban la atomicidad de la auditoría operación por operación.

## 7. Decisiones delegadas (por ratificar)

Se anotan aquí para no chocar con los otros carriles en `progress/decisiones-delegadas.md`.

| Id | Decisión | Motivo |
| :--- | :--- | :--- |
| DD-170 | En los módulos críticos no se quitó ninguna prueba, tampoco la fila de 7 días de la sesión (≈50 s). | La tarea T19 de `web-base` pide los topes de 7 días y 8 horas en integración. |
| DD-171 | Las copias de Auditoría y Plataforma entre los dos servicios (cientos de cuerpos idénticos) **no** cuentan como duplicado exacto. | Cada copia prueba el código y la migración de su servicio. |
| DD-172 | Las repeticiones que fija un plan aprobado (20 pares en Pedidos y Devoluciones, 50 pares de descarga × reembolso) no se reducen. | Cambiarlas es cambiar un plan aprobado. |
| DD-173 | En no críticos se quitan segundos niveles y variantes aunque cubran un caso de rechazo, si queda otra prueba del mismo criterio. | «Más laxitud en el resto»; los umbrales 60/40 se cumplen con holgura. |

## 8. Verificación y pendientes

**Verificado** sobre el estado final: `dotnet build BabelHub.sln -warnaserror` sin advertencias; unitarias (5506 y 2537, 0 fallos); mis carpetas de integración (4162 superadas, 4 omitidas, 0 fallos); arquitectura (264); `SpecCheck` sin errores ni avisos; 646 criterios nombrados antes y después; cobertura por módulo.

**No verificado:** la suite completa, las carpetas de otros carriles, E2E y carga; la cobertura con toda la suite. Las filas y pruebas quitadas se revisaron por nombre y datos, no con pruebas de mutación.

**Pruebas inestables en código que no toqué** (cada una falló una vez, en una corrida intermedia o de línea base, y pasó en la corrida final):

- `EjecucionDeLaCancelacionTests.Al_ejecutarse_la_cancelacion_llama_a_los_receptores_en_el_orden_decidido…` (Identidad, unitaria): la lista estática `AplicadorDePrueba.OrdenDeLlamada` recibió llamadas duplicadas, probablemente de otra clase en paralelo.
- `CargaDelAcumuladoTests` (Auditoría, ≈49 s): `lock timeout` con la máquina cargada.

**Dónde habría más ahorro** (requiere cambiar un plan o una tarea aprobados, o la criticidad; duraciones de la línea base):

| Prueba | s |
| :--- | ---: |
| `ConcurrenciaDePedidosTests`: 21 pares × 20 repeticiones | 150 |
| Sesión: 7 días y 8 horas avanzando cada 25 minutos | 50 |
| `CargaDelAcumuladoTests` | 49 |
| Pagos: 50 carreras entre cierre de intenciones y carrito | 48 |
| Devoluciones: 50 pares de descarga × reembolso | 46 |
