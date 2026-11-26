# Almacenes de objetos: contenedores, credenciales y retención

Define qué contenedores existen, quién puede hacer qué en cada uno y cuánto se conserva lo que contienen (PLA-16, PLA-18, PLA-35, PLA-38, PLA-44; decisiones D07, D08, D23, D24, D26 y D27 de `specs/transversal-plataforma/`). Hay dos almacenes:

- **Oracle Object Storage** (libros, portadas y registro de supresiones): [`contenedores.txt`](contenedores.txt) y [`credenciales.txt`](credenciales.txt); [`ciclo-de-vida.json`](ciclo-de-vida.json) se genera de ellos (`scripts/almacen.sh ciclo-de-vida`) y una prueba comprueba que no se desfasa.
- **Cloudflare R2** (los respaldos; provisional): [`r2/contenedores.txt`](r2/contenedores.txt) y [`r2/credenciales.txt`](r2/credenciales.txt).

`scripts/almacen.sh` y las pruebas leen las mismas definiciones.

## Contenedores

| Almacén | Contenedor | Contiene | Retención con bloqueo | Ciclo de vida |
| :--- | :--- | :--- | :--- | :--- |
| Oracle | `libros` | Archivos digitales (AD19) | ninguna | no expira; aborta las subidas multipart incompletas al día |
| Oracle | `portadas` | Portadas de las obras del catálogo (imágenes que la web sirve por huella, D06 de `ecommerce-catalogo`) | ninguna | no expira; aborta las subidas multipart incompletas al día |
| Oracle | `supresiones-entradas` | Registro de supresiones, `entradas/<identificador>/<tipo>-<fase>.json` | 100 días, irreversible | no expira |
| Oracle | `supresiones-sellos` | Sellos horarios de VM-AUX, `sellos/AAAA-MM-DD-HHmm.json` | 100 días, irreversible | no expira |
| Oracle | `supresiones-copia` | Copia diaria del registro, hecha por VM-AUX | 100 días, irreversible | retira a los 100 días |
| R2 | `respaldos-diarios` | Copia consistente diaria del clúster y la configuración, comprimidas y cifradas | 7 días | retira a los 7 días |
| R2 | `respaldos-semanales` | Copia dominical | 28 días | retira a los 28 días |
| R2 | `respaldos-mensuales` | Copia del último día del mes | 92 días | retira a los 92 días |
| R2 | `respaldos-wal` | Archivo de cambios (WAL) comprimido y cifrado | 7 días | retira a los 7 días |

En R2 el bloqueo manda sobre el ciclo de vida y lo vencido se retira en un día como mucho. El registro de supresiones se retiene 100 días, más de lo que dura el respaldo más antiguo (92) (PLA-38), y son identificadores opacos sin datos personales. **Por qué el mensual es de 92 días y no de 90:** los meses no miden lo mismo; la simulación de `RetencionTests` (más de dos años, con un febrero bisiesto) muestra que con 90 hay días con solo 2 fines de mes, y con 92 quedan siempre 3 o, algunos días, 4.

**Diferencia con el plan.** El plan habla de un contenedor `supresiones` con los prefijos `entradas/` y `sellos/`. Las políticas de Oracle que se emiten aquí son por contenedor (`target.bucket.name`), y no se asumió que admitan una condición por prefijo; para que «el servicio no pueda pre-crear sellos futuros» no dependa de eso, el registro y los sellos son **dos contenedores** y las claves conservan sus prefijos. Si el spike T02 demuestra que el permiso por prefijo funciona, se pueden unificar sin cambiar el código. Está anotado en `progress/hallazgos.md`.

## Credenciales (mínimo privilegio, una por tarea)

### Oracle

Las acciones son `crear` (`OBJECT_CREATE`: agrega y no sobrescribe), `leer`, `listar` (`OBJECT_INSPECT`), `uso` (`BUCKET_READ`: el tamaño del contenedor), `sobrescribir` y `borrar`. **Lo que no consta está prohibido.**

