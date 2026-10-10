# Privacidad y datos personales

Aplica la Ley N.° 29733 y su reglamento (DS 016-2024-JUS) de forma **proporcionada a un proyecto académico** (véase el [marco legal](../01-analisis-de-sistema/03-marco-legal.md)). Este documento no es asesoría legal: ordena qué datos se tratan, para qué, por cuánto tiempo y quién los recibe, para diseñar con criterio de minimización. Respalda RNF23, AC11 y RC17.

## 1. Inventario de datos personales

| Dato | Quién lo aporta | Finalidad | Base | Dónde vive | Quién lo ve | Retención |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| Nombre y correo (verificado) | Cliente al registrarse | Identificar la cuenta, contactar, verificar y recuperar el acceso | Ejecución del servicio | Identidad | El titular; la administración | Mientras la cuenta esté activa |
| Número de WhatsApp (opcional) | Cliente | Avisar el estado de pedidos, solo con consentimiento | Consentimiento | Identidad | El titular; el notificador (solo el número) | Mientras la cuenta esté activa o hasta que se revoque el consentimiento |
| Consentimientos (WhatsApp y tratamiento) | Cliente | Probar la autorización y respetar su revocación | Obligación legal | Identidad | El titular; la administración | Dos años desde su revocación *(propuesta)* |
| Contraseña | Cliente | Autenticar | Ejecución del servicio | Identidad (solo resumen con sal) | Nadie | Mientras la cuenta esté activa |
| Dirección de entrega | Cliente en el checkout | Despacho a domicilio | Ejecución del servicio | E-commerce (pedido) | El titular; logística | Dos años desde el pedido *(propuesta)* |
| Historial de pedidos | Se genera | Seguimiento, devoluciones y cumplimiento | Ejecución del servicio | E-commerce | El titular; la administración; el encargado de recojo (solo su sucursal) | Dos años *(propuesta)* |
| Biblioteca digital | Se genera | Acceso a lo comprado | Ejecución del servicio | E-commerce | **Solo el titular** y el personal autorizado | Mientras la cuenta esté activa |
| Hoja de reclamación | Consumidor | Libro de Reclamaciones | Obligación legal | E-commerce | La administración y el consumidor | Al menos dos años |
| Registros de auditoría y de seguridad | Se genera | Trazabilidad y seguridad | Interés legítimo | Cada servicio | La administración | Dos años |
| Consultas al asistente | Cliente | Responder | Ejecución del servicio | No se persisten con identidad | — | No se conservan |
| Datos de tarjeta | Cliente a la pasarela | Cobrar | — | **Solo la pasarela** | — | No se almacenan |

Los plazos son los de RNF23 (auditoría y hojas de reclamación: dos años); los demás son **propuestas** hasta que la persona responsable las apruebe.

## 2. Reglas de minimización

1. Se pide solo lo necesario: no se pide documento de identidad, fecha de nacimiento ni datos de tarjeta.
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

Las solicitudes se registran con su estado (IDE-12). El plazo de respuesta se define con la persona responsable del proyecto.

## 4. Transferencias y proveedores

Cada proveedor recibe solo los datos mínimos necesarios (RC17).

| Proveedor | Datos que recibe | Ubicación probable | Observaciones |
| :--- | :--- | :--- | :--- |
| **Cloudflare** | Tráfico de la tienda (direcciones IP, cabeceras, contenido de las solicitudes mientras viaja). | Red global | Es un intermediario de tránsito; no se guardan datos en él. |
| **Oracle Cloud** | Todos los datos del sistema (infraestructura). | Región elegida al crear la cuenta | Se prefiere una región cercana; los respaldos se cifran. |
| **Almacén de objetos** (R2 u Oracle) | Archivos digitales y, en los respaldos, copias cifradas de las bases. | Según el proveedor | Sin datos personales en claro en los respaldos (cifrados). |
| **Culqi** | Datos del pago y del comprador necesarios para cobrar. | A verificar con el proveedor | La tarjeta se captura en su formulario; BabelHub recibe solo un identificador. |
| **WhatsApp / Evolution API** | Número de teléfono y el texto del aviso. | Fuera del país | El número se usa solo para avisos de pedidos; el texto no contiene datos sensibles. |
| **Proveedor de correo** | Correo del destinatario y el código de un solo uso. | A verificar con el proveedor (B08) | Solo mensajes transaccionales de seguridad; sin publicidad. |
| **Proveedor de LLM** | Texto de la consulta y datos de catálogo o del pedido del cliente que se necesiten para responder. | Fuera del país | Sin nombre, correo ni teléfono; sin historial de compra más allá del pedido consultado. |

**Pendiente:** completar con la ubicación exacta cuando se elijan Oracle, el almacén (B02) y el proveedor de LLM (B06).

## 5. Retención y borrado

| Dato | Plazo | Mecanismo |
| :--- | :--- | :--- |
| Auditoría y eventos de seguridad | 2 años | Depuración periódica: se elimina o anonimiza (AUD-11). |
| Hojas de reclamación | Al menos 2 años; no se eliminan antes | REC-06. |
| Pedidos | 2 años *(propuesta)* | Anonimización de datos de contacto al vencer. |
| Cuenta cancelada | Inmediato para datos de contacto | IDE-28. |
| Respaldos | Retención escalonada (diaria y semanal) | AD27; al cancelar una cuenta, el dato desaparece del respaldo cuando este rota. |

## 6. Incidentes de seguridad con datos personales

Se prevé un procedimiento sencillo (véase [operación segura](07-operacion-segura.md)): detectar, contener, evaluar si hay datos personales afectados, notificar a las personas y a la autoridad cuando corresponda, y documentar la causa y la corrección. Es un procedimiento documental; en un entorno real, lo revisaría una persona experta.

## 7. Banco de datos y registro

El reglamento prevé registrar los bancos de datos personales. Para el proyecto académico se documenta qué datos se tratan (sección 1) y se deja constancia de que, en una operación real, se debe validar con una persona experta si corresponde la inscripción y la designación de un oficial de datos personales.
