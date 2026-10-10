# Rendimiento percibido y experiencia móvil

La demanda fuera de Lima llega desde celulares con conexión variable (NN16, DA14). Este documento fija el presupuesto de rendimiento, las técnicas para cumplirlo con un servidor de 1 OCPU y cómo se mide. Respalda RNF26, WEB-21 y AD29.

## 1. Metas

| Métrica | Meta | Páginas | Condición de medición |
| :--- | :---: | :--- | :--- |
| **LCP** (carga del contenido principal) | ≤ 2,5 s | Inicio, catálogo, detalle | Percentil 75; móvil con 4G simulada |
| **INP** (respuesta a la interacción) | ≤ 200 ms | Las mismas | Percentil 75 |
| **CLS** (estabilidad visual) | ≤ 0,1 | Las mismas | Percentil 75 |
| Peso de la página de inicio sin imágenes diferidas | ≤ 500 KB *(propuesta)* | Inicio | Transferencia comprimida |
| Solicitudes iniciales | ≤ 30 *(propuesta)* | Inicio | |

LCP, INP y CLS son las métricas de Core Web Vitals. Los valores son metas iniciales y se validan en un servidor de 1 OCPU con las pruebas de carga.

## 2. Técnicas

| Área | Técnica |
| :--- | :--- |
| **HTML primero** | Las páginas se renderizan en el servidor (MVC con Razor); htmx actualiza fragmentos en lugar de cargar una aplicación de página única. El contenido principal llega en el primer documento. |
| **Imágenes** | Portadas en formato moderno (WebP o AVIF), con `srcset` y tamaños acotados; carga diferida fuera de pantalla (`loading="lazy"`); dimensiones explícitas para evitar saltos (CLS). La imagen principal de la página (LCP) no se difiere. |
| **CSS** | Un solo archivo compilado con la CLI de Tailwind, purgado y minificado; sin hojas de estilo de terceros bloqueantes. |
| **JavaScript** | Mínimo: htmx y pocos scripts propios, con `defer`. El script de la pasarela solo se carga en la página de pago. |
| **Fuentes** | Autoalojadas, subconjunto latino, `font-display: swap`; dos familias como máximo. |
| **Caché** | Recursos estáticos con huella en el nombre y caché larga; catálogo con caché de salida corta; disponibilidad con la caché de AD06. |
| **Compresión** | Brotli o gzip en Nginx. |
| **Servidor** | Consultas con paginación y topes; sin consultas N+1; disponibilidad desde caché y validación definitiva solo al reservar. |
| **Degradación** | Sin JavaScript, la navegación, el carrito y el checkout siguen funcionando con formularios normales. |

## 3. Experiencia móvil

| Tema | Decisión |
| :--- | :--- |
| Diseño | Móvil primero: se diseña a 375 píxeles y se amplía; sin desplazamiento horizontal (AC-WEB-01). |
| Objetivos táctiles | Al menos 24×24 píxeles (WCAG 2.5.8); las acciones principales de 44 píxeles. |
| Teclados | Tipos de entrada adecuados (`inputmode`, `autocomplete`) para teléfono, correo y códigos. |
| Checkout | Una columna, resumen siempre visible y acción principal al alcance del pulgar. |
| Conexión | Estados de carga con esqueletos; reintento claro ante error; el carrito se conserva si se pierde la conexión (WEB-26). |
| Pagos | Yape y tarjeta con el formulario de la pasarela adaptado al móvil; la reserva muestra su tiempo restante. |
| Modo oscuro | Respeta la preferencia del dispositivo (WEB-09). |

## 4. Medición

| Herramienta | Uso | Cuándo |
| :--- | :--- | :--- |
| **Lighthouse en CI** | Puntaje y métricas de laboratorio sobre inicio, catálogo y detalle con perfil móvil; falla si se incumple el presupuesto. | Cada cambio de interfaz |
| **k6** | Carga sobre las rutas críticas con 1 OCPU simulado (RNF02). | Antes de cada despliegue mayor |
| **Medición real** | Registro opcional de métricas web de personas reales (sin identificarlas) para verificar el percentil 75. | Después del despliegue |

El laboratorio no sustituye al campo: si ambos difieren, manda la medición real.

## 5. Control de regresiones

El presupuesto es un criterio de la spec (WEB-21). Un cambio que lo supera se corrige antes de integrarse; no se relaja la meta para que pase. Los hallazgos de rendimiento sin solución inmediata se registran como riesgo en los [drivers](../02-arquitectura-de-software/01-drivers-arquitectonicos.md).
