# Pruebas de usabilidad

Plan para medir RNF27 (compra fácil de completar sin ayuda) y detectar problemas de la interfaz durante el desarrollo, no solo al final (AD29). Es un plan **ligero y repetible**, adecuado a un proyecto académico con recursos limitados.

## 1. Objetivos y metas

| Meta | Valor | Fuente |
| :--- | :---: | :--- |
| Éxito de la tarea de compra con recojo, sin ayuda | ≥ 90 % de las personas | RNF27, WEB-22 |
| Puntuación **SUS** (escala de usabilidad del sistema) | ≥ 68 | RNF27 |
| Errores por tarea | < 1 en promedio | RNF27 |
| Pasos de la compra | ≤ 5 (≤ 4 si es solo digital) | RNF13, CAR-15 |

La puntuación SUS de 68 se toma como referencia de «promedio» ampliamente citada; la meta es no quedar por debajo.

## 2. Método

| Aspecto | Decisión |
| :--- | :--- |
| Tipo | Prueba moderada con pensamiento en voz alta (presencial o por videollamada) sobre el prototipo o la versión real. |
| Participantes | Rondas de **5 personas** (se detectan la mayoría de los problemas graves en cada ronda); al menos 2 rondas acumuladas antes de medir el 90 % (WEB-22). Perfiles de las [personas](05-personas-y-tareas.md) (lectoras de provincia, estudiantes) y, en cada ronda, al menos una persona con tecnología de apoyo. |
| Tareas | T01 a T04 y T09 para clientes; T05 a T08 para el personal ([personas y tareas](05-personas-y-tareas.md)). Guion sin pistas. |
| Dispositivos | Celular (mayoría) y computadora. |
| Datos | Entorno de pruebas con datos de ejemplo; nunca datos reales. |
| Consentimiento | Se informa el propósito, se pide permiso para registrar la pantalla y se pueden retirar en cualquier momento; no se recopilan datos personales identificables. |
| Moderación | Una persona modera y otra anota; el moderador no ayuda salvo que la persona esté bloqueada más de 2 minutos (cuenta como «con ayuda»). |

## 3. Qué se registra

| Dato | Cómo |
| :--- | :--- |
| Éxito de cada tarea | Completa sin ayuda / con ayuda / no completa. |
| Tiempo por tarea | Cronómetro. |
| Errores | Conteo y descripción (clic equivocado, mensaje no entendido, retroceso). |
| Satisfacción | Cuestionario SUS de 10 preguntas al terminar. |
| Observaciones | Frases textuales, dudas y gestos de frustración. |
| Gravedad | Cada hallazgo de 0 (cosmético) a 4 (catastrófico). |

## 4. Análisis y decisiones

1. Se agrupan los hallazgos por pantalla y por gravedad.
2. Los de gravedad 3 y 4 se corrigen antes de seguir con la spec (bloquean **Done**).
3. Se actualizan los prototipos y el sistema de diseño si el problema es de patrón.
4. Se registra el resultado en `progress/` con fecha, número de personas, tareas y métricas.
5. Se repite la prueba sobre las tareas con problemas hasta cumplir las metas.

## 5. Calendario por iteración

| Momento | Qué se prueba |
| :--- | :--- |
| Tras aprobar el prototipo | T01 a T03 y T09 sobre el prototipo (ronda 1). |
| Tras `web-base`, catálogo y carrito | T01 y T02 reales. |
| Tras checkout, pagos y biblioteca | T02 y T03 completas (ronda 2 para medir el 90 %). |
| Tras identidad | T04 (recuperación) con códigos reales. |
| Tras las áreas de Sucursal y Administración | T05 a T08 con personal o estudiantes que hagan de personal. |
| Antes de cerrar | Pasada final con lista de [accesibilidad](06-accesibilidad-wcag.md). |

## 6. Plantilla de informe

```
Fecha: …            Ronda: …            Versión probada: …
Participantes: n =  (perfil, dispositivo)
Resultados por tarea: éxito %, tiempo mediano, errores promedio
SUS promedio: …
Hallazgos (gravedad, pantalla, evidencia, propuesta)
Decisiones y responsables
```

