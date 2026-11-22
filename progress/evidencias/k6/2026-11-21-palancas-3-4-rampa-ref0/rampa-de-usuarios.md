# rampa-de-usuarios

Consulta de disponibilidad por niveles de 50 a 2000 usuarios virtuales; cada nivel con su p95 y su tasa de errores.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 347 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `iterations` | cuenta 18026 |  |
| `http_req_failed{nivel:2000}` | tasa 68.33 % | FALLA `rate<0.01` |
| `http_req_failed{nivel:250}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:250}` | p95 7052.3 ms, mediana 5501.5 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:50}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:100}` | p95 2791.4 ms, mediana 2060.7 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:500}` | p95 11889.4 ms, mediana 11382.0 ms | FALLA `p(95)<500` |
| `checks` | tasa 67.53 % | OK `rate>0.5` |
| `http_req_duration{nivel:2000}` | p95 18676.2 ms, mediana 16212.9 ms | FALLA `p(95)<500` |
| `vus_max` |  |  |
| `http_req_failed{nivel:100}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:1000}` | p95 17520.5 ms, mediana 15289.2 ms | FALLA `p(95)<500` |
| `http_req_failed` | tasa 32.39 % |  |
| `http_req_failed{nivel:1000}` | tasa 45.25 % | FALLA `rate<0.01` |
| `http_req_duration{nivel:50}` | p95 1399.5 ms, mediana 847.7 ms | FALLA `p(95)<500` |
| `http_req_duration` | p95 18003.2 ms, mediana 14414.1 ms |  |
| `http_reqs` | cuenta 18068 |  |
| `http_req_failed{nivel:500}` | tasa 0.00 % | OK `rate<0.01` |
