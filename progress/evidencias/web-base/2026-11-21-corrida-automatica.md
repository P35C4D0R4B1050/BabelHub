# Web base: corrida automática de T32, T33 y T34 (2026-11-21)

Entorno: host del E-commerce en `Development` (Debug) por HTTPS con el certificado de desarrollo de .NET en `https://localhost:8453` (el 8443 estaba ocupado por el entorno de carga del carril de rendimiento), Inventario en `http://localhost:8082`, PostgreSQL y Redis de `tests/e2e/entorno/`, datos ficticios de `sembrar-datos.sh --limpiar` antes de cada archivo. Navegadores: Chromium de escritorio (`escritorio`), Chromium con el perfil Pixel 5 (`telefono`) y WebKit. **Firefox no inicia en este equipo** (el sistema no lo deja lanzar) y Edge no está instalado: no se pudieron ejecutar; es parte humana (ver `protocolo-manual.md`).

Cómo repetirla (variables exportadas con `set -a`: sin ello las pruebas con sesión se omiten por falta de la clave del sembrador):

```bash
bash tests/e2e/entorno/preparar-entorno.sh
E2E_TLS=1 bash tests/e2e/entorno/arrancar-host.sh ecommerce &     # 8443 (u otro puerto libre, ajustando HostsPermitidos)
bash tests/e2e/entorno/arrancar-host.sh inventario &
bash tests/e2e/entorno/sembrar-datos.sh --limpiar
cd tests/e2e && set -a && source "$LOCALAPPDATA/babelhub-e2e/env" && set +a
E2E_BASE_URL=https://localhost:8443 npx playwright test base acceso/acceso-y-cuenta reclamaciones/libro-de-reclamaciones --workers=1
```

El CSS de `wwwroot/css/app.css` debe estar **reconstruido con `scripts/construir-estilos.sh`** antes de medir: con un CSS viejo `/reclamaciones/nueva` se desplaza en horizontal a 320 y 375 px (se comprobó: no es un defecto de la vista).

| Tarea | Qué se ejecutó | Resultado |
| :--- | :--- | :--- |
| T32a (AC-WEB-01, AC-WEB-08, AC-WEB-06) | `base/responsive-temas-y-movimiento`: sin desplazamiento horizontal a 320, 375, 768 y 1280 px en las pantallas públicas, controles de 24 px o más en 375 px, tema claro y oscuro por cookie y movimiento reducido | **Pasa** en `escritorio`, `telefono` y WebKit (7 de 7 en cada uno) |
| T33a (AC-WEB-07, AC-WEB-15) | `base/accesibilidad-del-recorrido`: axe (WCAG 2.0, 2.1 y 2.2 A y AA) en el recorrido de compra con recojo y en las pantallas base, en los dos temas, y foco visible por teclado | **Pasa** (36 de 36 con la corrida de Reclamaciones, en los tres navegadores) |
| T33a (pantallas de cuenta y de acceso) | `acceso/acceso-y-cuenta`: registro, recuperación y las siete secciones de la cuenta con axe, 375 px y recuperación de lo escrito | **Pasa** (36 de 36 en los tres navegadores; en WebKit, «cada sección de la cuenta» necesita más de los 30 s por omisión cuando el equipo está cargado: 42 s con `--timeout=150000`) |
| T33a (Libro de Reclamaciones) | `reclamaciones/libro-de-reclamaciones` con axe en los dos temas, 375 px y la constancia imprimible | **Pasa** (incluido en las 36 de arriba) |
| T34a (AC-WEB-16) | `base/rendimiento`: LCP, CLS e INP con 4G simulada (150 ms, 1,6 Mb/s) y procesador 4 veces más lento | **Reserva.** Por **HTTP** pasan inicio y catálogo; por **HTTPS de Kestrel en Debug con el certificado de desarrollo** el LCP de escritorio sale en **2,5 a 3,2 s** (meta 2,5 s) de forma repetida, CLS e INP dentro de la meta, y el teléfono pasa o falla según la corrida. La corrida de referencia de `verificacion.md` (1,7 a 1,8 s) no se reprodujo con TLS; la causa probable es el apretón de manos TLS bajo la simulación (más las 4 veces de CPU) más una compilación Debug, no el contenido, pero **no se confirmó**: hay que medir en `Release` detrás de Nginx (HTTP/2, TLS 1.3) en un equipo sin otra carga. Con el carril de rendimiento corriendo k6 en la misma máquina, los valores subieron. El detalle de obra no se pudo medir por HTTP |

**No automatizable** (queda en `protocolo-manual.md`): navegadores y móviles reales (Chrome, Edge, Firefox, Safari; Android e iOS), lector de pantalla (NVDA y VoiceOver) y las dos rondas de usabilidad con diez personas.
