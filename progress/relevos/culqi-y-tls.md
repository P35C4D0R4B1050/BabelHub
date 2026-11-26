# Relevo — Culqi (T38) y TLS de PostgreSQL (2026-11-26)

Para retomar **cuando Culqi apruebe la web** y para operar el TLS. Los dos carriles terminaron y su trabajo está en `main`; sus sesiones se pueden archivar.

## Culqi

**Hoy.** La tienda se publica en `Staging` con la pasarela **simulada** (ningún cobro; aviso visible). Es lo que Culqi revisa: catálogo, carrito, checkout, contacto, textos legales y Libro de Reclamaciones. No se necesita nada más de Culqi para esa revisión.

**Hecho (T37).** El adaptador real (`CulqiPasarelaDePago`) está construido y probado solo contra un Culqi **simulado**. Está inerte: solo se compone con `BABELHUB__Pagos__Pasarela=culqi` y una llave del entorno correcto.

**Pendiente, en este orden, cuando Culqi apruebe:**

1. **T38 (lo hace la persona):** prueba del adaptador contra Culqi real con las llaves de **integración** (`pk_test_…`, `sk_test_…`) en su equipo, sin dinero real. Pasos en `specs/ecommerce-pagos/guia-del-spike-culqi.md`. Responde lo que la documentación pública no dice: firma de los avisos, clave de idempotencia y reembolso de Yape (DD-95 a DD-98).
2. **Formulario de Culqi en la pantalla de pago (DD-99 a):** hoy `/checkout/pago` pide pegar a mano el identificador del medio de pago. Sin integrar el *Custom Checkout* (llave pública `pk_`, CSP en `BABELHUB__Web__OrigenesDeLaPasarela`) **un cliente real no puede pagar**. Es desarrollo aparte (spec, plan y tareas).
3. **Pasar a `Production`:** guía 08 §15.1, fase 2 (llave `sk_live_…`; la aplicación se niega a arrancar si entorno y llave no son coherentes). Antes, retirar y purgar las cuentas de prueba (`cuenta-de-prueba --retirar --todas` y `--purgar`; la purga aún no se probó contra el almacén real).

**Sin ratificar:** DD-95 a DD-99. DD-99 (b): no hay forma de usar `sk_test_` en una VM (a propósito, PAG-34).

**Dónde mirar:** `specs/ecommerce-pagos/` (`tasks.md` T37/T38, `decisiones-de-implementacion.md` sección T37, `verificacion.md`), `progress/ajustes-al-implementar.md` §19.

## TLS de PostgreSQL

**Hecho.** PostgreSQL solo acepta TLS verificado desde la red (`ssl=on`, `hostssl`, certificado de la CA privada) y los clientes usan `SSL Mode=VerifyFull`; `desplegar.sh` se niega si falta (DD-90 a DD-94, ratificadas). Evolution API (Prisma) no soporta `verify-full`: usa `sslmode=require` con `sslaccept=strict` (DD-91).

**Pendiente (fila de `progress/hallazgos.md` del 2026-11-25, «Riesgos residuales del TLS»):**

- El certificado del servidor vence a los **825 días** y la CA a los 1825: falta el procedimiento de rotación y una alerta de vencimiento.
- `hostssl` no filtra por origen: el filtro es la regla de red de Oracle.
- Al cambiar la versión de Evolution API, repetir el ensayo con `ENSAYO_EVOLUTION=1`.
- Si la carpeta de secretos se generó con una versión anterior del generador, **regenerarla**.
- Verificar en las VMs reales (no probado en Oracle).
