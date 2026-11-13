# Decisiones de implementación — transversal-seguridad

Decisiones menores tomadas al implementar que el plan no fija. No cambian el comportamiento de la spec.

| Tarea | Decisión | Motivo |
| :--- | :--- | :--- |
| T02 | El catálogo de políticas de identidad vive en `PoliticasDeSeguridad` (Dominio de cada servicio): E-commerce con ingresos fallidos, códigos por correo y por origen, segundo factor por origen y por cuenta, y registros por origen; Inventario con el presupuesto del personal (ajustes y traslados). Las claves son estables: `ingresos-fallidos`, `codigos-por-correo`, `codigos-por-origen`, `segundo-factor-por-origen`, `segundo-factor-por-cuenta`, `registros-por-origen`, `ajustes-del-personal`, `traslados-del-personal`. | Una sola fuente de los valores iniciales; la Aplicación los sobrescribe con la configuración. |
| T02 | `codigos-por-correo` y `segundo-factor-por-cuenta` usan el tipo `Usuario`: el sujeto es la huella de la cuenta o del correo, no una combinación con el origen. | `TipoDeSujetoDeLimite` no tiene un tipo «correo» y no hace falta crearlo. |
| T02 | Las pruebas simulan el motor con `SimuladorDeLimite` (evalúa, cuenta la entrada y crea el bloqueo solo si se permite). El comportamiento real del motor (atomicidad, `Liberar`, claves) se prueba en T13. | El Dominio se prueba sin almacén. |
