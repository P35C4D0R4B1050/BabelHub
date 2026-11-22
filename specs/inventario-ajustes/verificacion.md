---
spec: inventario-ajustes
fecha: 2026-11-21
---

# Verificación — Ajustes de inventario

> La produce la skill `spec-verify` al terminar las tareas. Una spec solo pasa a `Done` si el resultado es **Converge**.

## Resultado: Converge

**Estado final (cierre del 2026-11-21, carril A).** Las dos diferencias de prueba de la pasada inicial (D1 y D2) se cerraron en `0dc7ca1e` y en el commit de `ApiDeAjustesTests`: el historial afirma ahora el orden descendente real (con el reloj de la prueba avanzando), la fecha y los físicos anterior y posterior de cada ajuste (AJU-17); la lista de ingresos afirma cantidad, nota y fecha, y la prueba de la Api compara el elemento de la lista con el DTO del alta, campo a campo (AJU-20). `Criterio~AC-AJU-` en unitarias: 96 de 96. Con ello los 22 requisitos cumplen con prueba sustantiva; queda solo la **salvedad S1** (AJU-22: el texto dice «rechazar como conflicto» y el código espera a la primera y devuelve el original; AC-AJU-18 sí se cumple y está probado): cambiarla exige volver la spec a `Draft`, decisión de la persona responsable (ya en `progress/hallazgos.md`). Los Menores M1 a M8 siguen registrados con dueño y fase; ninguno cambia el resultado. La prueba de la Api (`ApiDeAjustesTests`, Docker) la cubre la próxima corrida completa de `main`.

### Pasada inicial de esta verificación (antes de las correcciones del cierre)

Segunda pasada (la primera es del 2026-11-19). El código de `Ajustes` **no cambió** desde esa pasada (`git diff ca3245c3 HEAD` sin archivos de `Ajustes` en `src/`, `tests/` ni `Shared`), y cada `ruta:línea` de la primera pasada se releyó y sigue vigente; 20 de 22 requisitos cumplen con prueba sustantiva. **AJU-17 y AJU-20 quedan Parcial**: sus pruebas dicen en el nombre que comprueban «fecha y cantidades» o «cantidad, nota y fecha», pero no lo afirman, de modo que no fallarían si se rompiera ese campo (D1, D2). Son unas ocho aserciones en dos pruebas unitarias ya existentes; con ellas en verde, converge. Lo demás (H1, H2 de la primera pasada) quedó cerrado por cambios ajenos (§3).

## 1. Comprobaciones automáticas

| Comprobación | Comando | Resultado |
| :--- | :--- | :--- |
| Compilación sin advertencias | `dotnet build BabelHub.sln -warnaserror` | 0 advertencias, 0 errores (coordinación, sobre el commit actual `6cc6d127`) |
| Pruebas unitarias del servicio | `dotnet test tests/unit/BabelHub.Inventario.UnitTests` | 2376/2376 en verde (coordinación); Ecommerce 5428/5428 |
| Pruebas unitarias de la feature | `dotnet test tests/unit/BabelHub.Inventario.UnitTests --no-build --filter "Criterio=AC-AJU-NN"` | **Ejecutadas por el verificador, todas en verde, 96 pruebas**: AC-AJU-01: 7 · 02: 13 · 03: 8 · 04: 3 · 05: 5 · 06: 5 · 07: 2 · 08: 5 · 09: 7 · 10: 9 · 11: 1 · 12: 1 · 13: 4 · 14: 4 · 15: 13 · 16: 3 · **17: 0 (solo integración)** · 18: 6 |
| Pruebas de integración (I) | `dotnet test tests/integration/BabelHub.IntegrationTests --filter "Criterio~AC-AJU-"` | **No ejecutadas por el verificador (sin Docker).** Cubiertas por la corrida completa de `main` (commit 5bb0a9ac, coordinación, con Docker): 7519 de 7523 en 22 min; las 4 fallas (`HumoDeImagenes` ×2, `CargaDelAcumulado` y el escáner de secretos, por contención de Docker o tiempo) pasan solas y son ajenas a `Ajustes`. Esa corrida incluye `AjustesPostgresTests`, `ApiDeAjustesTests` y `DepuracionDeAjustesTests`, y es posterior al refactor de la unidad de trabajo (`UnidadDeTrabajoPostgres`, 2026-11-20) y a D20 (identificador de fuente derivado del código) |
| Prueba de arquitectura | `dotnet test tests/architecture` | 244/244 (coordinación) |
| Specs, planes, tareas y trazabilidad | `dotnet run --project tools/SpecCheck` | OK, 20 specs, 0 avisos (coordinación); los 18 `AC-AJU` tienen prueba con `[Trait("Criterio", …)]` (AC-AJU-17 solo en integración: `DepuracionDeAjustesTests` ×4 y `AjustesPostgresTests` ×2) |
| Búsqueda de secretos | `git grep -nIiE "password\s*=\|api[_-]?key\s*=\|secret\s*=" -- src` | Ejecutada. **3 coincidencias, ninguna en `Ajustes` ni en el Inventario y ninguna es un secreto escrito**: `Ecommerce.Api/Controllers/CuentaController.cs:178` e `IngresoController.cs:367` (URI `otpauth` armada en memoria con el secreto TOTP del usuario, de `identidad-seguridad`) y `Ecommerce.Infrastructure/Composicion/ComposicionDeLaPlataformaDelEcommerce.cs:106` (`opciones.Password = clave`, lectura de una variable de entorno) |
| Árbol de trabajo | `git status` | Limpio al empezar; el único archivo escrito es este informe. Sin archivos digitales, `.env` ni datos personales |

