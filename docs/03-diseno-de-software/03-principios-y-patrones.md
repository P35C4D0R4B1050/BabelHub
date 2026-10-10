# Principios y patrones de diseño

Los patrones se usan **solo donde resuelven un problema real de este sistema**, siguiendo la regla de AGENTS.md: *sin sobreingeniería; un puerto solo si hay algo externo o una variación real; sin patrones que nadie pidió*. Este documento fija el vocabulario común y dice cuándo no aplicar cada patrón.

## 1. Principios

| Principio | Cómo se aplica |
| :--- | :--- |
| **Separación de responsabilidades** | Cuatro capas por servicio con regla de dependencia hacia el Dominio ([Clean Architecture](01-clean-architecture.md)). |
| **Inversión de dependencias** | La Aplicación define puertos; la Infraestructura los implementa. |
| **Responsabilidad única** | Un caso de uso por operación del negocio (`ReservarStock`, `AprobarTraslado`). |
| **Abierto a extensión, cerrado a modificación** | La regla de selección de fuente es sustituible sin tocar el contrato (SEL-10). |
| **Mínimo privilegio** | Cuentas de base y credenciales de servicio con el permiso justo. |
| **Fallar de forma segura** | Ante la duda, negar (SEG-04). |
| **Diseñar para probar** | El Dominio y la Aplicación se prueban sin base ni red. |
| **Explícito sobre implícito** | Sin magia: dependencias por constructor, configuración por entorno, errores tipados. |

## 2. Catálogo de patrones usados

| Patrón | Problema que resuelve | Dónde | Spec o decisión |
| :--- | :--- | :--- | :--- |
| **Entidad y objeto de valor** | Reglas del negocio con identidad (Reserva, Traslado) o sin ella (Cantidad, Dinero, ISBN). | Dominio | [modelo de dominio](../01-analisis-de-sistema/07-modelo-de-dominio.md) |
| **Agregado** | Garantizar invariantes dentro de una frontera transaccional (por ejemplo, el stock de una edición en una fuente con sus reservas). | Dominio de Inventario | AD23 |
| **Repositorio** | Aislar el acceso a datos detrás de un puerto que habla el lenguaje del dominio. | Puerto en Aplicación; adaptador en Infraestructura | AD09 |
| **Unidad de trabajo** | Cambio y auditoría en la misma transacción (si no se puede auditar, falla). | Aplicación e Infraestructura | AUD-04 |
| **Caso de uso (comando)** | Un punto de entrada claro por operación del negocio. | Aplicación | AD09 |
| **Consulta separada (CQRS ligero)** | Lecturas de disponibilidad rápidas y desde caché sin mezclar con las escrituras de reserva. | Aplicación de Inventario y E-commerce | AD06 |
| **Estrategia** | Cambiar la regla de selección de fuente sin tocar el contrato. | Dominio de Inventario | SEL-10 |
| **Máquina de estados** | Ciclos de vida con transiciones válidas (reserva, traslado, línea, envío, devolución). | Dominio | [estados y concurrencia](06-estados-y-concurrencia.md) |
| **Adaptador (puerto y adaptador)** | Cambiar de pasarela, notificador, almacén o proveedor de LLM sin tocar el negocio. | Infraestructura | Puertos de [organización interna](02-organizacion-interna.md) |
| **Capa anticorrupción** | Evitar que el modelo de Culqi, de n8n o del LLM contamine el dominio. | Infraestructura, dentro de cada adaptador | RNF21 |
| **Clave de idempotencia** | Repetir una operación no la duplica (cobros, reservas, reembolsos, avisos). | Aplicación e Infraestructura | AD23 |
| **Reintento con espera creciente y corte** | Dependencias externas lentas o caídas. | Infraestructura | AD25, NOT-04 |
| **Bandeja de salida (*outbox*) simple** | Registrar un aviso pendiente en la misma transacción y enviarlo después, sin perderlo ni bloquear. | Notificaciones | NOT-04, NOT-05 |
| **Resultado explícito** | Errores de negocio como valores (`Result`) y no como excepciones de control de flujo. | Aplicación | SEG-03 |
| **Objeto de transferencia (DTO)** | Contratos estables y estrechos entre servicios y hacia la Web. | `BabelHub.Shared` | API3 |
| **Política de autorización** | Reglas de rol, alcance y propiedad reutilizables. | Aplicación | RNF05, AD22 |
| **Opciones tipadas** | Configuración desde el entorno con validación al arrancar. | Api | AGENTS 8 |

## 3. Cuándo no usar cada patrón

| Patrón | No se usa cuando |
| :--- | :--- |
| Repositorio | El caso de uso es una consulta de solo lectura sencilla: se admite un lector específico. |
| Agregado | La regla no cruza varias filas; una entidad simple basta. |
| Estrategia | Solo hay una regla y nadie pidió una segunda; se deja la función simple. |
| Puerto | Lo que se envuelve no es externo ni variable (por ejemplo, el reloj del sistema se abstrae para probar, pero no una colección en memoria). |
| Bandeja de salida | El efecto externo puede perderse sin daño; se llama directamente con tiempo de espera. |
| Eventos de dominio | No existe bus ni consumidores: se llama al caso de uso o se guarda un registro. |
| Mediador / librería de CQRS | Los casos de uso se invocan con inyección de dependencias directa; no se agrega una librería sin necesidad. |

## 4. Reglas de codificación asociadas

1. Los nombres del dominio salen del [glosario](../glossary.md) (`ReservarStock`, `Fuente`).
2. Una regla de negocio nueva se escribe primero como prueba unitaria.
3. Los casos de uso no conocen HTTP, SQL ni SDKs de terceros.
4. Las excepciones se reservan para lo inesperado; los errores de negocio son resultados con un `code` estable.
5. Los constructores validan los invariantes: un objeto inválido no puede existir.
6. El tiempo y los identificadores se obtienen a través de abstracciones inyectadas, para poder probar expiraciones y orden.
