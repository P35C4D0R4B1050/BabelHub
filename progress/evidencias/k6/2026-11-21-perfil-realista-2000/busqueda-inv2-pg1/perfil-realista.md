# perfil-realista

Perfil realista de sesiones de lectores (pausa de 5 a 30 s, media 15 s; 75 % catálogo, 18 % detalle, 5 % carrito, 2 % checkout) hasta 2 000 sesiones concurrentes, con el dimensionamiento fijo de producción (DD-17); umbrales de RNF02 por nivel.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 309 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_req_failed` | tasa 0.07 % |  |
| `acciones{tipo:carrito}` | cuenta 1798 | OK `count>=0` |
| `http_reqs` | cuenta 10522 |  |
| `http_req_duration{nivel:2000,operacion:inventario.disponibilidad.consultar}` | p95 200.6 ms, mediana 50.4 ms | OK `p(95)<500` |
| `acciones{tipo:catalogo}` | cuenta 26888 | OK `count>=0` |
| `http_req_failed{nivel:2000}` | tasa 0.07 % | OK `rate<0.01` |
| `acciones{tipo:checkout}` | cuenta 718 | OK `count>=0` |
| `checkout_tramo{nivel:2000}` | p95 285.8 ms, mediana 125.8 ms | OK `p(95)<3000` |
| `http_req_duration` | p95 193.8 ms, mediana 52.2 ms |  |
| `acciones{tipo:detalle}` | cuenta 6528 | OK `count>=0` |
| `http_reqs{nivel:2000}` | cuenta 10480 | OK `count>=0` |
| `vus_max` |  |  |
| `acciones{nivel:2000}` | cuenta 35932 | OK `count>=0` |
| `iterations` | cuenta 34604 |  |