## 2. Requisito por requisito

Abreviaturas: **U** = `tests/unit/BabelHub.Inventario.UnitTests/Ajustes/` (`AjusteManualTests` = UM, `IngresosReingresosEHistorialTests` = UI, `ReglasDeAjusteTests` = UR); **IP** = `tests/integration/BabelHub.IntegrationTests/Ajustes/AjustesPostgresTests.cs`; **IA** = `…/ApiDeAjustesTests.cs`; **ID** = `…/DepuracionDeAjustesTests.cs`. Las líneas de las pruebas son las de su atributo `Trait`. **(I)** = prueba de integración: la leí y confirmé que falla si se rompe la regla, pero **no la ejecuté**; está cubierta por la corrida completa de `main`.

Código: Aplicación `src/BabelHub.Inventario/BabelHub.Inventario.Application/Ajustes/Interno/` (`RegistrarAjusteManual.cs` = RAM, `RegistrarIngresoDeMercaderia.cs` = RIM, `ReingresosEHistorial.cs` = REH, `AutorizadorDeAjustes.cs` = AUT, `EntradaYPresupuestoDeAjustes.cs`, `DepurarAjustes.cs`); Dominio `…Domain/Ajustes/` (`Ajuste.cs`, `ReglasDeAjuste.cs`); Infraestructura `…Infrastructure/Ajustes/` (`DefinicionDeAjustes.cs` = DEF, `AccesoYRepositoriosDeAjustes.cs` = REP); Api `…Api/Ajustes/EndpointsDeAjustes.cs`.

