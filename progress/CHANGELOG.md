# CHANGELOG

Qué se completó, cuándo y en qué feature. Se actualiza con la skill `progress-log`. El detalle de cada sesión está en `log/`.

## 2026-11-14 — Cuarta tanda de carriles (AUD-4, WEB-1b y WEB-1c, fundidas a `main`)

### Cubierto
- **Carril A:** `transversal-auditoria` T22–T27 (AUD-4 sin T21): migraciones, roles y disparadores de solo inserción, función de depuración por lotes de 5000, repositorios de registros, alertas, límites y `por_anonimizar`, cliente HTTP de la auditoría del Inventario y trabajos de fondo de cierre, correo y depuración con actor de sistema explícito; `IEstablecedorDeActor` pasa a ser un puerto de `Application.Plataforma`, con regla de arquitectura sobre quién puede usarlo.
- **Carril C:** `web-base` WEB-1b (T10–T15: almacén de sesiones y borradores, cifrado AES-GCM por host, anillo de Data Protection, purga en dos fases, token del usuario y cookie) y WEB-1c (T16–T20: antiforgery, cabeceras y CSP por ruta, filtros de área, pruebas de sesión y rutas).
- Corrección de integración: el registro del ejecutor de trabajos de fondo del Inventario pasa a `Infrastructure.Plataforma` (`a749d6f`).

### Pendiente
- El cliente de auditoría del E-commerce debe dejar de consumir directamente el token del usuario (lo corrige el carril A; una prueba de arquitectura sigue en rojo en `main` hasta entonces); T21 y AUD-5; WEB-1e; INV-2a en curso en el carril B.

## 2026-11-14 — Tercera ola de carriles en paralelo (fundida a `main`)

### Cubierto
- **Carril A:** `ITokenDelUsuarioDeLaSesion` en `Application.Plataforma` y `OrdenDePasoDeRestauracion.Web` (R12); `transversal-auditoria` AUD-3: puertos de `Plataforma`, consultas unificadas con cursor firmado, agrupación y envío de alertas, depuración diaria de 2 años y bajas del Inventario (T10, T17–T20).
- **Carril B:** `inventario-fuentes` T09–T25 (INV-1b e INV-1c): Aplicación, esquema `fuentes` con seguridad por filas, repositorios con el `UPDATE` condicional único, composición con actor de integración y de sistema, endpoints de lectura y administración, pruebas de contrato contra ECO-0 y escenario k6 (no ejecutado). **`inventario-fuentes` T01–T25 completa**; el riesgo del alcance sin actor quedó cubierto con pruebas contra PostgreSQL y el rol real.
- **Carril C:** `web-base` WEB-1b parcial (T10–T13): almacenes de sesión y borrador bajo el rol `web` con bloqueo de fila, cifrado AES-GCM por host, anillo de Data Protection por host y purga en dos fases.
- Validación de la fusión: unitarias 1541 y 1479, arquitectura 155, SpecCheck 23, CoverageCheck 12, integración 2601 de 2601, gitleaks limpio.

### Pendiente
- AUD-4 (hecho en la rama del carril A, sin validar), `web-base` T14–T15 y WEB-1c, INV-2a en adelante; hallazgos nuevos del carril B para A (motivo `alcance-de-datos-sin-actor`, composición del Inventario) y para C (supuestos de contrato) en `progress/hallazgos.md`.

## 2026-11-14 — Segunda ola de carriles en paralelo (fundida a `main`)

