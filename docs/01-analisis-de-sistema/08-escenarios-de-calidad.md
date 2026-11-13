# Escenarios de atributos de calidad

Desarrolla los seis [atributos de calidad](04-atributos-de-calidad.md) con **escenarios completos** (Bass, Clements y Kazman, *Software Architecture in Practice*). Un escenario describe, sin ambigüedad, qué pasa cuando un estímulo llega al sistema y cómo se mide que la respuesta fue la correcta. La tabla de `04-atributos-de-calidad.md` resume cada atributo en un párrafo; aquí cada párrafo se desglosa en escenarios que se pueden **probar**.

## 1. Cómo se leen

Cada escenario tiene seis partes:

| Parte | Qué responde |
| :--- | :--- |
| **Fuente** | Quién o qué origina el estímulo (cliente, encargado, atacante, el propio sistema, una persona operadora). |
| **Estímulo** | El evento que llega al sistema. |
| **Entorno** | En qué condición ocurre: operación normal, pico de tráfico, modo degradado, recién desplegado. |
| **Artefacto** | La parte del sistema que recibe el estímulo (módulo, servicio, VM, base de datos). |
| **Respuesta** | Lo que el sistema hace. |
| **Medida** | El umbral que dice si la respuesta fue la correcta. Sale de los [requisitos no funcionales](02-requisitos.md) (RNF). |

**Identificadores:** `ESC-ACnn-mm` (atributo `nn`, escenario `mm`). Cada atributo tiene al menos un escenario en **operación normal** y uno en **condición degradada o de abuso**, porque ahí es donde la arquitectura se pone a prueba.

**Trazabilidad:** después de cada tabla de escenarios, otra tabla enlaza cada uno con su RNF, la **táctica** que lo sostiene ([tácticas arquitectónicas](../02-arquitectura-de-software/06-tacticas-arquitectonicas.md)) y la **prueba** que lo verifica ([cómo se comprueba cada atributo](04-atributos-de-calidad.md) §3 y [estrategia de pruebas](../07-proceso-de-desarrollo/03-estrategia-de-pruebas.md)). Las metas numéricas son las de los RNF: propuestas iniciales para una red de 5 a 10 sucursales, que se validan con k6 al implementar.

## 2. Índice

| Atributo | Escenarios | Prioridad |
| :--- | :--- | :---: |
| **AC01** Consistencia del inventario | ESC-AC01-01 a 03 | Crítica |
| **AC02** Rendimiento y eficiencia | ESC-AC02-01 a 03 | Alta |
| **AC03** Disponibilidad y recuperabilidad | ESC-AC03-01 a 04 | Alta |
| **AC04** Seguridad y privacidad | ESC-AC04-01 a 05 | Alta |
| **AC05** Mantenibilidad y evolucionabilidad | ESC-AC05-01 a 03 | Media |
| **AC06** Usabilidad y accesibilidad | ESC-AC06-01 a 03 | Media |

## 3. AC01 Consistencia del inventario

Es el atributo que da origen al proyecto: **mostrar siempre stock real**. Vender una unidad que no existe destruye la confianza del cliente.

| ID | Fuente | Estímulo | Entorno | Artefacto | Respuesta | Medida |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **ESC-AC01-01** | Dos clientes distintos | Reservan a la vez la última unidad de una edición impresa en la misma sucursal. | Operación normal, base disponible. | Módulo Selección y reservas del Inventario y su base. | Concede una reserva y rechaza la otra, o la asigna a otra fuente con stock; el vendible no baja de cero. | 0 casos de vendible negativo; con 100 reservas simultáneas por 1 unidad, solo una prospera. |
| **ESC-AC01-02** | Cliente (o su navegador) | Repite la misma operación de reserva o de cobro por un reintento, un doble clic o una respuesta perdida. | Operación normal o red inestable. | Reservas y Pagos (clave de idempotencia). | La segunda solicitud con la misma clave devuelve el resultado de la primera; no reserva ni cobra dos veces. | 0 reservas o cobros duplicados con la misma clave en N repeticiones simultáneas. |
| **ESC-AC01-03** | Cliente | Su reserva pendiente vence (15 min) mientras el pago sigue en confirmación, y otro cliente pide la misma unidad. | Pico de compras; pago pendiente de confirmación. | Reservas, trabajo de fondo de expiración y Pagos. | La reserva vencida no cuenta como activa aunque el trabajo de fondo no la haya procesado; un pago pendiente la extiende una sola vez (hasta 30 min); gana quien confirma primero y el otro recibe estado inválido. | El vendible nunca cuenta una reserva vencida; 0 sobreventas en la prueba de dos pedidos que compiten por una reserva en el límite del vencimiento. |

