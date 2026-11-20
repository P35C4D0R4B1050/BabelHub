# rampa-de-usuarios

Consulta de disponibilidad por niveles de 50 a 2000 usuarios virtuales; cada nivel con su p95 y su tasa de errores.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 338 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_req_failed` | tasa 0.00 % |  |
| `http_req_duration{nivel:100}` | p95 679.1 ms, mediana 60.5 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:50}` | p95 242.4 ms, mediana 27.5 ms | OK `p(95)<500` |
| `http_req_duration{nivel:1000}` | p95 7285.2 ms, mediana 5293.2 ms | FALLA `p(95)<500` |
| `http_reqs` | cuenta 44237 |  |
| `http_req_failed{nivel:1000}` | tasa 0.00 % | OK `rate<0.01` |
| `iterations` | cuenta 44195 |  |
| `checks` | tasa 100.00 % | OK `rate>0.5` |
| `http_req_failed{nivel:2000}` | tasa 0.00 % | OK `rate<0.01` |
| `vus_max` |  |  |
| `http_req_failed{nivel:250}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:2000}` | p95 13308.6 ms, mediana 11260.7 ms | FALLA `p(95)<500` |
| `http_req_duration` | p95 12368.7 ms, mediana 2154.8 ms |  |
| `http_req_failed{nivel:50}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed{nivel:500}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_duration{nivel:500}` | p95 5192.7 ms, mediana 2494.3 ms | FALLA `p(95)<500` |
| `http_req_duration{nivel:250}` | p95 1405.0 ms, mediana 911.3 ms | FALLA `p(95)<500` |
| `http_req_failed{nivel:100}` | tasa 0.00 % | OK `rate<0.01` |
