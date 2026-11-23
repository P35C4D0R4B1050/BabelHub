# CHANGELOG

Qué se completó, cuándo y en qué feature. Se actualiza con la skill `progress-log`. El detalle de cada sesión está en `log/`.

## 2026-11-23 — Entorno `Staging` para la revisión de Culqi (carril `staging-culqi`)

### Cubierto
- `ecommerce-pagos` reabierta (PAG-33 a PAG-49, AC-PAG-29 a AC-PAG-41, D25; DD-65 a DD-69): `Staging` de tipo producción con el simulador de pagos y su aviso «Pagos simulados: no se realiza ningún cobro», `Production` sigue fallando cerrado; cuenta de prueba por consola (`cuenta-de-prueba`, `--retirar`), siembra de textos legales vigentes en `migrar`, HTTPS detrás de Cloudflare (redirección `308`, HSTS, sin contenido mixto), entorno y pasarela desde el archivo de secretos, datos de contacto por `BABELHUB__Contacto__*`.
- Guía del primer despliegue con `babel-hub.shop`, el flujo de dos fases con Culqi y la lista de requisitos. Comprobado con las imágenes reales (ensayo local).
- Ampliación (DD-85 a DD-88, ratificadas): operador logístico simulado también en `Production` con el aviso «Los envíos son simulados: proyecto académico» (PAG-42), `crear-administrador` (PAG-44, PAG-47), `cuenta-de-prueba --cantidad N`, `--retirar --todas` y `--purgar` (PAG-43, PAG-45), primera clave de firma en la misma transacción (PAG-46); 52 pruebas de integración de la operación sobre PostgreSQL.
- La spec sigue en `Draft` (no se aprobó): DD-89 ya está ratificada; falta cerrar la puerta de revisión (ver hallazgos). Abiertos: adaptador real de Culqi para `Production` (T37 y T38), `n8n` en el ensayo. Sin push.
- **Spec y plan de `ecommerce-pagos` aprobados el 2026-11-23** (por delegación, puerta sin Bloqueantes ni Importantes; filas en `progress/aprobaciones.md`; estado `Planned`); T39 a T47 pasaron a `tasks.md` (ECO-5g). DD-65 a DD-69 y DD-85 a DD-89 ratificadas. Aviso de pagos y de envíos verificados con Playwright (Chromium escritorio y teléfono) y la imagen real; `--purgar` pendiente de probar con el almacén de objetos real. Abiertos: adaptador real de Culqi para `Production` (T37 y T38), `n8n` en el ensayo. Sin push.
## 2026-11-23 — Preparación del primer despliegue en Oracle

### Cubierto
- Flujo `lanzamiento.yml` (imágenes `amd64` y `arm64` en GHCR por digest, con escaneo previo y procedencia firmada), `scripts/generar-secretos.sh`, `scripts/ensayo-local.sh` y la guía `08-guia-primer-despliegue.md`; `desplegar.sh` con `--sin`, `CAMBIAR`, `ca-bundle.pem` y entrega de llaves TLS.
- Ensayo con las imágenes reales: VM-INVENTARIO completa (Inventario en `Production`, HTTPS validado) y VM-VENTAS sin Evolution ni túnel. Corregidos Compose y tabla de secretos (HTTPS entre servicios, Biblioteca en la réplica, SMTP, alias del segundo conector). DD-55 a DD-59 pendientes de ratificar.
- Hallazgo mayor: el E-commerce no arranca en `Production` hasta el adaptador de Culqi (DD-58). Sin push ni etiquetas.


## 2026-11-23 — Web base: herramienta de apoyo para las pruebas humanas (T32b, T33b y T34b)

### Cubierto
- Nueva herramienta `tools/pruebas-humanas/` (`index.html` en un solo archivo, sin red ni telemetría, con `servir.mjs` para abrirla desde móviles reales): bloque A (navegadores y móviles), B (teclado y lector, con la definición de «crítico AA») y C (moderador de usabilidad con consentimiento, cronómetro, SUS 0 a 100 y resumen por ronda contra AC-WEB-17); exporta a JSON y Markdown, importa y combina archivos de varios dispositivos y borra los datos locales. Guías reforzadas (`09` y `10`), `README.md`, `veredicto.md` con el esquema JSON y los criterios para leer los resultados, e inventario de privacidad ampliado.
- 165 pruebas de Playwright propias (162 pasan y 3, de Tab, se omiten en WebKit) (`tests/e2e/pruebas-humanas/`: Chromium de escritorio y teléfono y WebKit de iPhone): SUS, resumen y criterio, exportación e importación, flujos, ausencia de red, política de contenido con hash y axe en ambos temas. Revisión de seguridad aplicada (DD-60 a DD-64, pendientes de ratificar).

### Pendiente
- Ninguna prueba con personas se ejecutó ni se inventó: T32b, T33b y T34b siguen en manos de la persona responsable; `web-base` sigue en `Planned`.


## 2026-11-21 (noche) — Cierre de Inventario: ajustes y selección de fuente a Done

### Cubierto
- `inventario-ajustes` vuelve a **Done**: AJU-22 se redactó como lo que el código ya hace (la repetición con la primera en curso espera y recibe el original o el conflicto; 503 uniforme si vence el bloqueo), se reaprobó spec y plan con la puerta (ronda 2 sin Bloqueantes ni Importantes) y se añadieron dos pruebas de AC-AJU-18. Decisiones DD-40 a DD-42, pendientes de ratificar.
- `inventario-seleccion-fuente` pasa a **Done**: S2 cerrada con el escenario k6 `tope-por-origen` (10 reservas exactas por origen con 12 cuentas por origen a la vez; el resto 429; sin 500 ni 503), evidencia en `progress/evidencias/k6/2026-11-21-tope-por-origen/` (DD-44).
- `inventario-fuentes` sigue «Converge salvo FUE-09/FUE-11 (diferidas) y FUE-10 (DD-18)»: FUE-09 y FUE-11 dependen del Catálogo y de Identidad (E-commerce) y de `web-base`; no se construyen en el Inventario.
- Verificado: build sin advertencias, unitarias del Inventario 2387, arquitectura 259, integración de Reservas/Ajustes/Fuentes/Traslados/Existencias 627 de 627, SpecCheck 0 avisos.
## 2026-11-21 (noche) — Avisos por WhatsApp: cierre de las reservas internas (DD-45 a DD-49)

