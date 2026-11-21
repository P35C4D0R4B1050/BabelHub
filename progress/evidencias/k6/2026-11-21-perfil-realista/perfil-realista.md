# perfil-realista

Perfil realista de sesiones de lectores (pausa de 5 a 30 s, media 15 s; 75 % catálogo, 18 % detalle, 5 % carrito, 2 % checkout) hasta 2 000 sesiones concurrentes, con el dimensionamiento fijo de producción (DD-17); umbrales de RNF02 por nivel.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 1071 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_req_duration{nivel:1000,operacion:inventario.disponibilidad.consultar}` | p95 22.5 ms, mediana 10.7 ms | OK `p(95)<500` |
| `checkout_tramo{nivel:1000}` | p95 83.1 ms, mediana 45.3 ms | OK `p(95)<3000` |
| `http_reqs{nivel:1000}` | cuenta 2227 | OK `count>=0` |
| `http_req_failed{nivel:100}` | tasa 0.00 % | OK `rate<0.01` |
| `checkout_tramo{nivel:1500}` | p95 11238.8 ms, mediana 7934.5 ms | FALLA `p(95)<3000` |
| `checkout_tramo{nivel:500}` | p95 92.5 ms, mediana 60.1 ms | OK `p(95)<3000` |
| `acciones{nivel:1500}` | cuenta 11444 | OK `count>=0` |
| `http_req_duration` | p95 2847.8 ms, mediana 13.6 ms |  |
| `vus_max` |  |  |
| `http_reqs{nivel:500}` | cuenta 1107 | OK `count>=0` |
| `acciones{nivel:100}` | cuenta 790 | OK `count>=0` |
| `http_req_failed{nivel:250}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:2000,operacion:inventario.disponibilidad.consultar}` | p95 1228.5 ms, mediana 12.2 ms | FALLA `p(95)<500` |
| `http_reqs{nivel:100}` | cuenta 216 | OK `count>=0` |
| `acciones{tipo:detalle}` | cuenta 9709 | OK `count>=0` |
| `http_req_duration{nivel:500,operacion:inventario.disponibilidad.consultar}` | p95 28.8 ms, mediana 13.1 ms | OK `p(95)<500` |
| `acciones{tipo:carrito}` | cuenta 2734 | OK `count>=0` |
| `http_reqs{nivel:250}` | cuenta 583 | OK `count>=0` |
| `http_reqs{nivel:1500}` | cuenta 3205 | OK `count>=0` |
| `http_req_duration{nivel:100,operacion:inventario.disponibilidad.consultar}` | p95 81.8 ms, mediana 16.3 ms | OK `p(95)<500` |
| `http_req_failed{nivel:1500}` | tasa 0.37 % | OK `rate<0.01` |
| `acciones{tipo:checkout}` | cuenta 1030 | OK `count>=0` |
| `http_reqs` | cuenta 15084 |  |
| `http_req_failed{nivel:500}` | tasa 0.00 % | OK `rate<0.01` |
| `iterations` | cuenta 52528 |  |
| `acciones{nivel:500}` | cuenta 3891 | OK `count>=0` |
| `http_req_duration{nivel:250,operacion:inventario.disponibilidad.consultar}` | p95 113.7 ms, mediana 25.1 ms | OK `p(95)<500` |
| `acciones{nivel:1000}` | cuenta 7684 | OK `count>=0` |
| `http_reqs{nivel:2000}` | cuenta 7704 | OK `count>=0` |
| `checkout_tramo{nivel:100}` | p95 190.4 ms, mediana 59.6 ms | OK `p(95)<3000` |
| `acciones{nivel:2000}` | cuenta 28148 | OK `count>=0` |
| `acciones{tipo:catalogo}` | cuenta 40412 | OK `count>=0` |
| `acciones{nivel:250}` | cuenta 1928 | OK `count>=0` |
| `checkout_tramo{nivel:2000}` | p95 2871.9 ms, mediana 10.5 ms | OK `p(95)<3000` |
| `http_req_duration{nivel:1500,operacion:inventario.disponibilidad.consultar}` | p95 3287.7 ms, mediana 33.3 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:2000}` | tasa 6.39 % | FALLA `rate<0.01` |
| `checkout_tramo{nivel:250}` | p95 198.2 ms, mediana 92.0 ms | OK `p(95)<3000` |
| `http_req_failed{nivel:1000}` | tasa 0.72 % | OK `rate<0.01` |
| `http_req_failed` | tasa 3.45 % |  |
