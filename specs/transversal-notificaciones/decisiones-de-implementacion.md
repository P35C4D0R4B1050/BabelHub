---
spec: transversal-notificaciones
---

# Decisiones de implementación — Notificaciones

Decisiones tomadas al construir (no cambian la spec ni el plan aprobados). Lee también `progress/ajustes-al-implementar.md`.

| Tarea | Decisión | Criterios |
| :--- | :--- | :--- |
| T01 | **`Aviso`** con siete estados y una máquina cerrada; los campos y el número se vacían al terminar el aviso. **`PoliticaDeReintento`**: 5 reintentos con esperas de 1, 5, 15, 30 y 60 minutos, edad máxima de 24 h y aplazamiento de 5 min sin consumir intento. | AC-NOT-04 |
| T02 | **`CatalogoDePlantillas`** con las 14 plantillas de NOT-02 (la de traslado es para sucursal); campos cerrados y tipados, sin enlaces, tarjetas ni texto libre. **`NumeroDeDestino`** normaliza a E.164 (+51 por defecto, `00`, cero inicial, dígitos Unicode) y nunca se imprime. | AC-NOT-06, AC-NOT-13 |
| T03 | **Contrato público** (`ContratoDeNotificaciones.cs`): modelos propios, sin tipos del Dominio ni de `Shared`. `IRecepcionDeAvisos`, `IRegistroDeEstadoDeEntrega` e `IConsultaDeAvisos` solo se **declaran**; sus implementaciones son de T09 a T11. | AC-NOT-07 |
| T04 | **`SolicitarAviso`** valida plantilla, destinatario, evento, referencia y campos **en memoria antes de tocar la base o Identidad**. El consentimiento se consulta a Identidad con `IEstadoDeCuenta`; una cuenta inexistente o cancelada cuenta como sin consentimiento. Si Identidad o la base fallan devuelve `NoSeGuardo`, publica la alerta técnica (reutiliza el motivo `dependencia-no-necesaria-fallida`) y **nunca lanza**; si la alerta también falla, tampoco. | AC-NOT-01, AC-NOT-02, AC-NOT-08 |
| T05 | La unicidad (evento, destinatario, plantilla) la da el `INSERT` condicional del repositorio (`InsertarSiNoExiste`); 100 solicitudes simultáneas dejan un solo aviso. El índice único de la base es de la Infraestructura. | AC-NOT-04 |
| T06 | **`CancelarAvisosPendientes`** cancela los avisos **no finales** del cliente con el motivo `destinatario-cambiado`; es idempotente y **lanza** si la base falla, para que Identidad no deje el cambio a medias. **Sin marcar:** la comprobación del estado vigente en cada intento de envío pertenece al enviador (T08). | AC-NOT-07 |
| T07 | **`DesactivarAvisosDeUnNumero`**: solo `administrador-central` (otro rol se deniega y se registra, `fuera-de-rol`); normaliza el número, guarda **solo la huella versionada**, audita `AvisosDeNumeroDesactivados` en la misma transacción con la huella parcial como id (si la auditoría falla, no se aplica). `EstaDesactivado` mira **todas las versiones vigentes** de la clave, así una rotación no reactiva un número. La reactivación **reutiliza la misma acción de auditoría** con `causa = reactivado-a-pedido-del-titular` (no hay acción propia en el catálogo). | AC-NOT-13 |

## Pendiente para las capas siguientes

- `IEstadoDeCuenta` (consentimiento) aún no tiene adaptador real: lo entrega la Infraestructura de Identidad (IDE-6).
- Adaptadores EF de `IRepositorioDeAvisos` y `IRepositorioDeNumerosDesactivados`, el índice único y `IHuellasDeNumeros` (HMAC con clave versionada) son de la Infraestructura de Notificaciones.

## Correcciones tras la revisión de NOT-1

Revisión de arquitectura («Cumple con observaciones», sin Bloqueantes ni Importantes) y de seguridad («Cumple con observaciones», 3 Importantes, sin Bloqueantes). **Corregido y probado:**

