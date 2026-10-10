# 02 — Requisitos

Los requisitos derivan de las necesidades del documento `01-necesidad-del-negocio.md` (columna **Origen**). Se dividen en funcionales (qué hace el sistema), no funcionales (qué tan bien debe hacerlo) y restricciones (condiciones impuestas).

## 1. Requisitos funcionales

Prioridad: **Alta**, **Media**, **Baja**.

### 1.1 Sistema de E-commerce

| ID | Requisito funcional | Prioridad | Origen |
| :--- | :--- | :---: | :--- |
| **RF01** | El sistema debe permitir buscar libros por título, autor, editorial, categoría y rango de precio, y filtrar por disponibilidad. | Alta | NN01 |
| **RF02** | El sistema debe permitir consultar la información de un libro y su disponibilidad, consolidada y por sucursal. | Alta | NN01 |
| **RF03** | El sistema debe permitir agregar, modificar y eliminar libros del carrito, conservándolo entre sesiones y dispositivos. | Alta | NN02 |
| **RF04** | El sistema debe permitir generar un pedido a partir del carrito, eligiendo despacho a domicilio o recojo en sucursal, y validar la disponibilidad real (stock vendible) antes de confirmar el pago. | Alta | NN02 |
| **RF05** | El sistema debe procesar el pago del pedido una sola vez mediante la pasarela de pago, sin almacenar datos de tarjeta. | Alta | NN02 |
| **RF06** | El sistema debe permitir consultar los pedidos realizados, su detalle y su estado (confirmado, en preparación, en tránsito entre sucursales, despachado o listo para recojo, entregado, devuelto). | Alta | NN03 |
| **RF07** | El sistema debe notificar al cliente y a la sucursal responsable ante cada cambio de estado del pedido. | Alta | NN03 |
| **RF08** | El sistema debe ofrecer un asistente de compra que recomiende libros y responda sobre disponibilidad y estado de pedidos usando datos reales del catálogo e inventario, e indique cuando no tenga información suficiente. | Media | NN04 |
| **RF09** | El sistema debe permitir solicitar una devolución y gestionar su reembolso con trazabilidad. | Baja | NN05 |
| **RF10** | El sistema debe permitir registrar, actualizar y dar de baja libros en el catálogo, validando que el ISBN sea único. | Alta | NN08 |
| **RF11** | El sistema debe generar guías de envío y consultar el seguimiento con el servicio de envío en los despachos a domicilio. | Media | NN11 |

### 1.2 Sistema de Inventario Multi-Sucursal

| ID | Requisito funcional | Prioridad | Origen |
| :--- | :--- | :---: | :--- |
| **RF12** | El sistema debe registrar cada sucursal como una fuente independiente y permitir consultar su stock físico, reservado y vendible por título. | Alta | NN06 |
| **RF13** | El sistema debe calcular el stock vendible como *stock físico − reservas activas* y exponerlo mediante una API REST consultable por el E-commerce y el asistente. | Alta | NN01, NN06 |
| **RF14** | El sistema debe crear reservas a solicitud del E-commerce, confirmarlas al aprobarse el pago y liberarlas si el pedido se cancela o expira. | Alta | NN02, NN06 |
| **RF15** | El sistema debe seleccionar automáticamente la sucursal desde la cual atender un pedido, priorizando cercanía al cliente y disponibilidad, e identificar otras sucursales con stock cuando la más cercana no lo tenga. | Alta | NN07 |
| **RF16** | El sistema debe generar, aprobar, despachar, recibir y dar seguimiento a traslados entre sucursales, notificando a origen y destino, y actualizando el stock de ambas. | Alta | NN07 |
| **RF17** | El sistema debe registrar ajustes de inventario con motivo (merma, daño, corrección de conteo) y usuario responsable, por sucursal. | Alta | NN06 |

### 1.3 Seguridad, administración y analítica

