# Decisiones de implementación — inventario-fuentes

Decisiones menores tomadas al implementar que el plan no fija. No cambian el comportamiento de la spec.

| Tarea | Decisión | Motivo |
| :--- | :--- | :--- |
| T02 | `IBloqueosAsesores` (`Application.Plataforma`, raíz) tiene un método por tipo de clave: `Cliente`, `Pedido`, `IntentarPedido`, `Recepcion` (clave única) y `Devolucion` (por identificador). Su adaptador `NpgsqlBloqueosAsesores` compone la clave `bloqueo-asesor\|tipo\|id` (el motor de límites usa `política\|contador`, así que no chocan) y la pasa a `pg_advisory_xact_lock(hashtextextended(clave, 0))`. | D04: prefijo propio por tipo; convención del motor de límites. |
| T02 | La espera máxima (`lock_timeout`, 5 s por defecto, configurable) rige solo mientras se toma cada bloqueo: se lee el valor vigente de la transacción y se restaura al terminar. Al vencer, el adaptador lanza `EsperaDeBloqueoAgotadaException` (en `Application.Plataforma`, porque `55P03` es de Npgsql) y el caso de uso la traduce a su resultado. `IntentarPedido` no espera nunca. | Plan §2 (Plataforma) y ajustes §5 (`INV-IDEMPOTENCIA-EN-CURSO` solo si vence el `lock_timeout`). |
