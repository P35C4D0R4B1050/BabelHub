# rampa-de-usuarios

Consulta de disponibilidad por niveles de 50 a 2000 usuarios virtuales; cada nivel con su p95 y su tasa de errores.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 343 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `iterations` | cuenta 37630 |  |
| `http_req_duration{nivel:50}` | p95 66.3 ms, mediana 18.5 ms | OK `p(95)<500` |
| `http_req_duration{nivel:2000}` | p95 15193.5 ms, mediana 14990.3 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:250}` | p95 2298.1 ms, mediana 1593.1 ms | FALLA `p(95)<500` |
| `vus_max` |  |  |
| `http_reqs` | cuenta 37672 |  |
| `http_req_failed{nivel:100}` | tasa 0.00 % | OK `rate<0.01` |
| `checks` | tasa 96.70 % | OK `rate>0.5` |
| `http_req_failed{nivel:2000}` | tasa 14.92 % | FALLA `rate<0.01` |
| `http_req_failed{nivel:50}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:100}` | p95 603.4 ms, mediana 405.0 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:250}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:1000}` | p95 8867.4 ms, mediana 7486.8 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:1000}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:500}` | p95 4398.6 ms, mediana 3805.1 ms | FALLA `p(95)<500` |
| `http_req_failed` | tasa 3.30 % |  |
| `http_req_duration` | p95 15092.0 ms, mediana 2395.8 ms |  |
| `http_req_failed{nivel:500}` | tasa 0.00 % | OK `rate<0.01` |
