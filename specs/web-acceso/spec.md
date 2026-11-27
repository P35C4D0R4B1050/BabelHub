---
id: web-acceso
titulo: Ayudas de contraseña, acceso del personal y código QR del segundo factor
estado: InProgress
aprobado_por: Claude (delegado por Diego Amiquero)
aprobado_en: 2026-11-27
plan_aprobado_por: Claude (delegado por Diego Amiquero)
plan_aprobado_en: 2026-11-27
servicio: E-commerce
prefijo: ACC
origen: [RNF15, RNF13, RNF25, RF27]
calidad: [AC04, AC06]
depende: [web-base, identidad-seguridad, web-informacion-del-comercio, transversal-seguridad]
---

# Spec — Ayudas de contraseña, acceso del personal y código QR del segundo factor

> **Qué y por qué, nunca cómo.** Esta spec describe comportamiento observable. Complementa a `web-base` e `identidad-seguridad` sin modificar su texto: la política de contraseñas (IDE-15, IDE-59), el segundo factor (IDE-21, IDE-37), las pantallas de acceso y de cuenta (WEB-20, WEB-35), la verificación de persona (SEG-31) y el pie (WEB-08, COM-01) siguen vigentes y aquí se les agregan ayudas.

## 1. Propósito

Que una persona elija una contraseña mejor sin adivinar las reglas, vea lo que escribe cuando lo necesita y configure su segundo factor escaneando un código; y que el personal que llega a la tienda encuentre su propio acceso. Nace de las observaciones de la persona responsable sobre la tienda desplegada (2026-11-27).

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Ayuda de reglas, barra de seguridad y aviso de coincidencia en las pantallas donde una persona define una contraseña nueva: registro, recuperación, cambio desde la cuenta y cambio de la contraseña temporal. | Cambiar la política de contraseñas (IDE-15, RNF15 y CTL-15 siguen igual) o imponer reglas de composición. |
| Botón para ver u ocultar el contenido en todos los campos de contraseña de la interfaz, incluido el ingreso, y protección de lo escrito frente a las ayudas de escritura del navegador. | Agregar un campo de repetición donde hoy no existe (el registro tiene un solo campo). |
| Enlace «Acceso del personal» en el pie de la tienda y en su pantalla de ingreso. | Cambiar quién puede ingresar en cada host, el acceso previo al host de gestión o los mensajes de error del ingreso (WEB-24, SEG-06). |
| Código QR del segundo factor junto a la clave en texto. | Cambiar cómo se genera, guarda o valida el segundo factor, o recuperar el segundo factor de un usuario (IDE-23, IDE-38, IDE-49). |
| | Las pantallas de administración, que no definen contraseñas (muestran una temporal generada por el sistema, IDE-23). |
| | Comprobar mientras se escribe si la contraseña es común o filtrada. |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| ACC-01 | Ubicuo | El sistema deberá mostrar en cada pantalla donde se define una contraseña nueva, bajo el primer campo de contraseña nueva, una ayuda que presente como **obligatorio** tener de 10 a 128 caracteres y como **recomendado** usar mayúsculas, minúsculas, números y símbolos y preferir una frase larga, y que diga que al guardar se comprueba que no sea una contraseña común o filtrada. | RNF15, RNF13 |
| ACC-02 | No deseado | Si una contraseña nueva no cumple una recomendación o su nivel es bajo, entonces el sistema no deberá impedir ni demorar el envío ni rechazarla por ese motivo: solo el servidor la rechaza, por la política de IDE-15 o porque su repetición no coincide, y ningún campo declarará una longitud mínima que el navegador exija. | RNF15 |
| ACC-03 | Estado | Mientras el navegador ejecuta los guiones de la página y el primer campo de contraseña nueva tiene contenido, el sistema deberá mostrar bajo ese campo una barra de seguridad con uno de cuatro niveles, Débil, Aceptable, Buena o Fuerte, siempre con su texto, con un color que avanza del rojo (Débil) al verde (Fuerte) y con un texto que dice que es solo una guía. | RNF15, RNF13 |
| ACC-04 | Ubicuo | El sistema deberá calcular el nivel con las reglas de la sección 5, que valoran primero la longitud y después la variedad de caracteres y restan peso a los patrones previsibles (palabras comunes y el nombre del comercio, también con números o símbolos añadidos o letras sustituidas, repeticiones y secuencias). | RNF15 |
| ACC-05 | Ubicuo | El sistema deberá calcular el nivel y la coincidencia en el dispositivo de la persona, sin enviar, guardar ni registrar la contraseña mientras se escribe; la contraseña solo sale del dispositivo al enviar el formulario. | RNF15 |
| ACC-06 | Estado | Mientras el navegador ejecuta los guiones de la página y el campo de repetición de una contraseña nueva tiene contenido, el sistema deberá avisar con texto e icono si las dos contraseñas son exactamente iguales o todavía no, actualizando el aviso cuando cambia cualquiera de los dos campos y sin impedir el envío. | RNF13 |
| ACC-07 | Ubicuo | El sistema deberá ofrecer en cada campo de contraseña, incluido el del ingreso, un botón para ver u ocultar su contenido, que no envíe el formulario ni borre lo escrito, no impida pegar ni autocompletar, anuncie a un lector de pantalla qué campo muestra y si está visible, y deje el contenido oculto al abrir la pantalla, al enviar y al volver desde el historial. | RNF13, RNF25 |
| ACC-08 | No deseado | Si el navegador no ejecuta los guiones de la página, entonces el sistema deberá mostrar la ayuda de ACC-01, no agregar ninguna condición para completar y enviar los formularios y no mostrar barras, avisos ni botones que no funcionen. | RNF13 |
| ACC-09 | Ubicuo | El sistema deberá presentar la ayuda, la barra, el aviso de coincidencia y el botón de ver u ocultar según las pautas WCAG 2.2 nivel AA en ambos temas, sin depender solo del color, y anunciar a un lector de pantalla solo los cambios de nivel y de coincidencia. | RNF25 |
| ACC-10 | Opcional | Donde esté configurada la dirección del acceso del personal, el sistema deberá mostrar en el pie de todas las páginas de la tienda y en la pantalla de ingreso de la tienda el enlace «Acceso del personal» hacia esa dirección, el mismo para cualquier persona, con sesión o sin ella y sin depender de ninguna cuenta ni de lo escrito, y no deberá mostrarlo en el host de gestión. | RNF13 |
| ACC-11 | No deseado | Si la dirección del acceso del personal no está configurada (ausente, vacía o solo con espacios), entonces el sistema deberá arrancar y funcionar igual sin mostrar el enlace. | RNF13 |
| ACC-12 | No deseado | Si la dirección del acceso del personal está configurada y no cumple las reglas de la sección 5, entonces el sistema deberá negarse a arrancar e informar el nombre de la variable de configuración, sin mostrar su valor. | RNF15 |
| ACC-13 | Evento | Cuando el sistema muestra la clave para configurar el segundo factor, tanto en el ingreso como en la cuenta, deberá mostrar además, junto a la clave en texto y al enlace para abrir la aplicación, un código QR que contenga la misma dirección de configuración que ese enlace. | RNF15, RF27 |
| ACC-14 | Ubicuo | El sistema deberá generar el código QR por sí mismo, sin enviar la clave ni la dirección de configuración a ningún tercero, sin guardar el código ni la dirección y sin registrarlos, dentro de la misma respuesta que muestra la clave y prohibiendo que esa respuesta se guarde en caché. | RNF15 |
| ACC-15 | Ubicuo | El sistema deberá presentar el código QR con un texto alternativo que no revele la clave, oscuro sobre fondo claro y con su margen en ambos temas, del tamaño mínimo de la sección 5 y visible sin que el navegador ejecute guiones. | RNF25 |
| ACC-16 | No deseado | Si el código QR no puede generarse, entonces el sistema deberá mostrar la clave en texto y el enlace como siempre, sin impedir configurar el segundo factor y sin registrar la clave ni la dirección. | RNF15, RNF13 |
| ACC-17 | Ubicuo | El sistema deberá hacer que ningún campo de contraseña, oculto o visible, active la corrección ortográfica, la autocorrección, la mayúscula automática ni las sugerencias de escritura del navegador o del teclado, con o sin guiones. | RNF15 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-ACC-01 | ACC-01, ACC-02, ACC-08 | **Dado** el registro, la recuperación, el cambio de contraseña de la cuenta y el cambio de la contraseña temporal, **cuando** se abre cada pantalla sin ejecutar guiones, **entonces** bajo el primer campo de contraseña nueva se lee como obligatorio de 10 a 128 caracteres, como recomendado mayúsculas, minúsculas, números, símbolos y una frase larga, y que al guardar se comprueba que no sea común o filtrada; los campos no declaran una longitud mínima que el navegador exija; y no hay barras, avisos de coincidencia ni botones de ver contraseña. |
| AC-ACC-02 | ACC-02 | **Dado** una contraseña de al menos 10 caracteres cuyo nivel es Débil y sin mayúsculas, números ni símbolos, **cuando** la persona envía el formulario, **entonces** se envía sin que ningún guion lo detenga ni lo demore y el servidor decide solo con IDE-15 y la repetición; y **dado** una de 9 caracteres o dos distintas, **entonces** el formulario también se envía y el servidor la rechaza indicando el motivo. |
| AC-ACC-03 | ACC-03 | **Dado** el primer campo de contraseña nueva con guiones, **cuando** se escribe, se borra, se pega (también más de 128 caracteres) o el navegador avisa de un relleno automático, **entonces** la barra muestra el texto del nivel que corresponde a lo escrito, uno de los cuatro, con el color de ese nivel (de rojo en Débil a verde en Fuerte, con ámbar y un verde intermedio) y el texto de que es solo una guía; y con el campo vacío no muestra nivel. |
| AC-ACC-04 | ACC-04 | **Dado** las contraseñas de la tabla de ejemplos de la sección 5, **cuando** se calcula su nivel, **entonces** coincide con el de la tabla. |
| AC-ACC-05 | ACC-05 | **Dado** una contraseña que se escribe en un campo de contraseña nueva y en el de repetición, **cuando** no se envía el formulario, **entonces** escribirla no origina ninguna solicitud y ninguna solicitud la contiene, y nada la guarda en el almacenamiento del navegador, en las cookies, en la dirección, en el contenido de la página fuera de sus campos ni en la consola. |
| AC-ACC-06 | ACC-06 | **Dado** la recuperación, el cambio desde la cuenta y el cambio de la temporal, **cuando** la persona escribe en el campo de repetición o cambia después el primero, **entonces** el aviso dice «Las contraseñas coinciden» si son exactamente iguales y «Las contraseñas todavía no coinciden» si difieren, con un icono distinto en cada caso; no hay aviso con el campo de repetición vacío; y el formulario se puede enviar en los tres casos. |
| AC-ACC-07 | ACC-07 | **Dado** cada campo de contraseña de las pantallas de ingreso, registro, recuperación, cuenta y contraseña temporal, **cuando** se activa su botón con el ratón o el teclado, **entonces** el contenido se ve y el botón anuncia qué campo es y que está visible; al activarlo de nuevo se oculta; no se envía el formulario ni se pierde lo escrito; el campo conserva su autocompletado y admite pegar; al enviar el formulario o volver desde el historial el contenido queda oculto; y una pantalla repintada por el servidor llega con los campos vacíos y ocultos. |
| AC-ACC-08 | ACC-09 | **Dado** las pantallas con las ayudas en los temas claro y oscuro, **cuando** se revisan con una herramienta automática de accesibilidad y con el teclado, **entonces** no hay incumplimientos críticos ni serios, cada nivel de la barra y cada estado se entiende sin color, los botones tienen foco visible, las regiones anunciadas cambian solo cuando cambia el nivel o la coincidencia y después de la pausa de la sección 5, y con movimiento reducido no hay transiciones. |
| AC-ACC-09 | ACC-10 | **Dado** la dirección configurada, **cuando** un visitante, un cliente con sesión y quien acaba de fallar un ingreso abren una página de la tienda o su ingreso, **entonces** ven «Acceso del personal» en el pie y, en el ingreso, además en la pantalla, siempre con la misma dirección; y el host de gestión no muestra el enlace en ninguna parte. |
| AC-ACC-10 | ACC-11 | **Dado** la dirección ausente, vacía o con solo espacios, **cuando** el servicio arranca en cualquier entorno y se abren la tienda y su ingreso, **entonces** arranca y no aparece el enlace. |
| AC-ACC-11 | ACC-12 | **Dado** una dirección relativa, sin conexión segura, con usuario o contraseña, con una ruta distinta de `/`, con consulta, con fragmento, con espacios o de otro esquema, **cuando** arranca el servicio, **entonces** no arranca y el error nombra la variable sin su valor; **dado** una dirección sin conexión segura hacia la propia máquina, arranca solo en los entornos de desarrollo y de prueba automatizada y no en ningún otro; y una dirección segura de un solo origen, con o sin `/` final y puerto, arranca en todos. |
| AC-ACC-12 | ACC-13, ACC-16 | **Dado** el personal en su primer ingreso y un cliente que activa el segundo factor en su cuenta, **cuando** se muestra la clave, **entonces** hay un código QR que, leído con un lector distinto del que lo generó, da exactamente la dirección de configuración del enlace, y la clave en texto sigue visible; y si Identidad no está disponible no aparecen ni la clave ni el código. |
| AC-ACC-13 | ACC-14 | **Dado** la respuesta que muestra la clave, **cuando** se inspecciona, **entonces** el código va dentro de la propia página, sin dirección de imagen ni solicitud a otro host, la respuesta prohíbe guardarse en caché, y ningún registro, borrador ni dato guardado contiene el código, la dirección o la clave en claro. |
| AC-ACC-14 | ACC-15 | **Dado** el código QR en los temas claro y oscuro y en una pantalla de 320 píxeles de ancho, **cuando** se revisa, **entonces** tiene texto alternativo sin la clave, módulos oscuros sobre fondo claro con un margen de 4 módulos en ambos temas, mide lo que fija la sección 5 sin desbordar la pantalla y se ve sin ejecutar guiones. |
| AC-ACC-15 | ACC-16 | **Dado** un generador de códigos que falla, **cuando** se muestra la clave, **entonces** la pantalla responde igual que antes, con la clave y el enlace, sin el código, y los registros dicen a lo sumo que no se pudo generar, sin la clave, la dirección ni el detalle de la falla. |
| AC-ACC-16 | ACC-17 | **Dado** cada campo de contraseña de todas las pantallas, **cuando** se revisa la página sin ejecutar guiones y después de hacerlo visible, **entonces** declara desactivadas la corrección ortográfica, la autocorrección, la mayúscula automática y las sugerencias de escritura. |

