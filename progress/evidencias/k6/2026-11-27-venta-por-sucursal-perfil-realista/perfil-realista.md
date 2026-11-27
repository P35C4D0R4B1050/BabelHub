# perfil-realista

Perfil realista de sesiones de lectores (pausa de 5 a 30 s, media 15 s; 75 % catálogo, 18 % detalle, 5 % carrito, 2 % checkout) hasta 2 000 sesiones concurrentes, con el dimensionamiento fijo de producción (DD-17); umbrales de RNF02 por nivel.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 662 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `vus_max` |  |  |
| `http_reqs{nivel:100}` | cuenta 219 | OK `count>=0` |
| `acciones{tipo:checkout}` | cuenta 305 | OK `count>=0` |
| `checkout_tramo{nivel:1000}` | p95 939.6 ms, mediana 140.6 ms | OK `p(95)<3000` |
| `http_reqs{nivel:250}` | cuenta 581 | OK `count>=0` |
| `iterations` | cuenta 14753 |  |
| `acciones{nivel:250}` | cuenta 1953 | OK `count>=0` |
| `checkout_tramo{nivel:250}` | p95 246.3 ms, mediana 131.2 ms | OK `p(95)<3000` |
| `http_req_duration{nivel:500,operacion:inventario.disponibilidad.consultar}` | p95 90.0 ms, mediana 26.4 ms | OK `p(95)<500` |
| `acciones{tipo:detalle}` | cuenta 2761 | OK `count>=0` |
| `http_req_duration` | p95 231.9 ms, mediana 21.9 ms |  |
| `checkout_tramo{nivel:100}` | p95 205.8 ms, mediana 94.3 ms | OK `p(95)<3000` |
| `acciones{nivel:1000}` | cuenta 8784 | OK `count>=0` |
| `http_reqs{nivel:1000}` | cuenta 2523 | OK `count>=0` |
| `http_req_duration{nivel:100,operacion:inventario.disponibilidad.consultar}` | p95 123.4 ms, mediana 30.7 ms | OK `p(95)<500` |
| `acciones{nivel:500}` | cuenta 3904 | OK `count>=0` |
| `http_reqs` | cuenta 4471 |  |
| `http_req_failed{nivel:500}` | tasa 0.09 % | OK `rate<0.01` |
| `checkout_tramo{nivel:500}` | p95 347.0 ms, mediana 116.6 ms | OK `p(95)<3000` |
| `http_req_duration{nivel:1000,operacion:inventario.disponibilidad.consultar}` | p95 283.0 ms, mediana 13.3 ms | OK `p(95)<500` |
| `acciones{tipo:catalogo}` | cuenta 11569 | OK `count>=0` |
| `http_reqs{nivel:500}` | cuenta 1106 | OK `count>=0` |
| `http_req_failed{nivel:1000}` | tasa 0.12 % | OK `rate<0.01` |
| `http_req_failed{nivel:100}` | tasa 0.00 % | OK `rate<0.01` |
| `acciones{nivel:100}` | cuenta 747 | OK `count>=0` |
| `http_req_duration{nivel:250,operacion:inventario.disponibilidad.consultar}` | p95 100.6 ms, mediana 22.1 ms | OK `p(95)<500` |
| `acciones{tipo:carrito}` | cuenta 753 | OK `count>=0` |
| `http_req_failed{nivel:250}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed` | tasa 0.09 % |  |
