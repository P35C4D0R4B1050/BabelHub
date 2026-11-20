# Pruebas de carga (k6) — K6-1

Un escenario por atributo de calidad, con su umbral como criterio de paso. Se ejecutan con la imagen oficial `grafana/k6` (no se instala nada en Windows) contra un entorno **local en Docker con los límites de CPU y memoria de VM-INVENTARIO** (`infra/docker-compose.inventario.yml`, `docs/02-arquitectura-de-software/07`). Los resultados quedan en `progress/evidencias/k6/<fecha>/` (un `.md` con los umbrales y un `.json` completo por escenario).

| Escenario | Atributo | Criterio |
| :--- | :--- | :--- |
| [`disponibilidad`](escenarios/disponibilidad.js) | Rendimiento — RNF02, AC-FUE-08 | 50 usuarios concurrentes consultan la disponibilidad: p95 < 500 ms, sin errores |
| [`checkout-confirmacion`](escenarios/checkout-confirmacion.js) | Rendimiento del checkout | Tramo del Inventario (reservar y confirmar), hasta 50 usuarios y 5 compras/s: p95 < 3 s |
| [`sobreventa`](escenarios/sobreventa.js) | Concurrencia — AC01 | 100 usuarios compiten por 10 unidades: exactamente 10 reservas, 90 rechazos `INV-STOCK-INSUFICIENTE` y vendible final 0 |
| [`limites`](escenarios/limites.js) | Seguridad — límites de uso (API4) | Más de 100 solicitudes/min de un usuario: 429 con `Retry-After`; otro usuario no se ve afectado |
| [`resiliencia`](escenarios/resiliencia.js) | Disponibilidad — RNF03 | PostgreSQL pausado 25 s: el proceso sigue vivo, las operaciones fallan con 503 uniforme (nunca 500) y el servicio se recupera solo |
| [`rampa`](escenarios/rampa.js) | Escalabilidad | 50 → 100 → 250 → 500 → 1000 → 2000 usuarios virtuales por niveles, con p95 y errores por nivel (el techo medido del entorno acotado) |

`disponibilidad-inventario.js` (raíz) es el script original de T25 de `inventario-fuentes`; los escenarios de `escenarios/` lo reemplazan en la evidencia.

## Cómo se ejecuta

```bash
bash tests/load/entorno/preparar-carga.sh          # secretos aleatorios, certificado, PostgreSQL + Inventario con límites, base migrada, servidor de claves
bash tests/load/ejecutar.sh disponibilidad         # o checkout-confirmacion | sobreventa | limites | resiliencia | rampa
bash tests/load/entorno/ajustar-limites.sh 0.6 0.4 # opcional: otros límites de CPU (Inventario, PostgreSQL) para medir su efecto
bash tests/load/entorno/bajar-carga.sh             # apagar (con -v borra la base)
```

Variables opcionales de `ejecutar.sh`: `DURACION`, `RITMO` (compras por segundo), `UNIDADES`, `COMPETIDORES`, `NIVELES`, `SEGUNDOS_POR_NIVEL`, `PENSAR_S` (pausa de cada usuario entre solicitudes; 0,5 s por omisión, `0` mide el peor caso) y `CARGA_FECHA` (carpeta de resultados).

**Requisitos:** Windows con Docker Desktop y Git Bash (los scripts usan `cygpath` y `docker`), Node (para emitir las credenciales de prueba) y .NET (migrar la base). En Linux o macOS habría que cambiar `cygpath -m` por la ruta directa.

## Cómo funciona el entorno (`entorno/`)

- **Fuera del repositorio:** los secretos son aleatorios, de esta máquina y viven en una carpeta hermana del repositorio (`…/babelhub-carga`; Docker Desktop no comparte `C:\Users\…\AppData`). El escáner de secretos no ve nada. Nada de esto es un dato real.
- **Sin Identidad real:** `servidor-de-claves.mjs` hace de Identidad solo en este entorno (comparte la red del contenedor del Inventario porque este acepta HTTP solo hacia la propia máquina): genera una clave ES256 efímera, publica su JWKS y las revocaciones, y emite credenciales de usuario de 14 minutos (un administrador y 2 200 clientes). El Inventario las verifica con su verificador real.
- **Credenciales de servicio** (cabecera `X-Credencial-De-Servicio`, más la de origen y el origen reenviado de un cliente distinto por usuario virtual) salen del archivo de entorno y se pasan a k6 por variables, nunca por la línea de comandos ni por los resultados.
- **Siembra** (`lib/comun.js`, `setup()` de cada escenario): por la propia Api, con el token del administrador: una fuente de recepción, ediciones nuevas y su mercadería en cada corrida (el stock inicial es exacto).
- La Api exige HTTPS entre servicios: Kestrel usa un certificado autofirmado y k6 corre con `insecureSkipTLSVerify`.

## Alcance y límites de lo medido

- Se mide el **Inventario** (el servicio que concentra la concurrencia de stock); el tramo del E-commerce del checkout (carrito, pedido) se medirá cuando su Web esté completa. La pasarela no entra (RNF).
- La máquina de desarrollo no es la VM ARM de 1 OCPU: los números absolutos son de **esta** máquina con los límites de contenedor declarados; lo que demuestran son los umbrales, la ausencia de sobreventa, el 429/503 uniformes y el **techo relativo** (DOC-1). k6 corre en la misma máquina que el entorno y compite con él por CPU.
- Una corrida limpia exige la máquina en reposo (sin otras pruebas de integración ni E2E a la vez).
