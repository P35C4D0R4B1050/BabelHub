---
id: identidad-recuperacion-del-segundo-factor
titulo: Restablecimiento del segundo factor de la única administración desde la consola
estado: Done
aprobado_por: Claude (delegado por Diego Amiquero)
aprobado_en: 2026-11-27
plan_aprobado_por: Claude (delegado por Diego Amiquero)
plan_aprobado_en: 2026-11-27
servicio: E-commerce
prefijo: RSF
origen: [RF19, RF20, RNF08, RNF15]
calidad: [AC04]
depende: [identidad-seguridad, transversal-auditoria, transversal-plataforma]
---

# Spec — Restablecimiento del segundo factor de la única administración desde la consola

> **Qué y por qué, nunca cómo.** Esta spec describe comportamiento observable. Complementa a `identidad-seguridad` sin modificar sus requisitos: el personal sigue sin recuperación por correo (IDE-17), el restablecimiento por otra administración sigue siendo la vía normal (IDE-38) y desde la aplicación nadie se restablece a sí mismo (IDE-49). Define, **solo para el segundo factor**, el procedimiento con acceso a la infraestructura que `identidad-seguridad` §6 deja para la única administración.

## 1. Propósito

Que la instalación no quede sin quien la administre cuando la única persona que puede hacerlo pierde su dispositivo y sus códigos de respaldo, sin que nadie tenga que tocar los datos a mano. Nace de lo ocurrido en la tienda desplegada el 2026-11-27, cuando la única administración tuvo que recuperarse borrando datos directamente en la base, sin dejar registro en la auditoría ni aviso.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Una orden que quien opera la instalación ejecuta en el equipo donde corre el E-commerce para restablecer el segundo factor de una cuenta de administración central cuando ninguna otra puede hacerlo. | La contraseña de esa cuenta: sigue en el procedimiento de infraestructura de `identidad-seguridad` §6 (y, si aún no completó su primer ingreso, en PAG-47). |
| Sus condiciones, sus efectos, su registro en la auditoría y el aviso a la persona titular. | El segundo factor de clientes, encargados y personal de logística, que lo restablece una administración (IDE-38). |
| | Pantallas, direcciones de red o cualquier otra forma de pedirlo a distancia. |
| | Los casos en que otra administración existe y puede ingresar según la sección 5, aunque también haya perdido su dispositivo. |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| RSF-01 | Evento | Cuando quien opera la instalación pide con la orden de consola restablecer el segundo factor de la cuenta de administración central de un correo y no se da ninguna causa de rechazo de RSF-04 a RSF-06, el sistema deberá eliminar el secreto y los códigos de respaldo de esa cuenta sin cambiar su contraseña, su correo, su rol, su estado ni los datos de ninguna otra cuenta. | RF19, RNF15 |
| RSF-02 | Ubicuo | El sistema deberá ofrecer este restablecimiento únicamente como orden de consola del E-commerce en el entorno de revisión y en producción, sin pantalla ni dirección de red, y deberá rechazarlo sin tocar ningún dato en cualquier otro entorno. | RNF15 |
| RSF-03 | No deseado | Si el restablecimiento de esta spec se pide en nombre de una persona, con o sin sesión, y no del propio sistema, entonces el sistema deberá rechazarlo sin cambiar ninguna cuenta y registrar el acceso denegado (AUD-05). | RNF08, RNF15 |
| RSF-04 | No deseado | Si la orden no indica un correo, este no tiene un formato válido o no corresponde a una cuenta de administración central activa y no cancelada, entonces el sistema deberá rechazarla sin cambiar nada e informar el motivo. | RF19, RNF15 |
| RSF-05 | No deseado | Si existe otra cuenta de administración central que puede ingresar según la sección 5, entonces el sistema deberá rechazar la orden sin cambiar nada e indicar que esa administración debe restablecer el segundo factor desde la aplicación (IDE-38). | RF19, RNF15 |
| RSF-06 | No deseado | Si la cuenta indicada no tiene un segundo factor activado, entonces el sistema deberá informarlo sin cambiar nada, sin cerrar sesiones, sin avisar y sin registrar un restablecimiento. | RF19 |
| RSF-07 | Evento | Cuando restablece el segundo factor, el sistema deberá revocar en la misma operación todas las renovaciones de la cuenta, de modo que ninguna de sus sesiones pueda renovarse. | RNF15 |
| RSF-08 | Evento | Cuando restablece el segundo factor, el sistema deberá dejar sin efecto en la misma operación todo ingreso de la cuenta empezado antes y sin terminar, en cualquiera de sus pasos, de modo que ninguno pueda completarse, tampoco después de configurar el segundo factor nuevo. | RNF15 |
| RSF-09 | Evento | Cuando restablece el segundo factor, el sistema deberá registrarlo en la auditoría en la misma operación como un restablecimiento del segundo factor (AUD-16), con el actor «sistema» y la cuenta como objeto, sin secretos, códigos, contraseñas ni correo, y si no puede registrarlo no deberá aplicar ningún cambio (AUD-04). | RF20, RNF08 |
| RSF-10 | Evento | Cuando el restablecimiento quedó aplicado, el sistema deberá avisar al correo de la cuenta con la alerta crítica de restablecimiento del segundo factor (IDE-38) con las reglas de tope, cupo, espera, supresión y registro de las alertas críticas de cuenta (IDE-74, IDE-77, IDE-79, IDE-80, IDE-86). | RNF15 |
| RSF-11 | Ubicuo | El sistema deberá redactar el aviso de restablecimiento del segundo factor sin enlaces ni datos sensibles y sin atribuirlo a otra persona de la aplicación, diciendo que habrá que configurarlo de nuevo al ingresar y que quien no lo pidió ingrese cuanto antes, cambie su contraseña y avise a la administración. | RNF15 |
| RSF-12 | No deseado | Si el aviso falla, se demora o se suprime, entonces el sistema no deberá deshacer ni impedir el restablecimiento. | RNF15 |
| RSF-13 | Evento | Cuando la orden termina, el sistema deberá informar por la consola si restableció el segundo factor o por qué no lo hizo y, si lo hizo, si el aviso salió en ese momento y que la persona titular debe ingresar para configurarlo de nuevo (IDE-21), terminando con éxito solo si lo restableció. | RNF15 |
| RSF-14 | Ubicuo | El sistema no deberá mostrar en la consola ni escribir en sus registros técnicos secretos, códigos, contraseñas ni el correo indicado, ni mostrar trazas o mensajes internos de una falla. | RNF15 |
| RSF-15 | No deseado | Si no puede completar el cambio porque los datos no están disponibles o porque choca con otra operación, entonces el sistema no deberá aplicar ninguna parte de él, deberá informar que no se completó y permitir repetir la orden. | RNF15 |
| RSF-16 | No deseado | Si dos órdenes coinciden o una cuenta de administración ya existente cambia mientras una orden se ejecuta, entonces el sistema deberá comprobar RSF-04 a RSF-06 sobre el estado vigente dentro de la misma operación que cambia los datos, sin que ese cambio simultáneo altere lo comprobado antes de terminar, y aplicar el restablecimiento como máximo una vez. | RF19, RNF15 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-RSF-01 | RSF-01, RSF-07, RSF-08 | **Dado** la única administración central, con su segundo factor activado, sesiones abiertas y un ingreso que espera su código, **cuando** se ejecuta la orden con su correo, **entonces** se eliminan su secreto y sus códigos de respaldo; su contraseña, correo, rol y estado y las demás cuentas no cambian; ninguna renovación anterior sirve; el ingreso empezado antes no se completa, tampoco después de configurar el segundo factor nuevo; y al ingresar con su contraseña debe configurarlo antes de recibir una credencial, recibe 8 códigos nuevos y los anteriores no sirven. |
| AC-RSF-02 | RSF-02 | **Dado** el entorno de revisión o el de producción, **cuando** se ejecuta la orden válida, **entonces** se atiende; **dado** un entorno de desarrollo, de prueba automatizada, de ensayo, de restauración o cualquier otro, **entonces** se rechaza sin tocar ningún dato; y la aplicación en marcha no ofrece ninguna pantalla ni dirección que la ejecute. |
| AC-RSF-03 | RSF-03 | **Dado** una petición del restablecimiento en nombre de una persona con sesión o de un visitante, **cuando** se intenta, **entonces** se rechaza, ninguna cuenta cambia y queda un evento de acceso denegado. |
| AC-RSF-04 | RSF-04 | **Dado** una orden sin correo, con un correo mal escrito o desconocido, o con el de un cliente, un encargado, una persona de logística o una administración desactivada o cancelada, **cuando** se ejecuta, **entonces** no cambia nada y se informa el motivo. |
| AC-RSF-05 | RSF-05 | **Dado** otra administración activa con su contraseña definitiva, o con una temporal todavía vigente, también si está bloqueada por intentos fallidos, tiene textos por aceptar o no configuró su segundo factor, **cuando** se ejecuta la orden, **entonces** se rechaza sin cambios e indica que esa administración debe hacerlo desde la aplicación; y **dado** que las demás están desactivadas, canceladas o tienen la contraseña temporal vencida, **entonces** se restablece. |
| AC-RSF-06 | RSF-06 | **Dado** una administración única sin segundo factor, o con uno que empezó a configurar y no confirmó, **cuando** se ejecuta la orden, **entonces** nada cambia, sus sesiones siguen, no se envía aviso ni se registra un restablecimiento y se informa. |
| AC-RSF-07 | RSF-09, RSF-15 | **Dado** un restablecimiento aplicado, **cuando** se revisa la auditoría, **entonces** hay una sola entrada de restablecimiento del segundo factor con el actor «sistema» y la cuenta, sin secretos, códigos, contraseña ni correo; y **dado** que la auditoría no puede registrar, **entonces** el segundo factor, los códigos, las sesiones y los ingresos pendientes quedan como estaban, no se envía aviso y la orden termina sin éxito. |
| AC-RSF-08 | RSF-10, RSF-11, RSF-12 | **Dado** un restablecimiento, **cuando** se aplica, **entonces** sale al correo de la cuenta la alerta de restablecimiento, sin enlaces ni datos sensibles ni atribuirlo a otra persona, con lo que pide RSF-11; **dado** que se suprime porque ya salió otra del mismo tipo en las últimas 24 horas o porque el correo no está verificado, **entonces** queda un evento de seguridad y un aviso en la cuenta; **dado** que no queda cupo de correo, **entonces** queda pendiente y se envía al renovarse la cuota; y en todos los casos, también si el envío falla, el restablecimiento se mantiene. |
| AC-RSF-09 | RSF-13, RSF-14 | **Dado** una orden que se aplica y otra que se rechaza, **cuando** terminan, **entonces** la consola dice qué pasó; en la aplicada dice si el aviso salió en ese momento y que debe ingresar a configurar el segundo factor, y termina con éxito aunque el aviso no haya salido; la rechazada termina sin éxito; y ni la consola ni los registros técnicos muestran secretos, códigos, contraseñas, el correo indicado ni trazas. |
| AC-RSF-10 | RSF-16, RSF-07 | **Dado** la única administración con segundo factor, **cuando** se ejecutan dos órdenes a la vez, **entonces** solo una lo restablece, la otra informa que no hay segundo factor activado y queda una sola entrada de auditoría; **dado** que a la vez se reactiva otra administración, **entonces** o la orden se aplica y la reactivación ocurre después, o se rechaza por RSF-05; y **dado** que a la vez un ingreso presenta su código, **entonces**, si la orden se aplica, ninguna renovación de la cuenta sirve al terminar, tampoco la que ese ingreso hubiera recibido. |
| AC-RSF-11 | RSF-15 | **Dado** que los datos no están disponibles, **cuando** se ejecuta la orden, **entonces** no cambia nada, informa que no se completó, termina sin éxito y al repetirla con los datos disponibles se aplica. |

