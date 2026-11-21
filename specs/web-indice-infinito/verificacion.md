---
spec: web-indice-infinito
fecha: 2026-11-21
---

# Verificación — El índice infinito (experimento opcional)

## Resultado: Converge

## 1. Comprobaciones automáticas

| Comprobación | Comando | Resultado |
| :--- | :--- | :--- |
| Compilación sin advertencias | `dotnet build BabelHub.sln -warnaserror` | Correcta (0 errores) |
| Pruebas unitarias del E-commerce | `dotnet test tests/unit/BabelHub.Ecommerce.UnitTests` | 5537 correctas (las del índice: `DireccionDelIndiceTests`, `GeneradorDePaginasDelIndiceTests`, `DireccionAlAzarTests`, `ConsultarIndiceInfinitoTests`) |
| Prueba de arquitectura | `dotnet test tests/architecture/BabelHub.ArchitectureTests` | 252 correctas (8 de `ReglasDelIndiceInfinitoTests`) |
| Integración del índice (en memoria, sin Docker) | `dotnet test tests/integration/BabelHub.IntegrationTests --filter IndiceInfinito` | 72 correctas |
| E2E del índice (host real con PostgreSQL y datos sembrados; Chromium de escritorio, de teléfono y WebKit) | `npx playwright test indice` | 17 correctas en cada proyecto; la prueba del 429 sobre el host real, añadida después, corrió y pasó en escritorio y WebKit (sin repetir la corrida completa de teléfono) |
| Specs, planes, tareas y trazabilidad | `dotnet run --project tools/SpecCheck` | OK: 20 specs, 0 avisos |
| Búsqueda de secretos | `scripts/escanear-secretos.sh repositorio` | Sin fugas |
| Revisión de arquitectura y de seguridad (aisladas) | agentes `arch-reviewer` y `security-reviewer` | Sin Bloqueantes; 1 Importante y 14 Menores en total, todos tratados (sección 3) |

## 2. Requisito por requisito

Abreviaturas: `Dom/` = `Domain/Web/Indice/`, `App/` = `Application/Web/` (`Indice/` el caso de uso), `Api/` = `Api/Areas/Tienda/`, `UT/` = `tests/unit/.../Web/`, `IT/` = `tests/integration/.../Web/`, `E2E/` = `tests/e2e/indice/indice-infinito.spec.ts`. «Cumple» exige una prueba que falle si se rompe la regla (se comprobó por mutación manual que fallan quitar `[SinRetorno]` y añadir una dependencia prohibida al caso de uso).