## 5. Reglas e invariantes

- **La barra es una guía.** No sustituye ni adelanta la comprobación del servidor, no consulta la lista de contraseñas comunes o filtradas, no conoce el correo ni el nombre de la persona y ningún texto la presenta como garantía de que la contraseña se aceptará o es segura.
- **Nivel (ACC-04).** El texto se normaliza como el servidor antes de contar (IDE-59, forma de compatibilidad NFKC) y la longitud son sus caracteres Unicode, uno por cada punto de código. Los patrones se buscan sin distinguir mayúsculas, de izquierda a derecha: en cada posición cuenta como un solo carácter el patrón más largo que empiece ahí, y la búsqueda sigue después de él; lo que no entra en un patrón cuenta un carácter cada uno. El resultado es la **longitud útil**. Son patrones previsibles:
  - (a) las palabras `password`, `contraseña`, `contrasena`, `qwerty`, `admin`, `bienvenido`, `welcome`, `iloveyou`, `usuario`, `letmein` y `babelhub`, también cambiando `o` por `0`, `i` por `1`, `e` por `3`, `a` por `4` o `@`, `s` por `5` o `$` y `t` por `7`;
  - (b) tres o más veces seguidas el mismo carácter;
  - (c) cuatro o más letras de la `a` a la `z`, o cuatro o más dígitos, consecutivos en orden ascendente o descendente;
  - (d) cuatro o más teclas contiguas, en cualquiera de los dos sentidos, de una de estas filas: `1234567890`, `qwertyuiop`, `asdfghjklñ`, `zxcvbnm`;
  - (e) un grupo de 2 a 8 caracteres escrito tres o más veces seguidas, o de 4 a 8 escrito dos o más veces seguidas.

  Si la longitud o la longitud útil es menor que 10, el nivel es **Débil**. En otro caso se suman: un punto si la longitud útil es de 10 a 15, dos si es de 16 o más; y un punto más si hay al menos tres de estas cuatro clases: letras minúsculas, letras mayúsculas (incluidas las acentuadas y la eñe), dígitos, y cualquier otro carácter, incluidos los espacios. Un punto es **Aceptable**, dos **Buena** y tres **Fuerte**.
