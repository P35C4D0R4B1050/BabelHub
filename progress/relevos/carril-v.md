# Relevo del carril V (Envíos y Devoluciones)

Lee primero `AGENTS.md`, `CLAUDE.md`, `progress/ajustes-al-implementar.md`, `progress/coordinacion.md` y `progress/relevos/coordinador.md` §6 (trampas). Estado a 2026-11-20, sobre `main` en `43a1734a` (el commit `989169c5` ya está dentro).

## Hecho
- Planes y `spec-verify` de 6 specs de comercio.
- **Envíos** 20/25: ECO-9a a 9d y parte de 9e (`RestReservasDelEnvio` sobre `CerrarReserva`). `IDespachoDeEnvios.Cancelar` ya informa «ya cancelado» como `Mantenido` (con prueba).
- **Devoluciones** 22/34: ECO-10a a 10d (T01–T22), con revisiones de arquitectura y seguridad cerradas en 10a, 10b y 10c. Bitácoras en `progress/log/2026-11-19-ECO-10*.md`; decisiones en `specs/ecommerce-devoluciones/decisiones-de-implementacion.md`.
- Piezas de Devoluciones: Dominio (`Devolucion`, políticas, `TextoRedactado`), casos de uso (`Application/Devoluciones/Interno/*`: solicitar impreso, reembolso digital, reportes, aprobar/rechazar, confirmar recepción, conciliación, revisión de plazos y estancadas, consultas, avisos, pendientes y redacción de cuenta, depuración de dos años), fachadas públicas, Infraestructura (`Infrastructure/Devoluciones/*`: esquema con disparadores, `RepositorioDeDevolucionesPostgres`, `RestInventarioDeDevoluciones`, trabajos de fondo) y `Composicion/ComposicionDeDevoluciones.cs`.

## Pendiente (en este orden)
1. **ECO-10d (T19–T22) sin revisiones**: pasar `arch-reviewer` y `security-reviewer` sobre `git diff` de `cef645f5..989169c5` (solo lo de Devoluciones y `IClienteDelInventario.ReingresarDevolucion`), corregir Importantes. En la última suite completa de integración fallaron 5 pruebas ajenas (Web `RecorridoDeRutas` y `ProteccionContraFalsificacion`, Auditoría `CargaDelAcumulado`, Plataforma `RegistroDeSupresionesEnObjetos`, imágenes `HumoDeImagenes`): comprobar si ya fallaban en `main`. Las 46 pruebas de Devoluciones pasaron con PostgreSQL (necesitan Docker).
2. **Devoluciones T23–T34**: T23–T24 acciones MVC de la Api (rechazo de `multipart`, `[ParametroDeSucursal]`, problem+json sin motivo ni texto), T25 `BackgroundService` de la Api (`AgregarTrabajosDeFondoDeDevoluciones`, patrón `TrabajosDeFondoDeEnviosEnLaApi`), T26–T28 vistas (esperan COMP-1), T29–T33 integración y E2E, T34 docs (`05-modelo-de-datos` §3, `04-modulos-y-contratos` §1.1, `04-privacidad`). Adoptar `IAccesosParaDevoluciones` real de Biblioteca (hoy `AccesosParaDevolucionesProvisional`) y quitar el doble de la composición/guarda cuando esté; revisar `IPedidosParaDevoluciones` y `IReembolsos` reales. La equivalencia identificador/código de sucursal (`ResolucionDeSucursalDeDevolucionesProvisional`) sigue sin fuente: hallazgo en `progress/hallazgos.md`.
3. **Envíos**: T14b (cliente real, diferido por D02), T20, T21b, T22, T24; el Pedidos real de B ya está en main.
4. `spec-verify` de `ecommerce-envios` y `ecommerce-devoluciones`.

## Trampas
- En bash, los heredocs con comillas simples mezcladas fallan: escribe archivos con la herramienta Write y usa scripts de perl con `use utf8;` (escapa `@` en los patrones).
- Fixtures de PostgreSQL sin literales de contraseña (`var clave = ...; Password = clave`).
- Los contratos de Pedidos son de B; espejar los catálogos de auditoría en los dos servicios (Shared, Ecommerce e Inventario).
- Una prueba larga de integración bloquea los DLL: no compiles mientras corre.

## Cómo avisar
Mensaje de una línea al coordinador al cerrar cada bloque; turno de integración para las pruebas con Docker.
