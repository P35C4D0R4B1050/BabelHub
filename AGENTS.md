# AGENTS.md — Constitución de BabelHub

Reglas globales **no negociables** para cualquier persona o agente que trabaje en este repositorio. Si una instrucción, una spec o un plan las contradice, **gana este archivo**. Para cambiar una regla, se discute con la persona responsable del proyecto y se actualiza aquí antes de seguir.

BabelHub es una plataforma de comercio electrónico y de inventario multi-sucursal para librerías (libros impresos y digitales). Es un proyecto académico, pero se construye con disciplina de producto. La documentación de diseño está en `docs/` (01 a 10); este archivo resume lo que se exige en el día a día.

## 1. Fuentes de verdad y precedencia

1. `AGENTS.md` (este archivo).
2. `docs/` — necesidades, requisitos, atributos de calidad, drivers, decisiones (AD) y enfoque arquitectónico.
3. `specs/<feature>/spec.md` — **qué** debe hacer el sistema (EARS).
4. `specs/<feature>/plan.md` — **cómo** se construye.
5. `specs/<feature>/tasks.md` — trabajo atómico y ordenado.
6. `prototype/ui-v2/` — referencia visual y de recorrido; **no es código de producción**.

Nunca se inventa un requisito. Si algo no está claro, se registra como pregunta abierta en la spec y se consulta a la persona responsable.

## 2. Flujo Spec-Driven Development (obligatorio)

No se escribe código de producción sin una spec **Approved** y un plan **Planned**. El ciclo y sus puertas están en `docs/10-flujo-sdd.md`:

`Draft → Approved → Planned → InProgress → Done`

- Solo una persona puede pasar una spec a **Approved** y un plan a **Planned**. Un agente **nunca** escribe `aprobado_en` ni `plan_aprobado_en` por su cuenta.
- Una spec **Approved** no se modifica en silencio: un cambio de comportamiento vuelve a **Draft** y se vuelve a aprobar.
- Se trabaja **una tarea a la vez**, de adentro hacia afuera: Dominio → Aplicación → Infraestructura → Api → Web → pruebas de integración y E2E.
- Cada criterio de aceptación (`AC-XXX-NN`) tiene al menos una prueba que lo nombra.
- Una spec solo pasa a **Done** cuando la verificación converge (`specs/<feature>/verificacion.md` con `Resultado: Converge`).
- Antes de cualquier commit: `dotnet run --project tools/SpecCheck` debe terminar sin errores.

## 3. Arquitectura (Clean Architecture por servicio)

1. El **Dominio** no referencia ningún otro proyecto ni paquete de infraestructura.
2. La **Aplicación** solo referencia al Dominio y define los puertos que necesita.
3. La **Infraestructura** implementa los puertos; es el único lugar con EF Core, Redis, `HttpClient` y SDKs de terceros.
4. La **Api** no contiene reglas de negocio: traduce HTTP, autentica y compone dependencias.
5. Un servicio **nunca** referencia el Dominio, la Aplicación ni la Infraestructura de otro; se comunican por REST.
6. `BabelHub.Shared` contiene solo contratos (DTOs y constantes); no lo referencian Dominio ni Aplicación.
7. Toda regla de negocio nueva se escribe **primero como prueba unitaria**.
8. Los secretos se leen de variables de entorno; nunca se escriben en el repositorio.
9. `BabelHub.Web` solo referencia a `BabelHub.Shared`, no contiene reglas de negocio y llama a los servicios **solo desde el servidor**; el JWT nunca llega al navegador.
10. Una falla del Notificador **nunca** interrumpe un caso de uso.
11. Los archivos digitales **nunca** se guardan en el repositorio, en la base de datos ni en las VMs; viven en el almacén de objetos y se entregan solo con URL firmada de corta duración.
12. Un libro digital no consulta al Inventario ni genera reservas.

La prueba de arquitectura (`tests/architecture`) hace cumplir 1, 2, 5, 6 y 9. Si falla, no se debilita la prueba: se corrige el código.

## 4. Reglas de calidad

