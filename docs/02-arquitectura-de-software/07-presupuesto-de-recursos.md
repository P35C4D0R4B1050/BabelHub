# Presupuesto de recursos

Los recursos gratuitos son el límite más estricto del proyecto (RC07, NN12). Este documento fija presupuestos por contenedor para que cada VM de 1 OCPU y 6 GB tenga holgura, y define cómo se comprueba. Las cifras son **metas iniciales** y se validan con mediciones y pruebas de carga (k6) antes de aprobar el despliegue. El reparto de servicios por VM está en el [despliegue](05-despliegue.md).

## 1. Marco

Cada VM ARM tiene **1 OCPU y 6 GB**. Objetivo de RNF12: uso sostenido de memoria **menor al 70 %** por VM, es decir, hasta unos **4,2 GB**.

| Perfil | VM | Contenido |
| :--- | :--- | :--- |
| Objetivo | **VM-BORDE** | cloudflared, Nginx, Web, Identidad. |
| Objetivo | **VM-VENTAS** | E-commerce, Asistente, n8n y Evolution API. |
| Objetivo | **VM-INVENTARIO** | Inventario. |
| Objetivo | **VM-DATOS** | PostgreSQL (bases `ecommerce`, `inventario`, `identidad` y `evolution`), Redis y respaldos. |
| Mínimo | **VM-APP** | Todos los contenedores de las tres VMs de aplicaciones anteriores. |
| Mínimo | **VM-DATOS** | La misma. |
| Ambos | **VM-AUX** (x86 micro, opcional) | Salto SSH y monitoreo ligero. |

## 2. Presupuesto de memoria por contenedor (aplicaciones)

| Contenedor | Límite (`mem_limit`) | Reserva esperada | VM (perfil objetivo) | Nota |
| :--- | :---: | :---: | :--- | :--- |
| BabelHub.Web | 400 MB | 150–250 MB | VM-BORDE | MVC con Razor; compresión y caché de salida. |
| BabelHub.Identidad | 250 MB | 100–150 MB | VM-BORDE | Versión reducida. |
| Nginx | 64 MB | 20–40 MB | VM-BORDE | |
| cloudflared | 64 MB | 20–40 MB | VM-BORDE | |
| BabelHub.Ecommerce | 400 MB | 150–250 MB | VM-VENTAS | |
| BabelHub.AsistenteCompra | 250 MB | 100–150 MB | VM-VENTAS | Versión reducida. |
| n8n | 600 MB | 300–500 MB | VM-VENTAS | Proceso de Node; el mayor consumo individual. |
| Evolution API | 600 MB | 300–500 MB | VM-VENTAS | Proceso de Node. |
| BabelHub.Inventario | 350 MB | 120–200 MB | VM-INVENTARIO | |
| Sistema y Docker | — | ~500 MB | Cada VM | |

| VM | Suma de límites | Reserva esperada (con el sistema) | Evaluación |
| :--- | :---: | :---: | :--- |
| VM-BORDE | ~0,8 GB + sistema | ~1,1–1,3 GB | Muy dentro del 70 % (4,2 GB). |
| VM-VENTAS | ~1,85 GB + sistema | ~1,4–2,1 GB | Dentro del 70 %, con holgura para picos. |
| VM-INVENTARIO | ~0,35 GB + sistema | ~0,7–0,8 GB | Muy amplia: la VM se justifica por la CPU, no por la memoria. |
| VM-APP (perfil mínimo) | ~3,2 GB + sistema | ~1,9–3,0 GB | Dentro del 70 %; la CPU es el límite. |

## 3. Presupuesto de memoria por contenedor (VM-DATOS)

| Contenedor | Límite | Nota |
| :--- | :---: | :--- |
| PostgreSQL (una instancia, cuatro bases) | 2,5 GB | `shared_buffers` ~768 MB; `max_connections` moderado, con `CONNECTION LIMIT` por rol y agrupamiento de conexiones por servicio. |
| Redis | 256 MB | `maxmemory` con política de expulsión; contraseña y ACL por servicio. |
| Tareas de respaldo | 256 MB | Volcado y cifrado secuenciales por base, fuera de horas pico. |
| Sistema | ~400 MB | |
| **Total** | **~3,4 GB** de límites | Por debajo del 70 % (4,2 GB); el consumo real esperado es de 2,0–2,8 GB y el resto lo usa el sistema como caché de disco. |

Una sola instancia ahorra unos 0,5–1 GB frente a dos instancias, porque comparte el juego de buffers.

## 4. CPU, concurrencia y red

La CPU es el cuello de botella previsto. Cada VM tiene 1 OCPU, pero el reparto reduce la competencia: VM-BORDE tiene dos aplicaciones .NET, VM-VENTAS dos .NET y dos de Node, y VM-INVENTARIO una sola. En el perfil mínimo, 5 aplicaciones .NET y 2 de Node comparten un OCPU.

| Medida | Meta | Cómo se verifica |
| :--- | :--- | :--- |
| Disponibilidad p95 | < 500 ms con 50 usuarios concurrentes (RNF02) | k6 con cada VM limitada a 1 OCPU, incluidas las llamadas entre VMs |
| Checkout p95 | < 3 s sin contar la pasarela | k6 |
| CPU sostenida por VM | < 70 % en la prueba de carga | `docker stats` o métricas del sistema |
| Latencia entre VMs (perfil objetivo) | A medir: cada llamada E-commerce → Inventario y cada consulta a la base suma un salto de red | Prueba de concepto de red (B11) y k6 |
| Hilos y conexiones | Límite de conexiones por servicio a la base; sin crecimiento sin tope | Revisión de configuración |
| Trabajos de fondo | Expiración de reservas y reintentos de avisos espaciados para no competir con el pico | Revisión del plan |

## 5. Almacenamiento

| Elemento | Presupuesto | Nota |
| :--- | :---: | :--- |
| Volúmenes de arranque | ~47 GB por VM | Mínimo de Oracle; con dos cuentas, 94 GB de las dos VMs ARM más los de las VMs micro por cuenta, dentro de 200 GB. |
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

- Cada contenedor en el Compose de su VM declara `mem_limit` y, cuando corresponde, `cpus`.
- Un incumplimiento de las metas en la prueba de carga se registra como riesgo en [drivers](01-drivers-arquitectonicos.md) y se mitiga antes del despliegue.
- Si el uso de memoria supera el 70 % sostenido, se reduce el consumo (por ejemplo, desactivar el modo desarrollo o ajustar la caché) antes de agregar recursos de pago.
- Si la latencia entre VMs hace incumplir RNF02, se agrupan Inventario y E-commerce en la misma VM (reparto del perfil mínimo para esos dos servicios) antes de tocar el código.
