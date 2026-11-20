# rampa-de-usuarios

Consulta de disponibilidad por niveles de 50 a 2000 usuarios virtuales; cada nivel con su p95 y su tasa de errores.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 342 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_req_duration{nivel:500}` | p95 4200.0 ms, mediana 3696.6 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:1000}` | p95 7330.2 ms, mediana 6107.4 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:50}` | p95 113.2 ms, mediana 31.5 ms | OK `p(95)<500` |
| `http_req_failed` | tasa 0.00 % |  |
| `http_req_failed{nivel:250}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:250}` | p95 2013.1 ms, mediana 1597.2 ms | FALLA `p(95)<500` |
| `vus_max` |  |  |
| `http_req_failed{nivel:1000}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:100}` | p95 613.6 ms, mediana 389.3 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:2000}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration` | p95 13696.1 ms, mediana 2748.9 ms |  |
| `http_req_failed{nivel:500}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed{nivel:50}` | tasa 0.00 % | OK `rate<0.01` |
| `checks` | tasa 100.00 % | OK `rate>0.5` |
| `http_req_duration{nivel:2000}` | p95 14190.0 ms, mediana 11512.4 ms | FALLA `p(95)<500` |
| `iterations` | cuenta 40520 |  |
| `http_reqs` | cuenta 40562 |  |
| `http_req_failed{nivel:100}` | tasa 0.00 % | OK `rate<0.01` |
