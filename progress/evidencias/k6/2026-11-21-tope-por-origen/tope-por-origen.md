# tope-por-origen

SEL-19, AC-SEL-19: 10 orígenes con 12 cuentas cada uno reservan a la vez; exactamente 10 se aceptan por origen y el resto recibe 429 INV-LIMITE-EXCEDIDO con Retry-After; un intento posterior del origen lleno sigue rechazado y un origen nuevo reserva con normalidad.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 41 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `estados{estado:0}` | cuenta 0 | OK `count>=0` |
| `estados{estado:200}` | cuenta 0 | OK `count>=0` |
| `reservas_aceptadas` | cuenta 100 | OK `count==100` |
| `fase2_limitadas` | cuenta 10 | OK `count==10` |
| `reservas_aceptadas{origen:o8}` | cuenta 10 | OK `count==10` |
| `reservas_aceptadas{origen:o5}` | cuenta 10 | OK `count==10` |
| `estados{estado:503}` | cuenta 0 | OK `count>=0` |
| `http_req_failed` | tasa 20.98 % |  |
| `reservas_aceptadas{origen:o4}` | cuenta 10 | OK `count==10` |
| `reservas_aceptadas{origen:o6}` | cuenta 10 | OK `count==10` |
| `estados{estado:409}` | cuenta 0 | OK `count>=0` |
| `vus_max` |  |  |
| `reservas_aceptadas{origen:o2}` | cuenta 10 | OK `count==10` |
| `respuestas_inesperadas` | cuenta 0 | OK `count==0` |
| `reservas_aceptadas{origen:o1}` | cuenta 10 | OK `count==10` |
| `reservas_aceptadas{origen:o3}` | cuenta 10 | OK `count==10` |
| `http_reqs` | cuenta 143 |  |
| `iterations` | cuenta 131 |  |
| `reservas_aceptadas{origen:o9}` | cuenta 10 | OK `count==10` |
| `reservas_aceptadas{origen:o0}` | cuenta 10 | OK `count==10` |
| `estados{estado:429}` | cuenta 30 | OK `count>=0` |
| `reservas_aceptadas{origen:o7}` | cuenta 10 | OK `count==10` |
| `reservas_limitadas` | cuenta 20 | OK `count==20` |
| `checks` | tasa 100.00 % | OK `rate==1` |
| `estados{estado:500}` | cuenta 0 | OK `count==0` |
| `http_req_duration` | p95 10020.0 ms, mediana 3007.4 ms |  |
| `estados{estado:201}` | cuenta 101 | OK `count>=0` |
| `fase2_origen_nuevo_aceptadas` | cuenta 1 | OK `count==1` |
