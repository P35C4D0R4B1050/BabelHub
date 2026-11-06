# Tácticas arquitectónicas

Una **táctica** es una decisión de diseño que mejora un atributo de calidad concreto (Bass, Clements y Kazman, *Software Architecture in Practice*). Este documento recorre los seis [atributos de calidad](../01-analisis-de-sistema/04-atributos-de-calidad.md) y registra qué táctica se eligió, dónde vive y qué la respalda. Sirve para comprobar que cada atributo tiene respuesta y que no hay decisiones huérfanas.

## 1. Tácticas por atributo

| Atributo | Táctica | Dónde se aplica | Decisión o spec |
| :--- | :--- | :--- | :--- |
| **AC01 Consistencia del inventario** | Transacciones con control de concurrencia en la base y `CHECK` que impide el vendible negativo | Reservas y ajustes de stock | AD23, SEL-04 |
| | Idempotencia con clave persistida | Reservas, cobros, reembolsos | AD23, SEL-09, PAG-04 |
| | Reserva todo o nada por pedido | Selección de fuente | SEL-05 |
| | Expiración automática de reservas (15 min), con una extensión única a 30 min si el pago queda pendiente | Inventario | AD08, SEL-08, SEL-22 |
| | La reserva viaja con las unidades de un traslado vinculado; el traslado de un pedido pagado no se rechaza | Inventario | TRA-07, TRA-14, TRA-16 |
| | El físico baja una sola vez, al entregar, al pasar a en reparto o al despachar el traslado | Inventario | SEL-11, SEL-21, TRA-07 |
| **AC02 Rendimiento y eficiencia** | Caché de lectura de disponibilidad con expiración corta, en la VM del E-commerce | Consultas de disponibilidad | AD06, DA02 |
| | Validación definitiva contra la base al reservar | Reserva | SEL-01 |
| | Paginación con tope y consultas acotadas | API de lectura | [API e integraciones](../05-seguridad/05-api-e-integraciones.md) |
| | Recursos estáticos con caché del navegador y compresión | Web | AD21, WEB-21 |
| | Dos aplicaciones .NET en lugar de cinco, imágenes multi-arquitectura y límites de memoria por contenedor | Despliegue | AD02, AD11, RNF11, RNF12 |
| **AC03 Disponibilidad y recuperabilidad** | Reinicio automático de contenedores y `healthcheck` | Todos | RNF03, AD10 |
| | Degradación controlada: el catálogo se sirve desde la caché si Inventario cae; el checkout informa en vez de cobrar a ciegas | E-commerce | CAR-06, CAT-12 |
| | Aislamiento de fallas: una falla del Notificador nunca interrumpe un caso de uso | Notificaciones | AGENTS 10, NOT-04 |
| | Réplica de gestión: el mismo contenedor del E-commerce en perfil «solo gestión» en la VM de Inventario, como servidor de respaldo de Nginx; el personal sigue operando el inventario si cae la VM de ventas | Despliegue | AD11, [despliegue](05-despliegue.md) §6 |
| | Segundo conector de Cloudflare Tunnel en la VM de ventas, para no depender solo de la VM de borde | Despliegue | AD13, [despliegue](05-despliegue.md) §6 |
| | Sesiones web en la base `ecommerce` (no en memoria ni en Redis): sobreviven al cambio de réplica | Web | WEB-04, AD15 |
| | Tiempos de espera y corte de dependencias | Llamadas externas y entre sistemas | SEG-14, AD25 |
| | Reintento con esperas crecientes | Avisos, consulta de pagos | NOT-04, PAG-07 |
| | Respaldo diario cifrado y archivado continuo de los cambios (WAL) fuera de Oracle; restauración probada | Datos | AD27, RNF24 |
| **AC04 Seguridad y privacidad** | Autenticar, autorizar y limitar acceso por rol y sucursal, comprobado en la Aplicación y repetido en la base con seguridad por filas (RLS) en Inventario | Todos | AD22, RNF05, AD31 |
| | Segundo factor obligatorio para el personal y opcional para el cliente; sesión web con inactividad de 30 min y tope absoluto | Identidad y Web | IDE-20, IDE-37, WEB-27 |
| | Contexto de lectura de 5 minutos y de solo lectura para el asistente; clave de firma exclusiva del módulo Identidad con rotación | Identidad y Asistente | IDE-33, IDE-35, IDE-36 |
| | Detectar: registro y alertas por sistema, con lista unificada y correo | Transversal | AUD-10, AUD-17, SEG-09 |
| | Reaccionar: bloqueo temporal, rechazo con espera sugerida | Identidad y borde | IDE-03, SEG-05, SEG-06 |
| | Reducir superficie: Inventario sin entrada pública, bases y paneles sin puertos publicados, un rol por esquema | Despliegue | RNF06, [despliegue](05-despliegue.md) §4 |
| | Mínimo privilegio por VM: cada VM recibe solo los secretos de sus contenedores | Despliegue | AD11, AD31 |
| | Entrega de libros digitales con URL firmada de corta duración y almacén privado | Biblioteca digital | AD19, AD20 |
| | Minimización, retención definida, borrado o anonimización; huella del origen en los contadores y aceptaciones con versión | Datos personales | RNF23, AD27, SEG-25, IDE-34 |
| | Límites de uso en cuatro niveles (Cloudflare, Nginx, Aplicación por rol y negocio); verificación humana (Turnstile) en flujos abiertos; correo verificado para pagar | Borde y Aplicación | AD24, SEG-24, REC-11, CAR-21 |
| **AC05 Mantenibilidad y evolucionabilidad** | Dos sistemas con límites claros: cada uno un monolito modular en cuatro capas con regla de dependencia | Cada sistema | AD01, AD09, RNF09 |
| | Módulos con contrato público y detalle interno; las pruebas de arquitectura impiden depender del interior de otro módulo | Cada sistema | AD02, `tests/architecture` |
| | Puertos para lo externo | Infraestructura | [organización interna](../03-diseno-de-software/02-organizacion-interna.md) |
| | Sucursales como datos, no como código | Fuentes | AD01, RNF04 |
| | Imágenes multi-arquitectura | Compose | RNF11 |
| **AC06 Usabilidad y accesibilidad** | Interfaz móvil primero, estados estándar, mejora progresiva sin dependencias de navegador no estándar | Web | AD21, AD29, RC14 |