| Credencial | Dónde vive | Qué puede |
| :--- | :--- | :--- |
| `registro-lector` | Equipo de la persona responsable | `leer` y `listar` en `supresiones-copia` (verificación al restaurar sin sellos). |
| `registro-vigilante` | VM-AUX | Solo `listar` en `supresiones-copia`. |
| `supresiones-registro` | El servicio (Identidad) | `crear`, `leer` y `listar` solo en `supresiones-entradas`. No toca `supresiones-sellos`. |
| `supresiones-sello` | VM-AUX | `crear` solo en `supresiones-sellos`; `leer` y `listar` en `supresiones-entradas`. |
| `respaldos-copia-registro` | VM-AUX | `crear` solo en `supresiones-copia`. |
| `cuota-lectura` | VM-AUX | `uso` de cada contenedor. |
| `catalogo-archivos` | E-commerce (módulo Catálogo) | `crear`, `leer` y `borrar` en `libros` y en `portadas`. No sobrescribe (las claves son aleatorias y un reemplazo crea un objeto nuevo y borra el anterior) y no lista. |
| `biblioteca-lector` | E-commerce (módulo Biblioteca) | Solo `leer` en `libros`: firma las URL de descarga de corta duración (D13 de `ecommerce-catalogo`). No toca `portadas`. |

**Solo `catalogo-archivos` borra (sus archivos y portadas) y ninguna sobrescribe; ninguna credencial de supresiones puede borrar o sobrescribir.** El retiro de lo vencido lo hace el ciclo de vida del propio almacén (D07).

### Cloudflare R2

R2 solo ofrece «lectura y escritura de objetos», «solo lectura» o administración, acotables por contenedor: **no hay permiso de solo crear ni de solo listar**.

| Token | Dónde vive | Qué puede |
| :--- | :--- | :--- |
| `respaldos-escritor` | VM de datos | Lectura y escritura de objetos en los cuatro `respaldos-*`. El bloqueo impide borrar o sobrescribir dentro del plazo; puede leer lo subido (va cifrado) y subir objetos nuevos. No cambia reglas. |
| `respaldos-lector` | Equipo de la persona responsable y máquina de ensayo; se rota tras cada ensayo con datos reales | Solo lectura en los cuatro. |
| `respaldos-vigilante` | VM-AUX (aplazada) | Solo lectura en los cuatro (puede descargar los respaldos cifrados: R2 no ofrece solo listar). |

La **administración** (un token de API con edición de R2) existe solo mientras la persona responsable aplica o comprueba las reglas, en su equipo, y se revoca al terminar; la cuenta exige segundo factor (PLA-44).

## Retención con bloqueo efectivo

**Oracle.** Una regla de retención **sin bloqueo** puede quitarse; una **con bloqueo** (`time-rule-locked`) ya no. Oracle exige que la fecha de bloqueo esté a 14 días o más de crearla y la regla sigue modificable hasta esa fecha. Por eso `scripts/almacen.sh aplicar` crea la regla con el bloqueo a 15 días, `comprobar` distingue **bloqueo pendiente** (código 3) de **bloqueado** (código 0), `plan` muestra los comandos sin ejecutarlos y `aplicar` no cambia una regla que ya existe. El bloqueo es **irreversible**.

**R2.** Las reglas de bloqueo impiden borrar y sobrescribir durante su plazo, pero **se pueden quitar** con la administración de la cuenta: no hay modo irreversible (riesgo aceptado y provisional, spec §5). `r2-aplicar` no cambia un bloqueo que ya existe y difiere.

## Cómo se usa

Lo ejecuta la persona responsable, en su equipo: nunca en una VM, en CI ni desde un agente.

