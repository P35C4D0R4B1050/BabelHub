# Pruebas humanas de BabelHub

Una página única, `index.html`, para que **las personas** ejecuten y documenten los tres bloques de pruebas de `web-base` que ningún agente puede hacer (T32b, T33b y T34b), y devuelvan el resultado en un archivo que otra persona o un agente lee para emitir el veredicto.

| Bloque | Tarea y criterio | Qué hace la persona | Guía |
| :--- | :--- | :--- | :--- |
| **A · Navegadores y móviles** | T32b · AC-WEB-18: la compra se completa en Chrome, Edge, Firefox, Safari de macOS, Chrome Android y Safari de iPhone | Recorre 8 pasos de la compra en cada combinación y anota resultado, defectos y capturas. | [`protocolo-manual.md`](../../progress/evidencias/web-base/protocolo-manual.md) |
| **B · Teclado y lector de pantalla** | T33b · AC-WEB-15: 0 incumplimientos críticos de nivel AA | Recorre 13 pantallas con NVDA y VoiceOver, en tema claro y oscuro, y marca 15 puntos por pantalla. | [`10-lista-manual-de-accesibilidad.md`](../../docs/06-experiencia-de-usuario/10-lista-manual-de-accesibilidad.md) |
| **C · Usabilidad (moderador)** | T34b · AC-WEB-17: al menos 9 de 10 sin ayuda, SUS promedio ≥ 68 y menos de 1 error por tarea | Modera dos rondas de 5 personas con guion, cronómetro, errores, SUS y consentimiento. | [`09-pruebas-de-usabilidad.md`](../../docs/06-experiencia-de-usuario/09-pruebas-de-usabilidad.md) |

**Qué NO es.** No inventa resultados ni trae datos de ejemplo: arranca vacía. Su «veredicto» es una ayuda para quien anota; **el definitivo lo emite quien lee el JSON exportado** según [`veredicto.md`](veredicto.md).

## Privacidad (léelo antes de usarla)

- **No envía nada a ningún sitio.** Es un solo archivo sin recursos externos, sin telemetría, sin cookies y con una política de contenido que **impide cualquier conexión de red** (`connect-src 'none'`). Los datos viven **solo en el almacenamiento local de ese navegador** y salen únicamente cuando tú pulsas «Descargar», «Compartir» o «Copiar».
- **Solo códigos, nunca nombres.** Cada persona de usabilidad es un código (`P1`, `P2`…) y quien evalúa es un código (`EV1`…). No escribas nombres, correos, teléfonos ni documentos; antes de exportar, la herramienta avisa si ve algo que parece un correo o un teléfono.
- **El consentimiento es verbal** (texto en la guía) y queda como casillas marcadas. Las grabaciones de pantalla o voz, si las hubo, se guardan fuera del repositorio.
- Ley N.º 29733 de Protección de Datos Personales: la herramienta **no recopila datos personales identificables** (solo códigos, perfil, si usa tecnología de apoyo —«sí» o «no», un dato que puede revelar una discapacidad—, tiempos, errores y respuestas). Si se **graba voz o pantalla** (opcional, con permiso), esa grabación sí es dato personal: vive fuera de la herramienta y del repositorio y se borra al cerrar el análisis. Inventario completo en [`docs/05-seguridad/04-privacidad-y-datos-personales.md`](../../docs/05-seguridad/04-privacidad-y-datos-personales.md).
- Lo que exporta el JSON además de tus respuestas: el *user agent* del navegador y el tamaño de pantalla (para reproducir defectos de T32b), un id aleatorio del dispositivo y tu código de evaluador. No exporta la zona horaria, el idioma ni nada que identifique a una persona.

## Cómo abrirla

Funciona **sin conexión y sin instalar nada**. Elige una de estas formas.

### En el mismo equipo (escritorio)

Abre `tools/pruebas-humanas/index.html` con doble clic: se abre en tu navegador como archivo. Los datos se guardan en ese navegador, para ese archivo.

> **Ojo:** el almacenamiento local depende de la dirección. Abrirla siempre de la **misma** forma (siempre como archivo, o siempre desde el mismo servidor y puerto) o no verás lo que guardaste antes.

### En un iPhone o un Android (red local)

Los móviles no pueden abrir el archivo del equipo directamente; hay que servirlo por la red.

1. Conecta el equipo y los móviles a la **misma red wifi** (de confianza: no una red pública).
2. En el equipo, desde la raíz del repositorio, ejecuta (necesita Node, que ya usa `tests/e2e`):

   ```powershell
   node tools/pruebas-humanas/servir.mjs
   ```

   El servidor usa el puerto 8090 (`node tools/pruebas-humanas/servir.mjs 8123` para otro) y **solo entrega `index.html`**. Imprime las direcciones, por ejemplo `http://192.168.1.20:8090/`.
