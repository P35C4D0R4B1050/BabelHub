# COMP-2 — migrador de esquemas y roles de PostgreSQL (2026-11-19)

**Hecho.** Modo `migrar` en el Api de cada servicio (`ModoMigrar`), con `MigradorDeLaBase` (el motor, idéntico en los dos) y el plan de cada servicio (`PlanDeMigracionDelEcommerce`: plataforma, auditoría, identidad, web, catálogo, carrito, pagos, envíos y reclamaciones; `PlanDeMigracionDelInventario`: plataforma, auditoría, fuentes, reservas, ajustes y traslados). Las cuentas de conexión se leen de las variables del propio servicio; la cuenta propietaria de `BABELHUB__Migrador__<Servicio>__Propietaria`. Decisión y razones en `specs/transversal-plataforma/decisiones-de-implementacion.md`.

**Despliegue.** `scripts/desplegar.sh inventario` exige `migrador.env`, levanta PostgreSQL, migra las dos bases y después levanta todo; `infra/README.md` explica la migración en local.

**Pruebas.** `MigradorDeLaBaseTests` (8, Testcontainers: esquema limpio de cada servicio, repetición con rotación, orden de los pasos y sin secretos en la salida, variable que falta, cuenta no administradora o igual a las del servicio, rol que falta, paso que falla revertido) y cuatro casos nuevos de `DesplegarShTests` (orden postgres → migrar → levantar, otras VM no migran, sin `migrador.env`, migración que falla).

**Pendiente.** Esquema de Pedidos (sin infraestructura), TLS a PostgreSQL, que la restauración (PLA-19/PLA-43) vuelva a crear roles y cuentas, y una prueba de arranque del servicio contra la base migrada (ver COMP-1).