- **Tabla de ejemplos (AC-ACC-04)**:

  | Contraseña | Nivel | Por qué |
  | :--- | :--- | :--- |
  | `Ab3$xk9` | Débil | menos de 10 |
  | `qzmvkrtp🙂` | Débil | 9 caracteres: el emoji cuenta uno |
  | `Password1!` | Débil | palabra común con número y símbolo |
  | `P4$$w0rd2026` | Débil | la misma palabra con sustituciones |
  | `@dm1n2026xy` | Débil | otra palabra con sustituciones |
  | `babelhub2026` | Débil | el nombre del comercio |
  | `aaaaaaaaaaaaaaaa` | Débil | repetición |
  | `abcdefghijklmn` | Débil | secuencia ascendente |
  | `zyxwvutsrqpo` | Débil | secuencia descendente |
  | `qwertyuiop12` | Débil | fila del teclado (más larga que la palabra) |
  | `holaholahola` | Débil | grupo repetido tres veces |
  | `lunalunazk` | Débil | grupo de 4 repetido dos veces |
  | `qzmvkrtpwj` | Aceptable | 10 letras sin patrón |
  | `ｑｚｍｖｋｒｔｐｗｊ` | Aceptable | las mismas en ancho completo, normalizadas |
  | `qzmvkrtpwxhbjnd` | Aceptable | 15 de una sola clase |
  | `ñandúveloz🙂🙂` | Aceptable | 12 con dos clases |
  | `qzmvkrtpwxhbjndl` | Buena | 16 de una sola clase |
  | `Rio7-nube!` | Buena | 10 con cuatro clases |
  | `mi gato come pescado fresco` | Buena | frase larga en minúsculas |
  | `Mi gato come pescado fresco` | Fuerte | frase larga con tres clases |