- **Referencia del cliente canónica:** `SolicitarAviso` guarda el id de la cuenta en su forma «D» en minúsculas (acepta «N» y mayúsculas); así la unicidad (NOT-05) y la cancelación (NOT-08) no dependen de cómo se escribió. La forma «B» (con llaves) sigue rechazada.
- **Motivo de la desactivación:** se rechaza con siete o más cifras de cualquier alfabeto (cualquier separador), un `@` (o su forma de ancho completo), un enlace o caracteres de control; antes solo detectaba dígitos ASCII seguidos.
- **Filtro de tarjeta:** en los campos `Identificador` y `NumeroDePedido` solo se rechaza el valor hecho únicamente de dígitos y separadores; un GUID o una guía mixta ya no se pierde.
- Una solicitud `Rechazado` deja una alerta técnica (es un error del módulo que llama).
- El rol exige `UsuarioId` no nulo y su constante se compara con `RolesDeUsuario.AdministradorCentral` en `tests/architecture` (`ReglasDeNotificacionesTests`); la prueba usaba un rol inexistente (`logistica`) y ahora usa `personal-de-logistica`.

**Para las tareas siguientes (en `progress/hallazgos.md`):** la implementación de `ICancelacionDeAvisos` (`CancelarAvisosPendientes`) se entrega en T06 aunque su contrato es de T03; `EstaDesactivado` necesita un puerto para el enviador (T08), que además debe revisar `EsFinal` tras un `IniciarEnvio` falso y comprobar el consentimiento y el estado vigente justo antes de cada envío; el cupo reservado se devuelve al cancelar en T09; la atomicidad de AC-NOT-04 se prueba con PostgreSQL en T22; `IHuellasDeNumeros` es el puerto que implementa el calculador de huella de T13.

## NOT-2: enviar, tope, solicitud del Inventario y estado de entrega (T06, T08 a T11)

| Tarea | Decisión | Criterios |
| :--- | :--- | :--- |
| T06 | **Cupo devuelto al cancelar:** `Aviso` guarda los **cupos reservados** (`CupoDelTope`: política, clave opaca del contador, identificador de la entrada, instante), no un booleano; así `CancelarAvisosPendientes` (que Identidad llama sin conocer el número) puede devolverlos con `Liberar`. Se vacían al llegar a un estado final y la clave del contador es una huella, nunca el número. | AC-NOT-07, AC-NOT-15 |
| T08 | **Escritura condicional** (`GuardarSiNoEsFinal`): el trabajo solo guarda un resultado si el aviso sigue sin terminar en la base; una cancelación de Identidad durante el envío nunca se revierte y el cupo recién reservado se devuelve. Esa misma escritura, justo antes de llamar al servicio de envío, es la comprobación «estado vigente». Un fallo ajeno (Identidad, Inventario, motor) deja el aviso pendiente **sin consumir un intento** (5 minutos) y publica la alerta técnica `dependencia-no-necesaria-fallida`. | AC-NOT-03, AC-NOT-12 |
| T08 | **200 «aceptado» ≠ entregado:** tras `Aceptado` el aviso sigue **en envío** con el arrendamiento a 10 minutos; si el estado firmado no llega, otro trabajo lo recoge y lo reenvía con el mismo `avisoId` (n8n responde `duplicado`). `IniciarEnvio` acepta también un aviso en envío con el arrendamiento vencido. Los modos `Ensayo` y `Restauracion` no se resuelven en la Aplicación: el trabajo de fondo no se registra en ellos (T14, T15). | AC-NOT-10, AC-NOT-12 |
| T08 | **Identidad entrega el contacto por un puerto nuevo** (`IContactoDeAvisosDelCliente`: cuenta vigente, consentimiento y número E.164, sin caché), porque `IEstadoDeCuenta` no lleva el número y el plan manda leerlo «por el contrato público». Solo Notificaciones lo consume. | AC-NOT-02 |
| T09 | **Evento del tope sin acción nueva:** se reutiliza `LimiteExcedido` (acción y agrupación por ventana de 5 minutos ya existentes) con la política y la **cuenta** (identificador opaco) como sujeto, sin el número; la acción propia del catálogo que pedía la tarea queda como Menor en `progress/hallazgos.md` (exige reflejarla en los dos servicios, `Shared` y el presentador). El rechazo de una política por número no cuenta contra ningún origen. | AC-NOT-14 |
| T10 | `RecibirSolicitudDelInventario` solo agrega a `SolicitarAviso` que el evento empiece por `traslado:` y la plantilla sea de traslado; el resto (formato, campos, unicidad, nunca lanza) es el mismo camino. | AC-NOT-08 |
| T11 | Un estado `fallido` solo reprograma un aviso **en envío**; uno que ya volvió a pendiente lo ignora (sería un fallo viejo). Un aviso inexistente responde `NoEncontrado`. | AC-NOT-05, AC-NOT-11 |

