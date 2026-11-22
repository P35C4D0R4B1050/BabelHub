# disponibilidad

RNF02 y AC-FUE-08: 50 usuarios concurrentes consultan la disponibilidad; p95 < 500 ms.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 92 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
| `http_reqs` | cuenta 8415 |  |
| `iterations` | cuenta 8373 |  |
| `vus_max` |  |  |
| `http_req_duration` | p95 97.6 ms, mediana 16.6 ms | OK `p(95)<500` |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
