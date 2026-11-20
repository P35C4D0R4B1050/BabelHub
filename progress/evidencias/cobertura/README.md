# Cobertura de líneas (2026-11-20)

`index.html` es el reporte de `tools/CoverageCheck --html` (un solo archivo; por servicio, por capa y por archivo de peor a mejor), generado de `dotnet test BabelHub.sln --collect:"XPlat Code Coverage" --settings tests/coverlet.runsettings` (unitarias, integración, arquitectura y herramientas; una línea cuenta como cubierta si alguna prueba la ejecuta). El CI lo archiva como artefacto `reporte-de-cobertura`.

| Servicio | Dominio y Aplicación (mínimo 85 %) | Total (mínimo 70 %) |
| :--- | :--- | :--- |
| Ecommerce | **95,5 %** | **94,6 %** |
| Inventario | **95,7 %** | **93,7 %** |

Los dos servicios **cumplen** los umbrales. Corrida sobre la rama `claude/evidencias-431ff7` en `dd89bb0a` (con `main` fundido hasta `d471a4f6`): 243 pruebas de arquitectura, 2 368 y 5 376 unitarias, 15 de `CoverageCheck`, 23 de `SpecCheck` y 6 960 de 6 973 de integración (46 min).

## Pruebas de integración que fallaron en esa corrida (no son de este carril)

Las 13 fallas se reparten a sus dueños: `AnfitrionDelEcommerceTests` (2, composición), `CargaDelAcumuladoTests` (1), `RegistroDeSupresionesEnObjetosTests` (1), `BajaDeTitularPostgresTests` (1, interbloqueos), `ContratoRestDeReservasTests` (1, esperaba 409 y obtuvo 400), `HumoDeImagenesTests` (2, el contenedor no respondió su salud en 4 min) y `ImagenesDeContenedorTests` (1), y las pruebas del escáner de secretos (`SecretosEnElRepositorioTests` ×3 y `EscanerDeSecretosTests` ×1) por `Biblioteca/S3EntregaDeArchivosTests.cs`, `Biblioteca/PostgresDeBibliotecaFixture.cs` y `Api/SaludDeLaBaseDeDatosTests.cs`.
