---
id: ecommerce-libro-reclamaciones
titulo: Libro de Reclamaciones virtual
estado: Draft
aprobado_por: Claude (delegado por Diego Amiquero)
aprobado_en: 2026-11-13
plan_aprobado_por: Claude (delegado por Diego Amiquero)
plan_aprobado_en: 2026-11-18
servicio: E-commerce
prefijo: REC
origen: [RF25, RF27, NN15]
calidad: [RNF07, RNF08, RNF17, RNF23, AC01, AC04]
depende: [identidad-seguridad, transversal-notificaciones, transversal-auditoria, transversal-seguridad]
---

# Spec — Libro de Reclamaciones virtual

## 1. Propósito

Que cualquier consumidor pueda registrar un reclamo o una queja desde la tienda, reciba una constancia y obtenga respuesta, y que la librería conserve las hojas (RF25, confirmado por el equipo). Se aplica con rigurosidad laxa por ser un proyecto académico (`docs/01-analisis-de-sistema/03-marco-legal.md`).

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Enlace permanente, registro de la hoja, constancia imprimible y respuesta. | La forma exacta del formulario oficial (se aproxima y se revisaría con un abogado). |
| Atención por la administración central y aviso de la respuesta por correo. | Atención de reclamos con indemnizaciones o procesos ante la autoridad. |
| Conservación de las hojas y descarte de las que son spam, sin borrarlas. | Gestión de plazos legales ante Indecopi. |
| Límite de hojas por origen (el formato de rechazo es de `transversal-seguridad`). | El Libro de Reclamaciones físico de cada sucursal. |
| Vínculo de cada hoja con la cuenta de su titular, para que cambiar el correo de la cuenta no la pierda (REC-21 a REC-34). | |

## 3. Requisitos (EARS)

