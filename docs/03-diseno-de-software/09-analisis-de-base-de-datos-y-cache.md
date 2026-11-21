# 09 Análisis de la base de datos y de la caché (DOC-1)

Qué hace la base de datos en las consultas críticas del Inventario, cómo se protege la concurrencia y qué se cachea, **con las mediciones de la prueba de carga** (K6-1, `tests/load/`, resultados en `progress/evidencias/k6/2026-11-20/`). Complementa [05 Modelo de datos](05-modelo-de-datos.md), [06 Estados y concurrencia](06-estados-y-concurrencia.md) y `docs/02-arquitectura-de-software/07-presupuesto-de-recursos.md`.

## 1. Cómo se midió

- **Entorno:** el Inventario y PostgreSQL 17 en contenedores con límites de CPU y memoria (`tests/load/entorno/`): la VM-INVENTARIO de 1 OCPU repartida entre el Inventario (0,6) y PostgreSQL (0,4), con `shared_buffers=768MB`. Ese reparto (Inventario 0,6 CPU y 512 MB, PostgreSQL 0,4) es el declarado hoy en `infra/docker-compose.inventario.yml` por DD-17; antes de DD-17 el Compose declaraba Inventario 0,3 y PostgreSQL 0,8, y esa configuración se conserva en las tablas como histórica. Máquina de desarrollo x86, no la VM ARM: valen los órdenes de magnitud y los umbrales, no los milisegundos absolutos.
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

> **Primera serie de mediciones (histórica).** Estas cifras son de la mañana del 2026-11-20, antes de la reducción de idas a la base (§6c) y del dimensionamiento de DD-17. **Las cifras vigentes son las de la serie final sobre `main` (§6b-bis)**; aquí se conservan porque muestran el método y la dispersión entre corridas (con los mismos límites, la disponibilidad pasó de 327 ms a ≈ 900 ms entre la mañana y la noche).

| Configuración | Prueba | p95 | Resultado |
| :--- | :--- | :--- | :--- |
| Inventario 0,6 CPU y PostgreSQL 0,4 | 50 usuarios, pausa de 0,5 s (`disponibilidad`) | 327 ms | **Cumple RNF02** (< 500 ms) |
| Inventario 0,6 CPU y PostgreSQL 0,4 | 50 usuarios sin pausa (peor caso) | 899 ms | No cumple |
| Límites declarados antes de DD-17: Inventario 0,3 CPU y PostgreSQL 0,8 | 50 usuarios, pausa de 0,5 s | 1 774 ms | **No cumple** |
| Inventario 0,6 CPU y PostgreSQL 0,4 | Rampa (`rampa`): 50 / 100 / 250 / 500 / 1 000 / 2 000 usuarios | 434 ms / 1 095 ms / 7,2 s / 8,1 s / 16 s / 17 s | Cumple en 50; el techo está entre 50 y 100 usuarios; con 1 000 y 2 000 usuarios fallan el 19 % y el 52 % de las solicitudes (503 y plazos vencidos) |
| Inventario 0,6 CPU y PostgreSQL 0,4 | 5 compras por segundo, hasta 50 usuarios (`checkout-confirmacion`, reservar y confirmar) | 226 ms (umbral 3 s) | **Cumple** |

**Lectura:** con la CPU que la VM-INVENTARIO da hoy al Inventario según el Compose (0,3), RNF02 no se cumple en esta máquina; repartida la OCPU de otra forma sí. Palancas, de mayor a menor efecto: (1) subir la CPU del contenedor del Inventario; (2) reducir el costo por solicitud en el servicio (la autorización, los límites y la auditoría hacen su propia ida a la base por solicitud; agrupar o diferir los contadores de límites de las lecturas); (3) caché de disponibilidad delante del Inventario (sección 5). La rampa muestra además que el servicio **se degrada de forma controlada** (503 uniformes, sin 500 ni interbloqueos) más allá de su techo; los 2 000 usuarios virtuales no se sostienen con una sola VM de 1 OCPU, y la extrapolación está documentada aquí (`docs/07-proceso-de-desarrollo/06-plan-sprint-2.md` §8).