## 5. Reglas e invariantes

- **Otra administración «puede ingresar»** (RSF-05) cuando es una cuenta de administración central activa, no cancelada y distinta de la indicada, cuya contraseña es definitiva o es temporal y todavía no venció. Un bloqueo temporal por intentos fallidos, textos legales por aceptar o un segundo factor sin configurar no la excluyen: puede ingresar y, después, restablecer a la otra (IDE-38). El sistema no puede saber si una administración perdió su dispositivo o su contraseña.
- La orden no concede a quien la ejecuta nada que no tenga ya: quien opera el equipo del servicio controla su configuración y sus datos, y frente a esa persona las condiciones no protegen. Evitan usarla como atajo cuando existe la vía normal, y la auditoría y el aviso dejan constancia. La auditoría registra el actor «sistema»: no identifica a quién operó el equipo.
- Solo se restablece el segundo factor: nunca la contraseña, el correo ni el rol, y nunca el segundo factor de otra cuenta.
- Los rechazos por RSF-04 a RSF-06 se informan por la consola y no se registran en la auditoría, como los de PAG-48.
- La orden no actúa con la cuenta: un cambio de correo no reconocido pendiente (IDE-96) no la impide, porque esa regla limita a quien pide un cambio desde la cuenta.
- Las credenciales de acceso ya emitidas caducan solas en 15 minutos como máximo (`identidad-seguridad` §5).
- **Riesgo residual:** hasta que la persona titular configure su nuevo segundo factor, quien conozca su contraseña podría configurarlo en su lugar, o leer la misma clave que ella está configurando mientras no la confirme; a diferencia de IDE-38, aquí no hay otra administración que lo remedie, y una segunda alerta en 24 horas no sale por correo (IDE-74).
- Esta orden recupera una cuenta de personal; las operaciones de consola de las cuentas de prueba de `ecommerce-pagos` siguen sin tocar cuentas de personal.

