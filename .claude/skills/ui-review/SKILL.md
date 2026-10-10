---
name: ui-review
description: Revisa visualmente una pantalla de BabelHub.Web en el navegador (responsividad, temas claro y oscuro, teclado, contraste, estados y paridad con el prototipo). Úsala después de implementar una pantalla o cuando se pida «revisar la interfaz» o «verificar la UI».
---

# ui-review — Revisión de interfaz

## Preparación

1. Levanta la Web (o, para el prototipo, `python -m http.server 8081 --directory prototype/ui-v2`).
2. Usa el navegador integrado (herramientas `mcp__Claude_Browser__*`): `preview_start` o `navigate`. Lee con `read_page` y `get_page_text` antes de recurrir a capturas.

## Lista de comprobación

1. **Paridad con el prototipo:** misma estructura, textos y comportamiento; las diferencias deben tener motivo.
2. **Responsividad:** anchos de 375, 768 y 1280 px (`resize_window`). Sin desplazamiento horizontal; controles tocables; el asistente de compra no tapa precios ni acciones.
3. **Temas:** claro y oscuro (`resize_window` con `colorScheme` y el botón del sitio). Mismo diseño con tokens distintos; sin colores sueltos.
4. **Estados estándar:** cargando, vacío, error con reintento, sin permiso, servicio no disponible.
5. **Teclado y foco:** recorre con Tab; foco siempre visible; enlace «saltar al contenido»; los diálogos atrapan el foco y se cierran con Escape.
6. **Contraste:** texto normal ≥ 4,5:1 en ambos temas; el estado nunca depende solo del color (texto + icono).
7. **Movimiento:** con `prefers-reduced-motion` no hay animaciones y todo funciona.
8. **Consola y red:** `read_console_messages` sin errores; ninguna petición a servicios internos desde el navegador (todo pasa por el servidor); ninguna credencial visible.
9. **Datos reales:** nada se simula (stock, precios, avisos).

## Informe

Lista cada hallazgo con pantalla, ancho/tema, qué se ve, qué debería verse y el archivo a corregir. Si todo cumple, di qué se comprobó y qué no.

## Reglas

- Restablece la emulación de ancho al terminar (`resize_window` con `preset: desktop`).
- No inicies sesión con cuentas reales ni envíes formularios con datos personales.
