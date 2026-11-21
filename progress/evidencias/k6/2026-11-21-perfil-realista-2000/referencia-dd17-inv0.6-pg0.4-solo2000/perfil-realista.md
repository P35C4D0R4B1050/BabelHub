# perfil-realista

Perfil realista de sesiones de lectores (pausa de 5 a 30 s, media 15 s; 75 % catálogo, 18 % detalle, 5 % carrito, 2 % checkout) hasta 2 000 sesiones concurrentes, con el dimensionamiento fijo de producción (DD-17); umbrales de RNF02 por nivel.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 308 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_req_duration` | p95 1706.8 ms, mediana 74.4 ms |  |
| `acciones{nivel:2000}` | cuenta 35913 | OK `count>=0` |
| `http_req_duration{nivel:2000,operacion:inventario.disponibilidad.consultar}` | p95 1575.7 ms, mediana 73.0 ms | FALLA `p(95)<500` |
| `http_req_failed` | tasa 0.01 % |  |
| `vus_max` |  |  |
| `http_reqs{nivel:2000}` | cuenta 10379 | OK `count>=0` |
| `acciones{tipo:catalogo}` | cuenta 26958 | OK `count>=0` |
| `http_reqs` | cuenta 10421 |  |
| `acciones{tipo:checkout}` | cuenta 713 | OK `count>=0` |
| `checkout_tramo{nivel:2000}` | p95 4400.7 ms, mediana 212.4 ms | FALLA `p(95)<3000` |
| `iterations` | cuenta 34549 |  |
| `acciones{tipo:detalle}` | cuenta 6448 | OK `count>=0` |
| `http_req_failed{nivel:2000}` | tasa 0.01 % | OK `rate<0.01` |
| `acciones{tipo:carrito}` | cuenta 1794 | OK `count>=0` |
