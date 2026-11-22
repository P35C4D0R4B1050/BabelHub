# perfil-realista

Perfil realista de sesiones de lectores (pausa de 5 a 30 s, media 15 s; 75 % catálogo, 18 % detalle, 5 % carrito, 2 % checkout) hasta 2 000 sesiones concurrentes, con el dimensionamiento fijo de producción (DD-17); umbrales de RNF02 por nivel.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 308 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_reqs` | cuenta 2184 |  |
| `http_req_failed` | tasa 0.14 % |  |
| `checkout_tramo{nivel:2000}` | p95 2021.5 ms, mediana 156.1 ms | OK `p(95)<3000` |
| `vus_max` |  |  |
| `acciones{tipo:checkout}` | cuenta 649 | OK `count>=0` |
| `iterations` | cuenta 34851 |  |
| `acciones{tipo:detalle}` | cuenta 6602 | OK `count>=0` |
| `http_req_duration` | p95 410.1 ms, mediana 46.8 ms |  |
| `http_req_failed{nivel:2000}` | tasa 0.14 % | OK `rate<0.01` |
| `http_req_duration{nivel:2000,operacion:inventario.disponibilidad.consultar}` | p95 130.3 ms, mediana 17.8 ms | OK `p(95)<500` |
| `acciones{tipo:carrito}` | cuenta 1825 | OK `count>=0` |
| `acciones{tipo:catalogo}` | cuenta 27111 | OK `count>=0` |
| `acciones{nivel:2000}` | cuenta 36187 | OK `count>=0` |
| `http_reqs{nivel:2000}` | cuenta 2142 | OK `count>=0` |
