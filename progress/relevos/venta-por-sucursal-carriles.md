# Relevo — Venta por sucursal: reparto en tres carriles (2026-11-27)

Parte de [`venta-por-sucursal.md`](venta-por-sucursal.md) (reglas) y de las decisiones DD-130 a DD-138. **DD-136 cambió el 2026-11-27 por decisión de la persona:** se devuelve el envío de una sucursal que no llegó a enviar; afecta a los carriles B y C (carrito, pagos y pedidos, reaprobados). **Plazo (corregido por la persona el 2026-11-27):** el 28-nov es solo la entrega del Sprint 2; esto es trabajo del Sprint 3 y de la entrega final, sin recortar alcance ni calidad. Cada carril lee su `spec.md`, la sección **§11 de su `plan.md`**, su bloque de `tasks.md` y `progress/ajustes-al-implementar.md`. Las decisiones de implementación van en `specs/<feature>/decisiones-de-implementacion.md`; el texto de `plan.md`, `tasks.md` y `decisions.md` no se edita (solo las casillas). Al empezar, cada spec pasa a `InProgress`.

## 1. Carriles

| Carril | Specs y tareas, en orden | Depende de |
| :--- | :--- | :--- |
| **A · Inventario** | `inventario-seleccion-fuente`: T34, T35, T36, T37, T44, T38, T42, T39, T40, T41, T43 (repetir k6) | Nada. **Entregar T34 primero** (commit propio) para que B pueda integrarlo. |
| **B · Carrito y tienda** | `ecommerce-carrito-checkout`: **T50 primero** (tipo y campo del envío por sucursal, commit propio solo con eso, para C) y T39 a T49; `web-base`: T40 (junto con T46); `inventario-seleccion-fuente`: T45 (motivo de la alerta, después de T44) | T43 espera T34 de A. T49 se cierra con los tres carriles integrados. |
| **C · Pedidos, pagos y devoluciones** | `ecommerce-pagos`: T53, T54, T55; `ecommerce-pedidos`: T43, T44, T45, T50, T51, T46, T52, T47, T48, T49 | Necesita T50 de carrito (B) para compilar T53 a T55 de pagos y, a través de T54, T51 de pedidos; lo demás compila solo. T49 usa el Inventario de A integrado. |

Orden de integración: T34 de A → B la fusiona; T50 de carrito (B) → C la fusiona; A completo → C → B → pruebas cruzadas y E2E de B (T49).

## 2. Archivos de cada carril (sin compartidos)

| Carril | Es dueño de |
| :--- | :--- |
| **A** | `src/BabelHub.Shared/Contratos/Inventario/*`; `src/BabelHub.Inventario/**` **salvo los archivos de acciones, de alertas y los catálogos de auditoría**; `docs/03-diseno-de-software/openapi/inventario.v1.json`; pruebas unitarias del Inventario (salvo las de auditoría), integración de `Reservas`, `Fuentes` y `Traslados`, y `tests/architecture/.../Contratos/ContratosDeDisponibilidadYReservasTests.cs`; `tests/load/**`, `progress/evidencias/k6/` (carpeta nueva) y las secciones de k6 de `docs/07-proceso-de-desarrollo/07-evidencias-sprint-2.md`; `specs/inventario-seleccion-fuente/`. |
| **B** | E-commerce: módulo `Carrito` (todas las capas), `Application/Plataforma/ClienteDelInventario.cs` e `Infrastructure/Plataforma/HttpClienteDelInventario.cs`, controladores y vistas de carrito, checkout, ficha y preferencias, cabecera, `ContextoDePagina`, `ModeloBaseDePagina`, `wwwroot/js/bh.js`, la pantalla de tarifas en `Areas/Admin`, el menú, la composición del anfitrión; `AccionesDeAuditoria` de `Shared` y los catálogos de auditoría **de ambos servicios** con sus pruebas, incluidos `MotivosDeAlertaDeAuditoria` de `Shared`, los `AlertasDeAuditoria` de ambos servicios y `PresentadorDeAuditoria`; `scripts/`, `infra/`; `tests/e2e/**` y las pruebas de integración que cruzan módulos (`PagosConElCarritoReal`, `PagosYPedidosReales`, `DevolucionesConPagosReal`, `CuentaConUnPedidoReal`); `specs/ecommerce-carrito-checkout/`, `specs/web-base/`. |
| **C** | Módulo `Pagos` (Dominio, Aplicación e Infraestructura, con `CompraAprobada` y `DefinicionDePagos`) y sus pruebas propias; módulos `Pedidos` y `Devoluciones` (todas las capas), sus controladores y vistas (incluida `Devoluciones/Devolver.cshtml`) y sus pruebas propias; `specs/ecommerce-pagos/` y `specs/ecommerce-pedidos/`. |
| Coordinadora | `progress/`, `docs/` y `specs/TRACEABILITY.md` al integrar (los carriles anotan en su `decisiones-de-implementacion.md`). |

Si un carril necesita tocar un archivo de otro, lo pide; no lo edita.

## 3. Puntos que no deben perderse

