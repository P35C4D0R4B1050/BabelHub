---
spec: identidad-recuperacion-del-segundo-factor
estado: Draft
---

# Plan — Restablecimiento del segundo factor de la única administración desde la consola

> **Cómo se construye.** Parte de la spec **Approved**. Todo vive en el módulo `Identidad` del E-commerce y en un modo de consola de su imagen. No hay migración de base, endpoint, pantalla, contrato entre servicios ni acción nueva de auditoría.

## 1. Resumen técnico

Un modo de consola nuevo de la imagen del E-commerce, `restablecer-segundo-factor --correo <dirección>`, sobre el anfitrión de consola de `crear-administrador` (`AnfitrionDeConsola`: comprueba antes de construir nada, fija el actor «sistema», no arranca el servidor ni los trabajos de fondo). Llama a un contrato público de Identidad, `IRestablecimientoDesdeLaInstalacion`, cuyo caso de uso comprueba actor, entorno y modo, abre **una unidad de trabajo**, bloquea todas las cuentas de administración, aplica una regla del Dominio, borra el segundo factor, anula los ingresos pendientes, revoca las renovaciones y audita; fuera de la transacción envía la alerta crítica de cuenta que ya existe. Para que ningún ingreso a medias sobreviva (RSF-08), el ingreso pasa a crear su paso siguiente bajo el bloqueo de la cuenta (D07).

## 2. Capas afectadas

| Capa | Piezas nuevas o modificadas |
| :--- | :--- |
| **Dominio** (`Identidad`) | `ReglasDeRestablecimientoDesdeLaInstalacion` (pura): `EntornoPermitido(entorno, modo)` (solo `Staging` o `Production`, con el modo vacío o `Normal`, sin distinguir mayúsculas); y la decisión sobre la cuenta objetivo (o ninguna), si tiene el segundo factor activo, las demás administraciones y la hora: `Permitido`, `NoEsUnaAdministracionActiva`, `HayOtraAdministracion` o `SinSegundoFactor`, en ese orden (RSF-04, RSF-05, RSF-06). `AccesoDeUnaAdministracion.PuedeIngresar(ahora)` es la definición de la spec §5; la vigencia de la temporal pasa a una sola función del Dominio, `ContrasenaTemporal.Vencida(debeCambiar, hasta, ahora)` (`ahora >= hasta` está vencida), que usan la regla y el ingreso. |
| **Aplicación** (`Identidad`) | Contrato público `IRestablecimientoDesdeLaInstalacion.Restablecer(entorno, modo, correo)` que devuelve `RestablecimientoDesdeLaInstalacion` (resultado cerrado y `AvisoEnviado`). `Interno/RestablecimientoDelSegundoFactorDesdeLaInstalacion`: si el actor no es «sistema», registra el acceso denegado (`AlcanceDeLaCuenta.Denegar`, motivo `fuera-de-rol`) y devuelve `NoPermitido` (RSF-03); si el entorno o el modo no se permiten, `EntornoNoPermitido` sin tocar datos (RSF-02, que se comprueba primero); normaliza el correo; en `IUnidadDeTrabajo.Ejecutar`: `IRepositorioDeCuentas.BloquearLasAdministraciones` (**miembro nuevo**: todas las cuentas de rol administración central, activas o no, bloqueadas y ordenadas por identificador), la cuenta objetivo **se toma de esa lista bloqueada** (nunca de una lectura previa), `ISegundoFactorAlmacen.Obtener`, la regla, `Desactivar`, `IRepositorioDeDesafios.AnularLosDelUsuario` (**miembro nuevo**), `RevocacionDeSesiones.Todas` y la auditoría `SegundoFactorRestablecidoPorAdministracion` (objeto `usuario`, actor «sistema», sin motivo ni detalle). Tras confirmar, `AvisosDeCuenta.SegundoFactorRestablecido`, que pasa a devolver si el correo salió. El texto de la plantilla `segundo-factor-restablecido` (`RenderizadorDeCorreoDeProduccion`) deja de decir «La administración restableció», añade qué hacer si no se pidió y avisar a quien opera la instalación o a la administración (RSF-11); es la misma plantilla para IDE-38. **`Ingreso`:** `Ingresar`, ya comprobada la contraseña, abre una unidad de trabajo, relee la cuenta con su bloqueo, rechaza con la respuesta uniforme si dejó de estar activa o si su corte de sesiones cambió desde la primera lectura, y dentro de ella decide y guarda el paso siguiente (lee ahí el segundo factor); `IniciarConfiguracionDelSegundoFactor` bloquea la cuenta y comprueba de nuevo el desafío antes de guardar un secreto sin confirmar (D07). |
| **Infraestructura** | `PostgresRepositorioDeCuentas.BloquearLasAdministraciones` (`SELECT … WHERE rol = 'administrador-central' ORDER BY id FOR UPDATE`) y `PostgresRepositorioDeDesafios.AnularLosDelUsuario` (`UPDATE … SET usado = true WHERE usuario_id = @u AND NOT usado`), ambos con parámetros; registro del caso de uso en `ComposicionDeIdentidad` (`TryAddScoped`). Se corrige el comentario de `PrimerAdministradorDeLaInstalacion` («recuperación de acceso», que el personal no tiene). |
| **Api** | `Plataforma/ModoRestablecerSegundoFactor` (argumentos; antes de construir el servicio lee `Modo` de la configuración `BABELHUB__` como `ArranqueSeguro` y niega entorno o modo no permitidos; limpia los proveedores de registro como `ModoCargarCatalogo`; no repite en su salida ningún argumento; captura toda excepción de la operación y escribe un mensaje fijo sin tipo, traza ni correo; código 0 solo si restableció) y su rama en `Program.cs`. `AnfitrionDeConsola` admite silenciar los registros. |
| **Web** | Ninguna pantalla. |
| **Shared** | Ninguna. |
| **Operación** | Guía de primer despliegue §15.1 (con la persona titular lista para ingresar y el cambio de contraseña ante sospecha), `docs/05-seguridad/07-operacion-segura.md` §3.2.0, `docs/05-seguridad/03-autenticacion-y-autorizacion.md`, la fila «Segundo factor» de `docs/01-analisis-de-sistema/06-procesos-de-negocio.md` y la amenaza AM49 en `docs/05-seguridad/01-modelo-de-amenazas.md`. |

