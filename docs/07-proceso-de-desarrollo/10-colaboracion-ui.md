# Colaboración en la interfaz con un segundo agente (Codex)

Decisión de la persona responsable (2026-11-27): **Codex** puede trabajar el apartado visual (UI/UX) de BabelHub, su documentación incluida. Reglas de negocio, base de datos y todo lo demás quedan fuera. [`AGENTS.md`](../../AGENTS.md) manda sobre este documento; aquí solo se acota más.

## 1. Quién hace qué

| Quién | Hace |
| :--- | :--- |
| **Persona responsable** | Decide prioridades y diseño, avisa a cada agente, aprueba la publicación. |
| **Codex** | Cambios visuales dentro de las rutas permitidas (§2), en su rama `codex/…`. |
| **Claude** | Todo lo demás; revisa cada entrega de Codex y atiende sus peticiones. La sesión «Guía en vivo» lleva todo lo de despliegue (`progress/coordinacion.md` §4). |

No hay un canal directo entre agentes: se coordinan con [`progress/coordinacion-ui.md`](../../progress/coordinacion-ui.md) y con Git.

## 2. Rutas

`Api/` abrevia `src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api/`.

**Permitidas**

| Ruta | Para qué |
| :--- | :--- |
| `Api/Views/**/*.cshtml`, `Api/Areas/*/Views/**/*.cshtml` | Marcado y clases de las pantallas. |
| `Api/Web/Estilos/**/*.css` | Estilos y animaciones. |
| `Api/wwwroot/js/*.js` (no `lib/`) | JavaScript de la interfaz. Prefiere archivos nuevos `ui-*.js`; cambiar uno existente se revisa a mano. |
| `Api/wwwroot/img/**` | Imágenes de la interfaz (`svg`, `png`, `webp`). |
| `docs/06-experiencia-de-usuario/**` | Documentación de experiencia de usuario. |
| `prototype/ui-v2/**` | Prototipo de referencia. |
| `progress/coordinacion-ui.md` | Solo las secciones «Entregas» y «Peticiones». |

**Excepciones dentro de las vistas (no se tocan):** `_ViewImports.cshtml`, `_ViewStart.cshtml`, `_TokenAntifalsificacion.cshtml`, `Views/Legal/**` y `Views/Textos/**`.

**Todo lo demás está prohibido**, en particular: Dominio, Aplicación, Infraestructura, controladores y cualquier `.cs`; base de datos; `specs/`; `tests/`; `infra/`, `scripts/`, `.github/`; archivos de proyecto y dependencias; `AGENTS.md` y `CLAUDE.md`; textos legales; secretos; y las bibliotecas de `wwwroot/lib/`.

**Toda la UI/UX es tarea de Codex** (decisión de la persona, 2026-11-27): Claude entrega las funciones nuevas con una interfaz sencilla y Codex la mejora después. Si una prueba u otro archivo prohibido impide una mejora visual, no se renuncia a ella: se anota en «Peticiones» y Claude hace ese cambio.

## 2 bis. Qué referencia vale

Ambos agentes parten de la misma información. Si dos fuentes se contradicen, gana la de arriba y se anota en «Peticiones».

1. `AGENTS.md`.
2. La spec de la función (`specs/<feature>/spec.md`) y `docs/`, en particular `docs/06-experiencia-de-usuario/`.
3. La aplicación actual de `main`: lo que el sistema hace hoy.
4. `prototype/ui-v2/`: referencia visual y de recorrido. Puede ofrecer cosas que la aplicación o la spec no tienen; no se copian sin pedirlo.
5. `progress/coordinacion-ui.md`: tareas y observaciones vigentes de la persona.

**La primera versión de la interfaz (`prototype/ui/`, v1) está retirada** (`docs/06-experiencia-de-usuario/03-estados-accesibilidad-y-criterios.md`). Si se comparte con un agente, es solo historia: no define diseño, textos, recorridos ni funciones. Lo que se rescate de ella se pide como cambio y se decide antes de hacerlo.

El trabajo de ramas sin integrar todavía no está en `main`: se consulta en «Reservado».

El resumen vigente de la interfaz (decisiones que no se revierten, cambios por pantalla, lo que no se puede romper y cómo verificar) está en [`docs/06-experiencia-de-usuario/12-estado-de-la-interfaz.md`](../06-experiencia-de-usuario/12-estado-de-la-interfaz.md); quien publique cambios de interfaz lo actualiza.

