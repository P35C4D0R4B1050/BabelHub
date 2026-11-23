# Web base: protocolo manual de T32b, T33b y T34b

Lo ejecutan **personas**; el agente no lo simula ni inventa resultados. Tres bloques independientes, cada uno con su registro.

> **Herramienta de apoyo (versión 2, 2026-11-23).** En vez de llenar a mano las tablas de abajo, **cada persona completa sola** la app [`tools/pruebas-humanas/index.html`](../../../tools/pruebas-humanas/README.md): un solo archivo sin red ni telemetría, en lenguaje llano, con una pantalla activa a la vez (bienvenida → tareas → gracias) y **sin moderador**. Se comparte como enlace (`?tienda=…&v=…&ronda=…`) o como archivo. Cada persona prueba con **su propio navegador** (la app lo detecta y le pide confirmarlo); al terminar descarga un JSON y un resumen en Markdown y los envía; quien organiza los **reúne** en la misma app (cobertura por navegador, qué falta) y quien emite el veredicto lee los JSON siguiendo [`veredicto.md`](../../../tools/pruebas-humanas/veredicto.md). Las tablas de este protocolo siguen siendo el registro de resultados en el repositorio. Guías completas: `docs/06-experiencia-de-usuario/09-pruebas-de-usabilidad.md` (usabilidad) y `docs/06-experiencia-de-usuario/10-lista-manual-de-accesibilidad.md` (teclado y lector). Evidencia automática ya hecha: `2026-11-21-corrida-automatica.md`.

**Preparación común (15 min).** Entorno local de `tests/e2e/entorno/` (`preparar-entorno.sh`, host de la tienda, Inventario y `sembrar-datos.sh --limpiar`) o la URL de pruebas que indique la persona responsable; datos ficticios; una cuenta de cliente nueva por persona de prueba. Registrar versión del navegador y del sistema.

## A. Navegadores y móviles reales (T32b, AC-WEB-18; ~10 min por persona)

> **Con la app v2:** cada persona hace la tarea «Recorre la compra paso a paso» en **su** navegador (8 pasos con «¿Pudiste hacerlo?» Sí / Con dificultad / No, nota y captura opcionales, «¿Lograste completar la compra?» y «¿Viste algo raro?»). Hacen falta personas que cubran las **seis combinaciones** de la tabla y, en las cuatro de escritorio, **dos versiones**; el resumen de la app indica qué falta. Los generales de la v1 (consola, 375 px, temas, «atrás») ya no se piden a las personas: los cubren las pruebas automáticas (`veredicto.md` §4 A.5).

1. En cada combinación de la tabla, recorrer: inicio → catálogo → ficha → agregar al carrito → ingreso o registro → checkout con recojo → pago con el simulador → estado del pedido.
2. En cada paso: sin errores en pantalla ni en la consola, sin desplazamiento horizontal a 375 px, ambos temas, y el botón «Atrás» no muestra páginas de la persona anterior tras cerrar sesión.
3. Criterio: la compra se completa en todas; cualquier fallo se registra con captura y pasos.

| Fecha | Persona | Navegador y versión | Sistema o dispositivo | Completó la compra (sí/no) | Defectos (enlace o texto) |
| :--- | :--- | :--- | :--- | :--- | :--- |
| | | Chrome (últimas 2 estables) | Escritorio | | |
| | | Edge (últimas 2 estables) | Escritorio | | |
| | | Firefox (últimas 2 estables) | Escritorio | | |
| | | Safari (últimas 2 estables) | macOS | | |
| | | Chrome | Android real | | |
| | | Safari | iPhone real | | |

## B. Teclado y lector de pantalla (T33b, AC-WEB-15; ~10 min por persona con teclado, ~30 min con lector)

> **Con la app v2:** **B1** «Solo con el teclado» (cualquier persona con un teclado: 6 preguntas de 5 puntos y «¿hubo un momento en que no pudiste continuar?») y **B2** «Lector de pantalla» (1 o 2 personas que ya lo usan, de preferencia usuarias reales, con guía de NVDA y VoiceOver). El «sí» a la pregunta de bloqueo es el **incumplimiento crítico**. El mapeo a los puntos K1 a K7 y L1 a L8 está en `veredicto.md` §4 B.

1. Seguir los apartados 4.1 (teclado) y 4.2 (lector) de `10-lista-manual-de-accesibilidad.md` (en la app, el bloque B) en las pantallas del recorrido de compra, mi cuenta, mis pedidos, biblioteca y el Libro de Reclamaciones, con NVDA (Windows, Firefox o Chrome) y VoiceOver (iPhone, Safari), en `babelhub` y `babelhub-dark`.
2. Criterio: **0 incumplimientos críticos de nivel AA** (cualquier paso donde la persona no pueda continuar, o un control sin nombre, o un error que no se anuncia).

| Fecha | Persona | Lector y navegador | Pantalla | Tema | Resultado (cumple/falla) | Observación |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| | | | | | | |

## C. Dos rondas de usabilidad (T34b, AC-WEB-17; ~15 min por persona)

> **Con la app v2 (sin moderador):** la tarea «Compra un libro» con cronómetro automático, «¿pediste ayuda?», «¿cuántas veces te perdiste o te equivocaste?» y el SUS de 10 frases. **La ayuda y los errores son autodeclarados**; se recomienda **observar 2 de las 10 sesiones** para contrastar (`09-pruebas-de-usabilidad.md` §17). Esta tarea se hace **antes** de la compra guiada: mide la primera vez que alguien usa la tienda.

1. Cada ronda: 5 personas distintas que no conocen el sistema; tarea única: «compra un libro con recojo» sin ayuda (guion y consentimiento en `09-pruebas-de-usabilidad.md`). Anotar éxito sin ayuda (sí/no), errores y tiempo; al final, el cuestionario SUS de 10 preguntas.
2. Entre las rondas se corrigen los problemas más graves; la ronda 2 comprueba la corrección.
3. Criterio de aceptación: **al menos 9 de 10 completan sin ayuda, SUS promedio ≥ 68 y menos de 1 error por tarea en promedio**.
4. Registrar en `progress/usabilidad/rondas.md` (plantilla ya preparada). No se guardan nombres ni datos personales: un código por persona.

| Ronda | Persona (código) | Éxito sin ayuda | Errores | Tiempo (min) | SUS (0–100) | Problema principal |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| | | | | | | |

**Cierre.** Cuando A, B y C estén completos y cumplan, se anotan los resultados en `specs/web-base/verificacion.md` (§3, quitar la reserva), se marcan T32 a T34 en `tasks.md` y, si ya no queda otra reserva, se pasa la spec a `Done`.
