# disponibilidad

RNF02 y AC-FUE-08: 50 usuarios concurrentes consultan la disponibilidad; p95 < 500 ms.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 63 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
| `http_req_duration` | p95 1244.3 ms, mediana 899.7 ms | FALLA `p(95)<500` |
| `vus_max` |  |  |
| `iterations` | cuenta 3274 |  |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
| `http_reqs` | cuenta 3316 |  |
