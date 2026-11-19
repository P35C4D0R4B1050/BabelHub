---
spec: inventario-ajustes
fecha: 2026-11-19
---

# Verificación — Ajustes de inventario

> La produce la skill `spec-verify` al terminar las tareas. Una spec solo pasa a `Done` si el resultado es **Converge**.

## Resultado: Converge

Converge a nivel de código y pruebas unitarias y de arquitectura. Quedan **dos condiciones que el coordinador debe cerrar antes de marcar `Done`** (no son huecos de la spec): (1) las pruebas de integración y la concurrencia con PostgreSQL **no se ejecutaron en esta sesión** (se corren sobre `main`; si alguna falla, el resultado se reevalúa); (2) el hallazgo heredado de composición del anfitrión (§3, H1).

## 1. Comprobaciones automáticas

| Comprobación | Comando | Resultado |
| :--- | :--- | :--- |
| Compilación sin advertencias | `dotnet build BabelHub.sln -warnaserror` | 0 advertencias, 0 errores (comprobado por el coordinador en esta sesión) |
| Pruebas unitarias de la feature | `dotnet test tests/unit/BabelHub.Inventario.UnitTests --filter "Criterio~AC-AJU-"` | En verde: 96 pruebas (coordinador) |
| Pruebas de integración | `dotnet test tests/integration/BabelHub.IntegrationTests --filter "Criterio~AC-AJU-"` | **No ejecutadas en esta sesión** (PostgreSQL con Testcontainers; las corre el coordinador sobre `main`) |
| Prueba de arquitectura | `dotnet test tests/architecture --no-build` | En verde: 217 superadas, 0 con error (ejecutada por el verificador) |
| Specs, planes, tareas y trazabilidad | `dotnet run --project tools/SpecCheck` | 0 errores y 0 avisos (coordinador); los 18 `AC-AJU` tienen al menos una prueba con `[Trait("Criterio", …)]` |
| Búsqueda de secretos | `git grep -nIiE "password\s*=|api[_-]?key\s*=|secret\s*=" -- src` | Sin coincidencias (coordinador) |
| Árbol de trabajo | `git status` | Limpio al empezar; sin archivos digitales, `.env` ni datos personales (coordinador) |

## 2. Requisito por requisito

Abreviaturas de las pruebas: **U** = `tests/unit/BabelHub.Inventario.UnitTests/Ajustes/` (`AjusteManualTests`, `IngresosReingresosEHistorialTests`, `ReglasDeAjusteTests`); **IP** = `tests/integration/BabelHub.IntegrationTests/Ajustes/AjustesPostgresTests.cs`; **IA** = `…/ApiDeAjustesTests.cs`; **ID** = `…/DepuracionDeAjustesTests.cs`. «Int.» marca lo que solo se prueba (o solo se prueba contra la base real) en integración y por tanto **no se ejecutó en esta sesión**.

Notas de lectura del código: la Aplicación está en `src/BabelHub.Inventario/BabelHub.Inventario.Application/Ajustes/Interno/` (`RegistrarAjusteManual.cs`, `RegistrarIngresoDeMercaderia.cs`, `ReingresosEHistorial.cs`, `AutorizadorDeAjustes.cs`, `EntradaYPresupuestoDeAjustes.cs`, `DepurarAjustes.cs`); el Dominio, en `…Domain/Ajustes/`; la Infraestructura, en `…Infrastructure/Ajustes/`; la Api, en `…Api/Ajustes/EndpointsDeAjustes.cs`.

