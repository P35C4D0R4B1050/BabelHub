# 09 Análisis de la base de datos y de la caché (DOC-1)

Qué hace la base de datos en las consultas críticas del Inventario, cómo se protege la concurrencia y qué se cachea, **con las mediciones de la prueba de carga** (K6-1, `tests/load/`, resultados en `progress/evidencias/k6/2026-11-20/`). Complementa [05 Modelo de datos](05-modelo-de-datos.md), [06 Estados y concurrencia](06-estados-y-concurrencia.md) y `docs/02-arquitectura-de-software/07-presupuesto-de-recursos.md`.

## 1. Cómo se midió

- **Entorno:** el Inventario y PostgreSQL 17 en contenedores con límites de CPU y memoria (`tests/load/entorno/`): la VM-INVENTARIO de 1 OCPU repartida entre el Inventario (0,6) y PostgreSQL (0,4), con `shared_buffers=768MB`. Comparación con los límites declarados en `infra/docker-compose.inventario.yml` (Inventario 0,3 y PostgreSQL 0,8). Máquina de desarrollo x86, no la VM ARM: valen los órdenes de magnitud y los umbrales, no los milisegundos absolutos.
- **Qué se mide en la base:** `pg_stat_statements` (consultas por tiempo total), `pg_stat_user_tables` y `pg_stat_user_indexes` (índices frente a lecturas secuenciales), `pg_stat_database` (interbloqueos, aciertos de caché del búfer) y las conexiones por cuenta, reiniciados antes de cada escenario (`<escenario>-base.txt`).
- **Datos:** pocos (decenas de ediciones y fuentes, cientos de reservas por corrida): sirven para ver **qué índice usa el plan y cuánto cuesta cada sentencia**, no para dimensionar el crecimiento (el techo de filas está en el modelo de datos).

## 2. Consultas críticas e índices

| Operación (RNF) | Sentencias por solicitud | Índice que las sirve | Medido (`disponibilidad`, 4 500 solicitudes) |
| :--- | :--- | :--- | :--- |
| Consulta de disponibilidad (RNF02, FUE-10) | 1 lectura del stock de las ediciones (`WHERE edicion_id = ANY($1) ORDER BY edicion_id, fuente_id`), 1 de las fuentes, 1 de las ediciones y 1 de las reservas pendientes por fuente y edición | `pk_stock (fuente_id, edicion_id)` e `ix_stock_edicion (edicion_id)`; `uq_fuentes_codigo`; `ediciones_pkey`; `ix_reserva_lineas_stock (fuente_id, edicion_id, estado)` | 0,10 a 0,15 ms de media por sentencia; ~1 ms de base por solicitud |
| Selección de fuente y reserva (SEL, AC01) | Lectura del stock **con `FOR UPDATE`**, inserción de la reserva y de sus líneas, actualización del stock | `ix_stock_edicion`, `pk_stock`, `ux_reserva_lineas_activa`, `uq_reservas_cliente_clave` (idempotencia) | Todas por índice (0 lecturas secuenciales en `stock`, `reservas`, `reserva_lineas`) |
| Confirmar, extender y liberar (SEL) | Búsqueda de las líneas del pedido y actualización de la reserva | `ix_reserva_lineas_pedido`, `ix_reservas_pedido` | 0,3 a 0,4 ms de media |
| Vencimiento de reservas (trabajo de fondo) | Reservas con fecha de expiración vencida | `ix_reservas_vencimiento` (parcial: solo las que vencen) | 2,8 ms de media bajo carga de reserva (consulta de fondo) |
| Límites de uso (API4, transversal-seguridad) | 1 inserción en `plataforma.eventos_limite` y 1 lectura por clave | `ix_eventos_limite_clave_instante (clave, instante)` | 0,07 a 0,37 ms |
| Auditoría de la operación | 1 inserción en `auditoria.registros` en la misma transacción | `registros_pkey` y los índices parciales de consulta | 0,9 ms |

Conclusiones medidas:

1. **Ninguna consulta crítica lee la tabla completa.** Las lecturas secuenciales que aparecen son de `fuentes.fuentes` (16 filas: es más barato leerla entera que usar un índice) y son lo correcto para esa tabla.
2. **La base no es el cuello de botella de la consulta de disponibilidad:** son ~5 sentencias de décimas de milisegundo (~1 ms de base por solicitud) y el acierto de caché del búfer es del 99,99 %. Esta cifra mide solo el tiempo de ejecución de cada sentencia; **la capacidad medida (sección 6b) la corrige**: cada solicitud cuesta unas 13 idas a la base y PostgreSQL es, con el servicio, el cuello de botella.
3. **Costo fijo por transacción de la seguridad por filas** (sección 4): `SET LOCAL ROLE`, `current_user` y `current_setting` suman 3 sentencias de ~0,01 ms; despreciable en tiempo, visibles en el conteo de sentencias (18 000 `SET LOCAL ROLE` en 4 500 solicitudes).
4. **Reinicio de sesión (`DISCARD ALL`):** Npgsql lo manda al devolver cada conexión al grupo (~0,05 ms; en la carga fue la tercera sentencia por tiempo total). Se quitó del Inventario (`No Reset On Close`, sección 6c) porque la aplicación no deja estado de sesión: el rol, el alcance y las esperas valen solo dentro de la transacción. **Invariante que lo sostiene: ningún código del Inventario deja estado de sesión (`SET` sin `LOCAL`, `set_config(…, false)`, tablas temporales); los bloqueos asesores de sesión cierran la conexión física si no pueden soltarse.** El E-commerce conserva el reinicio.

## 3. Bloqueos y concurrencia (AC01)

- **Reservar bloquea las filas de stock de las ediciones pedidas con `SELECT … FOR UPDATE ORDER BY edicion_id, fuente_id`.** El orden fijo evita los interbloqueos entre reservas que tocan varias filas; la regla de dominio no basta (AGENTS.md §4), la atomicidad la da la base.
- **Medición (`sobreventa`, 100 usuarios sobre 10 unidades de una sola edición):** exactamente 10 reservas (201) y 90 rechazos `409 INV-STOCK-INSUFICIENTE`, vendible final 0, **0 interbloqueos**. La sentencia con `FOR UPDATE` tuvo 116 ms de media y 802 ms como máximo en la corrida: es la espera serializada por la fila (todas las solicitudes compiten por la misma), no un plan lento.
- **Idempotencia:** `uq_reservas_cliente_clave (cliente_id, clave_idempotencia)` y las claves únicas de `operaciones_idempotentes` hacen que repetir una solicitud no duplique el efecto (SEL-31).
- **Límite de conexiones por cuenta (hallazgo de la carga, corregido):** cada cuenta tiene `CONNECTION LIMIT 20` y el grupo de Npgsql abría hasta 100; con más de 20 consultas simultáneas la base rechazaba con «too many connections» y la Api respondía 500. El grupo queda ahora en **16 por omisión** (`BasesDelInventario.Abrir` y `BasesDelEcommerce.Abrir`; un valor explícito de la cadena se respeta): el exceso espera un turno en lugar de fallar. Con la base caída o sin conexiones, la Api responde **503 uniforme** y no 500.
- **Resiliencia medida:** con PostgreSQL pausado 25 s el proceso sigue vivo (`/health/live` 200), las operaciones fallan con 503 `INV-SERVICIO-NO-DISPONIBLE` y, al reanudar la base, el servicio se recupera solo, sin reiniciar. **Hallazgo abierto:** `/health/ready` sigue respondiendo 200 con la base caída porque el Inventario no registra ninguna comprobación (`progress/hallazgos.md`).

## 4. Seguridad por filas

Cada transacción asume un rol de base de datos por módulo (`SET LOCAL ROLE "rol_fuentes"`, `"inventario_acceso"`) y las políticas de fila (`CREATE POLICY … USING (current_setting('babelhub.alcance', true) = 'red' OR … = fuente_id)`) comparan la configuración de la transacción con el alcance del actor (la red completa o la fuente del usuario): una consulta fuera de alcance no devuelve filas y la Aplicación la traduce a `403` registrado (`AlcanceDeDatosDenegadoException`), nunca a «sin stock». Costo medido: tres sentencias de ~0,01 ms por transacción (sección 2). Las cuentas de aplicación y de depuración son distintas (`inventario_acceso`, `inventario_depurador_*`) y ninguna es propietaria de los esquemas.

