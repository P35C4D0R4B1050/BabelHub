# Contratos entre servicios v1

Congela los contratos REST entre el E-commerce y el Inventario (bloque ECO-0, 2026-11-14, nivel crítico), como lo exige [`05-desarrollo-por-bloques.md`](../07-proceso-de-desarrollo/05-desarrollo-por-bloques.md) §4 («contratos primero»). Completa el marco de [`04-modulos-y-contratos.md`](04-modulos-y-contratos.md): allí están las convenciones y los contratos de nivel de módulo; aquí está cada operación con su ruta, alcance, entrada, salida y errores. Los DTOs y las constantes viven en `src/BabelHub.Shared` (espacio `BabelHub.Shared.Contratos`), sin referenciar Dominio ni Aplicación (AGENTS.md regla 6).

**Fuente de verdad.** El catálogo `CatalogoDeOperaciones.V1` de `BabelHub.Shared` es el dato; este documento lo explica y una prueba (`tests/architecture/.../Contratos/DocumentoDeContratosTests.cs`) falla si las tablas de abajo difieren del catálogo. Cambiar una fila es un cambio de contrato: se hace en un bloque propio y avisa a los otros carriles (§12 de este documento).

## 1. Reglas comunes

| Tema | Regla |
| :--- | :--- |
| Ruta y versión | `/api/inventario/v1/<recurso>` y `/api/ecommerce/v1/integraciones/<origen>/<recurso>`; recursos en español, plural, minúsculas y con guiones. Las rutas de las tablas son relativas a esos prefijos (`RutasDeServicio`). |
| Credencial de servicio | Cada llamada lleva el secreto del alcance de su operación en `X-Credencial-De-Servicio` (doce alcances, `AlcancesDeCredencial`; rechazo por defecto, 401 uniforme y evento de seguridad). Va por HTTPS. |
| Token del usuario | Una operación de usuario lleva además `Authorization: Bearer <token>` del usuario en cuyo nombre se llama; **nunca** una cabecera de actor. Las operaciones del sistema (pasarela, trabajos de fondo, avisos) no llevan token y su actor sale del alcance. |
| Origen reenviado | `X-Origen-Reenviado` con `X-Credencial-De-Origen` (alcance `origen-reenviado`); sin la segunda se ignora (SEG-17, SEG-32). |
| Idempotencia | Las operaciones con «Clave: Sí» exigen `Idempotency-Key` (16 a 80 caracteres `[A-Za-z0-9_-]`); repetirla con la misma carga devuelve el resultado original; con otra carga, 409 `INV-IDEMPOTENCIA-EN-CONFLICTO`; mientras la primera sigue procesándose, 409 `INV-IDEMPOTENCIA-EN-CURSO`. Las demás operaciones con efecto son idempotentes por su propio estado (se indica en cada una). |
| Lecturas | Un `GET`, y un `POST` de consulta con cuerpo (varias ediciones, consulta previa, pendientes), no cambian nada: el cliente puede reintentarlos (`EsLectura`). |
| Errores | `application/problem+json` con `code` estable (`INV-…` o `ECO-…`), `traceId` y, si aplica, los campos inválidos sin el valor recibido (`ProblemaDto`). Estados: 400 validación, 401 sin credencial, 403 sin permiso, 404 no existe, 409 conflicto de estado, 422 regla de negocio, 429 límite, 503 dependencia no disponible. |
| Límites | 429 con `Retry-After` y `RespuestaDeLimiteDto` (`motivo`, `retryAfterSeconds`, `disponibleDesde`); una reserva rechazada por límite usa los motivos de `MotivosDeLimiteDeReservas`. |
| Paginación | `?pagina=1&tamano=20`, tope de 100; respuesta `PaginaDto<T>` con el total. |
| Plazos | 10 segundos por llamada, reintentos solo de lo idempotente y cortacircuitos (SEG-14); si el Inventario no responde, el E-commerce informa «no se puede validar el stock» sin cobrar (CAR-06) y el catálogo usa la última disponibilidad conocida (CAT-12). |
| Seguimiento | Toda respuesta, incluidos los errores, lleva `X-Trace-Id` igual al `traceId`. |
| Datos personales | Ningún contrato lleva datos de tarjeta, contraseñas ni enlaces firmados. El único dato personal es el número de contacto de una sucursal, que nunca se registra; los titulares se identifican por su identificador opaco y en el cuerpo, no en la ruta. |

