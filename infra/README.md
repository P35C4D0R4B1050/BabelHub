# Infraestructura

| Ruta | Estado | Contenido |
| :--- | :--- | :--- |
| `docker-compose.local.yml` | Disponible | Una instancia PostgreSQL con una base y un rol por servicio (`ecommerce` e `inventario`) y Redis para desarrollo local. Los servicios .NET y la Web se suman con sus Dockerfiles. |
| `postgres/init/` | Disponible | Crea las bases y los roles la primera vez que arranca el volumen (AD03). |
| `postgres/Dockerfile` | Disponible | PostgreSQL con `age` y `rclone` (cliente S3) para el archivado de cambios y el respaldo (D06, D10), multi-arquitectura y sin privilegios. |
| `almacen/` | Disponible | Contenedores, credenciales y retención con bloqueo del almacén de objetos de Oracle (PLA-16, PLA-35, PLA-38): `contenedores.txt`, `credenciales.txt`, `ciclo-de-vida.json` y `politicas.md`. Se aplican y se comprueban con `scripts/almacen.sh`, en el equipo de la persona responsable. |
| `postgres/respaldos/`, `respaldos/` | Disponible | Los scripts de archivado de cambios y respaldo diario cifrado con `age` que van dentro de la imagen de PostgreSQL (`archivar-wal.sh`, `respaldar.sh`) y cómo se usan y programan (`respaldos/README.md`); el respaldo bajo demanda es `scripts/respaldar-ahora.sh`. |
| `secretos-requeridos.txt` | Disponible | Los nombres de las claves obligatorias de cada archivo de secretos de una VM (sin valores). Lo lee `scripts/desplegar.sh <vm>`, que despliega una VM con un solo comando y no continúa si falta algo (PLA-04). |
| `../.github/workflows/lanzamiento.yml` | Disponible | Publica las imágenes propias (`ecommerce`, `inventario`, `postgres`) para `linux/amd64` y `linux/arm64` en GHCR por una etiqueta `v*` y muestra el digest de cada una para `IMAGEN_<SERVICIO>` (DD-57). |
| `../scripts/generar-secretos.sh`, `../scripts/ensayo-local.sh` | Disponible | El primero crea los archivos de secretos de cada VM, la CA privada y el par de `age` en una carpeta nueva (DD-59); el segundo ensaya todo el despliegue en tu equipo con secretos de mentira. Guía: `docs/07-proceso-de-desarrollo/08-guia-primer-despliegue.md`. |
| `scripts/vm-aux/`, `vm-aux/` | En curso | Scripts de VM-AUX con credenciales mínimas y desde fuera de la VM de datos (T24, T25): comprobación de respaldos y de atraso del archivado con aviso por correo, sellos horarios y copia diaria del registro de supresiones, ancla semanal, vigilancia de la retención y latido externo; la programación y el README están en `vm-aux/`. El endurecimiento de la propia VM-AUX llega con T26. |
| `excepciones-de-arquitectura.json` | Disponible | Registro de las imágenes de terceros que no existen para una arquitectura en la que corren (PLA-03); hoy vacío. |
| `nginx/nginx.conf` | Plantilla | Enrutamiento por host (tienda y gestión) con límites por IP y servidor de respaldo para la gestión (AD14). |
| `n8n/` | Pendiente | Flujos de n8n exportados (AD16), con la spec `transversal-notificaciones`. |
| `scripts/desplegar.sh` (migración) | Disponible | En VM-INVENTARIO, antes de levantar los servicios, levanta PostgreSQL y ejecuta `migrar` en la imagen de cada servicio (`inventario` y `ecommerce-gestion`): crea los roles de esquema, aplica los esquemas de los módulos y crea o rota las cuentas de conexión (COMP-2). La cuenta propietaria viene de `migrador.env` (`BABELHUB__Migrador__Inventario__Propietaria` y `BABELHUB__Migrador__Ecommerce__Propietaria`), que solo existe en esa VM y nunca en los archivos de los servicios. VM-VENTAS no migra: usa la base de la VM de datos. |
| `docker-compose.borde.yml`, `docker-compose.ventas.yml`, `docker-compose.inventario.yml` | Disponible | Un Compose por VM de Oracle (PLA-26, PLA-27): versiones y `digest` fijos, usuario sin privilegios, capacidades descartadas, límites, `healthcheck`, `read_only` (con las excepciones de Evolution API registradas en `x-excepciones`) y solo los secretos de esa VM, desde `BABELHUB_SECRETOS`. Las imágenes propias llegan por `IMAGEN_<SERVICIO>` con su `digest` (las fija el flujo de lanzamiento). VM-AUX no usa Compose: sus scripts llegan con T24. |

Reglas (ver `docs/02-arquitectura-de-software/02-decisiones-arquitectonicas.md` y `docs/07-proceso-de-desarrollo/02-convenciones-tecnicas.md`): imágenes multi-arquitectura, usuario sin privilegios, `healthcheck`, límites de memoria y puertos de bases de datos solo en `127.0.0.1` en local. Los secretos viven en `.env` (ignorado por Git); se versiona `.env.example`.

