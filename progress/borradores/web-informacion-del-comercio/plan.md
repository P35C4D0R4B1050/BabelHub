---
spec: web-informacion-del-comercio
estado: Draft
---

# Plan — Información del comercio, contacto y política de cambios y devoluciones

> **Cómo se construye.** Borrador de plan escrito mientras la spec espera la ratificación de DD-75 a DD-79 (la puerta de `spec-approve` terminó en la ronda 3 con un único Importante procedimental, ver `progress/log/2026-11-23-contacto-y-legales.md`). **No se aprueba ni se implementa** hasta que la spec esté Approved. No agrega comportamiento: si hace falta, se vuelve a la spec.

## 1. Resumen técnico

Todo vive en la **interfaz web** del E-commerce (Presentación) y en el módulo `Web` ya existente; no hay módulo nuevo, base de datos nueva ni servicio nuevo.

- **Contacto (COM-01 a COM-05).** `OpcionesDeContacto` (clase de opciones en `Api/Web/Contacto`, `BABELHUB__Contacto__Telefono`, `…Correo`, `…Direccion`) se lee y valida en `OpcionesDelAnfitrionDelEcommerce.Leer` junto al resto de la configuración (mismo mecanismo que `Carrito:CostoDeEnvio`): junta los problemas, nombra la variable con `LectorDeSecretos.Variable` y nunca el valor. Un parámetro `permitirContactoDeEjemplo` (verdadero solo si `IsDevelopment()` o el entorno se llama `Test`, calculado en `AgregarEcommerce`) decide si el dato ausente se sustituye por el ficticio; un dato presente e inválido falla en todo entorno. Las opciones validadas llegan a la vista como `DatosDeContacto` (registro inmutable con `Telefono`, `EnlaceDeLlamada`, `Correo`, `Direccion`): el enlace `tel:` se arma una vez al validar (dígitos y `+` inicial; `+51` si son 9 dígitos que empiezan con 9 y no hay `+`); el `mailto:` es el prefijo fijo más el correo ya validado.
- **Pie y página (COM-01, COM-06 a COM-09).** `ModeloBaseDePagina.Contacto` lo completa `ContextoDePagina.Completar` desde un servicio opcional `OpcionesDeContacto? = null` (como `IMenuDeNavegacion?`); `_Pie.cshtml` pinta los datos, el enlace `/contacto`, la frase del comercio y el enlace «Cambios y devoluciones». Un `ContactoController` (`GET /contacto`, sin `[PermitidaConPasoPendiente]` ni `[PermitidaConTextosPendientes]`, DD-79) arma la página con el estado estándar. Sin `Contacto` en el modelo (host de pruebas) el pie no pinta el bloque; la composición de producción lo registra siempre y la prueba de composición lo comprueba.
- **Cambios y devoluciones (COM-10, COM-11).** `TiposDeTextoLegal.Clave(Reembolso)` pasa a `cambios-y-devoluciones` y `TryParse` acepta además `reembolso`; el recurso `reembolso.txt` se renombra `cambios-y-devoluciones.txt`. En `LegalController.Ver`, la clave `reembolso` responde `RedirectPermanent("/legal/cambios-y-devoluciones")` (destino constante, sin copiar la consulta); la ruta ya lleva `[PermitidaConTextosPendientes(TextosLegales)]` y `[PermitidaConPasoPendiente]`. El tipo, el identificador y la versión de Identidad no cambian (D01). Se actualizan el título, la etiqueta del pago y los textos que citan «política de reembolso».
- **Contenido (COM-09, COM-12 a COM-14, COM-17).** Se reescriben los tres cuerpos (`terminos.txt`, `privacidad.txt`, `cambios-y-devoluciones.txt`) con los datos del comercio por referencia (el pie y `/contacto`, nunca un valor), la ley peruana, Indecopi y las reglas de Devoluciones. Siguen marcados `PROVISIONAL` (WEB-29).
- **Versión (COM-15).** `VersionesDeReferenciaDeTextos` (en las pruebas de integración) guarda por texto la versión de referencia y la huella SHA-256 del cuerpo embebido; una prueba falla si el cuerpo cambia sin que cambie la versión de referencia. El despliegue fija esa misma versión en `identidad.versiones_de_textos` (paso nuevo de `infra/README.md`; el sembrado E2E la toma de la referencia).

## 2. Capas afectadas

| Capa | Piezas nuevas o modificadas |
| :--- | :--- |
| **Dominio** | Ninguna. |
| **Aplicación** | `TextosLegales.cs`: clave `cambios-y-devoluciones` y alias `reembolso`. Sin puertos nuevos. |
| **Infraestructura** | Recursos `Web/Textos/*.txt` (tres cuerpos, uno renombrado). |
| **Api** | `Web/Contacto/OpcionesDeContacto` (+ `DatosDeContacto`), su lectura en `OpcionesDelAnfitrionDelEcommerce.Leer` y su registro en `AgregarEcommerce`; `ModeloBaseDePagina.Contacto`, `ContextoDePagina`; `ContactoController` y su vista; `LegalController` (redirección); `_Pie.cshtml`, `Inicio/Index.cshtml`, `Textos/Aceptar.cshtml`, etiquetas de `CheckoutController` y de los mensajes que citan la política. |
| **Web** | Estilos solo con tokens de daisyUI (sin colores sueltos). |
| **Shared** | Ninguno. |
| **Infra y docs** | `infra/secretos-requeridos.txt` (las tres variables, en `ecommerce.env` y `ecommerce-gestion.env`, marcadas como no secretas), `infra/README.md` (paso de versión), compose y guía de restauración si propagan variables, `tests/e2e/entorno/preparar-entorno.sh`, docs listados en spec §5. |

## 3. Modelo de datos

