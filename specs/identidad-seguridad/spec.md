---
id: identidad-seguridad
titulo: Identidad, roles y control de acceso
estado: Draft
servicio: Identidad
prefijo: IDE
origen: [RF18, RF19, NN09, NN15]
calidad: [AC05, RNF05, RNF06, RNF07, RNF08]
depende: [transversal-auditoria]
---

# Spec — Identidad, roles y control de acceso

## 1. Propósito

Autenticar a los usuarios y emitir credenciales que indiquen su rol y, cuando corresponde, su sucursal, para que cada servicio aplique el alcance correcto. Incluye la administración de usuarios y los derechos del cliente sobre sus datos.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Registro de clientes, inicio de sesión, renovación y cierre de sesión. | Verificación del correo por un proveedor externo (mejora posterior). |
| Roles, sucursal del encargado y desactivación de usuarios. | Inicio de sesión con redes sociales. |
| Consentimiento de WhatsApp y derechos sobre datos personales. | La comprobación del alcance en cada caso de uso (vive en cada spec). |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| IDE-01 | Evento | Cuando una persona se registra como cliente, el sistema deberá crear su cuenta con nombre, correo único y contraseña, y pedir su número de WhatsApp con su consentimiento. | RF19, NN15 |
| IDE-02 | Evento | Cuando un usuario activo ingresa credenciales correctas, el sistema deberá emitir una credencial de vigencia corta que incluya su rol y, si es encargado, su sucursal. | RF18 |
| IDE-03 | No deseado | Si las credenciales son incorrectas, entonces el sistema deberá rechazar el ingreso sin indicar cuál dato falló y limitar los intentos repetidos. | RF18, AC05 |
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

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-IDE-01 | IDE-01, IDE-13 | **Dado** un correo nuevo, **cuando** una persona se registra con consentimiento, **entonces** se crea la cuenta de cliente; y si el correo ya existe, se rechaza sin revelar datos. |
| AC-IDE-02 | IDE-02, IDE-04 | **Dado** un encargado de Ayacucho con credenciales correctas, **cuando** ingresa, **entonces** recibe una credencial de vigencia corta con el rol encargado y la sucursal Ayacucho. |
| AC-IDE-03 | IDE-03 | **Dado** una contraseña incorrecta, **cuando** se intenta ingresar cinco veces seguidas, **entonces** se rechaza cada vez con el mismo mensaje y se bloquean temporalmente los intentos. |
| AC-IDE-04 | IDE-05 | **Dado** un usuario con el rol encargado, **cuando** el administrador intenta guardarlo sin sucursal o con dos, **entonces** se rechaza. |
| AC-IDE-05 | IDE-06 | **Dado** una sucursal con un encargado activo, **cuando** se asigna a otro, **entonces** se rechaza indicando quién la tiene. |
| AC-IDE-06 | IDE-07 | **Dado** un administrador, **cuando** desactiva a un usuario, **entonces** el cambio se aplica y queda una entrada de auditoría. |
| AC-IDE-07 | IDE-08 | **Dado** un usuario desactivado, **cuando** intenta ingresar o renovar su sesión, **entonces** se rechaza. |
| AC-IDE-08 | IDE-09 | **Dado** la base de usuarios, **cuando** se inspecciona, **entonces** ninguna contraseña aparece en texto claro. |
| AC-IDE-09 | IDE-10 | **Dado** una credencial válida, **cuando** otro servicio la recibe, **entonces** valida su firma y vigencia sin llamar a Identidad. |
| AC-IDE-10 | IDE-11 | **Dado** un cliente con consentimiento activo, **cuando** lo revoca, **entonces** se guarda la fecha y los avisos siguientes no se envían. |
| AC-IDE-11 | IDE-12 | **Dado** un cliente, **cuando** solicita acceder a sus datos, **entonces** la solicitud queda registrada con su estado. |
| AC-IDE-12 | IDE-14 | **Dado** una sesión activa, **cuando** el usuario cierra sesión, **entonces** su renovación deja de funcionar. |

## 5. Reglas e invariantes

- La credencial lleva rol y sucursal; la sucursal es obligatoria para encargados y vacía para los demás.
- La vigencia de la credencial es corta; la renovación depende de que el usuario siga activo.
- Las contraseñas nunca se guardan ni se registran en texto claro.
- Las tablas de Identidad viven en su propio esquema, con permisos exclusivos (AD18).

## 6. Casos límite y errores

- Un usuario desactivado conserva su historial de operaciones y auditoría.
- Cambiar el rol de un usuario invalida sus credenciales vigentes en la siguiente renovación.
- Un cliente sin WhatsApp puede comprar; solo se queda sin avisos.

## 7. Preguntas abiertas

- [ ] ¿Cuál es la política mínima de contraseñas? Propuesta: 10 caracteres como mínimo y sin lista de contraseñas comunes. Responde: responsable del proyecto.
- [ ] ¿Cuánto dura la credencial y cuánto la renovación? Propuesta: 15 minutos y 7 días. Se decide en el plan.
- [ ] ¿Las llamadas entre servicios reenvían la credencial del usuario o usan una credencial de servicio? Propuesta: reenviar para acciones del usuario y usar credencial de servicio para tareas de fondo. Se decide en el plan.
