# Redis dedicado de contadores de límites

Instancia aparte del Redis de caché y de Evolution (`allkeys-lru`), para los contadores de `transversal-seguridad`: nunca expulsa una clave en silencio (`noeviction`), persiste con AOF cada segundo y solo permite al usuario `limites` ejecutar los scripts del motor sobre las claves `lim:*`, `blq:*`, `idem:*`, `rech:*` y `rsv:*` (el usuario por defecto está desactivado).

| Archivo | Uso |
| :--- | :--- |
| `redis.conf` | Memoria de 100 MB, `noeviction`, AOF `everysec`, sin instantáneas. |
| `users.acl.plantilla` | Reglas ACL; las pruebas de integración arrancan Redis con esta misma plantilla. Un archivo ACL no admite comentarios. |
| `redis.tls.conf` | Despliegue: solo TLS en el puerto 6380 con certificados montados como secretos (`REDIS_TLS=1`). |
| `iniciar.sh` | Genera el ACL con `BABELHUB__Seguridad__ClaveDeRedisDeLimites` (32 caracteres o más) y arranca Redis. |
