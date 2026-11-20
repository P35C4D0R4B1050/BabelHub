# rampa-de-usuarios

Consulta de disponibilidad por niveles de 50 a 2000 usuarios virtuales; cada nivel con su p95 y su tasa de errores.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 334 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_req_duration{nivel:250}` | p95 1286.4 ms, mediana 811.9 ms | FALLA `p(95)<500` |
| `vus_max` |  |  |
| `http_req_failed{nivel:250}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed{nivel:500}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed{nivel:1000}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed{nivel:2000}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:50}` | p95 50.5 ms, mediana 25.2 ms | OK `p(95)<500` |
| `http_req_duration{nivel:1000}` | p95 4990.9 ms, mediana 2828.8 ms | FALLA `p(95)<500` |
| `iterations` | cuenta 63990 |  |
| `http_req_failed{nivel:50}` | tasa 0.00 % | OK `rate<0.01` |
| `http_reqs` | cuenta 64032 |  |
| `http_req_failed` | tasa 0.00 % |  |
| `checks` | tasa 100.00 % | OK `rate>0.5` |
| `http_req_duration{nivel:100}` | p95 133.2 ms, mediana 28.5 ms | OK `p(95)<500` |
| `http_req_duration{nivel:2000}` | p95 7155.8 ms, mediana 6595.8 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:500}` | p95 2424.0 ms, mediana 1420.8 ms | FALLA `p(95)<500` |
| `http_req_duration` | p95 6958.1 ms, mediana 1434.6 ms |  |
| `http_req_failed{nivel:100}` | tasa 0.00 % | OK `rate<0.01` |
