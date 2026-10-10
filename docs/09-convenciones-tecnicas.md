# 09 — Convenciones técnicas

Reglas transversales que todos los servicios y la aplicación web siguen. Complementan la regla de dependencia de `07-enfoque-arquitectonico.md`. Lo marcado como **propuesta** se confirma en la spec que lo implementa.

## 1. Git

**Mensajes de commit:** `tipo: descripción` en inglés, en imperativo y en minúsculas, sin punto final. Commits pequeños: cada uno hace una sola cosa y deja el proyecto compilando.

| Tipo | Para qué sirve | Ejemplo |
| :--- | :--- | :--- |
| `feat` | Nueva funcionalidad | `feat: add stock reservation use case` |
| `fix` | Corrección de error | `fix: reject reservation above sellable stock` |
| `chore` | Mantenimiento y configuración | `chore: add project boilerplate` |
| `docs` | Documentación | `docs: update README` |
| `test` | Pruebas | `test: add concurrency tests for reservations` |
| `refactor` | Mejorar el código sin cambiar su comportamiento | `refactor: simplify source selection` |

Los commits de un agente terminan con la línea `Co-Authored-By` correspondiente.

**Ramas:** por ahora se trabaja en `main` con commits pequeños. Al empezar a implementar se evaluará una rama por spec (`spec/<nombre>`) con fusión a `main`.

**Qué no entra al repositorio:** secretos y archivos `.env`, binarios (`bin/`, `obj/`), CSS generado, archivos digitales de libros y datos personales. Se versionan `.env.example`, `.gitignore` y `.gitattributes` (`eol=lf`).

## 2. Idioma y nombres

| Elemento | Convención |
| :--- | :--- |
| Documentación, interfaz y mensajes al usuario | Español (es-PE). |
| Dominio y casos de uso | Español, con los términos de `docs/glossary.md` (`ReservarStock`, `Fuente`, `Traslado`). |
| Elementos técnicos | Sufijos estándar en inglés (`Controller`, `Options`, `Handler`). |
| Proyectos | `BabelHub.<Servicio>.<Capa>`. |
| Tablas y columnas | `snake_case`, en español. |

## 3. API REST

| Aspecto | Convención |
| :--- | :--- |
| Rutas | `/api/<servicio>/v1/<recurso>` en plural y minúsculas. El versionado va en la ruta. |
| Formato | JSON en `camelCase`. Fechas en ISO 8601 UTC. Identificadores UUID. Dinero como `decimal` con moneda `PEN`. |
| Errores | `application/problem+json` (RFC 9457) con `type`, `title`, `status`, `detail`, `code` estable y `traceId`. Nunca se devuelve una traza ni datos internos. |
| Paginación | `page` y `pageSize` (máximo 50); la respuesta incluye el total. |
| Idempotencia | Cabecera `Idempotency-Key` en las operaciones que cobran o reservan. Repetir la solicitud devuelve el mismo resultado. |
| Correlación | Cabecera `X-Correlation-Id`: se genera si falta, se propaga entre servicios y aparece en cada log. |
| Contratos | Los DTOs entre servicios viven en `BabelHub.Shared`. Un cambio incompatible exige una nueva versión de ruta. |

| Código HTTP | Uso |
| :--- | :--- |
| 400 | Solicitud mal formada o con datos inválidos. |
| 401 | Sin autenticar o token inválido. |
| 403 | Autenticado, pero fuera de su rol o sucursal (se registra, RNF05). |
| 404 | El recurso no existe, o no es visible para el usuario. |
| 409 | Conflicto: sin stock vendible, concurrencia, ISBN duplicado. |
| 410 | Recurso retirado o enlace de descarga vencido. |
| 429 | Demasiadas solicitudes. |
| 503 | Dependencia no disponible (por ejemplo, el Inventario); el mensaje lo explica. |

## 4. Autenticación y autorización

- El servicio de Identidad emite JWT con los claims `sub`, `role` y `branch_id` (vacío para clientes y administración central). Caducidad corta, con renovación desde el servidor de `BabelHub.Web` (el token nunca llega al navegador, AD15).
- Cada servicio valida firma, vigencia y rol; el alcance por sucursal se comprueba en la Aplicación (`07`, sección 8).
- **Propuesta:** las llamadas entre servicios en nombre de un usuario reenvían su JWT; las tareas de fondo (liberar reservas vencidas, reintentar avisos) usan un token de servicio de Identidad. Se fija en `identidad-seguridad`.

## 5. Configuración y secretos