## 2. Disponibilidad y ediciones

Alcance de lectura para el catálogo y el asistente; el registro de una edición lo hace la administración con su token. El E-commerce guarda la última disponibilidad conocida 10 minutos (CAT-12).

| Operación | Método y ruta | Alcance | Token | Clave | Éxito | Solicitud | Respuesta | Errores |
| :--- | :--- | :--- | :---: | :---: | :---: | :--- | :--- | :--- |
| `inventario.ediciones.registrar` | `PUT /ediciones/{edicionId}` | `operacion-de-usuario` | Sí | No | 204 | `RegistrarEdicionDto` | — | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-VALIDACION-INVALIDA`, `INV-FORMATO-EN-CONFLICTO`, `INV-LIMITE-EXCEDIDO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.disponibilidad.consultar` | `GET /disponibilidad/{edicionId}` | `lectura-de-disponibilidad` | No | No | 200 | — | `DisponibilidadDeEdicionDto` | `INV-NO-AUTENTICADO`, `INV-NO-ENCONTRADO`, `INV-EDICION-NO-IMPRESA`, `INV-LIMITE-EXCEDIDO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.disponibilidad.consultar-varias` | `POST /disponibilidad/consultas` | `lectura-de-disponibilidad` | No | No | 200 | `ConsultaDeDisponibilidadDto` | `DisponibilidadDeEdicionesDto` | `INV-NO-AUTENTICADO`, `INV-VALIDACION-INVALIDA`, `INV-LIMITE-EXCEDIDO`, `INV-SERVICIO-NO-DISPONIBLE` |

## 3. Reservas y selección de fuente

`pedidoId` es el identificador opaco que el E-commerce asigna al iniciar el pago; es la clave de la reserva y de los traslados que nacen de ella. `lineaId` lo asigna el E-commerce a cada línea. Repetir `crear` con el mismo `pedidoId` y una reserva pendiente la reemplaza y conserva la anterior si la nueva no puede crearse (SEL-16); la expiración es de 15 minutos desde la creación y de hasta 30 si el pago queda pendiente (SEL-22). Confirmar devuelve en `traslados` los traslados vinculados que genera (SEL-03). Cerrar descuenta el físico (SEL-11, SEL-21); repetir el cierre responde `ya-cerrada` y no vuelve a descontar. Liberar con `pedido-anulado` o `cancelado-por-el-cliente` cancela también los traslados vinculados (TRA-15, TRA-19).

| Operación | Método y ruta | Alcance | Token | Clave | Éxito | Solicitud | Respuesta | Errores |
| :--- | :--- | :--- | :---: | :---: | :---: | :--- | :--- | :--- |
| `inventario.reservas.consulta-previa` | `POST /reservas/consulta-previa` | `operacion-de-usuario` | Sí | No | 200 | `ConsultaPreviaDto` | `ConsultaPreviaRespuestaDto` | `INV-NO-AUTENTICADO`, `INV-NO-ENCONTRADO`, `INV-VALIDACION-INVALIDA`, `INV-LIMITE-EXCEDIDO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.reservas.crear` | `POST /reservas` | `operacion-de-usuario` | Sí | Sí | 201 | `CrearReservaDto` | `ReservaDto` | `INV-NO-AUTENTICADO`, `INV-NO-ENCONTRADO`, `INV-VALIDACION-INVALIDA`, `INV-STOCK-INSUFICIENTE`, `INV-EDICION-NO-IMPRESA`, `INV-LIMITE-EXCEDIDO`, `INV-IDEMPOTENCIA-EN-CONFLICTO`, `INV-IDEMPOTENCIA-EN-CURSO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.reservas.consultar` | `GET /reservas/{pedidoId}` | `operacion-de-usuario` | Sí | No | 200 | — | `ReservaDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-NO-ENCONTRADO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.reservas.confirmar` | `POST /reservas/{pedidoId}/confirmar` | `expiracion-de-reservas` | No | Sí | 200 | — | `ReservaDto` | `INV-NO-AUTENTICADO`, `INV-NO-ENCONTRADO`, `INV-VALIDACION-INVALIDA`, `INV-RESERVA-ESTADO-INVALIDO`, `INV-IDEMPOTENCIA-EN-CONFLICTO`, `INV-IDEMPOTENCIA-EN-CURSO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.reservas.extender` | `POST /reservas/{pedidoId}/extender` | `expiracion-de-reservas` | No | No | 200 | — | `ExtensionDeReservaDto` | `INV-NO-AUTENTICADO`, `INV-NO-ENCONTRADO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.reservas.liberar` | `POST /reservas/{pedidoId}/liberar` | `expiracion-de-reservas` | No | No | 200 | `LiberarReservaDto` | `ReservaDto` | `INV-NO-AUTENTICADO`, `INV-NO-ENCONTRADO`, `INV-VALIDACION-INVALIDA`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.reservas.cerrar` | `POST /reservas/{pedidoId}/cerrar` | `operacion-de-usuario` | Sí | Sí | 200 | `CerrarReservaDto` | `CierreDeReservaDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-NO-ENCONTRADO`, `INV-VALIDACION-INVALIDA`, `INV-RESERVA-ESTADO-INVALIDO`, `INV-IDEMPOTENCIA-EN-CONFLICTO`, `INV-IDEMPOTENCIA-EN-CURSO`, `INV-SERVICIO-NO-DISPONIBLE` |

