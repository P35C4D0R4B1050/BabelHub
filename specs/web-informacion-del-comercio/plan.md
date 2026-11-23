---
spec: web-informacion-del-comercio
estado: Draft
---

# Plan — Información del comercio, contacto y política de cambios y devoluciones

> **Cómo se construye.** Parte de la spec **Approved** (2026-11-23, tras la ratificación de DD-75 a DD-79). No agrega comportamiento: si hace falta, se vuelve a la spec.

## 1. Resumen técnico

Todo vive en la **interfaz web** del E-commerce (Presentación) y en el módulo `Web` ya existente; no hay módulo nuevo, base de datos nueva ni servicio nuevo.

- **Contacto (COM-01 a COM-05).** `OpcionesDeContacto` (en `Api/Web/Contacto`) lee `BABELHUB__Contacto__Telefono`, `…Correo` y `…Direccion` y las valida con las reglas de la spec §5: ASCII en teléfono y correo (la dirección admite tildes y ñ); teléfono de 7 a 15 dígitos `0-9`, hasta 30 caracteres, solo espacios U+0020, guiones, paréntesis y `+` inicial; correo `local@dominio.sufijo` de hasta 254; dirección de 10 a 200 sin caracteres de control, de formato (categorías Unicode `Cc` y `Cf`), separadores de línea o de párrafo (`Zl` y `Zp`) ni `<>"'&` ni tilde grave. **Reglas de implementación:** expresiones regulares con `\z` (nunca `$`), sin `RegexOptions.IgnoreCase`, con `[A-Za-z]` y `[0-9]` (nunca `\d`, `char.IsDigit` ni `MailAddress`); los errores salen solo por `lector.Problema` (nunca `Capturar`, que copia el mensaje de una excepción de framework que podría contener el valor); `DatosDeContacto` solo se construye por la fábrica que valida (constructor `internal`). Se invoca desde `OpcionesDelAnfitrionDelEcommerce.Leer` (mismo mecanismo que `Carrito:CostoDeEnvio`): junta los problemas, nombra la variable con `LectorDeSecretos.Variable` y nunca el valor. `Leer(configuracion, esProduccion, permitirContactoDeEjemplo, soloEnModoNormal = true)` recibe el parámetro **obligatorio** antes del opcional (sin valor por omisión; las llamadas existentes, todas con nombre, pasan el valor explícito, y `Restauracion` y `Ensayo` exigen los datos igual). `AgregarEcommerce` lo calcula como `IsDevelopment() || IsEnvironment("Test")`; con él, un dato ausente, vacío o con solo espacios se sustituye por el ficticio; un dato presente e inválido falla en todo entorno. Las opciones validadas producen un `DatosDeContacto` inmutable (`Telefono`, `EnlaceDeLlamada`, `Correo`, `EnlaceDeCorreo`, `Direccion`, `EsDeEjemplo`): `tel:` + dígitos con `+` inicial si se configuró y `+51` si son 9 dígitos que empiezan con 9 sin `+`; `mailto:` + el correo validado. Se registra como singleton.
- **Pie y página (COM-01, COM-06 a COM-09).** `ContextoDePagina` recibe `DatosDeContacto? = null` (como `IMenuDeNavegacion?`) y `Completar` lo copia a `ModeloBaseDePagina.Contacto`; `_Pie.cshtml` pinta los datos, el enlace `/contacto`, la frase del comercio y el enlace «Cambios y devoluciones». `ContactoController` (en `Api/Controllers`, existe en los dos hosts; `GET /contacto`, sin `[PermitidaConPasoPendiente]` ni `[PermitidaConTextosPendientes]`, DD-79) depende solo de `ContextoDePagina`; si `Contacto` fuera nulo (host de pruebas sin el servicio) muestra el estado de servicio no disponible y no un 500. El pie **omite el bloque de contacto sin lanzar** cuando `Contacto` es nulo (lo que ocurre en los hosts de prueba que construyen `ContextoDePagina` a mano: `CarritoApiHost`, `DevolucionesApiHost` y `LogisticaApiHost`), y una prueba lo comprueba; el host de pruebas `AnfitrionWeb` registra `DatosDeContacto` de ejemplo; una prueba de composición comprueba que `AgregarEcommerce` lo registra, de modo que en producción nunca es nulo.
- **Cambios y devoluciones (COM-10, COM-11).** **La clave estable no cambia**: `TiposDeTextoLegal.Clave(Reembolso)` sigue siendo `reembolso` (la usan el identificador del conjunto de textos que compara Identidad, `terminos:V|privacidad:V|reembolso:V`, el recurso embebido `reembolso.txt` y las pruebas de aceptación). Se agrega `TiposDeTextoLegal.Ruta(tipo)` (`terminos`, `privacidad`, `cambios-y-devoluciones`) para el segmento de la dirección; `TryParse` sigue aceptando `reembolso` (sin cambiar su prueba unitaria) y entiende además `cambios-y-devoluciones`; `Aceptar.cshtml`, el pie y el checkout enlazan con `Ruta`. `LegalController.Ver` compara el segmento con `reembolso` (ordinal) **antes** de `TryParse` y de pedir el texto a `ITextosLegales`, y responde `LocalRedirectPermanent("/legal/cambios-y-devoluciones")` (destino constante, sin copiar la consulta). La política de textos pendientes ya consulta a Identidad antes de la acción: la redirección se atiende también con pendientes, con paso pendiente y con Identidad caída, porque `/legal/{texto}` es de la capacidad `TextosLegales`; la acción ya lleva `[PermitidaConTextosPendientes(TextosLegales)]` y `[PermitidaConPasoPendiente]`, solo `GET`. Cambian el título del cuerpo y los textos de Presentación que citan «política de reembolso» (`Api/Carrito/PantallasDelCarrito.cs`, `Api/Pedidos/PantallasDePedidos.cs`, la etiqueta de `CheckoutController` y `Aceptar.cshtml`); el tipo, el identificador y la versión de Identidad no cambian (D01).
- **Contenido (COM-09, COM-12 a COM-14, COM-17).** Se reescriben los tres cuerpos con los datos del comercio por referencia (el pie y `/contacto`, nunca un valor), la ley peruana, el Indecopi, las reglas de Devoluciones y los derechos sobre datos personales desde la cuenta. Siguen marcados `PROVISIONAL` (WEB-29).
- **Versión (COM-15).** Un archivo de datos junto a la prueba (`tests/integration/.../Web/versiones-de-referencia-de-textos.json`) guarda por texto la versión de referencia `AAAA-MM-DD-<8 hex de la huella>`; la prueba calcula el SHA-256 de los bytes UTF-8 del recurso embebido, tal cual y sin normalizar (`.gitattributes` fija `eol=lf`), y verifica que el prefijo de la versión coincide: cambiar el cuerpo obliga a cambiar la versión. `infra/README.md` cita ese archivo (no repite los valores) y da el paso de despliegue de la sección 6. La segunda mitad de AC-COM-13 (reaceptación) la cubren las pruebas existentes de Identidad, de la aceptación y de CAR-22 del pago, con `[Trait("Criterio", "AC-COM-13")]` agregado.

