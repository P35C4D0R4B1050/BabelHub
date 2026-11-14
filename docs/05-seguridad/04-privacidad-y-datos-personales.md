# Privacidad y datos personales

Aplica la Ley N.° 29733 y su reglamento (DS 016-2024-JUS) de forma **proporcionada a un proyecto académico** (véase el [marco legal](../01-analisis-de-sistema/03-marco-legal.md)). Este documento no es asesoría legal: ordena qué datos se tratan, para qué, por cuánto tiempo y quién los recibe, para diseñar con criterio de minimización. Respalda RNF23, AC04 y RC17.

## 1. Inventario de datos personales

| Dato | Quién lo aporta | Finalidad | Base | Dónde vive | Quién lo ve | Retención |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| Nombre y correo (verificado) | Cliente al registrarse | Identificar la cuenta, contactar, verificar y recuperar el acceso | Ejecución del servicio | Identidad | El titular; la administración | Mientras la cuenta esté activa |
| Usuario responsable y nota de un ajuste o de un ingreso de mercadería | Personal de la sucursal o administración central | Dejar constancia de quién y por qué movió el stock (AUD-04, AJU-20) | Interés legítimo (control del inventario) | Inventario (`ajustes`, `ingresos`) | La administración central | Dos años; luego se anonimiza (AJU-21) |
| Contacto de notificación de la sucursal (número, puede ser el celular de una persona del personal) | Administración central al registrar o editar la fuente (FUE-01, FUE-16) | Avisar a la sucursal los cambios de estado de sus traslados (TRA-10, TRA-11) | Interés legítimo (operación de la sucursal) | Inventario (`fuentes.fuentes`, columna `contacto_aviso`) | La administración central (al editar la fuente) y el Notificador, solo el número y por la credencial de alcance `lectura-de-contacto`; el encargado y los clientes no lo ven | Mientras la sucursal exista (no hay baja de fuentes, FUE spec §7); si la persona dueña del número deja la sucursal, la administración lo cambia o lo quita al editar la fuente (FUE-16), que es la vía de rectificación y supresión; nunca entra a logs, errores, auditoría ni mensajes: la auditoría de la fuente registra solo el nombre del campo cambiado |
| Número de WhatsApp (opcional) | Cliente | Avisar el estado de pedidos, solo con consentimiento | Consentimiento | Identidad | El titular; el notificador (solo el número) | Mientras la cuenta esté activa o hasta que se revoque el consentimiento |
| Consentimientos (WhatsApp y tratamiento) y aceptación de términos, privacidad y reembolso (versión y fecha) | Cliente | Probar la autorización y respetar su revocación | Obligación legal | Identidad y pedido | El titular; la administración | Dos años desde su revocación o desde el pedido *(propuesta)* |
| Declaración de ser mayor de 18 años | Cliente al registrarse | Excluir a menores de edad del servicio | Ejecución del servicio | Identidad | El titular; la administración | Mientras la cuenta esté activa |
| Marca de dispositivo y región habitual | Se genera | Reconocer dispositivos y detectar cambios de correo inusuales | Interés legítimo | Identidad (solo la marca y la región del borde, no la ubicación exacta) | El titular (sus dispositivos) | Mientras la cuenta esté activa; se elimina al cancelarla (IDE-55) |
| Dirección IP (origen de las solicitudes) | Se genera | Límites de uso, detección de abuso y seguridad | Interés legítimo | Contadores (huella HMAC-SHA256 con clave versionada del entorno, sobre la IPv4 completa o la red /64 de una IPv6 —la /48 aparte, solo para el registro—, sin la dirección; todo contador expira antes de 24 horas, y durante una rotación de la clave se cuenta con la actual y la anterior); eventos de seguridad (dirección completa) | La administración (eventos) | Contadores 24 horas; eventos 2 años y luego se anonimiza (SEG-25, AUD-11) |
| Contraseña | Cliente | Autenticar | Ejecución del servicio | Identidad (solo resumen con sal) | Nadie | Mientras la cuenta esté activa |
| Sesión web (identificador de usuario, host, instantes de creación y de última acción, y la credencial de acceso y la renovación **cifradas**) | Se genera | Mantener la sesión sin exponer la credencial al navegador (WEB-04, WEB-27) | Ejecución del servicio | E-commerce, esquema `web` (`sesiones_web`); la cookie lleva solo un identificador opaco y la tabla guarda su huella SHA-256 | Nadie (solo el servidor) | 30 minutos de inactividad, tope absoluto de 7 días (cliente) u 8 horas (personal); una fila cerrada o vencida se conserva 30 minutos y, si la revocación de su renovación está pendiente, hasta que se confirme o venza la renovación; la restauración las vacía (PLA-39) |
| Borrador de pantalla (lo que la persona escribió cuando su sesión venció) | Cliente o personal | No perder lo escrito al vencer la sesión (WEB-25) | Interés legítimo (continuidad del servicio) | E-commerce, esquema `web` (`borradores`), cifrado, solo campos marcados de una lista cerrada de pantallas; sin contraseñas ni códigos | Solo la misma persona, una sola vez | 30 minutos; se borra al entregarse, y hasta 5 por usuario |
| Cookies del sitio (ver §1.1) | Se genera | Sesión, seguridad, tema y sucursal preferida | Ejecución del servicio; interés legítimo | Navegador de la persona | La persona | Según la cookie (§1.1) |
| Dirección de entrega | Cliente en el checkout (CAR-30) | Despacho a domicilio | Ejecución del servicio | E-commerce (checkout, intención de pago y pedido) | El titular; logística | En el checkout y la intención de pago, hasta que se aprueba el pago (PAG-28) y Pedidos guarda su copia (PED-32), se cancela el checkout o se resuelve el pago sin pedido, o 30 días desde su última modificación en el checkout (CAR-34) y desde su creación en la intención (PAG-27); en el pedido, dos años desde que se entrega o se anula; luego se anonimiza (PED-34); también al cancelar la cuenta (PED-33); el envío lo lee del pedido y no guarda copia (PED-32) |
| Nombre y teléfono de contacto de la entrega | Cliente en el checkout (CAR-30) | Despacho a domicilio y guía del operador logístico | Ejecución del servicio | E-commerce (checkout, intención de pago y pedido) y operador logístico | El titular; logística | En el checkout y la intención de pago, hasta que se aprueba el pago (PAG-28) y Pedidos guarda su copia (PED-32), se cancela el checkout o se resuelve el pago sin pedido, o 30 días desde su última modificación en el checkout (CAR-34) y desde su creación en la intención (PAG-27); en el pedido, dos años desde que se entrega o se anula; luego se anonimiza (PED-34); también al cancelar la cuenta (PED-33); el envío lo lee del pedido y no guarda copia (PED-32) |
| Intención e intentos de pago (identificador de cuenta, importe, fechas, estados, identificadores de la pasarela y del medio de pago) | Se genera | Cobro, reembolso y conciliación | Ejecución del servicio | E-commerce (Pagos) | El titular; administración central | Dos años desde su fecha, como el pedido (propuesta, se confirma al planificar); al cancelar la cuenta se desvinculan de la persona y quedan solo importes, fechas, estados e identificadores de la pasarela (PAG-26) |
| Historial de pedidos | Se genera | Seguimiento, devoluciones y cumplimiento | Ejecución del servicio | E-commerce | El titular; la administración; el encargado de recojo (solo su sucursal); logística (solo pedidos a domicilio, de toda la red, para generar guías) | Dos años desde que se entrega o se anula (decisión del 2026-11-13); luego se anonimiza el contacto y se conserva el resto con el identificador opaco de la cuenta (PED-33, PED-34) |
| Biblioteca digital | Se genera | Acceso a lo comprado | Ejecución del servicio | E-commerce (Biblioteca) | **Solo el titular** y el personal autorizado | Mientras la cuenta esté activa; se elimina al cancelarla (BIB-25) |
| Registro de descargas (acceso, usuario, edición y fecha) | Se genera | Contar descargas, atender ayudas y decidir reembolsos | Ejecución del servicio | E-commerce (Biblioteca) | Administración central y personal autorizado | Dos años desde su fecha (decisión del 2026-11-13); luego solo la edición y el día (BIB-26); se elimina al cancelar la cuenta (BIB-25) |
| Solicitud de ayuda de descarga (acceso, motivo y fecha) | Cliente | Atender un problema de descarga | Ejecución del servicio | E-commerce (Biblioteca) | Administración central y personal autorizado | Dos años desde su fecha (decisión del 2026-11-13); luego se elimina (BIB-29); se elimina al cancelar la cuenta (BIB-25) |
| Solicitudes de devolución y reportes de archivo defectuoso (motivo y texto libre, responsable del cambio) | Cliente | Gestionar devoluciones y reembolsos | Ejecución del servicio | E-commerce (Devoluciones) | El titular; encargado de la sucursal receptora (solo las suyas); administración central | Dos años desde su último cambio a reembolsada o rechazada (decisión del 2026-11-13); luego se redacta el texto y se quita el responsable cliente (DEV-27); se redactan al cancelar la cuenta (DEV-24); sin adjuntos |
| Hoja de reclamación (nombre, documento, domicilio, correo, monto y detalle) | Consumidor | Libro de Reclamaciones y respuesta por correo | Obligación legal | E-commerce | La administración y el consumidor | Al menos dos años; luego se anonimiza (también las descartadas) |
| Registros de auditoría y de seguridad | Se genera | Trazabilidad y seguridad | Interés legítimo | Cada servicio | La administración | Dos años; después, el residuo anonimizado sin plazo |
| Registro de supresiones (identificador opaco, tipo, fase y fecha) | Se genera | Reaplicar supresiones al restaurar un respaldo (PLA-19) | Obligación legal y ejecución del servicio | Almacén de objetos (contenedor `supresiones`, fuera de las bases) | Administración técnica con credencial de lectura | Al menos lo que dure el respaldo más antiguo retenido (PLA-38), con retención con bloqueo de tiempo; sin datos personales |
| Datos de un respaldo de producción durante un ensayo de restauración | Respaldo de producción | Comprobar que se puede restaurar (PLA-10, PLA-11, PLA-22) | Interés legítimo (excepción documentada) | Máquina de la persona responsable, en un volumen cifrado efímero | Solo la persona responsable | Solo durante el ensayo; se destruye al terminar y el informe guarda solo contadores |
| Consultas al asistente | Cliente | Responder | Ejecución del servicio | No se persisten con identidad | — | No se conservan |
| Datos de tarjeta | Cliente a la pasarela | Cobrar | — | **Solo la pasarela** | — | No se almacenan |

