# Tablero de coordinación de los carriles

Lo mantiene **el coordinador** (la sesión raíz «BabelHub documentación y specs»); los carriles lo **leen al empezar cada turno** y no lo editan: lo que necesiten de otro carril lo dejan en su informe de bloque y en `progress/hallazgos.md`, y el coordinador lo traslada aquí. Complementa `progress/bloques.md` (qué bloques hay) con **quién hace qué ahora, qué espera a quién y cómo se integra**. Última actualización: 2026-11-19 (tarde).

## 1. Protocolo (obligatorio para todos los carriles)

1. **Al empezar un turno:** `git merge main` (la identidad de git ya está configurada en el repositorio), leer este tablero y el último informe del carril.
2. **Un commit por tarea**, `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`, sin `push`, sin tocar `BabelHub.sln`, `progress/CHANGELOG.md`, `progress/bloques.md`, `specs/TRACEABILITY.md` ni la memoria (los integra el coordinador).
3. **Al cerrar un bloque:** validación del área, revisores (`security-reviewer` y `arch-reviewer` en nivel C, `arch-reviewer` en E), informe en `progress/log/`, y un mensaje de una línea al coordinador con la rama y el último commit. **El coordinador funde, valida `main` y asigna lo siguiente**: un carril no se queda esperando sin decir qué le falta.
4. **Integración completa (`dotnet test` de integración, ~20 min, con Docker):** la corre el coordinador sobre `main` tras cada fusión; los carriles corren solo las pruebas de su área, y la integración completa de su bloque solo cuando el coordinador lo indique (turnos), para no competir por la máquina.
5. **Dependencias entre carriles:** si necesitas algo de otro carril, usa un doble que **falle cerrado**, deja la guarda de producción que impida publicar sin lo real, regístralo con dueño y fase y sigue. Nunca declares tú un puerto que corresponde a otro carril; nunca agregues excepciones a una regla de arquitectura.
6. **Cambios a `BabelHub.Shared` o al contrato v1:** solo aditivos, en commit propio, con la decisión registrada en `docs/03-diseno-de-software/08-contratos-entre-servicios-v1.md` y aviso al coordinador.
7. **Paradas:** decisión de negocio, hallazgo de seguridad Bloqueante o Importante sin arreglo, puerta que falla o conflicto entre carriles → una pregunta corta al coordinador.

## 2. Carriles y su siguiente encargo

| Carril | Sesión | Ahora | Después |
| :--- | :--- | :--- | :--- |
| **A — Transversales** | carril-a-contratos-de-pl | Terminar IDE-8 (resto de T44 y T45) → IDE-9 (T46–T50) → **COMP-1: componer los `Program.cs`** (Inventario primero: Fuentes, Reservas, Ajustes, Traslados, Auditoría y verificador de tokens; luego E-commerce) → NOT-2 a NOT-4 (envío, tope por número, recepción, adaptadores) | PLA-R2 (T29, T31–T34), PLA-R3/R4, `identidad-seguridad` T27/T34 pendientes de Infraestructura de correo |
| **B — Carrito y Pedidos** | carril-b-inv-2d-ajustes | **T25b y T26c del carrito** (ya no esperan: A entregó el cliente del Inventario, límites y auditoría de Carrito) y T36 (E2E) → `ecommerce-pedidos` (ECO-6a a 6f, T01–T42; los contratos para Envíos ya están) | `ecommerce-biblioteca-digital` (ECO-8) |
| **C — Pagos y reclamaciones** | carril-c-cat-logo-eco-2a | `ecommerce-pagos` ECO-5d en adelante (T24–T36 y T37; ECO-5f, T38, espera B05) y `ecommerce-libro-reclamaciones` ECO-11c y 11d (T13–T23) | **WEB-2** (`web-base` T28–T31, con Identidad ya en main) y `ecommerce-devoluciones` (ECO-10) |
| **V — Envíos** | planificar-eco-7-bibliot | `ecommerce-envios` ECO-9b a 9e (T06–T25), con los contratos de Pedidos ya en main | `ecommerce-devoluciones` (ECO-10) o `web-base` T32–T35 (E2E, axe, rendimiento) según quién termine antes; la verificación FIN-1 de cada spec que se complete |

## 3. Qué espera a quién

| Quién espera | A quién | Qué | Estado |
| :--- | :--- | :--- | :--- |
| B (Carrito T25b) | A | `IClienteDelInventario` con consulta previa, reservar y liberar (contrato v1 de reservas) | **entregado por A y en main** (IDE-4 e IDE-8) |
| B (Carrito T26c) | A | Políticas de límite de Carrito en `Plataforma`; acciones de auditoría de Carrito en `AccionesAuditables`; lectura con bloqueo de la marca de cancelación y consentimiento de WhatsApp en Identidad; alertas | **entregado por A y en main** (IDE-4 e IDE-8) |
| C (Pagos) | A | `INotificador` de Notificaciones (NOT-1 a NOT-4) y las plantillas de correo de Pagos en el catálogo de `Plataforma` | pendiente |
| C (Reclamaciones) | A | Estado de cuenta de Identidad (`IEstadoDeCuenta`) y contrato público de Identidad | IDE-2 a IDE-7 hechos y en main |
| `web-base` T28–T31 | A | Identidad IDE-3 a IDE-5 y Auditoría (hecha) | **listo para empezar** (Identidad en main); falta dueño: ver tabla de carriles |
| Todos (`Program.cs`) | A | Verificador de tokens de Identidad (IDE-8) y la base: bloque de composición único de los dos `Program.cs` (Auditoría, Fuentes, Reservas, Ajustes, Traslados, Catálogo, Carrito, Pagos, Sesión web) | después de IDE-8 |
| Pagos y Pedidos | B | Contrato público de Carrito fijado en `progress/decisiones-delegadas.md` (DD-07) | hecho |
| Biblioteca (ECO-8) | B | `IPosesionDeEdiciones.Posee` → `ResultadoDePosesion` (DD-06) | hecho |

