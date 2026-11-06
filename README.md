# BabelHub — Plataforma de comercio electrónico y gestión de inventario multi-sucursal para librerías

## Datos del proyecto

| Información | Detalle |
| :--- | :--- |
| Universidad | Universidad Nacional de San Cristóbal de Huamanga |
| Escuela | Ingeniería de Sistemas |
| Curso | Arquitectura de Software IS-488 — Laboratorio |
| Docente | Ing. Lizbeth Jaico Quispe |
| Estudiante | Diego Rodrigo Amiquero Antonio |

## Descripción

BabelHub se compone de **dos sistemas independientes pero interoperables** para una librería con presencia en varias regiones del Perú (5 a 10 sucursales):

- **Sistema de E-commerce:** catálogo de libros impresos y digitales, carrito, checkout, pagos, pedidos, biblioteca digital y asistente de compra.
- **Sistema de Inventario Multi-Sucursal:** cada sucursal es una fuente de stock (patrón *Multi-Source Inventory*); *stock vendible = stock físico − reservas activas*.

**Caso de estudio:** Librerías Crisol (recojo en tienda) y el patrón MSI de Adobe Commerce.

## Documentación

| Carpeta | Contenido |
| :--- | :--- |
| [01 Análisis de sistema](docs/01-analisis-de-sistema/) | [Necesidad del negocio](docs/01-analisis-de-sistema/01-necesidad-del-negocio.md) (NN), [requisitos](docs/01-analisis-de-sistema/02-requisitos.md) (RF, RNF, RC), [marco legal](docs/01-analisis-de-sistema/03-marco-legal.md) y [atributos de calidad](docs/01-analisis-de-sistema/04-atributos-de-calidad.md) (AC). |
| [02 Arquitectura de software](docs/02-arquitectura-de-software/) | [Drivers](docs/02-arquitectura-de-software/01-drivers-arquitectonicos.md) (DA), [decisiones](docs/02-arquitectura-de-software/02-decisiones-arquitectonicas.md) (AD), [estilo](docs/02-arquitectura-de-software/03-estilo-arquitectonico.md), [escenarios y flujos](docs/02-arquitectura-de-software/04-escenarios-y-flujos.md) y [despliegue en Oracle Cloud](docs/02-arquitectura-de-software/05-despliegue.md), [tácticas](docs/02-arquitectura-de-software/06-tacticas-arquitectonicas.md) y [presupuesto de recursos](docs/02-arquitectura-de-software/07-presupuesto-de-recursos.md). |
| [03 Diseño de software](docs/03-diseno-de-software/) | [Clean Architecture](docs/03-diseno-de-software/01-clean-architecture.md), [organización interna](docs/03-diseno-de-software/02-organizacion-interna.md), [patrones](docs/03-diseno-de-software/03-principios-y-patrones.md), [módulos y contratos](docs/03-diseno-de-software/04-modulos-y-contratos.md), [modelo de datos](docs/03-diseno-de-software/05-modelo-de-datos.md), [estados y concurrencia](docs/03-diseno-de-software/06-estados-y-concurrencia.md) y [mapeo a IEEE 1016](docs/03-diseno-de-software/07-mapeo-ieee-1016.md). |
| 04 Modelo C4 | Reservada; se desarrollará más adelante. |
| [05 Seguridad](docs/05-seguridad/) | Modelo de amenazas, controles ASVS, autenticación, privacidad, API e integraciones, seguridad del asistente, operación segura y mapeo a OWASP e ISO. |
| [06 Experiencia de usuario](docs/06-experiencia-de-usuario/) | [Identidad y sistema de diseño](docs/06-experiencia-de-usuario/01-identidad-y-sistema-de-diseno.md), [pantallas y recorridos](docs/06-experiencia-de-usuario/02-pantallas-y-recorridos.md) [estados, accesibilidad y criterios](docs/06-experiencia-de-usuario/03-estados-accesibilidad-y-criterios.md), [heurísticas](docs/06-experiencia-de-usuario/04-principios-y-heuristicas.md), [personas y tareas](docs/06-experiencia-de-usuario/05-personas-y-tareas.md), [accesibilidad WCAG](docs/06-experiencia-de-usuario/06-accesibilidad-wcag.md), [rendimiento y móvil](docs/06-experiencia-de-usuario/07-rendimiento-y-movil.md), [contenido y mensajes](docs/06-experiencia-de-usuario/08-contenido-y-mensajes.md) y [pruebas de usabilidad](docs/06-experiencia-de-usuario/09-pruebas-de-usabilidad.md). |
| [07 Proceso de desarrollo](docs/07-proceso-de-desarrollo/) | [Flujo SDD](docs/07-proceso-de-desarrollo/01-flujo-sdd.md), [convenciones técnicas](docs/07-proceso-de-desarrollo/02-convenciones-tecnicas.md) y [estrategia de pruebas](docs/07-proceso-de-desarrollo/03-estrategia-de-pruebas.md). |

