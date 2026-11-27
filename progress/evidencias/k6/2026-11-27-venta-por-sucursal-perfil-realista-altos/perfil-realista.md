# perfil-realista

Perfil realista de sesiones de lectores (pausa de 5 a 30 s, media 15 s; 75 % catálogo, 18 % detalle, 5 % carrito, 2 % checkout) hasta 2 000 sesiones concurrentes, con el dimensionamiento fijo de producción (DD-17); umbrales de RNF02 por nivel.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 411 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_reqs{nivel:2000}` | cuenta 8192 | OK `count>=0` |
| `http_req_duration{nivel:2000,operacion:inventario.disponibilidad.consultar}` | p95 1157.9 ms, mediana 11.3 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:2000}` | tasa 0.04 % | OK `rate<0.01` |
| `iterations` | cuenta 38299 |  |
| `checkout_tramo{nivel:2000}` | p95 3419.3 ms, mediana 128.3 ms | FALLA `p(95)<3000` |
| `acciones{nivel:1500}` | cuenta 11583 | OK `count>=0` |
| `vus_max` |  |  |
| `http_req_duration{nivel:1500,operacion:inventario.disponibilidad.consultar}` | p95 358.1 ms, mediana 10.4 ms | OK `p(95)<500` |
| `http_req_failed` | tasa 0.03 % |  |
| `http_req_duration` | p95 1055.4 ms, mediana 13.5 ms |  |
| `acciones{tipo:carrito}` | cuenta 1963 | OK `count>=0` |
| `checkout_tramo{nivel:1500}` | p95 2628.3 ms, mediana 57.4 ms | OK `p(95)<3000` |
| `acciones{tipo:checkout}` | cuenta 818 | OK `count>=0` |
| `acciones{nivel:2000}` | cuenta 28013 | OK `count>=0` |
| `acciones{tipo:detalle}` | cuenta 7120 | OK `count>=0` |
| `http_reqs{nivel:1500}` | cuenta 3345 | OK `count>=0` |
| `http_reqs` | cuenta 11579 |  |
| `acciones{tipo:catalogo}` | cuenta 29695 | OK `count>=0` |
| `http_req_failed{nivel:1500}` | tasa 0.00 % | OK `rate<0.01` |
