# Guía del primer despliegue en Oracle Cloud

Para una persona que **nunca** usó Docker, Oracle Cloud, máquinas virtuales ni Ubuntu sin pantalla. Cada paso trae el comando exacto, **qué debes ver** si salió bien, **qué hacer si falla** y **cómo deshacerlo**. No saltes pasos ni cambies el orden: cada etapa prepara la siguiente.

> **Léela entera una vez antes de empezar.** Dedícale varios días: la mayor parte del tiempo es esperar (la cuenta de Oracle, el DNS, la capacidad ARM). Nada de lo que hagas aquí te cuesta dinero si respetas el cupo gratuito (ver el paso 3), pero un error con secretos sí puede costarte caro. Ante la duda, **para y pregunta**.

## 0. Antes de empezar: el estado real de BabelHub (léelo primero)

Esta guía deja listo **todo lo que es infraestructura** con **dos máquinas virtuales** (topología vigente desde el 2026-11-25: no hubo capacidad para las AMD micro). Pero hay cosas que debes saber antes de gastar tiempo:

| # | Qué pasa | Por qué | Qué decidir |
| :--- | :--- | :--- | :--- |
| **1** | **RESUELTO con el entorno `Staging` (2026-11-23).** El E-commerce no arrancaba en `Production` sin el adaptador de Culqi (T37 y T38, a la espera de las llaves, B05): el simulador solo valía en `Development` y `Test`. | Ahora el simulador también vale en **`Staging`**, un entorno que se endurece **igual que `Production`** (HTTPS entre servicios, Redis con TLS, correo y almacén reales, hosts permitidos…) y que solo afloja el cobro: a nadie se le cobra, porque no hay pasarela (`ninguna`, el valor por defecto desde el 2026-11-26: el pago en línea se anuncia como «disponible pronto», DD-110) o porque es la simulada, que lo dice en pantalla. `Production` **sigue fallando cerrado** sin la pasarela real y su llave `sk_live_…`. | Nada que decidir: despliega en **`Staging`** (el generador de secretos lo hace por defecto, con `ninguna`) para que **Culqi revise la tienda** (§15.1, fase 1) y pasa a `Production` cuando lleguen las llaves (§15.1, fase 2). |
| **1 bis** | **RESUELTO: el primer administrador se crea con un comando** (`crear-administrador`, §15.1). | Sin una primera cuenta de administración nadie entra a `gestion.babel-hub.shop`, ni carga el catálogo (Culqi exige **al menos 5 productos**) ni administra pedidos. | Ejecutar `crear-administrador --correo <tu correo>` (§15.1); en el primer ingreso cambias la contraseña y configuras el segundo factor (TOTP). |
| **1 ter** | **RESUELTO: los envíos se operan con el simulador, también en `Production`** (DD-22, DD-88). | No existe un operador logístico real ni lo habrá. Los archivos de secretos llevan `BABELHUB__Envios__Operador="simulador"` (el generador lo escribe y `desplegar.sh` lo exige) y la tienda muestra «Los envíos son simulados: proyecto académico» encima del pie y en el checkout, **salvo con la pasarela `ninguna`**, en la que no se puede comprar y no se muestra (DD-111). | Nada que decidir. Las guías de envío a domicilio las genera el simulador y ningún paquete sale. |
| **2** | **RESUELTO (2026-11-25): PostgreSQL habla solo TLS verificado** entre VM-VENTAS, los servicios de VM-INVENTARIO y Evolution API (AM24, DD-90 a DD-94). | El servidor usa un certificado de la CA privada (`tls-postgres-*.pem`, que `generar-secretos.sh` crea con los demás), `pg_hba.conf` rechaza toda conexión de red sin TLS y las cadenas de conexión llevan `SSL Mode=VerifyFull`; `desplegar.sh` se niega a desplegar si alguna no lo pide. | Nada que decidir. **Si ya generaste tu carpeta de secretos con una versión anterior del generador, regenérala** (la CA anterior no permite el nombre `postgres`): usa una carpeta nueva y vuelve a copiar los valores «CAMBIAR» que completaste; no se puede parchear a mano. |
| **3** | Los clientes de seguridad exigen **HTTPS entre servicios** en producción. | Es una regla de la aplicación (SEG-15). | Ya está resuelto aquí: `scripts/generar-secretos.sh` crea una **CA privada** y los certificados de cada VM, y los Compose abren el puerto 8443. No hay que hacer nada, solo entenderlo (reglas de red en el paso 3). |
| **4** | **Solo dos VMs** (DD-100). `cloudflared` y Nginx corren dentro de VM-VENTAS; **no hay VM-BORDE** y **VM-AUX queda aplazada**. | Sin capacidad para las AMD micro en `sa-santiago-1`. | Nada que decidir. Lee en §14 lo que **no** tienes mientras no haya VM-AUX (entre otras cosas, nadie te avisa si fallan los respaldos). |
| **5** | **Los respaldos van comprimidos y cifrados a Cloudflare R2** (2026-11-25, **provisional**); los libros, las portadas y el registro de supresiones siguen en Oracle. | R2 no ofrece una credencial de «solo crear» ni un bloqueo irreversible: la VM de datos no puede borrar un respaldo dentro de su plazo, pero **quien entre a tu cuenta de Cloudflare sí podría** quitar el bloqueo y borrarlo todo (riesgo aceptado, spec `transversal-plataforma` §5). El archivo de cambios se guarda solo **7 días**. | Activa el segundo factor en Cloudflare, sigue §9b y **no pongas datos reales** hasta que la prueba de tu cuenta (§9b.5) salga bien. Antes de operar con clientes reales se vuelve a decidir (B09, B13). |

Lo que sí **funciona hoy y se probó de verdad** (en tu equipo, con las imágenes reales y los mismos scripts): construir las tres imágenes, PostgreSQL con sus migraciones, el **Inventario en `Production`** respondiendo `/health/ready` por HTTPS con el certificado validado, la réplica de gestión y el E-commerce (estos dos en `Development`), Redis con TLS, n8n y los scripts de despliegue. Lo que **no** se pudo probar sin Oracle y sin dominio: Cloudflare, el túnel, el almacén de objetos de Oracle, tu cuenta de R2 (los respaldos se probaron contra un almacén compatible local), el cobro con Culqi, WhatsApp y la capacidad ARM real.

## 1. Qué necesito tener a mano

Marca cada uno antes de empezar:

**Cuentas y dinero**
- [ ] Una tarjeta de crédito o débito **internacional** (Oracle la pide para verificar la cuenta; no te cobra mientras te quedes en el cupo gratuito). Anota que Oracle hace un cargo temporal de verificación (~1 USD) y lo devuelve.
- [ ] Un correo que revises a diario y un teléfono para el SMS de verificación.
- [ ] La cuenta de **GitHub** dueña del repositorio.
- [ ] Una cuenta de **Cloudflare** (gratuita) y el **dominio comprado en Spaceship** con acceso a su panel. Para los respaldos se activa **R2** en esa cuenta: tiene 10 GB al mes gratis y puede pedirte un medio de pago (lo que pase del cupo se cobra).
- [ ] Una cuenta de **Gmail sencilla** (no institucional) con verificación en dos pasos y una **contraseña de aplicación** (Cuenta de Google → Seguridad → Contraseñas de aplicaciones). Es para los correos del sistema. Otra contraseña de aplicación (o la misma cuenta con otra contraseña) para los avisos del espacio reservado.
- [ ] Más adelante: llaves de Culqi (B05), tu API key de Anthropic (B06), un número de WhatsApp dedicado (B07). **No las necesitas para empezar.**

**En tu equipo**
- [ ] Windows 11 con **WSL** (Ubuntu) o una Linux/macOS. En Windows, abre la app «Ubuntu» (si no la tienes: PowerShell como administrador → `wsl --install -d Ubuntu`, reinicia). Todos los comandos marcados **[Tu equipo]** se ejecutan ahí.
- [ ] **Docker Desktop** (con la integración WSL activada) o Docker Engine en tu Linux.
- [ ] `git`, `openssl`, `curl`, `ssh`, y para los respaldos `age`, `zstd` y `rclone`. En Ubuntu: `sudo apt update && sudo apt install -y git openssl curl openssh-client age zstd rclone`.
- [ ] El **repositorio clonado**: `git clone https://github.com/<propietario>/BabelHub.git ~/BabelHub && cd ~/BabelHub`.
- [ ] La **CLI de Oracle** (`oci`) para el almacén de objetos (§9): <https://docs.oracle.com/en-us/iaas/Content/API/SDKDocs/cliinstall.htm>.
- [ ] Un **gestor de contraseñas** o un cuaderno **fuera de línea** y **dos lugares distintos** para guardar la clave de respaldos (§10.1).

**Convenciones.** `[Tu equipo]` = tu WSL/Linux. `[VM-X]` = dentro de esa VM por SSH. Lo que va entre `<ángulos>` lo reemplazas tú. **Nunca pegues un secreto en un chat, un correo ni el repositorio.** Si un comando imprime algo que parece secreto, no lo copies a ningún lado.

## 2. El mapa

```
Internet ──► Cloudflare (DNS, TLS, WAF, Access) ──túnel saliente──► VM-VENTAS [cloudflared → Nginx → E-commerce] ──► VM-INVENTARIO (Inventario + PostgreSQL)
                                                                                        └── gestión ──────────────► VM-INVENTARIO (réplica de gestión)
                                  tu equipo ──SSH (solo desde tu IP)──► las dos VMs
```

| VM | Tipo en Oracle | IP privada (la que usaremos) | Qué corre |
| :--- | :--- | :--- | :--- |
| **VM-INVENTARIO** | ARM `VM.Standard.A1.Flex`, 1 OCPU, 6 GB | `10.0.0.20` | PostgreSQL, Inventario, réplica de gestión del E-commerce |
| **VM-VENTAS** | ARM `VM.Standard.A1.Flex`, 1 OCPU, 6 GB | `10.0.0.10` | `cloudflared`, Nginx, E-commerce, Redis (caché y contadores), n8n, Evolution API |

### 2.1 Tu dominio: `babel-hub.shop`

El dominio ya está en Cloudflare, con SSL. En esta guía el dominio es **`babel-hub.shop`** y sus nombres son estos:

| Nombre | Para qué | Dónde llega |
| :--- | :--- | :--- |
| `babel-hub.shop` (el «apex») | **La tienda**, el nombre canónico que ve el público (y Culqi) | Túnel → Nginx (VM-VENTAS) → E-commerce (rol `tienda`) |
| `www.babel-hub.shop` | Redirige al apex con una regla de Cloudflare (§12.2); la aplicación también lo acepta | ídem |
| `gestion.babel-hub.shop` | **La gestión** (sucursales y administración), protegida por Cloudflare Access (§12.4) | Túnel → Nginx (VM-VENTAS) → réplica de gestión en VM-INVENTARIO (rol `gestion`) |

Nginx (`infra/nginx/nginx.conf`) ya enruta así: el servidor por defecto atiende la tienda y `server_name gestion.*` la gestión; el generador de secretos escribe los hosts permitidos de cada servicio (`babel-hub.shop,www.babel-hub.shop` y `gestion.babel-hub.shop`) y los de Turnstile. No hay nada que cambiar en los archivos del repositorio por el dominio.

**El orden de la guía** (cada etapa termina con una comprobación; no pases a la siguiente si falla):

1. Ensayo en tu equipo (paso 4) → 2. Publicar las imágenes (paso 5) → 3. Cuenta, red y las 2 VMs de Oracle (pasos 6–8) → 4. Almacenes (Oracle y Cloudflare R2) y secretos (pasos 9–10) → 5. **VM-INVENTARIO** (§11) → 6. **Cloudflare** (§12) → 7. **VM-VENTAS** con la entrada (§13) → 8. Lo que falta sin VM-AUX (§14) → 9. Después del despliegue (§15).

*Por qué ese orden:* los secretos llevan las llaves de los dos almacenes y las claves de Turnstile (que se crean al completar los secretos, §10.2), y los de VM-VENTAS además el token del túnel (§12.3); sin ellas el despliegue se niega a continuar a propósito.

*Contacto del comercio y versión de los textos legales (mínimo, `web-informacion-del-comercio`):* el `ecommerce.env` de VM-VENTAS y el `ecommerce-gestion.env` de VM-INVENTARIO llevan además `BABELHUB__Contacto__Telefono`, `…Correo` y `…Direccion` como `CAMBIAR` (no son secretos): escribe los reales entre comillas dobles antes de desplegar, y tras `desplegar inventario` fija la versión de los textos legales como indica `infra/README.md` (sección «Contacto del comercio y versión de los textos legales»).

## 3. Reglas de oro (para no perder dinero ni datos)

- **El cupo gratuito** (verifícalo en la consola al crear la cuenta; las cifras de esta guía vienen de fuentes secundarias del 2026-11-06): **2 VMs ARM con 2 OCPU y 12 GB en total, 2 VMs AMD micro, 200 GB de disco en total**. Crear una tercera VM ARM, una VM más grande, un balanceador o un volumen extra **sí cobra**. Crea exactamente lo de esta guía.
- **Presupuesto con alerta de 1 USD** desde el primer día (paso 6.3).
- **La región no se puede cambiar** después de crear la cuenta: elige **Chile (Santiago) `sa-santiago-1`** (la más cercana a Perú).
- **Nunca** ejecutes `docker compose down -v` en VM-INVENTARIO: **borra la base de datos**. `down` sin `-v` no.
- **Nunca** copies la carpeta `custodia/` a una VM. Contiene la clave que descifra todos los respaldos.
- **Nunca** hagas `git push --force`, ni subas un `.env`, una `.pem` ni una clave al repositorio (es **público**).

## 4. Paso 0 — Ensayo en tu equipo (30–60 minutos, gratis, sin riesgo)

Reproduce el despliegue con los mismos scripts y los mismos Compose, sin Oracle, sin dominio y con secretos de mentira. **Hazlo siempre antes de tocar Oracle.**

**[Tu equipo]**

```bash
cd ~/BabelHub
bash scripts/ensayo-local.sh
```

> **En Windows (PowerShell):** no escribas `bash scripts/ensayo-local.sh` a secas. En PowerShell, `bash` abre el *bash de WSL*, que falla con «execvpe(/bin/bash) failed» si no tienes una distribución de Linux instalada. Usa **Git Bash**, que ya trae Git para Windows, y **abre antes Docker Desktop** (espera a que diga «Engine running»):
>
> ```powershell
> cd D:\Trabajo\BabelHub
> & "C:\Program Files\Git\bin\bash.exe" scripts/ensayo-local.sh
> ```
>
> O abre la aplicación **Git Bash** desde el menú de inicio y ejecuta allí `cd /d/Trabajo/BabelHub && bash scripts/ensayo-local.sh`. Para limpiar el ensayo, lo mismo con `--limpiar`.

