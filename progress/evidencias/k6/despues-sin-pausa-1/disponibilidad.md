# disponibilidad

RNF02 y AC-FUE-08: 50 usuarios concurrentes consultan la disponibilidad; p95 < 500 ms.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 65 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_req_duration` | p95 1504.3 ms, mediana 986.8 ms | FALLA `p(95)<500` |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
| `vus_max` |  |  |
| `iterations` | cuenta 2892 |  |
| `http_reqs` | cuenta 2934 |  |
