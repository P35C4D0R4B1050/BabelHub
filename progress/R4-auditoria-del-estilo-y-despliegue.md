# R4. Auditoría del estilo basado en servicios y del despliegue

> **Nota (2026-11-06, posterior):** las decisiones de despliegue de este informe (cuatro VMs en dos cuentas, base `asistente`) fueron reemplazadas por [R5](R5-reformulacion-a-dos-servicios.md). Se conservan los hallazgos de estilo y de red como historial.

**Fecha:** 2026-11-06. **Alcance:** `docs/02-arquitectura-de-software`, `docs/04-modelo-c4`, `docs/05-seguridad` (red y datos), requisitos de infraestructura (RC07, RNF12) y archivos de `infra/`. Las specs no se tocaron: ninguna menciona máquinas, esquemas de datos ni componentes de infraestructura, por lo que siguen **Approved**.

## 1. Motivo

Una observación sobre el diagrama de despliegue (cuatro servicios en una sola VM) y nueva información sobre Oracle: el cupo gratuito es de **2 OCPU y 12 GB ARM por cuenta** y 2 VMs x86 micro, es decir, **como máximo 4 VMs por cuenta** (2 ARM útiles). La pasarela de pago queda en **Culqi**. Se decidió considerar una segunda cuenta de Oracle y se preguntó si una instancia de PostgreSQL con varias bases por servicio es aceptable.

## 2. Respuesta sobre el estilo

El estilo declarado, **basado en servicios de grano grueso**, es el correcto para el problema y está bien fundamentado en lo esencial (AD01, AD02, AD04, AD09), pero la documentación tenía cuatro defectos:

| # | Hallazgo | Gravedad | Resolución |
| :--- | :--- | :---: | :--- |
| H1 | No distinguía la **unidad de despliegue** (servicio, imagen) del **host** (VM). El diagrama mostraba todo en una VM de 1 OCPU y no explicaba por qué eso no es un monolito ni qué se pierde. | Alta | `05-despliegue.md` §2 y §3: criterios de asignación, perfil objetivo de cuatro VMs y perfil mínimo; AD11 reformulada. |
| H2 | No mostraba **cómo cumple** las características del estilo (4–12 servicios, despliegue independiente, UI separada, base compartida o dividida, REST, coordinación entre servicios), ni cuándo revisarlo. | Alta | `03-estilo-arquitectonico.md` §7: tabla de alineación, desviaciones conscientes y criterios de revisión. |
| H3 | **AD03 y AD18 mezclaban criterios:** «una base por sistema» con instancias separadas, e Identidad como esquema dentro de la instancia de Ventas (acoplada a ella y con aislamiento débil). | Media | Una instancia, una base lógica por servicio y un rol por base (AD03, AD18); comparación de tres opciones en `05-despliegue.md` §5.2. |
| H4 | La frase «base por servicio» como regla del estilo es inexacta: en el estilo basado en servicios lo habitual es **compartir** la base; la base por servicio es la regla de los microservicios. | Media | Aclarado en AD03 y en el estilo; la elección es una variante justificada, no una exigencia. |

**Veredicto:** la arquitectura **no se reformula**; se corrige la documentación y el reparto. Un monolito se descarta porque cada servicio tiene imagen, versión, configuración y base propias y no hay referencias de código entre servicios (prueba de arquitectura). Una VM por servicio no es requisito del estilo.

## 3. Hallazgos técnicos del despliegue