### Cubierto
- **Carril A:** `transversal-auditoria` T07, T08, T09 y T11–T16 (AUD-2): contratos de registro, consulta y alerta, puertos de Identidad y Reservas, 83 acciones de auditoría por servicio, denegación de acceso y eventos de seguridad. Revisión de nivel C con 7 Importantes de seguridad y 5 de arquitectura, corregidos.
- **Carril B:** `inventario-fuentes` T01–T08 (INV-1a): bloqueos asesores, reintento por interbloqueo, alcance de datos por transacción con elevación restringida a `Fuentes` y `Ajustes`, reglas de existencias, fuentes y rutas, contrato público y puertos, y movimientos de stock.
- **Carril C:** `web-base` WEB-1d (T21–T27: sistema de diseño con Tailwind 4 y daisyUI 5, layout de tres áreas, estados, textos legales, «Sorpréndeme», descarga) y WEB-1a (T01–T09: módulo `Web` con sesión y borrador).
- Validación de la fusión: unitarias 1406 y 1221, arquitectura 155, SpecCheck 23, CoverageCheck 12, integración 2284 de 2285 (una prueba de tiempo real que pasa sola), gitleaks limpio. Tareas de la fase 3 hechas: 92 (58 + 34).

### Pendiente
- Riesgo de INV-1c registrado en `progress/hallazgos.md` (el alcance falla cerrado: llamadas de servicio y trabajos de fondo necesitan un actor explícito). Tercera ola: AUD-3, INV-1b, WEB-1b; antes, `ITokenDelUsuarioDeLaSesion` y la constante `Web` del orden de restauración (carril A).

## 2026-11-14 — Primera ola de carriles en paralelo (fundida a `main`)

### Cubierto
- **Carril A:** `transversal-plataforma` T26, T27, T28 y T30 (endurecimiento de VM-AUX, espacio local reservado, custodia y descifrado de ensayo, `tools/RestauracionCheck`) y `transversal-auditoria` T01 a T06 (Dominio duplicado en los dos servicios). Revisión de seguridad y arquitectura de nivel C con todos los Bloqueantes e Importantes corregidos. La persona aprobó la credencial de correo propia `aviso.env` de la VM de datos.
- **Carril B:** los planes de las cuatro specs de `inventario-*` (102 tareas) con spec Approved y plan Planned tras seis rondas; SEL-36/37/39 (tope de 3 reemplazos, residual de 75 minutos pendiente de confirmar) y AJU-21/22 (retención de 2 años y anonimización) reabrieron esas specs.
- **Carril C:** ECO-0 (43 contratos v1 entre E-commerce e Inventario en `BabelHub.Shared`, documento `08-contratos-entre-servicios-v1.md`, dobles de usuario actual) y los planes aprobados de `web-base` (39 tareas) y `ecommerce-catalogo` (28 tareas). Riesgo aceptado por la persona: los alcances de `expiracion-de-reservas` y `solicitar-aviso` exceden su nombre.
- `tools/RestauracionCheck` añadido a `BabelHub.sln`. Tareas de la fase 3 hechas: 58 (48 anteriores + 10 de esta ola); las specs recién planificadas suman 169 tareas más (ver `progress/bloques.md`).

### Pendiente
- AUD-2 en adelante (de ello depende INV-1a), WEB-1d, INV-1a y los bloques siguientes de `progress/bloques.md`; confirmar el residual de 75 minutos; decidir el desvío de la hora de Lima en `transversal-auditoria` (T07).

## 2026-11-14 — Proceso: desarrollo por bloques, carriles y rigor graduado

### Cubierto
- Decisión de la persona responsable (producto completo en 2 a 3 semanas): rigor graduado por riesgo (crítico, estándar, bajo), trabajo por bloques con una sola validación y revisión por bloque, y hasta tres carriles en paralelo con protocolo de integración. Documento nuevo `docs/07-proceso-de-desarrollo/05-desarrollo-por-bloques.md` y lista de bloques en `progress/bloques.md`.
- Propuestas que chocan con `AGENTS.md` y con la aprobación delegada (una tarea a la vez por carril, puerta graduada de planes) quedan redactadas como pendientes de la persona; no rigen.

## 2026-11-14 — Implementación de `transversal-plataforma` (T06 a T24)

