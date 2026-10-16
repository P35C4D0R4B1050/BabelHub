# Componentes (nivel 3)

Cada diagrama abre un contenedor del [nivel 2](01-contexto-y-contenedores.md) y muestra sus **módulos** (casos de uso y reglas agrupados) y los **puertos** que usan, con la Infraestructura que los implementa. Son vistas de referencia a nivel de módulo: los nombres de clases y proyectos los fija el `plan.md` de cada spec. Convención: rectángulo = módulo; cilindro = almacén; óvalo = puerto.

## 1. Servicio de Inventario

```mermaid
flowchart TD
API["Api<br/>controladores v1, credencial, límites"]

subgraph APL["Aplicación y Dominio"]
FUE["Fuentes y disponibilidad<br/>FUE-01 a FUE-11"]
RUT["Rutas entre fuentes<br/>FUE-12 a FUE-14"]
RES["Reservas y selección de fuente<br/>SEL-01 a SEL-21"]
TRA["Traslados<br/>TRA-01 a TRA-17"]
AJU["Ajustes<br/>AJU-01 a AJU-12"]
EXP["Expiración de reservas<br/>trabajo de fondo"]
AUD["Auditoría y eventos de seguridad<br/>AUD, SEG"]
end

subgraph PUE["Puertos"]
PR(["Repositorio de inventario"])
PC(["Caché"])
PN(["Notificador"])
PA(["Registro de auditoría"])
PT(["Reloj y usuario actual"])
end

subgraph INF["Infraestructura"]
DB[("PostgreSQL Inventario")]
RE[("Redis")]
N8["n8n"]
end

API --> FUE
API --> RES
API --> TRA
API --> AJU
API --> RUT
RES -->|"stock vendible"| FUE
RES -->|"plazo y cercanía"| RUT
TRA -->|"consume y recrea la reserva"| RES
AJU -->|"stock físico"| FUE
EXP --> RES
FUE --> PR
RES --> PR
TRA --> PR
AJU --> PR
RUT --> PR
FUE --> PC
TRA --> PN
FUE --> AUD
RES --> AUD
TRA --> AUD
AJU --> AUD
RUT --> AUD
AUD --> PA
EXP --> PT
PR --> DB
PA --> DB
PC --> RE
PN --> N8

classDef n fill:#222,stroke:#fff,color:#fff
class API,FUE,RUT,RES,TRA,AJU,EXP,AUD,PR,PC,PN,PA,PT,DB,RE,N8 n
```

| Componente | Responsabilidad | Spec |
| :--- | :--- | :--- |
| Fuentes y disponibilidad | Fuente por sucursal, stock físico, reservado y vendible (nunca negativo), consulta consolidada y por fuente. | `inventario-fuentes` |
| Rutas entre fuentes | Plazo estimado en días entre dos fuentes, como dato; ordena fuentes por cercanía. | `inventario-fuentes` |
| Reservas y selección de fuente | Reservas temporales, límites por cliente y origen, selección de la fuente, consulta previa sin reservar y descuento de físico al entregar o pasar a en reparto. | `inventario-seleccion-fuente` |
| Traslados | Ciclo solicitado → recibido, cancelación al anular el pedido y movimiento de la reserva vinculada. | `inventario-traslados` |
| Ajustes | Merma, daño, corrección de conteo y reingreso por devolución. | `inventario-ajustes` |
| Expiración de reservas | Marca como expiradas las reservas vencidas; con credencial de servicio de mínimo privilegio. | `inventario-seleccion-fuente`, IDE-30 |
| Auditoría y eventos de seguridad | Contrato único de auditoría en la misma transacción, accesos denegados y límites excedidos. | `transversal-auditoria`, `transversal-seguridad` |

## 2. Servicio de E-commerce

