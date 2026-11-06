# R5. Reformulación a dos servicios y seis atributos de calidad

**Fecha:** 2026-11-06. **Quién decide:** la persona responsable. Este informe reemplaza las decisiones de despliegue de [R4](R4-auditoria-del-estilo-y-despliegue.md) (cuatro VMs en dos cuentas, base `asistente`).

## 1. Decisiones

| Tema | Decisión |
| :--- | :--- |
| Servicios | **Dos**: E-commerce (monolito modular, 11 módulos: Catálogo, Carrito y checkout, Pagos, Pedidos, Envíos, Devoluciones, Biblioteca digital, Libro de Reclamaciones, Identidad, Asistente e interfaz web) e Inventario (monolito modular, 4 módulos: Fuentes, Selección y reservas, Traslados y Ajustes). Cuatro capas por servicio (AD01, AD02, AD15). |
| Estilo | Basado en servicios de grano grueso con dos servicios; declarado por debajo del rango habitual de 4–12. Sin ESB (SOA clásica descartada). El monolito único se descarta de forma consciente. |
| Bases | Una por servicio (`ecommerce` e `inventario`), un esquema por módulo, un rol por esquema; Identidad es un esquema aislado por rol (AD03, AD18). Sin base `asistente`. |
| Despliegue | **Solo recursos gratuitos de una cuenta**: VM-BORDE (AMD micro: cloudflared y Nginx), VM-VENTAS (ARM: E-commerce, Redis, n8n, Evolution API, segundo conector), VM-INVENTARIO (ARM: Inventario, réplica de gestión y PostgreSQL), VM-AUX (AMD micro: salto SSH y monitoreo) (AD11). |
| Riesgo aceptado | La base comparte VM con el Inventario: si cae esa VM, cae todo; si se compromete el servidor, la base también. Mitigado con defensa en profundidad (AD31), respaldo y WAL fuera de Oracle y la réplica de gestión. |
| Entrada | Dos hosts (tienda y gestión), Cloudflare con Access y Turnstile, Nginx con límites y límites por actor en cuatro niveles (AD13, AD14, AD24). |
| Atributos de calidad | **De 14 a 6** (AC01 a AC06), cada uno con su prueba y medida; los RNF no cambian. |
| Cambios de negocio | Ingreso de mercadería solo en la fuente de recepción (Lima por defecto), desde el Catálogo; unidades vendidas por sucursal; un título agotado no se da de baja; el administrador puede ajustar cualquier sucursal. |

## 2. Atributos de calidad

| Antes | Ahora |
| :--- | :--- |
| AC01 | **AC01** Consistencia del inventario |
| AC02, AC07 (eficiencia) | **AC02** Rendimiento y eficiencia |
| AC03, AC12 | **AC03** Disponibilidad y recuperabilidad |
| AC05, AC09, AC11, AC13 | **AC04** Seguridad y privacidad |
| AC04, AC06, AC07 (portabilidad) | **AC05** Mantenibilidad y evolucionabilidad |
| AC08, AC10, AC14 | **AC06** Usabilidad y accesibilidad |

Los identificadores se renumeraron en docs y specs con un cambio mecánico (la columna de trazabilidad). Las pruebas por atributo están en `docs/07-proceso-de-desarrollo/03-estrategia-de-pruebas.md`.

## 3. Specs

| Spec | Cambio | Estado |
| :--- | :--- | :--- |
| `inventario-ajustes` | Ingreso de mercadería solo en la fuente de recepción, con referencia, límites y lista (AJU-02, AJU-13, AJU-19 y AJU-20; AC-AJU-10, 15 y 16). | **Approved** (2026-11-06) |
| `inventario-fuentes` | Fuente de recepción, su cambio auditado, unidades vendidas y su atribución (FUE-19 a FUE-22; AC-FUE-16 a AC-FUE-18). | **Approved** (2026-11-06) |
| `ecommerce-catalogo` | Stock inicial al dar de alta y título agotado sin baja (CAT-20 a CAT-23; AC-CAT-16 a AC-CAT-19); depende de `inventario-ajustes`. | **Approved** (2026-11-06) |
| `identidad-seguridad` | Validación por el Inventario y los módulos, contexto de lectura en lugar de credencial para el asistente (IDE-10, 30, 33 y 35). | **Approved** (2026-11-06) |
| `asistente-compra-ia` | Contratos de módulos y contexto de lectura (ASI-06, ASI-19) y límite de concurrencia (ASI-20, AC-ASI-19). | **Approved** (2026-11-06) |
| `transversal-seguridad` | Credencial entre el E-commerce y el Inventario y contexto del actor entre módulos (SEG-15); presupuestos del personal y verificación humana (SEG-30 y SEG-31). | **Approved** (2026-11-06) |
| Las otras 13 | Solo la columna de atributos de calidad; `servicio:` pasa a E-commerce en `web-base` y `web-indice-infinito`; el nombre `BabelHub.Web` en `web-base`. Sin cambio de comportamiento. | Approved |

Se quitaron `aprobado_por` y `aprobado_en` de las seis specs mientras estuvieron en Draft y se volvieron a escribir el 2026-11-06 por indicación expresa del responsable (C09).

### 3.1 Revisión de las seis specs (misma fecha)

Se revisó cada spec contra la arquitectura nueva y contra las demás. Hallazgos y correcciones:

