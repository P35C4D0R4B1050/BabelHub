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

Versiones: `@playwright/test` y `playwright-core` fijados en **1.64.0**, la que usa los navegadores de la caché local (`chromium-1248`, `firefox-1555`); con otra versión Playwright pide otros navegadores y falla al lanzarlos. Node está en `C:\Program Files\nodejs` (si el shell no lo ve: `export PATH="$PATH:/c/Program Files/nodejs"`); `npm install` y `npx playwright test carrito` (sin las variables `E2E_*` los recorridos con datos se omiten).

## Entorno local de punta a punta (T36 de `ecommerce-carrito-checkout`)

Todo en `tests/e2e/entorno/`, solo para desarrollo y sin datos reales (`.env.e2e` lo ignora Git y se genera con secretos aleatorios):

```bash
bash tests/e2e/entorno/preparar-entorno.sh                # PostgreSQL y Redis aislados (proyecto `bhe2e`, puertos 55432 y 56379) y las dos bases migradas (COMP-2)
bash tests/e2e/entorno/arrancar-host.sh inventario &      # http://localhost:8082
bash tests/e2e/entorno/arrancar-host.sh ecommerce &       # http://localhost:8080 (el borde real recibe HTTPS)
cd tests/e2e && E2E_FORWARDED_PROTO=https npx playwright test carrito/humo-del-entorno --project=escritorio --project=telefono --workers=1
```

- `E2E_FORWARDED_PROTO=https` hace que Playwright envíe `X-Forwarded-Proto: https`: el host local escucha en HTTP y sin esa cabecera las cookies `__Host-` y el antiforgery fallan (el borde real termina el TLS).
- Cada servicio usa **cuentas de base de datos con nombres distintos** (`acceso_inv`, `depurador_ajustes_inv`, `depurador_auditoria_inv` en el Inventario): los roles de PostgreSQL son del clúster y, en una sola instancia, los nombres iguales de los dos servicios (`acceso`, `depurador_auditoria`) se pisan las contraseñas al migrar.
- El host se niega a arrancar con las variables `BABELHUB__Migrador__*` en su entorno (COMP-2); `arrancar-host.sh` las quita.
- Para apagar: `docker compose -p bhe2e -f infra/docker-compose.local.yml --env-file .env.e2e down` (el volumen conserva las bases).

**Estado:** el humo del visitante (carrito vacío, catálogo, cabeceras de seguridad y axe AA) corre en Chromium de escritorio y de teléfono. Los recorridos con compra (`carrito-checkout.spec.ts`) siguen omitidos por faltar: (1) sembrado de catálogo (obras y ediciones) y de inventario (sucursales, fuentes y stock); (2) sesiones de clientes (no hay pantalla de ingreso: es de `web-identidad`) y su `storageState`; (3) el esquema `pedidos` (T28) para confirmar la compra; y (4) Firefox no se puede lanzar en este equipo (`firefox.exe` responde «Permission denied» y Playwright «spawn UNKNOWN», también sin sandbox).

Navegadores: `escritorio` y `telefono` (Chromium) y `firefox`; los tres usan los navegadores de la caché de Playwright (`%LOCALAPPDATA%ms-playwright`) y solo hace falta Node para ejecutarlos.

Requisitos previos de los recorridos del carrito: la composición del Carrito en `Program.cs` (`AgregarCarrito`, `AgregarTrabajosDelCarritoEnLaApi`, `UsarLimiteDeCuerpoDelCarrito`), el Inventario y Pagos reales o sus pantallas, y el carrito sembrado en cada `storageState` (la ficha aún no agrega al carrito).

Requisitos previos de los recorridos del catálogo: el CSS compilado (`scripts/construir-estilos.sh`) y la composición del Catálogo en `Program.cs`.

Los `storageState` (cookies de sesión) van en `tests/e2e/.auth/`, que Git ignora: nunca se suben. Los `trace` de Playwright pueden contener cookies; `test-results/` también está ignorado.