| ID | RNF | Táctica (decisión o spec) | Prueba |
| :--- | :--- | :--- | :--- |
| ESC-AC01-01 | RNF01 | Transacción con control de concurrencia y `CHECK` en la base (AD23, SEL-04); reserva todo o nada (SEL-05). | xUnit con Testcontainers: solicitudes simultáneas sobre la última unidad. |
| ESC-AC01-02 | RNF01 | Idempotencia con clave persistida (AD23, SEL-09, PAG-04). | xUnit con Testcontainers: repeticiones simultáneas con la misma clave. |
| ESC-AC01-03 | RNF01 | Expiración automática de reservas con extensión única (AD08, SEL-08, SEL-22). | xUnit con reloj inyectado y Testcontainers: competencia en el límite del vencimiento. |

## 4. AC02 Rendimiento y eficiencia

La disponibilidad está en el camino crítico de la compra y los recursos gratuitos son limitados (VMs de 1 OCPU).

| ID | Fuente | Estímulo | Entorno | Artefacto | Respuesta | Medida |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **ESC-AC02-01** | Decenas de clientes | Consultan la disponibilidad y agregan libros al carrito a la vez durante una feria del libro. | Pico de tráfico; VMs de 1 OCPU; Inventario y Redis disponibles. | Consulta de disponibilidad (caché en Redis) y carrito del E-commerce. | Responde desde la caché de expiración corta; la validación definitiva contra la base ocurre solo al reservar. | p95 < 500 ms con 50 usuarios concurrentes. |
| **ESC-AC02-02** | Cliente | Confirma su pedido (reserva, cobro y confirmación) mientras otros 49 clientes compran. | Pico de tráfico. | Carrito y checkout, Selección y reservas y Pagos. | Completa la reserva y la confirmación dentro del presupuesto, sin contar el tiempo de la pasarela. | Confirmación de checkout p95 < 3 s. |
| **ESC-AC02-03** | Cliente en celular | Abre inicio, catálogo y detalle de un libro. | 4G simulada; operación normal; caché fría y caliente. | Interfaz web (recursos estáticos con caché y compresión). | Muestra el contenido principal sin esperar a todo el script. | LCP ≤ 2,5 s (p75), INP ≤ 200 ms, CLS ≤ 0,1; memoria y CPU sostenidas < 70 % por VM durante la prueba. |

| ID | RNF | Táctica (decisión o spec) | Prueba |
| :--- | :--- | :--- | :--- |
| ESC-AC02-01 | RNF02, RNF12 | Caché de lectura con expiración corta (AD06, DA02); consultas acotadas y paginadas. | k6 sobre disponibilidad con 50 usuarios; `docker stats`. |
| ESC-AC02-02 | RNF02 | Validación definitiva contra la base al reservar (SEL-01); dos aplicaciones .NET y límites de memoria (AD02, AD11). | k6 sobre el checkout con la pasarela simulada. |
| ESC-AC02-03 | RNF26, RNF12 | Recursos estáticos con caché y compresión (AD21, WEB-21). | Lighthouse sobre inicio, catálogo y detalle. |

## 5. AC03 Disponibilidad y recuperabilidad

No hay SLA ni redundancia real: se compensa con degradación controlada y recuperación probada. La tensión con AC02 y con los recursos gratuitos se explica en [atributos de calidad](04-atributos-de-calidad.md) §4.

