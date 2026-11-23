---
spec: transversal-carga-inicial
fecha: 2026-11-23
---

# Verificación — Carga inicial del catálogo y del inventario

## Resultado: Converge con reserva declarada

La reserva y lo que falta para `Done` están en la sección 3.

## 1. Comprobaciones automáticas

| Comprobación | Comando | Resultado |
| :--- | :--- | :--- |
| Compilación sin advertencias | `dotnet build BabelHub.sln -warnaserror` | Correcta (0 advertencias, 0 errores) |
| Unitarias del E-commerce | `dotnet test tests/unit/BabelHub.Ecommerce.UnitTests` | 5758 correctas (54 nuevas de la carga) |
| Unitarias del Inventario | `dotnet test tests/unit/BabelHub.Inventario.UnitTests` | 2454 correctas (63 nuevas) |
| Arquitectura | `dotnet test tests/architecture/BabelHub.ArchitectureTests` | 261 correctas (incluida `ReglasDeLaIdentidadDeOperacionTests`) |
| Integración con PostgreSQL y almacén S3 reales | `dotnet test tests/integration/BabelHub.IntegrationTests --filter "CargaInicial\|Catalogo"` | 256 correctas; los dos guardias de credencial (`SesionWebHandlerTests`, `CredencialDeUsuarioDelInventarioTests`): 37 correctas |
| Specs, planes y trazabilidad | `dotnet run --project tools/SpecCheck` | OK: 21 specs, 0 avisos |

## 2. Requisito por requisito

| Requisito | Estado | Prueba o evidencia |
| :--- | :--- | :--- |
| CIN-01, CIN-02, CIN-19 | Cumple | `CargaInicialDelCatalogoTests` (tres obras, repetida y dos simultáneas contra PostgreSQL y S3 reales); ejecución real de 40 obras y repetición con 40 «ya cargadas» y plan idéntico byte a byte |
| CIN-03, CIN-04, CIN-15 | Cumple | `LectorDelArchivoDeCargaTests`, `CargarCatalogoInicialTests` |
| CIN-05 | Cumple con la salvedad de los enlaces duros | `CarpetaDePortadasDeLaCarga` (traversal, ruta absoluta, enlaces simbólicos, flujos alternos, nombres reservados); no detecta enlaces duros (hallazgo Menor) |
| CIN-06, CIN-07 | Cumple | Integración con la auditoría real (identidad y registro de la corrida con la huella recortada a 16), `ReglasDeLaIdentidadDeOperacionTests` y los dos guardias de credencial |
| CIN-08, CIN-09 | Cumple (solo EPUB) | `GeneradorDeLibroDeMuestra` verificado por `VerificadorDeContenido`; solo EPUB (el PDF de muestra exigiría un cambio de spec) |
| CIN-10, CIN-11 | Cumple | Plan de referencia `tools/carga-inicial/plan-de-referencia.json` leído por las pruebas de ambos servicios; `LectoresDeLaCargaInicialTests` |
| CIN-12, CIN-13 | Cumple | `CargaInicialDelInventarioPostgresTests` (sucursales, rutas, conocimiento de la edición, ingreso y corrección, repetir, simultáneas, merma previa) |
| CIN-14 | Cumple | Presupuesto real de 60 por hora: 62 operaciones se detienen en 60 con la hora de reanudación y la repetición completa; `--esperar` con tope de 2 horas |
| CIN-16 | Cumple | `DatosVersionadosDeLaCargaTests` reproduce la regla FNV-1a de `completar-datos.ps1` y compara el archivo |
| CIN-17, CIN-18, CIN-20 | Cumple | `ModoCargarStockTests`, pruebas del modo `Operacion`, ninguna ruta mapeada; resumen sin cadenas de conexión, claves ni rutas |

## 3. Reserva y hallazgos

Ver `progress/hallazgos.md` (Menores de la revisión del código) y `progress/ajustes-al-implementar.md` §12.
