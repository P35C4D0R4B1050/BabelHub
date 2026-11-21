# limites-de-uso

Un usuario que excede 100 solicitudes por minuto recibe 429 con Retry-After; otro usuario no se ve afectado.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 40 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `vecino_limitadas` | cuenta 0 | OK `count==0` |
| `vus_max` |  |  |
| `http_req_duration` | p95 16.5 ms, mediana 7.3 ms |  |
| `iterations` | cuenta 180 |  |
| `http_req_failed` | tasa 100.00 % |  |
| `abusivo_permitidas` | cuenta 100 | OK `count<=100` |
| `abusivo_limitadas` | cuenta 40 | OK `count>=30` |
| `http_reqs` | cuenta 180 |  |
| `checks{tipo:limite}` | tasa 100.00 % | OK `rate==1` |
