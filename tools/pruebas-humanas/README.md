# Pruebas humanas de BabelHub (versión 2)

Una página única, `index.html`, para que **cualquier persona** pruebe la tienda **sola**, sin moderador y sin saber de tecnología, y devuelva su resultado en un archivo. Cubre lo que ningún agente puede hacer en `web-base`: T32b (navegadores), T33b (teclado y lector de pantalla) y T34b (usabilidad).

La persona ve **una sola pantalla a la vez**: *Bienvenida → tareas → Gracias*. Todo se guarda solo y se puede seguir otro día.

| Lo que ve la persona | Qué mide | Tarea y criterio |
| :--- | :--- | :--- |
| **1. Compra un libro** (unos 15 min) | Usabilidad sin moderador: cronómetro automático, «¿pediste ayuda?», «¿cuántas veces te perdiste?», SUS de 10 frases (0–100) | T34b · AC-WEB-17: ≥ 9 de 10 sin ayuda, SUS promedio ≥ 68, < 1 error por tarea |
| **2. Recorre la compra paso a paso** (unos 10 min) | Que la compra se completa **en el navegador de esa persona**, con 8 pasos y «¿pudiste hacerlo?» | T32b · AC-WEB-18: las seis combinaciones (Chrome, Edge, Firefox, Safari en Mac, Chrome en Android, Safari en iPhone) |
| **3. Usa la tienda solo con el teclado** (unos 10 min) | Seis preguntas de 5 puntos y «¿hubo un momento en que no pudiste continuar?» | T33b · AC-WEB-15: 0 incumplimientos críticos de nivel AA |
| *(opcional)* **Lector de pantalla** (solo si ya lo usa; 1 o 2 personas) | Diez preguntas de 5 puntos, guía de NVDA y VoiceOver, y la misma pregunta de bloqueo | T33b · AC-WEB-15 |

El orden recomendado pone la compra de usabilidad **primero**: mide la primera vez que alguien usa la tienda y la compra guiada la haría más fácil. La app lo avisa y marca la sesión si no se respetó.

**Qué NO es.** No inventa resultados ni trae datos de ejemplo: arranca vacía. Su «veredicto» es una ayuda; **el definitivo lo emite quien lee los JSON** según [`veredicto.md`](veredicto.md). **La ayuda y los errores de la tarea de usabilidad son autodeclarados** (no hay moderador): por eso se recomienda observar 2 de las 10 sesiones ([`09-pruebas-de-usabilidad.md`](../../docs/06-experiencia-de-usuario/09-pruebas-de-usabilidad.md) §17).

## Privacidad (léelo antes de compartirla)

- **No envía nada a ningún sitio.** Un solo archivo, sin recursos externos, sin telemetría, sin cookies y con una política de contenido que **impide cualquier conexión de red** (`connect-src 'none'`). Los datos viven **solo en el almacenamiento local de ese navegador** y salen únicamente cuando la persona pulsa «Descargar mi resultado».
- **Solo códigos, nunca nombres.** Cada persona recibe un código aleatorio (`EV-K7Q3`); el apodo es opcional. La app avisa, mientras se escribe y antes de enviar, si un texto libre parece un correo o un teléfono.
- **Datos automáticos** (para reproducir defectos y calcular la cobertura): navegador y versión, sistema, tipo de aparato, tamaño de pantalla, fecha y hora, y el *user agent*. No se guarda la zona horaria ni el idioma. **Perfil en rangos** (edad, frecuencia de compra por internet) y si usa tecnología de apoyo (dato que puede revelar una discapacidad: es opcional y se puede responder «prefiero no decirlo»).
- **Consentimiento breve** al empezar la tarea de usabilidad (tres casillas, texto en lenguaje llano). No se graba pantalla ni voz.
- Ley N.º 29733: sin datos personales identificables. Inventario en [`docs/05-seguridad/04-privacidad-y-datos-personales.md`](../../docs/05-seguridad/04-privacidad-y-datos-personales.md).

## Cómo compartirla

La página es un archivo estático: **se comparte como enlace o como archivo**. Elige una forma.

### 1. Como enlace (lo más simple para quien prueba)

Sirve el archivo desde cualquier lugar (abajo) y comparte el enlace **con parámetros**, para que la persona no tenga que escribir nada:

```text
https://<donde-esté-alojada>/?tienda=https://<tienda-de-pruebas>&v=2026-11-23&ronda=1&para=correo@ejemplo.pe
```

| Parámetro | Qué es | Notas |
| :--- | :--- | :--- |
| `tienda` | Dirección de la tienda de pruebas | Debe ser `http` o `https` y sin usuario ni clave. Si falta o no es válida, la app la pregunta **una sola vez**. |
| `v` | Versión o fecha de la tienda que se prueba | Opcional (máx. 60 caracteres). Distingue la tienda de la ronda 1 de la de la ronda 2. |
| `ronda` | `1` o `2` | Opcional. Quien reúne los resultados también puede asignarla persona por persona. |
| `para` | A quién enviar el resultado | Opcional; se muestra como texto en la pantalla final (máx. 80 caracteres). |

La tienda se abre **desde un botón de la propia app**, en otra pestaña, sin pasar el origen ni la ventana de la herramienta (`noopener noreferrer`).

### 2. Como archivo

