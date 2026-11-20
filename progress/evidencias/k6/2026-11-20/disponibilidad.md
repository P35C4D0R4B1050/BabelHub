# disponibilidad

RNF02 y AC-FUE-08: 50 usuarios concurrentes consultan la disponibilidad; p95 < 500 ms.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 63 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `iterations` | cuenta 4501 |  |
| `http_req_duration` | p95 326.9 ms, mediana 124.3 ms | OK `p(95)<500` |
| `vus_max` |  |  |
| `http_reqs` | cuenta 4543 |  |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
