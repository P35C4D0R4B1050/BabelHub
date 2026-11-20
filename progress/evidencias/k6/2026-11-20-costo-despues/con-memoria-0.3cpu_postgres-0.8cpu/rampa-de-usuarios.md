# rampa-de-usuarios

Consulta de disponibilidad por niveles de 50 a 2000 usuarios virtuales; cada nivel con su p95 y su tasa de errores.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 346 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_reqs` | cuenta 24248 |  |
| `http_req_duration{nivel:500}` | p95 6202.6 ms, mediana 3902.1 ms | FALLA `p(95)<500` |
| `vus_max` |  |  |
| `http_req_failed{nivel:50}` | tasa 0.00 % | OK `rate<0.01` |
| `iterations` | cuenta 24206 |  |
| `http_req_failed{nivel:2000}` | tasa 64.33 % | FALLA `rate<0.01` |
| `http_req_duration` | p95 18589.3 ms, mediana 6099.9 ms |  |
| `http_req_duration{nivel:1000}` | p95 15406.9 ms, mediana 8409.3 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:250}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed{nivel:1000}` | tasa 3.88 % | FALLA `rate<0.01` |
| `checks` | tasa 82.18 % | OK `rate>0.5` |
| `http_req_failed{nivel:500}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed{nivel:100}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:2000}` | p95 19190.5 ms, mediana 16976.9 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:100}` | p95 2291.5 ms, mediana 1698.9 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:50}` | p95 1296.0 ms, mediana 801.5 ms | FALLA `p(95)<500` |
| `http_req_failed` | tasa 17.79 % |  |
| `http_req_duration{nivel:250}` | p95 4405.9 ms, mediana 3408.5 ms | FALLA `p(95)<500` |