## 5. Caché

| Qué | Dónde | Expiración | Estado |
| :--- | :--- | :--- | :--- |
| Contadores de límites de uso del E-commerce | Redis dedicado (`noeviction`, 100 MB, ACL y TLS) | La de cada ventana de la política (por ejemplo, 1 min por usuario) | Implementado (`MotorDeLimitesRedis`); el Inventario guarda sus contadores en PostgreSQL (`plataforma.eventos_limite`), por diseño |
| Claves públicas y revocaciones de Identidad | En memoria del Inventario | Se renuevan cada 5 minutos como máximo; sin renovación, se rechaza toda credencial (IDE-61) | Implementado (`CacheDeClavesDelInventario`) |
| **Disponibilidad de ediciones** | Redis de caché del E-commerce (AD06) | Expiración corta (propuesta: 5 a 15 s) | **No implementado.** El código no cachea la disponibilidad: cada consulta va a la base |
| Catálogo | Redis de caché | Expiración mayor (propuesta: minutos) | No implementado en el Inventario; el catálogo es del E-commerce |

**Qué no se cachea y por qué:** la decisión de reservar **nunca** usa la caché (se valida contra la base con bloqueo; `06-tacticas-arquitectonicas.md`), el stock exacto ni los datos de reserva, ni nada que dependa del alcance del usuario.

**Con y sin caché:** el plan pedía comparar el p95 con y sin caché de disponibilidad. Como esa caché no existe en el código, **no hay medición con caché**; la línea base sin caché es la de la sección 6. Como la CPU de PostgreSQL es parte del cuello de botella (sección 6b), una caché de disponibilidad en el E-commerce quitaría solicitudes enteras al **Inventario y a PostgreSQL** (la VM de 1 OCPU). Decidirla es de la persona responsable (alcance del Sprint 2).

## 6. Techo medido de la consulta de disponibilidad

| Configuración | Prueba | p95 | Resultado |
| :--- | :--- | :--- | :--- |
| Inventario 0,6 CPU y PostgreSQL 0,4 | 50 usuarios, pausa de 0,5 s (`disponibilidad`) | 327 ms | **Cumple RNF02** (< 500 ms) |
| Inventario 0,6 CPU y PostgreSQL 0,4 | 50 usuarios sin pausa (peor caso) | 899 ms | No cumple |
| Límites declarados: Inventario 0,3 CPU y PostgreSQL 0,8 | 50 usuarios, pausa de 0,5 s | 1 774 ms | **No cumple** |
| Inventario 0,6 CPU y PostgreSQL 0,4 | Rampa (`rampa`): 50 / 100 / 250 / 500 / 1 000 / 2 000 usuarios | 434 ms / 1 095 ms / 7,2 s / 8,1 s / 16 s / 17 s | Cumple en 50; el techo está entre 50 y 100 usuarios; con 1 000 y 2 000 usuarios fallan el 19 % y el 52 % de las solicitudes (503 y plazos vencidos) |
| Inventario 0,6 CPU y PostgreSQL 0,4 | 5 compras por segundo, hasta 50 usuarios (`checkout-confirmacion`, reservar y confirmar) | 226 ms (umbral 3 s) | **Cumple** |

**Lectura:** con la CPU que la VM-INVENTARIO da hoy al Inventario según el Compose (0,3), RNF02 no se cumple en esta máquina; repartida la OCPU de otra forma sí. Palancas, de mayor a menor efecto: (1) subir la CPU del contenedor del Inventario; (2) reducir el costo por solicitud en el servicio (la autorización, los límites y la auditoría hacen su propia ida a la base por solicitud; agrupar o diferir los contadores de límites de las lecturas); (3) caché de disponibilidad delante del Inventario (sección 5). La rampa muestra además que el servicio **se degrada de forma controlada** (503 uniformes, sin 500 ni interbloqueos) más allá de su techo; los 2 000 usuarios virtuales no se sostienen con una sola VM de 1 OCPU, y la extrapolación está documentada aquí (`docs/07-proceso-de-desarrollo/06-plan-sprint-2.md` §8).

## 6b. Capacidad medida frente a recursos (rampa de 50 a 2000 usuarios virtuales)