## 3. Modelo de datos

Sin cambios de esquema ni permisos. Se usan `identidad.usuarios`, las tablas del segundo factor, `identidad.desafios_de_ingreso` (ya con `UPDATE` para `rol_identidad`), las renovaciones y el registro aislado de auditoría.

## 4. Contratos

| Operación | Entrada | Salida | Errores |
| :--- | :--- | :--- | :--- |
| `IRestablecimientoDesdeLaInstalacion.Restablecer` | entorno, modo y correo | `Restablecido` (con `AvisoEnviado`), `NoPermitido`, `EntornoNoPermitido`, `CorreoInvalido`, `NoEsUnaAdministracionActiva`, `HayOtraAdministracion` o `SinSegundoFactor` | la falla de la base o de la auditoría sube como excepción y no confirma nada |
| Consola `restablecer-segundo-factor --correo <dirección>` | argumentos | texto en la salida estándar o de error; código 0 solo si restableció | opción desconocida, entorno o modo no permitido o configuración incompleta: código 1 sin tocar la base; falla de la operación: código 1 con un mensaje fijo («no se completó o no se pudo confirmar; repite la orden: si ya se aplicó, dirá que no hay un segundo factor activado») |
| `IRepositorioDeCuentas.BloquearLasAdministraciones` (interno) | — | las cuentas de administración central, bloqueadas | — |
| `IRepositorioDeDesafios.AnularLosDelUsuario` (interno) | usuario | — | — |

## 5. Concurrencia, transacciones e idempotencia

El restablecimiento es una sola transacción (lectura confirmada) cuyo primer paso bloquea las filas de **todas** las cuentas de administración: dos órdenes se serializan (la segunda lee el factor ya borrado y responde `SinSegundoFactor`) y una reactivación o un restablecimiento simultáneo de otra administración espera o se refleja en la lectura (RSF-16). Desactivar o cambiar de rol toman los bloqueos en otro orden (primero la cuenta objetivo): puede haber un interbloqueo, la base aborta una de las dos sin efectos y, si es la orden, informa que no se completó (RSF-15).

Ingresos simultáneos, por paso:
- **Contraseña** (`Ingresar`): crea su desafío (o su sesión) bajo el bloqueo de la cuenta y solo si el corte de sesiones no cambió. Si confirma antes que la orden, la orden anula ese desafío y revoca esa sesión; si va después, ve el corte nuevo y se rechaza.
- **Presentar el código o confirmar la configuración:** compite por la fila del segundo factor y la del desafío; o confirma antes y su renovación se revoca, o encuentra el factor borrado o el desafío anulado y falla (o la base aborta una de las dos).
- **Guardar un secreto sin confirmar:** bajo el bloqueo de la cuenta, solo con el desafío todavía vigente.
- **Cambiar la contraseña temporal:** ya relee la cuenta con bloqueo y marca el desafío; si confirma antes, el desafío que encadena lo anula la orden; si va después, su desafío está anulado.
- **Renovar:** rota bajo el bloqueo de la fila y respeta el corte.

Una falla de la auditoría revierte todo. El aviso va después de confirmar y nunca lanza (AGENTS.md §3, regla 10). Si se pierde la conexión al confirmar, la orden no puede saber si se aplicó (R04).

## 6. Seguridad y alcance

