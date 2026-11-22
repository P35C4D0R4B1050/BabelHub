# checkout-confirmacion

Tramo del Inventario de la confirmación del checkout (reservar y confirmar), 50 usuarios concurrentes; p95 < 3 s.

Resultado: **NO cumple algún umbral (ver tabla)** — duración de la prueba: 66 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `iterations` | cuenta 300 |  |
| `checks` | tasa 100.00 % | OK `rate>0.999` |
| `estados{estado:500}` | cuenta 0 | OK `count>=0` |
| `http_req_failed` | tasa 0.00 % | OK `rate<0.001` |
| `http_req_duration` | p95 3485.3 ms, mediana 891.9 ms |  |
| `vus_max` |  |  |
| `checkout_reservar_y_confirmar` | p95 5184.1 ms, mediana 2465.0 ms | FALLA `p(95)<3000` |
| `estados{estado:0}` | cuenta 0 | OK `count>=0` |
| `estados{estado:503}` | cuenta 0 | OK `count>=0` |
| `estados{estado:429}` | cuenta 0 | OK `count>=0` |
| `estados{estado:200}` | cuenta 300 | OK `count>=0` |
| `estados{estado:201}` | cuenta 300 | OK `count>=0` |
| `estados{estado:409}` | cuenta 0 | OK `count>=0` |
| `http_reqs` | cuenta 622 |  |
