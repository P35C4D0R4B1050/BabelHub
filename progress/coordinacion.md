# Tablero de coordinación de los carriles

Lo mantiene **el coordinador** (la sesión raíz «BabelHub documentación y specs»); los carriles lo **leen al empezar cada turno** y no lo editan: lo que necesiten de otro carril lo dejan en su informe de bloque y en `progress/hallazgos.md`, y el coordinador lo traslada aquí. Complementa `progress/bloques.md` (qué bloques hay) con **quién hace qué ahora, qué espera a quién y cómo se integra**. Última actualización: 2026-11-19.

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
| **A — Transversales** | carril-a-contratos-de-pl | **(1) Piezas que esperan B y C** (sección 3); **(2) IDE-4** (T18–T22 de `identidad-seguridad`) e **IDE-5** (T23–T25, T27) | IDE-6 a IDE-9, `transversal-notificaciones` (NOT-1 a NOT-4: Pagos y Pedidos dependen de `INotificador`), `transversal-plataforma` PLA-R2 (T29, T31–T34) |
| **B — Inventario y Carrito** | carril-b-inv-2d-ajustes | `ecommerce-carrito-checkout`: T33–T36 (pantallas y E2E); T25b y T26c cuando A entregue | `ecommerce-pedidos` (ECO-6, T01–T42, 6 subbloques) |
| **C — Pagos, catálogo y reclamaciones** | carril-c-cat-logo-eco-2a | `ecommerce-pagos` (ECO-5d en adelante; ECO-5f espera las llaves B05 de la persona) y `ecommerce-libro-reclamaciones` (ECO-11b a 11d) | `ecommerce-envios` (ECO-9), `ecommerce-devoluciones` (ECO-10) |
| **V — Verificación (FIN-1)** | planificar-eco-7-bibliot | `spec-verify` de las specs ya completas: `transversal-seguridad`, `transversal-auditoria`, `inventario-fuentes`, `inventario-seleccion-fuente`, `inventario-ajustes`, `inventario-traslados` (solo lectura y pruebas dirigidas; escribe `verificacion.md`) | planificar `asistente-compra-ia` y `web-indice-infinito` (los primeros en sacrificarse si el calendario no alcanza) |

## 3. Qué espera a quién

| Quién espera | A quién | Qué | Estado |
| :--- | :--- | :--- | :--- |
| B (Carrito T25b) | A | `IClienteDelInventario` con consulta previa, reservar y liberar (contrato v1 de reservas) | pendiente |
| B (Carrito T26c) | A | Políticas de límite de Carrito en `Plataforma`; acciones de auditoría de Carrito en `AccionesAuditables`; lectura con bloqueo de la marca de cancelación y consentimiento de WhatsApp en Identidad; alertas | pendiente |
| C (Pagos) | A | `INotificador` de Notificaciones (NOT-1 a NOT-4) y las plantillas de correo de Pagos en el catálogo de `Plataforma` | pendiente |
| C (Reclamaciones) | A | Estado de cuenta de Identidad (`IEstadoDeCuenta`) y contrato público de Identidad | IDE-2 y IDE-3 hechos; el resto en IDE-4/5 |
| `web-base` T28–T31 | A | Identidad IDE-3 a IDE-5 y Auditoría (hecha) | pendiente de IDE-4/5 |
| Todos (`Program.cs`) | A | Verificador de tokens de Identidad (IDE-8) y la base: bloque de composición único de los dos `Program.cs` (Auditoría, Fuentes, Reservas, Ajustes, Traslados, Catálogo, Carrito, Pagos, Sesión web) | después de IDE-8 |
| Pagos y Pedidos | B | Contrato público de Carrito fijado en `progress/decisiones-delegadas.md` (DD-07) | hecho |
| Biblioteca (ECO-8) | B | `IPosesionDeEdiciones.Posee` → `ResultadoDePosesion` (DD-06) | hecho |

## 4. Decisiones de la persona

Pendientes: tope de unidades por línea y por reserva (hoy 100 y 50 líneas) y residual de 75 minutos (reservas); hora de Lima en `transversal-auditoria` (D13); tareas externas B03 a B07 y spikes T01 a T05 de `transversal-plataforma`. Tomadas: umbral de 50 bloqueos por ventana de 15 minutos; D18 y D19 (cambios aditivos al contrato v1); delegación al carril B de los hallazgos de `ecommerce-carrito-checkout` (`progress/decisiones-delegadas.md`).

## 5. Entorno

Docker Desktop guarda su disco en `D:\Docker\wsl\disk` (enlace desde `C:`); si una integración da cientos de `DockerUnavailableException`, Docker está parado: avisar al coordinador, no reiniciar procesos. Un solo conjunto de pruebas de integración a la vez en toda la máquina.