## 6b. Capacidad medida frente a recursos (rampa de 50 a 2000 usuarios virtuales) — primera serie, histórica

> Serie de la mañana del 2026-11-20 (código anterior a §6c, memoria del Inventario 350 MB). **La serie vigente, sobre `main` y con 512 MB, es §6b-bis**; en particular, el resultado de 2 000 usuarios de la mejor configuración (4 / 2 CPU) es **10,1 s sin errores (vigente)**, no los 7,2 s de la tabla siguiente (ver la nota de §6b-bis, punto 3).

Rampa de la consulta de disponibilidad (`tests/load/escenarios/rampa.js`, pausa de 0,5 s entre solicitudes, 40 s por nivel), con el Inventario y PostgreSQL en contenedores con los recursos de cada fila (memoria del Inventario 350 MB). **p95 por nivel de usuarios virtuales** (entre paréntesis, la tasa de errores cuando supera el 0,05 %; el criterio es p95 < 500 ms y menos de 1 % de errores):

| Inventario / PostgreSQL (CPU) / tope del grupo | 50 | 100 | 250 | 500 | 1 000 | 2 000 |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 0,3 / 0,8 / 16 (histórico: declarado antes de DD-17) | 897 ms | 1,5 s | 5,1 s | 8,3 s | 18,8 s (10 %) | 19,4 s (48 %) |
| 1 / 1 / 16 | 242 ms | 679 ms | 1,4 s | 5,2 s | 7,3 s | 13,3 s |
| 2 / 1 / 16 | 146 ms | 403 ms | 2,1 s | 7,6 s | 13,2 s | 15,2 s (27 %) |
| 2 / 1 / 40 | 308 ms | 981 ms | 3,3 s | 6,4 s | 13,6 s | 15,5 s (18 %) |
| 4 / 2 / 16 | 50 ms | 133 ms | 1,3 s | 2,4 s | 5,0 s | 7,2 s |
| 4 / 2 / 60 | 25 ms | 133 ms | 885 ms | 2,4 s | 5,7 s | 13,7 s |

(Referencia previa, 0,6 / 0,4 / 16, hoy el dimensionamiento **declarado** por DD-17: 434 ms con 50 usuarios y 1,1 s con 100; la serie final sobre `main` con ese dimensionamiento va en `progress/evidencias/k6/2026-11-20-final/`.) Resultados completos en `progress/evidencias/k6/2026-11-20-capacidad/`.

**Lo que se mide, sin maquillar**

1. **Ninguna configuración sostiene 2 000 usuarios virtuales con p95 < 500 ms**; la mejor (4 / 2 CPU) dio en esta primera serie p95 de 7,2 s y sin errores (en la serie final vigente, 10,1 s; §6b-bis), y con los límites declarados (0,3 CPU) ni siquiera 50 usuarios cumplen (897 ms) y con 2 000 falla casi la mitad de las solicitudes. Los usuarios que se sostienen con p95 < 500 ms son: 0 con 0,3 CPU, 50 con 1 / 1, 100 con 2 / 1 y 100 con 4 / 2 (con 250: 885 ms a 1,3 s).
2. **El cuello de botella medido es la CPU de PostgreSQL, junto con la del servicio.** Muestreo de `docker stats` en el nivel de 1 000 usuarios con 4 / 2 CPU: PostgreSQL queda **fijo en ~200 % (su tope de 2 CPU)**, el Inventario usa 130 a 300 % de sus 400 % y el generador de carga, 40 a 75 % de una CPU. Duplicar la CPU de PostgreSQL de 1 a 2 junto con la del Inventario baja el p95 de 1 000 usuarios de 13 s a 5 s. Esto **corrige** la conclusión 2 de la sección 2: el tiempo de ejecución de cada sentencia es pequeño, pero cada solicitud cuesta unas **13 idas a la base** (cambio de rol, comprobación del usuario y del alcance, reinicio de sesión y 3 lecturas), y esas idas, no las lecturas, consumen la CPU de PostgreSQL.
3. **Subir el tope del grupo de conexiones no ayuda** (2 / 1: 308 ms con tope 40 frente a 146 ms con 16 en 50 usuarios; 4 / 2: sin mejora en 1 000 y peor en 2 000): con la CPU de PostgreSQL saturada, más conexiones solo aumentan la contención. El tope de 16 queda.
4. **Rendimiento sostenido estimado** (ley de Little con la mediana: usuarios / (mediana + pausa)): con 0,3 / 0,8 ≈ 65 a 85 solicitudes por segundo; con 1 / 1 ≈ 100 a 170; con 4 / 2 ≈ 280 a 300. Cada solicitud cuesta del orden de 8 ms de CPU de PostgreSQL y 6 a 12 ms de CPU del servicio.
5. **Memoria:** el Inventario llegó a 263 MiB de los 350 MiB con 1 000 usuarios (75 %). El presupuesto de 350 MB queda justo para esa carga; se propone 512 MB si se espera ese nivel.
6. Con los recursos declarados entonces (0,3 CPU), el sistema **se degrada de forma controlada** (503 y plazos vencidos, sin 500 ni sobreventa), pero degrada.