- **Superficie:** sin endpoint; solo un modo de consola que exige acceso a la VM y a las variables del servicio (AM44, AM49). El caso de uso exige además el actor «sistema», el entorno y el modo, de modo que ningún otro consumidor del contrato en el servidor en marcha lo ejecuta en nombre de una persona.
- **Alcance por sucursal:** no aplica (operación de la instalación sobre una cuenta de administración).
- **Abuso:** la regla niega la orden cuando existe la vía normal (RSF-05); lo que haga queda auditado y avisado. Quien controla la VM puede eludir todo editando la base: residual declarado.
- **Tras restablecer:** sesiones e ingresos pendientes anulados; la contraseña sigue protegiendo la cuenta y el siguiente ingreso obliga a configurar el segundo factor (IDE-21). Las credenciales ya emitidas caducan en 15 minutos como máximo.
- **Datos:** ni la salida ni los registros ni la auditoría llevan secretos, códigos, contraseñas ni el correo; el correo viaja como argumento (queda en el historial de la terminal: la guía lo dice).
- **Dependencias externas:** si el contador de límites no responde, la alerta crítica sale igual; lo que quede pendiente por cuota o transporte lo entrega el servicio en marcha.

## 7. Pantallas

No tiene.

## 8. Estrategia de pruebas

| Criterio | Tipo de prueba | Dónde |
| :--- | :--- | :--- |
| AC-RSF-02, AC-RSF-04, AC-RSF-05, AC-RSF-06 | Unitaria del Dominio (tabla de entornos y modos y de casos, con el límite exacto del vencimiento) | `tests/unit/BabelHub.Ecommerce.UnitTests/Identidad/` |
| AC-RSF-01, AC-RSF-03 a AC-RSF-08 | Unitaria de la Aplicación con los dobles de Identidad (actor, entorno, efectos, desafíos de los tres modos, auditoría que falla, aviso enviado, suprimido, encolado y que falla, texto de la plantilla; el ingreso cuyo corte cambia entre la contraseña y el paso siguiente) | `tests/unit/BabelHub.Ecommerce.UnitTests/Identidad/` |
| AC-RSF-01, AC-RSF-05, AC-RSF-07, AC-RSF-10, AC-RSF-11 | Integración con PostgreSQL real: efectos y las demás cuentas intactas; ingreso empezado antes en cada paso que no se completa tampoco tras configurar el factor nuevo; ingreso posterior que configura y recibe 8 códigos; otra administración bloqueada, con textos por aceptar o sin segundo factor y la indicada con temporal vencida; auditoría caída; dos órdenes, reactivación e ingreso (contraseña y código) a la vez, aceptando «no se completó» sin cambios; base no disponible | `tests/integration/BabelHub.IntegrationTests/Identidad/` |
| AC-RSF-02, AC-RSF-04 a AC-RSF-06, AC-RSF-09, AC-RSF-11 | Integración del modo de consola: entornos y modos (`Ensayo`, `Restauracion`, `Operacion`, uno inválido) negados antes de construir; sobre el servicio compuesto con base real, la orden atendida en `Staging` y `Production` y el mensaje y el código de cada resultado, de una falla y de un choque (con un doble que lo lanza); argumentos que no se repiten, salida **y registradores** sin secretos ni correo, y ninguna ruta de la aplicación que lo ejecute | `tests/integration/BabelHub.IntegrationTests/Identidad/` |
| AC-RSF-08 (sin cupo) | La espera y el reenvío al renovarse la cuota son del envío común, ya probados con `ReintentoDeCorreosPendientes` (transversal-plataforma e identidad-seguridad); aquí se prueba que la orden entrega la alerta crítica y trata lo encolado como «no salió ahora» | pruebas existentes y unitaria de Identidad |

## 9. Riesgos

| Riesgo | Mitigación |
| :--- | :--- |
| R01. Se usa contra la cuenta equivocada. | Exige el correo exacto de una administración activa y que no haya otra que pueda ingresar; el aviso llega a su correo. |
| R02. Interbloqueo con una operación simultánea (orden de bloqueo distinto). | La base aborta una de las dos sin efectos; la orden se repite. |
| R03. El ingreso toma ahora el bloqueo de la fila de la cuenta tras comprobar la contraseña. | Es una fila por ingreso y un tramo corto, sin red dentro; las pruebas de ingreso y de concurrencia de sesiones siguen en verde. |
| R04. La conexión se pierde al confirmar: se aplicó pero la orden no lo sabe y la alerta por correo no sale. | El mensaje pide repetir (dirá que no hay segundo factor) y la auditoría queda; residual declarado. |
| R05. Reversión. | Sin migración: volver a la imagen anterior quita la orden, el bloqueo del ingreso y devuelve el texto anterior del aviso. |

## 10. Verificación contra AGENTS.md

- [x] Dominio sin dependencias externas.
- [x] Puertos definidos en Aplicación (dos miembros nuevos en puertos que existen); adaptadores solo en Infraestructura.
- [x] Sin referencias entre servicios; módulos solo por contrato público (regla 13); sin cambios en la interfaz web.
- [x] Alcance por sucursal: no aplica.
- [x] Sin secretos ni archivos digitales en repositorio o base de datos.
- [x] Auditoría en la misma transacción (§4) y aviso que nunca interrumpe (§3, regla 10).
- [x] Concurrencia garantizada por la infraestructura y probada con solicitudes simultáneas.