## 7. Limitaciones

Con pocas personas, los porcentajes son indicativos: 9 de 10 equivale a 90 %, pero con mucha incertidumbre. Por eso se acumulan rondas y las decisiones se apoyan en los hallazgos cualitativos, no solo en el número.

---

# Guía operativa de las dos rondas de `web-base` (T34b, AC-WEB-17)

Las secciones 1 a 7 son el plan; lo que sigue es lo que hace falta para **ejecutarlo**: quién participa, qué se dice, qué se anota y cómo se cierra cada ronda. Las secciones 8 a 16 describen la sesión **moderada** (presencial o por videollamada), que se lleva en papel con la plantilla de la sección 6. **La herramienta [`tools/pruebas-humanas`](../../tools/pruebas-humanas/README.md) (versión 2) las ejecuta sin moderador**: la persona la completa sola y el resultado se devuelve como JSON, que lee quien emite el veredicto ([`veredicto.md`](../../tools/pruebas-humanas/veredicto.md)). Las diferencias, qué medidas son autodeclaradas y cómo contrastarlas están en la **sección 17**.

## 8. Reclutamiento

| Aspecto | Regla |
| :--- | :--- |
| Cuántas personas | **5 por ronda, 10 en total, todas distintas**: quien participó en la ronda 1 no participa en la 2. Si una sesión se descarta (la persona se retira, no cumple el perfil o el entorno falló), se recluta otra hasta tener 5 válidas por ronda. |
| Quién sí | Personas que compran libros en línea o podrían hacerlo: lectoras y lectores de provincia, estudiantes ([personas](05-personas-y-tareas.md) Rosa y Mateo). Mayores de edad. |
| Quién no | Quien participó en el desarrollo o la documentación de BabelHub, quien vio el prototipo o la tienda de pruebas, y quien ya participó en otra ronda. |
| Tecnología de apoyo | **Al menos una persona por ronda** que use lector de pantalla, ampliación de pantalla o navegación con teclado (sección 2). La herramienta avisa si una ronda no tiene a nadie. |
| Dispositivo | Mayoría en celular propio (o prestado, con el navegador ya abierto); el resto, en computadora. |
| Preguntas de filtro (antes de aceptar) | 1) «¿Has comprado libros por internet alguna vez?» 2) «¿Participaste en el desarrollo de BabelHub o has visto antes la tienda de pruebas?» (si sí, no califica) 3) «¿Usas lector de pantalla, ampliación o solo teclado?» (solo para equilibrar). Se anota únicamente el perfil (`lectora-provincia`, `estudiante` u `otro`) y la respuesta 3, **sin nombre**. |
| Contacto | El nombre y el teléfono o correo que se usen para coordinar la cita **viven fuera de la herramienta y del repositorio** (por ejemplo, en el chat de quien modera) y se borran al terminar la ronda. Nunca se anotan junto a los resultados. |
| Compensación | Opcional y simbólica (un refrigerio); no condiciona la participación ni los resultados. |

## 9. Preparación de la sesión

Cada ronda se prepara una vez; cada persona necesita unos 10 minutos de preparación del entorno antes de entrar.

1. **Entorno de pruebas** con datos ficticios: el local de `tests/e2e/entorno/` (`preparar-entorno.sh`, host de la tienda, Inventario y `sembrar-datos.sh --limpiar`) o la URL de pruebas que indique la persona responsable. Anota la **versión probada** (fecha o commit del despliegue) en la herramienta («Inicio»). **No se cambia la tienda durante una ronda**: los cambios se hacen entre rondas.
2. **Una cuenta de cliente nueva por persona**, con correo y contraseña ficticios (por ejemplo, `p01@prueba.invalid`, nunca un correo real). Confirma con la persona responsable cómo se crean las cuentas en el entorno de la ronda; si el entorno exige verificar el correo, deja la cuenta ya lista y entrega los datos en la tarjeta.
3. **Tarjeta de datos de prueba**, impresa o en otra pantalla, que se entrega **solo cuando la persona llega al paso de crear la cuenta o de pagar** (no antes: no es una pista):

   > **Datos para tu compra de prueba** (no se cobra nada)
   > Correo: `p01@prueba.invalid` · Contraseña: la que figura en la tarjeta (de ejemplo, solo para la prueba)
   > Pago: medio **Tarjeta** · «Identificador del medio de pago»: `tok_aprobado`

   El identificador `tok_aprobado` es el del simulador de la pasarela: el cobro se aprueba sin datos de tarjeta reales. Si el simulador cambia, la persona responsable actualiza la tarjeta.
