# Relevo del carril A (plataforma y transversales)

Última actualización: 2026-11-19. Rama `claude/competent-khayyam-1d6d28` (worktree `mystifying-panini-0d9826`). **Sin push** (nunca).

## 1. Rol y rama

Carril A: Plataforma, Auditoría, Notificaciones, Identidad (y su composición en el anfitrión), migrador COMP-2, sembrador de sesiones de desarrollo. Los módulos de otros carriles (Carrito, Pagos, Pedidos, Envíos, Devoluciones, Web, Inventario de B) solo se tocan por contrato y con aviso al coordinador.

## 2. Hecho (últimos commits en la rama)

- **Notificaciones completas** (T06, T08–T25 de `transversal-notificaciones`): estados del aviso, tope por número, n8n firmado, estados de entrega firmados, trabajos de fondo solo en la réplica de la tienda, Nginx con 404 a las rutas internas, flujo de n8n versionado (`infra/n8n/`). Falta la **prueba de humo del runtime real de n8n** (FIN-2): `infra/n8n/README.md` paso 2 es obligatorio antes de activar avisos.
- **COMP-2**: cada servicio migra solo cuentas con su prefijo (`ecommerce_`, `inventario_`), `REVOKE CONNECT FROM PUBLIC`.
- **Sembrador de sesiones** (`d35d2fc0`): `POST /desarrollo/sesion`, solo Debug + Development, loopback, clave `BABELHUB__Sembrador__Clave`. Uso en `tests/e2e/README.md`.
- **D20 en Identidad** (`7fc74b69`): sucursal del encargado por código, validada contra `sucursales.listar` (`ICatalogoDeSucursales`).
- **Lista de usuarios y sesión reconocida** (`cf856c3b`): `IAdministracionDeUsuarios.Listar`, `IDispositivosDeLaCuenta.Reconoce`, pantalla de usuarios.
- **D20 opción (b) en el Inventario** (ver §3 si aún no está commiteado): `Fuente.IdentificadorDe(codigo)` es el `Id` de toda fuente.

## 3. En curso

- Cambio del Inventario (opción b): código, pruebas y documentación listos y verdes; **faltaba cerrar los revisores de seguridad y arquitectura y commitear**. Revisa `git status`: si hay cambios en `src/BabelHub.Inventario/.../Fuentes`, `tests/.../Fuentes`, `docs/03/08` (fila D20) y `specs/inventario-fuentes/decisiones-de-implementacion.md`, son esos; corre las pruebas de Fuentes/Reservas/VerificadorDelInventario y commitea.
- **No tomar ECO-8 (biblioteca digital)**: va a un carril nuevo (decisión del coordinador, plan del sprint 2).

## 4. Decisiones vigentes

- 2026-11-19, coordinador: opción (b), el `Id` de una fuente es el `Guid` derivado del código; el contrato REST no cambia (D15).
- 2026-11-19: `IdentificadorDeSucursal` vive en `Application/Plataforma` del E-commerce, **no en `Shared`** (regla 6); el Inventario lo tiene en el Dominio de Fuentes (`Fuente.IdentificadorDe`) porque el Dominio no ve la Aplicación; los mismos valores fijos están en las pruebas de los dos servicios.
- Nunca se aprueban specs o planes por iniciativa propia; ante un hallazgo grave se reporta.

## 5. Trampas conocidas

- Fixtures de PostgreSQL: **sin literal `Password=`** (usar variable local o valor generado en ejecución); la prueba `SecretosEnElRepositorioTests` lo detecta.
- Los catálogos de auditoría se **espejan** en los dos servicios (y la prueba de copias lo exige).
- No agregar excepciones a las pruebas de arquitectura (se intentó con Shared y se revirtió; la regla 6 manda).
- Las pruebas de Release: lo que depende de código `#if DEBUG` va en archivos bajo `#if DEBUG`.
- El bash del entorno rompe con heredocs con apóstrofes: usar las herramientas Write/Edit. No hay Python; Node 26 en `C:\Program Files\nodejs`.
- `progress/hallazgos.md`: cada fila nueva va **sin línea en blanco** antes (rompe la tabla).

## 6. Hallazgos abiertos de este carril (ver `progress/hallazgos.md`)

- Alcance y auditoría: la búsqueda de un cliente por correo exacto debe **auditarse** cuando una pantalla la ofrezca; el cambio de sucursal no queda en la auditoría de cambio de rol; bajar `LargoMaximoDelCodigo` a 20 cuando se confirme D15.
- Producción: no hay arranque que cree la primera clave de firma ni siembre `identidad.versiones_de_textos` (FIN-2/despliegue).
- Renovación de la sesión actual (`ISesionesWeb.ObtenerRenovacionDeLaSesion`): es del carril C; sin ella cambiar contraseña o segundo factor cierra también la sesión actual.
- `HttpClienteDelInventario.cs.tmp` quedó versionado por un merge: borrarlo.

## 7. Siguiente encargo y aviso

Prioridad del sprint 2 (`docs/07-proceso-de-desarrollo/06-plan-sprint-2.md`): evidencia (OpenAPI, k6, cobertura, base de datos y caché) y cerrar el recorrido de compra. Avisar al coordinador con `SendMessage` a la sesión que te inició, en una línea, al cerrar cada bloque.
