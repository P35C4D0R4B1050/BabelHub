# Lista manual de accesibilidad: teclado y lector de pantalla

Complementa la revisión automática con axe (`tests/e2e/base/accesibilidad-del-recorrido.spec.ts` y las pantallas de cada spec), que no detecta todo. La ejecuta **una persona** antes de pasar `web-base` a `Done` y después de un cambio grande de interfaz. Respalda WEB-10, WEB-11 y WEB-20 (AC-WEB-07 y AC-WEB-15). Resultado: **0 incumplimientos críticos de nivel AA**; lo que falle se corrige o se registra como riesgo en [`progress/hallazgos.md`](../../progress/hallazgos.md).

El registro se puede llevar en la tabla de la sección 6 o, **sin moderador y para cualquier persona**, en la herramienta [`tools/pruebas-humanas`](../../tools/pruebas-humanas/README.md) (versión 2), que **divide esta lista en dos tareas**: **B1 «Solo con el teclado»** (cualquier persona con un teclado: seis preguntas de 5 puntos que responden a K1 a K5, más «¿hubo algún momento en que no pudiste continuar?») y **B2 «Lector de pantalla»** (1 o 2 personas que ya usan un lector, de preferencia usuarias reales: diez preguntas que responden a L1 a L8, K6 y K7, la guía de la sección 3 paso a paso y la misma pregunta de bloqueo). **La respuesta «sí» a la pregunta de bloqueo es el «incumplimiento crítico»** de la sección 1 (la persona no pudo continuar, un control sin nombre, un error que no se anunció o contenido solo por color o imagen); las respuestas de 1 o 2 puntos son «no críticas». El mapeo exacto está en [`veredicto.md`](../../tools/pruebas-humanas/veredicto.md) §4 B. La herramienta evalúa **el recorrido de compra una vez por persona**, no las 13 pantallas en cuatro sesiones: la lista completa de las secciones 2 a 6 sigue vigente para quien la quiera ejecutar. **No hace falta saber de accesibilidad ni haber usado antes un lector de pantalla** (salvo para B2): las secciones 1 y 2 explican lo necesario; el resto se aprende en media hora.

## 1. Qué se prueba y cómo se decide «pasa» o «falla»

Un **lector de pantalla** es un programa que dice en voz alta lo que hay en la pantalla (títulos, enlaces, botones, campos y mensajes) y permite recorrerla con el teclado o con gestos, sin mirar. Lo usan personas ciegas o con baja visión. La prueba consiste en **hacer las tareas de la tienda usando solo el lector y el teclado** y comprobar que cada cosa se dice y se puede usar.

Para cada punto de la lista (K1 a K7 y L1 a L8) y para cada pantalla eliges **uno** de cuatro resultados:

| Resultado | Cuándo |
| :--- | :--- |
| **Cumple** | Hace lo esperado, sin tener que adivinar. |
| **Crítico AA** | Falla de una de las cuatro maneras de abajo. Impide cumplir el criterio de la spec. |
| **No crítico** | Falla pero la persona puede continuar y entender (algo mejorable). No impide cumplir, pero **se anota**. |
| **No aplica** | El punto no existe en esa pantalla (por ejemplo, tablas en una pantalla sin tablas). |

**Un punto es «Crítico AA» cuando se cumple cualquiera de estas cuatro:**

1. **La persona no puede continuar o terminar** lo que se hace en la pantalla (el foco queda atrapado, un botón no se alcanza con el teclado, el foco se pierde o no se ve dónde está).
2. **Un control sin nombre**: un botón, enlace o campo que el lector dice solo «botón», «enlace» o «sin etiqueta», o con un nombre que no dice qué hace («Aquí», «Clic»).
3. **Un error o un cambio de estado importante que no se anuncia**: por ejemplo, el pago falla o un campo es inválido y el lector no dice nada.
4. **Contenido esencial para comprar que solo se entiende por color o por imagen**, sin texto equivalente (por ejemplo, el stock indicado solo con un círculo verde).

Todo lo demás que falle es **«No crítico»**: encabezados fuera de orden, un texto alternativo pobre pero presente, una etiqueta poco clara que igual se entiende, un anuncio algo largo. **Cada falla, crítica o no, lleva una nota** con qué se dijo o no se dijo y, si puedes, el nombre de la captura o grabación de la pantalla. Si dudas entre crítico y no crítico, marca crítico y anota la duda: quien emite el veredicto lo revisa.

**Meta de la prueba: 0 críticos.** Los no críticos se registran en `progress/hallazgos.md` y no impiden cumplir.

## 2. Preparación

