# disponibilidad

RNF02 y AC-FUE-08: 50 usuarios concurrentes consultan la disponibilidad; p95 < 500 ms.

Resultado: **NO SE EJECUTÓ (falló la preparación del escenario)** — duración de la prueba: 2 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_reqs` | cuenta 9 |  |
| `http_req_failed` | tasa 11.11 % | FALLA `rate<0.001` |
| `http_req_duration` | p95 555.5 ms, mediana 127.6 ms | FALLA `p(95)<500` |
| `checks` | tasa 0.00 % | OK `rate>0.999` |
| `vus_max` |  |  |