**Dimensionamiento propuesto (lo que no cumple)**

- **RNF02 (50 usuarios, p95 < 500 ms):** se midió que cumple con el reparto 0,6 / 0,4 en la primera serie (434 ms) y con 1 / 1 (242 ms), pero **la serie final de la sección 6b-bis, con `main` y el dimensionamiento declarado (DD-17), no lo reprodujo (≈ 900 ms con 0,6 / 0,4)**; con los 0,3 CPU anteriores tampoco se cumplía. La OCPU de la VM es la restricción: ver 6b-bis para el techo medido y lo que falta.
- **2 000 usuarios virtuales con pausa de 0,5 s equivalen a ~4 000 solicitudes por segundo**: con el costo actual por solicitud necesitarían del orden de 30 CPU para PostgreSQL y otras tantas para el servicio; **no es un objetivo alcanzable en una VM de 1 OCPU**. Con una pausa realista de 5 a 10 s entre acciones de una persona, 2 000 usuarios son 200 a 400 solicitudes por segundo, que es **lo que sostiene la configuración de 4 / 2 CPU** (p95 degradado, sin errores). Para 2 000 usuarios reales en la VM de 1 OCPU de hoy hace falta, antes que más CPU, **reducir el costo por solicitud**.
- **Palancas de ingeniería (candidatas, no hechas; Importantes en `progress/hallazgos.md`):** (a) fusionar el cambio de rol y la primera sentencia de cada transacción en un solo viaje y quitar las comprobaciones de `current_user` y de `current_setting` por sentencia; (b) evitar el reinicio de sesión (`DISCARD ALL`) en cada devolución al grupo, compatible porque el rol es local a la transacción; (c) caché de disponibilidad delante del Inventario (sección 5), que quita solicitudes enteras; (d) los contadores de límites por origen en memoria o en Redis en lugar de insertar y leer en PostgreSQL en cada llamada.


## 6b-bis. Serie final sobre `main` con el dimensionamiento declarado (DD-17)

Corrida final del 2026-11-20 (16:02 a 17:23), con `main` ya con la reducción de idas a la base (§6c), un solo cliente k6 por paso, memoria de referencias apagada, tope del grupo 16. Resultados en `progress/evidencias/k6/2026-11-20-final/` (rampa por configuración), `…-final-escenarios/` (los cinco escenarios con 0,6 / 0,4 / 512 MB), `…-final-escenarios-caliente/` (disponibilidad repetida con el servicio ya caliente) y `…-final-reparto-*/` (disponibilidad con otros repartos de CPU). **p95 por nivel de usuarios virtuales de la rampa** (entre paréntesis, tasa de errores si supera el 0,05 %):