**Términos.** *Cliente*: usuario con el rol cliente. *Consumidor*: quien registra o consulta una hoja, con o sin cuenta. *Hoja vinculada*: la que lleva el identificador opaco de la cuenta de su titular. *Hoja no vinculada*: la que no lleva cuenta y no es de una cuenta cancelada ni está anonimizada; son las de un visitante sin sesión, las de un usuario del personal, las registradas antes del vínculo y las desvinculadas por REC-30. *Hoja de una cuenta cancelada*: la marcada por REC-20. *Correo verificado de la cuenta*: el que Identidad informa en ese instante, nunca el de una credencial de sesión.

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| REC-01 | Ubicuo | El sistema deberá mostrar en todas las páginas de la tienda un enlace visible y permanente al Libro de Reclamaciones. | RF25 |
| REC-02 | Ubicuo | El sistema deberá permitir registrar una hoja sin que el consumidor haya iniciado sesión. | RF25 |
| REC-03 | Evento | Cuando el consumidor envía una hoja, el sistema deberá registrar su nombre, documento de identidad, domicilio y correo de contacto, el bien contratado, el monto reclamado, el tipo reclamo o queja, el detalle y, si lo indica, su pedido. | RF25 |
| REC-04 | No deseado | Si falta alguno de los datos obligatorios de la hoja (nombre, documento, domicilio, correo, bien contratado, monto reclamado, tipo y detalle), entonces el sistema deberá rechazarla indicando qué campos faltan. | RF25 |
| REC-05 | Evento | Cuando se registra una hoja, el sistema deberá asignarle un número único y una fecha, y mostrar una constancia, con el plazo de respuesta informado, que el consumidor pueda imprimir sin costo. | RF25 |
| REC-06 | Ubicuo | El sistema deberá conservar cada hoja al menos dos años y no permitir su eliminación. | RF25, RNF08 |
| REC-07 | Evento | Cuando la administración responde una hoja pendiente, el sistema deberá cambiar su estado a respondido, guardar la respuesta y enviarla por correo electrónico al correo de contacto de la hoja, sin incluir el documento ni el domicilio. | RF25 |
| REC-08 | Evento | Cuando la administración responde una hoja, el sistema deberá registrarlo en la auditoría. | RNF08 |
| REC-09 | Ubicuo | El sistema deberá permitir a la administración central listar y filtrar las hojas por estado. | RF25 |
| REC-10 | Ubicuo | El sistema deberá proteger los datos personales de las hojas: el nombre, el documento y el domicilio los ve solo la administración central, y el titular de la hoja (REC-23) ve su número, fecha, tipo, estado y respuesta, con la misma respuesta ante una hoja ajena, inexistente o de una cuenta cancelada. | RNF07 |
| REC-11 | No deseado | Si un mismo origen intenta registrar una cuarta hoja en una ventana deslizante de una hora, entonces el sistema deberá rechazarla y responder y registrar el exceso según `transversal-seguridad` (SEG-07 y SEG-08). | RNF17, AC04 |
| REC-12 | Evento | Cuando la administración central descarta una hoja por ser spam, el sistema deberá marcarla como descartada, ocultarla del listado por defecto, conservarla sin eliminarla y registrarlo en la auditoría. | RF25, RNF08 |
| REC-13 | No deseado | Si falla el envío del correo de respuesta, entonces el sistema deberá mantener la hoja como respondida con la respuesta guardada, marcar el aviso como pendiente y permitir a la administración reintentarlo. | RF25 |
| REC-14 | Evento | Cuando un cliente con sesión consulta sus hojas, ya sea la lista o una de ellas por su número, el sistema deberá mostrar solo las hojas de las que es titular (REC-23), con su número, fecha, estado y respuesta. | RF25, RNF07 |
| REC-15 | Evento | Cuando la administración central consulta los datos personales de una hoja, el sistema deberá registrarlo en la auditoría. | RNF08, AC04 |
| REC-16 | Ubicuo | El sistema deberá gestionar los estados pendiente, respondido y descartado de una hoja, permitir descartar solo una hoja pendiente y rechazar responder una hoja respondida o descartada, aunque dos respuestas lleguen a la vez. | RF25, AC01 |
| REC-17 | Evento | Cuando una hoja cumple dos años desde su registro, el sistema deberá anonimizar sus datos personales y redactar los que haya en el detalle y la respuesta, desvincularla de su cuenta, conservar el resto de la hoja, y registrar el proceso en la auditoría. | RF25, RNF07, RNF08 |
| REC-18 | Ubicuo | El sistema deberá permitir a quien registró una hoja sin sesión ver su constancia en el momento del registro, y no mostrarle los datos de otra hoja por su número. | RF25, RNF07 |
| REC-19 | Evento | Cuando Identidad consulta los pendientes de una cuenta con correo verificado, el sistema deberá informar cuántas hojas pendientes tiene como titular (REC-23), sin datos de las hojas y sin contar las descartadas ni las de una cuenta cancelada; una cuenta sin correo verificado cuenta cero. | RF25, RF27 |
| REC-20 | Evento | Cuando se ejecuta la cancelación de una cuenta, el sistema deberá, en la misma operación, marcar como de una cuenta cancelada y desvincular las hojas de las que la cuenta es titular (REC-23) y conservarlas hasta los dos años de REC-17. | RF25, RF27, RNF23 |
| REC-21 | Evento | Cuando un cliente con sesión registra una hoja, el sistema deberá vincular solo esa hoja a la cuenta de su sesión (nunca a un valor del formulario), conservando el correo de contacto que escribió, que puede ser otro y es el destino de la respuesta (REC-07); un visitante o un usuario del personal no vincula. | RF25, RNF07 |
| REC-22 | Evento | Cuando un cliente con sesión y correo verificado consulta sus hojas (REC-14), o Identidad consulta los pendientes de su cuenta, el sistema deberá vincular a esa cuenta, en la misma operación, las hojas no vinculadas cuyo correo de contacto es el correo verificado de la cuenta. | RF25, RNF07 |
| REC-23 | Ubicuo | El sistema deberá considerar titular de una hoja, salvo lo que dice REC-31, al cliente a cuya cuenta está vinculada aunque su correo no esté verificado y, si no está vinculada, al cliente cuyo correo verificado de la cuenta es el correo de contacto de la hoja. | RNF07 |
| REC-24 | No deseado | Si no puede marcar y desvincular las hojas de una cuenta que se cancela, o no puede leer su correo verificado, entonces el sistema deberá informarlo para que no se ejecute la cancelación (IDE-70). | RF27, RNF23 |
| REC-25 | Evento | Cuando Identidad aplica el cambio del correo verificado de una cuenta de cliente, el sistema deberá vincular a esa cuenta, antes de que se libere el correo anterior, las hojas no vinculadas cuyo correo de contacto es el anterior, y si no puede hacerlo, informarlo para que no se aplique el cambio. | RF25, RNF07 |
| REC-26 | Evento | Cuando se ejecuta la vinculación inicial de las hojas existentes, que recorre una sola vez todos los correos de contacto de las hojas no vinculadas, el sistema deberá vincular las hojas cuyo correo de contacto es el correo verificado de una cuenta de cliente, sin modificar sus datos, y repetirla no deberá cambiar nada. | RF25, RNF07 |
| REC-27 | No deseado | Si no puede vincular las hojas que exige REC-22 o no puede leer el correo de la cuenta en Identidad (un error, que no es lo mismo que una cuenta sin correo verificado), entonces el sistema deberá fallar la consulta (la lista o la hoja de la cuenta, el conteo de pendientes de Identidad) sin devolver un resultado parcial ni una lista vacía. | RF27, RNF07 |
| REC-28 | No deseado | Si dos operaciones intentan vincular a la vez la misma hoja, entonces el sistema deberá vincularla a una sola, sin fallar la otra operación, y nunca deberá vincular a otra cuenta una hoja ya vinculada ni vincular una hoja de una cuenta cancelada o anonimizada. | RNF07, AC01 |
| REC-29 | Evento | Cuando el sistema vincula una o más hojas a una cuenta, el sistema deberá registrarlo en la auditoría en la misma operación, con la cuenta como identificador opaco, el disparador (registro, consulta, pendientes, cambio de correo o vinculación inicial) y el conteo, sin el correo ni el número de ninguna hoja, y no vincular si no es posible registrarlo, salvo en el registro con sesión, donde se aplica REC-33. | RNF08, RNF23 |
| REC-30 | Evento | Cuando Identidad reemplaza una cuenta de cliente sin correo verificado (IDE-84), el sistema deberá desvincular las hojas vinculadas a ella, que quedan como hojas no vinculadas, y si no puede hacerlo, informarlo para que no se ejecute el reemplazo. | RF25, RNF07 |
| REC-31 | Ubicuo | El sistema no deberá considerar titular a ningún consumidor de una hoja de una cuenta cancelada ni de una hoja anonimizada. | RNF07, RNF23 |
| REC-32 | Ubicuo | El sistema no deberá cambiar a los titulares de las hojas vinculadas por un cambio del correo de una cuenta. | RNF07 |
| REC-34 | No deseado | Si Identidad o la auditoría fallan durante la vinculación inicial, entonces el sistema no deberá vincular esa hoja y deberá continuar en la siguiente ejecución hasta completar el recorrido. | RNF07, RNF23 |
| REC-33 | No deseado | Si el sistema no puede registrar la auditoría de la vinculación de REC-21 al registrar una hoja con sesión, entonces el sistema deberá rechazar ese registro como servicio no disponible, sin guardar la hoja, indicando que puede registrarla sin sesión (REC-02). | RNF08, RNF23 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-REC-01 | REC-01 | **Dado** cualquier página de la tienda, **cuando** se muestra, **entonces** el pie incluye el enlace al Libro de Reclamaciones. |
| AC-REC-02 | REC-02, REC-03 | **Dado** un visitante sin sesión, **cuando** completa la hoja con su nombre, documento, domicilio, correo, el bien, el monto reclamado y el detalle, **entonces** la hoja queda registrada. |
| AC-REC-03 | REC-04 | **Dado** una hoja sin detalle, o sin documento o domicilio, **cuando** se envía, **entonces** se rechaza indicando cada campo que falta. |
| AC-REC-04 | REC-05 | **Dado** una hoja válida, **cuando** se registra, **entonces** recibe un número único y una fecha, y se muestra una constancia imprimible que indica el plazo de respuesta de 15 días calendario. |
| AC-REC-05 | REC-06 | **Dado** una hoja registrada, **cuando** alguien intenta eliminarla, **entonces** se rechaza y la hoja sigue disponible. |
| AC-REC-06 | REC-07, REC-08 | **Dado** una hoja pendiente, **cuando** la administración la responde, **entonces** pasa a respondido, el consumidor recibe la respuesta en su correo y queda la auditoría. |
| AC-REC-07 | REC-09 | **Dado** hojas pendientes y respondidas, **cuando** la administración filtra por pendientes, **entonces** ve solo esas. |
| AC-REC-08 | REC-10, REC-23 | **Dado** una hoja con datos personales, **cuando** otro cliente intenta consultarla, **entonces** se rechaza con la misma respuesta que ante una hoja inexistente o de una cuenta cancelada; un visitante sin sesión debe iniciar sesión y un encargado de sucursal o de logística recibe el rechazo por rol; y su titular sí la ve, sin el documento ni el domicilio. |
| AC-REC-09 | REC-11 | **Dado** un origen que registró 3 hojas en la última hora, **cuando** envía una cuarta, **entonces** se rechaza con la respuesta de límite excedido y su espera sugerida, queda un evento de seguridad y las 3 hojas anteriores siguen registradas. |
| AC-REC-10 | REC-12 | **Dado** una hoja de spam, **cuando** la administración la descarta, **entonces** su estado es descartada, no aparece en el listado por defecto pero sí al filtrar por descartadas, no se puede eliminar y queda una entrada de auditoría. |
| AC-REC-11 | REC-13 | **Dado** que el envío de correo falla, **cuando** la administración responde una hoja, **entonces** la hoja queda respondida con su respuesta guardada, el aviso figura pendiente y al reintentar con el envío disponible el consumidor recibe el correo una sola vez. |
| AC-REC-12 | REC-14, REC-23 | **Dado** un cliente con el correo verificado que dejó dos hojas con ese correo, **cuando** consulta sus hojas, **entonces** ve esas dos con su estado y respuesta, y ninguna de otro correo ni de otra cuenta. |
| AC-REC-13 | REC-15 | **Dado** una hoja con datos personales, **cuando** un administrador central la abre, **entonces** queda una entrada de auditoría con el usuario y el número de hoja. |
| AC-REC-14 | REC-07, REC-16 | **Dado** una hoja respondida y otra descartada, **cuando** la administración intenta responderlas, **entonces** se rechaza por estado inválido; y el correo de respuesta no incluye el documento ni el domicilio. |
| AC-REC-28 | REC-16 | **Dado** una hoja pendiente, **cuando** la administración envía dos respuestas a la vez, o responde y descarta a la vez, **entonces** gana una sola, la otra se rechaza por estado inválido y solo queda una entrada de auditoría; y descartar una hoja que no está pendiente se rechaza. |
| AC-REC-15 | REC-17 | **Dado** una hoja vinculada que cumplió dos años, **cuando** corre la revisión de conservación, **entonces** su nombre, documento, domicilio y correo quedan anonimizados y el número, la fecha, el tipo y el estado se conservan; los datos personales escritos en el detalle y la respuesta se redactan; la hoja queda sin cuenta y no aparece en las hojas de su titular ni en el conteo de pendientes; y el proceso queda en la auditoría. |
| AC-REC-16 | REC-18, REC-10 | **Dado** un visitante que acaba de registrar una hoja, **cuando** consulta otra por su número, **entonces** se rechaza; y un encargado de sucursal o logística tampoco puede ver los datos de ninguna hoja. |
| AC-REC-17 | REC-19 | **Dado** un consumidor con una hoja pendiente, otro con solo hojas respondidas y otro con una hoja descartada como spam, todos con correo verificado, **cuando** Identidad consulta sus pendientes, **entonces** el primero figura con una hoja y los otros dos con ninguna, sin datos de las hojas; y una cuenta sin correo verificado con una hoja pendiente vinculada figura con cero. |
| AC-REC-18 | REC-20, REC-24, REC-31, REC-17 | **Dado** un consumidor con una hoja respondida de hace un año que cancela su cuenta, **cuando** se ejecuta la cancelación, **entonces** la hoja queda marcada como de una cuenta cancelada y sin cuenta, la administración central la sigue viendo con sus datos, otra persona que se registre después con el mismo correo no la ve ni en su lista ni consultándola por su número, y a los dos años queda anonimizada; y si no se pueden marcar las hojas, o no se puede leer el correo verificado de la cuenta, la cancelación no se ejecuta. |
| AC-REC-31 | REC-20, REC-23 | **Dado** una cuenta sin correo verificado con el correo V, una hoja vinculada a ella y hojas no vinculadas de un tercero con el correo V, **cuando** se cancela la cuenta, **entonces** solo se marca la hoja vinculada y las hojas del tercero siguen visibles para quien verifique V. |
| AC-REC-19 | REC-20, REC-32, REC-14 | **Dado** una cuenta que cambió su correo y tiene una hoja vinculada pendiente y otra de visitante nunca consultada del correo verificado actual, **cuando** se cancela la cuenta, **entonces** las dos quedan marcadas como de una cuenta cancelada y sin cuenta, y ninguna persona con el correo anterior o el actual las ve. |
| AC-REC-20 | REC-21, REC-23, REC-07 | **Dado** un cliente con sesión, aun con el correo sin verificar, que registra una hoja con un correo de contacto distinto del de su cuenta, **cuando** consulta sus hojas, **entonces** la ve; la respuesta de la administración sale al correo de contacto; y otro cliente con ese correo de contacto verificado no la ve en su lista ni por su número. |
| AC-REC-29 | REC-21 | **Dado** un cliente con sesión que envía una hoja con un identificador de cuenta ajeno en el formulario, o un visitante o un usuario del personal que registra una hoja, **cuando** se registra, **entonces** el identificador del formulario se ignora y la hoja de un visitante o del personal no queda vinculada a ninguna cuenta. |
| AC-REC-21 | REC-22, REC-14, REC-23 | **Dado** una hoja de un visitante con el correo X y un cliente con X verificado, **cuando** consulta sus hojas por la lista o por el número de esa hoja, **entonces** la ve y queda vinculada a su cuenta; y un cliente con X sin verificar, o con otro correo, un usuario del personal con X verificado, o una hoja de una cuenta cancelada o anonimizada, ni la ven ni se vinculan. |
| AC-REC-22 | REC-22, REC-19, REC-27 | **Dado** una hoja pendiente no vinculada del correo verificado de una cuenta, **cuando** Identidad consulta sus pendientes, **entonces** la hoja queda vinculada y se cuenta; y si no se puede vincular, o no se puede leer el correo de la cuenta en Identidad, la consulta falla sin devolver un conteo parcial ni una lista vacía, y lo mismo ocurre con la lista de la cuenta. |
| AC-REC-23 | REC-25, REC-32, REC-14 | **Dado** una hoja no vinculada con el correo X, una hoja de una cuenta cancelada con ese correo y una cuenta con X verificado que cambia su correo a Y, **cuando** Identidad aplica el cambio, **entonces** solo la hoja no vinculada queda vinculada a la cuenta antes de liberar X; otra persona que verifique X después no la ve, y la titular la sigue viendo y contando; y si no se puede vincular, el cambio no se aplica. |
| AC-REC-24 | REC-26, REC-34 | **Dado** hojas existentes de correos que son el correo verificado de dos cuentas de cliente, de un correo sin cuenta, de un correo sin verificar, de un usuario del personal, de una cuenta cancelada y una anonimizada, **cuando** se ejecuta la vinculación inicial, **entonces** solo las de las dos cuentas de cliente quedan vinculadas, ningún dato de las hojas cambia y ejecutarla de nuevo no cambia nada; y si Identidad o la auditoría fallan, no se vincula esa hoja y se reintenta en la siguiente ejecución. |
| AC-REC-25 | REC-28 | **Dado** una hoja no vinculada del correo de una cuenta, **cuando** la lista y el conteo de pendientes de esa cuenta la vinculan a la vez, o dos cuentas la reclaman a la vez durante un cambio de correo (en la prueba, invocando el reclamo directamente), **entonces** queda vinculada a una sola, ninguna operación falla, y una hoja ya vinculada, de una cuenta cancelada o anonimizada nunca pasa a otra cuenta. |
| AC-REC-26 | REC-29 | **Dado** una vinculación por el registro con sesión, por la consulta, por el conteo de pendientes, por el cambio de correo o por la vinculación inicial, **cuando** se hace, **entonces** queda en la misma operación una entrada de auditoría con la cuenta como identificador opaco, el disparador y el conteo, sin el correo ni el número de las hojas; y si la auditoría no puede registrar una vinculación, esa vinculación no se hace. |
| AC-REC-30 | REC-33 | **Dado** un cliente con sesión y la auditoría no disponible, **cuando** envía una hoja, **entonces** se rechaza como servicio no disponible sin guardar la hoja, el mensaje indica que puede registrarla sin sesión, y el mismo envío sin sesión se registra. |
| AC-REC-27 | REC-30, REC-19 | **Dado** una cuenta de cliente sin correo verificado con una hoja vinculada, **cuando** Identidad la reemplaza por otra (IDE-84), **entonces** la hoja queda sin cuenta, asociada a su correo de contacto, y la cuenta nueva no es su titular mientras no tenga el correo verificado; y si no se puede desvincular, el reemplazo no se ejecuta. |