Rampa de la consulta de disponibilidad (`tests/load/escenarios/rampa.js`, pausa de 0,5 s entre solicitudes, 40 s por nivel), con el Inventario y PostgreSQL en contenedores con los recursos de cada fila (memoria del Inventario 350 MB). **p95 por nivel de usuarios virtuales** (entre paréntesis, la tasa de errores cuando supera el 0,05 %; el criterio es p95 < 500 ms y menos de 1 % de errores):

| Inventario / PostgreSQL (CPU) / tope del grupo | 50 | 100 | 250 | 500 | 1 000 | 2 000 |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **0,3 / 0,8 / 16 (los declarados en el Compose)** | 897 ms | 1,5 s | 5,1 s | 8,3 s | 18,8 s (10 %) | 19,4 s (48 %) |
| 1 / 1 / 16 | 242 ms | 679 ms | 1,4 s | 5,2 s | 7,3 s | 13,3 s |
| 2 / 1 / 16 | 146 ms | 403 ms | 2,1 s | 7,6 s | 13,2 s | 15,2 s (27 %) |
| 2 / 1 / 40 | 308 ms | 981 ms | 3,3 s | 6,4 s | 13,6 s | 15,5 s (18 %) |
| 4 / 2 / 16 | 50 ms | 133 ms | 1,3 s | 2,4 s | 5,0 s | 7,2 s |
| 4 / 2 / 60 | 25 ms | 133 ms | 885 ms | 2,4 s | 5,7 s | 13,7 s |

(Referencia previa, 0,6 / 0,4 / 16: 434 ms con 50 usuarios y 1,1 s con 100.) Resultados completos en `progress/evidencias/k6/2026-11-20-capacidad/`.

**Lo que se mide, sin maquillar**

1. **Ninguna configuración sostiene 2 000 usuarios virtuales con p95 < 500 ms**; la mejor (4 / 2 CPU) da p95 de 7,2 s y sin errores, y con los límites declarados (0,3 CPU) ni siquiera 50 usuarios cumplen (897 ms) y con 2 000 falla casi la mitad de las solicitudes. Los usuarios que se sostienen con p95 < 500 ms son: 0 con 0,3 CPU, 50 con 1 / 1, 100 con 2 / 1 y 100 con 4 / 2 (con 250: 885 ms a 1,3 s).
2. **El cuello de botella medido es la CPU de PostgreSQL, junto con la del servicio.** Muestreo de `docker stats` en el nivel de 1 000 usuarios con 4 / 2 CPU: PostgreSQL queda **fijo en ~200 % (su tope de 2 CPU)**, el Inventario usa 130 a 300 % de sus 400 % y el generador de carga, 40 a 75 % de una CPU. Duplicar la CPU de PostgreSQL de 1 a 2 junto con la del Inventario baja el p95 de 1 000 usuarios de 13 s a 5 s. Esto **corrige** la conclusión 2 de la sección 2: el tiempo de ejecución de cada sentencia es pequeño, pero cada solicitud cuesta unas **13 idas a la base** (cambio de rol, comprobación del usuario y del alcance, reinicio de sesión y 3 lecturas), y esas idas, no las lecturas, consumen la CPU de PostgreSQL.
3. **Subir el tope del grupo de conexiones no ayuda** (2 / 1: 308 ms con tope 40 frente a 146 ms con 16 en 50 usuarios; 4 / 2: sin mejora en 1 000 y peor en 2 000): con la CPU de PostgreSQL saturada, más conexiones solo aumentan la contención. El tope de 16 queda.
4. **Rendimiento sostenido estimado** (ley de Little con la mediana: usuarios / (mediana + pausa)): con 0,3 / 0,8 ≈ 65 a 85 solicitudes por segundo; con 1 / 1 ≈ 100 a 170; con 4 / 2 ≈ 280 a 300. Cada solicitud cuesta del orden de 8 ms de CPU de PostgreSQL y 6 a 12 ms de CPU del servicio.
5. **Memoria:** el Inventario llegó a 263 MiB de los 350 MiB con 1 000 usuarios (75 %). El presupuesto de 350 MB queda justo para esa carga; se propone 512 MB si se espera ese nivel.
6. Con los recursos declarados, el sistema **se degrada de forma controlada** (503 y plazos vencidos, sin 500 ni sobreventa), pero degrada.

