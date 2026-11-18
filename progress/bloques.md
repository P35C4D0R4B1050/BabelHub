# Bloques de trabajo y carriles

Aplica [`docs/07-proceso-de-desarrollo/05-desarrollo-por-bloques.md`](../docs/07-proceso-de-desarrollo/05-desarrollo-por-bloques.md) (decisión del 2026-11-14). Cada bloque tiene su nivel de rigor (**C** crítico, **E** estándar, **B** bajo; el de las tareas es el del bloque salvo que se indique, y siempre sube al de la parte más riesgosa), sus dependencias y su entregable. Se actualiza al abrir y al cerrar cada bloque. Los bloques de las specs sin plan (`INV-*`, `ECO-*`) se detallan al planificarlas.

Estado: ☐ pendiente · ▶ en curso · ☑ hecho (fundido a `main`).

## Carril A — Transversales

| Bloque | Contenido | Tareas | Nivel | Depende de | Estado |
| :--- | :--- | :--- | :---: | :--- | :---: |
| **PLA-R1** | `transversal-plataforma`: endurecimiento de VM-AUX, espacio local reservado, custodia de la clave y ensayo de descifrado, `tools/RestauracionCheck` | T26, T27, T28, T30 | C | T25 (hecha) | ☑ |
| **AUD-1** | `transversal-auditoria`: Dominio (detalle seguro, catálogo de acciones, actor, anonimización, alertas, acumulado de límites) | T01–T06 | C | — | ☑ |
| **AUD-2** | Contratos de registro, consulta y alerta, puertos de Identidad y Reservas y los catálogos de acciones | T07, T08, T09, T11–T16 | C | AUD-1 | ☑ |
| **AUD-3** | Implementación de los puertos de `Plataforma`, consultas unificadas, agrupar y enviar alertas, depuración y bajas del Inventario | T10, T17–T20 | C | AUD-2 | ☑ |
| **SEG-C** | Cierre de `transversal-seguridad`: T27b y T29 (acumulador persistente) y `spec-verify` | T27b, T29 | C | AUD-3 (T10, T18) | ☑ (30/30 tareas; falta `spec-verify` en FIN-1, que debe comprobar el tope de 24 correos con el adaptador de Identidad) |
| **AUD-4** | Migraciones, roles, función de depuración, repositorios de solo inserción, cliente del Inventario, trabajos de fondo | T21–T27 | C | AUD-3 | ☑ (T21 hecha en AUD-5 con el catálogo de contratos ampliado, D18) |
| **AUD-5** | Endpoints, contrato web, integración de punta a punta y documentación | T28–T31 | E | AUD-4 | ☑ (con T21; `transversal-auditoria` queda con todas las tareas marcadas, falta `spec-verify` en FIN-1 y componer la auditoría en cada `Program.cs`, hallazgo Importante abierto) |
| **IDE-1** | `identidad-seguridad`: Dominio (contraseñas, usuario y roles, segundo factor, desafío, códigos, textos, solicitudes, cancelación, marcas, clave de firma) | T01–T10 | C | — (puede ir en paralelo a AUD-1) | ☑ (AC-IDE-14, 17 y 38 solo con su parte de Dominio; hallazgo Importante) |
| **IDE-2** | Contratos de correo y de cliente del Inventario en `Plataforma` y contrato público de Identidad | T11, T12, T13 | C | IDE-1, AUD-2 | ☐ |
| **IDE-3** | Registro, ingreso en dos pasos, sesiones y recuperación | T14–T17 | C | IDE-2 | ☐ |
| **IDE-4** | Cuenta del cliente, administración de usuarios, cambio de correo, solicitudes de datos, cancelación (solicitud) | T18–T22 | C | IDE-3 | ☐ |
| **IDE-5** | Cancelación (ejecución), cierre y baja, rotación de la clave, puertos de Auditoría y de restauración | T23–T25, T27 | C | IDE-4, AUD-3 | ☐ |
| **IDE-6** | Credenciales de servicio, topes, clasificación de correos y la infraestructura de seguridad (roles de acceso, Argon2id, emisor ES256, repositorios) | T26, T28, T29, T30–T33 | C | IDE-5 | ☐ |
| **IDE-7** | Adaptadores de correo e Inventario, trabajos de fondo (vencimiento, descarte, rotación, bajas) | T34–T40 | C | IDE-6 | ☐ |
| **IDE-8** | Verificador del Inventario, endpoints y la cookie de sesión de la web | T41–T45 | C | IDE-7 | ☐ |
| **IDE-9** | Integración con el Inventario, carga de Argon2id, tiempos uniformes, concurrencia y custodia de la clave | T46–T50 | C | IDE-8 | ☐ |
| **NOT-1** | `transversal-notificaciones`: Dominio y contrato, solicitar y cancelar avisos, desactivación de números | T01–T07 | C | IDE-2 | ☐ |
| **NOT-2** | Envío, tope por número, recepción del Inventario y estado de entrega | T08–T11 | E | NOT-1 | ☐ |
| **NOT-3** | Infraestructura: migraciones, huella del número, n8n firmado, simulado y nulo, contacto de sucursal, flujo de n8n y Evolution | T12–T18 | C | NOT-2 | ☐ |
| **NOT-4** | Endpoints, integración con PostgreSQL y el motor de límites reales, contratos | T19–T25 | E | NOT-3 | ☐ |
| **PLA-R2** | `transversal-plataforma`: `restaurar.sh`, ensayo aislado, datos sintéticos, restauración con carga y verificador de la puerta PLA-11 | T29, T31–T34 | C | IDE-5, AUD-3, PLA-R1; spikes T01 y T05 (persona) | ☐ |
| **PLA-R3** | Trivy y SBOM, reescaneo semanal y flujo de lanzamiento | T35–T37 | E | — | ☐ |
| **PLA-R4** | Discos cifrados, medición de recursos, alertas de presupuesto y cuota, constancias y pruebas de contrato | T38–T42 | C/E | PLA-R2 | ☐ |