## 2. Tácticas descartadas

| Táctica | Por qué no |
| :--- | :--- |
| Bus de eventos o mensajería, o un ESB | El volumen (5–10 sucursales) no lo justifica; REST síncrono es suficiente (AD04). |
| Bloqueos distribuidos en Redis para el stock | Agrega un punto de falla; las transacciones en la base ya garantizan la atomicidad (AD23). |
| Más servicios (identidad, asistente y web por separado) | Cinco aplicaciones sobre 1 OCPU por VM no caben ni aportan aislamiento real; son módulos del E-commerce (AD02). |
| Una instancia de PostgreSQL por sistema | Con 1 OCPU no da aislamiento de CPU y duplica la memoria de buffers; se usa una base por sistema y un rol por esquema (AD03). |
| Una VM de datos propia | Exigiría una tercera VM ARM de pago; la base convive con Inventario y se compensa con red y roles (AD11, AD12). |
| Sesiones de servidor en Redis | Redis vive en la VM de ventas y caería con ella; las sesiones van en la base (AD15). |
| DRM comercial | Costo y complejidad; se mitiga con enlace firmado, límite de descargas y registro (AD20). |
| Réplica de PostgreSQL en caliente | Útil, pero añade operación; queda como mejora opcional (AD27). |

## 3. Compromisos entre tácticas

| Tensión | Resolución |
| :--- | :--- |
| Caché de disponibilidad (rendimiento) frente a stock exacto (consistencia) | La caché solo alimenta la consulta; la decisión de reservar siempre se valida contra la base. |
| Credencial corta (seguridad) frente a carga sobre Identidad (eficiencia) | La verificación de la firma es local en cada sistema; solo la renovación llega al módulo Identidad. |
| Límites estrictos (abuso) frente a clientes legítimos detrás de una red compartida | Los límites por origen son más altos que los por cuenta, y los mensajes explican el motivo sin acusar. |
| Registro detallado (detección) frente a minimización de datos (privacidad) | Se registran eventos y no contenido; nunca contraseñas, tokens ni datos de tarjeta. |
| Base compartida con Inventario en una VM (costo cero) frente a aislamiento de la base (seguridad) | Red privada, un rol por esquema, RLS, contenedores sin privilegios y respaldo fuera de Oracle; si hiciera falta, la base se mueve a otra VM sin cambiar código. |
