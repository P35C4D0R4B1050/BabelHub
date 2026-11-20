# rampa-de-usuarios

Consulta de disponibilidad por niveles de 50 a 2000 usuarios virtuales; cada nivel con su p95 y su tasa de errores.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 346 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_req_duration{nivel:50}` | p95 308.1 ms, mediana 32.6 ms | OK `p(95)<500` |
| `http_req_failed{nivel:50}` | tasa 0.00 % | OK `rate<0.01` |
| `vus_max` |  |  |
| `http_req_duration{nivel:1000}` | p95 13580.6 ms, mediana 9804.7 ms | FALLA `p(95)<500` |
| `checks` | tasa 95.48 % | OK `rate>0.5` |
| `http_req_duration{nivel:100}` | p95 980.8 ms, mediana 92.7 ms | FALLA `p(95)<500` |
| `http_req_duration` | p95 15265.5 ms, mediana 3131.9 ms |  |
| `http_req_failed{nivel:100}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed{nivel:2000}` | tasa 18.31 % | FALLA `rate<0.01` |
| `iterations` | cuenta 31786 |  |
| `http_req_duration{nivel:2000}` | p95 15488.6 ms, mediana 14981.1 ms | FALLA `p(95)<500` |
| `http_reqs` | cuenta 31828 |  |
| `http_req_failed{nivel:1000}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed` | tasa 4.51 % |  |
| `http_req_failed{nivel:250}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:250}` | p95 3293.5 ms, mediana 2592.0 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:500}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:500}` | p95 6387.5 ms, mediana 5589.5 ms | FALLA `p(95)<500` |
