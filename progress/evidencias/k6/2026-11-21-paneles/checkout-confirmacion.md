# checkout-confirmacion

Tramo del Inventario de la confirmación del checkout (reservar y confirmar), 50 usuarios concurrentes; p95 < 3 s.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 61 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_req_duration` | p95 42.6 ms, mediana 22.8 ms |  |
| `vus_max` |  |  |
| `http_reqs` | cuenta 624 |  |
| `estados{estado:409}` | cuenta 0 | OK `count>=0` |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
| `checkout_reservar_y_confirmar` | p95 100.0 ms, mediana 42.0 ms | OK `p(95)<3000` |
| `iterations` | cuenta 301 |  |
| `estados{estado:201}` | cuenta 301 | OK `count>=0` |
| `estados{estado:429}` | cuenta 0 | OK `count>=0` |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
| `estados{estado:500}` | cuenta 0 | OK `count>=0` |
| `estados{estado:503}` | cuenta 0 | OK `count>=0` |
| `estados{estado:0}` | cuenta 0 | OK `count>=0` |
| `estados{estado:200}` | cuenta 301 | OK `count>=0` |