### Cubierto
- **20 de 42 tareas** (T06 a T25) de `transversal-plataforma`: puertas de calidad y de cadena de suministro (cobertura, secretos, versiones fijadas, pruebas sin red), registro de supresiones y modos de arranque con salud, rotación de credenciales, imágenes multi-arquitectura, Compose endurecido por VM, definición del almacén de objetos con retención bloqueada, respaldo diario y archivado de WAL cifrados con `age` (con restauración probada), respaldo bajo demanda, `desplegar.sh`, la prueba de retención con el vigilante de VM-AUX y los scripts de VM-AUX (comprobación de respaldos con aviso por correo, sellos y copia del registro, ancla y latido).
- El plazo mensual de retención pasa de 90 a 92 días (la simulación mostró que 90 no garantiza los 3 meses de AC-PLA-18).
- Estabilidad de la suite: colección sin paralelismo para las pruebas pesadas y márgenes de tiempo mayores.

### Pendiente
- 5 spikes (necesitan Oracle, VMs o datos reales), T26 a T42, `transversal-seguridad` T27b y T29 (esperan a Auditoría) y las demás specs de la fase 3 (126 tareas sin empezar).

## 2026-11-13 — Implementación de `transversal-seguridad` (T01 a T30)

### Cubierto
- **28 de 30 tareas** de `transversal-seguridad` en los dos servicios: límites de uso (motores de Redis, PostgreSQL y local), huellas HMAC, firma de integraciones y entradas procesadas, verificación de persona con Turnstile, cliente HTTP resiliente, secretos de entorno con arranque que falla y registros redactados, errores uniformes con `problem+json`, pipeline de límites de la Api, origen en el borde con Nginx, credencial de servicio con alcances y rotación, atributo de Turnstile por página, y revisiones automáticas del repositorio y de los registros.
- **Documentación al día:** autenticación, modelo de datos, privacidad, lista de secretos y Redis de contadores.
- Dos fallos de la redacción de registros descubiertos por las pruebas y corregidos.

### Pendiente
- T27b y T29 dependen de `transversal-auditoria`; después, `spec-verify` de `transversal-seguridad`. Siguiente plan: `transversal-plataforma`.

## 2026-11-13 — Reaperturas de la fase 3 y requisitos receptores de la cancelación

### Cubierto
- **Reaprobadas por la puerta delegada** (tres revisores sin Bloqueantes ni Importantes sobre el mismo texto, filas en `progress/aprobaciones.md`): `transversal-auditoria`, `transversal-seguridad`, `web-base`, `identidad-seguridad` (dos reaperturas: marcado de hojas, «anonimizar, desvincular o eliminar», IDE-89 y reembolsos pendientes en IDE-27), `inventario-seleccion-fuente`, `ecommerce-libro-reclamaciones`, `ecommerce-carrito-checkout`, `ecommerce-pagos` (dos veces), `ecommerce-biblioteca-digital`, `ecommerce-pedidos`, `ecommerce-envios` y `ecommerce-devoluciones`. Las 20 specs están `Approved`.
- **Receptores de la cancelación de cuenta y retención de 2 años:** SEL-33 a SEL-35, REC-19 y REC-20, CAR-30 a CAR-37, PAG-24 a PAG-32, BIB-25 a BIB-29, PED-31 a PED-41, ENV-15 a ENV-19, DEV-23 a DEV-31. CTL-33 vuelve a «Especificado».
- **Decisiones de la persona responsable** (en `progress/decisiones-previas-al-plan.md`): retención de 2 años; teléfono de entrega en el checkout; contacto de entrega abandonado a 30 días; el reembolso pendiente difiere la cancelación; el pedido conserva el identificador opaco de la cuenta y el no recogido cuenta como pendiente; retención de 2 años del texto libre de Devoluciones; se aceptan ambos avisos de un envío.
- **Documentos alineados:** privacidad (filas de entrega, intención e intentos de pago, biblioteca, descargas, ayudas, devoluciones y retención), amenazas AM30 y AM31, modelo de datos (`pedidos`, `envios`, `descargas`, `ayudas_descarga`), `docs/02` y `docs/03` (dependencias, puerto de despacho, idempotencia del descuento por envío).