| Requisito | Evidencia en código | Criterio y prueba | Estado |
| :--- | :--- | :--- | :--- |
| AJU-01 | `Ajuste.cs:33-96` (motivo, edición, fuente, usuario, anterior, posterior, nota); RAM `:195-199`; DEF `:46-67` (tabla y `CHECK`) | AC-AJU-01 · UM `:21` (9 → 7, motivo, nota, usuario, fuente), UR `:36,:143`; IA `:64` (I: DTO con edición, usuario y cantidades) e IP `:221` (I: fila guardada) | Cumple |
| AJU-02 | RAM `:22-27` (solo tres motivos manuales); `ReglasDeAjuste.cs:48` (`EsManual`); `ck_ajustes_motivo` DEF `:59` | AC-AJU-02 · UM `:80` («regalo», `devolucion`, `ingreso-de-mercaderia` y vacío: validación y stock intacto), UR `:14,:28`; IA `:199` (I: 400) | Cumple |
| AJU-03 | `ReglasDeAjuste.cs:110-115`; RAM `:42-51`; `ck_ajustes_resta` DEF `:62` | AC-AJU-03 · UM `:99` (0 y −3: `cantidad-no-positiva`, sin ajuste guardado), `:213` (`int.MaxValue`), UR `:46`; IP `:90` (I: `CHECK` 23514) | Cumple |
| AJU-04 | `ReglasDeAjuste.cs:118-121` (compara con físico − reservado de la fila ya bloqueada); RAM `:227-229` (409 con físico, reservado, vendible) | AC-AJU-04 · UM `:115` (físico 9, reservado 1, daño de 9: rechazo con detalle 9/1/8), UR `:60` (8 se acepta, 9 no); IA `:174` (I: 7 sobre vendible 6 con reserva real) | Cumple |
| AJU-05 | `ReglasDeAjuste.cs:127-146`; `Ajuste.cs:145` (`Corregido`) | AC-AJU-05 · UM `:132` (físico 5, reservado 5, conteo 7: físico 7, vendible 2, diferencia +2), `:184`, UR `:81`; IP `:256` y `:400` (I) | Cumple |
| AJU-06 | `ReglasDeAjuste.cs:140-143` (menor se rechaza, igual se acepta); RAM `:231-234` (pedidos vía `IReservasDeStock.PedidosQueApartan`, real en `NpgsqlRepositorioDeReservas.cs:707`) | AC-AJU-06 · UM `:147` (4 rechazado con los dos pedidos y total, 5 aceptado y vendible 0), UR `:90`; IP `:256` e IA `:174` (I: pedidos reales y `ProblemaDeConteoDto`). El tope de 20 pedidos solo lo fija el código (M2) | Cumple |
| AJU-07 | `ReglasDeAjuste.cs:134-137`; `ck_ajustes_conteo` DEF `:64` | AC-AJU-07 · UM `:170` (`sin-cambios`, nada guardado), UR `:103`; IP `:283` (I) e IA `:174` (I: `sin-cambios`) | Cumple |
| AJU-08 | RAM `:168-183` (`Bloquear` → regla con la fila bloqueada → `Aplicar`); sin reintento por interbloqueo (`ComposicionDeAjustes.cs:30`, D06) | AC-AJU-11 y AC-AJU-05 · UM `:407` (secuencial: la segunda se valida contra el stock actualizado); **IP `:359` (I, solicitudes simultáneas con barrera: dos mermas de 3 sobre vendible 5, una aplicada, físico 2, un solo ajuste, 0 interbloqueos)**, `:400` (I: dos conteos simultáneos, cadena 0 → a → b) | Cumple |
| AJU-09 | AUT `:49-70` (`ExigirFuente`: el encargado solo su fuente; 403 antes que 404) y RAM `:136-139`; segunda capa: política de filas DEF `:108-121` y `DeniegadoPorAlcance` AUT `:73-77`, RAM `:184-187` | AC-AJU-08 · UM `:230,:247,:264` (encargado de Ayacucho en Cusco: `Prohibido` y motivo `fuera-de-sucursal`, fuente inexistente igual, logística, cliente y sin sesión); IA `:117,:137,:350` (I: 7 denegaciones registradas, 401 y alcance antes que forma) e IP `:176` (I: política de filas, escribir en otra sucursal 42501) | Cumple |
| AJU-10 | RAM `:201-203` (misma unidad de trabajo, lanza si la auditoría falla); lista blanca `:65-77` (sin nota ni referencia); la Api traduce `AuditoriaNoDisponibleException` a 503 (`EndpointsDeAjustes.cs:139-151`); transacción por caso de uso `ComposicionDeAjustes.cs:73-79`; claves del detalle verificadas contra el catálogo real (`CatalogoDeAccionesDeAuditoria.Operaciones.cs:26-28`) | AC-AJU-01 · UM `:67` (lanza), `:41` (la nota no va a la auditoría), UI `:39`; **IP `:283` (I: con la auditoría caída el físico sigue en 9 y no queda ningún ajuste)** e IA `:245` (I: centinelas). Ver M5 y M8 | Cumple |
| AJU-11 | REH `:157-190` (`Aplicar(+c)`, ajuste con motivo `devolucion`, devolución y pedido, auditoría por línea); `ck_ajustes_suma` y `ck_ajustes_devolucion` DEF `:63,:65` | AC-AJU-09 · UI `:184` (3 → 4, motivo, auditoría, sin presupuesto), `:319`; IP `:321` e IA `:275` (I: 3 → 4 y DTO de ECO-0) | Cumple |
| AJU-12 | `ReglasDeAjuste.cs:110-115`; `Ajuste.cs:66-67` y `ck_ajustes_resta` DEF `:62`; el endpoint manual no acepta `ingreso-de-mercaderia` | AC-AJU-03 · UM `:99`, UR `:154` (merma 5 → 7 y daño 5 → 5 no se crean); IP `:90` (I: `CHECK` rechaza 5 → 7 y 5 → 5) | Cumple |
| AJU-13 | RIM `:88-173` (`Aplicar(+c)` en la fuente de recepción, fila de `ingresos` y de `ajustes`, una auditoría); referencia opcional `Ajuste.cs:13-18` y `ck_ingresos_referencia` DEF `:86` | AC-AJU-10 · UI `:19` (0 → 10 y 10 → 15, «GR-001», una auditoría cada uno), UR `:236`; IP `:301` (I: cadena y referencia guardada) e IA `:80` | Cumple |
| AJU-14 | AUT `:42-43` (`ExigirAdministracionCentral`, evento de acceso denegado `:96`); RIM `:61-64` (el rol va antes del presupuesto) | AC-AJU-10 · UI `:53` (encargado, logística y cliente: `fuera-de-rol`, sin ingreso ni presupuesto); IP `:301` e IA `:117` (I: 403 y denegación registrada) | Cumple |
| AJU-15 | AUT `:58-61` (administración, cualquier fuente); RAM `:76`/`ConversionDeAjustes.Auditar` (`SucursalOrigen = fuente`); ingreso solo en recepción RIM `:98-110` | AC-AJU-12 · UM `:54` (la auditoría trae Cusco); IP `:241` (I) | Cumple |
| AJU-16 | REH `:116` (bloqueo asesor de la devolución), `:122-138` (misma fuente, cantidad y pedido es repetición; otra cosa, `idempotencia-en-conflicto`), `:97-100` (`23505` como «en curso»); único parcial `ux_ajustes_devolucion_edicion` DEF `:70`; REP `:110-122` | AC-AJU-09 · UI `:184,:210,:229,:250`; **IP `:419` (I, tres reingresos simultáneos: una sola suma y una sola fila `devolucion`)**, `:131` (I: unicidad), IA `:275` (I: repetición `yaAplicado`, conflicto por otro pedido o cantidad) | Cumple |
| AJU-17 | REH `:223-257` (alcance en la Aplicación, edición, motivo, usuario, fecha y cantidades; fecha descendente; sin nota); REP `:124-148` | AC-AJU-13 · UI `:338` (solo Ayacucho; edición, motivo, usuario y nota nula; **no afirma fecha ni cantidades anterior y posterior**), `:378` (topes); IA `:99` (I: no trae Cusco ni la nota; **tampoco afirma fecha ni cantidades**). Las cantidades las protege indirectamente AC-AJU-01 por la misma conversión; **la fecha no la afirma ninguna prueba del módulo** | **Parcial** (D1) |
| AJU-18 | RAM `:156-165` y RIM `:127-136` (inexistente 404, digital 422 `edicion-no-impresa`); REH `:145-154`; fuente inexistente RAM `:141-144` (encargado 403, administración 404) | AC-AJU-14 · UM `:278`, UI `:104,:263`; IA `:299,:322` (I: 422 con el código que declara cada operación) | Cumple |
| AJU-19 | RIM `:98-110` (`fuente-no-es-de-recepcion`) y `:122-125` (1 a 5 000, `ReglasDeIngreso`); `ck_ingresos_cantidad` DEF `:85` | AC-AJU-15 · UI `:71` (Cusco, 0 y 6 000: motivo y stock intacto), UR `:213`; IA `:157` e IP `:113` (I: 422 con su código y `CHECK`) | Cumple |
| AJU-20 | RIM `:216-241` (solo administración central; edición, cantidad, referencia, nota, usuario y fecha; `limite` de 1 a 50); REP `:247-267` (`ORDER BY fecha DESC, id DESC`) | AC-AJU-16 · UI `:146` (tres ingresos; **solo afirma edición, fuente, referencia y usuario no nulo; no afirma cantidad, nota ni fecha**; el encargado es rechazado), `:167` (límite); IA `:80` (I: un ingreso, cantidad en el DTO de alta y 403 del encargado; la lista solo compara el `IngresoId`) | **Parcial** (D2) |
| AJU-21 | DEF `:124-160` (guardián: `current_user`, más de 2 años, columnas intactas, solo anula usuario y vacía nota), `:192-220` (`anonimizar_registros()` en lotes de 5 000), `:241-255` (la aplicación solo `SELECT` e `INSERT`; depuración solo `EXECUTE`); `DepurarAjustes.cs:26-41` (exige actor «sistema»); `ComposicionDeAjustes.cs:90-137` (trabajo diario con registro de la corrida) | AC-AJU-17 · **ID `:46,:64,:119,:144` e IP `:146,:208` (todas I)**: 2 años y 1 día queda sin usuario ni nota con movimiento y cantidades intactos (ajuste e ingreso), el de 1 año no cambia, repetir devuelve 0, el guardián rechaza otro cambio, la aplicación no puede modificar, la cuenta de depuración no lee, el trabajo corre como sistema y registra la corrida y sin actor falla a la vista | Cumple (solo I) |
| AJU-22 | RAM `:146-154` y `:208-221`; RIM `:112-120` y `:176-189` (clave primero con `ON CONFLICT DO NOTHING` en la misma transacción; el alcance se comprueba antes; misma huella: el original; otra: conflicto); REP `:288-315`; PK `(usuario_id, operacion, clave)` DEF `:100`; purga a 30 días DEF `:222-234` | AC-AJU-18 · UM `:335` (original y conflicto), `:357` (otra sucursal rechazada sin el resultado; otro usuario, independiente), `:378` (forma de la clave), UI `:115` (ingreso); **IP `:440` (I, cuatro solicitudes simultáneas con la misma clave: una aplicación, un ajuste)**, `:463` (I: acotada por operación); ID `:104` (I: purga de más de 30 días; repetirla después no se ejecuta, M4). Redacción de «primera en curso»: S1 | Cumple (concurrencia solo I) |

