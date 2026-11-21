# perfil-realista

Perfil realista de sesiones de lectores (pausa de 5 a 30 s, media 15 s; 75 % catálogo, 18 % detalle, 5 % carrito, 2 % checkout) hasta 2 000 sesiones concurrentes, con el dimensionamiento fijo de producción (DD-17); umbrales de RNF02 por nivel.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 307 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `acciones{tipo:checkout}` | cuenta 758 | OK `count>=0` |
| `checkout_tramo{nivel:2000}` | p95 710.1 ms, mediana 114.2 ms | OK `p(95)<3000` |
| `http_reqs{nivel:2000}` | cuenta 10494 | OK `count>=0` |
| `acciones{tipo:carrito}` | cuenta 1784 | OK `count>=0` |
| `vus_max` |  |  |
| `acciones{tipo:catalogo}` | cuenta 26965 | OK `count>=0` |
| `http_req_failed{nivel:2000}` | tasa 0.04 % | OK `rate<0.01` |
| `http_req_duration{nivel:2000,operacion:inventario.disponibilidad.consultar}` | p95 313.1 ms, mediana 36.0 ms | OK `p(95)<500` |
| `iterations` | cuenta 34615 |  |
| `http_req_failed` | tasa 0.04 % |  |
| `acciones{nivel:2000}` | cuenta 35943 | OK `count>=0` |
| `acciones{tipo:detalle}` | cuenta 6436 | OK `count>=0` |
| `http_req_duration` | p95 310.5 ms, mediana 39.2 ms |  |
| `http_reqs` | cuenta 10536 |  |