## 4. Fuentes, rutas, existencias, vendidas y contacto

| Operación | Método y ruta | Alcance | Token | Clave | Éxito | Solicitud | Respuesta | Errores |
| :--- | :--- | :--- | :---: | :---: | :---: | :--- | :--- | :--- |
| `inventario.sucursales.listar` | `GET /sucursales` | `lectura-de-disponibilidad` | No | No | 200 | — | `SucursalesDto` | `INV-NO-AUTENTICADO`, `INV-LIMITE-EXCEDIDO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.sucursales.contacto` | `GET /sucursales/{codigo}/contacto-de-notificacion` | `lectura-de-contacto` | No | No | 200 | — | `ContactoDeSucursalDto` | `INV-NO-AUTENTICADO`, `INV-NO-ENCONTRADO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.fuentes.listar` | `GET /fuentes` | `operacion-de-usuario` | Sí | No | 200 | — | `ListaDeFuentesDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.fuentes.consultar` | `GET /fuentes/{codigo}` | `operacion-de-usuario` | Sí | No | 200 | — | `FuenteDetalleDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-NO-ENCONTRADO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.fuentes.registrar` | `POST /fuentes` | `operacion-de-usuario` | Sí | No | 201 | `RegistrarFuenteDto` | `FuenteDetalleDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-VALIDACION-INVALIDA`, `INV-FUENTE-DUPLICADA`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.fuentes.editar` | `PUT /fuentes/{codigo}` | `operacion-de-usuario` | Sí | No | 200 | `EditarFuenteDto` | `FuenteDetalleDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-NO-ENCONTRADO`, `INV-VALIDACION-INVALIDA`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.fuentes.designar-recepcion` | `PUT /fuente-de-recepcion` | `operacion-de-usuario` | Sí | No | 204 | `DesignarRecepcionDto` | — | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-NO-ENCONTRADO`, `INV-VALIDACION-INVALIDA`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.fuentes.existencias` | `GET /fuentes/{codigo}/existencias` | `operacion-de-usuario` | Sí | No | 200 | — | `PaginaDto<ExistenciaDto>` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-NO-ENCONTRADO`, `INV-VALIDACION-INVALIDA`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.rutas.listar` | `GET /rutas` | `operacion-de-usuario` | Sí | No | 200 | — | `ListaDeRutasDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.rutas.consultar` | `GET /rutas/{origen}/{destino}` | `operacion-de-usuario` | Sí | No | 200 | — | `RutaDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-NO-ENCONTRADO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.rutas.definir` | `PUT /rutas/{origen}/{destino}` | `operacion-de-usuario` | Sí | No | 200 | `DefinirRutaDto` | `RutaDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-NO-ENCONTRADO`, `INV-RUTA-INVALIDA`, `INV-VALIDACION-INVALIDA`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.unidades-vendidas.consultar` | `GET /unidades-vendidas` | `operacion-de-usuario` | Sí | No | 200 | — | `UnidadesVendidasDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-NO-ENCONTRADO`, `INV-SERVICIO-NO-DISPONIBLE` |

## 5. Pendientes y bajas de un titular

El identificador del titular viaja en el cuerpo. La baja es repetible sin efecto duplicado (204 también al repetirla) y reintentada por Identidad hasta que el Inventario la acepta (IDE-64).

| Operación | Método y ruta | Alcance | Token | Clave | Éxito | Solicitud | Respuesta | Errores |
| :--- | :--- | :--- | :---: | :---: | :---: | :--- | :--- | :--- |
| `inventario.titulares.consultar-pendientes` | `POST /titulares/consultas-de-pendientes` | `consulta-de-pendientes` | No | No | 200 | `ConsultaDePendientesDto` | `PendientesDeTitularDto` | `INV-NO-AUTENTICADO`, `INV-VALIDACION-INVALIDA`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.titulares.baja` | `POST /bajas-de-titular` | `baja-de-titular` | No | No | 204 | `BajaDeTitularDto` | — | `INV-NO-AUTENTICADO`, `INV-VALIDACION-INVALIDA`, `INV-SERVICIO-NO-DISPONIBLE` |