**Totales: 20 Cumple, 2 Parcial, 0 No cumple.**

### Casos límite de la sección 6

| Caso límite | Prueba | Estado |
| :--- | :--- | :--- |
| Dos ajustes simultáneos sobre la misma edición: el segundo se valida contra el stock ya actualizado | IP `:359` (I), UM `:407` | Cubierto |
| Vendible 0: no se puede registrar merma ni daño (todo lo restante está reservado) | `ReglasDeAjuste.cs:118`; **UR `:74` `ConVendibleCeroNoSePuedeRegistrarMerma` (`Restar(5, 5, 1)`)** y UR `:60`; IA `:174` (I). La primera pasada decía que no había caso exacto: había (M3 cerrado). La Aplicación devuelve el detalle `físico/reservado/vendible` (RAM `:227-229`) probado con 9/1/8, no con vendible 0 | Cubierto |
| Conteo mayor al físico con la nota vacía es válido | UM `:132,:184` (conteo 7 y conteo sobre pareja sin fila, sin nota) | Cubierto |

### Lo que el código hace y la spec no pide

Viene de los planes y de otras specs, no es un desvío silencioso:

- Presupuesto del personal de 60 operaciones por hora y usuario para ajuste e ingreso (`PresupuestoDelPersonal`, `EntradaYPresupuestoDeAjustes.cs:104-122`; política `ajustes-del-personal`, que sigue en `PoliticasDeSeguridad.cs:10` y en los motivos de límite del catálogo de auditoría, `CatalogoDeAccionesDeAuditoria.EventosDeSeguridad.cs:85`, cuyo conteo de 45 vigila `EventosDeSeguridadTests.cs:200`). Prueba: UM `:294-330`, IA `:220` (I).
- Tope técnico de 1 000 000 de unidades (`ReglasDeAjuste.TopeTecnico`), referencia de 60 caracteres con juego acotado, nota de 200 sin caracteres de control, reingreso de 1 a 20 líneas y 1 a 5 000 unidades por línea.
- El historial no devuelve la nota (texto libre de otro usuario).
- El reingreso admite repetición parcial y tiene un bloqueo asesor por devolución.
- Purga de las claves de idempotencia a los 30 días (necesaria para AJU-22).
- El motivo `ingreso-de-mercaderia` de AJU-02 no entra por el endpoint manual sino por su propia operación: lectura más estricta, coherente con AJU-14 y AJU-19.

