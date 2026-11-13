---
spec: transversal-notificaciones
estado: Draft
---

# Plan — Avisos por WhatsApp

> **Cómo se construye.** Parte de la spec `transversal-notificaciones` (16 requisitos, 16 criterios; spec Approved el 2026-11-13 tras ampliarla con NOT-14 a NOT-16). El módulo `Notificaciones` vive **solo en el E-commerce** (decisión del 2026-11-13); el Inventario le pide los avisos de traslado por REST. El envío es asíncrono y tolerante a fallos (AD16): el aviso se guarda con la operación y un trabajo de fondo lo entrega al webhook de n8n, que arma el mensaje con su plantilla y lo envía por Evolution API. Depende, antes de compilar, de `IUnidadDeTrabajo`, `IReloj` e `IControlDeLimites` (`transversal-seguridad` T04 y T14), de los puertos de `Auditoria` y del contrato de Identidad.

## 1. Resumen técnico

- **Guardar primero, enviar después.** Un caso de uso llama a `INotificador.Solicitar(SolicitudDeAviso)` (contrato público del módulo, en la misma transacción), que **solo inserta** y **nunca lanza ni toca la red**: valida plantilla, campos y valores **en memoria antes de tocar la base**, y el `INSERT` va dentro de un **savepoint** (un error SQL aborta la transacción de PostgreSQL aunque se capture, así que se revierte solo al savepoint); ante cualquier fallo propio publica la alerta técnica **fuera de la transacción** y deja la operación intacta (NOT-01, 03, 04, D01).
- **Un aviso por evento, destinatario y plantilla.** El `evento_id` es determinista por hecho de negocio (`<tipo>:<id>:<estado destino>`); clave única y `ON CONFLICT DO NOTHING` (NOT-05, D02).
- **Máquina de estados.** `pendiente` → `enviando` → `enviado` | `fallido`; `cancelado`; `omitido_sin_consentimiento`; `omitido_por_tope`. `enviado`, `fallido`, `cancelado` y `omitido_*` son **terminales**: un estado de entrega tardío no los revierte. **Solo un estado de entrega firmado marca `enviado`**; el 200 del webhook de n8n significa «aceptado», no entregado (NOT-06, NOT-12, D07).
- **Consentimiento y número se revalidan al enviar.** La decisión al solicitar evita avisos retroactivos y la del envío cubre lo que cambió entre tanto (NOT-03, NOT-08, D03).
- **Tope por número de cliente (NOT-14).** Antes del envío se reserva cupo en el motor de límites de `Plataforma` con dos políticas «por número» (5 por hora deslizante, 20 en 24 horas deslizantes) con clave = HMAC del número E.164; **los contactos de sucursal están exentos** (D04).
- **Plantillas en n8n.** El servicio envía la clave de plantilla y un conjunto **cerrado y tipado** de campos; n8n arma el texto (AD16) y el texto se cambia editando el flujo versionado en `infra/n8n/`, sin tocar código (NOT-10, D05).
- **Cancelación por puerto de Identidad.** Identidad **define** `ICancelacionDeAvisos` y Notificaciones lo **implementa**; Identidad lo invoca en la misma operación que revoca el consentimiento, cambia el número, cancela o desactiva (NOT-08, D06).
- **Autenticación en ambos sentidos.** La llamada a n8n y los estados de entrega llevan firma HMAC con marca de tiempo y una clave por sentido; n8n también verifica; lo no verificable se descarta, se registra y el aviso no se marca `enviado` (NOT-12, D07).
- **Entorno de desarrollo, ensayo y restauración.** `NotificadorSimulado` registra sin enviar (NOT-11); en `Ensayo` hay un adaptador nulo con el trabajo de fondo apagado, en `Restauracion` no se envía hasta pasar la revalidación, y el servicio se niega a arrancar si el modo no coincide con el notificador (D08).
- Decisiones en `decisions.md`; AD: AD16, AD24, AD25.

## 2. Capas afectadas

