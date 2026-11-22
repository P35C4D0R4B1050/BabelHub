# disponibilidad

RNF02 y AC-FUE-08: 50 usuarios concurrentes consultan la disponibilidad; p95 < 500 ms.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 63 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_reqs` | cuenta 4615 |  |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
| `http_req_duration` | p95 891.8 ms, mediana 615.9 ms | FALLA `p(95)<500` |
| `iterations` | cuenta 4573 |  |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
| `vus_max` |  |  |
