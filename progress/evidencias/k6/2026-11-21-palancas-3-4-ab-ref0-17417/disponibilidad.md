# disponibilidad

RNF02 y AC-FUE-08: 50 usuarios concurrentes consultan la disponibilidad; p95 < 500 ms.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 63 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_reqs` | cuenta 2772 |  |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
| `vus_max` |  |  |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
| `http_req_duration` | p95 1400.9 ms, mediana 1097.5 ms | FALLA `p(95)<500` |
| `iterations` | cuenta 2730 |  |
