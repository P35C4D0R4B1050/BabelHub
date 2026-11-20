# rampa-de-usuarios

Consulta de disponibilidad por niveles de 50 a 2000 usuarios virtuales; cada nivel con su p95 y su tasa de errores.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 340 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_req_failed{nivel:100}` | tasa 0.00 % | OK `rate<0.01` |
| `http_reqs` | cuenta 42305 |  |
| `http_req_duration{nivel:50}` | p95 424.7 ms, mediana 58.6 ms | OK `p(95)<500` |
| `http_req_duration{nivel:500}` | p95 4106.5 ms, mediana 2642.3 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:250}` | p95 2104.2 ms, mediana 1300.7 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:500}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:100}` | p95 602.9 ms, mediana 310.5 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:50}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed{nivel:2000}` | tasa 0.17 % | OK `rate<0.01` |
| `http_req_failed{nivel:1000}` | tasa 33.32 % | FALLA `rate<0.01` |
| `vus_max` |  |  |
| `iterations` | cuenta 42263 |  |
| `http_req_failed{nivel:250}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:1000}` | p95 8087.8 ms, mediana 2914.0 ms | FALLA `p(95)<500` |
| `http_req_duration` | p95 13919.3 ms, mediana 1806.6 ms |  |
| `http_req_failed` | tasa 8.42 % |  |
| `http_req_duration{nivel:2000}` | p95 14690.9 ms, mediana 13510.1 ms | FALLA `p(95)<500` |
| `checks` | tasa 91.57 % | OK `rate>0.5` |
