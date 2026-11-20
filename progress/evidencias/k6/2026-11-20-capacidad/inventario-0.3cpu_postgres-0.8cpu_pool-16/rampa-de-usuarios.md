# rampa-de-usuarios

Consulta de disponibilidad por niveles de 50 a 2000 usuarios virtuales; cada nivel con su p95 y su tasa de errores.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 349 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_req_failed` | tasa 15.11 % |  |
| `vus_max` |  |  |
| `http_req_duration{nivel:500}` | p95 8286.9 ms, mediana 5607.7 ms | FALLA `p(95)<500` |
| `http_req_duration` | p95 18362.5 ms, mediana 7286.5 ms |  |
| `http_req_duration{nivel:50}` | p95 897.2 ms, mediana 490.3 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:250}` | tasa 0.00 % | OK `rate<0.01` |
| `iterations` | cuenta 23013 |  |
| `http_req_failed{nivel:2000}` | tasa 47.69 % | FALLA `rate<0.01` |
| `http_req_failed{nivel:100}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:1000}` | p95 18826.0 ms, mediana 11291.9 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:50}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed{nivel:1000}` | tasa 9.62 % | FALLA `rate<0.01` |
| `checks` | tasa 84.86 % | OK `rate>0.5` |
| `http_req_duration{nivel:100}` | p95 1528.6 ms, mediana 1102.7 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:250}` | p95 5103.5 ms, mediana 3392.2 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:2000}` | p95 19401.5 ms, mediana 15894.6 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:500}` | tasa 0.00 % | OK `rate<0.01` |
| `http_reqs` | cuenta 23055 |  |