## 3. Diferencias encontradas

### Diferencias reales (impiden converger)

| ID | Diferencia | Causa exacta y mínimo para cerrarla |
| :--- | :--- | :--- |
| D1 | **AJU-17 / AC-AJU-13 (Parcial, prueba).** El historial devuelve fecha y cantidades (`ConversionDeAjustes.ARespuesta`, `RegistrarAjusteManual.cs:62-63`), pero ninguna prueba del historial las afirma y **ninguna prueba del módulo afirma `Instante` (la fecha)**: si el DTO o el repositorio devolvieran otra fecha, nada fallaría. | Añadir a `tests/unit/BabelHub.Inventario.UnitTests/Ajustes/IngresosReingresosEHistorialTests.cs:338` (`ElEncargadoDeAyacuchoVeSoloLosAjustes…`) aserciones sobre `FisicoAnterior`/`FisicoPosterior` de cada elemento (20 → 19 y 19 → 17), sobre `Instante` (igual al reloj de la prueba, `_e.Base.Reloj.Ahora`) y sobre el orden real (hoy el `Order().Reverse()` ordena el resultado antes de comparar y no prueba el orden descendente que declara el nombre). Solo prueba; no toca código. |
| D2 | **AJU-20 / AC-AJU-16 (Parcial, prueba).** La prueba nombrada «…con edición, cantidad, referencia, nota, usuario y fecha» solo afirma edición, fuente, referencias y usuario no nulo. Cantidad, nota y fecha de la lista no se comprueban (ni en U ni en IA `:80`, que compara solo el `IngresoId`). | Añadir a `IngresosReingresosEHistorialTests.cs:146` aserciones de `Cantidad` (1, 2, 3), `Nota` («n1»…«n3») e `Instante` (reloj de la prueba) de cada `IngresoListado`; y, mejor, en `ApiDeAjustesTests.cs:80` comparar cantidad, referencia, nota y fecha del elemento de la lista con el DTO de alta. Solo prueba. |

