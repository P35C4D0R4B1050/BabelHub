# Almacén de objetos: contenedores, credenciales y retención

Define qué contenedores existen en el almacén de Oracle, quién puede hacer qué en cada uno y cuánto se conserva lo que contienen (PLA-16, PLA-35, PLA-38; decisiones D07, D08, D23 y D24 de `specs/transversal-plataforma/`). La definición vive en dos archivos de texto que leen `scripts/almacen.sh` y las pruebas: [`contenedores.txt`](contenedores.txt) y [`credenciales.txt`](credenciales.txt). [`ciclo-de-vida.json`](ciclo-de-vida.json) se genera de ellos (`scripts/almacen.sh ciclo-de-vida`) y una prueba comprueba que no se desfasa.

## Contenedores

| Contenedor | Contiene | Retención con bloqueo | Ciclo de vida |
| :--- | :--- | :--- | :--- |
| `libros` | Archivos digitales (AD19) | ninguna | no expira |
| `respaldos-diarios` | Copia consistente diaria del clúster, cifrada con `age` | 7 días, bloqueada | retira a los 7 días |
| `respaldos-semanales` | Copia dominical | 28 días, bloqueada | retira a los 28 días |
| `respaldos-mensuales` | Copia del último día del mes | 92 días, bloqueada | retira a los 92 días |
| `respaldos-wal` | Archivo de cambios (WAL) cifrado | 100 días, bloqueada | retira a los 100 días |
| `supresiones-entradas` | Registro de supresiones, `entradas/<identificador>/<tipo>-<fase>.json` | 100 días, bloqueada | no expira |
| `supresiones-sellos` | Sellos horarios de VM-AUX, `sellos/AAAA-MM-DD-HHmm.json` | 100 días, bloqueada | no expira |
| `supresiones-copia` | Copia diaria del registro, hecha por VM-AUX | 100 días, bloqueada | retira a los 100 días |

La retención con bloqueo de Oracle es **por contenedor**, no por prefijo (D23): una regla de 90 días sobre todos los respaldos rompería los 7 diarios de PLA-18, por eso hay un contenedor por clase. Los de más de 100 días (`respaldos-wal` y los tres de supresiones) cubren más que el respaldo más antiguo retenido (90 días) con margen (PLA-38). El registro de supresiones y sus sellos no expiran: son identificadores opacos sin datos personales y el sello cubre un conjunto estable.

**Por qué el mensual es de 92 días y no de 90.** Los meses no miden lo mismo, así que ningún plazo en días deja siempre exactamente tres fines de mes: la simulación de `RetencionTests` (más de dos años de respaldos, con un febrero bisiesto) muestra que con 90 días hay días en que solo quedan 2 (p. ej. el 28 de septiembre), lo que dejaría sin su tercer mes más reciente a PLA-18; con 92 quedan siempre 3 o, algunos días, 4 (el cuarto se retira a los pocos días). Entre no tener un mes y guardar uno de más, se guarda el de más. Los diarios (7 días) dejan siempre 7 y los semanales (28), siempre 4 domingos.

**Diferencia con el plan.** El plan habla de un contenedor `supresiones` con los prefijos `entradas/` y `sellos/`. Las políticas de Oracle que se emiten aquí son por contenedor (`target.bucket.name`), y no se asumió que admitan una condición por prefijo; para que «el servicio no pueda pre-crear sellos futuros» no dependa de eso, el registro y los sellos son **dos contenedores** y las claves conservan sus prefijos. Si el spike T02 demuestra que el permiso por prefijo funciona, se pueden unificar sin cambiar el código: el contenedor es una opción de configuración. Está anotado en `progress/hallazgos.md`.

## Credenciales (mínimo privilegio, una por tarea)

Las acciones son `crear` (`OBJECT_CREATE`: agrega y no sobrescribe), `leer`, `listar` (`OBJECT_INSPECT`), `uso` (`BUCKET_READ`: el tamaño del contenedor), `sobrescribir` y `borrar`. **Lo que no consta está prohibido.**