## 5. Reglas e invariantes

- Una hoja es inmutable una vez registrada, salvo el vínculo con la cuenta (REC-21, REC-22, REC-25, REC-26, REC-30, REC-17, REC-20); la respuesta se agrega sin alterar el contenido original.
- El número de hoja es único y secuencial por año.
- Se conserva al menos dos años.
- El plazo de respuesta que se informa es de 15 días calendario, dentro del máximo legal (a verificar en una operación real, ver `docs/01-analisis-de-sistema/03-marco-legal.md`).
- Los datos de identificación de la hoja (documento y domicilio) se piden por obligación legal; son una excepción documentada a la minimización de `docs/05-seguridad/04-privacidad-y-datos-personales.md`.
- Una hoja descartada se conserva como las demás y sus datos personales se anonimizan al cumplirse el plazo de conservación.
- REC-19 cuenta todas las hojas pendientes de la cuenta como titular, aunque cualquiera pueda registrar una hoja con el correo de otra persona (REC-02): es un riesgo aceptado (decisión del 2026-11-13) que un tercero difiera una cancelación hasta el tope de 30 días de IDE-81; una hoja descartada como spam sale del conteo (REC-12) y, desde el vínculo, cambiar el correo ya no saca a la persona de esa hoja. Una cuenta sin correo verificado cuenta cero para que quien cree una cuenta con el correo de otra persona y registre una hoja no bloquee el reemplazo de IDE-84; su cancelación igualmente marca sus hojas vinculadas (REC-20).
- **Vínculo con la cuenta (decisión de la persona del 2026-11-21, DD-26).** Una hoja se asocia a su titular por el identificador opaco de su cuenta y no por el correo, para que cambiar el correo de la cuenta (`identidad-seguridad`: IDE-53 y el cambio de correo de todo usuario) no pierda el acceso a las hojas, no deje de contarlas al cancelar y no las exponga a quien registre después el correo anterior. Una hoja ya vinculada nunca cambia de cuenta; solo el rol cliente vincula; las hojas de un visitante o del personal siguen asociadas por su correo de contacto hasta que una cuenta con ese correo verificado las reclama (REC-22), se vinculan al cambiar el correo (REC-25) o con la vinculación inicial (REC-26). El identificador de la cuenta de una hoja no se muestra en ninguna pantalla ni respuesta: solo viaja en la auditoría opaca de REC-29. Cómo se guarda y se asigna (condicional y atómica) lo fija el plan.
- Dependencias de otros módulos (aplicadas por sus dueños y reaprobadas antes que esta spec): de Identidad, el aviso del cambio de correo y del reemplazo de una cuenta sin verificar (IDE-99, con IDE-53, IDE-84 e IDE-88: el cambio o el reemplazo fallan si el módulo no puede aplicarlo) al módulo, una excepción de IDE-92 que le permita recibir el correo anterior solo para vincular, y la consulta de la cuenta de cliente de un correo verificado para la vinculación inicial; de Auditoría, la acción de la vinculación (AUD-46) y su tratamiento como operación que concede acceso (AUD-04). El módulo lee el correo verificado de la cuenta de Identidad antes de que Identidad anonimice la cuenta (IDE-47).
- Consultar la lista de hojas y los pendientes de una cuenta **escriben** (REC-22): la primera vez vinculan y repetirlas no cambia el resultado.
- Riesgos residuales (conocidos por la persona al decidir DD-26): una hoja registrada con sesión, o reclamada por una operación concurrente, durante o justo después de la cancelación o de un reemplazo (IDE-84) puede quedar vinculada a una cuenta que ya no existe sin la marca de REC-20 hasta REC-17 (nadie la ve ni la cuenta, la administración la conserva); una hoja nueva con un correo X que entra entre el vínculo de REC-25 y la liberación de X, un correo que cambia y se libera entre su lectura y el reclamo, y las hojas de un visitante que nadie reclama antes de que se libere su correo, quedan visibles para quien verifique ese correo después; y las hojas de las cuentas que cambiaron de correo antes de esta vinculación, y las que registró un usuario del personal, quedan visibles para quien verifique su correo; en todos esos casos lo visible es el número, la fecha, el tipo, el estado y la respuesta de la administración (texto libre que puede traer datos personales), nunca el nombre, el documento ni el domicilio, y las primeras no pueden recuperarse.