- El Inventario hoy **rechaza repetir una edición** en una reserva y cuenta las unidades vendidas por (pedido, edición): T36 y T42.
- `pedidos.lineas_una_por_edicion` y `pedidos_recojo_con_sucursal` harían fallar un pedido nuevo **después de cobrar**: T46 de `ecommerce-pedidos`.
- El E-commerce nuevo no funciona contra el Inventario anterior. El Inventario nuevo sí acepta al E-commerce anterior.
- `CAR-49`: las tarifas se leen una sola vez, dentro de la transacción que congela el total.
- T43 del carril A repite las mediciones de k6 del Sprint 2 con la reserva por sucursal; no usar ni tocar los contenedores `bhcarga-*` sin coordinarlo con su dueño.
- T45 la programa B y la reporta; su casilla y su nota en `specs/inventario-seleccion-fuente/` las marca la coordinadora al integrar.
- B: antes de seguir, fusionar la reaprobación de carrito (habrá conflicto en el estado de `spec.md`, ya en InProgress) y rehacer el congelado del envío: ahora se congela el importe de cada sucursal además de la suma, y lo congelado se descarta al volver a edición. T50 va en el siguiente commit, solo con el tipo `EnvioDeSucursalDeCompra` y el campo.
- La persona no tiene ningún pedido hecho (2026-11-27): antes de desplegar se comprueba que no haya pedidos, pagos pendientes ni anulaciones (si los hay, no se despliega sin consultar), se detienen `ecommerce` (VM-VENTAS) y `ecommerce-gestion` (VM-INVENTARIO) antes de migrar y no se vuelve a la versión anterior con anulaciones en curso.
- Rojo esperado entre carriles: con T53 integrada y sin T40 ni T42 de carrito, ninguna compra a domicilio crea intención y fallan `PagosYPedidosRealesTests`, `DevolucionesConPagosRealTests` y `CuentaConUnPedidoRealTests` (de B). C no las edita ni las debilita, y ese estado no se despliega; las E2E con compra a domicilio también fallan en ese estado.
- T44 (carril A): el soporte `PilaDeReservas` de integración valida la composición al construir y hoy no registra logging, así que habrá que añadir `AddLogging`; el campo del aviso no se llama `Codigo` (el redactor oculta ese nombre), sino `CodigoDeSucursal`.
- Un E-commerce sin el motivo de la alerta de concentración la da por atendida sin correo: desplegar Inventario y Ventas seguidos.

## 4. Variables, migraciones y despliegue (para «Guía en vivo»)

| Tema | Qué hace falta |
| :--- | :--- |
| Variables | Una nueva y **opcional** en el Inventario, `BABELHUB__Inventario__MaximoApartadoParaAlertar` (100 si falta). Cambiar `BABELHUB__Carrito__CostoDeEnvio` a `30.00` en `ecommerce.env` **y** `ecommerce-gestion.env`. |
| Migraciones | Inventario: restricción `ck_reservas_destino`. E-commerce: `carrito` (columnas, `envios_congelados`, claves y tabla `tarifas_de_envio`), `pagos` (`intenciones.envios_por_sucursal`) y `pedidos` (índice, restricción, `envios_por_sucursal` y `anulaciones.envio`). Devoluciones: sin cambios de esquema. Todas repetibles por el modo de migración. |
| Antes | Respaldo bajo demanda; comprobar que no haya **pedidos**, pagos pendientes de confirmación ni anulaciones (si los hay, consultar y no desplegar; con la pasarela `ninguna` no debería haberlos). Detener `ecommerce` en VM-VENTAS y `ecommerce-gestion` en VM-INVENTARIO. |
| Orden | `desplegar.sh inventario` en VM-INVENTARIO (migra las bases del Inventario y del E-commerce y levanta el Inventario y la gestión) y enseguida `desplegar.sh ventas` en VM-VENTAS, con los digests nuevos. |
| Después | Portada 200; la ficha muestra la sucursal y su stock; compra de dos sucursales a domicilio muestra S/ 60,00; la administración ve las tarifas en la gestión. |
| Datos existentes | Las líneas de carrito sin sucursal se piden de nuevo. No se asigna sucursal a nada. |
| Vuelta atrás | Corregir hacia adelante. La versión anterior no entiende recojos por sucursal ni varias líneas por edición; restaurar el respaldo pierde lo posterior y exige conciliar. |
## 5. Estimación, riesgos y qué podría separarse

Cifras **orientativas**, sin una medición previa que las respalde (trabajo de agente, con los tres carriles en paralelo):

| Carril | Tareas | Estimación |
| :--- | :---: | :--- |
| A · Inventario | 11 | 6 a 10 horas, con k6 |
| B · Carrito y tienda | 14 | 11 a 17 horas (camino crítico) |
| C · Pedidos, pagos y devoluciones | 13 | 8 a 13 horas |
| Integración, suite completa, E2E y correcciones | — | 4 a 8 horas; cada corrida completa tarda unos 40 minutos |

En calendario, de 2 a 3 días contando integración y la revisión de la persona.

**Riesgos:** B concentra el trabajo y las pruebas cruzadas, que solo corren al integrar; migraciones sobre carritos, checkouts y pedidos existentes; vuelta atrás solo hacia adelante una vez haya compras por sucursal; los dos servicios se despliegan seguidos; las mediciones con k6 varían entre corridas y RNF02 ya no se cumplía con el dimensionamiento declarado.

**Recortes:** nada de lo aprobado se quita sin reabrir su spec. Se pueden dejar para el final, sin frenar los recorridos de compra, las mediciones de k6 (T43) y la alerta (T44 y T45); las dos siguen siendo obligatorias para cerrar las specs.
