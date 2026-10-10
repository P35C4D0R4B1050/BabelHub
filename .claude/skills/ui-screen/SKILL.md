---
name: ui-screen
description: Construye una pantalla de BabelHub.Web (vista Razor, ViewModel, cliente HTTP y fragmentos htmx) a partir de su prototipo, su spec y el sistema de diseño de docs/08. Úsala al implementar las tareas de Web de una feature o cuando se pida «crear la pantalla de X».
---

# ui-screen — De prototipo a vista Razor

## Entradas

1. La spec y el `plan.md` de la feature (sección «Pantallas»).
2. El prototipo en `prototype/ui-v2/<área>/<pantalla>.html` (estructura, textos, estados).
3. `docs/08-interfaz-de-usuario.md` (identidad, tokens, estados, accesibilidad) y `docs/glossary.md`.

## Reglas

- **Una sola app, tres áreas:** `Areas/Tienda`, `Areas/Sucursal`, `Areas/Admin`. El menú y las acciones dependen del rol y del alcance del usuario.
- La Web **no tiene reglas de negocio**: valida la forma para ayudar al usuario, pero la autoridad es siempre el servicio. Llama a los servicios **desde el servidor** con clientes tipados en `Clients/` (contratos de `BabelHub.Shared`).
- **Sistema de diseño:** Tailwind CSS + daisyUI con los temas `babelhub` y `babelhub-dark`. Usa clases de componentes (`btn`, `card`, `badge`, `modal`…) y tokens; **ningún color suelto**. Las clases del prototipo que dependan de utilidades propias (`bh-celda`, `chip`, `bh-display`) se trasladan a la hoja de estilos del proyecto.
- **Estados estándar** en toda pantalla de datos: cargando (esqueleto), vacío, error con reintento, sin permiso y servicio no disponible.
- **Interactividad** con htmx (fragmentos parciales de Razor) y JavaScript mínimo.
- **Accesibilidad:** etiquetas, roles, foco visible, enlace para saltar al contenido, `aria-live` en avisos, contraste AA, `prefers-reduced-motion`. El estado nunca depende solo del color.
- **Formato:** soles (`S/ 1.234,50` en es-PE) y fechas peruanas; textos en español.
- **Seguridad:** formularios con token anti-falsificación; ninguna credencial en el navegador; las descargas redirigen a una URL firmada, nunca transmiten el archivo.
- Los textos de error vienen de códigos estables del servicio, nunca de trazas.

## Pasos

1. Crea el `ViewModel` y el cliente HTTP (si no existen), con su prueba de contrato.
2. Crea la vista y las vistas parciales; reproduce la estructura y textos del prototipo.
3. Implementa cada estado estándar y los errores del servicio.
4. Verifica con la skill `ui-review` y, si hay recorrido completo, una prueba E2E con Playwright.
5. Commit pequeño (`feat:`).
