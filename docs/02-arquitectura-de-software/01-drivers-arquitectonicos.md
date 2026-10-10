# Drivers arquitectónicos

Un **driver arquitectónico** es un requisito, atributo de calidad o restricción que condiciona de forma significativa la estructura del sistema. Se seleccionaron los que, si cambiaran, obligarían a cambiar la arquitectura.

## 1. Cadena de derivación

```mermaid
flowchart LR
NN["Necesidades<br/>NN01 - NN12"] --> REQ["Requisitos<br/>RF, RNF, RC"]
REQ --> AC["Atributos de calidad<br/>AC01 - AC08"]
AC --> DA["Drivers<br/>DA01 - DA12"]
REQ --> DA
DA --> AD["Decisiones<br/>AD01 - AD21"]
AD --> EST["Estilo y estructura<br/>servicios + Clean Architecture"]

style NN fill:#222,stroke:#fff,color:#fff
style REQ fill:#222,stroke:#fff,color:#fff
style AC fill:#222,stroke:#fff,color:#fff
style DA fill:#222,stroke:#fff,color:#fff
style AD fill:#222,stroke:#fff,color:#fff
style EST fill:#222,stroke:#fff,color:#fff
```

## 2. Drivers

| ID | Driver arquitectónico | Tipo | Origen | ¿Por qué influye en la arquitectura? | Decisiones |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **DA01** | El sistema debe evitar la sobreventa cuando un título está en varias sucursales. | Calidad | AC01, RNF01, RF13, RF14 | Exige un servicio y una base de datos propios para el inventario, con reservas transaccionales y control de concurrencia. | AD01, AD03, AD08 |
| **DA02** | El sistema debe mantener tiempos de respuesta adecuados al consultar disponibilidad en picos de tráfico. | Calidad | AC02, RNF02 | Influye en el uso de caché, en la comunicación entre servicios y en el reparto de recursos de las VMs. | AD04, AD06 |
| **DA03** | El sistema debe seguir operando de forma aceptable sin SLA y con fallos parciales. | Calidad | AC03, RNF03 | Condiciona el reinicio automático, la degradación controlada ante la caída del inventario y los respaldos. | AD04, AD10, AD11 |
| **DA04** | El sistema debe permitir sumar sucursales sin rediseñar. | Calidad | AC04, RNF04 | Obliga a modelar cada sucursal como dato (fuente) y no como código o esquema. | AD01, AD03 |
| **DA05** | Cada encargado solo debe acceder a su sucursal, y los datos de usuarios y pagos deben protegerse. | Calidad | AC05, RNF05–RNF08, RF18 | Influye en la autenticación centralizada, el aislamiento de las bases de datos, la protección del borde de red y que el token no llegue al navegador. | AD05, AD12, AD13, AD15, AD18 |
| **DA06** | Debe integrarse con pasarela de pago, servicio de envío, LLM y WhatsApp. | Restricción | RC04, RC05, RC10, RC12 | Obliga a aislar los accesos externos para poder cambiar de proveedor sin tocar las reglas del negocio. | AD07, AD09, AD16, AD17 |
| **DA07** | La comunicación entre componentes debe ser mediante API REST, y el sistema debe tener una interfaz web responsiva para tres tipos de usuario. | Restricción | RC01, RC03, RC06, RNF13 | Limita las alternativas de comunicación, descarta un bus de eventos y exige un componente de interfaz que consuma las API sin romper la independencia de los servicios. | AD04, AD15 |
| **DA08** | Los cambios de reglas o proveedores deben quedar aislados y ser fáciles de probar. | Calidad | AC06, RNF09, RNF10 | Influye en la organización interna de cada servicio. | AD09 |
| **DA09** | El sistema debe ejecutarse en recursos gratuitos limitados y heterogéneos (ARM64 y x86_64). | Restricción | RC06–RC08, RNF11, RNF12, AC07 | Limita la cantidad de VMs, servicios y contenedores, y exige imágenes multi-arch. | AD02, AD10, AD11 |
| **DA10** | La operación debe tener costo mínimo y esfuerzo técnico manejable. | Negocio | NN12, RC09 | Favorece servicios gratuitos gestionados (Cloudflare) frente a componentes propios que haya que mantener. | AD13, AD14 |
| **DA11** | El cliente y la sucursal deben recibir avisos de estado por WhatsApp sin costo por mensaje y sin que el aviso bloquee la compra. | Negocio / Restricción | RF07, RC12, NN03, NN12 | Obliga a un puerto de notificación aislado, a un envío tolerante a fallos y a alojar n8n y Evolution API dentro del presupuesto de recursos. | AD16 |
| **DA12** | La librería vende libros digitales con licencia de terceros, y el archivo debe llegar solo a quien lo compró. | Calidad / Restricción legal | NN13, NN14, NN15, RF22–RF24, RNF14, RC13, AC09 | Exige un almacén de archivos fuera de las VMs, entrega por enlaces firmados, un modelo de licencia por título (titular, territorio, vigencia) y que la compra digital no dependa del Inventario. | AD19, AD20 |

