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

Todo en `tests/e2e/entorno/`, solo para desarrollo y sin datos reales (el archivo de entorno se genera con secretos aleatorios en `%LOCALAPPDATA%babelhub-e2eenv`, fuera del repositorio; se cambia con `E2E_ENV_FILE`):

```bash
bash tests/e2e/entorno/preparar-entorno.sh                # PostgreSQL y Redis aislados (proyecto `bhe2e`, puertos 55432 y 56379) y las dos bases migradas (COMP-2)
bash tests/e2e/entorno/arrancar-host.sh inventario &      # http://localhost:8082
bash tests/e2e/entorno/arrancar-host.sh ecommerce &       # http://localhost:8080 (el borde real recibe HTTPS)
cd tests/e2e && E2E_FORWARDED_PROTO=https npx playwright test carrito/humo-del-entorno --project=escritorio --project=telefono --workers=1
```

- `E2E_FORWARDED_PROTO=https` hace que Playwright envíe `X-Forwarded-Proto: https`: el host local escucha en HTTP y sin esa cabecera las cookies `__Host-` y el antiforgery fallan (el borde real termina el TLS).
- Cada servicio usa **cuentas de base de datos con nombres distintos** (`acceso_inv`, `depurador_ajustes_inv`, `depurador_auditoria_inv` en el Inventario): los roles de PostgreSQL son del clúster y, en una sola instancia, los nombres iguales de los dos servicios (`acceso`, `depurador_auditoria`) se pisan las contraseñas al migrar.
- El host se niega a arrancar con las variables `BABELHUB__Migrador__*` en su entorno (COMP-2); `arrancar-host.sh` las quita.
- Para apagar: `docker compose -p bhe2e -f infra/docker-compose.local.yml --env-file "$LOCALAPPDATA/babelhub-e2e/env" down` (el volumen conserva las bases).

**Estado:** el humo del visitante (carrito vacío, catálogo, cabeceras de seguridad y axe AA) corre en Chromium de escritorio y de teléfono. Los recorridos con compra (`carrito-checkout.spec.ts`) siguen omitidos por faltar: (1) sembrado de catálogo (obras y ediciones) y de inventario (sucursales, fuentes y stock); (2) sesiones de clientes (no hay pantalla de ingreso: es de `web-identidad`) y su `storageState`; (3) el esquema `pedidos` (T28) para confirmar la compra;.

Navegadores (decisión de la persona, `progress/coordinacion.md` §6): **obligatorios** `escritorio` y `telefono` (Chromium) y `webkit` si la caché lo tiene; **Firefox es informativo**: un proyecto opcional que `playwright.config.ts` omite con un aviso si no está en la caché o si el sistema no deja lanzarlo (hoy `firefox.exe` responde «Permission denied»; no se toca la seguridad del sistema). Un fallo solo de Firefox se registra como hallazgo Menor y no reabre bloques cerrados, salvo un defecto real de estándares o accesibilidad que afecte también a los demás. WebKit está instalado en esta máquina (`webkit-2370`, autorizado por la persona) y corre; cada navegador opcional se sondea lanzándolo de verdad (el resultado se guarda en el entorno para los procesos de trabajo). `E2E_SIN_NAVEGADORES_OPCIONALES=1` los omite siempre.

Requisitos previos de los recorridos del carrito: la composición del Carrito en `Program.cs` (`AgregarCarrito`, `AgregarTrabajosDelCarritoEnLaApi`, `UsarLimiteDeCuerpoDelCarrito`), el Inventario y Pagos reales o sus pantallas, y el carrito sembrado en cada `storageState` (la ficha aún no agrega al carrito).

Requisitos previos de los recorridos del catálogo: el CSS compilado (`scripts/construir-estilos.sh`) y la composición del Catálogo en `Program.cs`.

Los `storageState` (cookies de sesión) van en `tests/e2e/.auth/`, que Git ignora: nunca se suben. Los `trace` de Playwright pueden contener cookies; `test-results/` también está ignorado.

## Sembrador de sesiones (solo desarrollo)

`POST /desarrollo/sesion` en el host del E-commerce crea una cuenta con el correo verificado y la hace ingresar **por el camino real** (el ingreso, el segundo factor del personal con su secreto TOTP y la sesión web); devuelve el `storageState` de Playwright con la cookie `__Host-bh-tienda` (cliente) o `__Host-bh-gestion` (personal). La cabecera `X-Usuario-Id` trae el identificador de la cuenta creada. Cada llamada crea una cuenta nueva con correo `e2e-<rol>-<hex>@example.test` y contraseña aleatoria que no se devuelve.

```bash
source "$LOCALAPPDATA/babelhub-e2e/env"   # trae BABELHUB__Sembrador__Clave
curl -s -X POST http://localhost:8080/desarrollo/sesion -H "X-Sembrador-De-Desarrollo: $BABELHUB__Sembrador__Clave" -H "Content-Type: application/json" -d '{"rol":"cliente"}' > .auth/cliente.json
curl -s -X POST http://localhost:8080/desarrollo/sesion -H "X-Sembrador-De-Desarrollo: $BABELHUB__Sembrador__Clave" -H "Content-Type: application/json" -d '{"rol":"encargado","sucursal":"ayacucho"}' > .auth/encargado.json
```

- Roles: `cliente`, `encargado` (con `sucursal`: el **código** de la sucursal, convertido al identificador canónico de `IdentificadorDeSucursal`), `logistica` y `administrador`. El cliente solo existe en el host de la tienda y el personal solo en el de la gestión: una instancia sirve un host (`BABELHUB__Web__Host`); para sembrar personal se levanta otra con `gestion`.
- **No existe en producción, ni siquiera por configuración**: (1) el código está bajo `#if DEBUG` y la imagen se publica en `Release`, así que el endpoint no está en el binario; (2) solo se registra con `ASPNETCORE_ENVIRONMENT=Development`; (3) solo atiende conexiones de la propia máquina (loopback); (4) exige la cabecera `X-Sembrador-De-Desarrollo` con la clave aleatoria `BABELHUB__Sembrador__Clave` (la genera `preparar-entorno.sh` en el archivo de entorno; sin ella el endpoint ni se mapea). El host solo atiende conexiones con origen y destino loopback hacia `localhost`, `127.0.0.1` o `[::1]`: un túnel o una reasignación de DNS no pasan. Lo que falla responde 404 sin pista (sin leer el cuerpo). `arrancar-host.sh` ya arranca en Debug y Development. Una prueba (`SembradorDeSesionesTests`) comprueba cada cerrojo.
- Requisitos: las bases migradas (COMP-2) y una fila en `identidad.versiones_de_textos` (`terminos`, `privacidad` y `reembolso` vigentes; el sembrado de la operación aún no la crea). Si no hay clave de firma activa, el sembrador crea la primera, como haría la operación.
- El navegador acepta la cookie `__Host-` solo sobre HTTPS: con el host local en HTTP, Playwright manda `X-Forwarded-Proto: https` (`E2E_FORWARDED_PROTO=https`).
- Los archivos resultantes van en `tests/e2e/.auth/` (ignorado por Git): son sesiones vivas.