| Inventario / PostgreSQL (CPU) / memoria | 50 | 100 | 250 | 500 | 1 000 | 2 000 |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 0,3 / 0,8 / 350 MB (histórico, antes de DD-17) | 1,4 s | 1,6 s | 5,3 s | 8,6 s | 14,6 s | 17,2 s (82 %) |
| **0,6 / 0,4 / 512 MB (declarado, DD-17)** | **1,4 s** | **1,9 s** | **4,3 s** | **7,4 s** | **16,4 s (11,5 %)** | **16,9 s (51 %)** |
| 1 / 1 / 512 MB | 425 ms | 603 ms | 2,1 s | 4,1 s | 8,1 s (33 %) | 14,7 s (0,2 %) |
| 2 / 1 / 512 MB | 66 ms | 603 ms | 2,3 s | 4,4 s | 8,9 s | 15,2 s (15 %) |
| 4 / 2 / 512 MB | 35 ms | 47 ms | 937 ms | 2,2 s | 5,0 s | 10,1 s |

**Disponibilidad con 50 usuarios y pausa de 0,5 s, 60 s (RNF02; `disponibilidad`)**

| Inventario / PostgreSQL (CPU) | p95 | Mediana | Iteraciones | RNF02 (p95 < 500 ms) |
| :--- | :--- | :--- | :--- | :--- |
| **0,6 / 0,4 (declarado), primera corrida** | **908 ms** | 597 ms | 2 727 | **No cumple** |
| **0,6 / 0,4 (declarado), segunda corrida, servicio caliente** | **897 ms** | 598 ms | 2 723 | **No cumple** |
| 0,4 / 0,6 | 1 189 ms | 411 ms | 3 011 | No cumple |
| 0,3 / 0,8 | 2 610 ms | 1 902 ms | 1 227 | No cumple |

Los demás escenarios con 0,6 / 0,4 / 512 MB **cumplen**: `checkout-confirmacion` (reservar y confirmar, p95 108 ms frente a 3 s), `sobreventa` (exactamente 10 reservas y 90 rechazos, sin sobreventa), `limites` (100 permitidas y el resto 429; el vecino sin afectar) y `resiliencia` (PostgreSQL pausado 25 s: 503 uniforme, ningún 500, recuperación sola).

**Lo que se mide, sin maquillar**

1. **Con el dimensionamiento declarado (0,6 / 0,4) RNF02 no se cumple en esta corrida**, ni en la rampa (1,4 s a 50 usuarios) ni en estado estable (≈ 900 ms, dos corridas, también con el servicio caliente: no es el arranque en frío). La conclusión de la sección 6b («434 ms con 0,6 / 0,4») y la medición de la sección 6 (327 ms) **no se reprodujeron** en esta serie.
2. **La razón que sí se ve en los datos:** 50 usuarios con 0,5 s de pausa piden del orden de 80 solicitudes por segundo y el entorno atendió 44 (2 727 en 62 s). El rendimiento sostenible lo fija la cuota de CPU: con 0,3 / 0,4 / 0,6 CPU para el Inventario el p95 fue 2,6 s, 1,2 s y 0,9 s. Es la misma saturación de la sección 6b (cada solicitud cuesta unos 6 a 12 ms de CPU del servicio y unos 8 ms de PostgreSQL): el costo por solicitud no baja lo suficiente para que 0,6 CPU alcance a 80 solicitudes por segundo.
3. **Las cifras absolutas de esta serie son peores que las de la mañana con los mismos límites** (con 0,3 CPU, 1 227 iteraciones frente a 4 501 en la corrida oficial; con 1 / 1 en la rampa, 425 ms frente a 242 ms), mientras que con 2 / 1 y 4 / 2 mejoraron (66 y 35 ms frente a 146 y 50 ms). Hay **ruido del host**: es una máquina de desarrollo compartida (en la corrida seguían activos contenedores de otros carriles y un reproductor de medios; no se registró la carga del procesador) y la reducción de idas a la base de §6c entró entre las dos mediciones, de modo que **no se puede atribuir la diferencia a una sola causa**. Lo reproducible es el orden: más CPU para el Inventario, menor p95, y ninguna configuración sostiene 2 000 usuarios con p95 < 500 ms (la mejor, 4 / 2, da **10,1 s sin errores: es la cifra vigente**; los 7,2 s de la misma configuración en la primera serie, §6b, se midieron con el código anterior a §6c, 350 MB de memoria y la máquina más descargada, y no se reprodujeron: cuando dos series difieren, vale la última sobre `main`, y la diferencia entra en el ruido de una máquina compartida que ya se vio en la disponibilidad).
4. **Techo medido con el dimensionamiento declarado:** con 50 usuarios, p95 de 0,9 s (en la rampa, 1,4 s); con 2 000 usuarios, p95 de 16,9 s y el 51 % de las solicitudes fallan (503 y plazos vencidos; sin 500 ni sobreventa). Con 1 / 1 se cumple RNF02 en la rampa a 50 usuarios (425 ms) pero ya no a 100 (603 ms).
5. **Qué haría falta para cumplir RNF02 con una OCPU:** que cada solicitud cueste menos CPU (hallazgos Importantes abiertos en `progress/hallazgos.md`: fusionar el cambio de rol, mover los contadores de límites fuera de PostgreSQL, caché de disponibilidad por decisión de la persona) o aceptar que la prueba de 50 usuarios con 0,5 s de pausa (≈ 80 solicitudes por segundo) es más exigente que 50 personas reales (que piden cada 5 a 10 s: 5 a 10 solicitudes por segundo); esa lectura **no se midió** en esta serie y no se afirma que se cumpla.

