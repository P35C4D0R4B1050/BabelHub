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
| [02 Arquitectura de software](docs/02-arquitectura-de-software/) | [Drivers](docs/02-arquitectura-de-software/01-drivers-arquitectonicos.md) (DA), [decisiones](docs/02-arquitectura-de-software/02-decisiones-arquitectonicas.md) (AD), [estilo](docs/02-arquitectura-de-software/03-estilo-arquitectonico.md), [escenarios y flujos](docs/02-arquitectura-de-software/04-escenarios-y-flujos.md) y [despliegue en Oracle Cloud](docs/02-arquitectura-de-software/05-despliegue.md). |
| [03 Diseño de software](docs/03-diseno-de-software/) | [Clean Architecture](docs/03-diseno-de-software/01-clean-architecture.md) y [organización interna](docs/03-diseno-de-software/02-organizacion-interna.md). |
| 04 Modelo C4 | Reservada; se desarrollará más adelante. |
| 05 Seguridad | En preparación: amenazas, controles y estándares (OWASP, ISO). |
| [06 Experiencia de usuario](docs/06-experiencia-de-usuario/) | [Identidad y sistema de diseño](docs/06-experiencia-de-usuario/01-identidad-y-sistema-de-diseno.md), [pantallas y recorridos](docs/06-experiencia-de-usuario/02-pantallas-y-recorridos.md) y [estados, accesibilidad y criterios](docs/06-experiencia-de-usuario/03-estados-accesibilidad-y-criterios.md). |
| [07 Proceso de desarrollo](docs/07-proceso-de-desarrollo/) | [Flujo SDD](docs/07-proceso-de-desarrollo/01-flujo-sdd.md) y [convenciones técnicas](docs/07-proceso-de-desarrollo/02-convenciones-tecnicas.md). |

Además: [glosario](docs/glossary.md), [índice de specs](specs/README.md) y [matriz de trazabilidad](specs/TRACEABILITY.md).

## Trazabilidad

`Necesidad (NN)` → `Requisito (RF, RNF, RC)` → `Atributo de calidad (AC)` → `Driver (DA)` → `Decisión (AD)` → `Estilo y estructura`

## Resumen de la arquitectura

- Cuatro servicios .NET 10 de granularidad gruesa (E-commerce, Inventario, Identidad y Asistente de Compra), con REST síncrono y Clean Architecture interna.
- Aplicación web `BabelHub.Web` (ASP.NET Core MVC con Razor) con áreas por rol, que consume los servicios desde el servidor.
- PostgreSQL por sistema (Identidad con esquema propio), Redis como caché y Nginx como puerta de entrada interna.
- Pagos con Culqi (Yape y tarjetas) y avisos por WhatsApp con n8n y Evolution API, autoalojados.
- Libros digitales (EPUB y PDF) en un almacén de objetos externo, con descarga por URL firmada; no pasan por el Inventario. El marco legal, aplicado con rigurosidad laxa por ser un proyecto académico, está en [`docs/01-analisis-de-sistema/03-marco-legal.md`](docs/01-analisis-de-sistema/03-marco-legal.md).
- Despliegue con Docker Compose en Oracle Cloud Free Tier: dos VMs ARM (Aplicaciones y Datos, de 1 OCPU y 6 GB) y una VM x86 micro opcional de apoyo.
- Cloudflare gratuito en el borde, con Cloudflare Tunnel, para no abrir puertos en Oracle.
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
