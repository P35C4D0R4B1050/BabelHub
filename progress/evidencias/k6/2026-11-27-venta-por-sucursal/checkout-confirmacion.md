# checkout-confirmacion

Tramo del Inventario de la confirmación del checkout (reservar y confirmar), 50 usuarios concurrentes; p95 < 3 s.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 61 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `http_reqs` | cuenta 622 |  |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
| `checkout_reservar_y_confirmar` | p95 171.1 ms, mediana 61.0 ms | OK `p(95)<3000` |
| `vus_max` |  |  |
| `estados{estado:503}` | cuenta 0 | OK `count>=0` |
| `iterations` | cuenta 300 |  |
| `http_req_duration` | p95 89.6 ms, mediana 30.7 ms |  |
| `estados{estado:429}` | cuenta 0 | OK `count>=0` |
| `estados{estado:0}` | cuenta 0 | OK `count>=0` |
| `estados{estado:200}` | cuenta 300 | OK `count>=0` |
| `estados{estado:500}` | cuenta 0 | OK `count>=0` |
| `estados{estado:201}` | cuenta 300 | OK `count>=0` |
| `estados{estado:409}` | cuenta 0 | OK `count>=0` |
