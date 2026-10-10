# Estados, accesibilidad y criterios de aceptación visual

## 1. Estados de pantalla estándar

Cada pantalla de datos contempla los mismos estados, con el mismo patrón visual (RNF13):

| Estado | Patrón |
| :--- | :--- |
| Cargando | Esqueletos con la forma del contenido; sin saltos de diseño. |
| Vacío | Icono, título breve, explicación y una acción clara. |
| Error | Aviso con la causa en lenguaje simple y un botón para reintentar. |
| Sin permiso (403) | Mensaje de que la cuenta no tiene acceso, con salida a una pantalla permitida. |
| Servicio no disponible (503) | Informa qué parte no responde (por ejemplo, «No podemos validar el stock ahora»); nunca cobra a ciegas. |
| Éxito | Aviso breve y no bloqueante, con `role="status"`. |

## 2. Microinteracciones y accesibilidad

| Elemento | Comportamiento |
| :--- | :--- |
| Tarjeta de libro | Elevar 3–5 px y reforzar la sombra al pasar el cursor. |
| Portada | Inclinación muy leve; escala máxima aproximada de 1,01. |
| Botones | Cambio de color y desplazamiento mínimo. |
| Buscador | Anillo de foco visible y panel de sugerencias (recientes, categorías, títulos). |
| Carrito | Pequeño rebote del contador al añadir un artículo. |
| Filtros | Estado activo claro y transición breve de resultados. |
| «Sorpréndeme» | Elige un libro disponible al azar y pasa a su ficha con una transición corta. |
| Fondo | Movimiento ambiental lento y opcional solo en áreas decorativas, nunca detrás del texto. |

Accesibilidad: contraste AA en ambos temas; navegación completa por teclado con foco visible; «saltar al contenido»; etiquetas y roles en formularios, tablas y diálogos; `prefers-reduced-motion` desactiva las animaciones sin perder funcionalidad; ninguna animación retrasa la respuesta de un control; el botón del asistente no tapa precios ni acciones en pantallas pequeñas.

## 3. «El índice infinito» (opcional)

Una pieza de marca separada del flujo de compra, accesible desde el catálogo. Genera una «página de Babel» de texto pseudoaleatorio a partir de una dirección: la misma dirección devuelve siempre el mismo texto. No requiere base de datos ni mezcla su contenido con búsquedas, inventario, reseñas o títulos reales; avisa de que el texto es generado y puede carecer de sentido, y enlaza de vuelta al catálogo real. Se implementa con un generador propio (sin reutilizar código de terceros, para no depender de licencias ajenas) y solo después de estabilizar el catálogo. Corresponde a la spec opcional `web-indice-infinito`.

## 4. Cumplimiento visible

Pie de todas las páginas de la Tienda: enlace permanente al **Libro de Reclamaciones** (RF25), términos y condiciones, política de privacidad y política de reembolso de libros digitales. Consentimiento de WhatsApp en el registro o en la cuenta y en el checkout. Detalle legal y su estado en [marco legal](../01-analisis-de-sistema/03-marco-legal.md).

## 5. Criterios de aceptación visual

- [ ] Se conserva la estructura funcional del catálogo.
- [ ] BabelHub tiene identidad reconocible más allá de una paleta oscura genérica.
- [ ] Los temas claro y oscuro son coherentes, legibles y cumplen contraste AA en texto.
- [ ] Coral, azul y papel definen la marca; verde, violeta y cian se usan con moderación.
- [ ] Las portadas siguen siendo el foco de las tarjetas.
- [ ] Filtros, precio, stock y carrito se localizan fácilmente.
- [ ] El stock y el formato nunca dependen solo del color.
- [ ] Las animaciones son breves, útiles y tienen alternativa con movimiento reducido.
- [ ] El checkout se completa en 4 o 5 pasos según el carrito.
- [ ] No se simula stock ni se confunden textos generados con productos reales.
- [ ] El experimento del índice infinito está claramente separado del flujo de compra.

## 6. Prototipos

| Prototipo | Ubicación | Estado |
| :--- | :--- | :--- |
| v1: recorrido funcional básico | `prototype/ui/` | Retirado (sigue en el historial de Git). |
| v2, entrega 1: sistema de diseño y área Tienda (incluye libros digitales) | `prototype/ui-v2/tienda/` | Aprobado en su identidad visual. |
| v2, entrega 2: áreas Sucursal, Logística y Administración | `prototype/ui-v2/{sucursal,logistica,admin}/` | Lista para revisión. |

Son descartables: HTML estático con Tailwind y daisyUI por CDN y datos ficticios. Lo aprobado en ellos queda reflejado en este documento antes de escribir las vistas Razor.