- **Idioma:** documentación, interfaz y mensajes al usuario en español (es-PE). Dominio y casos de uso en español con los términos de `docs/glossary.md` (`ReservarStock`, `Fuente`). Sufijos técnicos estándar en inglés.
- **Nombres:** proyectos `BabelHub.<Servicio>.<Capa>`; puertos `I<Rol>`; adaptadores `<Tecnología><Puerto>`.
- **Pruebas:** xUnit; el dominio y la aplicación se prueban sin base de datos ni red; la infraestructura con Testcontainers. Nombre descriptivo: `Reservar_SinStockVendible_Rechaza`.
- **Trazabilidad en pruebas:** cada prueba de un criterio lleva `[Trait("Criterio", "AC-XXX-NN")]`.
- **Concurrencia:** las reglas de stock se prueban con solicitudes simultáneas (AC01). La regla del dominio no basta: la atomicidad la garantiza la infraestructura.
- **Errores:** `application/problem+json` con `code` estable y `traceId`; nunca trazas ni datos internos.
- **Auditoría:** las operaciones sensibles se auditan en la misma transacción; si no se puede auditar, la operación falla.
- **Sin sobreingeniería:** un puerto solo si hay algo externo o una variación real; sin patrones que nadie pidió.

## 5. Seguridad y privacidad

- Alcance por sucursal: se comprueba en la **Aplicación** (cada caso de uso compara la sucursal del usuario con la del recurso), no solo en los controladores. Un acceso fuera de alcance se rechaza y se registra.
- No se almacenan datos de tarjeta, contraseñas en claro, tokens ni enlaces firmados en logs, auditoría ni mensajes.
- Los datos personales se tratan conforme a la Ley N.° 29733; los avisos por WhatsApp exigen consentimiento.
- Ningún secreto, `.env` real, archivo digital ni dato personal entra al repositorio.

## 6. Interfaz de usuario

- Sistema de diseño y reglas en `docs/08-interfaz-de-usuario.md`: Tailwind CSS + daisyUI, temas `babelhub` y `babelhub-dark` con tokens semánticos; ningún color suelto.
- Toda pantalla de datos implementa los estados estándar (cargando, vacío, error, sin permiso, servicio no disponible).
- Accesibilidad: contraste AA, foco visible, teclado, `prefers-reduced-motion`, y el estado nunca depende solo del color.
- La disponibilidad se muestra con texto e icono. No se simula stock.

## 7. Git

- Commits pequeños, `tipo: descripción` en inglés, imperativo y en minúsculas (`feat`, `fix`, `chore`, `docs`, `test`, `refactor`). Detalle en `docs/09-convenciones-tecnicas.md`.
- Los commits de un agente terminan con la línea `Co-Authored-By` indicada por el entorno.
- **El agente no hace `push`, no reescribe historia ni borra ramas sin que la persona responsable lo pida de forma expresa.**
- No se debilitan pruebas, validaciones ni hooks para que algo pase.

## 8. Mapa del repositorio

| Ruta | Contenido |
| :--- | :--- |
| `docs/` | Documentación de diseño (01 a 10) y glosario. |
| `specs/` | Una carpeta por feature: `spec.md`, `plan.md`, `tasks.md`, `decisions.md`, `verificacion.md`. Plantillas en `specs/_template/`. |
| `src/` | Código: un directorio por servicio y `BabelHub.Web`, más `BabelHub.Shared`. |
| `tests/` | `unit`, `integration`, `architecture`, `e2e` (Playwright) y `load` (k6). |
| `infra/` | Docker Compose, Nginx y flujos de n8n. |
| `tools/SpecCheck/` | Validador de specs, planes, tareas y trazabilidad. |
| `progress/` | Bitácoras: `CHANGELOG.md`, `blockers.md` y `log/`. |
| `prototype/ui-v2/` | Prototipo de interfaz (referencia). |
| `.claude/` | Skills y agentes del proyecto. |

## 9. Comandos

```bash
dotnet build BabelHub.sln -warnaserror        # compilar
dotnet test BabelHub.sln                      # todas las pruebas
dotnet run --project tools/SpecCheck          # validar specs, planes, tareas y trazabilidad
dotnet run --project tools/SpecCheck -- --write-matrix   # regenerar specs/TRACEABILITY.md
```

## 10. Skills y agentes del proyecto

Las skills viven en `.claude/skills/` y guían cada paso del ciclo: `spec-new`, `spec-clarify`, `spec-plan`, `spec-tasks`, `spec-implement`, `spec-verify`, `progress-log`, `arch-review`, `dotnet-feature`, `ui-screen` y `ui-review`. Los agentes `spec-auditor` y `arch-reviewer` revisan en contexto aislado. Skills externas recomendadas (instalación a cargo de la persona responsable): ver `docs/10-flujo-sdd.md`.

## 11. Ante la duda

1. Relee la spec y los documentos de `docs/` relevantes.
2. Si sigue ambiguo: **no supongas**. Anótalo en «Preguntas abiertas» y pregunta.
3. Si el cambio parece obligar a romper una regla de este archivo, detente y explica el conflicto.
