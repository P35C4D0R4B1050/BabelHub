# perfil-realista

Perfil realista de sesiones de lectores (pausa de 5 a 30 s, media 15 s; 75 % catálogo, 18 % detalle, 5 % carrito, 2 % checkout) hasta 2 000 sesiones concurrentes, con el dimensionamiento fijo de producción (DD-17); umbrales de RNF02 por nivel.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 310 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_reqs` | cuenta 8098 |  |
| `acciones{tipo:detalle}` | cuenta 5186 | OK `count>=0` |
| `http_reqs{nivel:2000}` | cuenta 8056 | OK `count>=0` |
| `acciones{tipo:checkout}` | cuenta 612 | OK `count>=0` |
| `iterations` | cuenta 27089 |  |
| `http_req_failed` | tasa 4.78 % |  |
| `acciones{nivel:2000}` | cuenta 28556 | OK `count>=0` |
| `vus_max` |  |  |
| `http_req_duration{nivel:2000,operacion:inventario.disponibilidad.consultar}` | p95 15592.3 ms, mediana 14852.5 ms | FALLA `p(95)<500` |
| `acciones{tipo:carrito}` | cuenta 1421 | OK `count>=0` |
| `checkout_tramo{nivel:2000}` | p95 48623.5 ms, mediana 46291.3 ms | FALLA `p(95)<3000` |
| `acciones{tipo:catalogo}` | cuenta 21337 | OK `count>=0` |
| `http_req_failed{nivel:2000}` | tasa 4.80 % | FALLA `rate<0.01` |
| `http_req_duration` | p95 29089.8 ms, mediana 14906.4 ms |  |
