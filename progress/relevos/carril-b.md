# Relevo del carril B (Inventario → Carrito y Pedidos)

Lee primero `AGENTS.md`, `progress/coordinacion.md`, `progress/decisiones-delegadas.md` y `progress/relevos/coordinador.md` §6 (trampas). Nombre en el tablero: «Carril B».

## Hecho (todo en `main`)
Inventario completo: `inventario-fuentes` 25/25, `inventario-seleccion-fuente` 33/33, `inventario-ajustes` 18/18, `inventario-traslados` 26/26. `ecommerce-carrito-checkout` 35/37 (decisiones del carrito en `progress/decisiones-delegadas.md`, DD-01 a DD-15, delegación de la persona). `ecommerce-pedidos` T01–T30 (Dominio, Aplicación, adaptadores reales sobre `CerrarReserva`, límites y auditoría, esquema `pedidos`, trabajos diarios). Contratos de Pedidos para Envíos y Devoluciones publicados. Entorno E2E en `tests/e2e/entorno/` (PostgreSQL y Redis aislados, migrador, hosts 8080 y 8082; secretos aleatorios fuera del repo en `%LOCALAPPDATA%\babelhub-e2e`).

## En curso / siguiente
0. **Cambio de tope decidido por la persona (2026-11-19): 20 unidades por línea y 30 líneas por reserva** (antes 100 y 50). Aplícalo en `LimitesDeReservas` del Inventario y, si coincide, en los topes del carrito (DD-01 fija 50 líneas y 99 unidades por línea del carrito: concilia ambos y regístralo), con pruebas; añade el residual en AM01 (`docs/05-seguridad/01-modelo-de-amenazas.md`). No edites la spec aprobada. Es prioritario y pequeño (hallazgo en `progress/hallazgos.md`).
1. **T36 del carrito (E2E con compra):** falta el sembrado de catálogo e inventario (obras y ediciones; sucursales, fuentes con `Id` = `Fuente.IdentificadorDe(código)`, stock). Para clientes con sesión existe `POST /desarrollo/sesion` (solo Development, cabecera `X-Sembrador-De-Desarrollo`, ver `tests/e2e/README.md`). Navegadores: Chromium escritorio, Chromium teléfono y WebKit obligatorios; Firefox informativo.
2. **`ecommerce-pedidos` ECO-6f (T31–T42):** fachadas y Api (`PedidosNoDisponiblesException` → 503 en `ManejadorDeExcepciones`, solo aditivo), pantallas, pruebas e integración. Faltan motivos de alerta propios de Pedidos (depuración, contacto sin liberar, líneas sin estado final) por catalogar en los dos servicios.
3. Las bases de desarrollo con fuentes creadas antes de D20 deben recrearse.

## Decisiones vigentes
D15/D20: el `SucursalId` del encargado es el `Guid` derivado del código (`IdentificadorDeSucursal`); `Fuente.Id` lo usa. El E2E del carrito pasa por el host compuesto de A (COMP-1).

## Trampas
Fixtures de PostgreSQL sin literales de contraseña; catálogos de auditoría espejados en los dos servicios con texto español en el presentador; no declarar contratos de otros módulos; ninguna excepción nueva a pruebas de arquitectura.

## Cómo avisar
Un mensaje de una línea al coordinador al cerrar cada bloque (rama y último commit). Integración completa solo con turno.