## 6. Casos límite y errores

- Una hoja con un número de pedido que no existe se acepta; el pedido es un dato opcional.
- Dos envíos idénticos seguidos generan hojas separadas, pues no se puede distinguir una repetición de un nuevo reclamo.
- El correo de contacto es obligatorio: sin él la hoja se rechaza (REC-04).
- Quien registró la hoja sin sesión conserva su constancia (REC-18); el número no basta para ver los datos de otra persona.
- El «origen» de REC-11 es el definido en `transversal-seguridad` (SEG-17).
- Un correo mal escrito impide recibir la respuesta; la hoja y la respuesta quedan registradas y la administración puede reenviarla (REC-13). Si el titular cambia el correo de su cuenta, la respuesta de una hoja sigue saliendo al correo de contacto con el que se escribió (REC-21), que puede haber quedado libre. Un correo de contacto mal escrito que pertenece a un tercero con ese correo verificado le da acceso a la hoja si no está vinculada, pero solo a su número, fecha, tipo, estado y respuesta (el nombre, el documento y el domicilio los ve solo la administración central): es el riesgo de REC-02 y se acota con la vinculación y el límite de REC-11.
- Una cuenta con el correo sin verificar ve las hojas que registró con sesión (vinculadas) pero no las de visitantes de su correo: esas solo se reclaman con el correo verificado (REC-22).
- Una cuenta de cliente vinculada a hojas que pasa a ser del personal conserva el vínculo, pero deja de ver sus hojas como consumidor (REC-14 es del cliente) y la administración central las sigue viendo.

## 7. Preguntas abiertas

- [x] ¿Cuál es el plazo de respuesta que se informa al consumidor? Resuelto el 2026-10-16: 15 días calendario (REC-05).
- [x] ¿Cuál es el medio de contacto obligatorio? Resuelto el 2026-10-16: correo electrónico; la respuesta sale por correo (REC-07, RC18 ampliada).
- [x] ¿Cómo se evita el spam si las hojas no se pueden borrar? Resuelto el 2026-10-16: límite de 3 hojas por hora por origen y descarte sin borrado (REC-11, REC-12).
- [x] ¿Cómo se asocia una hoja a su titular cuando el correo puede cambiar? Resuelto el 2026-11-21 (DD-26, la persona): por la cuenta, con vinculación inicial y reclamo de las hojas de visitantes al consultar, contar o cambiar el correo (REC-21 a REC-34); solo el rol cliente vincula; quien registró con sesión ve su hoja aunque su correo no esté verificado y reclamar exige correo verificado; cada vinculación se audita.
- Nota de verificación (B09): los campos de la hoja (documento, domicilio y monto reclamado) se contrastan con el formato oficial vigente antes de operar con clientes reales; no cambia el comportamiento de la spec.