4. **Dispositivo de la persona** con el navegador abierto en la pantalla de inicio de la tienda de pruebas y sin sesión iniciada (ni de otra cuenta ni de la participante anterior).
5. **Grabación** de pantalla y voz solo si la persona la permitió; se guarda **fuera del repositorio** y se borra al terminar el análisis. No se graba el rostro.
6. **Herramienta del moderador** abierta en otro dispositivo (o en el mismo si es un solo evaluador): sección «C · Usabilidad» → «Agregar persona a la ronda N». Si hay dos evaluadores (uno modera y otro anota), cada uno puede llevar su dispositivo y luego se **combinan** los JSON (importación).
7. **Sala** silenciosa, sin que otras personas vean la pantalla. Quien anota no habla.

## 10. Escenario y tarea

La tarea es **una sola** (T02 de [personas y tareas](05-personas-y-tareas.md)): *comprar un libro impreso con recojo en sucursal*. Se mide la **primera vez** que la persona usa la tienda.

| Elemento | Valor |
| :--- | :--- |
| Escenario que se lee | «Imagina que vives en Ayacucho y quieres comprar el libro *El zorro y el cóndor del Ande*, impreso, para recogerlo tú misma o tú mismo en una sucursal. Compra ese libro en esta tienda para recogerlo en la sucursal que te quede más cómoda, y paga.» |
| Libro | *El zorro y el cóndor del Ande* (dato sembrado: impreso con existencias en Ayacucho y en Lima; hay también una edición digital, que **no** es la que se pide). |
| Qué es «éxito» | Llega a la pantalla de **confirmación del pedido pagado** con el libro correcto, el recojo en sucursal elegido por la persona y el estado del pedido a la vista. |
| Qué es «sin ayuda» | La persona lo logra sin que el moderador le dé ninguna pista, aclaración sobre la tienda ni ayuda (reglas de la sección 14). |
| Fin de la tarea | La persona dice que terminó, completa la compra, abandona o llega a los **15 minutos** (regla operativa de esta guía: pasado ese tiempo se detiene y se anota «no completó»). |
| Pasos esperados | Hasta 5 en la compra (RNF13, CAR-15): carrito, ingreso o registro, entrega con recojo, pago y confirmación. La persona puede explorar antes del carrito. |

## 11. Guion del moderador (palabra por palabra)

Lee lo que está entre comillas **tal cual**; lo que está en cursiva es una indicación para ti. Habla siempre en un tono tranquilo y amable. En la herramienta, los textos de los pasos 2, 3 y 5 aparecen ya listos para leer.

**1. Recepción (2 min)**

> «Hola, gracias por venir. Hoy vamos a probar una tienda de libros en línea que todavía está en construcción. La sesión dura entre 20 y 30 minutos.»

**2. Consentimiento informado (3 min)** — *Lee el texto de la sección 13 completo, en voz alta, y responde sus dudas. En la herramienta: paso «Antes»; marca «Leí el texto», «Dijo que entiende que puede parar» y «Acepta participar». Si dice que no a participar, agradece y termina; si dice que no a la grabación, participa igual y no se graba.* **No anotes su nombre.**

**3. Cómo funciona la prueba (2 min)**

> «Lo que probamos es la tienda, no a ti. No hay respuestas buenas ni malas; si algo te confunde, la culpa es de la tienda. Mientras la uses, me gustaría que pienses en voz alta: dime qué buscas, qué esperas que pase y qué te llama la atención o te confunde. Yo voy a estar aquí, pero no voy a poder ayudarte mientras lo haces, y no es porque lo estés haciendo mal: necesito ver qué haría una persona sin mí. Si en algún momento quieres dejarlo, me lo dices y paramos sin problema. ¿Tienes alguna pregunta?»