Los plazos son los de RNF23 (auditoría y hojas de reclamación: dos años); los demás son **propuestas** hasta que la persona responsable las apruebe.

### 1.1 Cookies que usa el sitio

Todas las cookies llevan `Secure`; las de sesión, desafío, dispositivo y antiforgery son `HttpOnly`, `SameSite=Lax` y llevan el prefijo `__Host-` (sin `Domain`, ruta `/`). Ninguna lleva datos personales: son identificadores opacos o preferencias. La política de privacidad (WEB-29) las enumera tal como están aquí.

| Cookie | Finalidad | Retención |
| :--- | :--- | :--- |
| `__Host-bh-tienda` y `__Host-bh-gestion` | Identifica la sesión web del host que la creó (un valor aleatorio de 32 bytes; no contiene datos). Estrictamente necesaria. | De sesión (sin `Max-Age`); el servidor la vence a los 30 minutos de inactividad o al tope absoluto. |
| `__Host-bh-desafio` | Mantiene el paso pendiente del ingreso del personal (segundo factor o contraseña temporal). Un solo uso. Estrictamente necesaria. | 5 minutos, no persistente; se borra al completar, fallar o vencer el paso. |
| `__Host-bh-dispositivo` | Marca opaca de 256 bits que Identidad usa para reconocer el dispositivo y avisar de cambios de correo inusuales (IDE-53 a IDE-55). Solo se emite tras un ingreso completo. | 180 días; se elimina al cancelar la cuenta (IDE-55). |
| Cookie del antiforgery (`__Host-…`) | Protege los formularios contra solicitudes falsificadas (WEB-05). Estrictamente necesaria. | De sesión. |
| `bh-tema` | Recuerda el tema claro u oscuro (WEB-09). Sin datos personales. | Persistente (hasta un año). |
| `bh-sucursal` | Recuerda la sucursal preferida (WEB-16), solo su código, validado contra la lista de sucursales. Sin datos personales. | Persistente (hasta un año). |

