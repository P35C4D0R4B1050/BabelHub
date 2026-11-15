# Ajustes al implementar los planes de la fase 3

> **LEE ESTO JUNTO CON EL PLAN.** Este documento **corrige y complementa** los planes aprobados el 2026-11-13 (`transversal-seguridad`, `transversal-plataforma`, `transversal-auditoria`, `transversal-notificaciones` e `identidad-seguridad`). Los planes **no se pueden editar** sin perder su aprobación (la huella de `plan.md`, `tasks.md` y `decisions.md` está en `progress/aprobaciones.md`), así que las erratas, las tareas que deben partirse y las pruebas que faltan viven aquí.
>
> **Regla:** antes de empezar una tarea, lee su `plan.md`, su `tasks.md`, su `decisions.md` **y la sección de su plan en este documento**. Si algo de aquí contradice el texto del plan, **vale lo de aquí** (salvo que contradiga `AGENTS.md`, `docs/` o una decisión de la persona en `progress/decisiones-previas-al-plan.md`: ahí te detienes y consultas). Al terminar un ajuste, márcalo `[x]` y anota el commit. Nada de esto cambia el comportamiento aprobado: son correcciones de redacción, divisiones de tareas, pruebas y precisiones.

## 0. Antes de implementar nada

- [x] **Resuelto el 2026-11-13 (autorizado por la persona responsable):** la huella del plan ignora solo el estado de las casillas de `tasks.md` (`- [ ]` frente a `- [x]`; las huellas ya registradas no cambian) y cualquier otro cambio de `plan.md`, `tasks.md` o `decisions.md` sigue invalidando la aprobación. Por eso: marca las casillas libremente, **no edites el texto de las tareas ni `decisions.md`**; las decisiones de implementación van en `specs/<feature>/decisiones-de-implementacion.md` y las divisiones de tareas, en este archivo.
- [ ] Las decisiones del 2026-11-13 de `progress/decisiones-previas-al-plan.md` (módulos propios, tope de NOT-14 solo para clientes con sucursales exentas, TRA-22 por correo, retención de avisos y huellas) y D26 de seguridad (la ventana de intentos **no** se vacía al crear `blq:`) rigen por encima de cualquier frase que las contradiga.
- [ ] Orden de implementación entre planes: `transversal-seguridad` T04 y T14 → `transversal-plataforma` → `transversal-auditoria` (T07, T08, T13 primero) → `identidad-seguridad` (T11 y T12 antes de T13) → `transversal-notificaciones`. Los dobles de prueba cubren lo que aún no existe (ver cada encabezado de `tasks.md`).
- [ ] Nombres de adaptadores: usa `<Tecnología><Puerto>` (AGENTS.md §4): `SmtpEnviadorDeCorreo`, `Argon2idHasher`, `RedisMotorDeLimites`, `N8nEnvioDeWhatsApp`, etc., aunque el plan escriba el nombre al revés.

## 1. `transversal-seguridad`

Erratas y textos:
- [ ] Cabecera de `tasks.md` (línea 7): reubicar el paréntesis «(el E-commerce con Redis y el Inventario con PostgreSQL y memoria local)» junto a «salvo el motor de Redis…»; `plan.md` línea 26: separar los dos paréntesis seguidos de `respaldoLocal`; línea 40: leer «SEL-14 y SEL-19» (SEL-13 no se cuenta).
- [ ] D22 se lee «(Refinada por D27)»; D16, «(Refinada por D25 y D27)».