| Requisito | Evidencia en código | Criterio y prueba | Estado |
| :--- | :--- | :--- | :--- |
| AJU-01 | `Domain/Ajustes/Ajuste.cs:33-96` (motivo, edición, fuente, usuario, anterior y posterior, nota); `RegistrarAjusteManual.cs:195-199` (usuario y fecha); `DefinicionDeAjustes.cs:46-67` (tabla y `CHECK`) | AC-AJU-01 · U `AjusteManualTests.cs:21` (9 → 7, nota, usuario, fuente); IP `:221` e IA `:64` (Int.: 9 → 7 con la base real, fila guardada y DTO de ECO-0) | Cumple |
| AJU-02 | `RegistrarAjusteManual.cs:22-27` (solo tres motivos manuales; `devolucion` e `ingreso-de-mercaderia` tienen su propia operación, plan §1); `ReglasDeAjuste.cs:48` (`EsManual`); `CHECK ck_ajustes_motivo` `DefinicionDeAjustes.cs:59` | AC-AJU-02 · U `AjusteManualTests.cs:80` («regalo», `devolucion`, `ingreso-de-mercaderia` y vacío rechazados, stock intacto y sin presupuesto consumido); IA `:199` (Int.: 400 con la base real) | Cumple |
| AJU-03 | `ReglasDeAjuste.cs:110-115` (cantidad `<= 0` rechazada); `RegistrarAjusteManual.cs:42-51` (forma: cantidad obligatoria en merma y daño); `ck_ajustes_resta` `DefinicionDeAjustes.cs:62` | AC-AJU-03 · U `AjusteManualTests.cs:99` (0 y −3: rechazo `cantidad-no-positiva`, sin ajuste guardado), `ReglasDeAjusteTests.cs:46`; IP `:89` (Int.: `CHECK` de la tabla) | Cumple |
| AJU-04 | `ReglasDeAjuste.cs:118-121` (compara con `físico − reservado` de la fila ya bloqueada); `RegistrarAjusteManual.cs:227-229` (409 con físico, reservado y vendible) | AC-AJU-04 · U `AjusteManualTests.cs:115` (físico 9, reservado 1, daño de 9: rechazo y detalle 9/1/8), `ReglasDeAjusteTests.cs:60,74`; IA `:174` (Int.: 7 sobre vendible 6) | Cumple |
| AJU-05 | `ReglasDeAjuste.cs:127-146` (el físico pasa a ser el conteo; diferencia = posterior − anterior); `Ajuste.cs:145` (`Corregido`) | AC-AJU-05 · U `AjusteManualTests.cs:132` (físico 5, reservado 5, conteo 7: físico 7, vendible 2, diferencia +2), `:184`; IP `:256` y `:400` (Int.) | Cumple |
| AJU-06 | `ReglasDeAjuste.cs:140-143` (conteo `<` reservado; igual se acepta); `RegistrarAjusteManual.cs:231-234` (primeros 20 pedidos y total vía `IReservasDeStock.PedidosQueApartan`) | AC-AJU-06 · U `AjusteManualTests.cs:147` (4 rechazado con los dos pedidos, 5 aceptado), `ReglasDeAjusteTests.cs:91,113`; IP `:256` e IA `:174` (Int.: pedidos reales, `ProblemaDeConteoDto`). **Sin prueba del tope de 20 pedidos** (ver M2) | Cumple |
| AJU-07 | `ReglasDeAjuste.cs:134-137`; `ck_ajustes_conteo` `DefinicionDeAjustes.cs:64` | AC-AJU-07 · U `AjusteManualTests.cs:170` (`sin-cambios`, sin ajuste guardado); IP `:283` (Int.) | Cumple |
| AJU-08 | `RegistrarAjusteManual.cs:168-183` (`Bloquear` → regla con la fila bloqueada → `Aplicar` condicional); no reintenta por interbloqueo (decisión T04) | AC-AJU-05 y AC-AJU-11 · U `AjusteManualTests.cs:407` (secuencial); **IP `:359` (Int., concurrencia real con barrera: dos mermas de 3 sobre vendible 5, una aplicada, vendible 2, un solo ajuste y 0 interbloqueos)**, `:400` (dos conteos simultáneos sobre pareja sin fila: cadena 0 → a → b) | Cumple (concurrencia solo en Int.) |
| AJU-09 | `AutorizadorDeAjustes.cs:49-70` (`ExigirFuente`: el encargado solo su fuente; el 403 antes que el 404) y `RegistrarAjusteManual.cs:136-139`; segunda capa: política de filas `DefinicionDeAjustes.cs:108-121` y `ResultadoDeMovimiento.DeniegadoPorAlcance` `:184-187` | AC-AJU-08 · U `AjusteManualTests.cs:230,247,264`; IA `:117,:137,:350` y IP `:176` (Int.: encargado ajeno, logística, cliente, sin credencial y política de filas, con el evento de acceso denegado en cada endpoint) | Cumple |
| AJU-10 | `RegistrarAjusteManual.cs:201-203` (misma transacción; si `IAuditoria` falla lanza y la transacción revierte); lista blanca `:65-77` (sin nota ni referencia); la Api traduce `AuditoriaNoDisponibleException` a 503 `EndpointsDeAjustes.cs:139-149` | AC-AJU-01 y AC-AJU-10 · U `AjusteManualTests.cs:67` (lanza si falla), `:42` (la nota no va a la auditoría); IP `:283` (Int.: con auditoría caída el stock sigue en 9 y no queda ningún ajuste) y IA `:244` (centinela fuera de auditoría y errores) | Cumple |
| AJU-11 | `ReingresosEHistorial.cs:143-190` (`Aplicar(+c)`, ajuste con motivo `devolucion`, devolución y pedido, auditoría `ReingresoPorDevolucion` por línea); `ck_ajustes_suma` y `ck_ajustes_devolucion` `DefinicionDeAjustes.cs:63,65` | AC-AJU-09 · U `IngresosReingresosEHistorialTests.cs:184` (3 → 4, motivo, auditoría, sin presupuesto), `:319` (unidades por pedido); IP `:321` e IA `:274` (Int.: física 3 → 4 y DTO de ECO-0; suma lo devuelto en todas las sucursales) | Cumple |
| AJU-12 | `ReglasDeAjuste.cs:110-115` (merma y daño solo restan: la cantidad no positiva se rechaza); `Ajuste.cs:66-67` y `ck_ajustes_resta` (la tabla rechaza `posterior >= anterior`); el endpoint manual no acepta `ingreso-de-mercaderia` | AC-AJU-03 · U `AjusteManualTests.cs:99`, `ReglasDeAjusteTests.cs:154`; IP `:89` (Int.: `CHECK` con `merma` 5 → 7 y 5 → 5 rechazados, `23514`) | Cumple |
| AJU-13 | `RegistrarIngresoDeMercaderia.cs:88-173` (`Aplicar(+c)` en la fuente de recepción, fila de `ingresos` y de `ajustes` en una transacción, una sola auditoría `IngresoDeMercaderiaRegistrado`); referencia opcional: `Domain/Ajustes/Ajuste.cs:13-18` y `ck_ingresos_referencia` `DefinicionDeAjustes.cs:86` | AC-AJU-10 · U `IngresosReingresosEHistorialTests.cs:19` (0 → 10 y 10 → 15, referencia «GR-001», una auditoría cada uno); IP `:301` (Int.: cadena y referencia guardada) e IA `:80` | Cumple |
| AJU-14 | `AutorizadorDeAjustes.cs:42-43` (`ExigirAdministracionCentral`, con evento de acceso denegado `:96`); `RegistrarIngresoDeMercaderia.cs:61-64` (el rol va antes del presupuesto) | AC-AJU-10 · U `IngresosReingresosEHistorialTests.cs:52` (encargado, logística y cliente: `fuera-de-rol`, sin ingreso ni presupuesto); IP `:301` e IA `:117` (Int.: 403 y denegación registrada) | Cumple |
| AJU-15 | `AutorizadorDeAjustes.cs:58-61` (administración cualquier fuente); `RegistrarAjusteManual.cs:76` y `ConversionDeAjustes.Auditar` (`SucursalOrigen = fuente`) | AC-AJU-12 · U `AjusteManualTests.cs:54` (la auditoría trae Cusco); IP `:241` (Int.) | Cumple |
| AJU-16 | `ReingresosEHistorial.cs:116` (bloqueo asesor de la devolución) y `:119-138` (misma fuente, cantidad y pedido: repetición; otra: `idempotencia-en-conflicto`); `:97-100` (`23505` como «en curso», transacción revertida); único parcial `ux_ajustes_devolucion_edicion` `DefinicionDeAjustes.cs:70`; `PorDevolucionYEdicion` lee con alcance de red `AccesoYRepositoriosDeAjustes.cs:110-122` | AC-AJU-09 · U `IngresosReingresosEHistorialTests.cs:184,210,229,250`; IP `:419` (Int., **solicitudes simultáneas reales**: tres reingresos paralelos, una sola suma y una sola fila `devolucion`), `:321` e IA `:274` (Int.: repetición `yaAplicado`, conflicto por otro pedido, otra fuente u otra cantidad) y IP `:131` (unicidad en la base) | Cumple |
| AJU-17 | `ReingresosEHistorial.cs:223-257` (alcance en la Aplicación, edición, motivo, usuario, fecha y cantidades; orden fecha descendente e id; sin la nota); `AccesoYRepositoriosDeAjustes.cs:124-148` | AC-AJU-13 · U `IngresosReingresosEHistorialTests.cs:338,378` (solo Ayacucho; tope de página); IA `:98` (Int.: no trae Cusco ni la nota) | Cumple |
| AJU-18 | `RegistrarAjusteManual.cs:156-165` y `RegistrarIngresoDeMercaderia.cs:127-136` (edición inexistente: 404; digital: 422 `edicion-no-impresa`); `ReingresosEHistorial.cs:145-154`; fuente inexistente: `RegistrarAjusteManual.cs:141-144` (un encargado recibe 403 y la administración 404) | AC-AJU-14 · U `AjusteManualTests.cs:278`, `IngresosReingresosEHistorialTests.cs:104,263`; IA `:298` y `:322` (Int.: 422 con el código que declara cada operación) | Cumple |
| AJU-19 | `RegistrarIngresoDeMercaderia.cs:98-110` (fuente distinta de la de recepción: `fuente-no-es-de-recepcion`) y `:122-125` (cantidad 1 a 5 000 como regla de negocio, `ReglasDeIngreso`); `ck_ingresos_cantidad` `DefinicionDeAjustes.cs:85` | AC-AJU-15 · U `IngresosReingresosEHistorialTests.cs:71,90` (Cusco, 0 y 6 000, stock intacto); IA `:156` e IP `:113` (Int.: 422 con su código y `CHECK`) | Cumple |
| AJU-20 | `RegistrarIngresoDeMercaderia.cs:216-241` (solo administración central; edición, cantidad, referencia, nota, usuario y fecha; `limite` de 1 a 50); `AccesoYRepositoriosDeAjustes.cs:247-267` (`ORDER BY fecha DESC, id DESC`) | AC-AJU-16 · U `IngresosReingresosEHistorialTests.cs:146` (tres ingresos con sus campos; el encargado, rechazado), `:166`; IA `:80` (Int.: un ingreso real y el 403 del encargado). Sin prueba del orden ni del límite contra la base (ver M1) | Cumple |
| AJU-21 | `DefinicionDeAjustes.cs:124-160` (disparador guardián que valida `current_user`, más de 2 años, columnas intactas y solo anula `usuario_id` y vacía `nota`), `:192-220` (`anonimizar_registros()` en lotes de 5 000), `:241-255` (el rol de la aplicación solo `SELECT` e `INSERT`; la depuración solo `EXECUTE`); `DepurarAjustes.cs:26-41` (exige actor «sistema»); `ComposicionDeAjustes.cs:91-` (trabajo diario con registro de la corrida) | AC-AJU-17 · **ID `:46,:64,:119,:144` e IP `:146,:208` (todas Int.)**: a los 2 años y 1 día queda sin usuario ni nota con el movimiento y las cantidades intactos, el de 1 año no cambia, repetir no cambia nada, el guardián rechaza otro cambio, la cuenta de la aplicación no puede modificar esas columnas, y el trabajo corre como sistema y registra la corrida | Cumple (solo Int.) |
| AJU-22 | `RegistrarAjusteManual.cs:146-154` y `:209-221`; `RegistrarIngresoDeMercaderia.cs:112-120` y `:176-189` (la clave se guarda primero con `ON CONFLICT DO NOTHING` en la misma transacción; el alcance se comprueba antes; misma huella: el original; otra: conflicto); `AccesoYRepositoriosDeAjustes.cs:288-315`; PK `(usuario_id, operacion, clave)` `DefinicionDeAjustes.cs:100`; purga a 30 días `:222-234` | AC-AJU-18 · U `AjusteManualTests.cs:335` (original y conflicto), `:357` (otra sucursal rechazada sin el resultado; otro usuario con alcance, independiente), `IngresosReingresosEHistorialTests.cs:115` (ingreso); **IP `:440` (Int., cuatro solicitudes simultáneas con la misma clave: una aplicación, un solo ajuste), `:463` (Int.: la clave se acota por operación)**; ID `:104` (Int.: purga de más de 30 días y repetir no cambia nada) | Cumple (concurrencia solo en Int.) |

