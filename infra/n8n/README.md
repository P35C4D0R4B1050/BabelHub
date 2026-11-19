# n8n

Aquí se versionan los flujos de n8n exportados (JSON) que reciben los avisos del E-commerce y los envían por Evolution API (AD16, `transversal-notificaciones`). **No se guardan credenciales ni números reales.**

## `flujo-de-avisos.json`

El flujo `BabelHub - Avisos por WhatsApp`:

1. **Webhook** `POST /webhook/babelhub-avisos` (solo la red interna: el puerto 5678 se publica en `127.0.0.1` y no sale por el túnel).
2. **Verificar** (NOT-12): comprueba la firma HMAC-SHA256 de `{marca}.POST./webhook/babelhub-avisos.{cuerpo}` con la clave del **sentido saliente** (`BABELHUB_FIRMA_DE_AVISOS`, la misma que el servicio tiene en `BABELHUB__Notificaciones__N8n__ClaveSaliente`), la marca de tiempo (5 minutos), la forma del cuerpo, que la plantilla exista y que cada campo esté en la lista de esa plantilla. Lo no verificable responde 401 o 400 y no envía nada: sin esta verificación el webhook sería un relé abierto de WhatsApp.
3. **Duplicados**: un `avisoId` ya entregado responde `200 {"estado":"aceptado","duplicado":true}` y vuelve a emitir el estado `entregado`; un `avisoId` cuyo envío falló **sí** se envía de nuevo. Se guarda solo el identificador (nunca el número) por 48 horas, en los datos estáticos del flujo.
4. **Armar mensaje**: las **plantillas versionadas** viven en el nodo `Verificar` (constante `PLANTILLAS`). Para cambiar un texto se edita ahí, se exporta de nuevo este archivo y **no se toca el servicio** (NOT-10); un campo nuevo exige antes agregarlo al catálogo del Dominio (`CatalogoDePlantillas`).
5. **Enviar por Evolution** con la credencial `Evolution API (apikey)` de n8n (tipo *Header Auth*, cabecera `apikey`): la llave se crea en n8n y **nunca** va en este archivo.
6. **Estado de entrega firmado**: `POST {ECOMMERCE_URL}/api/ecommerce/v1/integraciones/n8n/estados` con `X-Marca-De-Tiempo` y `X-Firma` (`sha256=` + HMAC sobre `{marca}.{cuerpo}`) con la clave del **sentido entrante** (`BABELHUB_FIRMA_DE_ESTADOS`, igual a `BABELHUB__Integraciones__n8n__Secreto` del servicio). Solo ese estado marca un aviso como enviado; el `200` de la llamada del servicio significa «aceptado».

Una verificación de CI (`FlujoDeN8nTests`) lee este archivo y falla si una plantilla usa un campo que no está en su lista ni en el catálogo del Dominio, si falta una plantilla del catálogo, si un texto lleva un enlace o un dato que no debe salir, o si hay un secreto incrustado.

## Variables de `n8n.env`

| Variable | Qué es |
| :--- | :--- |
| `BABELHUB_FIRMA_DE_AVISOS` | La clave que **verifica** la llamada del servicio. Igual a `BABELHUB__Notificaciones__N8n__ClaveSaliente`. |
| `BABELHUB_FIRMA_DE_ESTADOS` | La clave con la que n8n **firma** los estados. Igual a `BABELHUB__Integraciones__n8n__Secreto`. Distinta de la anterior (una clave por sentido). |
| `ECOMMERCE_URL` | La dirección interna del E-commerce (por ejemplo `http://ecommerce:8080`). |
| `EVOLUTION_URL` y `EVOLUTION_INSTANCIA` | La dirección interna de Evolution API y el nombre de la instancia de WhatsApp. |
| `N8N_ENCRYPTION_KEY` | La clave de cifrado de las credenciales de n8n. |

El Compose de VM-VENTAS añade `N8N_BLOCK_ENV_ACCESS_IN_NODE=false` y `NODE_FUNCTION_ALLOW_BUILTIN=crypto` (el flujo lee esas variables y usa `crypto`), purga las ejecuciones a las 168 horas (`EXECUTIONS_DATA_MAX_AGE`) y no guarda las ejecuciones correctas. Evolution API corre con `DATABASE_SAVE_*` en falso: no guarda mensajes, contactos ni chats.

## Importar y probar

1. En n8n: *Import from file* con `flujo-de-avisos.json`, crear la credencial `Evolution API (apikey)` y activar el flujo.
2. Prueba de humo desde la VM, con un aviso del entorno de ensayo: el servicio con `BABELHUB__Notificaciones__Envio=n8n` envía un aviso a un número de prueba y debe llegar el estado `entregado` firmado (el aviso queda `enviado`).
3. Apagar Evolution un momento: el aviso queda en `pendiente` con reintentos de 1, 5, 15, 30 y 60 minutos y pasa a `fallido` al agotarlos; al volver Evolution, un reintento con el mismo `avisoId` se envía y entrega.

> El flujo se escribió sin poder ejecutar n8n en el entorno de desarrollo: **el paso 2 es obligatorio antes de activar los avisos en producción**.
