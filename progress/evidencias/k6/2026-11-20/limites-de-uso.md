# limites-de-uso

Un usuario que excede 100 solicitudes por minuto recibe 429 con Retry-After; otro usuario no se ve afectado.

Resultado: **CUMPLE todos los umbrales** — duración de la prueba: 40 s

| Métrica | Valores | Umbrales |
| :--- | :--- | :--- |
| `iterations` | cuenta 181 |  |
| `abusivo_permitidas` | cuenta 100 | OK `count<=100` |
| `vecino_limitadas` | cuenta 0 | OK `count==0` |
| `checks{tipo:limite}` | tasa 100.00 % | OK `rate==1` |
| `vus_max` |  |  |
| `http_req_failed` | tasa 100.00 % |  |
| `abusivo_limitadas` | cuenta 40 | OK `count>=30` |
| `http_reqs` | cuenta 181 |  |
| `http_req_duration` | p95 56.8 ms, mediana 35.1 ms |  |