## 6. Casos límite y errores

- Con dos o más administraciones que pueden ingresar según la sección 5 y ninguna con acceso (sin dispositivo, sin códigos o sin recordar su contraseña), la orden se niega: queda el procedimiento con acceso a la infraestructura de `identidad-seguridad` §6, fuera del sistema. Se evita guardando los códigos de respaldo.
- La única administración que olvidó su contraseña no se recupera con esta orden. La orden se aplica aunque la cuenta indicada tenga una contraseña temporal vencida: restablece solo el segundo factor.
- Una administración dada de alta cuya contraseña temporal sigue vigente cuenta como alguien que puede ingresar, aunque nadie conserve esa contraseña: basta esperar a que venza (24 horas, IDE-23).
- Una administración nueva dada de alta en el mismo instante puede no entrar en la comprobación: solo podría crearla la propia cuenta indicada con una sesión abierta, y la orden cierra sus sesiones.
- Repetir la orden después de aplicada no cambia nada (RSF-06).

## 7. Preguntas abiertas

- [x] ¿Cuál es la vía para recuperar el segundo factor de la única administración? Resuelto el 2026-11-27 (DD-140): una orden de consola en el equipo del servicio, con condiciones, aviso y auditoría; la persona responsable pidió ese día desarrollar los pendientes señalados y resolver las dudas con la alternativa recomendada, que era esta.
- [x] ¿Qué significa que no haya otra administración que pueda hacerlo? Resuelto el 2026-11-27 (DD-141): la definición de la sección 5.
- [x] ¿Qué pasa si hay otras administraciones que pueden ingresar y ninguna tiene acceso? Resuelto el 2026-11-27 (DD-141): la orden se niega y queda el procedimiento de infraestructura; la persona responsable puede pedir ampliarlo.
- [x] ¿Se restablece también la contraseña? Resuelto el 2026-11-27 (DD-140): no.
