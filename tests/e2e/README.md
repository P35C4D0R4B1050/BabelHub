# Pruebas E2E (Playwright)

Recorridos completos sobre la interfaz web del E-commerce que verifican los criterios de aceptación de las specs: búsqueda → compra con recojo → cambio de estado, compra digital → descarga, traslado, ajuste de inventario, etc. Se crean con `ui-screen` y `spec-implement` cuando la Web tenga las pantallas.

Convenciones: un archivo por recorrido, el criterio en el nombre o en una anotación (`AC-XXX-NN`), datos de ejemplo de `docs/07-proceso-de-desarrollo/02-convenciones-tecnicas.md` y ejecución contra el entorno local de `infra/`.

## Cómo ejecutarlas

No se instalan paquetes automáticamente. Cuando la persona responsable lo apruebe:

```bash
cd tests/e2e
npm install
npx playwright install chromium
E2E_BASE_URL=http://localhost:8080 npx playwright test catalogo
```

Variables de datos (los recorridos que las necesitan se omiten si faltan; nunca se inventan datos):

| Variable | Qué es |
| :--- | :--- |
| `E2E_BASE_URL` | URL del E-commerce local de `infra/`. |
| `E2E_OBRA_DOS_EDICIONES` | Título de una obra con impresa (con stock en alguna sucursal) y digital (AC-CAT-07). |
| `E2E_OBRA_IMPRESA_AGOTADA` | Título de una obra con impresa agotada en toda la red y digital disponible (AC-CAT-17). |
| `E2E_ADMIN_URL` | URL del host de gestión (la administración no existe en el host de la tienda). |
| `E2E_ADMIN_STORAGE_STATE` | `storageState` de Playwright con la sesión de un administrador central (las pantallas de acceso son de `web-identidad`). |
| `E2E_ENCARGADO_STORAGE_STATE` | Ídem, de un Encargado de sucursal (debe recibir 403 en `/administracion/obras`). |
| `E2E_CLIENTE_MIXTO_STORAGE_STATE` | `storageState` de un cliente con un impreso con stock y un digital que no posee en el carrito (carrito y checkout, CAR-15). |
| `E2E_CLIENTE_DIGITAL_STORAGE_STATE` | Ídem, con solo libros digitales (AC-CAR-06). |
| `E2E_VISITANTE_STORAGE_STATE` | Visitante con un libro en su carrito (cookie `__Host-bh-carrito`; AC-CAR-13). |
| `E2E_CLIENTE_TRAS_FUSION_STORAGE_STATE` | Cliente recién ingresado con avisos de ajuste sin ver (AC-CAR-22). |

Navegadores: `escritorio` y `telefono` (Chromium) y `firefox`; los tres usan los navegadores de la caché de Playwright (`%LOCALAPPDATA%ms-playwright`) y solo hace falta Node para ejecutarlos.

Requisitos previos de los recorridos del carrito: la composición del Carrito en `Program.cs` (`AgregarCarrito`, `AgregarTrabajosDelCarritoEnLaApi`, `UsarLimiteDeCuerpoDelCarrito`), el Inventario y Pagos reales o sus pantallas, y el carrito sembrado en cada `storageState` (la ficha aún no agrega al carrito).

Requisitos previos de los recorridos del catálogo: el CSS compilado (`scripts/construir-estilos.sh`) y la composición del Catálogo en `Program.cs`.

Los `storageState` (cookies de sesión) van en `tests/e2e/.auth/`, que Git ignora: nunca se suben. Los `trace` de Playwright pueden contener cookies; `test-results/` también está ignorado.
