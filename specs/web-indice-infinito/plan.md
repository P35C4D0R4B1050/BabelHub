---
spec: web-indice-infinito
estado: Draft
---

# Plan — El índice infinito (experimento opcional)

> **Cómo se construye.** Parte de la spec `web-indice-infinito` (9 requisitos, 7 criterios; spec Approved el 2026-10-16). Es una pieza literaria de marca **sin base de datos, sin caché y sin estado**: una función pura de una dirección a una página de texto, servida por la interfaz web del E-commerce. Vive en el módulo `Web` en una subcarpeta propia (`Indice`), sin esquema y **sin tocar ningún otro módulo de negocio**. El módulo `Web` ya hospeda la interfaz web (`docs/03/04` fila de módulos: «Interfaz web … índice infinito»); como `docs/02/03` línea 174 y `docs/03/04` línea 34 dicen que `Web` «solo» tiene las reglas de la sesión, **T01 los corrige primero** (D01). Se construye **al final y solo con el catálogo estable** (decisión de la persona responsable, 2026-10-16). Nivel de rigor B (`docs/07-proceso-de-desarrollo/05-desarrollo-por-bloques.md` §2), con las tareas de la entrada pública en `E`.

## 1. Resumen técnico

- **Generador propio, puro y determinista (IND-01, IND-02, IND-06).** `GeneradorDePaginasDelIndice` convierte una dirección validada en una página de **20 líneas de 70 símbolos** de un alfabeto fijo de 30 (26 letras sin tilde, ñ, espacio, coma y punto), unidas por `\n` (sin salto final). Algoritmo **propio y estable** (D02): la dirección se pasa a UTF-8 y se resume con **FNV-1a de 64 bits** (constantes publicadas: base `0xcbf29ce484222325`, primo `0x100000001b3`) que siembra un **SplitMix64** (incremento `0x9E3779B97F4A7C15`, multiplicadores `0xBF58476D1CE4E5B9` y `0x94D049BB133111EB`); cada símbolo es `(salida >> 32) % 30`. No usa `string.GetHashCode` (aleatorizado por proceso), `System.Random` (su secuencia no está garantizada entre versiones) ni código de terceros: son algoritmos publicados, escritos desde cero. Sin cultura ni reloj ni estado. Las pruebas comprueban cada pieza con **vectores de referencia publicados** (FNV-1a de 64 bits y SplitMix64 con semilla 0) y fijan además un **patrón de oro** de una dirección dada, que falla si alguien cambia el algoritmo (cambiaría todas las páginas ya compartidas).
- **Direcciones (IND-03, IND-04, IND-07).** `DireccionDelIndice` es válida si tiene de 1 a 64 caracteres de `a-z`, `0-9` y `-`; **cualquier otra cosa** (más de 64, mayúsculas, tildes, espacios, `/`, `%`, controles) **no es válida** y no se guarda ni se repite. Solo el campo del formulario «Ir a una dirección» recorta los espacios de los extremos (en `Entrar`); un campo vacío o de solo espacios equivale a pedir una al azar (spec §6). La dirección **al azar** tiene la forma `s{1–40}-p{1–4}-e{1–5}-v{1–32}-pg{1–410}` (siempre válida y de menos de 64 caracteres) y es **distinta de la actual** (se reintenta si coincide). El entero lo elige la Aplicación con **muestreo por rechazo (sin sesgo) sobre `IAleatoriosSeguros`**, el puerto de bytes criptográficos que ya tiene el módulo `Web` (sin puerto ni adaptador nuevos; las pruebas usan un doble con bytes fijos).
- **Rutas (IND-01, IND-03, IND-04, IND-07, IND-08).** La dirección canónica viaja en la ruta, de modo que **copiar el enlace** ya incluye la dirección (IND-04): `GET /indice-infinito/{**direccion}` muestra la página (la captura total hace que una dirección con `/` sea simplemente no válida y no un 404); `GET /indice-infinito` y `?direccion=` (formulario) redirigen con 302 a la canónica si es válida, o a una al azar si viene vacía; `GET /indice-infinito/acciones/al-azar?desde={direccion}` redirige con 302 a una dirección nueva y distinta (la ruta de la acción tiene **dos segmentos y nunca puede ser una dirección válida**, de modo que la dirección `al-azar` sigue abriendo su página). Una dirección **no válida** responde **200** con un mensaje amable y **una página al azar ya mostrada** con su enlace (IND-07, AC-IND-06), **sin repetir nunca el texto recibido**: ni en el mensaje, el título, los enlaces, el campo del formulario (que no usa `asp-for`, no reaparece lo escrito y limpia el estado del modelo) ni los registros propios. Un `desde` o un `direccion` repetido o no válido se **ignora** y se redirige a una al azar, sin 200 amable y sin eco. «Reintentar» de los estados de error apunta a la ruta fija `/indice-infinito`, nunca a la ruta recibida. Las redirecciones solo construyen rutas propias desde direcciones ya validadas (`Location` con la forma `^/indice-infinito/[a-z0-9-]{1,64}$`: sin redirección abierta ni inyección).
- **Cabeceras de toda respuesta del índice (D06).** Un **middleware por el prefijo `/indice-infinito`**, antes de los límites, añade `X-Robots-Tag: noindex, nofollow` y `Cache-Control: no-store` a **todas** las respuestas (200, 200 amable, 302, 429 y los errores del servidor): ninguna caché compartida sirve una página al azar a otro visitante ni reutiliza el nonce de la CSP. `robots.txt` es un archivo estático de `wwwroot` con `Disallow: /indice-infinito` (global al sitio y servido por ambos hosts); el `X-Robots-Tag` cubre a quien llega por un enlace compartido y los enlaces al índice llevan `rel="nofollow"`.
- **Sin almacenamiento (IND-06, AC-IND-05).** No hay tablas, repositorio, caché ni cola; el caso de uso `ConsultarIndiceInfinito` no tiene transacción ni depende de la persistencia. La prueba de AC-IND-05 cuenta con un interceptor de comandos de EF (que define la prueba) **cero comandos a la base de datos** para cien páginas pedidas al caso de uso y para cincuenta solicitudes HTTP de un visitante sin sesión (por debajo del límite de SEG-18, cuyo rechazo sí publicaría un evento en la base). El contador de SEG-18 vive en Redis y no almacena páginas ni direcciones.
- **Límite de solicitudes (IND-09, AC-IND-07).** Ninguna lógica propia: las rutas del índice pasan por el pipeline de `transversal-seguridad` y **no están exentas**; más de 60 solicitudes por minuto de un origen sin sesión reciben la respuesta uniforme de límite excedido (SEG-18, `ECO-LIMITE-EXCEDIDO`, 429 con `Retry-After`), contadas por origen y no por ruta; «otra página al azar» cuesta **dos** solicitudes (el 302 y la página). Esa composición (límites, sesión y política de textos) pertenece a otros bloques; la prueba usa un host con ella (T10).
- **Layout, aviso y separación (IND-05, IND-08, AC-IND-04).** La página usa el **layout de la Tienda** (cabecera con su menú, el pie legal con el Libro de Reclamaciones y la lista de sucursales de la Plataforma), pero **su contenido no usa datos del catálogo, del carrito ni de la búsqueda** (IND-08: la experiencia está separada; el menú de la cabecera es de la plataforma y no se considera parte de ella). Toda página (también la del mensaje amable) lleva el aviso «el texto es generado, puede carecer de sentido y no son libros reales ni están a la venta» y un enlace al catálogo real. El módulo no referencia ningún otro módulo de negocio y una prueba de arquitectura lo impone (spec §5). Las acciones llevan `[SinRetorno]` para que la política de textos pendientes no guarde la ruta recibida como destino de retorno de un cliente con sesión.
- **Accesibilidad y movimiento (spec §5).** El texto va en un `<pre lang="es">` enfocable dentro de una región etiquetada («Página generada»); no hay animaciones que retrasen controles y todo efecto respeta `prefers-reduced-motion`; el contraste y los temas son los de `docs/06`; sin atributos `style=` (la CSP lo prohíbe) y con las clases compiladas en el CSS de la Tienda. «Copiar enlace» es una **mejora progresiva** (D08): la dirección se muestra en un campo de solo lectura que funciona sin JavaScript, y un script estático propio (`'self'`, sin inline) arma la URL absoluta con `location.origin` y la ruta canónica **de la página mostrada** (no `location.href`, que en la página del mensaje amable es la dirección no válida), la copia al portapapeles y avisa en una región `role="status"` («Enlace copiado» o, si el navegador lo impide, «Copia el enlace desde el campo»); los controles principales son enlaces y un formulario `GET` con `maxlength="64"` (usabilidad, no defensa).
- Decisiones locales en [`decisions.md`](decisions.md); aplican las decisiones de `web-base` sobre diseño, errores y CSP.

