# Specs de BabelHub

Fuente de verdad de **qué** debe hacer el sistema. Cada carpeta es una feature. El ciclo completo, sus puertas y la verificación están en `docs/07-proceso-de-desarrollo/01-flujo-sdd.md`.

## Estados

`Draft → Approved → Planned → InProgress → Done`

| Estado | Qué significa | Quién lo cambia | Exige (lo valida `SpecCheck`) |
| :--- | :--- | :--- | :--- |
| **Draft** | Borrador del agente, en revisión. | Agente | Estructura, EARS, criterios y trazabilidad correctos. |
| **Approved** | Una persona aprobó el comportamiento. | **Persona** | Cero preguntas abiertas, `aprobado_por` y `aprobado_en`. |
| **Planned** | Hay plan y tareas, y una persona aprobó el plan. | **Persona** | `plan_aprobado_en`, `plan.md`, `tasks.md` y cada criterio cubierto por una tarea. |
| **InProgress** | Se está implementando, tarea por tarea. | Agente | Cada criterio nombrado en una prueba de `tests/`. |
| **Done** | La verificación converge. | Agente, tras `spec-verify` | Tareas marcadas y `verificacion.md` con `Resultado: Converge`. |

Una spec aprobada que cambia de comportamiento vuelve a **Draft**.

## Convenciones

- Carpeta = `id` de la spec; cada spec tiene un **prefijo** de tres letras (`FUE`, `SEL`, …).
- Requisitos: `XXX-NN`. Criterios de aceptación: `AC-XXX-NN`. Los identificadores son únicos en todo el repositorio.
- Los requisitos siguen EARS (ubicuo, evento, estado, no deseado, opcional), contienen «deberá» y **no nombran tecnología**.
- Cada requisito se cubre con al menos un criterio *Dado / cuando / entonces*, y cada criterio con al menos una prueba (`[Trait("Criterio", "AC-XXX-NN")]`).
- Las plantillas están en `_template/`; la configuración del validador, en `speccheck.json`.

## Validar

```bash
dotnet run --project tools/SpecCheck                    # valida todo
dotnet run --project tools/SpecCheck -- --write-matrix  # regenera TRACEABILITY.md
```

`TRACEABILITY.md` se genera y se versiona; `SpecCheck` falla si está desactualizado.

## Índice

| Spec | Servicio | Prefijo | Depende de |
| :--- | :--- | :---: | :--- |
| `transversal-auditoria` | Transversal | AUD | — |
| `inventario-fuentes` | Inventario | FUE | auditoría |
| `inventario-seleccion-fuente` | Inventario | SEL | fuentes, auditoría |
| `inventario-ajustes` | Inventario | AJU | fuentes, auditoría |
| `inventario-traslados` | Inventario | TRA | fuentes, selección de fuente, auditoría, notificaciones |
| `identidad-seguridad` | Identidad | IDE | auditoría |
| `transversal-notificaciones` | Transversal | NOT | identidad |
| `ecommerce-catalogo` | E-commerce | CAT | fuentes, auditoría |
| `ecommerce-carrito-checkout` | E-commerce | CAR | catálogo, selección de fuente |
| `ecommerce-pagos` | E-commerce | PAG | carrito y checkout, selección de fuente, auditoría |
| `ecommerce-pedidos` | E-commerce | PED | pagos, selección de fuente, notificaciones, auditoría |
| `ecommerce-biblioteca-digital` | E-commerce | BIB | catálogo, pagos, auditoría |
| `ecommerce-envios` | E-commerce | ENV | pedidos, notificaciones |
| `ecommerce-devoluciones` | E-commerce | DEV | pedidos, pagos, biblioteca digital, ajustes |
| `ecommerce-libro-reclamaciones` | E-commerce | REC | auditoría, notificaciones |
| `asistente-compra-ia` | Asistente | ASI | catálogo, pedidos, identidad |
| `web-base` | Web | WEB | identidad, libro de reclamaciones |
| `web-indice-infinito` *(opcional)* | Web | IND | web base |

## Orden de aprobación e implementación sugerido

| Orden | Specs | Motivo |
| :---: | :--- | :--- |
| 1 | `transversal-auditoria` | Los ajustes, traslados y precios la usan. |
| 2 | `inventario-fuentes`, `inventario-seleccion-fuente`, `inventario-ajustes`, `inventario-traslados` | Núcleo y mayor riesgo (AC01). |
| 3 | `identidad-seguridad`, `transversal-notificaciones`, `web-base` | Activan el alcance por sucursal, los avisos y la interfaz. |
| 4 | `ecommerce-catalogo`, `ecommerce-carrito-checkout`, `ecommerce-pagos`, `ecommerce-pedidos` | Camino crítico de la compra. |
| 5 | `ecommerce-biblioteca-digital`, `ecommerce-envios`, `ecommerce-devoluciones`, `ecommerce-libro-reclamaciones` | Completan el ciclo digital, de entrega, de retorno y legal. |
| 6 | `asistente-compra-ia`, `web-indice-infinito` | Cierre. |