**4. Contexto (1 min)** — *Anota perfil, dispositivo y tecnología de apoyo (sin nombre). Confirma el filtro 2 de la sección 8: nunca vio la tienda.*

**5. La tarea (lee el escenario)**

> «Imagina que vives en Ayacucho y quieres comprar el libro “El zorro y el cóndor del Ande”, impreso, para recogerlo tú misma o tú mismo en una sucursal. Compra ese libro en esta tienda para recogerlo en la sucursal que te quede más cómoda, y paga. Cuando creas que terminaste, dímelo. La tienda es de prueba: no se cobra nada.»

*Si no entendió, repite el escenario **sin añadir nada**. Cuando llegue al paso de crear la cuenta o de pagar, entrega la tarjeta de datos de prueba diciendo solo: «Para crear tu cuenta y para pagar puedes usar los datos de esta tarjeta». Pulsa «Iniciar» en el cronómetro cuando la persona empieza a mirar la pantalla.*

**6. Durante la tarea**

- Silencio. Observa y anota (sección 12).
- Si la persona calla más de unos segundos: «¿Qué estás pensando?».
- Si dice «¿es por aquí?» o «¿esto está bien?»: «¿Qué esperarías que pasara?» o «Haz lo que harías si yo no estuviera aquí».
- Si pregunta «¿puedo…?»: «Haz lo que te parezca natural».
- Frases permitidas, y solo ellas: «¿Qué estás pensando?», «¿Qué esperabas que pasara?», «Cuéntame qué estás buscando», «¿Qué harías si yo no estuviera aquí?». **Nada más** (reglas de la sección 14).

**7. Fin de la tarea** — *Pulsa «Detener» cuando la persona dice que terminó (o a los 15 minutos).*

> «Gracias, con eso terminamos la tarea.»

*Marca el resultado (sin ayuda, con ayuda o no completó) en el paso «Resultado».*

**8. Preguntas después de la tarea (3 min)** — *Léelas tal cual y anota frases textuales.*

> 1. «En general, ¿qué tan fácil o difícil fue completar esta tarea? Del 1 al 7, donde 1 es muy difícil y 7 es muy fácil.» (SEQ)
> 2. «¿Qué fue lo más difícil o lo que más te confundió?»
> 3. «¿Qué fue lo más fácil?»
> 4. «Si pudieras cambiar una sola cosa de la tienda, ¿cuál sería?»

**9. Cuestionario SUS (3 min)**

> «Ahora te voy a leer 10 frases. Dime qué tan de acuerdo estás con cada una, del 1 al 5: 1 es “muy en desacuerdo” y 5 es “muy de acuerdo”. Responde lo primero que pienses; no hay respuestas correctas.»

*Lee las 10 frases de la sección 15 en orden. Si la persona prefiere, puede marcarlas ella misma en la herramienta.*

**10. Cierre (1 min)**

> «Eso es todo. Muchas gracias: nos ayudaste muchísimo. Si quieres saber algo más de la prueba, pregúntame. Todo lo anotado está sin tu nombre, y si más tarde quieres que se borre lo tuyo, dímelo.»

*Anota el problema principal y su gravedad (sección 12) cuando la persona se haya ido.*

## 12. Qué anotar

| Qué | Cómo | En la herramienta |
| :--- | :--- | :--- |
| **Éxito** | Sin ayuda, con ayuda o no completó (sección 10). | Paso «Resultado». |
| **Tiempo** | Desde que empieza hasta que dice que terminó. | Cronómetro (paso «Tarea»); editable en segundos. |
| **Errores** | Uno por cada clic equivocado, mensaje que no entendió, retroceso (volver a una pantalla anterior por no encontrar lo que buscaba) u otro tropiezo. No se cuentan los fallos del entorno (sección 14). | Botones «+ tipo», con nota de qué pasó. |
| **Ayudas** | Cada vez que se da una pista, con el momento y el motivo. | «Registrar una ayuda». |
| **Frases textuales** | Lo que dijo en voz alta: dudas, expectativas, frustración. Entre comillas, sin nombres. | Notas de los errores y «Preguntas abiertas». |
| **Recorrido** | Qué ruta siguió, dónde dudó o retrocedió, si encontró la opción de recojo y el medio de pago. | «Otras observaciones». |
| **Satisfacción** | SEQ (1 a 7) y SUS (10 frases, 0 a 100). | Pasos «Resultado» y «SUS». |
| **Problema principal** | El que más estorbó, en una o dos frases, con la pantalla donde ocurrió y su gravedad. | Paso «Cierre». |