### Cubierto
- `transversal-notificaciones`: D4 (auditoría real en PostgreSQL de la desactivación de números), D5 (DD-46), D6 (la reactivación entra en NOT-13; spec reaprobada con la puerta delegada) y la redacción de NOT-15 y AC-NOT-15 y AC-NOT-16; NOT-14 en la compra mixta aceptado (DD-45). Pruebas nuevas de AC-NOT-13 y AC-NOT-16.
- Queda como reserva externa S1 (n8n y Evolution reales con un número de WhatsApp real); DD-45 a DD-49 esperan ratificación.
## 2026-11-21 (noche) — Web base: WEB-25 en el Libro de Reclamaciones y evidencia automática de T32 a T34

### Cubierto
- `/reclamaciones/nueva` conserva y recupera, tras una sesión vencida, bien, monto, referencia, tipo y detalle (nunca documento, domicilio, nombre ni correo; DD-30 pendiente de ratificar). 8 pruebas de integración nuevas (AC-WEB-20).
- Corrida automática de T32 a T34 en Chromium de escritorio y teléfono y WebKit: responsivo, temas, movimiento reducido, axe y teclado pasan; evidencia en `progress/evidencias/web-base/`.

### Pendiente
- Partes humanas (navegadores reales, lector de pantalla, dos rondas de usabilidad): `protocolo-manual.md`. Reserva de medición de AC-WEB-16 con TLS. `web-base` sigue en `Planned`.
## 2026-11-21 (noche) — Cierre de seguridad: identidad-seguridad a Done y SEG-17/23 del Inventario

### Cubierto
- `identidad-seguridad` **Done**: D5 (la cancelación diferida indica qué falta por grupo, DD-35) y AC-IDE-70 con un pedido real por el flujo real del E-commerce.
- `transversal-seguridad`: SEG-17/23 del Inventario corregidos (origen desconocido con evento, espejado en ambos servicios, DD-36); SEG-31 del asistente queda diferido y declarado (DD-37), la spec sigue InProgress.
- Revisiones de seguridad y arquitectura sin Bloqueantes ni Importantes. Detalle: `progress/log/2026-11-21-cierre-seguridad.md`.
## 2026-11-21 (noche) — Palancas de rendimiento 3 y 4 (DD-50 a DD-54)

### Cubierto
- Caché de disponibilidad: ya existía en el E-commerce; documentada y medida. Memoria de referencias del Inventario encendida en 5 s (+45 % en medición alternada). Límites generales en memoria descartado. Detalle en `docs/03/09` §6e.

## 2026-11-21 (noche) — Libro de Reclamaciones: vínculo de las hojas con la cuenta (DD-26), Done

### Cubierto
- `ecommerce-libro-reclamaciones` reabierta y vuelta a **Done**: las hojas se vinculan por el identificador opaco de la cuenta (`cuenta_id`), con reclamo atómico por correo verificado, registro con sesión que audita la vinculación (REC-33: sin auditar no se guarda, el cupo vuelve con `Liberar` y la clave se suelta), receptor real de los cambios de cuenta (cambio de correo y reemplazo, cerrando la regresión del host real), vinculación inicial por lotes con cursor, cancelación que marca y desvincula, y anonimización que desvincula. T24 a T36 cerradas; REC-01 a REC-34 cumplen (`verificacion.md`: Converge).
- Verificado con PostgreSQL real (suite de integración: 7827 de 7828; la roja es un flaky de S3 de Plataforma que pasa sola), E2E (20 de la tienda y 12 de la gestión), arquitectura (259), unitarias (5662) y `ui-review`. Revisiones de arquitectura y de seguridad del código: sin Bloqueantes; Importantes corregidos.
- Alerta técnica nueva `vinculacion-inicial-de-hojas-sin-completar` en el catálogo cerrado (Shared, ambos servicios y presentador).

## 2026-11-21 (noche) — Primer CI de GitHub en verde

