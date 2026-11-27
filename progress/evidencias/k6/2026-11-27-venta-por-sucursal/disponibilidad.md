# disponibilidad

RNF02 y AC-FUE-08: 50 usuarios concurrentes consultan la disponibilidad; p95 < 500 ms.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 62 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
| `vus_max` |  |  |
| `http_reqs` | cuenta 5415 |  |
| `iterations` | cuenta 5373 |  |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
| `http_req_duration` | p95 194.9 ms, mediana 37.8 ms | OK `p(95)<500` |
