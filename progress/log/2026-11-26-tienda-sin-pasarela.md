# 2026-11-26 — La tienda que revisa Culqi, sin pasarela

**Carril `sin-pasarela`** (sesión de trabajo por encargo de la coordinación). Rama `claude/sin-pasarela-16e1d1`.

## Qué se hizo
- Valor `ninguna` de `BABELHUB__Pagos__Pasarela` (`Development`, `Test` y `Staging`, sin llave): el cobro se cierra en la Aplicación (`ECO-PAGO-NO-DISPONIBLE`), el pago dice «El pago en línea estará disponible pronto» y no se dibuja ningún aviso de pagos ni de envíos simulados. El simulador se conserva y `Production` no cambia.
- `generar-secretos.sh` escribe `ninguna` por defecto en `staging` (opción `--pasarela`), `desplegar.sh` admite `ninguna` o `simulador`, el ensayo local prueba `ninguna` y la guía 08 explica cómo alternar (commits `4d55fab2` y `8c318263`, ya en `main` con `f9d7b240`).
- `ecommerce-pagos` reabierta (PAG-50 a PAG-52, AC-PAG-42 y AC-PAG-43, D26, T48 a T52) y reaprobada, spec y plan, tras tres rondas de la puerta (`5f6b43b0`, `ce518cf0`, `90351a0d`).
- `90351a0d` es un cambio de producto **posterior a lo publicado**: sin pasarela, la pantalla de pago ya no invita a reintentar una compra cerrada (Importante de la ronda 2).

## Decisiones
- DD-110 a DD-113 ratificadas por la persona (2026-11-26). DD-114 (textos visibles que delatan trabajo en curso) sin ratificar: la coordinación prepara la versión final de los textos legales.
- La persona pidió no cambiar código de producto salvo que un Bloqueante o un Importante lo exija: los Menores de código quedaron en `progress/hallazgos.md`.

## Qué se probó de verdad y qué no
- **Probado:** compilación sin advertencias; unitarias de los dos servicios, arquitectura y herramientas; integración completa sobre `f9d7b240` (8679 correctas, 4 omitidas) y la afectada sobre `90351a0d`; ensayo local completo con las imágenes reales y `ninguna` (sin avisos y cobro hecho a mano rechazado).
- **No probado:** revisión visual en navegador de la pantalla de pago sin pasarela, E2E de Playwright para ese modo y el ensayo con `ENSAYO_PASARELA=simulador`.

## Pendiente / siguiente
- Persona: cambiar `simulador` por `ninguna` en `ecommerce.env` y `ecommerce-gestion.env` ya generados y redesplegar (guía 08 §15.1); publicar `90351a0d` si quiere la corrección.
- Persona: textos legales definitivos (DD-114); T38 (prueba contra Culqi real) cuando lleguen las llaves.
- Menores abiertos de la puerta en `progress/hallazgos.md` (2026-11-26).
