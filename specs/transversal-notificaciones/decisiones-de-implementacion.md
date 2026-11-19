---
spec: transversal-notificaciones
---

# Decisiones de implementación — Notificaciones

Decisiones tomadas al construir (no cambian la spec ni el plan aprobados). Lee también `progress/ajustes-al-implementar.md`.

| Tarea | Decisión | Criterios |
| :--- | :--- | :--- |
| T01 | **`Aviso`** con siete estados y una máquina cerrada; los campos y el número se vacían al terminar el aviso. **`PoliticaDeReintento`**: 5 reintentos con esperas de 1, 5, 15, 30 y 60 minutos, edad máxima de 24 h y aplazamiento de 5 min sin consumir intento. | AC-NOT-04 |
| T02 | **`CatalogoDePlantillas`** con las 14 plantillas de NOT-02 (la de traslado es para sucursal); campos cerrados y tipados, sin enlaces, tarjetas ni texto libre. **`NumeroDeDestino`** normaliza a E.164 (+51 por defecto, `00`, cero inicial, dígitos Unicode) y nunca se imprime. | AC-NOT-06, AC-NOT-13 |
| T03 | **Contrato público** (`ContratoDeNotificaciones.cs`): modelos propios, sin tipos del Dominio ni de `Shared`. `IRecepcionDeAvisos`, `IRegistroDeEstadoDeEntrega` e `IConsultaDeAvisos` solo se **declaran**; sus implementaciones son de T09 a T11. | AC-NOT-07 |
| T04 | **`SolicitarAviso`** valida plantilla, destinatario, evento, referencia y campos **en memoria antes de tocar la base o Identidad**. El consentimiento se consulta a Identidad con `IEstadoDeCuenta`; una cuenta inexistente o cancelada cuenta como sin consentimiento. Si Identidad o la base fallan devuelve `NoSeGuardo`, publica la alerta técnica (reutiliza el motivo `dependencia-no-necesaria-fallida`) y **nunca lanza**; si la alerta también falla, tampoco. | AC-NOT-01, AC-NOT-02, AC-NOT-08 |
| T05 | La unicidad (evento, destinatario, plantilla) la da el `INSERT` condicional del repositorio (`InsertarSiNoExiste`); 100 solicitudes simultáneas dejan un solo aviso. El índice único de la base es de la Infraestructura. | AC-NOT-04 |
| T06 | **`CancelarAvisosPendientes`** cancela los avisos **no finales** del cliente con el motivo `destinatario-cambiado`; es idempotente y **lanza** si la base falla, para que Identidad no deje el cambio a medias. **Sin marcar:** la comprobación del estado vigente en cada intento de envío pertenece al enviador (T08). | AC-NOT-07 |
| T07 | **`DesactivarAvisosDeUnNumero`**: solo `administrador-central` (otro rol se deniega y se registra, `fuera-de-rol`); normaliza el número, guarda **solo la huella versionada**, audita `AvisosDeNumeroDesactivados` en la misma transacción con la huella parcial como id (si la auditoría falla, no se aplica). `EstaDesactivado` mira **todas las versiones vigentes** de la clave, así una rotación no reactiva un número. La reactivación **reutiliza la misma acción de auditoría** con `causa = reactivado-a-pedido-del-titular` (no hay acción propia en el catálogo). | AC-NOT-13 |

## Pendiente para las capas siguientes

- `IEstadoDeCuenta` (consentimiento) aún no tiene adaptador real: lo entrega la Infraestructura de Identidad (IDE-6).
- Adaptadores EF de `IRepositorioDeAvisos` y `IRepositorioDeNumerosDesactivados`, el índice único y `IHuellasDeNumeros` (HMAC con clave versionada) son de la Infraestructura de Notificaciones.

## Correcciones tras la revisión de NOT-1

Revisión de arquitectura («Cumple con observaciones», sin Bloqueantes ni Importantes) y de seguridad («Cumple con observaciones», 3 Importantes, sin Bloqueantes). **Corregido y probado:**

- **Referencia del cliente canónica:** `SolicitarAviso` guarda el id de la cuenta en su forma «D» en minúsculas (acepta «N» y mayúsculas); así la unicidad (NOT-05) y la cancelación (NOT-08) no dependen de cómo se escribió. La forma «B» (con llaves) sigue rechazada.
- **Motivo de la desactivación:** se rechaza con siete o más cifras de cualquier alfabeto (cualquier separador), un `@` (o su forma de ancho completo), un enlace o caracteres de control; antes solo detectaba dígitos ASCII seguidos.
- **Filtro de tarjeta:** en los campos `Identificador` y `NumeroDePedido` solo se rechaza el valor hecho únicamente de dígitos y separadores; un GUID o una guía mixta ya no se pierde.
- Una solicitud `Rechazado` deja una alerta técnica (es un error del módulo que llama).
- El rol exige `UsuarioId` no nulo y su constante se compara con `RolesDeUsuario.AdministradorCentral` en `tests/architecture` (`ReglasDeNotificacionesTests`); la prueba usaba un rol inexistente (`logistica`) y ahora usa `personal-de-logistica`.

**Para las tareas siguientes (en `progress/hallazgos.md`):** la implementación de `ICancelacionDeAvisos` (`CancelarAvisosPendientes`) se entrega en T06 aunque su contrato es de T03; `EstaDesactivado` necesita un puerto para el enviador (T08), que además debe revisar `EsFinal` tras un `IniciarEnvio` falso y comprobar el consentimiento y el estado vigente justo antes de cada envío; el cupo reservado se devuelve al cancelar en T09; la atomicidad de AC-NOT-04 se prueba con PostgreSQL en T22; `IHuellasDeNumeros` es el puerto que implementa el calculador de huella de T13.