| ID | Fuente | Estímulo | Entorno | Artefacto | Respuesta | Medida |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **ESC-AC03-01** | El propio sistema | Un contenedor se detiene por un error o por falta de memoria. | Operación normal. | Cualquier contenedor de una VM. | Se reinicia solo y vuelve a responder su comprobación de salud. | Reinicio automático en menos de 2 minutos; ≥ 99 % de disponibilidad mensual. |
| **ESC-AC03-02** | Cliente | Consulta el catálogo o confirma un pedido cuando el Inventario no responde. | Degradado: Inventario caído. | Catálogo y checkout del E-commerce. | El catálogo se sirve desde la caché con la última disponibilidad conocida; el checkout informa que no puede validar el stock, en lugar de cobrar a ciegas. Si falla WhatsApp, la compra se completa y el aviso queda pendiente. | 0 cobros sin reserva confirmada; catálogo consultable; 0 compras interrumpidas por el Notificador. |
| **ESC-AC03-03** | Persona operadora | La VM-VENTAS deja de responder. | Degradado: sin tienda. | VM-INVENTARIO (réplica de gestión) y Nginx. | Nginx pasa el host de gestión al servidor de respaldo; el personal sigue operando el inventario con sus sesiones (están en la base). | El personal retoma su trabajo sin volver a iniciar sesión; la tienda vuelve al recrear la VM. |
| **ESC-AC03-04** | Persona operadora | Se pierde la VM de datos. | Desastre. | PostgreSQL (bases `ecommerce` e `inventario`) y sus respaldos en el almacén de objetos. | Se restaura el último respaldo cifrado y el archivo de cambios en un entorno limpio. | RPO ≤ 24 h; RTO ≤ 4 h, con restauración probada antes de cada despliegue mayor. |

| ID | RNF | Táctica (decisión o spec) | Prueba |
| :--- | :--- | :--- | :--- |
| ESC-AC03-01 | RNF03 | Reinicio automático y `healthcheck` (AD10). | Prueba de caos manual: detener un contenedor y medir. |
| ESC-AC03-02 | RNF03 | Degradación controlada (CAR-06, CAT-12); aislamiento de fallas del Notificador (AGENTS 10, NOT-04). | Apagar el Inventario y recorrer catálogo y checkout; xUnit con notificador que falla. |
| ESC-AC03-03 | RNF03 | Réplica de gestión y servidor de respaldo de Nginx (AD11); sesiones en la base (WEB-04, AD15). | Apagar VM-VENTAS (o su contenedor) en el entorno de prueba y operar como personal. |
| ESC-AC03-04 | RNF24 | Respaldo diario cifrado y archivado continuo en el almacén de objetos (AD27). | Guion de restauración en un entorno limpio, midiendo el tiempo. |

## 6. AC04 Seguridad y privacidad

Se manejan datos personales, pagos y contenido con licencia de terceros; los flujos abiertos (reservas, descargas, asistente) son blanco de abuso. En todos los escenarios, el acceso indebido **se rechaza y se registra**.

