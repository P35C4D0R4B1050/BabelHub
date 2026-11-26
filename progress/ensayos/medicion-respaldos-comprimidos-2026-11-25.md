# Medición local de los respaldos comprimidos (2026-11-25)

Para decidir si la retención de PLA-18 cabe en el cupo gratuito de Cloudflare R2 (10 GB-mes) con compresión antes de cifrar (D25, D26). **Es una medición local con datos inventados, no una constancia de la VM ni de R2**: se repite en la VM la primera semana (plan §9).

## Cómo se midió

- Equipo de desarrollo (x86-64, no ARM) con la pila de dos «VMs» de `scripts/ensayo-local.sh`: PostgreSQL 17 con la configuración de `infra/docker-compose.inventario.yml` (`archive_timeout=300` y el latido de cada minuto). Solo se cambió `archive_command` para guardar cada segmento en un volumen y medirlo con `zstd -3` (y `zstd -10` y `gzip` para comparar).
- Ventas inventadas en un esquema aparte (reserva, tres líneas, descuento de stock, pedido, pago, ocho filas de auditoría y un aviso cifrado por venta), con textos aleatorios, que comprimen peor que los reales. La aplicación no generó esas ventas.
- Fases: 31 minutos en reposo; 31 minutos con 53 ventas (unas 2 460 al día); una ráfaga de 2 000 ventas; y la copia del clúster (`pg_basebackup -Ft`) con distintos volúmenes.

## Resultados

Archivo de cambios (cada segmento mide 16 MiB sin comprimir):

| Fase | Segmentos | Media con `zstd -3` | Máximo | Media `zstd -10` |
| :--- | :---: | :---: | :---: | :---: |
| Reposo (solo latido) | 6 en 31 min | 45 KB | 58 KB | 43 KB |
| Venta ligera (53 ventas) | 6 en 31 min | 138 KB | 189 KB | 131 KB |
| Ráfaga de 2 000 ventas | 1 casi lleno | 5,9 MB | — | 5,6 MB |

Copia del clúster:

| Datos | Bases en disco | `tar` sin comprimir | Con `zstd -3` | Con `zstd -10` |
| :--- | :---: | :---: | :---: | :---: |
| Sin ventas | 54 MB | 73 MB | 6,0 MB | 5,3 MB |
| 2 053 ventas | 65 MB | 83 MB | 10,1 MB | 9,3 MB |
| 42 053 ventas | 247 MB | 265 MB | 91 MB | 88 MB |

La copia de 265 MB tardó 21 s en leer, comprimir y contar en este equipo (en la VM de 1 OCPU tardará más). Cada venta inventada añade unos 2 KB comprimidos a la copia. `zstd -10` ahorra entre un 3 % y un 13 % y gasta mucha más CPU: se mantiene el nivel 3.

## Proyección frente a los 10 GB

Se cuentan **17 copias** (7 diarias, 4 semanales y 3 mensuales, más las que esperan hasta un día su retiro) y **8 días** de archivo (7 de retención y hasta uno de retiro), a 288 segmentos diarios.

| Escenario | Copias | Archivo de cambios | Total | Del cupo |
| :--- | :---: | :---: | :---: | :---: |
| Sin ventas | 0,10 GB | 0,10 GB | **0,2 GB** | 2 % |
| 42 000 ventas acumuladas y venta ligera | 1,55 GB | 0,32 GB | **1,9 GB** | 19 % |
| Las mismas copias y unas 20 000 ventas al día (extrapolación, no medida) | 1,55 GB | ~1,8 GB | **~3,4 GB** | 34 % |

- **Cabe con holgura: queda muy por debajo del umbral de ~7 GB**, así que no se proponen alternativas ahora.
- **Cuándo dejaría de caber:** con la venta ligera, a partir de unos **390 MB comprimidos por copia** (cerca de 1,1 GB de `tar`; unas 190 000 ventas como las inventadas) el total pasa de 7 GB. Entonces se proponen alternativas a la persona responsable antes de tocar nada.
- Sin comprimir no cabría: solo el archivo de 8 días serían unos 39 GB.
- Operaciones: unas 9 000 subidas al mes de archivo y unas decenas de copias (en varias partes cuando son grandes), frente a 1 000 000 de operaciones de clase A gratuitas.

## Lo que esta medición no cubre

Datos reales (incluidas las bases de n8n u otras que crezcan en el mismo clúster), ARM, el tiempo y la memoria en la VM con la venta activa, y el cómputo de GB-mes de R2. La primera semana con la VM se compara el tamaño de los cuatro contenedores con esta tabla (guía de despliegue).