| Capa | Piezas nuevas o modificadas |
| :--- | :--- |
| **Dominio** (`Notificaciones`) | `Aviso` (estado, intentos, próxima fecha, cupo reservado), `PlantillaDeAviso` (clave y **campos permitidos con su tipo y largo máximo**, sin texto), `PoliticaDeReintento` (intento inicial y hasta 5 reintentos con esperas de 1, 5, 15, 30 y 60 minutos), `TipoDeDestinatario` (cliente o sucursal), `NumeroDeDestino` (normaliza a E.164 con país por defecto +51, trata espacios, `00`, `0` inicial y dígitos Unicode, y rechaza lo inválido). Solo permite eventos de la lista de NOT-02. |
| **Aplicación** (`Notificaciones`) | **Contrato público en la raíz:** `INotificador` y `SolicitudDeAviso`, `IRecepcionDeAvisos` (solicitudes del Inventario), `IRegistroDeEstadoDeEntrega`, `IConsultaDeAvisos`, `IDesactivacionDeNumeros` e **implementación** de `ICancelacionDeAvisos` (puerto que declara Identidad). En `Interno`: `SolicitarAviso`, `EnviarAvisosPendientes`, `CancelarAvisosPendientes`, `DesactivarAvisosDeUnNumero`, `RecibirSolicitudDelInventario`, `RegistrarEstadoDeEntrega` y `ConsultarAvisos`; puertos `IEnvioDeWhatsApp`, `IContactoDeSucursal`, `IRepositorioDeAvisos` e `IRepositorioDeNumerosDesactivados` (cliente REST del Inventario), los contratos de Identidad (consentimiento, número, estado de la cuenta), `IAuditoria` y `IRegistroDeSeguridad` (de `Auditoria`) y `IControlDeLimites`, `ICalculadorDeHuella`, `IReloj`, `IUnidadDeTrabajo`, `IVerificadorDeFirma` e `IAlmacenDeEntradasProcesadas` (de `Plataforma`, sin duplicarlos) e `IAlertas` (de `Auditoría`). Las acciones del catálogo de `Auditoria` se crean con su clase: `IAuditoria` para la desactivación (AUD-22 y AC-AUD-19) e `IPublicadorDeEventosDeSeguridad` de `Plataforma` para el tope (AUD-26). |
| **Infraestructura** (`Notificaciones`) | `N8nEnvioDeWhatsApp` y `SimuladoEnvioDeWhatsApp` (convención `<Tecnología><Puerto>`), `ClienteDeContactoDelInventario` (REST, 10 s), repositorios EF Core (`avisos`, `numeros_desactivados`), `CalculadorDeHuellaDeNumero` (sobre `ICalculadorDeHuella` de `Plataforma` con propósito propio), trabajo de fondo (solo en modo normal) y depuración a 90 días con alerta técnica si falla (AUD-31). |
| **Api** | Recepción firmada de estados de entrega de n8n (ruta solo de la red interna, fuera del límite por origen y de SEG-23), recepción de solicitudes de aviso del Inventario (credencial de alcance `solicitar-aviso`) y los endpoints administrativos de desactivación de números y de consulta de avisos (solo administración central); traducción HTTP, sin reglas. |
| **Web** | Sin pantallas propias: la spec no las pide y `web-base` no las define; el hueco para NOT-13 y para la visibilidad de los avisos fallidos queda registrado en `progress/hallazgos.md`. |
| **Shared** | DTOs de la solicitud de aviso del Inventario y de la respuesta de contacto, y constantes de alcances y de estados de entrega; los modelos de la Aplicación no son DTOs. |
| **Infra** (`infra/n8n/`, `infra/evolution/`) | Flujo de n8n versionado: recibe el webhook, **verifica la firma saliente, la marca de tiempo y la clave**, rechaza `avisoId` repetidos respondiendo 200 `duplicado=true`, arma el texto con la plantilla, envía por Evolution API y devuelve el estado de entrega firmado; sin secretos incrustados (credenciales de n8n). Configuración: n8n con `EXECUTIONS_DATA_PRUNE` a 168 horas y registros de Docker rotados; Evolution con `DATABASE_SAVE_*` en falso (sin guardar mensajes ni chats). |

## 3. Modelo de datos

Esquema `notificaciones` del E-commerce:

