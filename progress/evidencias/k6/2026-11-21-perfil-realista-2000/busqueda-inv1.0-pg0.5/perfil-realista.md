# perfil-realista

Perfil realista de sesiones de lectores (pausa de 5 a 30 s, media 15 s; 75 % catálogo, 18 % detalle, 5 % carrito, 2 % checkout) hasta 2 000 sesiones concurrentes, con el dimensionamiento fijo de producción (DD-17); umbrales de RNF02 por nivel.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 307 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `acciones{tipo:checkout}` | cuenta 742 | OK `count>=0` |
| `http_req_duration` | p95 1589.8 ms, mediana 16.4 ms |  |
| `http_req_failed` | tasa 0.02 % |  |
| `http_reqs` | cuenta 10552 |  |
| `checkout_tramo{nivel:2000}` | p95 5599.9 ms, mediana 61.2 ms | FALLA `p(95)<3000` |
| `http_req_failed{nivel:2000}` | tasa 0.02 % | OK `rate<0.01` |
| `acciones{tipo:catalogo}` | cuenta 26934 | OK `count>=0` |
| `http_req_duration{nivel:2000,operacion:inventario.disponibilidad.consultar}` | p95 1480.9 ms, mediana 14.5 ms | FALLA `p(95)<500` |
| `iterations` | cuenta 34623 |  |
| `acciones{tipo:detalle}` | cuenta 6452 | OK `count>=0` |
| `vus_max` |  |  |
| `http_reqs{nivel:2000}` | cuenta 10510 | OK `count>=0` |
| `acciones{tipo:carrito}` | cuenta 1835 | OK `count>=0` |
| `acciones{nivel:2000}` | cuenta 35963 | OK `count>=0` |