## 2. Reglas de minimización

1. Se pide solo lo necesario: no se pide documento de identidad, fecha de nacimiento ni datos de tarjeta. **Excepción:** la hoja del Libro de Reclamaciones pide documento y domicilio por obligación legal (REC-03).
2. El asistente recibe únicamente lo que necesita para responder; nunca nombre, correo ni teléfono si la consulta no lo requiere (ASI-08).
3. Los mensajes de WhatsApp no incluyen datos de tarjeta, contraseñas ni enlaces de descarga (NOT-07).
4. Los registros técnicos, de auditoría y de seguridad no contienen contraseñas, credenciales, datos de tarjeta ni enlaces (AUD-06, SEG-10).
5. La biblioteca revela hábitos de lectura: se muestra solo a su titular y no se comparte con editoriales ni con terceros.

## 3. Derechos del titular (ARCO)

| Derecho | Cómo se ejerce | Spec |
| :--- | :--- | :--- |
| **Acceso** | Desde la cuenta del cliente o por solicitud registrada; se informa qué datos se tienen. | IDE-12, IDE-24 |
| **Rectificación** | El cliente corrige sus datos en su cuenta. | IDE-24, IDE-25 |
| **Cancelación** | El cliente cancela su cuenta; se suprimen o anonimizan sus datos personales, salvo lo que una obligación legal exija (pedidos y hojas de reclamación). | IDE-26, IDE-27, IDE-28 |
| **Oposición** | Solicitud registrada con estado; incluye revocar el consentimiento de avisos. | IDE-11, IDE-12, NOT-08 |

