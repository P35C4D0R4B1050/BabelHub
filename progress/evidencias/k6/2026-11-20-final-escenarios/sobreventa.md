# sobreventa

AC01: 100 usuarios compiten por 10 unidades; nunca más de 10 reservas, vendible final no negativo y vendible final + reservas = 10 (sin sobreventa ni stock perdido).

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 10 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `rechazos_por_capacidad` | cuenta 0 | OK `count>=0` |
| `respuestas_inesperadas` | cuenta 0 | OK `count==0` |
| `reservas_sin_stock` | cuenta 90 | OK `count>=0` |
| `vus_max` |  |  |
| `estados{estado:500}` | cuenta 0 | OK `count>=0` |
| `estados{estado:201}` | cuenta 10 | OK `count>=0` |
| `http_req_duration` | p95 9127.1 ms, mediana 5159.7 ms |  |
| `estados{estado:200}` | cuenta 1 | OK `count>=0` |
| `http_req_failed` | tasa 85.71 % |  |
| `estados{estado:409}` | cuenta 90 | OK `count>=0` |
| `reservas_exitosas` | cuenta 10 | OK `count<=10` |
| `checks` | tasa 100.00 % | OK `rate==1` |
| `estados{estado:503}` | cuenta 0 | OK `count>=0` |
| `vendible_final` | p95 0.0 ms, mediana 0.0 ms | OK `min>=0`<br>OK `max<=10` |
| `http_reqs` | cuenta 105 |  |
| `iterations` | cuenta 100 |  |
| `estados{estado:429}` | cuenta 0 | OK `count>=0` |
| `estados{estado:0}` | cuenta 0 | OK `count>=0` |