**Gravedad** (0 a 4, adaptada de la escala de Nielsen): **0** cosmético, no afecta la compra; **1** menor, molesta pero la persona sigue sin perder tiempo; **2** moderado, retrasa o provoca un error del que la persona se recupera sola; **3** grave, la persona casi no puede continuar o necesita ayuda (**bloquea «Done»**); **4** catastrófico, no puede completar la compra o pagaría de más o perdería datos (**bloquea «Done»**).

## 13. Texto del consentimiento informado (se lee en voz alta)

> Gracias por ayudarnos. Estamos probando una tienda de libros en línea de un proyecto académico llamado BabelHub. Lo que se prueba es la tienda, no a ti: no hay respuestas buenas ni malas.
>
> Te pediré hacer una compra de prueba (no se cobra nada y los datos son ficticios) y después responder 10 preguntas cortas. Dura entre 20 y 30 minutos.
>
> Participar es voluntario. Puedes parar o retirarte cuando quieras, sin dar explicaciones y sin ninguna consecuencia; si te retiras, se borra lo que anotamos de ti.
>
> No te pedimos tu nombre ni otros datos personales. En los registros solo aparece un código (por ejemplo, P3) y no se anota nada que te identifique.
>
> Anotaremos lo que haces, cuánto demoras, tus tropiezos y tus respuestas, y de ti solo tu tipo de perfil (por ejemplo, estudiante), si usas el celular o la computadora y si usas tecnología de apoyo como lector de pantalla. Si lo permites, también grabaremos la pantalla y tu voz mientras piensas en voz alta; la grabación se guarda fuera del repositorio del proyecto, solo la ve el equipo y se borra al terminar el análisis. Puedes decir que no a la grabación y participar igual.
>
> Usaremos los resultados únicamente para mejorar la tienda y para el informe del proyecto, sin identificarte, conforme a la Ley N.º 29733, de Protección de Datos Personales.
>
> Puedes preguntar lo que quieras antes, durante y después.

**Cómo se registra.** Como no se recopilan datos personales identificables (la grabación de voz, si la permite, es la excepción y se guarda fuera del repositorio), el consentimiento es **verbal** y se anota como casillas marcadas con la fecha (leído, entiende que puede retirarse, acepta, permite grabar). **Nunca** se pide firma con nombre ni documento. Si la persona se retira, se borra su registro entero (en la herramienta: «Borrar todo lo anotado de esta persona») y se recluta otra. Las grabaciones, si las hubo, se guardan fuera del repositorio y se eliminan al cerrar el análisis de la ronda.

## 14. Reglas para no ayudar

1. **No expliques la tienda.** No digas dónde hacer clic, qué significa una palabra, qué botón sigue ni «ya casi».
2. **No confirmes ni corrijas.** Ni «vas bien» ni «no, ahí no». Si pregunta «¿está bien?», responde con una pregunta: «¿Qué esperarías que pasara?».
3. **No reacciones** con gestos, suspiros ni cambios de tono ante un error. Si se equivoca, anótalo y sigue callado.
4. **El silencio es información.** Espera: dar tiempo a que la persona se recupere sola es parte de la prueba.
5. **Regla de los 2 minutos.** Si la persona **no avanza** durante 2 minutos (en la herramienta, «Se bloqueó» arranca el contador y a los 2 minutos avisa), puedes dar la ayuda mínima necesaria. **Desde ese momento cuenta como «con ayuda»** y se registra con «Registrar una ayuda».
6. **Si pide ayuda antes:** «Haz lo que harías si yo no estuviera. Si quieres dejarlo, me dices.» Si insiste y abandona, es «no completó».
7. **Los fallos del entorno no son errores de la persona.** Si la tienda de pruebas se cae, el simulador no responde o falta el stock del libro, pausa, repara, anótalo en «Otras observaciones» y, si ya no se puede continuar con normalidad, **excluye la sesión** («la sesión falló») y recluta otra persona. No se cuenta como error ni como ayuda.
8. **No modifiques la tienda** durante la ronda, aunque veas un defecto: anótalo y arréglalo después.
9. **Quien anota no interviene** ni comenta; solo observa y registra.

