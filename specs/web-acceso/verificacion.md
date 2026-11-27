---
spec: web-acceso
fecha: 2026-11-27
---

# Verificación — Ayudas de contraseña, acceso del personal y código QR del segundo factor

## Resultado: No converge

## 1. Comprobaciones automáticas

| Comprobación | Comando | Resultado |
| :--- | :--- | :--- |
| Compilación sin advertencias | `dotnet build BabelHub.sln -warnaserror` | 0 advertencias y 0 errores, con `main` (`v0.1.2`) ya fusionada. |
| Pruebas | `dotnet test BabelHub.sln` | Unitarias 5770 y 2454, cobertura 15 y `SpecCheck` 23: todas correctas. Integración: 8805 correctas, 4 omitidas (cobros reales con Culqi) y **2 con error** ajenas a esta spec (`InstanciaDeContadoresTests.TodaClaveDeLosContadores_ExpiraAntesDeLas24Horas` y `RegistroDeSupresionesEnObjetosTests.LeerConMetadatos_RecorreMasDeUnaPaginaDeResultados`, almacén de prueba «unavailable»); sus dos clases, repetidas aisladas, dan 22 de 22. |
| Prueba de arquitectura | `dotnet test tests/architecture` (incluida en la suite) | 261 de 261. |
| Specs, planes, tareas y trazabilidad | `dotnet run --project tools/SpecCheck` | 23 specs, 0 errores y 0 avisos. |
| Búsqueda de secretos | `git grep -nIiE "password\s*=\|api[_-]?key\s*=\|secret\s*=" -- src` y `scripts/escanear-secretos.sh repositorio` | Sin secretos: las dos coincidencias son la plantilla de la dirección `otpauth` (sin valor) y una asignación anterior a este cambio; el escáner no halla fugas. |
| Archivos del cambio | `git diff --stat` | Sin `.env`, archivos digitales ni datos personales; los contactos y dominios de las pruebas son ficticios. |
| Navegador | Playwright, `tests/e2e/acceso/ayudas-de-acceso.spec.ts`, y navegador integrado | 24 de 24 en Chromium de escritorio y teléfono y en WebKit, con axe sin incumplimientos críticos ni serios; revisión visual en claro y oscuro a 1100, 375 y 320 px. Contra un arnés temporal con vistas, guion, estilos y política de contenido reales y dobles de Identidad, **no** contra el entorno completo. |

## 2. Requisito por requisito

Rutas: `Api` = `src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api`; `Web` = `tests/integration/BabelHub.IntegrationTests/Web`.