## Carril B — Inventario

| Bloque | Contenido | Nivel | Depende de | Estado |
| :--- | :--- | :---: | :--- | :---: |
| **INV-0** | Planificar en lote las cuatro specs (`spec-plan`, `spec-tasks`, puerta delegada): 102 tareas, las cuatro specs Approved y los cuatro planes Planned | C | ECO-0 (contratos) | ☑ |
| **INV-1a** | `inventario-fuentes` T01–T08: piezas de Plataforma (`IBloqueosAsesores`, `ReintentoPorInterbloqueo`, alcance de datos por transacción) y Dominio | C | INV-0; ECO-0; `transversal-auditoria` T07/T08/T12 (AUD-2) | ☑ |
| **INV-1b** | `inventario-fuentes` T09–T16: Aplicación (fuentes, rutas, existencias, `IMovimientosDeStock`, puertos) | C | INV-1a | ☑ |
| **INV-1c** | `inventario-fuentes` T17–T25: Infraestructura (RLS, migraciones), Api y pruebas. **Riesgo a cubrir (hallazgo del 2026-11-14 en `progress/hallazgos.md`):** el alcance de datos por transacción falla cerrado, así que sin actor (llamadas de servicio, trabajos de fondo, migraciones) las políticas de `stock` deniegan y devuelven 0 filas sin error; la Api y los trabajos deben fijar un actor explícito (integración o sistema), con pruebas contra PostgreSQL con el rol real | C | INV-1b | ☑ |
| **INV-2a** | `inventario-seleccion-fuente` T01–T10 y T19: Plataforma, Dominio, contrato, reservar, expiración compartida y primera prueba de concurrencia | C | INV-1c | ☑ |
| **INV-2b** | `inventario-seleccion-fuente` T11–T18: idempotencia, límites, reemplazo, confirmar, extender, liberar, cerrar y consulta | C | INV-2a | ☑ |
| **INV-2c** | `inventario-seleccion-fuente` T20–T27: puertos para otros módulos, receptores de la cancelación, trabajos de fondo y Api | C | INV-2b | ☑ |
| **INV-2d** | `inventario-seleccion-fuente` T28–T33: pruebas de integración y documentación (T32 y T33 de nivel E) | C/E | INV-2c | ☑ (`inventario-seleccion-fuente` 33/33; falta `spec-verify`) |
| **INV-3a** | `inventario-ajustes` T01–T09: Dominio y Aplicación | C | INV-1c, INV-2c | ☐ |
| **INV-3b** | `inventario-ajustes` T10–T18: Infraestructura, depuración de 2 años, Api, concurrencia | C | INV-3a; AUD-4 (conexión de depuración) | ☐ |
| **INV-4a** | `inventario-traslados` T01–T09: Dominio y Aplicación de las transiciones | C | INV-2c | ☐ |
| **INV-4b** | `inventario-traslados` T10–T17: puertos de `Reservas`, infraestructura y concurrencia | C | INV-4a | ☐ |
| **INV-4c** | `inventario-traslados` T18–T26: buzón de salida, trabajos de fondo, Api y pruebas | C | INV-4b; contrato del aviso de `transversal-notificaciones` (T20) | ☐ |