Tareas y pruebas a añadir o precisar:
- [ ] **T04 y T14:** `EjecutarComo(esquema)` recibe un tipo cerrado `EsquemaDeModulo` que solo construye la Infraestructura de cada módulo, exige una transacción activa y restaura el rol con `SET LOCAL ROLE <previo>` (no `RESET ROLE`). Pruebas: cada módulo llama solo con su esquema; el rol de acceso (`NOINHERIT`) no es miembro de los roles de depuración ni de los dueños de tablas; sin `EjecutarComo` el acceso a `identidad.*` falla.
- [x] **T04 se parte (2026-11-13):** T04a = contrato público de `Plataforma` (límites, origen, huella, reloj, actor, unidad de trabajo, alcance y los puertos de `Auditoria`) con el puerto interno `IMotorDeLimites`; T04b = `ICasoDeUso`, `Resultado`, `ErrorDeAplicacion` y el decorador `ValidacionDeEntrada` con sus validadores (AC-SEG-01). T04 se marca al terminar T04b.
- [x] **Tipos que van en la Aplicación y no en el Dominio (corrige `plan.md` §2, fila Dominio):** `HuellaDeOrigen`, `HuellaDeSujeto`, `ReservaDeLimite` y `CategoriaDeError` forman parte del contrato público que usan otros módulos y la Api; la regla 13 prohíbe que un módulo dependa del Dominio de otro y la Api no referencia el Dominio. Viven en `Application.Plataforma` (raíz), con `InternalsVisibleTo` a la Infraestructura para que solo `CalculadorDeHuellaHmac` construya las huellas. En el Dominio quedan `PoliticaDeLimite`, `DecisionDeLimite`, `RespuestaDeLimite`, `PoliticasDeSeguridad` y `UmbralDeAlerta`. Los módulos nombran las políticas con `ClaveDePolitica`/`PoliticasDeLimite` (D04) y reciben un `ResultadoDeLimite` sin tipos del Dominio.
- [ ] **T13:** ejecutar **todos** los scripts con el usuario ACL restringido; `Reservar` rechaza sin agregar ni recrear `blq:` si el conteo sigue ≥ máximo; `retryAfterSeconds` desde la entrada más antigua (prueba de SEG-29: 10 fallos en 50 minutos y un intento al terminar el bloqueo); `Liberar` borra `blq:`, su entrada y su `rsv:`.
- [ ] **T03/T05:** `HuellaDeSujeto` solo la construye `ICalculadorDeHuella`; prueba con un número sembrado que no aparezca en `lim:*`, `idem:*` ni `rsv:*`. T05: corregir «el umbral y la ventana de 5 minutos son de Auditoría» a «la ventana de 5 minutos de SEG-08 es de Auditoría; el umbral de SEG-09 lo evalúa T11».
- [ ] **T15:** dispara solo al cruzar el umbral, con marca de agua del último evento evaluado, y registra su corrida en `trabajos_de_fondo`; en el Inventario evalúa solo los accesos denegados.
- [ ] **T29:** agrega también la lista de secretos (`BABELHUB__Seguridad__ClaveDeHuella`, clave del cursor de Auditoría) a `docs/05` y a `transversal-plataforma` (el arranque falla si falta, es corta o se repite).
- [ ] Idempotencia de enlaces firmados: la prueba «repetir la clave en el minuto 6 consume una descarga» y «100 repeticiones no superan el primer vencimiento» (D24) va con el plan de la biblioteca digital.
- [ ] Pendientes heredados que decide este plan al implementar: agrupar el evento de AUD-44/SEG-32 y aclarar que la «dirección directa» de SEG-32 solo sirve de clave del tope (no choca con SEG-17); citar SEL-13 y SEL-14 como límites por cliente que siguen aplicando.

## 2. `transversal-plataforma`

Tareas a partir (una tarea = un commit con su prueba primero):
- [ ] **T15:** (a) `RegistroDeSupresionesEnObjetos` y su migración; (b) adaptador EF Core de `IRegistroDeTrabajosDeFondo` con su prueba y el rol de escritura del esquema `plataforma`.
- [ ] **T22:** (a) respaldo diario, archivado de WAL con latido y cifrado `age`; (b) respaldo bajo demanda y copias dominical y del último día del mes.
- [ ] **T29 (`restaurar.sh`):** (a) descifrado en el equipo, transporte por `ProxyJump` y pruebas negativas; (b) orquestación de la apertura en el orden de §5.1.

