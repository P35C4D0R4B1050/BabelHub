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
2. **La base no es el cuello de botella de la consulta de disponibilidad:** son ~5 sentencias de décimas de milisegundo (~1 ms de base por solicitud) y el acierto de caché del búfer es del 99,99 %. El costo está en el servicio .NET (autenticación de la credencial, límites, auditoría y serialización): unos 7 ms de CPU por solicitud, que explican el techo medido (sección 6).
3. **Costo fijo por transacción de la seguridad por filas** (sección 4): `SET LOCAL ROLE`, `current_user` y `current_setting` suman 3 sentencias de ~0,01 ms; despreciable en tiempo, visibles en el conteo de sentencias (18 000 `SET LOCAL ROLE` en 4 500 solicitudes).
4. Cada conexión devuelta al grupo ejecuta `DISCARD ALL` (reinicio de sesión de Npgsql, ~0,05 ms), necesario para que el rol y la configuración de una transacción no se filtren a la siguiente.

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

**Con y sin caché:** el plan pedía comparar el p95 con y sin caché de disponibilidad. Como esa caché no existe en el código, **no hay medición con caché**; la línea base sin caché es la de la sección 6. Dado que la base no es el cuello de botella (de ~7 ms de CPU por solicitud, ~1 ms es la base y el resto el servicio), una caché de disponibilidad en el E-commerce evitaría la llamada entre servicios, no la consulta a PostgreSQL: su valor sería quitar carga al **Inventario** (la VM de 1 OCPU), no a la base. Decidirla es de la persona responsable (alcance del Sprint 2).

## 6. Techo medido de la consulta de disponibilidad

| Configuración | Prueba | p95 | Resultado |
| :--- | :--- | :--- | :--- |
| Inventario 0,6 CPU y PostgreSQL 0,4 | 50 usuarios, pausa de 0,5 s (`disponibilidad`) | 327 ms | **Cumple RNF02** (< 500 ms) |
| Inventario 0,6 CPU y PostgreSQL 0,4 | 50 usuarios sin pausa (peor caso) | 899 ms | No cumple |
| Límites declarados: Inventario 0,3 CPU y PostgreSQL 0,8 | 50 usuarios, pausa de 0,5 s | 1 774 ms | **No cumple** |
| Inventario 0,6 CPU y PostgreSQL 0,4 | Rampa (`rampa`): 50 / 100 / 250 / 500 / 1 000 / 2 000 usuarios | 434 ms / 1 095 ms / 7,2 s / 8,1 s / 16 s / 17 s | Cumple en 50; el techo está entre 50 y 100 usuarios; con 1 000 y 2 000 usuarios fallan el 19 % y el 52 % de las solicitudes (503 y plazos vencidos) |
| Inventario 0,6 CPU y PostgreSQL 0,4 | 5 compras por segundo, hasta 50 usuarios (`checkout-confirmacion`, reservar y confirmar) | 226 ms (umbral 3 s) | **Cumple** |

**Lectura:** con la CPU que la VM-INVENTARIO da hoy al Inventario según el Compose (0,3), RNF02 no se cumple en esta máquina; repartida la OCPU de otra forma sí. Palancas, de mayor a menor efecto: (1) subir la CPU del contenedor del Inventario; (2) reducir el costo por solicitud en el servicio (la autorización, los límites y la auditoría hacen su propia ida a la base por solicitud; agrupar o diferir los contadores de límites de las lecturas); (3) caché de disponibilidad delante del Inventario (sección 5). La rampa muestra además que el servicio **se degrada de forma controlada** (503 uniformes, sin 500 ni interbloqueos) más allá de su techo; los 2 000 usuarios virtuales no se sostienen con una sola VM de 1 OCPU, y la extrapolación está documentada aquí (`docs/07-proceso-de-desarrollo/06-plan-sprint-2.md` §8).

## 7. Qué cubre y qué no

- Cubre el Inventario (el servicio que concentra la concurrencia de stock) con datos de carga, en una máquina de desarrollo con los límites declarados de contenedor.
- **No** cubre el E-commerce (carrito, pedido, pasarela) ni la VM-BORDE, ni la restauración con carga (PLA-R3), ni medidas en la VM real (despliegue fuera del Sprint 2).
- Las cifras de tiempo dependen de la máquina y de que esté en reposo; los umbrales, la ausencia de sobreventa y el comportamiento ante la caída son lo reproducible (`bash tests/load/ejecutar.sh <escenario>`, ver `tests/load/README.md`).
