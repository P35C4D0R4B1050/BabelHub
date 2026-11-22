# checkout-confirmacion

Tramo del Inventario de la confirmación del checkout (reservar y confirmar), 50 usuarios concurrentes; p95 < 3 s.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 947 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `estados{estado:429}` | cuenta 0 | OK `count>=0` |
| `checks` | tasa 65.02 % | FALLA `rate>0.999` |
| `estados{estado:409}` | cuenta 0 | OK `count>=0` |
| `estados{estado:200}` | cuenta 145 | OK `count>=0` |
| `estados{estado:201}` | cuenta 145 | OK `count>=0` |
| `estados{estado:500}` | cuenta 0 | OK `count>=0` |
| `estados{estado:503}` | cuenta 0 | OK `count>=0` |
| `estados{estado:0}` | cuenta 0 | OK `count>=0` |
| `http_req_failed` | tasa 33.33 % | FALLA `rate<0.001` |
| `iterations` | cuenta 301 |  |
| `checkout_reservar_y_confirmar` | p95 638.0 ms, mediana 79.0 ms | OK `p(95)<3000` |
| `http_req_duration` | p95 426.2 ms, mediana 64.3 ms |  |
| `http_reqs` | cuenta 468 |  |
| `vus_max` |  |  |
