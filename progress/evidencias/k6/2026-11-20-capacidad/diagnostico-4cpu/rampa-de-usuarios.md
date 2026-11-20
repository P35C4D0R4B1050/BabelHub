# rampa-de-usuarios

Consulta de disponibilidad por niveles de 50 a 2000 usuarios virtuales; cada nivel con su p95 y su tasa de errores.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 56 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_req_failed{nivel:1000}` | tasa 0.00 % | OK `rate<0.01` |
| `http_req_failed` | tasa 0.00 % |  |
| `checks` | tasa 100.00 % | OK `rate>0.5` |
| `http_req_duration{nivel:1000}` | p95 3877.0 ms, mediana 3481.3 ms | FALLA `p(95)<500` |
| `vus_max` |  |  |
| `http_reqs` | cuenta 13001 |  |
| `iterations` | cuenta 12959 |  |
| `http_req_duration` | p95 3876.8 ms, mediana 3480.6 ms |  |