## 15. Las 10 frases del cuestionario SUS

Se leen en este orden y la persona responde de 1 (muy en desacuerdo) a 5 (muy de acuerdo). Es la versión estándar de la escala de usabilidad del sistema (SUS), con «esta tienda» en lugar de «este sistema».

| N.º | Frase |
| :---: | :--- |
| 1 | Creo que me gustaría usar esta tienda con frecuencia. |
| 2 | Encontré la tienda innecesariamente compleja. |
| 3 | Pensé que la tienda era fácil de usar. |
| 4 | Creo que necesitaría el apoyo de una persona con conocimientos técnicos para poder usar esta tienda. |
| 5 | Encontré que las diversas funciones de la tienda estaban bien integradas. |
| 6 | Pensé que había demasiada inconsistencia en la tienda. |
| 7 | Imagino que la mayoría de las personas aprenderían a usar esta tienda muy rápidamente. |
| 8 | Encontré la tienda muy incómoda de usar. |
| 9 | Me sentí muy segura o seguro usando la tienda. |
| 10 | Necesité aprender muchas cosas antes de poder empezar a usar la tienda. |

**Puntaje (0 a 100, fórmula estándar):** en las frases **impares** (1, 3, 5, 7, 9) se resta 1 a la respuesta; en las **pares** (2, 4, 6, 8, 10) se resta la respuesta de 5; se suman los 10 resultados (0 a 40) y se multiplican por 2,5. Ejemplo: respuestas 4, 2, 4, 2, 4, 2, 4, 2, 4, 2 → impares 3 × 5 = 15, pares 3 × 5 = 15 → 30 × 2,5 = **75**. La herramienta lo calcula al responder las 10 y tiene una prueba automática de la fórmula; solo se calcula con las 10 respuestas.

## 16. Cierre de cada ronda y registro

1. Al terminar las 5 personas, mira el **resumen de la ronda** (herramienta, sección C): personas válidas, éxito sin ayuda, SUS promedio, errores por tarea, tiempo mediano y problemas de gravedad 3 y 4. Es una ayuda; las metas formales son las del **acumulado de las dos rondas** (sección 1).
2. Escribe en «Resumen y decisiones» qué problemas fueron los más graves y qué se decidió. **Sin nombres.**
3. **Ronda 1:** corrige los problemas de gravedad 3 y 4 antes de la ronda 2 (sección 4) y anota en «Cambios hechos entre las rondas» qué se cambió, para que la ronda 2 **compruebe la corrección**.
4. **Exporta el JSON** (herramienta, sección «Datos») y guárdalo; así hay una copia aunque el navegador borre sus datos. Al terminar las dos rondas, devuelve el JSON como indica el [README](../../tools/pruebas-humanas/README.md).
5. Quien emite el veredicto lee el JSON y lo copia a [`progress/usabilidad/rondas.md`](../../progress/usabilidad/rondas.md) y a `specs/web-base/verificacion.md` (procedimiento en [`veredicto.md`](../../tools/pruebas-humanas/veredicto.md)).

## 17. Versión sin moderador (herramienta `tools/pruebas-humanas`, versión 2)

La persona responsable pidió que cualquier persona pueda completar la prueba **sola**, desde un enlace o un archivo, con acompañamiento mínimo. La herramienta v2 sustituye al «modo moderador» de la v1: **donde las secciones 9 a 16 mencionan botones o pasos de la herramienta del moderador («Agregar persona a la ronda N», «Se bloqueó», «Registrar una ayuda»), esos controles ya no existen**; la guía sigue valiendo para las sesiones en papel o con moderador.

