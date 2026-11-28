# Recorte de pruebas de los módulos de negocio (carril 3)

Fecha: 2026-11-27. Rama: `claude/pruebas-negocio-c1f63f`. Alcance: `tests/unit/**` y las carpetas de negocio de `tests/integration/BabelHub.IntegrationTests/`. Sin cambios en `src/`.

Dos pasadas. La primera usó los pisos 85/70 y 60/40 y solo tocó los módulos no críticos. En la segunda, por decisión de la persona del 2026-11-27, **los porcentajes son solo referenciales** y se recorta por el valor de cada prueba, también en los críticos; en ellos se conservan rechazos, autorización, concurrencia, dinero y datos personales.

## 1. Resultado

| Casos | Antes | Pasada 1 | Pasada 2 | Total quitado |
| :--- | ---: | ---: | ---: | ---: |
| Unitarias | 8511 | 8043 | 7923 | −588 (−6,9 %) |
| Integración (mis carpetas) | 4259 | 4166 | 3949 | −310 (−7,3 %) |
| De ellos, no críticos: unitarias | 1887 | 1419 | 1419 | −468 (−25 %) |
| De ellos, no críticos: integración | 879 | 786 | 786 | −93 (−11 %) |

623 métodos de prueba menos y 91 teorías con 229 filas menos; 213 archivos tocados y unas 9000 líneas menos.

**Tiempo.** Lo quitado sumaba en la línea base **175 s en integración** (de 1970 s sumados en mis carpetas, −9 %) y 17 s en unitarias. Tres pruebas explican 115 s: la carga del acumulado de auditoría (49 s), el tope de 7 días de la sesión (46 s) y el libro de 50 MiB contra el adaptador S3 (20 s). La suite completa sobre `d96d9873` tarda 45 min 33 s en integración (dato de la coordinación) y mi corrida de línea base, solas mis carpetas, unos 6 minutos: **la mayor parte del tiempo está fuera de mi alcance**. La medición «después» la hará la coordinación con los tres carriles integrados.

## 2. Vicios encontrados (métodos quitados, las dos pasadas)

La clasificación es aproximada: se asignó por el nombre y el archivo de cada método.

| Vicio | Métodos | Ejemplos | Por qué aporta poco |
| :--- | ---: | :--- | :--- |
| Variantes redundantes de la misma regla | 333 | «Repetir no cambia nada», «sin datos no hace nada», el mismo camino feliz con otro actor, órdenes internos de bloqueo | Fallan junto con la prueba principal del criterio. |
| Composición, constantes y forma del código | 89 | `ElGrafoDeIdentidad_SeValida…`, `CadaConstanteDeAccion_TieneElNombreDeSuValor`, `ClaveDePolitica_ConElMismoValor_EsIgual`, «la raíz expone los contratos», nombres e intervalos de trabajos | Lo detectan el compilador, `tests/architecture` o las pruebas con el host real. |
| El mismo comportamiento en otro nivel | 79 | Carga del acumulado (además inestable), API y pantallas con la aplicación simulada que repiten validaciones | El segundo nivel no demuestra nada propio. |
| Pruebas del soporte, de los dobles o de la propia prueba | 34 | `ActoresDePruebaTests` (en los dos servicios), `DoblesDeEnviosTests`, dobles de reservas, `PostgreSQL_NoLanzaErrorSinAlcance…`, `ElIdentificadorQueUsaElFixture…`, «la revisión de colores los detecta» | Prueban código de pruebas o a PostgreSQL, no el producto. |
| Persistencia repetida o trivial | 23 | La misma prueba de 50 pares en `Concurrencia` y en `Repositorios` de Biblioteca; 50 registros simultáneos dos veces en Reclamaciones | Duplicados literales, o guardar y leer ya recorridos por los flujos. |
| El mismo bucle de trabajos de fondo por módulo | 21 | «La cancelación del apagado no es un fallo», «corre al empezar», en Auditoría, Identidad, Pagos y Reclamaciones | Se conservan las alertas por fallos. |
| Copia entre servicios sin riesgo propio | 17 | En `AuditoriaInventario`: migración idempotente, índices, paginación, filtros | Quedan en los dos servicios inmutabilidad, permisos, atomicidad, concurrencia y anonimización. |
| Guardas de argumentos de programación | 15 | `Un_pedido_vacio_es_un_error_de_programacion`, `Alertas_ConDatosInvalidos_Lanzan` | Ninguna entrada de usuario las alcanza. |
| Simulador de la pasarela | 12 | Cobro lento, el simulador no es idempotente, reembolsos simulados | Prueban el doble; queda que nunca aprueba lo desconocido ni repite el token. |

