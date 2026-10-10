---
id: identidad-seguridad
titulo: Identidad, roles y control de acceso
estado: Draft
servicio: Identidad
prefijo: IDE
origen: [RF18, RF19, RF26, RF27, NN09, NN15, NN18]
calidad: [AC05, AC11, AC13, RNF05, RNF06, RNF07, RNF08, RNF15, RNF18, RNF23]
depende: [transversal-auditoria]
---

# Spec — Identidad, roles y control de acceso

## 1. Propósito

Autenticar a los usuarios y emitir credenciales que indiquen su rol y, cuando corresponde, su sucursal, para que cada servicio aplique el alcance correcto. Incluye la administración de usuarios, la recuperación del acceso, el segundo factor del administrador central y la gestión de la cuenta y de los derechos del cliente sobre sus datos.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Registro de clientes, verificación de su número de WhatsApp, inicio de sesión, renovación y cierre de sesión. | Verificación del correo por un proveedor externo (mejora posterior). |
| Política de contraseñas, bloqueo temporal y recuperación del acceso con código de un solo uso. | Recuperación por correo (el canal es WhatsApp). |
| Segundo factor (TOTP) del administrador central. | Segundo factor para los demás roles (mejora posterior). |
| Roles, sucursal del encargado y desactivación de usuarios. | Inicio de sesión con redes sociales. |
| Gestión de la cuenta, consentimiento de WhatsApp y derechos sobre datos personales (acceso, rectificación, cancelación y oposición). | La comprobación del alcance en cada caso de uso (vive en cada spec). |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| IDE-01 | Evento | Cuando una persona se registra como cliente, el sistema deberá crear su cuenta con nombre, correo único, contraseña y número de WhatsApp obligatorio, y registrar aparte si consiente recibir avisos. | RF19, RF26, NN15 |
| IDE-02 | Evento | Cuando un usuario activo ingresa credenciales correctas, el sistema deberá emitir una credencial de vigencia corta que incluya su rol y, si es encargado, su sucursal. | RF18 |
| IDE-03 | No deseado | Si las credenciales son incorrectas, entonces el sistema deberá rechazar el ingreso sin indicar cuál dato falló y bloquear temporalmente el ingreso tras 5 intentos fallidos en 15 minutos. | RF18, RNF15, AC05, AC13 |
| IDE-04 | Ubicuo | El sistema deberá reconocer los roles cliente, encargado de sucursal, personal de logística y administrador central. | RF19 |
| IDE-05 | Ubicuo | El sistema deberá exigir que un encargado de sucursal tenga exactamente una sucursal y que los demás roles no tengan ninguna. | RF19 |
| IDE-06 | No deseado | Si se intenta asignar un segundo encargado activo a una sucursal, entonces el sistema deberá rechazar la asignación indicando quién la tiene. | RF19 |
| IDE-07 | Evento | Cuando el administrador central crea, actualiza o desactiva un usuario, el sistema deberá aplicar el cambio y registrarlo en la auditoría. | RF19, RNF08 |
| IDE-08 | Estado | Mientras un usuario esté desactivado, el sistema deberá impedir su ingreso y su renovación de sesión. | RF19 |
| IDE-09 | Ubicuo | El sistema deberá almacenar las contraseñas protegidas con un algoritmo de resumen robusto y nunca en texto claro. | RNF06, AC05 |
| IDE-10 | Ubicuo | El sistema deberá permitir a los demás servicios validar la firma y la vigencia de una credencial sin consultar a Identidad en cada solicitud. | RF18 |
| IDE-11 | Evento | Cuando el cliente cambia su consentimiento para WhatsApp, el sistema deberá guardar la decisión con su fecha y aplicarla a los avisos siguientes. | NN15, RNF07 |
| IDE-12 | Evento | Cuando un cliente solicita acceder a sus datos, rectificarlos, cancelarlos u oponerse a un tratamiento, el sistema deberá registrar la solicitud y su estado. | RNF07, NN15 |
| IDE-13 | No deseado | Si se registra una cuenta con un correo que ya existe, entonces el sistema deberá rechazar el registro sin revelar datos de la cuenta existente. | RF19 |
| IDE-14 | Evento | Cuando el usuario cierra sesión, el sistema deberá invalidar su renovación de sesión. | RF18 |
| IDE-15 | Ubicuo | El sistema deberá exigir una contraseña de al menos 8 caracteres con mayúscula, minúscula, número y símbolo, que no figure en una lista de contraseñas comunes, al registrarse, al cambiarla y al recuperarla. | RNF15, AC05 |
| IDE-16 | Evento | Cuando una persona se registra, el sistema deberá enviar a su número de WhatsApp un código de un solo uso y marcar el número como verificado al recibir el código correcto. | RF26, RNF15 |
| IDE-17 | Evento | Cuando un usuario solicita recuperar su acceso, el sistema deberá enviar un código de un solo uso y vigencia corta al número verificado de la cuenta, y responder con el mismo mensaje exista o no la cuenta. | RF26, RNF15, AC13 |
| IDE-18 | Evento | Cuando el usuario ingresa el código de recuperación correcto antes de que venza, el sistema deberá permitirle definir una nueva contraseña, invalidar el código y cerrar todas sus sesiones vigentes. | RF26, RNF15 |
| IDE-19 | No deseado | Si el código de recuperación es incorrecto, venció o ya se usó, o si se superan los intentos o las solicitudes permitidas, entonces el sistema deberá rechazar la operación con un mensaje genérico y registrar el evento. | RF26, RNF15, AC13 |
| IDE-20 | Evento | Cuando el administrador central ingresa credenciales correctas, el sistema deberá exigir además un código de un segundo factor de un solo uso y no emitir la credencial sin él. | RNF15, AC05 |
| IDE-21 | Evento | Cuando un cliente consulta su cuenta, el sistema deberá mostrarle sus datos personales, sus consentimientos y el estado de sus solicitudes de acceso, rectificación, cancelación y oposición, y permitirle corregir sus datos y cambiar su contraseña. | RF27, NN15 |
| IDE-22 | Evento | Cuando un cliente cancela su cuenta, el sistema deberá desactivarla y suprimir o anonimizar sus datos personales, conservando solo lo que una obligación legal exija. | RF27, RNF23, AC11 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-IDE-01 | IDE-01, IDE-13 | **Dado** un correo nuevo, **cuando** una persona se registra con consentimiento, **entonces** se crea la cuenta de cliente; y si el correo ya existe, se rechaza sin revelar datos. |
| AC-IDE-02 | IDE-02, IDE-04 | **Dado** un encargado de Ayacucho con credenciales correctas, **cuando** ingresa, **entonces** recibe una credencial de vigencia corta con el rol encargado y la sucursal Ayacucho. |
| AC-IDE-03 | IDE-03 | **Dado** una contraseña incorrecta, **cuando** se intenta ingresar cinco veces en 15 minutos, **entonces** se rechaza cada vez con el mismo mensaje y el ingreso queda bloqueado temporalmente. |
| AC-IDE-04 | IDE-05 | **Dado** un usuario con el rol encargado, **cuando** el administrador intenta guardarlo sin sucursal o con dos, **entonces** se rechaza. |
| AC-IDE-05 | IDE-06 | **Dado** una sucursal con un encargado activo, **cuando** se asigna a otro, **entonces** se rechaza indicando quién la tiene. |
| AC-IDE-06 | IDE-07 | **Dado** un administrador, **cuando** desactiva a un usuario, **entonces** el cambio se aplica y queda una entrada de auditoría. |
| AC-IDE-07 | IDE-08 | **Dado** un usuario desactivado, **cuando** intenta ingresar o renovar su sesión, **entonces** se rechaza. |
| AC-IDE-08 | IDE-09 | **Dado** la base de usuarios, **cuando** se inspecciona, **entonces** ninguna contraseña aparece en texto claro. |
| AC-IDE-09 | IDE-10 | **Dado** una credencial válida, **cuando** otro servicio la recibe, **entonces** valida su firma y vigencia sin llamar a Identidad. |
| AC-IDE-10 | IDE-11 | **Dado** un cliente con consentimiento activo, **cuando** lo revoca, **entonces** se guarda la fecha y los avisos siguientes no se envían. |
| AC-IDE-11 | IDE-12 | **Dado** un cliente, **cuando** solicita acceder a sus datos, **entonces** la solicitud queda registrada con su estado. |
| AC-IDE-12 | IDE-14 | **Dado** una sesión activa, **cuando** el usuario cierra sesión, **entonces** su renovación deja de funcionar. |
| AC-IDE-13 | IDE-15 | **Dado** el registro de un cliente, **cuando** la contraseña tiene menos de 8 caracteres, no mezcla mayúscula, minúscula, número y símbolo, o es una contraseña común, **entonces** se rechaza indicando qué falta. |
| AC-IDE-14 | IDE-16 | **Dado** un cliente recién registrado, **cuando** ingresa el código recibido por WhatsApp, **entonces** su número queda verificado; y si el código es incorrecto, no se verifica. |
| AC-IDE-15 | IDE-17, IDE-18 | **Dado** un cliente con número verificado, **cuando** solicita recuperar el acceso e ingresa el código correcto a tiempo, **entonces** define una nueva contraseña, el código deja de servir y sus sesiones anteriores se cierran. |
| AC-IDE-16 | IDE-17 | **Dado** un correo que no existe, **cuando** alguien solicita recuperar el acceso, **entonces** recibe exactamente la misma respuesta que para una cuenta existente. |
| AC-IDE-17 | IDE-19 | **Dado** un código de recuperación vencido o ya usado, o más intentos de los permitidos, **cuando** se ingresa, **entonces** se rechaza con un mensaje genérico y se registra el evento. |
| AC-IDE-18 | IDE-20 | **Dado** un administrador central con credenciales correctas, **cuando** no presenta el segundo factor, **entonces** no recibe credencial; y con el código correcto, sí. |
| AC-IDE-19 | IDE-21 | **Dado** un cliente con sesión, **cuando** abre su cuenta, **entonces** ve sus datos, consentimientos y solicitudes, y puede corregir sus datos. |
| AC-IDE-20 | IDE-22 | **Dado** un cliente con pedidos, **cuando** cancela su cuenta, **entonces** la cuenta queda desactivada, sus datos personales se suprimen o anonimizan y se conserva solo lo exigido por una obligación legal. |

