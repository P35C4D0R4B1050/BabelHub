# disponibilidad

RNF02 y AC-FUE-08: 50 usuarios concurrentes consultan la disponibilidad; p95 < 500 ms.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 66 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
| `iterations` | cuenta 1227 |  |
| `vus_max` |  |  |
| `http_req_duration` | p95 2609.6 ms, mediana 1902.3 ms | FALLA `p(95)<500` |
| `http_reqs` | cuenta 1269 |  |
