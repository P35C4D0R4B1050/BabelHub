# rampa-de-usuarios

Consulta de disponibilidad por niveles de 50 a 2000 usuarios virtuales; cada nivel con su p95 y su tasa de errores.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 343 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_reqs` | cuenta 24735 |  |
| `http_req_duration{nivel:50}` | p95 433.7 ms, mediana 98.7 ms | OK `p(95)<500` |
| `http_req_failed{nivel:100}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed{nivel:500}` | tasa 0.00 % | OK `rate<0.01` |
| `vus_max` |  |  |
| `http_req_failed{nivel:1000}` | tasa 19.18 % | FALLA `rate<0.01` |
| `http_req_duration{nivel:500}` | p95 8098.3 ms, mediana 6693.9 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:2000}` | tasa 51.75 % | FALLA `rate<0.01` |
| `http_req_duration{nivel:250}` | p95 7215.5 ms, mediana 2993.6 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:2000}` | p95 16835.4 ms, mediana 15112.5 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:1000}` | p95 16100.1 ms, mediana 14448.0 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:100}` | p95 1095.1 ms, mediana 694.6 ms | FALLA `p(95)<500` |
| `iterations` | cuenta 24693 |  |
| `checks` | tasa 82.89 % | OK `rate>0.5` |
| `http_req_failed` | tasa 17.09 % |  |
| `http_req_duration` | p95 16029.6 ms, mediana 6589.9 ms |  |
| `http_req_failed{nivel:250}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed{nivel:50}` | tasa 0.00 % | OK `rate<0.01` |