### Cubierto
- `push` de `main` y tres corridas de `ci` y `secretos`: 135 fallos en Linux, luego 7, luego **verde** (`e0bc86df`, [ci](https://github.com/P35C4D0R4B1050/BabelHub/actions/runs/44932437329)). Causas raíz: permisos de ejecución de dobles y scripts, prueba de tiempos del LLM, enlaces de la hoja maestra en SpecCheck, prueba de arranque de imágenes con todos los secretos (opción 1, PostgreSQL desechable por arquitectura), semillas falsas del historial en `.gitleaksignore`.
- Entregado en la misma tanda: NOT-02 (`pedido-confirmado` y `libro-digital-disponible`), índice infinito (Done), perfil realista a 2 000 sesiones (2+1 CPU las sostiene), y decisiones DD-24 y DD-25 (nombre y correo de todo usuario).

## 2026-11-21 (tarde) — Paquete de evidencias del Sprint 2: OpenAPI interactivo, hoja maestra, paneles k6 y perfil realista

### Cubierto
- **Interfaz interactiva de OpenAPI (Scalar):** `/documentacion` en cada Api (solo `Development`, recursos embebidos, sin CDN); verificada en navegador real. Paquete `Scalar.AspNetCore` 2.19.0 autorizado por la persona (DD-23, confirmación directa «Sí, autoricé»).
- **Hoja maestra del Sprint 2:** `docs/07-proceso-de-desarrollo/07-evidencias-sprint-2.md` (rúbrica → métrica, umbral, resultado y enlace a cada evidencia); `progress/evidencias/README.md` corregido y con una sola cifra vigente a 2 000 usuarios (10,1 s con 4+2 CPU).
- **k6:** panel HTML por escenario (`progress/evidencias/k6/2026-11-21-paneles/` y `-paneles-1cpu/`) y **perfil realista** de 2 000 sesiones con tiempo de reflexión (`tests/load/escenarios/perfil-realista.js`; series `-perfil-realista` y `-repeticion`). Resultado con el dimensionamiento de 1 OCPU (DD-17): ≈ 1 000 sesiones sostenidas, 1 500 en el límite, 2 000 no sostenidas. Solo se midió el Inventario; la ruta del E-commerce no entró.
- **README** corregido («Qué falta» y verificaciones). Estado de `main`: `50b8c880`, sin `push`.
- **Perfil realista a 2 000 sesiones con más recursos (fundido en `e0978d42`):** 0,6+0,4 CPU (DD-17, sin cambio), 1,0+0,5 y 1,5+0,75 no cumplen; **2+1 CPU sí** (201 y 313 ms en dos corridas, mínima probada). La corrida completa previa usó credenciales de 14 min (el Inventario no admite más de 15): corregido en DOC-1 §6d. Solo Inventario medido.

## 2026-11-20 — Biblioteca digital (ECO-8a a 8d), evidencias OAS-1, K6-1 y DOC-1, y `/health/ready`

### Cubierto
- **Carril biblioteca (nuevo):** ECO-8 hasta T24 (esquema y rol `rol_biblioteca`, adaptador S3 con enlaces SigV4, trabajos, pantallas); T25 (E2E) espera el host compuesto.
- **Carril de evidencias (nuevo):** OAS-1 (OpenAPI por servicio), K6-1 (escenarios, entorno limitado y corrida oficial en `progress/evidencias/k6/2026-11-20/`), DOC-1 y el reporte HTML de cobertura; dos correcciones reales (pool de Npgsql y 503 con la base caída).
- **Carril A:** `/health/ready` detecta la caída de la base en los dos servicios. **Carril B:** fachadas y lecturas de Pedidos (ECO-6f).
- Validación de `main`: compilación sin advertencias, SpecCheck 0 avisos, unitarias 5376 y 2368, arquitectura 243, integración 6954 de 6966; 12 fallos: 2 flaky que pasan sueltos, 2 de imágenes por contención de Docker, y 8 reales repartidos a sus dueños (4 pruebas por fixtures con contraseña o clave literal, composición de la biblioteca, Dockerfiles distintos, prueba de reservas con el tope nuevo).

## 2026-11-21 (cierre) — Integración final de `main` y estado de las specs

### Cubierto
- **Integración completa final:** 7601 de 7603 en 20 min (solo `HumoDeImagenes` ×2, conocido); escáner de secretos limpio; build `-warnaserror` 0 avisos; SpecCheck 0 avisos; unitarias 5437 y 2380; arquitectura 244. Tareas 547 de 611.
- **Specs en `Done` (7):** biblioteca, carrito, catálogo, devoluciones, reclamaciones, pedidos y traslados. Las demás quedan «Converge salvo …» con la reserva declarada (detalle en `progress/bloques.md`, «Estado de cierre»).
- **Decisiones de la persona registradas:** DD-16 (topes 20/30), DD-17 (dimensionamiento VM-INVENTARIO y memoria apagada), DD-18 (RNF02: demostrar con el entorno sin tope de CPU y declarar el techo de la VM), DD-19 (ronda extra única), DD-20 (un no recogido no se entrega; 360 h), DD-21 (D5 y D9 de Devoluciones), DD-22 (sin operador logístico real: simulador).
- **Cambios de fondo de las últimas rondas:** adaptador real de textos legales, marcas de dispositivo en el ingreso web (D1), orden global de receptores de la cancelación, línea digital reembolsada fuera de la anulación, enmiendas de spec del Carrito y de selección de fuente, menú por rol, WhatsApp de Pagos vía `INotificador`.

## 2026-11-21 (madrugada) — Cuarta integración completa de `main` y convergencia de specs

### Cubierto
- **Integración completa de `main` (5bb0a9ac):** 7519 de 7523 en 22 min; escáner de secretos limpio; unitarias 5428 y 2376; arquitectura 244; SpecCheck 0 avisos. Los 4 fallos son conocidos o flaky: `HumoDeImagenes` ×2 (Docker), `CargaDelAcumulado` y el escáner por tiempo (pasan solos).
- **Verificación (`verificacion.md`):** biblioteca **Converge**; carrito **converge salvo D-02**; catálogo, reclamaciones, pagos, pedidos y web-base **convergen salvo** Menores, partes humanas o externos (T37/T38 B05, WhatsApp Evolution/n8n, D9/D10) con motivo y dueño; devoluciones y envíos siguen «No converge» (V cierra su ronda); A re-verifica las specs de Inventario, seguridad, auditoría e identidad.
- **Cambios de fondo en esta ronda:** adaptador real de versiones de textos legales sobre Identidad (A), orden global de los receptores de la cancelación de cuenta (V), línea digital reembolsada excluida de la anulación y E2E de compra a domicilio y solo digital (B), WEB-02 menú por rol y PAG-22 WhatsApp vía `INotificador` (C), enmienda de topes del Carrito aprobada por excepción única (DD-19).

## 2026-11-20 (tarde) — Serie final de k6 y decisión sobre RNF02 (DD-18)

### Cubierto
- **Evidencias:** serie final sobre `main` con DD-17: RNF02 no se cumple con 0,6/0,4/512m (p95 ≈ 900 ms a 50 usuarios; 1,4 s en rampa); cumplen checkout (< 3 s), sobreventa (AC01), límites y resiliencia; a 2 000 usuarios p95 16,9 s y 51 % de errores (mejor 4/2 CPU: 10,1 s sin errores). DOC-1 §6b-bis e informe final de la rúbrica actualizados.
- **Decisión de la persona (DD-18, opción A):** RNF02 se demuestra con el entorno de prueba sin tope de CPU de la VM (1/1 CPU) y se declara con honestidad que en la VM de 1 OCPU compartida no se sostiene a 50 usuarios; D05 y las specs se conservan, la memoria de 5 s sigue apagada. Hallazgo cerrado como «declarado con evidencia».
- **Fundido en `main`:** carril B (ECO-6g escrito), C (renovación de la sesión de WEB-2), A (cierre de ronda 1) y el relevo del carril V (sesión nueva).

## 2026-11-20 (mediodía) — Decisiones de la persona sobre RNF02 (DD-17)

### Cubierto
- La memoria de 5 s de referencias del Inventario queda **apagada** (D05 y la spec no cambian). Dimensionamiento de VM-INVENTARIO adoptado: Inventario 0,6 CPU y 512 MB, PostgreSQL 0,4 CPU (`infra/docker-compose.inventario.yml`, `docs/02/07` con nota fechada: es un cambio de decisión de dimensionamiento, no de una spec aprobada). Hallazgos de RNF02, dimensionamiento y memoria cerrados; el de costo por solicitud queda «en curso» hasta la rampa final con ese reparto.

## 2026-11-20 (mañana) — E2E de compra y pedido, optimización del Inventario y tercera integración

### Cubierto
- **Carril B:** ECO-6f cerrada con reservas (T33–T36 y T41 como «ECO-6g»); E2E `pedidos/compra-y-pedido.spec.ts` 9/9 (Chromium escritorio y teléfono, WebKit; axe AA); correcciones reales: `IConfirmacionDeCompra`, `ICuentaDelPagador`, lista de sucursales, clave de reembolso (Guid «N»/«D») y simulador.
- **Carril de rendimiento (nuevo):** sentencias por solicitud del Inventario ~31 → ~18 (~12 con la memoria opt-in apagada, D05), sin mejora consistente de capacidad con 0,3 CPU (RNF02 sigue sin cumplirse; cuello = CPU del servicio); decisión de dimensionamiento de la persona pendiente (doc 09 §6c).
- Integración completa de `main` (1d92fe25): 7161 de 7165 en 21 min; escáner de secretos limpio. Fallos: `HumoDeImagenes` ×2 (conocidos), una prueba de carrera de biblioteca que pasa sola y una copia de adaptadores sin espejar (carril de rendimiento, en corrección).

## 2026-11-20 (madrugada) — Segunda integración de `main`, cobertura y COMP-1

### Cubierto
- **Evidencias:** reporte de cobertura completo en `progress/evidencias/cobertura/` (E-commerce 95,5 % Dominio+Aplicación y 94,6 % total; Inventario 95,7 % y 93,7 %; umbrales 85/70 cumplidos). **A:** COMP-1 (auditoría y claves de Identidad en el host del E-commerce). **B:** 45 pruebas de pantallas y concurrencia de Pedidos.
- Integración completa de `main` (4aa7c568 más cobertura): 7086 de 7098 en 31 min; escáner de secretos limpio. De los 12 fallos: `HumoDeImagenes` ×2 (conocidos, Docker), `CargaDelAcumulado`, `ArranqueSeguroEcommerce` y el escáner por tiempo (pasan solos) y 7 reales repartidos: OpenAPI publicado desactualizado y `CorreoDeProduccion` con reloj fijo (A), copia de `ManejoDeErrores` y rutas/composición de Pedidos del cliente (B, 4 casos), carrera de concesión simultánea de la biblioteca (23505, biblioteca).

## 2026-11-19 (noche) — Tope de reserva de 20 unidades por línea y 30 líneas

### Cubierto
- **Carril B:** decisión de la persona aplicada en `LimitesDeReservas` del Inventario (también acota cerrar y liberar), carrito (30 líneas, 20 unidades, fusión, CHECK de la base, controlador), Pedidos y Envíos (lotes de 30); registrado en DD-16 y residual de 600 unidades en AM01. Sin tocar specs aprobadas.
- Validación de `main` tras la fusión: compilación sin advertencias, SpecCheck 0 avisos, unitarias 5208 y 2364, arquitectura 238 (integración filtrada de reservas, carrito, envíos y migrador en verde, según B).

## 2026-11-19 — COMP-1 y COMP-2 de A; devoluciones, pedidos y pagos avanzan

### Cubierto
- **Carril A:** COMP-1 completo del E-commerce (`Program.cs` compone plataforma, Auditoría, Identidad, Catálogo y Carrito reales, sesión web, correo y Turnstile; en la parte 2 Pagos con la ráfaga de avisos de la pasarela y la exención SEG-18, Envíos, Reclamaciones y Pedidos) y **COMP-2: migrador de esquemas y roles de PostgreSQL** (modo `migrar` en el Api de cada servicio, idempotente, falla cerrado, integrado en `desplegar.sh`); lista blanca de auditoría y motivos de alerta de Pedidos.
- **Carril B:** ECO-6c (T14–T22) y T23 de `ecommerce-pedidos`.
- **Carril C:** Pagos T30 (13 pruebas contra PostgreSQL, con la corrección real de AC-PAG-03), T31 parcial, T33–T35 y correcciones de rutas y cabeceras; ECO-11 completo.
- **Carril V:** ECO-10a y ECO-10b (T01–T11 de `ecommerce-devoluciones`).
- Integración completa de `main` antes de los últimos merges: 6228 de 6233; fallos: 2 reales de Reclamaciones (en manos de C) y 3 por contención o Docker (reservas con reloj y humo de imágenes).

### Pendiente
- Fallos de Reclamaciones en el host de pruebas (C); ECO-5e, ECO-6d, ECO-10c, parte restante de envíos; E2E con el host sembrado (Node ya instalado); biblioteca digital sin dueño; notificaciones NOT-2 a NOT-4; `spec-verify` de las specs completas.

## 2026-11-19 — Plataforma para Pagos y Pedidos, ECO-5d, ECO-6b, ECO-9c a 9e y ECO-11 completo

### Cubierto
- **Carril A:** PLA-X y PLA-Y: catálogos compartidos (motivos `carrito-*` y `envio-*`, claves del detalle de envíos, plantillas de correo de Pagos, Envíos y Pedidos), `IClienteDelInventario` con consultar, extender, confirmar y cerrar reservas, políticas de límite de Pedidos y de ráfaga de avisos de la pasarela, 7 acciones de auditoría de Pedidos; COMP-1 del Inventario (composición de `Program.cs` con credenciales y cuentas de base de datos propias por host).
- **Carril B:** ECO-6a y ECO-6b (T01–T13 de `ecommerce-pedidos`), contratos de Pedidos para Envíos (T07a) y para Devoluciones (T07b) y los adaptadores reales de Pedidos sobre `CerrarReserva`, límites y auditoría.
- **Carril C:** ECO-5d (T24–T29 de Pagos: esquema, simulador, llaves por entorno, acciones MVC, reservas del pago) y **ECO-11 completo** (reclamaciones: esquema, anonimización, composición, trabajos, pantallas y documentos; 114 pruebas contra PostgreSQL).
- **Carril V:** ECO-9c a ECO-9e parcial (T12–T19, T21a, T23, T25 y T13 de `ecommerce-envios`: límites, simulador, operador nulo, trabajos de fondo, Api y Web, matriz de roles, privacidad, `RestReservasDelEnvio`).
- Integración completa de `main` (con 20 merges): 5884 de 5891; fallos reales por corregir: dos de C por `GET /checkout/pago` ambiguo en el host de pruebas de la política de textos pendientes (y la CSP de la pasarela), el fixture de Pagos con contraseña literal y otras dos flaky que pasan sueltas (auditoría y humo de imágenes).

### Pendiente
- Correcciones de C; COMP-1 del E-commerce (A); ECO-6c en adelante (B); devoluciones y T20/T22 de envíos (V); biblioteca digital (ECO-8) sin dueño; E2E (Node instalado, falta el host compuesto).

## 2026-11-19 — IDE-9 cierra Identidad; carrito completo salvo el E2E; ECO-6a y ECO-9b

### Cubierto
- **Carril A:** IDE-8 completo (T44 resto y T45: audiencia y alcance del contexto de lectura, `IContextoDelAsistente` sin caché, puertos de sesión) e IDE-9 (T46–T50: integración con el Inventario, carga de Argon2id, tiempos uniformes, concurrencia y custodia de la clave): **`identidad-seguridad` queda con sus 50 tareas**. Cuatro variables `BABELHUB__Identidad__*` nuevas en `infra/secretos-requeridos.txt`, obligatorias en producción.
- **Carril B:** T25b y T26c del carrito con el Inventario real y los puertos de límites, auditoría, marca de cancelación, consentimiento y alertas reales (`ecommerce-carrito-checkout` 36 de 37; el E2E T36 espera la composición de los `Program.cs`); ECO-6a (T01–T06 de `ecommerce-pedidos`: Dominio de Pedidos).
- **Carril V:** ECO-9b (T06–T11 de `ecommerce-envios`: casos de uso de envíos).
- Validación de `main` con IDE-9: unitarias 4395 y 2334, arquitectura 230, SpecCheck 23, CoverageCheck 12, integración 5447 de 5449 (las 2 pasan sueltas: una de restauración con `tar` y el escáner de secretos, contención), gitleaks limpio.

### Pendiente
- PLA-X, COMP-1 e NOT-2 a NOT-4 (A); ECO-6b en adelante (B); ECO-5d en adelante y ECO-11c y 11d (C); ECO-9c a 9e (V); `spec-verify` de las specs que se completen (identidad, catálogo y carrito, entre ellas); WEB-2.

## 2026-11-19 — IDE-7 e IDE-8 (primera entrega) fundidos; `main` completamente verde

### Cubierto
- **Carril A:** IDE-7 (T27, T34, T36–T40): adaptadores de correo (MailKit 4.18.2, cola cifrada, tope de 24 al día), adaptadores de `identidad.registros` con una exención estrecha y registrada a la regla de Auditoría, adaptadores del Inventario y trabajos de fondo de Identidad. IDE-8, primera entrega: verificador de credenciales del Inventario con caché de 5 minutos que falla cerrada (T41), composición real de Identidad con guarda de producción, `IEstadoDeCuenta` real, puertos de la web, endpoints de claves (`/.well-known/jwks.json`, `/revocaciones`, rotar y comprometida) y prueba negativa de que ningún endpoint JSON expone la credencial. T42 y T43 con alcance reducido por `AGENTS.md` §3.9 (decisión IDE-8b).
- Corrección de integración: el catálogo de eventos de seguridad del Inventario vuelve a ser copia idéntica del del E-commerce (4 motivos de límite).
- Validación de `main` con los carriles quietos: unitarias 4302 y 2334, arquitectura 229, SpecCheck 23, CoverageCheck 12, **integración 5364 de 5364**, gitleaks limpio.

### Pendiente
- Resto de T44 y T45 (A); `web-base` WEB-2 (pantallas de ingreso, registro, cuenta y administración) ya con la composición de Identidad disponible; pedidos (B), pagos y reclamaciones (C), envíos (V); bloque de composición de los `Program.cs`; verificaciones FIN-1 de las specs que se completen.

## 2026-11-19 — IDE-6, NOT-1, ECO-9a y los contratos de Pedidos para Envíos

### Cubierto
- **Carril A:** IDE-6 (T26, T28–T33 de `identidad-seguridad`): credenciales de servicio, topes, clasificación de correos, Argon2id con Konscious 1.3.1 (MIT), emisor ES256, esquema y roles de acceso y repositorios sobre PostgreSQL; cupo de códigos por origen corregido tras la revisión. NOT-1 (T01–T05 y T07 de `transversal-notificaciones`): Dominio, contrato, solicitar y cancelar avisos y desactivación de números. Hallazgo Importante abierto: la lista de contraseñas comunes está generada por patrones y la persona debe elegir la fuente de una lista vetada antes de FIN-1.
- **Carril B:** contratos de Pedidos para Envíos (T07a de `ecommerce-pedidos`): `IDespachoDeEnvios`, `IProgresoDeEnvios`, `IContactoDeEntregaDelPedido` e `IPedidosParaEnvios`, con dobles que fallan cerrado y guarda de producción.
- **Carril V:** ECO-9a (T01–T05 de `ecommerce-envios`): Dominio de envíos; planificación terminada (las 20 specs con plan `Planned`).

### Pendiente
- IDE-7 e IDE-8 (A), resto de pedidos (B), ECO-9b a 9e (V), pagos y reclamaciones (C); la integración completa de `main` con todo esto.

## 2026-11-19 — IDE-5 y los planes de `web-indice-infinito` y del asistente

### Cubierto
- **Carril A:** IDE-5 (T23–T25 de `identidad-seguridad`; T27 y T34 parciales en la capa de Aplicación): cancelación de cuenta (ejecución), cierre y baja, rotación de la clave de firma; alertas de traslados con logística como destinataria (cierra TRA-22); reaplicación y restauración; enviador de correo con cuota y tope de 24 al día. Falta la Infraestructura de correo (SMTP, tablas, plantillas) y los adaptadores de registros aislados.
- **Carril V:** plan de `web-indice-infinito` aprobado (`Planned`, 15 tareas, ECO-13a a 13c) y plan de `asistente-compra-ia` redactado (24 tareas) con los cinco valores ratificados por la persona el 2026-11-19; falta una ronda 4 acotada de `arch-reviewer` para aprobarlo.
- Validación de `main` (sin IDE-5): unitarias 3744 y 2320, arquitectura 217, integración 4975 de 4976. Con IDE-5 apareció un fallo real de la guarda de secretos en `ServiciosDeLaCuenta.cs`, a cargo del carril A.

### Pendiente
- Fallo de secretos de A; IDE-6 y NOT-1; ECO-6 de pedidos, ECO-5d, ECO-11c y 11d; infraestructura de correo; la autorización de la ronda 4 del asistente.

## 2026-11-19 — IDE-4, SEL-39, ECO-11b y las pruebas de verificación fundidos

### Cubierto
- **Carril A:** IDE-4 (T18–T22 de `identidad-seguridad`): cuenta del cliente, administración de usuarios, cambio de correo con congelamiento, solicitudes sobre datos personales y solicitud de cancelación; incluye lo que esperaba el carrito (cliente del Inventario con consulta previa, reservar y liberar; políticas de límite y 7 acciones de auditoría de Carrito; marca de cancelación con bloqueo y consentimiento de WhatsApp por huella).
- **Carril B:** SEL-39 corregido en el código (alcance antes que la forma de la clave, con su evento).
- **Carril C:** ECO-11b (T07–T12 de reclamaciones) y 21 motivos de alerta de Pagos y Reclamaciones registrados en el catálogo cerrado.
- **Carril V (verificación FIN-1):** `verificacion.md` de seis specs (una converge, cinco no por huecos con dueño; ver `progress/coordinacion.md`) y las pruebas que cerraban SEL-03, SEL-24, AC-AUD-09, AC-AUD-25 y AC-SEG-23.
- Integración de `main` antes del último merge: 4942 de 4945; dos fallos reales (texto en español de los 21 motivos nuevos en `PresentadorDeAuditoria`, a cargo de C, y una cadena de conexión con contraseña literal en `PostgresDelCarritoFixture`, a cargo de B) y el escáner de secretos por contención.
- Un conflicto semántico al fundir IDE-4 (listas de políticas de límite de A y de C) se resolvió a mano en una lista unión: el recuento de motivos de límite pasa a 29.

### Pendiente
- Correcciones de B y C; IDE-5 (A); la corrida de integración de `main` con IDE-4; ECO-6 de pedidos, ECO-11c y 11d, ECO-5d; planes de `asistente-compra-ia` y `web-indice-infinito` (ratificación de cinco valores por la persona).

## 2026-11-19 — ECO-4c, ECO-4d y ECO-5c fundidos; tablero de coordinación

### Cubierto
- **Carril B:** ECO-4c (T14–T23) y ECO-4d (T24–T32 y T37 de `ecommerce-carrito-checkout`): casos de uso de la reserva y el pago del checkout, esquema y repositorios sobre PostgreSQL, adaptador del Inventario (disponibilidad por lotes y sucursales), composición con guarda de producción, presentación del carrito y checkout, trabajos de fondo y las pruebas de integración del carrito con PostgreSQL real (198 de 198). T25b y T26c esperan al carril A.
- **Carril C:** ECO-5b y ECO-5c (T07–T23 de `ecommerce-pagos`) y ECO-11a (reclamaciones), con revisiones sin Bloqueantes.
- **Coordinación:** nuevo `progress/coordinacion.md` con el protocolo de los carriles, el encargo de cada uno, la matriz de qué espera a quién y los turnos de integración. La sesión de planificación de ECO-7 pasa a ser el carril V (verificación FIN-1 de las specs completas); el carril A se reactiva con las piezas que esperan B y C, IDE-4 e IDE-5.
- Validación de `main`: unitarias 3638 y 2309, arquitectura 217, CoverageCheck 12, gitleaks limpio, integración 4857 de 4871 (las 14 pasan sueltas: 13 de bloqueos asesores y el escáner de secretos, contención de la máquina).

### Pendiente
- T25b y T26c del carrito (carril A), ECO-5d en adelante, ECO-11b a 11d, IDE-4 e IDE-5, ECO-6 de pedidos, `spec-verify` de las specs completas; el disco de Docker en D crece rápido (quedan ~38 GB libres).

## 2026-11-18 — ECO-4b fundido (casos de uso del carrito)

### Cubierto
- **Carril B:** `ecommerce-carrito-checkout` T07–T13 (ECO-4b): contratos públicos y dobles provisionales con guarda de producción, alcance y posesión, casos de uso del carrito para visitante y cuenta, edición con checkout vivo con reapertura de la intención de pago, fusión idempotente del carrito de visitante y retiro de ediciones dadas de baja; revisiones sin Bloqueantes. Integración 4402 de 4404 (las 2 pasan solas).
- **Decisiones delegadas:** la persona delegó en el carril B la coordinación de los hallazgos de `ecommerce-carrito-checkout` (mensaje del 2026-11-18); sus 15 decisiones están en `progress/decisiones-delegadas.md` y no modifican ninguna spec ni plan aprobados. Ratifican, entre otras, los topes técnicos del carrito (50 líneas, 99 unidades por línea), la lectura de `retiro_pendiente` de CAT-18 y los nombres del contrato público de Carrito.

### Pendiente
- ECO-4c (T14–T23); la T26b de B necesita del carril A políticas de límite y acciones de auditoría de Carrito, la lectura de la marca de cancelación y el consentimiento de WhatsApp en Identidad, y el cliente del Inventario con consulta previa, reservar y liberar (carril A pausado).

## 2026-11-18 — ECO-2d, ECO-5a y ECO-4a fundidos, Docker reubicado

### Cubierto
- **Carril C:** ECO-2d (T24–T28 de `ecommerce-catalogo`): pantallas públicas y de administración del catálogo, pantalla de ingresos, documentos y E2E de Playwright (escritos, sin ejecutar); ECO-5a (T01–T06 de `ecommerce-pagos`): Dominio de importe, intención, intento, compensación, reembolso, resultado y token.
- **Carril B:** ECO-4a (T01–T06 de `ecommerce-carrito-checkout`): carrito, fusión de visitante, checkout con estados, aceptaciones por texto y retención del contacto; contratos públicos del carrito y puertos (T07) y dobles provisionales con guarda de producción (T08). T09–T13 escritos en su rama (integración y revisiones de ECO-4b en curso).
- **Entorno:** el disco de Docker (106,7 GB) se movió de `C:` a `D:\Docker\wsl\disk` con un enlace en la ruta original; `C:` pasó de 0,1 a 106,8 GB libres y Docker conserva sus 32 imágenes y 195 volúmenes. Identidad de git configurada solo en el repositorio.
- Validación de `main` (antes del último merge): unitarias 2484 y 2295, arquitectura 193, SpecCheck 23, CoverageCheck 12, integración 4449 de 4453 (las 4 pasan sueltas) y gitleaks limpio. Tras el merge de ECO-4a: unitarias del E-commerce 2735 y arquitectura 211.

### Pendiente
- ECO-4b a ECO-4e y ECO-5b en adelante, ECO-11 en curso (reclamaciones, T01–T02 hechos), IDE-4 sin empezar (carril A pausado), `spec-verify` y los pendientes de la persona.

## 2026-11-18 — Quinta ola, segunda parte y planes de comercio (fundidos a `main`)

### Cubierto
- **Carril B:** `inventario-ajustes` completo (T01–T18: Dominio, Aplicación, esquema, depuración de 2 años, Api de ajustes, ingresos y reingreso con `pedidoId` obligatorio, D19) e `inventario-traslados` completo (T01–T26: transiciones, generación y cancelación de traslados por pedido, repositorios, buzón de salida, trabajos de fondo y Api). El Inventario queda sin tareas pendientes salvo `spec-verify` y la composición de los `Program.cs`.
- **Carril A:** IDE-2 e IDE-3 (T11–T17 de `identidad-seguridad`): cuota de correo, cliente del Inventario ampliado (pendientes y baja del titular), contrato público de Identidad, registro con verificación y reenvío de código, ingreso en dos pasos, sesiones y recuperación; umbral de 50 bloqueos por 15 minutos confirmado por la persona.
- **Carril C:** ECO-2b y ECO-2c de `ecommerce-catalogo` (T07–T23: casos de uso, conciliación de ingresos, adaptador del Inventario, almacén S3 con verificador por rangos, Api, trabajos de fondo); ECO-2d (T24–T28) commiteada en su rama, con revisiones sin Bloqueantes ni Importantes.
- **Planes aprobados (`Planned`) por delegación:** `ecommerce-carrito-checkout` (37 tareas), `ecommerce-pagos` (38), `ecommerce-pedidos` (42), `ecommerce-biblioteca-digital` (26), `ecommerce-envios` (25), `ecommerce-devoluciones` (34) y `ecommerce-libro-reclamaciones` (23). Solo faltan por planificar `asistente-compra-ia` y `web-indice-infinito`. ECO-3 necesitó 5 rondas y ECO-7, 4, autorizadas por la persona.
- Un choque semántico al fundir (los dobles de `IClienteDelInventario` del catálogo no tenían las operaciones del titular de identidad) se corrigió en `723a071`.

### Pendiente
- Validar `main` con la integración completa (se cortó dos veces con los carriles corriendo a la vez); ECO-4a de carrito (T01–T06) en curso; ECO-2d por fundir; IDE-4 sin empezar (carril A pausado a petición de la persona).

## 2026-11-17 — Quinta ola, primera parte (fundida a `main`)

### Cubierto
- **Carril A (piezas de plataforma):** puerto y cliente REST del Inventario ampliado (registrar edición, disponibilidad por lotes, ingresos), contenedor `portadas` y credenciales del almacén del catálogo, motivos de alerta nuevos en `Shared` y evento propio del bloqueo por textos pendientes (solo aditivo). T11–T14 de identidad siguen en su rama, sin revisión.
- **Carril B:** INV-2d (T28–T33): `inventario-seleccion-fuente` queda con sus 33 tareas (falta `spec-verify`): pruebas de reversión de límites, carreras y ciclo de vida con reloj controlado, abuso de credencial y de entrada, contrato REST de reservas con un E-commerce simulado e inventario de privacidad con la clave de bajas fuera de la rotación.
- **Carril C:** ECO-2a (T01–T06 de `ecommerce-catalogo`): ISBN, precio, tipos y límites de archivo, licencia digital, agregado obra-edición, contratos y puertos, autorización y validadores.
- Validación de la fusión: unitarias 1985 y 1923, arquitectura 183, SpecCheck 23, CoverageCheck 12, integración 3744 de 3746 (las 2 pasan sueltas), gitleaks limpio.

### Pendiente
- En curso: IDE-2/IDE-3 (A), INV-3 (B), ECO-2b/2c (C) y la planificación de carrito, pagos y pedidos en su ronda 3.

## 2026-11-15 — SEG-C, IDE-1, INV-2a/2b/2c y WEB-1e (fundidos a `main`)

### Cubierto
- **Carril A:** SEG-C (T27b y T29): `transversal-seguridad` queda con sus 30 tareas, pendiente de `spec-verify`; IDE-1 (T01–T10 de `identidad-seguridad`): Dominio de contraseñas con Argon2id, usuario y cuatro roles, TOTP y códigos de respaldo, desafío y renovación opaca, código de un solo uso, aceptación de textos y consentimiento de WhatsApp, solicitudes sobre datos y calendario hábil, cancelación diferida, dispositivos y cambio de correo, ciclo de vida de la clave de firma.
- **Carril B:** `inventario-seleccion-fuente` T01–T27 (INV-2a, 2b y 2c): estrategia de selección de la fuente más cercana, reserva atómica sobre stock bloqueado, esquema con restricciones, repositorios PostgreSQL, prueba de concurrencia sin sobreventa ni interbloqueos, idempotencia, límites dentro de la transacción, reemplazo con tope de tres, confirmar, extender, liberar, cerrar, consulta previa, puertos para traslados y cancelación de cuenta, bajas de titular con identificador anónimo, trabajos de fondo con actor de sistema y endpoints. Faltan INV-2d (T28–T33).
- **Carril C:** `web-base` WEB-1e (T36–T39): política de acceso con textos pendientes, middleware con validación de arranque, pantalla mínima de aceptación e integración con la sesión real.
- Integración: dos choques de arquitectura entre carriles resueltos en `main` sin debilitar reglas (el manejador del token de salida y el fijador de actor); se quitó una excepción provisoria a `ReglasDelTokenDelUsuarioTests`.
- Validación de la fusión: unitarias 1812 y 1917, arquitectura 173, SpecCheck 23, CoverageCheck 12, **integración 3645 de 3645**, gitleaks limpio. Tareas hechas de las specs planificadas: 178 de 347.

### Pendiente
- Decisión de negocio sobre el tope de unidades por reserva (100 por línea, 50 líneas); un bloque de composición de los `Program.cs`; motivos de alerta que faltan; las nueve specs sin plan (carrito y checkout, pagos, pedidos, biblioteca digital, envíos, devoluciones, reclamaciones, asistente e índice infinito).

## 2026-11-15 — AUD-5 cierra `transversal-auditoria` (fundido a `main`)

### Cubierto
- **Carril A:** T21 y T28–T31: DTOs y constantes de auditoría en `Shared` (D18: el catálogo de contratos añade 4 operaciones del E-commerce y cambia los tipos de 3 del Inventario), endpoints de los dos servicios, un único punto que registra el acceso denegado, presentador de la web, prueba de punta a punta con los dos servicios y PostgreSQL real, y documentación. Redactor con el hash de sesión y el cuerpo de un borrador; el cliente de auditoría ya no consume el token del usuario directamente. **`transversal-auditoria` queda con sus 31 tareas marcadas** (falta `spec-verify` y la composición en cada `Program.cs`, hallazgo Importante abierto).
- `transversal-seguridad` T27b y T29 quedan desbloqueadas (bloque SEG-C).
- Validación de la fusión: unitarias 1572 y 1507, arquitectura 169, SpecCheck 23, CoverageCheck 12, integración 3195 de 3198 (los 3 fallos eran del avance de A, ya corregidos y comprobados: auditoría 280/280), gitleaks limpio.

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