| Elemento | Decisión |
| :--- | :--- |
| Entorno | El local de `tests/e2e/entorno/` con datos ficticios y `sembrar-datos.sh --limpiar`, o la URL de pruebas que indique la persona responsable. |
| Lectores de pantalla | **NVDA** (gratuito) con Firefox o Chrome en Windows; **VoiceOver** (ya viene en el iPhone) con Safari. El celular es la mayoría de la demanda. |
| Navegadores | Las dos últimas versiones de Chrome, Edge, Firefox y Safari (AC-WEB-18; se verifican en la sección «A · Navegadores» de la herramienta). |
| Tema | Cada paso se repite en `babelhub` (claro) y `babelhub-dark` (oscuro). El lector no cambia con el tema; lo que cambia es el contraste y el foco visible. |
| Pantallas | Inicio, catálogo, ficha del libro, carrito, checkout, pago, estado del pedido, ingreso, registro, mi cuenta, mis pedidos, biblioteca digital y Libro de Reclamaciones (13). La herramienta las lista en ese orden. |
| Sesiones | Cuatro: NVDA claro, NVDA oscuro, VoiceOver claro y VoiceOver oscuro. Cada una recorre las 13 pantallas. |
| Duración | Unas 2 horas en total (30 minutos por sesión, con práctica previa). |
| Registro | Una fila por pantalla y sesión (sección 6); en la herramienta, un punto por pantalla. |

**Consejos para quien empieza.** Practica 15 minutos con el lector en una página cualquiera antes de empezar; baja el volumen o usa auriculares; si te pierdes, vuelve al inicio de la página (NVDA: `Ctrl+Inicio`) y repite. Es normal tardar más de lo esperado. Para sentir lo que vive una persona ciega, en VoiceOver activa la **pantalla cortina** (sección 3.2) y prueba una pantalla sin mirar.

## 3. Cómo activar y manejar cada lector

### 3.1 NVDA (Windows)

1. **Descarga** NVDA desde el sitio oficial de NV Access (nvaccess.org). Puedes instalarlo o crear una copia portátil; ambas sirven.
2. **Inicia** NVDA: `Ctrl+Alt+N` (si lo instalaste) o desde el menú Inicio. Dirá «NVDA iniciado». **Para cerrarlo:** `NVDA+Q` y confirma.
3. La **tecla NVDA** es `Insertar` (o `Bloq Mayús` en la distribución de portátil). En esta guía, `NVDA+algo` significa esa tecla y algo a la vez.
4. **Callar la voz:** `Ctrl`. **Leer todo desde aquí:** `NVDA+↓`. **Leer la línea actual:** `NVDA+↑`.
5. **Modo exploración y modo formulario:** en una página NVDA lee con el cursor virtual («modo exploración»); al entrar en un campo de texto cambia solo a «modo formulario» (se oye un sonido) para que puedas escribir. `NVDA+Espacio` alterna a mano.

Atajos mínimos en modo exploración (con `Mayús` va hacia atrás):

| Qué | Tecla |
| :--- | :--- |
| Siguiente encabezado | `H` (por nivel: `1` a `6`) |
| Siguiente región (banner, navegación, principal, pie) | `D` |
| Siguiente enlace / botón | `K` / `B` |
| Siguiente campo de formulario | `F` |
| Siguiente tabla | `T` |
| Lista de elementos de la página (encabezados, enlaces, regiones…) | `NVDA+F7` |
| Activar un enlace o botón | `Enter` (o `Espacio` en un botón) |

Para esta lista, NVDA se usa **con teclado solo**: no toques el ratón.

### 3.2 VoiceOver (iPhone)

1. **Activa** VoiceOver en Ajustes › Accesibilidad › VoiceOver. Con un **atajo** es más cómodo: Ajustes › Accesibilidad › Atajo de accesibilidad › VoiceOver; luego **triple clic del botón lateral** (o del botón de inicio) lo activa y desactiva.
2. **Gestos básicos** (con VoiceOver activo cambian): **tocar una vez** selecciona un elemento y lo lee; **doble toque** (en cualquier lugar) lo activa; **deslizar a la derecha o a la izquierda** pasa al elemento siguiente o anterior; **deslizar con tres dedos** desplaza la página.
3. **Rotor:** gira dos dedos sobre la pantalla como una perilla; elige «Encabezados», «Enlaces», «Controles de formulario» o «Regiones»; **desliza hacia arriba o abajo** para saltar al anterior o siguiente de ese tipo.
4. **Leer desde arriba:** deslizar dos dedos hacia arriba. **Pausar o seguir la lectura:** un toque con dos dedos.
5. **Escape** (cerrar un diálogo): dibuja una «Z» con dos dedos.
6. **Pantalla cortina** (opcional, para probar sin mirar): triple toque con tres dedos apaga la pantalla sin apagar el lector; repite para encender.
7. **Desactiva** VoiceOver con el mismo atajo (triple clic).

Para el teclado en el iPhone, la lista se adapta a gestos (la herramienta ya muestra el texto de VoiceOver); un teclado externo es opcional.

