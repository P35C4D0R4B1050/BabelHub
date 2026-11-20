# resiliencia

PostgreSQL pausado entre los 20 y los 45 s: el Inventario sigue vivo, responde 503 uniforme y se recupera solo.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 82 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `iterations` | cuenta 3664 |  |
| `estados{estado:409}` | cuenta 0 | OK `count>=0` |
| `estados{estado:429}` | cuenta 0 | OK `count>=0` |
| `vus_max` |  |  |
| `estados{estado:0}` | cuenta 0 | OK `count>=0` |
| `respuestas_inesperadas` | cuenta 0 | OK `count==0` |
| `http_req_failed` | tasa 0.37 % |  |
| `http_req_duration` | p95 498.7 ms, mediana 200.6 ms |  |
| `estados{estado:200}` | cuenta 3571 | OK `count>=0` |
| `checks{fase:previa}` | tasa 100.00 % | OK `rate>0.999` |
| `estados{estado:201}` | cuenta 0 | OK `count>=0` |
| `http_reqs` | cuenta 3755 |  |
| `liveness_fallida` | cuenta 0 | OK `count==0` |
| `ready_200_durante_la_falla` | cuenta 15 | OK `count>=0` |
| `estados{estado:503}` | cuenta 14 | OK `count>=0` |
| `estados{estado:500}` | cuenta 0 | OK `count>=0` |
| `checks{fase:posterior}` | tasa 100.00 % | OK `rate>0.99` |
