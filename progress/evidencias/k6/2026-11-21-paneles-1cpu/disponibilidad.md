# disponibilidad

RNF02 y AC-FUE-08: 50 usuarios concurrentes consultan la disponibilidad; p95 < 500 ms.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 62 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `iterations` | cuenta 5312 |  |
| `vus_max` |  |  |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
| `http_reqs` | cuenta 5354 |  |
| `http_req_duration` | p95 206.6 ms, mediana 43.5 ms | OK `p(95)<500` |