| ID | Fuente | Estímulo | Entorno | Artefacto | Respuesta | Medida |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **ESC-AC04-01** | Encargado de una sucursal | Pide el inventario o los pedidos de otra sucursal. | Operación normal, sesión válida. | Casos de uso de la Aplicación y, en el Inventario, la seguridad por filas. | Rechaza con 403 y registra un único evento de acceso denegado con usuario, rol y recurso. | 100 % de accesos fuera de alcance rechazados y registrados; alerta ante más de 10 accesos denegados en 5 minutos. |
| **ESC-AC04-02** | Usuario autenticado | Intenta descargar un libro digital que no compró, o reutilizar un enlace ya vencido. | Operación normal. | Biblioteca digital y el almacén privado de archivos. | Rechaza sin entregar el archivo; un titular recibe un enlace firmado de corta duración y la descarga se cuenta. | 0 descargas no autorizadas; enlaces válidos 5 minutos como máximo; 5 descargas por compra. |
| **ESC-AC04-03** | Programa automatizado | Crea cuentas en masa, prueba contraseñas, reserva sin pagar o consulta al asistente sin parar. | Ataque de abuso desde la superficie pública. | Borde (Cloudflare y Nginx) y límites de la Aplicación por rol y negocio. | Aplica los límites en cuatro niveles, exige verificación humana en los flujos abiertos y bloquea temporalmente; responde de forma uniforme con espera sugerida. | Ingreso: 5 intentos por 15 minutos y bloqueo; API: 100 solicitudes por minuto por usuario; descargas: 10 por hora; asistente: 10 consultas por minuto; clientes legítimos sin pérdida de servicio; alerta ante más de 20 ingresos fallidos en 5 minutos. |
| **ESC-AC04-04** | Cliente | Pide acceder a sus datos personales y cancelar su cuenta. | Operación normal. | Identidad, hojas de reclamación y registros con datos personales. | Atiende la solicitud dentro del plazo, anonimiza lo que corresponde y conserva la auditoría sin datos personales. | Acceso en 20 días hábiles y las demás solicitudes en 10 (propuestos, por verificar); registros con más de 2 años anonimizados. |
| **ESC-AC04-05** | Usuario malicioso | Escribe al asistente instrucciones para que revele su configuración, produzca HTML ejecutable o ejecute acciones. | Operación normal con el proveedor de LLM disponible. | Módulo Asistente (solo lectura). | Responde solo con datos obtenidos de los módulos y del Inventario, como texto; sin herramientas de escritura y sin revelar instrucciones internas. | 0 acciones de escritura; salida sin HTML ejecutable; tope de consumo por consulta. |

| ID | RNF | Táctica (decisión o spec) | Prueba |
| :--- | :--- | :--- | :--- |
| ESC-AC04-01 | RNF05, RNF08, RNF20 | Alcance en la Aplicación y RLS en el Inventario (AD22, AD31); detectar y registrar (AUD-05, AUD-08, AUD-10, AUD-17, AUD-27, SEG-09). | xUnit de acceso fuera de alcance (403); revisión del registro de seguridad. |
| ESC-AC04-02 | RNF14 | Entrega con URL firmada y almacén privado (AD19, AD20). | Pruebas de integración de descarga ajena y de enlace vencido. |
| ESC-AC04-03 | RNF15, RNF17, RNF20 | Límites de uso en cuatro niveles y Turnstile (AD24, SEG-24, SEG-05, SEG-06, IDE-03). | Pruebas de integración y de carga con casos de abuso; ZAP baseline. |
| ESC-AC04-04 | RNF07, RNF23 | Minimización, retención y anonimización (RNF23, AD27, SEG-25, IDE-34). | Pruebas de integración de la solicitud y de la depuración diaria; inspección documental. |
| ESC-AC04-05 | RNF22 | Contexto de lectura de 5 minutos y solo lectura (IDE-33, IDE-35, IDE-36); límites del asistente. | Pruebas con casos de abuso (inyección de instrucciones). |

## 7. AC05 Mantenibilidad y evolucionabilidad

Con un equipo pequeño y desarrollo guiado por especificaciones, el costo del cambio debe ser bajo y crecer debe ser configuración, no desarrollo.

| ID | Fuente | Estímulo | Entorno | Artefacto | Respuesta | Medida |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **ESC-AC05-01** | Equipo de desarrollo | Cambia la regla de selección de fuente o reemplaza la pasarela de pago. | Desarrollo; el sistema está en producción. | Estrategia de selección en el Dominio del Inventario, o el adaptador de la pasarela en la Infraestructura del E-commerce. | El cambio queda en un solo módulo y una sola capa, se valida con pruebas y no altera el contrato entre los dos sistemas. | Cambio localizado en un módulo y una capa; contratos versionados en la ruta; cobertura ≥ 85 % en Dominio y Aplicación. |
| **ESC-AC05-02** | Administración de la librería | Abre una sucursal nueva (hasta 20). | Operación normal. | Módulo Fuentes del Inventario. | La sucursal se registra como dato con su contacto, sin cambiar código ni esquema. | 0 líneas de código; hasta 20 fuentes. |
| **ESC-AC05-03** | Desarrollador | Hace que un módulo use el interior de otro (por ejemplo, `Pagos` usa `Catalogo.Interno`) o que el Dominio use una librería de infraestructura. | Desarrollo, antes de integrar el cambio. | Pruebas de arquitectura y la verificación automática. | La verificación falla señalando la dependencia y el cambio no se integra; una prueba de sensibilidad comprueba que la regla detecta la violación. | Reglas de dependencia verificadas automáticamente en cada cambio; 0 violaciones integradas. |