## Carril C — E-commerce y web

| Bloque | Contenido | Nivel | Depende de | Estado |
| :--- | :--- | :---: | :--- | :---: |
| **ECO-0** | Contratos REST entre el E-commerce y el Inventario y sus DTOs en `BabelHub.Shared` (43 operaciones v1); doble de «usuario actual» de prueba | C | — | ☑ |
| **ECO-1** | Planificar `web-base` (39 tareas) y `ecommerce-catalogo` (28 tareas): specs Approved y planes Planned | C | ECO-0 | ☑ |
| **WEB-1d** | `web-base` T21–T27 (T21 antes de T01): errores, diseño (Tailwind y daisyUI), layout, componentes de estado, textos legales, «Sorpréndeme» y descarga | B/E/C | ECO-1 | ☑ |
| **WEB-1a** | `web-base` T01–T09: módulo `Web`, Dominio (sesión, borrador, áreas) y Aplicación | C | WEB-1d (T21); dobles de Identidad, Auditoría y Catálogo | ☑ |
| **WEB-1b** | `web-base` T10–T15: Infraestructura de sesión, cifrado por host, token del usuario y cookie | C | WEB-1a; `ITokenDelUsuarioDeLaSesion` en `Application.Plataforma` (carril A, ya en main). T10–T15 hechas | ☑ |
| **WEB-1c** | `web-base` T16–T20: antiforgery, cabeceras y CSP, filtros de área y paso pendiente, pruebas de sesión y rutas | C | WEB-1b | ☑ |
| **WEB-1e** | `web-base` T36–T39 | C | WEB-1c | ☑ |
| **WEB-2** | `web-base` T28–T31: pantallas de ingreso, registro, recuperación, cuenta, administración, auditoría y alertas | C | IDE-3 a IDE-5, AUD-3 | ☐ |
| **WEB-4** | `web-base` T32–T35: E2E multinavegador y móvil, axe, rendimiento y usabilidad | E/C | pantallas de catálogo y compra | ☐ |
| **ECO-2a** | `ecommerce-catalogo` T01–T06: Dominio (ISBN, obra y ediciones, licencia, archivos), contratos y puertos | C | — | ☑ |
| **ECO-2b** | `ecommerce-catalogo` T07–T13: casos de uso, cerrado con dobles de auditoría | C | ECO-2a | ☐ |
| **ECO-2c** | `ecommerce-catalogo` T14–T23: Infraestructura, Api, matriz de autorización, integración con S3 e Inventario simulado, trabajos de fondo | C | AUD-4; ampliación de `IClienteDelInventario` y del almacén (carril A) | ☐ |
| **ECO-2d** | `ecommerce-catalogo` T24–T28: pantallas del catálogo y de la administración, documentos y E2E | B/C/E | WEB-1d | ☐ |
| **ECO-3** | Planificar `ecommerce-carrito-checkout`, `ecommerce-pagos` y `ecommerce-pedidos` | C | ECO-2 | ☐ |
| **ECO-4** | `ecommerce-carrito-checkout` (reservas e idempotencia) | C | ECO-3, INV-2 | ☐ |
| **ECO-5** | `ecommerce-pagos` (pasarela de pruebas de Culqi) | C | ECO-4; B05 (persona) | ☐ |
| **ECO-6** | `ecommerce-pedidos` | E | ECO-5 | ☐ |
| **ECO-7** | Planificar biblioteca digital, envíos, devoluciones y libro de reclamaciones | C | ECO-6 | ☐ |
| **ECO-8** | `ecommerce-biblioteca-digital` (archivos y URLs firmadas) | C | ECO-7 | ☐ |
| **ECO-9** | `ecommerce-envios` | E | ECO-7 | ☐ |
| **ECO-10** | `ecommerce-devoluciones` | E | ECO-9 | ☐ |
| **ECO-11** | `ecommerce-libro-reclamaciones` | E | ECO-7 | ☐ |
| **ECO-12** | `asistente-compra-ia` | C | ECO-6; B06 (persona) | ☐ |
| **ECO-13** | `web-indice-infinito` | B | WEB-1d | ☐ |

