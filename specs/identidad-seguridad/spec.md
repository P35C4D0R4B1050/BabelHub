---
id: identidad-seguridad
titulo: Identidad, roles y control de acceso
estado: Draft
servicio: Identidad
prefijo: IDE
origen: [RF18, RF19, RF26, RF27, RF28, NN09, NN15, NN18]
calidad: [AC05, AC11, AC13, RNF05, RNF06, RNF07, RNF08, RNF15, RNF18, RNF22, RNF23]
depende: [transversal-auditoria, transversal-seguridad]
---

# Spec — Identidad, roles y control de acceso

## 1. Propósito

Autenticar a los usuarios y emitir credenciales que indiquen su rol y, cuando corresponde, su sucursal, para que cada servicio aplique el alcance correcto. Incluye la administración de usuarios, la verificación del correo, la recuperación del acceso, el segundo factor (obligatorio para el personal y opcional para el cliente), las credenciales entre servicios y la gestión de la cuenta y de los derechos del cliente sobre sus datos.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Registro de clientes, verificación del correo, inicio de sesión, renovación y cierre de sesión. | Inicio de sesión con redes sociales. |
| Política de contraseñas y recuperación del acceso del cliente con un código de un solo uso enviado al correo. | Recuperación por WhatsApp. |
| Segundo factor (TOTP) y códigos de respaldo: obligatorios para el personal y opcionales para el cliente. | Segundo factor por mensaje de texto o por llave física (mejora posterior). |
| Restablecimiento de contraseñas del personal por el administrador central. | La comprobación del alcance en cada caso de uso (vive en cada spec). |
| Roles, sucursal del encargado y desactivación de usuarios. | Los límites de intentos y la respuesta de rechazo (`transversal-seguridad`). |
| Credenciales de servicio para llamadas entre servicios. | El envío de avisos de pedidos por WhatsApp (`transversal-notificaciones`). |
| Gestión de la cuenta, consentimiento de WhatsApp y derechos de acceso, rectificación, cancelación y oposición. | Atención humana de reclamos (`ecommerce-libro-reclamaciones`). |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| IDE-01 | Evento | Cuando una persona se registra como cliente, el sistema deberá crear su cuenta con nombre, correo único y contraseña, con el correo sin verificar. | RF19, RF26 |
| IDE-02 | Evento | Cuando un usuario activo ingresa credenciales correctas y cumple el segundo factor que le corresponde (IDE-20 e IDE-50), el sistema deberá emitir una credencial de acceso de 15 minutos que incluya su rol, su sucursal si es encargado y si su correo está verificado. | RF18, RNF15 |
| IDE-03 | No deseado | Si las credenciales son incorrectas, entonces el sistema deberá rechazar el ingreso con el mismo mensaje sin indicar cuál dato falló. | RF18, RNF15, AC13 |
| IDE-04 | Ubicuo | El sistema deberá reconocer los roles cliente, encargado de sucursal, personal de logística y administrador central. | RF19 |
| IDE-05 | Ubicuo | El sistema deberá exigir que un encargado de sucursal tenga exactamente una sucursal y que los demás roles no tengan ninguna. | RF19 |
| IDE-06 | No deseado | Si se intenta asignar un segundo encargado activo a una sucursal, entonces el sistema deberá rechazar la asignación indicando quién la tiene. | RF19 |
| IDE-07 | Evento | Cuando el administrador central crea, actualiza, desactiva o reactiva un usuario, o cambia su rol, el sistema deberá aplicar el cambio y registrarlo en la auditoría. | RF19, RNF08 |
| IDE-08 | Estado | Mientras un usuario esté desactivado, el sistema deberá impedir su ingreso y su renovación de sesión. | RF19 |
| IDE-09 | Ubicuo | El sistema deberá almacenar las contraseñas con una función de resumen adaptativa y una sal única por cuenta, y nunca en texto claro. | RNF18, AC05 |
| IDE-10 | Ubicuo | El sistema deberá permitir a los demás servicios validar la firma, la vigencia, la audiencia y el alcance de una credencial de acceso sin consultar a Identidad en cada solicitud. | RF18 |
| IDE-11 | Evento | Cuando el cliente cambia su consentimiento para recibir avisos por WhatsApp, el sistema deberá guardar la decisión con su fecha y aplicarla a los avisos siguientes. | NN15, RNF07 |
| IDE-12 | Evento | Cuando un cliente solicita acceder a sus datos, rectificarlos, cancelarlos u oponerse a un tratamiento, el sistema deberá registrar la solicitud con el estado recibida y su fecha límite, contada en días hábiles (lunes a viernes) desde el día siguiente: 20 para el acceso y 10 para los demás tipos. | RF27, RNF07, NN15 |
| IDE-13 | No deseado | Si se registra una cuenta con un correo que ya existe, entonces el sistema deberá rechazar el registro sin revelar datos de la cuenta existente. | RF19 |
| IDE-14 | Evento | Cuando el usuario cierra sesión, el sistema deberá revocar su renovación. | RF18 |
| IDE-15 | Ubicuo | El sistema deberá exigir una contraseña de entre 10 y 128 caracteres Unicode que no figure en una lista de contraseñas comunes o filtradas, sin imponer reglas de composición, al registrarse, al cambiarla y al recuperarla. | RNF15, AC05 |
| IDE-16 | Evento | Cuando una persona se registra o cambia su correo, el sistema deberá enviar a ese correo un código de un solo uso, con la misma vigencia e intentos que el de recuperación (IDE-19), y al recibir el código correcto antes de que venza marcar el correo como verificado y emitirle una credencial nueva con ese dato. | RF26, RNF15 |
| IDE-17 | Evento | Cuando se solicita recuperar el acceso de una cuenta, el sistema deberá responder con el mismo mensaje exista o no la cuenta, sea de personal, esté o no verificado su correo, y enviar el código de un solo uso solo si es de un cliente con el correo verificado. | RF26, RNF15, AC13 |
| IDE-18 | Evento | Cuando el usuario ingresa el código de recuperación correcto antes de que venza, el sistema deberá permitirle definir una nueva contraseña, invalidar el código y revocar todas sus renovaciones vigentes. | RF26, RNF15 |
| IDE-19 | No deseado | Si el código es incorrecto, venció o ya se usó, entonces el sistema deberá rechazar la operación con un mensaje genérico y registrar el evento; el código vence a los 10 minutos y admite 5 intentos. | RF26, RNF15, AC13 |
| IDE-20 | Evento | Cuando un usuario del personal (encargado, logística o administrador central) ingresa credenciales correctas, el sistema deberá exigir además un código TOTP y no emitir la credencial sin él. | RNF15, AC05 |
| IDE-21 | Estado | Mientras un usuario del personal no tiene el segundo factor configurado, el sistema deberá exigirle configurarlo en su siguiente ingreso y entregarle 8 códigos de respaldo de un solo uso. | RNF15 |
| IDE-22 | Evento | Cuando un usuario usa un código de respaldo válido en lugar del código TOTP, el sistema deberá aceptarlo una sola vez, registrarlo como evento de seguridad e indicar cuántos códigos le quedan. | RNF15, AC05 |
| IDE-23 | Evento | Cuando el administrador central crea un usuario del personal o restablece su contraseña, el sistema deberá fijar una contraseña temporal que vence a las 24 horas, mostrarla solo al administrador, exigir cambiarla en el siguiente ingreso y registrar la acción en la auditoría; si no se cambia, sigue válida hasta vencer, y vencida solo la administración puede restablecerla. | RF19, RNF15 |
| IDE-24 | Evento | Cuando un cliente consulta su cuenta, el sistema deberá mostrarle sus datos personales, sus consentimientos y el estado de sus solicitudes, y permitirle corregir sus datos; la corrección directa reemplaza una solicitud de rectificación. | RF27, NN15 |
| IDE-25 | Evento | Cuando un cliente cambia su contraseña o su correo, el sistema deberá exigir su contraseña actual. | RF27, RNF15 |
| IDE-26 | Evento | Cuando la administración central atiende una solicitud de acceso, rectificación, cancelación u oposición, el sistema deberá registrar su respuesta (con el motivo si la rechaza), cambiar su estado a en atención, atendida o rechazada, mostrar la respuesta en la cuenta del cliente, avisarle por correo y registrar la acción en la auditoría. | RF27, RNF23 |
| IDE-27 | No deseado | Si un cliente pide cancelar su cuenta teniendo pedidos sin entregar, reservas, pagos pendientes, devoluciones o reclamos abiertos, entonces el sistema deberá registrar la solicitud, impedirle nuevas compras, indicarle cuáles faltan y ejecutar la cancelación cuando se cierren todos. | RF27 |
| IDE-28 | Evento | Cuando un cliente pide cancelar su cuenta, el sistema deberá advertir que perderá el acceso a su biblioteca digital y, antes de confirmar, exigir su contraseña actual y su segundo factor si lo tiene. | RF27, RNF23, AC11 |
| IDE-29 | Estado | Mientras el correo de una cuenta no esté verificado, el sistema deberá impedir su recuperación de acceso y el inicio de un pago, y permitir sus demás operaciones. | RF26, RF28 |
| IDE-30 | Ubicuo | El sistema deberá emitir credenciales de servicio de mínimo privilegio para las llamadas entre servicios y las tareas de fondo. | RNF21, AC05 |
| IDE-31 | Evento | Cuando el cliente registra o cambia su número de WhatsApp, el sistema deberá validar su formato, guardarlo solo si es válido y dejar sin efecto su consentimiento hasta que lo confirme para ese número. | NN15, RNF07 |
| IDE-32 | Evento | Cuando la administración central abre la lista de solicitudes sobre datos personales, el sistema deberá mostrarlas ordenadas por fecha límite e indicar las vencidas sin atender (una solicitud en atención cuenta como sin atender). | RF27, RNF23 |
| IDE-33 | Evento | Cuando la capa web pide una credencial para el asistente en nombre de un cliente autenticado, el sistema deberá emitir una credencial de 5 minutos, sin renovación, con el alcance único de leer los pedidos de ese cliente y válida solo para el servicio de pedidos. | RNF22, AC05 |
| IDE-34 | Evento | Cuando una persona se registra, el sistema deberá exigirle aceptar los términos y la política de privacidad y guardar la versión y la fecha aceptadas. | NN15, RNF23 |
| IDE-35 | Ubicuo | El sistema deberá firmar las credenciales con una clave que solo posee el servicio de Identidad, un algoritmo fijo y el identificador de la clave en cada credencial, y publicar la clave de verificación para que los demás servicios validen las credenciales sin poder emitirlas. | RNF18, AC05 |
| IDE-36 | Evento | Cuando transcurren 90 días desde la última rotación, el sistema deberá rotar la clave de firma, aceptando las credenciales de la clave anterior solo hasta que venzan, y registrar la rotación en la auditoría. | RNF18, AC05 |
| IDE-37 | Evento | Cuando un cliente activa el segundo factor en su cuenta, el sistema deberá pedirle su contraseña actual, configurarlo, entregarle 8 códigos de respaldo y exigirlo en sus ingresos siguientes; y para desactivarlo, deberá pedir su contraseña y un código vigente. | RNF15, RF27 |
| IDE-38 | Evento | Cuando el administrador central restablece el segundo factor de otro usuario que perdió su dispositivo y sus códigos de respaldo, el sistema deberá borrar su configuración, exigirle configurarlo en su siguiente ingreso, revocar todas sus renovaciones, avisar a su correo y registrarlo en la auditoría. | RNF15, RF19 |
| IDE-39 | Evento | Cuando una persona se registra, el sistema deberá exigirle declarar que es mayor de 18 años y guardar la declaración con su fecha, y rechazar el registro sin ella. | NN15, RNF23 |
| IDE-40 | Ubicuo | El sistema deberá guardar el secreto del segundo factor cifrado y los códigos de respaldo solo como un resumen que no permita recuperarlos. | RNF18, AC05 |
| IDE-41 | No deseado | Si se presenta un código TOTP ya usado en su ventana, entonces el sistema deberá rechazarlo, aceptando solo uno si dos se presentan a la vez. | RNF15, AC05 |
| IDE-42 | Estado | Mientras un usuario ingresó la contraseña pero no el segundo factor, durante 5 minutos como máximo, el sistema deberá permitirle solo configurarlo o presentarlo y no emitirle ninguna credencial; vencido el plazo, deberá reiniciar el ingreso. | RNF15, AC05 |
| IDE-43 | Evento | Cuando la versión vigente de los términos o de la privacidad difiere de la que una persona aceptó, el sistema deberá pedirle aceptar la nueva en su siguiente ingreso o renovación de sesión y guardar su versión y fecha. | NN15, RNF23 |
| IDE-44 | Evento | Cuando un usuario cambia su contraseña o activa, desactiva o restablece su segundo factor, el sistema deberá revocar sus demás renovaciones; y cuando otra persona restablece su contraseña, deberá revocar todas. | RNF15, AC05 |
| IDE-45 | Evento | Cuando se cambia o se restablece la contraseña de una cuenta, el sistema deberá avisar al correo de la cuenta, sin incluir la contraseña. | RNF15, AC05 |
| IDE-46 | Evento | Cuando se emite la credencial de un ingreso, el sistema deberá emitir también una renovación opaca de 7 días para el cliente o de 8 horas para el personal, cuyo vencimiento absoluto se hereda en cada uso y no se extiende. | RF18, RNF15 |
| IDE-47 | Evento | Cuando se ejecuta la cancelación de una cuenta, el sistema deberá desactivarla, revocar sus renovaciones, cancelar sus avisos pendientes, anonimizar su nombre, correo y número, pedir al E-commerce anonimizar los datos de contacto de sus pedidos y conservar solo los pedidos y las hojas de reclamación exigidos por una obligación legal. | RF27, RNF23, AC11 |
| IDE-48 | Ubicuo | El sistema deberá permitir registrar de nuevo un correo anonimizado por una cancelación como una cuenta nueva, sin recuperar nada de la cuenta cancelada. | RF27, RNF23 |
| IDE-49 | No deseado | Si un administrador intenta restablecer, desactivar o cambiar el rol de su propio usuario, o desactivar o degradar al último administrador activo, entonces el sistema deberá rechazarlo. | RF19, RNF05 |
| IDE-50 | Evento | Cuando un cliente que activó el segundo factor ingresa credenciales correctas, el sistema deberá exigir además un código TOTP y no emitir la credencial sin él. | RNF15, AC05 |
| IDE-51 | Ubicuo | El sistema deberá aceptar un código TOTP con un desfase de una ventana de 30 segundos hacia cada lado. | RNF15 |
| IDE-52 | Evento | Cuando un usuario con el segundo factor activo pide regenerar sus códigos de respaldo, el sistema deberá pedir su contraseña y un código vigente, invalidar los anteriores y entregar 8 nuevos. | RNF15, AC05 |
| IDE-53 | Evento | Cuando un cliente cambia su correo desde un dispositivo reconocido y una región habitual, el sistema deberá aplicar el cambio tras verificar el correo nuevo y avisar en la cuenta y al correo nuevo, sin avisar al correo anterior. | RNF15, AC05 |
| IDE-54 | Evento | Cuando un cliente intenta cambiar su correo desde un dispositivo o una región no reconocidos, el sistema deberá no aplicar el cambio, congelar los cambios sensibles de ese dispositivo, alertar por todos los medios disponibles (correo anterior, WhatsApp con consentimiento y sesiones reconocidas) y permitir al titular confirmarlo o rechazarlo desde un dispositivo reconocido durante 24 horas, vencidas las cuales el cambio se descarta. | RNF15, AC05 |
| IDE-55 | Ubicuo | El sistema deberá considerar reconocido un dispositivo que lleva una marca emitida en un ingreso anterior completo, y habitual una región que coincide con la de ingresos anteriores entregada por el borde, y guardar solo esa marca y esa región, no la ubicación exacta. | RNF15, RNF23 |
| IDE-56 | Evento | Cuando el cliente pide reenviar el código de verificación de su correo, el sistema deberá enviar uno nuevo, sujeto al límite de SEG-19. | RF26, RNF15 |
| IDE-57 | Ubicuo | El sistema deberá guardar la versión vigente de los términos, la privacidad y el reembolso, y rechazar una aceptación que cite otra versión. | NN15, RNF23 |
| IDE-58 | Estado | Mientras una persona no acepta la versión vigente de los textos legales, el sistema deberá permitirle ver su cuenta y sus pedidos, e impedirle iniciar pagos y hacer cambios sensibles. | NN15, RNF23 |
| IDE-59 | Ubicuo | El sistema deberá comparar la contraseña, normalizada, con una lista local de al menos 10 000 contraseñas comunes o filtradas, contar su longitud en caracteres y permitir pegarla y autocompletarla. | RNF15, AC05 |
| IDE-60 | Evento | Cuando un usuario pide cerrar todas sus sesiones, el sistema deberá revocar todas sus renovaciones. | RF18, RNF15 |
| IDE-61 | Evento | Cuando se declara comprometida la clave de firma, el sistema deberá rotarla, dejar de aceptar sus credenciales de acceso en un máximo de 5 minutos y revocar de inmediato las renovaciones opacas. | RNF18, AC05 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-IDE-01 | IDE-01, IDE-13 | **Dado** un correo nuevo, **cuando** una persona se registra, **entonces** se crea la cuenta de cliente con el correo sin verificar; y si el correo ya existe, se rechaza sin revelar datos. |
| AC-IDE-02 | IDE-02, IDE-04 | **Dado** un encargado de Ayacucho con credenciales y código TOTP correctos, **cuando** ingresa, **entonces** recibe una credencial de acceso de 15 minutos con el rol encargado, la sucursal Ayacucho y el dato de correo verificado; y con solo la contraseña no recibe ninguna. |
| AC-IDE-03 | IDE-03 | **Dado** una contraseña incorrecta, **cuando** se intenta ingresar, **entonces** se rechaza con el mismo mensaje que para un correo inexistente. |
| AC-IDE-04 | IDE-05 | **Dado** un usuario con el rol encargado, **cuando** el administrador intenta guardarlo sin sucursal o con dos, **entonces** se rechaza. |
| AC-IDE-05 | IDE-06 | **Dado** una sucursal con un encargado activo, **cuando** se asigna a otro, **entonces** se rechaza indicando quién la tiene. |
| AC-IDE-06 | IDE-07 | **Dado** un administrador, **cuando** desactiva a un usuario, **entonces** el cambio se aplica y queda una entrada de auditoría. |
| AC-IDE-07 | IDE-08 | **Dado** un usuario desactivado, **cuando** intenta ingresar o renovar su sesión, **entonces** se rechaza. |
| AC-IDE-08 | IDE-09 | **Dado** la base de usuarios, **cuando** se inspecciona, **entonces** ninguna contraseña aparece en texto claro y dos cuentas con la misma contraseña tienen resúmenes distintos. |
| AC-IDE-09 | IDE-10 | **Dado** una credencial válida, **cuando** otro servicio la recibe, **entonces** valida su firma y vigencia sin llamar a Identidad. |
| AC-IDE-10 | IDE-11 | **Dado** un cliente con consentimiento activo, **cuando** lo revoca, **entonces** se guarda la fecha y los avisos siguientes no se envían. |
| AC-IDE-11 | IDE-12 | **Dado** un cliente, **cuando** solicita acceder a sus datos, rectificarlos, cancelarlos u oponerse, **entonces** cada solicitud queda registrada con el estado recibida y su fecha límite: 20 días hábiles si es de acceso y 10 si es de otro tipo. |
| AC-IDE-12 | IDE-14 | **Dado** una sesión activa, **cuando** el usuario cierra sesión, **entonces** su renovación deja de funcionar. |
| AC-IDE-13 | IDE-15 | **Dado** el registro de un cliente, **cuando** la contraseña tiene 9 o 129 caracteres o es una contraseña común o filtrada, **entonces** se rechaza indicando el motivo; y con 10 o 128 caracteres, o una frase de 12 letras minúsculas fuera de la lista, se acepta; y se puede pegar y autocompletar. |
| AC-IDE-14 | IDE-16 | **Dado** un cliente recién registrado, **cuando** ingresa el código recibido por correo, **entonces** su correo queda verificado; y si el código es incorrecto, no se verifica. |
| AC-IDE-15 | IDE-17, IDE-18 | **Dado** un cliente con correo verificado, **cuando** solicita recuperar el acceso e ingresa el código correcto a tiempo, **entonces** define una nueva contraseña, el código deja de servir y sus renovaciones anteriores quedan revocadas. |
| AC-IDE-16 | IDE-17, IDE-29 | **Dado** un correo que no existe, uno no verificado y uno de una cuenta del personal, **cuando** alguien solicita recuperar el acceso, **entonces** recibe exactamente la misma respuesta que para una cuenta verificada y no se envía ningún correo. |
| AC-IDE-17 | IDE-19 | **Dado** un código vencido, ya usado o ingresado mal cinco veces, **cuando** se ingresa de nuevo, **entonces** se rechaza con un mensaje genérico y queda un registro del evento. |
| AC-IDE-18 | IDE-20, IDE-50 | **Dado** un encargado, un usuario de logística o un administrador con credenciales correctas, **cuando** no presenta el código TOTP, **entonces** no recibe credencial; y con el código correcto, sí; y lo mismo vale para un cliente que activó el segundo factor, mientras que uno que no lo activó ingresa solo con su contraseña. |
| AC-IDE-19 | IDE-21 | **Dado** un encargado que ingresa por primera vez, o un cliente cuyo rol cambió a encargado, **cuando** configura el segundo factor, **entonces** recibe 8 códigos de respaldo y no puede continuar sin configurarlo. |
| AC-IDE-20 | IDE-22 | **Dado** un usuario sin su dispositivo, **cuando** usa un código de respaldo, **entonces** ingresa, se le informa cuántos le quedan y el mismo código no sirve una segunda vez. |
| AC-IDE-21 | IDE-23 | **Dado** un encargado que perdió su contraseña, **cuando** el administrador la restablece, **entonces** el encargado ingresa con la temporal, debe cambiarla y queda una entrada de auditoría; si no la cambia, sigue válida hasta las 24 horas; y pasado ese plazo se rechaza hasta que la administración la restablezca. |
| AC-IDE-22 | IDE-24, IDE-25 | **Dado** un cliente con sesión, **cuando** abre su cuenta y cambia su contraseña indicando la actual, **entonces** ve sus datos, consentimientos y solicitudes y el cambio se aplica; y sin la actual, se rechaza. |
| AC-IDE-23 | IDE-26 | **Dado** una solicitud recibida, **cuando** el administrador la atiende y registra su respuesta, o la rechaza con un motivo, **entonces** su estado cambia, el cliente ve la respuesta en su cuenta y recibe un correo, y queda una entrada de auditoría. |
| AC-IDE-24 | IDE-27 | **Dado** un cliente con un pedido sin entregar y un reclamo abierto, **cuando** pide cancelar su cuenta, **entonces** la solicitud queda registrada, no puede hacer nuevas compras, ve qué falta cerrar y la cuenta se cancela sola cuando todo se cierra. |
| AC-IDE-25 | IDE-28 | **Dado** un cliente sin pendientes, **cuando** pide cancelar su cuenta, **entonces** ve la advertencia de que perderá su biblioteca y debe indicar su contraseña actual y su código del segundo factor si lo tiene; sin ellos no se confirma. |
| AC-IDE-26 | IDE-29 | **Dado** una cuenta con el correo sin verificar, **cuando** el cliente arma su carrito y consulta su cuenta, **entonces** puede hacerlo; **cuando** intenta iniciar un pago, **entonces** se le invita a verificar su correo; y si solicita recuperar el acceso, no se envía ningún código. |
| AC-IDE-27 | IDE-30 | **Dado** una tarea de fondo del E-commerce, **cuando** llama al Inventario, **entonces** usa una credencial de servicio que solo permite las operaciones de esa tarea. |
| AC-IDE-28 | IDE-31 | **Dado** un número de WhatsApp con formato inválido, **cuando** el cliente lo guarda, **entonces** se rechaza; con formato válido, se guarda; y si cambia de número, no recibe avisos hasta confirmar su consentimiento para el número nuevo. |
| AC-IDE-29 | IDE-32 | **Dado** una solicitud de acceso con 21 días hábiles sin atender y una de rectificación con 3, **cuando** la administración abre la lista, **entonces** las ve ordenadas por fecha límite y la primera figura como vencida. |
| AC-IDE-30 | IDE-33 | **Dado** un cliente autenticado, **cuando** la capa web pide la credencial del asistente, **entonces** recibe una credencial que vence a los 5 minutos, permite leer solo los pedidos de ese cliente y es rechazada al intentar cualquier escritura, leer otro cliente o usarla en la biblioteca, la cuenta o la descarga de archivos. |
| AC-IDE-31 | IDE-34 | **Dado** una persona que se registra sin aceptar los términos o la privacidad, **cuando** envía el formulario, **entonces** se rechaza; y al aceptarlos, la cuenta guarda la versión y la fecha de ambos textos. |
| AC-IDE-32 | IDE-35 | **Dado** un servicio de negocio, **cuando** valida una credencial, **entonces** solo usa la clave de verificación publicada y no puede emitir una credencial que otro servicio acepte. |
| AC-IDE-33 | IDE-36, IDE-61 | **Dado** una clave de firma de 91 días, **cuando** se rota, **entonces** las credenciales firmadas con la anterior siguen siendo válidas hasta su vencimiento y las nuevas usan la clave nueva; y si la clave se declara comprometida, las credenciales de acceso firmadas con ella se rechazan en un máximo de 5 minutos, incluso si Identidad no responde a la actualización, y las renovaciones se revocan de inmediato; y la rotación queda en la auditoría. |
| AC-IDE-34 | IDE-37 | **Dado** un cliente con sesión, **cuando** activa el segundo factor indicando su contraseña, **entonces** recibe 8 códigos de respaldo y en su siguiente ingreso se le pide el código; y para desactivarlo debe presentar su contraseña y un código vigente. |
| AC-IDE-35 | IDE-38 | **Dado** un encargado que perdió su dispositivo y sus códigos, **cuando** el administrador restablece su segundo factor, **entonces** al ingresar debe configurarlo de nuevo, sus renovaciones previas quedan revocadas, recibe un aviso en su correo y queda una entrada de auditoría. |
| AC-IDE-36 | IDE-39 | **Dado** una persona que se registra sin declarar que es mayor de 18 años, **cuando** envía el formulario, **entonces** se rechaza; y con la declaración, la cuenta guarda la declaración con su fecha. |
| AC-IDE-37 | IDE-40 | **Dado** la base de Identidad, **cuando** se inspecciona, **entonces** el secreto del segundo factor está cifrado y los códigos de respaldo solo figuran como resumen. |
| AC-IDE-38 | IDE-41, IDE-42 | **Dado** un encargado que ingresó su contraseña, **cuando** presenta un código TOTP ya usado en su ventana, **entonces** se rechaza; si dos se presentan a la vez, se acepta solo uno; y mientras no presente el segundo factor solo puede configurarlo o presentarlo, y pasados 5 minutos debe reiniciar el ingreso. |
| AC-IDE-39 | IDE-43, IDE-57, IDE-58 | **Dado** un cliente que aceptó la versión 1 y una versión 2 vigente, **cuando** ingresa o renueva su sesión, **entonces** se le pide aceptar la versión 2 y se guarda su versión y fecha; una aceptación que cite otra versión se rechaza; y hasta que acepte puede ver su cuenta y pedidos, pero no iniciar pagos ni hacer cambios sensibles. |
| AC-IDE-40 | IDE-44, IDE-45 | **Dado** un cliente con dos sesiones abiertas, **cuando** cambia su contraseña, **entonces** la otra sesión deja de renovarse; y si un administrador restablece la contraseña de un encargado, todas sus renovaciones quedan revocadas; llega un aviso a su correo sin la contraseña, y si recupera la contraseña con segundo factor activo, sigue debiendo presentar el código. |
| AC-IDE-41 | IDE-46 | **Dado** un encargado que ingresó a las 08:00 y un cliente que ingresó un lunes, **cuando** usan su renovación, **entonces** el encargado queda sin renovación a las 16:00 aunque la haya usado y el cliente al cabo de 7 días, sin que cada uso extienda el plazo. |
| AC-IDE-42 | IDE-59 | **Dado** una contraseña común escrita con otra normalización Unicode, **cuando** se registra, **entonces** se rechaza igual que la original. |
| AC-IDE-43 | IDE-47, IDE-48 | **Dado** una cancelación confirmada, **cuando** se ejecuta, **entonces** la cuenta se desactiva, sus renovaciones y avisos pendientes quedan sin efecto, sus datos personales y los de contacto de sus pedidos se anonimizan y se conservan los pedidos y las hojas exigidos; y quien se registre luego con ese mismo correo obtiene una cuenta nueva sin biblioteca ni historial. |
| AC-IDE-44 | IDE-49 | **Dado** un administrador y el último administrador activo, **cuando** el primero intenta restablecer su propio segundo factor, o desactivar o degradar al último administrador, **entonces** se rechaza. |
| AC-IDE-45 | IDE-51 | **Dado** códigos TOTP de la ventana anterior, la actual, la siguiente y de dos ventanas atrás, **cuando** se presentan, **entonces** los tres primeros se aceptan y el último se rechaza. |
| AC-IDE-46 | IDE-52 | **Dado** un usuario con el segundo factor activo, **cuando** regenera sus códigos de respaldo indicando su contraseña y un código vigente, **entonces** recibe 8 nuevos y los anteriores dejan de servir. |
| AC-IDE-47 | IDE-53 | **Dado** un cliente en un dispositivo reconocido y una región habitual, **cuando** cambia su correo y verifica el nuevo, **entonces** el cambio se aplica, recibe un aviso en la cuenta y en el correo nuevo y no se envía nada al correo anterior. |
| AC-IDE-48 | IDE-54, IDE-55 | **Dado** un intento de cambiar el correo desde un dispositivo sin marca y otra región, **cuando** ocurre, **entonces** no se aplica, los cambios sensibles de ese dispositivo quedan congelados, el correo anterior, el WhatsApp con consentimiento y las sesiones reconocidas reciben una alerta, y el titular puede confirmarlo o rechazarlo desde un dispositivo reconocido en 24 horas; vencido el plazo, se descarta. |
| AC-IDE-49 | IDE-56 | **Dado** un cliente que no recibió el código de verificación, **cuando** pide reenviarlo, **entonces** recibe uno nuevo mientras no supere el límite de solicitudes de código. |
| AC-IDE-50 | IDE-60 | **Dado** un usuario con tres sesiones abiertas, **cuando** pide cerrar todas, **entonces** ninguna de sus renovaciones sigue funcionando. |