Las solicitudes se registran con su estado y su fecha límite (IDE-12): 20 días hábiles para el acceso y 10 para rectificación, cancelación y oposición, a verificar contra el DS 016-2024-JUS. La administración las ve ordenadas por fecha límite, con las vencidas marcadas (IDE-32).

## 4. Transferencias y proveedores

Cada proveedor recibe solo los datos mínimos necesarios (RC17).

| Proveedor | Datos que recibe | Ubicación probable | Observaciones |
| :--- | :--- | :--- | :--- |
| **Cloudflare** | Tráfico de la tienda (direcciones IP, cabeceras, contenido de las solicitudes mientras viaja). | Red global | Es un intermediario de tránsito; no se guardan datos en él. |
| **Oracle Cloud** | Todos los datos del sistema (infraestructura). | Región elegida al crear la cuenta | Se prefiere una región cercana; los respaldos se cifran. |
| **Almacén de objetos** (Oracle Object Storage) | Archivos digitales y, en los respaldos, copias cifradas de las bases. | Según el proveedor | Sin datos personales en claro en los respaldos (cifrados). |
| **Operador logístico** | Nombre, dirección y teléfono del destinatario, solo para generar la guía de un envío a domicilio. | A verificar con el operador (no elegido aún) | Nunca recibe el documento, el correo ni datos de pago; la integración se autentica y la generación de la guía se audita. |
| **Culqi** | Importe, moneda, referencia opaca de la intención y, si la pasarela lo exige, el correo del titular (PAG-30); nunca el contacto de entrega. | A verificar con el proveedor | La tarjeta se captura en su formulario; BabelHub recibe solo un identificador. La retención propia de Culqi no se borra al cancelar la cuenta (propuesta, se confirma al planificar). |
| **WhatsApp / Evolution API** | Número de teléfono y el texto del aviso. | Fuera del país | El número se usa solo para avisos de pedidos; el texto no contiene datos sensibles. |
| **Proveedor de correo** | Correo del destinatario y el código de un solo uso, o la respuesta a una hoja de reclamación. | Google (cuenta de Gmail); condiciones a verificar. Los mensajes enviados quedan en la carpeta «Enviados» de la cuenta: se usa una cuenta dedicada al proyecto con segundo factor y se vacía esa carpeta cada 7 días, incluida la Papelera | Solo mensajes transaccionales (seguridad, alertas a la administración, respuesta a reclamos y estado de un pago pendiente); sin publicidad. La respuesta a un reclamo puede contener datos del caso: el texto se redacta sin documento ni domicilio. |
| **n8n y Evolution API** | Número de WhatsApp y texto de cada aviso, durante el envío y en el historial de ejecuciones de n8n y de mensajes de Evolution. | Viven en VM-VENTAS (volumen de n8n) y en la base técnica `evolution` de VM-INVENTARIO, que entra cifrada en la copia consistente. Decidido el 2026-11-13: el historial de n8n se purga a los 7 días (`EXECUTIONS_DATA_PRUNE` a 168 horas, registros de Docker rotados) y Evolution no guarda mensajes ni chats (`DATABASE_SAVE_*` en falso); lo respaldado sigue el plazo de los respaldos. |
| **Avisos del Notificador (`notificaciones.avisos`)** | Referencia opaca de la cuenta o código de sucursal, plantilla, estado y fechas; los campos del mensaje se vacían al terminar el aviso; sin el número. | Esquema `notificaciones` del E-commerce. Se purgan a los 90 días de su estado final y entran en los respaldos con su plazo (decisión del 2026-11-13). `destinatario_ref` de una cuenta cancelada sobrevive hasta esa purga. |
| **Números con avisos desactivados (`notificaciones.numeros_desactivados`)** | Huella del número (HMAC con clave versionada), sin el número. | Esquema `notificaciones`. Se conserva mientras dure la desactivación y se reactiva solo a pedido del titular (decisión del 2026-11-13); se verifica contra todas las versiones vigentes de la clave. |
| **Proveedor de LLM** | Texto de la consulta y datos de catálogo o del pedido del cliente que se necesiten para responder. | Fuera del país | Sin nombre, correo ni teléfono; sin historial de compra más allá del pedido consultado. |

