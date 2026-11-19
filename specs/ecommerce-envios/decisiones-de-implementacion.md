---
spec: ecommerce-envios
---

# Decisiones de implementación — Guías de envío a domicilio

Decisiones no triviales tomadas al implementar (el `decisions.md` del plan no se edita: forma parte de su huella).

| Fecha | Tarea | Decisión | Motivo |
| :--- | :--- | :--- | :--- |
| 2026-11-20 | T01 | **`NumeroDeGuia` acepta de 6 a 40 letras sin tilde, dígitos y guiones.** | El plan pide «largo y caracteres acotados» (SEG-13) sin cifras; el rango cubre los formatos habituales de operadores y se ajusta al elegir el real. |
| 2026-11-20 | T01 | **`Envio.Cancelar` devuelve `YaEstabaCancelado` para un envío ya cancelado** (éxito idempotente), y la Aplicación (T05) lo informa a Pedidos como `Cancelado`. | El plan dice «un cancelado previo es éxito idempotente» (T05) pero lo lista junto a `Mantenido` (§1): si se informara `Mantenido`, la repetición de la anulación quedaría bloqueada por un envío que ya está cancelado. **A confirmar con el carril B (contrato de ECO-6).** |
| 2026-11-20 | T01 | **`TomarSolicitudDeGuia` y `IniciarDespacho` reciben la hora y la clave por parámetro** y devuelven resultados explícitos (`Tomada`, `EnCurso`, `EstadoInvalido`; `Iniciado`, `EnCurso`, `EstadoInvalido`). | El Dominio no lee el reloj ni genera identificadores (puertos). |
| 2026-11-20 | T02 | **`DestinatarioParaGuia.Validar` trata un valor más largo que el máximo (nombre 120, dirección 200, teléfono 20) como campo no válido** y devuelve ese campo, sin repetir el valor. | ENV-10 exige indicar el campo; los máximos son los de la hoja de Reclamaciones y la base de datos de Pedidos. |