## 2. Capas afectadas

| Capa | Piezas nuevas o modificadas |
| :--- | :--- |
| **Dominio** | Ninguna. |
| **Aplicación** | `TextosLegales.cs`: `TiposDeTextoLegal.Ruta` y `TryParse` por segmento. Sin puertos nuevos. |
| **Infraestructura** | Cuerpos `Web/Textos/{terminos,privacidad,reembolso}.txt` reescritos (el recurso `reembolso.txt` conserva su nombre). |
| **Api** | `Web/Contacto/OpcionesDeContacto` y `DatosDeContacto`, su lectura en `OpcionesDelAnfitrionDelEcommerce.Leer` y su registro en `AgregarEcommerce`; `ModeloBaseDePagina.Contacto`, `ContextoDePagina`; `Controllers/ContactoController` y su vista; `LegalController`; `_Pie.cshtml`, `Inicio/Index.cshtml`, `Textos/Aceptar.cshtml`, la etiqueta de aceptación de `CheckoutController` y los mensajes de `Carrito/PantallasDelCarrito.cs` y de `Pedidos/PantallasDePedidos.cs` que citan la política (solo textos de Presentación). |
| **Web** | Estilos solo con tokens de daisyUI (sin colores sueltos). |
| **Shared** | Ninguno. |
| **Infra y docs** | `infra/secretos-requeridos.txt` (las tres variables, no secretas, en `ecommerce.env` y `ecommerce-gestion.env`), `scripts/generar-secretos.sh` (las escribe como `CAMBIAR` con `pendiente()`; `desplegar.sh` ya se niega a desplegar con un `CAMBIAR`), `scripts/ensayo-local.sh` (las rellena con los ficticios, antes del caso `*)`, y cambia el correo de ensayo con dominio `.local` a `.invalid`), `infra/README.md` (paso de versión y comprobación del nombre del entorno), `infra/respaldos/README.md` y `docs/05-seguridad/07-operacion-segura.md` (la restauración y el ensayo reutilizan el `ecommerce.env` con las tres variables), docs de la spec §5 y mención mínima en `docs/07-proceso-de-desarrollo/08-guia-primer-despliegue.md` (otro carril la edita). `tests/e2e/entorno/preparar-entorno.sh` no cambia (corre en `Development`). |