**Qué debes ver** (la primera vez tarda 10–20 minutos construyendo las imágenes; las siguientes, 2–3):

```
==> 1/6 Registro local y construcción de las tres imágenes ...
==> 3/6 VM-INVENTARIO: PostgreSQL, migraciones, Inventario y réplica de gestión
migracion: inventario al día (...)
migracion: ecommerce al día (15 pasos, 3 cuentas)
ok postgres
ok ecommerce-gestion
ok inventario
VM inventario desplegada y sana.
==> 4/6 VM-VENTAS: ...
ok redis-contadores   ok redis   ok ecommerce   ok n8n
VM ventas desplegada y sana.
==> 5/6 Humo: ...
    Inventario (https://inventario:8443): {"estado":"listo",...}  [HTTP 200]
    E-commerce (https://ecommerce:8443):  {"estado":"listo",...}  [HTTP 200]
```

Antes de 2026-11-25 se veía una línea `Cannot load library libgssapi_krb5.so.2` al migrar (un aviso inofensivo del controlador de PostgreSQL, que sondea Kerberos); las cadenas de conexión ya llevan `GSS Encryption Mode=Disable` y no debe aparecer. En el ensayo, el paso 6b comprueba además que PostgreSQL rechaza lo que no usa TLS verificado.

**Si falla:** el mensaje nombra qué falta. Los fallos típicos: *Docker no está corriendo* (ábrelo), *puerto ocupado* (cierra lo que use 5055, 35432, 38080…; en Windows, si dice «address already in use» en un puerto 5xxxx de una versión anterior, es que ese puerto cayó en el rango dinámico del sistema: usa la versión actual del script, que usa puertos 3xxxx), *poca memoria* (cierra programas; necesita ~6 GB libres).

**Cómo deshacer** (siempre que quieras repetirlo desde cero):

```bash
bash scripts/ensayo-local.sh --limpiar
```

Esto borra solo los contenedores, volúmenes y archivos del ensayo.

## 5. Paso 1 — Publicar las imágenes (GitHub Actions → GHCR)

Las VMs no construyen nada: **bajan imágenes ya construidas** del registro de contenedores de GitHub (GHCR). Un flujo (`.github/workflows/lanzamiento.yml`) las construye para ARM y x86, las revisa (secretos en las capas, usuario sin privilegios), las sube y te muestra el **digest** de cada una: una huella (`sha256:…`) que identifica *exactamente* ese contenido. Esa huella es lo que pegas en las VMs.

### 5.1 Preparación única en GitHub (10 minutos)

1. **Protege las etiquetas de versión.** GitHub → tu repositorio → *Settings → Rules → Rulesets → New ruleset → New tag ruleset*. *Target:* etiquetas `v*`. Reglas: *Restrict creations*, *Restrict updates*, *Restrict deletions*. *Bypass list:* solo tú. Así nadie más puede crear la versión que se publica.
2. **Crea el entorno `lanzamiento`.** *Settings → Environments → New environment* → nombre `lanzamiento` → *Required reviewers*: tú. Así cada publicación te pide aprobación (un botón verde) antes de construir. Como la revisora eres tú, **desmarca *Prevent self-review*** (si no, no podrías aprobar tus propias publicaciones) y desmarca *Allow administrators to bypass*; **pulsa *Save protection rules*** (sin ese botón no se guarda nada). En el paso 1, *Enforcement status* nace en *Disabled*: ponlo en **Active**.
3. Confirma que el flujo `ci` y el de `secretos` están **en verde** en `main` (pestaña *Actions*). El flujo de lanzamiento se niega a publicar si el commit no pasó ambos.

### 5.2 Lanzar

La persona responsable (tú, no un agente) etiqueta un commit de `main` **que ya esté subido a GitHub** y sube la etiqueta:

**[Tu equipo]**

```bash
cd ~/BabelHub
git checkout main && git pull
git tag -a v0.1.0 -m "primer despliegue"
git push origin v0.1.0
```

GitHub → pestaña *Actions* → «lanzamiento» → te pide **Review deployments** → *Approve*. Tarda 15–30 minutos (construye 6 imágenes: 3 × 2 arquitecturas).

**Qué debes ver:** los seis trabajos «Construir …» en verde y, al final, el resumen **«Digests para los Compose»** con tres líneas:

```
IMAGEN_ECOMMERCE=ghcr.io/<propietario>/babelhub-ecommerce@sha256:<64 caracteres>
IMAGEN_INVENTARIO=ghcr.io/<propietario>/babelhub-inventario@sha256:<64 caracteres>
IMAGEN_POSTGRES=ghcr.io/<propietario>/babelhub-postgres@sha256:<64 caracteres>
```

**Cópialas** a un archivo de texto tuyo (también quedan como artefacto «imagenes-publicadas» 90 días). Las usarás tal cual en los pasos 11–13.

**Si falla:** «El flujo ci.yml no terminó en verde» → espera o corrige; «El commit no está en la rama principal» → etiqueta un commit de `main`; «La etiqueta ya existe» → una versión publicada nunca se sobrescribe: crea `v0.1.1`; «Se encontraron secretos en la imagen» → **no publiques**, avisa (es un hallazgo de seguridad real).

### 5.3 Que las VMs puedan bajar las imágenes

Los paquetes de GHCR nacen **privados**. Haz públicas las tres imágenes (el código ya es público y las imágenes no llevan secretos: el flujo lo comprueba): GitHub → tu perfil u organización → *Packages* → cada paquete (`babelhub-ecommerce`, `babelhub-inventario`, `babelhub-postgres`) → *Package settings → Change visibility → Public*. **Es irreversible** para ese paquete: hazlo solo después de ver el flujo en verde.

Alternativa (si prefieres paquetes privados): crea un *Personal Access Token (classic)* con **solo** `read:packages`, y en cada VM `echo <TOKEN> | sudo docker login ghcr.io -u <usuario> --password-stdin`. Es un secreto más de la VM: guárdalo con permisos 600 y caduca el token a los 90 días.

### 5.4 Verificar de dónde vienen (recomendado)

La procedencia firmada demuestra qué commit y qué flujo construyó cada imagen:

**[Tu equipo]** (necesita la CLI `gh`)

```bash
gh attestation verify oci://ghcr.io/<propietario>/babelhub-inventario@sha256:<64> --repo <propietario>/BabelHub --source-ref refs/tags/v0.1.0
```

**Debes ver** `✓ Verification succeeded!`. Si dice que no encuentra atestación o que el origen no coincide, **no despliegues esa imagen**.

**Cómo deshacer:** una versión mala no se borra ni se sobrescribe; publicas `v0.1.1` y despliegas esa. Los paquetes se pueden borrar desde *Package settings → Delete this package*.

## 6. Paso 2 — La cuenta de Oracle Cloud

### 6.1 Crear la cuenta

1. Entra a <https://www.oracle.com/cloud/free/> → *Start for free*.
2. **Región de inicio (Home Region): Chile (Santiago) — `sa-santiago-1`.** No se puede cambiar. Si no aparece como opción, la más cercana disponible; anótala, porque cambia los nombres de la consola.
3. Verifica correo, teléfono y tarjeta. La cuenta tarda de minutos a horas en quedar activa.

### 6.2 Subir a «Pay As You Go» (PAYG)

Oracle recupera las VMs gratuitas con uso muy bajo y las cuentas gratuitas fallan más al pedir capacidad ARM. *Menú → Billing & Cost Management → Upgrade and Manage Payment → Upgrade to Pay As You Go.* **Subir a PAYG no amplía el cupo**: lo gratuito sigue gratis; solo lo que *superes* se cobra. Anota la fecha en que confirmaste las cifras del cupo en la consola (*Governance → Limits, Quotas and Usage*).

### 6.3 Presupuesto y alerta de 1 USD (hazlo ya)

*Billing & Cost Management → Budgets → Create Budget*: monto mensual **1 USD**, alerta al **100 %** del gasto *real* a tu correo. Si te llega un correo, **entra y busca qué recurso se pasó del cupo**. La consola puede mostrar los montos en tu moneda (por ejemplo soles): entonces el «1» es 1 sol, un aviso más estricto, que sirve igual. **Revisa dos veces la dirección de correo.** Al crear una VM, el estimador puede mostrar un costo mensual por el volumen de inicio: es el precio de lista, sin descontar el cupo gratuito; compruébalo después en *Cost Analysis*.

**Cómo deshacer todo lo de Oracle:** *Compute → Instances → cada una → Terminate (marca «delete boot volume»)*, y luego la red (§7). Con eso no queda nada que cobre.

## 7. Paso 3 — Red privada y las dos VMs

### 7.1 La red (VCN)

**Crea la red antes de abrir el formulario de una VM** (sin ella no puedes elegir subred). *Menú → Networking → Virtual Cloud Networks → Start VCN Wizard → Create VCN with Internet Connectivity* → nombre `babelhub`, CIDR `10.0.0.0/16`, subred pública `10.0.0.0/24`. El asistente crea además una subred privada (`10.0.1.0/24`): déjala vacía.

La pantalla de resumen del asistente es **de solo lectura**. Las reglas se editan en la VCN `babelhub` → pestaña **Seguridad** → **Default Security List for babelhub** (la de la subred pública) → *Reglas de seguridad*.

Las VMs necesitan **salida** a Internet (bajar imágenes de Docker y conectarse a Cloudflare), pero **ningún puerto de entrada abierto a Internet** salvo SSH **desde tu IP**. Edita la **Security List** de la subred pública así:

*Ingress rules* (borra todas las que traiga el asistente y deja **solo** estas; «Source CIDR» = quién puede entrar):

