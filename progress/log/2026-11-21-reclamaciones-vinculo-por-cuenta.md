# 2026-11-21 — Libro de Reclamaciones: vínculo por cuenta (DD-26)

- **Qué:** implementación T24 a T36 y verificación de `ecommerce-libro-reclamaciones` tras la reapertura de DD-26; carril C, rama `claude/keen-ishizaka-8f8cfa`, sin `push`.
- **Decisiones de implementación:** `specs/ecommerce-libro-reclamaciones/decisiones-de-implementacion.md` (filas «DD-26 (implementación, ronda 6)», T25 a T32, T33 y T34, T35 y T36 y las dos revisiones del código).
- **Revisiones:** arquitectura y seguridad del código sin Bloqueantes; corregidos el inicio de la vinculación inicial antes del primer lote, las filas tomadas en orden, la retención del cursor y el aviso de destinatarios.
- **Dependencias cerradas:** carril A dejó `hojas-por-origen` liberable (`7f10b3ad`) y los contratos `IReceptorDeCambiosDeCuenta` e `IClientePorCorreoVerificadoDeSistema`.
- **Verificación:** ver `specs/ecommerce-libro-reclamaciones/verificacion.md` (Converge). Contenedores del entorno E2E (`bhe2e`) apagados y con el volumen borrado.
- **Residual aceptado:** registro con sesión que confirma justo después de cancelar la misma cuenta (spec §6).
