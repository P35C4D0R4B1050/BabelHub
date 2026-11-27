# 2026-11-27 — La ficha del libro no permitía comprar

**Carril de corrección** (sesión de trabajo por encargo de la coordinación). Rama `claude/dazzling-lewin-3cbe83`.

## Qué se hizo
- `ecommerce-carrito-checkout` T38: la ficha ofrece «Agregar impreso al carrito» (con cantidad) y «Comprar digital», con formularios sin JavaScript; agotado, deshabilitado; sin poder confirmar el stock, permitido (CAT-12); sin botones con textos pendientes.
- El rechazo por falta de stock dice el máximo en pantalla (CAR-04) y el resumen del checkout dice «Gratis (recojo)».
- Las pruebas nuevas y los recorridos E2E envían el formulario que ofrece la página. Guía 08 §15.1 corregida: en `v0.1.1` no se puede comprar desde la web.

## Pendiente / siguiente
- Persona: publicar una versión nueva (imágenes) y redesplegar antes de enviar la tienda a Culqi; cargar catálogo y stock.
- Pantallas y acciones sin enlace y otras observaciones: `progress/hallazgos.md` (2026-11-27).

## Qué se probó de verdad y qué no
- **Probado:** compilación sin advertencias; unitarias del E-commerce (5770), arquitectura (261) e integración de pantallas web, carrito y catálogo (1700), todo en verde; `SpecCheck` sin errores ni avisos.
- **Recorrido en navegador (Playwright contra el entorno local, datos ficticios), claro y oscuro, con `ninguna`:** ficha, carrito, cambiar y quitar, ingreso real con fusión, checkout con recojo y con domicilio hasta «El pago en línea estará disponible pronto»; sin JavaScript y en teléfono; axe sin violaciones.
- **E2E con el simulador (escritorio y teléfono):** carrito, fusión, pago, pedidos, biblioteca, catálogo, humo y accesibilidad en verde salvo la comprobación de «@» de la biblioteca (en `hallazgos.md`). Una siembra de sesión de administración falló una vez (base no disponible) y el humo de teléfono recibió un 429 tras corridas seguidas: ambos pasaron al repetir.
- **No probado:** el registro de una cuenta nueva en el navegador (el entorno local no completa Turnstile ni envía el correo); WebKit y Firefox; la tienda desplegada.