Pasa `index.html` por WhatsApp, correo o memoria; la persona lo abre con doble clic (o desde la app de archivos del celular). Sin enlace, la app preguntará la dirección de la tienda al empezar. Los datos se guardan en ese navegador, para ese archivo: **abrirla siempre de la misma forma**.

### 3. En la red local (para probar en celulares reales desde un equipo)

```powershell
node tools/pruebas-humanas/servir.mjs          # puerto 8090; `servir.mjs 8123` para otro
```

El servidor **solo entrega `index.html`** (ignora los parámetros de la dirección) y escucha en toda la red: úsalo solo en una red de confianza y ciérralo con `Ctrl+C`. Imprime las direcciones, por ejemplo `http://192.168.1.20:8090/`; añade los parámetros: `http://192.168.1.20:8090/?tienda=…`. El equipo y los móviles deben estar en la misma wifi y el cortafuegos de Windows debe dejar entrar a Node en redes privadas. Una vez cargada, la página ya no necesita el servidor.

### 4. Alojada como página estática (más adelante, `babel-hub.shop`)

Cuando el dominio `babel-hub.shop` esté en uso se puede publicar `index.html` como **página estática** en un subdominio o ruta propios (por ejemplo `pruebas.babel-hub.shop`), **separada de la tienda y sin acceso a sus cookies**. No necesita cabeceras especiales (declara su política de contenido), no tiene backend y no guarda nada en el servidor. Si se aloja, añade las cabeceras `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer` y `frame-ancestors 'none'`, como hace `servir.mjs`. Hoy no hay despliegue: esto queda como decisión de la persona responsable (`progress/decisiones-delegadas.md`, DD-84).

> En una dirección `http://` de la red local el navegador desactiva «Compartir» y el portapapeles (solo en HTTPS o `localhost`). No es un problema: **«Descargar mi resultado» siempre funciona**.

## Cómo la usa una persona (lo que se le dice)

1. Abre el enlace. **Confirma su navegador** («Detectamos Edge 131 en Windows 11, ¿es correcto?», o lo corrige) y pulsa **Empezar**.
2. Hace las tareas **en orden**, una por una; en cada paso la pantalla le dice qué hacer. Puede dejarlo y seguir otro día.
3. Al terminar, pulsa **«Descargar mi resultado»** y envía los dos archivos por WhatsApp o correo a quien se lo pidió (instrucciones en pantalla).

Cada persona prueba con **su propio navegador**: la cobertura de las seis combinaciones se calcula al **reunir** los archivos.

## Cómo reunir los resultados (quien organiza)

En el equipo donde quieras juntar todo: pie de la página › **«Para quien reúne los resultados»** (`#/avanzado`).

1. **Importa** uno o varios `.json` (o pega el texto) y revisa el informe: *agregadas*, *actualizadas* y *conservadas*. Cada persona tiene un **id único**: si ya está, gana la versión más reciente; nada se borra; importar el mismo archivo dos veces no duplica nada. Se rechazan los archivos de la versión 1.
2. Mira la **cobertura**: la tabla de las seis combinaciones indica qué **falta** («Faltan: Firefox en computadora; Safari en iPhone») y de cuáles falta una **segunda versión** estable, y el resumen de teclado, lector y usabilidad.
3. Asigna la **ronda** de cada persona si el enlace no la traía.
4. **Descarga todo lo reunido** (JSON + resumen). Ese JSON es el que se lee con [`veredicto.md`](veredicto.md) para emitir el veredicto y se guarda en `progress/evidencias/web-base/pruebas-humanas/<fecha>/` (solo contiene códigos). **Antes**, revisa la lista «Posibles datos personales» si aparece.

## Borrar los datos locales

«Reiniciar todo» (botón de la cabecera, visible en toda pantalla, con confirmación; vuelve al inicio), «Borrar mis datos» (pantalla final) o «Borrar los datos locales…» (pantalla de reunir) borran todo lo guardado en ese navegador. Hazlo en un dispositivo prestado. Los archivos ya descargados no se tocan.

## Si modificas `index.html`

La política de contenido admite solo el hash del único `<script>`. Tras **cualquier cambio al JavaScript** ejecuta:

```powershell
node tools/pruebas-humanas/fijar-csp.mjs
```

Sin eso el navegador no ejecuta el script (la página queda en blanco) y la prueba `privacidad-y-sin-red` falla con ese aviso.

## Verificación de la herramienta

Pruebas automáticas en `tests/e2e/pruebas-humanas/` (Playwright, Chromium de escritorio y de teléfono y WebKit de iPhone; no necesitan el entorno de la tienda; sus salidas van a la carpeta temporal del sistema, no al repositorio):

```powershell
cd tests/e2e
npx playwright test --config pruebas-humanas/playwright.config.ts
```

Cubren: detección de navegador con *user agents* simulados (Chrome, Edge, Brave, Opera, Firefox, Safari de Mac y de iPhone, Chrome/Edge/Firefox en iOS, Android, Samsung), el recorrido de una persona (progreso, guardado y retomar, tareas, saltar el teclado), el cálculo del SUS, la cobertura y los criterios de AC-WEB-15, 17 y 18, la exportación e importación (JSON y Markdown, fusión, archivos hostiles, versión 1), la ausencia de red y de `eval`, y la accesibilidad (axe en cada pantalla y paso, ambos temas, teclado, 320 y 375 px). **No contienen ni inventan resultados de personas**: sus datos de ejemplo viven solo en las pruebas.