- `appsettings.json` solo contiene valores no sensibles. Los secretos (Culqi, LLM, webhook de n8n, almacén de archivos, cadenas de conexión) vienen de variables de entorno con el prefijo `BABELHUB__`.
- Se versiona `.env.example` con todas las variables y valores de ejemplo; el `.env` real está en `.gitignore`. En desarrollo se pueden usar los secretos de usuario de .NET.
- Los servicios fallan al arrancar si falta una variable obligatoria.

## 6. Persistencia

| Aspecto | Convención |
| :--- | :--- |
| Acceso | EF Core con Npgsql. Esquemas `ventas`, `identidad` (AD18) e `inventario`, cada uno con su propio rol de base de datos. |
| Migraciones | Una carpeta por servicio. En producción se aplican con un comando o tarea explícita, no al arrancar la aplicación. |
| Tiempo | `timestamptz` en UTC; se muestra en hora de Lima. El reloj es un puerto (`IReloj`) para poder probar la expiración de reservas. |
| Concurrencia | Reservas y ajustes usan bloqueo de fila o control optimista; la estrategia concreta se decide en el `plan.md` de `inventario-seleccion-fuente`. Debe probarse con solicitudes simultáneas. |
| Auditoría | Tabla de solo inserción en cada servicio; el rol de la aplicación no tiene permiso de `UPDATE` ni `DELETE` sobre ella (RNF08). |
| Archivos digitales | Nunca en la base de datos ni en las VMs: solo en el almacén de objetos (AD19). |

## 7. Resiliencia y observabilidad

- **Clientes HTTP** hacia otros servicios y proveedores usan la biblioteca de resiliencia de Microsoft: tiempo de espera corto, reintentos solo en operaciones idempotentes y cortacircuitos. Si el Inventario no responde, el E-commerce informa y no cobra (AC03).
- **Notificaciones:** el envío es asíncrono y tolerante a fallos; un aviso fallido queda pendiente y se reintenta (AD16).
- **Salud:** cada servicio expone `/health/live` y `/health/ready`; Docker Compose los usa en `healthcheck` con `restart: unless-stopped` (RNF03).
- **Logs:** estructurados en formato JSON hacia la consola, con `X-Correlation-Id`. No se registran datos de tarjeta, contraseñas, tokens ni archivos; los datos personales se enmascaran.

## 8. Pruebas

- Una prueba por criterio de aceptación de cada spec. Las pruebas llevan un rasgo (`Trait`) con el nombre de la spec y el criterio, para poder trazarlas.
- Nombres descriptivos en español: `Reservar_SinStockVendible_Rechaza`.
- Las pruebas de integración usan Testcontainers (PostgreSQL y un emulador de almacén compatible con S3) y necesitan Docker.
- Una prueba de arquitectura (NetArchTest) verifica la regla de dependencia de `07` y que ningún servicio referencie a otro.

## 9. Integración continua

GitHub Actions: compilar, ejecutar pruebas unitarias y de arquitectura en cada cambio; pruebas de integración en `main`; construcción de imágenes `linux/amd64` y `linux/arm64` con `buildx`. Los secretos se guardan en los secretos del repositorio, nunca en los flujos.

## 10. Docker

- Un `Dockerfile` por `Api` y por `Web`, en varias etapas. En la de `Web` se compila el CSS con la CLI independiente de Tailwind.
- Imágenes multi-arquitectura (RNF11), usuario sin privilegios, `healthcheck` y límites de memoria en Compose (AD16 y RNF12).
- `infra/docker-compose.local.yml` levanta PostgreSQL, Redis, un emulador de almacén compatible con S3 (a elegir, bloqueo B02), un notificador simulado y los servicios. No necesita Oracle ni Cloudflare.

## 11. Datos de ejemplo

Un conjunto sembrable para desarrollo, demostración y k6: 6 sucursales (Lima Centro, Arequipa, Trujillo, Cusco, Piura y Ayacucho), unos 40 títulos entre impresos y digitales con licencias de ejemplo (titular, territorio y vigencia), stock variado (con casos de poco stock, agotado y presente en una sola sucursal) y un usuario de ejemplo por rol. Las contraseñas de ejemplo solo existen en el archivo de ejemplo y nunca se usan fuera de desarrollo.

## 12. Definición de «terminado» de una tarea

1. Compila y las pruebas pasan, incluida la de arquitectura.
2. Cada criterio de la spec afectado tiene su prueba.
3. No hay secretos, datos personales ni archivos digitales en el cambio.
4. La spec, el plan o `docs/` se actualizan si el comportamiento cambió.
5. Se registra en `progress/` (log de la sesión y `CHANGELOG`).