## Cierre (todos los carriles)

| Bloque | Contenido | Nivel | Estado |
| :--- | :--- | :---: | :---: |
| **FIN-1** | `spec-verify` de cada spec y paso a `Done` | C | ☐ |
| **FIN-2** | Despliegue: E2E con Playwright, carga con k6, Compose de Oracle y criterios de aceptación y metas de rendimiento | C | ☐ |

## Medición (se llena al cerrar cada bloque)

| Bloque | Tareas | Fecha de cierre | Duración | Notas (hallazgos, paradas, replanificación) |
| :--- | :---: | :--- | :--- | :--- |
| (antes de los bloques: `transversal-plataforma` T20–T25, una tarea por ciclo) | 6 | 2026-11-14 | ~1 jornada | Referencia: cada tarea con validación completa costaba 5 a 8 min solo de pruebas. |
| PLA-R1 + AUD-1 (carril A) | 10 | 2026-11-14 | ~6 h en paralelo con B y C | Revisión de nivel C: PLA-R1 dio 1 Bloqueante y 4 Importantes; AUD-1, 7 Importantes de seguridad y 1 de arquitectura; todos corregidos. Integración sola: 1768 pruebas, 11 min. |
| INV-0 (carril B) | 4 planes (102 tareas) | 2026-11-14 | ~6 h | La puerta delegada de 3 rondas no alcanzó: hicieron falta 6 (4.ª, 5.ª y 6.ª autorizadas por la persona). Reabrió las specs de selección (SEL-36/37/39) y ajustes (AJU-21/22). Pendiente de la persona: residual de reemplazos de 75 min. |
| ECO-0 + ECO-1 (carril C) | 6 tareas + 2 planes | 2026-11-14 | ~6 h | 43 contratos congelados; planes de `web-base` y `ecommerce-catalogo` también con rondas extra; decisión WEB-34 («B, acotar» y luego «B literal»); riesgo aceptado de los alcances de reservas y traslados. |
| AUD-2 (carril A) | 10 | 2026-11-14 | ~1,5 h | 83 acciones de auditoría por servicio; revisión: 7 Importantes de seguridad y 5 de arquitectura, corregidos; desvío de la hora de Lima registrado. |
| INV-1a (carril B) | 8 | 2026-11-14 | ~1 h | Dominio, bloqueos asesores, alcance por transacción y contrato público de `Fuentes`; 2 Importantes corregidos; la lista de `ConAlcanceDeRed` queda en `Fuentes` y `Ajustes`. |
| WEB-1d + WEB-1a (carril C) | 16 | 2026-11-14 | ~1,5 h | Sistema de diseño, layout, estados, textos legales y módulo `Web` (sesión y borrador). WEB-1b sin empezar: T14 y T15 esperan `ITokenDelUsuarioDeLaSesion` del carril A. |
| AUD-3 + token de sesión (carril A) | 6 + R12 | 2026-11-14 | ~1,5 h | `ITokenDelUsuarioDeLaSesion` y `OrdenDePasoDeRestauracion.Web` entregados primero para destrabar a C; colas, consultas unificadas, depuración diaria y bajas. AUD-4 (T22–T27) ya hecho en la rama del carril A, aún sin validar ni fundir. |
| INV-1b + INV-1c (carril B) | 17 | 2026-11-14 | ~1,5 h | `inventario-fuentes` T01–T25 completa. Riesgo del actor cubierto con cuatro puntos de detección y pruebas con el rol real de PostgreSQL; 125 pruebas nuevas contra PostgreSQL. |
| WEB-1b parcial (carril C) | 4 | 2026-11-14 | ~0,5 h | T10–T13 (almacenes de sesión y borrador, cifrado por host, anillo de claves, purga en dos fases); T14 y T15 esperaban el token del carril A. |