- `avisos`: id, `evento_id` (determinista), `destinatario_ref` (opaco: id de cuenta o código de sucursal), `tipo_destinatario`, `plantilla`, `version_plantilla` (versión del conjunto de campos del Dominio), `campos` (jsonb con **solo** los campos permitidos y tipados de la plantilla: sin número, sin enlaces, sin datos de pago, sin texto libre del usuario; el nombre, si se usa, es el primer nombre con un patrón de letras), `estado`, `intentos`, `proxima_fecha`, `arrendado_hasta`, `cupo_reservado` (bool), `repeticiones_duplicado`, `motivo_de_cierre` (omisión o vencido), `creado_en`, `enviado_en`. **Único** `(evento_id, destinatario_ref, plantilla)`; índice por `(estado, proxima_fecha)`. Sin el número en claro: se resuelve al enviar. `campos` se vacía al alcanzar un estado final.
- `numeros_desactivados`: `huella_numero` (HMAC con propósito propio y **versión de clave**, sobre el número E.164), `desactivado_en`, `motivo`. Se verifica contra **todas las versiones vigentes** de la clave y la clave vieja se conserva mientras existan huellas de esa versión; esta clave **queda fuera de la rotación de PLA-43** (el número no se conserva y no se puede recalcular). Reactivación solo a pedido del titular, por una operación administrativa auditada.
- **Retención (decidida el 2026-11-13):** los avisos enviados, fallidos (incluido el vencido), cancelados u omitidos se purgan a los 90 días de su estado final; `numeros_desactivados` se conserva mientras dure la desactivación; el historial de n8n y Evolution, 7 días; los respaldos siguen su propio plazo. `destinatario_ref` de una cuenta cancelada sobrevive hasta esa purga y así se declara en `docs/05-seguridad/04-privacidad-y-datos-personales.md`.
- Escritura: los módulos que llaman a `Solicitar` escriben en `notificaciones.avisos` por el contrato, dentro de su transacción y mediante `IUnidadDeTrabajo.EjecutarComo(esquema)` (`SET LOCAL ROLE` del esquema `notificaciones`, restaurando el rol previo, `transversal-seguridad` T14) dentro de la transacción del llamador, como fija AD18 (`identidad-seguridad` D08), y sin claves foráneas entre esquemas.

## 4. Contratos

| Operación | Método y ruta | Entrada | Salida | Errores |
| :--- | :--- | :--- | :--- | :--- |
| Solicitar aviso (módulos del E-commerce) | puerto `INotificador.Solicitar` | evento determinista, destinatario, plantilla, campos tipados | no lanza; crea u omite | alerta técnica propia ante un fallo |
| Solicitar aviso (Inventario) | `POST /api/ecommerce/v1/integraciones/inventario/avisos` | `eventoId` (formato `traslado:<id>:<estado>`), `plantilla` (solo de traslado), `codigoDeSucursal` (solo el formato se valida en la recepción; su existencia se comprueba al enviar, al resolver el contacto, para no crear una dependencia circular en la entrega del Inventario), campos tipados; credencial de alcance `solicitar-aviso`; tamaño máximo y límite de ráfaga | 202; el mismo resultado si se repite | `ECO-NO-AUTENTICADO` sin credencial (evento de seguridad, AUD-37), `ECO-VALIDACION-INVALIDA` si la plantilla, un campo, el evento o el formato de la sucursal no son válidos |
| Contacto de una sucursal | `GET /api/inventario/v1/sucursales/{codigo}/contacto-de-notificacion` (del Inventario) | credencial de alcance `lectura-de-contacto` | número de contacto registrado | `INV-NO-AUTENTICADO` e `INV-NO-ENCONTRADO` cuentan como intento (errores permanentes) e `INV-SERVICIO-NO-DISPONIBLE` o la espera agotada dejan el aviso pendiente sin consumir un intento |
| Enviar | `POST` al webhook de n8n | `avisoId`, plantilla, campos y número; firma HMAC sobre `{marca}.{método}.{ruta}.{cuerpo}` con la clave del sentido saliente | 200 «aceptado» (con `duplicado=true` si ya lo recibió) | espera de 10 s o 5xx ⇒ reintento |
| Estado de entrega | `POST /api/ecommerce/v1/integraciones/n8n/estados` (solo red interna) | `avisoId`, estado de un enum cerrado (`entregado`, `fallido`), marca de tiempo; firma con la clave del sentido entrante | 200 con el mismo resultado si se repite | 401 sin firma válida (registrado, descartado); estado desconocido descartado (SEG-13) |
| Desactivar número | `POST /api/ecommerce/v1/avisos/numeros-desactivados` | número (el cuerpo no se registra) | 204 (audita con una huella parcial y el campo marcado personal) | `ECO-PROHIBIDO` (solo administración central), `ECO-VALIDACION-INVALIDA` |
| Consultar avisos | `GET /api/ecommerce/v1/avisos?estado=&pagina=&tamano=` | filtros | página con estado y fechas, sin número | `ECO-PROHIBIDO` |