| ID | Requisito funcional | Prioridad | Origen |
| :--- | :--- | :---: | :--- |
| **RF18** | El sistema debe autenticar a los usuarios y emitir tokens que incluyan su rol y la sucursal a la que pueden acceder. | Alta | NN09 |
| **RF19** | El sistema debe permitir registrar, actualizar y desactivar usuarios, y asignarles rol (cliente, encargado de sucursal, administrador central, logística) y sucursal. | Alta | NN09 |
| **RF20** | El sistema debe registrar en auditoría las operaciones sensibles: cambios de precio, anulaciones de pedido, ajustes de inventario y aprobación de traslados. | Media | NN09 |
| **RF21** | El sistema debe ofrecer indicadores de venta, inventario y traslados por sucursal y consolidados nacionales. *(Fase posterior.)* | Baja | NN10 |

**Justificación:** los requisitos se agrupan por sistema porque cada uno se implementa en un servicio distinto (ver `05-decisiones-arquitectonicas.md`). RF04, RF13 y RF14 forman el camino crítico del problema: validar, calcular y reservar el stock antes de cobrar.

## 2. Requisitos no funcionales

Las metas numéricas son **propuestas iniciales** para una red de 5 a 10 sucursales y deben validarse con pruebas de carga (k6) en la etapa de implementación.

| ID | Categoría | Requisito no funcional | Meta | Atributo |
| :--- | :--- | :--- | :--- | :---: |
| **RNF01** | Consistencia | El stock vendible nunca debe ser negativo, ni siquiera ante solicitudes simultáneas sobre la última unidad. | 0 casos de sobreventa en las pruebas de concurrencia. | AC01 |
| **RNF02** | Rendimiento | La consulta de disponibilidad y las operaciones del carrito deben responder rápido en picos de tráfico. | p95 < 500 ms con hasta 50 usuarios concurrentes; confirmación de checkout p95 < 3 s (sin contar la pasarela). | AC02 |
| **RNF03** | Disponibilidad | La instancia pública debe estar disponible a cualquier hora y recuperarse sola de fallos de contenedores. | ≥ 99 % mensual (sin SLA contratado); reinicio automático en menos de 2 minutos; catálogo consultable desde caché si Inventario no responde. | AC03 |
| **RNF04** | Escalabilidad | Incorporar sucursales debe ser una operación de datos, no de código. | Hasta 20 fuentes sin cambios de código ni de esquema. | AC04 |
| **RNF05** | Seguridad: acceso | Cada encargado solo puede acceder al inventario y pedidos de su propia sucursal. | 100 % de accesos fuera de alcance rechazados (HTTP 403) y registrados. | AC05 |
| **RNF06** | Seguridad: transporte y secretos | Todo el tráfico externo debe viajar cifrado; las bases de datos no deben ser accesibles desde Internet; las claves (pagos, LLM) no deben estar en el repositorio. | TLS en el 100 % del tráfico externo; 0 puertos de BD expuestos; 0 secretos en Git. | AC05 |
| **RNF07** | Seguridad: datos y pagos | No se almacenan datos de tarjeta, y los datos personales se tratan conforme a la normativa peruana. | Cumplimiento de la Ley N.° 29733; datos de pago solo en la pasarela. | AC05 |
| **RNF08** | Auditabilidad | Las operaciones sensibles y los movimientos de stock deben ser trazables y no modificables. | 100 % de operaciones sensibles con usuario, fecha y detalle. | AC05 |
| **RNF09** | Mantenibilidad | Los cambios (nueva regla de selección de fuente, otra pasarela) deben quedar aislados. | Cambio localizado en una capa de un servicio, sin tocar el dominio ni otros servicios. | AC06 |
| **RNF10** | Testabilidad | La lógica de negocio debe poder probarse sin base de datos ni servicios externos. | Cada criterio de aceptación con al menos una prueba (unitaria, de integración o E2E). | AC06 |
| **RNF11** | Portabilidad | El sistema debe ejecutarse igual en ARM64 y x86_64. | Imágenes Docker multi-arch; despliegue con un comando por VM. | AC07 |
| **RNF12** | Eficiencia de recursos | El sistema completo debe operar dentro del Free Tier de Oracle. | Uso sostenido de memoria < 70 % por VM; sin costo mensual. | AC07 |
| **RNF13** | Usabilidad | La interfaz debe ser responsiva y mostrar estados claros (vacío, cargando, error, con datos). | Compra completada en no más de 5 pasos desde el catálogo. | AC08 |

