# Contexto y contenedores (niveles 1 y 2)

## 1. Nivel 1: contexto del sistema

BabelHub es un único sistema de software con dos subsistemas de negocio (E-commerce e Inventario) que usan personas de cuatro roles y que se apoya en servicios externos.

```mermaid
flowchart LR
CLI["Cliente<br/>compra libros impresos y digitales"]
VIS["Visitante<br/>navega, usa el asistente y registra reclamos"]
ENC["Encargado de sucursal<br/>gestiona el stock y los pedidos de su sucursal"]
LOG["Personal de logística<br/>traslados y guías de envío"]
ADM["Administrador central<br/>catálogo, usuarios, auditoría y reclamos"]

BH(["BabelHub<br/>tienda en línea e inventario multi-sucursal"])

PAG["Culqi<br/>pagos con Yape y tarjeta"]
ENV["Operador logístico<br/>guías y seguimiento"]
LLM["Proveedor de LLM<br/>respuestas del asistente"]
WA["WhatsApp<br/>vía n8n y Evolution API"]
COR["Proveedor de correo<br/>códigos, respuestas y alertas"]
ALM["Almacén de archivos<br/>libros digitales y respaldos"]

CLI --> BH
VIS --> BH
ENC --> BH
LOG --> BH
ADM --> BH
BH --> PAG
BH --> ENV
BH --> LLM
BH --> WA
BH --> COR
BH --> ALM
CLI -.->|"descarga con enlace firmado"| ALM

classDef n fill:#222,stroke:#fff,color:#fff
classDef s fill:#444,stroke:#fff,stroke-width:3px,color:#fff
class CLI,VIS,ENC,LOG,ADM,PAG,ENV,LLM,WA,COR,ALM n
class BH s
```

| Actor o sistema | Relación | Fuente |
| :--- | :--- | :--- |
| Cliente y visitante | Compran, consultan pedidos y biblioteca, usan el asistente y registran reclamos. | `docs/01-analisis-de-sistema/05-actores-y-permisos.md` |
| Encargado, logística y administración | Operan el inventario, los traslados, los envíos, el catálogo y la auditoría según su rol y alcance. | Ídem |
| Culqi | Cobra y reembolsa; avisa el estado del pago. | AD17, RC04 |
| Operador logístico | Genera guías de envío. | RC05 |
| Proveedor de LLM | Genera las respuestas del asistente; recibe solo datos mínimos. | RC10, RC17 |
| WhatsApp (n8n + Evolution API) | Avisos de pedidos y traslados con consentimiento. | AD16, RC12 |
| Proveedor de correo | Códigos de seguridad, respuestas a reclamos y alertas a la administración. | AD30, RC18 |
| Almacén de archivos | Guarda los EPUB y PDF y los respaldos cifrados; entrega con URL firmada. | AD19, AD27 |

## 2. Nivel 2: contenedores

Un contenedor es una aplicación o un almacén de datos desplegable por separado. Las VMs, los límites de memoria y la red están en el [despliegue](../02-arquitectura-de-software/05-despliegue.md).

