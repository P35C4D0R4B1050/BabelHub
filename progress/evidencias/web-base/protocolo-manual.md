# Web base: protocolo manual de T32b, T33b y T34b

Lo ejecutan **personas**; el agente no lo simula ni inventa resultados. Tres bloques independientes, cada uno con su registro. Guías completas: `docs/06-experiencia-de-usuario/09-pruebas-de-usabilidad.md` (usabilidad) y `docs/06-experiencia-de-usuario/10-lista-manual-de-accesibilidad.md` (teclado y lector). Evidencia automática ya hecha: `2026-11-21-corrida-automatica.md`.

**Preparación común (15 min).** Entorno local de `tests/e2e/entorno/` (`preparar-entorno.sh`, host de la tienda, Inventario y `sembrar-datos.sh --limpiar`) o la URL de pruebas que indique la persona responsable; datos ficticios; una cuenta de cliente nueva por persona de prueba. Registrar versión del navegador y del sistema.

## A. Navegadores y móviles reales (T32b, AC-WEB-18; ~1,5 h)

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

## B. Teclado y lector de pantalla (T33b, AC-WEB-15; ~2 h)

1. Seguir los apartados 2 (teclado) y 3 (lector) de `10-lista-manual-de-accesibilidad.md` en las pantallas del recorrido de compra, mi cuenta, mis pedidos, biblioteca y el Libro de Reclamaciones, con NVDA (Windows, Firefox o Chrome) y VoiceOver (iPhone, Safari), en `babelhub` y `babelhub-dark`.
2. Criterio: **0 incumplimientos críticos de nivel AA** (cualquier paso donde la persona no pueda continuar, o un control sin nombre, o un error que no se anuncia).

| Fecha | Persona | Lector y navegador | Pantalla | Tema | Resultado (cumple/falla) | Observación |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| | | | | | | |

## C. Dos rondas de usabilidad (T34b, AC-WEB-17; ~4 h por ronda)

1. Cada ronda: 5 personas distintas que no conocen el sistema; tarea única: «compra un libro con recojo» sin ayuda (guion y consentimiento en `09-pruebas-de-usabilidad.md`). Anotar éxito sin ayuda (sí/no), errores y tiempo; al final, el cuestionario SUS de 10 preguntas.
2. Entre las rondas se corrigen los problemas más graves; la ronda 2 comprueba la corrección.
3. Criterio de aceptación: **al menos 9 de 10 completan sin ayuda, SUS promedio ≥ 68 y menos de 1 error por tarea en promedio**.
4. Registrar en `progress/usabilidad/rondas.md` (plantilla ya preparada). No se guardan nombres ni datos personales: un código por persona.

| Ronda | Persona (código) | Éxito sin ayuda | Errores | Tiempo (min) | SUS (0–100) | Problema principal |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| | | | | | | |

**Cierre.** Cuando A, B y C estén completos y cumplan, se anotan los resultados en `specs/web-base/verificacion.md` (§3, quitar la reserva), se marcan T32 a T34 en `tasks.md` y, si ya no queda otra reserva, se pasa la spec a `Done`.
