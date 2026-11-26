# Presupuesto de recursos

Los recursos gratuitos son el límite más estricto del proyecto (RC07, NN12). Este documento fija presupuestos por contenedor para que cada VM tenga holgura, y define cómo se comprueba. Las cifras son **metas iniciales** y se validan con mediciones y pruebas de carga (k6) antes de aprobar el despliegue. El reparto de servicios por VM está en el [despliegue](05-despliegue.md).

## 1. Marco

Desde el 2026-11-25 (DD-100) son **dos VMs** de una sola cuenta, las ARM de **1 OCPU y 6 GB**: no hubo capacidad para las AMD micro, la entrada (cloudflared y Nginx) corre en VM-VENTAS y VM-AUX está aplazada. Objetivo de RNF12: uso sostenido de memoria y CPU **menor al 70 %** por VM, unos **4,2 GB**.

| VM | Recursos | Contenido |
| :--- | :--- | :--- |
| **VM-VENTAS** | ARM, 1 OCPU y 6 GB | cloudflared, Nginx, servicio E-commerce, Redis, n8n y Evolution API. |
| **VM-INVENTARIO** | ARM, 1 OCPU y 6 GB | Servicio Inventario, réplica de gestión del E-commerce y PostgreSQL. |
| VM-AUX (**aplazada**) | AMD micro | Salto SSH y monitoreo ligero ([despliegue](05-despliegue.md) §6.2). |

## 2. Presupuesto de memoria por contenedor

| Contenedor | Límite (`mem_limit`) | Reserva esperada | VM | Nota |
| :--- | :---: | :---: | :--- | :--- |
| Nginx | 64 MB | 20–40 MB | VM-VENTAS | 0,2 CPU. |
| cloudflared | 128 MB | 20–60 MB | VM-VENTAS | Único conector del túnel: 0,25 CPU (con 0,05, el tope de cuando era un segundo conector, limitaría toda la entrada). |
| BabelHub.Ecommerce | 600 MB | 250–450 MB | VM-VENTAS | Monolito modular con la interfaz web, Identidad y Asistente. |
| Redis (caché y Evolution) | 128 MB | 30–80 MB | VM-VENTAS | `maxmemory` con política de expulsión `allkeys-lru`; caché de disponibilidad y del catálogo y datos de Evolution. |
| Redis (contadores de límites) | 100 MB | 10–40 MB | VM-VENTAS | Instancia dedicada con `noeviction`, tope de claves, ACL y TLS (`transversal-seguridad` D17); alerta al 70 %. |
| n8n | 600 MB | 300–500 MB | VM-VENTAS | Proceso de Node; el mayor consumo individual. |
| Evolution API | 600 MB | 300–500 MB | VM-VENTAS | Proceso de Node. |
| BabelHub.Inventario | 512 MB | 150–250 MB | VM-INVENTARIO | Dimensionamiento por decisión de la persona del 2026-11-20 (DD-17): 0,6 CPU y 512 MB (antes 0,3 CPU y 350 MB), para cumplir RNF02 según la medición de K6-1. |
| BabelHub.Ecommerce (gestión) | 450 MB | 200–350 MB | VM-INVENTARIO | Réplica de respaldo: mismo código, solo las áreas Sucursal y Admin. |
| PostgreSQL | 2,0 GB | 1,2–1,8 GB | VM-INVENTARIO | 0,4 CPU (DD-17). Una instancia con dos bases de negocio (`ecommerce` e `inventario`) y la técnica de Evolution API; `shared_buffers` ~768 MB; `CONNECTION LIMIT` por rol. |
| Tareas de respaldo | 256 MB | — | VM-INVENTARIO | Volcado y cifrado secuenciales, fuera de horas pico. |
| Sistema y Docker | — | ~0,3–0,5 GB | Cada VM | |

| VM | Suma de límites | Reserva esperada (con el sistema) | Evaluación |
| :--- | :---: | :---: | :--- |
| VM-VENTAS | ~2,3 GB + sistema | ~1,5–2,4 GB | Dentro del 70 % (4,2 GB): los límites suman 2 220 MB (600 + 128 + 100 + 600 + 600 + 64 + 128) más 0,3–0,5 GB de sistema; queda margen para los comandos de consola puntuales (hasta 600 MB). Los topes de CPU suman 2,05 sobre 1 OCPU: son topes, no reservas, y la entrada compite con la venta. |
| VM-INVENTARIO | ~3,2 GB + sistema | ~2,1–2,9 GB | **La VM ARM más justa en memoria:** la suma de límites (512 + 450 + 2000 + 256 MB = ~3,2 GB) más el sistema (0,3–0,5 GB) llega a unos 3,5–3,7 GB, con 0,5–0,7 GB de margen bajo el 70 % (4,2 GB); el respaldo y el archivado de WAL corren dentro de esa suma (PLA-05). Orden de recorte si supera el 70 %: bajar `shared_buffers` de PostgreSQL (768 MB), reducir el límite de la réplica de gestión (450 MB), correr el respaldo solo fuera de horas pico y, al final, mover la réplica a otro lugar. Se mide en B11. |

