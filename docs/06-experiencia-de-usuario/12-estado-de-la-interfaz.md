# Estado de la interfaz: qué cambió en `v0.1.3` y qué no se revierte

Punto de partida para quien mejore la interfaz (Codex). Describe la aplicación **desde `v0.1.2` hasta `v0.1.3`** (2026-11-27). Las reglas de trabajo, las rutas permitidas y la entrega están en [colaboración en la interfaz](../07-proceso-de-desarrollo/10-colaboracion-ui.md); las tareas, en el [tablero](../../progress/coordinacion-ui.md). Si algo de aquí contradice una spec o la aplicación, ganan ellas y se anota en «Peticiones».

`Api/` abrevia `src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api/`.

## 1. Decisiones de la persona responsable que la interfaz respeta

| Tema | Decisión |
| :--- | :--- |
| Sucursal | Se elige **al comprar**, en la ficha del libro. **El selector de sucursal de la cabecera se quitó y no se repone.** La última elegida solo preselecciona en la ficha. |
| Stock | La ficha muestra el stock por sucursal y el máximo es el de la sucursal elegida. No se simula ni se suma el stock de la red. |
| Líneas | Un libro impreso por sucursal, hasta 20 unidades por línea; puede haber más líneas del mismo libro y sucursal si su stock alcanza; 30 líneas por compra. Una línea nunca se divide ni se cambia de sucursal sola. |
| Envío | Cada sucursal que atiende cobra el suyo, incluida la primera (S/ 30,00 por defecto, configurable). Recojo gratis en la sucursal de cada libro, sin traslados para el cliente. Si una sucursal no llega a enviar, se devuelve su envío. |
| Precio | Uno por edición. No se calcula cercanía ni se compara sucursales por precio. |
| Contraseñas | La barra **orienta** (de rojo a verde); no se obliga a mezclar mayúsculas, números o símbolos. Mínimo 10 caracteres. |
| Segundo factor | Se configura con **código QR**, además de la clave escrita. |
| Acceso del personal | Un enlace «Acceso del personal» en la tienda (solo aparece si está configurado). No se agrega un segundo botón «Ingresar». |
| Pagos y envíos | La tienda desplegada no cobra: se conserva el mensaje de pago no disponible y los avisos «Pagos simulados» y «Envíos simulados» donde aparecen. |
| Contacto y legales | El contacto del comercio sigue visible (pie y página de contacto). Los textos legales no se editan; la política de privacidad mantiene «sucursal preferida». |

## 2. Qué cambió, por pantalla

| Pantalla (archivos) | Hoy |
| :--- | :--- |
| Cabecera (`Views/Shared/_Cabecera.cshtml`) | Sin selector de sucursal. |
| Ficha (`Areas/Tienda/Views/Catalogo/Obra.cshtml`) | Lista «Sucursal que atenderá tu pedido», «Cantidad» con máximo según la sucursal, «Agregar impreso al carrito» o «Comprar digital», y la tabla «Disponibilidad por sucursal». Estados: sin sucursales (mensaje y «Reintentar»), agotado (botón deshabilitado). |
| Carrito (`Areas/Tienda/Views/Carrito/Index.cshtml`, `wwwroot/js/bh.js`) | Cada tarjeta dice «Lo atiende …». Cambiar la cantidad envía el formulario solo (sin JavaScript queda «Actualizar»); al volver recupera el foco y anuncia el resultado. «Quitar» nombra libro y sucursal. Los libros agregados antes de este cambio solo se pueden quitar. |
| Checkout (`Areas/Tienda/Views/Checkout/Index.cshtml`) | Recojo gratis por sucursal; a domicilio, «Envío» y debajo el importe de cada sucursal. Si una tarifa cambia antes de pagar, avisa que el total cambió. |
| Pedidos (`Views/Shared/Pedidos/*`) | Cada línea dice qué sucursal la atiende (nombre en la tienda, código para el personal). El encargado ve solo sus impresos. Al anular solo parte, se avisa. |
| Costos de envío (`Areas/Admin/Views/CostosDeEnvio/Index.cshtml`) | Pantalla nueva de la administración central, con su entrada de menú. Interfaz sencilla. |
| Ingreso, registro y cuenta (`Views/Ingreso`, `Areas/Tienda/Views/Registro`, `Views/Cuenta`, `Views/Shared/_CampoDeContrasena.cshtml`, `wwwroot/js/contrasenas.js`) | Mostrar u ocultar la contraseña, barra orientativa, aviso de coincidencia. Tras ingresar desde la compra se vuelve al carrito. |
| Segundo factor (`Views/Shared/_CodigoQr.cshtml`) | Código QR generado en el servidor. |
| Pie (`Views/Shared/_Pie.cshtml`) e ingreso | Enlace «Acceso del personal». |
| Gestión | La raíz lleva al ingreso o al destino del rol. |
| Cuenta y devoluciones | Enlaces a «Mis devoluciones», «Mis hojas de reclamación» y, en la administración, a los reportes de archivos defectuosos. |
| Todas las tablas | El contenedor que se desplaza de lado se puede enfocar (`tabindex="0" role="region" aria-label="…"`). **Se conserva** en cualquier rediseño. |

