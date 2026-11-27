# rampa-de-usuarios

Consulta de disponibilidad por niveles de 50 a 2000 usuarios virtuales; cada nivel con su p95 y su tasa de errores.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 341 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `vus_max` |  |  |
| `http_req_duration{nivel:50}` | p95 277.3 ms, mediana 17.3 ms | OK `p(95)<500` |
| `http_req_duration` | p95 12006.2 ms, mediana 1489.2 ms |  |
| `http_req_duration{nivel:2000}` | p95 13181.4 ms, mediana 4986.5 ms | FALLA `p(95)<500` |
| `http_reqs` | cuenta 45510 |  |
| `http_req_failed{nivel:500}` | tasa 0.01 % | OK `rate<0.01` |
| `http_req_failed{nivel:1000}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed{nivel:100}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:1000}` | p95 8711.1 ms, mediana 7596.0 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:100}` | p95 899.7 ms, mediana 396.4 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:500}` | p95 3156.6 ms, mediana 2691.9 ms | FALLA `p(95)<500` |
| `checks` | tasa 84.60 % | OK `rate>0.5` |
| `iterations` | cuenta 45468 |  |
| `http_req_failed{nivel:50}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed` | tasa 15.39 % |  |
| `http_req_duration{nivel:250}` | p95 2101.3 ms, mediana 1114.8 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:250}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed{nivel:2000}` | tasa 45.86 % | FALLA `rate<0.01` |