**Migrar las bases en local** (COMP-2): con el Compose local arriba, las cuentas del servicio (`BABELHUB__<Servicio>__Base__…`) y la cuenta propietaria en el entorno:

```bash
read -rs -p "Contraseña de la cuenta administradora: " ADMIN && echo
export BABELHUB__Migrador__Ecommerce__Propietaria="Host=localhost;Database=ecommerce;Username=postgres;Password=$ADMIN"; unset ADMIN
dotnet run --project src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api -- migrar   # y lo mismo con Inventario y su variable
unset BABELHUB__Migrador__Ecommerce__Propietaria
```

**Cuentas de conexión:** cada servicio solo migra cuentas cuyo nombre empieza por el suyo (`ecommerce_acceso`, `ecommerce_depurador_auditoria`, `inventario_acceso`…) porque los roles son del clúster y, con las dos bases en una instancia, un mismo nombre en los dos servicios se pisa la contraseña; el migrador lo exige y deja el `CONNECT` de cada base solo para sus cuentas. Ajusta los usuarios de `BABELHUB__<Servicio>__Base__…` y de las cadenas de `postgres.env` antes de migrar.

Es idempotente (repetirlo es seguro y solo rota las contraseñas), falla cerrado nombrando la variable o el rol que falta y no imprime ninguna clave. La cuenta propietaria debe ser de administración (crea roles y extensiones) y distinta de las del servicio.

```bash
cp .env.example .env
docker compose -f infra/docker-compose.local.yml --env-file .env up -d
```

## Contacto del comercio y versión de los textos legales (web-informacion-del-comercio)

**Contacto.** El teléfono, el correo y la dirección que muestra la tienda (el pie de toda página y `/contacto`) **no están en el repositorio**: salen de `BABELHUB__Contacto__Telefono`, `BABELHUB__Contacto__Correo` y `BABELHUB__Contacto__Direccion` en `ecommerce.env` y `ecommerce-gestion.env` (no son secretos). `scripts/generar-secretos.sh` los deja como `CAMBIAR` y `scripts/desplegar.sh` se niega a desplegar con un `CAMBIAR`: quien despliega escribe los valores reales **entre comillas dobles** y sin `$` (Compose lo interpola aun entre comillas), `"`, `#` (Compose lo toma como comentario sin comillas) ni barra invertida. El correo debe ser una dirección de atención al cliente, no la cuenta que envía los avisos. Si falta uno, o su formato no es válido, el servicio **no arranca** y el error nombra la variable (nunca el valor); solo `Development` y `Test` usan valores ficticios. La restauración y el ensayo reutilizan el mismo `ecommerce.env`, así que ya llevan las tres variables. **Comprobación del nombre del entorno:** los Compose de despliegue fijan `ASPNETCORE_ENVIRONMENT: Production`; un servicio de producción que mostrara `999 000 000` o `contacto@ejemplo.test` estaría mal nombrado.

**Versión de los textos legales.** El cuerpo de los términos, la privacidad y «Cambios y devoluciones» vive en el repositorio y su versión en la base (`identidad.versiones_de_textos`, una fila, `id = 1`), que **ningún arranque siembra** y que la cuenta de la aplicación solo lee. Cada vez que cambia un cuerpo hay que fijar su versión nueva, y la versión de referencia de cada texto está en `tests/integration/BabelHub.IntegrationTests/Web/versiones-de-referencia-de-textos.json` (la forma es `AAAA-MM-DD-<8 hex de la huella del cuerpo>`; una prueba falla si el cuerpo cambia sin cambiar esa versión). Orden en un despliegue: `desplegar inventario` (migra y levanta), este paso con la cuenta propietaria de la base del E-commerce (la del modo migrar, nunca la de la aplicación; sin pasar la contraseña por la línea de comandos), `desplegar ventas` y `desplegar borde`. En una actualización hay una ventana corta con el cuerpo nuevo y la versión vieja, riesgo aceptado (COM-15). Quien lo ejecuta anota en la bitácora de despliegue quién y cuándo, porque obliga a todos a aceptar los textos de nuevo:

```sql
BEGIN;
INSERT INTO identidad.versiones_de_textos (id, terminos, privacidad, reembolso, vigente_desde)
VALUES (1, '<versión de terminos del archivo de referencia>', '<versión de privacidad>', '<versión de reembolso>', '<AAAA-MM-DD>')
ON CONFLICT (id) DO UPDATE SET terminos = EXCLUDED.terminos, privacidad = EXCLUDED.privacidad,
  reembolso = EXCLUDED.reembolso, vigente_desde = EXCLUDED.vigente_desde;
SELECT terminos, privacidad, reembolso, vigente_desde FROM identidad.versiones_de_textos;  -- exactamente una fila
COMMIT;
```

Ejecutar con `psql -v ON_ERROR_STOP=1` y comprobar que afecta exactamente una fila.
