# R6. Auditoría final de coherencia y cobertura

**Fecha:** 2026-11-06. **Alcance:** documentación (`docs/`), specs, `AGENTS.md`, skills y agentes de `.claude/`, pruebas, infraestructura, prototipo y bitácoras, tal como quedaron tras [R5](R5-reformulacion-a-dos-servicios.md). **Propósito:** reunir lo que falta, contradice, quedó a medias o es ambiguo, para cubrirlo en orden. Este informe **no corrige nada**: cada hallazgo indica cómo cubrirlo y si necesita una decisión o una aprobación de la persona responsable.

## 1. Método

| Comprobación | Resultado |
| :--- | :--- |
| Enlaces relativos entre documentos (script) | Sin enlaces rotos. |
| IDs citados que no existen (AD, DA, AM, CTL, RF, RNF, RC, NN, OB y 472 requisitos de specs) | Todos existen (los únicos «no definidos» son los marcadores `…00` de las plantillas). |
| Trazabilidad DA ↔ AD | **20 vínculos sin inverso** (H06). Ninguna AD queda sin driver. |
| `SpecCheck` | 19 specs `Approved`, sin errores; 6 avisos de RNF sin cita (H07). |
| Términos obsoletos (BabelHub.Web, BabelHub.Identidad, AC07 a AC14 viejos, «ingreso inicial», cuatro servicios, base `asistente`) en docs, specs, código, infra y prototipo | Solo quedan los de H01, H03, H05 y H04. Las equivalencias de atributos viejos en `04-atributos-de-calidad.md` §5 son intencionales. |
| Codificación (texto dañado) en el repositorio | Sin problemas. |
| Revisión manual de contradicciones entre specs, `docs/`, prototipo, Compose, Nginx, presupuesto y pruebas | Hallazgos H01 a H18. |

**No se verificó** (ver §5): Compose y script de arranque con Docker, `nginx -t`, cifras de Oracle contra la consola, ejecución real de las pruebas de concurrencia (aún no hay código de dominio) y el contenido completo de `docs/05-seguridad/02-controles-asvs.md`, `08-mapeo-a-estandares.md` y `docs/06-experiencia-de-usuario/04` a `09`, que solo se recorrieron con búsquedas.

## 2. Resumen

| Prioridad | Cantidad | Significado |
| :---: | :---: | :--- |
| **P1** | 5 | Contradicciones o vacíos que hay que cubrir **antes de escribir los planes**. |
| **P2** | 8 | A medias, desactualizado o sin dueño; cubrir durante la planificación. |
| **P3** | 5 | Ambigüedades menores o riesgos ya aceptados que conviene dejar escritos. |

## 3. Hallazgos

### P1. Antes de planificar

