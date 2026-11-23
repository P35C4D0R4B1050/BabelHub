---
spec: transversal-carga-inicial
fecha: 2026-11-23
---

# Verificación — Carga inicial del catálogo y del inventario

## Resultado: Converge con reserva declarada

La reserva y lo que falta para `Done` están en la sección 3; la spec sigue en `InProgress`.

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

## 3. Reservas y hallazgos

**Verificado de verdad (2026-11-23, entorno local aislado: PostgreSQL y almacén S3 propios en Docker, servicios en Development).**
- `cargar-catalogo` cargó las 40 obras con sus portadas; repetida dio 40 «ya cargadas» y el mismo plan byte a byte.
- `cargar-stock --esperar` terminó con salida `0`: 6 sucursales y 10 rutas nuevas, 40 ediciones registradas y **120 operaciones de stock** (la recepción y dos sucursales por edición), con el presupuesto de 60 por hora respetado (la segunda tanda esperó la ventana); repetida, no cambia nada.
- Revisión visual (`ui-review`) de la lista y de la ficha de una obra con descripción y de otra digital, en móvil, tablet y escritorio, en tema claro y oscuro: cada producto muestra portada, precio y disponibilidad real por sucursal, y la ficha su descripción; sin defectos. Capturas en la carpeta de evidencias de la sesión (fuera del repositorio). Defecto hallado y corregido: las portadas genéricas dibujaban el motivo sobre el título (`generar-portadas-genericas.ps1`, commit `aee83fbd`).

**Reserva declarada (la spec queda en `InProgress`).** No se midió: el contraste con una herramienta (solo a ojo sobre las capturas), los estados vacío, error y servicio no disponible de la lista y la ficha, el recorrido con el teclado y el lector de pantalla, la ejecución de `tests/e2e/catalogo/catalogo-publico.spec.ts` con Playwright (las capturas se tomaron con un navegador sin los specs) ni WebKit. Tampoco se probó la carga en las VMs reales de Oracle: los comandos de las órdenes se ensayaron en local, pero no el `docker compose run` de producción de la guía §14b. Pasa a `Done` cuando se cierre esa reserva en el entorno real o la persona la acepte.

**Hallazgos Menores** en `progress/hallazgos.md` (revisión del código) y en `progress/ajustes-al-implementar.md` §12: enlaces duros de las portadas, ruta del plan temporal, `idempotencia-en-conflicto` contada como «ya cargado» y solo EPUB de muestra.
