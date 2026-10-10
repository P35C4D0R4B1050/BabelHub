---
id: identidad-seguridad
titulo: Identidad, roles y control de acceso
estado: Draft
servicio: Identidad
prefijo: IDE
origen: [RF18, RF19, RF26, RF27, NN09, NN15, NN18]
calidad: [AC05, AC11, AC13, RNF05, RNF06, RNF07, RNF08, RNF15, RNF18, RNF23]
depende: [transversal-auditoria, transversal-seguridad]
---

# Spec — Identidad, roles y control de acceso

## 1. Propósito

Autenticar a los usuarios y emitir credenciales que indiquen su rol y, cuando corresponde, su sucursal, para que cada servicio aplique el alcance correcto. Incluye la administración de usuarios, la verificación del correo, la recuperación del acceso, el segundo factor del administrador central, las credenciales entre servicios y la gestión de la cuenta y de los derechos del cliente sobre sus datos.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Registro de clientes, verificación del correo, inicio de sesión, renovación y cierre de sesión. | Inicio de sesión con redes sociales. |
| Política de contraseñas y recuperación del acceso del cliente con un código de un solo uso enviado al correo. | Recuperación por WhatsApp. |
| Segundo factor (TOTP) y códigos de respaldo del administrador central. | Segundo factor para los demás roles (mejora posterior). |
| Restablecimiento de contraseñas del personal por el administrador central. | La comprobación del alcance en cada caso de uso (vive en cada spec). |
| Roles, sucursal del encargado y desactivación de usuarios. | Los límites de intentos y la respuesta de rechazo (`transversal-seguridad`). |
| Credenciales de servicio para llamadas entre servicios. | El envío de avisos de pedidos por WhatsApp (`transversal-notificaciones`). |
| Gestión de la cuenta, consentimiento de WhatsApp y derechos de acceso, rectificación, cancelación y oposición. | Atención humana de reclamos (`ecommerce-libro-reclamaciones`). |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| IDE-01 | Evento | Cuando una persona se registra como cliente, el sistema deberá crear su cuenta con nombre, correo único y contraseña, con el correo sin verificar. | RF19, RF26 |
| IDE-02 | Evento | Cuando un usuario activo ingresa credenciales correctas, el sistema deberá emitir una credencial de acceso de 15 minutos que incluya su rol y, si es encargado, su sucursal, y una renovación de 7 días. | RF18, RNF15 |
| IDE-03 | No deseado | Si las credenciales son incorrectas, entonces el sistema deberá rechazar el ingreso con el mismo mensaje sin indicar cuál dato falló. | RF18, RNF15, AC13 |
| IDE-04 | Ubicuo | El sistema deberá reconocer los roles cliente, encargado de sucursal, personal de logística y administrador central. | RF19 |
| IDE-05 | Ubicuo | El sistema deberá exigir que un encargado de sucursal tenga exactamente una sucursal y que los demás roles no tengan ninguna. | RF19 |
| IDE-06 | No deseado | Si se intenta asignar un segundo encargado activo a una sucursal, entonces el sistema deberá rechazar la asignación indicando quién la tiene. | RF19 |
| IDE-07 | Evento | Cuando el administrador central crea, actualiza o desactiva un usuario, el sistema deberá aplicar el cambio y registrarlo en la auditoría. | RF19, RNF08 |
| IDE-08 | Estado | Mientras un usuario esté desactivado, el sistema deberá impedir su ingreso y su renovación de sesión. | RF19 |
| IDE-09 | Ubicuo | El sistema deberá almacenar las contraseñas con una función de resumen adaptativa y una sal única por cuenta, y nunca en texto claro. | RNF18, AC05 |
| IDE-10 | Ubicuo | El sistema deberá permitir a los demás servicios validar la firma y la vigencia de una credencial de acceso sin consultar a Identidad en cada solicitud. | RF18 |
| IDE-11 | Evento | Cuando el cliente registra su número de WhatsApp o cambia su consentimiento para recibir avisos, el sistema deberá guardar la decisión con su fecha y aplicarla a los avisos siguientes. | NN15, RNF07 |
| IDE-12 | Evento | Cuando un cliente solicita acceder a sus datos, rectificarlos, cancelarlos u oponerse a un tratamiento, el sistema deberá registrar la solicitud con el estado recibida. | RF27, RNF07, NN15 |
| IDE-13 | No deseado | Si se registra una cuenta con un correo que ya existe, entonces el sistema deberá rechazar el registro sin revelar datos de la cuenta existente. | RF19 |
| IDE-14 | Evento | Cuando el usuario cierra sesión, el sistema deberá revocar su renovación. | RF18 |
| IDE-15 | Ubicuo | El sistema deberá exigir una contraseña de al menos 8 caracteres con mayúscula, minúscula, número y símbolo, que no figure en una lista de contraseñas comunes, al registrarse, al cambiarla y al recuperarla. | RNF15, AC05 |
| IDE-16 | Evento | Cuando una persona se registra o cambia su correo, el sistema deberá enviar a ese correo un código de un solo uso y marcar el correo como verificado al recibir el código correcto antes de que venza. | RF26, RNF15 |
| IDE-17 | Evento | Cuando una persona solicita recuperar su acceso, el sistema deberá enviar un código de un solo uso al correo verificado de la cuenta y responder con el mismo mensaje exista o no la cuenta y esté o no verificado su correo. | RF26, RNF15, AC13 |
| IDE-18 | Evento | Cuando el usuario ingresa el código de recuperación correcto antes de que venza, el sistema deberá permitirle definir una nueva contraseña, invalidar el código y revocar todas sus renovaciones vigentes. | RF26, RNF15 |
| IDE-19 | No deseado | Si el código es incorrecto, venció o ya se usó, entonces el sistema deberá rechazar la operación con un mensaje genérico y registrar el evento; el código vence a los 10 minutos y admite 5 intentos. | RF26, RNF15, AC13 |
| IDE-20 | Evento | Cuando el administrador central ingresa credenciales correctas, el sistema deberá exigir además un código TOTP y no emitir la credencial sin él. | RNF15, AC05 |
| IDE-21 | Evento | Cuando el administrador central ingresa por primera vez, el sistema deberá exigirle configurar el segundo factor y entregarle 8 códigos de respaldo de un solo uso. | RNF15 |
| IDE-22 | Evento | Cuando el administrador central usa un código de respaldo válido en lugar del código TOTP, el sistema deberá aceptarlo una sola vez y registrarlo como evento de seguridad. | RNF15, AC05 |
| IDE-23 | Evento | Cuando el administrador central restablece la contraseña de un usuario del personal, el sistema deberá fijar una contraseña temporal de un solo uso, exigir cambiarla en el siguiente ingreso y registrar la acción en la auditoría. | RF19, RNF15 |
| IDE-24 | Evento | Cuando un cliente consulta su cuenta, el sistema deberá mostrarle sus datos personales, sus consentimientos y el estado de sus solicitudes, y permitirle corregir sus datos. | RF27, NN15 |
| IDE-25 | Evento | Cuando un cliente cambia su contraseña o su correo, el sistema deberá exigir su contraseña actual. | RF27, RNF15 |
| IDE-26 | Evento | Cuando la administración central atiende una solicitud de acceso, rectificación, cancelación u oposición, el sistema deberá registrar su respuesta, cambiar su estado a en atención, atendida o rechazada, y registrar la acción en la auditoría. | RF27, RNF23 |
| IDE-27 | No deseado | Si un cliente cancela su cuenta teniendo pedidos sin entregar o reservas pendientes, entonces el sistema deberá rechazar la cancelación indicando cuáles. | RF27 |
| IDE-28 | Evento | Cuando un cliente cancela su cuenta, el sistema deberá advertir que perderá el acceso a su biblioteca digital y, al confirmar, desactivarla, anonimizar su nombre, correo y número, y conservar solo los pedidos y las hojas de reclamación exigidos por una obligación legal. | RF27, RNF23, AC11 |
| IDE-29 | Estado | Mientras el correo de una cuenta no esté verificado, el sistema deberá impedir su recuperación de acceso y permitir sus demás operaciones. | RF26 |
| IDE-30 | Ubicuo | El sistema deberá emitir credenciales de servicio de mínimo privilegio para las llamadas entre servicios y las tareas de fondo. | RNF21, AC05 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-IDE-01 | IDE-01, IDE-13 | **Dado** un correo nuevo, **cuando** una persona se registra, **entonces** se crea la cuenta de cliente con el correo sin verificar; y si el correo ya existe, se rechaza sin revelar datos. |
| AC-IDE-02 | IDE-02, IDE-04 | **Dado** un encargado de Ayacucho con credenciales correctas, **cuando** ingresa, **entonces** recibe una credencial de acceso de 15 minutos con el rol encargado y la sucursal Ayacucho, y una renovación de 7 días. |
| AC-IDE-03 | IDE-03 | **Dado** una contraseña incorrecta, **cuando** se intenta ingresar, **entonces** se rechaza con el mismo mensaje que para un correo inexistente. |
| AC-IDE-04 | IDE-05 | **Dado** un usuario con el rol encargado, **cuando** el administrador intenta guardarlo sin sucursal o con dos, **entonces** se rechaza. |
| AC-IDE-05 | IDE-06 | **Dado** una sucursal con un encargado activo, **cuando** se asigna a otro, **entonces** se rechaza indicando quién la tiene. |
| AC-IDE-06 | IDE-07 | **Dado** un administrador, **cuando** desactiva a un usuario, **entonces** el cambio se aplica y queda una entrada de auditoría. |
| AC-IDE-07 | IDE-08 | **Dado** un usuario desactivado, **cuando** intenta ingresar o renovar su sesión, **entonces** se rechaza. |
| AC-IDE-08 | IDE-09 | **Dado** la base de usuarios, **cuando** se inspecciona, **entonces** ninguna contraseña aparece en texto claro y dos cuentas con la misma contraseña tienen resúmenes distintos. |
| AC-IDE-09 | IDE-10 | **Dado** una credencial válida, **cuando** otro servicio la recibe, **entonces** valida su firma y vigencia sin llamar a Identidad. |
| AC-IDE-10 | IDE-11 | **Dado** un cliente con consentimiento activo, **cuando** lo revoca, **entonces** se guarda la fecha y los avisos siguientes no se envían. |
| AC-IDE-11 | IDE-12 | **Dado** un cliente, **cuando** solicita acceder a sus datos, rectificarlos, cancelarlos u oponerse, **entonces** cada solicitud queda registrada con el estado recibida. |
| AC-IDE-12 | IDE-14 | **Dado** una sesión activa, **cuando** el usuario cierra sesión, **entonces** su renovación deja de funcionar. |
| AC-IDE-13 | IDE-15 | **Dado** el registro de un cliente, **cuando** la contraseña tiene menos de 8 caracteres, no mezcla mayúscula, minúscula, número y símbolo, o es una contraseña común, **entonces** se rechaza indicando qué falta. |
| AC-IDE-14 | IDE-16 | **Dado** un cliente recién registrado, **cuando** ingresa el código recibido por correo, **entonces** su correo queda verificado; y si el código es incorrecto, no se verifica. |
| AC-IDE-15 | IDE-17, IDE-18 | **Dado** un cliente con correo verificado, **cuando** solicita recuperar el acceso e ingresa el código correcto a tiempo, **entonces** define una nueva contraseña, el código deja de servir y sus renovaciones anteriores quedan revocadas. |
| AC-IDE-16 | IDE-17, IDE-29 | **Dado** un correo que no existe o no está verificado, **cuando** alguien solicita recuperar el acceso, **entonces** recibe exactamente la misma respuesta que para una cuenta verificada y no se envía ningún correo. |
| AC-IDE-17 | IDE-19 | **Dado** un código vencido, ya usado o ingresado mal cinco veces, **cuando** se ingresa de nuevo, **entonces** se rechaza con un mensaje genérico y queda un registro del evento. |
| AC-IDE-18 | IDE-20 | **Dado** un administrador central con credenciales correctas, **cuando** no presenta el código TOTP, **entonces** no recibe credencial; y con el código correcto, sí. |
| AC-IDE-19 | IDE-21 | **Dado** un administrador que ingresa por primera vez, **cuando** configura el segundo factor, **entonces** recibe 8 códigos de respaldo y no puede continuar sin configurarlo. |
| AC-IDE-20 | IDE-22 | **Dado** un administrador sin su dispositivo, **cuando** usa un código de respaldo, **entonces** ingresa; y el mismo código no sirve una segunda vez. |
| AC-IDE-21 | IDE-23 | **Dado** un encargado que perdió su contraseña, **cuando** el administrador la restablece, **entonces** el encargado ingresa con la temporal, debe cambiarla y queda una entrada de auditoría. |
| AC-IDE-22 | IDE-24, IDE-25 | **Dado** un cliente con sesión, **cuando** abre su cuenta y cambia su contraseña indicando la actual, **entonces** ve sus datos, consentimientos y solicitudes y el cambio se aplica; y sin la actual, se rechaza. |
| AC-IDE-23 | IDE-26 | **Dado** una solicitud recibida, **cuando** el administrador la atiende y registra su respuesta, **entonces** su estado cambia y queda una entrada de auditoría. |
| AC-IDE-24 | IDE-27 | **Dado** un cliente con un pedido sin entregar, **cuando** intenta cancelar su cuenta, **entonces** se rechaza indicando el pedido. |
| AC-IDE-25 | IDE-28 | **Dado** un cliente sin pedidos pendientes, **cuando** confirma la cancelación, **entonces** la cuenta se desactiva, sus datos personales se anonimizan y se conservan solo los pedidos y las hojas de reclamación exigidos. |
| AC-IDE-26 | IDE-29 | **Dado** una cuenta con el correo sin verificar, **cuando** el cliente compra, **entonces** la compra se completa; y si solicita recuperar el acceso, no se envía ningún código. |
| AC-IDE-27 | IDE-30 | **Dado** una tarea de fondo del E-commerce, **cuando** llama al Inventario, **entonces** usa una credencial de servicio que solo permite las operaciones de esa tarea. |