```mermaid
flowchart TD
API["Api<br/>controladores v1, credencial, límites"]

subgraph APL["Aplicación y Dominio"]
CAT["Catálogo y licencias<br/>CAT"]
CAR["Carrito y checkout<br/>CAR"]
PAG["Pagos<br/>PAG"]
PED["Pedidos<br/>PED"]
BIB["Biblioteca digital<br/>BIB"]
ENV["Envíos<br/>ENV"]
DEV["Devoluciones<br/>DEV"]
REC["Libro de Reclamaciones<br/>REC"]
AUD["Auditoría y seguridad"]
end

subgraph PUE["Puertos"]
PI(["Cliente de inventario"])
PP(["Pasarela de pago"])
PE(["Servicio de envío"])
PN(["Notificador"])
PM(["Enviador de correo"])
PF(["Almacén de archivos"])
PR(["Repositorio de ventas"])
PC(["Caché"])
end

subgraph EXT["Infraestructura y externos"]
INV["Servicio de Inventario"]
CUL["Culqi"]
OPE["Operador logístico"]
N8["n8n"]
COR["Proveedor de correo"]
ALM[("Almacén de archivos")]
DB[("PostgreSQL Ventas")]
RE[("Redis")]
end

API --> CAT
API --> CAR
API --> PAG
API --> PED
API --> BIB
API --> ENV
API --> DEV
API --> REC
CAR -->|"precio y licencia"| CAT
CAR -->|"consulta y reserva"| PI
PAG -->|"cobra el pedido"| CAR
PAG -->|"confirma"| PED
PED -->|"concede accesos"| BIB
PED -->|"entrega, no recogido, anulación"| PI
ENV -->|"estado de la línea"| PED
ENV -->|"descuenta al pasar a en reparto"| PI
DEV -->|"reembolso"| PAG
DEV -->|"revoca acceso"| BIB
DEV -->|"reingreso"| PI
BIB --> PF
CAT --> PF
PAG --> PP
ENV --> PE
PED --> PN
DEV --> PN
REC --> PM
CAT --> PC
CAT --> PR
CAR --> PR
PED --> PR
BIB --> PR
REC --> PR
PI --> INV
PP --> CUL
PE --> OPE
PN --> N8
PM --> COR
PF --> ALM
PR --> DB
PC --> RE
CAT --> AUD
PED --> AUD
BIB --> AUD
REC --> AUD

classDef n fill:#222,stroke:#fff,color:#fff
class API,CAT,CAR,PAG,PED,BIB,ENV,DEV,REC,AUD,PI,PP,PE,PN,PM,PF,PR,PC,INV,CUL,OPE,N8,COR,ALM,DB,RE n
```

| Componente | Responsabilidad | Spec |
| :--- | :--- | :--- |
| Catálogo y licencias | Obras, ediciones, ISBN único, licencias digitales, búsqueda y validación de archivos y portadas subidos. | `ecommerce-catalogo` |
| Carrito y checkout | Carrito persistente, validación de stock, reserva, aceptación de textos legales y correo verificado. | `ecommerce-carrito-checkout` |
| Pagos | Cobro único con idempotencia, conciliación y reembolsos; confirma pedido, reservas y accesos. | `ecommerce-pagos` |
| Pedidos | Líneas y estados, recojo, no recogido y anulación. | `ecommerce-pedidos` |
| Biblioteca digital | Accesos, descargas con enlace firmado y tope, ayuda y restitución. | `ecommerce-biblioteca-digital` |
| Envíos | Guías, seguimiento y descuento de físico al pasar a en reparto. | `ecommerce-envios` |
| Devoluciones | Solicitud, aprobación, reembolso, reingreso y revocación de accesos. | `ecommerce-devoluciones` |
| Libro de Reclamaciones | Hojas, constancia, límite por origen, descarte sin borrado y respuesta por correo. | `ecommerce-libro-reclamaciones` |
| Notificador | Contrato de aviso por WhatsApp, que nunca interrumpe un caso de uso. | `transversal-notificaciones` |

## 3. Servicio de Identidad

Servicio de versión reducida: Presentación y Aplicación con acceso a datos simple, sin Dominio separado.

```mermaid
flowchart TD
API["Api<br/>registro, ingreso, credenciales, usuarios"]

subgraph APL["Aplicación"]
REG["Registro, verificación y aceptación de textos<br/>IDE-01, IDE-16, IDE-34"]
SES["Sesión y credenciales<br/>IDE-02, IDE-08, IDE-14, IDE-33"]
REC["Recuperación de acceso<br/>IDE-17 a IDE-19"]
MFA["Segundo factor y códigos de respaldo<br/>IDE-20 a IDE-22"]
USU["Usuarios, roles y sucursal<br/>IDE-04 a IDE-07, IDE-23"]
CTA["Cuenta, consentimientos y solicitudes ARCO<br/>IDE-11, IDE-12, IDE-24 a IDE-28, IDE-32"]
CLA["Claves de firma<br/>IDE-35, IDE-36"]
AUD["Auditoría, límites y eventos de seguridad"]
end

subgraph PUE["Puertos"]
PR(["Repositorio de identidad"])
PM(["Enviador de correo"])
PA(["Registro de auditoría"])
PT(["Reloj"])
end

DB[("PostgreSQL Ventas<br/>esquema identidad")]
COR["Proveedor de correo"]

API --> REG
API --> SES
API --> REC
API --> MFA
API --> USU
API --> CTA
SES -->|"firma"| CLA
SES --> MFA
REG --> PM
REC --> PM
USU --> PM
REG --> PR
SES --> PR
REC --> PR
MFA --> PR
USU --> PR
CTA --> PR
CLA --> PR
REG --> AUD
USU --> AUD
CTA --> AUD
AUD --> PA
PR --> DB
PA --> DB
PM --> COR
REC --> PT

classDef n fill:#222,stroke:#fff,color:#fff
class API,REG,SES,REC,MFA,USU,CTA,CLA,AUD,PR,PM,PA,PT,DB,COR n
```