| ID | RNF | Táctica (decisión o spec) | Prueba |
| :--- | :--- | :--- | :--- |
| ESC-AC05-01 | RNF09, RNF28 | Monolitos modulares en cuatro capas con regla de dependencia (AD01, AD09); puertos para lo externo. | xUnit del Dominio con estrategias intercambiables; pruebas de contrato; cobertura. |
| ESC-AC05-02 | RNF04 | Sucursales como datos, no como código (AD01). | Prueba de integración que crea una fuente nueva sin cambios de código. |
| ESC-AC05-03 | RNF09, RNF28 | Módulos con contrato público e interior privado (AD02, regla 13 de `AGENTS.md`). | `tests/architecture`, incluidas las pruebas de sensibilidad. |

## 8. AC06 Usabilidad y accesibilidad

La demanda en provincias llega desde dispositivos y conexiones variables, y la accesibilidad amplía el público (NN16).

| ID | Fuente | Estímulo | Entorno | Artefacto | Respuesta | Medida |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **ESC-AC06-01** | Cliente que compra por primera vez | Busca un libro y completa una compra con recojo en sucursal. | Celular; Chrome, Firefox, Safari o Edge recientes. | Interfaz web del E-commerce (áreas Tienda). | Guía el recorrido con estados claros (cargando, vacío, error) y confirma el pedido. | Máximo 5 pasos desde el catálogo; ≥ 90 % completa la compra sin ayuda; SUS ≥ 68; menos de 1 error por tarea. |
| **ESC-AC06-02** | Persona que usa solo teclado y lector de pantalla | Recorre catálogo, carrito y checkout sin ratón. | Escritorio o móvil con lector de pantalla. | Interfaz web (todas las pantallas de compra). | Mantiene el foco visible, anuncia los avisos y no depende solo del color para indicar el estado. | 0 incumplimientos WCAG 2.2 AA críticos; lista manual de teclado y lector superada. |
| **ESC-AC06-03** | Cliente | Usa la tienda cuando el Inventario o un módulo no responde, o cuando su conexión falla. | Degradado. | Interfaz web (estado de servicio no disponible). | Muestra el estado de servicio no disponible con una acción clara y conserva el carrito. | 100 % de las pantallas de datos con los cinco estados estándar; el carrito no se pierde. |

| ID | RNF | Táctica (decisión o spec) | Prueba |
| :--- | :--- | :--- | :--- |
| ESC-AC06-01 | RNF13, RNF27 | Interfaz móvil primero y estados estándar (AD21, AD29, RC14). | Playwright (recorrido de compra); prueba de usabilidad con personas (SUS). |
| ESC-AC06-02 | RNF25 | Accesibilidad AA y reglas de la interfaz ([accesibilidad](../06-experiencia-de-usuario/06-accesibilidad-wcag.md)). | Playwright con axe; lista manual de teclado y lector de pantalla. |
| ESC-AC06-03 | RNF13 | Estados estándar; WEB-26. | Prueba E2E con el Inventario apagado. |

## 9. Cómo se mantienen

- Un atributo o un RNF nuevo exige al menos un escenario aquí; un escenario sin RNF o sin prueba es un hueco que se corrige antes de planificar.
- Cuando una prueba de carga o de caos dé un resultado distinto de la medida, se registra en `progress/` y se decide entre ajustar la arquitectura o el RNF (con la persona responsable), nunca la prueba.
- Los flujos de [escenarios y flujos](../02-arquitectura-de-software/04-escenarios-y-flujos.md) muestran cómo responde la arquitectura a los escenarios de AC01, AC03 y la compra de un libro digital; aquí se fija la medida con que se juzgan.