### Pendiente
- Planes y tareas de la fase 3 (`spec-plan`, `spec-tasks`); los Menores y los Importantes heredados están en `progress/hallazgos.md` (CAR-20 y el ciclo latente de Biblioteca, idempotencia del reembolso en Pagos y de SEL-28 con ENV-12, PAG-05).
- Bloqueos de credenciales y legales (B03 a B07, B09, B11, B13).

## 2026-11-12 — Aprobación delegada de la primera tanda de specs

### Cubierto
- **Proceso:** puerta de aprobación por gravedad (Bloqueante, Importante, Menor) en `docs/07-proceso-de-desarrollo/04-aprobacion-delegada.md`, skill `spec-approve`, registro `progress/aprobaciones.md` con huella, registro de Menores en `progress/hallazgos.md` y validación en `tools/SpecCheck` con pruebas en `tests/tools`.
- **Aprobadas:** `transversal-auditoria` (huella `0a3077334d725f96`) y `transversal-plataforma` (huella `0bfc0ef175eb8bf4`), cada una con los tres revisores sin Bloqueantes ni Importantes.
- **Decisiones de la persona responsable:** Oracle Object Storage para los respaldos (riesgo B13 aceptado), MailKit con una cuenta de Gmail sencilla, Culqi y OpenCode Go como proveedores, cuatro rondas más de revisión autorizadas, cuota de correo simplificada en la spec (los tamaños y topes pasan al plan).
- **Specs reescritas o ampliadas:** `transversal-auditoria` (AUD-01 a AUD-41), `transversal-plataforma` (PLA-01 a PLA-43), `identidad-seguridad` (IDE-01 a IDE-86, con cancelación diferida, clave comprometida y cuota de correo).
- **Documentos alineados:** autenticación, operación segura, despliegue (copia consistente del clúster, restauración solo del clúster completo), modelo de datos (`fecha_confirmacion`, segundo factor del personal), privacidad y bloqueos.

### Pendiente
- `identidad-seguridad` quedó `Approved` el 2026-11-12 por la puerta delegada (huella `87679a1b8ebb351a`, 88 requisitos y 66 criterios) tras la auditoría de cierre.
- Reabrir al planificar cada una (hay Importantes de diseño en `progress/R7-auditoria-de-cierre-fase-2.md`): `inventario-seleccion-fuente`, carrito, pagos, pedidos, envíos, devoluciones, reclamaciones y biblioteca; `transversal-auditoria`, `transversal-seguridad` y `web-base` ya se reabrieron y reaprobaron el 2026-11-13 (detalle en `progress/hallazgos.md`).

## 2026-11-06 — Auditoría final de coherencia y cobertura

Informe en `progress/R6-auditoria-final.md`: 18 hallazgos (5 de prioridad P1, 8 de P2 y 5 de P3) y, en §7, el estado de cada uno.

### Cubierto
- **Specs:** `transversal-auditoria` reescrita para dos servicios y **devuelta a `Draft`**; nueva spec `transversal-plataforma` (PLA-01 a PLA-14) para RNF10, 11, 12, 19, 24 y 28, en `Draft`. Ambas esperan la aprobación de la persona responsable (B12). WEB-26 y dos criterios de `web-base` con la redacción actual (sin cambio de comportamiento).
- **Requisitos y dominio:** RF10, RF12 y RF17 respaldan el ingreso de mercadería, el stock inicial, la fuente de recepción y las unidades vendidas; el modelo de dominio los incorpora.
- **Proceso:** fases del flujo SDD y orden de `specs/README.md` calculados a partir de las dependencias (Identidad antes que el Inventario; `web-base` incremental); índice de specs regenerado; skills, agente y plantilla actualizados a la arquitectura de dos servicios.
- **Documentación:** vínculos drivers–decisiones completos, base técnica `evolution`, presupuesto de VM-INVENTARIO sin margen y su orden de recorte, limitaciones declaradas, nombres de trabajo de los hosts, nota de verificación de cifras.
- **Infraestructura y pruebas:** Nginx confía en la IP del cliente solo desde el túnel; con Docker se verificaron Compose, las dos bases, el aislamiento entre roles y `nginx -t`; nuevas pruebas de sensibilidad de arquitectura (26 pruebas).
- **Presentación (H16):** `docs/BabelHub_Exposicion.pptx` (diapositivas 1, 4 a 10 y 13 a 15) y el resumen local actualizados a dos servicios, cuatro VMs, seis atributos, 31 decisiones y 20 specs. Canva queda fuera del proyecto por decisión del responsable.