**Dimensionamiento propuesto (lo que no cumple)**

- **RNF02 (50 usuarios, p95 < 500 ms):** se cumple con el reparto de la OCPU de VM-INVENTARIO en 0,6 para el Inventario y 0,4 para PostgreSQL (434 ms medido con pausa de 0,5 s) y con 1 / 1 (242 ms); **no se cumple con los 0,3 CPU del Compose**: el límite del contenedor del Inventario debe subir a 0,6 como mínimo y el de PostgreSQL a 0,4 como mínimo, repartiendo la única OCPU de la VM.
- **2 000 usuarios virtuales con pausa de 0,5 s equivalen a ~4 000 solicitudes por segundo**: con el costo actual por solicitud necesitarían del orden de 30 CPU para PostgreSQL y otras tantas para el servicio; **no es un objetivo alcanzable en una VM de 1 OCPU**. Con una pausa realista de 5 a 10 s entre acciones de una persona, 2 000 usuarios son 200 a 400 solicitudes por segundo, que es **lo que sostiene la configuración de 4 / 2 CPU** (p95 degradado, sin errores). Para 2 000 usuarios reales en la VM de 1 OCPU de hoy hace falta, antes que más CPU, **reducir el costo por solicitud**.
- **Palancas de ingeniería (candidatas, no hechas; Importantes en `progress/hallazgos.md`):** (a) fusionar el cambio de rol y la primera sentencia de cada transacción en un solo viaje y quitar las comprobaciones de `current_user` y de `current_setting` por sentencia; (b) evitar el reinicio de sesión (`DISCARD ALL`) en cada devolución al grupo, compatible porque el rol es local a la transacción; (c) caché de disponibilidad delante del Inventario (sección 5), que quita solicitudes enteras; (d) los contadores de límites por origen en memoria o en Redis en lugar de insertar y leer en PostgreSQL en cada llamada.


## 6c. Reducción de idas a la base por consulta (K6-1, capacidad)

La conclusión 2 de la sección 6b (el costo está en las idas, no en las lecturas) se atendió así. **Medición por conteo de sentencias** con PostgreSQL real y el rol real de la aplicación (`CostoPorSolicitudDeDisponibilidadTests`, que cuenta lo que Npgsql envía a la base):

| Paso | Qué cambia | Sentencias por consulta (fichas, fuentes y stock; sin el tramo de Reservas) |
| :--- | :--- | :--- |
| Antes | `SELECT current_user` y `current_setting` por bloque, 3 sentencias de arranque, 2 por la elevación, `SET LOCAL ROLE` y su restauración por bloque | 21 (más 4 de Reservas, `BEGIN`/`COMMIT` y `DISCARD ALL`) |
| 1 | La unidad de trabajo lleva el rol y el alcance **en memoria** (es la única que los cambia): se quitan `current_user`, `current_setting` y la lectura previa de la elevación; un bloque anidado del mismo esquema no cambia de rol; el arranque (solo lectura, `lock_timeout`, alcance, rol) viaja en **una** sentencia | 12 |
| 2 | La entrada y la salida del bloque con alcance de red (rol + alcance) viajan cada una en **una** ida (`EjecutarComoConAlcanceDeRed`) | 10 |
| 3 (opcional, apagada) | Memoria de 5 s de la lista de fuentes (código y nombre) y del formato de las ediciones, **solo para la consulta pública** | 4 (la 2.ª consulta) |