## 4. Servicio de Asistente

Servicio de versión reducida y de solo lectura (AD28).

```mermaid
flowchart TD
API["Api<br/>consulta del cliente"]

subgraph APL["Aplicación"]
CON["Consulta<br/>valida tamaño, límites y alcance<br/>ASI-09, ASI-14"]
REC["Recomendación y disponibilidad<br/>ASI-01, ASI-02, ASI-10"]
PEDM["Estado de pedidos<br/>ASI-04, ASI-19"]
GUA["Guardas de salida<br/>texto plano, fundamento en datos, tope<br/>ASI-12, ASI-16, ASI-17"]
end

subgraph PUE["Puertos"]
PC(["Cliente de catálogo e inventario<br/>credencial de servicio de solo lectura"])
PP(["Cliente de pedidos<br/>credencial del cliente, 5 min, solo lectura"])
PL(["Proveedor de LLM"])
end

ECO["Servicio de E-commerce"]
INV["Servicio de Inventario"]
LLM["Proveedor de LLM"]

API --> CON
CON --> REC
CON --> PEDM
CON --> GUA
REC --> PC
PEDM --> PP
CON --> PL
PC --> ECO
PC --> INV
PP --> ECO
PL --> LLM

classDef n fill:#222,stroke:#fff,color:#fff
class API,CON,REC,PEDM,GUA,PC,PP,PL,ECO,INV,LLM n
```

El asistente no tiene acceso a ninguna base de datos ni herramientas de escritura. El filtro de los pedidos lo aplica el servicio de pedidos con la credencial del cliente.

## 5. BabelHub.Web

`BabelHub.Web` no es una capa de Clean Architecture sino un cliente de los servicios, sin Dominio ni Aplicación (AD15).

```mermaid
flowchart TD
subgraph AREAS["Áreas por rol"]
TIE["Tienda<br/>catálogo, carrito, checkout, biblioteca, cuenta, reclamos, índice infinito"]
SUC["Sucursal<br/>inventario, ajustes, pedidos de recojo, traslados, envíos"]
ADM["Admin<br/>catálogo, usuarios, pedidos, reclamos, auditoría, alertas"]
end

subgraph INF["Infraestructura de la Web"]
SESW["Sesión del servidor<br/>WEB-04, WEB-19, WEB-27, WEB-28"]
SEGW["Cabeceras, CSRF y errores<br/>WEB-05, WEB-07, WEB-18"]
LEG["Textos legales versionados<br/>WEB-08, WEB-29"]
MEZ["Mezcla de auditoría y alertas<br/>AUD-09, AUD-17, AUD-18"]
end

subgraph CLI["Clients - HttpClient tipados"]
CE["Cliente de E-commerce"]
CI["Cliente de Inventario"]
CD["Cliente de Identidad"]
CA["Cliente de Asistente"]
end

TIE --> SESW
SUC --> SESW
ADM --> SESW
ADM --> MEZ
TIE --> LEG
TIE --> CE
TIE --> CA
SUC --> CE
SUC --> CI
ADM --> CE
ADM --> CI
ADM --> CD
MEZ --> CE
MEZ --> CI
MEZ --> CD
SESW --> CD
AREAS --> SEGW

classDef n fill:#222,stroke:#fff,color:#fff
class TIE,SUC,ADM,SESW,SEGW,LEG,MEZ,CE,CI,CD,CA n
```

| Aspecto | Regla |
| :--- | :--- |
| Referencias | Solo a `BabelHub.Shared`; nunca al Dominio, Aplicación o Infraestructura de un servicio. |
| Llamadas | Solo desde el servidor; el navegador no ve credenciales ni llama a los servicios. |
| Descargas | La Web redirige al enlace firmado; no transporta el archivo. |
| Auditoría | Pide a cada servicio su página y mezcla por fecha; sin reglas de negocio. |

## 6. Elementos compartidos

| Elemento | Dónde vive | Regla |
| :--- | :--- | :--- |
| `BabelHub.Shared` | Referenciado por Api, Infraestructura y Web | Solo contratos (DTOs, constantes); no lo referencian Dominio ni Aplicación. |
| Auditoría y eventos de seguridad | En cada servicio, mismo contrato | Misma transacción que el cambio; si no se puede auditar, la operación falla (AUD-04). |
| Errores uniformes y límites | En cada Api | `problem+json` y respuesta única de límite excedido (SEG-03, SEG-07). |
