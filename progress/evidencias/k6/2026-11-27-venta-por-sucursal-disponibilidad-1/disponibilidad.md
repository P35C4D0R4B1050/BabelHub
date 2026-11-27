# disponibilidad

RNF02 y AC-FUE-08: 50 usuarios concurrentes consultan la disponibilidad; p95 < 500 ms.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 61 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
| `http_reqs` | cuenta 5256 |  |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
| `http_req_duration` | p95 235.2 ms, mediana 48.9 ms | OK `p(95)<500` |
| `vus_max` |  |  |
| `iterations` | cuenta 5214 |  |