- **Anuncios (ACC-09).** El nivel y la coincidencia se ven al instante; a un lector de pantalla se le anuncian solo cuando cambian y después de un segundo sin escribir, sin interrumpir lo que está leyendo.
- **Teclado y movimiento (ACC-09).** Todo se usa por teclado con foco visible, y nada de esta spec se anima cuando la persona pide reducir el movimiento.
- **Código QR (ACC-15).** Lleva un margen claro de 4 módulos y mide de lado al menos 176 píxeles, o el ancho disponible si la pantalla es más angosta.
- **Dirección del personal (ACC-12).** Una dirección absoluta con conexión segura, de un solo origen (nombre y puerto opcional), sin espacios, usuario, contraseña, ruta distinta de `/`, consulta ni fragmento. Solo en los entornos que el servicio reconoce como de desarrollo y de prueba automatizada (los mismos de COM-05) se admite sin conexión segura hacia la propia máquina. Se configura por entorno; ningún host la deduce de la solicitud.
- El enlace no concede acceso: el host de gestión conserva sus controles (la correspondencia entre rol y host de la sesión web, el segundo factor obligatorio del personal, los límites de ingreso de SEG-06 y el acceso previo de la operación). Mostrar la dirección no revela cuentas ni cambia quién puede ingresar. Que facilite encontrar el ingreso del personal es un riesgo aceptado con esos controles (DD-121) y registrado en el modelo de amenazas.
- El código QR contiene el secreto del segundo factor: se trata como la clave en texto (se muestra solo en esa respuesta, no se registra ni se almacena).
- Ningún cambio de esta spec altera las respuestas, los límites de uso, la verificación de persona, la auditoría ni los contratos de Identidad.

