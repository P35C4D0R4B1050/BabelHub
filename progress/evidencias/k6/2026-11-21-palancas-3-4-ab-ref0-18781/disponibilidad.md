# disponibilidad

RNF02 y AC-FUE-08: 50 usuarios concurrentes consultan la disponibilidad; p95 < 500 ms.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 63 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_reqs` | cuenta 3425 |  |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
| `vus_max` |  |  |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
| `iterations` | cuenta 3383 |  |
| `http_req_duration` | p95 1216.3 ms, mediana 863.4 ms | FALLA `p(95)<500` |