## 6. Ajustes, ingresos y devoluciones

El ingreso de mercadería es solo de la administración central y entra en la fuente de recepción: sin `codigoDeFuente` va a ella y otra se rechaza (AJU-19); la cantidad es un entero entre 1 y 5 000. El reingreso por devolución se identifica por la devolución de la ruta y repetirlo responde `yaAplicado` sin sumar (AJU-16).

| Operación | Método y ruta | Alcance | Token | Clave | Éxito | Solicitud | Respuesta | Errores |
| :--- | :--- | :--- | :---: | :---: | :---: | :--- | :--- | :--- |
| `inventario.ajustes.registrar` | `POST /ajustes` | `operacion-de-usuario` | Sí | Sí | 201 | `RegistrarAjusteDto` | `AjusteRegistradoDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-NO-ENCONTRADO`, `INV-EDICION-NO-IMPRESA`, `INV-VALIDACION-INVALIDA`, `INV-STOCK-INSUFICIENTE`, `INV-CONTEO-MENOR-QUE-RESERVAS`, `INV-SIN-CAMBIOS`, `INV-LIMITE-EXCEDIDO`, `INV-IDEMPOTENCIA-EN-CONFLICTO`, `INV-IDEMPOTENCIA-EN-CURSO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.ingresos.registrar` | `POST /ingresos-de-mercaderia` | `operacion-de-usuario` | Sí | Sí | 201 | `RegistrarIngresoDto` | `IngresoDeMercaderiaDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-NO-ENCONTRADO`, `INV-EDICION-NO-IMPRESA`, `INV-VALIDACION-INVALIDA`, `INV-FUENTE-NO-ES-DE-RECEPCION`, `INV-IDEMPOTENCIA-EN-CONFLICTO`, `INV-IDEMPOTENCIA-EN-CURSO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.ingresos.listar` | `GET /ingresos-de-mercaderia` | `operacion-de-usuario` | Sí | No | 200 | — | `ListaDeIngresosDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.devoluciones.reingresar` | `POST /devoluciones/{devolucionId}/reingreso` | `operacion-de-usuario` | Sí | No | 200 | `ReingresarDevolucionDto` | `ReingresoDeDevolucionDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-NO-ENCONTRADO`, `INV-EDICION-NO-IMPRESA`, `INV-VALIDACION-INVALIDA`, `INV-SERVICIO-NO-DISPONIBLE` |