Pruebas y precisiones a añadir:
- [x] Prueba de conformidad entre el script de sellos de VM-AUX y el verificador en C# (T12 o T25): hecha en T25 (`SellosDelRegistroTests`).
- [ ] Ruta del informe local: `progress/ensayos/local/` (ya en `.gitignore`); prueba de que está excluida y de que `progress/ensayos/` solo tiene contadores (T14 o T29).
- [x] **Marca de un solo uso de `restaurar.sh`:** HMAC con un secreto que solo conocen el script y el servicio, nonce de un solo uso y caducidad de pocos minutos; prueba negativa con una marca reutilizada o vencida (T17).
- [ ] Quién vacía la caché de claves del Inventario tras rotar (paso 3 de §5.1): `restaurar.sh` o `IClienteDelInventario`.
- [ ] Prueba negativa de la VM: «ni clave privada ni tar descifrado ni copia del flujo; solo el directorio de datos de PostgreSQL».
- [ ] T33 (4 horas) se mide por el camino de `restaurar.sh` desde el equipo de la persona y registra el ancho de banda.
- [ ] `respaldos-lector` se rota «tras cada ensayo con datos reales **o restauración real**».
- [ ] Fe de erratas de D07/D08/D15: `respaldos-wal` es de 100 días (no 95), contenedores por clase, copia mensual del último día, `T24` se lee `T26`, `supresiones-registro` solo en `entradas/`; D24 añade que el ciclo de vida de `respaldos-wal` es de 100 días.
- [ ] Decidir si la apertura tras restaurar, la rotación y la vía de excepción del sello se registran como evento de auditoría; y fijar en T34 la fuente de verdad de «último cambio» (etiqueta de lanzamiento).
- [ ] Residuales a vigilar: la detección de agentes por procesos es heurística (concretar en T05 qué se desactiva); cuota inflable con objetos bloqueados (alerta de crecimiento anómalo); entrada falsa dirigida a una cuenta (el informe lista los identificadores reaplicados); reformular PLA-16/AC-PLA-16 al reabrir la spec.

## 3. `transversal-auditoria`

Erratas y textos:
- [ ] T18: reordenar los paréntesis y agregar un motivo del catálogo «no enviable por correo» para distinguir la alerta originada por el correo; T31: separar `docs/03/05` y `docs/05`; `plan.md` §8 (AC-AUD-03 y AC-AUD-10): la lista de lo que revierte incluye la declaración de clave comprometida; D14: el desvío de la nota de la spec §7 está en hallazgos; anotar como D24 que los ingresos fallidos se cuentan por `IFuenteDeRegistrosAislados` (AD18).

Pruebas y precisiones:
- [ ] **T24:** probar AC-AUD-09 contra **ambos** esquemas (`auditoria` e `identidad.registros`, esta última con el plan de Identidad T27 y T30) y un registro de más de dos años de un usuario activo.
- [ ] **T10:** el conteo recibe solo una acción del catálogo y una ventana máxima de 5 minutos, devuelve solo un número, con tiempo de espera corto y fallo seguro (si Identidad no responde, el límite sigue protegiendo y se genera una alerta técnica); índice parcial por acción e instante; prueba de AC-SEG-09 con 21 ingresos fallidos de Identidad.
- [ ] **Rol de despliegue** de las migraciones: su credencial no está en el entorno de la aplicación, es distinto del rol de la aplicación y del de depuración; prueba de que la aplicación no puede hacer `SET ROLE` a los propietarios.
- [ ] **T25/T30:** el tope de concurrencia con cortacircuitos cubre también la escritura del acumulado; prueba funcional (no solo de carga) de la agrupación en «otros» y del tope de 1000 filas con contador de ventana.
- [ ] **T31:** `BABELHUB__Auditoria__ClaveDeCursor` (distinta por servicio, con versión) en la lista de secretos; riesgo residual de `por_anonimizar` ya está en AM34.
- [ ] Justificar los 30 días de `por_anonimizar` frente a un registro posterior que mencione la cuenta, o alinearlos con la retención del registro de supresiones.
- [ ] División por servicio: T18, T26 y `IDestinatariosDeAlertas` son solo del E-commerce; la parte de T20 (baja idempotente 204, esquema estricto) la implementa `inventario-seleccion-fuente` (SEL-34 y SEL-35).
- [ ] Residuales: agrupación en «otros» frente a AUD-28, mínimo de 3 caracteres del filtro, caducidad del cursor y tope de ventana; fila que confirma tarde (D22); AUD-37 no enumera el evento del tope (alinear al reabrir).

## 4. `identidad-seguridad`