| # | Hallazgo | Gravedad | Resolución |
| :--- | :--- | :---: | :--- |
| H5 | Los supuestos de «red interna de Docker» (AM24, AD16, operación segura) solo valen dentro de una VM. Con varias VMs la red entre ellas es una frontera de confianza. | Alta | Sección de red: escucha solo en la dirección privada, malla WireGuard, credencial de servicio, `pg_hba` y TLS, Redis con ACL. AM24 y las fronteras de confianza actualizadas. |
| H6 | RC07, RNF12, AD11 y el README asumían 2 VMs ARM. La hipótesis de 4 ARM en una cuenta ya estaba descartada. El perfil de cuatro VMs exige **una segunda cuenta, que el FAQ de Oracle prohíbe** (una cuenta gratuita por persona; riesgo de suspensión). | Alta | Riesgo registrado (B10). La cuenta secundaria solo aloja cómputo sin estado; el estado y el Inventario están en la principal; perfil mínimo completo como contingencia; alternativa de pagar las VMs adicionales. |
| H7 | Más VMs en serie, sin redundancia, **aumentan los puntos de falla** y no existía una matriz de fallos. | Media | `05-despliegue.md` §7: efecto y recuperación por VM, por cuenta y total. |
| H8 | **VM-DATOS** sigue siendo el punto de falla total; la separación de lo crítico no lo cambia. | Media | Aceptado y documentado: RPO ≤ 24 h, RTO ≤ 4 h, respaldo por base fuera de la VM. |
| H9 | Una VM con solo el Inventario tendrá poco uso: Oracle puede recuperar instancias inactivas. | Media | PAYG sin superar el cupo, presupuesto y alerta de 1 USD (B03). |
| H10 | La latencia añadida por E-commerce → Inventario y por las consultas a la base entre VMs puede afectar RNF02. | Media | Medición en k6 y prueba de concepto de red (B11); si falla, agrupar Inventario y E-commerce. |
| H11 | Redis compartido sin autenticación ni espacios de nombres. | Baja | Contraseña, ACL por servicio y prefijos de claves. |
| H12 | Credenciales de 15 minutos validadas en cada servicio dependen de la hora de cada VM. | Baja | `chrony` en todas las VMs y tolerancia de desviación (propuesta: ±60 s). |
| H13 | Sin orden de arranque entre VMs, un servicio puede iniciar sin su base o su dependencia. | Baja | Reintentos con esperas crecientes y `healthcheck` por servicio. |
| H14 | Secretos: cada VM debía recibir solo los suyos; antes todos vivían en una VM. | Baja | Regla de mínimo privilegio por VM (Culqi solo en VM-VENTAS; clave de firma solo en VM-BORDE). |
| H15 | Los registros quedan repartidos en cuatro VMs. | Baja | `traceId` como clave de correlación; sin agregación central (fuera de alcance); consulta por SSH desde VM-AUX. |
| H16 | El Compose local tenía dos instancias de PostgreSQL y no reflejaba AD03. | Baja | `infra/docker-compose.local.yml` con una instancia y `infra/postgres/init/01-bases.sh`; `.env.example` actualizado. |
| H17 | **El Asistente no tenía dónde guardar sus eventos de seguridad.** La documentación decía que no tiene base, pero ASI-05 exige registrar el intento como evento de seguridad y AUD-10 y AUD-11 piden conservarlo dos años, como en los demás servicios. | Media | Base mínima `asistente` con la tabla `auditoria` (y, si el plan lo decide, `eventos_limite`); sin consultas ni respuestas (ASI-18). Añadida a los documentos y al Compose local. |

## 4. Respuestas a las preguntas

1. **Free Tier y PAYG.** Es correcto: PAYG no amplía el cupo gratuito; lo que lo supere se cobra. Quedan 2 VMs ARM y 2 VMs micro por cuenta, máximo 4 VMs, y la idea de 4 ARM en una cuenta no es válida. Las cifras provienen de fuentes secundarias y se verifican en la consola.
2. **Segunda cuenta.** El responsable la adoptó el 2026-11-06, también en PAYG, como perfil objetivo con las salvaguardas de H6; el riesgo queda aceptado (B10).
3. **Documentación del estilo.** Ver §2.
4. **Culqi.** Confirmada (AD17; C05).
5. **Una instancia con varias bases.** Sí, con una base y un rol por servicio. Ver H3 y `05-despliegue.md` §5.2. Se mantiene el aislamiento de seguridad porque la VM de datos sigue sin acceso desde Internet.
6. **Otras brechas.** H5 a H17.

## 5. Pendiente (no es de documentación)

- Comprobar si ser PAYG cambia la regla de una cuenta gratuita por persona; la segunda cuenta ya está decidida (B10).
- Prueba de concepto de red (B11) y medición con k6 antes de aprobar el despliegue.
- Planes de las 19 specs (`spec-plan`) y sus tareas; hasta entonces no hay tareas que corregir.
- La presentación (`docs/BabelHub_Exposicion.pptx`), su resumen local y el diseño de Canva siguen mostrando el despliegue anterior (diapositivas 6 y 13): actualizar si se van a presentar.