## 7. Traslados

El ciclo de vida es el de TRA-03; cada transición es atómica y, si dos operaciones compiten, gana la primera que confirma y la otra recibe 409 `INV-TRASLADO-ESTADO-INVALIDO` (SEL-29, TRA-23).

| Operación | Método y ruta | Alcance | Token | Clave | Éxito | Solicitud | Respuesta | Errores |
| :--- | :--- | :--- | :---: | :---: | :---: | :--- | :--- | :--- |
| `inventario.traslados.solicitar` | `POST /traslados` | `operacion-de-usuario` | Sí | No | 201 | `SolicitarTrasladoDto` | `TrasladoDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-NO-ENCONTRADO`, `INV-EDICION-NO-IMPRESA`, `INV-VALIDACION-INVALIDA`, `INV-STOCK-INSUFICIENTE`, `INV-LIMITE-EXCEDIDO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.traslados.listar` | `GET /traslados` | `operacion-de-usuario` | Sí | No | 200 | — | `PaginaDto<TrasladoDto>` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-VALIDACION-INVALIDA`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.traslados.consultar` | `GET /traslados/{trasladoId}` | `operacion-de-usuario` | Sí | No | 200 | — | `TrasladoDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-NO-ENCONTRADO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.traslados.aprobar` | `POST /traslados/{trasladoId}/aprobar` | `operacion-de-usuario` | Sí | No | 200 | — | `TrasladoDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-NO-ENCONTRADO`, `INV-TRASLADO-ESTADO-INVALIDO`, `INV-LIMITE-EXCEDIDO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.traslados.rechazar` | `POST /traslados/{trasladoId}/rechazar` | `operacion-de-usuario` | Sí | No | 200 | `MotivoDeTrasladoDto` | `TrasladoDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-NO-ENCONTRADO`, `INV-VALIDACION-INVALIDA`, `INV-TRASLADO-ESTADO-INVALIDO`, `INV-TRASLADO-VINCULADO-NO-SE-RECHAZA`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.traslados.despachar` | `POST /traslados/{trasladoId}/despachar` | `operacion-de-usuario` | Sí | No | 200 | — | `TrasladoDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-NO-ENCONTRADO`, `INV-STOCK-INSUFICIENTE`, `INV-TRASLADO-ESTADO-INVALIDO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.traslados.recibir` | `POST /traslados/{trasladoId}/recibir` | `operacion-de-usuario` | Sí | No | 200 | — | `TrasladoDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-NO-ENCONTRADO`, `INV-TRASLADO-ESTADO-INVALIDO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.traslados.cancelar` | `POST /traslados/{trasladoId}/cancelar` | `operacion-de-usuario` | Sí | No | 200 | `MotivoDeTrasladoDto` | `TrasladoDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-NO-ENCONTRADO`, `INV-VALIDACION-INVALIDA`, `INV-TRASLADO-ESTADO-INVALIDO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.traslados.registrar-guia` | `PUT /traslados/{trasladoId}/guia` | `operacion-de-usuario` | Sí | No | 200 | `RegistrarGuiaDto` | `TrasladoDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-NO-ENCONTRADO`, `INV-VALIDACION-INVALIDA`, `INV-TRASLADO-ESTADO-INVALIDO`, `INV-SERVICIO-NO-DISPONIBLE` |

## 8. Historial, auditoría y alertas

Las rutas, los alcances y los errores los fijó el plan de `transversal-auditoria` (§4); aquí solo se congela el cuerpo del historial. Los cuerpos de la auditoría y de las alertas (y su cursor firmado) los define ese plan en el bloque AUD-2.

| Operación | Método y ruta | Alcance | Token | Clave | Éxito | Solicitud | Respuesta | Errores |
| :--- | :--- | :--- | :---: | :---: | :---: | :--- | :--- | :--- |
| `inventario.historial.consultar` | `GET /historial` | `historial` | Sí | No | 200 | — | `HistorialDeSucursalDto` | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-SERVICIO-NO-DISPONIBLE` |
| `inventario.auditoria.consultar` | `GET /auditoria` | `consulta-de-auditoria` | Sí | No | 200 | — | — | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-VALIDACION-INVALIDA` |
| `inventario.alertas.listar` | `GET /alertas` | `lectura-alertas` | No | No | 200 | — | — | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO` |
| `inventario.alertas.marcar-revisada` | `POST /alertas/{id}/revisada` | `marcar-alerta` | Sí | No | 204 | — | — | `INV-NO-AUTENTICADO`, `INV-PROHIBIDO`, `INV-SERVICIO-NO-DISPONIBLE` |

