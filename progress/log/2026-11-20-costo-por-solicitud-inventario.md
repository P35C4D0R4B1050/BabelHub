# 2026-11-20 — Costo por solicitud del Inventario (K6-1, capacidad)

**Rama:** `claude/eager-davinci-c26d43`. **Nivel:** C (revisado por `security-reviewer` y `arch-reviewer`, sin Bloqueantes).

## Qué se hizo
- `UnidadDeTrabajoPostgres`: rol y alcance llevados en memoria; arranque de transacción en una sentencia; bloque anidado del mismo esquema sin cambio de rol; `EjecutarComoConAlcanceDeRed` (entrada y salida fusionadas); `IAlcanceDeDatosFijado` (interna del Inventario) sustituye la pregunta `current_setting` de `AccesoAFuentes` y `AccesoATraslados`.
- `BasesDelInventario.Abrir`: `No Reset On Close` (sin `DISCARD ALL`); invariante «sin estado de sesión» documentado (doc 09 §2 y §6c). Corrección de la revisión de seguridad: los bloqueos asesores de sesión cierran la conexión física si no pueden soltarse.
- `CacheDeReferencias` (lista de fuentes y formato de ediciones, solo consulta pública), **apagada por omisión** (`Inventario:ReferenciasEnMemoriaSegundos`): D05 de `inventario-fuentes` la contradice; encenderla es decisión de la persona responsable (hallazgo Importante).
- Pruebas: `CostoPorSolicitudDeDisponibilidadTests` (conteo de sentencias, aislamiento tras reutilizar la conexión, elevación fusionada, bloque que falla, actor sin alcance tras un encargado), `ConsultarDisponibilidadConReferenciasEnMemoriaTests`.

## Resultado
Sentencias por consulta (fichas, fuentes y stock): 21 → 10; por solicitud en la base: ≈ 31 → ≈ 18 (≈ 12 con memoria). **k6: sin mejora consistente de capacidad** en esta máquina (tabla en el doc 09 §6c); con 0,3 CPU RNF02 sigue sin cumplirse porque el cuello es la CPU del servicio. Quedan palancas (hallazgos).

## Verificación
`dotnet build -warnaserror` 0; unitarias del Inventario 2 375; arquitectura 243; integración Fuentes, Plataforma, Traslados, Api, Reservas, Ajustes, Auditoría del Inventario y Límites verdes (3 pruebas de concurrencia de Reservas fallaron una vez bajo carga de la máquina y pasan aisladas, 13/13 y 368/368); `SpecCheck` OK. No se corrió la suite completa del E-commerce (no se tocó).
