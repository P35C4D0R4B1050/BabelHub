# perfil-realista

Perfil realista de sesiones de lectores (pausa de 5 a 30 s, media 15 s; 75 % catálogo, 18 % detalle, 5 % carrito, 2 % checkout) hasta 2 000 sesiones concurrentes, con el dimensionamiento fijo de producción (DD-17); umbrales de RNF02 por nivel.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 308 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_reqs{nivel:2000}` | cuenta 5832 | OK `count>=0` |
| `acciones{tipo:carrito}` | cuenta 1888 | OK `count>=0` |
| `acciones{tipo:checkout}` | cuenta 744 | OK `count>=0` |
| `http_req_duration` | p95 353.0 ms, mediana 22.9 ms |  |
| `http_reqs` | cuenta 5874 |  |
| `acciones{tipo:detalle}` | cuenta 6439 | OK `count>=0` |
| `acciones{nivel:2000}` | cuenta 36198 | OK `count>=0` |
| `http_req_failed` | tasa 0.02 % |  |
| `acciones{tipo:catalogo}` | cuenta 27127 | OK `count>=0` |
| `http_req_failed{nivel:2000}` | tasa 0.02 % | OK `rate<0.01` |
| `vus_max` |  |  |
| `iterations` | cuenta 34880 |  |
| `http_req_duration{nivel:2000,operacion:inventario.disponibilidad.consultar}` | p95 183.3 ms, mediana 17.8 ms | OK `p(95)<500` |
| `checkout_tramo{nivel:2000}` | p95 1108.6 ms, mediana 229.1 ms | OK `p(95)<3000` |
