# disponibilidad

RNF02 y AC-FUE-08: 50 usuarios concurrentes consultan la disponibilidad; p95 < 500 ms.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 62 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `iterations` | cuenta 2723 |  |
| `http_reqs` | cuenta 2765 |  |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
| `vus_max` |  |  |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
| `http_req_duration` | p95 896.9 ms, mediana 598.1 ms | FALLA `p(95)<500` |