| Aspecto | Sesión moderada (§8 a §16) | Versión sin moderador (herramienta v2) |
| :--- | :--- | :--- |
| Consentimiento | Se lee en voz alta (§13) y quien modera marca casillas | Texto breve en lenguaje llano en pantalla y **tres casillas** que marca la propia persona («Leí y entendí», «Sé que puedo parar», «Acepto participar»); no se graba pantalla ni voz |
| Perfil | Perfil (`lectora-provincia`, `estudiante`, `otro`), dispositivo, tecnología de apoyo | **Rangos**: edad (18–24 … 65 o más, o «prefiero no decirlo») y frecuencia de compra por internet; el dispositivo se detecta; tecnología de apoyo opcional; «¿ya habías visto esta tienda?» como filtro |
| Tarea | Se lee el escenario (§10) | Se muestra el mismo escenario; **«Empezar tarea» / «Terminé»** con cronómetro automático y botón «No pude terminarla»; datos de prueba (correo `<código>@prueba.invalid`, medio «Tarjeta», `tok_aprobado`) en un cuadro plegable |
| Éxito | Quien modera decide (reglas de los 2 minutos, §14) | **Autodeclarado**: «¿Lograste comprar el libro?» y «¿Pediste ayuda a alguien (una persona, un buscador, un asistente…)?» |
| Errores | Quien anota cuenta cada tropiezo (§12) | **Autodeclarado**: «¿Cuántas veces te perdiste o te equivocaste?» (0, 1, 2 o 3 o más; «3 o más» se cuenta como 3, un mínimo) |
| SUS | 10 frases leídas o marcadas (§15) | 10 frases en **español llano** con 5 puntos y puntaje 0–100 automático con la **misma fórmula estándar** y la misma polaridad (impares positivas, pares negativas); las frases 2 a 10 se reformularon ligeramente con palabras más simples (por ejemplo, «Necesité aprender muchas cosas antes de poder usar la tienda»); la 1 queda igual; queda como decisión DD-83 |
| Preguntas abiertas | SEQ y tres preguntas (§11.8) | Solo «¿Qué fue lo más difícil o lo que más te confundió?» |
| Gravedad 0 a 4 | La asigna quien modera (§12) | La asigna quien lee los resultados al agrupar las respuestas de «lo más difícil» (§4) |

**Limitaciones que hay que decir al informar el resultado:**

1. **La ayuda y los errores son autodeclarados.** Sin alguien que observe, una persona puede olvidar que pidió ayuda o restarle importancia a un tropiezo, y el cronómetro mide también el tiempo que la persona se tomó para leer o para detenerse (se pide corregirlo si pasó de 15 minutos).
2. **Se recomienda observar 2 de las 10 sesiones** (presencial o por videollamada, con permiso) y comparar lo visto con lo declarado. Si difieren, **manda lo observado** y se anota la diferencia; si en esas dos sesiones hay una discrepancia, se revisa con mayor cuidado el resto.
3. **Es la primera vez.** La tarea de usabilidad se ofrece **antes** de la compra guiada (la guía facilitaría la compra y ya no mediría la primera vez). La herramienta marca la sesión si no se respetó el orden (`despuesDeA`) o si la persona ya conocía la tienda; esas personas **no cuentan** para AC-WEB-17.
4. **El criterio de AC-WEB-17 no cambia:** al menos 9 de cada 10 sin ayuda, SUS promedio ≥ 68 y menos de 1 error por tarea, en dos rondas de cinco personas. El cálculo sobre lo autodeclarado se informa **como autodeclarado**.
5. **Quién la completa:** perfiles de las [personas](05-personas-y-tareas.md) (§8), sin quienes hayan trabajado en BabelHub, y en cada ronda al menos una persona con tecnología de apoyo (la herramienta lo avisa al reunir). El enlace lleva la tienda de la ronda (`?tienda=…&v=…&ronda=1`); **no se cambia la tienda durante una ronda**.
