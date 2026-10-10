# Mapeo a IEEE 1016

La propuesta original del proyecto pide documentar con el modelo C4 e IEEE 1016 (RC11). IEEE 1016 organiza un documento de diseño de software en **puntos de vista** (*viewpoints*). Este mapeo muestra dónde se cubre cada uno, para no duplicar contenido ni dejar vacíos. El modelo C4 queda reservado en `04-modelo-c4` y se desarrollará más adelante.

> Los nombres de los puntos de vista se tomaron de la estructura habitual de la norma IEEE 1016-2009; antes de una entrega formal, se verifican contra el texto del estándar.

## 1. Puntos de vista

| Punto de vista (IEEE 1016) | Qué describe | Dónde se cubre |
| :--- | :--- | :--- |
| **Contexto** | Servicios, actores y límites del sistema | [Necesidad del negocio](../01-analisis-de-sistema/01-necesidad-del-negocio.md) (diagrama de contexto), [actores y permisos](../01-analisis-de-sistema/05-actores-y-permisos.md); C4 nivel 1 (reservado) |
| **Composición** | Partes en que se descompone el sistema | [Estilo arquitectónico](../02-arquitectura-de-software/03-estilo-arquitectonico.md) (componentes), [módulos y contratos](04-modulos-y-contratos.md); C4 niveles 2 y 3 (reservado) |
| **Lógico** | Clases, tipos y relaciones del dominio | [Modelo de dominio](../01-analisis-de-sistema/07-modelo-de-dominio.md), [organización interna](02-organizacion-interna.md) |
| **Dependencia** | Relaciones y dependencias entre partes | [Clean Architecture](01-clean-architecture.md) (regla de dependencia y matriz), pruebas de arquitectura |
| **Información** | Datos persistentes y su manejo | [Modelo de datos](05-modelo-de-datos.md), [privacidad](../05-seguridad/04-privacidad-y-datos-personales.md) |
| **Uso de patrones** | Patrones de diseño aplicados | [Principios y patrones](03-principios-y-patrones.md), [tácticas arquitectónicas](../02-arquitectura-de-software/06-tacticas-arquitectonicas.md) |
| **Interfaz** | Interfaces externas e internas | [Módulos y contratos](04-modulos-y-contratos.md), [API e integraciones](../05-seguridad/05-api-e-integraciones.md), [pantallas](../06-experiencia-de-usuario/02-pantallas-y-recorridos.md) |
| **Estructura** | Organización interna de partes | [Clean Architecture](01-clean-architecture.md) (mapa del repositorio), [organización interna](02-organizacion-interna.md) |
| **Interacción** | Colaboración entre partes en el tiempo | [Escenarios y flujos](../02-arquitectura-de-software/04-escenarios-y-flujos.md), [procesos de negocio](../01-analisis-de-sistema/06-procesos-de-negocio.md), [autenticación](../05-seguridad/03-autenticacion-y-autorizacion.md) |
| **Dinámica de estados** | Estados y transiciones | [Estados y concurrencia](06-estados-y-concurrencia.md) |
| **Algoritmo** | Lógica detallada de operaciones clave | Se define en el `plan.md` de cada spec (selección de fuente, expiración de reservas, límites); regla del dominio primero como prueba unitaria |
| **Recursos** | Uso de recursos y sus límites | [Presupuesto de recursos](../02-arquitectura-de-software/07-presupuesto-de-recursos.md), [despliegue](../02-arquitectura-de-software/05-despliegue.md) |

## 2. Preocupaciones transversales (*design concerns*)

| Preocupación | Dónde |
| :--- | :--- |
| Calidad y atributos | [Atributos de calidad](../01-analisis-de-sistema/04-atributos-de-calidad.md), [tácticas](../02-arquitectura-de-software/06-tacticas-arquitectonicas.md) |
| Seguridad y privacidad | [05-seguridad](../05-seguridad/) |
| Experiencia de usuario y accesibilidad | [06-experiencia-de-usuario](../06-experiencia-de-usuario/) |
| Decisiones y justificación | [Decisiones arquitectónicas](../02-arquitectura-de-software/02-decisiones-arquitectonicas.md) |
| Trazabilidad con requisitos | [Matriz de trazabilidad](../../specs/TRACEABILITY.md) |

## 3. Vacíos conocidos

| Vacío | Plan |
| :--- | :--- |
| Modelo C4 (contexto, contenedores, componentes y código) | Carpeta `04-modelo-c4` reservada; se desarrollará cuando se indique. |
| Algoritmos detallados | Se escriben en cada plan al aprobar la spec. |
| Diagramas de secuencia por caso de uso | Se agregan en los planes de las features críticas (reserva, pago y descarga). |
