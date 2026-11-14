# Bloques de trabajo y carriles

Aplica [`docs/07-proceso-de-desarrollo/05-desarrollo-por-bloques.md`](../docs/07-proceso-de-desarrollo/05-desarrollo-por-bloques.md) (decisión del 2026-11-14). Cada bloque tiene su nivel de rigor (**C** crítico, **E** estándar, **B** bajo; el de las tareas es el del bloque salvo que se indique, y siempre sube al de la parte más riesgosa), sus dependencias y su entregable. Se actualiza al abrir y al cerrar cada bloque. Los bloques de las specs sin plan (`INV-*`, `ECO-*`) se detallan al planificarlas.

Estado: ☐ pendiente · ▶ en curso · ☑ hecho (fundido a `main`).

## Carril A — Transversales

| Bloque | Contenido | Tareas | Nivel | Depende de | Estado |
| :--- | :--- | :--- | :---: | :--- | :---: |
| **PLA-R1** | `transversal-plataforma`: endurecimiento de VM-AUX, espacio local reservado, custodia de la clave y ensayo de descifrado, `tools/RestauracionCheck` | T26, T27, T28, T30 | C | T25 (hecha) | ☐ |
| **AUD-1** | `transversal-auditoria`: Dominio (detalle seguro, catálogo de acciones, actor, anonimización, alertas, acumulado de límites) | T01–T06 | C | — | ☐ |
| **AUD-2** | Contratos de registro, consulta y alerta, puertos de Identidad y Reservas y los catálogos de acciones | T07, T08, T09, T11–T16 | C | AUD-1 | ☐ |
| **AUD-3** | Implementación de los puertos de `Plataforma`, consultas unificadas, agrupar y enviar alertas, depuración y bajas del Inventario | T10, T17–T20 | C | AUD-2 | ☐ |
| **SEG-C** | Cierre de `transversal-seguridad`: T27b y T29 (acumulador persistente) y `spec-verify` | T27b, T29 | C | AUD-3 (T10, T18) | ☐ |
| **AUD-4** | Migraciones, roles, función de depuración, repositorios de solo inserción, cliente del Inventario, trabajos de fondo | T21–T27 | C | AUD-3 | ☐ |
| **AUD-5** | Endpoints, contrato web, integración de punta a punta y documentación | T28–T31 | E | AUD-4 | ☐ |
| **IDE-1** | `identidad-seguridad`: Dominio (contraseñas, usuario y roles, segundo factor, desafío, códigos, textos, solicitudes, cancelación, marcas, clave de firma) | T01–T10 | C | — (puede ir en paralelo a AUD-1) | ☐ |
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
| **INV-0** | Planificar en lote las cuatro specs (`spec-plan`, `spec-tasks`, puerta delegada) | C | ECO-0 (contratos) | ☐ |
| **INV-1** | `inventario-fuentes` | E | INV-0 | ☐ |
| **INV-2** | `inventario-seleccion-fuente`: selección y reservas con pruebas de concurrencia sin sobreventa (AC01) | C | INV-1 | ☐ |
| **INV-3** | `inventario-ajustes` (ajustes guiados por motivo, auditados) | C | INV-2 | ☐ |
| **INV-4** | `inventario-traslados` | C | INV-3 | ☐ |

## Carril C — E-commerce y web

| Bloque | Contenido | Nivel | Depende de | Estado |
| :--- | :--- | :---: | :--- | :---: |
| **ECO-0** | Contratos REST entre el E-commerce y el Inventario y sus DTOs en `BabelHub.Shared`; puerto de «usuario actual» de prueba | C | — | ☐ |
| **ECO-1** | Planificar `web-base` y `ecommerce-catalogo` | E | ECO-0 | ☐ |
| **WEB-1** | Estructura de `web-base` (diseño, sesión, estados) | E | ECO-1 | ☐ |
| **ECO-2** | `ecommerce-catalogo` | E | WEB-1 | ☐ |
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
| **ECO-13** | `web-indice-infinito` | B | WEB-1 | ☐ |

## Cierre (todos los carriles)

| Bloque | Contenido | Nivel | Estado |
| :--- | :--- | :---: | :---: |
| **FIN-1** | `spec-verify` de cada spec y paso a `Done` | C | ☐ |
| **FIN-2** | Despliegue: E2E con Playwright, carga con k6, Compose de Oracle y criterios de aceptación y metas de rendimiento | C | ☐ |

## Medición (se llena al cerrar cada bloque)

| Bloque | Tareas | Fecha de cierre | Duración | Notas (hallazgos, paradas, replanificación) |
| :--- | :---: | :--- | :--- | :--- |
| (antes de los bloques: `transversal-plataforma` T20–T25, una tarea por ciclo) | 6 | 2026-11-14 | ~1 jornada | Referencia: cada tarea con validación completa costaba 5 a 8 min solo de pruebas. |

## Acordado y pendiente de la persona

- **Orden de sacrificio** si el calendario no alcanza (sección 6 del documento 05): `web-indice-infinito` → `asistente-compra-ia` → avisos por WhatsApp en producción → `ecommerce-devoluciones` → `ecommerce-envios` avanzados → `ecommerce-biblioteca-digital`.
- **Pendiente de decidir** (sección 8 del documento 05): cambiar `AGENTS.md` §2 para que «una tarea a la vez» sea por carril, y la puerta graduada de planes (exige cambiar además `04-aprobacion-delegada.md`, la skill `spec-approve`, `tools/SpecCheck` y sus pruebas en el mismo cambio).
- **Fundir bloques a `main`:** lo hace la persona o quien ella designe, sin `push`, y se valida el resultado fundido.