## 5. Reglas e invariantes

- La credencial lleva rol y sucursal; la sucursal es obligatoria para encargados y vacía para los demás.
- La credencial de acceso dura 15 minutos; la renovación es opaca, se valida en Identidad, dura 7 días (cliente) u 8 horas (personal), hereda el vencimiento absoluto del ingreso y depende de que el usuario siga activo.
- «Cerrar todas las sesiones» significa revocar las renovaciones (IDE-60); las credenciales de acceso ya emitidas caducan solas en 15 minutos como máximo.
- La cancelación de la cuenta (IDE-28 e IDE-47) cumple el derecho de cancelación del titular; las solicitudes de cancelación de otros datos las atiende la administración (IDE-26).
- Identidad consulta al E-commerce y al Inventario por sus contratos para saber qué pendientes tiene un cliente al cancelar su cuenta; no los lista como dependencia para evitar ciclos entre specs.
- El correo de seguridad no es el Notificador: su falla no se reintenta en cola, y el aviso de cambio de contraseña o de correo nunca bloquea el cambio.
- Un cambio de correo desde un dispositivo o región no reconocidos nunca avisa solo al correo anterior: la alerta va por todos los medios, para no depender de un correo que puede estar comprometido.
- Las contraseñas nunca se guardan ni se registran en texto claro.
- Recuperar la contraseña no desactiva el segundo factor: quien lo tiene activo debe seguir presentando el código al ingresar.
- Los códigos de un solo uso (verificación, recuperación, segundo factor y respaldo) caducan, se invalidan al usarse y no se guardan ni se registran en claro.
- El ingreso y la recuperación no revelan si una cuenta existe. El registro sí puede indicar que el correo ya está en uso; esa enumeración se acota con los límites de `transversal-seguridad`.
- Los límites de ingreso, de solicitudes de códigos y del segundo factor se definen en `transversal-seguridad` (SEG-06, SEG-19, SEG-20) y el de cuentas nuevas por origen (SEG-24); la vigencia y los intentos de cada código, en IDE-19.