**Totales: 22 Cumple, 0 Parcial, 0 No cumple.**

Cobertura de los criterios en integración (para el coordinador, **no ejecutada**): AC-AJU-01, 05, 06, 07, 08, 09, 10, 11, 12, 13, 15, 17 y 18 tienen pruebas en `tests/integration`; **AC-AJU-11 y la parte de concurrencia de AC-AJU-09, AC-AJU-10 y AC-AJU-18, y todo AC-AJU-17, solo se prueban allí**. AC-AJU-02, 03, 04, 14 y 16 tienen su prueba principal en unitarias y apoyo en integración (AC-AJU-16 solo unitaria con el trait). Para el resultado final deben pasar `AjustesPostgresTests`, `ApiDeAjustesTests` y `DepuracionDeAjustesTests`.

### Casos límite de la sección 6

| Caso límite | Prueba | Estado |
| :--- | :--- | :--- |
| Dos ajustes simultáneos sobre la misma edición: el segundo se valida contra el stock ya actualizado | IP `:359` (Int.), U `AjusteManualTests.cs:407` | Cubierto |
| Vendible 0: no se puede registrar merma ni daño (todo lo restante está reservado) | `ReglasDeAjuste.cs:118` (`cantidad > Math.Max(0, físico − reservado)`) y `ReglasDeAjusteTests.cs:60,74`; IA `:174` (vendible 6). No hay un caso con vendible exactamente 0 y reservado = físico en Aplicación ni en integración | Cubierto por la regla; ver M3 |
| Conteo mayor al físico con la nota vacía es válido | U `AjusteManualTests.cs:132,184` (conteo 7 y conteo sobre pareja sin fila, sin nota) | Cubierto |

