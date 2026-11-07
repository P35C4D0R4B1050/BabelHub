---
name: dotnet-feature
description: Guía para agregar casos de uso, entidades, puertos, adaptadores, endpoints y pruebas en BabelHub respetando Clean Architecture y las convenciones del repositorio. Úsala al escribir código de uno de los dos servicios .NET (E-commerce o Inventario) dentro de spec-implement.
---

# dotnet-feature — Cómo se escribe código en BabelHub

## Dónde va cada cosa

| Pieza | Proyecto | Ejemplo |
| :--- | :--- | :--- |
| Entidades, objetos de valor, reglas, estrategias | `BabelHub.<Servicio>.Domain` | `Fuente`, `Reserva`, `EstrategiaDeSeleccion` |
| Casos de uso y puertos | `BabelHub.<Servicio>.Application` | `ReservarStock`, `IRepositorioDeFuentes` |
| Adaptadores (EF Core, Redis, HTTP, SDKs) | `BabelHub.<Servicio>.Infrastructure` | `RepositorioDeFuentesPostgres`, `PasarelaCulqi` |
| Endpoints, autenticación, composición | `BabelHub.<Servicio>.Api` | controladores, `Program.cs` |
| Contratos entre servicios | `BabelHub.Shared` | DTOs, constantes de roles |

Cada servicio es un monolito modular: los módulos son **carpetas y espacios de nombres dentro de cada capa** (por ejemplo `BabelHub.Ecommerce.Application.Pagos`), con su contrato público en la raíz y su interior en `Interno` (regla 13). Identidad y Asistente son módulos del E-commerce con una versión reducida de las capas; la interfaz web es la Presentación (`BabelHub.Ecommerce.Api`, áreas Tienda, Sucursal y Admin). No se crean proyectos nuevos por módulo. Las carpetas de `Application` coinciden con las de `specs/` (`SeleccionFuente/`, `Traslados/`…).

## Convenciones

- Nombres: caso de uso = verbo + sustantivo (`AprobarTraslado`); puerto = `I` + rol (`IPasarelaDePago`); adaptador = tecnología + puerto.
- Dominio y casos de uso en español con los términos de `docs/glossary.md`.
- Los casos de uso devuelven resultados explícitos (éxito o error del dominio), no excepciones para flujos esperados. La Api los traduce a `application/problem+json`.
- Un caso de uso: obtiene el usuario actual (puerto), **verifica alcance y rol**, carga con puertos, aplica reglas del dominio, persiste y audita dentro de una unidad de trabajo.
- Un módulo solo llama a otro por su contrato público, en el mismo proceso. Un `HttpClient` hacia el otro servicio vive en Infraestructura, con resiliencia y tiempos de espera cortos; el contrato viene de `Shared` y se traduce a un modelo interno.
- Reloj, generador de identificadores y usuario actual son puertos, para poder probarlos.

## Pruebas

1. Dominio: xUnit puro, sin mocks de infraestructura.
2. Aplicación: puertos simulados a mano; verifica el orden de pasos y los fallos.
3. Infraestructura: Testcontainers con PostgreSQL real (migraciones incluidas); concurrencia con solicitudes simultáneas.
4. Cada prueba de criterio lleva `[Trait("Criterio", "AC-XXX-NN")]`.

## Comandos útiles

```bash
dotnet new classlib -n BabelHub.Inventario.Domain -o src/BabelHub.Inventario/BabelHub.Inventario.Domain   # solo si el proyecto no existe
dotnet sln BabelHub.sln add <proyecto.csproj> --solution-folder <carpeta>
dotnet build BabelHub.sln -warnaserror
dotnet test BabelHub.sln --filter "Criterio=AC-FUE-01"
```

## Ayuda externa recomendada

La skill oficial `dotnet-test` y `dotnet-data` de `dotnet/skills`, y `dotnet-aspnetcore`, complementan esta guía (instalación en `docs/07-proceso-de-desarrollo/01-flujo-sdd.md`). Esta skill manda en caso de diferencia.