### Salvedad de redacción (no es diferencia de código)

| ID | Salvedad | Motivo y dueño |
| :--- | :--- | :--- |
| S1 | **Cerrada (2026-11-21, DD-40 a DD-42):** el texto de AJU-22, AC-AJU-18 y §6 describe ahora lo que el código hace (la repetición con la primera en curso espera y recibe el original o el conflicto; 503 uniforme si vence el bloqueo; alcance antes de la clave, sin oráculo). La spec volvió a Draft y se reaprobó con la puerta (ronda 2 limpia: 0 Bloqueantes y 0 Importantes). | Carril de cierre | Cerrada |

### Hallazgos de la primera pasada que se cierran

| Antes | Estado ahora | Evidencia |
| :--- | :--- | :--- |
| H1 (Importante): el anfitrión del Inventario no componía `Ajustes` | **Cerrado** (COMP-1) | `Inventario.Api/Program.cs:24-26` (`AgregarInventario`) y `Plataforma/AnfitrionDelInventario.cs:219` (matriz de alcances con `MatrizDeAjustes`), `:243` (`AgregarModuloAjustes`), `:253` (`AgregarTrabajosDeAjustes`, solo en modo normal), `:273` (`MapearAjustes`); `progress/hallazgos.md:312` |
| H2 (Menor): Devoluciones debía enviar `pedidoId` | **Cerrado** | `Ecommerce.Infrastructure/Devoluciones/RestInventarioDeDevoluciones.cs:67-72` y `Plataforma/HttpClienteDelInventario.cs:462-468` (`ReingresarDevolucionDto(pedidoId, …)`, falla si falta); `ReingresarDevolucionDto` en `Shared/Contratos/Inventario/AjustesYTrasladosDto.cs:73-76`. La coordinación puede cerrar `progress/hallazgos.md:252` |
| M3: sin caso con vendible 0 | **Cerrado** (había prueba) | UR `:74` |