## 2. Capas afectadas

| Capa | Piezas nuevas o modificadas |
| :--- | :--- |
| **Dominio** (`Web/Indice`) | `DireccionDelIndice` (valida de 1 a 64 caracteres de `a-z`, `0-9` y `-`; resultado `Valida`, `Vacia` o `NoValida`, sin guardar el texto no válido), `DireccionAlAzar` (forma `s-p-e-v-pg` con sus rangos, a partir de una función de enteros) y `GeneradorDePaginasDelIndice` (FNV-1a de 64 bits más SplitMix64, 20 × 70 símbolos del alfabeto de 30). Solo tipos del lenguaje; sin cultura, reloj ni estado. |
| **Aplicación** (`Web`) | **Contrato público** en la raíz del módulo, con modelos propios: `IIndiceInfinito` con `Abrir(direccion)` → `PaginaDelIndice` (dirección canónica y texto) o `DireccionNoValida` con una página al azar ya generada, `Entrar(textoDelFormulario?)` → una dirección canónica a la que redirigir, o `DireccionNoValida` con su página al azar, y `ElegirAlAzar(desde?)` → una dirección distinta de la actual. Interior (`Interno`): el caso `ConsultarIndiceInfinito` y la elección de enteros sin sesgo sobre el puerto existente `IAleatoriosSeguros`. Sin transacción, repositorio ni puertos nuevos; no usa otros módulos ni `Plataforma`. |
| **Infraestructura** (`Web`) | Sin cambios (`AleatoriosSeguros` ya implementa el puerto y está registrado). |
| **Api** (Presentación, área Tienda) | `IndiceInfinitoController` (en `Areas/Tienda`, con su `IndiceInfinitoVista : ModeloBaseDePagina`) con las rutas de §4 y `[SinRetorno]`; el middleware de cabeceras del prefijo `/indice-infinito`; `wwwroot/robots.txt`. Sin reglas de negocio. No declara excepciones a la política de textos pendientes ni a los límites. |
| **Web** (vistas) | `Areas/Tienda/Views/IndiceInfinito/Pagina.cshtml` (con `@section Scripts`), `wwwroot/js/indice-infinito.js` y el enlace «El índice infinito» desde el inicio de la Tienda, junto a «Sorpréndeme», como en el prototipo. |
| **Shared** | Sin cambios. |