| Origen | Protocolo / puerto destino | Para qué |
| :--- | :--- | :--- |
| `<tu IP pública>/32` (búscala en <https://ifconfig.me>) | TCP 22 | Tú → las dos VMs (la regla vale para toda la subred) |
| `10.0.0.10/32` (VM-VENTAS) | TCP 8081 | Nginx → réplica de gestión (VM-INVENTARIO) |
| `10.0.0.10/32` | TCP 8082 | E-commerce → Inventario (VM-INVENTARIO, por HTTPS) |
| `10.0.0.10/32` | TCP 5432 | E-commerce y Evolution API → PostgreSQL (VM-INVENTARIO, con TLS) |
| `10.0.0.20/32` (VM-INVENTARIO) | TCP 8443 | Inventario → E-commerce (VM-VENTAS, por HTTPS) |
| `10.0.0.20/32` | TCP 6380 | Réplica de gestión → Redis de contadores (VM-VENTAS, TLS) |
| `0.0.0.0/0` | ICMP tipo 3 código 4 | Avisos de tamaño de paquete: sin ellos se cortan conexiones grandes (es una regla que Oracle trae por defecto) |

**Si ya habías creado las reglas de la versión anterior:** borra **solo** las que tengan origen `10.0.0.30/32` o `10.0.0.40/32` y añade la de `10.0.0.10/32` al puerto 8081. No borres la de tu IP (TCP 22).

*Egress:* deja la regla `0.0.0.0/0` todos los protocolos (salida libre). **No la borres** al limpiar las de entrada: sin ella las VMs no salen a Internet.

⚠️ **Esta lista de seguridad es la única barrera desde Internet.** El cortafuegos de la imagen de Oracle (paso 8.1) protege el SSH del propio sistema, pero **los puertos que publica Docker no pasan por él**, y publicar solo en la IP privada tampoco basta: Oracle entrega a esa IP lo que llega a la pública. **No abras nunca 80, 443, 5432, 5678, 6380, 8081, 8082 ni 8443 a `0.0.0.0/0`**, ni «un momento» para probar.

> **Nota:** la entrada pública no necesita ninguna regla: `cloudflared` abre el túnel **hacia fuera**, y de él a Nginx y al E-commerce se va por redes internas de Docker, sin publicar puertos en la VM.

### 7.2 Las llaves SSH (en tu equipo)

**[Tu equipo]**

```bash
ssh-keygen -t ed25519 -f ~/.ssh/babelhub_oracle -C "babelhub-oracle"
```

Te pide una **frase de paso**: ponla (protege la llave si te roban el equipo). **Qué debes ver:** dos archivos, `~/.ssh/babelhub_oracle` (**privada: jamás la copies, envíes ni subas**) y `~/.ssh/babelhub_oracle.pub` (pública: esta sí se pega en Oracle). `cat ~/.ssh/babelhub_oracle.pub` imprime una línea `ssh-ed25519 AAAA… babelhub-oracle`.

En Windows basta PowerShell (no hace falta WSL):

```powershell
New-Item -ItemType Directory -Force "$env:USERPROFILE\.ssh" | Out-Null
ssh-keygen -t ed25519 -f "$env:USERPROFILE\.ssh\babelhub_oracle" -C "babelhub-oracle"
Get-Content "$env:USERPROFILE\.ssh\babelhub_oracle.pub"
```

### 7.3 Crear las dos VMs

*Menú → Compute → Instances → Create instance* (repite dos veces):

| Campo | VM-INVENTARIO | VM-VENTAS |
| :--- | :--- | :--- |
| Name | `vm-inventario` | `vm-ventas` |
| Image | Canonical **Ubuntu 24.04** (aarch64) | Ubuntu 24.04 (aarch64) |
| Shape | *Ampere* → `VM.Standard.A1.Flex`, **1 OCPU, 6 GB** | ídem |
| Subred | `babelhub` pública | ídem |
| IP pública | *Assign a public IPv4* | ídem |
| IP privada | *Show advanced options → Networking → Private IPv4 address*: `10.0.0.20` | `10.0.0.10` |
| SSH keys | *Paste public keys* → pega `babelhub_oracle.pub` | ídem |
| Boot volume | **47 GB** (el mínimo; no marques volumen extra) | ídem |

(La IP pública sirve para tu SSH y para que las VMs *salgan* a Internet; la lista de seguridad impide que entre nadie más.)

**Detalles del formulario** (vistos en la consola el 2026-11-25):

- Elige **Ubuntu 24.04**, no 26.04. En ARM debe ser la versión `aarch64` (compruébalo luego con `uname -m`); en ARM la «instancia blindada» no existe y su interruptor se deja apagado.
- En *Claves SSH* la opción marcada por defecto genera una llave nueva: cámbiala a **«Pegar clave pública»**.
- El *nombre de VNIC* queda como nombre del equipo (`vnic-ventas` aparece en el prompt). Pon el que quieras ver ahí.
- Para los 47 GB deja **apagado** el tamaño personalizado del volumen de inicio: usa 46,6 GB.

**«Out of host capacity» / «Out of capacity for shape»** es muy común en ARM. No es un error tuyo. Pruebas: otro *Availability Domain* (AD-1, AD-2, AD-3) en el mismo formulario; reintentar cada pocos minutos, de madrugada o en fines de semana; subir a PAYG (§6.2). Puede tardar horas o días. Las **AMD micro** tampoco tenían capacidad en Santiago el 2026-11-25: por eso la guía usa solo las dos ARM. Santiago tiene un solo *Availability Domain*, así que prueba los *dominios de errores* (FD-1 a FD-3). No cambies a una forma de pago sin decidirlo.

Para automatizar los reintentos usa **Cloud Shell** (icono `>_` de la consola, con la CLI ya autenticada) y repite `oci --no-retry compute instance launch …` con una pausa de 30 s hasta que termine bien. `--no-retry` hace que cada intento muestre enseguida su error. **No pases `--boot-volume-size-in-gbs`**: por la CLI el mínimo es 50 GB y, sin la opción, se usa el tamaño de la imagen. Si el error no es de capacidad, detén el bucle (`Ctrl+C`).

**Qué debes ver:** las dos instancias en estado **Running** con su IP privada y su IP pública. Anota las **IP públicas** (solo para entrar por SSH).

**Cómo deshacer:** *Instance → More actions → Terminate* y marca «Permanently delete the attached boot volume».

### 7.4 Entrar por SSH

Edita `~/.ssh/config` en **[Tu equipo]** (créalo si no existe: `nano ~/.ssh/config`):

```
Host babelhub-inventario
    HostName <IP pública de vm-inventario>
    User ubuntu
    IdentityFile ~/.ssh/babelhub_oracle
    ForwardAgent no

Host babelhub-ventas
    HostName <IP pública de vm-ventas>
    User ubuntu
    IdentityFile ~/.ssh/babelhub_oracle
    ForwardAgent no
```

```bash
chmod 600 ~/.ssh/config
ssh babelhub-ventas      # primera vez: escribe «yes» a la huella; después pide tu frase de paso
```

**Qué debes ver:** `ubuntu@vm-ventas:~$`. Sal con `exit`. Repite con `ssh babelhub-inventario`. **Si no entra:** revisa que la IP pública sea la de la VM (no la de tu casa), la regla de entrada TCP 22 con *tu* IP actual (cambia si tu proveedor cambia tu IP; en PowerShell, tu IPv4 es `(Invoke-RestMethod https://api.ipify.org).Trim()`) y que la llave pública sea la correcta. Cambiar la regla no afecta a la llave.

## 8. Paso 4 — Preparar cada VM (Ubuntu, cortafuegos, Docker)

Hazlo **en las dos VMs** (`ssh babelhub-<nombre>`). Cada bloque se pega completo.

### 8.1 Actualizar, tiempo y cortafuegos

**[VM-X]**

```bash
sudo apt-get update && sudo apt-get -y full-upgrade
sudo apt-get install -y unattended-upgrades chrony ca-certificates curl git
sudo dpkg-reconfigure -plow unattended-upgrades     # responde «Yes»: parches de seguridad automáticos
sudo systemctl enable --now chrony                  # reloj sincronizado: las credenciales duran 15 minutos
chronyc tracking                                    # «Leap status: Normal»
sudo reboot                                         # espera 2 o 3 minutos y vuelve a entrar
```

Si la actualización pregunta por un archivo de configuración modificado, **conserva la versión local**. Aunque se anuncie una versión nueva de Ubuntu, **no ejecutes `do-release-upgrade`**.

**Cortafuegos del sistema: deja el de Oracle. No instales `ufw` ni borres `iptables-persistent`.** [Oracle indica gestionar estas imágenes con `iptables`](https://blogs.oracle.com/developers/post/enabling-network-traffic-to-ubuntu-images-in-oracle-cloud-infrastructure) y hay un [fallo registrado de `ufw` que impide arrancar](https://bugs.launchpad.net/ufw/+bug/1946804) con el disco de arranque en red. Comprueba (solo lee):

```bash
sudo iptables -S INPUT
sudo systemctl is-enabled netfilter-persistent
```

**Qué debes ver:** aceptar conexiones ya establecidas, ICMP, `lo` y TCP 22, terminar en `-j REJECT` y `enabled`. **No ejecutes `netfilter-persistent save`** (guardaría las reglas temporales de Docker). Los puertos que publica Docker **no pasan por `INPUT`**: desde Internet solo los protege la Security List de Oracle (§7.1), que también es la que limita desde dónde se entra por SSH (los Compose publican solo en la IP privada o en `127.0.0.1`, lo que evita otras interfaces pero no sustituye a la lista).

### 8.2 Endurecer SSH y comprobar el disco

```bash
sudo tee /etc/ssh/sshd_config.d/60-babelhub.conf >/dev/null <<'EOF'
PermitRootLogin no
PasswordAuthentication no
KbdInteractiveAuthentication no
X11Forwarding no
AllowAgentForwarding no
EOF
sudo sshd -t && sudo systemctl reload ssh
swapon --show          # debe salir VACÍO (sin swap: los secretos en memoria no se escriben a disco). Si sale algo: sudo swapoff -a  y borra la línea «swap» de /etc/fstab
lsblk -f
```

**Qué debes ver:** `sshd -t` sin mensajes; `swapon --show` vacío. (El swap desactivado es obligatorio en el **entorno de restauración de ensayo**, PLA-39 y T05; en las VMs de producción es una buena práctica.) **No cierres tu sesión SSH actual hasta probar que puedes abrir otra** en una terminal nueva.

El cifrado en reposo de los discos de Oracle está siempre activo y no se ve desde dentro; en el paso 14 lo verificas con la CLI.

### 8.3 Instalar Docker

En **las dos VMs**:

```bash
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" | sudo tee /etc/apt/sources.list.d/docker.list >/dev/null
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo docker version --format '{{.Server.Version}} {{.Server.Arch}}'
sudo docker compose version
sudo docker run --rm hello-world | head -3
```

**Qué debes ver:** una versión de Docker 28 o más y `arm64` (en las ARM) o `amd64`; `Docker Compose version v2…` o posterior (el 2026-11-25 se instaló la v5.8.0); y `Hello from Docker!`. **No** te agregues al grupo `docker` (equivale a ser administrador): usa `sudo`.

### 8.4 Traer el código de los scripts

Los scripts y los Compose viven en el repositorio. Se clonan como **root** (los scripts de VM-INVENTARIO deben ser de root, lo comprueba `desplegar.sh`) y se fija la versión:

```bash
sudo git clone https://github.com/<propietario>/BabelHub.git /opt/babelhub
cd /opt/babelhub && sudo git checkout v0.1.0
sudo git log -1 --format='%H %s'        # debe ser el commit de tu etiqueta
```

(Repo público: no pide contraseña. Para actualizar: `cd /opt/babelhub && sudo git fetch --tags && sudo git checkout v0.1.1`.)

## 9. Paso 5 — El almacén de objetos de Oracle

Guarda los **libros digitales**, las **portadas** y el **registro de supresiones**. Los **respaldos cifrados** van a Cloudflare R2 (§9b). Cada tarea tiene su propia credencial con permisos mínimos (`infra/almacen/politicas.md`).

**[Tu equipo]** — configura la CLI una vez: `oci setup config` (te pide el OCID de tu usuario y tenancy, que están en *Perfil → Tenancy/User settings*, y crea una llave de API que subes en *User settings → API keys*). Comprueba con `oci iam region list`.

1. Datos que necesitas: el **espacio de nombres** (`oci os ns get`) y el **OCID del compartimento** (crea uno llamado `babelhub`: *Identity → Compartments*).
2. Mira lo que se va a hacer, **sin ejecutar nada**:

   ```bash
   cd ~/BabelHub
   bash scripts/almacen.sh plan --compartimento-id <ocid> --compartimento-nombre babelhub --espacio <espacio-de-nombres>
   ```

3. Aplícala (la retención de los tres contenedores `supresiones-*` lleva bloqueo de 100 días, **irreversible** a los 15 días: revisa antes el plan):

   ```bash
   bash scripts/almacen.sh aplicar --compartimento-id <ocid> --compartimento-nombre babelhub --espacio <espacio-de-nombres>
   ```

   **Debes ver** los cinco contenedores creados: `libros`, `portadas` y `supresiones-entradas`, `-sellos` y `-copia`. No crees ningún `respaldos-*` en Oracle. Si habías creado a mano alguno de los cinco, el script lo respeta y solo le añade lo que falta.
4. Las políticas de acceso: `bash scripts/almacen.sh politicas-iam --compartimento-nombre babelhub` imprime las sentencias; créalas en *Identity → Policies*. Cada credencial de `infra/almacen/credenciales.txt` es un **usuario de IAM** propio (por ejemplo `bh-biblioteca-lector`) dentro de un grupo con esa política; para cada usuario crea una **Customer Secret Key** (*Usuario → Customer secret keys → Generate*): te da una **clave de acceso** y una **clave secreta** (la secreta se muestra **una sola vez**: cópiala a tu gestor de contraseñas en el acto).
5. Comprueba: `bash scripts/almacen.sh comprobar --espacio <espacio-de-nombres>`. Durante los primeros 15 días termina con **código 3** («bloqueo pendiente»): es lo esperado; repítelo pasada la fecha que mostró `aplicar`. El endpoint S3 de las credenciales es `https://<espacio-de-nombres>.compat.objectstorage.sa-santiago-1.oraclecloud.com`.

Qué credencial va a qué archivo (§10): `catalogo-archivos`, `biblioteca-lector` y `supresiones-registro` → `ecommerce.env` y `ecommerce-gestion.env`; `registro-lector` → solo tu equipo (verificar la copia del registro al restaurar). Las credenciales de vigilancia (`registro-vigilante`, `supresiones-sello`, `respaldos-copia-registro` y `cuota-lectura`) son de VM-AUX: **no las crees mientras esté aplazada**.

**Cómo deshacer:** los contenedores con bloqueo ya aplicado no se pueden vaciar antes de su fecha; por eso la regla de oro de no «probar» con esta etapa fuera de la guía.

## 9b. Paso 5 bis — Los respaldos en Cloudflare R2

Cuatro contenedores en R2 reciben la copia diaria del clúster y el archivo de cambios, **comprimidos y cifrados** en VM-INVENTARIO antes de salir: `respaldos-diarios` (7 días), `-semanales` (28), `-mensuales` (92) y `respaldos-wal` (7). Cada uno impide borrar o sobrescribir durante su plazo y retira solo lo vencido (puede tardar hasta un día más). Medición local: unos 1,9 de los 10 GB gratuitos con venta ligera (`progress/ensayos/medicion-respaldos-comprimidos-2026-11-25.md`).

**Qué pierdes frente a Oracle** (riesgo aceptado y provisional): no hay credencial de «solo crear» (la de la VM puede leer, listar y subir objetos nuevos en esos cuatro contenedores) y el bloqueo lo puede **quitar quien administre tu cuenta de Cloudflare**. Esa misma cuenta lleva el dominio y el túnel: protégela como la clave de respaldos.

### 9b.1 Protege la cuenta antes de nada

1. **Segundo factor:** Cloudflare → *My Profile → Authentication → Two-Factor Authentication* → actívalo y guarda los códigos de recuperación fuera de línea. Usa para la cuenta un correo **distinto** del que recibe los avisos del sistema.
2. Activa **R2** (*R2 Object Storage* en el panel). Si pide un medio de pago, léelo: el cupo gratuito es de 10 GB al mes y lo que pase **se cobra**.
3. **Aviso de facturación:** *Notifications → Add* → elige una alerta de facturación o de uso (por ejemplo, de facturación por uso) con **tu correo** como destinatario y un umbral bajo. **Si tu cuenta no ofrece ninguna, para y avisa**: la spec la exige (PLA-06) y habría que decidir otra cosa.
4. Anota el **Account ID** (aparece en la portada de R2; 32 caracteres). No es un secreto.

### 9b.2 Crea los contenedores y sus reglas

1. Crea un **token temporal de administración**: *My Profile → API Tokens → Create Token → Custom token*: permiso **Account → Workers R2 Storage → Edit**, solo tu cuenta, con fecha de fin **hoy o mañana**. Se muestra una vez; no lo guardes.
2. **[Tu equipo, en tu propia terminal]** (el script se niega si detecta un agente o una integración continua):

   ```bash
   cd ~/BabelHub
   bash scripts/almacen.sh r2-plan                                                  # lo que se va a enviar; no llama a nadie
   CLOUDFLARE_ACCOUNT_ID=<account-id> bash scripts/almacen.sh r2-aplicar --ubicacion enam
   ```

   Pega el token cuando lo pida (no se muestra). `--ubicacion` es opcional (`enam` es el este de Norteamérica; R2 no ofrece Sudamérica) y solo cuenta al crear.
3. **Debes ver** `ok respaldos-diarios`, `ok respaldos-semanales`, `ok respaldos-mensuales`, `ok respaldos-wal` y «R2 coincide con la definición».
4. **Revoca el token** (*API Tokens → … → Delete*). Para comprobar más adelante crea otro de **solo lectura** (*Workers R2 Storage → Read*), ejecuta `r2-comprobar` y revócalo.

**Si falla:** «estado 403» = el token no tiene el permiso o no es de esa cuenta; «ya tiene un bloqueo distinto» = alguien lo cambió: revísalo en el panel (el script no lo toca). **Cómo deshacer:** un contenedor con objetos bloqueados no se puede vaciar hasta que venzan (o hasta que quites la regla en el panel).

### 9b.3 Las credenciales de objetos

*R2 → Manage R2 API Tokens → Create API token*. En cada una, **«Apply to specific buckets only»** con **los cuatro contenedores `respaldos-*`**, y nunca un permiso «Admin»:

| Token | Permiso | Dónde va |
| :--- | :--- | :--- |
| `respaldos-escritor` | *Object Read & Write* | `postgres.env` de VM-INVENTARIO (§10.2) |
| `respaldos-lector` | *Object Read only* | Solo tu equipo: la comprobación diaria (§14) y restaurar. Rótalo después de cada ensayo con datos reales. |

(`respaldos-vigilante` es de VM-AUX: no lo crees mientras esté aplazada.) De cada uno copia **Access Key ID** y **Secret Access Key** a tu gestor de contraseñas, y del escritor también el **Token value** (solo lo usa la prueba de §9b.5); el endpoint es `https://<account-id>.r2.cloudflarestorage.com`.

### 9b.4 Revisa quién puede tocar R2

En *My Profile → API Tokens* (y *Manage Account → Account API Tokens*) no debe quedar **ningún** token con permiso de edición de R2, ni haber uno en las VMs, el repositorio o GitHub. Anótalo con la fecha en `progress/ensayos/AAAA-MM-DD-cuenta-r2.md` junto con el segundo factor y el aviso de facturación (sin valores secretos).

### 9b.5 Prueba tu cuenta antes de poner datos que importen (obligatorio, T47)

**[Tu equipo]**, con las claves de `respaldos-escritor` (usa `respaldos-wal`: lo que dejes allí se retira solo a los 7 días):

```bash
export RCLONE_CONFIG_PRUEBA_TYPE=s3 RCLONE_CONFIG_PRUEBA_PROVIDER=Cloudflare RCLONE_CONFIG_PRUEBA_REGION=auto
export RCLONE_CONFIG_PRUEBA_ENDPOINT=https://<account-id>.r2.cloudflarestorage.com
read -r -p "Access Key ID: " RCLONE_CONFIG_PRUEBA_ACCESS_KEY_ID; read -r -s -p "Secret Access Key: " RCLONE_CONFIG_PRUEBA_SECRET_ACCESS_KEY; echo
export RCLONE_CONFIG_PRUEBA_ACCESS_KEY_ID RCLONE_CONFIG_PRUEBA_SECRET_ACCESS_KEY
echo uno > /tmp/uno.txt && echo "este es el segundo contenido" > /tmp/dos.txt
o="--s3-no-check-bucket --ignore-times --retries 1 -v"     # --ignore-times: sin él rclone puede no intentar la subida
a=prueba:respaldos-wal/prueba-t47-a; b=prueba:respaldos-wal/prueba-t47-b
rclone copyto $o /tmp/uno.txt $a && echo "bien: subió" || echo "MAL"
rclone copyto $o /tmp/dos.txt $b && echo "bien: subió" || echo "MAL"
rclone deletefile --retries 1 -v $a && echo "MAL: se pudo borrar" || echo "rechazado"
rclone copyto $o /tmp/dos.txt $a && echo "MAL: se sobrescribió" || echo "rechazado"
rclone copyto $o --s3-upload-cutoff 0 /tmp/dos.txt $a && echo "MAL: se sobrescribió en varias partes" || echo "rechazado"
rclone copyto $o $b $a && echo "MAL: se sobrescribió copiando" || echo "rechazado"
rclone cat $a                                              # DEBE decir «uno»
rclone lsd prueba:                                         # no debe mostrar contenedores ajenos a respaldos-*
```

**En cada «rechazado» debe verse encima el motivo `ObjectLockedByBucketPolicy` o un error 409**; un rechazo por otra causa (clave mal pegada, 403, red) no prueba el bloqueo.

**Ese token no lee ni cambia las reglas.** Con el *Token value* de `respaldos-escritor`, sin que quede en el historial (lo que se envía en los `PUT` es la regla que ya hay: si se aceptara, no cambiaría nada):

```bash
read -r -s -p "Token value del escritor: " v; echo
api=https://api.cloudflare.com/client/v4; cuenta=<account-id>
printf 'header = "Authorization: Bearer %s"\n' "$v" | curl --disable -s --config - $api/user/tokens/verify; echo
pedir() { printf 'header = "Authorization: Bearer %s"\n' "$v" | curl --disable -s -o /dev/null -w "$1 $2 -> %{http_code}\n" --config - -X "$1" ${3:+-H "Content-Type: application/json" --data "$3"} "$api/accounts/$cuenta/r2/buckets/respaldos-wal$2"; }
pedir GET /lock
pedir GET /lifecycle
pedir PUT /lock '{"rules":[{"id":"retencion-7-dias","enabled":true,"condition":{"type":"Age","maxAgeSeconds":604800}}]}'
pedir PUT /lifecycle '{"rules":[{"id":"retiro-a-los-7-dias","enabled":true,"conditions":{"prefix":""},"deleteObjectsTransition":{"condition":{"type":"Age","maxAge":604800}},"abortMultipartUploadsTransition":{"condition":{"type":"Age","maxAge":86400}}}]}'
unset v pedir
```

La primera respuesta debe decir `"status":"active"` (si el panel lo creó como token de cuenta, usa `…/accounts/<account-id>/tokens/verify`) y las cuatro líneas siguientes deben terminar en **403**: un 200 es un fallo grave; un 400 o un 401 no prueban nada.

**No alcanza otro contenedor.** Crea en el panel un contenedor vacío `t47-ajeno` y comprueba que `rclone lsf prueba:t47-ajeno` y `rclone copyto $o /tmp/uno.txt prueba:t47-ajeno/x` **fallan** (403 / `AccessDenied`); bórralo al terminar. Y que las claves de `catalogo-archivos` (Oracle) no abren R2 ni las de R2 abren Oracle: define otro remoto con las claves de unas y el endpoint del otro y `rclone lsd` debe fallar.

Anota cada resultado en `progress/ensayos/AAAA-MM-DD-T47-r2.md` (sin pegar claves). **Si algo que debía fallar funcionó, no actives los respaldos y avisa.** Cierra la terminal al terminar.

## 10. Paso 6 — Los secretos (en tu equipo, nunca en el repositorio)

`scripts/generar-secretos.sh` crea **un archivo por servicio y por VM** con valores aleatorios (contraseñas, claves de firma, credenciales entre servicios, certificados de la red privada, el par de claves de cifrado de respaldos). Lo que solo puede dar un tercero (Gmail, Turnstile, almacén, túnel) queda como `CAMBIAR`.

**[Tu equipo]**

```bash
cd ~/BabelHub
bash scripts/generar-secretos.sh --destino ~/babelhub-secretos \
  --dominio babel-hub.shop --entorno staging \
  --ip-ventas 10.0.0.10 --ip-inventario 10.0.0.20
```

**TLS de PostgreSQL (AM24):** el generador también emite `tls-postgres-cert.pem` y `tls-postgres-key.pem` (solo en la carpeta de VM-INVENTARIO) y deja todas las cadenas de conexión con `SSL Mode=VerifyFull` y la de Evolution con `sslmode=require&sslaccept=strict&sslcert=/run/secrets/tls-ca.pem` (Prisma, el cliente de Evolution, ignora `verify-full`). No tienes que editar nada; `desplegar.sh` se niega a desplegar si alguna cadena no pide TLS verificado.

`--entorno staging` (el valor por defecto) deja el E-commerce en **`Staging` sin pasarela (`ninguna`)**, la tienda que revisa Culqi (fase 1, §15.1); añade `--pasarela simulador` solo si quieres la pasarela simulada (§15.1, «Alternar la pasarela en Staging»); `--entorno production` (cuando tengas las llaves) lo deja en `Production` con `culqi` y te pide la llave `sk_live_…`. El dominio es `babel-hub.shop`: la tienda atiende `babel-hub.shop` y `www.babel-hub.shop`, y la gestión `gestion.babel-hub.shop` (§2.1).

**Qué debes ver:** «Listo. Se generaron los secretos de las dos VMs» y «Falta completar N valores «CAMBIAR»». Se crean `inventario/`, `ventas/`, `custodia/` y `PENDIENTES.txt`. Si la carpeta ya existe con archivos, el script **se niega** (no pisa secretos). **Si generaste tus secretos con una versión anterior** (la que creaba una carpeta `borde/` y pedía `--ip-borde`), vuelve a generarlos en una carpeta nueva: cambió en quién confía cada servicio.

### 10.1 Custodia: lo más importante de toda la guía

`~/babelhub-secretos/custodia/` tiene **la clave privada de `age`** (descifra TODOS los respaldos, que contienen datos personales) y **la llave de la CA privada**. **Nunca van a una VM.**

1. Copia `custodia/age-clave-privada.txt` a **dos lugares distintos fuera de línea** (por ejemplo dos memorias USB guardadas en sitios diferentes). Sin esta clave, los respaldos son **irrecuperables**.
2. Llena `infra/respaldos/custodia.md` (titular, destinatario público `age1…`, dónde están las dos copias) y comprueba: `bash scripts/comprobar-custodia.sh --entorno ~/babelhub-secretos/inventario/postgres.env`.
3. En Windows/WSL los permisos `600` pueden **no aplicarse** si la carpeta está bajo `/mnt/c`: genera los secretos dentro del sistema de archivos de WSL (`~/…`), no en `C:\`. `desplegar.sh` los vuelve a comprobar en la VM.

### 10.2 Completar los `CAMBIAR`

```bash
cat ~/babelhub-secretos/PENDIENTES.txt          # la lista, por archivo
nano ~/babelhub-secretos/inventario/ecommerce-gestion.env
```

| Qué | De dónde sale |
| :--- | :--- |
| `…Smtp__Servidor`, `…Smtp__Cuenta`, `…Smtp__Remitente`, `…Smtp__Credencial` | Gmail: servidor `smtp.gmail.com`; cuenta y remitente tu Gmail; credencial = la **contraseña de aplicación** (16 letras; escríbela sin espacios). |
| `…Turnstile__ClaveDeSitio` y `…Secreto` | **Créalo ahora, antes de copiar las carpetas** (VM-INVENTARIO ya lo exige y no necesita el túnel): Cloudflare → *Turnstile → Add site* (está en el menú de la cuenta, no dentro del dominio; modo **Managed** y *pre-clearance* apagado), hostnames `babel-hub.shop`, `www.babel-hub.shop` y `gestion.babel-hub.shop`; clave de sitio (`0x4…`, pública) y clave secreta, en `ecommerce.env` y `ecommerce-gestion.env`. |
| `…Almacen__Catalogo__*`, `…Biblioteca__*`, `…Supresiones__*` | §9: las claves de cada credencial; `Endpoint` = `https://<espacio>.compat.objectstorage.sa-santiago-1.oraclecloud.com`. |
| `…Web__OrigenDelAlmacen` (ventas) | El origen `https://<espacio>.compat.objectstorage.sa-santiago-1.oraclecloud.com` (igual al host del endpoint de `biblioteca-lector`). |
| `postgres.env`: `RCLONE_CONFIG_ALMACEN_ENDPOINT`, `…ACCESS_KEY_ID`, `…SECRET_ACCESS_KEY` | §9b.3: el endpoint de R2 y las claves de **`respaldos-escritor`** (el proveedor `Cloudflare` y la región `auto` ya vienen escritos). Nunca el token de administración. |
| `aviso.env` (inventario) | Un correo propio de esa VM para el aviso del espacio reservado: `AUX_SMTP_URL=smtps://smtp.gmail.com:465` (**`smtps://`**, con TLS), usuario, contraseña de aplicación, remitente y destinatario (tú). |
| `…Contacto__Telefono`, `…Contacto__Correo`, `…Contacto__Direccion` (`ecommerce.env` y `ecommerce-gestion.env`) | **El teléfono, el correo y la dirección de contacto que mostrará la tienda** (Culqi exige contacto visible). Son **datos personales tuyos: nunca los escribas en el repositorio**; solo van aquí. `desplegar.sh` no continúa mientras digan `CAMBIAR`. |
| `…Pagos__LlavePrivada` | **Solo con `--entorno production`:** tu llave `sk_live_…` de Culqi. En `Staging` **no** lleva llave (el script se niega si la hay). |
| `cloudflared.env` (ventas) | El token del túnel (§12.3). |

**El entorno y la pasarela (`ASPNETCORE_ENVIRONMENT` y `BABELHUB__Pagos__Pasarela`) ya vienen escritos** por el generador en `ecommerce.env` y `ecommerce-gestion.env` y son lo único que cambia al pasar de `Staging` a `Production` (§15.1, fase 2). `desplegar.sh` se niega si faltan, si no son coherentes (`Staging` con `ninguna` o `simulador`; `Production` con `culqi` y una llave `sk_live_…`) o si el entorno no es ninguno de los dos. **Si generaste tu carpeta antes del 2026-11-26, dice `simulador`:** para la revisión de Culqi cámbiala a `ninguna` en los dos archivos (§15.1, «Alternar la pasarela en Staging»).

Edita con `nano`, guarda con `Ctrl+O`, `Enter`, y sal con `Ctrl+X`. No dejes comillas ni espacios alrededor del `=`.

### 10.3 Copiar cada carpeta **solo** a su VM

```bash
cd ~/babelhub-secretos
scp -r inventario babelhub-inventario:/tmp/secretos-inventario
scp -r ventas     babelhub-ventas:/tmp/secretos-ventas
```

**Nunca** copies la carpeta `~/babelhub-secretos` entera ni `custodia/`: `desplegar.sh` se niega a desplegar si ve una clave privada ajena en la carpeta de la VM. En cada VM, deja los secretos fuera del repositorio y solo legibles por root:

**[VM-X]** (cambia `inventario` por `ventas`)

```bash
sudo mkdir -p /etc/babelhub
sudo rm -rf /etc/babelhub/secretos
sudo mv /tmp/secretos-inventario /etc/babelhub/secretos
sudo chown -R root:root /etc/babelhub/secretos
sudo chmod 700 /etc/babelhub/secretos && sudo chmod 600 /etc/babelhub/secretos/*
sudo ls -l /etc/babelhub/secretos
```

**Debes ver** todos los archivos `-rw-------  root root`.

**Cómo deshacer / rotar:** vuelve a generar en una carpeta **nueva** (el script nunca pisa) y repite; para rotar credenciales de una VM en marcha: `scripts/rotar-credenciales.sh` (ver su cabecera).

## 11. Paso 7 — Etapa 1: VM-INVENTARIO

Corre PostgreSQL, el Inventario y la réplica de gestión. Es la VM más crítica (guarda la base de datos).

**[VM-INVENTARIO]** — pega tus tres líneas de digest del paso 5 (con los valores reales):

```bash
cd /opt/babelhub
sudo env \
  BABELHUB_SECRETOS=/etc/babelhub/secretos \
  IP_PRIVADA=10.0.0.20 \
  IP_VENTAS=10.0.0.10 \
  IMAGEN_POSTGRES=ghcr.io/<propietario>/babelhub-postgres@sha256:<64> \
  IMAGEN_INVENTARIO=ghcr.io/<propietario>/babelhub-inventario@sha256:<64> \
  IMAGEN_ECOMMERCE=ghcr.io/<propietario>/babelhub-ecommerce@sha256:<64> \
  scripts/desplegar.sh inventario
```

(`IP_VENTAS` es la dirección de VM-VENTAS: la réplica de gestión solo cree la IP del cliente que le llega desde ella.)

(La réplica de gestión del E-commerce corre en **`Staging`**, igual que la tienda: ya arranca sin el adaptador de Culqi, §0. Si prefieres desplegar primero solo PostgreSQL y el Inventario, agrega `--sin ecommerce-gestion`; la migración de la base del E-commerce corre igual, porque la hace el script.)

**Qué debes ver, en este orden:**

1. Descarga de las imágenes (`Pulling…`).
2. `Migrando las bases (esquemas y roles)...` y líneas `paso: roles`, `paso: plataforma`… hasta `migracion: inventario al día (… pasos, … cuentas)` y `migracion: ecommerce al día (15 pasos, 3 cuentas)` (el paso 15 es `textos-vigentes`: siembra, **una sola vez y sin pisar nada**, la versión de los textos legales de `migrador.env`; sin esa fila nadie podría aceptar los textos ni comprar).
3. `ok postgres`, `ok inventario`, `ok ecommerce-gestion`, `ok tarea diaria de respaldo (/etc/cron.d/babelhub-respaldo)` y `VM inventario desplegada y sana.`

**Si falla:** el script **nombra** cada cosa que falta («falta la clave X en postgres.env», «aviso.env es de … debe ser de root», «CAMBIAR sin completar»). Corrige y vuelve a ejecutar: **es idempotente** (repetirlo no rompe nada). Si dice «no es una imagen publicada con su digest», copiaste mal una línea del paso 5.

### 11.1 Comprobaciones

```bash
sudo docker ps --format 'table {{.Names}}\t{{.Status}}'                                  # todos «(healthy)»
sudo docker logs --tail 20 babelhub-inventario-postgres-1                                # sin FATAL
sudo docker exec babelhub-inventario-postgres-1 pg_isready -d postgres                   # «accepting connections»
# El Inventario por HTTPS, validando el certificado con tu CA privada:
sudo docker run --rm --network babelhub-inventario_default -v /etc/babelhub/secretos/tls-ca.pem:/ca.pem:ro \
  curlimages/curl@sha256:3fb7e13e5d179a845c97d78204f3f1064d1907037f26a3ebf91710571151fd93 \
  -sS --cacert /ca.pem https://inventario:8443/health/ready
```

**Debes ver** `{"estado":"listo","componentes":[{"nombre":"base-de-datos","estado":"ok"}]}`. En los registros de PostgreSQL **no** debe haber errores de `respaldos:`; si los hay, revisa las claves de R2 en `postgres.env` (mientras falle, los cambios se acumulan en la VM hasta unos 4 GB y recibirás avisos del espacio reservado).

**Primer respaldo y su comprobación:**

```bash
sudo docker exec babelhub-inventario-postgres-1 /opt/babelhub/respaldar.sh --bajo-demanda
```

Debe terminar sin error y decir `respaldos: subido respaldos-diarios/AAAA-MM-DD/…tar.zst.age` (dos veces: el clúster y la configuración). En el panel de R2, `respaldos-wal` recibe un objeto `…zst.age` cada pocos minutos. **Un respaldo que nunca se restauró no es un respaldo**: el spike T01 (§15.4) lo comprueba.

**Cómo deshacer** (sin perder datos): `cd /opt/babelhub/infra && sudo env BABELHUB_SECRETOS=… IP_PRIVADA=… IP_VENTAS=… IMAGEN_…=… docker compose -f docker-compose.inventario.yml down` (**sin `-v`**).

## 12. Paso 8 — Etapa 2: Cloudflare, el dominio, el túnel y Access (antes de VM-VENTAS)

### 12.1 Pasar el dominio de Spaceship a Cloudflare

> **Tu dominio ya está en Cloudflare, con SSL** (2026-11-23): si ves «Great news! Cloudflare is now protecting your site» en el *Overview* de `babel-hub.shop`, **salta este apartado** y sigue en §12.2.

1. Cloudflare → *Add a domain* → tu dominio → plan **Free** → *Continue*. Importará los registros DNS que ya tengas; revísalos.
2. Cloudflare te muestra **dos nameservers** (por ejemplo `xxx.ns.cloudflare.com` y `yyy.ns.cloudflare.com`).
3. **Spaceship** → *Domains* → tu dominio → *Nameservers* → *Custom nameservers* → pega los dos de Cloudflare → guardar. Si tienes **DNSSEC** activo en Spaceship, desactívalo primero (si no, el dominio deja de resolver).
4. Espera. Cloudflare te envía un correo cuando el dominio queda **Active** (de minutos a 24 horas). **No sigas hasta verlo activo.**

**Qué debes ver:** en Cloudflare, *Overview* del dominio con «Great news! Cloudflare is now protecting your site».

### 12.2 TLS y seguridad básica (certificado válido: requisito de Culqi)

En *SSL/TLS → Overview*: modo **Full (strict)**; *SSL/TLS → Edge Certificates*: **Always Use HTTPS = On**, **Minimum TLS Version = 1.2**, **Automatic HTTPS Rewrites = On**. El certificado del borde es **Universal SSL** (gratuito y válido) y se emite solo cuando el dominio está activo. *Security → Settings*: **Bot Fight Mode = On**. *Security → WAF → Managed rules*: activa el conjunto gratuito de Cloudflare. Culqi exige **SSL en todas las URLs** y que la URL de sus avisos tenga un certificado válido: lo da este paso.

**Qué hace cada ajuste y qué hace la aplicación por su cuenta** (revisado en el código el 2026-11-23, con pruebas):

| Pieza | Qué ocurre |
| :--- | :--- |
| **Always Use HTTPS** | Cloudflare responde `301` a todo `http://` **antes** de que llegue al túnel: es la regla que ve el público. |
| **Full (strict)** | Cloudflare exige un certificado válido en el origen. Con un **túnel** la conexión Cloudflare ↔ origen ya va cifrada por el propio túnel, así que este modo no cambia nada en esta arquitectura; se deja en *strict* por si algún día se publica un origen sin túnel. |
| **El túnel y Nginx** | Reciben HTTPS en Cloudflare y entregan HTTP **dentro** de VM-VENTAS (y, para la gestión, hacia VM-INVENTARIO por la red privada); Nginx pasa tal cual la cabecera `X-Forwarded-Proto` que fija Cloudflare (la aplicación nunca ve la conexión del usuario). |
| **La aplicación** | Toma **solo** `X-Forwarded-Proto` para ver `https` (así las cookies `__Host-…` y el antiforgery funcionan); **redirige con `308` a `https://` lo que el borde declare `http`** aunque Always Use HTTPS no estuviera activo (defensa en profundidad; la salud no se redirige); envía **HSTS** (`max-age=31536000`) en toda respuesta; marca las cookies de sesión `Secure` y `HttpOnly`; y ninguna vista ni recurso propio pide nada por `http://` (no hay contenido mixto; lo verifica una prueba que barre las vistas y los recursos). |
| **HSTS de Cloudflare** | **No lo actives:** lo envía la aplicación (con dos hosts distintos, un `includeSubDomains` desde Cloudflare afectaría también a la gestión sin que lo decidas). |

**Apaga *Email Address Obfuscation*** (*Scrape Shield*): reescribe el correo de contacto visible de la tienda. El *Bot Fight Mode* gratuito **no admite excepciones**: si fallaran los avisos de Culqi, revísalo.

**Limpia el DNS antes del túnel:** borra los registros `A` aparcados del apex que importó Cloudflare (chocan con los `CNAME` del túnel). Si el dominio no envía correo, añade los `TXT` SPF `v=spf1 -all` en el apex y DMARC `v=DMARC1; p=reject` en `_dmarc`, para que nadie suplante tu dominio.

En la regla siguiente, usa la condición **equals**; si Cloudflare avisa de que falta un registro DNS para `www`, elige *Ignore and deploy rule anyway* (el registro lo crea el túnel).

**Redirigir `www` al apex** (para que la tienda tenga un solo nombre): *Rules → Redirect Rules → Create rule* → nombre `www-al-apex`; *When incoming requests match*: **Hostname equals `www.babel-hub.shop`**; *Then*: **Dynamic**, expresión `concat("https://babel-hub.shop", http.request.uri.path)`, código **301**, **Preserve query string**. (Se hace en Cloudflare y no en Nginx para que todo el que llegue vea una sola redirección en el borde.)

(Las comprobaciones con `curl` se hacen cuando la tienda ya responde: §13.3.)

### 12.3 El túnel

*Zero Trust* (primera vez: elige el plan gratis) → *Networks → Tunnels → Create a tunnel → Cloudflared* → nombre `babelhub` → en «Install and run connectors» **no ejecutes lo que te muestra**: solo copia el **token** (la cadena larga después de `--token`). Ese token es un secreto: ponlo en `ventas/cloudflared.env` (`TUNNEL_TOKEN=…`) y copia ese archivo a VM-VENTAS (§10.3).

*Public Hostnames* (agrega tres):

| Hostname | Servicio |
| :--- | :--- |
| `babel-hub.shop` | `HTTP` → `nginx:8080` |
| `www.babel-hub.shop` | `HTTP` → `nginx:8080` |
| `gestion.babel-hub.shop` | `HTTP` → `nginx:8080` |

Los tres apuntan a `nginx:8080`: el Nginx de VM-VENTAS enruta por el nombre de host (la tienda al E-commerce de la misma VM; `gestion.` a la réplica de VM-INVENTARIO). Usa **un solo conector** (el que despliega la guía): no arranques `cloudflared` en ningún otro sitio con este token. Cloudflare crea solo los registros DNS (`CNAME`) de cada hostname.

### 12.4 Cloudflare Access para la gestión (obligatorio)

En la consola actual, **primero crea la política**: *Zero Trust → Access → Policies → Add a policy*, nombre `personal`, acción **Allow**, regla *Emails* = los correos del personal autorizado. **Después la aplicación**: *Access → Applications → Add an application → Self-hosted*, dominio `gestion.babel-hub.shop`, *Add existing policy* → `personal`; el nombre (`gestion`) y la duración de la sesión se piden al final. Comprueba que la política diga *Used by applications: 1* y que en *Identity providers* esté *One-time PIN*. En el plan gratuito el método es el **código de un solo uso por correo**. Para exigir un segundo factor real, añade en *Settings → Authentication* un proveedor de identidad (Google o GitHub) con verificación en dos pasos y haz que la política lo **requiera**. Sin esto, **cualquiera podría llegar a la pantalla de ingreso del personal**.

### 12.5 Copiar el token a VM-VENTAS

Con `TUNNEL_TOKEN` ya en `ventas/cloudflared.env`, copia la carpeta `ventas` actualizada a VM-VENTAS como en §10.3 (Turnstile ya lo completaste en §10.2).

## 13. Paso 9 — Etapa 3: VM-VENTAS (tienda y entrada pública)

Aquí corren `cloudflared` y Nginx junto al E-commerce, en dos redes de Docker propias: `cloudflared` solo habla con Nginx, y el E-commerce solo cree la IP del cliente que le entrega Nginx. Ninguno de los dos publica puertos.

### 13.1 Desplegar

**[VM-VENTAS]**

```bash
cd /opt/babelhub
sudo env \
  BABELHUB_SECRETOS=/etc/babelhub/secretos \
  IP_PRIVADA=10.0.0.10 \
  IP_INVENTARIO=10.0.0.20 \
  IMAGEN_ECOMMERCE=ghcr.io/<propietario>/babelhub-ecommerce@sha256:<64> \
  scripts/desplegar.sh ventas --sin evolution-api
```

(`evolution-api` espera el número de WhatsApp, §15.3. `IP_INVENTARIO` es la dirección a la que Nginx envía la gestión.)

**Qué debes ver:** `ok nginx`, `ok ecommerce`, `ok redis`, `ok redis-contadores`, `ok n8n`, `ok cloudflared` y `VM ventas desplegada y sana.` Si el E-commerce no arranca, `sudo docker logs --tail 50 babelhub-ventas-ecommerce-1` nombra la variable que falla (nunca su valor): lo típico es un `CAMBIAR` sin completar, `BABELHUB__Pagos__Pasarela` incoherente con el entorno, o `ninguna` o el simulador en `Production`.

**Si falla con «Pool overlaps with other one on this address space»:** alguna red de Docker de la VM ya usa `10.213.10.0/28` o `10.213.11.0/28`. `sudo docker network ls` y borra la que no sea de BabelHub.

### 13.2 Comprobaciones dentro de la VM (obligatorias la primera vez)

En tu equipo se probó el camino de la entrada con las imágenes reales, pero **no** con el cortafuegos (`iptables`) de la imagen de Oracle. Compruébalo en la VM; no cambia nada:

```bash
# 1. Nginx llega al E-commerce por la red interna de Docker:
sudo docker exec babelhub-ventas-nginx-1 wget -qO- -T 5 --header='Host: babel-hub.shop' http://ecommerce:8080/health/live
# 2. Nginx llega a la réplica de gestión en VM-INVENTARIO (lista de seguridad y cortafuegos de la otra VM):
sudo docker exec babelhub-ventas-nginx-1 wget -qO- -T 5 --header='Host: gestion.babel-hub.shop' http://ecommerce-gestion:8081/health/live
# 3. El túnel salió a Internet (deben salir líneas):
sudo docker logs babelhub-ventas-cloudflared-1 2>&1 | grep -i "registered tunnel connection"
# 4. Nada de la entrada quedó publicado en la VM (no debe salir ninguna línea con nginx ni cloudflared):
sudo docker ps --format '{{.Names}} {{.Ports}}' | grep -E 'nginx|cloudflared' | grep -- '->' || echo "sin puertos publicados: correcto"
```

Y comprueba que **solo Nginx** llega a la réplica de gestión (la regla la instala `desplegar.sh ventas` y se repone cada minuto):

```bash
# 5. Desde cualquier otro contenedor no se puede llegar:
sudo docker run --rm --network babelhub-ventas_default redis:7-alpine@sha256:7f7d30b7fdd09467bac58198a1f166dd69789d348d4fe859cccb20e0cf287fbe \
  sh -c 'nc -z -w 5 10.0.0.20 8082 && echo "control: llega al 8082" || echo "ATENCION: la prueba no sirve, no llega ni al 8082";
         nc -z -w 5 10.0.0.20 8081 && echo "MAL: llega al 8081" || echo "correcto: 8081 bloqueado"'
sudo iptables -S DOCKER-USER      # debe aparecer la regla que descarta 10.0.0.20:8081 salvo desde 10.213.10.3
```

**Debes ver** en 5 las dos líneas «control: llega al 8082» y «correcto: 8081 bloqueado» (sin la primera la prueba no demuestra nada). Si dice «MAL: llega», no sigas: repite `desplegar.sh ventas`, comprueba `systemctl is-active cron` y revisa `/etc/cron.d/babelhub-gestion`. **La primera vez, reinicia VM-VENTAS (`sudo reboot`), espera dos minutos y repite la comprobación 5**: confirma que la regla vuelve sola.

**Debes ver** una respuesta de salud en 1 y 2, al menos una línea en 3 y «sin puertos publicados» en 4. Si 1 falla con las dos VMs sanas, el cortafuegos de Oracle está cortando el tráfico entre contenedores: guarda la salida de `sudo iptables -S FORWARD` y avisa (no borres reglas). Si falla solo 2, revisa la regla TCP 8081 desde `10.0.0.10/32` (§7.1).

*(Comprobación opcional: un contenedor hacia un puerto publicado en la dirección privada de **su propia** VM. El diseño no lo necesita; solo importaría si algún día se separa Nginx en otro Compose. `sudo docker run --rm redis:7-alpine@sha256:7f7d30b7fdd09467bac58198a1f166dd69789d348d4fe859cccb20e0cf287fbe sh -c 'nc -z -w 5 10.0.0.10 6380 && echo llega || echo "no llega"'`.)*

**Paneles de n8n y Evolution:** no están en Internet. Se alcanzan con un túnel SSH desde tu equipo: `ssh -L 5678:127.0.0.1:5678 babelhub-ventas` y abres <http://localhost:5678>.

### 13.3 Humo de punta a punta

**[Tu equipo]**

```bash
curl -s  https://babel-hub.shop/health              # «ok» (lo responde Nginx)
curl -s  https://babel-hub.shop/health/live          # respuesta del E-commerce
curl -sI https://babel-hub.shop/ | head -3          # «HTTP/2 200» (E-commerce en Staging); sin errores de certificado
curl -sI https://gestion.babel-hub.shop/ | head -3  # redirige a la pantalla de Cloudflare Access (no a la app)
curl -s  https://babel-hub.shop/ | grep -c "data-bh-pagos-simulados"   # 0 con la pasarela «ninguna» (no debe haber avisos); 1 con «simulador»
```

Entra además a `https://gestion.babel-hub.shop` en el navegador, pasa Access y comprueba que aparece el ingreso del personal.

Y la redirección a HTTPS (sin `-k`: no debe haber errores de certificado):

```bash
curl -sI http://babel-hub.shop/       | head -3     # 301 → https://babel-hub.shop/
curl -sI https://babel-hub.shop/      | grep -i strict-transport-security
curl -sI https://www.babel-hub.shop/  | head -3     # 301 → https://babel-hub.shop/
```

**Desde tu equipo, comprueba que Internet no llega a ningún puerto interno** (pon las IP **públicas** de las dos VMs; el puerto 22 debe salir **ABIERTO**, que demuestra que la prueba llega a la VM, y todos los demás «cerrado»):

```bash
for ip in <IP pública de vm-ventas> <IP pública de vm-inventario>; do for p in 22 80 443 5432 5678 6380 8080 8081 8082 8443; do
  timeout 5 bash -c "</dev/tcp/$ip/$p" 2>/dev/null && echo "ABIERTO $ip:$p" || echo "cerrado $ip:$p"; done; done
```

Si algún puerto distinto del 22 dice «ABIERTO», quita de la Security List la regla que lo permite **antes de seguir**; si el 22 sale «cerrado», revisa la IP o prueba desde otra red: la prueba no vale.

**Si falla:** `502` = Nginx no alcanza al E-commerce (tienda: `sudo docker logs --tail 50 babelhub-ventas-nginx-1`; gestión: regla 8081, §7.1); error de certificado = el dominio aún no está activo en Cloudflare; `1033`/`530` = el túnel no está conectado (token mal copiado); `400` en la gestión = falta `gestion.babel-hub.shop` en los hosts permitidos de `ecommerce-gestion.env`.

**Cómo deshacer:** `down` del Compose de VM-VENTAS; en Cloudflare, borra el túnel y sus hostnames; para devolver el dominio a Spaceship, cambia los nameservers de vuelta.

## 14. Paso 10 — Etapa 4: sin VM-AUX (lo que haces tú mientras tanto)

VM-AUX (la vigilancia **desde fuera** de la VM de datos) está **aplazada**. No instales sus scripts (`scripts/vm-aux/`) en ninguna de las dos VMs: sus credenciales quedarían junto a lo que vigilan. Mientras tanto **no tienes** (detalle en `docs/02-arquitectura-de-software/05-despliegue.md` §6.2): aviso externo si fallan los respaldos o cae VM-INVENTARIO, sellos horarios y copia diaria del registro de supresiones, vigilancia de la retención, latido externo, alertas de ingreso SSH ni un punto único de salto. Y si cae VM-VENTAS tampoco puedes entrar a la gestión.

Haz esto:

1. **Monitor externo** gratuito (por ejemplo en healthchecks.io o UptimeRobot) que pida `https://babel-hub.shop/health/live` cada pocos minutos y te avise por correo.
2. **[Tu equipo]** Disco cifrado de las dos VMs (T38): `bash scripts/verificar-cifrado-de-disco.sh --instancia <ocid de la VM> --compartimento <ocid> --dominio <AD>` para `vm-inventario` y `vm-ventas`. Debe decir que el volumen está `AVAILABLE` con clave de Oracle. Guarda la salida.
3. Una vez por semana, en cada VM: `last -20` y `sudo journalctl -u ssh --since "7 days ago" | grep -i accepted` (solo tus ingresos).
4. **Cada día, comprueba el respaldo** (nadie te avisará si falla, y el archivo de cambios solo dura 7 días). **[Tu equipo]**, con las claves de `respaldos-lector` guardadas fuera del repositorio en un archivo tuyo con permisos 600 (`~/.config/babelhub/r2-lector.env`, con las líneas `export RCLONE_CONFIG_LECTOR_TYPE=s3`, `…_PROVIDER=Cloudflare`, `…_REGION=auto`, `…_ENDPOINT=…`, `…_ACCESS_KEY_ID=…` y `…_SECRET_ACCESS_KEY=…`):

   ```bash
   . ~/.config/babelhub/r2-lector.env && echo "== copias de las ultimas 24 horas" && TZ=UTC rclone lsf -R --files-only --use-server-modtime --max-age 24h --format tp --separator ' ' lector:respaldos-diarios | sort && echo "== ultimo cambio archivado" && TZ=UTC rclone lsf -R --files-only --use-server-modtime --max-age 24h --format tp --separator ' ' lector:respaldos-wal | sort | tail -n 1
   ```

   **Debes ver exactamente dos objetos** `…tar.zst.age` (el clúster y `-config-`), subidos **a la hora programada (02:30 UTC)**, más una pareja por cada respaldo bajo demanda que tú hayas lanzado, y un segmento de **los últimos 15 minutos**. **Cualquier otro objeto es una alerta**: quien tenga la VM puede subir copias falsas (no anotes ninguna hasta aclararlo). **Anota en tu hoja el nombre completo de la copia del clúster** de cada día: al restaurar se elige **por ese nombre**, nunca «la más reciente». Si falta: en la VM, `sudo tail -20 /var/log/babelhub-respaldo.log` y `sudo docker logs --tail 50 babelhub-inventario-postgres-1`; respaldo bajo demanda: `ssh babelhub-inventario sudo docker exec babelhub-inventario-postgres-1 /opt/babelhub/respaldar.sh --bajo-demanda`.
5. **Cada semana, revisa el uso de R2** (panel de R2, o `rclone size lector:respaldos-diarios` y lo mismo con los otros tres) y anótalo. La primera semana compáralo con la medición local; **si la suma pasa de unos 7 GB, avisa** antes de que se acabe el cupo.

**Antes de operar con clientes o datos reales** hace falta VM-AUX o un vigilante equivalente fuera de las dos VMs.

## 14b. Carga inicial del catálogo y del inventario (antes de pedir la revisión de Culqi)

La revisión de la pasarela pide **al menos cinco productos con foto, descripción y precio visibles**. El repositorio trae 40 obras listas (`tools/carga-inicial/catalogo.csv`, selección en `docs/07-proceso-de-desarrollo/09-seleccion-del-catalogo-inicial.md`, spec `transversal-carga-inicial`). La carga son **dos órdenes del propio servicio** (como `migrar`): no abren puertos ni son una pantalla, usan las mismas reglas y la misma auditoría que la administración y son **repetibles** (repetirlas no duplica nada).

**Antes de empezar.**
- Las dos VMs ya están desplegadas (§11 y §13) y **migradas**, y su `/health/ready` responde.
- Las órdenes componen el servicio completo y **leen la misma configuración** que él (`ecommerce.env`, `inventario.env`): el almacén de objetos, la base, las claves. No necesitan variables nuevas, pero sí todas las que el servicio ya exige (incluida `BABELHUB__Pagos__Pasarela`: en la orden la pasarela es siempre la nula y no llama a nadie). Si el servicio no arranca por falta de una variable, la orden termina **nombrándola** (nunca su valor) antes de escribir nada.
- **Aviso sobre el ISBN.** El catálogo exige un ISBN con dígito de control y **no se puede cambiar después**. Para no inventar el de una edición real, cada edición lleva un identificador provisional del rango `979-0` (que no se asigna a libros). La tienda lo muestra como ISBN. Si conoces el ISBN real de una edición, escríbelo en la columna `isbn_impreso` de `catalogo.csv` **antes** de cargar.
- **Aviso sobre las digitales.** Solo las 5 obras genéricas del prototipo tienen edición digital y su archivo es un **EPUB de muestra** que dice que no es el libro: no las vendas con pagos reales sin reemplazar el archivo desde la administración.

**1. Las portadas (solo viajan a VM-VENTAS y de ahí al almacén de objetos; nunca al repositorio).** Las 40 imágenes están en tu equipo (`D:\Trabajo\BHPortadas`; las cinco genéricas se regeneran en PowerShell de Windows con `powershell -ExecutionPolicy Bypass -File tools/carga-inicial/generar-portadas-genericas.ps1 -Salida <carpeta>`: `-ExecutionPolicy Bypass` solo vale para esa ejecución, porque Windows no deja correr scripts sin firmar por defecto). Cada una pesa menos de 2 MB (el tope del catálogo, CAT-16) y se verifica por su contenido al subirla. Cópialas a una carpeta **temporal** de VM-VENTAS (con la configuración SSH de §7.4):

```bash
# [Tu equipo, en WSL] (D:\ se ve como /mnt/d)
scp -r /mnt/d/Trabajo/BHPortadas babelhub-ventas:/tmp/portadas
```

**2. El catálogo en VM-VENTAS.**

```bash
# [VM-VENTAS] carpeta de salida del plan de inventario (fuera del repositorio), legible por el usuario del contenedor
sudo install -d -o 1654 -g 1654 /var/lib/babelhub/carga
sudo chmod -R a+rX /tmp/portadas
cd /opt/babelhub
sudo env BABELHUB_SECRETOS=/etc/babelhub/secretos IP_PRIVADA=10.0.0.10 IP_INVENTARIO=10.0.0.20 IMAGEN_ECOMMERCE=<digest-de-la-imagen> \
  docker compose --project-directory infra -f infra/docker-compose.ventas.yml run --rm --no-deps \
    -v /opt/babelhub/tools/carga-inicial:/carga:ro -v /tmp/portadas:/portadas:ro -v /var/lib/babelhub/carga:/salida \
    ecommerce cargar-catalogo --archivo /carga/catalogo.csv --portadas /portadas --plan /salida/plan-de-inventario.json
```

**Qué debes ver:** una línea por obra no cargada con su número de fila y el campo, y los totales (`cargadas`, `ya cargadas`, `rechazadas`); el código de salida (`echo $?`) es `0` (todo cargado o ya cargado), `2` (alguna fila rechazada o sin cargar: léela, corrige el archivo o el almacén y **repite la orden**) o `1` (error: configuración o archivo inválido). Se crea `/var/lib/babelhub/carga/plan-de-inventario.json` (sin datos personales). **Repetirla** debe dar «40 ya cargadas». Las portadas y los archivos de muestra quedan **solo en el almacén de objetos**; borra la copia temporal: `rm -rf /tmp/portadas`.

**3. El plan viaja a VM-INVENTARIO** (es un archivo pequeño de identificadores y cantidades):

```bash
# [Tu equipo]
scp babelhub-ventas:/var/lib/babelhub/carga/plan-de-inventario.json /tmp/plan-de-inventario.json
scp /tmp/plan-de-inventario.json babelhub-inventario:/tmp/plan-de-inventario.json
```

**4. Las sucursales, las rutas y el stock en VM-INVENTARIO.**

Los tres `<digest>` son los mismos con los que desplegaste (Compose los exige aunque la orden use solo uno).

```bash
# [VM-INVENTARIO]
sudo install -d -o 1654 -g 1654 /var/lib/babelhub/carga && sudo mv /tmp/plan-de-inventario.json /var/lib/babelhub/carga/
cd /opt/babelhub
sudo env BABELHUB_SECRETOS=/etc/babelhub/secretos IP_PRIVADA=10.0.0.20 IP_VENTAS=10.0.0.10 IMAGEN_INVENTARIO=<digest> IMAGEN_ECOMMERCE=<digest> IMAGEN_POSTGRES=<digest> \
  docker compose --project-directory infra -f infra/docker-compose.inventario.yml run --rm --no-deps \
    -v /opt/babelhub/tools/carga-inicial:/carga:ro -v /var/lib/babelhub/carga:/salida:ro \
    inventario cargar-stock --plan /salida/plan-de-inventario.json --sucursales /carga/sucursales.csv --rutas /carga/rutas.csv --esperar
```

**Qué debes ver:** las 6 sucursales (la primera, Lima Centro, es la fuente de **recepción**), las 10 rutas, la edición de cada impresa registrada y el stock (Lima y dos sucursales más por edición: unas 120 operaciones). El Inventario limita los ajustes del personal a **60 por hora** (SEG-30) y la orden **no lo rodea**: con `--esperar` espera la siguiente ventana (hasta 2 horas) y continúa, así que **la orden puede tardar más de una hora**; sin `--esperar` se detiene al llegar a 60, escribe a partir de cuándo puede continuar y sale con código `2`: repítela pasado ese plazo. Repetir la orden nunca duplica ni toca una sucursal que ya tiene movimientos de esa edición (código `0` y «ya cargada»). Si se corta una conexión, **vuelve a ejecutarla**.

**Aviso sobre las ráfagas.** Los límites de uso por origen del servicio responden **429** (demasiadas solicitudes) o **503** (el almacén o la base no dan abasto) si lanzas muchas solicitudes seguidas, por ejemplo al comprobar la tienda con un script o al recargar el catálogo sin parar justo después de la carga. No es un fallo de la carga: espera uno o dos minutos y repite. Las órdenes de carga trabajan en serie para no provocarlo.

**5. Comprobar.** En la tienda, `/catalogo` debe mostrar las 40 obras con portada y precio, y la ficha de cualquiera, su descripción y su disponibilidad por sucursal (Lima, Arequipa, Trujillo, Cusco, Piura y Ayacucho según el stock cargado).

**Cómo deshacerlo.** La carga solo agrega. Para quitar una obra usa la baja de la administración del catálogo (la obra deja de mostrarse y conserva sus datos); el ISBN dado de baja sigue reservado. Una carga que no te convence se corrige editando `catalogo.csv` **antes** de repetirla: una fila ya cargada no se modifica.

## 15. Después del despliegue

### 15.1 Culqi (B05)

Culqi se integra en **dos fases**: primero **revisa tu web** (no necesita todavía integrar la pasarela) y, cuando la aprueba y te da las llaves, **se integra el cobro real**. La tienda está lista para las dos.

#### Fase 1: Culqi revisa la web (entorno `Staging`, sin pasarela)

> ⚠️ **La tienda en `Staging` es pública**: el registro de cuentas y el Libro de Reclamaciones aceptan datos reales de cualquiera. Los respaldos (§9b, §11.1) deben estar activos y comprobados, y **todavía no hay una restauración probada en la VM**. Usa solo datos de prueba y decide si mientras tanto limitas quién puede entrar (por ejemplo, con una regla de Cloudflare que solo deje pasar a Culqi y a ti).

Es lo que despliega esta guía por defecto (`BABELHUB__Pagos__Pasarela="ninguna"`, DD-110: Culqi suele rechazar una web con una pasarela falsa). En `Staging` la tienda está en Internet con SSL y **no cobra nada porque no hay pasarela**: el catálogo, el carrito y el checkout (revisión de libros, entrega, contacto y totales) funcionan, y el paso de pago solo dice «El pago en línea estará disponible pronto», con la vuelta al carrito y al catálogo. No hay formulario, campo de identificador ni botón de pagar, el servidor rechaza cualquier cobro (también hecho a mano) y no aparece ningún aviso de pagos ni de envíos simulados. Los requisitos que Culqi revisa y dónde se cumple cada uno:

| Requisito de Culqi | Dónde está en BabelHub | Qué debes hacer tú |
| :--- | :--- | :--- |
| **Contacto visible** (teléfono, correo, dirección) | Los muestra la tienda desde `BABELHUB__Contacto__Telefono`, `…__Correo` y `…__Direccion` (otro carril de trabajo implementa su lectura y su lugar en el pie y en la página de contacto) | Completar esas tres variables en `ecommerce.env` y `ecommerce-gestion.env` (§10.2). **Son datos personales: no van al repositorio.** |
| **Términos y condiciones** | `/legal/terminos` (enlace en el pie) | Revisar el texto con la persona responsable (hoy es de trabajo). |
| **Política de cambios y devoluciones** | `/legal/reembolso` («Política de reembolso», enlace en el pie) y el flujo de devoluciones | Confirmar que el texto cubre cambios **y** devoluciones, como lo pide Culqi (otro carril trabaja en los textos legales). |
| **Libro de Reclamaciones** | `/reclamaciones` (enlace en el pie) | Nada: ya funciona. |
| **Al menos 5 productos con foto, descripción y precio** | El catálogo público | Cargarlos desde la administración del catálogo. **En `Staging` solo van obras y archivos digitales de MUESTRA, sin valor comercial** (así, si después activas el simulador para las pruebas con personas, nada de valor se entrega), **y los pedidos de `Staging` no se atienden**. Los carga el administrador que creas con `crear-administrador` (§0, fila 1 bis). |
| **Carrito o botón «Comprar»** | Ficha de cada obra y `/carrito` → `/checkout` | Nada: ya funciona (el último paso dice que el pago en línea estará disponible pronto). |
| **Usuario y contraseña de prueba** para el revisor | El comando `cuenta-de-prueba` (abajo) | Ejecutarlo y pasar el correo y la contraseña a Culqi por un canal seguro (no por el chat público ni el repositorio). |
| **SSL en todas las URLs** | Cloudflare: Universal SSL + Always Use HTTPS (§12.2); la aplicación redirige y envía HSTS | Verificar con los `curl` de §13.3 y recorrer la tienda en el navegador buscando avisos de «contenido no seguro» (no debe haber). |

**Crear el usuario y la contraseña de prueba** (crear y cambiar la contraseña solo existe en `Staging`; en `Production` el comando se niega, salvo `--retirar` y `--purgar`). **[VM-VENTAS]**, con el entorno del propio servicio y **una sola vez** (la contraseña se muestra una vez, por la consola, y no queda en ningún archivo ni registro; si la pierdes, vuelves a ejecutar el comando y se **cambia** la contraseña de esa misma cuenta):

```bash
cd /opt/babelhub
sudo env BABELHUB_SECRETOS=/etc/babelhub/secretos IP_PRIVADA=10.0.0.10 IP_INVENTARIO=10.0.0.20 \
  IMAGEN_ECOMMERCE=ghcr.io/<propietario>/babelhub-ecommerce@sha256:<64> \
  docker compose --project-directory infra -f infra/docker-compose.ventas.yml \
  run --rm --no-deps --no-TTY ecommerce cuenta-de-prueba
```

**Qué debes ver:** `cuenta-de-prueba: cuenta creada (correo verificado, textos vigentes aceptados).`, el correo (por defecto `revision@example.test`; con `--correo <dirección>` solo puedes elegir `persona-01@example.test` a `persona-20@example.test`; cualquier otro correo se niega sin tocar nada) y la **contraseña**. **Anótala ahora** en tu gestor de contraseñas. La cuenta nace con el correo **verificado** y con los textos legales aceptados, de modo que puede ingresar y comprar sin recibir ningún correo. El comando también crea, si faltaba, la **primera clave de firma** de las credenciales (sin ella nadie podría ingresar) y deja todo registrado en la auditoría. **Si era la primera vez**, espera **un minuto** antes de ingresar: el servicio ya encendido tarda eso en ver la clave nueva y, mientras tanto, responde «no podemos mostrar esto ahora».

**Si falla**, el mensaje dice qué falta: «solo existe en Staging» (el archivo de secretos dice `Production`), «Faltan los textos legales vigentes» (la migración de VM-INVENTARIO no sembró los textos: revisa `BABELHUB__Migrador__Ecommerce__VersionDeTextos` en `migrador.env` y vuelve a ejecutar `desplegar.sh inventario`) o una variable del servicio sin completar.

**Qué ve el revisor en el paso de pago (`ninguna`):** «El pago en línea estará disponible pronto», que no se le cobró nada ni se creó ningún pedido, «Volver al carrito» y «Seguir mirando el catálogo». Los libros impresos que el checkout separó al pasar al pago se liberan al volver al carrito (o solos, a los 15 minutos). Dile a Culqi que la tienda todavía no cobra y que integrarás su pasarela cuando la apruebe.

##### Alternar la pasarela en `Staging` (`ninguna` ↔ `simulador`)

El simulador **se conserva** para las pruebas con personas: recorren la compra completa sin que se cobre nada. Cambiar de modo es **una línea en dos archivos** (el entorno sigue siendo `Staging` y no lleva llave), y **los dos deben decir lo mismo**: la tienda y la gestión corren en VMs distintas y cada `desplegar.sh` solo ve su archivo.

| Archivo | VM | Línea |
| :--- | :--- | :--- |
| `ecommerce.env` | VM-VENTAS | `BABELHUB__Pagos__Pasarela="ninguna"` o `"simulador"` |
| `ecommerce-gestion.env` | VM-INVENTARIO | la misma |

1. **[Tu equipo]** Cambia la línea en `~/babelhub-secretos/ventas/ecommerce.env` y en `~/babelhub-secretos/inventario/ecommerce-gestion.env` (no vuelvas a generar los secretos: rotarías todos) y copia cada archivo a su VM como en §10.3. Si prefieres editarlo en la VM, el archivo está en `/etc/babelhub/secretos/`.
2. Vuelve a ejecutar `desplegar.sh ventas` (VM-VENTAS) y `desplegar.sh inventario` (VM-INVENTARIO) con las mismas variables y opciones de §11 y §13.1: Compose recrea el servicio que cambió.
3. Comprueba desde tu equipo: `curl -s https://babel-hub.shop/ | grep -c "data-bh-pagos-simulados"` da `0` con `ninguna` y `1` con `simulador`.

**Con `simulador`** la tienda dice «Pagos simulados: no se realiza ningún cobro» en el pie de toda página, en el checkout y en el pago, y vuelve a mostrar el aviso de envíos simulados. La pantalla de pago pide un «identificador del medio de pago»: `tok_aprobado` aprueba; `tok_tarjeta_rechazada`, `tok_yape_rechazado` y `tok_fondos_insuficientes` rechazan con su motivo. Úsalo solo con las personas de la prueba y **vuelve a `ninguna`** antes de que Culqi revise la tienda.

**Antes de pasar de `simulador` a `ninguna`** no dejes un pago a medias: con `ninguna` nadie consulta a ninguna pasarela, así que un pago pendiente de confirmación vence solo y un reembolso pendiente queda pendiente en la administración. Los pedidos ya creados con el simulador siguen en la base y en las pantallas (purga lo de prueba con `cuenta-de-prueba --purgar` si estorban).

**El correo de verificación** (para quien se registre solo, como cualquier cliente): el E-commerce ya trae un **transporte SMTP real** (`TransporteDeCorreoSmtp`, con STARTTLS obligatorio). Funciona cuando completas `BABELHUB__Correo__Smtp__Servidor`, `…__Puerto` (587 por defecto), `…__Cuenta`, `…__Credencial` y `…__Remitente` (§10.2: Gmail con contraseña de aplicación). Sin esos datos el servicio **no arranca** en `Staging` ni en `Production` (SMTP es obligatorio), así que el envío real queda configurado de todos modos; lo que no se ha probado de verdad es el envío contra Gmail (queda como pendiente de tu cuenta). Cuotas: Gmail limita unos 500 correos al día, y la cola de BabelHub aplica su propio tope por cuenta y hora. La cuenta de revisión **no depende de ese correo**; las demás personas sí.

#### El primer administrador, varias cuentas de prueba y la purga

Todos son comandos de consola de la imagen del E-commerce, con el mismo `docker compose … run --rm --no-deps --no-TTY ecommerce <comando>` de arriba (en **[VM-VENTAS]**). Cada contraseña se muestra **una sola vez** y no queda en ningún archivo ni registro (cópiala al momento a tu gestor de contraseñas; el comando no lleva la contraseña, así que el historial del shell no la guarda, pero sí el correo). Si cortas el comando a medias y queda un contenedor, quítalo con `docker rm -f <nombre>` y repite.

| Qué | Comando | Dónde vale |
| :--- | :--- | :--- |
| **El primer administrador central** (sin él nadie entra a `gestion.babel-hub.shop` ni carga el catálogo) | `crear-administrador --correo <tu correo> [--nombre "Tu nombre"]` | `Staging` y `Production` |
| Varias cuentas de cliente **para las pruebas con personas** (de 1 a 20) | `cuenta-de-prueba --cantidad 5` → `persona-01@example.test` … `persona-05@example.test`, cada una con su contraseña | solo `Staging` |
| Retirar una cuenta, o **todas** las de prueba | `cuenta-de-prueba --retirar --correo <dirección>` o `cuenta-de-prueba --retirar --todas` | `Staging` y `Production` |
| **Purgar lo de prueba** al pasar a `Production` con la misma base | `cuenta-de-prueba --purgar` | `Staging` y `Production` |

**Primer administrador.** Muestra una **contraseña temporal de 24 horas**. En el primer ingreso (`https://gestion.babel-hub.shop`, detrás de Cloudflare Access) **cambias la contraseña y configuras el segundo factor (TOTP)** con tu app de autenticación: sin eso no recibes credencial. Solo crea **el primero**: si ya hay un administrador central, se niega (los demás los da de alta la administración); repetirlo con el mismo correo no duplica nada y, solo si aún no ingresaste, genera otra contraseña temporal (siempre que sea el único administrador). Si algún día no queda **ningún** administrador activo, el mismo comando sirve para recuperar el acceso. Tu correo va solo en el comando: no está en el repositorio. **Revísalo dos veces antes de ejecutar:** la cuenta nace con el correo verificado sin enviarle nada y ahí llegarán los avisos de seguridad de la administración. Con la gestión puedes cargar el catálogo (**en `Staging`, solo obras y archivos de muestra**).

**Cuentas para las pruebas con personas.** Reparte cada correo con **su** contraseña a una persona distinta **por un canal seguro** (mensajería cifrada o en persona; nunca por un chat público ni el repositorio) y anota quién tiene cuál en una hoja tuya fuera del repositorio. Repetir `--cantidad` **cambia** las contraseñas (y cierra las sesiones abiertas): sirve para reponer una perdida. Las cuentas entran verificadas y con los textos aceptados: no necesitan correo. Al terminar la ronda: `--retirar --todas`.

**Purga al pasar a `Production`** (la base es la misma): `cuenta-de-prueba --purgar` **cancela** cada cuenta de prueba con la **cancelación de cuenta** de Identidad, de modo que cada módulo desvincula o anonimiza lo suyo **por su contrato** (pedidos, pagos, devoluciones, carritos, biblioteca y reclamaciones), los avisos pendientes se cancelan y la baja de las reservas del Inventario la entrega el trabajo de bajas por su contrato REST. **No toca el catálogo ni a ningún usuario real** y no hace SQL sobre el esquema de otro módulo ni del otro servicio. **Límite honesto:** la cancelación **anonimiza, no borra**: los pedidos y pagos de prueba quedan como registros anónimos (la retención legal lo exige); si necesitas borrarlos de verdad, hace falta un contrato de purga en cada módulo (spec nueva). Cada cancelación queda en la auditoría (`CuentaCancelada`). Necesita el almacén de supresiones configurado (el que ya usa la cancelación de cuentas). **Pendiente tuyo:** la purga solo se probó con PostgreSQL real y un registro en memoria; el ensayo local no levanta el almacén de objetos, así que allí se niega con `RegistroDeSupresionesNoDisponibleException` sin tocar nada. Pruébala una vez en el despliegue, con el almacén de Oracle ya configurado, y antes de abrir al público. **Lo que la purga no hace** (decisión DD-89, ratificada el 2026-11-23): no distingue ni borra lo que **un usuario real** haya hecho en `Staging` con la pasarela simulada, y los pedidos no terminales de una cuenta de prueba quedan anonimizados y siguen su curso (sus reservas vencen por su plazo). Por eso **no abras `Staging` al público**: solo tú y las cuentas de prueba. **Hazla después de la revisión de Culqi y antes de abrir al público**, y repítela (no hace nada si ya no quedan cuentas de prueba).

#### Fase 2: integrar el cobro real (entorno `Production`)

> ⛔ **Antes de cobrar dinero real** debe cumplirse, como mínimo: respaldos activos y **una restauración probada** (T01, §15.4), un vigilante fuera de las dos VMs (§14), la revisión del almacén de respaldos provisional (§0 fila 5, B13) y la revisión legal pendiente (B09). Pasar a `Production` sin eso arriesga perder pedidos y pagos ya cobrados.

Cuando Culqi apruebe la web y te dé las llaves:

1. **El adaptador de Culqi ya está hecho (T37 de `ecommerce-pagos`) y probado contra un Culqi simulado, pero nunca contra Culqi real.** Antes de pasar a `Production`, con tus llaves de **integración** (`pk_test_…`, `sk_test_…`), haz el **spike T38** en tu equipo siguiendo `specs/ecommerce-pagos/guia-del-spike-culqi.md` (20 minutos, sin dinero real). **No pases a `Production` hasta que esté en verde.** Además, la pantalla de pago todavía pide pegar el identificador del medio de pago: integrar el formulario de Culqi que lo genera en el navegador es una tarea pendiente (DD-99, T38b) y sin ella un cliente real no puede pagar.
1 bis. **Purga lo de prueba antes de abrir al público (`cuenta-de-prueba --purgar`, y retira las cuentas con `--retirar --todas`)** (es una credencial que conoce un tercero y las dos fases comparten base): en **[VM-VENTAS]**, el mismo comando de la fase 1 con `--purgar` (`… ecommerce cuenta-de-prueba --purgar`, con el servicio levantado para que la baja llegue al Inventario) y, si prefieres solo desactivar, con `--retirar --todas`. Se permiten en `Staging` y en `Production`, solo actúan sobre las cuentas de prueba (21 correos reservados y su nombre fijo) y lo dejan en la auditoría; repetirlos no cambia nada. Mientras Culqi siga revisando, **no la retires**. Además, antes de operar con clientes decide qué hacer con lo que se «compró» en `Staging` (pedidos, reservas y accesos digitales simulados: el simulador entrega lo que «cobra»).
2. **Pasar de `Staging` a `Production`** son **tres líneas** en `ecommerce.env` (VM-VENTAS) y las mismas en `ecommerce-gestion.env` (VM-INVENTARIO), en **tu equipo** (carpeta `~/babelhub-secretos`) y copiadas a cada VM como en §10.3:

   ```
   ASPNETCORE_ENVIRONMENT="Production"
   BABELHUB__Pagos__Pasarela="culqi"
   BABELHUB__Pagos__LlavePrivada="sk_live_…"        # añade esta línea: solo en estos dos archivos
   ```

   (Con `scripts/generar-secretos.sh --entorno production` en una carpeta nueva obtienes los mismos archivos con esas tres claves ya puestas, pero **no** vuelvas a generar todo: rotarías todos los secretos.)
3. Vuelve a ejecutar `scripts/desplegar.sh ventas` y `scripts/desplegar.sh inventario` (con las mismas variables y opciones de §13.1, incluido `--sin evolution-api` mientras no haya WhatsApp, y de §11). `desplegar.sh` **se niega** si el entorno y la pasarela no son coherentes o si la llave no empieza con `sk_live_`; y la aplicación se niega a arrancar si, aun así, algo no cuadra: **nunca cobra «a ciegas»**. Con `Production` no aparece el aviso «Pagos simulados».
4. **No registres todavía el webhook en el panel de Culqi:** su documentación pública no describe una firma ni una autenticación de los avisos y la entrada `…/integraciones/pasarela/avisos` está deshabilitada a propósito (responde 404; DD-95); el estado de un pago pendiente lo confirma la consulta de cada minuto. Contrasta a mano los reembolsos y cobros dudosos con el panel (`docs/05-seguridad/07-operacion-segura.md` §3.2.1).
5. Las llaves **de integración** de Culqi (`sk_test_…`) no valen en la VM: la aplicación las rechaza fuera de `Development` y `Test` (se usan solo en el spike, en tu equipo). **Nunca** pegues una llave en el chat ni en el repositorio.

**Para volver atrás** (por ejemplo, si algo falla en `Production`): vuelve a poner las tres líneas de `Staging` (`ASPNETCORE_ENVIRONMENT="Staging"`, `BABELHUB__Pagos__Pasarela="ninguna"` y **borra** la línea de la llave) y repite `desplegar.sh`.

### 15.2 LLM del asistente (B06)

Tienes una API key de Anthropic. El asistente (ECO-12) está **diferido** y su variable de entorno aún no existe: `specs/asistente-compra-ia/decisions.md` D07 fija la compuerta (alta en `NombresDeSecretos` y en el redactor, `docs/05-seguridad/06-seguridad-del-asistente.md`, base legal de la transferencia fuera del país). **Hasta entonces no la escribas en ningún archivo.** Cuando exista: va **solo** en `ventas/ecommerce.env` de VM-VENTAS (nunca en `ecommerce-gestion.env` ni en otra VM), con permisos 600, y se rota si hay sospecha (Anthropic Console → API keys → revocar y crear).

### 15.3 WhatsApp (B07)

Necesitas un **número dedicado y descartable** (no el personal). Pasos (detalle en `infra/n8n/README.md`):

1. Crea la base técnica de Evolution **una vez** en VM-INVENTARIO, **sin escribir la contraseña en la línea de órdenes** (quedaría en el historial y en el registro de `sudo`): abre `sudo docker exec -it babelhub-inventario-postgres-1 psql -U postgres` y escribe, una por una: `CREATE ROLE evolution LOGIN;`, `\password evolution` (pega la contraseña de `DATABASE_CONNECTION_URI` de `ventas/evolution.env` cuando la pida), `CREATE DATABASE evolution OWNER evolution;`, `REVOKE CONNECT ON DATABASE evolution FROM PUBLIC;`, `GRANT CONNECT ON DATABASE evolution TO evolution;` y `\q`. Entra por el socket local del contenedor, que no usa la red; Evolution, desde VM-VENTAS, llega por TLS verificado con la CA de `tls-ca.pem`.
2. Despliega VM-VENTAS **sin** `--sin evolution-api`.
3. Por túnel SSH (`ssh -L 8081:127.0.0.1:8081 babelhub-ventas`) abre <http://localhost:8081> y empareja el número (QR).
4. Por otro túnel (`-L 5678:127.0.0.1:5678`) importa `infra/n8n/flujo-de-avisos.json` en n8n, crea la credencial `Evolution API (apikey)` (la de `evolution.env`) y activa el flujo.
5. **Obligatorio antes de activar avisos en producción:** la prueba de humo de `infra/n8n/README.md` («Importar y probar», paso 2).
6. Los avisos por WhatsApp exigen consentimiento (Ley N.° 29733).

### 15.4 Spikes opcionales T01–T05 (`specs/transversal-plataforma/tasks.md`)

Son mediciones que cierran decisiones de la spec. Ninguna bloquea el despliegue, pero **T01 es la única prueba de que tus respaldos sirven** (y la prueba de la cuenta de R2, §9b.5, va antes de poner datos que importen). Registra cada resultado en `progress/ensayos/AAAA-MM-DD-<spike>.md` (fecha, qué hiciste, el número medido, la decisión) y avisa para actualizar `specs/transversal-plataforma/decisions.md`.

| Spike | Qué hacer (ejecutable) | Qué registrar |
| :--- | :--- | :--- |
| **T01** Restaurar una copia hecha en ARM | En tu equipo, **dentro de un volumen cifrado efímero** (T05) y nunca en tu carpeta personal: baja con `respaldos-lector` la copia del clúster **cuyo nombre anotaste** (§14), comprueba que `sha256sum <archivo>` coincide con la suma que lleva el nombre y entonces `bash scripts/descifrar-respaldo.sh --entrada <respaldo>.tar.zst.age < <tu clave de age> \| bash scripts/extraer-respaldo.sh --destino <volumen-cifrado>/restaurada` (necesita `zstd`); configura la recuperación como dice `infra/respaldos/README.md` (con o sin archivo de cambios); arranca `postgres:17-alpine` (la imagen de `infra/postgres` con `--platform linux/arm64`, en emulación) montando esa carpeta y comprueba `select count(*) from …` de pedidos y reservas. Hazlo en un **volumen cifrado efímero**, con swap apagado, sin red hacia proveedores. | Duración total, contadores, si arrancó sin errores en ARM emulado, y la decisión D12. |
| **T02** Almacén de Oracle | `bash scripts/almacen.sh comprobar --espacio <ns>`; sube dos veces la misma clave con la credencial `supresiones-registro` y comprueba que la segunda **falla** (no sobrescribe); confirma si la retención es por contenedor y **cuándo** ejecuta el ciclo de vida (los objetos de prueba vencen a los días que dice `contenedores.txt`). | Error exacto al repetir, si la retención es por contenedor o por prefijo (decide D23), fecha en que corrió el ciclo de vida. |
| **T03** Tamaño y cuota | En VM-INVENTARIO: `sudo docker exec babelhub-inventario-postgres-1 psql -U postgres -c "select pg_size_pretty(sum(pg_database_size(datname))) from pg_database"`; en R2, el tamaño de los cuatro contenedores (§14, paso 5), y en Oracle, *Object Storage → cuota* con libros y registro. Compara con `progress/ensayos/medicion-respaldos-comprimidos-2026-11-25.md`. | Tamaño de la base, tamaño por contenedor, cuotas gratuitas vigentes y la proyección contra ellas. |
| **T04** Memoria y CPU de VM-INVENTARIO | Con la VM bajo uso normal **y** un respaldo en curso (`respaldar.sh --bajo-demanda`): `free -m` (usa «available», sin caché), `sudo docker stats --no-stream`, `uptime`. Durante 15 minutos, cada minuto. | Memoria usada (sin caché recuperable), CPU, pico durante el respaldo; compara con el 70 % de RNF12 (~4,2 GB). |
| **T05** Volumen cifrado efímero | Una VM o distro de ensayo **aparte** con `swapon --show` vacío; crea un volumen LUKS (`cryptsetup`), restaura ahí, destrúyelo (`cryptsetup luksClose` y borra la llave) y comprueba que no quedan datos en `/tmp`, el historial de la shell ni los registros de Docker (`docker system prune -af --volumes`). | Qué quedó fuera del volumen (residual) y la constancia de destrucción. |

(T29 y T31, los scripts de restauración y de ensayo aislado, **aún no están hechos**: esos spikes se hacen a mano como arriba.)

## 16. Si algo sale mal: tabla de rescate

| Síntoma | Causa probable | Qué hacer |
| :--- | :--- | :--- |
| No entras por SSH | Tu IP cambió o la regla de la Security List | Actualiza la regla TCP 22; o usa *Instance → Console connection* de Oracle. |
| `desplegar.sh` dice «CAMBIAR sin completar» | Falta un dato externo | `cat PENDIENTES.txt`, complétalo, vuelve a copiar el archivo y repite. |
| `desplegar.sh` dice «clave privada que no usa esta VM» | Copiaste una carpeta de más | Borra ese archivo de `/etc/babelhub/secretos` y copia solo la carpeta de la VM. |
| Un contenedor sale «unhealthy» | Configuración o dependencia caída | `sudo docker logs --tail 50 <contenedor>`: el mensaje nombra la variable que falla (nunca su valor). |
| El Inventario responde 403 a las llamadas del E-commerce | Falta HTTPS o la credencial | Ambos lados usan `https://<IP>:8443` o `:8082` y el certificado de la CA privada; revisa `ca-bundle.pem` en la carpeta de secretos. |
| La IP pública de una VM cambió | Reinicio sin IP reservada | Solo afecta tu SSH (las VMs hablan por IP privada). |
| Te llega la alerta de 1 USD | Algo fuera del cupo | *Cost Analysis* → qué recurso; termínalo. |
| Quieres empezar de cero una VM | — | `down` (sin `-v` en VM-INVENTARIO si hay datos), borra `/opt/babelhub` y `/etc/babelhub`, vuelve al paso correspondiente. |
| Sospechas que se filtró un secreto | — | Rota ya: genera nuevos, copia, `desplegar.sh`; revoca en Oracle/Cloudflare/Gmail lo que corresponda; sigue `docs/05-seguridad/07-operacion-segura.md` §6. |

## 17. Dónde está cada pieza (para quien mantenga esto)

| Pieza | Archivo |
| :--- | :--- |
| Flujo de publicación | `.github/workflows/lanzamiento.yml` |
| Ensayo local | `scripts/ensayo-local.sh` |
| Generador de secretos | `scripts/generar-secretos.sh` y `infra/secretos-requeridos.txt` |
| Despliegue de una VM | `scripts/desplegar.sh <ventas\|inventario>` e `infra/docker-compose.<vm>.yml` |
| Almacenes (Oracle y R2) | `scripts/almacen.sh` (`r2-…`: `scripts/almacen-r2.sh`), `infra/almacen/` e `infra/almacen/r2/` |
| Restauración manual | `scripts/descifrar-respaldo.sh`, `extraer-respaldo.sh` y `descifrar-wal.sh` (`infra/respaldos/README.md`) |
| VM-AUX (aplazada) | `scripts/vm-aux/` e `infra/vm-aux/` |
| Respaldos y custodia | `infra/respaldos/`, `scripts/comprobar-custodia.sh` |
| Decisiones de esta guía | `progress/decisiones-delegadas.md` (DD-55 a DD-59; para `Staging`, la cuenta de prueba, el dominio y HTTPS, DD-65 a DD-69; para las dos VMs, DD-100 a DD-104; respaldos en R2, DD-105 a DD-109; la tienda sin pasarela, DD-110 a DD-114) |
| Entorno `Staging`, sin pasarela o con la simulada y su aviso, y cuenta de prueba | `specs/ecommerce-pagos/` (PAG-33 a PAG-49, D25 y D26), `EntornoDelServicio`, `ModoCuentaDeRevision` y `CuentaDeRevisionDeLaPasarela` |