### Pregunta puntual: `pedidoId` en `ReingresarDevolucionDto`

El plan (§4, supuesto 1) lo daba como faltante y prerrequisito de T08. **El código ya lo cumple:** `ReingresarDevolucionDto` lleva `pedidoId` como primer campo (`src/BabelHub.Shared/Contratos/Inventario/AjustesYTrasladosDto.cs:73-76`, enmienda D19 autorizada por el coordinador el 2026-11-17, `decisiones-de-implementacion.md` T14); la validación lo exige (`ReingresosEHistorial.cs:19-23`, 400 si falta), la tabla lo exige con el motivo devolución (`ck_ajustes_devolucion`) y cambiarlo en la repetición es conflicto (`:132`). Pruebas: U `IngresosReingresosEHistorialTests.cs:210,295`; IA `:274,:298` (Int.: sin pedido = 400, otro pedido = 409). No es un hallazgo del Inventario. Queda como dependencia de **otra** feature (H2).

## 3. Diferencias encontradas

No hay diferencias que impidan la convergencia de la spec. Lo que sigue son diferencias menores, hallazgos heredados y funcionalidad de más. Ningún hallazgo es Bloqueante.

**Hallazgos abiertos (con dueño y fase)**

| ID | Hallazgo | Gravedad | Dueño | Fase |
| :--- | :--- | :--- | :--- | :--- |
| H1 | **Heredado, ya registrado** (`progress/hallazgos.md`, entrada de `inventario-traslados` del 2026-11-18): `Inventario.Api/Program.cs` solo mapea la salud; ningún módulo (incluido `Ajustes`: `AgregarModuloAjustes`, `AgregarTrabajosDeAjustes`, `MapearAjustes` y `MatrizDeAjustes` en la matriz de alcances) está compuesto en el anfitrión de producción. Fuera de las pruebas (`ApiDeReservasHost`) no hay endpoints de ajuste ni corre la depuración de 2 años. Lo anota `decisiones-de-implementacion.md` (T13). | Importante (transversal; no afecta a la prueba de la feature) | Coordinador / bloque de composición del anfitrión | FIN-1, antes de los E2E y de dar el sistema por terminado |
| H2 | El módulo de Devoluciones del E-commerce debe enviar `pedidoId` en el reingreso (`decisiones-de-implementacion.md` T14). Hoy ningún código del E-commerce consume `ReingresarDevolucionDto`. | Menor (dependencia) | `ecommerce-devoluciones` | Su plan y sus tareas |
| H3 | El historial `GET /ajustes` (`inventario.ajustes.historial`) no está en el catálogo de `Shared`/ECO-0; se declara en `MatrizDeAjustes` (`EndpointsDeAjustes.cs:107-120`) como supuesto de contrato y responde `PaginaDto<AjusteRegistradoDto>`. | Menor | Coordinador (ECO-0, `docs/03/08`) | Antes de que la interfaz web de «Historial de ajustes» lo consuma |
| M1 | AJU-20 y AC-AJU-16: ninguna prueba contra PostgreSQL comprueba el orden descendente ni el límite de 1 a 50 de `Ultimos`, ni que el DTO traiga edición, cantidad, nota, usuario y fecha leídos de la base (la integración de `IA :80` solo compara el `IngresoId` de un ingreso); las unidades lo prueban con el repositorio en memoria. | Menor | Carril de Ajustes (Inventario) | FIN-1 (una prueba de integración con tres ingresos) |
| M2 | AJU-06: no hay prueba de que el rechazo por conteo pida como mucho 20 pedidos y devuelva el total (lo había anotado la revisión de INV-3a para INV-3b). | Menor | Carril de Ajustes (Inventario) | FIN-1 |
| M3 | Sección 6, vendible 0: no hay un caso explícito con físico = reservado (vendible 0) para merma y daño. | Menor | Carril de Ajustes (Inventario) | FIN-1 |
| M4 | AJU-22: la repetición y el conflicto de un **ingreso** y la rama «sin alcance» solo se prueban con repositorios simulados (U); en integración hay concurrencia solo para el ajuste y para la independencia por operación. El código del ingreso comparte la misma pieza (`NpgsqlRepositorioDeOperacionesIdempotentes`) y su bloqueo asesor de recepción serializa. | Menor | Carril de Ajustes (Inventario) | FIN-1 |
| M5 | Etiquetas `Trait` que no corresponden al criterio que ejercitan, en pruebas que sí cubren otra cosa: `AjustesPostgresTests.cs:110` (un `Trait` huérfano antes de `[Fact]`, que deja a `LasRestriccionesDelIngresoYDeLaIdempotencia` con AC-AJU-03 y AC-AJU-15), `:283` (auditoría caída, etiquetada AC-AJU-07; es de AC-AJU-01 y AC-AJU-10) y `ApiDeAjustesTests.cs:220` (presupuesto de 60 por hora, etiquetado AC-AJU-08). No cambian el resultado, pero confunden la trazabilidad. | Menor | Carril de Ajustes (Inventario) | FIN-1 |
| M6 | Del cierre de INV-3a (ya anotados en `decisiones-de-implementacion.md`; sin nuevos): la huella de la carga incluye la nota sin sal (valorar HMAC); un encargado puede obtener 409 con el `devolucionId` de otra sucursal usando su fuente; la nota admite controles Unicode de formato; el cuerpo máximo de 4 KB no se prueba con 413 (la Api responde 400). | Menor | Carril de Ajustes / `transversal-seguridad` | Revisión de FIN-1 |
| M7 | Documentación: el plan §4 (supuestos 1 y 2) y §2/T03 siguen describiendo un `pedidoId` ausente y la fachada `IAjustesDeStock`, que no se creó (el contrato público son las entradas y salidas de los casos de uso, como en `Fuentes` y `Reservas`; `ajustes-al-implementar` y `decisiones-de-implementacion.md` lo aclaran). | Menor | Coordinador | Al cierre de la fase |