### Menores abiertos (con dueño y fase; ninguno cambia el resultado)

| ID | Hallazgo | Dueño | Fase |
| :--- | :--- | :--- | :--- |
| H3 | `GET /ajustes` (`inventario.ajustes.historial`) no está en `OperacionesDelInventario`/ECO-0; se declara en `MatrizDeAjustes` (`EndpointsDeAjustes.cs:107-117`) y consta en `progress/hallazgos.md:381` (OAS-1) | Carril A / dueño de Ajustes y Traslados | Antes de FIN-1 |
| M1 | AJU-20: ninguna prueba contra PostgreSQL comprueba el orden descendente ni el límite de 1 a 50 de `Ultimos` (REP `:247-267`); solo el repositorio en memoria (se resuelve junto con D2 en integración) | Carril de Ajustes | FIN-1 |
| M2 | AJU-06: no hay prueba de que el rechazo pida a lo sumo 20 pedidos (`MaximoDePedidosEnElRechazo`) ni del total mayor que 20 | Carril de Ajustes | FIN-1 |
| M4 | AJU-22: «pasados 30 días, repetirla es una operación nueva» se prueba por partes (la purga, ID `:104`; la clave nueva es un `INSERT`), no con una repetición tras la purga; la repetición y el conflicto del ingreso y la rama «sin alcance» solo con repositorios simulados (U); la purga corre una vez al día, de modo que los 30 días son aproximados | Carril de Ajustes | FIN-1 |
| M5 | Etiquetas `Trait` que no corresponden: IP `:110` (atributo huérfano que deja a `LasRestriccionesDelIngresoYDeLaIdempotencia` con AC-AJU-03 y AC-AJU-15), IP `:283` (la prueba de auditoría caída, que prueba AJU-10, va con AC-AJU-07; debería llevar también AC-AJU-01) e IA `:220` (presupuesto de 60 por hora, con AC-AJU-08) | Carril de Ajustes | FIN-1 |
| M6 | Del cierre de INV-3a: huella con la nota sin sal (valorar HMAC); un encargado puede obtener 409 con el `devolucionId` de otra sucursal usando su fuente; la nota admite controles Unicode de formato; el cuerpo máximo de 4 KB responde 400 y no se prueba con 413 | Carril de Ajustes / `transversal-seguridad` | Revisión de FIN-1 |
| M7 | Documentación: el plan §4 (supuestos 1 y 2) y §2/T03 aún describen un `pedidoId` ausente y la fachada `IAjustesDeStock`, que no existe (el contrato público son las entradas y salidas de los casos de uso) | Coordinación | Cierre de fase |
| M8 | AJU-10: la prueba de «si no se puede auditar, no se aplica» existe para el ajuste manual (IP `:283`, I) pero no para el ingreso ni el reingreso, y la integración usa un espía de auditoría (`AuditoriaDeIntegracionEspia`), no el módulo real: la coincidencia de las claves de detalle que emite `Ajustes` con la lista blanca real se comprobó por lectura (`CatalogoDeAccionesDeAuditoria.Operaciones.cs:26-28`), sin prueba que la ate | Carril de Ajustes / `transversal-auditoria` | FIN-1 |

## 4. Reglas transversales