## 5. Reglas e invariantes

- La credencial lleva rol y sucursal; la sucursal es obligatoria para encargados y vacía para los demás.
- La credencial de acceso dura 15 minutos y la renovación 7 días; la renovación depende de que el usuario siga activo.
- «Cerrar todas las sesiones» significa revocar las renovaciones; las credenciales de acceso ya emitidas caducan solas en 15 minutos como máximo.
- Las contraseñas nunca se guardan ni se registran en texto claro.
- Los códigos de un solo uso (verificación, recuperación, segundo factor y respaldo) caducan, se invalidan al usarse y no se guardan ni se registran en claro.
- El ingreso y la recuperación no revelan si una cuenta existe. El registro sí puede indicar que el correo ya está en uso; esa enumeración se acota con los límites de `transversal-seguridad`.
- Los límites de intentos, solicitudes y bloqueos de ingreso, de verificación, de recuperación y del segundo factor se definen en `transversal-seguridad`.

## 6. Casos límite y errores

- Un usuario desactivado conserva su historial de operaciones y auditoría.
- Cambiar el rol de un usuario invalida sus credenciales vigentes en la siguiente renovación.
- Un cliente puede comprar con el correo sin verificar; solo queda sin recuperación de acceso.
- El número de WhatsApp es opcional; sin él o sin consentimiento, el cliente solo se queda sin avisos.
- Si el envío del correo falla, la verificación o la recuperación no se completan y la persona puede reintentar; ninguna otra operación se interrumpe.
- Si un administrador pierde su dispositivo y sus códigos de respaldo, la recuperación se hace con acceso a la infraestructura según un procedimiento documentado fuera del sistema.
- Cancelar la cuenta hace perder el acceso a la biblioteca digital; no hay reembolso automático por ello.
- La identidad de un usuario cancelado en los registros de auditoría se anonimiza en la depuración (`transversal-auditoria`).

