# Estrategia de pruebas

Define qué se prueba, con qué herramienta, en qué capa y con qué umbrales. Parte de la regla del proyecto: **cada criterio de aceptación (`AC-XXX-NN`) tiene al menos una prueba que lo nombra** (RNF10), y la lógica de negocio se prueba primero (AGENTS.md, regla 7). Respalda RNF28 y AC05.

## 1. Pirámide de pruebas

| Nivel | Qué prueba | Herramienta | Velocidad | Dónde |
| :--- | :--- | :--- | :---: | :--- |
| **Unitarias de Dominio** | Reglas del negocio sin base ni red: stock vendible, estados, límites, invariantes | xUnit 2.9.3 | Muy rápida | `tests/unit` |
| **Unitarias de Aplicación** | Casos de uso con puertos simulados: orquestación, alcance por sucursal, auditoría | xUnit | Rápida | `tests/unit` |
| **De arquitectura** | Reglas de referencias y de tipos entre proyectos y límites entre módulos (solo contratos públicos, sin ciclos) | NetArchTest | Rápida | `tests/architecture` |
| **De integración** | Infraestructura con la base real: migraciones, restricciones, concurrencia, adaptadores | xUnit + Testcontainers | Media | `tests/integration` |
| **De contrato** | Formato de errores, esquemas y compatibilidad entre el E-commerce y el Inventario | xUnit | Media | `tests/integration` |
| **E2E** | Recorridos completos en navegador, accesibilidad y cabeceras | Playwright + axe | Lenta | `tests/e2e` |
| **De carga** | Rendimiento y límites bajo concurrencia | k6 | Lenta | `tests/load` |
| **De seguridad** | SAST, dependencias, imágenes, secretos y DAST básico | CodeQL, Dependabot, Trivy, escáner de secretos, ZAP baseline | Variable | CI |
| **De usabilidad** | Tareas con personas | Sesiones moderadas | Manual | [pruebas de usabilidad](../06-experiencia-de-usuario/09-pruebas-de-usabilidad.md) |

La base de la pirámide (Dominio y Aplicación) es la más numerosa; las lentas se reservan para lo que solo ellas pueden demostrar.

## 2. Reglas

1. **Prueba primero para reglas de negocio:** se escribe la prueba que falla y luego el código (TDD), de adentro hacia afuera.
2. **Trazabilidad:** cada prueba de un criterio lleva `[Trait("Criterio", "AC-XXX-NN")]`; `SpecCheck` falla si un criterio no está nombrado en ninguna prueba: en `InProgress`, para los criterios cuyas tareas ya están todas marcadas; en `Done`, para todos.
3. **Nombres descriptivos** en español: `Reservar_SinStockVendible_Rechaza`.
4. **Sin red ni base** en Dominio y Aplicación; las pruebas de integración usan Testcontainers (PostgreSQL y un emulador compatible con S3) y necesitan Docker.
5. **Pruebas deterministas:** el tiempo, los identificadores y el azar se inyectan; ninguna prueba depende del reloj real ni del orden.
6. **No se debilitan pruebas ni validaciones para que algo pase.** Si falla la prueba de arquitectura, se corrige el código.
7. **Concurrencia real:** las reglas de stock, límites y descargas se prueban con solicitudes simultáneas sobre la base real ([estados y concurrencia](../03-diseno-de-software/06-estados-y-concurrencia.md)).
8. **Datos de prueba** de ejemplo, nunca datos reales (salvo el ensayo de restauración aislado, PLA-22); secretos de prueba generados y descartables.

## 3. Cobertura por criticidad (RNF28)

El 2026-11-27 la persona responsable decidió recortar las pruebas: la exigencia alta queda solo en lo crítico. La cobertura de líneas se mide **por módulo**:

| Criticidad del módulo | Dominio y Aplicación | Total del módulo (cuatro capas) |
| :--- | :---: | :---: |
| **Crítico** (y todo módulo sin clasificar) | **≥ 85 %** | **≥ 70 %** |
| **No crítico** | **≥ 60 %** | **≥ 40 %** |

### 3.1 Clasificación

