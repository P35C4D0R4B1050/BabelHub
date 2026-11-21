# Lista manual de accesibilidad: teclado y lector de pantalla

Complementa la revisión automática con axe (`tests/e2e/base/accesibilidad-del-recorrido.spec.ts` y las pantallas de cada spec), que no detecta todo. La ejecuta **una persona** antes de pasar `web-base` a `Done` y después de un cambio grande de interfaz. Respalda WEB-10, WEB-11 y WEB-20 (AC-WEB-07 y AC-WEB-15). Resultado: **0 incumplimientos críticos de nivel AA**; lo que falle se corrige o se registra como riesgo en [`progress/hallazgos.md`](../../progress/hallazgos.md).

## 1. Preparación

| Elemento | Decisión |
| :--- | :--- |
| Entorno | El local de `tests/e2e/entorno/` con datos ficticios y `sembrar-datos.sh --limpiar`. |
| Lectores de pantalla | NVDA con Firefox o Chrome en Windows; VoiceOver con Safari en iPhone (el celular es la mayoría de la demanda). |
| Navegadores | Las dos últimas versiones de Chrome, Edge, Firefox y Safari (AC-WEB-18). |
| Tema | Cada paso se repite en `babelhub` y `babelhub-dark`. |
| Registro | Una fila por paso en la tabla de la sección 4: persona, fecha, navegador, lector, resultado y observación. |

## 2. Teclado (sin ratón)

Para cada pantalla del recorrido (inicio, catálogo, ficha, carrito, checkout, pago, estado, ingreso, registro, mi cuenta, mis pedidos y biblioteca):

1. `Tab` desde el principio: el primer destino es «Saltar al contenido» y lleva al contenido principal.
2. El orden de foco sigue el orden visual, sin trampas de teclado, y **todo control muestra un foco visible**.
3. Los controles se operan con `Enter` o `Espacio`; los grupos de opción (medio de pago) con las flechas.
4. Los diálogos y avisos con foco inicial lo reciben y `Esc` los cierra cuando corresponde; al cerrar, el foco vuelve al control que los abrió.
5. Los errores de un formulario llevan el foco al aviso, que dice qué corregir sin repetir lo escrito.
6. Con `prefers-reduced-motion` activo no hay animaciones.
7. Con el zoom al 200 % y a 320 px de ancho no hay pérdida de contenido ni desplazamiento horizontal.

## 3. Lector de pantalla

| Comprobación | Esperado |
| :--- | :--- |
| Título y encabezados | Cada página tiene un único nivel 1 y encabezados en orden; el título de la pestaña dice la pantalla. |
| Regiones | Hay `banner`, `navigation` con nombre, `main` y `contentinfo`. |
| Formularios | Cada campo se anuncia con su etiqueta, su ayuda y si es obligatorio; los errores se anuncian al aparecer (`aria-live`). |
| Estado | El estado nunca depende solo del color: se anuncia con texto («Pago pendiente», «Hay stock en la red»). |
| Tablas | Tienen título (`caption`), cabeceras con alcance y las regiones desplazables se anuncian con su nombre. |
| Cambios dinámicos | El estado del pago y los avisos se anuncian sin mover el foco. |
| Imágenes | Las portadas tienen texto alternativo útil; las decorativas, vacío. |
| Enlaces y botones | El nombre dice la acción («Ver el pedido», no «Aquí»). |

## 4. Registro de la ejecución

| Fecha | Persona | Navegador y lector | Pantalla o recorrido | Resultado | Observación y registro del hallazgo |
| :--- | :--- | :--- | :--- | :--- | :--- |
| *(pendiente: la ejecuta la persona responsable)* | | | | | |
