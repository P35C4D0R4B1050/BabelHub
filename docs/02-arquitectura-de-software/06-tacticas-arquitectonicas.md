# Tácticas arquitectónicas

Una **táctica** es una decisión de diseño que mejora un atributo de calidad concreto (Bass, Clements y Kazman, *Software Architecture in Practice*). Este documento recorre los atributos de calidad prioritarios y registra qué táctica se eligió, dónde vive y qué la respalda. Sirve para comprobar que cada atributo tiene respuesta y que no hay decisiones huérfanas.

## 1. Tácticas por atributo

| Atributo | Táctica | Dónde se aplica | Decisión o spec |
| :--- | :--- | :--- | :--- |
| **AC01 Consistencia** | Transacciones con control de concurrencia en la base | Reservas y ajustes de stock | AD23, SEL-04 |
| | Idempotencia con clave persistida | Reservas, cobros, reembolsos | AD23, SEL-09, PAG-04 |
| | Reserva todo o nada por pedido | Selección de fuente | SEL-05 |
| | Expiración automática de reservas (15 min) | Inventario | AD08, SEL-08 |
| **AC02 Rendimiento** | Caché de lectura de disponibilidad con expiración corta | Consultas de disponibilidad | AD06, DA02 |
| | Validación definitiva contra la base al reservar | Reserva | SEL-01 |
| | Paginación con tope y consultas acotadas | API de lectura | [API e integraciones](../05-seguridad/05-api-e-integraciones.md) |
| | Recursos estáticos con caché del navegador y compresión | Web | AD21, WEB-21 |
| **AC03 Disponibilidad** | Reinicio automático de contenedores | Todos | RNF03, AD10 |
| | Degradación controlada: el catálogo se sirve desde caché si Inventario cae; el checkout informa en vez de cobrar a ciegas | E-commerce | CAR-06 |
| | Aislamiento de fallas: una falla del Notificador nunca interrumpe un caso de uso | Notificaciones | AGENTS 10, NOT-04 |
| | Tiempos de espera y corte de dependencias | Llamadas externas | SEG-14, AD25 |
| | Reintento con esperas crecientes | Avisos, consulta de pagos | NOT-04, PAG-07 |
| **AC04 Escalabilidad** | Sucursales como datos, no como código | Fuentes | AD01, RNF04 |
| **AC05 Seguridad** | Autenticar, autorizar, limitar acceso | Todos | AD22, RNF05 |
| | Detectar: registro y alertas de eventos de seguridad | Transversal | AUD-10, SEG-09 |
| | Reaccionar: bloqueo temporal, rechazo con espera sugerida | Identidad y borde | IDE-03, SEG-05, SEG-06 |
| | Recuperar: respaldo cifrado y restauración probada | Datos | AD27, RNF24 |
| | Reducir superficie: paneles y bases sin puertos publicados | Despliegue | RNF06 |
| **AC06 Mantenibilidad / testabilidad** | Aislamiento por capas con regla de dependencia | Cada servicio | AD09, RNF09 |
| | Puertos para lo externo | Infraestructura | [organización interna](../03-diseno-de-software/02-organizacion-interna.md) |
| | Pruebas de arquitectura que hacen cumplir las referencias | Repositorio | `tests/architecture` |
| **AC07 Eficiencia de recursos** | Imágenes multi-arquitectura y límites de memoria | Compose | RNF11, RNF12 |
| | Un solo almacén de caché compartido | Redis | AD06, RNF12 |
| **AC08 / AC10 Usabilidad y accesibilidad** | Interfaz móvil primero, estados estándar | Web | AD21, AD29 |
| **AC09 Protección de contenido** | Entrega con URL firmada de corta duración y almacén privado | Biblioteca digital | AD19, AD20 |
| **AC11 Privacidad** | Minimización, retención definida, borrado o anonimización | Datos personales | RNF23, AD27 |
| **AC12 Recuperabilidad** | Respaldo diario, objetivos RPO y RTO, prueba de restauración | Datos | AD27 |
| **AC13 Resistencia al abuso** | Límites de uso en dos niveles | Borde y Aplicación | AD24 |
| **AC14 Compatibilidad** | Mejora progresiva, sin dependencias de navegador no estándar | Web | RC14 |

## 2. Tácticas descartadas

| Táctica | Por qué no |
| :--- | :--- |
| Bus de eventos o mensajería | El volumen (5–10 sucursales) no lo justifica; REST síncrono es suficiente (AD04). |
| Bloqueos distribuidos en Redis para el stock | Agrega un punto de falla; las transacciones en la base ya garantizan la atomicidad (AD23). |
| Réplicas y alta disponibilidad de bases | El Free Tier no ofrece recursos; se compensa con respaldo y restauración probada. |
| Sesiones de servidor en cada servicio | La credencial firmada y corta evita consultar a Identidad en cada solicitud (AD22). |
| DRM comercial | Costo y complejidad; se mitiga con enlace firmado, límite de descargas y registro (AD20). |

## 3. Compromisos entre tácticas

| Tensión | Resolución |
| :--- | :--- |
| Caché de disponibilidad (rendimiento) frente a stock exacto (consistencia) | La caché solo alimenta la consulta; la decisión de reservar siempre se valida contra la base. |
| Credencial corta (seguridad) frente a carga sobre Identidad (eficiencia) | La verificación de firma es local; solo la renovación llega a Identidad. |
| Límites estrictos (resistencia al abuso) frente a clientes legítimos detrás de una red compartida | Los límites por origen son más altos que los por cuenta, y los mensajes explican el motivo sin acusar. |
| Registro detallado (detección) frente a minimización de datos (privacidad) | Se registran eventos y no contenido; nunca contraseñas, tokens ni datos de tarjeta. |