## 6. Casos límite y errores

- Una contraseña con emojis u otros caracteres fuera del plano básico cuenta un carácter por cada uno, igual que el servidor.
- Un texto pegado de más de 128 caracteres muestra su nivel; el servidor lo rechaza con el motivo (IDE-15).
- Una pantalla repintada por el servidor llega con los campos de contraseña vacíos y ocultos y sin nivel.
- Un gestor de contraseñas que rellena sin teclear actualiza la barra y la coincidencia cuando el navegador avisa del cambio; si no avisa, el formulario se envía igual.
- La sesión vencida o el límite de uso no cambian: lo escrito en contraseñas no se conserva (WEB-25).
- Un error al mostrar la configuración del segundo factor (Identidad no disponible) no muestra código ni clave.
- Quien quiera capturar o fotografiar el código tiene lo mismo que con la clave en texto: se muestra una sola vez y solo a quien está configurando su segundo factor.

## 7. Preguntas abiertas

- [x] ¿Se imponen reglas de composición? Resuelto el 2026-11-27 (persona responsable, paquete de mejoras del acceso): no; se orienta con recomendaciones y una barra; IDE-15, RNF15 y CTL-15 siguen igual.
- [x] ¿Qué pantallas reciben las ayudas, el enlace del personal y el código QR? Resuelto el 2026-11-27 (persona responsable, mismo paquete): las de la sección 2.
- [x] ¿Se agrega un campo de repetición al registro? Resuelto el 2026-11-27 (DD-116): no; el paquete pide el aviso en el campo de repetición, y el registro no lo tiene; agregarlo cambiaría el alta y queda como propuesta aparte.
- [x] ¿Con qué reglas se calcula el nivel? Resuelto el 2026-11-27 (DD-117): las de la sección 5, que cumplen la orientación de la persona (longitud primero, variedad después, patrones previsibles penalizados, una frase larga en minúsculas puede llegar a Buena).
- [x] ¿Qué pasa con una dirección del personal mal escrita y qué instalaciones la tienen? Resuelto el 2026-11-27 (DD-118): ausente no muestra nada; mal formada impide arrancar, como el resto de la configuración; las instalaciones nuevas la generan y en las ya generadas se agrega a mano según la guía.
- [x] ¿Se acepta que el enlace facilite encontrar el ingreso del personal? Resuelto el 2026-11-27: la persona pidió el enlace en su paquete; el riesgo y sus controles quedan en DD-121.