| Requisito | Evidencia en código | Criterio y prueba | Estado |
| :--- | :--- | :--- | :--- |
| IND-01 página generada a partir de la dirección | `Dom/GeneradorDePaginasDelIndice.cs`, `App/Indice/ConsultarIndiceInfinito.cs`, `Api/Controllers/IndiceInfinitoController.cs` (`Abrir`), `Api/Views/IndiceInfinito/Pagina.cshtml` | AC-IND-01: `UT/GeneradorDePaginasDelIndiceTests` (vectores publicados de FNV-1a y SplitMix64, forma de 20 × 70), `IT/IndiceInfinitoPantallaTests` | Cumple |
| IND-02 siempre la misma página | `Dom/GeneradorDePaginasDelIndice.cs` (función pura sin cultura, reloj ni estado) | AC-IND-01: patrón de oro calculado con otra implementación (Node.js, SHA-256 de la página), cultura `tr-TR` frente a `es-PE`, 64 llamadas en paralelo; `IT/…Pantalla` (dos `GET` iguales); `E2E/` («la misma dirección da el mismo texto» y otra persona sin cookies) | Cumple |
| IND-03 otra página al azar | `Dom/DireccionAlAzar.cs`, `App/Indice/ConsultarIndiceInfinito.cs` (`ElegirAlAzar`: muestreo por rechazo, 8 reintentos y avance determinista), `Api/…Controller.cs` (`OtraAlAzar`) | AC-IND-02: `UT/DireccionAlAzarTests`, `UT/ConsultarIndiceInfinitoTests` (extremos sin sesgo, repetición, generador roto), `IT/…Pantalla`, `E2E/` («cambia la dirección y el texto») | Cumple |
| IND-04 enlace con la dirección | La dirección va en la ruta `/indice-infinito/{**direccion}`; `Pagina.cshtml` (`data-ruta`) y `wwwroot/js/indice-infinito.js` | AC-IND-03: `IT/…Pantalla` (otro cliente sin cookies abre el mismo enlace), `E2E/` («Copiar enlace» copia la URL absoluta y avisa; sin JavaScript el campo de solo lectura) | Cumple |
| IND-05 aviso de texto generado | `Pagina.cshtml` (alerta «Texto generado», en toda página, también la amable) | AC-IND-04: `IT/…Pantalla` (cuatro estados), `E2E/` | Cumple |
| IND-06 sin almacenar ni consultar base de datos | `ConsultarIndiceInfinito` solo recibe `IAleatoriosSeguros`; composición sin repositorio ni caché (`Infrastructure/Web/ListaDeSucursalesConUltimaConocida.cs`) | AC-IND-05: `UT/…Caso` (constructor y cien páginas), `IT/IndiceInfinitoComposicionTests` (dobles que fallan ante cualquier uso de la unidad de trabajo o la conexión), `IT/IndiceInfinitoSinBaseYConLimitesTests` (cincuenta solicitudes HTTP, cero usos), `tests/architecture/ReglasDelIndiceInfinitoTests` (sin persistencia, `Plataforma` ni otros módulos, Dominio y Aplicación) | Cumple (ver Menor 4) |
| IND-07 dirección no válida | `Dom/DireccionDelIndice.cs` (lista permitida de 1 a 64 caracteres), controlador (200 amable con una página al azar, sin eco) | AC-IND-06: `UT/DireccionDelIndiceTests`, `IT/IndiceInfinitoSinEcoTests` (24 entradas con centinela: 65 y 2 KB, mayúsculas, tildes, `<script>`, `%0d%0a`, `%2f`, repetidos, `%0a`, sin eco en cuerpo, cabeceras ni registros `BabelHub.*`), `E2E/` | Cumple |
| IND-08 enlace al catálogo real, separado del carrito y la búsqueda | `Pagina.cshtml` (enlace `/catalogo`; sin datos del catálogo, el carrito ni la búsqueda en `<main>`) | AC-IND-04: `IT/…Pantalla`, `E2E/` («no usa datos del catálogo, del carrito ni de la búsqueda»), arquitectura (ningún otro módulo depende del índice y el índice de ninguno) | Cumple |
| IND-09 límite de solicitudes | Las rutas pasan por el pipeline de SEG-18 sin exenciones (`Api/Plataforma/AnfitrionDelEcommerce.cs`) | AC-IND-07: `IT/IndiceInfinitoSinBaseYConLimitesTests` (solicitud 61 en las tres rutas, por origen y no por ruta, «otra» cuesta dos solicitudes), `IT/IndiceInfinitoPipelineRealTests` (orden del pipeline real y ninguna ruta exenta), `E2E/` (429 sobre el host real con las cabeceras del índice) | Cumple |

**Casos límite de la sección 6.** Dirección de más de 64 caracteres: no válida sin generar texto (`UT`, `IT/…SinEco`); dirección vacía: una al azar (`UT`, `IT/…Pantalla`: `/indice-infinito`, `/indice-infinito/`, `?direccion=` vacío y de solo espacios). **Reglas de la sección 5.** El texto se calcula y no se guarda; nada de lo generado aparece en el catálogo, el buscador, el carrito ni los avisos (arquitectura y `E2E/`); accesible y con movimiento reducido (`E2E/`: axe sin A ni AA en los dos temas, teclado y foco, `prefers-reduced-motion`, 320, 375 y 768 px).

**Cabeceras y navegador (D06).** `X-Robots-Tag: noindex, nofollow` y `Cache-Control: no-store` en 200, 200 amable, 302, 429 y un error forzado (`IT/…Cabeceras`, `IT/…Limites`, `E2E/`), `Location` con la forma exacta `^/indice-infinito/[a-z0-9-]{1,64}\z`, `robots.txt` con el `Disallow`, vistas y script sin `Html.Raw`, `style=`, scripts en línea ni manejadores `on*=` (prueba estática), sin violaciones de la CSP en la consola, política de textos pendientes sin guardar la ruta como retorno (`IT/IndiceInfinitoConTextosPendientesTests`).

**`ui-review` (2026-11-21).** Capturas del host compuesto a 1280 y 375 px en los dos temas, con la página y con el mensaje amable, contra `prototype/ui-v2/tienda/indice-infinito.html`: la estructura es la del prototipo (rótulo «Experimento literario», título, aviso, dirección con «Otra página al azar» y «Copiar enlace», página en bloque de texto, formulario «Ir a una dirección» y enlace al catálogo). Corregido en la revisión: la tilde del título pisaba el rótulo (margen del `h1`), la dirección se encogía a un punto a 375 px (la fila pasa a ancho completo) y el CSS compilado estaba desactualizado (`scripts/construir-estilos.sh`).