## 3. Modelo de datos

Ninguno. Los datos de contacto no se guardan. La versión vigente de los textos vive en `identidad.versiones_de_textos` (una fila, `id = 1`) que **ningún arranque siembra**: esta entrega la crea o actualiza con el paso de la sección 6.

## 4. Contratos

| Operación | Método y ruta | Entrada | Salida | Errores |
| :--- | :--- | :--- | :--- | :--- |
| Página de contacto | `GET /contacto` | ninguna | página con los tres datos | servicio no disponible si Identidad no responde y hay sesión (WEB-44) |
| Cambios y devoluciones | `GET /legal/cambios-y-devoluciones` | ninguna | texto con versión y vigencia | 404 / 503 como los demás textos |
| Ruta anterior | `GET /legal/reembolso` | ninguna | `301` a la ruta nueva (sin consulta ni cuerpo propio) | — |

## 5. Concurrencia, transacciones e idempotencia

Sin estado ni escritura: las opciones se leen una vez al arrancar y son inmutables. No hay carreras. El paso de versión es idempotente (`INSERT … ON CONFLICT … DO UPDATE`).

## 6. Seguridad y alcance

| Control | Cómo lo cumple el plan |
| :--- | :--- |
| AM06, CTL-02 (salida codificada) | Razor codifica; no hay `Html.Raw`; `href` = prefijo fijo + valor validado. |
| CTL-01 (validación de entrada) | Reglas de la sección 1 al arrancar; pruebas con `\n` final, la K Kelvin, `İ`, dígito árabe-índico y letra no ASCII en el dominio, sustitutos sueltos y uso privado (en teléfono y correo se rechazan por la regla ASCII; en la dirección se aceptan, por no estar en la regla, y la salida de Razor no lanza ni emite caracteres crudos). |
| CTL-05 (cabeceras) | Sin cambios; `/contacto` y la redirección salen con las cabeceras de `web-base`. |
| CTL-20, AM17 (configuración del entorno, nada en el repositorio) | Variables de entorno; escaneo de AC-COM-02. |
| CTL-33 (inventario de privacidad) | T01 agrega los datos de contacto y el buzón de consultas. |

