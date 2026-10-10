---
name: spec-plan
description: Escribe el plan técnico (plan.md y decisions.md) de una spec aprobada, asignando cada pieza a su capa de Clean Architecture. Úsala cuando una spec esté Approved y se pida «planificar», «diseñar la solución» o «crear el plan» de una feature.
---

# spec-plan — Del comportamiento al diseño

Convierte una spec **Approved** en `specs/<feature>/plan.md`. El plan dice **cómo**, sin agregar comportamiento nuevo.

## Precondiciones (si no se cumplen, detente)

- La spec está en `Approved` (`aprobado_por` y `aprobado_en` escritos por una persona) y `SpecCheck` pasa.
- Las specs de las que depende están al menos `Approved`; si hace falta, anota cómo se integra con ellas.

## Pasos

1. Lee `AGENTS.md`, `docs/02-arquitectura-de-software/` (AD, estilo, despliegue), `docs/03-diseno-de-software/` y `docs/07-proceso-de-desarrollo/02-convenciones-tecnicas.md`, y la spec completa.
2. Copia `specs/_template/plan.md` y complétalo:
   - **Capas afectadas:** entidades, objetos de valor y reglas (Dominio); casos de uso y puertos (Aplicación); adaptadores (Infraestructura); endpoints (Api); pantallas y clientes (Web); contratos (Shared).
   - **Modelo de datos:** esquema (`ventas`, `identidad`, `inventario`), tablas, restricciones, índices, migraciones.
   - **Contratos:** operaciones, rutas `/api/<servicio>/v1/...`, errores `problem+json` con `code` estable (ver `docs/07-proceso-de-desarrollo/02-convenciones-tecnicas.md`).
   - **Concurrencia, transacciones e idempotencia:** dónde puede haber carreras y qué garantiza la infraestructura.
   - **Seguridad y alcance:** roles, alcance por sucursal en la Aplicación, datos sensibles, auditoría en la misma transacción.
   - **Pantallas:** enlaza el prototipo `prototype/ui-v2/` y los estados cubiertos.
   - **Estrategia de pruebas:** tabla criterio → tipo de prueba → ruta.
   - **Riesgos y mitigaciones.**
3. Resuelve las decisiones técnicas que la spec dejó al plan y regístralas en `decisions.md` con su motivo y alternativa descartada. Una decisión que afecte a varias features pide un AD en `docs/02-arquitectura-de-software/02-decisiones-arquitectonicas.md`: propónlo, no lo agregues sin confirmación.
4. Completa la lista **Verificación contra AGENTS.md** y corrige el plan hasta que todo cumpla (dependencias entre capas, sin referencias entre servicios, `Shared` solo contratos, Web solo usa `Shared`, sin secretos ni archivos digitales fuera del almacén).
5. Pasa el plan por el agente `arch-reviewer` y corrige sus hallazgos.
6. Detente: pide la revisión humana del plan. **No** escribas `plan_aprobado_en` ni cambies el estado a `Planned`.

## Reglas

- Si el plan exige cambiar el comportamiento, vuelve a la spec (pasa a `Draft`) en lugar de ajustar el plan.
- Nada de código de producción en este paso.
- Ordena lo difícil primero: lo que reduce el riesgo (concurrencia, integraciones externas) va antes en las tareas.
