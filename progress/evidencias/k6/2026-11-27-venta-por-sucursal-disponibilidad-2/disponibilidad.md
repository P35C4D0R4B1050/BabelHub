# disponibilidad

RNF02 y AC-FUE-08: 50 usuarios concurrentes consultan la disponibilidad; p95 < 500 ms.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 66 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
| `vus_max` |  |  |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
| `iterations` | cuenta 5261 |  |
| `http_reqs` | cuenta 5303 |  |
| `http_req_duration` | p95 211.3 ms, mediana 50.1 ms | OK `p(95)<500` |