### Pendiente
- Aprobación de `transversal-auditoria` y `transversal-plataforma` (B12); decisiones B02, B06 y B08 (con recomendaciones); redacción de `AGENTS.md` §3 sobre la regla 9.

## 2026-11-06 — Reformulación a dos servicios y seis atributos de calidad

Informe en `progress/R5-reformulacion-a-dos-servicios.md`.

### Arquitectura y documentación
- **Dos servicios**, cada uno un monolito modular en cuatro capas: E-commerce (11 módulos, con la interfaz web, Identidad y Asistente) e Inventario (4 módulos). Una base por servicio con esquema por módulo y rol por esquema (AD01, AD02, AD03, AD15, AD18).
- **Despliegue gratuito de cuatro VMs en una cuenta** (AD11): borde y salto en AMD micro; ventas e inventario con la base en ARM. Réplica de gestión, segundo conector de cloudflared, caché en la VM de ventas, defensa en profundidad con RLS y WAL fuera de Oracle (AD27, AD31). Límites de uso en cuatro niveles (AD24).
- **Atributos de calidad de 14 a 6** (AC01 a AC06), con prueba y medida por atributo.
- Reescritos: estilo (con alineación y relación con SOA y ESB), despliegue, presupuesto de recursos y tácticas. Revisados: decisiones (nueva AD31), drivers, C4 niveles 2 y 3, Clean Architecture, módulos y contratos, modelo de datos, seguridad, análisis, UX, proceso, glosario, `AGENTS.md` (regla 13), README y CLAUDE.md.

### Specs
- **Aprobadas de nuevo por el responsable el 2026-11-06** (C09). Habían vuelto a `Draft` y se **revisaron y corrigieron el mismo día** (sin preguntas abiertas): `inventario-ajustes`, `inventario-fuentes`, `ecommerce-catalogo`, `identidad-seguridad`, `asistente-compra-ia` y `transversal-seguridad`. Nuevos requisitos: ingreso de mercadería en la fuente de recepción (AJU-19, AJU-20), fuente de recepción y unidades vendidas (FUE-19 a FUE-22), stock inicial, ingreso desde el catálogo y agotado sin baja (CAT-20 a CAT-23), límite de concurrencia del asistente (ASI-20), presupuestos del personal y verificación humana (SEG-30, SEG-31). Hallazgos en `progress/R5-reformulacion-a-dos-servicios.md` §3.1. Las demás solo cambian los identificadores de atributos.

### Código, pruebas e infraestructura
- Se unieron al E-commerce los proyectos de Identidad, Asistente y Web (solo andamiaje); módulos como carpetas por capa; nuevas pruebas de módulos (regla 13). `dotnet build -warnaserror` y todas las pruebas correctas.
- Compose local con dos bases; Nginx por host con límites; prototipo con stock único, ingreso en el Catálogo, Vendidos y descuento al pasar a en reparto.

### Bloqueos
- Cerrados: segunda cuenta (C06), red entre cuentas (C07), reformulación (C08) y reaprobación de las seis specs (C09, aprobadas el 2026-11-06). Nuevo B11 (rendimiento de la entrada).

## 2026-11-06 — Auditoría del estilo basado en servicios y del despliegue (reemplazada por la reformulación posterior)