## 4. Puntos que se comprueban

### 4.1 Teclado (sin ratón; en VoiceOver, con gestos)

Para cada pantalla del recorrido:

| Id | Qué se comprueba |
| :---: | :--- |
| **K1** | Con `Tab` desde el principio, el primer destino es «Saltar al contenido» y lleva al contenido principal. *(Solo NVDA.)* |
| **K2** | El orden de foco sigue el orden visual, sin trampas de teclado, y **todo control muestra un foco visible** (un contorno que se ve). *(VoiceOver: deslizar a la derecha recorre la pantalla en orden y el cursor encuadra cada control.)* |
| **K3** | Los controles se operan con `Enter` o `Espacio`; los grupos de opción (medio de pago) con las flechas. *(VoiceOver: doble toque.)* |
| **K4** | Los diálogos y avisos con foco inicial lo reciben, `Esc` los cierra cuando corresponde y, al cerrar, el foco vuelve al control que los abrió. *(VoiceOver: gesto de escape.)* |
| **K5** | Los errores de un formulario llevan el foco al aviso, que dice qué corregir **sin repetir lo escrito** (ni contraseñas ni datos). |
| **K6** | Con «reducir movimiento» activo en el sistema operativo no hay animaciones. |
| **K7** | Con el zoom al 200 % y a 320 px de ancho no hay pérdida de contenido ni desplazamiento horizontal. *(VoiceOver: con el texto más grande.)* |

### 4.2 Lector de pantalla

| Id | Qué se comprueba | Esperado |
| :---: | :--- | :--- |
| **L1** | Título y encabezados | Cada página tiene un único nivel 1 y encabezados en orden; el título de la pestaña dice la pantalla. |
| **L2** | Regiones | Hay `banner`, `navigation` con nombre, `main` y `contentinfo`. |
| **L3** | Formularios | Cada campo se anuncia con su etiqueta, su ayuda y si es obligatorio; los errores se anuncian al aparecer (`aria-live`). |
| **L4** | Estado | El estado nunca depende solo del color: se anuncia con texto («Pago pendiente», «Hay stock en la red»). |
| **L5** | Tablas | Tienen título (`caption`), cabeceras con alcance y las regiones desplazables se anuncian con su nombre. |
| **L6** | Cambios dinámicos | El estado del pago y los avisos se anuncian sin mover el foco. |
| **L7** | Imágenes | Las portadas tienen texto alternativo útil; las decorativas, vacío (no se leen). |
| **L8** | Enlaces y botones | El nombre dice la acción («Ver el pedido», no «Aquí»). |

**Cómo comprobar cada uno con el lector (ejemplos):**

- **L1:** con NVDA pulsa `H` repetidamente y escucha: debe haber un solo «encabezado nivel 1» y los siguientes bajan de nivel de uno en uno. En VoiceOver, rotor «Encabezados».
- **L2:** con `D` recorre las regiones: debes oír «banner», «navegación», «principal» y «pie de página» (o equivalentes). Falla si la página no tiene región principal.
- **L3:** con `F` entra en cada campo: debes oír su nombre («Correo electrónico»), si es obligatorio y la ayuda. Provoca un error (deja un campo vacío y envía): debes oír el aviso **sin tocar nada**.
- **L4:** busca estados (stock, estado del pedido, pago): el lector debe decir el texto («En 3 sucursales», «Pago pendiente»), no solo «imagen» o nada.
- **L6:** en el pago, el cambio de estado («Estamos confirmando tu pago») debe anunciarse solo, sin que se mueva el foco.
- **L7:** pasa por una portada: debes oír algo útil («Portada de *El zorro y el cóndor del Ande*»). Falla si oyes el nombre del archivo.

## 5. Reglas de registro

1. **Crítico y no crítico llevan nota**: qué dijo (o dejó de decir) el lector, en qué control, y el nombre de la captura o la grabación (guardadas fuera del repositorio).
2. Si un punto falla en un tema y no en el otro (por ejemplo, un foco invisible solo en oscuro), anótalo en la sesión de ese tema.
3. Al terminar cada sesión, **exporta el JSON** desde «Datos»; no confíes solo en la memoria del navegador.
4. Lo que falle se corrige en una tarea nueva (por el flujo de spec) o se registra como riesgo en `progress/hallazgos.md`; no se debilita la prueba para que pase.

## 6. Registro de la ejecución

Se vuelca desde el JSON exportado (la herramienta genera esta tabla en su resumen en Markdown).

| Fecha | Persona | Navegador y lector | Pantalla o recorrido | Resultado | Observación y registro del hallazgo |
| :--- | :--- | :--- | :--- | :--- | :--- |
| *(pendiente: la ejecuta la persona responsable)* | | | | | |