## NOT-3: infraestructura (T12 a T18)

| Tarea | Decisión | Criterios |
| :--- | :--- | :--- |
| T12 | `avisos` guarda `cupos` (jsonb) en lugar del booleano `cupo_reservado` (ver T06); la base obliga con `CHECK` a vaciar campos, cupos y arrendamiento al terminar. El `INSERT` va en un punto de guardado; las demás escrituras del trabajo son condicionales (`GuardarSiNoEsFinal`). Rol `rol_notificaciones` (AD18) y paso `notificaciones` en el plan de migración. | AC-NOT-03, AC-NOT-04, AC-NOT-06 |
| T13 | La clave de la huella de desactivación es propia, versionada (`Notificaciones:ClaveDeHuellaDeNumero` = `versión:clave`, más `…Anterior`) y fuera de la rotación de `Plataforma`; el tope usa `ICalculadorDeHuella.DeSujeto` con propósito propio. | AC-NOT-13, AC-NOT-14 |
| T14, T15 | `N8nEnvioDeWhatsApp` firma `{marca}.{método}.{ruta}.{cuerpo}` con la clave saliente; el estado entrante reutiliza `IProcesadorDeEntradasDeIntegracion` (proveedor `n8n`, secreto `Integraciones:n8n:Secreto`, firma `{marca}.{cuerpo}`) en una sola transacción. El modo se lee de `Notificaciones:Envio` (n8n o simulado; el nulo en ensayo y restauración, que además no registran el trabajo de fondo). Los trabajos usan el actor «sistema». | AC-NOT-10, AC-NOT-11, AC-NOT-12 |
| T16 | El contacto de sucursal se pide con `IClienteDelInventario.ConsultarContactoDeSucursal` (alcance `lectura-de-contacto`) y un adaptador fino `ClienteDeContactoDelInventario`. | AC-NOT-08 |
| T17, T18 | Flujo `infra/n8n/flujo-de-avisos.json` con las plantillas dentro del nodo `Verificar`; verificación de CI en `FlujoDeN8nTests`. La lógica de los nodos de código se ejecuta bajo Node.js en `FlujoDeN8nEjecutadoTests` (firma, rechazos, duplicados, plantillas y estado firmado, 25 casos); **el runtime real de n8n no se probó** (haría falta descargar su imagen): la prueba de humo del README es obligatoria antes de activarlo. Compose: purga a 168 h, ejecuciones correctas sin guardar y `DATABASE_SAVE_*` en falso en Evolution. | AC-NOT-06, AC-NOT-09, AC-NOT-11 |

## NOT-4: Api y pruebas (T19 a T25)

