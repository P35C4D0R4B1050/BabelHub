# Documentación de BabelHub

La documentación se organiza en siete carpetas temáticas. El orden sugiere el camino de lectura: qué se necesita, cómo se estructura la solución, cómo se diseña, cómo se protege, cómo se usa y cómo se construye.

| Carpeta | Pregunta que responde | Documentos |
| :--- | :--- | :--- |
| [01-analisis-de-sistema](01-analisis-de-sistema/) | ¿Qué problema se resuelve y qué debe cumplir el sistema? | Necesidad del negocio (NN), requisitos (RF, RNF, RC), marco legal, atributos de calidad (AC), actores y permisos, procesos y modelo de dominio. |
| [02-arquitectura-de-software](02-arquitectura-de-software/) | ¿Qué estructura global tiene y por qué? | Drivers (DA), decisiones (AD), estilo, escenarios y flujos, despliegue. |
| [03-diseno-de-software](03-diseno-de-software/) | ¿Cómo se ordena el código por dentro? | Clean Architecture y organización interna por servicio. |
| 04-modelo-c4 | Reservada | Se desarrollará más adelante. |
| [05-seguridad](05-seguridad/) | ¿Cómo se protege el sistema y sus datos? | Modelo de amenazas, controles ASVS, autenticación, privacidad, API, asistente, operación y mapeo a OWASP e ISO. |
| [06-experiencia-de-usuario](06-experiencia-de-usuario/) | ¿Cómo se ve, se usa y se hace accesible? | Identidad y sistema de diseño, pantallas y recorridos, estados y accesibilidad. |
| [07-proceso-de-desarrollo](07-proceso-de-desarrollo/) | ¿Cómo se construye y se verifica? | Flujo Spec-Driven Development y convenciones técnicas. |

Además: [glosario](glossary.md).

## Trazabilidad

`Necesidad (NN)` → `Requisito (RF, RNF, RC)` → `Atributo de calidad (AC)` → `Driver (DA)` → `Decisión (AD)` → `Estilo y diseño` → `Spec`

Los identificadores existentes no se renumeran; los nuevos se agregan al final de su serie. `dotnet run --project tools/SpecCheck` valida que los enlaces entre documentos existan y que las specs citen identificadores reales.
