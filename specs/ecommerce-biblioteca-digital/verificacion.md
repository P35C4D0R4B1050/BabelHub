---
spec: ecommerce-biblioteca-digital
fecha: 2026-11-20
---

# Verificación — Biblioteca digital (ECO-8)

## Resultado: Converge

Con una salvedad ajena a esta feature (sección 3): una prueba de arquitectura de `main` falla por un tipo del sembrador de sesiones de desarrollo, no por la Biblioteca.

## 1. Comprobaciones automáticas

| Comprobación | Comando | Resultado |
| :--- | :--- | :--- |
| Compilación sin advertencias | `dotnet build BabelHub.sln -warnaserror` | 0 advertencias, 0 errores (tras fundir `main`). |
| Pruebas unitarias del E-commerce | `dotnet test tests/unit/BabelHub.Ecommerce.UnitTests` | 5387 superadas, 0 con error (165 de la Biblioteca). |
| Pruebas de integración de la Biblioteca (PostgreSQL 17 y RustFS reales con Testcontainers) | `dotnet test tests/integration/BabelHub.IntegrationTests --filter "FullyQualifiedName~Biblioteca"` | 115 superadas, 0 con error. La suite completa de integración corre en el turno del coordinador. |
| Prueba de arquitectura | `dotnet test tests/architecture` | 242 de 243. La que falla, `El_usuario_actual_de_prueba_no_existe_en_ningun_proyecto_de_produccion(Ecommerce)`, nombra `SembradorDeSesionesDeDesarrollo.RetirarAlEncargadoDePruebasAnterior` (otro carril; ver sección 3). Las reglas de la Biblioteca (`ReglasDeBibliotecaTests`) pasan. |
| Specs, planes, tareas y trazabilidad | `dotnet run --project tools/SpecCheck` | `OK: 20 specs, 0 avisos.` |
| Búsqueda de secretos | `git grep -nIiE "password\s*=|api[_-]?key\s*=|secret\s*=" -- src` | Sin coincidencias en la Biblioteca. |
| Archivos digitales, `.env` o datos personales en el cambio | `git diff --name-only` | Ninguno. Los archivos de las pruebas son sintéticos y se generan en memoria. |
| E2E de la biblioteca (Playwright, host real con PostgreSQL, Redis, RustFS y Nginx sembrados) | `npx playwright test biblioteca/biblioteca` | 5 de 5 en Chromium escritorio, 5 de 5 en Chromium teléfono y 5 de 5 en WebKit. Firefox no se ejecutó (informativo). |
| Accesibilidad (axe, WCAG 2 A y AA) | dentro del E2E | Sin violaciones en Mi biblioteca (claro y oscuro) y en la lista de ayudas de la administración. |

## 2. Requisito por requisito

Los 29 requisitos y sus 26 criterios tienen al menos una prueba que los nombra (`[Trait("Criterio", "AC-BIB-NN")]`); la matriz se regenera en `specs/TRACEABILITY.md` (la integra el coordinador). Evidencia principal:

| Requisito | Evidencia en código | Criterio y prueba | Estado |
| :--- | :--- | :--- | :--- |
| BIB-01, BIB-19 | `Application/Biblioteca/Interno/ConcesionRevocacionYPosesion.cs`; `Infrastructure/Biblioteca/RepositorioDeAccesosPostgres.cs` (`INSERT … ON CONFLICT DO NOTHING`) | AC-BIB-01, 17 · `ConcesionRevocacionYPosesionTests`, `RepositoriosDeBibliotecaPostgresTests`, E2E `biblioteca.spec.ts` | Cumple |
| BIB-02 | `Application/Biblioteca/Interno/ConsultasDeBiblioteca.cs`; `Areas/Tienda/Views/MiBiblioteca/Index.cshtml` | AC-BIB-02 · `ConsultasDeBibliotecaTests`, `PantallasDeBibliotecaTests`, E2E | Cumple |
| BIB-03, BIB-07, BIB-12 | `Application/Biblioteca/Interno/AutorizacionYProyeccion.cs` | AC-BIB-03, 10 · `SeguridadDeBibliotecaTests`, `SolicitarEnlaceTests`, E2E (otra cuenta) | Cumple |
| BIB-04, BIB-05, BIB-11, BIB-17 | `Interno/OrquestadorDeDescargas.cs`, `Interno/ConsumirDescarga.cs`; `RepositorioDeDescargasPostgres.cs` (`UPDATE … WHERE … RETURNING`) | AC-BIB-04, 15 · `DescargaConAlmacenRealTests`, `ConcurrenciaYCancelacionDeBibliotecaTests` | Cumple |
| BIB-06, BIB-18 | `Domain/Biblioteca/Acceso.cs`; `S3EntregaDeArchivos.cs` (firma de 300 s) | AC-BIB-05, 06 · `AccesoTests`, `S3EntregaDeArchivosTests` | Cumple |
| BIB-08, BIB-16 | `Interno/AyudaYRestitucion.cs` | AC-BIB-07, 14 · `AyudaYRestitucionTests`, `PantallasDeBibliotecaTests`, E2E | Cumple |
| BIB-09, BIB-10, BIB-21 | `Acceso.cs`, `Interno/AutorizacionYProyeccion.cs` | AC-BIB-08, 09, 16 · `AccesoTests`, `ConsultasDeBibliotecaTests` | Cumple |
| BIB-13 | `Application/Catalogo/Interno/ReferenciaDeArchivosDelCatalogoDeLaAplicacion.cs`, `S3EntregaDeArchivos.ExisteArchivo` | AC-BIB-11 · `S3EntregaDeArchivosTests`, `DescargaConAlmacenRealTests` | Cumple |
| BIB-14 | `S3EntregaDeArchivos.cs` (redirección 302 a la URL firmada, sin cuerpo y sin caché) | AC-BIB-12 · `S3EntregaDeArchivosTests`, E2E (la respuesta es 302 con `Location` al almacén y cuerpo vacío) | Cumple |
| BIB-15 | `RepositorioDeDescargasPostgres.cs` (ventana de una hora, fail-closed SEG-22/26) | AC-BIB-13 · `SolicitarEnlaceTests`, `RepositoriosDeBibliotecaPostgresTests` | Cumple |
| BIB-20, BIB-24 | `Acceso.cs`, `ConcesionRevocacionYPosesion.cs` | AC-BIB-18, 21 · `AccesoTests`, `ConcurrenciaYCancelacionDeBibliotecaTests` | Cumple |
| BIB-22, BIB-23 | `Application/Biblioteca/AccesosParaDevoluciones.cs`, `Interno/AccesosParaDevolucionesDeLaAplicacion.cs` | AC-BIB-19, 20 · `AccesosParaDevolucionesTests`, `ConcurrenciaYCancelacionDeBibliotecaTests` | Cumple |
| BIB-25, BIB-27, BIB-28 | `Interno/EliminarBibliotecaDeUnaCuenta.cs` | AC-BIB-22, 25, 26 · `CancelacionDeLaCuentaTests`, `ConcurrenciaYCancelacionDeBibliotecaTests` | Cumple |
| BIB-26, BIB-29 | `Interno/DepuracionesDeBiblioteca.cs`, `TrabajosDeFondoDeBiblioteca.cs` (03:30 hora de Lima) | AC-BIB-23, 24 · `DepuracionesDeBibliotecaTests`, `PoliticasDeBibliotecaTests` | Cumple |

Casos límite de la sección 6: cubiertos por las mismas pruebas (clave de un solo uso, repetición dentro de la vigencia, cancelación simultánea, almacén caído con plazo de 3 s, carreras 23503 y 23505 con rondas repetidas).

## 3. Diferencias encontradas

- **Mi biblioteca mostraba «Ingresar» con sesión iniciada.** Solo el Carrito rellenaba `HaySesion`; se corrigió en `MiBibliotecaController` (la ruta exige sesión). Otras pantallas con sesión no se revisaron aquí: queda anotado como seguimiento para el carril de la web.
- **Prueba de arquitectura en rojo en `main`**, ajena a la Biblioteca: `SembradorDeSesionesDeDesarrollo.RetirarAlEncargadoDePruebasAnterior` hace saltar la regla de tipos de prueba en producción (el nombre coincide con el patrón). Pide un cambio de nombre en el carril del sembrador; no se debilitó la prueba.
- **E2E sin estilos si falta `app.css`.** Sin `scripts/construir-estilos.sh` las capturas salen sin diseño y axe pasa sin significado. El README del E2E ya lo exige como requisito; la revisión visual se hizo con el CSS compilado.
- **Revisión visual (`ui-review`)** de Mi biblioteca en escritorio y teléfono, claro y oscuro: tarjeta con título, formato, fecha en hora de Lima, estado con texto e icono, «N de 5 descargas usadas» con barra, ayuda en `<details>` y foco por teclado. Sin hallazgos abiertos.

- **Alcance del E2E de la administración (T25).** El E2E cubre la lista de ayudas con axe en tema claro; la biblioteca de un cliente, la restitución y los estados «archivo no disponible» y «límite» se cubren con pruebas de integración (AC-BIB-11, AC-BIB-13). Registrado en `progress/hallazgos.md` como Menor.

## 4. Conclusión

La Biblioteca digital converge con su spec: 26 de 26 criterios con prueba, E2E verde en tres navegadores, axe AA en claro y oscuro, y concurrencia y cancelación probadas contra PostgreSQL real. Falta solo que el coordinador corrija la prueba de arquitectura ajena y ejecute la suite completa.