| ID | Hallazgo | Dónde | Cómo cubrirlo | Necesita |
| :---: | :--- | :--- | :--- | :--- |
| **H01** | **Spec aprobada con la arquitectura anterior.** `transversal-auditoria` habla de «los tres servicios» y de «Ventas, Inventario e Identidad» (AC-AUD-15 y AC-AUD-16), de «todos los servicios» (AUD-09, AUD-13, AUD-17 y AUD-18) y de «los tres almacenes» (pregunta resuelta). Hoy hay **dos** servicios; Identidad es un módulo (esquema aislado) del E-commerce y no existe «Ventas». La consulta unificada se compone entre el E-commerce y el Inventario, y el orden entre los módulos del E-commerce ya no tiene diferencias de reloj entre servicios. | `specs/transversal-auditoria/spec.md` líneas 18, 40, 44, 48, 49, 73, 74, 84, 86, 90, 103. | Devolverla a `Draft`, reescribir con los dos servicios (registros por módulo en el E-commerce y consulta al Inventario), ajustar AC-AUD-15 y AC-AUD-16, revisarla y **reaprobarla**. | Reaprobación (comportamiento). |
| **H02** | **Requisito derivado sin requisito de origen.** El ingreso de mercadería y el stock inicial (AJU-02, AJU-13, AJU-19, AJU-20, CAT-20 a CAT-23, FUE-19 a FUE-22) citan RF10, RF12 y RF17, pero **RF17 sigue listando solo merma, daño y corrección de conteo**, y RF10 y RF12 no mencionan el stock inicial, la fuente de recepción ni las unidades vendidas. `AGENTS.md` §1: nunca se inventa un requisito. El único respaldo está en `06-procesos-de-negocio.md` y en el glosario. | `docs/01-analisis-de-sistema/02-requisitos.md` (RF10, RF12, RF17); `01-necesidad-del-negocio.md` (NN06). | Ampliar RF10 (stock inicial al dar de alta), RF12 (fuente de recepción y unidades vendidas) y RF17 (motivo «ingreso de mercadería» solo en la fuente de recepción, con referencia y límites) y reflejarlo en la matriz de trazabilidad del análisis. | Su visto bueno (cambio de requisitos). |
| **H03** | **Modelo de dominio sin lo nuevo y con lo viejo.** `07-modelo-de-dominio.md` aún dice «ingreso inicial (solo administración)» en la clase `Ajuste` (línea 63) y en la invariante 10 (línea 85), y no modela la **fuente de recepción**, el **ingreso** (cantidad, referencia, nota, usuario, fecha) ni las **unidades vendidas** y su atribución, que sí están en el glosario, en las specs y en `05-modelo-de-datos.md`. | `docs/01-analisis-de-sistema/07-modelo-de-dominio.md` (clases `Fuente`, `Ajuste`; invariantes). | Agregar `Fuente.esRecepcion`, la entidad `Ingreso`, `UnidadesVendidas` (derivada) y reescribir la invariante 10: el stock inicial entra como ingreso en la fuente de recepción (AJU-19, CAT-20). | Nada (editorial, coherente con specs aprobadas). |
| **H04** | **Proceso desactualizado y sin siguiente paso.** `01-flujo-sdd.md` §6 dice que la fase 0 tiene «19 specs en borrador» y que la fase 2 («Revisión y aprobación de las specs») es la **Siguiente**; en realidad las 19 están aprobadas (2026-10-16 y 2026-11-06). Además **ninguna spec tiene `plan.md` ni `tasks.md`** (0 de 19), así que según `AGENTS.md` §2 todavía no se puede escribir código de producción. | `docs/07-proceso-de-desarrollo/01-flujo-sdd.md` §6; `specs/*/`. | Actualizar la tabla de fases (fase 2 hecha; fase 3 pasa a ser «planes y tareas» por orden de fases) y arrancar `spec-plan` con la primera spec de la fase 3. | Nada (editorial). El orden lo decide el responsable. |
| **H05** | **Skills, agentes y plantilla con la arquitectura anterior.** `arch-review` («Web solo a Shared», sin la regla 13 de módulos) y su agente `arch-reviewer`; `dotnet-feature` («Identidad y Asistente son proyectos únicos…»); `ui-review` y `ui-screen` (hablan de `BabelHub.Web` como proyecto y de «llamar a los servicios»); `spec-plan` («pantallas y clientes (Web)», «Web solo usa `Shared`»), `spec-tasks` y `spec-implement` (capa «Web» separada); `specs/_template/spec.md` (`servicio:` con «Identidad \| Asistente \| Web»). Si se usan tal cual, los planes saldrán con proyectos que ya no existen. | `.claude/skills/*`, `.claude/agents/arch-reviewer.md`, `specs/_template/spec.md`. | Reescribir esas líneas: la presentación es `BabelHub.Ecommerce.Api` (áreas), los módulos son carpetas con contrato público e `Interno` (regla 13), `servicio:` solo E-commerce, Inventario o Transversal. Ejecutar `spec-plan` solo después. | Nada (editorial). |

### P2. A medias, desactualizado o sin dueño