## 5. Reglas e invariantes

- La credencial lleva rol y sucursal; la sucursal es obligatoria para encargados y vacía para los demás.
- La vigencia de la credencial es corta; la renovación depende de que el usuario siga activo.
- Las contraseñas nunca se guardan ni se registran en texto claro.
- Los códigos de un solo uso (verificación, recuperación y segundo factor) caducan, se invalidan al usarse y no se guardan ni se registran en claro.
- Las respuestas de ingreso, registro y recuperación no revelan si una cuenta existe.
- Las tablas de Identidad viven en su propio esquema, con permisos exclusivos (AD18).

## 6. Casos límite y errores

- Un usuario desactivado conserva su historial de operaciones y auditoría.
- Cambiar el rol de un usuario invalida sus credenciales vigentes en la siguiente renovación.
- Un cliente que no consiente los avisos puede comprar; el número se usa entonces solo para verificar y recuperar la cuenta.
- Si el envío del código por WhatsApp falla, la recuperación no se completa y la persona puede reintentar; ninguna otra operación se interrumpe.
- Las solicitudes de acceso, rectificación, cancelación y oposición se conservan según el plazo de retención de la auditoría (RNF23).

## 7. Preguntas abiertas

- [x] ¿Cuál es la política mínima de contraseñas? Resuelto el 2026-10-10: 8 caracteres como mínimo con mayúscula, minúscula, número y símbolo, y fuera de listas comunes (IDE-15).
- [x] ¿Cuánto dura la credencial y cuánto la renovación? Resuelto por AD22: 15 minutos y 7 días.
- [ ] ¿Las llamadas entre servicios reenvían la credencial del usuario o usan una credencial de servicio? Propuesta: reenviar para acciones del usuario y usar credencial de servicio para tareas de fondo. Se decide en el plan.
- [ ] ¿Cuánto dura el código de recuperación y cuántos intentos y solicitudes se permiten? Propuesta: 10 minutos, 5 intentos y 3 solicitudes por hora. Responde: responsable del proyecto.
- [ ] ¿Qué datos se conservan al cancelar una cuenta? Propuesta: solo los pedidos y hojas de reclamación exigidos por ley, sin datos de contacto. Responde: responsable del proyecto.
