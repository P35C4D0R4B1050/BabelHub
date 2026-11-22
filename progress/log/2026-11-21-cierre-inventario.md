# 2026-11-21 — Cierre de Inventario (carril «cierre-inventario»)

## Hecho
- **FUE-09 y FUE-11:** reevaluadas; no se construyen. Búsqueda por título, autor o ISBN → datos del Catálogo (E-commerce, no referenciable desde el Inventario, AGENTS §3.5); encargado asignado → Identidad (ECO-0 D16); pantallas de `web-base`, sin requisito. Reserva documentada en `specs/inventario-fuentes/verificacion.md`. FUE-10 intacta (DD-18).
- **S2:** nuevo escenario `tests/load/escenarios/tope-por-origen.js` (+ variables `ORIGENES` e `INTENTOS` en `ejecutar.sh` y fila en el README). Corrida final: 100 de 100 reservas esperadas (10 por origen), 30 × 429, 0 × 500/503, fase 2 correcta. Primeras corridas: una se cortó porque otra sesión compartía el contenedor `bhcarga-k6`; otra con 300 solicitudes a la vez saturó PostgreSQL (503; nunca más de 10 por origen) — dato de capacidad, no fallo del tope. DD-44.
- **AJU-22:** spec reabierta a Draft, redacción alineada con el código (AJU-22, AC-AJU-18, §6; plan §3 y §5), DD-40 a DD-42, dos pruebas nuevas (unitaria del 503; integración de cargas distintas en paralelo), puerta de 2 rondas (la ronda 1 halló un Importante: una cláusula de AJU-17 sin criterio, retirada), reaprobada con huellas en `progress/aprobaciones.md`.
- Specs a `Done`: `inventario-ajustes` e `inventario-seleccion-fuente`; matriz regenerada.

## Pendiente / Menores
- `docs/03-diseno-de-software/08-contratos-entre-servicios-v1.md` líneas 82 y 83 aún listan `INV-IDEMPOTENCIA-EN-CURSO` para ajuste e ingreso (hallazgos).
- FUE-09 y FUE-11 (persona / `web-base`), FUE-10 (DD-18).
