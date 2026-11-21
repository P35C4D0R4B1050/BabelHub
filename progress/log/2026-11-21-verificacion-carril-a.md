# Re-verificación de specs por el carril A (2026-11-21)

Encargo del coordinador: re-ejecutar `spec-verify` con el estado actual de `main` (`6cc6d127`: integración completa 7519 de 7523 con fallos solo flaky o conocidos, arquitectura 244, SpecCheck 0 avisos) y reescribir `verificacion.md` de `inventario-fuentes`, `inventario-seleccion-fuente`, `inventario-traslados`, `transversal-seguridad`, `transversal-auditoria` e `inventario-ajustes`; crear el de `identidad-seguridad` y el de `transversal-notificaciones`. Sin Docker, sin cambiar estados de spec, máximo tres rondas. Rama `claude/competent-khayyam-1d6d28`, sin push.

## 1. Cómo se hizo

Ocho verificadores en contexto aislado (uno por spec, solo escribían su `verificacion.md`, sin compilar ni ejecutar integración) recorrieron cada requisito contra el código y las pruebas actuales. Se comprobó por lectura lo que reportaron como defecto real antes de corregirlo (el correo de alertas sin registrar, los enrutadores, las fuentes de depuración, el paso de restauración, el evento vencido y el cupo de códigos).

## 2. Resultado por spec

| Spec | Resultado | Lo que queda (dueño) |
| :--- | :--- | :--- |
| `inventario-ajustes` | **Converge salvo AJU-22** | Redacción del texto de la spec (la persona responsable). |
| `inventario-fuentes` | **Converge salvo FUE-09/FUE-11, FUE-10 (DD-18) y la prueba PostgreSQL de FUE-21** | FUE-09/11 diferidas (la persona responsable); FUE-10 aceptada en DD-18; prueba de FUE-21 con Docker (carril B). |
| `inventario-seleccion-fuente` | **Converge salvo SEL-35 (prueba PostgreSQL) y S1 a S3** | Prueba con Docker (carril B); S1 a S3 de la persona responsable. |
| `inventario-traslados` | **Converge salvo el caso límite «anular tras recibir»** | Prueba de integración con Docker (carril C). TRA-22 corregida. |
| `transversal-seguridad` | **Converge salvo SEG-17/SEG-23 en el Inventario (Menor) y SEG-31 en el asistente** | La persona responsable decide el origen sin dirección reenviada; asistente T18. |
| `transversal-auditoria` | **Converge** salvo comprobación en integración del enrutamiento real y Menores D7 a D12 | Corrida completa de `main` (coordinación). |
| `identidad-seguridad` | **No converge** | D1 (Importante): las marcas de dispositivo no llegan a Identidad (carril A con `web-base`). |
| `transversal-notificaciones` | **Converge salvo NOT-02** | Dos avisos sin emisor (carril B o la persona responsable). |

Ninguna spec pasa a `Done` (lo hace el coordinador).

## 3. Correcciones de código y de pruebas de esta ronda

- `54a3c0a0`: el host real del E-commerce registra ahora `IEnvioDeCorreoDeAlertas`; los enrutadores de registros y de eventos de Identidad (se resuelven al pedirlos); las fuentes de depuración de Identidad en `DepurarAuditoria`; el paso de restauración de Auditoría en los dos servicios; y el reintento de los correos pendientes, la purga de caducados y el vaciado de Enviados como trabajos de fondo. Pruebas: `ComposicionDeLaAuditoriaYElCorreoEnElHostTests` (sin Docker).
- `aeb7f653`: el registro reserva el cupo de códigos por origen (IDE-83); la purga emite `CambioDeCorreoNoReconocidoVencido` antes de borrar la fila (AUD-33); `TrabajosDeFondoDeNotificacionesTests` cubre NOT-04.
- `0dc7ca1e`: aserciones de orden, fecha y cantidades en Ajustes (AJU-17, AJU-20), reinicio de la cuenta de reemplazos para un pedido nuevo (SEL-37) y el 400 de forma en `cerrar` para el encargado de la sucursal correcta.
- `332fad46`: una alerta sin cupo de correo sigue pendiente y se reintenta, en vez de marcarse como enviada.
- `32f22e54`: la pantalla de cancelación advierte que se pierde la biblioteca digital (IDE-28).
- `7534c0c3`: pruebas de AUD-04 con la auditoría caída (cambio de rol, desactivar y regenerar el segundo factor).

## 4. Lo que no se pudo cerrar y por qué

- **D1 de identidad (Importante):** atraviesa el ingreso web y no se puede verificar sin Docker; se deja como primera tarea de la próxima ronda (hallazgo registrado).
- **Pruebas de integración con Docker** (FUE-21, SEL-35, TRA «anular tras recibir»): no se escribieron sin poder ejecutarlas.
- **Pasos de restauración de Plataforma** (`PasoVerificarRegistroDeSupresiones`, `PasoReaplicarSupresiones`): sin registro ni orquestación del modo de restauración (FIN-2), registrado en `progress/hallazgos.md`.
- **Riesgo para la corrida completa de integración:** con Identidad compuesta, los eventos aislados de Identidad van a `identidad.registros`; una prueba con el host completo que esperara el evento en `auditoria.registros` fallaría por el enrutador (correcto por D17).

## 5. Verificación

`dotnet build BabelHub.sln -warnaserror`: 0 advertencias y 0 errores. Unitarias: E-commerce 5435 y Inventario 2379 (todas superadas); arquitectura 244; SpecCheck: 20 specs, 0 avisos; la búsqueda de secretos en `src` solo da los tres falsos positivos conocidos (URI `otpauth` y la contraseña de Redis leída del entorno). Pruebas sin Docker del host compuesto: `AnfitrionDelEcommerceTests`, `AnfitrionDelInventarioTests`, `ComposicionDeLaAuditoriaYElCorreoEnElHostTests`, `TrabajosDeFondoDeNotificacionesTests` y `PantallasDeLaCuentaTests` en verde (118 pruebas). Sin integración con Docker en este encargo.