Segunda ola fundida (merges de `claude/mystifying-panini-0d9826`, `claude/youthful-blackburn-db3929` y `claude/bold-kalam-0567bd`): unitarias 1406 y 1221, arquitectura 155, SpecCheck 23, CoverageCheck 12, integración 2284 de 2285 (una de tiempo real con Redis que pasa sola), gitleaks limpio. Con tres carriles a la vez las suites pesadas compiten y fallan pruebas de contenedores o de tiempo real: la integración completa se corre solo en la fusión.


Quinta ola, primera parte (2026-11-17; piezas de plataforma del carril A hasta `47dda76`, INV-2d completo y ECO-2a): unitarias 1985 y 1923, arquitectura 183, SpecCheck 23, CoverageCheck 12, integración 3744 de 3746 (dos pruebas de tiempo real o de carrera que pasan sueltas, con los otros carriles corriendo suites a la vez), gitleaks limpio. Las sesiones de esta ola se pausaron varias veces a media tarea: cada una deja trabajo sin commitear que hay que retomar.

Cuarta tanda fundida (AUD-4 T22–T27 del carril A y WEB-1b/WEB-1c del carril C): unitarias 1552 y 1487, arquitectura 165 con un fallo abierto (el cliente de auditoría del E-commerce consume el token del usuario directamente; lo corrige el carril A), integración 3100 de 3102 (1 omitida y 1 fallo: el escaneo de secretos detectó valores de muestra en el worktree del carril A, no en `main`), SpecCheck 23. Dos choques de arquitectura entre carriles solo aparecieron al fundir (el fijador de actor en `ComposicionDeFuentes`, ya corregido en `a749d6f`): conviene fundir con más frecuencia.

Tercera ola fundida (merges `22df391` de A, `claude/festive-feynman-2c4bef` de B y `claude/elastic-jepsen-1b3bd3` de C): unitarias 1541 y 1479, arquitectura 155, SpecCheck 23, CoverageCheck 12, **integración 2601 de 2601** (22 min, sola), gitleaks limpio. Ritmo: cada ola de carriles rinde unas 35 a 50 tareas por 1,5 a 2 horas de reloj.

**Lectura de la primera ola (2026-11-14).** La planificación con puerta delegada es el cuello de botella real: tres de cuatro carriles-ola gastaron 4 a 6 rondas de revisión en planes. Para las specs que aún no tienen plan (biblioteca digital, envíos, devoluciones, reclamaciones, carrito, pagos, pedidos, asistente), conviene planificar en lote desde el inicio y presupuestar 2 rondas extra por spec crítica.

## Acordado y pendiente de la persona

- **Orden de sacrificio** si el calendario no alcanza (sección 6 del documento 05): `web-indice-infinito` → `asistente-compra-ia` → avisos por WhatsApp en producción → `ecommerce-devoluciones` → `ecommerce-envios` avanzados → `ecommerce-biblioteca-digital`.
- **Pendiente de decidir** (sección 8 del documento 05): cambiar `AGENTS.md` §2 para que «una tarea a la vez» sea por carril, y la puerta graduada de planes (exige cambiar además `04-aprobacion-delegada.md`, la skill `spec-approve`, `tools/SpecCheck` y sus pruebas en el mismo cambio).
- **Fundir bloques a `main`:** lo hace la persona o quien ella designe, sin `push`, y se valida el resultado fundido.