## 7. Preguntas abiertas

- [x] ¿Cuál es la política mínima de contraseñas? Resuelto el 2026-10-10: 8 caracteres como mínimo con mayúscula, minúscula, número y símbolo, y fuera de listas comunes (IDE-15).
- [x] ¿Cuánto dura la credencial y cuánto la renovación? Resuelto por AD22: 15 minutos y 7 días (IDE-02).
- [x] ¿Cuál es el canal de recuperación? Resuelto el 2026-10-10: correo electrónico con un proveedor gratuito de envío (reemplaza la propuesta de WhatsApp); el personal no usa recuperación propia: la restablece el administrador (IDE-23).
- [ ] ¿Qué proveedor gratuito de correo se usa y con qué dominio remitente? Es el bloqueo B08. Responde: responsable del proyecto.
- [ ] ¿Confirmas 10 minutos de vigencia, 5 intentos por código y 3 solicitudes por hora para los códigos? Propuesta: sí (IDE-19 y `transversal-seguridad`). Responde: responsable del proyecto.
- [ ] ¿Cuál es el plazo para atender una solicitud de acceso, rectificación, cancelación u oposición? Propuesta: 20 días hábiles. Responde: responsable del proyecto.
- [ ] ¿Las llamadas entre servicios reenvían la credencial del usuario o usan una credencial de servicio? Propuesta: reenviar para acciones del usuario y usar la credencial de servicio de IDE-30 para tareas de fondo. Se decide en el plan.
