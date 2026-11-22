# perfil-realista

Perfil realista de sesiones de lectores (pausa de 5 a 30 s, media 15 s; 75 % catálogo, 18 % detalle, 5 % carrito, 2 % checkout) hasta 2 000 sesiones concurrentes, con el dimensionamiento fijo de producción (DD-17); umbrales de RNF02 por nivel.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 306 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_reqs` | cuenta 10604 |  |
| `iterations` | cuenta 34765 |  |
| `http_req_duration{nivel:2000,operacion:inventario.disponibilidad.consultar}` | p95 70.5 ms, mediana 13.6 ms | OK `p(95)<500` |
| `acciones{tipo:carrito}` | cuenta 1860 | OK `count>=0` |
| `http_reqs{nivel:2000}` | cuenta 10562 | OK `count>=0` |
| `acciones{tipo:checkout}` | cuenta 727 | OK `count>=0` |
| `acciones{tipo:catalogo}` | cuenta 26993 | OK `count>=0` |
| `checkout_tramo{nivel:2000}` | p95 356.1 ms, mediana 66.0 ms | OK `p(95)<3000` |
| `acciones{tipo:detalle}` | cuenta 6521 | OK `count>=0` |
| `acciones{nivel:2000}` | cuenta 36101 | OK `count>=0` |
| `vus_max` |  |  |
| `http_req_duration` | p95 80.5 ms, mediana 15.3 ms |  |
| `http_req_failed{nivel:2000}` | tasa 0.04 % | OK `rate<0.01` |
| `http_req_failed` | tasa 0.04 % |  |