| Credencial | Dónde vive | Qué puede |
| :--- | :--- | :--- |
| `respaldos-escritor` | VM de datos | Solo `crear` en `respaldos-diarios`, `-semanales`, `-mensuales` y `-wal`. No lee, no sobrescribe, no borra: si se compromete la VM de datos no se puede alterar ni leer un respaldo existente. |
| `respaldos-lector` | Equipo de la persona responsable y máquina de ensayo; se rota tras cada ensayo con datos reales | `leer` y `listar` en los respaldos y en `supresiones-copia`. |
| `respaldos-vigilante` | VM-AUX | Solo `listar` (metadatos) en los respaldos y en `supresiones-copia`. |
| `supresiones-registro` | El servicio (Identidad) | `crear`, `leer` y `listar` solo en `supresiones-entradas`. No toca `supresiones-sellos`. |
| `supresiones-sello` | VM-AUX | `crear` solo en `supresiones-sellos`; `leer` y `listar` en `supresiones-entradas`. |
| `respaldos-copia-registro` | VM-AUX | `crear` solo en `supresiones-copia`. |
| `cuota-lectura` | VM-AUX | `uso` de cada contenedor. |
| `libros` | E-commerce | `crear`, `leer`, `listar`, `sobrescribir` y `borrar` solo en `libros`. |

**Ninguna credencial de respaldos o de supresiones puede borrar o sobrescribir.** El retiro de lo vencido lo hace el ciclo de vida del propio almacén, que no es una credencial que esté en una VM (D07): así se lee AC-PLA-16 («una credencial distinta que retira»), anotado en `progress/hallazgos.md` para reformularla.

## Retención con bloqueo efectivo

Una regla de retención **sin bloqueo** puede quitarse; una **con bloqueo** (`time-rule-locked`) ya no. Oracle exige que la fecha de bloqueo esté a 14 días o más de crearla y la regla sigue modificable hasta esa fecha. Por eso:

1. `scripts/almacen.sh aplicar` crea la regla con el bloqueo a 15 días.
2. `scripts/almacen.sh comprobar` distingue **bloqueo pendiente** (código 3: todavía se podría quitar la regla) de **bloqueado** (código 0). El despliegue no da por cumplida la retención mientras esté pendiente.
3. El bloqueo es **irreversible**: una duración mal elegida no se corrige y hay que dejar vencer o crear otro contenedor. Por eso `plan` muestra los comandos sin ejecutarlos y `aplicar` no cambia una regla que ya existe.

## Cómo se usa

Lo ejecuta la persona responsable, en su equipo, con su credencial de administración de Oracle (`oci` configurado): nunca en una VM ni en CI.

```bash
scripts/almacen.sh plan --compartimento-id <ocid> --compartimento-nombre <nombre> --espacio <espacio-de-nombres>
scripts/almacen.sh aplicar --compartimento-id <ocid> --compartimento-nombre <nombre> --espacio <espacio-de-nombres>
scripts/almacen.sh comprobar --espacio <espacio-de-nombres>
```

`aplicar` también imprime las sentencias de política IAM (una por credencial y contenedor); se aplican con la consola o con `oci iam policy create`. Cada credencial es un usuario de IAM con su clave secreta de cliente (la API compatible con S3); las claves se guardan fuera del repositorio.

## Qué está comprobado y qué no

- **Comprobado con pruebas:** la definición cumple las reglas (cada credencial solo hace lo que consta y se rechaza lo ajeno, incluidos los libros y los respaldos, el registro frente a los sellos y todo intento de borrar o sobrescribir; la retención y el bloqueo de cada contenedor; el ciclo de vida nunca antes que la retención), el script genera los comandos y las sentencias esperados, rechaza definiciones inválidas y `comprobar` detecta cada diferencia con un doble del CLI.
- **No comprobado aquí (spike T02):** que el almacén de Oracle acepte la sintaxis de las sentencias IAM tal como se emiten, que la retención sea por contenedor, que `crear` rechace una clave repetida y cuándo ejecuta el almacén el ciclo de vida. La forma de las respuestas del CLI que usa `comprobar` (`time-amount`, `time-unit`, `time-rule-locked`, `is-enabled`) se contrastará con el compartimento de prueba del spike.
