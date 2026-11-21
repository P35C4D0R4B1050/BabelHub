# perfil-realista

Perfil realista de sesiones de lectores (pausa de 5 a 30 s, media 15 s; 75 % catálogo, 18 % detalle, 5 % carrito, 2 % checkout) hasta 2 000 sesiones concurrentes, con el dimensionamiento fijo de producción (DD-17); umbrales de RNF02 por nivel.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 307 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_reqs{nivel:2000}` | cuenta 10395 | OK `count>=0` |
| `acciones{tipo:checkout}` | cuenta 713 | OK `count>=0` |
| `iterations` | cuenta 34558 |  |
| `acciones{tipo:catalogo}` | cuenta 26944 | OK `count>=0` |
| `http_req_duration{nivel:2000,operacion:inventario.disponibilidad.consultar}` | p95 559.7 ms, mediana 52.7 ms | FALLA `p(95)<500` |
| `acciones{tipo:carrito}` | cuenta 1790 | OK `count>=0` |
| `vus_max` |  |  |
| `http_req_failed` | tasa 0.11 % |  |
| `http_req_duration` | p95 547.6 ms, mediana 53.2 ms |  |
| `http_reqs` | cuenta 10437 |  |
| `acciones{tipo:detalle}` | cuenta 6467 | OK `count>=0` |
| `checkout_tramo{nivel:2000}` | p95 1168.8 ms, mediana 125.5 ms | OK `p(95)<3000` |
| `http_req_failed{nivel:2000}` | tasa 0.11 % | OK `rate<0.01` |
| `acciones{nivel:2000}` | cuenta 35914 | OK `count>=0` |