Los códigos y el formato de error son los de `transversal-seguridad`; la matriz de permisos es la de `docs/01-analisis-de-sistema/05-actores-y-permisos.md`. El Inventario guarda en su módulo `Traslados` la solicitud de aviso en la misma transacción del cambio de estado y la entrega con reintentos (tarea del plan de `inventario-traslados`, registrada en `progress/hallazgos.md`); su `eventoId` es determinista por traslado y estado. **El aviso de TRA-22 no pasa por aquí:** sale como alerta por correo a la administración central y a logística (decisión del 2026-11-13).

## 5. Concurrencia, transacciones e idempotencia

- **Aviso con la operación:** el `INSERT` va en la transacción del caso de uso mediante `IUnidadDeTrabajo` y dentro de un savepoint; `ON CONFLICT DO NOTHING` evita duplicados (NOT-05).
- **Envío sin duplicados:** el trabajo toma pendientes con `FOR UPDATE SKIP LOCKED`, marca `enviando` con `arrendado_hasta` corto y envía `avisoId` como clave de idempotencia; **n8n guarda un `avisoId` solo cuando Evolution lo entregó con éxito (vida de al menos 48 horas, sin el número)** y ante un repetido responde 200 `duplicado=true` y vuelve a emitir el estado `entregado`; si el intento anterior terminó en `fallido`, un reintento con el mismo `avisoId` **sí se envía de nuevo**, y un `fallido` firmado cuenta como un intento fallido con reintento (el estado de entrega `fallido` no es el estado terminal del aviso, que solo se alcanza al agotar los reintentos; `RegistrarEstadoDeEntrega` reprograma `proxima_fecha`); así un reintento tras una espera agotada no genera un segundo mensaje y el aviso no termina `fallido` por un mensaje ya entregado.
- **Orden de comprobaciones al enviar:** (1) estado vigente (si fue cancelado o es terminal, no se envía), (2) **solo para clientes**: consentimiento y número actuales por el contrato de Identidad (sin consentimiento ⇒ `omitido_sin_consentimiento`; sin número registrado ⇒ `omitido_sin_consentimiento` con motivo «sin número»; cuenta cancelada ⇒ `cancelado` con motivo); **para sucursales**: se resuelve el contacto registrado por REST y se omite el tope, (3) número no desactivado en ninguna versión de clave (desactivado ⇒ `cancelado` con motivo), (4) cupo del tope por número **solo para clientes**, reservado **una vez** por aviso (`cupo_reservado`) aunque se reintente y devuelto con `Liberar` si el aviso termina cancelado u omitido; si el cupo no alcanza, `omitido_por_tope` y un evento de seguridad **agrupado por huella y ventana de 5 minutos** (como SEG-08, sin el número); si el motor de límites no está disponible, el aviso sigue `pendiente` y **no consume un intento** (falla cerrada, sin usar el motor local). **La reserva es idempotente por `avisoId`**: `Reservar(politica, sujeto)` reclama el `avisoId` con `SET NX` dentro del mismo script, de modo que un reintento tras una caída recupera la misma reserva y nunca cuenta dos veces (NOT-15); `cupo_reservado` es solo una optimización (prueba de reserva doble forzada en T23).
- **Reintentos:** intento inicial y hasta 5 reintentos con esperas de 1, 5, 15, 30 y 60 minutos (NOT-04); tras el último, `fallido`, visible por la consulta administrativa. Esperar el estado de entrega más allá de 10 minutos devuelve el aviso a `pendiente` sin consumir un intento si n8n responde `duplicado` (como máximo 3 veces seguidas sin estado final; la cuarta cuenta como intento). **Los aplazamientos sin consumir intento** (motor de límites caído, Inventario sin responder) esperan 5 minutos entre intentos, y **todo aviso tiene una edad máxima de 24 horas desde su creación**, tras la cual pasa a `fallido` con motivo `vencido` y se vacían sus campos (un aviso de traslado no debe llegar días tarde). Los errores permanentes (`INV-NO-AUTENTICADO`, `INV-NO-ENCONTRADO`, contacto sin registrar) **cuentan como intento**.
- **Cancelación:** Identidad invoca `ICancelacionDeAvisos` en la misma operación; los avisos del destinatario pasan a `cancelado`, y aun si llega durante un envío, la comprobación (1) del siguiente intento lo detiene. El trabajo de fondo corre con un actor de sistema (SEG-15).
- **Evento anterior al consentimiento:** no genera avisos retroactivos porque la decisión se toma al solicitar.
- **Modos de arranque:** en `Restauracion` no corre el trabajo de fondo hasta pasar la revalidación de `transversal-plataforma` (los avisos pendientes restaurados traen números reales); en `Ensayo` el adaptador es nulo y el trabajo está apagado; un `NotificadorSimulado` en producción hace fallar el arranque.

