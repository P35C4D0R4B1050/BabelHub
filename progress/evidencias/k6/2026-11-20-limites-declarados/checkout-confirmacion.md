# checkout-confirmacion

Tramo del Inventario de la confirmación del checkout (reservar y confirmar), 50 usuarios concurrentes; p95 < 3 s.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 63 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `checks` | tasa 99.83 % | FALLA `rate>0.999` |
| `iterations` | cuenta 301 |  |
| `http_reqs` | cuenta 623 |  |
| `estados{estado:409}` | cuenta 0 | OK `count>=0` |
| `http_req_duration` | p95 614.0 ms, mediana 162.8 ms |  |
| `estados{estado:503}` | cuenta 0 | OK `count>=0` |
| `checkout_reservar_y_confirmar` | p95 1160.0 ms, mediana 400.0 ms | OK `p(95)<3000` |
| `estados{estado:201}` | cuenta 300 | OK `count>=0` |
| `estados{estado:429}` | cuenta 1 | OK `count>=0` |
| `estados{estado:200}` | cuenta 300 | OK `count>=0` |
| `estados{estado:0}` | cuenta 0 | OK `count>=0` |
| `http_req_failed` | tasa 0.16 % | FALLA `rate<0.001` |
| `vus_max` |  |  |
| `estados{estado:500}` | cuenta 0 | OK `count>=0` |
