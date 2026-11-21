# perfil-realista

Perfil realista de sesiones de lectores (pausa de 5 a 30 s, media 15 s; 75 % catálogo, 18 % detalle, 5 % carrito, 2 % checkout) hasta 2 000 sesiones concurrentes, con el dimensionamiento fijo de producción (DD-17); umbrales de RNF02 por nivel.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 636 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `checkout_tramo{nivel:1500}` | p95 737.1 ms, mediana 151.8 ms | OK `p(95)<3000` |
| `http_req_duration{nivel:1000,operacion:inventario.disponibilidad.consultar}` | p95 56.0 ms, mediana 12.4 ms | OK `p(95)<500` |
| `acciones{tipo:checkout}` | cuenta 1109 | OK `count>=0` |
| `http_req_failed` | tasa 0.18 % |  |
| `http_req_failed{nivel:1000}` | tasa 0.13 % | OK `rate<0.01` |
| `acciones{nivel:1500}` | cuenta 11678 | OK `count>=0` |
| `http_req_duration` | p95 193.1 ms, mediana 22.1 ms |  |
| `checkout_tramo{nivel:2000}` | p95 346.2 ms, mediana 81.6 ms | OK `p(95)<3000` |
| `http_req_duration{nivel:1500,operacion:inventario.disponibilidad.consultar}` | p95 365.7 ms, mediana 22.3 ms | OK `p(95)<500` |
| `vus_max` |  |  |
| `http_reqs{nivel:2000}` | cuenta 10565 | OK `count>=0` |
| `iterations` | cuenta 54451 |  |
| `checkout_tramo{nivel:1000}` | p95 141.2 ms, mediana 59.5 ms | OK `p(95)<3000` |
| `acciones{tipo:detalle}` | cuenta 10057 | OK `count>=0` |
| `http_reqs` | cuenta 16210 |  |
| `http_req_failed{nivel:2000}` | tasa 0.15 % | OK `rate<0.01` |
| `http_req_failed{nivel:1500}` | tasa 0.30 % | OK `rate<0.01` |
| `http_reqs{nivel:1500}` | cuenta 3347 | OK `count>=0` |
| `acciones{tipo:carrito}` | cuenta 2785 | OK `count>=0` |
| `http_reqs{nivel:1000}` | cuenta 2256 | OK `count>=0` |
| `acciones{tipo:catalogo}` | cuenta 41796 | OK `count>=0` |
| `http_req_duration{nivel:2000,operacion:inventario.disponibilidad.consultar}` | p95 146.5 ms, mediana 22.0 ms | OK `p(95)<500` |
| `acciones{nivel:2000}` | cuenta 36243 | OK `count>=0` |
| `acciones{nivel:1000}` | cuenta 7826 | OK `count>=0` |