- Los mensajes de arranque nombran la variable, nunca el valor (`ConfiguracionDeSecretosInvalidaException`).
- Política de textos pendientes: `/contacto` sin atributo se niega a quien tiene pendientes (DD-79) y se atiende al visitante; `/legal/{texto}` sigue permitida solo para `GET`. `PoliticaConSesionRealTests` compara una lista cerrada de rutas atribuidas, que no cambia; una prueba confirma que `/contacto` queda en el conjunto de «todo lo no atribuido se niega» y que la redirección se atiende con pendientes y con Identidad caída (haciendo fallar a la vez el estado de aceptación y las versiones).
- Entorno de ejemplo: solo `Development` y `Test`; una prueba arranca con `Development` y `Test` (ficticios) y con `Staging`, `Production`, un nombre de ensayo y uno vacío (no arrancan sin las tres variables), y otra comprueba que los Compose de despliegue fijan `ASPNETCORE_ENVIRONMENT: Production`.
- Despliegue: los valores reales los escribe quien despliega en los `.env` de la VM, **entre comillas dobles** y sin `$`, `"`, `#` ni barra invertida dentro (Compose trata `#` como comentario sin comillas e interpola `$` aun entre comillas; el README y el comentario de `generar-secretos.sh` lo advierten); el generador solo deja `CAMBIAR` y `desplegar.sh` se niega a continuar con él.
- **Paso de versión de los textos** (documentado en `infra/README.md`; va **después** de `migrar`, porque la tabla debe existir, y **antes** de abrir tráfico, porque sin la fila Identidad falla con «falta sembrarlas»; con la cuenta propietaria de la base del E-commerce, la del modo migrar, nunca la de la aplicación, que solo tiene `SELECT`; sin pasar la contraseña por la línea de comandos): en una transacción y con `ON_ERROR_STOP`, `INSERT INTO identidad.versiones_de_textos (id, terminos, privacidad, reembolso, vigente_desde) VALUES (1, '<versión de terminos>', '<versión de privacidad>', '<versión de reembolso>', '<fecha>') ON CONFLICT (id) DO UPDATE SET terminos = EXCLUDED.terminos, privacidad = EXCLUDED.privacidad, reembolso = EXCLUDED.reembolso, vigente_desde = EXCLUDED.vigente_desde`, comprobando que afecta exactamente una fila, y un `SELECT` de verificación. Las versiones se toman del archivo de referencia de COM-15. Quien lo ejecuta anota en la bitácora de despliegue quién y cuándo (la operación obliga a todos a reaceptar y ocurre fuera de la aplicación).
- Sin datos personales de clientes. El contacto del comercio no se audita ni se registra.

## 7. Pantallas

Pie de `_Pie.cshtml` (referencia: `prototype/ui-v2/tienda/`, pie y `legal.html`) con los tokens `bg-neutral`/`text-neutral-content` ya usados; bloque de contacto con `address`, enlaces `link link-hover` con foco visible y nombre accesible («Llamar al …», «Escribir a …»). `/contacto`: tarjeta única con los tres datos y la frase del comercio, estados estándar (los de error y servicio no disponible los da la política), ambos temas, 375 px sin desplazamiento horizontal. Inicio: la frase «Libros impresos y digitales…» (ya existe: se conserva y se refuerza).

## 8. Estrategia de pruebas

Cada tarea que cambia algo que una prueba existente fija actualiza esa prueba en la misma tarea (nunca la debilita); T14 es el cierre y la nota en el CHANGELOG.