| Por qué es crítico | Módulos del Inventario | Módulos del E-commerce |
| :--- | :--- | :--- |
| Stock | Reservas, Fuentes, Traslados, Ajustes | |
| Dinero y pedidos | | Pagos, Pedidos |
| Identidad y acceso por rol y sucursal | | Identidad, Web (sesión del servidor, área por rol y la Presentación de las áreas) |
| Biblioteca digital | | Biblioteca |
| Auditoría y datos personales | Auditoría | Auditoría, Reclamaciones |
| Seguridad transversal (límites, secretos) | Plataforma | Plataforma (incluye el arranque, la composición y la migración) |

**No críticos:** Catálogo, Carrito, Envíos, Devoluciones, Notificaciones y Asistente.

La única lista que se hace cumplir está en `tools/CoverageCheck/PoliticaDeCobertura.cs`; esta tabla la explica. Un módulo es la carpeta de primer nivel de cada capa; las carpetas `Areas`, `Controllers` y `Views` cuentan como Web, y la raíz del proyecto, `Composicion` y `Migracion`, como Plataforma. Un módulo nuevo se exige como crítico hasta que se clasifique (una prueba de la herramienta obliga a clasificarlo). Pasar un módulo a no crítico es una decisión de la persona responsable.

### 3.2 La puerta

La cobertura (RNF28, PLA-14) es una **puerta**: la verificación automática falla si un módulo baja de sus mínimos o si un servicio o un módulo crítico no tiene datos. Los totales por servicio son informativos: el resto del servicio no compensa a un módulo. Se mide con `coverlet.collector` y se verifica con `tools/CoverageCheck`, que une los informes de todas las pruebas (una línea cuenta si alguna ejecución la cubre), no cuenta migraciones ni código generado, compara sin redondear (84,99 % no es 85 %) y no permite cambiar los mínimos desde la línea de órdenes:

```bash
dotnet test BabelHub.sln --collect:"XPlat Code Coverage" --settings tests/coverlet.runsettings --results-directory TestResults
dotnet run --project tools/CoverageCheck -- TestResults
```

Aun así, la cobertura no es el objetivo: no se escriben pruebas vacías para subir el número, y los mínimos son un piso, no la meta a la que recortar. Opcionalmente, una pasada de pruebas de mutación sobre el Dominio (por ejemplo, con Stryker.NET) valida que las pruebas detecten errores reales.

### 3.3 Criterio para recortar pruebas

Antes de quitar o fusionar una prueba se contestan dos preguntas:

1. **¿Qué fallo detecta?** Se nombra el defecto concreto que la haría fallar. Si no hay ninguno (comprueba el marco o una constante contra sí misma), sobra.
2. **¿Otra prueba ya lo detecta?** Si la respuesta es sí, se nombra esa prueba y queda la más barata que lo detecte (unitaria antes que integración, integración antes que E2E). Si la respuesta es no, se queda, aunque el módulo siga por encima de su mínimo.

No se quita, en ningún módulo:

- la última prueba que nombra un criterio `AC-XXX-NN` (§2, regla 2);
- las pruebas de concurrencia real de stock, límites y descargas (§2, regla 7);
- un rechazo de seguridad (acceso fuera de alcance, límite, secreto, descarga ajena) que ninguna otra prueba detecte.

Fusionar casos repetidos es válido si la prueba resultante **informa todos los infractores**, sigue fallando ante un defecto sembrado (se comprueba) y no debilita ninguna aserción (§2, regla 6). Una comprobación estática sobre muchos archivos es **una** prueba, no un caso por archivo.

## 4. Qué se prueba por tipo de requisito

| Tipo | Cómo se prueba | Ejemplos |
| :--- | :--- | :--- |
| **Reglas del negocio** | Unitarias de Dominio | Vendible nunca negativo; ciclo del traslado; ajustes. |
| **Casos de uso** | Unitarias de Aplicación con puertos simulados | Alcance por sucursal rechaza y registra; la auditoría falla ⇒ la operación falla. |
| **Persistencia y concurrencia** | Integración | 100 reservas por 1 unidad; un cobro por pedido; el `CHECK` de stock. |
| **Seguridad** | Integración, E2E y CI | Límites con respuesta uniforme; cabeceras; ingreso con bloqueo; escaneos. |
| **Contratos** | Contrato | `problem+json` con `code`; compatibilidad `v1`. |
| **Interfaz** | E2E con axe | Recorrido de compra; accesibilidad; estados; tema oscuro. |
| **Rendimiento** | k6 y Lighthouse | p95 de disponibilidad; LCP, INP y CLS. |
| **Integraciones** | Integración con dobles verificados | Pasarela, WhatsApp, correo, almacén y LLM con respuestas simuladas, incluidas las inválidas. |
| **Abuso** | Casos de abuso | Reserva repetida, descarga ajena, instrucciones al asistente ([seguridad del asistente](../05-seguridad/06-seguridad-del-asistente.md)). |