| Regla (AGENTS.md) | Verificación | Estado |
| :--- | :--- | :--- |
| Capas y referencias (§3.1 a 3.6, 3.13) | Prueba de arquitectura 244/244 (coordinación). El Dominio de `Ajustes` no referencia `Fuentes`; el único puerto hacia otro módulo es `IReingresosPorDevolucion` de `Fuentes` y usa su raíz | Cumple |
| Alcance por sucursal en la Aplicación (§5) | `AutorizadorDeAjustes` (rol y fuente) en cada caso de uso antes de tocar datos y antes de buscar la clave (AUT `:34-70`); política de filas como segunda capa (DEF `:108-121`, IP `:176`); el rechazo registra el acceso denegado fuera de la transacción | Cumple |
| Auditoría en la misma transacción (§4, AJU-10) | `IAuditoria.Registrar` dentro del caso de uso envuelto por `TransaccionPorCasoDeUso` (`ComposicionDeAjustes.cs:73-79`); si falla lanza y revierte (IP `:283`, I); ver M8 | Cumple (I) |
| Idempotencia (AJU-16, AJU-22) | Clave primero con `ON CONFLICT DO NOTHING`; unicidad `(devolución, edición)` como última defensa; ver S1 | Cumple |
| Concurrencia con solicitudes simultáneas (§4) | IP `:359,:381,:400,:419,:440` (merma, ingreso, conteo, reingreso y misma clave), todas (I) | Cumple (I) |
| Errores `application/problem+json` con `code` estable y `traceId` | `ErroresDeAjustes.Falla`; IA `:322` (cada fallo trae un código de los que declara su operación) y `:174` (I) | Cumple (I) |
| Secretos y datos personales (§5) | La nota y la referencia no van a la auditoría, a los registros ni a los errores (centinela IA `:245`, I; `ToString` sin texto libre, UR `:262`); `PostgresException.Detail` no se propaga (REP `:100`); sin secretos en `Ajustes` (§1) | Cumple |
| Pantallas | Esta spec no tiene pantallas propias | No aplica |
| Cambios recientes ajenos que la tocan | Reservas (`CerrarReservas`, topes de 20 unidades) no cambia `PedidosQueApartan`; el refactor de la unidad de trabajo (`UnidadDeTrabajoPostgres.cs`, en una sola ida por transacción) conserva el alcance y el rol que usa `AccesoAAjustes`; el catálogo de auditoría (45 motivos de límite excedido) y el de políticas conservan `ajustes-del-personal`; D20 deriva el id de la fuente del código, que es lo que compara `EsEncargadoDe`. Todo verificado por lectura y por la corrida completa de `main` | Cumple |

## 5. Conclusión

Se confirma lo esencial de la primera pasada (22 requisitos con evidencia vigente, 20 con prueba sustantiva, código de `Ajustes` sin cambios, H1 y H2 ya cerrados por COMP-1 y por Devoluciones), pero se refuta que la spec esté lista para `Done`: AJU-17 y AJU-20 solo tienen pruebas que no afirman fecha, cantidades ni nota (D1, D2). Con esas aserciones añadidas en las dos pruebas unitarias y el filtro `Criterio~AC-AJU-` en verde, el resultado pasa a Converge, con la salvedad de redacción S1 (dueño: persona responsable) y los menores registrados.

## Actualización final del 2026-11-21 (carril de cierre): S1 cerrada, **Converge**

- **AJU-22 (S1) cerrada:** spec y plan reaprobados el 2026-11-21 (`progress/aprobaciones.md`, huellas `4b617c7fea9f9b2f` y `abd8889ce97ea35c`; DD-40 a DD-42, pendientes de ratificar). El código no cambió. Se añadieron dos pruebas de AC-AJU-18: `AjusteManualTests.LaRepeticionConLaPrimeraEnCursoSiSeAgotaLaEsperaResponde503SinAplicarNada` (unitaria, doble `EsperaAgotada` de `RepositorioDeOperacionesEnMemoria`) y `AjustesPostgresTests.LaMismaClaveEnParaleloConCargasDistintas_SeAplicaUnaSolaYLasDemasRecibenElOriginalOElConflicto` (PostgreSQL, cuatro solicitudes simultáneas con dos cargas). El 503 por plazo vencido solo tiene prueba unitaria (Menor, `progress/hallazgos.md`).
- **Comprobaciones de esta pasada:** `dotnet build BabelHub.sln -warnaserror` sin advertencias; `Criterio~AC-AJU-` 97 de 97 (unitarias); unitarias del Inventario 2387 verdes; arquitectura 259 verdes; integración de Reservas, Ajustes, Fuentes, Traslados y Existencias 627 de 627 (PostgreSQL real); `SpecCheck` OK, 20 specs, 0 avisos.
- Quedan solo los Menores M1 a M8 y los de la reaprobación, ya registrados con dueño y fase: no impiden `Done`. **Resultado: Converge; la spec pasa a `Done`.**