Tareas a partir:
- [ ] **T15:** (a) ingreso en dos pasos y emisión; (b) contraseña temporal y eventos de ingreso fallido, bloqueo y código inválido.
- [ ] **T34:** (a) `SmtpEnviadorDeCorreo` y canal en memoria de códigos; (b) cola `correos_pendientes` cifrada y purga; (c) `cuota_correo` y reintento con prioridad.
- [ ] **T47:** carga de Argon2id y reversión de la cancelación por módulo y verbo son dos tareas.
- [ ] **T32/T42:** aclarar quién valida las credenciales en el E-commerce y cómo se enchufa al middleware, sin que otros módulos referencien Identidad salvo por su contrato.

Pruebas y precisiones:
- [ ] **T48:** añadir «canal lleno: misma respuesta, el trabajo se descarta en silencio y se publica una alerta técnica». **T37:** añadir la purga de `solicitudes_de_codigo` a los 10 minutos.
- [ ] **Envío de códigos (T30 y las tareas que envían códigos):** el correo de un código de un solo uso se construye solo con `CorreoDeCodigo.ParaElTitular(correoDeLaCuenta, propósito, ValorSecreto)` de `Plataforma` (transversal-seguridad T12), con la dirección registrada de la cuenta y nunca la de la solicitud; el código viaja como `ValorSecreto` y los registros y avisos pasan por `RedactorDeDatosProhibidos`.
- [ ] **T17 y T20:** repetir que la solicitud de recuperación y el rechazo o vencimiento del cambio de correo no reconocido van por `IRegistroDeSeguridad`; T15: corregir la coma doble.
- [ ] **T27:** `IFuenteDeRegistrosAislados` incluye el conteo por acción y ventana deslizante de 5 minutos, con la prueba de los 21 ingresos fallidos.
- [ ] **T39/T50:** la clave de entorno se rota por redespliegue y el trabajo de rotación re-cifra la privada con una versión nueva (campo de versión en `claves_de_firma`); `infra/respaldos/custodia.md` lo crea `transversal-plataforma` T28 y T50 solo añade su entrada; T50 traslada a `docs/05/03` §6 la decisión de custodia (la lista de alcances ya está allí).
- [ ] T45 depende del plan de `web-base` (`web.sesiones_web`, paso de restauración que invalida sesiones web, prueba E2E de que el JWT y la renovación no llegan al navegador); T46 depende del plan de `inventario-seleccion-fuente`.
- [ ] Riesgos heredados a decidir al reabrir la spec: IDE-84 (edad mínima de la cuenta sin verificar, TOCTOU con la consulta de pendientes, rechazo indistinguible de IDE-13), llamada REST al Inventario dentro de la transacción de cancelación, dueño único del recuento de reembolsos pendientes, IDE-30 («emitir») frente a D18.

## 5. `transversal-notificaciones`

Textos y orden:
- [ ] `plan.md` §2 (línea 28): «(cliente REST del Inventario)» acompaña a `IContactoDeSucursal`; quitar `IRegistroDeSeguridad` de esa lista y nombrar `IPublicadorDeEventosDeSeguridad`; decidir si `CalculadorDeHuellaDeNumero` (T13) es adaptador o servicio de Aplicación sobre `ICalculadorDeHuella`.
- [ ] **T08/T09:** implementa T09 (cupo) antes de cerrar T08; `plan.md` línea 108 y D17: «sin consumir intento» solo para indisponibilidad o espera agotada, y los `fallido` entran en la retención de 90 días.