| Criterio | Tipo de prueba | Dónde |
| :--- | :--- | :--- |
| AC-COM-03, AC-COM-04 | De las opciones, sin red (casos de la sección 6) y de integración del arranque (`Leer` con y sin el parámetro de ejemplo, ausente, vacío e inválido; el mensaje y los registros del arranque sin valores; la regla del nombre del entorno, que ignora mayúsculas como el resto del servicio: `development`, `TEST` y `Development` valen, y se prueba) | `tests/integration/.../Api/OpcionesDeContactoTests.cs`, `AnfitrionDelEcommerceTests.cs`, `ArranqueSeguroEcommerceTests.cs` |
| AC-COM-01, AC-COM-05, AC-COM-07, AC-COM-08 | Integración de las vistas: barrido del pie en la tienda (inicio, catálogo, textos legales, contacto, error, ingreso) y en las áreas Sucursal y Administración, con el enlace a `/contacto` desde el host de gestión; `/contacto` sin `<form>`, sin recursos de terceros y con las cabeceras de `web-base` (también la redirección); inicio, pantalla de aceptación y checkout con digitales | `tests/integration/.../Web/ContactoYPieTests.cs`, `LayoutTests.cs` |
| AC-COM-02 | Dos hosts con configuración distinta muestran lo configurado; escaneo del código de producción, la infraestructura y la documentación: los archivos de `git ls-files` bajo `src/`, `infra/`, `scripts/` y `docs/`, salvo binarios, `*.lock.json` y `wwwroot/lib/**` (el alcance literal de AC-COM-02; `tests/` queda fuera porque AGENTS.md exige datos ficticios en las pruebas, una convención que revisa la persona, no un detector, y `progress/`, `specs/` y `prototype/` porque son bitácoras y referencia con ejemplos ficticios por diseño): correo `[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}`, teléfono celular peruano `(?<![0-9A-Za-z])(\+?51[ -]?)?9[0-9]{2}[ -]?[0-9]{3}[ -]?[0-9]{3}(?![0-9A-Za-z])` y teléfono internacional `(?<![0-9A-Za-z])\+[0-9]{1,3}[ -]?[0-9][0-9 ()-]{5,14}[0-9](?![0-9A-Za-z])`, también en `.cshtml` (donde se ignoran las coincidencias de correo cuya parte local termina en `-`, `.` o `_`, que son atributos de Razor como `cantidad-@ayuda.AccesoCorto`); los ficticios de COM-05 (`999 000 000`, `999000000`, `+51999000000`, `contacto@ejemplo.test`) se admiten en cualquier archivo; las demás excepciones son pares (archivo, valor) cerrados y declarados en la prueba (por ejemplo `987654321` en `Checkout/Index.cshtml`), cada una justificada en un comentario y ninguna un dato real, y la prueba falla si un par queda sin usar; los dominios `.test`, `.invalid` y `.example` no cuentan | `tests/integration/.../Web/ContactoFueraDelRepositorioTests.cs` |
| AC-COM-06 | Integración con la política real de textos pendientes (cliente con pendientes, personal con segundo factor, Identidad caída) y composición de producción | `.../Web/PoliticaDeTextosPendientesTests.cs`, `PoliticaConSesionRealTests.cs` |
| AC-COM-09 | Integración de la redirección (con pendientes y con Identidad caída) | `.../Web/ContenidoDeTextosTests.cs` |
| AC-COM-10, AC-COM-11, AC-COM-12 | Integración del contenido (plazos comparados con `PoliticaDeDevolucion`, estados, leyes) | `.../Web/ContenidoDeTextosTests.cs` |
| AC-COM-13 | Integración: versión de referencia con la huella de cada cuerpo; la reaceptación la cubren con `[Trait("Criterio", "AC-COM-13")]` `EstadoDeCuentaDeIdentidadTests`, `AceptacionDeTextosConElHostRealTests` y la prueba de CAR-22 del pago | `.../Web/VersionesDeReferenciaDeTextosTests.cs`, `.../Identidad/`, `.../Carrito/` |
| AC-COM-14, AC-COM-01, AC-COM-08 | E2E con axe (ambos temas, teclado) y humo del pie en la tienda y de `/contacto` | `tests/e2e/base/contacto-y-pie.spec.ts` |

## 9. Riesgos

| Riesgo | Mitigación |
| :--- | :--- |
| Desplegar un cuerpo nuevo con la versión anterior. | COM-15: la versión de referencia lleva la huella; paso de despliegue idempotente con verificación. |
| Un entorno de producción mal nombrado (`Development` o `Test`) mostraría los valores ficticios. | Prueba de la regla del nombre del entorno, los Compose fijan `Production` y la dirección ficticia lleva «dato de ejemplo». |
| Los otros carriles tocan `AnfitrionDelEcommerce.cs`, `infra/` y la guía de despliegue. | Cambio mínimo en `Leer` y `AgregarEcommerce`; resolver conflictos a mano al fundir. |
| Cambiar la clave de ruta rompería la aceptación de textos. | D01: la clave del identificador no cambia; `Ruta` es aparte y una prueba fija el formato del identificador. |
| Reaceptación masiva de textos al desplegar (DD-77). | Es el comportamiento ya aprobado (IDE-43, WEB-41); avisar a la persona antes del despliegue. |

## 10. Verificación contra AGENTS.md

- [x] Dominio sin dependencias externas (no se toca).
- [x] Puertos en Aplicación; la lectura de configuración está en la raíz de composición de la Api, como las demás opciones.
- [x] Sin referencias entre servicios; la interfaz solo usa contratos públicos.
- [x] Sin datos de clientes ni alcance por sucursal que comprobar.
- [x] Sin secretos ni archivos digitales en el repositorio: el contacto real sale del entorno; en el repositorio solo hay valores ficticios (`999 000 000`, `contacto@ejemplo.test`).
