# rampa-de-usuarios

Consulta de disponibilidad por niveles de 50 a 2000 usuarios virtuales; cada nivel con su p95 y su tasa de errores.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 337 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_req_duration{nivel:500}` | p95 2217.0 ms, mediana 1761.9 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:1000}` | tasa 0.00 % | OK `rate<0.01` |
| `iterations` | cuenta 63304 |  |
| `http_req_failed` | tasa 0.00 % |  |
| `http_req_duration{nivel:250}` | p95 936.7 ms, mediana 697.1 ms | FALLA `p(95)<500` |
| `vus_max` |  |  |
| `http_req_duration{nivel:50}` | p95 34.6 ms, mediana 21.2 ms | OK `p(95)<500` |
| `http_req_duration{nivel:2000}` | p95 10117.0 ms, mediana 8834.2 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:100}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed{nivel:250}` | tasa 0.00 % | OK `rate<0.01` |
| `http_reqs` | cuenta 63346 |  |
| `checks` | tasa 100.00 % | OK `rate>0.5` |
| `http_req_failed{nivel:50}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:1000}` | p95 5006.6 ms, mediana 3621.9 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:500}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:100}` | p95 47.3 ms, mediana 22.5 ms | OK `p(95)<500` |
| `http_req_duration` | p95 9678.7 ms, mediana 970.0 ms |  |
| `http_req_failed{nivel:2000}` | tasa 0.00 % | OK `rate<0.01` |