## 3. Modelo de datos

**Ninguno** (IND-06): sin esquema, tablas, caché ni cola. La página no se guarda en ningún lado y se recalcula en cada solicitud (1400 símbolos: costo despreciable).

## 4. Contratos

La web usa el contrato de módulo; **no se publica una API JSON**.

| Operación | Ruta | Entrada | Salida | Errores |
| :--- | :--- | :--- | :--- | :--- |
| Abrir una página | `GET /indice-infinito/{**direccion}` | dirección en la ruta | página con su dirección, el aviso, el enlace al catálogo, «Otra página al azar» y «Copiar enlace»; si la dirección no es válida, **200** con el mensaje amable y una página al azar | `ECO-LIMITE-EXCEDIDO` (429, SEG-18) |
| Entrada del índice y formulario | `GET /indice-infinito` y `?direccion=` | dirección opcional (se recortan los espacios de los extremos) | 302 a `/indice-infinito/{direccion}` si es válida o a una al azar si viene vacía; si no es válida, la misma respuesta amable | `ECO-LIMITE-EXCEDIDO` |
| Otra página al azar | `GET /indice-infinito/acciones/al-azar?desde={direccion}` | la actual, opcional (si no es válida se ignora) | 302 a una dirección nueva y distinta | `ECO-LIMITE-EXCEDIDO` |

Todas las respuestas del prefijo llevan `X-Robots-Tag: noindex, nofollow` y `Cache-Control: no-store`. Contratos entre módulos: ninguno. Contrato público nuevo: `IIndiceInfinito` (lo consume solo la Presentación). Los estados de error del servidor (429, 503, 4xx del servidor) los da la plataforma de `web-base`.

## 5. Concurrencia, transacciones e idempotencia

Sin estado compartido ni transacciones: el generador es una función pura y cada solicitud es independiente y segura entre hilos. Una repetición de la misma solicitud devuelve el mismo texto (IND-02); el azar es la única fuente no determinista y solo decide la dirección, nunca el texto de una dirección dada.

## 6. Seguridad y alcance

