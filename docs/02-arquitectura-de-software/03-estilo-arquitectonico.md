# Estilo arquitectónico

## 1. Estilo seleccionado

**Arquitectura basada en servicios (*Service-Based*) de granularidad gruesa**, con **Clean Architecture** como organización interna de cada servicio.

- **Estructura global:** cuatro servicios independientes (E-commerce, Inventario, Identidad y Asistente), cada uno con una responsabilidad clara y, cuando guarda datos, con su propia **base de datos lógica** y su propio rol. Se comunican por **REST síncrono**. La interfaz es una aplicación web (`BabelHub.Web`, MVC con Razor) que actúa como cliente de los servicios (AD15).
- **Estructura interna:** cada servicio se divide en cuatro capas cuyas dependencias apuntan siempre hacia el dominio.
- **Unidad de despliegue:** cada servicio y la Web tienen su propia imagen, versión y configuración. En qué VM corre cada una es una decisión de despliegue (AD11), no del estilo: ver [despliegue](05-despliegue.md).

**Justificación:** la red prevista es de 5 a 10 sucursales y los recursos gratuitos son 2 OCPU y 12 GB por cuenta de Oracle, por lo que dividir el sistema en más servicios agregaría coordinación y consumo sin ventaja real de escalamiento (DA09). Un monolito, en cambio, mezclaría el inventario con la venta y dificultaría aislar y probar la parte más crítica (DA01). Clean Architecture compensa el tamaño reducido del sistema con límites claros dentro de cada servicio (DA08). La sección 7 explica punto por punto cómo se cumple el estilo.

## 2. Diagrama de arquitectura

```mermaid
flowchart TD
subgraph CLIENTES["CLIENTES"]
Nav["Navegador - cliente, sucursal, administración"]
end
subgraph BORDE["BORDE DE RED"]
CF["Cloudflare - DNS, TLS, WAF y túnel"]
NGX["Nginx - enrutamiento interno"]
end
subgraph SERVICIOS["SERVICIOS Y APLICACIÓN WEB"]
Web["BabelHub.Web - MVC Razor<br/>áreas Tienda, Sucursal, Admin"]
ECO["Servicio de E-commerce"]
INV["Servicio de Inventario Multi-Fuente"]
IDE["Servicio de Identidad y Seguridad"]
ASI["Servicio de Asistente de Compra"]
end
subgraph DATOS["DATOS"]
BDV[("BD ecommerce")]
BDI[("BD inventario")]
BDD[("BD identidad")]
RED[("Caché Redis")]
end
subgraph EXTERNOS["SISTEMAS EXTERNOS"]
PAG["Culqi"]
ENV["Servicio de envío"]
LLM["Proveedor de LLM"]
NOT["n8n + Evolution API<br/>WhatsApp"]
ALM[("Almacén de archivos<br/>libros digitales")]
end

Nav -->|"HTTPS"| CF
CF --> NGX
NGX --> Web
NGX --> ECO
NGX --> INV
NGX --> IDE
NGX --> ASI

Web -->|"REST interno"| ECO
Web -->|"REST interno"| INV
Web -->|"REST interno"| IDE
Web -->|"REST interno"| ASI
ECO -->|"REST"| INV
ASI -->|"REST"| ECO
ASI -->|"REST"| INV
ECO -.->|"valida JWT"| IDE
INV -.->|"valida JWT"| IDE
ASI -.->|"valida JWT"| IDE

ECO --> BDV
INV --> BDI
IDE --> BDD
ECO --> RED
INV --> RED

ECO -->|"pagos"| PAG
ECO -->|"envíos"| ENV
ECO -->|"avisos"| NOT
INV -->|"avisos"| NOT
ECO -->|"archivos digitales"| ALM
Nav -.->|"descarga con URL firmada"| ALM
ASI -->|"consultas"| LLM

style Nav fill:#222,stroke:#fff,color:#fff
style NOT fill:#222,stroke:#fff,color:#fff
style ALM fill:#222,stroke:#fff,color:#fff
style CLIENTES fill:#222,stroke:#fff,stroke-width:2px,color:#fff
style BORDE fill:#222,stroke:#fff,stroke-width:2px,color:#fff
style SERVICIOS fill:#222,stroke:#fff,stroke-width:2px,color:#fff
style DATOS fill:#222,stroke:#fff,stroke-width:2px,color:#fff
style EXTERNOS fill:#222,stroke:#fff,stroke-width:2px,color:#fff
style Web fill:#222,stroke:#fff,color:#fff
style CF fill:#222,stroke:#fff,color:#fff
style NGX fill:#222,stroke:#fff,color:#fff
style ECO fill:#222,stroke:#fff,color:#fff
style INV fill:#222,stroke:#fff,color:#fff
style IDE fill:#222,stroke:#fff,color:#fff
style ASI fill:#222,stroke:#fff,color:#fff
style BDV fill:#222,stroke:#fff,color:#fff
style BDI fill:#222,stroke:#fff,color:#fff
style BDD fill:#222,stroke:#fff,color:#fff
style RED fill:#222,stroke:#fff,color:#fff
style PAG fill:#222,stroke:#fff,color:#fff
style ENV fill:#222,stroke:#fff,color:#fff
style LLM fill:#222,stroke:#fff,color:#fff
```