Además: [glosario](docs/glossary.md), [índice de specs](specs/README.md) y [matriz de trazabilidad](specs/TRACEABILITY.md).

## Trazabilidad

`Necesidad (NN)` → `Requisito (RF, RNF, RC)` → `Atributo de calidad (AC)` → `Driver (DA)` → `Decisión (AD)` → `Estilo y estructura`

## Resumen de la arquitectura

- **Dos servicios** .NET 10 de granularidad gruesa, cada uno un **monolito modular** con Clean Architecture en cuatro capas: **E-commerce** (11 módulos: Catálogo, Carrito y checkout, Pagos, Pedidos, Envíos, Devoluciones, Biblioteca digital, Libro de Reclamaciones, Identidad, Asistente e interfaz web) e **Inventario** (4 módulos: Fuentes, Selección y reservas, Traslados y Ajustes), integrados por REST síncrono.
- La interfaz web es la capa de presentación del E-commerce (ASP.NET Core MVC con Razor y htmx), con áreas por rol (Tienda, Sucursal y Admin) en dos hosts: la tienda y la gestión del personal. Llama al Inventario por REST solo desde el servidor.
- PostgreSQL con una base por servicio (`ecommerce`, con un esquema por módulo, e `inventario`) en una instancia, un rol por esquema y seguridad por filas en Inventario; Redis como caché y Nginx como puerta de entrada.
- Pagos con Culqi (Yape y tarjetas) y avisos por WhatsApp con n8n y Evolution API, autoalojados.
- Libros digitales (EPUB y PDF) en un almacén de objetos externo, con descarga por URL firmada; no pasan por el Inventario. El marco legal, aplicado con rigurosidad laxa por ser un proyecto académico, está en [`docs/01-analisis-de-sistema/03-marco-legal.md`](docs/01-analisis-de-sistema/03-marco-legal.md).
- Despliegue con Docker Compose, un Compose por VM, **solo con recursos gratuitos de una cuenta de Oracle**: dos VMs AMD micro (borde de red; salto SSH y monitoreo) y dos VMs ARM de 1 OCPU y 6 GB (ventas; inventario con la base y una réplica de gestión).
- Cloudflare gratuito en el borde (DDoS, WAF, límites, Turnstile y Access en el host de gestión), con Cloudflare Tunnel de dos conectores, para no abrir puertos en Oracle.
- Pruebas con xUnit, Testcontainers, Playwright y k6. Desarrollo posterior bajo Spec-Driven Development.

## Desarrollo

| Comando | Para qué |
| :--- | :--- |
| `dotnet build BabelHub.sln -warnaserror` | Compilar sin advertencias. |
| `dotnet test BabelHub.sln` | Todas las pruebas, incluida la de arquitectura. |
| `dotnet run --project tools/SpecCheck` | Validar specs, planes, tareas y trazabilidad. |
| `python -m http.server 8081 --directory prototype/ui-v2` | Ver el prototipo de interfaz. |
| `docker compose -f infra/docker-compose.local.yml --env-file .env up -d` | Bases de datos y caché locales (copia antes `.env.example` a `.env`). |

El trabajo sigue el ciclo de [`docs/07-proceso-de-desarrollo/01-flujo-sdd.md`](docs/07-proceso-de-desarrollo/01-flujo-sdd.md): ninguna línea de producción sin una spec aprobada y un plan aprobado. Las reglas obligatorias están en `AGENTS.md`.