## 6. Seguridad y alcance

- **Consentimiento y número** se leen de Identidad por su contrato público (IDE-31 deja sin efecto el consentimiento al cambiar el número); se revalidan al enviar y Identidad guarda el número ya normalizado a E.164.
- **Campos cerrados y tipados:** cada plantilla declara en el Dominio sus campos con tipo y largo (id, enumeración, número de pedido, fecha); `SolicitarAviso` rechaza un campo fuera del catálogo y por valor (sin enlaces, tarjetas ni códigos) y no admite texto libre del usuario: el registrante controla lo que recibe un tercero, así que solo se usa el primer nombre con un patrón de letras; una verificación en CI recorre los flujos de `infra/n8n/` y falla si una plantilla usa un campo que no está en su lista, o lleva un secreto incrustado (NOT-07, SEG-10, SEG-11).
- **Número de teléfono:** no se guarda en `avisos`; la huella usa una clave y un propósito propios con identificador de versión y normalización a E.164 (+51 por defecto); NOT-13 y NOT-14 se prueban con variantes de formato.
- **Firmas y red:** HMAC con marca de tiempo (tolerancia de 5 minutos), comparación en tiempo constante, **una clave por sentido**, registro anti-repetición de firma o `avisoId`+estado durante al menos 15 minutos (cubre la tolerancia de 5 minutos y el desfase de reloj), y rotación de cada clave con dos claves vigentes por un tiempo breve, y verificación también en n8n (si n8n no validara, su webhook sería un relé abierto de WhatsApp); lo no verificable se descarta y se registra como evento de seguridad (SEG-12, AUD-37). El puerto 5678 de n8n no se expone a VM-INVENTARIO (el Inventario ya no llama a n8n; se cierra la regla de `docs/02/05-despliegue.md`) y la ruta de estados no sale por el túnel público.
- **Desactivación (NOT-13):** exige administración central y se audita en la misma transacción (`IAuditoria`) con una huella parcial; el cuerpo de la solicitud no se registra; se conserva solo la huella.
- **Tope (NOT-14):** cuenta por número de cliente y no por cuenta; el evento de seguridad se publica por `IPublicadorDeEventosDeSeguridad` de `Plataforma` con una acción propia del catálogo de `Auditoria` (contrato de AUD-26), sin el número, y `Auditoria` lo agrupa por huella y ventana de 5 minutos, y las políticas «por número» se declaran sin publicación automática en `IControlDeLimites` para que no haya un segundo evento por el mismo rechazo. Riesgo aceptado: quien registre el número de otra persona puede agotarle su cupo de avisos (la propiedad del número no se verifica); se documenta en la spec §6.
- **Datos en n8n y Evolution:** purga de ejecuciones a 168 horas, Evolution sin guardar mensajes ni chats, registros de Docker rotados, secretos solo por entorno y volumen cifrado de la VM (`transversal-plataforma`); el número viaja a n8n solo en la llamada firmada.
- **Sin publicidad:** el Dominio solo admite plantillas de los eventos de NOT-02.

## 7. Pantallas

No hay pantallas propias en esta feature (ni en `web-base`); la administración usa los endpoints de §4 hasta que se agregue una pantalla (hueco registrado).

## 8. Estrategia de pruebas

Los emisores (Pedidos, Pagos, Envíos, Biblioteca, Devoluciones e Inventario) llaman a `INotificador`; cada uno de sus planes añade su llamada y su prueba, y AC-NOT-01 se cierra en el `verificacion.md` de cada uno.