## 3. Restricciones

| ID | Restricción | Descripción | Justificación |
| :--- | :--- | :--- | :--- |
| **RC01** | Aplicación web | El sistema se accede desde un navegador, con diseño responsivo. | Alcance inicial; la app móvil queda como extensión. |
| **RC02** | Control de versiones | El código se gestiona con Git en un repositorio compartido. | Requisito del curso y del trabajo en equipo. |
| **RC03** | API REST | La comunicación entre frontend y servicios, y entre E-commerce e Inventario, es por REST. | Es simple y uniforme; no se justifica un bus de eventos para 5–10 sucursales. |
| **RC04** | Pasarela de pago | Integración con Niubiz o Culqi para Yape y tarjetas, con comisión por transacción y sin costo fijo. | Cumple el objetivo de costo mínimo (NN12). |
| **RC05** | Servicio de envío | Integración con un servicio externo de envío para despachos a domicilio. | La entrega no es parte del dominio del sistema. |
| **RC06** | Tecnologías base | Backend en .NET 10 (ASP.NET Core), PostgreSQL, Redis y Docker con Docker Compose. | Decisión del proyecto; Compose basta para 2–4 VMs (Kubernetes no se justifica). |
| **RC07** | Infraestructura | Despliegue en Oracle Cloud Free Tier: hasta 2 VMs ARM (en total 2 OCPU y 12 GB de RAM) y 2 VMs x86 micro (1/8 OCPU y 1 GB cada una), con 200 GB de almacenamiento en bloque. | Recursos gratuitos vigentes; ver `05-decisiones-arquitectonicas.md`. |
| **RC08** | Sistema operativo | Linux en instalación mínima, sin entorno gráfico, en todas las VMs. | Reduce el consumo de RAM, que es el recurso más escaso. |
| **RC09** | Borde de red | Se usa el plan gratuito de Cloudflare (DNS, proxy, TLS y WAF básico); requiere un dominio propio. | Protege el origen sin costo de infraestructura. |
| **RC10** | LLM externo | El asistente usa un modelo de lenguaje de terceros y consulta los datos solo mediante las API del sistema. | Mantiene el aislamiento de las bases de datos. |
| **RC11** | Documentación | Se documenta con el modelo C4 e IEEE 1016, y el desarrollo posterior sigue Spec-Driven Development. | Marco de la propuesta original. |

## 4. Trazabilidad necesidad → requisitos

| Necesidad | Requisitos funcionales | Requisitos no funcionales |
| :--- | :--- | :--- |
| NN01 | RF01, RF02, RF13 | RNF01, RNF02 |
| NN02 | RF03, RF04, RF05, RF14 | RNF01, RNF02, RNF07 |
| NN03 | RF06, RF07 | RNF03 |
| NN04 | RF08 | RNF03 |
| NN05 | RF09 | — |
| NN06 | RF12, RF13, RF14, RF17 | RNF01, RNF08 |
| NN07 | RF15, RF16 | RNF01, RNF04 |
| NN08 | RF10 | RNF09 |
| NN09 | RF18, RF19, RF20 | RNF05, RNF06, RNF08 |
| NN10 | RF21 | — |
| NN11 | RF11 | — |
| NN12 | — | RNF11, RNF12 (y RC07, RC09) |
