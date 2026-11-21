# perfil-realista

Perfil realista de sesiones de lectores (pausa de 5 a 30 s, media 15 s; 75 % catálogo, 18 % detalle, 5 % carrito, 2 % checkout) hasta 2 000 sesiones concurrentes, con el dimensionamiento fijo de producción (DD-17); umbrales de RNF02 por nivel.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 576 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_req_failed{nivel:1000}` | tasa 0.00 % | OK `rate<0.01` |
| `acciones{tipo:carrito}` | cuenta 2266 | OK `count>=0` |
| `http_req_failed` | tasa 0.23 % |  |
| `http_reqs{nivel:2000}` | cuenta 6802 | OK `count>=0` |
| `http_reqs{nivel:1000}` | cuenta 2261 | OK `count>=0` |
| `http_reqs{nivel:1500}` | cuenta 3274 | OK `count>=0` |
| `http_req_duration{nivel:1000,operacion:inventario.disponibilidad.consultar}` | p95 136.0 ms, mediana 11.6 ms | OK `p(95)<500` |
| `http_req_duration{nivel:2000,operacion:inventario.disponibilidad.consultar}` | p95 13881.5 ms, mediana 8824.6 ms | FALLA `p(95)<500` |
| `http_req_duration` | p95 13893.6 ms, mediana 300.4 ms |  |
| `checkout_tramo{nivel:1000}` | p95 581.5 ms, mediana 46.2 ms | OK `p(95)<3000` |
| `acciones{tipo:checkout}` | cuenta 876 | OK `count>=0` |
| `http_req_duration{nivel:1500,operacion:inventario.disponibilidad.consultar}` | p95 375.2 ms, mediana 10.9 ms | OK `p(95)<500` |
| `acciones{tipo:catalogo}` | cuenta 32730 | OK `count>=0` |
| `iterations` | cuenta 42081 |  |
| `http_req_failed{nivel:2000}` | tasa 0.29 % | OK `rate<0.01` |
| `http_req_failed{nivel:1500}` | tasa 0.24 % | OK `rate<0.01` |
| `http_reqs` | cuenta 12379 |  |
| `acciones{nivel:1000}` | cuenta 7689 | OK `count>=0` |
| `acciones{nivel:1500}` | cuenta 11596 | OK `count>=0` |
| `checkout_tramo{nivel:2000}` | p95 42341.5 ms, mediana 27595.8 ms | FALLA `p(95)<3000` |
| `vus_max` |  |  |
| `acciones{tipo:detalle}` | cuenta 7692 | OK `count>=0` |
| `acciones{nivel:2000}` | cuenta 24279 | OK `count>=0` |
| `checkout_tramo{nivel:1500}` | p95 702.9 ms, mediana 43.8 ms | OK `p(95)<3000` |
