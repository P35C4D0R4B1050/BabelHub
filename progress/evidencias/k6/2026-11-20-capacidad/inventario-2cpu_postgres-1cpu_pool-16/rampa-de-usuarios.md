# rampa-de-usuarios

Consulta de disponibilidad por niveles de 50 a 2000 usuarios virtuales; cada nivel con su p95 y su tasa de errores.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 342 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_req_failed{nivel:100}` | tasa 0.00 % | OK `rate<0.01` |
| `iterations` | cuenta 37301 |  |
| `http_req_failed{nivel:500}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:100}` | p95 403.1 ms, mediana 106.7 ms | OK `p(95)<500` |
| `http_req_duration` | p95 15084.6 ms, mediana 1301.5 ms |  |
| `vus_max` |  |  |
| `http_req_failed{nivel:50}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:250}` | p95 2090.2 ms, mediana 1089.8 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:2000}` | p95 15205.1 ms, mediana 14997.8 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:1000}` | tasa 0.00 % | OK `rate<0.01` |
| `http_reqs` | cuenta 37343 |  |
| `checks` | tasa 94.26 % | OK `rate>0.5` |
| `http_req_failed{nivel:250}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed{nivel:2000}` | tasa 27.18 % | FALLA `rate<0.01` |
| `http_req_duration{nivel:500}` | p95 7570.8 ms, mediana 4258.2 ms | FALLA `p(95)<500` |
| `http_req_failed` | tasa 5.73 % |  |
| `http_req_duration{nivel:50}` | p95 146.2 ms, mediana 29.5 ms | OK `p(95)<500` |
| `http_req_duration{nivel:1000}` | p95 13197.2 ms, mediana 9303.1 ms | FALLA `p(95)<500` |
