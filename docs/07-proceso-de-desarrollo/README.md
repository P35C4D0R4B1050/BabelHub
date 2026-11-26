# Proceso de desarrollo

Cómo se construye y se verifica BabelHub: Spec-Driven Development, convenciones técnicas y estrategia de pruebas.

| Documento | Para qué sirve |
| :--- | :--- |
| [01 Flujo SDD](01-flujo-sdd.md) | Ciclo de una feature, puertas humanas, validaciones, skills y agentes. |
| [02 Convenciones técnicas](02-convenciones-tecnicas.md) | Git, nombres, API, configuración, persistencia, CI y Docker. |
| [03 Estrategia de pruebas](03-estrategia-de-pruebas.md) | Pirámide, umbrales de cobertura, dobles de prueba y puertas. |
| [04 Aprobación delegada](04-aprobacion-delegada.md) | Cuándo y cómo el agente aprueba specs y planes por delegación: gravedad, puerta, registro y límites. |
| [05 Desarrollo por bloques](05-desarrollo-por-bloques.md) | Rigor graduado por riesgo, bloques, carriles en paralelo, informe de cierre y orden de sacrificio (decisión del 2026-11-14). |
| [06 Plan del Sprint 2](06-plan-sprint-2.md) | Rúbrica y evidencia, alcance congelado, calendario hasta el 28-nov, control de tokens (relevos), CI/CD y Dependabot. |
| [08 Guía del primer despliegue](08-guia-primer-despliegue.md) | Paso a paso para quien nunca usó Docker, Oracle ni Ubuntu: ensayo local, imágenes en GHCR, cuenta y VMs de Oracle, secretos, VM-INVENTARIO, Cloudflare, VM-VENTAS (con la entrada), lo que falta sin VM-AUX y lo que sigue (Culqi, LLM, WhatsApp, spikes T01 a T05). |

Las reglas obligatorias están en `AGENTS.md`; este directorio las desarrolla.