La vista de despliegue sobre las VMs de Oracle está en [despliegue](05-despliegue.md).

## 3. Componentes y responsabilidades

| Componente | Responsabilidad | Decisión |
| :--- | :--- | :--- |
| **Aplicación Web (`BabelHub.Web`)** | Interfaz de clientes, encargados de sucursal y administración central en tres áreas; consume los servicios por REST desde el servidor y guarda el JWT en el servidor. No contiene reglas de negocio. | RC01, RC03, AD15 |
| **Cloudflare y Nginx** | Cloudflare protege y cifra el acceso; Nginx enruta cada solicitud al servicio correspondiente. | AD13, AD14 |
| **Servicio de E-commerce** | Catálogo (impresos y digitales), carrito, checkout, pagos, pedidos y biblioteca digital (acceso y descarga). Único servicio con el que interactúa el cliente final. | AD01, AD02, AD19, AD20 |
| **Servicio de Inventario Multi-Fuente** | Fuentes por sucursal, stock vendible (físico − reservas), reservas, selección de fuente, traslados y ajustes. | AD01, AD08 |
| **Servicio de Identidad y Seguridad** | Autentica usuarios (con segundo factor para el personal), emite JWT con rol y sucursal, firma con una clave propia que rota, y emite la credencial de lectura del asistente. | AD05, AD22 |
| **Servicio de Asistente de Compra** | Recomienda libros y responde dudas con datos de las API y un LLM externo. | AD07 |
| **Bases `ecommerce`, `inventario` e `identidad`** | Una base lógica por servicio con rol exclusivo, en una sola instancia PostgreSQL de la VM de datos. Ninguna consulta cruza bases; el Asistente no tiene base. | AD03, AD12, AD18 |
| **Notificaciones (n8n + Evolution API)** | Reciben avisos de E-commerce e Inventario por webhook y los envían por WhatsApp; su falla no bloquea ninguna operación. | AD16 |
| **Almacén de archivos digitales** | Guarda los EPUB y PDF en un bucket privado; el cliente los descarga con URL firmada de corta duración. | AD19, AD20 |
| **Caché Redis** | Disponibilidad (corta duración) y catálogo (mayor duración). | AD06 |

## 4. Reglas de comunicación

| Origen | Destino | Tipo | Propósito |
| :--- | :--- | :--- | :--- |
| Navegador | Cloudflare → Nginx → Web | HTTPS | Enviar todas las solicitudes de la interfaz. |
| Aplicación Web | E-commerce, Inventario, Identidad y Asistente | REST síncrono (red interna) | Obtener y modificar datos con el JWT del usuario. |
| E-commerce | Inventario | REST síncrono | Consultar disponibilidad real y solicitar reservas. |
| Asistente | E-commerce e Inventario | REST síncrono | Obtener datos, sin acceder a las bases; los pedidos con la credencial de lectura del propio cliente. |
| Servicios | Proveedor de correo | API externa | Códigos de seguridad, respuestas a reclamos, alertas y avisos de pago pendiente. |
| Todos los servicios | Identidad | Validación de JWT | Verificar rol y sucursal permitidos. |
| E-commerce, Inventario e Identidad | Su propia base (y Redis, los dos primeros) | SQL y caché | Persistir y acelerar sus datos; cada uno con su rol y sin acceso a las otras bases. |
| E-commerce | Culqi y servicio de envío | API externa | Cobrar y gestionar entregas. |
| E-commerce e Inventario | n8n | Webhook (asíncrono, tolerante a fallos) | Pedir el envío de un aviso por WhatsApp. |
| n8n | Evolution API → WhatsApp | API interna y externa | Armar y enviar el mensaje. |
| E-commerce | Almacén de archivos | API S3 (HTTPS) | Subir archivos digitales y firmar URLs de descarga. |
| Navegador | Almacén de archivos | HTTPS con URL firmada | Descargar el libro; la Web y el E-commerce no transportan el archivo. |
| Asistente | Proveedor de LLM | API externa | Generar respuestas. |

## 5. Alternativas descartadas

| Alternativa | Motivo del descarte |
| :--- | :--- |
| **Monolito único** | Mezcla el inventario con la venta y dificulta aislarlo, probarlo y asignarle recursos. |
| **Microservicios finos** | Añaden coordinación, comunicación y despliegue innecesarios para 5–10 sucursales, con 12 GB de RAM en total. |
| **Arquitectura orientada a eventos** | Su complejidad operativa no se justifica con el volumen previsto; REST síncrono basta. |
| **Interfaz dentro del servicio E-commerce** | Mezclaría la UI del personal de sucursal con la venta y rompería la independencia de los servicios. |
| **SPA que llama a las API desde el navegador** | Expone el JWT al navegador, exige CORS y añade otro stack al proyecto. |
| **Guardar los archivos digitales en la VM o en PostgreSQL** | El disco es escaso, los archivos pasarían por una VM de 1 OCPU y inflarían los respaldos. Ver AD19. |
| **Capas tradicionales** | El dominio quedaría atado a la base de datos y a los proveedores externos, y sería más difícil de probar. |