## 3. Lo que un cambio visual no puede romper

- **Formularios:** `action`, `method`, nombres y valores de campos, `asp-*`, el token antifalsificación y los botones de `<noscript>`.
- **Atributos que usan los guiones o las pruebas:** `data-bh-sucursal-de-ficha`, `data-bh-maximo`, `data-bh-cantidad`, `data-bh-anuncio`, `data-bh-campo-de-contrasena`, `data-bh-nueva`, `data-bh-repite`, `data-bh-medidor`, `data-bh-coincidencia`, `data-bh-ver`, `data-bh-nombre`, `data-bh-formulario`, `data-bh-qr`, `data-bh-secreto`, `data-bh-acceso-del-personal`, `data-bh-pago-no-disponible`, `data-bh-pagos-simulados`, `data-bh-envios-simulados`, `data-bh-cuenta-regresiva`, `data-bh-vencido`, `data-bh-icono`. Los demás `data-bh-*` tampoco se quitan.
- **Textos que las pruebas buscan** (etiquetas, nombres de botones, regiones y encabezados). Entre otros: «Sucursal que atenderá tu pedido», «Cantidad», «Cantidad de … en …», «Agregar impreso al carrito», «Comprar digital», «Tu carrito», «Libros en tu carrito», «Avisos sobre tu carrito», «Continuar con la compra», «Elegir recojo», «Elegir domicilio», «Ir al pago», «Para continuar, acepta:», «Resumen de tu pedido», «Pagar tu compra», «Correo electrónico», «Contraseña», «Ingresar», «Preparar», «Marcar listas», «Generar guía del envío», «Anular el pedido», «Secciones de tu cuenta». Cambiar un texto es un cambio que se pide en «Peticiones».
- **Sin estilos ni scripts en línea** (CSP) y colores solo con los tokens de los temas.
- **Sin JavaScript** todo sigue funcionando.

## 4. Cómo ver y verificar los cambios

1. **Genera la hoja de estilos antes de mirar nada** (en cada copia nueva del repositorio no existe): `bash scripts/construir-estilos.sh`. Sin ella la aplicación se ve sin diseño y las comprobaciones de accesibilidad fallan.
2. Entorno local con datos ficticios: [`tests/e2e/README.md`](../../tests/e2e/README.md).
3. Pantallas (unos 2 minutos): `dotnet test tests/integration/BabelHub.IntegrationTests --filter "(FullyQualifiedName~Pantallas|FullyQualifiedName~.Web.)&Categoria!=Lanzamiento&Categoria!=Guiones"`.
4. Corrida de cada cambio (unos 9 minutos): `dotnet test BabelHub.sln --filter "Categoria!=Lanzamiento&Categoria!=Guiones"`.
5. Recorridos con Playwright y axe, en escritorio y teléfono, para lo que toques. Revisa también el teléfono: ahí aparecieron fallos que en escritorio no se ven.
6. `bash scripts/guardia-ui.sh codex/<tarea> main` y `dotnet run --project tools/SpecCheck`.

## 5. Qué queda por mejorar en la interfaz

- La persona ve la interfaz **«muy estática»** (animaciones, por detallar) y el panel de la gestión distinto del [prototipo](../../prototype/ui-v2/): títulos gigantes y menú superior que se parte en dos filas.
- Las pantallas nuevas (venta por sucursal, costos de envío, ayudas de contraseña, QR) son funcionales y **no tienen diseño fino**.
- Las tareas y su estado están en el tablero (UI-0 a UI-5).
