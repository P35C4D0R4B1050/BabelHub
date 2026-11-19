# 2026-11-19 — Verificación FIN-1 (carril V): specs con todas sus tareas

Solo lectura más los archivos `specs/<spec>/verificacion.md` (no se tocó `src/`, `tests/`, `BabelHub.sln`, CHANGELOG, `bloques.md` ni la memoria; ninguna spec pasó a `Done`).

## Comprobaciones comunes (sobre `main` fundido, rama `claude/silly-chaum-f7213a`)

- `dotnet build BabelHub.sln -warnaserror`: 0 advertencias, 0 errores.
- `dotnet run --project tools/SpecCheck`: OK, 20 specs, 0 avisos.
- Búsqueda de secretos en `src/`: sin coincidencias. Árbol de trabajo limpio antes de escribir los informes.
- Pruebas unitarias y de arquitectura filtradas por criterio (`Criterio~AC-<PREFIJO>-`), todas en verde: SEG 379 + 317 + 3; AUD 561 + 550; FUE 239; SEL 404 + 25; AJU 96; TRA 249 + 23 (Ecommerce.UnitTests, Inventario.UnitTests y ArchitectureTests).
- **No se ejecutaron las pruebas de integración** (las corre el coordinador sobre `main`; había contenedores de otros procesos activos). Cada `verificacion.md` marca con «(I)» o «(Int.)» los requisitos cuya evidencia clave está allí.
- Cobertura de pruebas por criterio: todos los AC de las seis specs tienen una prueba con `[Trait("Criterio", …)]`, salvo **AC-FUE-08** (solo el script k6, sin ejecutar).

## Resultado por spec

| Spec | Resultado | Cumple / Parcial / No cumple | Huecos principales (dueño · fase) |
| :--- | :--- | :---: | :--- |
| `inventario-ajustes` | **Converge** (condicionado a la integración) | 22 / 0 / 0 | AC-AJU-09 (concurrencia), 10, 11, 17 y 18 solo en integración · coordinador; `Inventario.Api/Program.cs` sin componer `Ajustes` · coordinador, FIN-1 |
| `transversal-seguridad` | No converge | 29 / 3 / 0 | SEG-27 y AC-SEG-23: tope de 24 correos sin prueba etiquetada ni adaptador de producción · `identidad-seguridad` T34, IDE-7; SEG-31 solo en reserva y hoja de reclamación · `web-base` WEB-2 T28 y el asistente |
| `transversal-auditoria` | No converge | 28 / 16 / 0 | `Program.cs` de ambos servicios sin componer la auditoría · FIN-1; llamantes y adaptadores de Identidad, correo, Pedidos, Envíos, Devoluciones, Biblioteca y Notificaciones; hora de Lima (D13) pendiente de la persona; etiquetas AC-AUD-36 y AC-AUD-41 por corregir |
| `inventario-fuentes` | No converge | 19 / 3 / 0 | AC-FUE-08 (k6 sin ejecutar) · FIN-2; FUE-09 y FUE-11 (búsqueda y encargado asignado sin dueño en `web-base`) · la persona decide; `Program.cs` sin componer `Fuentes` · FIN-1 |
| `inventario-seleccion-fuente` | No converge | 36 / 3 / 0 | G1 SEL-03 (faltan dos pruebas); G2 SEL-24 (selector con tabla de rutas sin prueba en la Aplicación); G3 SEL-39 (400 antes del 403 con evento: reordenar o precisar la spec, decide la persona); pendientes P1 (tope de unidades) y P2 (residual de 75 minutos) |
| `inventario-traslados` | No converge | 22 / 1 / 0 | TRA-22: la alerta de 48 horas no llega a logística (`IDestinatariosDeAlertas` solo devuelve administradores) · carril A; receptores del E-commerce de avisos y estados aún sin construir (`ecommerce-pedidos` T32); concurrencia (AC-TRA-22 a 24) solo en integración |

## Observaciones

- Ninguna de las seis specs tiene un requisito «No cumple»: los huecos son pruebas que faltan, piezas de otros carriles o composición pendiente de los `Program.cs` (FIN-1).
- Etiquetas `Trait` cruzadas o con el número del requisito en vez del criterio (SEG-28, SEG-21, AUD-36, AUD-41 y otras menores): las detecta una lectura, no `SpecCheck`.
- La integración y los k6 de carga quedan para el coordinador; con su resultado se puede re-evaluar `inventario-ajustes` (condicionada) y los «(I)» de las demás.