3. En el móvil, abre esa dirección en **Safari** (iPhone) o **Chrome** (Android). Una vez cargada la página ya no necesita el servidor (no pide nada más a la red), pero **no recargues** si el servidor está apagado.
4. Si no carga, revisa que el móvil y el equipo estén en la misma red y que el **cortafuegos de Windows** permita a Node recibir conexiones en redes privadas.
5. Al terminar, cierra el servidor con `Ctrl+C`.

### Desde cualquier servidor estático

Es un archivo estático: sirve cualquier servidor que entregue `index.html` (por ejemplo, el de `python -m http.server --directory tools/pruebas-humanas` si tienes Python). No necesita cabeceras especiales; la página declara su propia política de contenido.

> En una dirección `http://` de la red local el navegador desactiva «Compartir» y el portapapeles (solo funcionan en HTTPS o `localhost`). No es un problema: **«Descargar JSON» siempre funciona**, y «Ver el texto» deja copiar a mano.

## Cómo se usa

1. En **Inicio**, escribe tu **código de evaluador** (por ejemplo `EV1`), el **dispositivo** (modelo y sistema) y la **versión probada** de la tienda.
2. Entra en el bloque que te toca y sigue las pantallas. Todo lo que escribes **se guarda solo**, en el momento (se ve «Guardado en este dispositivo»).
3. **Exporta al terminar cada sesión**, no solo al final: el navegador puede borrar los datos (Safari en iPhone lo hace si pasa mucho tiempo sin usarlo, y el modo privado no guarda nada).
4. Si varias personas o dispositivos aportan datos, **importa y combina** los JSON en uno solo (abajo).

El orden recomendado: **A** (1,5 h), **B** (2 h) y **C** (dos rondas, unas 4 h cada una). Son independientes y pueden hacerlas personas distintas en dispositivos distintos.

## Qué devolver

**Devuelve el archivo JSON** (`babelhub-pruebas-humanas_<evaluador>_<fecha>-<hora>.json`), uno por dispositivo o, mejor, **uno combinado**. Es lo que lee quien emite el veredicto. El resumen en Markdown (`.md`) es opcional: sirve para revisar rápido y pegarlo en `progress/`.

1. Ve a **Datos**.
2. Pulsa **Descargar JSON** (o «Compartir JSON» en un móvil con HTTPS). En el iPhone, el archivo queda en la app **Archivos**; compártelo por AirDrop, correo o la forma que acuerdes.
3. Entrega el archivo a quien emite el veredicto, o déjalo en `progress/evidencias/web-base/pruebas-humanas/` (la carpeta se crea al hacerlo). **Antes**, revisa la lista «Posibles datos personales» que muestra la herramienta: si aparece algo, corrígelo y vuelve a exportar.

## Combinar los datos de varios dispositivos

En el equipo donde quieras reunir todo (cualquiera): **Datos › Importar y combinar**, elige uno o varios `.json` (o pega el texto) y revisa el informe: *agregados*, *actualizados* y *conservados*.

- Cada corrida, sesión y persona tiene un **id único**. Si el id es nuevo, se agrega; si ya existe, gana la versión **más reciente**; nada se borra. Importar el mismo archivo dos veces no duplica nada.
- La herramienta **avisa de códigos de persona repetidos** (dos evaluadores que usaron `P1`): corrígelos antes de exportar el combinado.
- Después de combinar, **exporta de nuevo**: ese es el archivo a devolver.

## Borrar los datos locales

**Datos › Borrar los datos locales…** borra todo lo guardado en ese navegador (pide confirmación y avisa si hay cambios sin exportar). **Hazlo al terminar** si el dispositivo es prestado. Los archivos que ya exportaste no se tocan.

## Cosas que conviene saber

- **Cronómetro:** usa la hora del dispositivo; sigue contando si recargas la página.
- **Tema:** el botón «Tema» alterna entre el del sistema, claro y oscuro; no cambia lo que mides.
- **Un solo navegador por vez:** los datos de Chrome no se ven en Safari; usa «Datos» para moverlos.
- **Nada de esto sustituye a la guía:** la herramienta ordena y calcula; la persona observa y decide.

## Si modificas `index.html`

La política de contenido admite solo el hash del único `<script>`. Tras **cualquier cambio al JavaScript** ejecuta:

```powershell
node tools/pruebas-humanas/fijar-csp.mjs
```

Sin eso el navegador no ejecuta el script (la página queda en blanco) y la prueba `privacidad-y-sin-red` falla con ese mismo aviso.

## Verificación de la herramienta

Pruebas automáticas en `tests/e2e/pruebas-humanas/` (Playwright, Chromium de escritorio y de teléfono y WebKit de iPhone; no necesitan el entorno de la tienda):

```powershell
cd tests/e2e
npx playwright test --config pruebas-humanas/playwright.config.ts
```

Cubren: cálculo del SUS, exportación e importación (JSON y Markdown, fusión, archivos hostiles), flujos de los tres bloques, ausencia de red y de `eval`, y accesibilidad (axe en ambos temas, teclado, 320 y 375 px). **No contienen ni inventan resultados de personas**: sus datos de ejemplo viven solo en las pruebas.