| Criterio | Tipo de prueba | Dónde |
| :--- | :--- | :--- |
| AC-NOT-01, AC-NOT-08 | Unitaria de Aplicación con puertos simulados (un caso por evento de NOT-02, destinatarios correctos, contacto de sucursal) y contratos REST de las solicitudes del Inventario y del contacto | `tests/unit/.../Notificaciones/` y `tests/integration/.../Contratos/` |
| AC-NOT-02, AC-NOT-05, AC-NOT-07 | Unitaria de Aplicación (consentimiento al solicitar y al enviar, sin número, estados terminales con fecha, cancelación por el puerto) | `tests/unit/.../Notificaciones/` |
| AC-NOT-03, AC-NOT-04, AC-NOT-12 | Integración con PostgreSQL: canal caído, un error forzado del `INSERT` que deja la operación confirmada, reintentos con reloj controlado, unicidad y concurrencia de dos trabajadores y de duplicados simultáneos | `tests/integration/.../Notificaciones/` |
| AC-NOT-06 | Unitaria sobre todas las plantillas y campos con datos prohibidos de prueba (por valor), y verificación de los flujos de n8n | `tests/unit/.../Notificaciones/` y CI |
| AC-NOT-09 | Prueba del flujo de n8n: cambiar el texto de la plantilla en `infra/n8n/` cambia el mensaje y el servicio no se modifica (el cuerpo que envía el servicio no contiene texto) | `tests/integration/.../Notificaciones/` |
| AC-NOT-10 | Integración con el entorno de desarrollo (simulado), con el modo `Ensayo` (nulo) y con la negativa a arrancar si el modo no coincide | `tests/integration/.../Notificaciones/` |
| AC-NOT-11 | Integración de la Api con firma ausente, inválida, vieja, repetida y de otro sentido, estado desconocido, y de n8n que rechaza lo no firmado | `tests/integration/.../Integraciones/` |
| AC-NOT-13 | Integración con auditoría, el número desactivado por huella en distintos formatos y tras rotar la clave de huella | `tests/integration/.../Notificaciones/` |
| AC-NOT-14, AC-NOT-15, AC-NOT-16 | Integración con el motor de límites real: 5 en la hora, 20 en 24 horas, seis solicitudes simultáneas a un número que dejan cinco enviados, el omitido por tope que no se reenvía, la precedencia frente a consentimiento y desactivación, edad máxima de 24 horas, otro número sin efecto, el contacto de sucursal exento, vuelta a enviar pasada la ventana, un cupo por aviso con reintentos, motor caído y reserva doble forzada | `tests/integration/.../Notificaciones/` |

Todas las pruebas llevan `[Trait("Criterio", "AC-NOT-NN")]`.

## 9. Riesgos

| Riesgo | Mitigación |
| :--- | :--- |
| Evolution API (WhatsApp no oficial) puede bloquear el número. | Adaptador reemplazable (AD16), reintentos acotados y avisos fallidos visibles por la consulta administrativa. |
| Un número ingresado por error recibe avisos de un tercero, o un tercero agota el cupo de un número ajeno. | Mensajes sin datos sensibles, consentimiento por número (IDE-31), tope por número (NOT-14), desactivación por la administración (NOT-13) y riesgo aceptado documentado. |
| n8n o Evolution caen. | El aviso queda pendiente; la operación no se interrumpe. |
| El Inventario no responde al pedir el contacto de una sucursal. | El aviso queda pendiente y se reintenta sin consumir un intento. |
| El historial de n8n y Evolution conserva números y mensajes. | Purga a 7 días, Evolution sin guardar mensajes y volumen cifrado. |
| Un respaldo restaurado trae avisos pendientes con números reales. | Modo `Restauracion` sin trabajo de fondo hasta la revalidación y `Ensayo` con adaptador nulo. |

## 10. Verificación contra AGENTS.md

- [x] Dominio sin dependencias externas ni referencia a `Shared`.
- [x] Puertos definidos en Aplicación; adaptadores solo en Infraestructura (n8n, simulado, REST del Inventario, EF Core); la Api solo traduce HTTP y llama al contrato de la raíz.
- [x] Sin referencias entre servicios (el Inventario y el E-commerce se hablan por REST); módulos solo por contrato público (Identidad, Auditoría, Plataforma); Identidad define el puerto que este módulo implementa.
- [x] Alcance por sucursal: el aviso va al contacto registrado, no a un usuario de otra sucursal.
- [x] Sin secretos ni archivos digitales; el mensaje nunca lleva el enlace de descarga.
- [x] Un fallo del Notificador nunca interrumpe un caso de uso (`Solicitar` no lanza y aísla el `INSERT` con un savepoint).
