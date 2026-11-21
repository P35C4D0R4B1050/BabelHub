# ECO-10d (T19–T22): revisiones de arquitectura y seguridad (carril V, 2026-11-20)

Alcance: `git diff cef645f5..989169c5` (Devoluciones: esquema con disparadores, repositorio de PostgreSQL, `RestInventarioDeDevoluciones`, trabajos de fondo y composición, más `IClienteDelInventario.ReingresarDevolucion`). Revisión de solo lectura (sin builds ni pruebas).

**Resultado de la ronda 1:** `arch-reviewer` y `security-reviewer` sin hallazgos Bloqueantes ni Importantes.

## Menores y su destino
1. Comentario de `AgregarTrabajosDeFondoDeDevoluciones` indicaba `AddHostedService<ServicioDeTrabajosDeDevoluciones>()` (construiría el servicio con una lista vacía): **corregido** (la fábrica `AddHostedService(sp => sp.GetRequiredService<…>())`); T25 usa esa forma.
2. T22 sin la resolución real de la sucursal del actor: **corregido** con `ResolucionDeSucursalDeDevoluciones` (compara el identificador del token con `IdentificadorDeSucursal.DesdeCodigo` de cada sucursal activa del Inventario; falla cerrado) y prueba `ResolucionDeSucursalDeDevolucionesTests`. Se quitó el doble `ResolucionDeSucursalDeDevolucionesProvisional` y el de accesos digitales (Biblioteca ya entrega `IAccesosParaDevoluciones`).
3. Reingreso: 401 e `INV-PROHIBIDO` se tratan como «incierto» (D07, falla cerrado, deja el marcador): intencional; sin cambio.
4. Conciliación (`ObtenerConReembolsoPendiente`): las 100 más antiguas sin rotación pueden dejar sin atender a las siguientes si 100 fallan de forma permanente: Menor, carril V, antes del cierre de ECO-10 (ordenar por último intento o alertar al tocar el tope).
5. Disparadores: falta el CHECK recíproco `motivo = '[redactado]'` ⇒ `redactada_en IS NOT NULL` y congelar `estado_en` y `aprobada_en` en estados finales: Menor (el rol no tiene `DELETE` y los permisos son por columna), carril V.
6. `cupos_de_linea` sin purga: inventariar en `docs/05-seguridad/04` (T34) con su justificación (el tope DEV-03 necesita conservarla).
7. `inventario.sucursales.listar` no filtra por activas (`ConsultasDeDisponibilidad`): ya en hallazgos (d); carril del Inventario.
8. Menor de estilo en `HttpClienteDelInventario.cs` (constante entre métodos y doble línea en blanco): carril V.