## 6. Casos límite y errores

- Un usuario desactivado conserva su historial de operaciones y auditoría.
- Cambiar el rol de un usuario invalida sus credenciales vigentes en la siguiente renovación.
- Un cliente con el correo sin verificar puede armar su carrito y usar su cuenta, pero no iniciar un pago ni recuperar el acceso hasta verificarlo.
- La credencial de lectura del asistente (IDE-33) vence a los 5 minutos y no se renueva: la capa web pide una nueva en cada consulta.
- El número de WhatsApp es opcional; sin él o sin consentimiento, el cliente solo se queda sin avisos.
- Si el envío del correo falla, la verificación o la recuperación no se completan y la persona puede reintentar; ninguna otra operación se interrumpe.
- Un cliente que pierde su dispositivo y sus códigos de respaldo pide a la administración restablecer su segundo factor (IDE-38), tras comprobar su identidad por un medio ajeno al sistema.
- Si el único administrador pierde su contraseña, o su dispositivo y sus códigos de respaldo, la recuperación se hace con acceso a la infraestructura según un procedimiento documentado fuera del sistema.
- Cancelar la cuenta hace perder el acceso a la biblioteca digital; no hay reembolso automático por ello.
- La identidad de un usuario cancelado en los registros de auditoría se anonimiza en la depuración (`transversal-auditoria`).