Pruebas y precisiones:
- [ ] **T22/T23:** añadir la prueba «Inventario no responde → pendiente → `fallido` por vencido a las 24 horas».
- [ ] **Reserva idempotente (T09/T23):** clave por política (hora y 24 horas), `en_curso` (409) se trata como aplazamiento sin intento, `Completar` tras persistir `cupo_reservado` y `Fallar` si no se persiste; la repetición no cuenta contra el tope.
- [ ] **T20:** ráfaga de 60 por minuto y techo de 200 avisos por hora por sucursal, con prueba.
- [ ] **Orden con `EjecutarComo` y el savepoint:** primero `ROLLBACK TO SAVEPOINT` y solo después restaurar el rol; prueba junto con el error forzado del `INSERT` de T22.
- [ ] La huella del evento del tope se marca como campo personal del detalle (AUD-06) para que la depuración de 2 años la anonimice; no se publica el número ni la huella completa.
- [ ] Las desactivaciones de números posteriores a un respaldo no se reaplican al restaurar: incluir `numeros_desactivados` en la reaplicación de PLA-19 o registrar el residual.
- [ ] Spec: AC-NOT-16 debe incluir un aviso cancelado por NOT-08 o NOT-13; NOT-15 mezcla dos dependencias (redactar «un aviso a un cliente nunca consume más de un cupo» con un conteo); AC-NOT-12 «se intenta y se reintenta hasta 5 veces»; `docs/05/07` y `docs/02/07` aún no mencionan NOT-14.
- [ ] Fuera de este plan: los planes de Pedidos, Pagos, Envíos, Biblioteca y Devoluciones llaman a `INotificador`; los de Reservas y Traslados del Inventario guardan y entregan las solicitudes de aviso; `web-base` decide la pantalla de avisos desactivados y fallidos.

## 7. Convenciones que dejó la implementación de `transversal-seguridad`

- [ ] **Roles por esquema (migraciones de cada servicio):** el rol de cada esquema se llama `rol_<esquema>` (`rol_identidad`, `rol_pedidos`…), sin login y sin ser dueño de las tablas; la aplicación se conecta con un rol de acceso `NOINHERIT` que es miembro solo de los roles de esquema (nunca del de depuración ni del propietario). `EjecutarComo` hace `SET LOCAL ROLE "rol_<esquema>"`; si se cambia el prefijo, se cambia en `OpcionesDeUnidadDeTrabajo` y en las migraciones a la vez.
- [ ] **Repositorios y contextos de EF Core de cada módulo:** toman la conexión y la transacción de `IConexionDeLaUnidadDeTrabajo` (contrato público de `Plataforma`) para escribir dentro de la transacción del caso de uso; no se abre otra conexión.
- [ ] **`transversal-plataforma` T13 y T15:** `IRegistroDeTrabajosDeFondo` ya existe en `Application.Plataforma` (lo definió `transversal-seguridad` T15 con `Registrar(trabajo, ResultadoDeTrabajo, instante)`); el plan de Plataforma lo adopta, lo amplía solo si hace falta y construye el adaptador de `plataforma.trabajos_de_fondo` y, junto con su migración, la persistencia del estado de los umbrales de alerta (hoy `EstadoDeUmbralesEnMemoria`).
- [x] **T23 se parte (2026-11-13):** T23a = contratos de error en `Shared` (`ProblemaDto`, `RespuestaDeLimiteDto`, `CodigosDeError`), `IExceptionHandler`, correlación, mapeo de categorías a `<SRV>-…` y respuesta 429 uniforme con `Retry-After` (AC-SEG-03, AC-SEG-07 y AC-SEG-08 en el formato); T23b = middleware de límites en orden (tope previo, límite general por origen sin sesión y por usuario) sobre `IControlDeLimites`. `MotorDeLimitesNoDisponibleException` pasa a la raíz del contrato de `Plataforma` (la Api no puede depender de `Interno`).
- [x] **T27 se parte (2026-11-13):** T27a = lo que no necesita `Auditoria` real, con un ingreso doble sobre el pipeline real y el motor de Redis real: respuesta de límite idéntica exista o no la cuenta, 429 **antes** de verificar (con credenciales correctas también), intentos paralelos sobre una cuenta (30 simultáneos: 5 se verifican, 25 se rechazan), bloqueo por combinación (otro origen sigue accediendo) y un evento de seguridad por rechazo sin cuenta ni correo (`tests/integration/.../Limites/IngresoConLimitesTests.cs`); **hecha**. T27b = los 50 rechazos de un origen en una ventana de 5 minutos con **un solo evento** y el contador en 50, y el reinicio sin perder ni duplicar: es del acumulador persistente de `Auditoria` (T10, T18 y T25 de `transversal-auditoria`) y se hace cuando ese plan esté construido; **T27b hecha (2026-11-14, SEG-C)**: `tests/integration/.../Limites/LimitesConAuditoriaRealTests.cs`.
