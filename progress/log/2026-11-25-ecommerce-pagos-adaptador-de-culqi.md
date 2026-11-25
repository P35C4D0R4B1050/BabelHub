# 2026-11-25 — ecommerce-pagos T37: el adaptador real de Culqi

**Carril `culqi`** (sesión de trabajo por encargo de la coordinación). Rama `claude/quirky-einstein-aa1902`.

## Qué se hizo
- Investigación de la API de Culqi con su especificación OpenAPI (`apidocs.culqi.com/apiculqi.yaml`), su documentación y el SDK oficial para .NET; resumen citado en `specs/ecommerce-pagos/decisiones-de-implementacion.md` (sección «T37») y **DD-95 a DD-99** en `progress/decisiones-delegadas.md` (pendientes de ratificar).
- `CulqiPasarelaDePago` y `ClienteHttpDeCulqi` (Infraestructura), composición por `BABELHUB__Pagos__Pasarela=culqi`, correo del titular activado con `culqi`, puerto `ConsultarEstado(…, iniciadoEn)`.
- Pruebas: `CulqiPasarelaDePagoTests`, `PagosConCulqiSimuladoTests` (PostgreSQL real), `ComposicionDePagosTests`, `EntornoStagingTests`; prueba de humo `CulqiRealTests` y `guia-del-spike-culqi.md` para T38.
- Revisiones: `security-reviewer` (3 Importantes corregidos: aprobar un `201` solo sin evidencia en contra, rechazar solo con evidencia positiva, reembolso sin marca bloquea sin vencimiento) y `arch-reviewer` (sin bloqueantes; Menores aplicados).

## Qué se probó de verdad y qué no
- **Probado:** el adaptador contra un servidor HTTP simulado con la forma de la especificación; el flujo completo con PostgreSQL real (Testcontainers) y un Culqi simulado con estado; la guarda y el arranque por entorno (`Production` con `sk_live_` arranca; `sk_test_` en `Production`, toda llave en `Staging` y `sk_live_` en `Test` fallan sin repetir la llave).
- **No probado: ninguna llamada a Culqi real.** La forma exacta del cargo, la ventana de la consulta, el `PATCH` de la devolución, el reembolso de Yape, si el token es de un solo uso, `duplicated`, los límites de tasa y qué envía un webhook.

## Pendiente
- T38 (persona): `specs/ecommerce-pagos/guia-del-spike-culqi.md`. Registrar el resultado en `progress/log/`.
- T38b / DD-99a: la pantalla de pago aún no genera el token de Culqi.
