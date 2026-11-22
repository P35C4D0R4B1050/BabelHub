# disponibilidad

RNF02 y AC-FUE-08: 50 usuarios concurrentes consultan la disponibilidad; p95 < 500 ms.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 62 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `iterations` | cuenta 6125 |  |
| `http_reqs` | cuenta 6167 |  |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
| `vus_max` |  |  |
| `http_req_duration` | p95 706.8 ms, mediana 490.1 ms | FALLA `p(95)<500` |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