**Funcionalidad de más** (el código la hace y la spec no la pide; viene de los planes y de otras specs, no es un desvío silencioso):

- Presupuesto del personal de 60 operaciones por hora y usuario para el ajuste y el ingreso (`PresupuestoDelPersonal`, `EntradaYPresupuestoDeAjustes.cs:104-122`): viene de `transversal-seguridad` SEG-30 y del plan §6.
- Tope técnico de 1 000 000 de unidades (`ReglasDeAjuste.TopeTecnico`), referencia con juego de caracteres y largo de 60, nota de 200 con rechazo de caracteres de control, y reingreso con máximo de 20 líneas y 1 a 5 000 unidades por línea (plan §3 y §5, D08).
- El historial no devuelve la nota (decisión de INV-3a: texto libre de otro usuario).
- El reingreso admite repetición parcial (aplica las líneas nuevas) y tiene un bloqueo asesor por devolución.
- Una operación de purga de claves de idempotencia a los 30 días (necesaria para AJU-22).
- El motivo `ingreso-de-mercaderia` de AJU-02 no entra por el endpoint de ajuste manual sino por su propia operación (plan §1): es una lectura literal más estricta que la de la spec, coherente con AJU-14 y AJU-19; no se corrige.

## 4. Reglas transversales

| Regla (AGENTS.md) | Verificación | Estado |
| :--- | :--- | :--- |
| Capas y referencias (§3.1 a 3.6, 3.13) | Prueba de arquitectura en verde (217 superadas). El Dominio de `Ajustes` no referencia `Fuentes`: las reglas reciben físico y reservado como enteros; el único puerto de salida hacia otro módulo es `IReingresosPorDevolucion` de `Fuentes`, y `Ajustes` usa `Fuentes` por su raíz | Cumple |
| Alcance por sucursal en la Aplicación (§5) | `AutorizadorDeAjustes` (rol y fuente) en cada caso de uso, antes de buscar la clave y de tocar datos; la política de filas es la segunda capa; el rechazo registra el acceso denegado fuera de la transacción | Cumple |
| Auditoría en la misma transacción (§4, AJU-10) | `IAuditoria.Registrar` dentro de la unidad de trabajo; si falla, lanza y la transacción revierte (prueba de integración `IP :283`, no ejecutada) | Cumple (Int.) |
| Idempotencia (AJU-16, AJU-22) | Clave primero con `ON CONFLICT DO NOTHING`; unicidad por `(devolución, edición)` como última defensa | Cumple |
| Concurrencia con solicitudes simultáneas (§4) | Probada para merma, ingreso, conteo, reingreso por devolución y ajuste con la misma clave (`IP :359,:381,:400,:419,:440`); no ejecutada en esta sesión | Cumple (Int.) |
| Secretos y datos personales (§5) | La nota y la referencia no van a la auditoría, a los registros ni a los errores (prueba con centinela `IA :244`, Int.); `PostgresException.Detail` no se propaga; sin secretos en `src` | Cumple |
| Pantallas | Esta spec no tiene pantallas propias (plan §7) | No aplica |

## 5. Conclusión

Los 22 requisitos (AJU-01 a AJU-22) cumplen en el código y en las pruebas unitarias y de arquitectura, con trazabilidad completa y sin hallazgos Bloqueantes ni Importantes propios de la feature (el Importante H1 de composición del anfitrión es transversal y ya está registrado); el DTO de reingreso ya lleva `pedidoId`. Para pasar a `Done` el coordinador debe confirmar que las pruebas de integración y de concurrencia sobre `main` pasan, y dejar H1 con su dueño en FIN-1; si alguna de ellas falla, este resultado se reevalúa.