## 9. Del Inventario al E-commerce

Las dos operaciones se llaman con la credencial de alcance `solicitar-aviso` y se reintentan hasta que el E-commerce las acepta; son idempotentes por `eventoId` (`traslado:<id>:<estado>`).

| Operación | Método y ruta | Alcance | Token | Clave | Éxito | Solicitud | Respuesta | Errores |
| :--- | :--- | :--- | :---: | :---: | :---: | :--- | :--- | :--- |
| `ecommerce.avisos.solicitar-del-inventario` | `POST /integraciones/inventario/avisos` | `solicitar-aviso` | No | No | 202 | `SolicitudDeAvisoDeInventarioDto` | — | `ECO-NO-AUTENTICADO`, `ECO-VALIDACION-INVALIDA`, `ECO-LIMITE-EXCEDIDO` |
| `ecommerce.traslados.informar-estado` | `POST /integraciones/inventario/traslados` | `solicitar-aviso` | No | No | 204 | `EstadoDeTrasladoDePedidoDto` | — | `ECO-NO-AUTENTICADO`, `ECO-VALIDACION-INVALIDA`, `ECO-NO-ENCONTRADO`, `ECO-SERVICIO-NO-DISPONIBLE` |

## 10. Decisiones de contrato tomadas en ECO-0

Cada una resuelve un vacío que ninguna spec cerraba. Los carriles que planifican o implementan estas piezas las toman como dadas; si alguna no sirve, se cambia en un bloque de contrato.