**Pendiente:** completar con la ubicación exacta cuando se confirmen Oracle y su almacén de objetos (C10) y se elija el proveedor de LLM (B06).

## 5. Retención y borrado

| Dato | Plazo | Mecanismo |
| :--- | :--- | :--- |
| Ajustes e ingresos de mercadería del Inventario (usuario responsable y nota) | 2 años (decisión del 2026-11-14) | AJU-21: depuración diaria con un rol propio, distinto de la aplicación, que anonimiza el usuario y vacía la nota y deja registro de su ejecución; el movimiento y sus cantidades se conservan. |
| Auditoría y eventos de seguridad | 2 años | Depuración diaria: se anonimiza el usuario, la dirección IP, los campos personales del detalle y, cuando identifican a una persona, el rol y el objeto; las cuentas canceladas, en la depuración siguiente (AUD-19, AUD-32). El residuo anonimizado (acción, fecha, tipo y resultado) se conserva sin plazo (AUD-11). |
| Hojas de reclamación | Al menos 2 años; no se eliminan antes | REC-06. |
| Pedidos | 2 años (decisión del 2026-11-13) | PED-34 y PED-39: depuración diaria con auditoría sin datos personales; al cancelar la cuenta, PED-33. El pedido conserva el identificador opaco de la cuenta (decisión del 2026-11-13). |
| Registro de descargas y solicitudes de ayuda | 2 años (decisión del 2026-11-13) | BIB-26 y BIB-29: depuración diaria con auditoría sin datos personales; se eliminan al cancelar la cuenta (BIB-25). |
| Solicitudes de devolución y reportes de archivo defectuoso | 2 años (decisión del 2026-11-13) | DEV-27: depuración diaria con auditoría sin datos personales; al cancelar la cuenta, DEV-24. |
| Cuenta cancelada | Inmediato para datos de contacto | IDE-28. |
| Respaldos | Retención escalonada: 7 diarios, 4 semanales y 3 mensuales (PLA-18) | AD27; un dato suprimido puede persistir en los respaldos hasta unos tres meses; al restaurar se vuelve a suprimir (PLA-19). El archivo de cambios se conserva mientras lo exija el respaldo más antiguo. |

## 6. Incidentes de seguridad con datos personales

Se prevé un procedimiento sencillo (véase [operación segura](07-operacion-segura.md)): detectar, contener, evaluar si hay datos personales afectados, notificar a las personas y a la autoridad cuando corresponda, y documentar la causa y la corrección. **Meta interna: notificar dentro de las 48 horas de conocer el incidente** (a verificar contra el plazo del DS 016-2024-JUS). Es un procedimiento documental; en un entorno real, lo revisaría una persona experta.

## 7. Banco de datos y registro

El reglamento prevé registrar los bancos de datos personales. Para el proyecto académico se documenta qué datos se tratan (sección 1) y se deja constancia de que, en una operación real, se debe validar con una persona experta si corresponde la inscripción y la designación de un oficial de datos personales.