Ninguno. Los datos de contacto no se guardan (spec §5). La versión vigente de los textos ya vive en `identidad.versiones_de_textos` (una fila); esta entrega solo cambia su contenido en el despliegue.

## 4. Contratos

| Operación | Método y ruta | Entrada | Salida | Errores |
| :--- | :--- | :--- | :--- | :--- |
| Página de contacto | `GET /contacto` | ninguna | página con los tres datos | servicio no disponible si Identidad no responde y hay sesión (WEB-44) |
| Cambios y devoluciones | `GET /legal/cambios-y-devoluciones` | ninguna | texto con versión y vigencia | 404 / 503 como los demás textos |
| Ruta anterior | `GET /legal/reembolso` | ninguna | `301` a la ruta nueva (permanente, sin consulta ni cuerpo propio) | — |

## 5. Concurrencia, transacciones e idempotencia

Sin estado ni escritura: las opciones se leen una vez al arrancar y son inmutables. No hay carreras.

## 6. Seguridad y alcance

- Salida siempre codificada por Razor; `href` = prefijo fijo (`tel:`, `mailto:`) + valor validado (reglas de spec §5, conjuntos ASCII, sin `?`, `&`, `%`, `,`, espacios Unicode ni caracteres de formato). Los mensajes de arranque nombran la variable, nunca el valor (`ConfiguracionDeSecretosInvalidaException`).
- Las rutas nuevas pasan por la política de textos pendientes: `/contacto` sin atributo (se niega a quien tiene pendientes, DD-79) y `/legal/{texto}` ya permitida; la prueba que recorre `EndpointDataSource` de ambos hosts sigue verde y se amplía con ambas.
- CSP y cabeceras de `web-base` sin cambios; `/contacto` no carga recursos de terceros. La redirección tiene destino constante: no hay redirección abierta.
- Sin datos personales de clientes. El contacto del comercio no se audita ni se registra.

## 7. Pantallas

Pie de `_Pie.cshtml` (referencia visual: `prototype/ui-v2/tienda/`, pie y `legal.html`) con tokens `bg-neutral`/`text-neutral-content` ya usados; bloque de contacto con `address`, enlaces `link link-hover` con foco visible y nombre accesible («Llamar al …», «Escribir a …»). `/contacto`: tarjeta única con los tres datos y la frase del comercio, estados estándar (vacío no aplica; error y servicio no disponible los da la política), ambos temas, 375 px sin desplazamiento. Inicio: frase «Libros impresos y digitales…» (ya existe: se conserva y se refuerza).

## 8. Estrategia de pruebas

| Criterio | Tipo de prueba | Dónde |
| :--- | :--- | :--- |
| AC-COM-03, AC-COM-04 | Unitaria de las opciones (formatos, ficticios, enlace `tel:`) y de integración del arranque (`Leer` en Production/Staging/Development sin y con valores) | `tests/integration/.../Api/OpcionesDeContactoTests.cs`, `AnfitrionDelEcommerceTests.cs` |
| AC-COM-01, AC-COM-05, AC-COM-07, AC-COM-08 | Integración de las vistas (barrido de páginas con pie, `/contacto`, inicio, aceptación) | `tests/integration/.../Web/ContactoYPieTests.cs`, `LayoutTests.cs` |
| AC-COM-02 | Escaneo de patrones de teléfono y correo en `src`, `infra`, `docs` y `tests` con la lista permitida | `tests/integration/.../Web/ContactoFueraDelRepositorioTests.cs` |
| AC-COM-06 | Integración con la política real de textos pendientes (cliente con pendientes, personal con segundo factor, Identidad caída) | `.../Web/PoliticaDeTextosPendientesTests.cs` |
| AC-COM-09 | Integración de la redirección (también con pendientes) | `.../Web/ContenidoDeTextosTests.cs` |
| AC-COM-10, AC-COM-11, AC-COM-12 | Integración del contenido (plazos comparados con `PoliticaDeDevolucion`, estados, leyes) | `.../Web/ContenidoDeTextosTests.cs` |
| AC-COM-13 | Integración: huella y versión de referencia de cada cuerpo | `.../Web/VersionesDeReferenciaDeTextosTests.cs` |
| AC-COM-14 | E2E con axe (ambos temas, teclado) | `tests/e2e/base/contacto-y-pie.spec.ts` |
| AC-COM-01, AC-COM-08 | E2E de humo del pie en tienda y página de contacto | `tests/e2e/base/contacto-y-pie.spec.ts` |

## 9. Riesgos

| Riesgo | Mitigación |
| :--- | :--- |
| Desplegar un cuerpo nuevo con la versión anterior. | COM-15: prueba de huella + paso de despliegue; D03. |
| Un entorno de producción mal nombrado (`Development`) mostraría los valores ficticios. | Comprobación en la guía de despliegue; el pie marca la dirección como «dato de ejemplo». |
| Los otros carriles tocan `AnfitrionDelEcommerce.cs` (modo Staging). | Cambio mínimo en `Leer` y `AgregarEcommerce`; resolver conflictos a mano al fundir. |
| Reaceptación masiva de textos al desplegar (DD-77). | Es el comportamiento ya aprobado de IDE-43 / WEB-41; avisar a la persona antes del despliegue. |

## 10. Verificación contra AGENTS.md

- [x] Dominio sin dependencias externas (no se toca).
- [x] Puertos en Aplicación; la lectura de configuración está en la raíz de composición de la Api, como las demás opciones.
- [x] Sin referencias entre servicios; la interfaz solo usa contratos públicos.
- [x] Sin datos de clientes ni alcance por sucursal que comprobar.
- [x] Sin secretos ni archivos digitales en el repositorio: el contacto real sale del entorno; en el repositorio solo hay valores ficticios (`999 000 000`, `contacto@ejemplo.test`).
