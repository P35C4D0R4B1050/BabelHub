# Presupuesto de recursos

Los recursos gratuitos son el límite más estricto del proyecto (RC07, NN12). Este documento fija presupuestos por contenedor para que el despliegue entre en 2 OCPU y 12 GB, y define cómo se comprueba. Las cifras son **metas iniciales** y se validan con mediciones y pruebas de carga (k6) antes de aprobar el despliegue.

## 1. Marco

| VM | Recursos | Contenido |
| :--- | :--- | :--- |
| **VM-APP** (ARM) | 1 OCPU y 6 GB | cloudflared, Nginx, Web, E-commerce, Inventario, Identidad, Asistente, n8n, Evolution API y Redis. |
| **VM-DATOS** (ARM) | 1 OCPU y 6 GB | PostgreSQL Ventas (con el esquema `identidad` y la base de Evolution API) y PostgreSQL Inventario, más respaldos. |
| **VM-AUX** (x86 micro, opcional) | 1/8 OCPU y 1 GB | Acceso de salto y monitoreo ligero. |

Objetivo de RNF12: uso sostenido de memoria **menor al 70 %** por VM, es decir, hasta unos **4,2 GB** por VM de 6 GB.

## 2. Presupuesto de memoria por contenedor (VM-APP)

| Contenedor | Límite (`mem_limit`) | Reserva esperada | Nota |
| :--- | :---: | :---: | :--- |
| BabelHub.Web | 400 MB | 150–250 MB | MVC con Razor; compresión y caché de salida. |
| BabelHub.Ecommerce | 400 MB | 150–250 MB | |
| BabelHub.Inventario | 350 MB | 120–200 MB | |
| BabelHub.Identidad | 250 MB | 100–150 MB | Versión reducida. |
| BabelHub.AsistenteCompra | 250 MB | 100–150 MB | Versión reducida. |
| n8n | 600 MB | 300–500 MB | Proceso de Node; el mayor consumo individual. |
| Evolution API | 600 MB | 300–500 MB | Proceso de Node. |
| Redis | 256 MB | 50–100 MB | `maxmemory` con política de expulsión. |
| Nginx | 64 MB | 20–40 MB | |
| cloudflared | 64 MB | 20–40 MB | |
| Sistema y Docker | — | ~500 MB | |
| **Total de límites** | **~3,2 GB** + sistema | **~1,9–3,0 GB** | Dentro del 70 % (4,2 GB), con holgura para picos. |

## 3. Presupuesto de memoria por contenedor (VM-DATOS)

| Contenedor | Límite | Nota |
| :--- | :---: | :--- |
| PostgreSQL Ventas | 2,0 GB | `shared_buffers` ~512 MB; `max_connections` moderado con agrupamiento de conexiones por servicio. |
| PostgreSQL Inventario | 2,0 GB | Igual que el anterior. |
| Tareas de respaldo | 256 MB | Volcado y cifrado secuenciales, fuera de horas pico. |
| Sistema | ~400 MB | |
| **Total** | **~4,7 GB** de límites | Los límites suman más que el 70 % pero el consumo real esperado (2,5–3,5 GB) lo respeta; el resto lo usa el sistema como caché de disco. |

## 4. CPU y concurrencia

La CPU es el cuello de botella previsto: 5 aplicaciones .NET y 2 de Node comparten un solo OCPU.

| Medida | Meta | Cómo se verifica |
| :--- | :--- | :--- |
| Disponibilidad p95 | < 500 ms con 50 usuarios concurrentes (RNF02) | k6 sobre una VM con 1 OCPU simulado |
| Checkout p95 | < 3 s sin contar la pasarela | k6 |
| CPU sostenida de VM-APP | < 70 % en la prueba de carga | `docker stats` o métricas del sistema |
| Hilos y conexiones | Límite de conexiones por servicio a la base; sin crecimiento sin tope | Revisión de configuración |
| Trabajos de fondo | Expiración de reservas y reintentos de avisos espaciados para no competir con el pico | Revisión del plan |

## 5. Almacenamiento

| Elemento | Presupuesto | Nota |
| :--- | :---: | :--- |
| Volúmenes de arranque | ~47 GB por VM | Mínimo de Oracle. |
| Bases de datos | < 10 GB al inicio | Crecimiento vigilado. |
| Registros de contenedores | Rotación (por ejemplo, 3 archivos de 10 MB por contenedor) | Evita llenar el disco. |
| Respaldos locales temporales | < 5 GB | Se suben al almacén de objetos y se borran. |
| Archivos digitales | 0 en las VMs | Viven en el almacén de objetos (AD19). |

## 6. Presupuesto de la interfaz

| Medida | Meta | Fuente |
| :--- | :---: | :--- |
| LCP (percentil 75, 4G simulada) | ≤ 2,5 s | RNF26 |
| INP | ≤ 200 ms | RNF26 |
| CLS | ≤ 0,1 | RNF26 |
| Peso de la página de inicio (sin imágenes diferidas) | ≤ 500 KB *(propuesta)* | WEB-21 |
| JavaScript propio | Mínimo: htmx y pocos scripts propios | AD21 |

## 7. Control

- Cada contenedor en el Compose declara `mem_limit` y, cuando corresponde, `cpus`.
- Un incumplimiento de las metas en la prueba de carga se registra como riesgo en [drivers](01-drivers-arquitectonicos.md) y se mitiga antes del despliegue.
- Si el uso de memoria supera el 70 % sostenido, se reduce el consumo (por ejemplo, desactivar el modo desarrollo o ajustar la caché) antes de agregar recursos de pago.
