# Prototipo v2 de BabelHub

Prototipo **descartable** en HTML estático. Valida el sistema de diseño de `docs/06-experiencia-de-usuario/` y los recorridos de todos los actores: Tienda (cliente y visitante), Sucursal (encargado), Logística y Administración. No forma parte de la solución .NET.

- **Stack:** Tailwind CSS 4 (build del navegador) y daisyUI 5 por CDN, con fuentes de Google Fonts. Necesita Internet.
- **Identidad:** «Babel cromático + laberinto de historias». Tokens en `assets/theme.css`, con los temas `babelhub` (claro) y `babelhub-dark` (oscuro). Por defecto sigue la preferencia del sistema.
- **Portadas:** generadas con motivos propios (`BH.cover` en `assets/app.js`); no se usan portadas reales.
- **Datos:** ficticios, en `assets/data.js` (catálogo) y `assets/data-paneles.js` (paneles). Todo lo que se hace se guarda en `localStorage`: carrito, biblioteca, pedidos, stock, traslados, usuarios, reclamos y auditoría. Para reiniciar, borra los datos del sitio en el navegador.
- **Gráficos:** barras con leyenda, etiqueta directa, descripción accesible, tooltip y vista de tabla. Los dos colores de serie pasaron el validador de paletas en ambos temas (`--bh-s1` y `--bh-s2`).
- **Controles del prototipo:** barra inferior izquierda con selector de **estado** de pantalla; en la Tienda, de **sesión**; en los paneles, el selector **Actor** de la cabecera (cambia el rol del token).

## Cómo abrirlo

```bash
python -m http.server 8081 --directory prototype/ui-v2
```

Luego abre `http://localhost:8081/`. En el índice, cada panel entra con su actor (`?actor=encargado|logistica|admin`).

## Qué cubre

| Área | Pantallas | Reglas que demuestra |
| :--- | :--- | :--- |
| **Tienda** | Inicio, catálogo, detalle impreso y digital, carrito, checkout de 4 o 5 pasos, mi biblioteca, pedidos y devoluciones, cuenta, Libro de Reclamaciones, páginas legales, El índice infinito | Stock real por sucursal, reserva de 15 minutos, inventario caído sin cobrar, licencia vencida, descargas con tope y enlace de 5 minutos, consentimiento de WhatsApp. |
| **Sucursal** | Resumen con alertas, inventario, ajustes, pedidos de recojo, traslados | Ajuste que no deja el vendible en negativo, 403 por sucursal, traslado que no se despacha sin stock vendible en el origen, recojo que espera su traslado. |
| **Logística** | Traslados nacionales con guía, guías de envío | Guía generada con el operador, seguimiento y avisos. |
| **Administración** | Indicadores, catálogo con licencias, sucursales, usuarios y roles, pedidos, reclamos, auditoría | ISBN único por edición, licencia por título, nueva sucursal como dato, un encargado por sucursal, anulación con motivo, sin permiso por rol, auditoría de solo lectura. |