## 5. Dobles de prueba para lo externo

| Externo | Doble | Fidelidad |
| :--- | :--- | :--- |
| Pasarela de pago (Culqi) | Simulador en proceso con respuestas aprobadas, rechazadas, dudosas y repetidas; modo de pruebas de Culqi para humo | Conserva idempotencia y errores. |
| Notificador (n8n y Evolution API) | Notificador simulado que registra avisos | NOT-11. |
| Correo | Servidor de correo de captura en contenedor | Verifica contenido sin enviar. |
| Almacén de archivos | Emulador S3 en contenedor | Firma y caducidad de enlaces. |
| Proveedor de LLM | Doble que devuelve respuestas fijas, incluidas respuestas hostiles | Pruebas de salida como texto y de límites. |
| Servicio de envío | Doble determinista | Estados de la guía. |

## 6. Orden de ejecución y puertas

| Puerta | Qué debe pasar |
| :--- | :--- |
| Antes de cada commit | `dotnet run --project tools/SpecCheck`; compilación sin advertencias; pruebas afectadas. |
| Integración continua | SpecCheck, compilación, todas las pruebas y arquitectura; análisis estático y escaneos cuando estén configurados. |
| Antes de pasar una spec a `Done` | `spec-verify` con resultado *Converge*; cobertura de criterios; revisión de arquitectura y de seguridad. |
| Antes de cada despliegue (cada versión que se despliega en producción) | Carga con k6, ZAP baseline, restauración de respaldo y lista manual de accesibilidad. |

## 7. Riesgos de la estrategia

| Riesgo | Mitigación |
| :--- | :--- |
| Pruebas de integración lentas por Docker | Contenedores compartidos por clase de pruebas y categorías para ejecutar solo las afectadas. |
| Dobles que divergen del servicio real | Pruebas de humo contra el modo de pruebas de Culqi y verificación de contratos. |
| Falsa seguridad por cobertura alta | Revisión de criterios, pruebas de mutación opcionales y casos de abuso explícitos. |
| Mantenimiento de E2E frágiles | Pocos recorridos largos, selectores estables por rol y etiqueta, y datos de ejemplo controlados. |

## Pruebas de los seis atributos de calidad

Cada [atributo de calidad](../01-analisis-de-sistema/04-atributos-de-calidad.md) tiene una prueba que lo verifica y una medida. Son la lista de pruebas de aceptación de calidad del proyecto:

| Atributo | Prueba y medida | Herramienta |
| :--- | :--- | :--- |
| **AC01 Consistencia del inventario** | 100 reservas simultáneas por 1 unidad: solo una prospera y el vendible nunca es negativo. | xUnit + Testcontainers |
| **AC02 Rendimiento y eficiencia** | p95 < 500 ms con 50 usuarios, checkout p95 < 3 s, LCP ≤ 2,5 s, memoria y CPU < 70 % por VM. | k6, Lighthouse, `docker stats` |
| **AC03 Disponibilidad y recuperabilidad** | Apagar el contenedor de Inventario: el catálogo sigue y el checkout informa; restaurar un respaldo con RPO ≤ 24 h y RTO ≤ 4 h. | Prueba de caos manual y guion de restauración |
| **AC04 Seguridad y privacidad** | 100 % de accesos fuera de alcance rechazados; 0 descargas ajenas; límites aplicados; 0 hallazgos críticos en ZAP, Trivy y CodeQL. | xUnit, ZAP baseline, Trivy, CodeQL |
| **AC05 Mantenibilidad y evolucionabilidad** | Reglas de dependencia y de módulos cumplidas; cada módulo en o sobre los mínimos de cobertura de su criticidad (§3); una sucursal nueva sin cambiar código. | `tests/architecture`, cobertura |
| **AC06 Usabilidad y accesibilidad** | Compra en ≤ 5 pasos, SUS ≥ 68, 0 incumplimientos AA críticos, recorrido exitoso en cuatro navegadores. | Playwright + axe, sesiones con personas |