## 3. CPU, concurrencia y red

La CPU es el cuello de botella previsto. VM-VENTAS tiene un .NET, dos de Node y ahora la entrada (cloudflared y Nginx); VM-INVENTARIO tiene dos .NET y PostgreSQL.

| Medida | Meta | Cómo se verifica |
| :--- | :--- | :--- |
| Disponibilidad p95 | < 500 ms con 50 usuarios concurrentes (RNF02) | k6 con cada VM limitada a su OCPU, incluidas las llamadas entre VMs |
| Checkout p95 | < 3 s sin contar la pasarela | k6 |
| CPU sostenida por VM | < 70 % en la prueba de carga | `docker stats` o métricas del sistema |
| Entrada en VM-VENTAS bajo carga | Sin cola de solicitudes ni errores 5xx con 50 usuarios, y CPU de la VM < 70 % | k6 en la VM real (pendiente); si falla, se ajustan los topes o se vuelve a una VM aparte para la entrada cuando haya capacidad |
| Latencia entre VMs | A medir: cada llamada E-commerce → Inventario y cada consulta a la base suma un salto de red | k6 |
| Hilos y conexiones | Límite de conexiones por rol a la base; sin crecimiento sin tope | Revisión de configuración |
| Trabajos de fondo | Expiración de reservas y reintentos de avisos espaciados para no competir con el pico | Revisión del plan |

## 4. Almacenamiento

| Elemento | Presupuesto | Nota |
| :--- | :---: | :--- |
| Volúmenes de arranque | ~47 GB por VM | Mínimo de Oracle; 2 VMs = 94 GB de los 200 GB gratuitos. |
| Bases de datos | < 10 GB al inicio | Crecimiento vigilado. |
| Registros de contenedores | Rotación (por ejemplo, 3 archivos de 10 MB por contenedor) | Evita llenar el disco. |
| Respaldos y WAL locales temporales | < 5 GB | Se suben comprimidos y cifrados al almacén de respaldos y se borran. |
| Respaldos en Cloudflare R2 | ≤ 7 GB de los 10 GB gratuitos | Medición local con datos inventados: ~1,9 GB con 42 000 ventas acumuladas y venta ligera ([medición](../../progress/ensayos/medicion-respaldos-comprimidos-2026-11-25.md)). Se repite en la VM la primera semana; al superar ~7 GB se proponen alternativas a la persona responsable. |
| Archivos digitales | 0 en las VMs | Viven en el almacén de objetos (AD19); la administración los sube directamente. |

## 5. Presupuesto de la interfaz

| Medida | Meta | Fuente |
| :--- | :---: | :--- |
| LCP (percentil 75, 4G simulada) | ≤ 2,5 s | RNF26 |
| INP | ≤ 200 ms | RNF26 |
| CLS | ≤ 0,1 | RNF26 |
| Peso de la página de inicio (sin imágenes diferidas) | ≤ 500 KB *(propuesta)* | WEB-21 |
| JavaScript propio | Mínimo: htmx y pocos scripts propios | AD21 |

## 6. Control

- Cada contenedor en el Compose de su VM declara `mem_limit` y, cuando corresponde, `cpus`.
- Un incumplimiento de las metas en la prueba de carga se registra como riesgo en [drivers](01-drivers-arquitectonicos.md) y se mitiga antes del despliegue.
- Si el uso de memoria supera el 70 % sostenido, se reduce el consumo (por ejemplo, desactivar el modo desarrollo o ajustar la caché) antes de agregar recursos de pago.
- Si la entrada en VM-VENTAS no rinde, se mide qué contenedor consume (cloudflared y Nginx tienen su propio tope) y se aplica el orden de recorte antes de pedir recursos de pago. Si la latencia entre VMs hace incumplir RNF02, se ajustan las cachés antes de tocar el código.
- Si hubiera presupuesto, la mejora natural es una tercera VM ARM para PostgreSQL (≈ 7 USD al mes, a verificar), con un cambio de Compose.