- **Seguridad por filas y alcance intactos:** el rol y el alcance que rigen en la base son los mismos de antes; el espejo en memoria solo sustituye a la pregunta («¿hay alcance?») que el acceso hacía a la base, y el cierre en falla (sin actor o sin alcance → rechazo y alerta) se mantiene. Las políticas de filas siguen siendo la autoridad. Cubierto con las pruebas de existencias, alcance de datos y las nuevas: la elevación fusionada restaura rol y alcance (también si el bloque falla), y un actor sin alcance tras un encargado sobre **la misma conexión física** sigue rechazado.
- **Sin `DISCARD ALL`:** ver la conclusión 4 de la sección 2 y su invariante. Prueba: tras una transacción, la conexión reutilizada tiene el rol de la aplicación, ningún alcance, `lock_timeout` 0 y escritura habilitada. Hallazgo de la revisión de seguridad, corregido: los bloqueos asesores de sesión (instancia única de trabajos y depuración) ahora cierran la conexión física si no se pueden soltar, porque ya nada los liberaría al devolverla.
- **La memoria de referencias queda apagada por omisión** (`Inventario:ReferenciasEnMemoriaSegundos`, de 1 a 30; 0 la apaga). Motivo: la decisión D05 de `inventario-fuentes` dice que no hay caché de disponibilidad en el Inventario, y cambiarla es de la persona responsable (AGENTS.md §2). Solo guarda tablas sin seguridad por filas, con la proyección mínima (id, código, nombre; formato), nunca stock, reservado ni vencidas, y nunca la usan los casos que deciden (reservar, traslados, ajustes). No guarda ausencias y se vacía a las 10 000 entradas. Desfase máximo: el plazo configurado, y solo en nombres de fuente y formato mostrados.

**Rampa antes y después** (`tests/load/escenarios/rampa.js`, pausa de 0,5 s; resultados en `progress/evidencias/k6/2026-11-20-costo-despues/`; la «antes» es la de la sección 6b, mismo código de carga, **una corrida por fila, en una máquina compartida**: las diferencias de ±20 % entre corridas son ruido):

| Inventario / PostgreSQL (CPU) | Sentencias por solicitud en la base (`pg_stat_statements`) | p95 con 50 / 100 / 250 usuarios | Iteraciones en la rampa |
| :--- | :--- | :--- | :--- |
| 0,3 / 0,8 — antes | ≈ 31 | 897 ms / 1,5 s / 5,1 s | 23 013 |
| 0,3 / 0,8 — después | ≈ 18 | 1 286 ms / 1,9 s / 4,1 s | 19 635 |
| 0,3 / 0,8 — después, con memoria de 5 s | ≈ 12 | 1 296 ms / 2,3 s / 4,4 s | 24 206 |
| 1 / 1 — antes | ≈ 31 | 242 ms / 679 ms / 1,4 s | 44 195 |
| 1 / 1 — después | ≈ 18 | 113 ms / 614 ms / 2,0 s | 40 520 |

**Lo que se mide, sin maquillar:** el conteo de sentencias bajó un 40 % (y un 60 % con la memoria), pero **el rendimiento medido con k6 no mejoró de forma consistente**: con 1 / 1 el p95 con 50 usuarios bajó de 242 a 113 ms (RNF02 se sigue cumpliendo con margen), pero con 250 usuarios y en las iteraciones totales las corridas «después» no superan a las «antes» (dentro del ruido de la máquina). Con 0,3 CPU para el Inventario **RNF02 sigue sin cumplirse** (≈ 1,3 s con 50 usuarios, con o sin memoria): ahí el cuello es la CPU del propio servicio (autenticación, serialización, 3 a 4 transacciones por solicitud y el trabajo de Npgsql por sentencia), no el conteo de sentencias, y una sentencia fusionada cuesta lo mismo en el servidor y en Npgsql que la misma sentencia suelta: solo se ahorra la espera de la ida. Lo conseguido es **menos viajes de red y menos trabajo de la base por solicitud** (útil en la VM real, con la red entre contenedores), no una capacidad medible mayor en esta máquina. Las palancas que quedan (hallazgo Importante abierto): un comando por lote que fusione el cambio de rol con la primera sentencia del bloque, menos bloques de rol por consulta (hoy tres), contadores de límites fuera de PostgreSQL y, sobre todo, la CPU declarada del Inventario (0,6 como mínimo, sección 6b).

## 7. Qué cubre y qué no

- Cubre el Inventario (el servicio que concentra la concurrencia de stock) con datos de carga, en una máquina de desarrollo con los límites declarados de contenedor.
- **No** cubre el E-commerce (carrito, pedido, pasarela) ni la VM-BORDE, ni la restauración con carga (PLA-R3), ni medidas en la VM real (despliegue fuera del Sprint 2).
- Las cifras de tiempo dependen de la máquina y de que esté en reposo; los umbrales, la ausencia de sobreventa y el comportamiento ante la caída son lo reproducible (`bash tests/load/ejecutar.sh <escenario>`, ver `tests/load/README.md`).