| Requisito | Evidencia en código | Criterio y prueba | Estado |
| :--- | :--- | :--- | :--- |
| ACC-01 | `Api/Views/Shared/_CampoDeContrasena.cshtml` (bloque `data-bh-ayuda`) | AC-ACC-01 · `Web/AyudasDeContrasenaTests.cs`, `Web/PantallasDeLaCuentaTests.cs`, E2E | Cumple |
| ACC-02 | `Api/wwwroot/js/contrasenas.js` (ningún manejador detiene el envío); sin `minlength` | AC-ACC-02 · `Web/GuionDeContrasenasTests.cs` | Cumple |
| ACC-03 | `contrasenas.js` `medidor`; `app.css` `.bh-medidor` | AC-ACC-03 · `Web/GuionDeContrasenasTests.cs`, E2E | Cumple |
| ACC-04 | `contrasenas.js` `evaluar` y `longitudUtil` | AC-ACC-04 · tabla de la spec leída por `Web/GuionDeContrasenasTests.cs` | Cumple |
| ACC-05 | `contrasenas.js` (sin red, almacenamiento ni consola) | AC-ACC-05 · `Web/GuionDeContrasenasTests.cs`, E2E con la red vigilada | Cumple |
| ACC-06 | `contrasenas.js` `coincidencia` | AC-ACC-06 · `Web/GuionDeContrasenasTests.cs`, E2E | Cumple |
| ACC-07 | `contrasenas.js` `botonDeVer`, `ponerVisible`, `ocultarAlEnviar`, `pagehide` y `pageshow` | AC-ACC-07 · `Web/GuionDeContrasenasTests.cs`, E2E con teclado | Cumple |
| ACC-08 | Barra, avisos y botones solo los crea el guion | AC-ACC-01 · `Web/AyudasDeContrasenaTests.cs`, E2E sin guiones | Cumple |
| ACC-09 | `contrasenas.js` `anunciador`; tokens `--bh-nivel-*`; transición solo sin reducir movimiento | AC-ACC-08 · `Web/AyudasDeContrasenaTests.cs`, `Web/GuionDeContrasenasTests.cs`, E2E con axe | Cumple |
| ACC-10 | `Api/Web/ContextoDePagina.cs`, `Views/Shared/_Pie.cshtml`, `Views/Ingreso/Ingreso.cshtml` | AC-ACC-09 · `Web/AccesoDelPersonalTests.cs`, `Web/PantallasDeLaCuentaTests.cs`, `Despliegue/DesplegarShTests.cs` | Cumple |
| ACC-11 | `Api/Web/Acceso/DireccionDelAccesoDelPersonal.cs` `Leer` | AC-ACC-10 · `Web/AccesoDelPersonalTests.cs` | Cumple |
| ACC-12 | ídem, `Valida`; `Api/Plataforma/AnfitrionDelEcommerce.cs` | AC-ACC-11 · `Web/AccesoDelPersonalTests.cs` | Cumple |
| ACC-13 | `Api/Controllers/IngresoController.cs` y `CuentaController.cs`; `Views/Shared/_CodigoQr.cshtml` | AC-ACC-12 · `Web/CodigoQrDelSegundoFactorTests.cs` (leído con `ZXing.Net`), `Web/PantallasDeLaCuentaTests.cs` | Cumple |
| ACC-14 | `Infrastructure/Web/CodecreteGeneradorDeCodigoQr.cs`; SVG en la página; `no-store` | AC-ACC-13 · `Web/CodigoQrDelSegundoFactorTests.cs`, `Web/PantallasDeLaCuentaTests.cs` | Cumple |
| ACC-15 | `_CodigoQr.cshtml`; `app.css` `.bh-qr` | AC-ACC-14 · `Web/AyudasDeContrasenaTests.cs`, E2E a 320 px en ambos temas y sin guiones | Cumple |
| ACC-16 | El adaptador devuelve nulo con un aviso fijo; las vistas dibujan el código solo si existe | AC-ACC-15 · `Web/CodigoQrDelSegundoFactorTests.cs` | Cumple |
| ACC-17 | `_CampoDeContrasena.cshtml`, el único que escribe un campo de contraseña | AC-ACC-16 · `Web/AyudasDeContrasenaTests.cs`, E2E | Cumple |

Casos límite de §6: emojis y puntos de código, texto de más de 128 caracteres, relleno por gestor (`change`), pantalla repintada (los campos no llevan `value`), configuración con Identidad no disponible. La captura o foto del código es un riesgo registrado (AM48), sin prueba.

## 3. Diferencias encontradas

1. «El código y la clave se muestran una sola vez» (§5 y §6) no es literal en el ingreso: Identidad vuelve a entregar el secreto sin confirmar mientras viva el desafío. Comportamiento anterior a esta spec; anotado en `progress/hallazgos.md` y corregido en el modelo de amenazas.
2. El plan nombraba un parcial `_AyudaDeContrasenaNueva`; la ayuda va dentro de `_CampoDeContrasena` (`decisiones-de-implementacion.md`). Sin efecto en el comportamiento.
3. La lectura del código con la cámara de un teléfono es una comprobación manual pendiente de la persona (plan §8).

## 4. Conclusión

Los 17 requisitos cumplen y sus criterios tienen pruebas correctas, pero **no converge** todavía y la spec queda `InProgress`: (1) el texto «se muestra una sola vez» necesita reabrir la spec (diferencia 1); (2) falta una corrida completa limpia, sin las dos pruebas intermitentes ajenas; (3) falta correr los recorridos E2E contra el entorno completo y leer el código con un teléfono.