| # | Decisión | Por qué y a quién afecta |
| :--- | :--- | :--- |
| D01 | El Inventario identifica una edición por el **identificador opaco del Catálogo** (`edicionId`); el ISBN no viaja. Para saber si una edición es impresa (FUE-07, SEL-12, AJU-18) el Catálogo la registra con `PUT /ediciones/{edicionId}` y su formato al darla de alta, y el Inventario responde 404 a una edición no registrada. | Responde la nota de `inventario-fuentes` §7. El plan de `ecommerce-catalogo` debe llamarlo al alta de toda edición; el de `inventario-fuentes` lo guarda. Si el Inventario no responde, la edición se crea igual y se registra después (como CAT-20). |
| D02 | `pedidoId` y `lineaId` los asigna el E-commerce; el Inventario solo los guarda. | Evita que el Inventario conozca pedidos. Afecta a los planes de Carrito, Pagos y Pedidos: el pedido hereda el `pedidoId` de la intención de pago. |
| D03 | **Alcances de las reservas.** Reservar, consultar, consulta previa y cerrar son hechos de una persona: `operacion-de-usuario` con su token. Confirmar, extender y liberar los dispara el sistema (aviso de la pasarela, trabajo de fondo, el propio servidor): `expiracion-de-reservas`, sin token. | El alcance `expiracion-de-reservas` hoy nombra solo la expiración; este contrato lo usa para todo el ciclo de reservas del sistema (docs/05 §3: «tareas de fondo, reintentos»). Conviene renombrarlo o ampliar su descripción al reabrir `transversal-seguridad` (carril A). |
| D04 | El Inventario informa a Pedidos los estados `en-transito`, `recibido` y `cancelado` de los traslados vinculados con `solicitar-aviso`, la única credencial del sentido Inventario → E-commerce. | TRA-11 solo pide «recibido»; PED-19 necesita también «en tránsito». Afecta al plan de `inventario-traslados` (los emite) y al de `ecommerce-pedidos` (los recibe). |
| D05 | Los motivos de un 429 de reservas son las claves de política `reservas-pendiente-por-cliente`, `reservas-por-hora-por-cliente` y `reservas-por-hora-por-origen`. | El plan de `inventario-seleccion-fuente` crea esas políticas con exactamente esos nombres (o avisa). |
| D06 | Exigen clave de idempotencia: crear, confirmar y cerrar reservas, ajustes e ingresos. Son idempotentes por estado: liberar, extender, la baja, el reingreso (por la devolución) y los `PUT`. | «Idempotency-Key en cobros, reservas y reembolsos» (docs/03/04 §2); un ajuste o un ingreso repetido por una doble pulsación sería un daño real. |
| D07 | La consulta previa exige sesión (token): el checkout la exige para pagar (CAR-16). La disponibilidad pública (catálogo, asistente) no lleva token. | Una consulta previa de visitante no existe. |
| D08 | `regionDeEntrega` es el identificador de región de la dirección en minúsculas y con guiones, el mismo que guarda la fuente (FUE-01). | El plan de `inventario-fuentes` define el conjunto de regiones; el de `ecommerce-carrito-checkout`, cómo se obtiene de la dirección. |
| D09 | La ruta del contacto de una sucursal (`/sucursales/{codigo}/contacto-de-notificacion`) es la que fijó el plan de notificaciones; `codigo` es el código de la fuente. El resto de recursos de stock usan `/fuentes`. | Un plan aprobado no se edita; la convivencia de `sucursales` y `fuentes` queda como está. |
| D10 | Las operaciones de gestión (fuentes, rutas, ajustes, traslados) se llaman con el token del usuario; el alcance por sucursal lo comprueba el Inventario en su Aplicación (AGENTS.md §5), no el E-commerce. | El E-commerce reenvía la identidad; no decide el permiso. |

## 11. Lo que no se congela

- Los cuerpos de la auditoría y de las alertas y su cursor (`transversal-auditoria`, AUD-2).
- Los nombres de las plantillas de aviso y sus campos (`transversal-notificaciones`): el contrato solo fija el sobre.
- Los endpoints de Identidad y la forma del token (`identidad-seguridad`): el contrato solo exige que el Inventario reciba el del usuario y lo valide.
- Los esquemas de base de datos, las reglas de selección de fuente (SEL-10) y los valores de los límites.
- El «usuario actual de prueba» no es un contrato: es un doble de pruebas por servicio (`tests/unit/*/Soporte/ActorDePrueba.cs`) que implementa el puerto `IActorActual` de `Application/Plataforma` hasta que Identidad emita credenciales. Ninguna Api lo registra (lo comprueba una prueba de arquitectura) y una cabecera de actor nunca lo reemplaza.

## 12. Cómo lo usan los planes

1. **Inventario:** cada endpoint declara `[OperacionDeServicio(<nombre del catálogo>)]` con su alcance y responde con los DTOs y códigos de `BabelHub.Shared`; una prueba de contrato recorre `CatalogoDeOperaciones.V1` y comprueba que no falta ni sobra ninguno.
2. **E-commerce:** el puerto `IClienteDelInventario` vive en la Aplicación con tipos propios; su adaptador en la Infraestructura traduce desde y hacia estos DTOs, llama con el alcance indicado (`OpcionesDeLaSolicitudDeServicio`) y marca como idempotentes las lecturas y las operaciones con clave.
3. **Evolución:** un cambio compatible (campo nuevo opcional) no cambia la versión; uno incompatible crea `v2` y mantiene `v1` al menos 30 días (PLA-12). Cada contrato tiene una prueba en quien lo publica y en quien lo consume (`04-modulos-y-contratos.md` §6).