**Qué recursos hacen falta para 2 000 sesiones de personas (perfil realista, no el de estrés de arriba; detalle y reservas en §6d-bis):**

| Inventario + PostgreSQL (CPU) | ¿Cumple a 2 000 sesiones (disponibilidad p95 < 500 ms, reserva + confirmación p95 < 3 s, errores < 1 %)? |
| :--- | :--- |
| 0,6 + 0,4 (declarada, DD-17; no cambia) | **No** (1,6 s y 4,4 s) |
| 1,0 + 0,5 | **No** (1,5 s y 5,6 s) |
| 1,5 + 0,75 | **No** (560 ms y 1,2 s; falla por 60 ms) |
| **2 + 1 (dimensionamiento recomendado para 2 000)** | **Sí** (201 y 313 ms de disponibilidad en dos corridas; curva de 1 000 a 2 000 sesiones también) |

Solo se midió el Inventario; la ruta del E-commerce no se midió.

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

## 6d. Perfil realista de 2 000 sesiones (VM de 1 OCPU, dimensionamiento de DD-17)

Las secciones 6b y 6b-bis usan un **perfil de estrés** (pausa de 0,5 s entre solicitudes, 2 000 usuarios ≈ 4 000 solicitudes por segundo): es evidencia del **límite** y de la degradación controlada, y se conserva sin reinterpretar. Esta sección mide lo que pidió la persona el 2026-11-21: **2 000 sesiones concurrentes de personas** sobre el dimensionamiento fijo de producción (Inventario 0,6 CPU y 512 MB, PostgreSQL 0,4 CPU; sin cambiar CPU para «aprobar»).

**Perfil y su justificación** (`tests/load/escenarios/perfil-realista.js`; docs de calidad del Sprint 1: AC02 y ESC-AC02-01 «decenas de clientes consultan la disponibilidad y agregan libros al carrito durante una feria», RNF02 «p95 < 500 ms con hasta 50 usuarios concurrentes; checkout p95 < 3 s»; la rúbrica pide 2 000 usuarios virtuales). Cada usuario virtual es **una sesión de un lector** de una librería independiente con varias sucursales: hace una acción, piensa de 5 a 30 s (media de 15 s) y repite.

| Acción | Parte | Qué llega al Inventario | Solicitudes por acción |
| :--- | :--- | :--- | :--- |
| Catálogo y búsqueda | 75 % | Nada (la atiende el E-commerce con su propio catálogo) | 0 |
| Detalle de una edición con disponibilidad | 18 % | 1 consulta de disponibilidad | 1 |
| Carrito | 5 % | 1 consulta de disponibilidad de la línea (cota superior de AC02; la validación fuerte ocurre al iniciar el checkout, CAR-05) | 1 |
| Checkout con reserva | 2 % | consulta de disponibilidad + reservar + confirmar tras el pago (la pasarela no entra y se simula con 1 a 3 s) | 3 |