## 6. Ventajas y limitaciones

| Ventajas | Limitaciones |
| :--- | :--- |
| Aísla el inventario, la parte más crítica, del resto del sistema. | La comunicación síncrona hace que el E-commerce dependa de la disponibilidad del Inventario. |
| Pocos servicios: despliegue y mantenimiento sencillos con Docker Compose. | Identidad y Redis son puntos compartidos que requieren cuidado. |
| Reglas de negocio aisladas y fáciles de probar. | Clean Architecture agrega interfaces y proyectos, lo que implica más código base para un sistema pequeño. |
| Se adapta a recursos gratuitos limitados. | Las bases separadas exigen mantener la coherencia entre ventas e inventario mediante llamadas REST y reservas, no con transacciones entre servicios. |
| Cada sucursal nueva es una fuente, no un cambio de código. | Si la red superara ampliamente las 20 sucursales, habría que revisar el estilo. |
| Un servicio caído no arrastra a los demás: la matriz de fallos está en [despliegue](05-despliegue.md) §7. | Más VMs en serie, sin redundancia, suman puntos de falla y llamadas de red. |

## 7. Alineación con el estilo basado en servicios

En su formulación habitual (M. Richards y N. Ford, *Fundamentals of Software Architecture*), el estilo se reconoce por las características de la tabla. Esta tabla muestra cómo las cumple BabelHub y dónde se aparta, para que el estilo declarado coincida con lo que se construye.

| Característica del estilo | Cómo se cumple en BabelHub | Evidencia |
| :--- | :--- | :--- |
| Pocos servicios de dominio y de grano grueso (habitualmente entre 4 y 12) | Cuatro servicios: E-commerce, Inventario, Identidad y Asistente. Cada uno agrupa varias capacidades (E-commerce reúne catálogo, carrito, pagos, pedidos y más). | AD02 |
| Cada servicio es una unidad de despliegue independiente | Imagen, versión y configuración propias. Ningún servicio referencia el Dominio, la Aplicación ni la Infraestructura de otro; solo se hablan por REST. `BabelHub.Shared` contiene únicamente contratos. | AGENTS.md §3 (reglas 5 y 6); `tests/architecture` |
| La interfaz se separa de los servicios | `BabelHub.Web` es un cliente sin reglas de negocio que llama a los servicios solo desde el servidor. | AD15 |
| Cada servicio tiene estructura interna propia | Clean Architecture en cuatro capas; versión reducida en Identidad y Asistente. | AD09 |
| La base de datos suele ser **compartida**; dividirla por dominio es una variante válida | Una base lógica por servicio con rol exclusivo, en una instancia. Se eligió dividirla para aislar los datos de seguridad (Identidad) y el stock (Inventario) con más fuerza que unos esquemas. No hay tablas ni consultas entre bases. | AD03, AD18; [despliegue](05-despliegue.md) §5 |
| Comunicación remota síncrona, sin mensajería obligatoria | REST síncrono; sin bus de eventos. | AD04 |
| Transacciones ACID dentro de cada servicio; entre servicios, coordinación | La reserva con expiración, las claves de idempotencia y la validación definitiva contra la base sustituyen las transacciones distribuidas. | AD08, AD23 |
| Los fallos y el escalamiento se acotan por servicio | Cada servicio puede reiniciarse, actualizarse o moverse de VM por separado; la matriz de fallos define el efecto de cada caída. | [despliegue](05-despliegue.md) §3 y §7 |
| *Una VM por servicio no es un requisito del estilo* | El reparto en VMs responde a capacidad y riesgo (aislar lo crítico, separar el estado), no al estilo. El perfil mínimo corre todos los servicios en una VM sin dejar de ser el mismo estilo, a costa de aislamiento. | AD11; [despliegue](05-despliegue.md) §2 y §3 |

**Dónde se aproxima o se aparta (a conciencia):**
- **Base por servicio** en lugar de una compartida: más aislamiento a cambio de no poder cruzar datos entre servicios con una consulta. Es la razón por la que las reservas y los pedidos se coordinan por REST.
- **Cuatro servicios**, en el extremo bajo del rango habitual: con 5–10 sucursales y 12 GB por cuenta, más servicios no aportarían escalamiento y sí coordinación (DA09).
- **Identidad y Asistente en versión reducida**: no tienen reglas de negocio complejas; mantienen las capas necesarias y la prueba de arquitectura.
- **Dependencia síncrona E-commerce → Inventario**: es el costo de REST sin eventos; se acota con caché, tiempos de espera y el mensaje de «no se puede validar el stock».

**Cuándo revisar el estilo:** más de unas 20 sucursales; un servicio que necesite escalar de forma muy distinta al resto (por ejemplo, el catálogo, que habría que separar de E-commerce); o un requisito de procesamiento asíncrono masivo que justifique mensajería.