- **Entrada pública validada (SEG-01, CTL-01).** La dirección se valida **antes** de generar nada y por lista permitida; la ruta usa captura total para dar el mensaje amable en lugar de un 404; `%00`, UTF-8 inválido, `%2f`, `..` y direcciones largas son no válidas y no se repiten. Una URL más larga que el límite del servidor (`MaxRequestLineSize`, que se fija de forma explícita) la rechaza el propio servidor con la respuesta de error uniforme, sin repetir la entrada: es un límite conocido (R06).
- **Sin eco (CTL-35, SEG-10).** La garantía es la que controla el módulo: la respuesta, los enlaces, el campo del formulario, los registros y la auditoría **propios del índice** nunca contienen lo recibido; una prueba con un sumidero de registros siembra un centinela y un `%0d%0a`. El rastro que dejan componentes de la Plataforma (el registro de solicitudes del servidor, el proxy y `ManejoDeErrores` o `DenegacionesHttp`, que guardan la ruta de un error inesperado o de una denegación) no lo controla este plan: se registra un hallazgo para el carril A (registrar la plantilla de ruta y no la ruta cruda).
- **Alcance.** Es una página pública de la Tienda, sin datos personales, sesión propia ni cookies propias (el layout fija la cookie antifalsificación de la plataforma); no concede nada ni consulta nada. Los textos legales pendientes de un cliente con sesión siguen la política por defecto de `web-base` (el índice no declara excepciones). **Datos personales:** el único que toca es la huella del origen del contador de SEG-18 y la IP del evento de límite excedido (SEG-08, SEG-25), ya inventariados por `transversal-seguridad`.
- **Abuso y consumo (RNF17, SEG-18, AM06).** El límite de 60 solicitudes por minuto por origen sin sesión lo da la Plataforma (IND-09); el costo por página es fijo y mínimo, así que no hay amplificación. **Rastreo:** `Disallow` en `robots.txt` y `X-Robots-Tag` (el espacio infinito de páginas no está en el modelo de amenazas: lo cubren SEG-18 y esas dos medidas).
- **Navegador (CTL-02, CTL-05, CTL-09).** Sin scripts de terceros ni inline: solo el script estático propio bajo la CSP de `web-base`; sin `Html.Raw`, sin `style=` ni manejadores `on*`; el contenido generado es texto codificado y las cuatro cabeceras de seguridad y la CSP salen en toda respuesta del índice.
- **Separación.** Ninguna referencia a Catálogo, Carrito, Pedidos ni Notificaciones: nada de lo generado puede aparecer en el buscador, el carrito ni los avisos (prueba de arquitectura).
- **Contenido generado.** Son 1400 símbolos de un alfabeto fijo de letras, espacio, coma y punto (sin `<`, `>`, `&`, `/` ni `:`): no pueden formar marcado ni enlaces; un texto aleatorio puede contener por azar una palabra corta real, y un visitante con paciencia (64 bits de semilla, no criptográficos) puede buscar una dirección cuyo texto muestre una palabra elegida; se acepta por el aviso visible, el alfabeto sin enlaces y el texto no clicable (R03).

## 7. Pantallas

Referencia: `prototype/ui-v2/tienda/indice-infinito.html` (identidad del laberinto, bloque de texto, dirección, «Otra página al azar», «Copiar enlace», formulario «Ir a una dirección» y enlace al catálogo); el prototipo calcula la página en el navegador y usa estilos en línea, la producción la calcula en el servidor y usa clases del sistema de diseño.

| Pantalla | Estados que cubre | Prototipo |
| :--- | :--- | :--- |
| El índice infinito: título, aviso, dirección y enlace, página, «Otra página al azar», «Copiar enlace» (con su aviso de éxito o de fallo), formulario «Ir a una dirección», enlace al catálogo real, y el mensaje amable ante una dirección no válida | normal y error amable (la dirección no válida); cargando, vacío, sin permiso y servicio no disponible los resuelve la plataforma de `web-base` (no hay datos que cargar ni permisos propios) | `prototype/ui-v2/tienda/indice-infinito.html` |

## 8. Estrategia de pruebas

