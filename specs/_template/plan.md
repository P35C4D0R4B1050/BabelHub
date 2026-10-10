---
spec: nombre-de-la-feature
estado: Draft
---

# Plan — Título de la feature

> **Cómo se construye.** Parte de una spec **Approved**. Asigna cada pieza a una capa y comprueba las reglas de `AGENTS.md`. No agrega comportamiento nuevo: si hace falta, se vuelve a la spec.

## 1. Resumen técnico

Enfoque en pocas líneas y qué decisiones tiene detrás (enlaza `decisions.md` y los AD de `docs/02-arquitectura-de-software/02-decisiones-arquitectonicas.md`).

## 2. Capas afectadas

| Capa | Piezas nuevas o modificadas |
| :--- | :--- |
| **Dominio** | Entidades, objetos de valor, reglas, estrategias. |
| **Aplicación** | Casos de uso, puertos. |
| **Infraestructura** | Adaptadores (persistencia, caché, clientes externos). |
| **Api** | Endpoints y traducción de errores. |
| **Web** | Pantallas y clientes HTTP (si aplica). |
| **Shared** | Contratos (DTOs) que cambian. |

## 3. Modelo de datos

Esquema, tablas y restricciones (únicos, claves, índices) y cómo se migran.

## 4. Contratos

| Operación | Método y ruta | Entrada | Salida | Errores |
| :--- | :--- | :--- | :--- | :--- |
| | | | | |

## 5. Concurrencia, transacciones e idempotencia

Dónde puede haber carreras, qué garantiza la infraestructura y cómo se prueba.

## 6. Seguridad y alcance

Roles y alcance por sucursal; datos sensibles; auditoría.

## 7. Pantallas

Pantallas de `BabelHub.Web` (enlaza el prototipo `prototype/ui-v2/`) y estados que cubren.

## 8. Estrategia de pruebas

| Criterio | Tipo de prueba | Dónde |
| :--- | :--- | :--- |
| AC-XXX-01 | Unitaria / integración / E2E | `tests/...` |

## 9. Riesgos

| Riesgo | Mitigación |
| :--- | :--- |
| | |

## 10. Verificación contra AGENTS.md

- [ ] Dominio sin dependencias externas.
- [ ] Puertos definidos en Aplicación; adaptadores solo en Infraestructura.
- [ ] Sin referencias entre servicios; Web solo usa Shared.
- [ ] Alcance por sucursal comprobado en la Aplicación.
- [ ] Sin secretos ni archivos digitales en repositorio o base de datos.