### Huecos que dejó la verificación FIN-1 (2026-11-19; detalle en `specs/<spec>/verificacion.md`)

| Spec | Resultado | Hueco | Dueño |
| :--- | :--- | :--- | :--- |
| `inventario-ajustes` | Converge | Composición de `Ajustes` en el `Program.cs` del Inventario | bloque de composición |
| `inventario-fuentes` | No converge (3 Parciales) | AC-FUE-08 (k6, FIN-2); FUE-09 y FUE-11 sin dueño en `web-base` (búsqueda por título, autor o ISBN, y encargado asignado); composición de `Fuentes` | persona (FUE-09/11), FIN-2, composición |
| `inventario-traslados` | No converge solo por TRA-22 | `IDestinatariosDeAlertas` del E-commerce solo devuelve administradores centrales: logística no recibe la alerta de 48 h | **A** (con IDE-5/NOT) |
| `inventario-seleccion-fuente` | No converge (3 Parciales) | SEL-03 y SEL-24 sin prueba (**V**, solo tests); SEL-39 validación de la clave antes del alcance (**B**, corregir el código; la spec manda) | V y B |
| `transversal-seguridad` | No converge (3 Parciales) | Tope de 24 correos (AC-SEG-23): prueba (**V**) y adaptador de correo de producción (**A**, IDE-7); SEG-31 solo en reserva y reclamación, no en registro ni ingreso (**web-base** WEB-2 T28) | V, A, C/web |
| `transversal-auditoria` | No converge (16 Parciales) | Todos por piezas de otras specs (Identidad, correo, Pedidos, Envíos, Devoluciones, Biblioteca, Notificaciones) y por la composición; etiquetas AC-AUD-09 y 25 (**V**); hora de Lima (persona) | cada spec dependiente |

## 4. Decisiones de la persona

Pendientes: tope de unidades por línea y por reserva (hoy 100 y 50 líneas) y residual de 75 minutos (reservas); hora de Lima en `transversal-auditoria` (D13); tareas externas B03 a B07 y spikes T01 a T05 de `transversal-plataforma`. Tomadas: umbral de 50 bloqueos por ventana de 15 minutos; D18 y D19 (cambios aditivos al contrato v1); delegación al carril B de los hallazgos de `ecommerce-carrito-checkout` (`progress/decisiones-delegadas.md`).

## 5. Entorno

Docker Desktop guarda su disco en `D:\Docker\wsl\disk` (enlace desde `C:`); si una integración da cientos de `DockerUnavailableException`, Docker está parado: avisar al coordinador, no reiniciar procesos. Un solo conjunto de pruebas de integración a la vez en toda la máquina.

## 6. Política de navegadores de los E2E (decisión de la persona, 2026-11-20)

- **Obligatorios (puerta de cierre):** Chromium de escritorio, Chromium en teléfono y WebKit (si la caché de Playwright lo tiene). Un fallo ahí bloquea el cierre del bloque.
- **Firefox: informativo, no indispensable.** Se ejecuta si el entorno lo permite (hoy `firefox.exe` está bloqueado por el sistema: «Permission denied»; no se toca la seguridad del equipo). Un fallo solo de Firefox se registra como hallazgo (Menor, con dueño y fase) y **no reabre un bloque cerrado**, salvo que revele un defecto real de estándares o accesibilidad que también afecte a los demás.
- Los proyectos de Playwright de cada carril declaran Firefox como proyecto opcional (se omite si no inicia), sin debilitar ninguna aserción de los obligatorios.

## 7. Plan del Sprint 2 (decisión de la persona, 2026-11-19)

Entrega del código y de la evidencia de atributos de calidad el **28 de noviembre**. Alcance: se hace el recorrido de compra y la **biblioteca digital (ECO-8)**; se **difieren** `asistente-compra-ia` (ECO-12) y `web-indice-infinito` (ECO-13). Calendario, bloques de evidencia (OAS-1 OpenAPI, K6-1 pruebas de carga hasta 2000 usuarios virtuales, DOC-1 base de datos y caché, cobertura), control de tokens (relevos de chat, fusiones en lote) y riesgos: `docs/07-proceso-de-desarrollo/06-plan-sprint-2.md`. Nadie empieza asistente ni índice infinito; A no debe tomar ECO-8 (va a un carril nuevo por relevo).
