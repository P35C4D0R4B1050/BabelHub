# Presupuesto de recursos

Los recursos gratuitos son el límite más estricto del proyecto (RC07, NN12). Este documento fija presupuestos por contenedor para que cada VM tenga holgura, y define cómo se comprueba. Las cifras son **metas iniciales** y se validan con mediciones y pruebas de carga (k6) antes de aprobar el despliegue. El reparto de servicios por VM está en el [despliegue](05-despliegue.md).

## 1. Marco

Cuatro VMs de una sola cuenta: dos ARM de **1 OCPU y 6 GB** y dos AMD micro de **1/8 OCPU y 1 GB**. Objetivo de RNF12: uso sostenido de memoria y CPU **menor al 70 %** por VM, es decir, unos **4,2 GB** en las ARM y **0,7 GB** en las micro.

| VM | Recursos | Contenido |
| :--- | :--- | :--- |
| **VM-BORDE** | AMD micro | cloudflared y Nginx. |
| **VM-VENTAS** | ARM, 1 OCPU y 6 GB | Servicio E-commerce, Redis, n8n, Evolution API y un segundo conector de cloudflared. |
| **VM-INVENTARIO** | ARM, 1 OCPU y 6 GB | Servicio Inventario, réplica de gestión del E-commerce y PostgreSQL. |
| **VM-AUX** | AMD micro | Salto SSH y monitoreo ligero. |

## 2. Presupuesto de memoria por contenedor

| Contenedor | Límite (`mem_limit`) | Reserva esperada | VM | Nota |
| :--- | :---: | :---: | :--- | :--- |
| Nginx | 64 MB | 20–40 MB | VM-BORDE | |
| cloudflared | 64 MB | 20–40 MB | VM-BORDE y VM-VENTAS | Dos conectores del mismo túnel. |
| BabelHub.Ecommerce | 600 MB | 250–450 MB | VM-VENTAS | Monolito modular con la interfaz web, Identidad y Asistente. |
| Redis | 256 MB | 50–150 MB | VM-VENTAS | `maxmemory` con política de expulsión; caché de disponibilidad y contadores. |
| n8n | 600 MB | 300–500 MB | VM-VENTAS | Proceso de Node; el mayor consumo individual. |
| Evolution API | 600 MB | 300–500 MB | VM-VENTAS | Proceso de Node. |
| BabelHub.Inventario | 350 MB | 150–250 MB | VM-INVENTARIO | |
| BabelHub.Ecommerce (gestión) | 450 MB | 200–350 MB | VM-INVENTARIO | Réplica de respaldo: mismo código, solo las áreas Sucursal y Admin. |
| PostgreSQL | 2,0 GB | 1,2–1,8 GB | VM-INVENTARIO | Una instancia con dos bases de negocio (`ecommerce` e `inventario`) y la técnica de Evolution API; `shared_buffers` ~768 MB; `CONNECTION LIMIT` por rol. |
| Tareas de respaldo | 256 MB | — | VM-INVENTARIO | Volcado y cifrado secuenciales, fuera de horas pico. |
| Sistema y Docker | — | ~0,3–0,5 GB | Cada VM | |

| VM | Suma de límites | Reserva esperada (con el sistema) | Evaluación |
| :--- | :---: | :---: | :--- |
| VM-BORDE | ~0,13 GB + sistema | ~0,5 GB | Dentro del 70 % (0,7 GB); es la más justa. |
| VM-VENTAS | ~2,1 GB + sistema | ~1,5–2,3 GB | Dentro del 70 % (4,2 GB), con holgura para picos. |
| VM-INVENTARIO | ~3,1 GB + sistema | ~2,1–2,9 GB | **La VM ARM más justa en memoria:** la suma de límites (350 + 450 + 2000 + 256 MB = ~3,1 GB) más el sistema (0,3–0,5 GB) llega a unos 3,4–3,6 GB, con 0,6–0,8 GB de margen bajo el 70 % (4,2 GB); el respaldo y el archivado de WAL corren dentro de esa suma (PLA-05). Orden de recorte si supera el 70 %: bajar `shared_buffers` de PostgreSQL (768 MB), reducir el límite de la réplica de gestión (450 MB), correr el respaldo solo fuera de horas pico y, al final, mover la réplica a otro lugar. Se mide en B11. |
| VM-AUX | — | ~0,3 GB | Amplia. |

## 3. CPU, concurrencia y red

La CPU es el cuello de botella previsto. VM-VENTAS tiene un .NET y dos de Node; VM-INVENTARIO tiene dos .NET y PostgreSQL; VM-BORDE tiene solo 1/8 de OCPU.

| Medida | Meta | Cómo se verifica |
| :--- | :--- | :--- |
| Disponibilidad p95 | < 500 ms con 50 usuarios concurrentes (RNF02) | k6 con cada VM limitada a su OCPU, incluidas las llamadas entre VMs |
| Checkout p95 | < 3 s sin contar la pasarela | k6 |
| CPU sostenida por VM | < 70 % en la prueba de carga | `docker stats` o métricas del sistema |
| VM-BORDE bajo carga | Sin cola de solicitudes ni errores 5xx con 50 usuarios | k6; si falla, cloudflared y Nginx pasan a VM-VENTAS |
| Latencia entre VMs | A medir: cada llamada E-commerce → Inventario y cada consulta a la base suma un salto de red | k6 |
| Hilos y conexiones | Límite de conexiones por rol a la base; sin crecimiento sin tope | Revisión de configuración |
| Trabajos de fondo | Expiración de reservas y reintentos de avisos espaciados para no competir con el pico | Revisión del plan |

## 4. Almacenamiento

| Elemento | Presupuesto | Nota |
| :--- | :---: | :--- |
| Volúmenes de arranque | ~47 GB por VM | Mínimo de Oracle; 4 VMs = 188 GB de los 200 GB gratuitos. |
| Bases de datos | < 10 GB al inicio | Crecimiento vigilado. |
| Registros de contenedores | Rotación (por ejemplo, 3 archivos de 10 MB por contenedor) | Evita llenar el disco. |
| Respaldos y WAL locales temporales | < 5 GB | Se suben cifrados al almacén de objetos y se borran. |
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
- Si la VM-BORDE no rinde, cloudflared y Nginx pasan a VM-VENTAS. Si la latencia entre VMs hace incumplir RNF02, se ajustan las cachés antes de tocar el código.
- Si hubiera presupuesto, la mejora natural es una tercera VM ARM para PostgreSQL (≈ 7 USD al mes, a verificar), con un cambio de Compose.