| Criterio | Tipo de prueba | Dónde |
| :--- | :--- | :--- |
| AC-IND-01 | Unitaria del generador (vectores publicados, patrón de oro de una dirección fija, solo símbolos del alfabeto, forma de 20 × 70, direcciones distintas dan textos distintos, sin dependencia de la cultura) e integración con dos `GET` iguales | `tests/unit/BabelHub.Ecommerce.UnitTests/Web/`, `tests/integration/.../Web/` |
| AC-IND-02, AC-IND-03 | Unitaria de `ConsultarIndiceInfinito` con bytes fijos en `IAleatoriosSeguros` (la dirección nueva es distinta aun si el azar repite; sin sesgo en los extremos de cada rango) e integración: «al azar» redirige a otra dirección con **otro texto**, la dirección `al-azar` abre su página y otra sesión abre la misma página con el enlace copiado | `tests/unit/.../Web/`, `tests/integration/.../Web/` |
| AC-IND-04 | Integración y E2E: todo estado de la pantalla trae el aviso y el enlace al catálogo real; el contenido no usa datos del catálogo ni del carrito | `tests/integration/.../Web/`, `tests/e2e/` |
| AC-IND-05 | Integración con un interceptor de comandos de EF: cien páginas por el caso de uso y cincuenta solicitudes HTTP de un visitante sin sesión, cero comandos; prueba de arquitectura: el índice no referencia persistencia ni otros módulos | `tests/integration/.../Web/`, `tests/architecture/` |
| AC-IND-06 | Unitaria de `DireccionDelIndice` (64 válida, 65, mayúscula, tilde, símbolos, `/`, vacía, solo espacios en el formulario, `al-azar` válida) e integración: 200 con el mensaje amable y una página al azar, **sin repetir el texto enviado** (un `<script>`, un `%0d%0a`, `%00`, `%ff`, `%2f`, `..`, un `?desde=` repetido y una URL larga), con un sumidero de registros sin el centinela | `tests/unit/.../Web/`, `tests/integration/.../Web/` |
| AC-IND-07 | Integración con el pipeline de seguridad, en una prueba aparte: la solicitud 61 de un origen sin sesión recibe la respuesta de límite excedido en las tres rutas | `tests/integration/.../Web/` |
| Cabeceras y navegador | Integración: `X-Robots-Tag`, `Cache-Control: no-store`, las cuatro cabeceras y la CSP sin `unsafe-inline` en 200, 200 amable, 302 y 429; `Location` con la forma exacta; `robots.txt` con el `Disallow`; prueba estática de las vistas (sin `Html.Raw`, `style=`, script en línea ni `on*=`); E2E sin violaciones de CSP en la consola | `tests/integration/.../Web/`, `tests/e2e/` |
| Pantallas | E2E con Playwright y `ui-review` contra el prototipo (temas, teclado, foco, contraste, movimiento reducido, pantallas pequeñas, sesión con textos pendientes) | `tests/e2e/` |

## 9. Riesgos

| Riesgo | Mitigación |
| :--- | :--- |
| R01. Cambiar el algoritmo cambiaría todas las páginas ya compartidas (IND-02). | Vectores publicados y patrón de oro; un cambio exige volver la spec a Draft. |
| R02. Un rastreador recorre un espacio infinito de páginas. | `Disallow`, `X-Robots-Tag`, `rel="nofollow"` y el límite por origen de SEG-18. |
| R03. El texto aleatorio puede contener por azar una palabra real o inconveniente, o un visitante puede buscar una dirección que la muestre. | Inherente al experimento; alfabeto fijo sin enlaces ni marcado, texto no clicable y aviso visible; aceptado. |
| R04. «Copiar enlace» depende del portapapeles del navegador. | Mejora progresiva: la dirección se muestra y se copia a mano sin JavaScript, con aviso de fallo. |
| R05. Se confunden los textos con libros reales. | Aviso en toda página, ningún dato del catálogo en el contenido y enlace de regreso al catálogo real. |
| R06. Una URL más larga que el límite del servidor recibe el error uniforme del servidor y no el mensaje amable de IND-07. | Se fija `MaxRequestLineSize` de forma explícita, no se repite la entrada y se acepta como límite conocido (hallazgo para el carril A). |

## 10. Verificación contra AGENTS.md

- [x] Dominio sin dependencias externas (solo tipos del lenguaje, sin cultura ni reloj).
- [x] Sin puertos nuevos: el azar usa el puerto existente `IAleatoriosSeguros` del módulo `Web` (sin sobreingeniería).
- [x] Sin referencias entre servicios ni a otros módulos de negocio; módulo `Web` con contrato público (regla 13) y la Presentación sin reglas de negocio (regla 9): llama a `IIndiceInfinito`, no usa el JWT ni el Inventario.
- [x] Sin secretos ni datos personales propios; sin almacenamiento de nada generado.
- [x] Una falla de este módulo no afecta ningún flujo de compra: es una página aislada (regla 10 en espíritu).
- [x] Reglas 11 y 12 no aplican (sin archivos digitales ni Inventario).
- [x] `Shared` sin cambios.