## 7. Preguntas abiertas

- [x] ¿Cuál es la política mínima de contraseñas? Resuelto el 2026-10-10 y revisado el 2026-10-16: 10 caracteres como mínimo, fuera de listas comunes o filtradas y sin reglas de composición, según la guía NIST 800-63B (IDE-15).
- [x] ¿Cuánto dura la credencial y cuánto la renovación? Resuelto por AD22: 15 minutos y 7 días (IDE-02).
- [x] ¿Cuál es el canal de recuperación? Resuelto el 2026-10-10: correo electrónico con un proveedor gratuito de envío (reemplaza la propuesta de WhatsApp); el personal no usa recuperación propia: la restablece el administrador (IDE-23).
- Nota para el plan: el proveedor gratuito de correo y el dominio remitente no están elegidos (bloqueo B08); no cambia el comportamiento.
- [x] ¿Confirmas 10 minutos de vigencia, 5 intentos por código y 3 solicitudes por hora para los códigos? Resuelto el 2026-10-16 (confirmado por el responsable): sí (IDE-19 y SEG-19).
- [x] ¿Cuál es el plazo para atender una solicitud de acceso, rectificación, cancelación u oposición? Resuelto el 2026-10-16: 20 días hábiles para el acceso y 10 para rectificación, cancelación y oposición (IDE-12); a verificar contra el DS 016-2024-JUS.
- [x] ¿Qué se considera día hábil para los plazos de las solicitudes? Resuelto el 2026-10-16 (confirmado por el responsable): lunes a viernes, sin considerar feriados en esta versión.
- Nota para el plan: las llamadas entre servicios reenvían la credencial del usuario para acciones del usuario y usan la credencial de servicio de IDE-30 para tareas de fondo; el asistente usa la credencial de lectura de IDE-33.
- [x] ¿La contraseña temporal del personal vence a las 24 horas? Resuelto el 2026-10-16 (confirmado por el responsable): sí (IDE-23).