| ID | Hallazgo | Dónde | Cómo cubrirlo | Necesita |
| :---: | :--- | :--- | :--- | :--- |
| **H06** | **Trazabilidad DA ↔ AD incompleta (20 vínculos).** La tabla de decisiones cita drivers que la tabla de drivers no lista como respaldo: AD02→DA08, AD13→DA13, AD15/16/18/19→DA09, AD21→DA07/DA09/DA10, AD22→DA05/DA13, AD23→DA01, AD25→DA03, AD26→DA05, AD30→DA05/DA11/DA13, AD31→DA05/DA13/DA16. | `01-drivers-arquitectonicos.md` (columna «Decisiones»), `02-decisiones-arquitectonicas.md`. | Completar la columna «Decisiones» de cada driver (o quitar el vínculo si no corresponde) y volver a correr el script de la auditoría. | Nada. |
| **H07** | **Seis RNF sin spec ni dueño:** RNF10 (testabilidad), RNF11 (multi-arch), RNF12 (recursos), RNF19 (cadena de suministro, SBOM), RNF24 (respaldo y restauración probados) y RNF28 (contratos versionados y cobertura). Son de plataforma, no de una feature, por eso `SpecCheck` los avisa y ninguna prueba los nombra. También AC02 y AC05 casi no aparecen en las specs (3 y 2 filas): se comprueban con k6, la prueba de arquitectura y CI. | `speccheck.json`; `docs/01-analisis-de-sistema/02-requisitos.md`; `docs/07-proceso-de-desarrollo/03-estrategia-de-pruebas.md`. | Crear una spec transversal de plataforma (`transversal-plataforma`: CI con SBOM y escaneo, imágenes multi-arch, respaldo y restauración, umbrales de recursos y cobertura) con sus criterios, o declarar en `speccheck.json` que esos RNF se verifican fuera de las specs y dónde. Recomendado: la spec. | Decisión y aprobación. |
| **H08** | **Almacén de archivos, pasarela, LLM y correo sin elegir.** B02 (R2 u Oracle Object Storage), B05 (llaves de Culqi), B06 (proveedor de LLM) y B08 (proveedor de correo y SPF/DKIM/DMARC) bloquean los planes de `ecommerce-biblioteca-digital`, `ecommerce-pagos`, `asistente-compra-ia` e `identidad-seguridad`/`ecommerce-libro-reclamaciones`. | `progress/blockers.md`. | Resolver B02, B06 y B08 antes de planificar esas specs; B05 se necesita solo para el modo de pruebas. | Decisión. |
| **H09** | **Base `evolution` a medias.** El despliegue (`05-despliegue.md` líneas 61 y 136) y el presupuesto («una instancia con tres bases») nombran una tercera base `evolution`, pero el criterio vigente es **dos bases** y `infra/postgres/init/01-bases.sh` solo crea `ecommerce` e `inventario`; Evolution API ni siquiera está en el Compose local. | `docs/02-arquitectura-de-software/05-despliegue.md`, `07-presupuesto-de-recursos.md`, `infra/`. | Escribir que son **dos bases de negocio** y que Evolution gestiona su propio esquema técnico (en la misma instancia o en su volumen), y decidir dónde; ajustar el script o el texto. | Decisión menor. |
| **H10** | **Presupuesto de memoria sin margen en VM-INVENTARIO.** Suma ~3,7 GB más el sistema contra un objetivo de 4,2 GB (70 % de 6 GB): el margen real es de unos 0,3 a 0,5 GB y el texto lo da por «dentro del 70 %». PostgreSQL (2,0 GB) + réplica de gestión (450 MB) + Inventario (350 MB) + respaldo (256 MB). | `07-presupuesto-de-recursos.md` líneas 26 a 36. | Marcarlo como el punto más justo (como se hizo con VM-BORDE), fijar el orden de recorte (`shared_buffers`, límites de contenedor, respaldo fuera de horas pico) y medirlo en B11. | Nada. |
| **H11** | **Nginx confía en cualquier IP.** `set_real_ip_from 0.0.0.0/0` permite falsificar `CF-Connecting-IP` a quien llegue directo al puerto y evadir los límites por IP (AD13, AD14, AD24). Está comentado como «en producción, solo los rangos de Cloudflare y del túnel», pero no hay requisito ni prueba que lo exija. | `infra/nginx/nginx.conf` línea 19; `docs/05-seguridad/07-operacion-segura.md`. | Restringir a la red privada del túnel y a los rangos de Cloudflare, bloquear el acceso público directo al puerto de Nginx (lista de seguridad de la VM) y agregar la comprobación a las pruebas de despliegue. | Nada. |
| **H12** | **Infraestructura sin ejecutar.** Compose y script de arranque solo se validaron con `docker compose config` y `sh -n` (Docker no corría); Nginx nunca pasó por `nginx -t`; el Compose local solo tiene PostgreSQL y Redis: `n8n`, Evolution API, cloudflared, Nginx y las imágenes de los dos servicios no están. | `infra/`. | Levantar Compose con Docker, ejecutar `nginx -t` en un contenedor y registrar el resultado en la bitácora. | Nada. |
| **H13** | **Pruebas de arquitectura que pasan «por vacío».** Mientras no haya código, `ReglasDeModulosTests` solo verifica carpetas y las comprobaciones de dependencias no encuentran tipos que violar; y la regla 9 (la web llama a los módulos por contrato y al Inventario solo desde el servidor; el JWT nunca llega al navegador) solo se cubre en parte (referencias de proyecto): lo demás necesita pruebas de integración y E2E, aunque `AGENTS.md` §3 dice que la prueba de arquitectura hace cumplir la 9. | `tests/architecture/`; `AGENTS.md` §3. | Con el primer código, añadir una prueba de sensibilidad (un tipo de ejemplo que viola la regla y debe fallar) y ajustar la redacción de `AGENTS.md` o añadir las pruebas de integración y E2E de la regla 9. | Nada (la redacción de `AGENTS.md` la cambia el responsable). |

