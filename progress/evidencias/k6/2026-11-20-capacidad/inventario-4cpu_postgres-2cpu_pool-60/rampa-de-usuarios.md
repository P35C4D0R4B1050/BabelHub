# rampa-de-usuarios

Consulta de disponibilidad por niveles de 50 a 2000 usuarios virtuales; cada nivel con su p95 y su tasa de errores.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 339 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_req_failed{nivel:50}` | tasa 0.00 % | OK `rate<0.01` |
| `http_reqs` | cuenta 58225 |  |
| `http_req_duration` | p95 11255.7 ms, mediana 1156.3 ms |  |
| `iterations` | cuenta 58183 |  |
| `http_req_failed{nivel:100}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:50}` | p95 24.7 ms, mediana 16.0 ms | OK `p(95)<500` |
| `http_req_failed{nivel:250}` | tasa 0.00 % | OK `rate<0.01` |
| `vus_max` |  |  |
| `http_req_failed{nivel:2000}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed{nivel:500}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:100}` | p95 132.9 ms, mediana 18.4 ms | OK `p(95)<500` |
| `http_req_duration{nivel:500}` | p95 2412.9 ms, mediana 1593.3 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:250}` | p95 885.5 ms, mediana 497.1 ms | FALLA `p(95)<500` |
| `checks` | tasa 100.00 % | OK `rate>0.5` |
| `http_req_duration{nivel:2000}` | p95 13697.3 ms, mediana 9986.8 ms | FALLA `p(95)<500` |
| `http_req_failed` | tasa 0.00 % |  |
| `http_req_duration{nivel:1000}` | p95 5697.8 ms, mediana 4102.9 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:1000}` | tasa 0.00 % | OK `rate<0.01` |
