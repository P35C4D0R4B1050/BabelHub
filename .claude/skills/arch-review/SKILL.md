---
name: arch-review
description: Revisa un cambio, un plan o una carpeta contra las reglas de arquitectura de AGENTS.md (capas, referencias entre proyectos, Shared, Web, secretos, archivos digitales, auditoría y alcance). Úsala antes de un commit grande, al terminar un plan o cuando se pida «revisar la arquitectura».
---

# arch-review — Revisión de arquitectura

Para una revisión en contexto aislado, delega en el agente `arch-reviewer`. Esta skill describe el método.

## Alcance de la revisión

Lo que se revisa: `git diff` (o la carpeta o plan indicados). Lo que **no** se hace: cambiar código; solo se reporta.

## Lista de comprobación

1. **Referencias entre proyectos** (`*.csproj`): Dominio sin referencias; Aplicación solo a Dominio; Infraestructura a Aplicación, Dominio y Shared; Api a Aplicación, Infraestructura (composición) y Shared; Web solo a Shared; ningún servicio referencia a otro; ni Dominio ni Aplicación referencian Shared.
2. **Paquetes**: EF Core, Redis, `HttpClient`, SDKs de terceros solo en Infraestructura. Nada de ASP.NET en Dominio o Aplicación.
3. **Lógica**: ¿hay reglas de negocio en controladores, vistas o adaptadores? Deben estar en Dominio o en casos de uso.
4. **Puertos**: definidos en Aplicación, implementados en Infraestructura; sin puertos «por costumbre» con una sola implementación sin motivo.
5. **Seguridad**: alcance por sucursal en la Aplicación; secretos fuera del repositorio; el JWT nunca llega al navegador; sin datos sensibles en logs, auditoría ni mensajes.
6. **Contenido digital**: archivos solo en el almacén de objetos, entrega por URL firmada, libros digitales sin reservas ni consulta al Inventario.
7. **Transacciones**: auditoría en la misma unidad de trabajo; idempotencia donde corresponde; concurrencia probada.
8. **Errores**: `problem+json` con `code` estable; sin trazas.
9. **Pruebas**: cada criterio nombrado; reglas nuevas con prueba unitaria primero.

## Informe

Lista cada hallazgo con: archivo y línea, regla que incumple (número de `AGENTS.md`), por qué importa y la corrección propuesta. Ordena de más a menos grave. Si no hay hallazgos, dilo explícitamente.
