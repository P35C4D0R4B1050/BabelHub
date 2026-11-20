# disponibilidad

RNF02 y AC-FUE-08: 50 usuarios concurrentes consultan la disponibilidad; p95 < 500 ms.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 67 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `iterations` | cuenta 2338 |  |
| `http_reqs` | cuenta 2380 |  |
| `vus_max` |  |  |
| `http_req_duration` | p95 1774.1 ms, mediana 671.2 ms | FALLA `p(95)<500` |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