**Cifras** con 2 000 sesiones: acciones por segundo = 2 000 / 15 s ≈ **133**; solicitudes al Inventario por acción = 0,18 + 0,05 + 0,02 × 3 = **0,29**, es decir ≈ **39 por segundo** (33 de disponibilidad y 2,7 reservas y 2,7 confirmaciones). Cumple lo propuesto (≈ 130 acciones/s y 35 a 40 solicitudes/s). **Medido** en la corrida: mezcla de 75,0 % / 18,0 % / 5,1 % / 1,9 % (40 412, 9 709, 2 734 y 1 030 acciones) y 0,27 solicitudes por acción (≈ 36 por segundo en la meseta de 2 000 sesiones, con los fallos incluidos); la pausa media resultante fue de ≈ 15 s (sesiones-segundo entre acciones).

**Niveles:** 100, 250, 500, 1 000, 1 500 y 2 000 sesiones; cada uno sube en 30 s y se sostiene 100 s (el de 2 000, 180 s); el último baja en 30 s. Umbrales de RNF02, sin cambiarlos, **por nivel**: p95 < 500 ms en la consulta de disponibilidad, p95 < 3 s en reservar + confirmar y menos de 1 % de solicitudes con error.

**Alcance:** se mide el **Inventario** con el tráfico que el E-commerce le enviaría. El 75 % de catálogo y búsqueda, el carrito y el pedido del E-commerce **no se ejercitaron** (el E-commerce no corre en este entorno y, en producción, vive en otra VM: VM-VENTAS; `docs/02/05` y `07`), de modo que **no se midió la latencia de extremo a extremo ni la capacidad de VM-VENTAS**; la capacidad de VM-INVENTARIO sí.

**Resultado** (corrida completa, `progress/evidencias/k6/2026-11-21-perfil-realista/`, con panel HTML; y repetición de los tres niveles altos, `…-perfil-realista-repeticion/`). Disponibilidad p95 / reservar + confirmar p95 / errores:

| Sesiones concurrentes | Solicitudes/s al Inventario (≈) | Corrida completa | Repetición | ¿Sostenible con p95 < 500 ms? |
| :--- | :--- | :--- | :--- | :--- |
| 100 | 1,9 | 82 ms / 190 ms / 0 % | — | **Sí** |
| 250 | 4,8 | 114 ms / 198 ms / 0 % | — | **Sí** |
| 500 | 9,7 | 29 ms / 92 ms / 0 % | — | **Sí** |
| 1 000 | 19 | 23 ms / 83 ms / 0,72 % | 136 ms / 581 ms / 0 % | **Sí** (las dos corridas) |
| 1 500 | 29 | **3,3 s** / **11,2 s** / 0,37 % (no cumple) | 375 ms / 703 ms / 0,24 % (cumple) | **En el límite**: cumplió en una corrida y falló en la otra |
| 2 000 | 39 | **1,2 s** / 2,9 s / **6,4 %** (no cumple) | **13,9 s** / **42,3 s** / 0,29 % (no cumple) | **No** |

**Lo que se mide, sin maquillar**

