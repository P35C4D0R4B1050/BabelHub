# limites-de-uso

Un usuario que excede 100 solicitudes por minuto recibe 429 con Retry-After; otro usuario no se ve afectado.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 40 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `checks{tipo:limite}` | tasa 100.00 % | OK `rate==1` |
| `http_reqs` | cuenta 181 |  |
| `http_req_duration` | p95 15.5 ms, mediana 6.6 ms |  |
| `vecino_limitadas` | cuenta 0 | OK `count==0` |
| `iterations` | cuenta 181 |  |
| `abusivo_permitidas` | cuenta 100 | OK `count<=100` |
| `http_req_failed` | tasa 100.00 % |  |
| `abusivo_limitadas` | cuenta 40 | OK `count>=30` |
| `vus_max` |  |  |
