# 2026-11-23 — Entorno `Staging`, aviso de pagos simulados, cuenta de prueba y HTTPS (carril `staging-culqi`)

Encargo: publicar la tienda en `babel-hub.shop` para que Culqi la revise **antes** de integrar la pasarela. Reapertura de `ecommerce-pagos` (PAG-33 a PAG-41, AC-PAG-29 a AC-PAG-36, D25) y DD-65 a DD-69 (pendientes de ratificar).

## Qué quedó
- **Staging de tipo producción** (`EntornoDelServicio.EsDeTipoProduccion`) que admite solo el simulador (`GuardaDeArranqueDePagos.AdmiteElSimulador`) y el operador logístico nulo (no existe uno real, DD-22); `Production` sigue fallando cerrado; `desplegar.sh` repite la comprobación entorno/pasarela/llave; el entorno sale del archivo de secretos (los Compose del E-commerce ya no lo fijan); `generar-secretos.sh --entorno staging|production`.
- **Aviso** «Pagos simulados: no se realiza ningún cobro» (`ModoDePagosDeLaWeb` + `BhAvisoDePagosSimulados`) encima del pie de toda página, en el checkout y en el pago (no se tocó `_Pie`).
- **HTTPS detrás de Cloudflare:** `HttpsDetrasDelBorde` (solo `X-Forwarded-Proto`, `308` de lo declarado HTTP, salud exenta), HSTS y cookies `__Host-` `Secure` verificados, barrido sin `http://`.
- **Cuenta de prueba:** modo de consola `cuenta-de-prueba` (solo `Staging`; `--retirar` también en `Production`), `CuentaDeRevisionDeLaPasarela`; textos legales vigentes sembrados por `migrar` (`BABELHUB__Migrador__Ecommerce__VersionDeTextos`); primera clave de firma.
- **Docs:** guía 08 (dominio, dos fases de Culqi, requisitos, Cloudflare, retiro de la cuenta), `07-operacion-segura` §3.2.0, `05-despliegue` §7.1, AM43 y el dato de contacto en privacidad, ajustes §18 (T39 a T44: `tasks.md` no se tocó), hallazgos.

## Qué se probó de verdad
- Pruebas de integración (PostgreSQL real, Docker): guarda y anfitrión por entorno, aviso en las vistas reales, esquema reenviado y redirección, cuenta de prueba (ingreso real con la contraseña, rotación con cierre de sesiones, auditoría, retiro, cuenta ajena o sin verificar, correo real rechazado), siembra de textos, `DesplegarShTests` (79 de 79).
- **Ensayo local con las imágenes reales** (`scripts/ensayo-local.sh`, ahora en `Staging`): E-commerce de VM-VENTAS y réplica de gestión sanos en `Staging` (la guarda de operador logístico falló la primera vez: corregido); aviso en la portada, HSTS, `308` para HTTP declarado, salud `200` y `cuenta-de-prueba` contra PostgreSQL real (código 0). `n8n` no arranca en este equipo (`/home/node/.cache`, ajeno; hallazgo).
- Defectos hallados al probar: la primera clave de firma se generaba fuera de la transacción (corregido); el operador logístico nulo bloqueaba el arranque de `Staging` (PAG-39); tipos con `DePrueba` rompían la prueba de arquitectura (renombrados).

## Qué no se probó
- Cloudflare, el túnel, Gmail/SMTP reales, Culqi, WhatsApp, E2E de Playwright con el aviso, el flujo de `Production` con la pasarela real (no existe el adaptador).

## Proceso
El código se redactó en paralelo a la reapertura de la spec (antes de la reaprobación); la puerta (spec-auditor, arch-reviewer, security-reviewer) revisó spec y código y se repitió tras corregir. Ver `progress/aprobaciones.md`.

Resultado: la spec **no se aprobó** (sigue en `Draft`). Tras la ratificación de DD-65 a DD-69 y DD-85 a DD-88 y una ronda extra, arquitectura y seguridad quedaron sin Bloqueantes ni Importantes; el auditor dejó 3 Importantes que se corrigieron en la spec (definición de cuenta de prueba, PAG-47 y «primer ingreso», alcance de la purga) y queda la decisión de negocio **DD-89** (compras simuladas de usuarios reales y pedidos no terminales en la purga). Hace falta una nueva revisión del auditor antes de aprobar spec y plan.