| Spec | Hallazgo | Corrección |
| :--- | :--- | :--- |
| `inventario-ajustes` | AJU-15 daba al administrador «los mismos motivos y reglas» en cualquier sucursal, lo que contradecía AJU-19 (el ingreso solo entra en la fuente de recepción); y la lista de ingresos no negaba el acceso a otros usuarios. | AJU-15 exceptúa el ingreso; AJU-20 rechaza la consulta de otros usuarios (RNF05). |
| `inventario-fuentes` | Un criterio de «vendidas» (AC-FUE-17) era ambiguo; una regla de «fuente inactiva» contradecía la resolución anterior de que desactivar fuentes queda fuera de alcance; faltaba la regla del traslado vinculado para no contar una venta dos veces. | AC-FUE-17 y AC-FUE-16 con datos concretos; se quitó la fuente inactiva; reglas y nota para el plan sobre la atribución (TRA-14). |
| `ecommerce-catalogo` | El botón «Registrar ingreso» del catálogo no tenía requisito propio y los límites de cantidad no estaban en la spec. | CAT-22 y CAT-23 con AC-CAT-18 y AC-CAT-19; CAT-20 con el rango; nota de idempotencia para el plan. |
| `asistente-compra-ia` | Al compartir proceso con la venta, un proveedor lento podía degradar el checkout sin que hubiera un requisito que lo impidiera. | ASI-20 (5 consultas simultáneas y 30 segundos) con AC-ASI-19 sobre el percentil 95 del checkout. |
| `transversal-seguridad` | Los presupuestos por rol del personal y la verificación humana (AD13, AD24) no estaban como requisitos; AC-SEG-14 y AC-SEG-15 hablaban de «servicios» y de «la Web». | SEG-30 y SEG-31 con AC-SEG-27 y AC-SEG-28; AC-SEG-14 y AC-SEG-15 reescritos. |
| `identidad-seguridad` | La regla de pendientes al cancelar una cuenta hacía que Identidad dependiera de otros módulos (ciclo); faltaba qué pasa con la clave pública si cae el E-commerce; vocabulario de servicio y base. | Puerto de pendientes que implementan los demás módulos; nota para el plan sobre la caché de claves y la réplica de gestión; contexto de lectura y esquema `identidad`. |

Resultado: sin preguntas abiertas y `SpecCheck` sin errores. **Aprobadas por la persona responsable el 2026-11-06** (`aprobado_por: Diego Amiquero`) con su indicación expresa; antes se mantuvieron en `Draft`.

## 4. Documentación

- **Estilo** (`03-estilo-arquitectonico.md`): reescrito (dos servicios, módulos, diagrama, alternativas descartadas, alineación con el estilo, relación con SOA y ESB, dominio y subdominio).
- **Despliegue** y **presupuesto de recursos**: reescritos para las cuatro VMs gratuitas, red, base, fallos y mitigaciones, reglas de red.
- **Decisiones**: AD01, AD02, AD03, AD05, AD06, AD07, AD10 a AD16, AD18, AD24, AD27 y AD28 revisadas; nueva **AD31**; comentarios de AD01/02, AD03/18, AD11 y AD15.
- **Drivers y riesgos**, **tácticas** (por los seis atributos), **escenarios** (ingreso y degradación).
- **C4** nivel 2 y 3; **diseño** (Clean Architecture con módulos, módulos y contratos con dependencias entre módulos, modelo de datos con esquemas, ingresos y sesiones).
- **Seguridad** (amenazas, autenticación, asistente, operación segura con RLS, hardening y límites en cuatro niveles); **análisis** (requisitos RC07, RNF12 y RNF24, actores, procesos, atributos); **UX** (pantallas, hosts); **proceso** (convenciones, pruebas por atributo); `glossary.md`, `AGENTS.md` (reglas 4, 5, 9 y nueva 13), `README.md` y `CLAUDE.md`.

## 5. Código e infraestructura

- `src/`: se eliminaron `BabelHub.Identidad`, `BabelHub.AsistenteCompra` y `BabelHub.Web` (solo andamiaje); la presentación vive en `BabelHub.Ecommerce.Api` (áreas, `wwwroot`). Carpetas de módulos por capa (10 en E-commerce y 4 en Inventario).
- `tests/architecture`: referencias para dos servicios (23 pruebas) y **nuevas pruebas de módulos** (regla 13).
- `infra/`: Compose local con una instancia y dos bases; Nginx por host con límites y servidor de respaldo; `.env.example`.
- `prototype/ui-v2/`: stock único, ingreso en el Catálogo, Vendidos y descuento al pasar a en reparto (commit `7e3ae92`).

## 6. Verificación

`dotnet run --project tools/SpecCheck`: sin errores. `dotnet build BabelHub.sln -warnaserror`: correcto. `dotnet test`: todas las pruebas correctas (23 de arquitectura). Diagramas Mermaid renderizados. **No verificado:** el Compose y el script de arranque con Docker (no estaba corriendo), la configuración de Nginx (no hay `nginx` local) y las cifras de Oracle (fuentes secundarias).

## 7. Pendiente

- Planes y tareas de las 19 specs (`spec-plan` y `spec-tasks`).
- Medir la VM-BORDE y la latencia entre VMs (B11).
- Actualizar la presentación, el diseño de Canva y el resumen local, que muestran la arquitectura anterior.
