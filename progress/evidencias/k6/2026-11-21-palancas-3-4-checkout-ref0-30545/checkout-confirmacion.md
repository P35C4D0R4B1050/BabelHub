# checkout-confirmacion

Tramo del Inventario de la confirmación del checkout (reservar y confirmar), 50 usuarios concurrentes; p95 < 3 s.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 64 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `checkout_reservar_y_confirmar` | p95 1559.0 ms, mediana 283.0 ms | OK `p(95)<3000` |
| `estados{estado:500}` | cuenta 0 | OK `count>=0` |
| `http_reqs` | cuenta 624 |  |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
| `estados{estado:429}` | cuenta 0 | OK `count>=0` |
| `http_req_duration` | p95 845.5 ms, mediana 115.8 ms |  |
| `estados{estado:503}` | cuenta 0 | OK `count>=0` |
| `estados{estado:0}` | cuenta 0 | OK `count>=0` |
| `vus_max` |  |  |
| `estados{estado:409}` | cuenta 0 | OK `count>=0` |
| `estados{estado:200}` | cuenta 301 | OK `count>=0` |
| `estados{estado:201}` | cuenta 301 | OK `count>=0` |
| `iterations` | cuenta 301 |  |
