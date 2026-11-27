# sobreventa-por-sucursal

AC-SEL-47: 100 clientes compiten por 20 unidades de una edición en una sucursal, con una a tres líneas cada uno y otras 20 en otra sucursal; nunca se aparta más que el vendible de la sucursal, la otra no se toca y apartadas + vendible final = 20.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 7 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `estados{estado:200}` | cuenta 0 | OK `count>=0` |
| `vendible_final_de_la_sucursal` | p95 0.0 ms, mediana 0.0 ms | OK `max<=20`<br>OK `min>=0` |
| `vendible_final_de_la_otra` | p95 20.0 ms, mediana 20.0 ms | OK `min>=20`<br>OK `max<=20` |
| `reservas_exitosas` | cuenta 13 | OK `count>=0` |
| `estados{estado:503}` | cuenta 0 | OK `count>=0` |
| `estados{estado:201}` | cuenta 13 | OK `count>=0` |
| `http_req_duration` | p95 5951.9 ms, mediana 4090.4 ms |  |
| `rechazos_por_capacidad` | cuenta 0 | OK `count>=0` |
| `estados{estado:500}` | cuenta 0 | OK `count>=0` |
| `http_req_failed` | tasa 80.56 % |  |
| `iterations` | cuenta 100 |  |
| `respuestas_inesperadas` | cuenta 0 | OK `count==0` |
| `vus_max` |  |  |
| `reservas_sin_stock` | cuenta 87 | OK `count>=0` |
| `estados{estado:429}` | cuenta 0 | OK `count>=0` |
| `unidades_apartadas` | cuenta 20 | OK `count<=20` |
| `http_reqs` | cuenta 108 |  |
| `estados{estado:0}` | cuenta 0 | OK `count>=0` |
| `estados{estado:409}` | cuenta 87 | OK `count>=0` |
| `checks` | tasa 100.00 % | OK `rate==1` |