```bash
# Oracle (con `oci` configurado con su credencial de administración):
scripts/almacen.sh plan --compartimento-id <ocid> --compartimento-nombre <nombre> --espacio <espacio-de-nombres>
scripts/almacen.sh aplicar --compartimento-id <ocid> --compartimento-nombre <nombre> --espacio <espacio-de-nombres>
scripts/almacen.sh comprobar --espacio <espacio-de-nombres>

# Cloudflare R2 (token de API temporal, que se pide sin mostrarse o llega en CLOUDFLARE_API_TOKEN):
scripts/almacen.sh r2-plan
CLOUDFLARE_ACCOUNT_ID=<account-id> scripts/almacen.sh r2-aplicar
CLOUDFLARE_ACCOUNT_ID=<account-id> scripts/almacen.sh r2-comprobar
```

`aplicar` también imprime las sentencias de política IAM (una por credencial y contenedor); se aplican con la consola o con `oci iam policy create`. Cada credencial de Oracle es un usuario de IAM con su clave secreta de cliente (la API compatible con S3). Los tokens de objetos de R2 se crean en el panel, acotados a sus contenedores (guía de despliegue §9b). Las claves se guardan fuera del repositorio.

## Qué está comprobado y qué no

- **Comprobado con pruebas:** las definiciones cumplen las reglas (cada credencial solo tiene lo que consta; la retención de cada contenedor; el ciclo de vida nunca antes que la retención; R2 y Oracle sin contenedores ni credenciales compartidos); el script genera los comandos, las sentencias y las llamadas esperadas, rechaza definiciones inválidas, no expone el token de R2, y `comprobar` y `r2-comprobar` detectan cada diferencia con dobles del CLI y de la API.
- **No comprobado aquí:** que el almacén de Oracle acepte la sintaxis IAM y las reglas tal como se emiten (spike T02), y que R2 se comporte como su documentación (forma de las respuestas de la API, bloqueo, alcance de cada token y retiro del ciclo de vida): constancia T47 en la cuenta antes de poner datos que importen.

## Almacén del catálogo (portadas y libros)

Lo pidió `ecommerce-catalogo` (R05, T17) y se agregó sin tarea propia de `transversal-plataforma` (fila «R05 del catálogo» de su `decisiones-de-implementacion.md`):

- **Contenedores.** `libros` (los archivos digitales) y `portadas`, ambos privados, sin retención con bloqueo y sin expiración: el Catálogo borra lo que ya no sirve (reemplazos y descartes) con su propia credencial y la web sirve las portadas por huella, nunca por el almacén.
- **Subidas incompletas.** `contenedores.txt` admite una quinta columna opcional (`subidas_dias`): los días tras los cuales el ciclo de vida aborta una subida multipart incompleta (`action: ABORT`, `target: multipart-uploads`). `libros` y `portadas` la fijan en 1 día. `scripts/almacen.sh` la genera en `ciclo-de-vida.json`, la aplica y `comprobar` verifica que sea la **última** regla de la política y que ninguna regla retire objetos en esos contenedores.
- **Credenciales.** `catalogo-archivos` (crear, leer y borrar, sin sobrescribir ni listar) y `biblioteca-lector` (solo leer `libros`); `cuota-lectura` mide también `portadas`. Reemplazan a la credencial genérica `libros`, que nadie usaba todavía.
- **Variables de entorno (convención; las consume la composición de la Biblioteca, `AnfitrionDelEcommerce`).** `BABELHUB__Almacen__Catalogo__Endpoint`, `…__Region`, `…__ClaveDeAcceso` y `…__ClaveSecreta` para `catalogo-archivos`, y `BABELHUB__Almacen__Biblioteca__…` para `biblioteca-lector`, en el archivo de secretos de VM-VENTAS. Se añaden a `infra/secretos-requeridos.txt` junto con el código que las lee (ECO-2c T17 y el bloque de la biblioteca), para no exigir en el despliegue claves que nada usa.
- **Por confirmar en el spike T02** (como el resto): que el permiso `OBJECT_READ` baste para las lecturas por rango y para las URL firmadas de la biblioteca, que abortar subidas multipart incompletas no exija otro permiso y que el almacén acepte `target: multipart-uploads` con `ABORT` en la política tal como se emite.
