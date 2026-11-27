---
spec: web-acceso
estado: Draft
---

# Plan — Ayudas de contraseña, acceso del personal y código QR del segundo factor

> **Cómo se construye.** Parte de la spec **Approved**. Todo vive en el E-commerce: la interfaz web (Presentación), un puerto en la Aplicación del módulo Web y su adaptador en la Infraestructura. No cambia Identidad, la base de datos ni ningún contrato entre servicios.

## 1. Resumen técnico

- **Ayudas de contraseña (ACC-01 a ACC-09, ACC-17):** el servidor escribe el texto de ayuda, marca los campos con atributos `data-bh-…` y declara en **todos** los campos de contraseña `spellcheck="false"`, `autocapitalize="none"`, `autocorrect="off"` y `writingsuggestions="false"`; un guion propio, `wwwroot/js/contrasenas.js`, crea en el navegador la barra, el aviso de coincidencia y los botones de ver u ocultar. Sin guiones no aparece ninguno (D02). El cálculo del nivel es una función sin DOM del mismo archivo, que las pruebas ejecutan con Node (D03).
- **Acceso del personal (ACC-10 a ACC-12):** una variable de configuración, `BABELHUB__Web__DireccionDeGestion`, leída y validada al arrancar junto al contacto del comercio; el pie y la pantalla de ingreso la muestran solo en el host de la tienda (D04).
- **Código QR (ACC-13 a ACC-16):** puerto `IGeneradorDeCodigoQr` en la Aplicación del módulo Web; adaptador en la Infraestructura con la biblioteca `Net.Codecrete.QrCodeGenerator` (D01); la vista dibuja un SVG en la propia página, sin imagen externa ni `data:`.

## 2. Capas afectadas

| Capa | Piezas nuevas o modificadas |
| :--- | :--- |
| **Dominio** | Ninguna. |
| **Aplicación** | Web: `IGeneradorDeCodigoQr` y `CodigoQr`, una clase (no un `record`) con el lado en módulos y la consulta de cada módulo, sin margen, sin el texto codificado, sin miembros que imprimir y con `ToString` neutro. Identidad: `ReglasDeContrasena` en el contrato público (ya agregado con la corrección de las 10 letras). |
| **Infraestructura** | Web: `CodecreteGeneradorDeCodigoQr` (corrección de errores media; ante cualquier excepción devuelve nulo y registra solo un aviso de motivo fijo, sin texto, dirección ni excepción) y su registro en la composición del módulo Web; la versión del paquete en `Directory.Packages.props` y los archivos de bloqueo que cambian (`Ecommerce.Infrastructure`, `Ecommerce.Api`, `tests/architecture` y `tests/integration`). |
| **Api** | `Web/Acceso/DireccionDelAccesoDelPersonal` (objeto de valor con la fábrica que valida §5; admite `http` hacia la propia máquina solo con `IsDevelopment()` o `IsEnvironment("Test")`, el mismo indicador que el contacto de ejemplo, nunca `!esProduccion`); `OpcionesDelAnfitrionDelEcommerce.Leer` lee `Web:DireccionDeGestion` y acumula el problema con el nombre de la variable; `ModeloBaseDePagina.AccesoDelPersonal` (el modelo vive en `Api/Web`), que `ContextoDePagina` llena solo con el host de la tienda; `ModeloBaseDePagina` de las pantallas de configuración lleva el `CodigoQr` que `IngresoController` y `CuentaController` piden al puerto con la misma dirección de configuración que el enlace (la respuesta ya es `no-store`). |
| **Web** | Parciales `_AyudaDeContrasenaNueva` (toma el mínimo y el máximo de `ReglasDeContrasena` y los escribe en atributos `data-bh-…` que lee el guion) y `_CodigoQr` (construye el trazo del SVG con los módulos, margen de 4); `Registro`, `Cuenta` e `Ingreso` declaran los atributos de ACC-17 y marcan sus campos; la ayuda, la barra, el aviso y el botón quedan fuera del `<label>` del campo; `_Pie` e `Ingreso` muestran «Acceso del personal»; `_Layout` carga `contrasenas.js` con `defer`; en `Estilos/app.css`, dentro de los **dos bloques de tema**, los tokens `--bh-nivel-debil`, `--bh-nivel-aceptable`, `--bh-nivel-buena` y `--bh-nivel-fuerte` (rojo, ámbar, verde intermedio y verde, con contraste de al menos 3 a 1 sobre el fondo de cada tema) y `--bh-qr-fondo` y `--bh-qr-tinta` (iguales en ambos temas). El SVG se pinta con clases, sin atributo `style`. |
| **Shared** | Ninguna. |
| **Operación** | `scripts/generar-secretos.sh` escribe la variable en `ventas/ecommerce.env`; comentario en `infra/secretos-requeridos.txt` (es opcional: no es una fila exigida, para no rechazar las carpetas ya generadas); los Compose no cambian (el archivo de entorno la entrega); la guía de primer despliegue dice cómo agregarla a mano. |

