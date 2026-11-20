# resiliencia

PostgreSQL pausado entre los 20 y los 45 s: el Inventario sigue vivo, responde 503 uniforme y se recupera solo.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 81 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `estados{estado:500}` | cuenta 0 | OK `count>=0` |
| `liveness_fallida` | cuenta 0 | OK `count==0` |
| `checks{fase:posterior}` | tasa 100.00 % | OK `rate>0.99` |
| `estados{estado:201}` | cuenta 0 | OK `count>=0` |
| `estados{estado:429}` | cuenta 0 | OK `count>=0` |
| `estados{estado:0}` | cuenta 0 | OK `count>=0` |
| `respuestas_inesperadas` | cuenta 0 | OK `count==0` |
| `estados{estado:409}` | cuenta 0 | OK `count>=0` |
| `ready_200_durante_la_falla` | cuenta 0 | OK `count>=0` |
| `checks{fase:previa}` | tasa 100.00 % | OK `rate>0.999` |
| `http_req_duration` | p95 703.3 ms, mediana 488.4 ms |  |
| `http_req_failed` | tasa 0.88 % |  |
| `http_reqs` | cuenta 2510 |  |
| `vus_max` |  |  |
| `iterations` | cuenta 2443 |  |
| `estados{estado:503}` | cuenta 14 | OK `count>=0` |
| `estados{estado:200}` | cuenta 2374 | OK `count>=0` |