### Documentación
- Informe `progress/R4-auditoria-del-estilo-y-despliegue.md` (17 hallazgos, H1 a H17).
- `03-estilo-arquitectonico.md` §7: alineación punto por punto con el estilo basado en servicios, desviaciones conscientes y criterios de revisión.
- `05-despliegue.md` reescrito: cupo de Oracle por cuenta y PAYG, unidad de despliegue frente a host, **perfil objetivo de cuatro VMs ARM** (VM-BORDE, VM-VENTAS, VM-INVENTARIO y VM-DATOS) y **perfil mínimo** de dos, red entre VMs, una instancia de PostgreSQL con una base por servicio, reglas de red y matriz de fallos. Presupuesto de recursos por VM.
- Decisiones revisadas: AD02, AD03, AD10, AD11, AD12, AD14, AD16 y AD18. Requisitos RC07 y RNF12; drivers y riesgos; tácticas; amenazas (AM24 y fronteras de confianza); operación segura; modelo de datos (bases `ecommerce`, `inventario` e `identidad`); C4 nivel 2 y 3; convenciones; README.
- Base mínima `asistente` (registro de eventos de seguridad) añadida a los documentos y al Compose local (H17). Alcance académico de los libros digitales explícito en AD19/AD20: subida directa de EPUB y PDF al almacén de objetos.
- Culqi confirmada como pasarela (C05). B01 cerrado (specs aprobadas). Nuevos B10 (segunda cuenta de Oracle) y B11 (red entre VMs).

### Infraestructura local
- `docker-compose.local.yml` con una instancia PostgreSQL y `postgres/init/01-bases.sh`; `.env.example` actualizado. Solo se validó la configuración (`docker compose config`); no se levantó el contenedor.

### Specs
- Sin cambios: ninguna spec menciona máquinas ni esquemas, por lo que siguen `Approved`.

## 2026-10-16 — Análisis inicial y cierre de brechas

### Documentación
- Informe de análisis `progress/R3-analisis-inicial.md`, con los hallazgos y sus resoluciones.
- Nuevo modelo C4 (niveles 1 a 3) en `docs/04-modelo-c4/`.
- Marco legal ampliado (Código del Consumidor, menores, cookies, plazos de reclamos, ARCO e incidentes, a verificar); privacidad, seguridad, estados, dominio y modelo de datos alineados con las specs.
- Revisadas AD22 (segundo factor para todo el personal), RNF15 (contraseña de 10 caracteres sin composición), RC18/AD30 (correo también para respuestas a reclamos, alertas y avisos de pago) y AD16 (plan de salida de WhatsApp). Sistema operativo documentado: Ubuntu Server LTS.

