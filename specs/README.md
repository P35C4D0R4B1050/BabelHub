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
| `transversal-seguridad` | Transversal | SEG | `transversal-auditoria` |
| `transversal-plataforma` | Transversal | PLA | — |
| `inventario-fuentes` | Inventario | FUE | `identidad-seguridad`, `transversal-auditoria` |
| `inventario-seleccion-fuente` | Inventario | SEL | `inventario-fuentes`, `identidad-seguridad`, `transversal-auditoria`, `transversal-seguridad` |
| `inventario-ajustes` | Inventario | AJU | `inventario-fuentes`, `identidad-seguridad`, `transversal-auditoria` |
| `inventario-traslados` | Inventario | TRA | `inventario-fuentes`, `inventario-seleccion-fuente`, `identidad-seguridad`, `transversal-auditoria`, `transversal-notificaciones` |
| `identidad-seguridad` | E-commerce | IDE | `transversal-auditoria`, `transversal-seguridad` |
| `transversal-notificaciones` | Transversal | NOT | `identidad-seguridad`, `transversal-seguridad` |
| `ecommerce-catalogo` | E-commerce | CAT | `inventario-fuentes`, `inventario-ajustes`, `transversal-auditoria` |
| `ecommerce-carrito-checkout` | E-commerce | CAR | `ecommerce-catalogo`, `inventario-seleccion-fuente`, `identidad-seguridad`, `transversal-seguridad` |
| `ecommerce-pagos` | E-commerce | PAG | `ecommerce-carrito-checkout`, `identidad-seguridad`, `inventario-seleccion-fuente`, `transversal-auditoria` |
| `ecommerce-pedidos` | E-commerce | PED | `ecommerce-pagos`, `ecommerce-biblioteca-digital`, `inventario-seleccion-fuente`, `transversal-notificaciones`, `transversal-auditoria` |
| `ecommerce-biblioteca-digital` | E-commerce | BIB | `ecommerce-catalogo`, `ecommerce-carrito-checkout`, `ecommerce-pagos`, `transversal-auditoria`, `transversal-seguridad` |
| `ecommerce-envios` | E-commerce | ENV | `ecommerce-pedidos`, `ecommerce-carrito-checkout`, `inventario-seleccion-fuente`, `identidad-seguridad`, `transversal-notificaciones`, `transversal-auditoria`, `transversal-seguridad` |
| `ecommerce-devoluciones` | E-commerce | DEV | `ecommerce-pedidos`, `ecommerce-pagos`, `ecommerce-biblioteca-digital`, `inventario-ajustes`, `transversal-auditoria`, `transversal-notificaciones`, `identidad-seguridad` |
| `ecommerce-libro-reclamaciones` | E-commerce | REC | `identidad-seguridad`, `transversal-notificaciones`, `transversal-auditoria`, `transversal-seguridad` |
| `asistente-compra-ia` | E-commerce | ASI | `ecommerce-catalogo`, `ecommerce-pedidos`, `identidad-seguridad`, `transversal-seguridad` |
| `web-base` | E-commerce | WEB | `identidad-seguridad`, `ecommerce-libro-reclamaciones`, `ecommerce-catalogo`, `ecommerce-carrito-checkout`, `ecommerce-biblioteca-digital`, `transversal-auditoria`, `transversal-seguridad` |
| `web-indice-infinito` *(opcional)* | E-commerce | IND | `web-base`, `transversal-seguridad` |

## Orden de aprobación e implementación sugerido

| Orden | Specs | Motivo |
| :---: | :--- | :--- |
| 1 | `transversal-auditoria`, `transversal-seguridad`, `transversal-plataforma` (integración continua, respaldo y umbrales desde el inicio) | Los ajustes, traslados y precios usan la auditoría; la seguridad define los errores, límites y registros que todas reutilizan. |
| 2 | `identidad-seguridad`, `transversal-notificaciones` y la estructura de `web-base` | Activan el alcance por sucursal, los avisos y la interfaz; el Inventario valida sus credenciales, por eso van antes. |
| 3 | `inventario-fuentes`, `inventario-seleccion-fuente`, `inventario-ajustes`, `inventario-traslados` | Núcleo y mayor riesgo (AC01). |
| 4 | `ecommerce-catalogo`, `ecommerce-carrito-checkout`, `ecommerce-pagos`, `ecommerce-biblioteca-digital`, `ecommerce-pedidos` | Camino crítico de la compra; los pedidos incluyen líneas digitales. |
| 5 | `ecommerce-envios`, `ecommerce-devoluciones`, `ecommerce-libro-reclamaciones` y el cierre de `web-base` | Completan el ciclo de entrega, de retorno y legal. |
| 6 | `asistente-compra-ia`, `web-indice-infinito` | Cierre. |

El orden respeta la columna «Depende de» (se calculó a partir del encabezado de cada spec). `web-base` depende de varias features porque muestra sus pantallas: se construye de forma incremental y solo pasa a `Done` cuando todas están.
