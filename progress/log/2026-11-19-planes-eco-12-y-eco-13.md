# 2026-11-19 — Planes de ECO-12 (asistente) y ECO-13 (índice infinito)

Carril del agente delegado (planificación en lote, solo documentación). Sin `push`; no se tocaron `src/`, `tests/`, `BabelHub.sln`, `BabelHub.Shared`, `progress/CHANGELOG.md`, `progress/bloques.md` ni la memoria.

## `web-indice-infinito` — **Planned** (2026-11-19)

- Aprobado por el agente delegado tras tres rondas de revisión (auditor, arquitectura y seguridad): la ronda 3 cerró sin Bloqueantes ni Importantes. Fila en `progress/aprobaciones.md` (huella `16c9e3bd57173996`, 20 Menores) y Menores en `progress/hallazgos.md`.
- Diseño: módulo `Web` (subcarpetas `Indice`), generador propio FNV-1a de 64 bits + SplitMix64 con patrón de oro, dirección en la ruta con captura total, acción al azar en una ruta de dos segmentos, 200 amable sin eco para una dirección no válida, cabeceras `noindex` y `no-store` por middleware, sin almacenamiento, límite de SEG-18.
- Tareas T01–T15 en tres bloques propuestos: `ECO-13a` = T01–T06, `ECO-13b` = T07–T11, `ECO-13c` = T12–T15 (nivel B con las tareas de la entrada pública en E). **Para `progress/bloques.md` (coordinador):** desdoblar la fila ECO-13 en esos tres bloques; depende de WEB-1d y de la composición del pipeline de límites y sesión en `Program.cs`.

## `asistente-compra-ia` — plan escrito, **spec sigue Approved** (no aprobado)

- Rondas 1 y 2 con Importantes corregidos; la ronda 3 dejó 1 Importante de arquitectura (el atributo `[VerificacionDePersona]` no puede ir en el POST; el controlador llama a `IExigenciaDeVerificacionDePersona.Exigir`). Corregido en el plan, pero cualquier cambio exige re-revisión y la regla del coordinador es no pedir más rondas por cuenta propia: **a la espera de la decisión de la persona sobre una ronda 4 acotada**.
- Valores y reglas de consumo ratificados por la persona responsable el 2026-11-19 (tope diario 100/5000 en 24 h deslizantes, 2 simultáneas por sujeto, 45 s por consulta, 400 tokens / 8 hechos / 6000 caracteres, 5 pedidos recientes solo en la ficha del servidor).
- Prerrequisitos de otros planes registrados (`progress/hallazgos.md` y `progress/ajustes-al-implementar.md` §11): `IPedidosParaElAsistente` implementado (ECO-6), `Verificar` del contexto de lectura (Identidad T26), `IContextoDelAsistente` (`web-base` T08), políticas de límite y secreto (carril A).
- Tareas T01–T24; bloques propuestos `ECO-12a` = T01–T07, `ECO-12b` = T08–T14, `ECO-12c` = T15–T19, `ECO-12d` = T20–T24. **Para `progress/bloques.md`:** ECO-12 depende además de IDE-6, de la parte pendiente de `web-base` T08 y del percentil 95 del checkout en FIN-2 (k6).
- B06 sigue abierto: el adaptador real del proveedor (con su compuerta, copiada al bloqueo) queda fuera del plan.
