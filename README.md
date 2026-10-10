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

- **Sistema de E-commerce:** catálogo, carrito, checkout, pagos, pedidos y asistente de compra.
- **Sistema de Inventario Multi-Sucursal:** cada sucursal es una fuente de stock (patrón *Multi-Source Inventory*); *stock vendible = stock físico − reservas activas*.

**Caso de estudio:** Librerías Crisol (recojo en tienda) y el patrón MSI de Adobe Commerce.

## Documentación

| N.° | Documento | Contenido |
| :--- | :--- | :--- |
| 01 | [Necesidad del negocio](01-necesidad-del-negocio.md) | Problema, objetivos, alcance, actores y necesidades (NN). |
| 02 | [Requisitos](02-requisitos.md) | Requisitos funcionales (RF), no funcionales (RNF) y restricciones (RC). |
| 03 | [Atributos de calidad](03-atributos-de-calidad.md) | Escenarios de calidad (AC) y su priorización. |
| 04 | [Drivers arquitectónicos](04-drivers-arquitectonicos.md) | Requisitos que condicionan la arquitectura (DA), riesgos y compromisos. |
| 05 | [Decisiones arquitectónicas](05-decisiones-arquitectonicas.md) | Decisiones (AD) con justificación y alternativas; despliegue en Oracle Cloud, base de datos separada y Cloudflare frente a Nginx. |
| 06 | [Estilo arquitectónico](06-estilo-arquitectonico.md) | Estilo basado en servicios, diagrama de componentes y organización interna con Clean Architecture. |

## Trazabilidad

`Necesidad (NN)` → `Requisito (RF, RNF, RC)` → `Atributo de calidad (AC)` → `Driver (DA)` → `Decisión (AD)` → `Estilo y estructura`

## Resumen de la arquitectura

- Cuatro servicios .NET 10 de granularidad gruesa (E-commerce, Inventario, Identidad y Asistente de Compra), con REST síncrono y Clean Architecture interna.
- PostgreSQL por sistema, Redis como caché y Nginx como puerta de entrada interna.
- Despliegue con Docker Compose en Oracle Cloud Free Tier: dos VMs ARM (Aplicaciones y Datos, de 1 OCPU y 6 GB) y una VM x86 micro opcional de apoyo.
- Cloudflare gratuito en el borde, con Cloudflare Tunnel, para no abrir puertos en Oracle.
- Pruebas con xUnit, Testcontainers, Playwright y k6. Desarrollo posterior bajo Spec-Driven Development.