En las teorías, el vicio fue **muchas filas de la misma clase** (formatos inválidos, páginas, códigos de error): se conservan las clases y los bordes.

## 3. Cómo se midió

- Casos y segundos: informes `trx`. Los segundos son la **suma de las duraciones de cada prueba**, no el reloj de pared. «Antes» es mi corrida sobre `d96d9873`. Las corridas posteriores tuvieron mucha más carga (módulos sin cambios suben varias veces), así que no se comparan segundos antes y después: solo se cuenta la duración base de lo quitado.
- Cobertura: `tools/CoverageCheck` del commit `2bec81b2` (carril 1), ejecutado desde una copia temporal, sobre las unitarias y **mis** carpetas de integración. Faltan las de los otros carriles.
- Criterios: el conjunto de `[Trait("Criterio", …)]` de todo `tests/` es el mismo antes y después (646).

## 4. Casos por módulo

| Módulo | Unitarias | Integración |
| :--- | ---: | ---: |
| Ajustes (Inventario) | 165 | 48 |
| Auditoria (Inventario) | 814 → 799 | 90 → 66 |
| Auditoria | 877 → 862 | 187 → 177 |
| Biblioteca | 155 → 153 | 104 → 94 |
| Carrito | 679 → 494 | 248 → 218 |
| Catalogo | 466 → 356 | 204 → 190 |
| Devoluciones | 297 → 231 | 156 → 140 |
| Envios | 257 → 190 | 132 → 112 |
| Fuentes (Inventario) | 367 → 343 | 146 → 140 |
| Identidad | 750 → 749 | 342 → 318 |
| Notificaciones | 188 → 148 | 139 → 126 |
| Otros | 14 → 2 | 2 |
| Pagos | 583 → 582 | 401 → 357 |
| Pedidos | 351 | 226 → 218 |
| Plataforma (Inventario) | 421 | 0 |
| Plataforma | 646 → 623 | 0 |
| Reclamaciones | 312 → 311 | 161 → 150 |
| Reservas (Inventario) | 500 | 245 → 237 |
| Traslados (Inventario) | 263 | 155 → 151 |
| Web | 406 → 380 | 1273 → 1205 |
| **Total** | 8511 → 7923 | 4259 → 3949 |

Las carpetas de integración de Plataforma son de otro carril.

## 5. Cobertura por módulo (referencial; Dominio y Aplicación / total, %)

| Módulo | Nivel | Antes | Después |
| :--- | :--- | ---: | ---: |
| Ecommerce/Auditoria | crítico | 96.2 / 95.1 | 95.8 / 94.7 |
| Ecommerce/Biblioteca | crítico | 91.7 / 90.4 | 91.7 / 90.4 |
| Ecommerce/Carrito | no crítico | 94.3 / 94.8 | 92.3 / 92.9 |
| Ecommerce/Catalogo | no crítico | 94.8 / 94.6 | 92.9 / 92.8 |
| Ecommerce/Devoluciones | no crítico | 96.3 / 94.4 | 94.3 / 92.8 |
| Ecommerce/Envios | no crítico | 92.2 / 93.8 | 91.4 / 92.8 |
| Ecommerce/Identidad | crítico | 97.0 / 97.0 | 97.0 / 97.0 |
| Ecommerce/Notificaciones | no crítico | 96.5 / 94.0 | 95.2 / 92.9 |
| Ecommerce/Pagos | crítico | 96.5 / 96.6 | 96.5 / 96.6 |
| Ecommerce/Pedidos | crítico | 96.0 / 93.2 | 96.0 / 93.2 |
| Ecommerce/Plataforma | crítico | 94.9 / 78.7 | 92.7 / 77.5 |
| Ecommerce/Reclamaciones | crítico | 94.5 / 94.7 | 94.5 / 94.7 |
| Ecommerce/Web | crítico | 97.8 / 94.2 | 97.8 / 94.2 |
| Inventario/Ajustes | crítico | 92.3 / 93.4 | 92.3 / 93.4 |
| Inventario/Auditoria | crítico | 95.1 / 93.5 | 94.3 / 90.9 |
| Inventario/Fuentes | crítico | 98.7 / 98.8 | 98.1 / 98.5 |
| Inventario/Plataforma | crítico | 94.7 / 56.7 | 94.7 / 56.7 |
| Inventario/Reservas | crítico | 96.5 / 96.7 | 96.5 / 96.7 |
| Inventario/Traslados | crítico | 96.4 / 97.5 | 96.4 / 97.5 |