### Specs
- Las 19 specs ajustadas, sin preguntas abiertas y sin errores de `SpecCheck`; aprobadas (`Approved`) por el responsable el 2026-10-16 con los valores propuestos; la verificación legal (B09) queda como riesgo aceptado.
- Revisión con `spec-auditor` y `security-reviewer` sobre auditoría, seguridad e identidad, y segunda ronda de ajustes (límites sin origen, registro de eventos agrupado, segundo factor, clave de firma, credencial del asistente, sesiones y auditoría ampliada). Nueva amenaza AM25 y AM26.
- Revisión de las specs de Inventario con `spec-auditor` y ajustes: ingreso inicial de stock, carreras entre reserva, traslado, anulación y entrega, reservas vencidas, traslados vinculados con aviso a las 48 horas, ajustes de la administración y criterios de concurrencia. Nuevo estado de reserva `cerrada`.
- Revisión de `identidad-seguridad`, `transversal-notificaciones` y `web-base` con `spec-auditor` y ajustes: cancelación de cuenta aplazada, renovación con vencimiento heredado, reto del segundo factor, cambio de correo según riesgo (nueva amenaza AM27), textos legales con versión validada por Identidad, correo verificado en la credencial, estados y reintentos de avisos y pantallas obligatorias de `web-base`.
- Revisión de `ecommerce-catalogo`, `ecommerce-carrito-checkout`, `ecommerce-pagos` y `ecommerce-pedidos` con `spec-auditor` y ajustes: intención de pago, ciclo de la línea con traslado, estado del pedido derivado, anulación y reembolso del envío, compensación del pago, pago pendiente solo digital, fusión del carrito de visitante y validaciones del catálogo.
- Revisión de `ecommerce-devoluciones`, `ecommerce-biblioteca-digital`, `ecommerce-envios` y `ecommerce-libro-reclamaciones` con `spec-auditor` y ajustes: reembolso digital automático o por reporte de defecto, sucursal receptora elegida por el cliente, devolución por tipo, reembolso sin envío, recompra que reactiva el acceso, envíos cancelados al anular un pedido y consulta de hojas con correo verificado.
- Barrido de coherencia de `docs/` (glosario, UX, procesos, requisitos, calidad, tácticas, arquitectura, diseño, datos y seguridad) y conflictos entre specs corregidos (TRA-09 frente a TRA-16, traslado perdido, carrito de visitante, envío por fuente, correo frente a WhatsApp).
- Nuevos o cambiados: rutas entre fuentes, reserva ligada al traslado, descuento de físico a domicilio, pedido no recogido, cancelación del cliente, pago pendiente con reserva extendida, límites de cuentas y de hojas por origen, sesión web, textos legales versionados, segundo factor del personal, mayoría de edad, auditoría unificada, alertas por correo y credencial de lectura del asistente.

## 2026-10-10 — Reformulación de la documentación

### Documentación
- `docs/` reorganizada en siete carpetas temáticas (análisis, arquitectura, diseño, modelo C4 reservado, seguridad, experiencia de usuario y proceso); documentos mixtos divididos y enlaces validados por `SpecCheck`.
- Línea base ampliada: NN16–NN18, OB07–OB08, RF26–RF29, RNF15–RNF28, RC14–RC18, AC10–AC14 (clasificados por ISO/IEC 25010:2023), DA13–DA17 y AD22–AD30 (catálogo en `progress/R0-catalogo-de-cambios.md`).
- Documentos nuevos: actores y permisos, procesos, modelo de dominio, tácticas, presupuesto de recursos, patrones, módulos y contratos, modelo de datos, estados y concurrencia, mapeo a IEEE 1016, seis documentos de UX, estrategia de pruebas y la carpeta `05-seguridad` (amenazas AM01–AM24 y controles CTL-01–CTL-38).

### Specs
- Nueva `transversal-seguridad` (SEG) y ajustes en nueve specs; dos pasadas del agente `spec-auditor` (ver `progress/R2-auditoria-de-specs.md`). Todas siguen en `Draft`.

### Marco SDD
- Nueva skill `security-review` y agente `security-reviewer`.
- Prototipo: el consentimiento de WhatsApp en el checkout pasa a ser opcional.

## 2026-10-10 — Fundación del proyecto

### Documentación
- `docs/01` a `docs/07`: necesidades, requisitos, atributos de calidad, drivers, decisiones AD01–AD21, estilo y enfoque arquitectónico.
- Libros digitales (NN13–NN15, RF22–RF25, RNF14, AC09, AD19–AD20) y marco legal con rigurosidad laxa por ser un proyecto académico.
- `docs/08` (interfaz de usuario) y `docs/09` (convenciones técnicas).
- `docs/10` (flujo SDD) y `docs/glossary.md`.

### Interfaz
- Prototipo `prototype/ui-v2/` con las áreas Tienda, Sucursal, Logística y Administración; temas claro y oscuro; estados de pantalla; ajustes de inventario guiados por motivo.

### Marco SDD
- `AGENTS.md`, `CLAUDE.md`, plantillas de `specs/`, 18 specs en borrador, validador `tools/SpecCheck`, skills y agentes en `.claude/`, integración continua.

### Esqueleto técnico
- Solución `BabelHub.sln` con los proyectos por servicio, proyectos de prueba, prueba de arquitectura e infraestructura local en `infra/`.
