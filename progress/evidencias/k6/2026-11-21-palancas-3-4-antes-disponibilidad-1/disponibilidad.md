# disponibilidad

RNF02 y AC-FUE-08: 50 usuarios concurrentes consultan la disponibilidad; p95 < 500 ms.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 93 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_reqs` | cuenta 8500 |  |
| `http_req_duration` | p95 104.9 ms, mediana 17.1 ms | OK `p(95)<500` |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
| `vus_max` |  |  |
| `iterations` | cuenta 8458 |  |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