## 3. Modelo de datos

Sin cambios.

## 4. Contratos

Sin operaciones nuevas ni cambios en las existentes. Los formularios y sus campos no cambian (el esquema estricto de cada `POST` sigue igual: los botones creados por el guion son `type="button"` y sin `name`).

| Puerto | Operación | Entrada | Salida | Errores |
| :--- | :--- | :--- | :--- | :--- |
| `IGeneradorDeCodigoQr` | `Generar` | el texto a codificar (`ValorSecreto`) | `CodigoQr` o nulo | nunca lanza ni registra el texto |

## 5. Concurrencia, transacciones e idempotencia

No aplica: nada escribe ni comparte estado.

## 6. Seguridad y alcance

- **Contraseña (AM17; CTL-15 y CTL-19 sin cambios):** el guion no hace solicitudes, no usa almacenamiento, cookies, historial ni consola y no copia la contraseña a atributos ni textos del documento; solo cambia el `type` del campo, que vuelve a `password` al enviar el formulario y al restaurar la página del historial (`pageshow`). Los atributos de ACC-17 los escribe el servidor, así que rigen también antes de que cargue el guion.
- **Política de contenido (AM06; CTL-05, CTL-07; WEB-18):** sin cambios. El guion es un archivo propio (`script-src 'self'`), sin `eval`, sin manejadores en línea, sin atributos `style` ni hojas inyectadas: los estados se expresan con clases y atributos definidos en la hoja de estilos. El QR es un SVG en el HTML (sin `img`, sin `data:`).
- **Secreto del segundo factor (AM17; CTL-32, SEG-10):** el código lo contiene; viaja solo en la respuesta que ya muestra la clave (`Cache-Control: no-store`), no se guarda, no se pone en la sesión ni en un borrador y no se registra. Sigue guardado cifrado como hoy (IDE-40); nada nuevo se persiste.
- **Enlace del personal (AM02, AM05, AM46):** dirección de configuración, nunca de la solicitud (sin redirección abierta ni envenenamiento por `Host`); validada y escrita codificada por la vista. Igual para todos: no depende de la cuenta ni de lo escrito (sin enumeración). Controles del destino sin cambios: Cloudflare Access (en el plan gratuito, con código por correo, guía 08 §12.4), Turnstile, límites de ingreso (SEG-06) y segundo factor obligatorio. Se registra la amenaza nueva en `docs/05-seguridad/01-modelo-de-amenazas.md` (DD-121) y la variable en `docs/05-seguridad/07-operacion-segura.md` §3.2.
- **Dependencia (AM16; CTL-34):** versión fija con archivo de bloqueo y revisión de las pruebas de cadena de suministro (D01).
- **Límites, auditoría, antifalsificación y verificación de persona:** sin cambios.

## 7. Pantallas

Registro y recuperación (`Areas/Tienda/Views/Registro`), cuenta (`Views/Cuenta`), ingreso con sus pasos (`Views/Ingreso`) y el pie (`Views/Shared/_Pie`). Siguen el estilo de las pantallas actuales y `docs/06-experiencia-de-usuario`; el prototipo `prototype/ui-v2/` no define la barra ni el código QR. Estados: sin guiones, vacío, escribiendo, coincide o no coincide, contenido visible u oculto, y QR no disponible. El texto de la configuración del segundo factor invita a escanear el código o a escribir la clave.

## 8. Estrategia de pruebas