1. **Con el dimensionamiento de DD-17 y el perfil realista, el sistema sostiene 1 000 sesiones concurrentes con p95 < 500 ms** (en las dos corridas: 23 y 136 ms, 0 y 0,72 % de errores); **1 500 está en el límite** (cumple en una corrida, falla en la otra, con latencias de varios segundos) y **2 000 no se sostiene**: p95 de 1,2 s con 6,4 % de errores en una corrida y de 13,9 s en la otra. Con la mayor carga la latencia ya no es estable: **dos corridas con la misma configuración dan resultados muy distintos**, y una sola no basta para fijar el punto exacto.
2. **El orden de magnitud lo fija la CPU de PostgreSQL**, igual que en 6b: a ≈ 39 solicitudes por segundo el entorno ya está en saturación. Cuando se satura la latencia no crece de forma gradual: en la repetición a 2 000 sesiones el checkout llegó a un p95 de 42 s.
3. **Los errores son tiempos de conexión agotados** (`dial: i/o timeout`), no 500 ni respuestas de negocio, y llegan en ráfagas cortas; no hubo sobreventa ni 500.
4. **La curva «sesiones sostenibles con p95 < 500 ms en VM-INVENTARIO de 1 OCPU» es ≈ 1 000 (hasta 1 500 en la mejor corrida)**, es decir, ≈ 20 a 30 solicitudes por segundo al Inventario. Para sostener 2 000 sesiones (≈ 39 solicitudes por segundo) **hace falta más capacidad que la declarada**: §6d-bis la midió con el mismo perfil y fija la configuración mínima probada que lo cumple (Inventario 2 CPU y PostgreSQL 1 CPU).
5. **Límites de esta medición:** una sola corrida completa y una repetición parcial; máquina de desarrollo compartida; solo el Inventario; la pasarela simulada; el tamaño de los datos es el de una prueba (20 ediciones).
6. **Corrección de validez (2026-11-21, al preparar §6d-bis):** las credenciales de cliente que emite el servidor de claves de carga duran 14 minutos (el Inventario rechaza vigencias de más de 15, `CredencialDeAcceso.VigenciaDeLaCredencialEnMinutos`) y **la corrida completa duró 17,9 minutos**: los niveles de 1 500 (desde el minuto ≈ 11,5) y sobre todo el de 2 000 (desde el minuto ≈ 14) hicieron sus reservas con credenciales vencidas, de modo que parte de los errores y de los tiempos de ese tramo (6,4 % de errores a 2 000) **no son de capacidad**. La repetición (tres niveles, ≈ 11 minutos) no tuvo ese problema, y su resultado (2 000 sesiones sin sostenerse) se confirmó de nuevo en §6d-bis con credenciales vigentes (1,6 s de p95 de disponibilidad y 4,4 s de reserva + confirmación con 0,6/0,4 CPU). Por eso la cifra de la corrida completa a 1 500 y 2 000 sesiones debe leerse con esta reserva; las corridas de §6d-bis duran menos de 14 minutos.

## 6d-bis. Qué recursos hacen falta para sostener 2 000 sesiones (perfil realista)

Pedido de la persona (2026-11-21): ejecutar el **mismo perfil realista** de §6d a **2 000 sesiones concurrentes** con el dimensionamiento que **sí** las sostenga, y dejar la configuración **mínima** que cumple. La configuración declarada de la VM de 1 OCPU (Inventario 0,6 CPU, PostgreSQL 0,4 CPU, 512 MB; DD-17) **no cambia**: queda como la configuración declarada; la de abajo es el dimensionamiento **recomendado para 2 000 sesiones**. Solo se mide el **Inventario**: **la ruta del E-commerce (catálogo, búsqueda, carrito, pedido) no se midió**.

**Método.** Una corrida por configuración, solo el nivel de 2 000 sesiones (sube en 30 s, se sostiene 240 s, baja en 30 s; ≈ 5 minutos, dentro de la vigencia de las credenciales), del menor al mayor tamaño sobre el mismo perfil (`NIVELES=2000 SEGUNDOS_FINAL=240`), memoria del Inventario fija en 512 MB, tope de 16 conexiones. Umbrales de RNF02 sin cambios: disponibilidad p95 < 500 ms, reservar + confirmar p95 < 3 s y menos de 1 % de solicitudes con error. Evidencias (panel HTML, JSON y resumen por corrida) en `progress/evidencias/k6/2026-11-21-perfil-realista-2000/`. El host de medición tiene 12 CPU lógicas y 16 GB; k6 corre en el mismo host, y la máquina es de desarrollo compartida.

