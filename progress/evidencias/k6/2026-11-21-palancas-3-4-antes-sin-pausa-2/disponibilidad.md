# disponibilidad

RNF02 y AC-FUE-08: 50 usuarios concurrentes consultan la disponibilidad; p95 < 500 ms.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 62 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_req_duration` | p95 1092.2 ms, mediana 574.4 ms | FALLA `p(95)<500` |
| `http_reqs` | cuenta 4827 |  |
| `vus_max` |  |  |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
| `iterations` | cuenta 4785 |  |