## 3. Riesgos y compromisos

| Riesgo o compromiso | Mitigación |
| :--- | :--- |
| La consulta síncrona al Inventario puede volverse cuello de botella o punto único de falla (DA02, DA03). | Caché Redis, reinicio automático, pruebas de carga con k6 y un mensaje claro cuando no se pueda validar el stock. |
| La disponibilidad en caché puede estar desactualizada (DA01, DA02). | Expiración corta y validación definitiva contra la base de datos al reservar. |
| La reducción del Free Tier de Oracle (ahora 2 OCPU y 12 GB en ARM) deja poca holgura de CPU (DA09). | Dimensionar con 1 OCPU por VM, medir con k6 y evitar componentes pesados. |
| Un archivo digital se filtra o se redistribuye fuera de la tienda (DA12). | Enlaces firmados de 5 minutos, límite de descargas, registro de cada descarga y auditoría; la marca de agua personalizada queda como mejora posterior. No existe protección total sin DRM, y se acepta como riesgo mientras ningún titular lo exija. |
| La licencia de un título vence o cambia, o el titular exige DRM comercial (DA12). | El catálogo guarda vigencia y territorio por título (RF24); el puerto `AlmacenDeArchivos` y el caso de uso de descarga permiten incorporar un DRM sin tocar el dominio. |
| Cambian las cuotas gratuitas del almacén de archivos (DA10, DA12). | El almacén va detrás de un puerto; se verifican cuotas vigentes antes de contratar y se limita el tamaño de archivo y de catálogo digital. |
| Oracle puede no tener capacidad ARM al crear la VM, o recuperar instancias inactivas (DA03, DA09). | Reintentar la creación, mantener el mismo Compose en local como respaldo y revisar la política de instancias inactivas. |
| El asistente depende de un LLM externo (latencia, costo, respuestas incorrectas) (DA06). | Responde solo con datos de las API, indica cuándo no sabe y su caída no bloquea la compra. |
| Cloudflare es una dependencia de terceros en el camino de todas las solicitudes (DA10). | Aceptable para este alcance; se documenta la alternativa de exponer Nginx directamente. |
| Evolution API no es un canal oficial de WhatsApp: el número puede ser bloqueado o el protocolo cambiar sin aviso (DA11). | Usar un número dedicado, volumen bajo y solo mensajes transaccionales con consentimiento; el puerto `Notificador` permite sustituirlo (WhatsApp Cloud API oficial o correo) sin tocar el negocio. Una falla de envío nunca bloquea la compra. |
| n8n y Evolution API consumen RAM y CPU en una VM con 1 OCPU (DA09, DA11). | Límites de memoria por contenedor, medición con k6 y reparto de carga: Evolution API usa la Redis existente y una base propia en la VM-DATOS. |
