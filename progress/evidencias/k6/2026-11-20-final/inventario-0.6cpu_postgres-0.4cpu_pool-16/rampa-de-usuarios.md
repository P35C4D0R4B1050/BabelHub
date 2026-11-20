# rampa-de-usuarios

Consulta de disponibilidad por niveles de 50 a 2000 usuarios virtuales; cada nivel con su p95 y su tasa de errores.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 347 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `vus_max` |  |  |
| `http_req_duration{nivel:100}` | p95 1868.5 ms, mediana 564.2 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:250}` | p95 4278.5 ms, mediana 1792.6 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:250}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:2000}` | p95 16921.9 ms, mediana 15208.8 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:1000}` | tasa 11.54 % | FALLA `rate<0.01` |
| `http_req_failed{nivel:100}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration` | p95 16186.4 ms, mediana 4328.6 ms |  |
| `http_req_failed{nivel:500}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:1000}` | p95 16389.4 ms, mediana 13073.2 ms | FALLA `p(95)<500` |
| `http_reqs` | cuenta 26947 |  |
| `http_req_failed{nivel:50}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed` | tasa 14.41 % |  |
| `http_req_failed{nivel:2000}` | tasa 50.85 % | FALLA `rate<0.01` |
| `checks` | tasa 85.57 % | OK `rate>0.5` |
| `http_req_duration{nivel:500}` | p95 7386.2 ms, mediana 4532.0 ms | FALLA `p(95)<500` |
| `iterations` | cuenta 26905 |  |
| `http_req_duration{nivel:50}` | p95 1445.2 ms, mediana 188.1 ms | FALLA `p(95)<500` |
