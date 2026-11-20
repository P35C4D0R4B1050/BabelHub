# rampa-de-usuarios

Consulta de disponibilidad por niveles de 50 a 2000 usuarios virtuales; cada nivel con su p95 y su tasa de errores.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 344 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_reqs` | cuenta 19677 |  |
| `http_req_duration{nivel:250}` | p95 4102.7 ms, mediana 3287.4 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:1000}` | tasa 33.77 % | FALLA `rate<0.01` |
| `vus_max` |  |  |
| `http_req_duration{nivel:1000}` | p95 20493.8 ms, mediana 15607.8 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:500}` | p95 10102.1 ms, mediana 8298.5 ms | FALLA `p(95)<500` |
| `http_req_duration` | p95 21980.1 ms, mediana 8500.6 ms |  |
| `http_req_duration{nivel:2000}` | p95 23406.2 ms, mediana 16180.3 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:50}` | p95 1286.2 ms, mediana 507.2 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:2000}` | tasa 44.41 % | FALLA `rate<0.01` |
| `http_req_failed{nivel:250}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed{nivel:500}` | tasa 1.47 % | FALLA `rate<0.01` |
| `http_req_failed` | tasa 18.44 % |  |
| `http_req_failed{nivel:50}` | tasa 0.00 % | OK `rate<0.01` |
| `checks` | tasa 81.52 % | OK `rate>0.5` |
| `http_req_failed{nivel:100}` | tasa 0.00 % | OK `rate<0.01` |
| `iterations` | cuenta 19635 |  |
| `http_req_duration{nivel:100}` | p95 1909.7 ms, mediana 1306.8 ms | FALLA `p(95)<500` |
