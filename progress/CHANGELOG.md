# CHANGELOG

Qué se completó, cuándo y en qué feature. Se actualiza con la skill `progress-log`. El detalle de cada sesión está en `log/`.

## 2026-11-06 — Reformulación a dos servicios y seis atributos de calidad

Informe en `progress/R5-reformulacion-a-dos-servicios.md`.

### Arquitectura y documentación
- **Dos servicios**, cada uno un monolito modular en cuatro capas: E-commerce (11 módulos, con la interfaz web, Identidad y Asistente) e Inventario (4 módulos). Una base por servicio con esquema por módulo y rol por esquema (AD01, AD02, AD03, AD15, AD18).
- **Despliegue gratuito de cuatro VMs en una cuenta** (AD11): borde y salto en AMD micro; ventas e inventario con la base en ARM. Réplica de gestión, segundo conector de cloudflared, caché en la VM de ventas, defensa en profundidad con RLS y WAL fuera de Oracle (AD27, AD31). Límites de uso en cuatro niveles (AD24).
- **Atributos de calidad de 14 a 6** (AC01 a AC06), con prueba y medida por atributo.
- Reescritos: estilo (con alineación y relación con SOA y ESB), despliegue, presupuesto de recursos y tácticas. Revisados: decisiones (nueva AD31), drivers, C4 niveles 2 y 3, Clean Architecture, módulos y contratos, modelo de datos, seguridad, análisis, UX, proceso, glosario, `AGENTS.md` (regla 13), README y CLAUDE.md.

### Specs
- Seis specs vuelven a `Draft` (B10) y se **revisaron y corrigieron el mismo día** (sin preguntas abiertas): `inventario-ajustes`, `inventario-fuentes`, `ecommerce-catalogo`, `identidad-seguridad`, `asistente-compra-ia` y `transversal-seguridad`. Nuevos requisitos: ingreso de mercadería en la fuente de recepción (AJU-19, AJU-20), fuente de recepción y unidades vendidas (FUE-19 a FUE-22), stock inicial, ingreso desde el catálogo y agotado sin baja (CAT-20 a CAT-23), límite de concurrencia del asistente (ASI-20), presupuestos del personal y verificación humana (SEG-30, SEG-31). Hallazgos en `progress/R5-reformulacion-a-dos-servicios.md` §3.1. Las demás solo cambian los identificadores de atributos.

### Código, pruebas e infraestructura
- Se unieron al E-commerce los proyectos de Identidad, Asistente y Web (solo andamiaje); módulos como carpetas por capa; nuevas pruebas de módulos (regla 13). `dotnet build -warnaserror` y todas las pruebas correctas.
- Compose local con dos bases; Nginx por host con límites; prototipo con stock único, ingreso en el Catálogo, Vendidos y descuento al pasar a en reparto.

### Bloqueos
- Cerrados: segunda cuenta (C06), red entre cuentas (C07) y reformulación (C08). Nuevos B10 (reaprobación de specs) y B11 (rendimiento de la entrada).

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