| Inventario + PostgreSQL (CPU) | Disponibilidad p95 | Reservar + confirmar p95 | Errores | Veredicto a 2 000 sesiones |
| :--- | :--- | :--- | :--- | :--- |
| **0,6 + 0,4** (DD-17, declarada) | 1 576 ms | 4 401 ms | 0,01 % | **No** (falla los dos tiempos) |
| 1,0 + 0,5 | 1 481 ms | 5 600 ms | 0,02 % | **No** |
| 1,5 + 0,75 | 560 ms | 1 169 ms | 0,11 % | **No** (disponibilidad por 60 ms) |
| **2,0 + 1,0** | **201 ms** | 286 ms | 0,07 % | **Sí** |
| 2,0 + 1,0 (repetición) | **313 ms** | 710 ms | 0,04 % | **Sí** |
| 2,0 + 1,0 (curva: 1 000 / 1 500 / 2 000 sesiones, una sola corrida de 10,6 min) | 56 / 366 / 147 ms | 141 / 737 / 346 ms | 0,13 / 0,30 / 0,15 % | **Sí** en los tres niveles |
| 4 + 2 | no se midió | | | La búsqueda se detuvo en la primera configuración que cumple |

**Lo que se mide, sin maquillar**

1. **La configuración mínima probada que sostiene 2 000 sesiones con los umbrales de RNF02 es Inventario 2 CPU + PostgreSQL 1 CPU (3 CPU en total, ≈ 3 veces el dimensionamiento declarado de 1 OCPU compartida)**: cumplió en las tres corridas que la incluyen (la búsqueda, su repetición y la curva). No es una medida fina: entre 1,5 + 0,75 (560 ms, por 60 ms) y 2 + 1 no se probaron valores intermedios (por ejemplo 1,5 + 1,0), así que «mínima» significa la menor de la escalera probada.
2. **Con el dimensionamiento declarado (0,6 + 0,4) las 2 000 sesiones no se sostienen** (disponibilidad p95 de 1,6 s y reserva + confirmación de 4,4 s), con el mismo método y el mismo día; coincide con §6d. La diferencia de cifra con la repetición de §6d (13,9 s) se debe a que aquella corrida llegó a 2 000 sesiones tras otros dos niveles ya saturados (acumulaba cola); aquí cada corrida parte en frío. Ambas lecturas dicen lo mismo: no se sostienen.
3. **El margen de 2 + 1 no es grande en 1 500 sesiones** (disponibilidad p95 de 366 ms en la curva, por debajo de 500 pero sin holgura) y la repetición a 2 000 subió de 201 a 313 ms: es el ruido normal de una máquina compartida. La recomendación para 2 000 sesiones sostenidas es 2 + 1 como **mínimo**, no como valor con holgura amplia.
4. **El cuello de botella es la CPU del Inventario y de PostgreSQL**, igual que en 6b: los errores de las configuraciones pequeñas son tiempos de conexión agotados y colas, no 500 ni sobreventa; con 2 + 1 los errores quedan en 0,04 a 0,30 %.
5. **Qué significa para la VM real.** Una VM de 1 OCPU compartida no alcanza: para 2 000 sesiones concurrentes (≈ 39 solicitudes por segundo al Inventario) se necesita del orden de 3 CPU para el Inventario y su base, es decir, otra forma de despliegue (más OCPU, o separar PostgreSQL de la VM del Inventario). Esa decisión de despliegue es de la persona responsable; aquí solo se registra qué recursos hacen falta según la medición.
6. **Límites:** solo el Inventario; la pasarela simulada; 20 ediciones de prueba; una corrida por configuración salvo 2 + 1 (tres); host de desarrollo compartido con k6 en la misma máquina; corridas de un solo nivel (partida en frío) además de la curva; la latencia de extremo a extremo y la capacidad de VM-VENTAS **no se midieron**.

## 7. Qué cubre y qué no

- Cubre el Inventario (el servicio que concentra la concurrencia de stock) con datos de carga, en una máquina de desarrollo con los límites declarados de contenedor.
- **No** cubre el E-commerce (carrito, pedido, pasarela) ni la VM-BORDE, ni la restauración con carga (PLA-R3), ni medidas en la VM real (despliegue fuera del Sprint 2).
- Las cifras de tiempo dependen de la máquina y de que esté en reposo; los umbrales, la ausencia de sobreventa y el comportamiento ante la caída son lo reproducible (`bash tests/load/ejecutar.sh <escenario>`, ver `tests/load/README.md`).
