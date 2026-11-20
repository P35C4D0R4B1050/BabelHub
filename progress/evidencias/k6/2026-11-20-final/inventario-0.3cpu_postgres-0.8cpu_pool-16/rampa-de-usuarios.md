# rampa-de-usuarios

Consulta de disponibilidad por niveles de 50 a 2000 usuarios virtuales; cada nivel con su p95 y su tasa de errores.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 348 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_req_duration{nivel:250}` | p95 5294.2 ms, mediana 1655.1 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:1000}` | p95 14612.7 ms, mediana 9205.0 ms | FALLA `p(95)<500` |
| `http_req_failed` | tasa 34.65 % |  |
| `http_req_failed{nivel:2000}` | tasa 81.73 % | FALLA `rate<0.01` |
| `http_req_duration{nivel:2000}` | p95 17238.5 ms, mediana 0.0 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:1000}` | tasa 0.00 % | OK `rate<0.01` |
| `http_reqs` | cuenta 33712 |  |
| `http_req_duration` | p95 16217.2 ms, mediana 1523.7 ms |  |
| `http_req_failed{nivel:50}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:500}` | p95 8577.5 ms, mediana 4651.9 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:100}` | p95 1564.6 ms, mediana 625.4 ms | FALLA `p(95)<500` |
| `iterations` | cuenta 33670 |  |
| `http_req_duration{nivel:50}` | p95 1445.2 ms, mediana 609.1 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:100}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed{nivel:250}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed{nivel:500}` | tasa 0.00 % | OK `rate<0.01` |
| `checks` | tasa 65.31 % | OK `rate>0.5` |
| `vus_max` |  |  |
