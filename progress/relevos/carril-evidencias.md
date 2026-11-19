# Encargo del carril de evidencias (nuevo) — Sprint 2

Lee primero `AGENTS.md`, `docs/07-proceso-de-desarrollo/06-plan-sprint-2.md`, `progress/coordinacion.md` y `progress/relevos/coordinador.md` §6. Es el carril que más importa para la rúbrica (criterio fundamental: demostrar con pruebas que se cumplen los atributos de calidad). Entrega el 28-nov-2026.

## Bloques (en este orden)
1. **OAS-1 — OpenAPI por servicio.** Documento OpenAPI 3 de cada Api (E-commerce e Inventario) generado desde el código, con rutas, DTO de `Shared`, errores `application/problem+json`, alcances y seguridad (JWT y credencial de servicio); servido en `Development`; archivo publicado en `docs/03-diseno-de-software/`; prueba que falle si una ruta pública no está documentada. Nivel E. Sin paquetes nuevos sin avisar (propón el más ligero, por ejemplo `Microsoft.AspNetCore.OpenApi`).
2. **K6-1 — pruebas de carga por atributo de calidad** en `tests/load/`: disponibilidad (p95 < 500 ms, RNF02, AC-FUE-08), confirmación del checkout (p95 < 3 s), concurrencia sin sobreventa (AC01), límites de uso y resiliencia; rampa progresiva de 50 hasta 2000 usuarios virtuales contra el entorno local de Docker (`tests/e2e/entorno/`, con CPU y memoria acotadas como en `docs/02/07`). k6 no está instalado: pide permiso a la persona para instalarlo (winget) o para usar la imagen oficial `grafana/k6` por Docker. Resultados en `progress/evidencias/`.
3. **DOC-1 — análisis de base de datos y caché** (índices usados por las consultas críticas, bloqueos, `FOR UPDATE`, seguridad por filas, qué se cachea y TTL) con las mediciones de K6-1.
4. **Cobertura:** reporte HTML (ReportGenerator o equivalente) desde `dotnet test --collect:"XPlat Code Coverage"` con los umbrales de `tools/CoverageCheck`; copia en `progress/evidencias/`; que lo archive el CI.

## Reglas
Prueba primero cuando aplique; un commit por pieza; revisores por bloque; integración completa solo con turno del coordinador; informe en `progress/log/`; mensaje de una línea al coordinador al cerrar cada bloque; sin `push`.