## 3. Diferencias encontradas

Todas las del plan se cumplen; las desviaciones de implementación están en `decisiones-de-implementacion.md`. Hallazgos de las revisiones aisladas y su tratamiento:

| Hallazgo | Tratamiento |
| :--- | :--- |
| **Importante (arquitectura):** ninguna prueba cubría el orden del pipeline real (`UsarEcommerce`) para el índice. | **Corregido:** `IndiceInfinitoPipelineRealTests` lee `UsarEcommerce` y exige el orden cabeceras de seguridad → cabeceras del índice → errores → estáticos → tope previo → límites → textos pendientes, y que ninguna ruta exenta cubra el índice; además el E2E comprueba el 429 con las cabeceras sobre el host real. |
| Menor (ambas): las expresiones de forma usaban `$` y aceptaban un `\n` final. | **Corregido** con `\z` en el Dominio, el controlador y la prueba, con casos `\n` y `\r\n` en `DireccionAlAzarTests`. |
| Menor (arquitectura): el comentario de la composición decía que `IAleatoriosSeguros` no estaba registrado. | **Corregido** (ya lo registra `AgregarSesionWeb`; `TryAdd` evita duplicar). |
| Menor (arquitectura): el aviso de la vista y el del catálogo de mensajes diferían. | **Corregido:** `docs/06/08` usa el texto de la vista. |
| Menor (arquitectura): huecos de la prueba de arquitectura (lista fija de módulos, Dominio de otros módulos, sin `Trait`). | **Corregido:** los módulos se derivan del ensamblado, la Aplicación del índice tampoco puede depender del Dominio de otro módulo y cada prueba lleva `Trait("Criterio", …)`. |
| Menor (arquitectura): el controlador pasa el valor `!` al contrato para un formulario repetido. | **Mitigado:** `DireccionDelIndiceTests` fija que `!` nunca es una dirección válida (el acoplamiento queda probado). |
| Menor (arquitectura): la unitaria de AC-IND-05 solo comprueba el constructor. | **Aceptado con constancia:** el conteo con dobles de la unidad de trabajo y la conexión vive en `IndiceInfinitoComposicionTests` y `IndiceInfinitoSinBaseYConLimitesTests`; la prueba de arquitectura prohíbe a la Aplicación del índice depender de persistencia o `Plataforma`. |
| Menor (arquitectura): CSS compilado desactualizado. | **Corregido** (se reconstruye con `scripts/construir-estilos.sh`; `wwwroot/css` no se versiona). |
| Menor (seguridad): el generador no decía que no es criptográfico. | **Corregido** en su documentación XML. |
| Menor (seguridad): el modelo de amenazas no cubría el espacio infinito de páginas. | **Corregido:** AM41 en `docs/05-seguridad/01-modelo-de-amenazas.md`. |
| Menor (seguridad): el registro del manejador de errores guarda la ruta cruda (CTL-35); `MaxRequestLineSize` (R06); `{**direccion}` no pasa la restricción de plantillas de la auditoría de denegaciones. | **Heredados, dueño carril A / Plataforma** (ya en `progress/hallazgos.md`); anotados de nuevo con el escenario del índice. |
| Menor (E2E, hallazgo propio): en WebKit toda página de la tienda informa dos veces «Refused to apply a stylesheet» en la consola. | **Registrado** en `progress/hallazgos.md` (de la plataforma, no del índice); la prueba de consola excluye solo ese mensaje. |

Menores abiertos que no impiden converger:

1. Los registros del servidor (`Microsoft.*`) y el de `ManejoDeErrores` pueden guardar la ruta cruda de un error inesperado bajo `/indice-infinito/` (carril A).
2. Una URL mayor que el límite de Kestrel, `%00` y UTF-8 inválido reciben la respuesta propia de Kestrel (R06; sin eco).
3. `Disallow` más `noindex` se solapan (decisión aceptada en D06).
4. La prueba de «cero conexiones» corre en el host de pruebas (MVC real y límites reales, sin base) y no sobre el host real con PostgreSQL: el índice no tiene ninguna dependencia de persistencia por construcción y lo impone la prueba de arquitectura.

## 4. Conclusión

El índice infinito converge: los 9 requisitos y los 7 criterios de aceptación tienen código y pruebas que fallan si se rompen, las 15 tareas están hechas, el E2E y el `ui-review` se corrieron sobre el host real, y las revisiones de arquitectura y seguridad no dejan Bloqueantes ni Importantes abiertos. Quedan solo los Menores de la sección 3.