| Tarea | Decisión | Criterios |
| :--- | :--- | :--- |
| T02 (corrección) | **La plantilla de traslado es `traslado-cambio-de-estado`** con los campos que el Inventario ya envía (`numeroDeTraslado`, `estado` de seis valores, `rolDeLaSucursal` origen o destino y `cantidad`, hasta cinco dígitos): el catálogo del Dominio y el flujo de n8n se alinearon al emisor real (`HttpEnvioAlEcommerce`), que ya estaba en `main` con sus pruebas, en vez de cambiar su carril. | AC-NOT-08 |
| T19 | `POST /api/ecommerce/v1/integraciones/n8n/estados` reutiliza la entrada de integración de `Plataforma` (firma `{marca}.{cuerpo}`, 5 minutos, deduplicación por aviso y estado) y la ráfaga por origen de la entrada de la pasarela (sin tope previo ni límite sin sesión). **Solo existe si el envío es por n8n**; el borde público (Nginx, tienda y gestión) la responde 404 y n8n llega directo al servicio por la red privada. | AC-NOT-11 |
| T20 | `POST …/integraciones/inventario/avisos` declara la operación `ecommerce.avisos.solicitar-del-inventario` (alcance `solicitar-aviso`) en la matriz del anfitrión; 202 al crear, repetir u omitir por consentimiento, 400 si la Aplicación rechaza, 503 si no se guardó (el Inventario reintenta). También 404 en el borde público. | AC-NOT-01, AC-NOT-08 |
| T21 | Desactivar, reactivar y consultar son endpoints de usuario: sin usuario, 401; el rol lo decide la Aplicación (`Prohibido`). La reactivación es una ruta aparte (`…/numeros-desactivados/reactivar`). La consulta nunca devuelve el número ni los campos. Estos tres no están en `OperacionesDelEcommerce` (contrato de `Shared`), que solo publica las dos operaciones de servicio. | AC-NOT-05, AC-NOT-13 |

## Correcciones tras la revisión de NOT-2 a NOT-4

Revisión de arquitectura («Cumple con observaciones», 4 Importantes) y de seguridad («Cumple con observaciones», 2 Importantes), sin Bloqueantes. **Corregido y probado:**

- **Un segundo `fallido` se descartaba:** el identificador de la entrada de un `fallido` lleva ahora la marca firmada (`{aviso}.fallido.{marca}`): repetir el mismo mensaje no cuenta dos veces, pero el fallo del siguiente reintento sí llega, así los reintentos siguen 1, 5, 15, 30 y 60 minutos y terminan en `reintentos-agotados` (prueba de la Api con dos fallos consecutivos).
- **CSRF en desactivar y reactivar:** filtro de origen y `Sec-Fetch-Site`, token antifalsificación y cuerpo JSON (como la administración de claves); pruebas de rechazo por token, origen, sitio y `text/plain`.
- **La réplica de gestión no corre el trabajo de envío** (no alcanza el puerto 5678 de n8n y competiría por los avisos fallándolos): solo la réplica de la tienda lo registra.
- Nginx responde 404 a las rutas internas **sin distinguir mayúsculas**; `ToString` de `EstadoDeEntregaFirmadoEntrada` y de `ContactoDeSucursalDto` no imprimen firma ni número; una guía de dígitos que pasa Luhn (13 a 19 cifras) se rechaza como tarjeta; el flujo de n8n rechaza valores con `:`, `@` o 13 dígitos seguidos.
- Arrendamiento de 6 minutos, lotes de 10 y plazo de corrida de 10 minutos (el de 2 minutos con lotes de 25 en serie podía vencer a mitad de lote); la alerta de la purga salta al primer fallo; `lock_timeout` se restaura tras las lecturas con bloqueo que corren dentro de la transacción de Identidad; pruebas unitarias del adaptador `ContactoDeAvisosDeIdentidad`.

**Riesgos residuales aceptados (también para `docs/05-seguridad`, AM32):** entre la comprobación condicional y la respuesta de n8n (hasta 10 s) una revocación o un cambio de número no detiene un mensaje ya en vuelo; `N8N_BLOCK_ENV_ACCESS_IN_NODE=false` expone el entorno de n8n a cualquier nodo de código (solo los administradores editan flujos); un nombre de pila de 30 letras escrito por quien registra el número de un tercero llega al mensaje (AM32).