| Criterio | Tipo de prueba | Dónde |
| :--- | :--- | :--- |
| AC-ACC-01, AC-ACC-16 | Integración de las vistas (todas las pantallas, sin guiones) | `tests/integration/.../Web/AyudasDeContrasenaTests.cs` |
| AC-ACC-02, 03, 05, 06, 07 | El guion real ejecutado con Node y un doble mínimo del documento (patrón de `FlujoDeN8nEjecutadoTests`), más la integración del envío al servidor | `tests/integration/.../Web/GuionDeContrasenasTests.cs` y `Web/js/` |
| AC-ACC-04 | La tabla de ejemplos de la spec, leída del propio archivo de la spec, contra la función del nivel, con Node; y que el mínimo del guion es el de `ReglasDeContrasena` | ídem |
| AC-ACC-03, 05, 07, 08, 14, 16 | E2E de Playwright con axe (críticos y serios), teclado, ambos temas, movimiento reducido, 320 px y la red vigilada | `tests/e2e/acceso/ayudas-de-acceso.spec.ts` |
| AC-ACC-08 | Integración de la hoja de estilos (tokens en ambos temas, contraste, movimiento reducido) | `Web/AyudasDeContrasenaTests.cs` |
| AC-ACC-09, 10, 11 | Integración del arranque (con un entorno ajeno a desarrollo y prueba que rechaza `http://localhost`), las vistas y el generador de secretos | `Web/AccesoDelPersonalTests.cs`, `Despliegue/DesplegarShTests.cs` |
| AC-ACC-12, 14 | Se dibuja a píxeles el SVG de la respuesta (rejilla propia a partir de su trazo y su lienzo) y lo **lee un lector independiente**, `ZXing.Net` (solo en pruebas, D05); el texto leído debe ser el `href` del enlace | `Web/CodigoQrDelSegundoFactorTests.cs` |
| AC-ACC-13, 15 | Integración con cabeceras, registros capturados, sin borradores ni recursos externos, y un generador que falla (adaptador y controlador) | ídem |

Las pruebas que ejecutan Node se omiten si Node no está instalado (como las del flujo de avisos), **salvo en la integración continua** (variable `CI`), donde su ausencia las hace fallar; la verificación de la spec exige que se hayan ejecutado. La lectura con la cámara de un teléfono es además una comprobación manual de la persona al configurar su segundo factor.

## 9. Riesgos

| Riesgo | Mitigación |
| :--- | :--- |
| La barra da una falsa sensación de seguridad (por ejemplo con el propio nombre o una contraseña de la lista). | El texto de que es una guía (ACC-03) y la ayuda que anuncia la comprobación al guardar; sin la palabra «segura»; el servidor decide. |
| Un gestor de contraseñas o una extensión no dispara eventos. | Se escuchan `input` y `change`; la barra es una ayuda y el envío no depende de ella. |
| Un navegador ignora alguno de los atributos de ACC-17. | Se declaran todos los que cada familia reconoce; el contenido está oculto salvo que la persona pida verlo. |
| La dirección mal escrita impide arrancar la tienda al actualizar. | Solo si se agrega y está mal formada; la guía muestra el valor exacto y `desplegar.sh` espera la salud, así que el fallo se ve al desplegar y se corrige quitando o arreglando la línea. |
| La biblioteca del QR deja de mantenerse. | Puerto propio: cambiarla es reemplazar el adaptador. Versión fijada y revisada por las pruebas de cadena de suministro. |

## 10. Verificación contra AGENTS.md

- [ ] Dominio sin dependencias externas.
- [ ] Puertos definidos en Aplicación; adaptadores solo en Infraestructura (la biblioteca del QR solo la referencia la Infraestructura; el lector, solo las pruebas).
- [ ] Sin referencias entre servicios; módulos solo por contrato público (regla 13); la interfaz web solo llama a contratos de módulos.
- [ ] Sin reglas de negocio en la interfaz: la política de contraseñas sigue solo en Identidad; la barra es orientación visual.
- [ ] Alcance por sucursal: no aplica.
- [ ] Sin secretos ni datos personales en el repositorio, los registros ni la caché.
- [ ] Interfaz según `docs/06` (tokens, accesibilidad, estados).