```mermaid
flowchart TD
NAV["Navegador<br/>cliente, sucursal, administración"]

subgraph BORDE["Borde de red"]
CF["Cloudflare<br/>DNS, TLS, WAF y túnel"]
NGX["Nginx<br/>enrutamiento interno"]
end

subgraph APP["VM de aplicaciones - Docker Compose"]
WEB["BabelHub.Web<br/>ASP.NET Core MVC, Razor y htmx"]
ECO["Servicio de E-commerce<br/>ASP.NET Core"]
INV["Servicio de Inventario<br/>ASP.NET Core"]
IDE["Servicio de Identidad<br/>ASP.NET Core"]
ASI["Servicio de Asistente<br/>ASP.NET Core"]
RED[("Redis<br/>caché")]
N8N["n8n + Evolution API<br/>avisos por WhatsApp"]
end

subgraph DAT["VM de datos"]
BDV[("PostgreSQL Ventas<br/>esquemas ventas e identidad")]
BDI[("PostgreSQL Inventario")]
end

subgraph EXT["Sistemas externos"]
PAG["Culqi"]
ENV["Operador logístico"]
LLM["Proveedor de LLM"]
COR["Proveedor de correo"]
ALM[("Almacén de archivos")]
WA["WhatsApp"]
end

NAV -->|"HTTPS"| CF
CF --> NGX
NGX --> WEB
WEB -->|"REST"| ECO
WEB -->|"REST"| INV
WEB -->|"REST"| IDE
WEB -->|"REST"| ASI
ECO -->|"REST"| INV
ASI -->|"REST"| ECO
ASI -->|"REST"| INV
ECO -.->|"valida credenciales"| IDE
INV -.->|"valida credenciales"| IDE
ASI -.->|"valida credenciales"| IDE
ECO --> BDV
IDE --> BDV
INV --> BDI
ECO --> RED
INV --> RED
ECO -->|"webhook"| N8N
INV -->|"webhook"| N8N
N8N --> WA
ECO --> PAG
ECO --> ENV
ECO -->|"sube y firma URLs"| ALM
IDE -->|"códigos y alertas"| COR
ECO -->|"respuestas a reclamos"| COR
ASI --> LLM
NAV -.->|"descarga con URL firmada"| ALM

classDef n fill:#222,stroke:#fff,color:#fff
class NAV,CF,NGX,WEB,ECO,INV,IDE,ASI,RED,N8N,BDV,BDI,PAG,ENV,LLM,COR,ALM,WA n
```

| Contenedor | Tecnología | Responsabilidad | Specs principales |
| :--- | :--- | :--- | :--- |
| **BabelHub.Web** | ASP.NET Core MVC, Razor, htmx, Tailwind y daisyUI | Interfaz por áreas (Tienda, Sucursal, Admin); sesión del lado del servidor; mezcla de la auditoría y las alertas de los servicios. Sin reglas de negocio. | `web-base`, `web-indice-infinito` |
| **Servicio de E-commerce** | ASP.NET Core, EF Core | Catálogo, carrito, pagos, pedidos, biblioteca digital, envíos, devoluciones y Libro de Reclamaciones. | `ecommerce-*`, `transversal-notificaciones` |
| **Servicio de Inventario** | ASP.NET Core, EF Core | Fuentes y rutas, stock vendible, reservas y selección de fuente, traslados y ajustes. | `inventario-*` |
| **Servicio de Identidad** | ASP.NET Core, EF Core | Usuarios, roles, credenciales y su firma, segundo factor, recuperación, consentimientos y solicitudes sobre datos personales. | `identidad-seguridad` |
| **Servicio de Asistente** | ASP.NET Core | Recomendaciones y consultas con datos de los servicios; solo lectura. | `asistente-compra-ia` |
| **Redis** | Redis | Caché de disponibilidad y catálogo. | AD06 |
| **n8n + Evolution API** | n8n, Evolution API | Plantillas y envío de avisos por WhatsApp. | AD16 |
| **PostgreSQL Ventas / Inventario** | PostgreSQL | Datos de cada sistema; sin acceso desde Internet. | AD03, AD18 |
| **Nginx y Cloudflare** | Nginx, Cloudflare | Entrada única, TLS, WAF y límites en el borde. | AD13, AD14 |

Los tres elementos transversales (**auditoría** y **seguridad**: límites, errores y eventos) no son contenedores: se implementan en cada servicio con el mismo contrato (`transversal-auditoria`, `transversal-seguridad`).

## 3. Fronteras de confianza

Se dibujan sobre este nivel en el [modelo de amenazas](../05-seguridad/01-modelo-de-amenazas.md) §2: Internet → borde, borde → aplicaciones, aplicaciones → datos y aplicaciones ↔ proveedores externos.
