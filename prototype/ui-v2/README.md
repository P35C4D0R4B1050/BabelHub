# Prototipo v2 de BabelHub (entrega 1 de 2)

Prototipo **descartable** en HTML estático. Valida el sistema de diseño de `docs/08-interfaz-de-usuario.md` y el área **Tienda**, con libros impresos y digitales. No forma parte de la solución .NET. La entrega 2 añadirá Sucursal, Logística y Admin.

- **Stack:** Tailwind CSS 4 (build del navegador) y daisyUI 5 por CDN, con fuentes de Google Fonts. Necesita Internet.
- **Identidad:** «Babel cromático + laberinto de historias». Tokens en `assets/theme.css`, con los temas `babelhub` (claro) y `babelhub-dark` (oscuro). Por defecto sigue la preferencia del sistema.
- **Portadas:** generadas con motivos propios (`BH.cover` en `assets/app.js`); no se usan portadas reales.
- **Datos:** ficticios, en `assets/data.js`. Carrito, biblioteca y pedidos se guardan en `localStorage`.
- **Controles del prototipo:** barra inferior izquierda con selector de **estado** de pantalla y de **sesión** (cliente o visitante).

## Cómo abrirlo

```bash
python -m http.server 8081 --directory prototype/ui-v2
```

Luego abre `http://localhost:8081/`. También funciona abriendo `index.html` directamente.

## Qué cubre

| Pantalla | Qué valida |
| :--- | :--- |
| Inicio | Hero editorial, colecciones y «Sorpréndeme». |
| Catálogo | Filtros por formato, categoría, precio y disponibilidad; estados cargando, vacío y error. |
| Detalle | Selector de edición impreso o digital, licencia, stock por sucursal, licencia vencida. |
| Carrito y checkout | Carrito mixto; checkout de 4 o 5 pasos según el contenido; reserva de 15 minutos; inventario caído; pago rechazado. |
| Mi biblioteca | Descargas con tope de 5 y enlace seguro de 5 minutos que vence. |
| Pedidos | Seguimiento por línea y devolución con política distinta para digitales. |
| Cuenta, Reclamos, Legales | Consentimiento de WhatsApp, derechos sobre datos, Libro de Reclamaciones con constancia imprimible. |
| El índice infinito | Generador determinista propio, aislado del flujo de compra. |