### P3. Ambigüedades y riesgos a dejar escritos

| ID | Hallazgo | Dónde | Cómo cubrirlo |
| :---: | :--- | :--- | :--- |
| **H14** | **Vocabulario residual en specs aprobadas.** WEB-26 («si la Web no logra comunicarse con los servicios») ya no es exacto: los módulos del E-commerce están en el mismo proceso y solo el Inventario es remoto; AC-IDE-32 y AC-SEG-15 siguen hablando de «otro servicio» con sentido válido. No cambia el comportamiento. | `specs/web-base/spec.md` línea 57. | Corregir la redacción al planificar y anotarlo en el `CHANGELOG`; no exige volver a `Draft` porque no cambia el comportamiento. |
| **H15** | **Dominio y hosts sin definir.** Los dos hosts (tienda y gestión), las reglas de Cloudflare Access y Turnstile y la restricción de `set_real_ip_from` dependen de B04 (dominio). | `blockers.md` B04; `infra/nginx/nginx.conf`. | Registrar un dominio o dejar nombres de ejemplo (`tienda.example`, `gestion.example`) en el plan de despliegue. |
| **H16** | **Presentaciones y resumen fuera de fecha.** `docs/BabelHub_Exposicion.pptx`, el diseño de Canva y el resumen local aún muestran la arquitectura anterior (cuatro servicios y catorce atributos). | `docs/BabelHub_Exposicion.pptx`; Canva; `docs/resumen-para-exposicion.md` (fuera del repositorio). | Actualizarlos con R5 antes de la exposición. |
| **H17** | **Cifras de Oracle y de proveedores de fuentes secundarias.** Cupos del nivel gratuito (VMs, disco, red), comportamiento de PAYG y recuperación por inactividad, precios de la VM ARM adicional y cuotas de R2/Object Storage no se confirmaron en la consola ni en la documentación oficial vigente. | `05-despliegue.md` §9; `blockers.md` B03. | Confirmarlas al crear la cuenta y anotar la fecha de verificación. |
| **H18** | **Riesgos aceptados que deben seguir visibles.** Si cae VM-INVENTARIO cae la base y, con ella, todo (AD31, defensa en profundidad y réplica de gestión); con 1/8 de OCPU la entrada puede no rendir (B11); consulta legal y de clientes reales no abordada (B09). | `05-despliegue.md` §6; `blockers.md`. | Mantenerlos en el informe final y en la exposición como limitaciones declaradas. |

## 4. Orden propuesto para cubrirlos

1. **Editorial inmediato (sin decisión):** H03, H04, H05, H06, H09, H10, H11 (solo texto y configuración), H14. Un solo commit por grupo.
2. **Con tu visto bueno:** H02 (requisitos) y H01 (devolver `transversal-auditoria` a `Draft`, reescribir y reaprobar).
3. **Decisiones externas:** H07 (¿spec de plataforma?), H08 (B02, B06, B08), H09, H15.
4. **Después, y ya con la base limpia:** `spec-plan` y `spec-tasks` por fase (H04), con H12 y H13 como primeras tareas de infraestructura y pruebas.
5. **Antes de exponer:** H16 y H17.

## 5. Qué está sano

- Sin enlaces rotos; todos los IDs citados existen; `SpecCheck`, compilación con `-warnaserror` y las 23 pruebas de arquitectura correctas.
- Las 19 specs están aprobadas, sin preguntas abiertas, y sus dependencias no forman ciclos entre módulos (`04-modulos-y-contratos.md` §1.1).
- Los números clave coinciden entre specs, documentos y prototipo: reserva de 15 minutos (extensión a 30 por pago pendiente), recojo de 15 días, credencial de 15 minutos, depuración a los 2 años, tope de 5 reservas por hora.
- Los seis atributos de calidad tienen escenario, medida y prueba; `AGENTS.md`, `README.md` y `CLAUDE.md` describen la arquitectura vigente.
- El prototipo tiene un modelo único de stock y es coherente con las specs de ajustes, fuentes y catálogo.

## 6. No verificado

Docker (Compose y script de arranque), Nginx, cifras de Oracle y de proveedores, pruebas de concurrencia (aún no hay código de dominio), y lectura completa de `02-controles-asvs.md`, `08-mapeo-a-estandares.md` y de `docs/06-experiencia-de-usuario/04` a `09`.