## 3. Reglas de la interfaz

1. **Solo apariencia.** No se cambia lo que el sistema hace. Mover una función, agregar o quitar un dato o cambiar un recorrido es un cambio de comportamiento: necesita spec (`AGENTS.md` §2) y se pide en «Peticiones».
2. **Formularios intactos:** no se cambian `action`, `method`, nombres ni valores de campos, atributos `asp-*` o `hx-*`, el token antifalsificación ni los `id` que usan las pruebas.
3. **CSP:** sin scripts ni estilos en línea (`<script>` con código, `<style>`, `style="…"`) y sin recursos de otros dominios.
4. **Sin lógica en las vistas:** nada de `@inject`, `Html.Raw` ni cálculos de negocio.
5. **Mejora progresiva:** cada pantalla sigue funcionando sin JavaScript. El JavaScript visual nuevo no hace solicitudes ni usa cookies o almacenamiento.
6. **Diseño y accesibilidad** (`AGENTS.md` §6 y `docs/06-experiencia-de-usuario/`): tokens de los temas, sin colores sueltos, contraste AA, foco visible, teclado, el estado nunca solo por color.
7. **Animaciones:** respetan `prefers-reduced-motion`, no retrasan ninguna acción ni ocultan contenido si el script falla.
8. **Textos** en español (es-PE). No se prometen funciones que no existen ni se simula stock. No se quitan avisos al usuario.
9. **Sin dependencias nuevas.** Si hace falta una, se pide.

## 4. Flujo de trabajo

1. Lee la versión vigente del tablero: `git show main:progress/coordinacion-ui.md`. Toma solo una tarea **Asignada** y no toques los archivos reservados.
2. Trabaja en una copia propia, nunca en la carpeta principal:
   `git worktree add ../BabelHub-codex -b codex/<tarea> main`
3. Commits pequeños, `tipo: descripción` en inglés, con la línea `Co-Authored-By` de tu entorno.
4. Antes de entregar (en una copia nueva, genera primero los estilos con `bash scripts/construir-estilos.sh`):
   - `dotnet build BabelHub.sln -warnaserror`
   - Las pruebas de pantallas (`dotnet test tests/integration/BabelHub.IntegrationTests --filter "(FullyQualifiedName~Pantallas|FullyQualifiedName~.Web.)&Categoria!=Lanzamiento&Categoria!=Guiones"`) y la de arquitectura. Si una falla por un cambio visual legítimo, **no la edites**: anótalo en «Peticiones».
   - `dotnet run --project tools/SpecCheck`
   - `bash scripts/guardia-ui.sh codex/<tarea> main`
   - Capturas en tema claro y oscuro, escritorio y móvil, guardadas fuera del repositorio.
5. Anota la entrega en el tablero (rama, commits, qué cambió, qué verificaste y qué no) y haz commit. **La entrega es la rama**, no un ZIP ni carpetas sueltas en la copia principal. Si tu entorno no puede usar Git, dilo antes de empezar.
6. **Sin `push`, PR, etiquetas ni fusiones.** No reescribas historia ni borres ramas.

## 5. Revisión y publicación (Claude)

1. Ejecuta el control **desde `main`**, para que la rama revisada no pueda cambiarlo: `bash scripts/guardia-ui.sh codex/<tarea> main`.
2. Revisa a mano cada aviso «REVISAR» (formularios, campos o protecciones tocados).
3. Compila, corre las pruebas y mira las pantallas.
4. Si pasa, la persona pide publicar: PR, CI en verde y `main`. Si no, anota en «Respuestas» qué corregir.

El control detecta rutas y patrones conocidos; **no sustituye la revisión**.

## 6. Mensaje para iniciar a Codex

> Lee `AGENTS.md`, `docs/07-proceso-de-desarrollo/10-colaboracion-ui.md` y `docs/06-experiencia-de-usuario/12-estado-de-la-interfaz.md`. Trabajas solo la interfaz, dentro de las rutas permitidas. La primera versión de la interfaz que se te haya compartido está retirada: la referencia es la del §2 bis. Lee `git show main:progress/coordinacion-ui.md`, toma la tarea asignada y sigue el flujo del §4. Responde en español. Si algo requiere salir de tus rutas o cambiar el comportamiento, no lo hagas: anótalo en «Peticiones» y avísame.