`Inventario/Plataforma` mide 56,7 % en total porque sus pruebas de integración están en carpetas que no ejecuté; como no pude confirmar que supere 70 %, **no se recortó**.

## 6. Qué se conservó a propósito

- En los módulos críticos: rechazos, matrices de autorización, concurrencia sobre base real, dinero y datos personales.
- Guardas de arranque en producción (pasarela, claves, dobles provisionales), permisos y restricciones de base de datos, e inmutabilidad de la auditoría en los dos servicios.
- Las repeticiones que fija un plan aprobado: 21 pares × 20 en Pedidos, 50 carreras en Pagos y 50 pares en Devoluciones (≈245 s en la línea base). Reducirlas es cambiar un plan.
- `AuditoriaRealDelCatalogoTests`: sus filas prueban la atomicidad de la auditoría operación por operación.

## 7. Decisiones delegadas (por ratificar)

Se anotan aquí para no chocar con los otros carriles en `progress/decisiones-delegadas.md`.

| Id | Decisión | Motivo |
| :--- | :--- | :--- |
| DD-170 | Se quitó la fila del cliente (7 días) de la sesión de extremo a extremo, aunque la tarea T19 de `web-base` menciona los dos topes en integración. | Lo prueban `SesionWebTests` y `CargarSesionWebTests`; la fila de 8 horas demuestra el mismo cableado real. |
| DD-171 | Entre las copias de los dos servicios se quitan las de forma, constantes y persistencia; las de rechazos, autorización, concurrencia y datos personales siguen en ambos. | Cada copia solo aporta algo propio donde ejecuta código con riesgo. |
| DD-172 | No se reducen las repeticiones fijadas por un plan aprobado. | No es quitar una prueba repetida, sino debilitarla. |
| DD-173 | Se quitan segundos niveles y variantes aunque cubran un caso de rechazo, si queda otra prueba del mismo criterio. En los no críticos el filtro fue más laxo. | Criterio de valor de cada prueba. |
| DD-174 | `Inventario/Plataforma` no se recorta. | Mi medición parcial no llega a 70 %. |
| DD-175 | Se conservan los «guardar y leer» de dinero y datos personales (pedidos, pagos, solicitudes de datos, registro de auditoría). | Detectan fallos propios de mapeo. |
| DD-176 | Se recortan las pruebas del simulador de la pasarela, el adaptador de los entornos sin proveedor. | Prueban un doble; los flujos de pago lo siguen ejercitando. |

## 8. Verificación y pendientes

**Verificado** sobre el estado final: `dotnet build BabelHub.sln -warnaserror` sin advertencias; unitarias (5431 y 2492, 0 fallos); arquitectura (264); `SpecCheck` sin errores ni avisos; 646 criterios nombrados. Mis carpetas de integración pasaron completas (3935 superadas, 4 omitidas, 0 fallos) y después se restauraron 10 pruebas, cuyas cuatro clases pasaron (81 de 81).

**No verificado:** la suite completa, las carpetas de otros carriles, E2E y carga; la cobertura con toda la suite (la de integración de la tabla se midió antes de restaurar esas 10 pruebas). Las pruebas quitadas se eligieron revisando nombres y datos, sin pruebas de mutación; en los críticos conviene que alguien revise el diff. La segunda pasada no revisó una por una las unitarias de los módulos críticos: se centró en los tipos de vicio de la tabla.

**Pruebas inestables** (fallaron una vez y pasaron en las corridas siguientes):

- `EjecucionDeLaCancelacionTests.Al_ejecutarse_la_cancelacion_llama_a_los_receptores_en_el_orden_decidido…` (código que no toqué): la lista estática `AplicadorDePrueba.OrdenDeLlamada` recibió llamadas duplicadas, probablemente de otra clase en paralelo.
- `CargaDelAcumuladoTests` fallaba por `lock timeout` con carga; quedó quitada.
